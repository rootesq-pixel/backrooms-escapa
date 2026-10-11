using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

// Servidores simulados para las pruebas: router UPnP (SSDP + descripción + SOAP), NAT-PMP y un servidor HTTP mínimo (eco de IP pública).

public sealed class MockHttpServer : IDisposable
{
    public delegate void Handler(string method, string path, Dictionary<string, string> headers, string body, out int status, out string contentType, out string response);
    private readonly TcpListener listener;
    private readonly Handler handler;
    private volatile bool stop;
    public int Port { get; private set; }

    public MockHttpServer(Handler h)
    {
        handler = h;
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        new Thread(Loop) { IsBackground = true }.Start();
    }

    private void Loop()
    {
        while (!stop)
        {
            try
            {
                if (!listener.Pending()) { Thread.Sleep(5); continue; }
                TcpClient c = listener.AcceptTcpClient();
                ThreadPool.QueueUserWorkItem(_ => Serve(c));
            }
            catch (Exception) { }
        }
    }

    private void Serve(TcpClient c)
    {
        try
        {
            c.ReceiveTimeout = 4000;
            NetworkStream ns = c.GetStream();
            MemoryStream ms = new MemoryStream();
            byte[] buf = new byte[4096];
            int headerEnd = -1;
            while (headerEnd < 0)
            {
                int n = ns.Read(buf, 0, buf.Length);
                if (n <= 0) return;
                ms.Write(buf, 0, n);
                string t = Encoding.UTF8.GetString(ms.ToArray());
                headerEnd = t.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            }
            string all = Encoding.UTF8.GetString(ms.ToArray());
            string head = all.Substring(0, headerEnd);
            string[] lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
            string[] rl = lines[0].Split(' ');
            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 1; i < lines.Length; i++) { int k = lines[i].IndexOf(':'); if (k > 0) headers[lines[i].Substring(0, k).Trim()] = lines[i].Substring(k + 1).Trim(); }
            int len = 0; if (headers.ContainsKey("Content-Length")) int.TryParse(headers["Content-Length"], out len);
            byte[] raw = ms.ToArray();
            int bodyStart = Encoding.UTF8.GetByteCount(all.Substring(0, headerEnd + 4));
            while (raw.Length - bodyStart < len)
            {
                int n = ns.Read(buf, 0, buf.Length); if (n <= 0) break;
                byte[] nr = new byte[raw.Length + n]; Buffer.BlockCopy(raw, 0, nr, 0, raw.Length); Buffer.BlockCopy(buf, 0, nr, raw.Length, n); raw = nr;
            }
            string body = Encoding.UTF8.GetString(raw, bodyStart, Math.Max(0, raw.Length - bodyStart));
            int status; string ct, resp;
            handler(rl[0], rl[1], headers, body, out status, out ct, out resp);
            byte[] rb = Encoding.UTF8.GetBytes(resp ?? "");
            string rh = "HTTP/1.1 " + status + (status == 200 ? " OK" : status == 500 ? " Internal Server Error" : " Not Found") + "\r\nContent-Type: " + ct + "\r\nContent-Length: " + rb.Length + "\r\nConnection: close\r\n\r\n";
            byte[] hb = Encoding.ASCII.GetBytes(rh);
            ns.Write(hb, 0, hb.Length); ns.Write(rb, 0, rb.Length); ns.Flush();
        }
        catch (Exception) { }
        finally { try { c.Close(); } catch (Exception) { } }
    }

    public void Dispose() { stop = true; try { listener.Stop(); } catch (Exception) { } }
}

public sealed class MockUpnpRouter : IDisposable
{
    public const string Svc = "urn:schemas-upnp-org:service:WANIPConnection:1";
    public string ExternalIp = "203.0.113.7";
    public bool RejectPermanentLease;     // responde 725 a concesión 0
    public bool ConflictOnce;             // responde 718 la primera vez
    public bool BreakDescription;         // la descripción devuelve 404
    public bool UsePppService;            // solo WANPPPConnection
    public readonly List<string> Log = new List<string>();
    public readonly Dictionary<int, string> Mappings = new Dictionary<int, string>();
    public int SsdpPort { get; private set; }
    public int HttpPort { get { return http.Port; } }
    public int SsdpRequests;
    private readonly UdpClient udp;
    private readonly MockHttpServer http;
    private volatile bool stop;

