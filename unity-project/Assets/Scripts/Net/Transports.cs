using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

// ---------------------------------------------------------------------------------------------------------------------
// Abstracción del transporte: NetSession solo ve sockets TCP ya conectados (INetListener.Accept / INetConnector.Result).
//   - DirectTcpTransport: conexión directa IP:puerto (el anfitrión escucha en un puerto TCP).
//   - RelayTransport: el anfitrión y los clientes salen a un servidor relay (backrooms-relay) que los empareja por código de sala (6 caracteres).
//     Después del apretón de manos el relay reenvía los bytes tal cual, así que el protocolo del juego no cambia.
// ---------------------------------------------------------------------------------------------------------------------

public interface INetListener
{
    int Port { get; }
    string RoomCode { get; }     // relay: código de 6 caracteres cuando la sala está abierta ("" mientras tanto); directo: ""
    string Status { get; }       // texto de estado del transporte (relay)
    string Error { get; }        // null si todo va bien; código interno si el transporte falló ("relay_unreachable", "relay:busy", "relay_lost" ...)
    bool Start(int port, out string error);
    Socket Accept();             // no bloquea; null si no hay nadie
    void Stop();
}

public interface INetConnector
{
    bool Done { get; }
    string Error { get; }        // "timeout", "refused", "badaddr", "unreachable", "relay_unreachable", "relay:<motivo>", "other:..."
    Socket Result { get; }
}

public interface ITransport
{
    string Name { get; }
    bool Available { get; }
    string UnavailableReason { get; }
    INetListener CreateListener(string hostName);
    // address: directo = IP o nombre del anfitrión; relay = código de sala
    INetConnector Connect(string address, int port, int timeoutMs);
}

public sealed class DirectTcpTransport : ITransport
{
    public string Name { get { return "TCP directo"; } }
    public bool Available { get { return true; } }
    public string UnavailableReason { get { return ""; } }
    public INetListener CreateListener(string hostName) { return new NetListener(); }
    public INetConnector Connect(string address, int port, int timeoutMs) { return new NetConnector(address, port, timeoutMs); }
}

public sealed class RelayTransport : ITransport
{
    public string Name { get { return "Relay por código"; } }
    public bool Available { get { return RelayConfig.Enabled; } }
    public string UnavailableReason { get { return RelayConfig.Enabled ? "" : "No hay servidor relay configurado."; } }
    public INetListener CreateListener(string hostName) { return new RelayListener(RelayConfig.Address, hostName, RelayConfig.Token); }
    public INetConnector Connect(string address, int port, int timeoutMs) { return new RelayConnector(RelayConfig.Address, RoomCodes.Normalize(address), timeoutMs, RelayConfig.Token); }
}

