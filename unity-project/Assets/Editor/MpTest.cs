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
    // v7.0.4: suelo junto a la puerta (en el aparcamiento la salida está en una planta más baja)
    private static float ExitFloorY(BackroomsGenerator g) { return g.Garage != null && g.ExitTransform != null && g.ExitTransform.parent != null ? g.ExitTransform.parent.position.y + 0.1f : g.PlayerStart.y; }

    private static IEnumerator RunHost()
    {
        SmokeTest.Report r = report;
        ExitLock.AutoKeyForTests = true;          // v7.0.4: la llave se prueba aparte (bloque «Llaves»)
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
            string[] steps = { "borde_norte", "borde_sur", "borde_oeste", "borde_este", "escalera", "rescate" };
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

        // ---- v6.8.1: nivel 2, salir de la piscina fija (borde 4 lados, escalera, rescate) ----
        {
            BackroomsGenerator gen1 = gb.Generator; int epoch1 = ns.Epoch; Vector3 ex1 = gen1.ExitPosition;
            GameManager.Instance.player.Teleport(new Vector3(ex1.x, ExitFloorY(gen1), ex1.z), 0f);
            yield return Wait(() => WorldReady(gb, ns, 2) && ns.Epoch > epoch1, 240, "nivel 2 cargado (cliente)");
            yield return WaitMark("host_level2", 240);
            gm = GameManager.Instance; FirstPersonController pw = gm.player;
            r.Check(gb.Generator.PoolFixed && PoolExit.Active, "[Piscina MP] la sala de piscina fija existe en el cliente");
            Rect pr = PoolExit.PoolXZ; Vector3 pc0 = new Vector3(pr.center.x, PoolExit.DeckY - 1.35f, pr.center.y);
            Vector3[] sides = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            string[] steps = { "borde_norte", "borde_sur", "borde_oeste", "borde_este", "escalera", "rescate" };
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
                else if (si == 4) { start = PoolExit.LadderPos - PoolExit.LadderOut * 0.5f + Vector3.up * 0.05f; yaw = Quaternion.LookRotation(PoolExit.LadderOut).eulerAngles.y; }
                else { start = pc0; yaw = 0f; }
                pw.Teleport(start, yaw);
                yield return 1.0f;
                r.Check(PoolExit.InPool(pw.transform.position), "[Piscina MP] " + steps[si] + ": el cliente está dentro del vaso");
                Mark("client_pool_" + steps[si] + "_in");
                yield return 1.5f;
                int c0 = pw.ClimbOutCount + pw.LadderClimbCount + pw.PoolRescueCount;
                pw.debugInput = true;
                if (si == 5) pw.debugJump = true; else pw.debugMove = new Vector2(0f, 1f);
                float tt = 0f;
                while (tt < 8f && (PoolExit.InPool(pw.transform.position) || pw.IsClimbingLadder)) { yield return 0.1f; tt += 0.1f; }
                yield return 0.3f;
                pw.debugMove = Vector2.zero; pw.debugJump = false; pw.debugInput = false;
                int c1 = pw.ClimbOutCount + pw.LadderClimbCount + pw.PoolRescueCount;
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
