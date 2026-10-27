using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.9.2: capturas (xvfb con gráficos) del todoterreno en 3.ª persona con el conductor, de las rodadas y de la arena del
// Nivel 114 a varias distancias del ascensor, y del HUD en el coche a varias resoluciones. Salida en $V792_OUT.
[InitializeOnLoad]
public static class V792Preview
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V792_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v792/shots" : o; } }
    static V792Preview() { if (SessionState.GetString("smoke_phase", "") != "v792prev") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v792prev");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V792Preview] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V792Preview] " + s); }

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
    // escena (cámara del juego o una colocada a mano) + HUD (opcional)
    private static void Shot(string name, int w, int h, bool hud, System.Action<Camera> place = null)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        Vector3 p0 = game.transform.position; Quaternion r0 = game.transform.rotation;
        if (place != null) place(game);
        RenderTexture o = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = o;
        game.transform.position = p0; game.transform.rotation = r0;
        if (hud) { SetupUi(); Canvas.ForceUpdateCanvases(); uiCam.targetTexture = rt; uiCam.Render(); uiCam.targetTexture = null; }
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
        Log("captura " + name + " " + w + "x" + h);
    }

    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", "");
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        gb.useRandomSeed = false; gb.seed = 12345;
        int ld = gb.LoadCount; int lv = 0; for (int i = 0; i < LevelConfig.Count; i++) if (LevelConfig.Get(i).layout == LayoutKind.Desert) lv = i;
        UIManager.Instance.StartGame(lv);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 12.5f;
        if (Health.Instance != null) Health.DisableForTests = true;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; DesertWorld w = DesertWorld.Active; DesertCars dc = DesertCars.Active;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        Flashlight fl = p.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(false);
        Inventory.Instance.Add(ItemType.Battery, 2); Inventory.Instance.Add(ItemType.AlmondWater, 1);
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        System.Func<float, float, float, Vector3> P = (x, z, up) => w.levelRoot.TransformPoint(new Vector3(x, DesertHeight.H(Mathf.Repeat(x, w.L), Mathf.Repeat(z, w.L)) + up, z));
        Vector3 s = w.StartLocal; float yaw = w.StartYaw * Mathf.Deg2Rad; Vector3 f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

        // ---- arena a varias distancias del ascensor (de pie, mediodía), mirando hacia delante y algo abajo
        float[] ds = { 8f, 200f, 500f, 1100f };
        string[] dn = { "terreno_ascensor", "terreno_200m", "terreno_500m", "terreno_1100m_dunas" };
        for (int k = 0; k < ds.Length; k++)
        {
            Vector3 q = s + f * ds[k] + new Vector3(f.z, 0f, -f.x) * (k * 7f);
            p.Teleport(P(q.x, q.z, 0.3f), w.StartYaw); yield return 2.5f;
            Vector3 lp = w.levelRoot.InverseTransformPoint(p.transform.position);
            Vector3 eye = P(lp.x, lp.z, 1.7f), look = P(lp.x + f.x * 40f, lp.z + f.z * 40f, -2f);
            Shot(dn[k], 1920, 1080, false, c => { c.transform.position = eye; c.transform.rotation = Quaternion.LookRotation(look - eye); });
        }

        // ---- todoterreno: subir, conducir unos segundos (rodadas), parar
        DesertCar car = dc.Drivable;
        if (car == null) { Log("SIN COCHE"); yield break; }
        p.Teleport(car.transform.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.6f;
        DesertCars.DebugInteract = true; yield return 0.1f; yield return 2.0f;
        Log("montado " + p.IsMounted + " conductor " + (DesertCars.LocalDriverCar != null));
        DesertCars.DebugDrive = true; DesertCars.DbgHandbrake = false; DesertCars.DbgThrottle = 1f; DesertCars.DbgSteer = 0.25f; yield return 5.0f;
        DesertCars.DbgThrottle = 0f; DesertCars.DbgSteer = 0f; DesertCars.DbgHandbrake = true; yield return 3.0f;
        DesertCars.CamView = 1; yield return 1.5f;
        Shot("coche_3a_persona_hud", 1920, 1080, true);
        // de lado y un poco por delante, para ver al conductor por la ventanilla
        Transform ct = car.transform; float side = car.driverSide;
        Vector3 e1 = ct.TransformPoint(new Vector3(side * 3.6f, 1.75f, 1.6f)), t1 = ct.TransformPoint(new Vector3(side * 0.4f, 1.35f, 0.1f));
        Shot("coche_conductor_lado", 1920, 1080, false, c => { c.transform.position = e1; c.transform.rotation = Quaternion.LookRotation(t1 - e1); });
        Vector3 e2 = ct.TransformPoint(new Vector3(side * 1.2f, 1.9f, 5.2f)), t2 = ct.TransformPoint(new Vector3(side * 0.3f, 1.3f, 0.1f));
        Shot("coche_conductor_frente", 1920, 1080, false, c => { c.transform.position = e2; c.transform.rotation = Quaternion.LookRotation(t2 - e2); });
        // rodadas: detrás del coche, mirando hacia atrás y abajo
        Vector3 e3 = ct.TransformPoint(new Vector3(-side * 2.2f, 1.6f, -3.0f)), t3 = ct.TransformPoint(new Vector3(0f, 0f, -11f));
        Shot("rodadas", 1920, 1080, false, c => { c.transform.position = e3; c.transform.rotation = Quaternion.LookRotation(t3 - e3); });
        // ---- HUD en el coche a varias resoluciones (aviso de controles y barra rápida)
        int[,] res = { { 1920, 1080 }, { 1280, 720 }, { 1366, 768 }, { 2560, 1440 }, { 1024, 768 }, { 800, 600 }, { 1280, 1024 }, { 1680, 1050 }, { 2560, 1080 }, { 3440, 1440 } };
        for (int i = 0; i < res.GetLength(0); i++) { Shot("hud_coche_" + res[i, 0] + "x" + res[i, 1], res[i, 0], res[i, 1], true); yield return 0.2f; }
        DesertCars.CamView = 0; yield return 1.0f;
        Shot("coche_1a_persona_hud", 1920, 1080, true);
        DesertCars.DebugDrive = false;
        DesertSky.ForceWeather = -1; DesertSky.ForceHour = -1f; Health.DisableForTests = false;
        Log("FIN");
    }
}