    public MockUpnpRouter()
    {
        http = new MockHttpServer(HandleHttp);
        udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        SsdpPort = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
        new Thread(SsdpLoop) { IsBackground = true }.Start();
    }

    private void SsdpLoop()
    {
        while (!stop)
        {
            try
            {
                if (udp.Client.Available == 0) { Thread.Sleep(5); continue; }
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] data = udp.Receive(ref from);
                string t = Encoding.ASCII.GetString(data);
                if (!t.StartsWith("M-SEARCH") || !t.Contains("ssdp:discover")) continue;
                lock (Log) { SsdpRequests++; }
                string resp = "HTTP/1.1 200 OK\r\nCACHE-CONTROL: max-age=120\r\nST: urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\nUSN: uuid:mock::urn:schemas-upnp-org:device:InternetGatewayDevice:1\r\nEXT:\r\nSERVER: Mock/1.0 UPnP/1.0\r\nLOCATION: http://127.0.0.1:" + http.Port + "/rootDesc.xml\r\n\r\n";
                byte[] b = Encoding.ASCII.GetBytes(resp);
                udp.Send(b, b.Length, from);
            }
            catch (Exception) { }
        }
    }

    private string DescXml()
    {
        string conn = UsePppService ? "WANPPPConnection" : "WANIPConnection";
        return "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\"><specVersion><major>1</major><minor>0</minor></specVersion><device>"
            + "<deviceType>urn:schemas-upnp-org:device:InternetGatewayDevice:1</deviceType><friendlyName>Mock Router</friendlyName><deviceList><device>"
            + "<deviceType>urn:schemas-upnp-org:device:WANDevice:1</deviceType><serviceList><service><serviceType>urn:schemas-upnp-org:service:WANCommonInterfaceConfig:1</serviceType><controlURL>/ctl/CmnIfCfg</controlURL></service></serviceList>"
            + "<deviceList><device><deviceType>urn:schemas-upnp-org:device:WANConnectionDevice:1</deviceType><serviceList><service><serviceType>urn:schemas-upnp-org:service:" + conn + ":1</serviceType>"
            + "<serviceId>urn:upnp-org:serviceId:" + conn + "1</serviceId><controlURL>/ctl/IPConn</controlURL><eventSubURL>/evt/IPConn</eventSubURL><SCPDURL>/WANIPCn.xml</SCPDURL></service></serviceList></device></deviceList>"
            + "</device></deviceList></device></root>";
    }

    private static string Arg(string body, string name)
    {
        Match m = Regex.Match(body, "<" + name + ">([^<]*)</" + name + ">");
        return m.Success ? m.Groups[1].Value : "";
    }

    private void Err(int code, out int status, out string ct, out string resp)
    {
        status = 500; ct = "text/xml";
        resp = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>UPnPError</faultstring><detail><UPnPError xmlns=\"urn:schemas-upnp-org:control-1-0\"><errorCode>" + code + "</errorCode><errorDescription>mock</errorDescription></UPnPError></detail></s:Fault></s:Body></s:Envelope>";
    }

    private void HandleHttp(string method, string path, Dictionary<string, string> headers, string body, out int status, out string ct, out string resp)
    {
        status = 200; ct = "text/xml"; resp = "";
        if (method == "GET" && path == "/rootDesc.xml")
        {
            if (BreakDescription) { status = 404; resp = "no"; return; }
            resp = DescXml(); return;
        }
        if (method == "POST" && path == "/ctl/IPConn")
        {
            string sa = headers.ContainsKey("SOAPAction") ? headers["SOAPAction"].Trim('"') : "";
            string action = sa.Contains("#") ? sa.Substring(sa.IndexOf('#') + 1) : "";
            string svc = sa.Contains("#") ? sa.Substring(0, sa.IndexOf('#')) : "";
            lock (Log)
            {
                if (action == "AddPortMapping")
                {
                    int port = int.Parse(Arg(body, "NewExternalPort")); string lease = Arg(body, "NewLeaseDuration");
                    string line = "AddPortMapping port=" + port + " proto=" + Arg(body, "NewProtocol") + " internalPort=" + Arg(body, "NewInternalPort") + " internal=" + Arg(body, "NewInternalClient") + " lease=" + lease + " enabled=" + Arg(body, "NewEnabled") + " svc=" + svc;
                    Log.Add(line);
                    if (RejectPermanentLease && lease == "0") { Err(725, out status, out ct, out resp); return; }
                    if (ConflictOnce) { ConflictOnce = false; Err(718, out status, out ct, out resp); return; }
                    Mappings[port] = line;
                    resp = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:AddPortMappingResponse xmlns:u=\"" + svc + "\"/></s:Body></s:Envelope>";
                    return;
                }
                if (action == "DeletePortMapping")
                {
                    int port = int.Parse(Arg(body, "NewExternalPort"));
                    Log.Add("DeletePortMapping port=" + port + " proto=" + Arg(body, "NewProtocol"));
                    Mappings.Remove(port);
                    resp = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:DeletePortMappingResponse xmlns:u=\"" + svc + "\"/></s:Body></s:Envelope>";
                    return;
                }
                if (action == "GetExternalIPAddress")
                {
                    Log.Add("GetExternalIPAddress");
                    resp = "<?xml version=\"1.0\"?><s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\"><s:Body><u:GetExternalIPAddressResponse xmlns:u=\"" + svc + "\"><NewExternalIPAddress>" + ExternalIp + "</NewExternalIPAddress></u:GetExternalIPAddressResponse></s:Body></s:Envelope>";
                    return;
                }
            }
            Err(401, out status, out ct, out resp); return;
        }
        status = 404; resp = "no";
    }

    public bool HasLog(string startsWith) { lock (Log) { foreach (string l in Log) if (l.StartsWith(startsWith)) return true; } return false; }
    public string FirstLog(string startsWith) { lock (Log) { foreach (string l in Log) if (l.StartsWith(startsWith)) return l; } return null; }
    public int CountLog(string startsWith) { int n = 0; lock (Log) { foreach (string l in Log) if (l.StartsWith(startsWith)) n++; } return n; }
    public int MappingCount { get { lock (Log) { return Mappings.Count; } } }

    public void Dispose() { stop = true; try { udp.Close(); } catch (Exception) { } http.Dispose(); }
}

