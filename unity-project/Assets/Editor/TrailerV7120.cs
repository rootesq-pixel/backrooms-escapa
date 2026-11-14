using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tráiler v7.12.0: metraje REAL (play mode, gráficos, Time.captureFramerate = 30) a JPG 1920x1080
// en /workspace/trailer2/frames/<plano>/00000.jpg y un WAV OfflineMix por plano en /workspace/trailer2/audio/.
//   TRAILER_ONLY=elevator|l0|l1|l2|l5|l6|l7|l8|l9|w188
//   xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath <p> -executeMethod TrailerV7120.Export -force-glcore
[InitializeOnLoad]
public static class TrailerV7120
{
    public static int W = 1920, H = 1080;
    const string Root = "/workspace/trailer2/frames/";
    const string AudioDir = "/workspace/trailer2/audio/";
    const string LogPath = "/workspace/trailer2/unity-shots.log";
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static double waitUntil;
    static int lastFrame = -1;
    static string shot;
    static int shotFrame;
    static Camera uiCam;
    static bool started;
    static string only;

    static TrailerV7120()
    {
        if (SessionState.GetString("smoke_phase", "") != "trailer2") return;
        EditorApplication.update += Tick;
    }

    public static void Export()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(AudioDir);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "trailer2");
        SessionState.SetBool("intro_real", false);
        SessionState.SetString("trailer_only", System.Environment.GetEnvironmentVariable("TRAILER_ONLY") ?? "");
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (!started)
        {
            started = true;
            Time.captureFramerate = 30;
            only = SessionState.GetString("trailer_only", "");
            Log("inicio only='" + only + "' gfx=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
            stack.Push(Run());
        }
        if (Time.frameCount == lastFrame) return;
        if (shot == null && EditorApplication.timeSinceStartup < waitUntil) return;
        lastFrame = Time.frameCount;
        try
        {
            if (shot != null)
            {
                Capture(Root + shot + "/" + shotFrame.ToString("00000") + ".jpg");
                shotFrame++;
            }
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
        catch (System.Exception e) { Log("EXC " + e); Debug.LogError("[Trailer2] " + e); Finish(); }
    }

    static void Finish()
    {
        SessionState.SetString("smoke_phase", "");
        SessionState.SetBool("intro_real", false);
        EditorApplication.update -= Tick;
        Time.captureFramerate = 0;
        GameManager.HideControlsHint = false;
        InventoryUI.ForceFit = 0f;
        Log("fin");
        EditorApplication.Exit(0);
    }

    static void Log(string s)
    {
        string line = System.DateTime.Now.ToString("HH:mm:ss") + " " + s;
        File.AppendAllText(LogPath, line + "\n");
        Debug.Log("[Trailer2] " + s);
    }

    static bool Want(string section)
    {
        if (string.IsNullOrEmpty(only)) return true;
        return only == section;
    }

    static void Begin(string name)
    {
        shot = name; shotFrame = 0;
        Directory.CreateDirectory(Root + name);
        Log("plano " + name);
    }

    static void End()
    {
        Log("fin plano " + shot + " (" + shotFrame + " fotogramas)");
        if (shotFrame > 0)
        {
            try
            {
                float sec = shotFrame / 30f;
                float[] data = AudioCapture.OfflineMix(Mathf.Max(0.25f, sec), 22050, null);
                AudioCapture.WriteWav(AudioDir + shot + ".wav", data, 1, 22050);
            }
            catch (System.Exception e) { Log("audio " + shot + " " + e.Message); }
        }
        shot = null;
    }

    static void SetupUi()
    {
        if (uiCam == null)
        {
            GameObject ugo = new GameObject("Trailer2UiCam");
            uiCam = ugo.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.Depth; uiCam.cullingMask = 1 << 31;
            uiCam.orthographic = true; uiCam.depth = 100; uiCam.enabled = false;
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
        if (game != null)
        {
            RenderTexture old = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = old;
        }
        SetupUi(); Canvas.ForceUpdateCanvases();
        uiCam.targetTexture = rt; uiCam.Render(); uiCam.targetTexture = null;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToJPG(92));
        Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt);
    }

    static IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

    static IEnumerator LoadLevel(int lv, bool elevator)
    {
        ElevatorCabin.InstantForTests = !elevator;
        GameBootstrap gb = GameBootstrap.Instance;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(lv);
        float wt = 0f;
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f)
        { yield return 0.2f; wt += 0.2f; }
        Log("cargado nivel " + lv + " en " + wt.ToString("F1") + " s (elevator=" + elevator + ")");
    }

    static void StopMonsters(GameManager gm)
    {
        if (gm.monsters == null) return;
        foreach (MonsterAI m in gm.monsters) { if (m == null) continue; m.Stop(); }
        gm.lostRestartSeconds = 999f;
    }

    static void Place(GameManager gm, Vector3 pos, float yaw, float pitch)
    {
        FirstPersonController f = gm.player;
        f.Teleport(pos, yaw);
        f.SetPitch(pitch);
    }

    static void Flash(GameManager gm, bool on)
    {
        Flashlight fl = gm.player != null ? gm.player.GetComponentInChildren<Flashlight>() : null;
        if (fl == null) return;
        if (on) fl.SetBattery(100f);
        fl.SetOn(on);
    }

    static float BestDir(GameManager gm, out float dist)
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

    static IEnumerator Walk(GameManager gm, string name, int frames, float yaw, float pitch0, float pitch1, float yawDrift, float speedY)
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

    static IEnumerator Track(GameManager gm, string name, int frames, Vector3 a, Vector3 b, Vector3 look, float p0, float p1)
    {
        FirstPersonController f = gm.player; f.debugInput = true; f.debugMove = Vector2.zero;
        Place(gm, a, YawTo(a, look), p0); yield return 0.4f;
        Begin(name);
        for (int i = 0; i < frames; i++)
        {
            float u = Mathf.SmoothStep(0f, 1f, i / (float)Mathf.Max(1, frames - 1));
            Vector3 p = Vector3.Lerp(a, b, u);
            Place(gm, p, YawTo(p, look), Mathf.Lerp(p0, p1, u));
            yield return null;
        }
        End();
    }

    static float YawTo(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a; d.y = 0f;
        return Quaternion.LookRotation(d.sqrMagnitude > 1e-4f ? d : Vector3.forward).eulerAngles.y;
    }

    static IEnumerator Run()
    {
        GameSettings.Quality = 2;
        GameSettings.VhsOn = true;
        GameSettings.SoftScare = false;
        GameManager.HideControlsHint = true;
        InventoryUI.ForceFit = 1f;
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) yield return 0.3f;
        yield return 1f;
        GameBootstrap.Instance.useRandomSeed = false;
        Health.DisableForTests = true;

        if (Want("elevator") || Want("l0")) yield return Level0();
        if (Want("l1")) yield return Level1();
        if (Want("l2")) yield return Level2();
        if (Want("l5")) yield return Level5();
        if (Want("l6")) yield return Level6();
        if (Want("l7")) yield return Level7();
        if (Want("l8")) yield return Level8();
        if (Want("l9")) yield return Level9();
        if (Want("w188")) yield return Level188();
    }

    static IEnumerator Level0()
    {
        GameBootstrap gb = GameBootstrap.Instance;
        int ld = gb.LoadCount;
        ElevatorCabin.InstantForTests = false;
        UIManager.Instance.StartGame(0);
        float wt = 0f;
        while (!(gb.LoadCount > ld && GameManager.Instance != null && !gb.InMenu && ElevatorCabin.Current != null) && wt < 240f)
        { yield return 0.2f; wt += 0.2f; }
        GameManager gm = GameManager.Instance;
        StopMonsters(gm);
        Flash(gm, true);
        if (Want("elevator"))
        {
            Begin("elevator");
            int guard = 0;
            while (ElevatorCabin.Current != null && ElevatorCabin.Current.T < ElevatorCabin.TDoors1 + 0.35f && guard++ < 600)
                yield return null;
            End();
        }
        else
        {
            // si solo se piden planos de L0, esperar a que abran las puertas
            int guard = 0;
            while (ElevatorCabin.Current != null && ElevatorCabin.Current.T < ElevatorCabin.TDoors1 + 0.2f && guard++ < 600)
                yield return null;
        }
        StopMonsters(gm);
        ElevatorCabin cab = ElevatorCabin.Current;
        float yaw0 = cab != null ? Quaternion.LookRotation(cab.Forward).eulerAngles.y : gm.player.transform.eulerAngles.y;
        if (!Want("l0")) yield break;
        yield return Walk(gm, "l0_exit", 90, yaw0, 2f, 0f, 0f, 0.55f);
        float d; float y = BestDir(gm, out d);
        yield return Walk(gm, "l0_corridor", 105, y, 0f, 3f, 12f, 0.5f);
        // cooperativo
        {
            y = BestDir(gm, out d);
            Place(gm, gm.player.transform.position, y, 4f);
            yield return Frames(3);
            Vector3 fwd = Quaternion.Euler(0, y, 0) * Vector3.forward, right = Vector3.Cross(Vector3.up, fwd);
            Vector3 feet = gm.player.transform.position; RaycastHit fh;
            if (Physics.Raycast(feet + Vector3.up * 0.5f, Vector3.down, out fh, 5f)) feet = fh.point;
            RemotePlayer a = RemotePlayer.Create(null, 2, "Marta", StepKind.Carpet);
            RemotePlayer b = RemotePlayer.Create(null, 3, "Luis", StepKind.Carpet);
            Vector3 pa = feet + fwd * Mathf.Min(d - 0.5f, 7f) + right * 0.6f;
            Vector3 pb = feet + fwd * Mathf.Min(d - 0.5f, 9f) - right * 0.6f;
            float yawBack = y + 180f;
            for (int i = 0; i < 12; i++) { a.Push(pa, yawBack, 0f, 0f, RemotePlayer.FlagFlash); b.Push(pb, yawBack, 0f, 0f, RemotePlayer.FlagFlash); yield return null; }
            Begin("l0_coop");
            for (int i = 0; i < 100; i++)
            {
                if (Vector3.Distance(pa, feet) > 2.2f) pa += -fwd * 1.5f / 30f;
                if (Vector3.Distance(pb, feet) > 3.0f) pb += -fwd * 1.5f / 30f;
                a.Push(pa, yawBack + Mathf.Sin(i * 0.05f) * 10f, 0f, 1.5f, RemotePlayer.FlagFlash);
                b.Push(pb, yawBack - 15f, 5f, 1.5f, i > 50 ? RemotePlayer.FlagFlash | RemotePlayer.FlagCrouch : RemotePlayer.FlagFlash);
                yield return null;
            }
            End();
            Object.Destroy(a.gameObject); Object.Destroy(b.gameObject);
        }
    }

    static IEnumerator Level1()
    {
        yield return LoadLevel(1, false);
        yield return 0.8f;
        GameManager gm = GameManager.Instance; StopMonsters(gm); Flash(gm, true);
        ElevatorCabin c = ElevatorCabin.Current;
        if (c != null) Place(gm, c.ExitPoint + c.Forward * 1.5f, Quaternion.LookRotation(c.Forward).eulerAngles.y, 0f);
        yield return Frames(8);
        float d; float y = BestDir(gm, out d);
        yield return Walk(gm, "l1_walk", 75, y, 2f, 6f, -14f, 0.5f);
    }

    static IEnumerator Level2()
    {
        yield return LoadLevel(2, false);
        yield return 1.0f;
        GameManager gm = GameManager.Instance; StopMonsters(gm); Flash(gm, false);
        BackroomsGenerator g = gm.generator;
        if (g == null || !g.PoolFixed)
        {
            float d; float y = BestDir(gm, out d);
            yield return Walk(gm, "l2_pool", 120, y, 8f, 14f, 10f, 0.35f);
            yield break;
        }
        Rect pr = g.PoolRectLocal; Transform gt = g.transform; float deck = BackroomsGenerator.PoolDeckY;
        FirstPersonController f = gm.player; f.debugInput = true; f.debugMove = Vector2.zero;
        List<Behaviour> off = new List<Behaviour>();
        foreach (Light l in f.GetComponentsInChildren<Light>(true)) if (l.enabled) { l.enabled = false; off.Add(l); }
        List<Renderer> roff = new List<Renderer>();
        Camera cam = Camera.main;
        if (cam != null) foreach (Renderer r in cam.GetComponentsInChildren<Renderer>(true)) if (r.enabled) { r.enabled = false; roff.Add(r); }
        Vector3 a = gt.TransformPoint(new Vector3(pr.xMin - 1.7f, deck, pr.yMin - 1.0f));
        Vector3 b = gt.TransformPoint(new Vector3(pr.center.x - 1.0f, deck, pr.yMin - 1.4f));
        Vector3 look = gt.TransformPoint(new Vector3(pr.xMax - 1.0f, deck - 0.4f, pr.center.y + 1.5f));
        Place(gm, a, YawTo(a, look), 14f);
        yield return 0.8f;
        Begin("l2_pool");
        for (int i = 0; i < 120; i++)
        {
            float u = Mathf.SmoothStep(0f, 1f, i / 119f);
            Vector3 p = Vector3.Lerp(a, b, u);
            Place(gm, p, YawTo(p, look) + Mathf.Lerp(-8f, 10f, u), Mathf.Lerp(16f, 22f, u));
            yield return null;
        }
        End();
        foreach (Behaviour l in off) if (l != null) l.enabled = true;
        foreach (Renderer r in roff) if (r != null) r.enabled = true;
        f.debugInput = false;
    }

    static Vector3 FW(BackroomsGenerator g, float x, float z, float up)
    {
        return g.Root.TransformPoint(new Vector3(x, FieldHeight.H(x, z) + up, z));
    }

    static IEnumerator Level5()
    {
        MimicDisguise.ForceForTests = 0;
        yield return LoadLevel(5, false);
        yield return 1.2f;
        GameManager gm = GameManager.Instance; StopMonsters(gm); Flash(gm, false);
        BackroomsGenerator g = gm.generator;
        Vector3 s = g.Root.InverseTransformPoint(gm.player.transform.position);
        float best = 0f, bd = -1f;
        for (int a = 0; a < 360; a += 15)
        {
            Vector3 d = Quaternion.Euler(0, a, 0) * Vector3.forward;
            float x = s.x + d.x * 30f, z = s.z + d.z * 30f; float h = FieldHeight.H(x, z);
            if (h > bd) { bd = h; best = a; }
        }
        Vector3 d0 = Quaternion.Euler(0, best, 0) * Vector3.forward, r = Vector3.Cross(Vector3.up, d0);
        Vector3 a0 = FW(g, s.x + r.x * 3f, s.z + r.z * 3f, 0.05f);
        Vector3 b0 = FW(g, s.x + d0.x * 6f, s.z + d0.z * 6f, 0.05f);
        Vector3 look = FW(g, s.x + d0.x * 60f, s.z + d0.z * 60f, 0f);
        yield return Track(gm, "l5_grass", 90, a0, b0, look, 9f, 3f);
        if (gm.monsters != null && gm.monsters.Length > 0 && gm.monsters[0] != null)
        {
            MonsterAI m = gm.monsters[0];
            if (m.mimic != null) m.mimic.SetDisguised(false);
            UnityEngine.AI.NavMeshAgent ag = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (ag != null) ag.enabled = false;
            m.enabled = false;
            foreach (FieldWatcherApparition fa in Object.FindObjectsOfType<FieldWatcherApparition>()) fa.enabled = false;
            Vector3 dir = Quaternion.Euler(0, 37f, 0) * Vector3.forward;
            Vector3 eye = FW(g, s.x, s.z, 0.05f);
            Place(gm, eye, YawTo(eye, eye + dir), 1f);
            MonsterVisual v = m.visual;
            float dist = 22f; int N = 90;
            yield return 0.4f;
            Begin("l5_watcher");
            for (int i = 0; i < N; i++)
            {
                if (i < 25) { if (v != null) v.SetLocomotion(MonsterAI.State.Patrol, 0f); }
                else
                {
                    dist = Mathf.Max(3.0f, dist - 7.0f / 30f);
                    if (v != null) v.SetLocomotion(MonsterAI.State.Chase, 4.5f);
                    if (i == 25 && v != null) v.OnChaseStart();
                }
                m.transform.position = FW(g, s.x + dir.x * dist, s.z + dir.z * dist, 0f);
                m.transform.rotation = Quaternion.LookRotation(-dir);
                Place(gm, eye, YawTo(eye, eye + dir), Mathf.Lerp(1f, -2f, i / (float)N));
                yield return null;
            }
            End();
            m.transform.position = FW(g, s.x - dir.x * 90f, s.z - dir.z * 90f, 0f);
        }
    }

    static IEnumerator Level6()
    {
        yield return LoadLevel(6, false);
        yield return 0.8f;
        GameManager gm = GameManager.Instance; StopMonsters(gm); Flash(gm, true);
        if (BallpitSetPiece.Roots != null && BallpitSetPiece.Roots.Count > 0 && BallpitSetPiece.Roots[0] != null)
        {
            Transform root = BallpitSetPiece.Roots[0].transform;
            Vector3 feet = root.TransformPoint(new Vector3(-3.0f, 0.05f, -3.2f));
            Vector3 look = root.TransformPoint(new Vector3(0.4f, 0.8f, 1.2f));
            Place(gm, feet, YawTo(feet, look), -8f);
            yield return 0.5f;
            yield return Track(gm, "l6_pit", 90, feet, feet + (look - feet).normalized * 2.2f, look + Vector3.up * 0.4f, -6f, 4f);
        }
        else
        {
            float d; float y = BestDir(gm, out d);
            yield return Walk(gm, "l6_pit", 90, y, 2f, 8f, 10f, 0.45f);
        }
    }

    static IEnumerator Level7()
    {
        yield return LoadLevel(7, false);
        yield return 1.0f;
        GameManager gm = GameManager.Instance; StopMonsters(gm); Flash(gm, false);
        Health.DisableForTests = true;
        FirstPersonController p = gm.player;
        OceanWorld w = OceanWorld.Active; OceanPlayer op = OceanPlayer.Local;
        if (w != null)
        {
            if (w.fish != null) w.fish.enabled = false;
            if (w.angler != null) w.angler.enabled = false;
            Vector3 porch = new Vector3(15f, -0.3f, 30.6f);
            Vector3 sea = new Vector3(13f, 1.2f, 70f);
            Place(gm, porch, YawTo(porch, sea), -6f);
            yield return 0.6f;
            yield return Track(gm, "l7_sea", 90, porch, porch + new Vector3(-1.2f, 0f, 1.5f), sea, -6f, -2f);
            if (op != null)
            {
                op.SetHelmet(true);
                if (op.flashlight != null) { op.flashlight.SetBattery(100f); op.flashlight.SetOn(true); }
            }
            Vector3 me = new Vector3(60f, -47f, 252f);
            Vector3 ang = new Vector3(66f, -45.8f, 257f);
            // no reactivar el componente: HostKill ignora Health.DisableForTests
            if (w.angler != null)
            {
                w.angler.enabled = false;
                w.angler.ForceForTests(ang, OceanAngler.AState.Patrol);
                w.angler.transform.position = ang;
                w.angler.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, 0.45f));
            }
            Place(gm, me, YawTo(me, ang), 8f);
            yield return 0.5f;
            Begin("l7_under");
            for (int i = 0; i < 105; i++)
            {
                float u = i / 104f;
                Vector3 ap = Vector3.Lerp(ang, me + new Vector3(3f, 0.4f, 2f), Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, (u - 0.25f) / 0.75f)));
                if (w.angler != null)
                {
                    w.angler.transform.position = ap;
                    w.angler.transform.rotation = Quaternion.LookRotation(me - ap);
                }
                Place(gm, me, YawTo(me, ap), Mathf.Lerp(8f, 2f, u));
                yield return null;
            }
            End();
            if (op != null) op.SetHelmet(false);
        }
        else
        {
            float d; float y = BestDir(gm, out d);
            yield return Walk(gm, "l7_sea", 90, y, 0f, 4f, 8f, 0.3f);
        }
    }

    static IEnumerator Level8()
    {
        yield return LoadLevel(8, false);
        yield return 1.0f;
        GameManager gm = GameManager.Instance; StopMonsters(gm); Flash(gm, false);
        DesertWorld w = DesertWorld.Active;
        if (w != null)
        {
            System.Func<float, float, float, Vector3> P = (x, z, up) =>
                w.levelRoot.TransformPoint(new Vector3(x, DesertHeight.H(Mathf.Repeat(x, w.L), Mathf.Repeat(z, w.L)) + up, z));
            Vector3 s = w.StartLocal;
            float yaw = w.StartYaw * Mathf.Deg2Rad;
            Vector3 f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            Vector3 q = s + f * 200f;
            Vector3 feet = P(q.x, q.z, 0.3f);
            Vector3 look = P(q.x + f.x * 40f, q.z + f.z * 40f, -2f);
            Vector3 b = P(q.x + f.x * 8f, q.z + f.z * 8f, 0.3f);
            Place(gm, feet, w.StartYaw, 4f);
            yield return 0.6f;
            yield return Track(gm, "l8_dunes", 90, feet, b, look, 4f, 1f);
        }
        else
        {
            float d; float y = BestDir(gm, out d);
            yield return Walk(gm, "l8_dunes", 90, y, 2f, 0f, 8f, 0.4f);
        }
    }

    static IEnumerator Level9()
    {
        yield return LoadLevel(9, false);
        yield return 0.8f;
        GameManager gm = GameManager.Instance; StopMonsters(gm); Flash(gm, false);
        CorridorWorld w = CorridorWorld.Active;
        if (w != null)
        {
            CorridorEvents.DisableForTests = true;
            float cs = CorridorWorld.CS;
            System.Func<float, float, float, Vector3> L = (x, y, z) => w.levelRoot.TransformPoint(new Vector3(w.CenterX + x, y, z));
            Vector3 feet = L(0f, 0.1f, 20f * cs);
            Vector3 look = L(0f, 1.45f, 60f * cs);
            Vector3 b = L(0f, 0.1f, 28f * cs);
            Place(gm, feet, 0f, 2f);
            yield return 0.5f;
            yield return Track(gm, "l9_hall", 90, feet, b, look, 2f, 1f);
            CorridorEvents.DisableForTests = false;
        }
        else
        {
            float d; float y = BestDir(gm, out d);
            yield return Walk(gm, "l9_hall", 90, y, 0f, 2f, 0f, 0.45f);
        }
    }

    static IEnumerator Level188()
    {
        GameBootstrap gb = GameBootstrap.Instance;
        gb.useRandomSeed = false;
        gb.seed = 188 - 10 * 101;
        yield return LoadLevel(10, false);
        yield return 1.2f;
        float wt = 0f;
        while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 0.6f;
        GameManager gm = GameManager.Instance;
        FirstPersonController p = gm.player;
        Health.DisableForTests = true;
        W188Events.NoGrabForTests = true;
        Flash(gm, false);
        W188World w = W188World.Active; W188Events ev = W188Events.Active;
        if (w == null) { Log("SIN W188"); yield break; }
        W188Plan P = w.plan;
        Transform R = w.levelRoot;
        Vector3 cc = R.TransformPoint(new Vector3(W188Plan.R + W188Plan.NX * W188Plan.M * 0.5f, 0f, W188Plan.R + W188Plan.NZ * W188Plan.M * 0.5f));
        Vector3 c1 = cc + R.TransformDirection(new Vector3(-3f, 0f, -12f)) + Vector3.up * 0.1f;
        float eyeH = 1.62f;
        System.Func<int, float, float, float, Vector3> Lw = (s, u, y, d) => w.levelRoot.TransformPoint(W188Plan.ToLevel(s, u, y, d));

        // llegada: desde cerca del ascensor hacia el centro del patio
        ElevatorCabin cab = ElevatorCabin.Current;
        Vector3 arriveA = cab != null ? cab.ExitPoint + cab.Forward * 1.2f : c1;
        Vector3 arriveB = Vector3.Lerp(arriveA, c1, 0.55f);
        Place(gm, arriveA, YawTo(arriveA, cc), 2f);
        yield return 0.5f;
        yield return Track(gm, "l188_arrive", 90, arriveA, arriveB, cc + Vector3.up * 3f, 2f, 6f);

        // fachadas
        Vector3 lookFac = c1 + Vector3.up * 7f + R.TransformDirection(new Vector3(4f, 0f, 14f));
        Vector3 facB = c1 + R.TransformDirection(new Vector3(2.5f, 0f, 3.0f));
        Place(gm, c1, YawTo(c1, lookFac), -18f);
        yield return 0.4f;
        yield return Track(gm, "l188_fachadas", 120, c1, facB, lookFac, -16f, -22f);

        // tilt al cielo
        Vector3 skyLook = c1 + Vector3.up * 16f + R.TransformDirection(new Vector3(9f, 0f, 9f));
        Begin("l188_cielo");
        for (int i = 0; i < 75; i++)
        {
            float u = Mathf.SmoothStep(0f, 1f, i / 74f);
            Place(gm, c1, YawTo(c1, Vector3.Lerp(lookFac, skyLook, u)), Mathf.Lerp(-22f, -48f, u));
            yield return null;
        }
        End();

        // Periférico de reojo
        Place(gm, c1, 0f, 0f);
        yield return 0.3f;
        W188Events.DisableForTests = false;
        if (ev != null)
        {
            ev.ForcePeripheral();
            float t = 0f;
            while (!ev.PeripheralVisible && t < 6f) { ev.ForcePeripheral(); yield return 0.1f; t += 0.1f; }
            if (ev.PeripheralVisible)
            {
                Vector3 eye = p.transform.position + Vector3.up * eyeH;
                Vector3 pp = ev.PeripheralPos + Vector3.up * 1.2f;
                Vector3 to = pp - eye; to.y = 0f;
                float side = Mathf.Sign(Vector3.SignedAngle(p.transform.forward, to, Vector3.up));
                Begin("l188_peri");
                for (int i = 0; i < 90; i++)
                {
                    float u = i / 89f;
                    // se mira un poco al frente; el periférico queda a un lado, y al final casi se gira hacia él
                    float off = Mathf.Lerp(-38f, -18f, u);
                    Vector3 fwd = Quaternion.Euler(0f, side * off, 0f) * to.normalized;
                    Place(gm, c1, YawTo(c1, c1 + fwd * 10f), 2f);
                    yield return null;
                }
                End();
            }
            else Log("periférico: no apareció");
        }
        W188Events.DisableForTests = true;

        // La Ventana llamando: dolly desde el patio hacia el cristal
        if (w.Figures.Count > 0)
        {
            W188World.Figure fg = w.Figures[0];
            Vector3 far = Lw(fg.s, W188Plan.UC(fg.i) - 1.2f, Mathf.Max(0.1f, fg.f * W188Plan.FH + 0.1f), -11f);
            Vector3 near = Lw(fg.s, W188Plan.UC(fg.i) - 0.15f, Mathf.Max(0.1f, fg.f * W188Plan.FH + 0.1f), -2.0f);
            Vector3 lookV = fg.root.position + Vector3.up * 1.4f;
            Place(gm, far, YawTo(far, lookV), 8f);
            Camera cam = Camera.main; float fov0 = cam != null ? cam.fieldOfView : 60f;
            yield return 0.5f;
            Begin("l188_ventana");
            for (int i = 0; i < 150; i++)
            {
                float u = Mathf.SmoothStep(0f, 1f, i / 149f);
                Vector3 pos = Vector3.Lerp(far, near, u);
                Place(gm, pos, YawTo(pos, lookV), Mathf.Lerp(6f, 2f, u));
                if (cam != null) cam.fieldOfView = Mathf.Lerp(32f, 48f, u);
                yield return null;
            }
            End();
            if (cam != null) cam.fieldOfView = fov0;

            // susto: acercarse a la puerta (ella se gira) y StartGrab
            Vector3 corr = w.DoorWorld(fg.f, fg.s, fg.i, W188Plan.CorrIn + 0.75f);
            Place(gm, corr, W188World.YawToCourt(fg.s), 4f);
            yield return 1.2f;   // tiempo real: se gira hacia el jugador
            Vector3 inside = w.CellWorld(fg.f, fg.s, fg.i, 0.4f, 2.4f);
            Place(gm, inside, W188World.YawToCourt(fg.s), 2f);
            yield return Frames(4);
            Health.DisableForTests = false;
            W188Events.NoGrabForTests = false;
            if (ev != null) ev.StartGrab(fg);
            Begin("l188_scare");
            for (int i = 0; i < 90; i++) yield return null;
            End();
            Health.DisableForTests = true;
            W188Events.NoGrabForTests = true;
        }
        else Log("sin figuras Ventana");
    }
}
