using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Xml;
using UnityEngine;

// ---------------------------------------------------------------------------------------------------------------------
// Apertura automática de puerto en el router del anfitrión: UPnP IGD (SSDP + SOAP AddPortMapping) y, si falla, NAT-PMP.
// Solo System.Net. Todo en un hilo de fondo con tiempos máximos; si algo falla, simplemente queda en estado «Failed».
// La asignación se elimina al parar (Stop) para no dejar puertos abiertos.
// ---------------------------------------------------------------------------------------------------------------------
public sealed class UpnpMapper
{
    public enum State { Idle, Working, Mapped, Failed }

    // configuración (los valores por defecto son los reales; las pruebas apuntan a servidores simulados)
    public IPEndPoint SsdpEndpoint = new IPEndPoint(IPAddress.Parse("239.255.255.250"), 1900);
    public int DiscoveryMs = 2500, HttpMs = 4000, NatPmpTryMs = 250;
    public bool EnableNatPmp = true;
    public IPAddress GatewayOverride;          // NAT-PMP: IP del router (por defecto la puerta de enlace de la red)
    public int NatPmpPort = 5351;
    public string Description = "Backrooms Escape";

    private volatile State state = State.Idle;
    private volatile string method = "", externalIp = "", detail = "";
    private int port;
    private Thread thread;
    private readonly ManualResetEvent stopEvt = new ManualResetEvent(false);
    private volatile bool removed;

    // datos del mapeo para poder renovarlo / borrarlo
    private string controlUrl, serviceType, internalIp = "";
    private int leaseSeconds;
    private IPAddress pmpGateway;

    public State Status { get { return state; } }
    public string Method { get { return method; } }
    public string ExternalIp { get { return externalIp; } }       // IP WAN del router (la que informa el router)
    public string Detail { get { return detail; } }
    public int Port { get { return port; } }
    public bool Removed { get { return removed; } }

    public string StatusText
    {
        get
        {
            switch (state)
            {
                case State.Working: return "Intentando abrir el puerto automáticamente (UPnP / NAT-PMP)...";
                case State.Mapped: return "Puerto abierto automáticamente (" + method + ", TCP " + port + ")";
                case State.Failed: return "No se pudo abrir el puerto automáticamente: abre TCP " + port + " en tu router o usa una VPN";
                default: return "";
            }
        }
    }

    public void Start(int tcpPort)
    {
        port = tcpPort;
        state = State.Working;
        thread = new Thread(Run) { IsBackground = true, Name = "UpnpMapper" };
        thread.Start();
    }

    // wait = true: espera (máx. 3 s) a que se haya borrado la asignación (al cerrar el juego)
    public void Stop(bool wait)
    {
        stopEvt.Set();
        if (wait && thread != null) thread.Join(3000);
    }

    private void Run()
    {
        try
        {
            bool ok = false;
            if (!stopEvt.WaitOne(0)) ok = TryUpnp();
            if (!ok && EnableNatPmp && !stopEvt.WaitOne(0)) ok = TryNatPmp();
            if (!ok) { state = State.Failed; return; }
            state = State.Mapped;
            // renovación (solo si el router no admite concesiones permanentes) hasta que nos paren
            while (true)
            {
                int wait = leaseSeconds > 0 ? Math.Max(5, leaseSeconds / 2) * 1000 : Timeout.Infinite;
                if (stopEvt.WaitOne(wait)) break;
                if (method == "UPnP") AddMapping(); else PmpMap(leaseSeconds);
            }
            RemoveMapping();
        }
        catch (Exception e) { detail = e.GetType().Name + ": " + e.Message; if (state != State.Mapped) state = State.Failed; else try { RemoveMapping(); } catch (Exception) { } }
    }

    // ------------------------------------------------------------------ UPnP IGD
    private bool TryUpnp()
    {
        List<string> locations = Discover();
        if (locations.Count == 0) { detail = "sin respuesta SSDP"; return false; }
        foreach (string loc in locations)
        {
            if (stopEvt.WaitOne(0)) return false;
            try
            {
                string xml = HttpGet(loc);
                string ctl, svc;
                if (!ParseDescription(xml, loc, out ctl, out svc)) { detail = "sin servicio WANIPConnection en " + loc; continue; }
                controlUrl = ctl; serviceType = svc;
                Uri cu = new Uri(ctl);
                internalIp = LocalAddressFor(cu.Host);
                if (!AddMapping()) continue;
                string resp; int err;
                if (Soap("GetExternalIPAddress", "", out resp, out err))
                {
                    string ip = XmlValue(resp, "NewExternalIPAddress");
                    IPAddress parsed;
                    if (!string.IsNullOrEmpty(ip) && IPAddress.TryParse(ip, out parsed)) externalIp = ip;
                }
                method = "UPnP";
                return true;
            }
            catch (Exception e) { detail = "UPnP: " + e.Message; }
        }
        return false;
    }

