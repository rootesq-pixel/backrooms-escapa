using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

// Capa de transporte propia sobre TCP (System.Net.Sockets): una conexión por jugador, mensajes con prefijo de longitud,
// sockets no bloqueantes que se "bombean" desde el hilo principal (sin hilos salvo para el connect con tiempo máximo).
// TCP da entrega fiable y ordenada, detección de desconexión y un único puerto (TCP) que abrir en el router.

public sealed class NetWriter
{
    private readonly MemoryStream ms = new MemoryStream(128);
    private readonly BinaryWriter w;
    public NetWriter(byte type) { w = new BinaryWriter(ms, Encoding.UTF8); w.Write(type); }
    public NetWriter Byte(int v) { w.Write((byte)v); return this; }
    public NetWriter Int(int v) { w.Write(v); return this; }
    public NetWriter Float(float v) { w.Write(v); return this; }
    public NetWriter Bool(bool v) { w.Write(v); return this; }
    public NetWriter Str(string v) { w.Write(v ?? string.Empty); return this; }
    public NetWriter Vec3(Vector3 v) { w.Write(v.x); w.Write(v.y); w.Write(v.z); return this; }
    public NetWriter Long(long v) { w.Write(v); return this; }
    public byte[] ToArray() { w.Flush(); return ms.ToArray(); }
}

public sealed class NetReader
{
    private readonly BinaryReader r;
    public readonly byte Type;
    public NetReader(byte[] data)
    {
        r = new BinaryReader(new MemoryStream(data, false), Encoding.UTF8);
        Type = r.ReadByte();
    }
    public int Byte() { return r.ReadByte(); }
    public int Int() { return r.ReadInt32(); }
    public float Float() { return r.ReadSingle(); }
    public bool Bool() { return r.ReadBoolean(); }
    public string Str() { return r.ReadString(); }
    public long Long() { return r.ReadInt64(); }
    public Vector3 Vec3() { return new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()); }
    public bool End { get { return r.BaseStream.Position >= r.BaseStream.Length; } }
}

// Una conexión TCP (lado anfitrión: una por cliente; lado cliente: la del anfitrión)
public sealed class NetConn
{
    public Socket Socket;
    public int PlayerId = -1;
    public string Name = "";
    public bool HelloDone;
    public float LastRecv;
    public bool Dead;
    public string DeadReason = "";
    public readonly Queue<byte[]> Incoming = new Queue<byte[]>();
    public long BytesIn, BytesOut;

    private byte[] rbuf = new byte[16384];
    private int rlen;
    private byte[] obuf = new byte[16384];
    private int olen;
    public const int MaxMessage = 262144;

    public NetConn(Socket s, float now)
    {
        Socket = s;
        s.Blocking = false;
        s.NoDelay = true;
        LastRecv = now;
    }

    public string RemoteEndPoint
    {
        get { try { return Socket != null && Socket.RemoteEndPoint != null ? Socket.RemoteEndPoint.ToString() : "?"; } catch (Exception) { return "?"; } }
    }

    public void Kill(string reason)
    {
        if (Dead) return;
        Dead = true;
        DeadReason = reason;
        try { Socket.Close(); } catch (Exception) { }
    }

    // Encola un mensaje (type + payload) con prefijo de longitud
    public void Send(byte[] msg)
    {
        if (Dead || msg == null) return;
        int need = olen + 4 + msg.Length;
        if (need > 4 * 1024 * 1024) { Kill("búfer de salida lleno (conexión demasiado lenta)"); return; }
        if (need > obuf.Length) Array.Resize(ref obuf, Mathf.Max(need, obuf.Length * 2));
        BitConverter.GetBytes(msg.Length).CopyTo(obuf, olen);
        Buffer.BlockCopy(msg, 0, obuf, olen + 4, msg.Length);
        olen += 4 + msg.Length;
    }

    // Lee lo disponible, trocea mensajes y vacía la cola de salida
    public void Pump(float now)
    {
        if (Dead) return;
        try
        {
            while (Socket.Poll(0, SelectMode.SelectRead))
            {
                if (Socket.Available == 0) { Kill("conexión cerrada"); return; }
                if (rlen == rbuf.Length)
                {
                    if (rbuf.Length >= 2 * MaxMessage) { Kill("mensaje demasiado grande"); return; }
                    Array.Resize(ref rbuf, rbuf.Length * 2);
                }
                int n = Socket.Receive(rbuf, rlen, rbuf.Length - rlen, SocketFlags.None);
                if (n <= 0) { Kill("conexión cerrada"); return; }
                rlen += n; BytesIn += n; LastRecv = now;
                if (!Parse()) return;
            }
            Flush();
        }
        catch (SocketException e)
        {
            if (e.SocketErrorCode != SocketError.WouldBlock) Kill("error de red (" + e.SocketErrorCode + ")");
        }
        catch (ObjectDisposedException) { Dead = true; }
    }

