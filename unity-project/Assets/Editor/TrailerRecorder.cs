using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Graba el metraje REAL del tráiler (play mode, gráficos, Time.captureFramerate = 30) como secuencias JPG en
// /workspace/trailer/frames/<plano>/00000.jpg. El montaje y el audio se hacen fuera (ffmpeg).
//   xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath <p> -executeMethod TrailerRecorder.Export -force-glcore
[InitializeOnLoad]
public static class TrailerRecorder
{
    public static int W = 1920, H = 1080;
    const string Root = "/workspace/trailer/frames/";
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static double waitUntil;
    static int lastFrame = -1;
    static string shot;            // plano que se está grabando (null = no se graba)
    static int shotFrame;
    static Camera uiCam;
    static bool started;

    static TrailerRecorder()
    {
        if (SessionState.GetString("smoke_phase", "") != "trailer") return;
        EditorApplication.update += Tick;
    }

    public static void Export()
    {
        string res = System.Environment.GetEnvironmentVariable("TRAILER_RES");
        Directory.CreateDirectory(Root);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "trailer");
        SessionState.SetBool("intro_real", true);
        SessionState.SetString("trailer_res", res ?? "");
        SessionState.SetString("trailer_only", System.Environment.GetEnvironmentVariable("TRAILER_ONLY") ?? "");
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (!started)
        {
            started = true;
            if (SessionState.GetString("trailer_res", "") == "720") { W = 1280; H = 720; }
            Time.captureFramerate = 30;
            IntroSequence.FixedStep = 1f / 30f;
            string only = SessionState.GetString("trailer_only", ""); stack.Push(only == "fix" ? RunFix() : only == "inv" ? RunInv() : Run());
        }
        if (Time.frameCount == lastFrame) return;
        if (shot == null && EditorApplication.timeSinceStartup < waitUntil) return;
        lastFrame = Time.frameCount;
        try
        {
            if (shot != null) { Capture(Root + shot + "/" + shotFrame.ToString("00000") + ".jpg"); shotFrame++; }
            while (true)
            {
                if (stack.Count == 0) { Finish(); return; }
                IEnumerator top = stack.Peek();
                if (!top.MoveNext()) { stack.Pop(); continue; }
                object c = top.Current;
                if (c is IEnumerator) { stack.Push((IEnumerator)c); continue; }
                if (c is float) waitUntil = EditorApplication.timeSinceStartup + (float)c;
                break;
            }
        }
        catch (System.Exception e) { Debug.LogError("[Trailer] " + e); Finish(); }
    }

    static void Finish()
    {
        SessionState.SetString("smoke_phase", "");
        SessionState.SetBool("intro_real", false);
        EditorApplication.update -= Tick;
        Time.captureFramerate = 0;
        GameManager.HideControlsHint = false; InventoryUI.ForceFit = 0f;
        Debug.Log("[Trailer] fin");
        EditorApplication.Exit(0);
    }

    static void Begin(string name) { shot = name; shotFrame = 0; Directory.CreateDirectory(Root + name); Debug.Log("[Trailer] plano " + name); }
    static void End() { Debug.Log("[Trailer] fin plano " + shot + " (" + shotFrame + " fotogramas)"); shot = null; }

    static void SetupUi()
    {
        if (uiCam == null)
        {
            GameObject ugo = new GameObject("TrailerUiCam");
            uiCam = ugo.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.Depth; uiCam.cullingMask = 1 << 31; uiCam.orthographic = true; uiCam.depth = 100; uiCam.enabled = false;
            Object.DontDestroyOnLoad(ugo);
        }
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = uiCam; c.planeDistance = 1f;
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        }
    }

    static void Capture(string path)
    {
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt; GL.Clear(true, true, Color.black); RenderTexture.active = prev;
        Camera game = Camera.main;
        if (game != null) { RenderTexture old = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = old; }
        SetupUi(); Canvas.ForceUpdateCanvases();
        uiCam.targetTexture = rt; uiCam.Render(); uiCam.targetTexture = null;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToJPG(93));
        Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt);
    }

    // ---------- utilidades ----------
    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    static IEnumerator LoadLevel(int lv, bool elevator)
    {
        ElevatorCabin.InstantForTests = !elevator;
        GameBootstrap gb = GameBootstrap.Instance;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(lv);
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu)) yield return null;
    }

    static void StopMonsters(GameManager gm)
    {
        foreach (MonsterAI m in gm.monsters) { if (m == null) continue; m.Stop(); }
        gm.lostRestartSeconds = 999f;
    }

    static void Place(GameManager gm, Vector3 pos, float yaw, float pitch)
    {
        FirstPersonController f = gm.player;
        CharacterController cc = f.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        f.transform.position = pos; f.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cc != null) cc.enabled = true;
        f.SetPitch(pitch);
    }

    // dirección horizontal con más espacio libre desde el jugador (para pasillos largos)
    static float BestDir(GameManager gm, out float dist, float minD = 6f)
    {
        Vector3 eye = gm.player.transform.position + Vector3.up * 1.2f;
        float best = gm.player.transform.eulerAngles.y; dist = 0f;
        for (int a = 0; a < 360; a += 10)
        {
            Vector3 d = Quaternion.Euler(0, a, 0) * Vector3.forward;
            RaycastHit h; float dd = Physics.SphereCast(eye, 0.35f, d, out h, 40f) ? h.distance : 40f;
            if (dd > dist) { dist = dd; best = a; }
        }
        return best;
    }

    static IEnumerator Walk(GameManager gm, string name, int frames, float yaw, float pitch0, float pitch1, float yawDrift, float speedY = 0.6f)
    {
        FirstPersonController f = gm.player;
        f.debugInput = true; f.InputEnabled = true;
        Begin(name);
        for (int i = 0; i < frames; i++)
        {
            float u = i / (float)Mathf.Max(1, frames - 1);
            f.debugMove = new Vector2(0f, speedY);
            f.transform.rotation = Quaternion.Euler(0f, yaw + yawDrift * Mathf.SmoothStep(0, 1, u), 0f);
            f.SetPitch(Mathf.Lerp(pitch0, pitch1, Mathf.SmoothStep(0, 1, u)));
            yield return null;
        }
        f.debugMove = Vector2.zero;
        End();
    }

    static IEnumerator Chase(GameManager gm, string name, int frames, float speed)
    {
        float dist; float yaw = BestDir(gm, out dist);
        Place(gm, gm.player.transform.position, yaw, 2f);
        yield return Frames(3);
        MonsterAI m = gm.monsters[0];
        UnityEngine.AI.NavMeshAgent ag = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (ag != null) ag.enabled = false;
        m.enabled = false;
        Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
        Vector3 feet = gm.player.transform.position; RaycastHit fh;
        if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out fh, 5f)) feet = fh.point;
        float start = Mathf.Clamp(dist - 1.2f, 5f, speed * frames / 30f + 2.2f);
        Vector3 p = feet + fwd * start;
        m.transform.position = p; m.transform.rotation = Quaternion.LookRotation(-fwd);
        MonsterVisual v = m.visual;
        if (v != null) { v.SetLocomotion(MonsterAI.State.Chase, speed); v.OnChaseStart(); }
        Debug.Log("[Trailer] " + name + " monstruo " + (v != null ? v.profile.id : "?") + " a " + start.ToString("F1") + " m (libre " + dist.ToString("F1") + ")");
        Begin(name);
        for (int i = 0; i < frames; i++)
        {
            p += -fwd * speed / 30f;
            if (Vector3.Distance(new Vector3(p.x, feet.y, p.z), feet) < 2.0f) p = feet + fwd * 2.0f;
            m.transform.position = p;
            if (v != null) v.SetLocomotion(MonsterAI.State.Chase, speed);
            yield return null;
        }
        End();
        m.transform.position = feet + fwd * 60f;
    }

    // ---------- guion ----------
    static IEnumerator Run()
    {
        GameSettings.ResetDefaults();
        GameManager.HideControlsHint = true; InventoryUI.ForceFit = 1f;   // sin texto de controles en pantalla
        // intro real del juego (cinta, aviso, PIXEL, título)
        while (IntroSequence.Current == null) yield return null;
        Begin("intro");
        while (IntroSequence.Active) yield return null;
        yield return Frames(20);
        End();
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) yield return 0.3f;
        yield return 1f;
        GameBootstrap.Instance.useRandomSeed = false;

        // ---- Nivel 0: ascensor completo ----
        GameBootstrap gb = GameBootstrap.Instance;
        int ld = gb.LoadCount;
        ElevatorCabin.InstantForTests = false;
        UIManager.Instance.StartGame(0);
        while (!(gb.LoadCount > ld && GameManager.Instance != null && !gb.InMenu && ElevatorCabin.Current != null)) yield return null;
        GameManager gm = GameManager.Instance;
        StopMonsters(gm);
        Begin("elevator");
        int guard = 0;
        while (ElevatorCabin.Current != null && ElevatorCabin.Current.T < ElevatorCabin.TDoors1 + 0.3f && guard++ < 600) yield return null;
        End();
        StopMonsters(gm);
        ElevatorCabin cab = ElevatorCabin.Current;
        float yaw0 = cab != null ? Quaternion.LookRotation(cab.Forward).eulerAngles.y : gm.player.transform.eulerAngles.y;
        yield return Walk(gm, "l0_exit", 150, yaw0, 2f, 0f, 0f, 0.55f);
        {
            float d; float y = BestDir(gm, out d);
            yield return Walk(gm, "l0_corridor", 180, y, 0f, 3f, 12f, 0.5f);
        }
        // linterna en mano: mirar la pared con la linterna
        {
            float d; float y = BestDir(gm, out d);
            yield return Walk(gm, "l0_flashlight", 90, y + 70f, 10f, 18f, -40f, 0.25f);
        }
        // inventario
        {
            Inventory inv = Inventory.Instance;
            inv.Add(ItemType.Battery, 3); inv.Add(ItemType.AlmondWater, 2); inv.Add(ItemType.Glowstick, 3); inv.Add(ItemType.VhsTape, 1);
            inv.AddNote(LoreNotes.IdFor(0, 0));
            UIManager ui = UIManager.Instance;
            ui.ToggleInventory();
            Begin("l0_inventory");
            for (int i = 0; i < 120; i++) { if (i == 40) ui.InventoryUi.Select(1); if (i == 80) ui.InventoryUi.Select(2); yield return null; }
            End();
            ui.ToggleInventory();
            yield return Frames(20);
        }
        // cooperativo: dos jugadores hazmat simulados
        {
            float d; float y = BestDir(gm, out d);
            Place(gm, gm.player.transform.position, y, 4f);
            yield return Frames(3);
            Vector3 fwd = Quaternion.Euler(0, y, 0) * Vector3.forward, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 feet = gm.player.transform.position; RaycastHit fh;
            if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out fh, 5f)) feet = fh.point;
            RemotePlayer a = RemotePlayer.Create(null, 2, "Marta", StepKind.Carpet);
            RemotePlayer b = RemotePlayer.Create(null, 3, "Luis", StepKind.Carpet);
            Vector3 pa = feet + fwd * Mathf.Min(d - 0.5f, 7f) + right * 0.6f, pb = feet + fwd * Mathf.Min(d - 0.5f, 9f) - right * 0.6f;
            float yawBack = y + 180f;
            for (int i = 0; i < 12; i++) { a.Push(pa, yawBack, 0f, 0f, RemotePlayer.FlagFlash); b.Push(pb, yawBack, 0f, 0f, RemotePlayer.FlagFlash); yield return null; }
            Begin("l0_coop");
            for (int i = 0; i < 135; i++)
            {
                if (Vector3.Distance(pa, feet) > 2.2f) pa += -fwd * 1.5f / 30f;
                if (Vector3.Distance(pb, feet) > 3.0f) pb += -fwd * 1.5f / 30f;
                a.Push(pa, yawBack + Mathf.Sin(i * 0.05f) * 10f, 0f, 1.5f, RemotePlayer.FlagFlash);
                b.Push(pb, yawBack - 15f, 5f, 1.5f, i > 70 ? RemotePlayer.FlagFlash | RemotePlayer.FlagCrouch : RemotePlayer.FlagFlash);
                yield return null;
            }
            End();
            Object.Destroy(a.gameObject); Object.Destroy(b.gameObject);
            yield return Frames(5);
        }
        yield return Chase(gm, "l0_monster", 75, 4.2f);
        // susto (El Alto)
        {
            MonsterAI m = gm.monsters[0];
            m.enabled = true;
            UnityEngine.AI.NavMeshAgent ag = m.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag != null) ag.enabled = true;
            Vector3 pp = gm.player.transform.position;
            m.transform.position = pp + gm.player.transform.forward * 2.5f;
            gm.lostRestartSeconds = 60f;
            m.ForceCatch();
            Begin("l0_screamer");
            for (int i = 0; i < 105; i++) yield return null;
            End();
            gm.Restart();
            yield return 1f;
        }

        // ---- Niveles 1-4 ----
        string[] names = { "", "l1", "l2", "l3", "l4" };
        for (int lv = 1; lv < LevelConfig.Count; lv++)
        {
            yield return LoadLevel(lv, false);
            yield return 1.0f;
            gm = GameManager.Instance;
            StopMonsters(gm);
            ElevatorCabin c = ElevatorCabin.Current;
            if (c != null) Place(gm, c.ExitPoint + c.Forward * 1.5f, Quaternion.LookRotation(c.Forward).eulerAngles.y, 0f);
            yield return Frames(10);
            float d; float y = BestDir(gm, out d);
            yield return Walk(gm, names[lv] + "_walk", 120, y, 2f, 6f, lv % 2 == 0 ? 18f : -18f, 0.5f);
            if (lv == 2 && gm.generator != null && gm.generator.PoolFixed) yield return PoolShot(gm);
            yield return Chase(gm, names[lv] + "_monster", 70, lv == 1 ? 3.6f : 4.4f);
        }
    }

    // v6.8: plano de la sala de piscina fija horneada (agua, cáusticas, ventanas), sin linterna, travelling por la playa
    static IEnumerator PoolShot(GameManager gm)
    {
        BackroomsGenerator g = gm.generator; Rect pr = g.PoolRectLocal; Transform gt = g.transform; float deck = BackroomsGenerator.PoolDeckY;
        FirstPersonController f = gm.player; f.debugInput = true; f.debugMove = Vector2.zero;
        List<Behaviour> off = new List<Behaviour>();
        foreach (Light l in f.GetComponentsInChildren<Light>(true)) if (l.enabled) { l.enabled = false; off.Add(l); }
        List<Renderer> roff = new List<Renderer>();
        Camera cam = Camera.main;
        if (cam != null) foreach (Renderer r in cam.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; roff.Add(r); }
        Vector3 a = gt.TransformPoint(new Vector3(pr.xMin - 1.7f, deck, pr.yMin - 1.0f));
        Vector3 b = gt.TransformPoint(new Vector3(pr.center.x - 1.0f, deck, pr.yMin - 1.4f));
        Vector3 look = gt.TransformPoint(new Vector3(pr.xMax - 1.0f, deck - 0.4f, pr.center.y + 1.5f));
        Place(gm, a, Quaternion.LookRotation(new Vector3(look.x - a.x, 0f, look.z - a.z)).eulerAngles.y, 14f);
        yield return 1.5f;
        Begin("l2_pool");
        for (int i = 0; i < 150; i++)
        {
            float u = Mathf.SmoothStep(0f, 1f, i / 149f);
            Vector3 p = Vector3.Lerp(a, b, u);
            Vector3 dir = look - p; dir.y = 0f;
            Place(gm, p, Quaternion.LookRotation(dir).eulerAngles.y + Mathf.Lerp(-8f, 10f, u), Mathf.Lerp(16f, 22f, u));
            yield return null;
        }
        End();
        foreach (Behaviour l in off) if (l != null) l.enabled = true;
        foreach (Renderer r in roff) if (r != null) r.enabled = true;
        f.debugInput = false;
    }

    // Solo el plano del inventario (TRAILER_ONLY=inv)
    static IEnumerator RunInv()
    {
        GameSettings.ResetDefaults();
        GameManager.HideControlsHint = true; InventoryUI.ForceFit = 1f;
        while (IntroSequence.Current == null) yield return null;
        while (IntroSequence.Active) yield return null;
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) yield return 0.3f;
        yield return 1f;
        GameBootstrap.Instance.useRandomSeed = false;
        yield return LoadLevel(0, false);
        yield return 1f;
        GameManager gm = GameManager.Instance;
        StopMonsters(gm);
        ElevatorCabin c = ElevatorCabin.Current;
        if (c != null) Place(gm, c.ExitPoint + c.Forward * 3.0f, Quaternion.LookRotation(c.Forward).eulerAngles.y, 0f);
        yield return Frames(10);
        Inventory inv = Inventory.Instance;
        inv.Add(ItemType.Battery, 3); inv.Add(ItemType.AlmondWater, 2); inv.Add(ItemType.Glowstick, 3); inv.Add(ItemType.VhsTape, 1);
        inv.AddNote(LoreNotes.IdFor(0, 0));
        if (gm.flashlight != null) gm.flashlight.SetBattery(83f);
        UIManager ui = UIManager.Instance;
        ui.ToggleInventory();
        Begin("l0_inventory");
        for (int i = 0; i < 120; i++) { if (i == 40) ui.InventoryUi.Select(1); if (i == 80) ui.InventoryUi.Select(2); yield return null; }
        End();
    }

    // 2ª pasada: intro a tiempo real (paso fijo) y susto con el tiempo de la secuencia fijado por fotograma
    static IEnumerator RunFix()
    {
        GameSettings.ResetDefaults();
        GameManager.HideControlsHint = true; InventoryUI.ForceFit = 1f;
        while (IntroSequence.Current == null) yield return null;
        Begin("intro");
        while (IntroSequence.Active) yield return null;
        yield return Frames(20);
        End();
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) yield return 0.3f;
        yield return 1f;
        GameBootstrap.Instance.useRandomSeed = false;
        yield return LoadLevel(0, false);
        yield return 1f;
        GameManager gm = GameManager.Instance;
        StopMonsters(gm);
        ElevatorCabin c = ElevatorCabin.Current;
        if (c != null) Place(gm, c.ExitPoint + c.Forward * 2.0f, Quaternion.LookRotation(c.Forward).eulerAngles.y, 0f);
        yield return Frames(5);
        float d; float y = BestDir(gm, out d);
        Place(gm, gm.player.transform.position, y, 0f);
        yield return Frames(5);
        MonsterAI m = gm.monsters[0];
        m.transform.position = gm.player.transform.position + gm.player.transform.forward * 2.5f;
        gm.lostRestartSeconds = 60f;
        m.ForceCatch();
        yield return null;
        Screamer sc = gm.screamer;
        Begin("l0_screamer");
        for (int i = 0; i < 112; i++) { if (sc != null) sc.PreviewSeek = i / 30f; yield return null; }
        End();
    }
}