    public static string[] SearchTargets = {
        "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:device:InternetGatewayDevice:2" };

    private List<string> Discover()
    {
        List<string> found = new List<string>();
        Socket s = null;
        try
        {
            s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Bind(new IPEndPoint(IPAddress.Any, 0));
            try { s.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2); } catch (Exception) { }
            foreach (string st in SearchTargets)
            {
                string req = "M-SEARCH * HTTP/1.1\r\nHOST: 239.255.255.250:1900\r\nMAN: \"ssdp:discover\"\r\nMX: 2\r\nST: " + st + "\r\n\r\n";
                byte[] b = Encoding.ASCII.GetBytes(req);
                try { s.SendTo(b, SsdpEndpoint); } catch (Exception e) { detail = "SSDP: " + e.Message; }
            }
            byte[] buf = new byte[4096];
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < DiscoveryMs && !stopEvt.WaitOne(0))
            {
                if (!s.Poll(200 * 1000, SelectMode.SelectRead)) continue;
                EndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                int n = s.ReceiveFrom(buf, ref ep);
                string text = Encoding.ASCII.GetString(buf, 0, n);
                foreach (string line in text.Split('\n'))
                {
                    string l = line.Trim();
                    if (l.StartsWith("location:", StringComparison.OrdinalIgnoreCase))
                    {
                        string loc = l.Substring(9).Trim();
                        if (loc.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !found.Contains(loc)) found.Add(loc);
                    }
                }
                if (found.Count > 0 && sw.ElapsedMilliseconds > 400) break;   // ya hay respuesta: no esperamos a todo el plazo
            }
        }
        catch (Exception e) { detail = "SSDP: " + e.Message; }
        finally { if (s != null) { try { s.Close(); } catch (Exception) { } } }
        return found;
    }

    public static bool ParseDescription(string xml, string location, out string controlUrlOut, out string serviceTypeOut)
    {
        controlUrlOut = serviceTypeOut = null;
        XmlDocument doc = new XmlDocument();
        doc.XmlResolver = null;
        doc.LoadXml(xml);
        string baseUrl = location;
        foreach (XmlNode n in doc.GetElementsByTagName("URLBase")) if (!string.IsNullOrEmpty(n.InnerText.Trim())) baseUrl = n.InnerText.Trim();
        string bestCtl = null, bestType = null; int bestScore = -1;
        foreach (XmlNode n in doc.GetElementsByTagName("service"))
        {
            string type = null, ctl = null;
            foreach (XmlNode c in n.ChildNodes)
            {
                if (c.LocalName == "serviceType") type = c.InnerText.Trim();
                else if (c.LocalName == "controlURL") ctl = c.InnerText.Trim();
            }
            if (type == null || ctl == null) continue;
            int score = type.Contains(":WANIPConnection:") ? 2 : type.Contains(":WANPPPConnection:") ? 1 : -1;
            if (score > bestScore) { bestScore = score; bestCtl = ctl; bestType = type; }
        }
        if (bestScore < 0) return false;
        controlUrlOut = new Uri(new Uri(baseUrl), bestCtl).ToString();
        serviceTypeOut = bestType;
        return true;
    }

    public static string BuildSoapEnvelope(string serviceType, string action, string argsXml)
    {
        return "<?xml version=\"1.0\"?>\r\n<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\"><s:Body><u:" + action + " xmlns:u=\"" + serviceType + "\">"
            + argsXml + "</u:" + action + "></s:Body></s:Envelope>";
    }

    public static string BuildAddMappingArgs(int port, string internalClient, string description, int lease)
    {
        return "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port + "</NewExternalPort><NewProtocol>TCP</NewProtocol><NewInternalPort>" + port
            + "</NewInternalPort><NewInternalClient>" + internalClient + "</NewInternalClient><NewEnabled>1</NewEnabled><NewPortMappingDescription>"
            + System.Security.SecurityElement.Escape(description) + "</NewPortMappingDescription><NewLeaseDuration>" + lease + "</NewLeaseDuration>";
    }

    private bool AddMapping()
    {
        string resp; int err;
        // 1.º concesión permanente (0); si el router solo admite temporales, 2 h renovables
        int[] leases = leaseSeconds > 0 ? new[] { leaseSeconds } : new[] { 0, 7200 };
        foreach (int lease in leases)
        {
            string args = BuildAddMappingArgs(port, internalIp, Description, lease);
            if (Soap("AddPortMapping", args, out resp, out err)) { leaseSeconds = lease; return true; }
            if (err == 718 || err == 727)    // conflicto: probablemente un resto nuestro; lo borramos y repetimos
            {
                Soap("DeletePortMapping", "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port + "</NewExternalPort><NewProtocol>TCP</NewProtocol>", out resp, out err);
                if (Soap("AddPortMapping", args, out resp, out err)) { leaseSeconds = lease; return true; }
            }
            detail = "AddPortMapping lease=" + lease + " error=" + err;
        }
        return false;
    }

    private void RemoveMapping()
    {
        if (removed) return;
        if (method == "UPnP")
        {
            string resp; int err;
            Soap("DeletePortMapping", "<NewRemoteHost></NewRemoteHost><NewExternalPort>" + port + "</NewExternalPort><NewProtocol>TCP</NewProtocol>", out resp, out err);
        }
        else if (method == "NAT-PMP") PmpMap(0);
        removed = true;
        state = State.Idle;
    }

    private bool Soap(string action, string argsXml, out string response, out int upnpError)
    {
        response = ""; upnpError = 0;
        try
        {
            string body = BuildSoapEnvelope(serviceType, action, argsXml);
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(controlUrl);
            req.Method = "POST"; req.Proxy = null; req.KeepAlive = false;
            req.Timeout = HttpMs; req.ReadWriteTimeout = HttpMs;
            req.ContentType = "text/xml; charset=\"utf-8\"";
            req.Headers.Add("SOAPAction", "\"" + serviceType + "#" + action + "\"");
            byte[] data = Encoding.UTF8.GetBytes(body);
            req.ContentLength = data.Length;
            using (Stream st = req.GetRequestStream()) st.Write(data, 0, data.Length);
            using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
            using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) { response = sr.ReadToEnd(); }
            return true;
        }
        catch (WebException we)
        {
            if (we.Response != null)
            {
                try
                {
                    using (StreamReader sr = new StreamReader(we.Response.GetResponseStream(), Encoding.UTF8)) response = sr.ReadToEnd();
                    string code = XmlValue(response, "errorCode");
                    int.TryParse(code, out upnpError);
                }
                catch (Exception) { }
            }
            detail = action + ": " + we.Message;
            return false;
        }
        catch (Exception e) { detail = action + ": " + e.Message; return false; }
    }

    private string HttpGet(string url)
    {
        HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
        req.Proxy = null; req.KeepAlive = false; req.Timeout = HttpMs; req.ReadWriteTimeout = HttpMs;
        using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
        using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8)) return sr.ReadToEnd();
    }

    public static string XmlValue(string xml, string localName)
    {
        try
        {
            XmlDocument d = new XmlDocument(); d.XmlResolver = null; d.LoadXml(xml);
            foreach (XmlNode n in d.GetElementsByTagName("*")) if (n.LocalName == localName) return n.InnerText.Trim();
        }
        catch (Exception) { }
        return "";
    }

    // dirección local desde la que se llega al router (la que debe recibir las conexiones)
    private static string LocalAddressFor(string routerHost)
    {
        try
        {
            IPAddress ip;
            if (!IPAddress.TryParse(routerHost, out ip)) ip = Dns.GetHostAddresses(routerHost)[0];
            using (Socket u = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                u.Connect(new IPEndPoint(ip, 9));
                return ((IPEndPoint)u.LocalEndPoint).Address.ToString();
            }
        }
        catch (Exception) { }
        List<string> l = NetUtil.LocalAddresses();
        foreach (string a in l) { int sp = a.IndexOf(' '); string only = sp > 0 ? a.Substring(0, sp) : a; IPAddress p; if (IPAddress.TryParse(only, out p)) return only; }
        return "127.0.0.1";
    }

    // ------------------------------------------------------------------ NAT-PMP (RFC 6886)
    private bool TryNatPmp()
    {
        List<IPAddress> gws = new List<IPAddress>();
        if (GatewayOverride != null) gws.Add(GatewayOverride);
        else
        {
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (GatewayIPAddressInformation g in ni.GetIPProperties().GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any) && !gws.Contains(g.Address)) gws.Add(g.Address);
                }
            }
            catch (Exception e) { detail = "NAT-PMP: " + e.Message; }
        }
        foreach (IPAddress gw in gws)
        {
            if (stopEvt.WaitOne(0)) return false;
            byte[] resp = PmpExchange(gw, new byte[] { 0, 0 }, 12);
            if (resp == null || resp[1] != 128 || (resp[2] << 8 | resp[3]) != 0) continue;
            string ext = resp[8] + "." + resp[9] + "." + resp[10] + "." + resp[11];
            pmpGateway = gw;
            leaseSeconds = 7200;
            if (!PmpMap(leaseSeconds)) continue;
            externalIp = ext;
            method = "NAT-PMP";
            return true;
        }
        if (detail == "") detail = "sin respuesta NAT-PMP";
        return false;
    }

    private bool PmpMap(int lifetime)
    {
        if (pmpGateway == null) return false;
        byte[] req = new byte[12];
        req[0] = 0; req[1] = 2;                                       // versión 0, opcode 2 = mapear TCP
        req[4] = (byte)(port >> 8); req[5] = (byte)port;              // puerto interno
        int extPort = lifetime == 0 ? 0 : port;
        req[6] = (byte)(extPort >> 8); req[7] = (byte)extPort;        // puerto externo sugerido
        req[8] = (byte)(lifetime >> 24); req[9] = (byte)(lifetime >> 16); req[10] = (byte)(lifetime >> 8); req[11] = (byte)lifetime;
        byte[] resp = PmpExchange(pmpGateway, req, 16);
        return resp != null && resp[1] == 130 && (resp[2] << 8 | resp[3]) == 0;
    }

    private byte[] PmpExchange(IPAddress gw, byte[] req, int expectLen)
    {
        Socket s = null;
        try
        {
            s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect(new IPEndPoint(gw, NatPmpPort));
            int wait = NatPmpTryMs;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                s.Send(req);
                if (s.Poll(wait * 1000, SelectMode.SelectRead))
                {
                    byte[] buf = new byte[64];
                    int n = s.Receive(buf);
                    if (n >= expectLen) { byte[] r = new byte[n]; Array.Copy(buf, r, n); return r; }
                }
                wait *= 2;
            }
        }
        catch (Exception e) { detail = "NAT-PMP: " + e.Message; }
        finally { if (s != null) { try { s.Close(); } catch (Exception) { } } }
        return null;
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// IP pública: consulta a un servicio «¿cuál es mi IP?» en segundo plano, con tiempo máximo y sin romper nada si no hay Internet.
// ---------------------------------------------------------------------------------------------------------------------
public static class PublicIpService
{
    public enum State { Idle, Working, Ok, Failed }