    private bool Parse()
    {
        int pos = 0;
        while (rlen - pos >= 4)
        {
            int len = BitConverter.ToInt32(rbuf, pos);
            if (len <= 0 || len > MaxMessage) { Kill("mensaje no válido"); return false; }
            if (rlen - pos - 4 < len) break;
            byte[] m = new byte[len];
            Buffer.BlockCopy(rbuf, pos + 4, m, 0, len);
            Incoming.Enqueue(m);
            pos += 4 + len;
        }
        if (pos > 0)
        {
            Buffer.BlockCopy(rbuf, pos, rbuf, 0, rlen - pos);
            rlen -= pos;
        }
        return true;
    }

    public void Flush()
    {
        if (Dead) return;
        try
        {
            while (olen > 0)
            {
                int n = Socket.Send(obuf, 0, olen, SocketFlags.None);
                if (n <= 0) break;
                BytesOut += n;
                Buffer.BlockCopy(obuf, n, obuf, 0, olen - n);
                olen -= n;
            }
        }
        catch (SocketException e)
        {
            if (e.SocketErrorCode != SocketError.WouldBlock) Kill("error de red (" + e.SocketErrorCode + ")");
        }
        catch (ObjectDisposedException) { Dead = true; }
    }

    // Intenta enviar lo pendiente antes de cerrar (mensajes de despedida)
    public void CloseGracefully()
    {
        if (Dead) return;
        Flush();
        Kill("cerrada");
    }
}

// Conexión TCP saliente bloqueante (para hilos de fondo). Devuelve null y un código de error si falla:
// "timeout", "refused", "badaddr", "unreachable", "other:..."
public static class NetSock
{
    public static Socket Connect(string host, int port, int timeoutMs, out string error)
    {
        error = null;
        Socket s = null;
        try
        {
            IPAddress ip;
            string h = (host ?? "").Trim();
            if (!IPAddress.TryParse(h, out ip))
            {
                IPAddress[] list = Dns.GetHostAddresses(h);
                ip = null;
                foreach (IPAddress a in list) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a; break; }
                if (ip == null) { error = "badaddr"; return null; }
            }
            if (ip.AddressFamily != AddressFamily.InterNetwork) { error = "badaddr"; return null; }
            s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IAsyncResult ar = s.BeginConnect(new IPEndPoint(ip, port), null, null);
            if (!ar.AsyncWaitHandle.WaitOne(timeoutMs))
            {
                try { s.Close(); } catch (Exception) { }
                error = "timeout"; return null;
            }
            try { s.EndConnect(ar); return s; }
            catch (SocketException e)
            {
                try { s.Close(); } catch (Exception) { }
                switch (e.SocketErrorCode)
                {
                    case SocketError.ConnectionRefused: error = "refused"; break;
                    case SocketError.HostUnreachable: case SocketError.NetworkUnreachable: error = "unreachable"; break;
                    case SocketError.TimedOut: error = "timeout"; break;
                    default: error = "other:" + e.SocketErrorCode; break;
                }
                return null;
            }
        }
        catch (SocketException e)
        {
            if (s != null) { try { s.Close(); } catch (Exception) { } }
            error = e.SocketErrorCode == SocketError.HostNotFound || e.SocketErrorCode == SocketError.TryAgain ? "badaddr" : "other:" + e.SocketErrorCode;
            return null;
        }
        catch (Exception e) { error = "other:" + e.GetType().Name; return null; }
    }

    // Lee una línea de texto (hasta \n) byte a byte, sin pasarse (lo que venga después pertenece al flujo de datos)
    public static string ReadLine(Socket s, int timeoutMs, Func<bool> cancelled = null)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        byte[] one = new byte[1];
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            if (cancelled != null && cancelled()) throw new OperationCanceledException();
            long left = timeoutMs - sw.ElapsedMilliseconds;
            if (left <= 0) throw new TimeoutException();
            if (!s.Poll((int)Math.Min(left, 300) * 1000, SelectMode.SelectRead)) continue;
            int n = s.Receive(one);
            if (n == 0) throw new System.IO.IOException("cerrada");
            if (one[0] == 10) return sb.ToString().TrimEnd('\r');
            if (sb.Length > 256) throw new System.IO.IOException("línea demasiado larga");
            sb.Append((char)one[0]);
        }
    }

    public static void WriteLine(Socket s, string line)
    {
        byte[] b = System.Text.Encoding.UTF8.GetBytes(line + "\n");
        int off = 0;
        while (off < b.Length) off += s.Send(b, off, b.Length - off, SocketFlags.None);
    }
}

