using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.10.2 (t500u): laberinto del Nivel 7460: plano cenital (dibujado desde el plano y render ortográfico real), un cruce,
// un pasillo largo, el juego a 1920x1080 con HUD, FPS en la caja y un bot que camina inicio → llave buena → salida.
// Uso: V7102_OUT=<dir> xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath <p> -executeMethod V7102Look.Export
[InitializeOnLoad]
public static class V7102Look
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V7102_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v7102/shots" : o; } }
    static int Seed { get { int s; return int.TryParse(System.Environment.GetEnvironmentVariable("V7102_SEED"), out s) ? s : 12345; } }
    static bool BotOnly { get { return System.Environment.GetEnvironmentVariable("V7102_BOTONLY") == "1"; } }
    static V7102Look() { if (SessionState.GetString("smoke_phase", "") != "v7102look") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v7102look");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V7102Look] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { Time.timeScale = 1f; SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V7102Look] " + s); }
    static bool HasGfx { get { return SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null; } }

    private static Camera uiCam;
    private static void SetupUi()
    {
        if (uiCam == null)
        {
            GameObject ugo = new GameObject("PreviewUiCam"); uiCam = ugo.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.Depth; uiCam.cullingMask = 1 << 31; uiCam.orthographic = true; uiCam.depth = 100; uiCam.enabled = false;
        }
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = uiCam; c.planeDistance = 1f;
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        }
    }
    static void Save(Texture2D tex, string name) { File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG()); Log("captura " + name + " " + tex.width + "x" + tex.height); }
    private static void Shot(string name, bool hud, Vector3? eye = null, Vector3? look = null, int w = 1920, int h = 1080, float fov = -1f)
    {
        if (!HasGfx) return;
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        Vector3 p0 = game.transform.position; Quaternion r0 = game.transform.rotation; float f0 = game.fieldOfView;
        List<Renderer> hidden = new List<Renderer>();
        if (eye.HasValue)
        {
            game.transform.position = eye.Value; game.transform.rotation = Quaternion.LookRotation(look.Value - eye.Value);
            FirstPersonController pl = FirstPersonController.LocalInstance;
            if (pl != null) foreach (Renderer rr in pl.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hidden.Add(rr); }
        }
        if (fov > 0f) game.fieldOfView = fov;
        RenderTexture o = game.targetTexture; game.targetTexture = rt;
        if (CorridorPlanar.Instance != null) CorridorPlanar.Instance.Refresh(game);
        game.Render(); game.targetTexture = o;
        game.transform.position = p0; game.transform.rotation = r0; game.fieldOfView = f0;
        foreach (Renderer rr in hidden) rr.enabled = true;
        if (hud) { SetupUi(); Canvas.ForceUpdateCanvases(); uiCam.targetTexture = rt; uiCam.Render(); uiCam.targetTexture = null; }
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; Save(tex, name); Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
    }
    // render ortográfico desde justo bajo el techo (el techo queda detrás de la cámara): el laberinto real visto desde arriba
    static void OrthoShot(string name, Transform root, Vector3 center, float wM, float hM, int wPx)
    {
        if (!HasGfx) return;
        int hPx = Mathf.RoundToInt(wPx * hM / wM);
        GameObject go = new GameObject("MapCam"); Camera c = go.AddComponent<Camera>();
        c.orthographic = true; c.orthographicSize = hM * 0.5f; c.aspect = wM / hM; c.nearClipPlane = 0.01f; c.farClipPlane = 6f;
        c.clearFlags = CameraClearFlags.SolidColor; c.backgroundColor = new Color(0.05f, 0.05f, 0.05f); c.enabled = false;
        go.transform.position = root.TransformPoint(new Vector3(0f, 0f, 0f)) * 0f + new Vector3(center.x, root.position.y + 2.95f, center.z);
        go.transform.rotation = Quaternion.LookRotation(Vector3.down, root.forward);
        bool fog = RenderSettings.fog; RenderSettings.fog = false; CorridorPlanar.ForceOff = true; if (CorridorPlanar.Instance != null) CorridorPlanar.Instance.Refresh(Camera.main);
        RenderTexture rt = RenderTexture.GetTemporary(wPx, hPx, 24, RenderTextureFormat.ARGB32);
        c.targetTexture = rt; c.Render(); c.targetTexture = null;
        RenderSettings.fog = fog; CorridorPlanar.ForceOff = false;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(wPx, hPx, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, wPx, hPx), 0, 0); tex.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(go);
        Save(tex, name); Object.DestroyImmediate(tex);
    }

    // camino más corto por las celdas abiertas del plano
    public static List<Vector2Int> Path(CorridorPlan pl, int W, Vector2Int a, Vector2Int b)
    {
        int H = pl.rows; int[,] prev = new int[W, H]; for (int x = 0; x < W; x++) for (int z = 0; z < H; z++) prev[x, z] = -2;
        Queue<Vector2Int> q = new Queue<Vector2Int>(); q.Enqueue(a); prev[a.x, a.y] = -1;
        Vector2Int[] dirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue(); if (c == b) break;
            foreach (Vector2Int d in dirs)
            {
                Vector2Int n = c + d; if (n.x < 0 || n.y < 0 || n.x >= W || n.y >= H || prev[n.x, n.y] != -2 || !pl.Step(c, n)) continue;
                prev[n.x, n.y] = c.x * 100000 + c.y; q.Enqueue(n);
            }
        }
        List<Vector2Int> path = new List<Vector2Int>(); if (prev[b.x, b.y] == -2) return path;
        Vector2Int k = b; while (true) { path.Add(k); int p = prev[k.x, k.y]; if (p < 0) break; k = new Vector2Int(p / 100000, p % 100000); }
        path.Reverse(); return path;
    }
    public static Vector2Int CellOf(CorridorWorld w, Vector3 world)
    {
        Vector3 l = w.levelRoot.InverseTransformPoint(world);
        return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(l.x / CorridorWorld.CS), 0, 10000), Mathf.Clamp(Mathf.FloorToInt(l.z / CorridorWorld.CS), 0, w.plan.rows - 1));
    }
    // celda abierta más cercana (los objetos van junto al zócalo; la celda del pasillo transversal o largo)
    public static Vector2Int OpenNear(CorridorPlan pl, int W, Vector2Int c)
    {
        for (int r = 0; r < 4; r++) for (int dx = -r; dx <= r; dx++) for (int dz = -r; dz <= r; dz++) if (pl.IsOpenCell(c.x + dx, c.y + dz)) return new Vector2Int(c.x + dx, c.y + dz);
        return c;
    }

    static Texture2D DrawMap(CorridorWorld w, int W, List<Vector2Int> toKey, List<Vector2Int> toExit, List<Vector2Int> keys, Vector2Int good)
    {
        CorridorPlan pl = w.plan; int H = pl.rows, S = 8;
        Texture2D t = new Texture2D(W * S, H * S, TextureFormat.RGB24, false);
        Color bg = new Color(0.08f, 0.07f, 0.05f), open = new Color(0.78f, 0.70f, 0.48f), crossC = new Color(0.66f, 0.60f, 0.42f);
        Color[] px = new Color[W * S * H * S]; for (int i = 0; i < px.Length; i++) px[i] = bg;
        System.Action<int, int, Color, int> cell = (x, z, col, inset) => { for (int a = inset; a < S - inset; a++) for (int b = inset; b < S - inset; b++) px[(z * S + b) * W * S + x * S + a] = col; };
        for (int x = 0; x < W; x++) for (int z = 0; z < H; z++) if (pl.IsOpenCell(x, z)) cell(x, z, pl.IsRoomCell(x, z) && !IsMain(pl, x) ? crossC : open, 0);
        // puertas cerradas / tapiadas de los pasillos largos: marcas en el borde
        foreach (CorridorPlan.Run ru in pl.runs) if (!ru.cross) for (int k = 0; k < ru.rows; k++) for (int s = 0; s < 2; s++)
        {
            CorridorPlan.Door d = ru.doors[s, k]; if (d == CorridorPlan.Door.Link) continue;
            int x = pl.ColOf(ru.main) + (s == CorridorPlan.Left ? -1 : 1);
            Color col = d == CorridorPlan.Door.Bricked ? new Color(0.55f, 0.2f, 0.15f) : d == CorridorPlan.Door.Exit ? Color.red : new Color(0.35f, 0.25f, 0.14f);
            if (x >= 0 && x < W) for (int b = 2; b < S - 2; b++) for (int a = 0; a < 2; a++) px[(k * S + b) * W * S + x * S + (s == CorridorPlan.Left ? S - 1 - a : a)] = col;
        }
        foreach (CorridorPlan.Run ru in pl.runs) if (ru.cross && ru.deadEnd) cell(pl.ColOf(ru.b) - 1, ru.row, new Color(0.55f, 0.2f, 0.15f), 0);   // fondo sin salida
        foreach (Vector2Int c in toKey) cell(c.x, c.y, new Color(1f, 0.75f, 0.1f), 2);
        foreach (Vector2Int c in toExit) cell(c.x, c.y, new Color(0.2f, 0.9f, 0.3f), 3);
        foreach (Vector2Int c in keys) cell(c.x, c.y, new Color(0.2f, 0.5f, 1f), 1);
        cell(good.x, good.y, new Color(1f, 0.3f, 1f), 0);
        Vector2Int ex = pl.ExitCell; cell(ex.x, ex.y, Color.red, 0);
        cell(pl.Col, 0, Color.white, 0);
        t.SetPixels(px); t.Apply(); return t;
    }
    static bool IsMain(CorridorPlan pl, int x) { for (int i = 0; i < pl.Mains; i++) if (pl.ColOf(i) == x) return true; return false; }

    static HotelDoorLeaf LeafNear(CorridorWorld w, Vector3 p) { HotelDoorLeaf best = null; float bd = 2f; foreach (HotelDoorLeaf l in w.OpenableDoors) { float d = Vector3.Distance(l.center, p); if (d < bd) { bd = d; best = l; } } return best; }

    // FPS: media de Time.unscaledDeltaTime y tiempo de un render a 1920x1080 con la cámara del juego
    static IEnumerator Fps(string tag, List<string> rows)
    {
        Camera cam = Camera.main; float t = 0f; int n = 0; float sum = 0f, worst = 0f;
        yield return 0.5f;
        float t0 = Time.realtimeSinceStartup; int f0 = Time.frameCount;
        while (t < 4f) { yield return 0f; float dt = Time.unscaledDeltaTime; sum += dt; n++; worst = Mathf.Max(worst, dt); t += dt; }
        float loopFps = (Time.frameCount - f0) / Mathf.Max(0.001f, Time.realtimeSinceStartup - t0);
        float rms = 0f; int calls = 0, tris = 0;
        if (HasGfx)
        {
            RenderTexture rt = RenderTexture.GetTemporary(1920, 1080, 24, RenderTextureFormat.ARGB32); RenderTexture o = cam.targetTexture; cam.targetTexture = rt;
            Texture2D one = new Texture2D(1, 1, TextureFormat.RGB24, false);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            for (int i = 0; i < 12; i++)
            {
                if (i == 2) sw.Start();
                cam.Render(); RenderTexture prev = RenderTexture.active; RenderTexture.active = rt; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); RenderTexture.active = prev;
            }
            sw.Stop(); rms = (float)sw.Elapsed.TotalMilliseconds / 10f;
            calls = UnityEditor.UnityStats.drawCalls; tris = UnityEditor.UnityStats.triangles;
            cam.targetTexture = o; RenderTexture.ReleaseTemporary(rt); Object.DestroyImmediate(one);
        }
        CorridorWorld w = CorridorWorld.Active;
        string s = tag + ": bucle " + loopFps.ToString("F1") + " fps (peor fotograma " + (worst * 1000f).ToString("F0") + " ms), render 1920x1080 " + rms.ToString("F1") + " ms (" + (rms > 0 ? (1000f / rms).ToString("F0") : "-") + " fps), " + calls + " draw calls, " + tris + " triángulos, pasillos dibujados " + (w != null ? w.VisibleRuns + "/" + w.Runs.Count : "?");
        rows.Add(s); Log("FPS " + s);
    }

    // bot: camina por el centro de las celdas del camino más corto (corriendo, sin teletransportes)
    public static IEnumerator Walk(FirstPersonController p, CorridorWorld w, List<Vector2Int> path, float[] res, float timeout)
    {
        res[0] = 0f; res[1] = 0f; res[2] = 0f;
        List<Vector3> wp = new List<Vector3>();
        for (int i = 0; i < path.Count; i++)
        {   // solo las esquinas (en tramos rectos no hace falta un punto por celda)
            bool corner = i == 0 || i == path.Count - 1 || (path[i + 1] - path[i]) != (path[i] - path[i - 1]);
            if (corner) wp.Add(w.CellWorld(path[i].x, path[i].y));
        }
        p.debugInput = true; p.debugSprint = true;
        int k = 1; float t = 0f, stuckT = 0f; Vector3 last = p.transform.position; float walked = 0f;
        while (k < wp.Count && t < timeout && (GameManager.Instance == null || GameManager.Instance.State == GameManager.GameState.Playing))
        {
            p.SetStaminaForTests(p.maxStamina);
            Vector3 d = wp[k] - p.transform.position; d.y = 0f;
            if (d.magnitude < 0.45f) { k++; continue; }
            p.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
            p.debugMove = new Vector2(0f, 1f);
            yield return 0f; t += Time.unscaledDeltaTime;
            Vector3 now = p.transform.position; float step = Vector3.Distance(Flat(now), Flat(last)); walked += step; last = now;
            if (step < 0.002f * Mathf.Max(1f, Time.timeScale)) stuckT += Time.deltaTime; else stuckT = 0f;
            if (stuckT > 5f) { res[2] += 1f; Log("bot atascado en " + now + " hacia " + wp[k]); break; }
        }
        p.debugInput = false; p.debugSprint = false; p.debugMove = Vector2.zero;
        res[0] = k >= wp.Count || (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing) ? 1f : 0f; res[1] = walked;
    }
    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", "");
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        int qOrig = GameSettings.Quality; GameSettings.Quality = 2; yield return 0.5f;
        Log("calidad " + GameSettings.Quality + ", gráficos " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName + ", CPU " + SystemInfo.processorType + " x" + SystemInfo.processorCount);
        // --- estadística del plano en 300 semillas ---
        LevelConfig c9 = LevelConfig.Get(9); int W = c9.gridW, N = c9.gridH;
        if (!BotOnly)
        {
            int mn = int.MaxValue, mx = 0; long sum = 0; int reach = 0, att = 0, cross = 0;
            for (int s = 1; s <= 300; s++)
            {
                CorridorPlan pl = CorridorPlan.Make(s * 7919 + 3, W, N, new bool[W + 1, N], new bool[W, N + 1]);
                mn = Mathf.Min(mn, pl.PathCells); mx = Mathf.Max(mx, pl.PathCells); sum += pl.PathCells; att += pl.Attempts; cross += pl.CrossCount;
                if (Path(pl, W, new Vector2Int(pl.Col, 0), pl.ExitCell).Count > 1) reach++;
            }
            Log("plano 300 semillas: camino ascensor→salida " + mn + "–" + mx + " celdas (media " + (sum / 300f).ToString("F0") + ", v7.10.1 ~" + CorridorPlan.OldAvgPathCells + " → ×" + (sum / 300f / CorridorPlan.OldAvgPathCells).ToString("F1") + "), alcanzable " + reach + "/300, transversales de media " + (cross / 300f).ToString("F1") + ", reintentos medios " + (att / 300f).ToString("F1"));
        }
        gb.useRandomSeed = false; gb.seed = Seed;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(9);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; CorridorWorld w = CorridorWorld.Active;
        if (w == null) { Log("SIN PASILLO"); yield break; }
        Health.DisableForTests = true; CorridorRed.ForceRed = 0f;
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        CorridorPlan plan = w.plan; float cs = CorridorWorld.CS;
        Log("semilla " + gb.Generator.UsedSeed + ": " + plan.Mains + " pasillos largos de " + plan.rows + " módulos + " + plan.CrossCount + " transversales de " + CorridorPlan.CrossModules + " (" + plan.DeadEnds + " sin salida; " + w.TotalModules + " módulos, " + (w.TotalModules * cs / 1000f).ToString("F2") + " km de pasillo), " + w.LinkCount + " puertas que se abren, " + plan.Count(CorridorPlan.Door.Bricked) + " tapiadas; salida en pasillo " + plan.exitRun + " módulo " + plan.exitRow + "; camino " + plan.PathCells + " celdas (" + (plan.PathCells * cs).ToString("F0") + " m)");
        // llaves
        ItemPickup goodKey = null; ItemType goodType = ExitLock.Active != null ? ExitLock.Active.CorrectKey : ItemType.Key0; List<Vector2Int> keyCells = new List<Vector2Int>();
        foreach (ItemPickup ip in ItemPickup.Active) if (ip != null && ItemDefs.IsKey(ip.type)) { keyCells.Add(OpenNear(plan, W, CellOf(w, ip.transform.position))); if (ExitLock.Active != null && ip.type == ExitLock.Active.CorrectKey) goodKey = ip; }
        Vector2Int start = new Vector2Int(plan.Col, 0), gk = goodKey != null ? OpenNear(plan, W, CellOf(w, goodKey.transform.position)) : start;
        List<Vector2Int> toKey = Path(plan, W, start, gk), toExit = Path(plan, W, gk, plan.ExitCell);
        string kd = ""; foreach (Vector2Int kc in keyCells) kd += " " + Mathf.Max(0, plan.Dist[kc.x, kc.y]);
        Log("llaves " + keyCells.Count + " a" + kd + " celdas del ascensor (llave buena " + (goodKey != null ? goodKey.type.ToString() : "?") + "); camino inicio→llave " + toKey.Count + " celdas, llave→salida " + toExit.Count + " celdas, total " + ((toKey.Count + toExit.Count) * cs).ToString("F0") + " m");
        if (!BotOnly)
        {
            // --- plano cenital ---
            Texture2D map = DrawMap(w, W, toKey, toExit, keyCells, gk); Save(map, "mapa_plano"); Object.DestroyImmediate(map);
            CorridorWorld.CullForTests = false; yield return 0.3f;
            Vector3 mc = w.levelRoot.TransformPoint(new Vector3(W * cs * 0.5f, 0f, N * cs * 0.5f));
            OrthoShot("mapa_cenital", w.levelRoot, mc, W * cs + 4f, N * cs + 4f, 1400);
            // --- cruce: pasillo largo con transversales a los dos lados en la misma fila ---
            int ji = -1, jr = -1;
            for (int i = 1; i < plan.Mains - 1 && ji < 0; i++) for (int k = 4; k < plan.rows - 4; k++) if (plan.runs[i].doors[0, k] == CorridorPlan.Door.Link && plan.runs[i].doors[1, k] == CorridorPlan.Door.Link) { ji = i; jr = k; break; }
            if (ji < 0) for (int i = 0; i < plan.Mains && ji < 0; i++) for (int k = 4; k < plan.rows - 4; k++) if (plan.runs[i].doors[1, k] == CorridorPlan.Door.Link) { ji = i; jr = k; break; }
            Log("cruce: pasillo " + ji + " módulo " + jr + (plan.runs[ji].doors[0, jr] == CorridorPlan.Door.Link ? " (cuatro direcciones)" : " (tres direcciones)"));
            Vector3 jc = w.CellWorld(plan.ColOf(ji), jr);
            OrthoShot("cruce_cenital", w.levelRoot, jc, 64f, 40f, 1600);
            foreach (int s in new[] { 0, 1 }) { HotelDoorLeaf l = LeafNear(w, w.DoorWorld(ji, s, jr, 0f)); if (l != null) l.ForceOpen(120f); }
            foreach (int s in new[] { 0, 1 }) { int other = ji + (s == 0 ? -1 : 1); if (other >= 0 && other < plan.Mains) { HotelDoorLeaf l = LeafNear(w, w.DoorWorld(other, 1 - s, jr, 0f)); if (l != null) l.ForceOpen(120f); } }
            CorridorWorld.CullForTests = true;
            Transform F = w.FrameOf(ji);
            System.Func<int, float, float, float, Vector3> LF = (run, x, y, z) => w.FrameOf(run).TransformPoint(new Vector3(w.CenterX + x, y, z));
            p.Teleport(LF(ji, 0f, 0.1f, (jr - 2.5f) * cs), 0f); yield return 2.5f;
            Shot("cruce_pasillo", false, LF(ji, -0.95f, 1.6f, (jr - 1.3f) * cs), LF(ji, 7.5f, 1.25f, (jr + 0.5f) * cs), 1920, 1080, 78f);
            Shot("cruce_pasillo_eje", false, LF(ji, 0f, 1.6f, (jr - 2.2f) * cs), LF(ji, 0f, 1.35f, (jr + 4f) * cs), 1920, 1080, 75f);
            // de pie dentro del transversal izquierdo (o derecho), mirando a través del cruce
            int sideCross = plan.runs[ji].doors[0, jr] == CorridorPlan.Door.Link ? 0 : 1;
            Vector3 inCross = w.DoorWorld(ji, sideCross, jr, -3.2f), across = w.DoorWorld(ji, 1 - sideCross, jr, -9f);
            p.Teleport(inCross + Vector3.up * 0.1f, 0f); yield return 2.5f;
            {   // depuración: qué hay entre el transversal y el pasillo largo
                Vector3 e0 = inCross + Vector3.up * 1.6f, dir = (across - inCross).normalized;
                foreach (float lat in new[] { -1.0f, 0f, 1.0f })
                {
                    Vector3 o = e0 + Vector3.Cross(Vector3.up, dir) * lat; string hs = "";
                    RaycastHit[] hits = Physics.RaycastAll(o, dir, 12f); System.Array.Sort(hits, (x1, x2) => x1.distance.CompareTo(x2.distance));
                    foreach (RaycastHit hh in hits) hs += " " + hh.collider.name + "@" + hh.distance.ToString("F2");
                    Log("rayo transversal→cruce lat " + lat + ":" + hs);
                }
                Vector3 lq; int rq = w.RunAt(inCross, out lq); Log("inCross en pasillo " + rq + " local " + lq + "; actual " + w.CurrentRun);
                if (rq >= 0) foreach (Renderer rr in w.FrameOf(rq).GetComponentsInChildren<Renderer>(true)) if (rr.name.StartsWith("End")) Log("  " + rr.name + " " + rr.transform.position + " fwd " + rr.transform.forward + " on " + rr.enabled + " off " + rr.forceRenderingOff);
            }
            Shot("cruce_transversal", false, inCross + Vector3.up * 1.6f + (inCross - across).normalized * 0.8f, across + Vector3.up * 1.3f, 1920, 1080, 70f);
            // --- extremos (muros del fondo, vistos desde dentro) ---
            p.Teleport(LF(0, 0f, 0.1f, 8f * cs), 180f); yield return 1.5f;
            Shot("fin_sur", false, LF(0, 0f, 1.6f, 4f * cs), LF(0, 0f, 1.3f, 0f), 1280, 720);
            p.Teleport(LF(1, 0f, 0.1f, (plan.rows - 4) * cs), 0f); yield return 1.5f;
            Shot("fin_norte", false, LF(1, 0f, 1.6f, (plan.rows - 3) * cs), LF(1, 0f, 1.3f, plan.rows * cs), 1280, 720);
            Shot("fin_sur_p1", false, LF(1, 0f, 1.6f, 3f * cs), LF(1, 0f, 1.3f, 0f), 1280, 720);
            // --- pasillo largo ---
            p.Teleport(LF(0, 0f, 0.1f, 6f * cs), 0f); yield return 2.5f;
            Shot("pasillo_largo", false, LF(0, 0.2f, 1.6f, 6f * cs), LF(0, 0f, 1.45f, 120f * cs), 1920, 1080);
            int cr = plan.Mains; RunView0(w, cr, out Vector3 ce, out Vector3 cl);
            p.Teleport(ce + Vector3.up * 0.1f, 0f); yield return 2.5f;
            Shot("pasillo_transversal", false, ce + Vector3.up * 1.6f, cl + Vector3.up * 1.4f, 1920, 1080);
            // --- reflejo, ruidos y luz roja en un transversal ---
            Vector3 lp0; int ri = w.RunAt(p.transform.position, out lp0);
            Vector3 bp = CorridorEvents.Active != null ? CorridorEvents.Active.BehindPoint(8f) : Vector3.zero; Vector3 lb; int rb = w.RunAt(bp, out lb);
            Log("en el transversal " + cr + ": pasillo detectado " + ri + ", sonda en " + (w.Probe != null ? w.Probe.transform.position + " tamaño " + w.Probe.size : "-") + ", ruido a la espalda en pasillo " + rb + " (" + bp + "), pasillos dibujados " + w.VisibleRuns + "/" + w.Runs.Count);
            CorridorRed.ForceRed = 1f; yield return 1.0f;
            Shot("rojo_transversal", false, ce + Vector3.up * 1.6f, cl + Vector3.up * 1.4f, 1920, 1080);
            Log("rojo en el transversal: _C7460Red " + Shader.GetGlobalFloat(CorridorRed.RedId).ToString("F2") + " niebla " + RenderSettings.fogColor);
            CorridorRed.ForceRed = 0f; yield return 0.6f;
            // --- juego a 1920x1080 con HUD ---
            p.Teleport(LF(ji, 0f, 0.1f, (jr - 3f) * cs), 0f); yield return 3.0f;
            Shot("juego_1920x1080", true);
            // --- FPS ---
            List<string> fr = new List<string>();
            p.Teleport(LF(0, 0f, 0.1f, 2f), 0f); { IEnumerator e = Fps("inicio (mirando el pasillo largo)", fr); while (e.MoveNext()) yield return e.Current; }
            p.Teleport(LF(ji, 0f, 0.1f, (jr - 1.5f) * cs), 0f); foreach (HotelDoorLeaf l in w.OpenableDoors) if (Vector3.Distance(l.center, jc) < 4f) l.ForceOpen(30f);
            { IEnumerator e = Fps("cruce con las puertas abiertas", fr); while (e.MoveNext()) yield return e.Current; }
            CorridorWorld.CullForTests = false; yield return 0.2f;
            { IEnumerator e = Fps("cruce SIN ocultación por portales", fr); while (e.MoveNext()) yield return e.Current; }
            CorridorWorld.CullForTests = true;
        }
        // --- bot: inicio → llave buena → salida ---
        Health.DisableForTests = true; CorridorEvents.DisableForTests = true;
        p.Teleport(w.CellWorld(plan.Col, 0) + new Vector3(0f, 0.1f, 1.2f), 0f); yield return 1.0f;
        Inventory inv = Inventory.Instance;
        Time.timeScale = 2f;
        float[] res = new float[3];
        float tw0 = Time.realtimeSinceStartup;
        { IEnumerator e = Walk(p, w, toKey, res, 900f); while (e.MoveNext()) yield return e.Current; }
        if (goodKey != null)
        {   // los últimos pasos, hasta la llave (junto al zócalo)
            p.debugInput = true; float tg = 0f;
            while (tg < 4f && goodKey != null && inv != null && !inv.HasKey(goodType))
            {
                Vector3 d = goodKey.transform.position - p.transform.position; d.y = 0f;
                p.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f); p.debugMove = d.magnitude > 0.2f ? new Vector2(0f, 0.6f) : Vector2.zero;
                yield return 0.05f; tg += 0.05f;
            }
            p.debugInput = false; p.debugMove = Vector2.zero;
        }
        yield return 0.5f;
        bool hasKey = inv != null && inv.HasKey(goodType);
        Log("bot inicio→llave: llegó " + (res[0] > 0) + ", " + res[1].ToString("F0") + " m andados, atascos " + res[2] + ", llave buena recogida " + hasKey);
        float k1 = res[1];
        { IEnumerator e = Walk(p, w, toExit, res, 900f); while (e.MoveNext()) yield return e.Current; }
        Log("bot llave→salida: llegó " + (res[0] > 0) + ", " + res[1].ToString("F0") + " m andados, atascos " + res[2]);
        // ante la puerta de salida
        Vector3 ed = w.ExitDoorWorld(0.5f);
        { List<Vector2Int> last = new List<Vector2Int>(); }
        float tx = 0f;
        p.debugInput = true;
        while (tx < 8f && gm.State == GameManager.GameState.Playing)
        {
            Vector3 d = ed - p.transform.position; d.y = 0f;
            if (d.magnitude > 0.3f) { p.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f); p.debugMove = new Vector2(0f, 1f); } else p.debugMove = Vector2.zero;
            if (ExitLock.Active != null && ExitLock.Active.Near(p.transform.position)) ExitLock.Active.AtDoor(inv);
            yield return 0.05f; tx += 0.05f;
        }
        p.debugInput = false; p.debugMove = Vector2.zero;
        Time.timeScale = 1f;
        Log("bot en la salida: estado " + gm.State + ", puerta " + (ExitLock.Active != null ? (ExitLock.Active.Unlocked ? "abierta" : "cerrada") : "-") + ", total andado " + (k1 + res[1]).ToString("F0") + " m en " + (Time.realtimeSinceStartup - tw0).ToString("F0") + " s reales (×2)");
        GameSettings.Quality = qOrig;
        Health.DisableForTests = false; CorridorEvents.DisableForTests = false; CorridorRed.ForceRed = -1f;
        Log("fin");
    }
    // extremo sur del transversal i (dentro) y un punto mirando al norte del mismo
    static void RunView0(CorridorWorld w, int run, out Vector3 eye, out Vector3 look)
    {
        Transform F = w.FrameOf(run); float L = w.Runs[run].Length;
        eye = F.TransformPoint(new Vector3(w.CenterX, 0f, 1.0f)); look = F.TransformPoint(new Vector3(w.CenterX, 0f, L));
    }
}