    public static string[] Endpoints = { "https://api.ipify.org", "http://api.ipify.org", "https://icanhazip.com" };
    public static int TimeoutMs = 4000;

    private static volatile State state = State.Idle;
    private static volatile string ip = "";
    private static DateTime stamp = DateTime.MinValue;

    public static State Status { get { return state; } }
    public static string Ip { get { return ip; } }

    public static void Reset() { state = State.Idle; ip = ""; stamp = DateTime.MinValue; }

    public static void Refresh(bool force)
    {
        if (state == State.Working) return;
        double age = (DateTime.UtcNow - stamp).TotalSeconds;
        if (!force && state == State.Ok && age < 300) return;
        if (!force && state == State.Failed && age < 15) return;
        state = State.Working;
        string[] eps = (string[])Endpoints.Clone();
        Thread t = new Thread(() => Query(eps)) { IsBackground = true, Name = "PublicIp" };
        t.Start();
    }

    private static void Query(string[] eps)
    {
        string found = "";
        foreach (string url in eps)
        {
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = TimeoutMs; req.ReadWriteTimeout = TimeoutMs; req.UserAgent = "BackroomsEscape/6.1"; req.KeepAlive = false;
                using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.ASCII))
                {
                    char[] buf = new char[80]; int n = sr.Read(buf, 0, buf.Length);
                    string text = new string(buf, 0, Math.Max(0, n)).Trim();
                    IPAddress parsed;
                    if (IPAddress.TryParse(text, out parsed) && parsed.AddressFamily == AddressFamily.InterNetwork) { found = text; break; }
                }
            }
            catch (Exception) { /* sin Internet / certificado / bloqueado: probamos el siguiente */ }
        }
        stamp = DateTime.UtcNow;
        ip = found;
        state = found.Length > 0 ? State.Ok : State.Failed;
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Textos de la pantalla del anfitrión (puros, probables sin Unity)
// ---------------------------------------------------------------------------------------------------------------------
public static class HostAddressInfo
{
    public static bool IsPrivateOrCgnat(string ip)
    {
        IPAddress a;
        if (!IPAddress.TryParse(ip ?? "", out a) || a.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] b = a.GetAddressBytes();
        if (b[0] == 10 || b[0] == 127) return true;
        if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
        if (b[0] == 192 && b[1] == 168) return true;
        if (b[0] == 169 && b[1] == 254) return true;
        if (b[0] == 100 && b[1] >= 64 && b[1] <= 127) return true;   // 100.64.0.0/10 (CGNAT)
        return false;
    }