// Conexión saliente con tiempo máximo (hilo aparte para no congelar el juego) — transporte directo TCP
public sealed class NetConnector : INetConnector
{
    private volatile bool done;
    private volatile string error;
    private Socket result;
    private readonly string host;
    private readonly int port;
    private readonly int timeoutMs;
    public bool Done { get { return done; } }
    public string Error { get { return error; } }     // código interno: "timeout", "refused", "badaddr", "unreachable", "other:..."
    public Socket Result { get { return result; } }

    public NetConnector(string host, int port, int timeoutMs)
    {
        this.host = host; this.port = port; this.timeoutMs = timeoutMs;
        Thread t = new Thread(Run) { IsBackground = true, Name = "NetConnect" };
        t.Start();
    }

    private void Run()
    {
        string e;
        Socket s = NetSock.Connect(host, port, timeoutMs, out e);
        result = s; error = e;
        done = true;
    }
}

// Servidor TCP: acepta conexiones sin bloquear — transporte directo TCP
public sealed class NetListener : INetListener
{
    private TcpListener listener;
    public int Port { get; private set; }
    public string RoomCode { get { return ""; } }
    public string Status { get { return ""; } }
    public string Error { get { return null; } }

    public bool Start(int port, out string error)
    {
        error = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            // Windows: acceso exclusivo (SO_REUSEADDR permitiría que dos procesos escuchen el mismo puerto y no hace falta para reabrir).
            // Linux/macOS (editor, pruebas): SO_REUSEADDR para poder volver a alojar enseguida en el mismo puerto (TIME_WAIT).
            if (UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsPlayer || UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor)
                listener.ExclusiveAddressUse = true;
            else
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start(8);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            return true;
        }
        catch (SocketException e)
        {
            error = e.SocketErrorCode == SocketError.AddressAlreadyInUse
                ? "El puerto " + port + " ya está en uso (¿hay otra partida abierta?). Prueba con otro puerto."
                : "No se pudo abrir el puerto " + port + " (" + e.SocketErrorCode + ").";
            listener = null;
            return false;
        }
    }

    public Socket Accept()
    {
        if (listener == null) return null;
        try { if (listener.Pending()) return listener.AcceptSocket(); }
        catch (SocketException) { }
        return null;
    }

    public void Stop()
    {
        if (listener != null) { try { listener.Stop(); } catch (Exception) { } listener = null; }
    }
}

public static class NetUtil
{
    // Direcciones IPv4 locales (LAN / VPN) para mostrarlas al anfitrión
    public static List<string> LocalAddresses()
    {
        List<string> list = new List<string>();
        try
        {
            foreach (System.Net.NetworkInformation.NetworkInterface ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (System.Net.NetworkInformation.UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string s = ua.Address.ToString();
                    if (s.StartsWith("169.254.")) continue;
                    list.Add(s + "  (" + ni.Name + ")");
                }
            }
        }
        catch (Exception) { }
        if (list.Count == 0) list.Add("127.0.0.1  (solo este equipo)");
        return list;
    }

    // FNV-1a de 64 bits para huellas del mundo
    public static ulong Fnv(ulong h, long v)
    {
        unchecked
        {
            for (int i = 0; i < 8; i++) { h ^= (ulong)((v >> (i * 8)) & 0xff); h *= 1099511628211UL; }
        }
        return h;
    }
    public const ulong FnvBasis = 14695981039346656037UL;
    public static ulong FnvVec(ulong h, Vector3 v)
    {
        h = Fnv(h, Mathf.RoundToInt(v.x * 1000f));
        h = Fnv(h, Mathf.RoundToInt(v.y * 1000f));
        h = Fnv(h, Mathf.RoundToInt(v.z * 1000f));
        return h;
    }
}