// ---------------------------------------------------------------------------------------------------------------------
// Direcciones («ip:puerto») y códigos de sala
// ---------------------------------------------------------------------------------------------------------------------
public static class NetAddress
{
    // Acepta "1.2.3.4", "1.2.3.4:7777", "host.dominio:7777", "tcp://1.2.3.4:7777". Solo IPv4 / nombres (sin IPv6).
    public static bool TryParse(string input, int defaultPort, out string host, out int port)
    {
        host = ""; port = defaultPort;
        if (string.IsNullOrEmpty(input)) return false;
        string s = input.Trim();
        int sch = s.IndexOf("://", StringComparison.Ordinal);
        if (sch >= 0) s = s.Substring(sch + 3);
        s = s.Trim().TrimEnd('/');
        if (s.Length == 0 || s.IndexOf(' ') >= 0) return false;
        int first = s.IndexOf(':'), last = s.LastIndexOf(':');
        if (first != last) return false;                 // IPv6 u otra cosa rara
        if (first >= 0)
        {
            string ps = s.Substring(first + 1);
            s = s.Substring(0, first);
            int p;
            if (ps.Length == 0 || !int.TryParse(ps, out p) || p < 1 || p > 65535) return false;
            port = p;
        }
        if (s.Length == 0) return false;
        foreach (char c in s) if (!(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')) return false;
        host = s;
        return true;
    }
}

public static class RoomCodes
{
    public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";   // sin I, O, 0, 1 (igual que el relay)
    public const int Length = 6;
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (char c in s.Trim().ToUpperInvariant()) if (!char.IsWhiteSpace(c) && c != '-') sb.Append(c);
        return sb.ToString();
    }
    public static bool IsValid(string s)
    {
        s = Normalize(s);
        if (s.Length != Length) return false;
        foreach (char c in s) if (Alphabet.IndexOf(c) < 0) return false;
        return true;
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Configuración del relay. Prioridad:  variable de entorno BACKROOMS_RELAY  >  campo del menú (PlayerPrefs)  >  relay.txt junto al .exe
//                                      >  relay oficial integrado (cifrado, ver RelayDefault).
// La dirección integrada nunca se muestra ni se escribe en logs: la interfaz usa DisplayAddress.
// ---------------------------------------------------------------------------------------------------------------------
public static class RelayConfig
{
    public const string KRelay = "mp_relay";
    public const int DefaultPort = 7780;
    public const string BuiltinLabel = "Servidor oficial (integrado)";
    public static string TestOverride;        // solo pruebas

    public static string FileAddress()
    {
        try
        {
            // junto al .exe (Application.dataPath = <exe>_Data; en el editor = <proyecto>/Assets → ../relay.txt = raíz del proyecto)
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../relay.txt"));
            if (!File.Exists(path)) return "";
            foreach (string raw in File.ReadAllLines(path))
            {
                string l = raw.Trim();
                if (l.Length == 0 || l.StartsWith("#") || l.StartsWith("//")) continue;
                return l;
            }
        }
        catch (Exception) { }
        return "";
    }

    // lo que ha puesto el usuario / el entorno (sin el relay integrado)
    public static string UserAddress
    {
        get
        {
            if (TestOverride != null) return TestOverride;
            string env = Environment.GetEnvironmentVariable("BACKROOMS_RELAY");
            if (!string.IsNullOrEmpty(env)) return env.Trim();
            string pref = PlayerPrefs.GetString(KRelay, "").Trim();
            if (pref.Length > 0) return pref;
            return FileAddress();
        }
    }

    // contenido del campo del menú: lo que escribió el usuario (nunca el relay integrado)
    public static string FieldText { get { return PlayerPrefs.GetString(KRelay, "").Trim(); } }

    public static bool UsingBuiltin { get { return TestOverride == null && UserAddress.Length == 0 && RelayDefault.HasBuiltin; } }

    public static string Address { get { string u = UserAddress; return u.Length > 0 ? u : RelayDefault.Address; } }

    // para la interfaz, errores y logs: nunca la dirección integrada
    public static string DisplayAddress { get { return UsingBuiltin ? BuiltinLabel : Address; } }

    // token del relay: BACKROOMS_RELAY_TOKEN (pruebas) o el integrado (solo si se usa el relay integrado: no se manda a relays de terceros)
    public static string Token
    {
        get
        {
            string env = Environment.GetEnvironmentVariable("BACKROOMS_RELAY_TOKEN");
            if (!string.IsNullOrEmpty(env)) return env.Trim();
            return UsingBuiltin ? RelayDefault.Token : "";
        }
    }

    public static bool Enabled
    {
        get { string h; int p; return NetAddress.TryParse(Address, DefaultPort, out h, out p); }
    }

    public static bool TryGet(out string host, out int port) { return NetAddress.TryParse(Address, DefaultPort, out host, out port); }

    public static void Save(string address)
    {
        address = (address ?? "").Trim();
        if (address.Length == 0) PlayerPrefs.DeleteKey(KRelay); else PlayerPrefs.SetString(KRelay, address);
        PlayerPrefs.Save();
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Relay: anfitrión
//   canal de control: HOST BR-RELAY-1 <nombre>  ->  OK <CODIGO>;  el relay manda "NEW <id>" por cada cliente y "PING"; respondemos "PONG".
//   por cada NEW abrimos otra conexión:  ACCEPT <CODIGO> <id>  ->  OK  y esa conexión ya es el flujo de ese jugador.
// ---------------------------------------------------------------------------------------------------------------------
public sealed class RelayListener : INetListener
{
    public const string RelayProto = "BR-RELAY-1";
    private readonly string relayAddress, hostName, token;
    private Socket control;
    private volatile bool stopped;
    private volatile string roomCode = "", status = "", error;
    private readonly ConcurrentQueue<Socket> ready = new ConcurrentQueue<Socket>();
    private string relayHost; private int relayPort;

    public int Port { get { return 0; } }
    public string RoomCode { get { return roomCode; } }
    public string Status { get { return status; } }
    public string Error { get { return error; } }

    public RelayListener(string relayAddress, string hostName, string token = "") { this.relayAddress = relayAddress; this.hostName = hostName ?? ""; this.token = token ?? ""; }

    public bool Start(int port, out string err)
    {
        err = null;
        if (!NetAddress.TryParse(relayAddress, RelayConfig.DefaultPort, out relayHost, out relayPort))
        {
            err = "La dirección del relay no es válida. Escríbela como host:puerto.";
            return false;
        }
        status = RelayConfig.UsingBuiltin ? "Conectando con el servidor oficial ..." : "Conectando con el relay " + relayHost + ":" + relayPort + " ...";
        Thread t = new Thread(ControlLoop) { IsBackground = true, Name = "RelayControl" };
        t.Start();
        return true;
    }

    private void ControlLoop()
    {
        try
        {
            string e;
            control = NetSock.Connect(relayHost, relayPort, 6000, out e);
            if (control == null) { error = "relay_unreachable"; status = "No se pudo contactar con el relay."; return; }
            NetSock.WriteLine(control, "HOST " + RelayProto + " N=" + hostName.Replace(' ', '_') + TokenField(token));
            string reply = NetSock.ReadLine(control, 8000, () => stopped);
            if (reply.StartsWith("OK "))
            {
                roomCode = reply.Substring(3).Trim();
                status = "Sala abierta en el relay. Código: " + roomCode;
            }
            else
            {
                error = reply.StartsWith("ERR ") ? "relay:" + reply.Substring(4).Trim() : "relay_unreachable";
                status = "El relay rechazó la sala (" + reply + ").";
                return;
            }
            while (!stopped)
            {
                string line;
                if (!control.Poll(300 * 1000, SelectMode.SelectRead)) continue;
                line = NetSock.ReadLine(control, 5000, () => stopped);
                if (line == "PING") NetSock.WriteLine(control, "PONG");
                else if (line.StartsWith("NEW "))
                {
                    string id = line.Substring(4).Trim();
                    ThreadPool.QueueUserWorkItem(_ => AcceptData(id));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!stopped)
            {
                error = roomCode.Length > 0 ? "relay_lost" : "relay_unreachable";
                status = roomCode.Length > 0 ? "Se perdió la conexión con el relay: el código de sala ya no sirve para nuevos jugadores." : "No se pudo contactar con el relay.";
                Debug.Log("[Relay] canal de control cerrado (" + ex.GetType().Name + ")");
            }
        }
        finally { CloseControl(); }
    }

    private void AcceptData(string id)
    {
        Socket s = null;
        try
        {
            string e;
            s = NetSock.Connect(relayHost, relayPort, 6000, out e);
            if (s == null) return;
            NetSock.WriteLine(s, "ACCEPT " + roomCode + " " + id + TokenField(token));
            string reply = NetSock.ReadLine(s, 8000, () => stopped);
            if (reply == "OK" && !stopped) { ready.Enqueue(s); s = null; }
        }
        catch (Exception) { }
        finally { if (s != null) { try { s.Close(); } catch (Exception) { } } }
    }

    public static string TokenField(string t) { return string.IsNullOrEmpty(t) ? "" : " T=" + t; }

    public Socket Accept()
    {
        Socket s;
        return ready.TryDequeue(out s) ? s : null;
    }

    private void CloseControl()
    {
        Socket c = control; control = null;
        if (c != null) { try { c.Close(); } catch (Exception) { } }
    }

    public void Stop()
    {
        stopped = true;
        CloseControl();
        Socket s;
        while (ready.TryDequeue(out s)) { try { s.Close(); } catch (Exception) { } }
    }
}

// ---------------------------------------------------------------------------------------------------------------------
// Relay: cliente.  JOIN <CODIGO>  ->  OK  (o ERR <motivo>)
// ---------------------------------------------------------------------------------------------------------------------
public sealed class RelayConnector : INetConnector
{
    private volatile bool done;
    private volatile string error;
    private Socket result;
    private readonly string relayAddress, code, token;
    public bool Done { get { return done; } }
    public string Error { get { return error; } }
    public Socket Result { get { return result; } }

    public RelayConnector(string relayAddress, string code, int timeoutMs, string token = "")
    {
        this.relayAddress = relayAddress; this.code = code; this.token = token ?? "";
        Thread t = new Thread(Run) { IsBackground = true, Name = "RelayConnect" };
        t.Start();
    }

    private void Run()
    {
        Socket s = null;
        try
        {
            string host; int port;
            if (!NetAddress.TryParse(relayAddress, RelayConfig.DefaultPort, out host, out port)) { error = "relay_unreachable"; return; }
            if (!RoomCodes.IsValid(code)) { error = "relay:badcode"; return; }
            string e;
            s = NetSock.Connect(host, port, 6000, out e);
            if (s == null) { error = "relay_unreachable"; return; }
            NetSock.WriteLine(s, "JOIN " + code + RelayListener.TokenField(token));
            string reply = NetSock.ReadLine(s, 14000);
            if (reply == "OK") { result = s; s = null; }
            else error = reply.StartsWith("ERR ") ? "relay:" + reply.Substring(4).Trim() : "relay_unreachable";
        }
        catch (TimeoutException) { error = "relay:timeout"; }
        catch (Exception) { error = "relay_unreachable"; }
        finally
        {
            if (s != null) { try { s.Close(); } catch (Exception) { } }
            done = true;
        }
    }
}
