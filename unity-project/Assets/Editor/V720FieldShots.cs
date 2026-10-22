using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.2.0: Campo infinito más realista: vistas fijas (semilla 7110) del prado, contraluz, primer plano, colinas, casas por fuera
// y por dentro, con tiempo de carga del nivel y coste de render en la GPU (mediana de 60 renders 1080p).
//   V720F_DIR=/ruta V720F_QUALITY=2 [V720F_PERF=1] [V720F_VARIANTS="nombre:GrassLook=a,b,c,d;HouseLM=x|..."] Unity -batchmode -executeMethod V720FieldShots.Run
[InitializeOnLoad]
public static class V720FieldShots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Dir { get { return System.Environment.GetEnvironmentVariable("V720F_DIR") ?? "/workspace/gpu-previews/v720f"; } }
    static V720FieldShots() { if (SessionState.GetString("smoke_phase", "") != "v720fshots") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v720fshots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V720FieldShots] " + e); Info("ERROR " + e.Message); Finish(); }
    }
    private static void Finish() { FieldGrass.Synchronous = false; SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; Debug.Log("[V720FieldShots] FIN"); EditorApplication.Exit(0); }
    static readonly System.Text.StringBuilder info = new System.Text.StringBuilder();
    static void Info(string s) { info.AppendLine(s); Directory.CreateDirectory(Dir); File.WriteAllText(Dir + "/info.txt", info.ToString()); Debug.Log("[V720FieldShots] " + s); }

    static void Render(Camera cam)
    {
        if (FieldGrass.Active != null) FieldGrass.Active.Submit();
        if (FieldWorld.Active != null) FieldWorld.Active.DrawGhosts();
        cam.Render();
    }
    static void Shot(Camera cam, string name, int w = 1920, int h = 1080)
    {
        RenderTexture rt = new RenderTexture(w, h, 24) { antiAliasing = QualitySettings.antiAliasing > 0 ? QualitySettings.antiAliasing : 1 }; RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt; Render(cam); RenderTexture.active = rt;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        Directory.CreateDirectory(Dir); File.WriteAllBytes(Dir + "/" + name + ".png", t.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
    }
    static double PerfMs(Camera cam, int n)
    {
        RenderTexture rt = new RenderTexture(1920, 1080, 24); Texture2D px = new Texture2D(1, 1, TextureFormat.RGB24, false);
        RenderTexture prev = cam.targetTexture; cam.targetTexture = rt;
        System.Collections.Generic.List<double> ts = new System.Collections.Generic.List<double>();
        // las órdenes DrawMesh*/Indirect se acumulan hasta el fin del fotograma: se encolan UNA vez y todos los render las dibujan
        if (FieldGrass.Active != null) FieldGrass.Active.Submit();
        if (FieldWorld.Active != null) FieldWorld.Active.DrawGhosts();
        for (int i = 0; i < n + 5; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            cam.Render(); RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply();
            if (i >= 5) ts.Add(sw.Elapsed.TotalMilliseconds);
        }
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt);
        ts.Sort(); return ts[ts.Count / 2];
    }
    struct View { public string n; public Vector3 p, t; public float fov; public View(string n, Vector3 p, Vector3 t, float fov) { this.n = n; this.p = p; this.t = t; this.fov = fov; } }

    private static IEnumerator Main()
    {
        int q = int.Parse(System.Environment.GetEnvironmentVariable("V720F_QUALITY") ?? "2");
        bool perfOn = System.Environment.GetEnvironmentVariable("V720F_PERF") == "1";
        string suf = System.Environment.GetEnvironmentVariable("V720F_SUFFIX") ?? (q == 2 ? "" : "_q" + q);
        GameSettings.Quality = q;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        gb.useRandomSeed = false; gb.seed = 7110;
        int ld = gb.LoadCount; float tl = Time.realtimeSinceStartup;
        UIManager.Instance.StartGame(5);
        float wt = 0f;
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        Info("carga nivel 5: " + (Time.realtimeSinceStartup - tl).ToString("F1") + " s (con menú y fundidos) | por pasos " + LoadDiag.LastTotalSeconds.ToString("F2") + " s | " + LoadDiag.Report);
        if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
        float w = 0f; while (ElevatorCabin.PlayerLocked && w < 20f) { yield return 0.25f; w += 0.25f; }
        GameManager gm = GameManager.Instance; FirstPersonController pw = gm.player;
        Camera cam = pw.GetComponentInChildren<Camera>();
        GameManager.HideControlsHint = true;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        Flashlight fl = pw.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(false);
        cam.cullingMask &= ~(1 << 8);
        FieldGrass.Synchronous = true;
        yield return 1.5f;
        FieldWorld fw = FieldWorld.Active;
        if (fw == null) { Info("sin campo"); yield break; }
        Transform root = gm.generator.Root;
        Info("calidad " + q + ", GPU " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + " | casas horneadas " + FieldWorld.HouseBaked + " | hierba " + FieldGrass.Look);
        System.Func<float, float, float, Vector3> P = (x, z, up) => root.TransformPoint(new Vector3(x, FieldHeight.H(x, z) + up, z));
        // casas: una cerrada y una abierta, vista desde 10 m delante de la fachada
        var views = new System.Collections.Generic.List<View>();
        FieldPlan.House? hc = null, ho = null;
        foreach (FieldPlan.House h in fw.plan.houses) { if (h.enterable && ho == null) ho = h; if (!h.enterable && hc == null) hc = h; }
        Vector3 sunDir = RenderSettings.sun != null ? RenderSettings.sun.transform.forward : Vector3.down;
        Vector3 start = root.InverseTransformPoint(pw.transform.position);
        {
            Vector3 a = new Vector3(start.x, 0, start.z); Vector3 d = new Vector3(0.6f, 0f, 0.8f);
            views.Add(new View("campo_vista", P(a.x + d.x * 8f, a.z + d.z * 8f, 1.65f), P(a.x + d.x * 38f, a.z + d.z * 38f, 1.2f), 60f));   // fuera del ascensor
            Vector3 sd = new Vector3(sunDir.x, 0f, sunDir.z).normalized;   // mirando hacia el sol (contraluz)
            views.Add(new View("campo_contraluz", P(a.x + 6f, a.z, 0.9f), P(a.x + 6f - sd.x * 20f, a.z - sd.z * 20f, 1.6f), 60f));
            views.Add(new View("campo_cerca", P(a.x + 3f, a.z + 5f, 1.5f), P(a.x + 4.5f, a.z + 7f, 0.1f), 60f));
            views.Add(new View("campo_colinas", P(a.x - 10f, a.z - 8f, 3.2f), P(a.x + 40f, a.z + 50f, 0f), 60f));
        }
        foreach (var hh in new[] { hc, ho })
        {
            if (hh == null) continue;
            FieldPlan.House h = hh.Value; Quaternion rot = Quaternion.Euler(0f, h.rot * 90f, 0f);
            Vector2 sz = FieldPlan.TemplateSize[h.template];
            Vector3 c = new Vector3(h.pos.x, 0f, h.pos.y), fwd = rot * Vector3.forward, right = rot * Vector3.right;
            Vector3 eye = c + fwd * (sz.y * 0.5f + 8.5f) + right * 3.5f;
            string tag = h.enterable ? "abierta" : "cerrada";
            views.Add(new View("casa_" + tag, P(eye.x, eye.z, 1.65f), P(c.x, c.z, 1.6f), 60f));
            if (h.enterable)
            {
                Vector3 back = c - fwd * (sz.y * 0.5f - 0.6f) + right * 0.3f;
                Vector3 hy = root.TransformPoint(new Vector3(0f, FieldHeight.H(c.x, c.z), 0f));
                Transform hgo = null; foreach (Transform t in fw.transform.Find("Houses")) if ((t.localPosition - new Vector3(h.pos.x, t.localPosition.y, h.pos.y)).sqrMagnitude < 0.01f) hgo = t;
                float fy = hgo != null ? hgo.position.y : hy.y;
                Vector3 e2 = root.TransformPoint(new Vector3(back.x, 0f, back.z)); e2.y = fy + 1.7f;
                Vector3 t2 = root.TransformPoint(new Vector3(c.x + fwd.x * sz.y, 0f, c.z + fwd.z * sz.y)); t2.y = fy + 1.1f;
                views.Add(new View("casa_dentro", e2, t2, 70f));
                Vector3 e3 = root.TransformPoint(new Vector3(c.x + fwd.x * (sz.y * 0.5f + 3f) + right.x * (DoorXw(h) ), 0f, c.z + fwd.z * (sz.y * 0.5f + 3f) + right.z * DoorXw(h))); e3.y = fy + 1.6f;
                Vector3 t3 = root.TransformPoint(new Vector3(c.x - fwd.x * 2f + right.x * DoorXw(h), 0f, c.z - fwd.z * 2f + right.z * DoorXw(h))); t3.y = fy + 1.2f;
                views.Add(new View("casa_puerta", e3, t3, 60f));
            }
        }
        string vs = System.Environment.GetEnvironmentVariable("V720F_VARIANTS");
        string[] variants = string.IsNullOrEmpty(vs) ? new[] { ":" } : vs.Split('|');
        Vector4 look0 = FieldGrass.Look; float hl0 = FieldWorld.HouseLMScale;
        for (int vi = 0; vi < variants.Length; vi++)
        {
            string vn = variants[vi].Split(':')[0], spec = variants[vi].Length > vn.Length ? variants[vi].Substring(vn.Length + 1) : "";
            FieldGrass.Look = look0; SetHouseLM(hl0);
            foreach (string part in spec.Split(';'))
            {
                int e = part.IndexOf('='); if (e <= 0) continue;
                string k = part.Substring(0, e).Trim(); string[] f = part.Substring(e + 1).Split(',');
                float[] v = new float[4]; for (int i = 0; i < 4; i++) v[i] = i < f.Length ? float.Parse(f[i], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                if (k == "GrassLook") FieldGrass.Look = new Vector4(v[0], v[1], v[2], v[3]);
                else if (k == "HouseLM") SetHouseLM(v[0]);
                else if (k == "Exposure") PostStack.Exposure = v[0];
            }
            FieldGrass.ApplyLook();
            string vsuf = suf + (vn.Length > 0 ? "_" + vn : "");
            if (vn.Length > 0) Info("variante " + vn + ": " + spec);
            foreach (View v in views)
            {
                pw.Teleport(new Vector3(v.p.x, v.p.y - 1.6f, v.p.z), Mathf.Atan2(v.t.x - v.p.x, v.t.z - v.p.z) * Mathf.Rad2Deg);
                for (int k = 0; k < 25; k++) { cam.transform.position = v.p; cam.transform.rotation = Quaternion.LookRotation(v.t - v.p); cam.fieldOfView = v.fov; yield return 0.04f; }
                cam.transform.position = v.p; cam.transform.rotation = Quaternion.LookRotation(v.t - v.p); cam.fieldOfView = v.fov;
                Shot(cam, v.n + vsuf);
                if (perfOn && vi == 0)
                {
                    yield return 0.04f;   // fotograma nuevo: sin las órdenes de dibujo de la captura
                    cam.transform.position = v.p; cam.transform.rotation = Quaternion.LookRotation(v.t - v.p); cam.fieldOfView = v.fov;
                    Info("coste " + v.n + suf + ": " + PerfMs(cam, 60).ToString("F2") + " ms por render 1920x1080 (mediana)");
                }
            }
        }
        Info("FIN");
    }
    static float DoorXw(FieldPlan.House h) { return FieldWorld.DoorX(h.template); }
    static void SetHouseLM(float s)
    {
        FieldWorld.HouseLMScale = s;
        foreach (MeshRenderer r in Object.FindObjectsOfType<MeshRenderer>())
            if (r.name.StartsWith("House_")) foreach (Material m in r.sharedMaterials) if (m != null && m.HasProperty("_LMScale") && m.GetTexture("_LightMap") != Texture2D.whiteTexture) m.SetFloat("_LMScale", s);
    }
}
