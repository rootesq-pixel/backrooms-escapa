using System;
using System.Collections.Generic;
using UnityEngine;

public enum NetRole { None, Host, Client }

// Mensajes (primer byte de cada paquete)
public static class NetMsg
{
    // cliente -> anfitrión
    public const byte Hello = 1, State = 2, PickupReq = 3, Chat = 4, Glow = 5, DropItem = 6, Ready = 7, Ping = 8, Bye = 9, DbgMonsters = 20;
    // anfitrión -> cliente
    public const byte Welcome = 101, Reject = 102, Lobby = 103, StartLevel = 104, Snapshot = 105, PickupTaken = 106, ItemSpawned = 107,
        Downed = 108, Revived = 109, AllDown = 110, LevelComplete = 111, ChatFrom = 112, GlowFrom = 113, PlayerLeft = 114, HostLeft = 115, Resync = 117;
}

public class NetPlayer
{
    public int id;
    public string name = "Jugador";
    public NetConn conn;                  // solo en el anfitrión (null para el propio anfitrión)
    public bool isLocal;
    public RemotePlayer avatar;
    public bool downed;
    public float revive;
    public float graceUntil;
    public Vector3 pos; public float yaw, pitch, speed; public int flags; public int epoch;
    public bool ready;
    public ulong hash;
    public float lastState;
}

public class NetStart
{
    public int epoch, level, seed, extraMonsters;
    public bool newRun, restart;
    public int[] ids;
    public int SlotOf(int playerId) { for (int i = 0; ids != null && i < ids.Length; i++) if (ids[i] == playerId) return i; return 0; }
}

// Sesión multijugador cooperativa (2-4 jugadores). El anfitrión es la autoridad (monstruos, recogidas, salida, derribos).
// Los clientes generan el mismo mundo a partir de la semilla que envía el anfitrión. Transporte: TCP propio (NetTransport).
public class NetSession : MonoBehaviour
{
    public const string Protocol = "BackroomsEscape-MP-1";
    public const int MaxPlayers = 4;
    public const int DefaultPort = 7777;
    public const float TickRate = 20f;
    public const float TimeoutSeconds = 20f;
    public const float ReviveSeconds = 4f;
    public const float ReviveRadius = 2.2f;

    public static NetSession Instance { get; private set; }
    public static bool Active { get { return Instance != null && Instance.Role != NetRole.None; } }
    public static bool IsHost { get { return Instance != null && Instance.Role == NetRole.Host; } }
    public static bool IsClient { get { return Instance != null && Instance.Role == NetRole.Client; } }
    public static bool LocalAtExit;           // lo escribe GameManager cada fotograma

    public NetRole Role { get; private set; }
    public bool InGame { get; private set; }            // la partida ya ha empezado (sala cerrada)
    public int LocalId { get; private set; }
    public string LocalName { get; private set; }
    public readonly List<NetPlayer> Players = new List<NetPlayer>();
    public string ConnectStatus = "";                    // texto mientras se conecta
    public bool Connecting { get; private set; }
    public string LastNotice = "";                       // mensaje para el menú principal (errores, desconexión)
    public string LastError = "";                        // último error de conexión (para la pantalla de unirse)
    public int Port { get; private set; }
    public int Epoch { get; private set; }               // nº de nivel en curso (lo decide el anfitrión)
    public int WorldEpoch { get; private set; }          // época del mundo construido (0 = ninguno)
    public ulong LocalWorldHash { get; private set; }
    public NetStart CurrentStart { get; private set; }
    public bool LocalDowned { get; private set; }
    public float LocalRevive01 { get; private set; }
    public int ExitCount { get; private set; }
    public int ExitTotal { get; private set; }
    public int HashMismatches { get; private set; }
    public int SnapshotsReceived { get; private set; }
    public int StatesReceived { get; private set; }
    public int ItemsTaken { get; private set; }
    public bool DebugReports;                            // pruebas: el cliente informa de la posición de sus monstruos
    public List<Vector3> DbgClientMonsters = new List<Vector3>(), DbgHostMonsters = new List<Vector3>();
    public int DbgReportCount;

    public event Action LobbyChanged;
    public event Action<string> Disconnected;            // motivo
    public event Action<string, string> ChatReceived;   // nombre, texto
    public event Action<string> SystemMessage;

    private GameBootstrap bootstrap;
    private NetListener listener;
    private readonly List<NetConn> pending = new List<NetConn>();
    private NetConn serverConn;
    private NetConnector connector;
    private float connectStart, helloSent;
    private string connectName;
    private NetStart pendingStart;
    private float tickAcc, pingAcc;
    private int nextPlayerId = 1;
    private int nextDropId = 100000;
    private readonly HashSet<int> taken = new HashSet<int>();
    private readonly Dictionary<int, int> takenBy = new Dictionary<int, int>();
    private readonly Dictionary<int, DropInfo> drops = new Dictionary<int, DropInfo>();
    private struct DropInfo { public ItemType type; public int amount; public Vector3 pos; }
    private static readonly Dictionary<int, Component> pickups = new Dictionary<int, Component>();
    private float gateUntil; private bool gateOpen = true;
    private FirstPersonController fpc;
    private Transform localPlayer;
    private BackroomsGenerator gen;
    private MonsterAI[] mons = new MonsterAI[0];
    private GameManager gm;
    private bool completeSent, allDownSent;
    private NetDownedFx downedFx;

    public Transform LocalPlayerTransform { get { return localPlayer; } }
    public int PlayerCount { get { return Players.Count; } }

    // ------------------------------------------------------------------
    public static NetSession Create(GameBootstrap b)
    {
        GameObject go = new GameObject("NetSession");
        go.transform.SetParent(b.transform, false);
        NetSession s = go.AddComponent<NetSession>();
        s.bootstrap = b;
        return s;
    }

