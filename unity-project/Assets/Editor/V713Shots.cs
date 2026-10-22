using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.1.3: agua y suelo de los Poolrooms frente a los renders de Blender (pool-render/build.py): las 3 cámaras de Blender
// (Cam_Wide, Cam_Low, Cam_Top) llevadas a la sala del juego (escala sx/sz de la sala horneada, alturas sobre la cubierta,
// FOV vertical de la lente a 16:9) + tiempo de render en la GPU.
//   V713_DIR=/ruta V713_QUALITY=2 [V713_PERF=1] Unity -batchmode -executeMethod V713Shots.Run   (con gráficos, sin -nographics)
[InitializeOnLoad]
public static class V713Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Dir { get { return System.Environment.GetEnvironmentVariable("V713_DIR") ?? "/workspace/gpu-previews/v713"; } }
    static V713Shots() { if (SessionState.GetString("smoke_phase", "") != "v713shots") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v713shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V713Shots] " + e); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; Debug.Log("[V713Shots] FIN"); EditorApplication.Exit(0); }

    static readonly System.Text.StringBuilder info = new System.Text.StringBuilder();
    static void Info(string s) { info.AppendLine(s); Directory.CreateDirectory(Dir); File.WriteAllText(Dir + "/info.txt", info.ToString()); Debug.Log("[V713Shots] " + s); }

    static void Shot(Camera cam, string name, int w = 1920, int h = 1080)
    {
        RenderTexture rt = new RenderTexture(w, h, 24); RenderTexture prev = cam.targetTexture;
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply();
        Directory.CreateDirectory(Dir); File.WriteAllBytes(Dir + "/" + name + ".png", t.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
    }
    static PoolPlanarReflection planar;
    static double PerfMs(Camera cam, int n)
    {
        RenderTexture rt = new RenderTexture(1920, 1080, 24); Texture2D px = new Texture2D(1, 1, TextureFormat.RGB24, false);
        RenderTexture prev = cam.targetTexture; cam.targetTexture = rt;
        System.Collections.Generic.List<double> ts = new System.Collections.Generic.List<double>();
        for (int i = 0; i < n + 5; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (planar != null) { cam.targetTexture = null; planar.Refresh(cam); cam.targetTexture = rt; }
            cam.Render(); RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply();
            if (i >= 5) ts.Add(sw.Elapsed.TotalMilliseconds);
        }
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt);
        ts.Sort(); return ts[ts.Count / 2];
    }

    // Blender (x, y, z sobre la cubierta) -> local de la malla horneada: (x*sx, z + cubierta, y*sz)
    const float SX = (16f - 0.2f) / 14f, SZ = (12f - 0.2f) / 10f, DECK = 0.28f;
    static Vector3 B(float x, float y, float z) { return new Vector3(x * SX, z + DECK, y * SZ); }
    static float VFov(float lensMm) { return 2f * Mathf.Atan(Mathf.Tan(Mathf.Atan(18f / lensMm)) * 9f / 16f) * Mathf.Rad2Deg; }
    struct View { public string n; public Vector3 p, t; public float fov; public View(string n, Vector3 p, Vector3 t, float fov) { this.n = n; this.p = p; this.t = t; this.fov = fov; } }

    private static IEnumerator Main()
    {
        int q = int.Parse(System.Environment.GetEnvironmentVariable("V713_QUALITY") ?? "2");
        bool perfOn = System.Environment.GetEnvironmentVariable("V713_PERF") == "1";
        string suf = System.Environment.GetEnvironmentVariable("V713_SUFFIX") ?? (q == 2 ? "" : "_q" + q);
        GameSettings.Quality = q;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        gb.useRandomSeed = false; gb.seed = 7110;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(2);
        float wt = 0f;
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
        float w = 0f; while (ElevatorCabin.PlayerLocked && w < 20f) { yield return 0.25f; w += 0.25f; }
        GameManager gm = GameManager.Instance; FirstPersonController pw = gm.player;
        Camera cam = pw.GetComponentInChildren<Camera>();
        GameManager.HideControlsHint = true;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        Flashlight fl = pw.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(false);
        yield return 1.5f;
        if (PoolRoomSetPiece.Root == null) { Info("sin sala de piscina"); yield break; }
        Transform root = PoolRoomSetPiece.Root.transform; Transform bk = root.Find("PoolRoomBaked"); if (bk != null) root = bk;
        WaterVolume wv = WaterVolume.Active;
        float wl = wv != null ? root.InverseTransformPoint(new Vector3(0f, wv.surfaceY, 0f)).y : float.NaN;
        Info("calidad " + q + ", GPU " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + ", agua local y=" + wl.ToString("F3") + " (cubierta " + DECK + "; Blender: agua 0,08 m bajo la cubierta)");
        cam.cullingMask &= ~(1 << 8);
        float lowY = (float.IsNaN(wl) ? DECK - 0.08f : wl) + 0.08f;   // Cam_Low: 8 cm sobre el agua, como en Blender
        View[] views = {
            new View("pool_1", B(-6.3f, -4.4f, 1.65f), B(1.5f, 0.8f, -0.4f), VFov(20f)),
            new View("pool_2", new Vector3(-3.7f * SX, lowY, 0.9f * SZ), new Vector3(2f * SX, lowY - 0.2f, -0.4f * SZ), VFov(24f)),
            new View("pool_3", B(-1.8f, -0.9f, 2.6f), B(-0.6f, 0.1f, -1.4f), VFov(28f)),
            new View("under", new Vector3(-2.5f * SX, lowY - 0.75f, -0.6f * SZ), new Vector3(2.5f * SX, lowY - 1.0f, 0.6f * SZ), 60f),   // cáusticas bajo el agua
        };
        // variantes de ajuste: V713_VARIANTS="nombre:Clave=a,b,c,d;Clave=...|nombre2:..." (PoolLook.Override)
        string vs = System.Environment.GetEnvironmentVariable("V713_VARIANTS");
        string[] variants = string.IsNullOrEmpty(vs) ? new[] { ":" } : vs.Split('|');
        MeshRenderer bmr = root.GetComponent<MeshRenderer>();
        planar = PoolRoomSetPiece.Root.GetComponent<PoolPlanarReflection>();
        Info("PoolLook por defecto: tinte " + PoolLook.WaterTint + " abs " + PoolLook.WaterAbs + " disp " + PoolLook.WaterScatter + " ondas " + PoolLook.WaveP + " cáust " + PoolLook.CausticK + " playa " + PoolLook.DeckP + " ventanas " + PoolLook.WindowEmission);
        Color l0 = PoolLook.LMTint, wt0 = PoolLook.WallTint; Vector4 ed0 = PoolLook.EdgeP; float ts0 = PoolLook.PoolTileScale; Vector4 k0 = PoolLook.WaveK, t0 = PoolLook.WaterTint, a0 = PoolLook.WaterAbs, s0 = PoolLook.WaterScatter, w0 = PoolLook.WaveP, c0 = PoolLook.CausticK, d0 = PoolLook.DeckP; Color e0 = PoolLook.WindowEmission;
        for (int vi = 0; vi < variants.Length; vi++)
        {
        string vn = variants[vi].Split(':')[0], spec = variants[vi].Length > vn.Length ? variants[vi].Substring(vn.Length + 1) : "";
        PoolLook.WaterTint = t0; PoolLook.WaterAbs = a0; PoolLook.WaterScatter = s0; PoolLook.WaveP = w0; PoolLook.CausticK = c0; PoolLook.DeckP = d0; PoolLook.WindowEmission = e0; PoolLook.LMTint = l0; PoolLook.WaveK = k0; PoolLook.WallTint = wt0; PoolLook.EdgeP = ed0; PoolLook.PoolTileScale = ts0;
        PoolLook.Override(spec); PoolLook.Apply();
        if (bmr != null) PoolLook.ApplyMaterials(bmr.sharedMaterials);
        string vsuf = suf + (vn.Length > 0 ? "_" + vn : "");
        if (vn.Length > 0) Info("variante " + vn + ": " + spec);
        foreach (View v in views)
        {
            Vector3 wp = root.TransformPoint(v.p), wtg = root.TransformPoint(v.t);
            pw.Teleport(root.TransformPoint(B(-6.2f, -4.3f, 0.02f)),   // jugador en una esquina de la playa (sin ondas de chapuzón en las capturas)
                 Mathf.Atan2(wtg.x - wp.x, wtg.z - wp.z) * Mathf.Rad2Deg);
            for (int k = 0; k < 20; k++) { cam.transform.position = wp; cam.transform.rotation = Quaternion.LookRotation(wtg - wp); cam.fieldOfView = v.fov; yield return 0.04f; }
            cam.transform.position = wp; cam.transform.rotation = Quaternion.LookRotation(wtg - wp); cam.fieldOfView = v.fov;
            if (planar != null) planar.Refresh(cam);
            Shot(cam, v.n + vsuf);
            if (vi == 0)
            {   // depuración: textura del reflejo planar tal como la ve el agua
                RenderTexture prt = Shader.GetGlobalTexture("_PoolPlanarTex") as RenderTexture;
                if (prt != null && Shader.GetGlobalFloat("_PoolPlanarOn") > 0.5f)
                {
                    RenderTexture tmp = RenderTexture.GetTemporary(prt.width, prt.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(prt, tmp); RenderTexture.active = tmp;
                    Texture2D tx = new Texture2D(prt.width, prt.height, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, prt.width, prt.height), 0, 0); tx.Apply();
                    File.WriteAllBytes(Dir + "/planar_" + v.n + ".png", tx.EncodeToPNG()); RenderTexture.active = null; RenderTexture.ReleaseTemporary(tmp); Object.DestroyImmediate(tx);
                }
                else Info("planar apagado en " + v.n);
            }
            if (perfOn && vi == 0) Info("coste " + v.n + suf + ": " + PerfMs(cam, 60).ToString("F2") + " ms por render 1920x1080 (mediana)");
        }
        }
        // secuencia corta de la ola (Cam_Wide), 6 fotogramas a 0,25 s (última variante)
        {
            View v = views[0]; Vector3 wp = root.TransformPoint(v.p), wtg = root.TransformPoint(v.t);
            for (int k = 0; k < 6; k++)
            {
                cam.transform.position = wp; cam.transform.rotation = Quaternion.LookRotation(wtg - wp); cam.fieldOfView = v.fov;
                yield return 0.25f;
                cam.transform.position = wp; cam.transform.rotation = Quaternion.LookRotation(wtg - wp); cam.fieldOfView = v.fov;
                if (planar != null) planar.Refresh(cam);
                Shot(cam, "seq_" + k + suf, 960, 540);
            }
        }
        Info("FIN");
    }
}
