using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Prueba automática de multijugador con DOS procesos de Unity (anfitrión + cliente) en localhost:
//   Unity ... -executeMethod MpTest.RunHost   (proyecto A)
//   Unity ... -executeMethod MpTest.RunClient (proyecto B, copia del proyecto)
// Se coordinan con ficheros marcador en /workspace/logs/mp y con el propio estado de red. Ver mp-test.sh.
public static class MpTest
{
    public const string Dir = "/workspace/logs/mp";
    public const int TestPort = 47777;
    public static void RunHost() { Start("host"); }
    public static void RunClient() { Start("client"); }

    private static void Start(string role)
    {
        Directory.CreateDirectory(Dir);
        SessionState.SetString("mp_role", role);
        File.WriteAllText(Dir + "/" + role + "-report.txt", "");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}

public class MpWait
{
    public Func<bool> cond; public double seconds; public string what;
    public MpWait(Func<bool> c, double s, string w) { cond = c; seconds = s; what = w; }
}

[InitializeOnLoad]
public static class MpPlayRunner
{
    private static IEnumerator routine;
    private static SmokeTest.Report report;
    private static int errorLogs;
    private static double waitUntil;
    private static MpWait waiting; private static double waitDeadline;
    private static string role;

    static MpPlayRunner()
    {
        role = SessionState.GetString("mp_role", "");
        if (role != "host" && role != "client") return;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    private static void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        {
            errorLogs++;
            if (report != null) report.Info("LOG(" + type + "): " + condition);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;
        if (routine == null)
        {
            report = new SmokeTest.Report(MpTest.Dir + "/" + role + "-report.txt");
            routine = role == "host" ? RunHost() : RunClient();
        }
        double now = EditorApplication.timeSinceStartup;
        if (now < waitUntil) return;
        if (waiting != null)
        {
            bool done = false;
            try { done = waiting.cond(); } catch (Exception e) { report.Fail("Excepción esperando «" + waiting.what + "»: " + e.Message); done = true; }
            if (!done && now < waitDeadline) return;
            if (!done) report.Fail("Timeout (" + waiting.seconds + " s) esperando: " + waiting.what);
            waiting = null;
        }
        try
        {
            if (!routine.MoveNext()) { Finish(); return; }
            object cur = routine.Current;
            if (cur is float) waitUntil = now + (float)cur;
            else if (cur is MpWait) { waiting = (MpWait)cur; waitDeadline = now + waiting.seconds; }
        }
        catch (Exception e) { report.Fail("Excepción en el test: " + e); Finish(); }
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        report.Check(errorLogs == 0, "Sin errores/excepciones en consola (" + errorLogs + ")");
        SessionState.SetString("mp_role", "");
        report.Finish();
    }

    private static MpWait Wait(Func<bool> c, double s, string what) { return new MpWait(c, s, what); }
    private static void Mark(string n) { File.WriteAllText(MpTest.Dir + "/" + n, DateTime.Now.ToString("O")); }
    private static bool Marked(string n) { return File.Exists(MpTest.Dir + "/" + n); }
    private static MpWait WaitMark(string n, double s) { return Wait(() => Marked(n), s, "marcador " + n); }

    private static bool WorldReady(GameBootstrap gb, NetSession ns, int level)
    {
        return gb.IsLoaded && !gb.InMenu && gb.CurrentLevel == level && ns.WorldEpoch != 0 && ns.WorldEpoch == ns.Epoch && GameManager.Instance != null;
    }

    private static bool RelayMode { get { return Environment.GetEnvironmentVariable("MP_MODE") == "relay"; } }
    private const string CodeFile = "relay_code.txt";

    private static void GoToMultiMenu(UIManager ui, SmokeTest.Report r)
    {
        r.Check(ui.ClickButton("Multijugador") && ui.Current == UIManager.Screen.Multi, "el menú principal abre «Multijugador»");
    }

    // ------------------------------------------------------------------
    // ANFITRIÓN
    // ------------------------------------------------------------------
    private static IEnumerator RunHost()
    {
        SmokeTest.Report r = report;
        PlayerPrefs.DeleteKey(MultiplayerUI.KName);
        GameSettings.ResetDefaults();
        yield return Wait(() => GameBootstrap.Instance != null && GameBootstrap.Instance.IsLoaded, 90, "menú principal");
        GameBootstrap gb = GameBootstrap.Instance;
        UIManager ui = UIManager.Instance;
        NetSession ns = NetSession.Instance;
        r.Check(ns != null && !NetSession.Active, "NetSession existe y está inactiva en el menú");
        r.Check(ui.Multiplayer != null, "MultiplayerUI existe");

        // ---- menú: Multijugador -> Alojar partida -> Crear sala ----
        GoToMultiMenu(ui, r);
        yield return 0.2f;
        ui.Multiplayer.NameField.text = "Anfitrion";
        if (RelayMode)
        {
            r.Check(ui.Multiplayer.RelayHostButton.interactable, "modo relay: «Alojar con código» activo (relay configurado por entorno: " + RelayConfig.Address + ")");
            r.Check(ui.ClickButton("Alojar con código") && ui.Current == UIManager.Screen.Lobby, "«Alojar con código» abre la sala");
            yield return Wait(() => ns.RoomCode.Length > 0 || ns.TransportError != null, 30, "código de sala del relay");
            r.Check(NetSession.IsHost && ns.ViaRelay && RoomCodes.IsValid(ns.RoomCode), "anfitrión con código de sala del relay (" + ns.RoomCode + ")");
            File.WriteAllText(MpTest.Dir + "/" + CodeFile, ns.RoomCode);
        }
        else
        {
            r.Check(ui.ClickButton("Alojar partida") && ui.Multiplayer.CurrentPage == MultiplayerUI.Page.Host, "«Alojar partida» abre la página del anfitrión");
            r.Check(ui.Multiplayer.HostPortField.text == "7777" || PlayerPrefs.HasKey(MultiplayerUI.KPort), "puerto por defecto 7777");
            ui.Multiplayer.HostPortField.text = MpTest.TestPort.ToString();
            r.Check(ui.ClickButton("Crear sala") && ui.Current == UIManager.Screen.Lobby, "«Crear sala» abre la sala");
            r.Check(NetSession.IsHost && ns.Port == MpTest.TestPort, "anfitrión escuchando en el puerto " + MpTest.TestPort);
            r.Check(ns.Upnp != null, "se intenta la apertura automática de puerto (UPnP) al alojar (" + (ns.Upnp != null ? ns.Upnp.StatusText : "-") + ")");
        }
        r.Check(PlayerPrefs.GetString(MultiplayerUI.KName, "") == "Anfitrion", "el nombre se guarda en PlayerPrefs");
        Mark("host_lobby");

        yield return Wait(() => ns.PlayerCount >= 2, 120, "que se una el cliente");
        yield return 0.5f;
        r.Check(ns.PlayerCount == 2, "la sala tiene 2 jugadores");
        r.Check(ui.Multiplayer.LobbyText.Contains("Cliente") && ui.Multiplayer.LobbyText.Contains("Anfitrion"), "la sala lista los nombres de los jugadores");
        r.Check(ui.Multiplayer.StartButton.gameObject.activeInHierarchy, "«Empezar» visible para el anfitrión");

        // ---- empezar partida (nivel 0) ----
        yield return Wait(() => Marked("client_in_lobby"), 60, "cliente en la sala");
        r.Check(ui.ClickButton("Empezar"), "pulsar «Empezar»");
        yield return Wait(() => WorldReady(gb, ns, 0), 180, "mundo del nivel 0 construido (anfitrión)");
        r.Check(gb.CurrentLevel == 0 && ns.InGame, "el anfitrión carga el nivel 0");
        yield return Wait(() => ns.AllReady(), 180, "que todos los jugadores hayan generado el mundo");
        GameManager gm = GameManager.Instance;
        BackroomsGenerator gen = gb.Generator;
        r.Info("semilla " + gen.UsedSeed + " huella anfitrión " + ns.LocalWorldHash.ToString("x16"));
        r.Check(ns.HashesMatch(), "misma semilla -> misma huella del mundo en anfitrión y cliente");
        r.Check(ns.HashMismatches == 0, "ninguna desincronización del mundo detectada");
        NetPlayer remote = ns.Players.Find(p => !p.isLocal);
        r.Check(remote != null && remote.avatar != null, "avatar remoto del cliente creado en el anfitrión");
        r.Check(remote.avatar.ModelLoaded && remote.avatar.animator != null, "el avatar remoto usa el modelo hazmat con Animator");
        r.Check(remote.avatar.GetComponentInChildren<Light>(true) != null, "el avatar remoto tiene linterna (luz)");
        r.Check(UIManager.InputBlocked == false, "en partida el input no está bloqueado");
        r.Check(ui.Multiplayer.HudVisible, "HUD multijugador visible");
        Mark("host_world0");
        GameObject oldAvatar = remote.avatar.gameObject;

        // ---- replicación de posición: el cliente va a BatterySpots[1] ----
        Vector3 target1 = gen.BatterySpots[1];
        yield return Wait(() => { Vector3 d = remote.avatar.LatestPos - target1; d.y = 0; return d.magnitude < 0.6f; }, 60, "posición del cliente replicada (al punto de la pila 1)");
        {
            Vector3 d = remote.avatar.LatestPos - target1; d.y = 0;
            r.Check(d.magnitude < 0.6f, "posición del cliente replicada en el anfitrión (error " + d.magnitude.ToString("0.00") + " m)");
        }
        yield return 1.0f;
        {
            Vector3 d = remote.avatar.transform.position - target1; d.y = 0;
            r.Check(d.magnitude < 0.8f, "el avatar interpolado llega a la posición del cliente (" + d.magnitude.ToString("0.00") + " m)");
        }
        r.Check(ui.Multiplayer.HudTeamText.Contains("Cliente"), "el HUD lista a los jugadores");

        // ---- recogida sincronizada: el cliente cogió la pila 1 ----
        int batBefore = Inventory.Instance.GetCount(ItemType.Battery);
        yield return Wait(() => !NetSession.PickupExists(1), 30, "pila 1 retirada en el anfitrión (la cogió el cliente)");
        r.Check(!NetSession.PickupExists(1), "la pila recogida por el cliente desaparece también en el anfitrión");
        r.Check(Inventory.Instance.GetCount(ItemType.Battery) == batBefore, "la pila del cliente NO entra en el inventario del anfitrión");
        Mark("host_saw_pick1");
        // el anfitrión coge la pila 0
        Vector3 target0 = gen.BatterySpots[0];
        GameManager.Instance.player.Teleport(new Vector3(target0.x, target0.y + 0.1f, target0.z), 0f);
        yield return Wait(() => !NetSession.PickupExists(0), 30, "pila 0 retirada (la coge el anfitrión)");
        r.Check(Inventory.Instance.GetCount(ItemType.Battery) == batBefore + 1, "la pila que coge el anfitrión entra en SU inventario");
        Mark("host_picked0");
        r.Check(ns.ItemsTaken >= 2, "el anfitrión contabiliza 2 objetos recogidos");

        // ---- chat ----
        yield return Wait(() => ui.Multiplayer.LastChatLine.Contains("hola desde el cliente"), 40, "mensaje de chat del cliente");
        r.Check(ui.Multiplayer.LastChatLine.Contains("Cliente"), "el chat del cliente llega al anfitrión con su nombre");
        ns.SendChat("hola cliente");
        r.Check(ui.Multiplayer.LastChatLine.Contains("hola cliente"), "el anfitrión ve su propio mensaje");
        // ---- barra luminosa lanzada por el anfitrión: aparece en el cliente ----
        int g0 = Glowstick.Active.Count;
        Inventory.Instance.Add(ItemType.Glowstick, 1);
        int slotG = -1;
        for (int i = 0; i < Inventory.SlotCount; i++) if (Inventory.Instance.GetSlot(i).type == ItemType.Glowstick) slotG = i;
        r.Check(slotG >= 0 && Inventory.Instance.UseSlot(slotG), "el anfitrión lanza una barra luminosa");
        r.Check(Glowstick.Active.Count == g0 + 1, "la barra luminosa existe en el anfitrión");
        Mark("host_glow");
        // ---- objeto soltado por el cliente: el anfitrión lo ve ----
        yield return WaitMark("client_dropped", 40);
        bool dropNet = false;
        yield return Wait(() => { foreach (int id in NetSession.PickupIds()) if (id >= 100000) return true; return false; }, 20, "objeto soltado por el cliente");
        foreach (int id in NetSession.PickupIds()) if (id >= 100000) dropNet = true;
        r.Check(dropNet, "el objeto que suelta el cliente aparece también en el anfitrión");
        // pausa del anfitrión: no congela
        ui.PressEscape();
        yield return 0.3f;
        r.Check(ui.Current == UIManager.Screen.Pause && Time.timeScale == 1f, "pausa del anfitrión: el mundo sigue (Time.timeScale = 1)");
        ui.PressEscape();
        yield return 0.2f;

        // ---- acuerdo de monstruos ----
        yield return Wait(() => ns.DbgReportCount >= 6, 60, "informes de monstruos del cliente");
        r.Check(ns.DbgClientMonsters.Count == ns.DbgHostMonsters.Count && ns.DbgHostMonsters.Count >= 1, "cliente y anfitrión tienen el mismo número de monstruos (" + ns.DbgHostMonsters.Count + ")");
        float worst = 0f;
        for (int i = 0; i < ns.DbgHostMonsters.Count && i < ns.DbgClientMonsters.Count; i++) worst = Mathf.Max(worst, (ns.DbgHostMonsters[i] - ns.DbgClientMonsters[i]).magnitude);
        r.Check(worst < 3.0f, "posición de los monstruos acuerda entre anfitrión y cliente (máx. diferencia " + worst.ToString("0.00") + " m)");
        r.Check(MonsterAI.Instances.Count >= 1 && !MonsterAI.Instances[0].netProxy, "en el anfitrión los monstruos llevan la IA real");
        // los monstruos se mueven de verdad y el cliente lo ve
        Vector3 m0 = ns.DbgHostMonsters[0];
        yield return 3.0f;
        yield return Wait(() => ns.DbgReportCount >= 10, 30, "más informes");
        Mark("host_monsters_ok");
        r.Info("monstruo 0: " + m0 + " -> " + ns.DbgHostMonsters[0]);

        // ---- derribo y reanimación ----
        MonsterAI mon = MonsterAI.Instances[0];
        bool all = ns.HostMonsterCaught(mon, remote.avatar.transform);
        r.Check(!all, "derribar a un jugador no reinicia (queda otro en pie)");
        r.Check(remote.downed, "el anfitrión marca al cliente como derribado");
        yield return Wait(() => Marked("client_downed_seen"), 30, "el cliente ve que está derribado");
        yield return 0.4f;
        r.Check(remote.avatar.Downed, "el avatar del derribado se tumba");
        // el anfitrión se queda a su lado ~4 s y lo reanima
        Vector3 ap = remote.avatar.transform.position;
        GameManager.Instance.player.Teleport(new Vector3(ap.x + 0.9f, ap.y + 0.05f, ap.z), 0f);
        yield return 1.5f;
        r.Check(remote.revive > 0.1f && remote.downed, "la reanimación progresa mientras el compañero está al lado (" + remote.revive.ToString("0.00") + ")");
        yield return Wait(() => !remote.downed, 20, "reanimación completada");
        r.Check(!remote.downed, "el compañero reanima al derribado tras ~4 s");
        r.Check(!remote.avatar.Downed, "el avatar vuelve a levantarse");
        Mark("host_revived");
        yield return Wait(() => Marked("client_revived_seen"), 30, "el cliente confirma la reanimación");

        // ---- todos derribados: screamer para todos y reinicio del nivel ----
        int epochA = ns.Epoch;
        ns.HostMonsterCaught(mon, remote.avatar.transform);
        bool allDown = ns.HostMonsterCaught(mon, ns.LocalPlayerTransform);
        r.Check(allDown, "si caen todos, se declara el reinicio");
        r.Check(GameManager.Instance.State == GameManager.GameState.Lost, "todos derribados: estado «Perdido» en el anfitrión");
        yield return 0.5f;
        r.Check(Screamer.Active != null, "screamer en el anfitrión al caer todos");
        Mark("host_alldown");
        yield return Wait(() => WorldReady(gb, ns, 0) && ns.Epoch > epochA, 240, "reinicio del nivel tras el screamer");
        r.Check(ns.Epoch > epochA && gb.Deaths >= 1, "el nivel se reinicia tras caer todos (época " + ns.Epoch + ", muertes " + gb.Deaths + ")");
        yield return Wait(() => ns.AllReady(), 180, "todos listos tras el reinicio");
        r.Check(ns.HashesMatch() && ns.HashMismatches == 0, "tras el reinicio el mundo vuelve a coincidir");
        r.Check(Inventory.Instance.GetCount(ItemType.Battery) == batBefore, "al reiniciar el inventario vuelve al del inicio del nivel");
        gm = GameManager.Instance; gen = gb.Generator;
        remote = ns.Players.Find(p => !p.isLocal);
        r.Check(remote != null && remote.avatar != null && !remote.downed && !ns.LocalDowned, "tras el reinicio todos están en pie y con avatar");
        Mark("host_restarted");
        yield return Wait(() => Marked("client_restarted"), 120, "el cliente confirma el reinicio");

        // ---- salida: ambos van a la puerta -> siguiente nivel ----
        int epoch0 = ns.Epoch;
        Vector3 ex = gen.ExitPosition;
        GameManager.Instance.player.Teleport(new Vector3(ex.x, gen.PlayerStart.y, ex.z), 0f);
        yield return Wait(() => ns.ExitCount >= 1, 20, "contador de salida");
        r.Info("jugadores en la salida: " + ns.ExitCount + "/" + ns.ExitTotal);
        yield return Wait(() => WorldReady(gb, ns, 1) && ns.Epoch > epoch0, 240, "nivel 1 cargado tras llegar a la salida todos");
        r.Check(gb.CurrentLevel == 1 && ns.Epoch > epoch0, "todos los jugadores en la salida -> se carga el nivel siguiente (época " + ns.Epoch + ")");
        yield return Wait(() => ns.AllReady(), 180, "todos listos en el nivel 1");
        r.Check(ns.HashesMatch() && ns.HashMismatches == 0, "el nivel 1 también coincide (misma huella)");
        NetPlayer remote1 = ns.Players.Find(p => !p.isLocal);
        r.Check(remote1 != null && remote1.avatar != null && remote1.avatar != null && (oldAvatar == null), "el avatar remoto se recrea en el nuevo nivel");
        r.Check(!ns.LocalDowned && remote1 != null && !remote1.downed, "al cambiar de nivel todos están en pie");
        Mark("host_level1");

        // ---- desconexión del cliente ----
        GameObject av1 = remote1.avatar.gameObject;
        yield return WaitMark("client_left", 120);
        yield return Wait(() => ns.PlayerCount == 1, 20, "que el anfitrión detecte la salida del cliente");
        r.Check(ns.PlayerCount == 1, "el anfitrión detecta que el cliente se ha ido");
        yield return 0.3f;
        r.Check(av1 == null, "el avatar del cliente desconectado se elimina");
        yield return WaitMark("client_rejected", 120);

        // ---- anfitrión vuelve al menú y reabre sala; el cliente se une y luego el anfitrión se va ----
        ui.OnMainMenu();
        yield return Wait(() => gb.InMenu && gb.IsLoaded && ui.Current == UIManager.Screen.Main, 60, "volver al menú principal");
        r.Check(!NetSession.Active, "salir al menú cierra la sesión de red");
        GoToMultiMenu(ui, r);
        ui.Multiplayer.NameField.text = "Anfitrion";
        if (RelayMode)
        {
            ui.ClickButton("Alojar con código");
            yield return Wait(() => ns.RoomCode.Length > 0 || ns.TransportError != null, 30, "código de sala nuevo");
            File.WriteAllText(MpTest.Dir + "/" + CodeFile, ns.RoomCode);
        }
        else
        {
            ui.ClickButton("Alojar partida");
            ui.Multiplayer.HostPortField.text = MpTest.TestPort.ToString();
            ui.ClickButton("Crear sala");
        }
        r.Check(NetSession.IsHost && ui.Current == UIManager.Screen.Lobby, "se puede alojar de nuevo tras salir");
        Mark("host_lobby2");
        yield return Wait(() => ns.PlayerCount >= 2, 120, "el cliente vuelve a unirse");
        r.Check(ns.PlayerCount == 2, "el cliente se une de nuevo a la sala nueva");
        yield return 1.0f;
        ui.OnMainMenu();        // el anfitrión abandona con el cliente conectado
        Mark("host_left");
        yield return Wait(() => gb.InMenu && gb.IsLoaded, 60, "menú tras abandonar");
        yield return WaitMark("client_done", 120);
        r.Check(Marked("client_done"), "el cliente terminó su parte");
    }

    // ------------------------------------------------------------------
    // CLIENTE
    // ------------------------------------------------------------------
    private static IEnumerator RunClient()
    {
        SmokeTest.Report r = report;
        PlayerPrefs.DeleteKey(MultiplayerUI.KName);
        GameSettings.ResetDefaults();
        yield return Wait(() => GameBootstrap.Instance != null && GameBootstrap.Instance.IsLoaded, 90, "menú principal");
        GameBootstrap gb = GameBootstrap.Instance;
        UIManager ui = UIManager.Instance;
        NetSession ns = NetSession.Instance;

        GoToMultiMenu(ui, r);
        ui.Multiplayer.NameField.text = "Cliente";
        if (RelayMode)
        {
            // ---- errores del relay: código inexistente ----
            r.Check(ui.ClickButton("Unirse con código") && ui.Multiplayer.CurrentPage == MultiplayerUI.Page.JoinCode, "«Unirse con código» abre su página");
            ui.Multiplayer.CodeField.text = "ZZZZZZ";
            ui.ClickButton("Conectar con código");
            yield return Wait(() => !ns.Connecting && !string.IsNullOrEmpty(ns.LastError), 30, "error de código inexistente");
            r.Check(ns.LastError.Contains("No existe ninguna sala"), "error claro con código inexistente: «" + ns.LastError + "»");
        }
        else
        {
        // ---- errores de conexión: puerto sin sala ----
        ui.ClickButton("Unirse");
        r.Check(ui.Multiplayer.CurrentPage == MultiplayerUI.Page.Join, "«Unirse» abre la página de unirse");
        ui.Multiplayer.JoinIpField.text = "127.0.0.1";
        ui.Multiplayer.JoinPortField.text = "47999";
        ui.ClickButton("Conectar");
        yield return Wait(() => !ns.Connecting && !string.IsNullOrEmpty(ns.LastError), 30, "error de conexión rechazada");
        r.Check(ns.LastError.Contains("rechazada"), "error claro si no hay sala: «" + ns.LastError + "»");
        yield return 0.3f;
        r.Check(ui.Multiplayer.CurrentPage == MultiplayerUI.Page.Join && !NetSession.Active, "tras el error se sigue en la página de unirse");
        ui.Multiplayer.JoinIpField.text = "no es una ip!!";
        ui.ClickButton("Conectar");
        yield return Wait(() => !ns.Connecting && !string.IsNullOrEmpty(ns.LastError), 30, "error de IP no válida");
        r.Check(ns.LastError.Contains("no válida") || ns.LastError.Contains("no encontrada"), "error claro con IP inválida: «" + ns.LastError + "»");

        }

        // ---- unirse a la sala del anfitrión ----
        yield return WaitMark("host_lobby", 180);
        if (RelayMode)
        {
            ui.Multiplayer.CodeField.text = File.ReadAllText(MpTest.Dir + "/" + CodeFile).Trim().ToLowerInvariant();
            ui.ClickButton("Conectar con código");
        }
        else
        {
            ui.Multiplayer.JoinIpField.text = "127.0.0.1:" + MpTest.TestPort;     // «ip:puerto» pegado
            r.Check(ui.Multiplayer.JoinIpField.text == "127.0.0.1" && ui.Multiplayer.JoinPortField.text == MpTest.TestPort.ToString(), "al pegar «ip:puerto» se separan los campos");
            ui.ClickButton("Conectar");
        }
        yield return Wait(() => ui.Current == UIManager.Screen.Lobby && ns.PlayerCount >= 2, 40, "entrar en la sala");
        r.Check(ui.Current == UIManager.Screen.Lobby && NetSession.IsClient, "el cliente entra en la sala del anfitrión");
        yield return 0.5f;
        r.Check(ui.Multiplayer.LobbyText.Contains("Anfitrion") && ui.Multiplayer.LobbyText.Contains("Cliente"), "el cliente ve la lista de jugadores");
        r.Check(!ui.Multiplayer.StartButton.gameObject.activeInHierarchy, "«Empezar» solo para el anfitrión");
        if (!RelayMode) r.Check(PlayerPrefs.GetString(MultiplayerUI.KIp, "") == "127.0.0.1", "la IP usada se recuerda en PlayerPrefs");
        Mark("client_in_lobby");

        // ---- partida ----
        yield return Wait(() => WorldReady(gb, ns, 0), 240, "mundo del nivel 0 construido (cliente)");
        GameManager gm = GameManager.Instance;
        BackroomsGenerator gen = gb.Generator;
        r.Check(ns.CurrentStart != null && ns.CurrentStart.level == 0 && gen.UsedSeed == ns.CurrentStart.seed, "el cliente genera el mundo con la semilla del anfitrión (" + gen.UsedSeed + ")");
        r.Check(gen.ExitTransform != null && gen.BatterySpots.Count >= 2, "mundo del cliente completo (salida, pilas)");
        r.Check(UnityEngine.Object.FindObjectsOfType<MonsterAI>().Length >= 1, "el cliente tiene los monstruos");
        bool allProxy = true;
        foreach (MonsterAI m in UnityEngine.Object.FindObjectsOfType<MonsterAI>()) if (!m.netProxy) allProxy = false;
        r.Check(allProxy, "en el cliente los monstruos son proxies sin IA (los mueve el anfitrión)");
        r.Check(UnityEngine.Object.FindObjectsOfType<UnityEngine.AI.NavMeshAgent>().Length == 0, "el cliente no crea NavMeshAgent");
        NetPlayer hostP = ns.Players.Find(p => !p.isLocal);
        r.Check(hostP != null && hostP.avatar != null && hostP.avatar.ModelLoaded, "avatar del anfitrión creado en el cliente");
        r.Check(Time.timeScale == 1f && !UIManager.InputBlocked, "mundo en marcha");
        Mark("client_world0");
        yield return WaitMark("host_world0", 240);
        int pickupsBefore = NetSession.PickupCount;
        r.Check(pickupsBefore >= 5 && NetSession.PickupExists(0) && NetSession.PickupExists(1), "pilas y objetos registrados con identificadores comunes (" + pickupsBefore + ")");
        yield return Wait(() => ns.SnapshotsReceived > 10, 30, "instantáneas del anfitrión");
        r.Check(ns.SnapshotsReceived > 10, "el cliente recibe instantáneas del anfitrión (" + ns.SnapshotsReceived + ")");

        // ---- el cliente va a la pila 1 y la coge ----
        int bat0 = Inventory.Instance.GetCount(ItemType.Battery);
        Vector3 t1 = gen.BatterySpots[1];
        gm.player.Teleport(new Vector3(t1.x, t1.y + 0.1f, t1.z), 0f);
        yield return Wait(() => !NetSession.PickupExists(1), 30, "pila 1 retirada");
        yield return 0.3f;
        r.Check(Inventory.Instance.GetCount(ItemType.Battery) == bat0 + 1, "el cliente recibe la pila en su propio inventario");
        // el anfitrión coge la 0: debe desaparecer también aquí
        yield return Wait(() => !NetSession.PickupExists(0), 40, "pila 0 retirada (la coge el anfitrión)");
        r.Check(!NetSession.PickupExists(0), "la pila recogida por el anfitrión desaparece en el cliente");
        r.Check(Inventory.Instance.GetCount(ItemType.Battery) == bat0 + 1, "la pila del anfitrión no entra en el inventario del cliente");
        yield return Wait(() => { Vector3 d0 = hostP.avatar.LatestPos - gen.BatterySpots[0]; d0.y = 0; return d0.magnitude < 0.8f; }, 20, "posición del anfitrión replicada en el cliente");
        {
            Vector3 d = hostP.avatar.LatestPos - gen.BatterySpots[0]; d.y = 0;
            r.Check(d.magnitude < 0.8f, "posición del anfitrión replicada en el cliente (" + d.magnitude.ToString("0.00") + " m)");
        }

        // ---- chat, barras luminosas, objetos soltados, pausa sin congelar ----
        int g0 = Glowstick.Active.Count;
        ns.SendChat("hola desde el cliente");
        yield return Wait(() => ui.Multiplayer.LastChatLine.Contains("hola cliente"), 30, "mensaje de chat del anfitrión");
        r.Check(ui.Multiplayer.LastChatLine.Contains("Anfitrion"), "el chat llega al cliente con el nombre del emisor");
        yield return WaitMark("host_glow", 30);
        yield return Wait(() => Glowstick.Active.Count > g0, 20, "barra luminosa lanzada por el anfitrión");
        r.Check(Glowstick.Active.Count == g0 + 1, "la barra luminosa lanzada por el anfitrión aparece en el cliente");
        Inventory.Instance.Add(ItemType.AlmondWater, 1);
        int slotW = -1;
        for (int i = 0; i < Inventory.SlotCount; i++) if (Inventory.Instance.GetSlot(i).type == ItemType.AlmondWater) slotW = i;
        int idsBefore = NetSession.PickupIds().Length;
        r.Check(slotW >= 0 && Inventory.Instance.DropSlot(slotW), "el cliente suelta un objeto");
        yield return Wait(() => NetSession.PickupIds().Length > idsBefore, 20, "objeto soltado registrado");
        bool dropNet = false;
        foreach (int id in NetSession.PickupIds()) if (id >= 100000) dropNet = true;
        r.Check(dropNet, "el objeto soltado recibe un identificador de red del anfitrión");
        Mark("client_dropped");
        ui.PressEscape();
        yield return 0.3f;
        r.Check(ui.Current == UIManager.Screen.Pause && Time.timeScale == 1f && !AudioListener.pause, "pausa en multijugador: el mundo NO se congela");
        r.Check(ui.Multiplayer.HudVisible, "el HUD multijugador sigue visible en pausa");
        ui.PressEscape();
        ui.ToggleInventory();
        yield return 0.3f;
        r.Check(ui.Current == UIManager.Screen.Inventory && Time.timeScale == 1f, "inventario en multijugador: el mundo NO se congela");
        ui.ToggleInventory();
        yield return 0.6f;
        r.Check(ui.Current == UIManager.Screen.None, "se cierra el inventario");

        // ---- monstruos ----
        ns.DebugReports = true;
        yield return Wait(() => Marked("host_monsters_ok"), 90, "comprobación de monstruos del anfitrión");
        ns.DebugReports = false;
        bool anySamples = false;
        foreach (MonsterAI m in UnityEngine.Object.FindObjectsOfType<MonsterAI>()) if (m.NetSamples > 5) anySamples = true;
        r.Check(anySamples, "los monstruos del cliente reciben muestras de posición");

        // ---- derribado y reanimado ----
        yield return Wait(() => ns.LocalDowned, 40, "ser derribado");
        r.Check(ns.LocalDowned && !gm.player.InputEnabled, "derribado: no puede moverse");
        yield return 0.8f;
        r.Check(gm.playerCamera.localPosition.y < 1.0f, "derribado: la cámara cae al suelo (" + gm.playerCamera.localPosition.y.ToString("0.00") + ")");
        r.Check(ui.Multiplayer.HudDownedText.Contains("DERRIBADO"), "aviso «ESTÁS DERRIBADO» en el HUD");
        Mark("client_downed_seen");
        yield return Wait(() => !ns.LocalDowned, 40, "ser reanimado");
        r.Check(!ns.LocalDowned && gm.player.InputEnabled, "reanimado: vuelve a moverse");
        yield return 0.8f;
        r.Check(gm.playerCamera.localPosition.y > 1.2f, "reanimado: la cámara vuelve a la altura de los ojos (" + gm.playerCamera.localPosition.y.ToString("0.00") + ")");
        Mark("client_revived_seen");

        // ---- salida ----
        yield return WaitMark("host_revived", 60);

        // ---- todos derribados -> screamer + reinicio ----
        int epochA = ns.Epoch;
        yield return WaitMark("host_alldown", 60);
        yield return Wait(() => gm != null && gm.State == GameManager.GameState.Lost, 15, "estado «Perdido» en el cliente");
        r.Check(gm.State == GameManager.GameState.Lost, "todos derribados: el cliente también pierde");
        yield return 0.5f;
        r.Check(Screamer.Active != null, "el screamer también se lanza en el cliente");
        yield return Wait(() => WorldReady(gb, ns, 0) && ns.Epoch > epochA, 240, "reinicio del nivel (cliente)");
        r.Check(ns.Epoch > epochA && gb.Deaths >= 1, "el cliente reinicia el nivel con el anfitrión");
        r.Check(Inventory.Instance.GetCount(ItemType.Battery) == bat0, "al reiniciar el inventario del cliente vuelve al del inicio del nivel");
        gm = GameManager.Instance; gen = gb.Generator;
        Mark("client_restarted");
        yield return WaitMark("host_restarted", 120);
        Vector3 ex = gen.ExitPosition;
        int epoch0 = ns.Epoch;
        gm.player.Teleport(new Vector3(ex.x, gen.PlayerStart.y, ex.z), 0f);
        yield return Wait(() => WorldReady(gb, ns, 1) && ns.Epoch > epoch0, 240, "nivel 1 cargado (cliente)");
        r.Check(gb.CurrentLevel == 1 && ns.CurrentStart.level == 1, "el cliente carga el nivel siguiente decidido por el anfitrión");
        r.Check(NetSession.PickupCount >= 5, "pickups del nivel 1 registrados");
        yield return WaitMark("host_level1", 240);

        // ---- salir, reintentar con la partida en marcha, reunirse y ver el cierre del anfitrión ----
        ui.OnMainMenu();
        r.Check(!NetSession.Active, "el cliente cierra la sesión al volver al menú");
        Mark("client_left");
        yield return Wait(() => gb.InMenu && gb.IsLoaded && ui.Current == UIManager.Screen.Main, 60, "menú principal (cliente)");
        GoToMultiMenu(ui, r);
        if (RelayMode)
        {
            ui.ClickButton("Unirse con código");
            ui.Multiplayer.CodeField.text = File.ReadAllText(MpTest.Dir + "/" + CodeFile).Trim();
            ui.ClickButton("Conectar con código");
        }
        else
        {
            ui.ClickButton("Unirse");
            ui.Multiplayer.JoinIpField.text = "127.0.0.1";
            ui.Multiplayer.JoinPortField.text = MpTest.TestPort.ToString();
            ui.ClickButton("Conectar");
        }
        yield return Wait(() => !ns.Connecting && !string.IsNullOrEmpty(ns.LastError), 30, "rechazo con la partida empezada");
        r.Check(ns.LastError.Contains("ya ha empezado"), "mensaje claro si la partida ya empezó: «" + ns.LastError + "»");
        Mark("client_rejected");
        yield return WaitMark("host_lobby2", 120);
        if (RelayMode)
        {
            ui.Multiplayer.CodeField.text = File.ReadAllText(MpTest.Dir + "/" + CodeFile).Trim();
            ui.ClickButton("Conectar con código");
        }
        else
        {
            ui.Multiplayer.JoinIpField.text = "127.0.0.1";
            ui.ClickButton("Conectar");
        }
        yield return Wait(() => ui.Current == UIManager.Screen.Lobby && ns.PlayerCount >= 2, 40, "volver a entrar en la sala");
        r.Check(NetSession.IsClient && ui.Current == UIManager.Screen.Lobby, "el cliente vuelve a entrar en la sala");
        yield return Wait(() => !NetSession.Active && ui.Current == UIManager.Screen.Main && gb.InMenu && gb.IsLoaded, 60, "el anfitrión se va");
        r.Check(!NetSession.Active, "si el anfitrión se va, el cliente sale de la sesión");
        r.Check(ui.Current == UIManager.Screen.Main, "el cliente vuelve al menú principal");
        yield return 0.5f;
        r.Check(ns.LastNotice.Contains("anfitrión") || ui.Multiplayer.NoticeText.Contains("anfitrión"), "mensaje «anfitrión ha abandonado la partida»: «" + ns.LastNotice + "»");
        Mark("client_done");
    }
}