    private void Awake() { Instance = this; }
    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Teardown();
    }
    private void OnApplicationQuit() { if (Active) Leave(null, false); }

    public NetPlayer PlayerById(int id) { foreach (NetPlayer p in Players) if (p.id == id) return p; return null; }
    public NetPlayer LocalPlayerInfo { get { return PlayerById(LocalId); } }

    // ------------------------------------------------------------------
    // Alojar / Unirse / Salir
    // ------------------------------------------------------------------
    public bool StartHost(int port, string name, out string error)
    {
        error = null;
        if (Role != NetRole.None) Leave(null, false);
        listener = new NetListener();
        if (!listener.Start(port, out error)) { listener = null; return false; }
        Port = listener.Port;
        Role = NetRole.Host;
        InGame = false;
        LocalName = CleanName(name);
        LocalId = 0;
        Players.Clear();
        Players.Add(new NetPlayer { id = 0, name = LocalName, isLocal = true });
        nextPlayerId = 1; Epoch = 0; WorldEpoch = 0;
        taken.Clear(); takenBy.Clear(); drops.Clear();
        LastNotice = ""; LastError = "";
        if (LobbyChanged != null) LobbyChanged();
        Debug.Log("[NetSession] Anfitrión escuchando en el puerto TCP " + Port);
        return true;
    }

    public void StartClient(string host, int port, string name)
    {
        if (Role != NetRole.None) Leave(null, false);
        LastError = ""; LastNotice = "";
        if (string.IsNullOrEmpty(host) || host.Trim().Length == 0) { LastError = "Escribe la dirección IP del anfitrión."; return; }
        if (port < 1 || port > 65535) { LastError = "Puerto no válido (1-65535)."; return; }
        connectName = CleanName(name);
        connector = new NetConnector(host, port, 6000);
        connectStart = Time.unscaledTime;
        Connecting = true;
        ConnectStatus = "Conectando con " + host.Trim() + ":" + port + " ...";
        Role = NetRole.Client;       // provisional: Update gestiona la fase de conexión
        InGame = false;
        Players.Clear();
        Epoch = 0; WorldEpoch = 0;
    }

    public static string CleanName(string n)
    {
        if (string.IsNullOrEmpty(n)) n = "Jugador";
        n = n.Trim();
        if (n.Length > 16) n = n.Substring(0, 16);
        if (n.Length == 0) n = "Jugador";
        return n.Replace("<", "").Replace(">", "");
    }

    // Cierra la sesión. notice: mensaje para el menú principal (null = ninguno). tellOthers: envía la despedida.
    public void Leave(string notice, bool tellOthers = true)
    {
        if (Role == NetRole.None && !Connecting) return;
        if (tellOthers)
        {
            if (Role == NetRole.Host) { Broadcast(new NetWriter(NetMsg.HostLeft).ToArray()); foreach (NetPlayer p in Players) if (p.conn != null) p.conn.CloseGracefully(); }
            else if (serverConn != null && !serverConn.Dead) { serverConn.Send(new NetWriter(NetMsg.Bye).ToArray()); serverConn.CloseGracefully(); }
        }
        Teardown();
        if (notice != null) LastNotice = notice;
        if (LobbyChanged != null) LobbyChanged();
    }

    private void Teardown()
    {
        foreach (NetPlayer p in Players) { if (p.avatar != null) Destroy(p.avatar.gameObject); if (p.conn != null) p.conn.Kill("cerrada"); }
        Players.Clear();
        foreach (NetConn c in pending) c.Kill("cerrada");
        pending.Clear();
        if (serverConn != null) serverConn.Kill("cerrada");
        serverConn = null;
        if (listener != null) listener.Stop();
        listener = null;
        connector = null;
        Connecting = false; ConnectStatus = "";
        Role = NetRole.None; InGame = false;
        Epoch = 0; WorldEpoch = 0; pendingStart = null; CurrentStart = null;
        LocalDowned = false; LocalRevive01 = 0f;
        pickups.Clear(); taken.Clear(); takenBy.Clear(); drops.Clear();
        mons = new MonsterAI[0]; gm = null; fpc = null; localPlayer = null; gen = null;
        if (downedFx != null) { Destroy(downedFx); downedFx = null; }
        LocalAtExit = false;
    }

    // ------------------------------------------------------------------
    // Bucle principal
    // ------------------------------------------------------------------
    private void Update()
    {
        if (Role == NetRole.None) return;
        float now = Time.unscaledTime;
        if (Role == NetRole.Host) HostUpdate(now); else ClientUpdate(now);
        if (Role == NetRole.None) return;

        // carga de nivel pendiente (tras recibir StartLevel)
        if (pendingStart != null && bootstrap != null && bootstrap.CanLoadNow)
        {
            NetStart ns = pendingStart; pendingStart = null;
            WorldEpoch = 0;
            bootstrap.LoadLevelNet(ns);
        }

        // ritmo de envío
        tickAcc += Time.unscaledDeltaTime;
        float interval = 1f / TickRate;
        if (tickAcc >= interval)
        {
            tickAcc = Mathf.Min(tickAcc - interval, interval);
            if (Role == NetRole.Host) HostTick(); else ClientTick();
        }
        pingAcc += Time.unscaledDeltaTime;
        if (pingAcc >= 1f)
        {
            pingAcc = 0f;
            byte[] ping = new NetWriter(NetMsg.Ping).ToArray();
            if (Role == NetRole.Host) foreach (NetPlayer p in Players) { if (p.conn != null) p.conn.Send(ping); }
            else if (serverConn != null) serverConn.Send(ping);
        }
        FlushAll();
    }

    private void FlushAll()
    {
        if (Role == NetRole.Host) foreach (NetPlayer p in Players) { if (p.conn != null) p.conn.Flush(); }
        foreach (NetConn c in pending) c.Flush();
        if (serverConn != null) serverConn.Flush();
    }

    // ------------------------------------------------------------------
    // Anfitrión
    // ------------------------------------------------------------------
    private void HostUpdate(float now)
    {
        Socket_AcceptAll(now);

        // conexiones aún sin saludo
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            NetConn c = pending[i];
            c.Pump(now);
            while (c.Incoming.Count > 0 && !c.HelloDone && !c.Dead) HandleHostMessage(null, c, c.Incoming.Dequeue());
            if (c.Dead) { pending.RemoveAt(i); continue; }
            if (c.HelloDone) { pending.RemoveAt(i); continue; }
            if (now - c.LastRecv > 6f && now - c.LastRecv > 0f && c.BytesIn == 0) { c.Kill("sin saludo"); pending.RemoveAt(i); }
        }

        // jugadores conectados
        for (int i = Players.Count - 1; i >= 0; i--)
        {
            NetPlayer p = Players[i];
            if (p.conn == null) continue;
            p.conn.Pump(now);
            int guard = 0;
            while (p.conn.Incoming.Count > 0 && !p.conn.Dead && guard++ < 400) HandleHostMessage(p, p.conn, p.conn.Incoming.Dequeue());
            if (!p.conn.Dead && now - p.conn.LastRecv > TimeoutSeconds) p.conn.Kill("tiempo de espera agotado");
            if (p.conn.Dead) HostRemovePlayer(p, p.conn.DeadReason);
        }

        if (InGame) HostGameLogic(now);
    }

    private void Socket_AcceptAll(float now)
    {
        if (listener == null) return;
        System.Net.Sockets.Socket s;
        while ((s = listener.Accept()) != null)
        {
            NetConn c = new NetConn(s, now);
            pending.Add(c);
        }
    }

    private void HandleHostMessage(NetPlayer p, NetConn c, byte[] data)
    {
        NetReader r;
        try { r = new NetReader(data); } catch (Exception) { return; }
        try
        {
            switch (r.Type)
            {
                case NetMsg.Hello:
                {
                    string proto = r.Str(); string nm = r.Str();
                    if (proto != Protocol)
                    {
                        c.Send(new NetWriter(NetMsg.Reject).Str("Versión incompatible: tu juego usa «" + proto + "» y el anfitrión «" + Protocol + "». Actualiza el juego a la misma versión.").ToArray());
                        c.CloseGracefully(); return;
                    }
                    if (InGame) { c.Send(new NetWriter(NetMsg.Reject).Str("La partida ya ha empezado. Espera a que el anfitrión vuelva a la sala.").ToArray()); c.CloseGracefully(); return; }
                    if (Players.Count >= MaxPlayers) { c.Send(new NetWriter(NetMsg.Reject).Str("La sala está llena (máximo " + MaxPlayers + " jugadores).").ToArray()); c.CloseGracefully(); return; }
                    NetPlayer np = new NetPlayer { id = nextPlayerId++, name = UniqueName(CleanName(nm)), conn = c };
                    c.HelloDone = true; c.PlayerId = np.id; c.Name = np.name;
                    Players.Add(np);
                    c.Send(new NetWriter(NetMsg.Welcome).Int(np.id).Str(np.name).Str(LocalName).ToArray());
                    BroadcastLobby();
                    if (SystemMessage != null) SystemMessage(np.name + " se ha unido a la sala");
                    break;
                }
                case NetMsg.State:
                {
                    if (p == null) break;
                    p.epoch = r.Int(); p.pos = r.Vec3(); p.yaw = r.Float(); p.pitch = r.Float(); p.speed = r.Float(); p.flags = r.Byte();
                    p.lastState = Time.unscaledTime;
                    StatesReceived++;
                    if (p.epoch == WorldEpoch && WorldEpoch != 0 && p.avatar != null) p.avatar.Push(p.pos, p.yaw, p.pitch, p.speed, p.flags);
                    break;
                }
                case NetMsg.PickupReq:
                    if (p != null) HostPickupRequest(p, r.Int());
                    break;
                case NetMsg.Chat:
                    if (p != null) HostChat(p.name, r.Str());
                    break;
                case NetMsg.Glow:
                {
                    if (p == null) break;
                    Vector3 pos = r.Vec3(), vel = r.Vec3();
                    byte[] m = new NetWriter(NetMsg.GlowFrom).Int(p.id).Vec3(pos).Vec3(vel).ToArray();
                    foreach (NetPlayer q in Players) if (q.conn != null && q != p) q.conn.Send(m);
                    SpawnGlow(pos, vel);
                    break;
                }
                case NetMsg.DropItem:
                    if (p != null) HostDrop((ItemType)r.Int(), r.Int(), r.Vec3());
                    break;
                case NetMsg.Ready:
                {
                    if (p == null) break;
                    int ep = r.Int(); ulong h = (ulong)r.Long();
                    if (ep == Epoch) { p.ready = true; p.hash = h; HostSendResync(p); EvaluateHash(p); }
                    break;
                }
                case NetMsg.Bye:
                    if (p != null) p.conn.Kill("se ha desconectado");
                    break;
                case NetMsg.DbgMonsters:
                {
                    if (p == null) break;
                    int ep = r.Int(); int n = r.Byte();
                    DbgClientMonsters.Clear();
                    for (int i = 0; i < n; i++) DbgClientMonsters.Add(r.Vec3());
                    DbgHostMonsters.Clear();
                    for (int i = 0; i < mons.Length; i++) DbgHostMonsters.Add(mons[i] != null ? mons[i].transform.position : Vector3.zero);
                    DbgReportCount++;
                    break;
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[NetSession] Mensaje no válido de cliente: " + e.Message); }
    }

    private string UniqueName(string n)
    {
        string baseName = n; int k = 2;
        while (true)
        {
            bool dup = false;
            foreach (NetPlayer q in Players) if (q.name == n) dup = true;
            if (!dup) return n;
            n = baseName + k++;
        }
    }

    private void HostRemovePlayer(NetPlayer p, string reason)
    {
        Players.Remove(p);
        if (p.avatar != null) Destroy(p.avatar.gameObject);
        Debug.Log("[NetSession] " + p.name + " desconectado: " + reason);
        Broadcast(new NetWriter(NetMsg.PlayerLeft).Int(p.id).Str(p.name).Str(reason).ToArray());
        BroadcastLobby();
        if (SystemMessage != null) SystemMessage(p.name + " ha salido de la partida (" + reason + ")");
        if (LobbyChanged != null) LobbyChanged();
    }

    private void BroadcastLobby()
    {
        NetWriter w = new NetWriter(NetMsg.Lobby);
        w.Byte(Players.Count);
        foreach (NetPlayer p in Players) w.Int(p.id).Str(p.name);
        byte[] m = w.ToArray();
        Broadcast(m);
        if (LobbyChanged != null) LobbyChanged();
    }

    private void Broadcast(byte[] m)
    {
        foreach (NetPlayer p in Players) if (p.conn != null) p.conn.Send(m);
    }

    // Empieza (o reinicia) una partida desde el nivel indicado
    public void HostStartGame(int level)
    {
        if (Role != NetRole.Host) return;
        InGame = true;
        HostLoadLevel(level, true, false);
    }

    public void HostLoadLevel(int level, bool newRun, bool restart)
    {
        if (Role != NetRole.Host) return;
        InGame = true;
        level = Mathf.Clamp(level, 0, LevelConfig.Count - 1);
        Epoch++;
        NetStart ns = new NetStart();
        ns.epoch = Epoch; ns.level = level; ns.newRun = newRun; ns.restart = restart;
        ns.seed = (Environment.TickCount * 31 + level * 7919 + Epoch * 104729) & 0x3fffffff;
        ns.extraMonsters = ExtraMonstersFor(Players.Count, level);
        ns.ids = new int[Players.Count];
        for (int i = 0; i < Players.Count; i++) ns.ids[i] = Players[i].id;
        foreach (NetPlayer p in Players) { p.ready = p.isLocal ? false : false; p.downed = false; p.revive = 0f; p.graceUntil = 0f; p.epoch = 0; }
        taken.Clear(); takenBy.Clear(); drops.Clear();
        completeSent = false; allDownSent = false; gateOpen = false; gateUntil = Time.unscaledTime + 25f;
        NetWriter w = new NetWriter(NetMsg.StartLevel);
        w.Int(ns.epoch).Int(ns.level).Int(ns.seed).Int(ns.extraMonsters).Bool(ns.newRun).Bool(ns.restart).Byte(ns.ids.Length);
        for (int i = 0; i < ns.ids.Length; i++) w.Int(ns.ids[i]);
        Broadcast(w.ToArray());
        pendingStart = ns;
        CurrentStart = ns;
    }

    // Escalado por nº de jugadores: a partir de 3 jugadores, +1 monstruo desde el nivel 3 (Poolrooms)
    public static int ExtraMonstersFor(int players, int level)
    {
        return players >= 3 && level >= 3 ? 1 : 0;
    }

    // Reinicio del nivel actual (todos derribados / el anfitrión lo pide)
    public void HostRestartLevel()
    {
        if (Role != NetRole.Host || !InGame) return;
        HostLoadLevel(CurrentStart != null ? CurrentStart.level : 0, false, true);
    }

    private void HostGameLogic(float now)
    {
        // monstruos: esperan a que todos hayan generado el mundo (o 25 s)
        if (!gateOpen && WorldEpoch == Epoch && WorldEpoch != 0)
        {
            bool all = true;
            foreach (NetPlayer p in Players) if (!p.ready) all = false;
            if (all || now > gateUntil) { gateOpen = true; foreach (MonsterAI m in mons) if (m != null) m.Resume(); }
        }
        if (WorldEpoch != Epoch || WorldEpoch == 0 || gm == null) return;
        if (gm.State != GameManager.GameState.Playing) return;

        // reanimación: un compañero en pie junto al derribado
        float dt = Time.unscaledDeltaTime;
        foreach (NetPlayer d in Players)
        {
            if (!d.downed) continue;
            Vector3 dp = PosOf(d);
            bool near = false;
            foreach (NetPlayer l in Players)
            {
                if (l == d || l.downed) continue;
                Vector3 lp = PosOf(l);
                Vector3 diff = lp - dp; diff.y = 0f;
                if (diff.magnitude <= ReviveRadius) { near = true; break; }
            }
            d.revive = near ? d.revive + dt / ReviveSeconds : Mathf.Max(0f, d.revive - dt * 0.5f);
            if (d.revive >= 1f) HostRevive(d);
        }

        // salida: todos los jugadores en pie en la puerta
        int living = 0, atExit = 0;
        foreach (NetPlayer p in Players)
        {
            if (p.downed) continue;
            living++;
            bool at = p.isLocal ? LocalAtExit : (p.flags & RemotePlayer.FlagExit) != 0 && p.epoch == WorldEpoch;
            if (at) atExit++;
        }
        ExitCount = atExit; ExitTotal = living;
        if (living > 0 && atExit == living && !completeSent)
        {
            completeSent = true;
            Broadcast(new NetWriter(NetMsg.LevelComplete).Int(Epoch).ToArray());
            ApplyLevelComplete();
        }
    }

    private Vector3 PosOf(NetPlayer p)
    {
        if (p.isLocal) return localPlayer != null ? localPlayer.position : Vector3.zero;
        return p.avatar != null ? p.avatar.transform.position : p.pos;
    }

    private void HostRevive(NetPlayer d)
    {
        d.downed = false; d.revive = 0f; d.graceUntil = Time.unscaledTime + 4f;
        Broadcast(new NetWriter(NetMsg.Revived).Int(d.id).ToArray());
        ApplyRevived(d);
    }

    private void HostTick()
    {
        if (!InGame || WorldEpoch != Epoch || WorldEpoch == 0 || gm == null) return;
        NetWriter w = new NetWriter(NetMsg.Snapshot);
        w.Int(Epoch).Byte(ExitCount).Byte(ExitTotal);
        w.Byte(Players.Count);
        foreach (NetPlayer p in Players)
        {
            Vector3 pos; float yaw, pitch, speed; int flags;
            if (p.isLocal) { CollectLocal(out pos, out yaw, out pitch, out speed, out flags); }
            else { pos = p.pos; yaw = p.yaw; pitch = p.pitch; speed = p.speed; flags = p.flags & ~RemotePlayer.FlagDowned; }
            if (p.downed) flags |= RemotePlayer.FlagDowned;
            w.Byte(p.id).Vec3(pos).Float(yaw).Float(pitch).Float(speed).Byte(flags).Byte(Mathf.RoundToInt(Mathf.Clamp01(p.revive) * 255f));
        }
        w.Byte(mons.Length);
        for (int i = 0; i < mons.Length; i++)
        {
            MonsterAI m = mons[i];
            if (m == null) { w.Byte(i).Vec3(Vector3.zero).Float(0f).Byte(0).Float(0f); continue; }
            w.Byte(i).Vec3(m.transform.position).Float(m.transform.eulerAngles.y).Byte((int)m.CurrentState).Float(m.VisSpeed);
        }
        Broadcast(w.ToArray());
    }

    // ------------------------------------------------------------------
    // Cliente
    // ------------------------------------------------------------------
    private void ClientUpdate(float now)
    {
        if (connector != null)
        {
            if (!connector.Done) { ConnectStatus = "Conectando ... (" + Mathf.CeilToInt(Mathf.Max(0f, 6f - (now - connectStart))) + " s)"; return; }
            if (connector.Result == null)
            {
                string e = connector.Error ?? "other";
                ConnectFailed(ConnectErrorText(e));
                return;
            }
            serverConn = new NetConn(connector.Result, now);
            connector = null;
            ConnectStatus = "Conectado. Entrando en la sala ...";
            serverConn.Send(new NetWriter(NetMsg.Hello).Str(Protocol).Str(connectName).ToArray());
            helloSent = now;
            LocalName = connectName;
            return;
        }
        if (serverConn == null) return;

        serverConn.Pump(now);
        int guard = 0;
        while (serverConn != null && serverConn.Incoming.Count > 0 && guard++ < 600)
        {
            HandleClientMessage(serverConn.Incoming.Dequeue());
            if (Role == NetRole.None) return;
        }
        if (serverConn == null) return;
        bool joined = Connecting == false;
        if (!serverConn.Dead && now - serverConn.LastRecv > TimeoutSeconds) serverConn.Kill("tiempo de espera agotado");
        if (!serverConn.Dead && Connecting && now - helloSent > 8f) serverConn.Kill("el anfitrión no responde");
        if (serverConn.Dead)
        {
            string reason = serverConn.DeadReason;
            if (Connecting) ConnectFailed("No se pudo entrar en la sala: " + reason + ".");
            else
            {
                string msg = reason.Contains("tiempo") ? "Se perdió la conexión con el anfitrión (tiempo de espera agotado)." : "Se perdió la conexión con el anfitrión (" + reason + ").";
                DisconnectedToMenu(msg);
            }
        }
    }

    private static string ConnectErrorText(string code)
    {
        if (code == "timeout") return "No se pudo conectar: tiempo de espera agotado. Comprueba la IP, que el anfitrión haya abierto la sala y que el puerto TCP esté abierto (router / cortafuegos / VPN).";
        if (code == "refused") return "Conexión rechazada: no hay ninguna sala abierta en esa IP y puerto (¿el anfitrión ya ha pulsado «Alojar partida»?).";
        if (code == "badaddr") return "Dirección IP no válida o no encontrada.";
        if (code == "unreachable") return "No se puede llegar a esa dirección (red inaccesible). Comprueba tu conexión o la VPN.";
        return "No se pudo conectar (" + code.Replace("other:", "") + ").";
    }

    private void ConnectFailed(string text)
    {
        LastError = text;
        Role = NetRole.None;
        Connecting = false;
        ConnectStatus = "";
        if (serverConn != null) { serverConn.Kill("cerrada"); serverConn = null; }
        connector = null;
        Players.Clear();
        if (LobbyChanged != null) LobbyChanged();
    }

    private void DisconnectedToMenu(string reason)
    {
        bool wasInGame = InGame;
        Teardown();
        LastNotice = reason;
        if (Disconnected != null) Disconnected(reason);
        if (LobbyChanged != null) LobbyChanged();
        if (bootstrap != null) { bootstrap.ReturnToMenuAfterNet(); }
    }

    private void HandleClientMessage(byte[] data)
    {
        NetReader r;
        try { r = new NetReader(data); } catch (Exception) { return; }
        try
        {
            switch (r.Type)
            {
                case NetMsg.Welcome:
                {
                    LocalId = r.Int(); LocalName = r.Str(); string hostName = r.Str();
                    Connecting = false; ConnectStatus = "";
                    Players.Clear();
                    Players.Add(new NetPlayer { id = LocalId, name = LocalName, isLocal = true });
                    Players.Add(new NetPlayer { id = 0, name = hostName });
                    if (LobbyChanged != null) LobbyChanged();
                    break;
                }
                case NetMsg.Reject:
                    ConnectFailed(r.Str());
                    break;
                case NetMsg.Lobby:
                {
                    int n = r.Byte();
                    List<NetPlayer> list = new List<NetPlayer>();
                    for (int i = 0; i < n; i++)
                    {
                        int id = r.Int(); string nm = r.Str();
                        NetPlayer ex = PlayerById(id) ?? new NetPlayer { id = id };
                        ex.name = nm; ex.isLocal = id == LocalId;
                        if (ex.avatar != null) ex.avatar.SetName(nm);
                        list.Add(ex);
                    }
                    foreach (NetPlayer old in Players) if (!list.Contains(old) && old.avatar != null) Destroy(old.avatar.gameObject);
                    Players.Clear(); Players.AddRange(list);
                    if (LobbyChanged != null) LobbyChanged();
                    break;
                }
                case NetMsg.StartLevel:
                {
                    NetStart ns = new NetStart();
                    ns.epoch = r.Int(); ns.level = r.Int(); ns.seed = r.Int(); ns.extraMonsters = r.Int(); ns.newRun = r.Bool(); ns.restart = r.Bool();
                    int n = r.Byte(); ns.ids = new int[n];
                    for (int i = 0; i < n; i++) ns.ids[i] = r.Int();
                    Epoch = ns.epoch; InGame = true; CurrentStart = ns; pendingStart = ns;
                    WorldEpoch = 0;
                    foreach (NetPlayer p in Players) { p.downed = false; p.revive = 0f; }
                    LocalDowned = false;
                    break;
                }
                case NetMsg.Snapshot: ApplySnapshot(r); break;
                case NetMsg.PickupTaken:
                {
                    int id = r.Int(), by = r.Int();
                    ApplyPickupTaken(id, by);
                    break;
                }
                case NetMsg.ItemSpawned:
                {
                    int id = r.Int(); ItemType t = (ItemType)r.Int(); int amount = r.Int(); Vector3 pos = r.Vec3();
                    ApplyItemSpawned(id, t, amount, pos);
                    break;
                }
                case NetMsg.Downed:
                {
                    int id = r.Int(); int mi = r.Int();
                    NetPlayer p = PlayerById(id);
                    if (p != null) { p.downed = true; ApplyDowned(p, mi); }
                    break;
                }
                case NetMsg.Revived:
                {
                    NetPlayer p = PlayerById(r.Int());
                    if (p != null) { p.downed = false; ApplyRevived(p); }
                    break;
                }
                case NetMsg.AllDown: ApplyAllDown(r.Int()); break;
                case NetMsg.LevelComplete:
                    if (r.Int() == Epoch) ApplyLevelComplete();
                    break;
                case NetMsg.ChatFrom:
                {
                    string nm = r.Str(), tx = r.Str();
                    if (ChatReceived != null) ChatReceived(nm, tx);
                    break;
                }
                case NetMsg.GlowFrom:
                {
                    r.Int(); Vector3 pos = r.Vec3(), vel = r.Vec3();
                    SpawnGlow(pos, vel);
                    break;
                }
                case NetMsg.PlayerLeft:
                {
                    int id = r.Int(); string nm = r.Str(); string why = r.Str();
                    NetPlayer p = PlayerById(id);
                    if (p != null) { if (p.avatar != null) Destroy(p.avatar.gameObject); Players.Remove(p); }
                    if (SystemMessage != null) SystemMessage(nm + " ha salido de la partida (" + why + ")");
                    if (LobbyChanged != null) LobbyChanged();
                    break;
                }
                case NetMsg.HostLeft:
                    DisconnectedToMenu("El anfitrión ha abandonado la partida.");
                    break;
                case NetMsg.Resync:
                {
                    int ep = r.Int();
                    if (ep != Epoch) break;
                    int n = r.Int();
                    List<int[]> tk = new List<int[]>();
                    for (int i = 0; i < n; i++) tk.Add(new[] { r.Int(), r.Int() });
                    int m = r.Int();
                    for (int i = 0; i < m; i++) { int id = r.Int(); ItemType t = (ItemType)r.Int(); int a = r.Int(); Vector3 pos = r.Vec3(); ApplyItemSpawned(id, t, a, pos); }
                    foreach (int[] e in tk) ApplyPickupTaken(e[0], -1);
                    break;
                }
            }
        }
        catch (Exception e) { Debug.LogWarning("[NetSession] Mensaje no válido del anfitrión: " + e.Message + "\n" + e.StackTrace); }
    }

    private void ClientTick()
    {
        if (serverConn == null || Connecting) return;
        if (!InGame || WorldEpoch != Epoch || WorldEpoch == 0 || gm == null) return;
        Vector3 pos; float yaw, pitch, speed; int flags;
        CollectLocal(out pos, out yaw, out pitch, out speed, out flags);
        serverConn.Send(new NetWriter(NetMsg.State).Int(WorldEpoch).Vec3(pos).Float(yaw).Float(pitch).Float(speed).Byte(flags).ToArray());
        if (DebugReports && mons.Length > 0)
        {
            NetWriter w = new NetWriter(NetMsg.DbgMonsters).Int(WorldEpoch).Byte(mons.Length);
            for (int i = 0; i < mons.Length; i++) w.Vec3(mons[i] != null ? mons[i].transform.position : Vector3.zero);
            serverConn.Send(w.ToArray());
        }
    }

    private void ApplySnapshot(NetReader r)
    {
        int ep = r.Int();
        int exitCount = r.Byte(), exitTotal = r.Byte();
        int n = r.Byte();
        bool valid = ep == WorldEpoch && WorldEpoch != 0;
        SnapshotsReceived++;
        for (int i = 0; i < n; i++)
        {
            int id = r.Byte(); Vector3 pos = r.Vec3(); float yaw = r.Float(), pitch = r.Float(), speed = r.Float(); int flags = r.Byte(); float rev = r.Byte() / 255f;
            if (!valid) continue;
            NetPlayer p = PlayerById(id);
            if (p == null) continue;
            if (id == LocalId) { LocalRevive01 = rev; continue; }
            if (p.avatar == null) continue;
            p.avatar.Push(pos, yaw, pitch, speed, flags & ~RemotePlayer.FlagDowned);
            p.avatar.SetRevive(rev);
            bool dn = (flags & RemotePlayer.FlagDowned) != 0;
            if (dn != p.avatar.Downed) p.avatar.SetDowned(dn);
            p.downed = dn;
        }
        int nm = r.Byte();
        for (int i = 0; i < nm; i++)
        {
            int idx = r.Byte(); Vector3 pos = r.Vec3(); float yaw = r.Float(); int st = r.Byte(); float sp = r.Float();
            if (valid && idx < mons.Length && mons[idx] != null) mons[idx].NetPush(pos, yaw, st, sp);
        }
        if (valid) { ExitCount = exitCount; ExitTotal = exitTotal; }
    }

    // ------------------------------------------------------------------
    // Hooks del juego (los llama GameBootstrap / GameManager / MonsterAI ...)
    // ------------------------------------------------------------------
    public static void RegisterPickup(int id, Component c) { if (Active && id >= 0) pickups[id] = c; }
    public static bool PickupExists(int id) { Component c; return pickups.TryGetValue(id, out c) && c != null; }
    public static int PickupCount { get { return pickups.Count; } }
    public static int[] PickupIds() { int[] a = new int[pickups.Count]; pickups.Keys.CopyTo(a, 0); return a; }
    public static Component PickupById(int id) { Component c; pickups.TryGetValue(id, out c); return c; }
    public static void ClearWorldRefs() { pickups.Clear(); }

    public NetPlayer Slot0Check() { return Players.Count > 0 ? Players[0] : null; }

    // El mundo del nivel actual está construido (generado, jugador y monstruos creados)
    public void OnWorldBuilt(GameManager manager, BackroomsGenerator generator, GameObject player, FirstPersonController controller, MonsterAI[] monsters, LevelConfig cfg)
    {
        gm = manager; gen = generator; fpc = controller; localPlayer = player.transform; mons = monsters ?? new MonsterAI[0];
        LocalWorldHash = generator.WorldHash();
        WorldEpoch = CurrentStart != null ? CurrentStart.epoch : Epoch;
        LocalDowned = false; LocalRevive01 = 0f; LocalAtExit = false;
        ExitCount = 0; ExitTotal = Players.Count; completeSent = false; allDownSent = false;
        if (downedFx != null) Destroy(downedFx);
        downedFx = player.AddComponent<NetDownedFx>();
        downedFx.fpc = controller;
        Transform parent = GameBootstrap.Instance != null ? GameBootstrap.Instance.WorldRoot.transform : null;
        foreach (NetPlayer p in Players)
        {
            p.downed = false; p.revive = 0f;
            if (p.avatar != null) { Destroy(p.avatar.gameObject); p.avatar = null; }
            if (p.isLocal) continue;
            p.avatar = RemotePlayer.Create(parent, p.id, p.name, cfg.step);
            p.avatar.transform.position = generator.PlayerStart;
        }
        if (Role == NetRole.Host)
        {
            LocalPlayerInfo.ready = true; LocalPlayerInfo.hash = LocalWorldHash;
            foreach (MonsterAI m in mons) if (m != null) m.Stop();   // esperan a que todos estén listos
            foreach (NetPlayer p in Players) if (!p.isLocal && p.ready) EvaluateHash(p);
        }
        else if (serverConn != null)
        {
            serverConn.Send(new NetWriter(NetMsg.Ready).Int(WorldEpoch).Long((long)LocalWorldHash).ToArray());
        }
        Debug.Log("[NetSession] Mundo listo (época " + WorldEpoch + ", nivel " + cfg.index + ", semilla " + generator.UsedSeed + ", huella " + LocalWorldHash.ToString("x16") + ")");
    }

    private void EvaluateHash(NetPlayer p)
    {
        if (WorldEpoch != Epoch || WorldEpoch == 0 || !p.ready) return;
        if (p.hash != LocalWorldHash)
        {
            HashMismatches++;
            string msg = "⚠ El mundo de " + p.name + " no coincide con el del anfitrión (huella distinta).";
            Debug.LogWarning("[NetSession] " + msg);
            if (SystemMessage != null) SystemMessage(msg);
        }
    }

    public bool AllReady()
    {
        foreach (NetPlayer p in Players) if (!p.ready) return false;
        return Players.Count > 0;
    }

    public bool HashesMatch()
    {
        foreach (NetPlayer p in Players) if (!p.isLocal && (!p.ready || p.hash != LocalWorldHash)) return false;
        return true;
    }

    public void OnWorldTeardown()
    {
        pickups.Clear();
        WorldEpoch = 0;
        foreach (NetPlayer p in Players) p.avatar = null;   // los destruye el mundo
        mons = new MonsterAI[0]; gm = null; fpc = null; localPlayer = null; gen = null;
        LocalDowned = false; LocalAtExit = false;
        downedFx = null;
    }

    public void CollectLocal(out Vector3 pos, out float yaw, out float pitch, out float speed, out int flags)
    {
        pos = Vector3.zero; yaw = 0f; pitch = 0f; speed = 0f; flags = 0;
        if (fpc == null) return;
        pos = fpc.transform.position;
        yaw = fpc.transform.eulerAngles.y;
        Transform cam = fpc.cameraPivot;
        if (cam != null) { pitch = cam.localEulerAngles.x; if (pitch > 180f) pitch -= 360f; }
        speed = fpc.FlatSpeed;
        if (fpc.IsCrouching) flags |= RemotePlayer.FlagCrouch;
        if (fpc.IsSprinting) flags |= RemotePlayer.FlagSprint;
        Flashlight fl = gm != null ? gm.flashlight : null;
        if (fl != null && fl.IsOn) flags |= RemotePlayer.FlagFlash;
        if (LocalAtExit) flags |= RemotePlayer.FlagExit;
        if (fpc.IsSwimming) flags |= RemotePlayer.FlagSwim;
    }

    // ------------------------------------------------------------------
    // Objetos
    // ------------------------------------------------------------------
    public void RequestPickup(int id)
    {
        if (Role == NetRole.Host) HostPickupRequest(LocalPlayerInfo, id);
        else if (serverConn != null) serverConn.Send(new NetWriter(NetMsg.PickupReq).Int(id).ToArray());
    }

    private void HostPickupRequest(NetPlayer p, int id)
    {
        if (p == null || taken.Contains(id) || p.downed) return;
        if (WorldEpoch != Epoch) return;
        taken.Add(id); takenBy[id] = p.id; drops.Remove(id);
        ItemsTaken++;
        Broadcast(new NetWriter(NetMsg.PickupTaken).Int(id).Int(p.id).ToArray());
        ApplyPickupTaken(id, p.id);
    }

    private void ApplyPickupTaken(int id, int by)
    {
        Component c;
        if (!pickups.TryGetValue(id, out c)) return;
        pickups.Remove(id);
        if (c == null) return;
        NetPlayer who = PlayerById(by);
        string nm = who != null ? who.name : "";
        ItemPickup ip = c as ItemPickup;
        BatteryPickup bp = c as BatteryPickup;
        bool mine = by == LocalId;
        if (ip != null) ip.NetApply(mine, by < 0, nm);
        else if (bp != null) bp.NetApply(mine, nm);
    }

    private void HostSendResync(NetPlayer p)
    {
        NetWriter w = new NetWriter(NetMsg.Resync).Int(Epoch);
        w.Int(takenBy.Count);
        foreach (KeyValuePair<int, int> kv in takenBy) w.Int(kv.Key).Int(kv.Value);
        w.Int(drops.Count);
        foreach (KeyValuePair<int, DropInfo> kv in drops) w.Int(kv.Key).Int((int)kv.Value.type).Int(kv.Value.amount).Vec3(kv.Value.pos);
        if (p.conn != null) p.conn.Send(w.ToArray());
    }

    public void DropItem(ItemType t, int amount, Vector3 pos)
    {
        if (Role == NetRole.Host) HostDrop(t, amount, pos);
        else if (serverConn != null) serverConn.Send(new NetWriter(NetMsg.DropItem).Int((int)t).Int(amount).Vec3(pos).ToArray());
    }

    private void HostDrop(ItemType t, int amount, Vector3 pos)
    {
        if (WorldEpoch != Epoch) return;
        int id = nextDropId++;
        drops[id] = new DropInfo { type = t, amount = amount, pos = pos };
        Broadcast(new NetWriter(NetMsg.ItemSpawned).Int(id).Int((int)t).Int(amount).Vec3(pos).ToArray());
        ApplyItemSpawned(id, t, amount, pos);
    }

    private void ApplyItemSpawned(int id, ItemType t, int amount, Vector3 pos)
    {
        if (pickups.ContainsKey(id)) return;
        Transform parent = GameBootstrap.Instance != null && GameBootstrap.Instance.WorldRoot != null ? GameBootstrap.Instance.WorldRoot.transform : null;
        ItemPickup p = ItemPickup.Spawn(t, pos, -1, parent, 2.5f);
        p.amount = amount;
        p.netId = id;
        RegisterPickup(id, p);
    }

    public void NotifyGlow(Vector3 pos, Vector3 vel)
    {
        if (Role == NetRole.Host)
        {
            byte[] m = new NetWriter(NetMsg.GlowFrom).Int(LocalId).Vec3(pos).Vec3(vel).ToArray();
            Broadcast(m);
        }
        else if (serverConn != null) serverConn.Send(new NetWriter(NetMsg.Glow).Vec3(pos).Vec3(vel).ToArray());
    }

    private void SpawnGlow(Vector3 pos, Vector3 vel) { Glowstick.Spawn(pos, vel, null); }

    // ------------------------------------------------------------------
    // Chat
    // ------------------------------------------------------------------
    public void SendChat(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (text.Length > 140) text = text.Substring(0, 140);
        if (Role == NetRole.Host) HostChat(LocalName, text);
        else if (serverConn != null) serverConn.Send(new NetWriter(NetMsg.Chat).Str(text).ToArray());
    }

    private void HostChat(string from, string text)
    {
        if (text.Length > 140) text = text.Substring(0, 140);
        Broadcast(new NetWriter(NetMsg.ChatFrom).Str(from).Str(text).ToArray());
        if (ChatReceived != null) ChatReceived(from, text);
    }

    // ------------------------------------------------------------------
    // Monstruos (solo anfitrión) y derribos
    // ------------------------------------------------------------------
    public int MonsterIndex(MonsterAI m) { for (int i = 0; i < mons.Length; i++) if (mons[i] == m) return i; return 0; }

    // Jugador vivo más cercano (anfitrión). Los recién reanimados no son objetivo durante unos segundos.
    public Transform PickTarget(Vector3 from, Transform current)
    {
        Transform best = null; float bd = float.MaxValue;
        float curD = float.MaxValue;
        foreach (NetPlayer p in Players)
        {
            if (p.downed || Time.unscaledTime < p.graceUntil) continue;
            Transform t = p.isLocal ? localPlayer : (p.avatar != null ? p.avatar.transform : null);
            if (t == null) continue;
            float d = (t.position - from).sqrMagnitude;
            if (t == current) curD = d;
            if (d < bd) { bd = d; best = t; }
        }
        if (current != null && best != current && curD < float.MaxValue && bd > curD * 0.5f) return current;   // histéresis
        return best;
    }

    public bool CanCatch(Transform target)
    {
        NetPlayer p = PlayerByTransform(target);
        return p != null && !p.downed && Time.unscaledTime >= p.graceUntil;
    }

    public NetPlayer PlayerByTransform(Transform t)
    {
        if (t == null) return null;
        foreach (NetPlayer p in Players)
        {
            if (p.isLocal && localPlayer != null && (t == localPlayer || t.IsChildOf(localPlayer))) return p;
            if (p.avatar != null && (t == p.avatar.transform || t.IsChildOf(p.avatar.transform))) return p;
        }
        return null;
    }

    // El monstruo alcanza a un jugador. Devuelve true si ya no queda nadie en pie (reinicio con screamer).
    public bool HostMonsterCaught(MonsterAI m, Transform target)
    {
        NetPlayer p = PlayerByTransform(target);
        if (p == null || p.downed) return false;
        p.downed = true; p.revive = 0f;
        int idx = MonsterIndex(m);
        Broadcast(new NetWriter(NetMsg.Downed).Int(p.id).Int(idx).ToArray());
        ApplyDowned(p, idx);
        bool all = true;
        foreach (NetPlayer q in Players) if (!q.downed) all = false;
        if (all && !allDownSent)
        {
            allDownSent = true;
            Broadcast(new NetWriter(NetMsg.AllDown).Int(idx).ToArray());
            ApplyAllDown(idx);
        }
        return all;
    }

    private void ApplyDowned(NetPlayer p, int monsterIndex)
    {
        if (p.isLocal)
        {
            LocalDowned = true;
            if (downedFx != null) downedFx.SetDown(true);
            if (gm != null) gm.ShowToast("¡Te han derribado! Un compañero puede reanimarte quedándose a tu lado");
            if (VhsEffect.Active != null) VhsEffect.Active.Spike(1f);
        }
        else
        {
            if (p.avatar != null) p.avatar.SetDowned(true);
            if (SystemMessage != null) SystemMessage(p.name + " ha sido derribado");
        }
    }

    private void ApplyRevived(NetPlayer p)
    {
        if (p.isLocal)
        {
            LocalDowned = false; LocalRevive01 = 0f;
            if (downedFx != null) downedFx.SetDown(false);
            if (gm != null) gm.ShowToast("¡Te han reanimado!");
        }
        else
        {
            if (p.avatar != null) p.avatar.SetDowned(false);
            if (SystemMessage != null) SystemMessage(p.name + " ha sido reanimado");
        }
    }

    private void ApplyAllDown(int monsterIndex)
    {
        if (gm == null) return;
        MonsterAI by = monsterIndex >= 0 && monsterIndex < mons.Length ? mons[monsterIndex] : (mons.Length > 0 ? mons[0] : null);
        if (downedFx != null) downedFx.SetDown(false);
        gm.PlayerCaught(by);
    }

    private void ApplyLevelComplete()
    {
        if (gm == null) return;
        if (downedFx != null) downedFx.SetDown(false);
        LocalDowned = false;
        gm.Win();
    }
}
