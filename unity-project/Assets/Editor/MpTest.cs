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
    public static readonly string Dir = TestPaths.Logs + "/mp";
    public static readonly int TestPort = int.TryParse(Environment.GetEnvironmentVariable("BE_MP_PORT"), out int tpEnv) ? tpEnv : 47777;   // BE_MP_PORT: otro puerto si hay dos pruebas a la vez
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
    // v7.0.4: suelo junto a la puerta (en el aparcamiento la salida está en una planta más baja)
    private static float ExitFloorY(BackroomsGenerator g) { return g.Garage != null && g.ExitTransform != null && g.ExitTransform.parent != null ? g.ExitTransform.parent.position.y + 0.1f : g.PlayerStart.y; }

    private static IEnumerator RunHost()
    {
        SmokeTest.Report r = report;
        ExitLock.AutoKeyForTests = true;          // v7.0.4: la llave se prueba aparte (bloque «Llaves»)
        NetSession.NoCatchForTests = true;        // v7.1.1: la IA real de los monstruos (en el anfitrión) no derriba por su cuenta: los derribos se provocan a mano (antes, por azar, causaba fallos intermitentes de reanimación y de la llave)
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
        // ---- ascensor de entrada (v6.4): ambos jugadores bajan juntos en la cabina ----
        {
            ElevatorCabin hc = ElevatorCabin.Current;
            r.Check(hc != null && hc.HasModel && hc.LevelIndex == 0, "[Ascensor MP] el anfitrión tiene la cabina del nivel 0");
            yield return Wait(() => hc.Begun, 20, "descenso del ascensor (anfitrión)");
            Mark("host_cab_begin");
            r.Check(hc.Locked && !gm.player.InputEnabled && !hc.Skippable, "[Ascensor MP] el anfitrión está bloqueado durante el descenso y no puede saltarlo");
            bool held = gm.monsters.Length > 0;
            foreach (MonsterAI m in gm.monsters) if (!m.IsStopped) held = false;
            r.Check(held, "[Ascensor MP] los monstruos esperan quietos en el anfitrión mientras baja el ascensor");
            yield return Wait(() => { Vector3 d = remote.avatar.LatestPos - hc.InteriorCenter; d.y = 0f; return d.magnitude < 1.6f; }, 30, "avatar del cliente dentro de la cabina");
            r.Check(remote.avatar != null, "[Ascensor MP] el avatar remoto del cliente está dentro de la cabina del anfitrión");
            yield return Wait(() => hc.Released, 40, "puertas abiertas (anfitrión)");
            Mark("host_cab_open");
            r.Check(!hc.Locked && gm.player.InputEnabled, "[Ascensor MP] al abrirse las puertas el anfitrión queda libre");
            bool freed = true;
            foreach (MonsterAI m in gm.monsters) if (m.IsStopped) freed = false;
            r.Check(freed, "[Ascensor MP] los monstruos se liberan en el anfitrión al abrirse las puertas");
            yield return WaitMark("client_cab_begin", 60);
            yield return WaitMark("client_cab_open", 60);
            double dBegin = Math.Abs((DateTime.Parse(File.ReadAllText(MpTest.Dir + "/host_cab_begin")) - DateTime.Parse(File.ReadAllText(MpTest.Dir + "/client_cab_begin"))).TotalSeconds);
            double dOpen = Math.Abs((DateTime.Parse(File.ReadAllText(MpTest.Dir + "/host_cab_open")) - DateTime.Parse(File.ReadAllText(MpTest.Dir + "/client_cab_open"))).TotalSeconds);
            r.Check(dBegin < 2.0 && dOpen < 2.0, "[Ascensor MP] el descenso está sincronizado entre anfitrión y cliente (inicio Δ " + dBegin.ToString("F2") + " s, apertura Δ " + dOpen.ToString("F2") + " s)");
            ElevatorCabin.InstantForTests = true;   // los niveles siguientes del test no esperan al ascensor
        }
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
        // ---- v7.0.2: silbido (G) sincronizado en los dos sentidos ----
        {
            int hh0 = Whistle.HeardCount;
            Mark("host_whistle_ready");
            yield return WaitMark("client_whistled", 40);
            yield return Wait(() => Whistle.HeardCount > hh0, 15, "silbido del cliente en el anfitrión");
            NetPlayer rc = ns.Players.Find(p => !p.isLocal);
            r.Check(Whistle.HeardCount > hh0 && rc != null && Whistle.LastPlayer == rc.id && !Whistle.LastFromMimic, "[Silbido MP] el anfitrión oye el silbido del cliente (jugador " + Whistle.LastPlayer + ")");
            r.Check(rc != null && rc.avatar != null && (Whistle.LastPos - rc.avatar.transform.position).magnitude < 3f, "[Silbido MP] suena en la posición del cliente (audio 3D)");
            Whistle wh = GameManager.Instance.player.GetComponent<Whistle>();
            int e0 = Whistle.EmitCount;
            if (wh != null) wh.debugPress = true;
            yield return 0.4f;
            r.Check(Whistle.EmitCount == e0 + 1, "[Silbido MP] el anfitrión silba (G)");
            Mark("host_whistled");
            yield return WaitMark("client_heard_host", 40);
        }
        // pausa del anfitrión: no congela
        ui.PressEscape();
        yield return 0.3f;
        r.Check(ui.Current == UIManager.Screen.Pause && Time.timeScale == 1f, "pausa del anfitrión: el mundo sigue (Time.timeScale = 1)");
        ui.PressEscape();
        yield return 0.2f;

        {   // v7.9.0: apagón rojo del nivel 0: el anfitrión adelanta su reloj y el cliente lo ve a la vez (reloj en el snapshot)
            Level0Events le = Level0Events.Active; var Inv = System.Globalization.CultureInfo.InvariantCulture;
            r.Check(le != null && le.Schedule.Count > 0, "[Nivel 0 MP] sucesos del nivel 0 en el anfitrión");
            if (le != null && le.Schedule.Count > 0)
            {
                float t = le.Schedule[0].start + 3f; le.DebugSetClock(t);
                File.WriteAllText(MpTest.Dir + "/host_red.txt", le.Schedule[0].start.ToString("R", Inv) + " " + le.Schedule[0].dur.ToString("R", Inv));
                Mark("host_red_on");
                yield return 0.5f;
                r.Check(le.RedOn, "[Nivel 0 MP] apagón rojo en el anfitrión");
                yield return WaitMark("client_red_seen", 60);
                le.DebugSetClock(le.Schedule[0].End + 2f);
                Mark("host_red_off");
                yield return WaitMark("client_red_off", 60);
                le.DebugSetClock(5f);
            }
        }
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
        // v7.2.0: reanimar = mantener E junto al derribado. Quedarse al lado sin E ya no basta.
        Vector3 ap = remote.avatar.transform.position;
        GameManager.Instance.player.Teleport(new Vector3(ap.x + 0.9f, ap.y + 0.05f, ap.z), 0f);
        NetSession.DebugHoldRevive = false;
        yield return 1.2f;
        r.Check(remote.revive < 0.01f && remote.downed, "sin mantener E, quedarse al lado no reanima (" + remote.revive.ToString("0.00") + ")");
        r.Check(ns.LocalReviveTarget == remote, "el derribado al alcance es el objetivo de reanimación");
        r.Check(ui.Multiplayer.HudReviveText.Contains("Mantén") && ui.Multiplayer.HudReviveText.Contains("E"), "aviso «Mantén E para reanimar» en el HUD («" + ui.Multiplayer.HudReviveText + "»)");
        // mantener E 1.5 s y soltar: progresa y se cancela
        NetSession.DebugHoldRevive = true;
        Mark("host_hold_revive");
        yield return 1.5f;
        r.Check(ns.LocalReviving && remote.revive > 0.2f && remote.downed, "manteniendo E la reanimación progresa (" + remote.revive.ToString("0.00") + ")");
        r.Check(ui.Multiplayer.HudReviveFill > 0.15f, "anillo de progreso en el HUD (" + ui.Multiplayer.HudReviveFill.ToString("0.00") + ")");
        r.Check(GameManager.Instance.player.IsCrouching, "el que reanima se arrodilla (primera persona agachado)");
        r.Check(PlayerBody.ReachK > 0.5f, "la mano se alarga hacia el derribado (" + PlayerBody.ReachK.ToString("0.00") + ")");
        yield return Wait(() => Marked("client_saw_reviver"), 15, "el cliente ve al anfitrión agachado sobre él");
        int cancels0 = ns.ReviveCancels;
        NetSession.DebugHoldRevive = false;
        Mark("host_release_revive");
        yield return 0.6f;
        r.Check(remote.downed && remote.revive < 0.01f && ns.ReviveCancels > cancels0, "soltar E cancela la reanimación (progreso " + remote.revive.ToString("0.00") + ", sigue derribado)");
        r.Check(!ns.LocalReviving, "soltar E: deja de reanimar");
        // mantener E y alejarse: también cancela
        NetSession.DebugHoldRevive = true;
        yield return 1.0f;
        float before = remote.revive;
        GameManager.Instance.player.Teleport(new Vector3(ap.x + 4.5f, ap.y + 0.05f, ap.z), 0f);
        yield return 0.6f;
        r.Check(before > 0.1f && remote.downed && remote.revive < 0.01f && !ns.LocalReviving, "alejarse con E mantenida cancela (" + before.ToString("0.00") + " -> " + remote.revive.ToString("0.00") + ")");
        // completa: volver y mantener E ~4 s
        GameManager.Instance.player.Teleport(new Vector3(ap.x + 0.9f, ap.y + 0.05f, ap.z), 0f);
        yield return Wait(() => !remote.downed, 20, "reanimación completada");
        NetSession.DebugHoldRevive = false;
        r.Check(!remote.downed, "manteniendo E unos 4 s el compañero reanima al derribado");
        yield return 0.3f;
        r.Check(!remote.avatar.Downed, "el avatar vuelve a levantarse");
        r.Check(ui.Multiplayer.HudReviveText == "", "el aviso de reanimar desaparece al levantarse");
        Mark("host_revived");
        yield return Wait(() => Marked("client_revived_seen"), 30, "el cliente confirma la reanimación");
        {   // v7.9.0: el cliente se queda sin VIDA → el anfitrión lo derriba (reanimable), y al reanimarlo vuelve con vida
            yield return WaitMark("client_hp_zero", 40);
            yield return Wait(() => remote.downed, 10, "derribado por quedarse sin vida");
            r.Check(remote.downed && ns.HazardDowns > 0, "[Vida MP] el cliente sin vida queda derribado (no muere: se le puede reanimar)");
            Vector3 ap2 = remote.avatar.transform.position;
            GameManager.Instance.player.Teleport(new Vector3(ap2.x + 0.9f, ap2.y + 0.05f, ap2.z), 0f);
            NetSession.DebugHoldRevive = true;
            yield return Wait(() => !remote.downed, 20, "reanimación tras quedarse sin vida");
            NetSession.DebugHoldRevive = false;
            r.Check(!remote.downed, "[Vida MP] se reanima al compañero que se quedó sin vida");
            yield return WaitMark("client_hp_checked", 30);   // que el cliente compruebe su vida antes de volver a derribarlo
        }

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
        // v7.0.1: salto del anfitrión y salto del cliente visto en su copia remota
        {
            IEnumerator je = SmokeTest.JumpCheck(r, "[MP anfitrión] ", GameManager.Instance.player, "en multijugador");
            while (je.MoveNext()) yield return je.Current;
            yield return WaitMark("client_jump_start", 40);
            Mark("host_jump_watch");
            RemotePlayer rpj = RemotePlayer.All.Count > 0 ? RemotePlayer.All[0] : null;
            float y0 = rpj != null ? rpj.transform.position.y : 0f, ym = y0;
            for (float tj = 0f; tj < 3.5f; tj += Time.deltaTime) { if (rpj != null) ym = Mathf.Max(ym, rpj.transform.position.y); yield return null; }
            r.Check(rpj != null && ym - y0 > 0.4f, "[MP anfitrión] [salto] el salto del cliente se ve en su copia remota (sube " + (ym - y0).ToString("F2") + " m)");
        }
        yield return Wait(() => Marked("client_restarted"), 120, "el cliente confirma el reinicio");

        // ---- salida: ambos van a la puerta -> siguiente nivel ----
        int epoch0 = ns.Epoch;
        Vector3 ex = gen.ExitPosition;
        GameManager.Instance.player.Teleport(new Vector3(ex.x, ExitFloorY(gen), ex.z), 0f);
        yield return Wait(() => ns.ExitCount >= 1, 20, "contador de salida");
        r.Check(ns.UnlockedEpoch == epoch0, "[Llaves MP] el anfitrión abre la puerta de salida con la llave buena (para todos)");
        r.Info("jugadores en la salida: " + ns.ExitCount + "/" + ns.ExitTotal);
        yield return Wait(() => WorldReady(gb, ns, 1) && ns.Epoch > epoch0, 240, "nivel 1 cargado tras llegar a la salida todos");
        r.Check(gb.CurrentLevel == 1 && ns.Epoch > epoch0, "todos los jugadores en la salida -> se carga el nivel siguiente (época " + ns.Epoch + ")");
        yield return Wait(() => ns.AllReady(), 180, "todos listos en el nivel 1");
        r.Check(ns.HashesMatch() && ns.HashMismatches == 0, "el nivel 1 también coincide (misma huella)");
        NetPlayer remote1 = ns.Players.Find(p => !p.isLocal);
        r.Check(remote1 != null && remote1.avatar != null && remote1.avatar != null && (oldAvatar == null), "el avatar remoto se recrea en el nuevo nivel");
        r.Check(!ns.LocalDowned && remote1 != null && !remote1.downed, "al cambiar de nivel todos están en pie");
        {   // v7.0.2: un mímico por nivel (1+) además del monstruo, con el aspecto de un compañero; decide el anfitrión
            int mims = 0, mains = 0; MimicDisguise md = null;
            foreach (MonsterAI m in MonsterAI.Instances) { if (m == null) continue; if (m.mimic != null) { mims++; md = m.mimic; } else mains++; }
            r.Check(mims == 1 && mains >= 1, "[Mímico MP] nivel 1: 1 mímico + " + mains + " monstruo(s) en el anfitrión");
            r.Check(md != null && md.Disguised && md.puppet != null && !md.ai.netProxy, "[Mímico MP] el anfitrión lleva la IA del mímico disfrazado (copia de «" + (md != null ? md.CopiedName : "-") + "»)");
            r.Check(Sanity.Instance != null, "[Cordura MP] el anfitrión tiene su propia cordura");
            if (md != null) { int em = Whistle.EmitCount; md.WhistleBackNow(); r.Check(Whistle.EmitCount == em + 1 && Whistle.LastFromMimic, "[Silbido MP] el mímico silba (decide el anfitrión) y se difunde"); yield return 1.0f; }
        }
        Mark("host_level1");

        // ---- v6.8.1: nivel 2 (Poolrooms) — el cliente sale de la piscina fija; el anfitrión lo ve en la playa ----
        {
            BackroomsGenerator gen1 = gb.Generator; int epoch1 = ns.Epoch; Vector3 ex1 = gen1.ExitPosition;
            {   // v7.0.4: esta vez abre el cliente (con su llave); el anfitrión no tiene llave y espera
                ExitLock.AutoKeyForTests = false;
                float tw = 0f; while (tw < 90f && !(ExitLock.Active != null && ExitLock.Active.Unlocked)) { yield return 0.25f; tw += 0.25f; }
                NetPlayer rc = ns.Players.Find(p => !p.isLocal);
                r.Check(ExitLock.Active != null && ExitLock.Active.Unlocked && rc != null && ExitLock.Active.UnlockedBy == rc.name,
                    "[Llaves MP] el cliente abre la puerta y el anfitrión la ve abierta (por «" + (ExitLock.Active != null ? ExitLock.Active.UnlockedBy : "-") + "»)");
                ExitLock.AutoKeyForTests = true;
            }
            GameManager.Instance.player.Teleport(new Vector3(ex1.x, ExitFloorY(gen1), ex1.z), 0f);
            yield return Wait(() => WorldReady(gb, ns, 2) && ns.Epoch > epoch1, 240, "nivel 2 cargado tras la salida del nivel 1");
            yield return Wait(() => ns.AllReady(), 180, "todos listos en el nivel 2");
            r.Check(gb.CurrentLevel == 2 && ns.HashesMatch() && ns.HashMismatches == 0, "[Piscina MP] nivel 2 cargado y con la misma huella (sala fija)");
            r.Check(gb.Generator.PoolFixed && PoolExit.Active, "[Piscina MP] la sala de piscina fija existe en el anfitrión");
            foreach (MonsterAI m in MonsterAI.Instances) m.Stop();
            GameManager.Instance.player.Teleport(gb.Generator.PlayerStart, 0f);
            Mark("host_level2");
            yield return Wait(() => { NetPlayer q = ns.Players.Find(p => !p.isLocal); return q != null && q.avatar != null; }, 60, "avatar remoto en el nivel 2");
            NetPlayer remote2 = ns.Players.Find(p => !p.isLocal);
            r.Check(remote2 != null && remote2.avatar != null, "[Piscina MP] avatar remoto del cliente en el nivel 2");
            string[] steps = { "borde_norte", "borde_sur", "borde_oeste", "borde_este", "escalera" };
            foreach (string st in steps)
            {
                yield return WaitMark("client_pool_" + st + "_in", 90);
                yield return Wait(() => remote2.avatar != null && PoolExit.InPool(remote2.avatar.transform.position + Vector3.up * 0.05f), 6, "avatar remoto dentro del vaso (" + st + ")");
                r.Check(remote2.avatar != null && PoolExit.InPool(remote2.avatar.transform.position + Vector3.up * 0.05f), "[Piscina MP] " + st + ": el anfitrión ve al cliente dentro del vaso");
                yield return WaitMark("client_pool_" + st + "_out", 90);
                yield return Wait(() => remote2.avatar != null && !PoolExit.InPool(remote2.avatar.transform.position) && remote2.avatar.transform.position.y > PoolExit.DeckY - 0.15f, 6, "avatar remoto en la playa (" + st + ")");
                Vector3 apR = remote2.avatar != null ? remote2.avatar.transform.position : Vector3.zero;
                r.Check(remote2.avatar != null && !PoolExit.InPool(apR) && apR.y > PoolExit.DeckY - 0.15f, "[Piscina MP] " + st + ": el anfitrión ve al cliente salir a la playa (y " + apR.y.ToString("F2") + ")");
            }
            Mark("host_pool_done");
            remote1 = remote2;
        }

        // ---- v7.0: nivel 5 (Campo infinito) — misma semilla = mismas casas/colinas; pasos de hierba y roce del cliente audibles ----
        {
            yield return WaitMark("client_pool_ack", 60);
            int epoch5 = ns.Epoch;
            ns.HostLoadLevel(5, false, false);
            yield return Wait(() => WorldReady(gb, ns, 5) && ns.Epoch > epoch5, 240, "nivel 5 cargado (anfitrión)");
            yield return Wait(() => ns.AllReady(), 180, "todos listos en el nivel 5");
            r.Check(gb.CurrentLevel == 5 && ns.HashesMatch() && ns.HashMismatches == 0, "[Campo MP] nivel 5 cargado y con la misma huella (terreno, casas y accesorios deterministas)");
            r.Check(gb.IsLoaded && GameBootstrap.LastPrewarmPercent == 100 && GameBootstrap.LastPrewarmSeconds <= GameBootstrap.PrewarmBudget + 0.6f, "[Campo MP] anfitrión: campo precargado dentro del presupuesto (" + GameBootstrap.LastPrewarmInfo + ", carga " + LoadDiag.LastTotalSeconds.ToString("F2") + " s)");
            FieldWorld fw = FieldWorld.Active;
            r.Check(fw != null && fw.houseCount >= 6 && FieldGrass.Active != null, "[Campo MP] campo con casas (" + (fw != null ? fw.houseCount : 0) + ") y hierba en el anfitrión");
            File.WriteAllText(MpTest.Dir + "/field_host.txt", gb.Generator.UsedSeed + ";" + (fw != null ? fw.houseCount : 0) + ";" + gb.Generator.ExitPosition.x.ToString("F2") + ";" + gb.Generator.ExitPosition.z.ToString("F2"));
            foreach (MonsterAI m in MonsterAI.Instances) m.Stop();
            Mark("host_level5");
            yield return Wait(() => { NetPlayer q = ns.Players.Find(p => !p.isLocal); return q != null && q.avatar != null; }, 60, "avatar remoto en el nivel 5");
            NetPlayer remote5 = ns.Players.Find(p => !p.isLocal);
            yield return WaitMark("client_field_walk", 120);
            int gs0 = remote5.avatar != null ? remote5.avatar.GrassStepsPlayed : 0;
            float rmax = 0f;
            for (float tt = 0f; tt < 5f; tt += 0.1f) { if (remote5.avatar != null) rmax = Mathf.Max(rmax, remote5.avatar.RustleLevel); yield return 0.1f; }
            int gs1 = remote5.avatar != null ? remote5.avatar.GrassStepsPlayed : 0;
            r.Check(gs1 > gs0, "[Campo MP] el anfitrión oye las pisadas de hierba del cliente (" + (gs1 - gs0) + " pasos)");
            r.Check(rmax > 0.1f, "[Campo MP] el anfitrión oye el roce de la hierba del cliente al moverse (nivel " + rmax.ToString("F2") + ")");
            yield return WaitMark("client_field_done", 120);
            Mark("host_field_done");
        }

        // ---- v7.1.0: nivel 6 (Parque de bolas): mismas salas y llaves; el monstruo se sumerge y emerge en el anfitrión y el cliente lo ve ----
        {
            int epoch6 = ns.Epoch;
            ns.HostLoadLevel(6, false, false);
            yield return Wait(() => WorldReady(gb, ns, 6) && ns.Epoch > epoch6, 240, "nivel 6 cargado (anfitrión)");
            yield return Wait(() => ns.AllReady(), 180, "todos listos en el nivel 6");
            BackroomsGenerator g6 = gb.Generator;
            r.Check(gb.CurrentLevel == 6 && ns.HashesMatch() && ns.HashMismatches == 0 && g6.Halls.Count >= 3 && BallPitField.All.Count == g6.Halls.Count, "[Parque MP] nivel 6 cargado con la misma huella (" + g6.Halls.Count + " salas de bolas)");
            Vector3 pk = g6.HallPitKeys.Count > 0 ? g6.HallPitKeys[0] : Vector3.zero;
            File.WriteAllText(MpTest.Dir + "/bp_host.txt", g6.UsedSeed + ";" + g6.Halls.Count + ";" + pk.x.ToString("F2") + ";" + pk.z.ToString("F2"));
            ElevatorCabin cab6 = ElevatorCabin.Current;
            yield return Wait(() => cab6 == null || cab6.Released, 40, "puertas del ascensor abiertas (parque)");
            yield return 0.5f;
            foreach (MonsterAI m in MonsterAI.Instances) m.Stop();
            Mark("host_level6");
            yield return WaitMark("client_bp_ready", 240);
            BallPitLurker lk = BallPitLurker.Active;
            r.Check(lk != null, "[Parque MP] el anfitrión controla el monstruo de las piscinas");
            if (lk != null && BallPitField.All.Count > 0)
            {
                bool dis = BallPitLurker.DisabledForTests; BallPitLurker.DisabledForTests = false;
                lk.Submerge(BallPitField.All[0], 0);
                yield return 0.8f;
                r.Check(lk.IsHidden, "[Parque MP] anfitrión: monstruo sumergido");
                Mark("host_bp_sub");
                yield return WaitMark("client_bp_seen", 60);
                lk.BeginEmerge(false);
                yield return Wait(() => lk.Current == BallPitLurker.Phase.Roaming, 10, "emerger (anfitrión)");
                foreach (MonsterAI m in MonsterAI.Instances) m.Stop();
                BallPitLurker.DisabledForTests = dis;
                yield return 1.5f;
            }
            Mark("host_bp_emerged");
            yield return WaitMark("client_bp_done", 120);
            Mark("host_bp_done");
        }

        // ---- v7.3.0: nivel 7 (Talasofobia): barca, casco, bengala, pez que derriba, reanimar en el agua, jaula y salida por la gruta ----
        {
            int epoch7 = ns.Epoch;
            ns.HostLoadLevel(7, false, false);
            yield return Wait(() => WorldReady(gb, ns, 7) && ns.Epoch > epoch7, 240, "nivel 7 cargado (anfitrión)");
            yield return Wait(() => ns.AllReady(), 180, "todos listos en el nivel 7");
            OceanWorld w = OceanWorld.Active;
            r.Check(gb.CurrentLevel == 7 && ns.HashesMatch() && ns.HashMismatches == 0 && w != null && MonsterAI.Instances.Count == 0, "[Mar MP] nivel 7 con la misma huella en los dos equipos y sin monstruo de NavMesh");
            ElevatorCabin cab7 = ElevatorCabin.Current;
            yield return Wait(() => cab7 == null || cab7.Released, 40, "puertas del ascensor abiertas (mar)");
            NetPlayer rc = ns.Players.Find(p => !p.isLocal);
            FirstPersonController hp = GameManager.Instance.player;
            OceanPlayer hop = OceanPlayer.Local;
            if (w != null) { w.fish.enabled = false; w.angler.enabled = false; }
            hp.Teleport(OceanWorld.PierLadderTop + new Vector3(0f, 0.05f, 0.6f), 90f);   // a salvo en el muelle
            yield return 0.5f;
            Mark("host_level7");
            // barca: la conduce el cliente, la autoridad la mueve
            yield return WaitMark("client_oc_boarded", 90);
            r.Check(w.boat.SeatOf(rc.id) == 0 && w.IsMountedHost(rc.id), "[Mar MP] el cliente sube a la barca (asiento del conductor según el anfitrión)");
            Vector3 b0 = w.boat.transform.position;
            yield return WaitMark("client_oc_drove", 60);
            float bd = Vector3.Distance(b0, w.boat.transform.position);
            r.Check(bd > 2f, "[Mar MP] el cliente conduce la barca y el anfitrión la mueve (" + bd.ToString("F1") + " m)");
            yield return WaitMark("client_oc_left", 30);
            r.Check(w.boat.SeatOf(rc.id) < 0 && !w.IsMountedHost(rc.id), "[Mar MP] el cliente baja de la barca");
            // casco
            yield return WaitMark("client_oc_helmet", 60);
            yield return 0.5f;
            bool rh = false; if (rc.avatar != null) foreach (Transform c in rc.avatar.GetComponentsInChildren<Transform>(true)) if (c.name == "RemoteHelmet") rh = true;
            r.Check(w.HasHelmetHost(rc.id) && w.rack.TakenCount == 1 && rh, "[Mar MP] el anfitrión concede el casco al cliente y lo ve en su avatar");
            // bengala del cliente
            yield return WaitMark("client_oc_flare", 60);
            yield return Wait(() => OceanFlare.All.Exists(f => f != null && f.owner == rc.id), 5, "bengala del cliente en el anfitrión");
            r.Check(OceanFlare.All.Exists(f => f != null && f.owner == rc.id), "[Mar MP] el anfitrión ve la bengala del cliente");
            // un pez derriba al cliente que nada (en multijugador: derribado, no muerto)
            yield return WaitMark("client_oc_swim", 60);
            yield return 0.5f;
            NetSession.NoCatchForTests = false; w.fish.enabled = true;
            OceanFishSchool.Fish fz = w.fish.fish[0];
            fz.pos = rc.avatar.LatestPos + new Vector3(5f, -1.5f, 0f); fz.state = OceanFishSchool.FishState.Charge; fz.target = rc.id; fz.t = 0f;
            yield return Wait(() => rc.downed, 10, "el pez derriba al cliente");
            NetSession.NoCatchForTests = true; w.fish.enabled = false;
            r.Check(rc.downed && w.fish.Bites >= 1 && GameManager.Instance.State == GameManager.GameState.Playing, "[Mar MP] un pez derriba al cliente que nada (los demás siguen jugando)");
            // reanimar en el agua (mantener E al lado)
            Vector3 cpos = rc.avatar.LatestPos;
            hp.Teleport(new Vector3(cpos.x + 1.3f, -0.6f, cpos.z), 270f);
            yield return 1.0f;
            NetSession.DebugHoldRevive = true;
            yield return Wait(() => !rc.downed, 20, "reanimación en el agua");
            NetSession.DebugHoldRevive = false;
            r.Check(!rc.downed, "[Mar MP] se reanima al compañero en el agua manteniendo E");
            Mark("host_oc_revived");
            // casco del anfitrión y jaula con los dos
            hp.Teleport(OceanWorld.HelmetRack + new Vector3(0.8f, 0.05f, -1.7f), 0f);
            yield return 0.6f;
            {
                // mirar al centro del soporte (robusto aunque el anfitrión llegue nadando o el muelle lo recoloque)
                Vector3 rc0 = w.rack.transform.position + Vector3.up * 1.1f, eye0 = hp.cameraPivot.position, dr = rc0 - eye0;
                hp.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(dr.x, dr.z) * Mathf.Rad2Deg, 0f);
                hp.SetPitch(-Mathf.Atan2(dr.y, new Vector2(dr.x, dr.z).magnitude) * Mathf.Rad2Deg);
            }
            yield return 0.2f;
            hop.debugHold = true;
            yield return Wait(() => hop.HasHelmet, 8, "casco del anfitrión");
            hop.debugHold = false; hp.SetPitch(0f);
            if (!hop.HasHelmet)
            {
                int sl; bool la = w.rack.LookingAt(hp.cameraPivot, out sl);
                r.Info("[Mar MP] casco del anfitrión: pies " + hp.transform.position.ToString("F2") + " soporte " + w.rack.transform.position.ToString("F2") + " mirando " + la + " hueco " + sl + " montado " + hp.IsMounted + " nadando " + hp.IsSwimming + " cogidos " + w.rack.TakenCount + " estado " + GameManager.Instance.State);
            }
            r.Check(hop.HasHelmet && w.rack.TakenCount == 2, "[Mar MP] dos cascos repartidos (anfitrión y cliente)");
            yield return WaitMark("client_oc_incage", 60);
            hp.Teleport(OceanWorld.CageTop + new Vector3(0.4f, 0.08f, 0.4f), 270f);
            yield return Wait(() => w.cage.State == OceanCage.CageState.Descending, 15, "la jaula baja con los dos");
            r.Check(w.cage.State == OceanCage.CageState.Descending && w.cage.IsRiderHost(rc.id), "[Mar MP] con los dos dentro y con casco la jaula baja (el cliente va dentro)");
            yield return Wait(() => w.cage.State == OceanCage.CageState.BottomOpen, 45, "la jaula llega al fondo");
            yield return 1.6f;
            r.Check(hp.transform.position.y < -40f && !hp.IsMounted, "[Mar MP] el anfitrión sale de la jaula en el fondo");
            Mark("host_oc_bottom");
            // salida: los dos en la poza de la gruta
            yield return WaitMark("client_oc_atexit", 60);
            hp.Teleport(OceanWorld.PocketCenter + new Vector3(-0.8f, -1.4f, 0f), 0f);
            yield return Wait(() => GameManager.Instance == null || GameManager.Instance.State != GameManager.GameState.Playing || gb.CurrentLevel == 8, 20, "nivel superado (anfitrión)");
            r.Check(GameManager.Instance == null || GameManager.Instance.State == GameManager.GameState.LevelComplete || gb.CurrentLevel == 8, "[Mar MP] los dos en la poza de la gruta: nivel superado (v7.7.0: sigue el Nivel 114)");
            yield return WaitMark("client_oc_done", 60);
            Mark("host_oc_done");
        }

        // ---- v7.7.0: nivel 8 (Nivel 114, El Desierto Cálido): misma huella, mismo reloj (hora y clima), objetos de la arena, arenas movedizas y salida en grupo ----
        {
            var Inv = System.Globalization.CultureInfo.InvariantCulture;
            yield return Wait(() => WorldReady(gb, ns, 8), 240, "nivel 8 cargado (anfitrión)");
            yield return Wait(() => ns.AllReady(), 180, "todos listos en el nivel 8");
            DesertWorld dw = DesertWorld.Active; DesertSky sky = DesertSky.Active;
            r.Check(gb.CurrentLevel == 8 && ns.HashesMatch() && ns.HashMismatches == 0 && dw != null && sky != null && MonsterAI.Instances.Count == 0, "[Desierto MP] Nivel 114 con la misma huella en los dos equipos, cielo propio y sin entidades");
            ElevatorCabin cab8 = ElevatorCabin.Current;
            yield return Wait(() => cab8 == null || cab8.Released, 40, "puertas del ascensor abiertas (desierto)");
            DesertFx.HazardsOn = false;
            NetPlayer rc = ns.Players.Find(p => !p.isLocal);
            FirstPersonController hp = GameManager.Instance.player;
            Mark("host_level8");
            yield return 2.0f;
            File.WriteAllText(MpTest.Dir + "/host_dclock.txt", sky.Clock.ToString("R", Inv) + " " + DateTime.UtcNow.Ticks + " " + (int)sky.Current + " " + sky.Hour.ToString("R", Inv) + " " + (DesertNight.Active != null ? DesertNight.Active.Seed : -1));
            Mark("host_dclock");
            yield return WaitMark("client_dclock", 60);
            {   // v7.7.1: la cordura del anfitrión tampoco baja en el desierto
                Sanity sn = Sanity.Instance; Flashlight fl8 = hp.GetComponentInChildren<Flashlight>(); if (fl8 != null) fl8.SetOn(false);
                if (sn != null) sn.value = 70f;
                yield return 3.0f;
                r.Check(sn != null && sn.value >= 70f - 0.001f && sn.DrainRate >= 0f, "[Desierto MP] la cordura del anfitrión no baja con la linterna apagada (" + (sn != null ? sn.value.ToString("F1") : "-") + ")");
                if (sn != null) sn.value = 100f;
            }
            // un objeto de la arena: lo coge el anfitrión
            ItemPickup sp = null; foreach (ItemPickup ip in UnityEngine.Object.FindObjectsOfType<ItemPickup>()) if (ip.netId >= DesertItems.NetBase && ip.netId < DesertItems.NetBase + 1000 && (sp == null || ip.netId < sp.netId)) sp = ip;
            int pid = sp != null ? sp.netId : -1;
            if (sp != null) hp.Teleport(sp.transform.position + Vector3.up * 0.15f, 0f);
            yield return Wait(() => pid >= 0 && !NetSession.PickupExists(pid), 15, "objeto del desierto recogido (anfitrión)");
            r.Check(pid >= 0 && !NetSession.PickupExists(pid), "[Desierto MP] el anfitrión recoge un objeto de la arena (id " + pid + ")");
            File.WriteAllText(MpTest.Dir + "/host_dpick.txt", pid.ToString());
            Mark("host_dpick_done");
            {   // v7.8.0: todoterreno en multijugador: el anfitrión conduce, el cliente va de copiloto (plazas, pose y bajar sincronizados)
                DesertCars dc = DesertCars.Active; DesertCar car = dc != null ? dc.Drivable : null;
                r.Check(car != null && dc.wreck != null, "[Coche MP] hay todoterreno y chatarra en el desierto (anfitrión)");
                if (car != null)
                {
                    hp.Teleport(car.transform.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.6f;
                    DesertCars.DebugInteract = true;
                    yield return Wait(() => car.SeatOf(ns.LocalId) == 0 && hp.IsMounted, 10, "anfitrión al volante");
                    r.Check(car.SeatOf(ns.LocalId) == 0 && hp.IsMounted, "[Coche MP] el anfitrión se sube al volante");
                    Mark("host_car_in");
                    yield return Wait(() => rc != null && car.SeatOf(rc.id) > 0, 40, "cliente de copiloto (anfitrión)");
                    r.Check(rc != null && car.SeatOf(rc.id) > 0 && ns.CarReqsReceived > 0, "[Coche MP] el cliente pide subir y el anfitrión le da la plaza " + (rc != null ? car.SeatOf(rc.id) : -1) + " (" + ns.CarReqsReceived + " peticiones)");
                    Vector3 c0 = car.transform.position;
                    DesertCars.DebugDrive = true; DesertCars.DbgThrottle = 1f; DesertCars.DbgSteer = 0.25f; DesertCars.DbgHandbrake = false;
                    yield return 3.5f;
                    float spd = car.Speed, moved = Vector3.Distance(c0, car.transform.position);
                    DesertCars.DbgThrottle = 0f; DesertCars.DbgSteer = 0f; DesertCars.DbgHandbrake = true;
                    float still = 0f, waited = 0f;   // parado de verdad: 0,6 s seguidos por debajo de 0,3 m/s
                    while (still < 0.6f && waited < 25f) { yield return null; waited += Time.deltaTime; still = car.Speed < 0.3f ? still + Time.deltaTime : 0f; }
                    r.Check(still >= 0.6f, "[Coche MP] el todoterreno se para con el freno de mano (" + waited.ToString("F1") + " s)");
                    yield return 1.0f;
                    r.Check(spd > 4f && moved > 6f, "[Coche MP] el anfitrión conduce con el cliente dentro (" + (spd * 3.6f).ToString("F0") + " km/h, " + moved.ToString("F0") + " m)");
                    Vector3 cl = dw.levelRoot.InverseTransformPoint(car.transform.position);
                    File.WriteAllText(MpTest.Dir + "/host_car.txt", cl.x.ToString("R", Inv) + " " + cl.y.ToString("R", Inv) + " " + cl.z.ToString("R", Inv));
                    {   // v7.9.0: claxon que oyen todos
                        DesertCars.DbgHorn = true; yield return 0.6f; DesertCars.DbgHorn = false; yield return 0.4f;
                        r.Check(car.HornEvents >= 1 && !car.HornOn, "[Coche MP] el anfitrión toca el claxon (" + car.HornEvents + " toques)");
                        RemotePlayer pas = null; foreach (RemotePlayer rp in UnityEngine.Object.FindObjectsOfType<RemotePlayer>()) if (rc != null && rp.playerId == rc.id) pas = rp;
                        r.Check(pas != null && !pas.LampShown && !pas.OnWheel, "[Coche MP] el copiloto remoto lleva la linterna guardada y no toca el volante");
                    }
                    Mark("host_car_drove");
                    yield return WaitMark("client_car_out", 60);
                    yield return Wait(() => rc == null || car.SeatOf(rc.id) < 0, 10, "plaza del cliente libre");
                    r.Check(rc != null && car.SeatOf(rc.id) < 0, "[Coche MP] el cliente se baja y su plaza queda libre en el anfitrión");
                    DesertCars.DebugInteract = true;
                    yield return Wait(() => !hp.IsMounted, 6, "anfitrión baja");
                    r.Check(!hp.IsMounted && car.Occupants == 0, "[Coche MP] el anfitrión baja; el coche queda vacío");
                    DesertCars.DebugDrive = false;
                }
                Mark("host_car_done");
            }
            // arenas movedizas del cliente: el anfitrión lo ve reaparecer lejos
            yield return WaitMark("client_dquick_start", 60);
            Vector3 q0 = rc != null && rc.avatar != null ? rc.avatar.LatestPos : Vector3.zero;
            yield return WaitMark("client_dquick_done", 60);
            yield return 1.0f;
            Vector3 q1 = rc != null && rc.avatar != null ? rc.avatar.LatestPos : Vector3.zero;
            Vector3 dq = new Vector3(DesertHeight.Wrap(q1.x - q0.x), 0f, DesertHeight.Wrap(q1.z - q0.z));
            r.Check(dq.magnitude > 15f, "[Desierto MP] el anfitrión ve al cliente reaparecer lejos tras las arenas movedizas (" + dq.magnitude.ToString("F0") + " m)");
            // salida en grupo por la puerta de piedra
            yield return WaitMark("client_d_atexit", 60);
            Vector3 door = dw.ExitTransform.position, st = dw.levelRoot.TransformPoint(dw.ExitLocal);
            Vector3 side = Vector3.Cross(Vector3.up, st - door).normalized;
            hp.Teleport(st + side * 0.45f + Vector3.up * 0.1f, Mathf.Atan2(door.x - st.x, door.z - st.z) * Mathf.Rad2Deg);
            yield return Wait(() => GameManager.Instance == null || GameManager.Instance.State != GameManager.GameState.Playing || gb.CurrentLevel == 9, 20, "nivel superado (anfitrión, desierto)");
            r.Check(GameManager.Instance == null || GameManager.Instance.State == GameManager.GameState.LevelComplete || gb.CurrentLevel == 9, "[Desierto MP] los dos en la puerta de piedra: nivel superado (sigue el Nivel 7460)");
            yield return WaitMark("client_d_done", 60);
            Mark("host_d_done");
            DesertFx.HazardsOn = true;
        }

        // ---- Nivel 7460 (El pasillo de las puertas): mismo plano para todos, puertas que se abren al pasar cualquiera, ruidos locales y salida en grupo ----
        {
            yield return Wait(() => WorldReady(gb, ns, 9), 240, "nivel 9 cargado (anfitrión)");
            yield return Wait(() => ns.AllReady(), 180, "todos listos en el nivel 9");
            CorridorWorld cw = CorridorWorld.Active;
            r.Check(gb.CurrentLevel == 9 && ns.HashesMatch() && ns.HashMismatches == 0 && cw != null && MonsterAI.Instances.Count == 0 && ExitLock.Active != null, "[Pasillo MP] Nivel 7460 con la misma huella en los dos equipos, sin entidades y con cerradura");
            ElevatorCabin cab9 = ElevatorCabin.Current;
            yield return Wait(() => cab9 == null || cab9.Released, 40, "puertas del ascensor abiertas (pasillo)");
            r.Check(cab9 == null || cab9.Released, "[Pasillo MP] llegada en ascensor como en los demás niveles");
            FirstPersonController hp = GameManager.Instance.player;
            File.WriteAllText(MpTest.Dir + "/host_corridor.txt", gb.Generator.UsedSeed + ";" + cw.plan.exitRow + ";" + cw.plan.exitSide + ";" + cw.plan.Count(CorridorPlan.Door.Room) + ";" + cw.RoomCount);
            Mark("host_level9");
            r.Check(CorridorEvents.Active != null, "[Pasillo MP] ruidos a la espalda del anfitrión (locales, no viajan por la red)");
            {   // v7.10.1: luz roja del 7460 a la vez para todos (reloj del anfitrión en el snapshot)
                CorridorRed cr = CorridorRed.Active; var Inv = System.Globalization.CultureInfo.InvariantCulture;
                r.Check(cr != null && cr.Schedule.Count > 0, "[Pasillo MP] luz roja de emergencia en el anfitrión");
                float rs0 = cr != null && cr.Schedule.Count > 0 ? cr.Schedule[0].start : 0f, rd0 = cr != null && cr.Schedule.Count > 0 ? cr.Schedule[0].dur : 0f;
                File.WriteAllText(MpTest.Dir + "/host_c_red.txt", rs0.ToString("R", Inv) + " " + rd0.ToString("R", Inv));
                if (cr != null) cr.DebugSetClock(rs0 + rd0 * 0.5f);
                Mark("host_c_red_on");
                yield return 0.5f;
                r.Check(cr != null && cr.RedOn, "[Pasillo MP] rojo en el anfitrión");
                yield return WaitMark("client_c_red_seen", 60);
                if (cr != null) cr.DebugSetClock(rs0 + rd0 + 2f);
                Mark("host_c_red_off");
                yield return WaitMark("client_c_red_off", 60);
                if (cr != null) cr.DebugSetClock(3f);
            }
            // una habitación: el anfitrión se acerca y el cliente debe ver la hoja abierta
            int rr = -1, rs = 0;
            for (int j = 0; j < cw.plan.rows && rr < 0; j++) for (int sd = 0; sd < 2; sd++) if (cw.plan.doors[sd, j] == CorridorPlan.Door.Room) { rr = j; rs = sd; break; }
            yield return WaitMark("client_c_ready", 60);
            if (rr >= 0)
            {
                hp.Teleport(cw.DoorWorld(rs, rr, 0.6f), rs == CorridorPlan.Left ? 270f : 90f);
                File.WriteAllText(MpTest.Dir + "/host_c_door.txt", rs + ";" + rr);
                Mark("host_c_door");
                yield return WaitMark("client_c_door_seen", 30);
            }
            // salida en grupo (la llave la prueba el bloque «Llaves»; aquí AutoKeyForTests)
            yield return WaitMark("client_c_atexit", 90);
            Vector3 st = cw.DoorWorld(cw.plan.exitSide, cw.plan.exitRow, 0.75f);
            Vector3 along = cw.levelRoot.TransformDirection(Vector3.forward);
            hp.Teleport(st + along * 0.35f + Vector3.up * 0.1f, cw.plan.exitSide == CorridorPlan.Left ? 270f : 90f);
            yield return Wait(() => GameManager.Instance.State == GameManager.GameState.Won, 25, "victoria (anfitrión)");
            r.Check(ExitLock.Active == null || ExitLock.Active.Unlocked, "[Pasillo MP] la salida queda abierta para los dos");
            r.Check(GameManager.Instance.State == GameManager.GameState.Won, "[Pasillo MP] los dos en la salida del pasillo: victoria final (10 niveles)");
            yield return WaitMark("client_c_done", 60);
            Mark("host_c_done");
        }

        // ---- desconexión del cliente ----
        NetPlayer rnow = ns.Players.Find(p => !p.isLocal);
        if (rnow != null) remote1 = rnow;
        GameObject av1 = remote1 != null && remote1.avatar != null ? remote1.avatar.gameObject : null;
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
        ExitLock.AutoKeyForTests = true;          // v7.0.4: la llave se prueba aparte (bloque «Llaves»)
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
        // ---- ascensor de entrada (v6.4): el cliente baja con el anfitrión ----
        {
            ElevatorCabin cc = ElevatorCabin.Current;
            r.Check(cc != null && cc.HasModel && cc.LevelIndex == 0, "[Ascensor MP] el cliente tiene la cabina del nivel 0 (misma posición que el anfitrión por la semilla)");
            yield return Wait(() => cc.Begun, 30, "descenso del ascensor (cliente, orden del anfitrión)");
            Mark("client_cab_begin");
            r.Check(cc.Locked && !gm.player.InputEnabled, "[Ascensor MP] el cliente está bloqueado durante el descenso");
            Vector3 fl = gm.player.transform.position - cc.InteriorCenter; fl.y = 0f;
            r.Check(fl.magnitude < 1.6f, "[Ascensor MP] el cliente aparece dentro de la cabina (" + fl.magnitude.ToString("F2") + " m)");
            yield return Wait(() => cc.Released, 40, "puertas abiertas (cliente)");
            Mark("client_cab_open");
            r.Check(!cc.Locked && gm.player.InputEnabled, "[Ascensor MP] al abrirse las puertas el cliente queda libre");
            ElevatorCabin.InstantForTests = true;
        }
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
        // ---- v7.0.2: silbido (G) sincronizado ----
        {
            yield return WaitMark("host_whistle_ready", 40);
            Whistle wh = gm.player.GetComponent<Whistle>();
            r.Check(wh != null, "[Silbido MP] el cliente tiene el silbido (G)");
            int e0 = Whistle.EmitCount;
            if (wh != null) wh.debugPress = true;
            yield return 0.4f;
            r.Check(Whistle.EmitCount == e0 + 1, "[Silbido MP] el cliente silba (G) y se envía al anfitrión");
            int h0 = Whistle.HeardCount;   // antes de avisar: el silbido del anfitrión puede llegar enseguida
            Mark("client_whistled");
            yield return WaitMark("host_whistled", 40);
            yield return Wait(() => Whistle.HeardCount > h0 && Whistle.LastPlayer != ns.LocalId, 15, "silbido del anfitrión en el cliente");
            r.Check(Whistle.HeardCount > h0 && Whistle.LastPlayer != ns.LocalId && !Whistle.LastFromMimic, "[Silbido MP] el cliente oye el silbido del anfitrión (jugador " + Whistle.LastPlayer + ")");
            Mark("client_heard_host");
        }
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

        {   // v7.9.0: apagón rojo a la vez que el anfitrión (su reloj llega en el snapshot; la semilla es la misma)
            Level0Events le = Level0Events.Active; var Inv = System.Globalization.CultureInfo.InvariantCulture;
            yield return WaitMark("host_red_on", 120);
            string[] hr = File.ReadAllText(MpTest.Dir + "/host_red.txt").Trim().Split(' ');
            float hs = float.Parse(hr[0], Inv), hd = float.Parse(hr[1], Inv);
            yield return Wait(() => le != null && le.RedOn, 10, "apagón rojo en el cliente");
            r.Check(le != null && le.RedOn && le.Schedule.Count > 0 && Mathf.Abs(le.Schedule[0].start - hs) < 1e-3f && Mathf.Abs(le.Schedule[0].dur - hd) < 1e-3f && le.NetClocks > 10,
                "[Nivel 0 MP] el cliente ve el apagón rojo a la vez (mismo calendario: " + hs.ToString("F1") + " s + " + hd.ToString("F1") + " s; reloj " + (le != null ? le.Clock.ToString("F1") : "-") + ", " + (le != null ? le.NetClocks : 0) + " relojes del anfitrión)");
            Mark("client_red_seen");
            yield return WaitMark("host_red_off", 60);
            yield return Wait(() => le != null && !le.RedOn, 10, "fin del apagón rojo en el cliente");
            r.Check(le != null && !le.RedOn, "[Nivel 0 MP] el apagón rojo acaba también en el cliente");
            Mark("client_red_off");
        }
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
        // v7.2.0: el anfitrión mantiene E a mi lado: lo veo agachado alargando la mano, y el progreso me llega
        yield return WaitMark("host_hold_revive", 40);
        NetPlayer hostP2 = hostP;
        yield return Wait(() => hostP2 != null && hostP2.avatar.Reviving && ns.LocalRevive01 > 0.1f, 10, "ver al compañero reanimándome");
        r.Check(hostP2 != null && hostP2.avatar.Reviving && hostP2.avatar.Crouching, "el compañero que reanima se ve agachado sobre mí");
        yield return 0.5f;
        r.Check(hostP2 != null && hostP2.avatar.ReviveK > 0.5f, "el compañero se inclina y alarga la mano (" + (hostP2 != null ? hostP2.avatar.ReviveK.ToString("0.00") : "-") + ")");
        r.Check(ns.LocalRevive01 > 0.1f && ui.Multiplayer.HudDownedText.Contains("%"), "el progreso de la reanimación llega al derribado (" + ns.LocalRevive01.ToString("0.00") + ")");
        Mark("client_saw_reviver");
        yield return WaitMark("host_release_revive", 30);
        yield return Wait(() => ns.LocalRevive01 < 0.01f, 5, "progreso cancelado");
        r.Check(ns.LocalDowned && ns.LocalRevive01 < 0.01f, "al soltar E el compañero, la reanimación se cancela y sigo derribado");
        yield return Wait(() => !ns.LocalDowned, 40, "ser reanimado");
        r.Check(!ns.LocalDowned && gm.player.InputEnabled, "reanimado: vuelve a moverse");
        yield return 0.8f;
        r.Check(gm.playerCamera.localPosition.y > 1.2f, "reanimado: la cámara vuelve a la altura de los ojos (" + gm.playerCamera.localPosition.y.ToString("0.00") + ")");
        Mark("client_revived_seen");
        {   // v7.9.0: sin vida → derribado (lo decide el anfitrión) → reanimado con algo de vida
            Health hp = Health.Instance;
            r.Check(hp != null && GameManager.Instance.healthFill != null, "[Vida MP] el cliente tiene barra de VIDA");
            yield return 4.5f;   // pasa la gracia de 4 s tras la reanimación
            if (hp != null) { hp.value = 1f; hp.Damage(5f, "prueba"); }
            yield return Wait(() => ns.LocalDowned, 10, "derribado al quedarse sin vida");
            r.Check(ns.LocalDowned && ns.HealthZeroSent > 0 && GameManager.Instance.State == GameManager.GameState.Playing, "[Vida MP] a 0 de vida el cliente queda derribado (aviso al anfitrión, " + ns.HealthZeroSent + ")");
            Mark("client_hp_zero");
            yield return Wait(() => !ns.LocalDowned, 40, "reanimado tras quedarse sin vida");
            yield return 0.3f;
            r.Check(!ns.LocalDowned && hp != null && hp.value >= Health.ReviveHealth - 0.5f, "[Vida MP] reanimado vuelve con " + (hp != null ? hp.value.ToString("F0") : "-") + " de vida");
            Mark("client_hp_checked");
        }

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
        // v7.0.1: el cliente salta (y el anfitrión lo ve subir)
        {
            GameManager gmj = GameManager.Instance;
            Mark("client_jump_start");
            yield return WaitMark("host_jump_watch", 40);
            yield return 0.3f;
            IEnumerator je = SmokeTest.JumpCheck(r, "[MP cliente] ", gmj.player, "en multijugador");
            while (je.MoveNext()) yield return je.Current;
        }
        Vector3 ex = gen.ExitPosition;
        int epoch0 = ns.Epoch;
        {   // v7.0.4: el cliente no tiene la llave: espera a que el anfitrión abra la puerta para todos
            ExitLock.AutoKeyForTests = false;
            float tw = 0f; while (tw < 90f && !(ExitLock.Active != null && ExitLock.Active.Unlocked)) { yield return 0.25f; tw += 0.25f; }
            NetPlayer hp = ns.Players.Find(p => !p.isLocal);
            r.Check(ExitLock.Active != null && ExitLock.Active.Unlocked && hp != null && ExitLock.Active.UnlockedBy == hp.name,
                "[Llaves MP] el anfitrión abre la puerta y el cliente la ve abierta (por «" + (ExitLock.Active != null ? ExitLock.Active.UnlockedBy : "-") + "»)");
            ExitLock.AutoKeyForTests = true;
        }
        gm.player.Teleport(new Vector3(ex.x, ExitFloorY(gen), ex.z), 0f);
        yield return Wait(() => WorldReady(gb, ns, 1) && ns.Epoch > epoch0, 240, "nivel 1 cargado (cliente)");
        r.Check(gb.CurrentLevel == 1 && ns.CurrentStart.level == 1, "el cliente carga el nivel siguiente decidido por el anfitrión");
        r.Check(NetSession.PickupCount >= 5, "pickups del nivel 1 registrados");
        int hm0 = Whistle.HeardCount;
        yield return WaitMark("host_level1", 240);
        yield return Wait(() => Whistle.HeardCount > hm0 && Whistle.LastFromMimic, 15, "silbido del mímico en el cliente");
        r.Check(Whistle.HeardCount > hm0 && Whistle.LastFromMimic && Whistle.LastPlayer == -1, "[Silbido MP] el cliente oye el silbido de respuesta del mímico (sincronizado)");
        {   // v7.0.2: el cliente ve el mímico (proxy) disfrazado de un jugador y tiene su propia cordura
            MimicDisguise md = null;
            yield return Wait(() => { foreach (MonsterAI m in MonsterAI.Instances) if (m != null && m.mimic != null) { md = m.mimic; return true; } return false; }, 30, "mímico en el cliente");
            r.Check(md != null && md.ai.netProxy && md.Disguised && md.puppet != null && md.puppet.gameObject.activeSelf, "[Mímico MP] el cliente ve el mímico disfrazado (proxy del anfitrión, copia de «" + (md != null ? md.CopiedName : "-") + "»)");
            Sanity sc = Sanity.Instance;
            r.Check(sc != null && sc.value > 0f, "[Cordura MP] el cliente tiene su propia barra COR (" + (sc != null ? sc.value.ToString("F0") : "-") + ")");
            if (sc != null && md != null)
            {
                // un monstruo (proxy) junto al cliente: su cordura baja (local, por proximidad)
                MonsterAI near = null; foreach (MonsterAI m in MonsterAI.Instances) if (m != null && m.mimic == null) near = m;
                if (near != null)
                {
                    Vector3 np = near.transform.position; Vector3 back = GameManager.Instance.player.transform.position; float by = GameManager.Instance.player.transform.eulerAngles.y;
                    GameManager.Instance.player.Teleport(np + near.transform.forward * 6f, Quaternion.LookRotation(-near.transform.forward).eulerAngles.y);
                    yield return 0.6f;
                    r.Check(sc.MonsterDrain > 1f, "[Cordura MP] cerca de un monstruo la cordura del cliente baja (" + sc.MonsterDrain.ToString("F2") + "/s a " + Vector3.Distance(GameManager.Instance.player.transform.position, near.transform.position).ToString("F1") + " m)");
                    GameManager.Instance.player.Teleport(back, by);
                    yield return 0.3f;
                }
            }
        }

        // ---- v6.8.1: nivel 2, salir de la piscina fija (borde 4 lados, escalera; v7.2.0: sin rescate) ----
        {
            BackroomsGenerator gen1 = gb.Generator; int epoch1 = ns.Epoch; Vector3 ex1 = gen1.ExitPosition;
            GameManager.Instance.player.Teleport(new Vector3(ex1.x, ExitFloorY(gen1), ex1.z), 0f);
            for (int di = 0; di < 6; di++)
            {   // diagnóstico v7.1.1 (prueba de llave intermitente por relay)
                yield return 0.5f;
                // v7.2.0: el nivel 2 puede empezar a cargarse durante el diagnóstico (GameManager/jugador recreados): sin esto, NRE intermitente
                if (GameManager.Instance == null || GameManager.Instance.player == null || ns.Epoch > epoch1) break;
                Vector3 dd = GameManager.Instance.player.transform.position - ex1; dd.y = 0f;
                r.Info("[Llaves diag] t=" + (di * 0.5f + 0.5f) + " derribado=" + ns.LocalDowned + " abierta=" + (ExitLock.Active != null && ExitLock.Active.Unlocked) + " por=" + (ExitLock.Active != null ? ExitLock.Active.UnlockedBy : "-") + " dist=" + dd.magnitude.ToString("F2") + " enSalida=" + NetSession.LocalAtExit + " ép=" + ns.WorldEpoch + "/" + ns.Epoch + " nivel=" + gb.CurrentLevel);
            }
            yield return Wait(() => WorldReady(gb, ns, 2) && ns.Epoch > epoch1, 240, "nivel 2 cargado (cliente)");
            yield return WaitMark("host_level2", 240);
            gm = GameManager.Instance; FirstPersonController pw = gm.player;
            r.Check(gb.Generator.PoolFixed && PoolExit.Active, "[Piscina MP] la sala de piscina fija existe en el cliente");
            Rect pr = PoolExit.PoolXZ; Vector3 pc0 = new Vector3(pr.center.x, PoolExit.DeckY - 1.35f, pr.center.y);
            Vector3[] sides = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            string[] steps = { "borde_norte", "borde_sur", "borde_oeste", "borde_este", "escalera" };
            for (int si = 0; si < steps.Length; si++)
            {
                Vector3 start; float yaw;
                if (si < 4)
                {
                    Vector3 d = sides[si]; float half = Mathf.Abs(d.x) > 0.5f ? pr.width * 0.5f : pr.height * 0.5f;
                    Vector3 along = new Vector3(d.z, 0f, d.x), bp = pc0 + d * (half - 1.0f), s1 = bp + along * 1.5f, s2 = bp - along * 1.5f;
                    start = PoolExit.HasLadder && (s1 - PoolExit.LadderPos).sqrMagnitude < (s2 - PoolExit.LadderPos).sqrMagnitude ? s2 : s1;
                    yaw = Quaternion.LookRotation(d).eulerAngles.y;
                }
                else { start = PoolExit.LadderPos - PoolExit.LadderOut * 0.5f + Vector3.up * 0.05f; yaw = Quaternion.LookRotation(PoolExit.LadderOut).eulerAngles.y; }
                pw.Teleport(start, yaw);
                yield return 1.0f;
                r.Check(PoolExit.InPool(pw.transform.position), "[Piscina MP] " + steps[si] + ": el cliente está dentro del vaso");
                Mark("client_pool_" + steps[si] + "_in");
                yield return 1.5f;
                int c0 = pw.ClimbOutCount + pw.LadderClimbCount;
                pw.debugInput = true;
                pw.debugMove = new Vector2(0f, 1f);
                float tt = 0f;
                while (tt < 8f && (PoolExit.InPool(pw.transform.position) || pw.IsClimbingLadder)) { yield return 0.1f; tt += 0.1f; }
                yield return 0.3f;
                pw.debugMove = Vector2.zero; pw.debugJump = false; pw.debugInput = false;
                int c1 = pw.ClimbOutCount + pw.LadderClimbCount;
                r.Check(!PoolExit.InPool(pw.transform.position) && pw.transform.position.y > PoolExit.DeckY - 0.1f && c1 > c0, "[Piscina MP] " + steps[si] + ": el cliente sale a la playa (" + tt.ToString("F1") + " s)");
                yield return 1.0f;
                Mark("client_pool_" + steps[si] + "_out");
            }
            yield return WaitMark("host_pool_done", 120);
        }

        // ---- v7.0: nivel 5 (Campo infinito): mismo mundo; el cliente camina y corre por la hierba ----
        {
            int epochP = ns.Epoch;
            float pb0 = GameBootstrap.PrewarmBudget;
            GameBootstrap.PrewarmStallForTest = true; GameBootstrap.PrewarmBudget = 1.0f;   // v7.0.3: en el cliente la precarga «nunca termina»
            Mark("client_pool_ack");
            yield return Wait(() => WorldReady(gb, ns, 5) && ns.Epoch > epochP && gb.IsLoaded, 240, "nivel 5 cargado (cliente)");
            GameBootstrap.PrewarmStallForTest = false; GameBootstrap.PrewarmBudget = pb0;
            r.Check(gb.IsLoaded && GameBootstrap.LastPrewarmTimedOut && GameBootstrap.LastPrewarmSeconds < 1.7f, "[Campo MP] cliente: con una precarga que nunca termina el campo se abre igual tras el tope (" + GameBootstrap.LastPrewarmInfo + ")");
            yield return WaitMark("host_level5", 240);
            string[] hf = File.Exists(MpTest.Dir + "/field_host.txt") ? File.ReadAllText(MpTest.Dir + "/field_host.txt").Split(';') : new string[0];
            FieldWorld fw = FieldWorld.Active;
            r.Check(hf.Length == 4 && hf[0] == gb.Generator.UsedSeed.ToString() && fw != null && hf[1] == fw.houseCount.ToString()
                && hf[2] == gb.Generator.ExitPosition.x.ToString("F2") && hf[3] == gb.Generator.ExitPosition.z.ToString("F2"),
                "[Campo MP] el cliente tiene la misma semilla, las mismas casas y la salida en el mismo sitio que el anfitrión");
            ElevatorCabin cab = ElevatorCabin.Current;
            yield return Wait(() => cab == null || cab.Released, 40, "puertas del ascensor abiertas (campo)");
            gm = GameManager.Instance; FirstPersonController pf = gm.player;
            Vector3 fwd = cab != null ? cab.Forward : Vector3.forward;
            Vector3 sp = (cab != null ? cab.ExitPoint : gb.Generator.PlayerStart) + fwd * 4f;
            Vector3 lp = gb.Generator.Root.InverseTransformPoint(sp); lp.y = FieldHeight.H(lp.x, lp.z) + 0.15f;
            pf.Teleport(gb.Generator.Root.TransformPoint(lp), Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg);
            yield return 0.8f;
            FootstepAudio fa = pf.GetComponent<FootstepAudio>();
            r.Check(GrassAudio.OnGrass(pf.transform.position), "[Campo MP] el cliente está sobre la hierba");
            int gst0 = fa != null ? fa.GrassStepsPlayed : 0;
            Mark("client_field_walk");
            pf.debugInput = true; pf.debugMove = new Vector2(0f, 1f); pf.debugSprint = true;
            float rmax = 0f;
            for (float tt = 0f; tt < 5f; tt += 0.1f) { if (fa != null) rmax = Mathf.Max(rmax, fa.RustleLevel); yield return 0.1f; }
            pf.debugMove = Vector2.zero; pf.debugSprint = false; pf.debugInput = false;
            int gst1 = fa != null ? fa.GrassStepsPlayed : 0;
            r.Check(gst1 > gst0, "[Campo MP] el cliente oye sus propias pisadas de hierba (" + (gst1 - gst0) + ")");
            r.Check(rmax > 0.3f, "[Campo MP] el cliente oye el roce de la hierba al correr (" + rmax.ToString("F2") + ")");
            Mark("client_field_done");
            yield return WaitMark("host_field_done", 60);
        }

        // ---- v7.1.0: nivel 6 (Parque de bolas) en el cliente ----
        {
            yield return Wait(() => WorldReady(gb, ns, 6) && gb.IsLoaded, 240, "nivel 6 cargado (cliente)");
            yield return WaitMark("host_level6", 240);
            BackroomsGenerator g6 = gb.Generator;
            string[] hb = File.Exists(MpTest.Dir + "/bp_host.txt") ? File.ReadAllText(MpTest.Dir + "/bp_host.txt").Split(';') : new string[0];
            Vector3 pk = g6.HallPitKeys.Count > 0 ? g6.HallPitKeys[0] : Vector3.zero;
            r.Check(hb.Length == 4 && hb[0] == g6.UsedSeed.ToString() && hb[1] == g6.Halls.Count.ToString() && hb[2] == pk.x.ToString("F2") && hb[3] == pk.z.ToString("F2") && BallPitField.All.Count == g6.Halls.Count,
                "[Parque MP] el cliente tiene las mismas salas y la llave enterrada en la misma piscina");
            gm = GameManager.Instance; FirstPersonController p6 = gm.player;
            if (BallPitField.All.Count > 0)
            {
                BallPitField f0 = BallPitField.All[0]; var pit = f0.pits[1 < f0.pits.Length ? 1 : 0];
                Rect ir = BallpitSetPiece.PitInner(pit, f0.rim);
                p6.Teleport(f0.transform.TransformPoint(new Vector3(ir.center.x, pit.floor + 0.3f, ir.center.y)), 0f);
                yield return 1.0f;
                r.Check(p6.BallDepth > 0.25f && p6.BallWadeFactor < 0.9f, "[Parque MP] el cliente vadea entre las bolas (profundidad " + p6.BallDepth.ToString("F2") + ")");
            }
            Mark("client_bp_ready");
            yield return WaitMark("host_bp_sub", 120);
            BallPitMonsterFx fx = null;
            yield return Wait(() => { foreach (BallPitMonsterFx q in UnityEngine.Object.FindObjectsOfType<BallPitMonsterFx>()) if (q.Under) { fx = q; return true; } return false; }, 8, "monstruo sumergido visto en el cliente");
            bool eyesOff = fx != null; if (fx != null) foreach (Light l in fx.GetComponentsInChildren<Light>()) if (l.enabled) eyesOff = false;
            r.Check(fx != null && fx.Under && eyesOff, "[Parque MP] el cliente ve al monstruo bajo las bolas (sin luz en los ojos)");
            int b0 = BallPitMonsterFx.Bursts;
            Mark("client_bp_seen");
            yield return WaitMark("host_bp_emerged", 60);
            yield return Wait(() => BallPitMonsterFx.Bursts > b0, 5, "estallido en el cliente");
            r.Check(BallPitMonsterFx.Bursts > b0 && fx != null && !fx.Under, "[Parque MP] el cliente ve y oye el estallido de bolas al emerger (" + (BallPitMonsterFx.Bursts - b0) + ")");
            Mark("client_bp_done");
            yield return WaitMark("host_bp_done", 60);
        }

        // ---- v7.3.0: nivel 7 (Talasofobia) en el cliente ----
        {
            yield return Wait(() => WorldReady(gb, ns, 7) && gb.IsLoaded, 240, "nivel 7 cargado (cliente)");
            yield return WaitMark("host_level7", 240);
            OceanWorld w = OceanWorld.Active;
            gm = GameManager.Instance; FirstPersonController pc = gm.player; OceanPlayer op = OceanPlayer.Local;
            r.Check(w != null && op != null && w.boat != null && w.cage != null && w.rack != null && w.fish != null && w.angler != null, "[Mar MP] el cliente tiene el mar con barca, jaula, cascos y criaturas");
            // barca
            Vector3 bp = w.boat.transform.position;
            pc.Teleport(bp + new Vector3(-2.1f, -0.6f, 0f), 90f);
            yield return 0.8f;
            op.debugInteract = true; yield return 0.2f; op.debugInteract = false;
            yield return Wait(() => w.boat.LocalSeat == 0 && pc.IsMounted, 6, "subir a la barca (cliente)");
            r.Check(w.boat.LocalSeat == 0 && pc.IsMounted, "[Mar MP] el cliente sube a la barca (lo confirma el anfitrión)");
            Mark("client_oc_boarded");
            Vector3 cb0 = w.boat.transform.position;
            pc.debugInput = true; pc.debugMove = new Vector2(0f, 1f);
            yield return 5f;
            pc.debugMove = Vector2.zero; pc.debugInput = false;
            float cbd = Vector3.Distance(cb0, w.boat.transform.position);
            r.Check(cbd > 2f && Vector3.Distance(pc.transform.position, w.boat.transform.position) < 2.6f, "[Mar MP] en el cliente la barca avanza y le lleva consigo (" + cbd.ToString("F1") + " m)");
            Mark("client_oc_drove");
            op.debugInteract = true; yield return 0.2f; op.debugInteract = false;
            yield return Wait(() => w.boat.LocalSeat < 0 && !pc.IsMounted, 6, "bajar de la barca (cliente)");
            r.Check(w.boat.LocalSeat < 0 && !pc.IsMounted, "[Mar MP] el cliente baja de la barca");
            Mark("client_oc_left");
            // casco
            pc.Teleport(OceanWorld.HelmetRack + new Vector3(0f, 0.05f, -1.7f), 0f);
            yield return 0.3f; pc.SetPitch(18f); yield return 0.2f;
            op.debugHold = true;
            yield return Wait(() => op.HasHelmet, 6, "casco (cliente)");
            op.debugHold = false; pc.SetPitch(0f);
            r.Check(op.HasHelmet && w.rack.TakenCount == 1 && op.flashlight.IsOn, "[Mar MP] el cliente se pone el casco (concedido por el anfitrión) con la lámpara encendida");
            Mark("client_oc_helmet");
            // bengala
            Inventory inv = Inventory.Instance; inv.Add(ItemType.FlareGun, 1);
            int sl = -1; for (int i = 0; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == ItemType.FlareGun) sl = i;
            pc.SetPitch(-60f);
            bool used = sl >= 0 && inv.UseSlot(sl);
            yield return Wait(() => OceanFlare.All.Exists(f => f != null && f.owner == ns.LocalId), 5, "bengala propia (cliente)");
            pc.SetPitch(0f);
            r.Check(used && OceanFlare.All.Exists(f => f != null && f.owner == ns.LocalId), "[Mar MP] el cliente dispara una bengala (la difunde el anfitrión)");
            Mark("client_oc_flare");
            // nadar en mar abierto: un pez le derriba; el anfitrión le reanima en el agua
            pc.Teleport(new Vector3(-8f, -0.6f, 46f), 0f);
            yield return 1.2f;
            Mark("client_oc_swim");
            yield return Wait(() => ns.LocalDowned, 15, "derribado por un pez (cliente)");
            r.Check(ns.LocalDowned && gm.State == GameManager.GameState.Playing, "[Mar MP] el cliente ve que un pez le ha derribado (sigue la partida)");
            yield return Wait(() => !ns.LocalDowned, 40, "reanimado (cliente)");
            r.Check(!ns.LocalDowned, "[Mar MP] el cliente es reanimado en el agua");
            yield return WaitMark("host_oc_revived", 30);
            // jaula
            pc.Teleport(OceanWorld.CageTop + new Vector3(-0.4f, 0.08f, -0.4f), 270f);
            yield return 1.0f;
            Mark("client_oc_incage");
            yield return Wait(() => w.cage.State == OceanCage.CageState.Descending && pc.IsMounted, 25, "jaula bajando (cliente)");
            r.Check(w.cage.State == OceanCage.CageState.Descending && pc.IsMounted, "[Mar MP] el cliente baja dentro de la jaula");
            yield return Wait(() => w.cage.State == OceanCage.CageState.BottomOpen && !pc.IsMounted, 50, "jaula en el fondo (cliente)");
            yield return 1.0f;
            r.Check(pc.transform.position.y < -40f && pc.IsUnderwater && op.Air01 > 0.99f, "[Mar MP] el cliente llega al fondo con el casco (aire lleno)");
            yield return WaitMark("host_oc_bottom", 60);
            pc.Teleport(OceanWorld.PocketCenter + new Vector3(0.8f, -1.4f, 0f), 0f);
            yield return Wait(() => NetSession.LocalAtExit || GameManager.Instance.State == GameManager.GameState.Won, 4, "cliente en la salida");
            r.Check(NetSession.LocalAtExit || GameManager.Instance.State == GameManager.GameState.Won, "[Mar MP] el cliente en la poza de la gruta cuenta como en la salida");
            Mark("client_oc_atexit");
            yield return Wait(() => gm == null || gm.State != GameManager.GameState.Playing || gb.CurrentLevel == 8, 30, "nivel superado (cliente)");
            r.Check(gm == null || gm.State == GameManager.GameState.LevelComplete || gb.CurrentLevel == 8, "[Mar MP] el cliente pasa al Nivel 114 tras la poza de la gruta");
            Mark("client_oc_done");
            yield return WaitMark("host_oc_done", 60);
        }

        // ---- v7.7.0: nivel 8 (desierto) en el cliente ----
        {
            var Inv = System.Globalization.CultureInfo.InvariantCulture;
            yield return Wait(() => WorldReady(gb, ns, 8) && gb.IsLoaded, 240, "nivel 8 cargado (cliente)");
            yield return WaitMark("host_level8", 240);
            DesertWorld dw = DesertWorld.Active; DesertSky sky = DesertSky.Active; DesertFx fx = DesertFx.Active;
            gm = GameManager.Instance; FirstPersonController pc = gm.player;
            DesertFx.HazardsOn = false;
            r.Check(dw != null && sky != null && fx != null && DesertItems.Placed > 0 && MonsterAI.Instances.Count == 0, "[Desierto MP] el cliente tiene el desierto (cielo, clima y " + DesertItems.Placed + " objetos), sin entidades");
            yield return WaitMark("host_dclock", 60);
            string[] hc = File.ReadAllText(MpTest.Dir + "/host_dclock.txt").Trim().Split(' ');
            float hClock = float.Parse(hc[0], Inv); long t0 = long.Parse(hc[1]);
            float expect = hClock + (float)((DateTime.UtcNow.Ticks - t0) / 1e7);
            float diff = Mathf.Abs(sky.Clock - expect);
            r.Check(diff < 1.5f && (int)sky.Current == int.Parse(hc[2]) && ns.DesertSnapshots > 10, "[Desierto MP] mismo reloj del desierto que el anfitrión: hora y clima iguales (diferencia " + diff.ToString("F2") + " s, clima " + sky.Current + ", " + ns.DesertSnapshots + " bloques recibidos)");
            {   // v7.9.0: sonidos de la noche: misma semilla → mismos sucesos (el reloj ya es el del anfitrión)
                DesertNight dn = DesertNight.Active; int hs = hc.Length > 4 ? int.Parse(hc[4]) : -2;
                int slot = Mathf.FloorToInt(sky.Clock / DesertNight.Slot); bool same = dn != null && dn.Seed == hs;
                for (int k = 0; k < 40 && same; k++) { DesertNight.Ev a = DesertNight.EventAt(dn.Seed, slot + k), b = DesertNight.EventAt(hs, slot + k); same = a.on == b.on && a.kind == b.kind && a.offset == b.offset && a.azimuth == b.azimuth; }
                r.Check(same, "[Desierto MP] sonidos de la noche: los mismos sucesos, a la vez y desde la misma dirección que el anfitrión (semilla " + hs + ")");
            }
            {   // v7.7.1: en el desierto la cordura no baja tampoco en multijugador (linterna apagada)
                Sanity sn = Sanity.Instance; Flashlight fl8 = pc.GetComponentInChildren<Flashlight>(); if (fl8 != null) fl8.SetOn(false);
                if (sn != null) sn.value = 70f;
                yield return 3.0f;
                r.Check(sn != null && sn.value >= 70f - 0.001f && sn.DrainRate >= 0f, "[Desierto MP] la cordura del cliente no baja con la linterna apagada (" + (sn != null ? sn.value.ToString("F1") : "-") + ")");
                if (sn != null) sn.value = 100f;
            }
            Mark("client_dclock");
            yield return WaitMark("host_dpick_done", 60);
            int pid = int.Parse(File.ReadAllText(MpTest.Dir + "/host_dpick.txt").Trim());
            yield return Wait(() => !NetSession.PickupExists(pid), 10, "objeto del desierto retirado (cliente)");
            r.Check(pid >= 0 && !NetSession.PickupExists(pid), "[Desierto MP] el objeto que coge el anfitrión desaparece también en el cliente");
            {   // v7.8.0: todoterreno: el cliente sube de copiloto, viaja y baja
                yield return WaitMark("host_car_in", 60);
                DesertCars dc = DesertCars.Active; DesertCar car = dc != null ? dc.Drivable : null;
                if (car != null)
                {
                    yield return 1.0f;
                    pc.Teleport(car.transform.TransformPoint(car.DoorLocal(1)) + Vector3.up * 0.1f, 0f); yield return 0.6f;
                    DesertCars.DebugInteract = true;
                    yield return Wait(() => car.SeatOf(ns.LocalId) > 0 && pc.IsMounted, 12, "cliente de copiloto");
                    r.Check(car.SeatOf(ns.LocalId) > 0 && pc.IsMounted && car.DriverId >= 0 && car.DriverId != ns.LocalId, "[Coche MP] el cliente sube (plaza " + car.SeatOf(ns.LocalId) + ") y ve al anfitrión al volante");
                    yield return WaitMark("host_car_drove", 60);
                    yield return 1.5f;
                    string[] hcar = File.ReadAllText(MpTest.Dir + "/host_car.txt").Trim().Split(' ');
                    Vector3 hl = new Vector3(float.Parse(hcar[0], Inv), float.Parse(hcar[1], Inv), float.Parse(hcar[2], Inv));
                    System.Func<float> carOff = () => { Vector3 q = dw.levelRoot.InverseTransformPoint(car.transform.position); return new Vector3(DesertHeight.Wrap(hl.x - q.x), hl.y - q.y, DesertHeight.Wrap(hl.z - q.z)).magnitude; };
                    yield return Wait(() => carOff() < 1.0f, 8, "el coche del cliente llega a donde lo dejó el anfitrión");   // por relé las instantáneas pueden llegar con retraso
                    float dd = carOff();
                    int ms = car.SeatOf(ns.LocalId);
                    float seatD = ms >= 0 ? Vector3.Distance(pc.transform.position, car.transform.TransformPoint(car.SeatLocal(ms))) : 99f;
                    {   // v7.9.0: el conductor remoto con las dos manos en el volante, linterna guardada; el claxon llega al cliente
                        RemotePlayer drv = null; foreach (RemotePlayer rp in UnityEngine.Object.FindObjectsOfType<RemotePlayer>()) if (rp.playerId == car.DriverId) drv = rp;
                        yield return Wait(() => drv != null && drv.OnWheel, 5, "conductor remoto con las manos en el volante");
                        r.Check(drv != null && drv.OnWheel && drv.WheelErr < 0.08f && !drv.LampShown, "[Coche MP] el cliente ve al anfitrión con las dos manos en el volante (error " + (drv != null ? drv.WheelErr.ToString("F3") : "-") + " m) y sin linterna");
                        r.Check(car.HornEvents >= 1 && !car.HornOn, "[Coche MP] el cliente oye el claxon del anfitrión (" + car.HornEvents + " toques, 3D)");
                        Flashlight cfl = pc.GetComponentInChildren<Flashlight>();
                        r.Check(cfl != null && !cfl.IsOn, "[Coche MP] de copiloto la linterna del cliente va guardada");
                    }
                    r.Check(dd < 1.0f && seatD < 0.35f && dc.NetReads > 10, "[Coche MP] el cliente ve el coche donde lo deja el anfitrión (" + dd.ToString("F2") + " m) y viaja en su asiento (" + seatD.ToString("F2") + " m, " + dc.NetReads + " bloques)");
                    DesertCars.DebugInteract = true;
                    yield return Wait(() => !pc.IsMounted && car.SeatOf(ns.LocalId) < 0, 10, "cliente baja");
                    yield return 0.3f;
                    Vector3 off = car.transform.InverseTransformPoint(pc.transform.position);
                    r.Check(!pc.IsMounted && car.SeatOf(ns.LocalId) < 0 && (Mathf.Abs(off.x) > 1.0f || Mathf.Abs(off.z) > 2.6f), "[Coche MP] el cliente baja junto al coche, fuera de la carrocería (" + off.x.ToString("F1") + ", " + off.z.ToString("F1") + ")");
                    Mark("client_car_out");
                    yield return WaitMark("host_car_done", 60);
                }
                else { r.Check(false, "[Coche MP] el cliente no tiene todoterreno"); Mark("client_car_out"); }
            }
            // arenas movedizas
            DesertFx.HazardsOn = true; DesertFx.VoidOn = false;
            Vector2 q = dw.plan.quick[0].c;
            pc.Teleport(dw.levelRoot.TransformPoint(new Vector3(q.x, DesertHeight.H(q.x, q.y) + 0.2f, q.y)), 0f);
            int s0 = fx.QuickSinks;
            yield return 0.8f;
            Mark("client_dquick_start");
            yield return Wait(() => fx.QuickSinks > s0 && !fx.Busy, 20, "arenas movedizas (cliente)");
            r.Check(fx.QuickSinks > s0 && !fx.Busy && gm.State == GameManager.GameState.Playing, "[Desierto MP] el cliente se hunde en las arenas movedizas y reaparece en otra zona, vivo");
            DesertFx.HazardsOn = false;
            yield return 0.5f;
            Mark("client_dquick_done");
            // salida en grupo
            Vector3 door = dw.ExitTransform.position, st = dw.levelRoot.TransformPoint(dw.ExitLocal);
            Vector3 side = Vector3.Cross(Vector3.up, st - door).normalized;
            pc.Teleport(st - side * 0.45f + Vector3.up * 0.1f, Mathf.Atan2(door.x - st.x, door.z - st.z) * Mathf.Rad2Deg);
            yield return Wait(() => NetSession.LocalAtExit || gm.State == GameManager.GameState.Won, 4, "cliente en la puerta de piedra");
            r.Check(NetSession.LocalAtExit || gm.State == GameManager.GameState.Won, "[Desierto MP] el cliente en la puerta de piedra cuenta como en la salida");
            Mark("client_d_atexit");
            yield return Wait(() => gm == null || gm.State != GameManager.GameState.Playing || gb.CurrentLevel == 9, 30, "nivel superado (cliente, desierto)");
            r.Check(gm == null || gm.State == GameManager.GameState.LevelComplete || gb.CurrentLevel == 9, "[Desierto MP] el cliente pasa al Nivel 7460 tras la puerta de piedra");
            Mark("client_d_done");
            yield return WaitMark("host_d_done", 60);
            DesertFx.HazardsOn = true;
        }

        // ---- Nivel 7460 en el cliente ----
        {
            yield return Wait(() => WorldReady(gb, ns, 9) && gb.IsLoaded, 240, "nivel 9 cargado (cliente)");
            yield return WaitMark("host_level9", 240);
            gm = GameManager.Instance; FirstPersonController pc = gm.player;
            CorridorWorld cw = CorridorWorld.Active;
            string[] hc = File.ReadAllText(MpTest.Dir + "/host_corridor.txt").Split(';');
            r.Check(cw != null && hc.Length >= 5 && gb.Generator.UsedSeed == int.Parse(hc[0]) && cw.plan.exitRow == int.Parse(hc[1]) && cw.plan.exitSide == int.Parse(hc[2]) && cw.RoomCount == int.Parse(hc[4]),
                "[Pasillo MP] el cliente tiene el mismo pasillo (semilla, puerta de salida en el módulo " + (cw != null ? cw.plan.exitRow : -1) + ", " + (cw != null ? cw.RoomCount : 0) + " habitaciones)");
            r.Check(MonsterAI.Instances.Count == 0 && CorridorEvents.Active != null && ExitLock.Active != null, "[Pasillo MP] el cliente: sin entidades, con sus propios ruidos a la espalda y la misma cerradura");
            r.Check(gm.healthFill != null && gm.healthFill.gameObject.activeInHierarchy, "[Pasillo MP] VIDA en el HUD del cliente");
            {   // v7.10.1: la luz roja empieza y acaba a la vez que en el anfitrión; la cordura del cliente no baja
                CorridorRed cr = CorridorRed.Active; var Inv = System.Globalization.CultureInfo.InvariantCulture;
                Sanity sn = Sanity.Instance; Flashlight fl = pc.GetComponentInChildren<Flashlight>();
                if (fl != null) fl.SetOn(false);
                if (sn != null) sn.value = 70f;
                float sMin = 100f;
                yield return WaitMark("host_c_red_on", 120);
                string[] hr = File.ReadAllText(MpTest.Dir + "/host_c_red.txt").Trim().Split(' ');
                float hs = float.Parse(hr[0], Inv), hd = float.Parse(hr[1], Inv);
                float tw = 0f; while (tw < 10f && !(cr != null && cr.RedOn)) { yield return 0.1f; tw += 0.1f; if (sn != null) sMin = Mathf.Min(sMin, sn.value); }
                r.Check(cr != null && cr.RedOn && cr.Schedule.Count > 0 && Mathf.Abs(cr.Schedule[0].start - hs) < 1e-3f && Mathf.Abs(cr.Schedule[0].dur - hd) < 1e-3f && cr.NetClocks > 0,
                    "[Pasillo MP] el cliente ve la luz roja a la vez (mismo calendario " + hs.ToString("F1") + " s + " + hd.ToString("F1") + " s; " + (cr != null ? cr.NetClocks : 0) + " relojes del anfitrión)");
                Mark("client_c_red_seen");
                yield return WaitMark("host_c_red_off", 60);
                tw = 0f; while (tw < 10f && cr != null && cr.RedOn) { yield return 0.1f; tw += 0.1f; if (sn != null) sMin = Mathf.Min(sMin, sn.value); }
                r.Check(cr != null && !cr.RedOn, "[Pasillo MP] la luz roja acaba también en el cliente");
                r.Check(sn != null && Sanity.NoDrainLevel && sMin >= 70f - 0.001f && sn.value >= 70f - 0.001f, "[Pasillo MP] la cordura del cliente no baja (linterna apagada, rojo; mín " + sMin.ToString("F2") + ")");
                if (fl != null) fl.SetOn(true);
                Mark("client_c_red_off");
            }
            pc.Teleport(cw.levelRoot.TransformPoint(new Vector3(cw.CenterX, 0.1f, 2f)), 0f);
            Mark("client_c_ready");
            yield return WaitMark("host_c_door", 60);
            {
                string[] dd = File.ReadAllText(MpTest.Dir + "/host_c_door.txt").Split(';');
                Vector3 dp = cw.DoorWorld(int.Parse(dd[0]), int.Parse(dd[1]), 0.6f); dp.y = 0f;
                HotelDoorLeaf leaf = null; float best = 9f;
                foreach (HotelDoorLeaf l in cw.OpenableDoors) { Vector3 c = l.center; c.y = 0f; float d = Vector3.Distance(c, dp); if (d < best) { best = d; leaf = l; } }
                yield return Wait(() => leaf != null && leaf.IsOpen, 8, "la puerta de la habitación se abre al llegar el anfitrión (cliente)");
                r.Check(leaf != null && leaf.IsOpen, "[Pasillo MP] el cliente ve abrirse la puerta de la habitación cuando llega el anfitrión");
                Mark("client_c_door_seen");
            }
            Vector3 st = cw.DoorWorld(cw.plan.exitSide, cw.plan.exitRow, 0.75f);
            Vector3 along = cw.levelRoot.TransformDirection(Vector3.forward);
            pc.Teleport(st - along * 0.35f + Vector3.up * 0.1f, cw.plan.exitSide == CorridorPlan.Left ? 270f : 90f);
            yield return Wait(() => NetSession.LocalAtExit || gm.State == GameManager.GameState.Won, 6, "cliente en la salida del pasillo");
            r.Check(NetSession.LocalAtExit || gm.State == GameManager.GameState.Won, "[Pasillo MP] el cliente en la puerta de salida cuenta como en la salida");
            Mark("client_c_atexit");
            yield return Wait(() => gm.State == GameManager.GameState.Won, 30, "victoria (cliente)");
            r.Check(ExitLock.Active == null || ExitLock.Active.Unlocked, "[Pasillo MP] el cliente ve la salida abierta");
            r.Check(gm.State == GameManager.GameState.Won, "[Pasillo MP] el cliente ve la victoria final");
            Mark("client_c_done");
            yield return WaitMark("host_c_done", 60);
        }

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