public sealed class MockNatPmp : IDisposable
{
    public string ExternalIp = "198.51.100.9";
    public readonly List<string> Log = new List<string>();
    public int Port { get; private set; }
    private readonly UdpClient udp;
    private volatile bool stop;
    public MockNatPmp()
    {
        udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)udp.Client.LocalEndPoint).Port;
        new Thread(Loop) { IsBackground = true }.Start();
    }
    private void Loop()
    {
        while (!stop)
        {
            try
            {
                if (udp.Client.Available == 0) { Thread.Sleep(5); continue; }
                IPEndPoint from = new IPEndPoint(IPAddress.Any, 0);
                byte[] d = udp.Receive(ref from);
                if (d.Length == 2 && d[0] == 0 && d[1] == 0)
                {
                    lock (Log) Log.Add("external-address");
                    string[] ip = ExternalIp.Split('.');
                    byte[] r = new byte[12]; r[1] = 128; r[7] = 1;
                    for (int i = 0; i < 4; i++) r[8 + i] = byte.Parse(ip[i]);
                    udp.Send(r, r.Length, from);
                }
                else if (d.Length == 12 && d[1] == 2)
                {
                    int iport = d[4] << 8 | d[5], eport = d[6] << 8 | d[7];
                    int life = d[8] << 24 | d[9] << 16 | d[10] << 8 | d[11];
                    lock (Log) Log.Add("map tcp internal=" + iport + " external=" + eport + " lifetime=" + life);
                    byte[] r = new byte[16]; r[1] = 130; r[7] = 1;
                    r[8] = d[4]; r[9] = d[5]; r[10] = d[6]; r[11] = d[7]; r[12] = d[8]; r[13] = d[9]; r[14] = d[10]; r[15] = d[11];
                    udp.Send(r, r.Length, from);
                }
            }
            catch (Exception) { }
        }
    }
    public bool Has(string startsWith) { lock (Log) { foreach (string l in Log) if (l.StartsWith(startsWith)) return true; } return false; }
    public void Dispose() { stop = true; try { udp.Close(); } catch (Exception) { } }
}