    // Aviso de CGNAT: la IP WAN del router no coincide con la IP pública, o es una IP privada / de rango CGNAT
    public static string CgnatWarning(string routerWanIp, string publicIp)
    {
        bool wanKnown = !string.IsNullOrEmpty(routerWanIp), pubKnown = !string.IsNullOrEmpty(publicIp);
        bool cg = (wanKnown && IsPrivateOrCgnat(routerWanIp)) || (wanKnown && pubKnown && routerWanIp != publicIp);
        if (!cg) return "";
        return "AVISO: tu operador usa CGNAT (la IP del router" + (wanKnown ? " " + routerWanIp : "") + (pubKnown ? " no es tu IP pública " + publicIp : " es privada")
            + "): desde Internet no se podrá conectar directamente. Necesitas una VPN (Radmin / ZeroTier / Hamachi) o un servidor intermedio (modo «con código»).";
    }

    // «ip:puerto» recomendado para pasar a los amigos
    public static string BestAddress(int port, string publicIp, string localIp)
    {
        string ip = !string.IsNullOrEmpty(publicIp) ? publicIp : localIp;
        return string.IsNullOrEmpty(ip) ? "" : ip + ":" + port;
    }

    public static string FirstLocalIp(IList<string> locals)
    {
        foreach (string a in locals)
        {
            string only = a.Trim(); int sp = only.IndexOf(' '); if (sp > 0) only = only.Substring(0, sp);
            IPAddress p; if (IPAddress.TryParse(only, out p) && p.AddressFamily == AddressFamily.InterNetwork) return only;
        }
        return "";
    }

    public static string Describe(int port, IList<string> locals, PublicIpService.State pubState, string pubIp, UpnpMapper.State upState, string upText, string routerWanIp, bool upnpStarted)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("Tus direcciones locales (LAN / VPN):");
        foreach (string l in locals)
        {
            int sp = l.IndexOf(' ');
            sb.AppendLine("  " + (sp > 0 ? l.Substring(0, sp) : l) + ":" + port + (sp > 0 ? l.Substring(sp) : ""));
        }
        sb.Append("IP pública: ");
        if (pubState == PublicIpService.State.Ok) sb.AppendLine(pubIp + ":" + port);
        else if (pubState == PublicIpService.State.Working || pubState == PublicIpService.State.Idle) sb.AppendLine("detectando...");
        else sb.AppendLine("no disponible (¿sin conexión a Internet?)");
        if (upnpStarted) sb.AppendLine(upText);
        else sb.AppendLine("El puerto se intentará abrir automáticamente (UPnP) al crear la sala.");
        string cg = upnpStarted ? CgnatWarning(routerWanIp, pubState == PublicIpService.State.Ok ? pubIp : "") : "";
        if (cg.Length > 0) sb.AppendLine(cg);
        return sb.ToString().TrimEnd();
    }
}
