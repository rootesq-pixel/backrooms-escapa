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
            string only = SessionState.GetString("trailer_only", ""); stack.Push(only == "fix" ? RunFix() : only == "inv" ? RunInv() : only == "field" ? RunField() : Run());
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
        string[] names = { "", "l1", "l2", "l3", "l4", "l5" };
        for (int lv = 1; lv < LevelConfig.Count; lv++)
        {
            MimicDisguise.ForceForTests = lv == 5 ? 1 : 0;   // tráiler: imitador solo en el campo
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
            if (lv == 5) { yield return FieldShots(gm); continue; }
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

    // v7.0: Campo infinito (hierba al viento, casas señuelo, El Vigía, puerta de salida)
    static Vector3 FW(BackroomsGenerator g, float x, float z, float up) { return g.Root.TransformPoint(new Vector3(x, FieldHeight.H(x, z) + up, z)); }
    static float YawTo(Vector3 a, Vector3 b) { Vector3 d = b - a; d.y = 0f; return Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d : Vector3.forward).eulerAngles.y; }

    static IEnumerator Track(GameManager gm, string name, int frames, Vector3 a, Vector3 b, Vector3 look, float p0, float p1, float yawOff0 = 0f, float yawOff1 = 0f)
    {
        FirstPersonController f = gm.player; f.debugInput = true; f.debugMove = Vector2.zero;
        Place(gm, a, YawTo(a, look), p0); yield return 1.0f;
        Begin(name);
        for (int i = 0; i < frames; i++)
        {
            float u = Mathf.SmoothStep(0f, 1f, i / (float)(frames - 1));
            Vector3 p = Vector3.Lerp(a, b, u);
            Place(gm, p, YawTo(p, look) + Mathf.Lerp(yawOff0, yawOff1, u), Mathf.Lerp(p0, p1, u));
            yield return null;
        }
        End();
    }

    static IEnumerator FieldShots(GameManager gm)
    {
        BackroomsGenerator g = gm.generator; FieldPlan plan = g.FieldPlan;
        foreach (Light l in gm.player.GetComponentsInChildren<Light>(true)) l.enabled = false;
        Vector3 s = g.Root.InverseTransformPoint(gm.player.transform.position);
        // 1) hierba: travelling lento a ras, mirando al horizonte con colinas
        {
            float best = 0f, bd = -1f;
            for (int a = 0; a < 360; a += 15) { Vector3 d = Quaternion.Euler(0, a, 0) * Vector3.forward; float x = s.x + d.x * 30f, z = s.z + d.z * 30f; float h = FieldHeight.H(x, z); if (h > bd) { bd = h; best = a; } }
            Vector3 d0 = Quaternion.Euler(0, best, 0) * Vector3.forward, r = Vector3.Cross(Vector3.up, d0);
            Vector3 a0 = FW(g, s.x + r.x * 3f, s.z + r.z * 3f, 0.05f), b0 = FW(g, s.x + d0.x * 6f, s.z + d0.z * 6f, 0.05f);
            Vector3 look = FW(g, s.x + d0.x * 60f, s.z + d0.z * 60f, 0f);
            yield return Track(gm, "l5_grass", 150, a0, b0, look, 9f, 3f, 12f, -6f);
            // 1b) mirada baja a la hierba agitándose
            Vector3 c0 = FW(g, s.x - r.x * 2f, s.z - r.z * 2f, 0.05f);
            yield return Track(gm, "l5_wind", 105, c0, c0 + d0 * 1.2f, FW(g, s.x + d0.x * 20f, s.z + d0.z * 20f, 0f), 24f, 16f, -20f, 15f);
        }
        // 2) casas señuelo: la pareja de casas más cercana, travelling lateral desde 16 m
        if (plan != null && plan.houses.Count > 1)
        {
            int bi = 0, bj = 1; float bdist = 1e9f;
            for (int i = 0; i < plan.houses.Count; i++) for (int j = i + 1; j < plan.houses.Count; j++)
            { float dd = FieldHeight.WrappedDist(plan.houses[i].pos.x, plan.houses[i].pos.y, plan.houses[j].pos.x, plan.houses[j].pos.y); if (dd > 10f && dd < bdist) { bdist = dd; bi = i; bj = j; } }
            Vector2 hi = plan.houses[bi].pos, hj = plan.houses[bj].pos;
            Vector2 dj = new Vector2(FieldHeight.Wrap(hj.x - hi.x), FieldHeight.Wrap(hj.y - hi.y));
            Vector2 mid = hi + dj * 0.5f, perp = new Vector2(-dj.y, dj.x).normalized;
            float back = Mathf.Max(16f, bdist * 0.9f);
            Vector2 cam = mid + perp * back, side = dj.normalized * 4f;
            Vector3 look = FW(g, mid.x, mid.y, 1.2f);
            Debug.Log("[Trailer] casas " + bi + "," + bj + " a " + bdist.ToString("F1") + " m");
            yield return Track(gm, "l5_houses", 165, FW(g, cam.x - side.x, cam.y - side.y, 0.05f), FW(g, cam.x + side.x, cam.y + side.y, 0.05f), look, 2f, 1f);
        }
        // 2b) v7.0.1 imitador: un "jugador" quieto, mirando, a tirones... y se transforma
        MimicDisguise md = gm.monsters.Length > 0 && gm.monsters[0] != null ? gm.monsters[0].mimic : null;
        if (md != null)
        {
            MonsterAI m = gm.monsters[0];
            UnityEngine.AI.NavMeshAgent ag0 = m.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag0 != null) ag0.enabled = false;
            m.enabled = false;
            md.SetDisguised(true);
            Vector3 d0 = Quaternion.Euler(0, -50f, 0) * Vector3.forward;
            Vector3 eye = FW(g, s.x, s.z, 0.05f);
            Place(gm, eye, YawTo(eye, eye + d0), 2f);
            float dist = 9f;
            yield return 0.6f;
            Begin("l5_mimic");
            for (int i = 0; i < 165; i++)
            {
                if (i == 100) md.SetDisguised(false);
                if (i > 100) { dist = Mathf.Max(1.6f, dist - 7.5f / 30f); if (m.visual != null) m.visual.SetLocomotion(MonsterAI.State.Chase, 5f); }
                m.transform.position = FW(g, s.x + d0.x * dist, s.z + d0.z * dist, 0f); m.transform.rotation = Quaternion.LookRotation(-d0);
                Place(gm, eye, YawTo(eye, eye + d0), Mathf.Lerp(2f, -4f, i / 165f));
                yield return null;
            }
            End();
            m.transform.position = FW(g, s.x - d0.x * 90f, s.z - d0.z * 90f, 0f);
        }
        // 3) El Vigía: de pie en la hierba, lejos, luego se acerca
        if (gm.monsters.Length > 0 && gm.monsters[0] != null)
        {
            MonsterAI m = gm.monsters[0];
            if (m.mimic != null) m.mimic.SetDisguised(false);
            UnityEngine.AI.NavMeshAgent ag = m.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag != null) ag.enabled = false;
            m.enabled = false;
            foreach (FieldWatcherApparition fa in Object.FindObjectsOfType<FieldWatcherApparition>()) fa.enabled = false;
            Vector3 d0 = Quaternion.Euler(0, 37f, 0) * Vector3.forward;
            Vector3 eye = FW(g, s.x, s.z, 0.05f);
            Place(gm, eye, YawTo(eye, eye + d0), 1f);
            MonsterVisual v = m.visual;
            float dist = 26f; int N = 150;
            yield return 0.5f;
            Begin("l5_watcher");
            for (int i = 0; i < N; i++)
            {
                if (i < 60) { if (v != null) v.SetLocomotion(MonsterAI.State.Patrol, 0f); }
                else { dist = Mathf.Max(3.0f, dist - 6.0f / 30f * Mathf.Min(1f, (i - 60) / 20f) * 1.6f); if (v != null) v.SetLocomotion(MonsterAI.State.Chase, 4.5f); if (i == 60 && v != null) v.OnChaseStart(); }
                float x = s.x + d0.x * dist, z = s.z + d0.z * dist;
                m.transform.position = FW(g, x, z, 0f); m.transform.rotation = Quaternion.LookRotation(-d0);
                Place(gm, eye, YawTo(eye, eye + d0), Mathf.Lerp(1f, -2f, i / (float)N));
                yield return null;
            }
            End();
            m.transform.position = FW(g, s.x - d0.x * 90f, s.z - d0.z * 90f, 0f);
            Debug.Log("[Trailer] l5_watcher " + (v != null ? v.profile.id : "?"));
        }
        // 4) la puerta de salida, sola en el campo, con su bombilla
        {
            Vector3 e = g.Root.InverseTransformPoint(g.ExitPosition);
            Transform door = FieldExitBeacon.Active != null ? FieldExitBeacon.Active.transform : null;
            Vector3 fwd = door != null ? door.forward : Vector3.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 ld = g.Root.InverseTransformDirection(fwd);
            Vector3 lookW = FW(g, e.x, e.z, 1.3f);
            Vector3 a = FW(g, e.x + ld.x * 11f + ld.z * 3f, e.z + ld.z * 11f - ld.x * 3f, 0.05f), b = FW(g, e.x + ld.x * 4.5f, e.z + ld.z * 4.5f, 0.05f);
            Debug.Log("[Trailer] salida a " + Vector3.Distance(e, s).ToString("F0") + " m");
            yield return Track(gm, "l5_exit", 150, a, b, lookW, 0f, -3f);
        }
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

    // Solo campo (TRAILER_ONLY=field)
    static IEnumerator RunField()
    {
        GameSettings.ResetDefaults();
        GameManager.HideControlsHint = true;
        while (IntroSequence.Current == null) yield return null;
        while (IntroSequence.Active) yield return null;
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) yield return 0.3f;
        yield return 1f;
        GameBootstrap.Instance.useRandomSeed = false;
        MimicDisguise.ForceForTests = 1;
        yield return LoadLevel(5, false);
        yield return 1.5f;
        GameManager gm = GameManager.Instance; StopMonsters(gm);
        yield return FieldShots(gm);
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
