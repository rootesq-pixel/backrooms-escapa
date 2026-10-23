using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.3.0: capturas de Talasofobia (nivel 7). En el box con render por software (xvfb, sin -nographics) o en el PC con GPU.
//   V730_DIR=/ruta V730_W=1280 V730_H=720 Unity -batchmode -projectPath <p> -executeMethod V730Shots.Run
[InitializeOnLoad]
public static class V730Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Dir { get { return System.Environment.GetEnvironmentVariable("V730_DIR") ?? "/workspace/v730/raw"; } }
    static int W { get { return int.Parse(System.Environment.GetEnvironmentVariable("V730_W") ?? "1280"); } }
    static int H { get { return int.Parse(System.Environment.GetEnvironmentVariable("V730_H") ?? "720"); } }
    static V730Shots() { if (SessionState.GetString("smoke_phase", "") != "v730shots") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v730shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Info("ERROR " + e); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; Debug.Log("[V730Shots] FIN"); EditorApplication.Exit(0); }

    static readonly System.Text.StringBuilder info = new System.Text.StringBuilder();
    static void Info(string s) { info.AppendLine(s); Directory.CreateDirectory(Dir); File.WriteAllText(Dir + "/info.txt", info.ToString()); Debug.Log("[V730Shots] " + s); }

    static string Suffix { get { return System.Environment.GetEnvironmentVariable("V730_SUFFIX") ?? ""; } }
    static bool PerfOn { get { return System.Environment.GetEnvironmentVariable("V730_PERF") == "1"; } }

    // coste de render (mediana) a 1920x1080; con GPU real es una estimación del coste por fotograma del plano
    static double PerfMs(Camera cam, int n)
    {
        RenderTexture rt = new RenderTexture(1920, 1080, 24); Texture2D px = new Texture2D(1, 1, TextureFormat.RGB24, false);
        RenderTexture prev = cam.targetTexture; cam.targetTexture = rt;
        System.Collections.Generic.List<double> ts = new System.Collections.Generic.List<double>();
        for (int i = 0; i < n + 5; i++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            cam.Render(); RenderTexture.active = rt; px.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); px.Apply();
            if (i >= 5) ts.Add(sw.Elapsed.TotalMilliseconds);
        }
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt); Object.DestroyImmediate(px);
        ts.Sort(); return ts[ts.Count / 2];
    }

    static void Shot(Camera cam, string name)
    {
        name += Suffix;
        if (PerfOn) Info("coste " + name + ": " + PerfMs(cam, 30).ToString("F2") + " ms por render 1920x1080 (mediana de 30)");
        RenderTexture rt = new RenderTexture(W, H, 24); RenderTexture prev = cam.targetTexture;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
        Directory.CreateDirectory(Dir); File.WriteAllBytes(Dir + "/" + name + ".png", t.EncodeToPNG());
        RenderTexture.active = null; cam.targetTexture = prev; Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
        Flashlight fl = Object.FindObjectOfType<Flashlight>();
        string fli = fl == null ? "sin linterna" : ("linterna on=" + fl.IsOn + " spot=" + (fl.spot != null && fl.spot.enabled) + " I=" + (fl.spot != null ? fl.spot.intensity.ToString("F2") : "-") + " R=" + (fl.spot != null ? fl.spot.range.ToString("F1") : "-") + " fog=" + RenderSettings.fogDensity.ToString("F3") + " amb=" + RenderSettings.ambientLight);
        Info(name + " " + fli + " (" + sw.ElapsedMilliseconds + " ms) cam " + cam.transform.position.ToString("F1") + " under " + (GameManager.Instance != null && GameManager.Instance.player.IsUnderwater));
    }

    // cámara libre (sin mover al jugador): para planos de presentación por encima del agua
    static void ShotAt(Camera cam, Vector3 pos, Vector3 target, string name)
    {
        Transform ct = cam.transform; Vector3 lp = ct.localPosition; Quaternion lr = ct.localRotation;
        ct.position = pos; ct.rotation = Quaternion.LookRotation(target - pos);
        Shot(cam, name);
        ct.localPosition = lp; ct.localRotation = lr;
    }

    static void Look(FirstPersonController p, Vector3 feet, Vector3 target)
    {
        p.Teleport(feet, 0f);
        Vector3 eye = p.cameraPivot.position; Vector3 d = target - eye;
        p.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
        p.SetPitch(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
    }

    private static IEnumerator Main()
    {
        GameSettings.Quality = int.Parse(System.Environment.GetEnvironmentVariable("V730_QUALITY") ?? "2");
        GameBootstrap gb = GameBootstrap.Instance;
        float w0 = 0f;
        while ((gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) && w0 < 120f) { yield return 0.2f; w0 += 0.2f; gb = GameBootstrap.Instance; }
        ElevatorCabin.InstantForTests = true;
        gb.useRandomSeed = false; gb.seed = 7300;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(7);
        float wt = 0f;
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player;
        Camera cam = p.GetComponentInChildren<Camera>();
        OceanWorld w = OceanWorld.Active; OceanPlayer op = OceanPlayer.Local;
        GameManager.HideControlsHint = true;
        w.fish.enabled = false; w.angler.enabled = false;
        gm.lostRestartSeconds = 999f;
        Info("GPU: " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceVersion + "  calidad " + GameSettings.Quality + "  " + W + "x" + H);
        yield return 1.5f;

        // 1) salón inundado
        op.flashlight.SetOn(true);
        Look(p, new Vector3(18.6f, -0.85f, 21.3f), new Vector3(15.6f, -0.1f, 26.6f));
        yield return 1.2f; Shot(cam, "01_casa_inundada");
        op.flashlight.SetOn(false);
        // 2) mar y niebla desde el porche
        Look(p, new Vector3(15f, -0.3f, 30.6f), new Vector3(13f, 1.2f, 70f));
        yield return 1.2f; Shot(cam, "02_mar_niebla");
        // 3) barca junto a la primera casa sobre pilotes (desde el agua)
        Vector3 stop = w.BoatStops.Count > 1 ? w.BoatStops[1] : OceanWorld.BoatHome;
        w.boat.ApplyNet(new Vector2(stop.x + 1.5f, stop.z), 200f, 0f, new[] { -1, -1, -1, -1 });
        Look(p, new Vector3(15f, -0.3f, 30.6f), new Vector3(13f, 1.2f, 70f));   // jugador en el porche (fuera del agua): la cámara libre no hereda la niebla submarina
        yield return 1.5f; ShotAt(cam, new Vector3(stop.x - 6f, 2.4f, stop.z - 9f), new Vector3(stop.x + 4f, 1.2f, stop.z + 6f), "03_barca_casa_pilotes");
        // 3b) bengala en el cielo con niebla (visible desde lejos)
        OceanFlare.Spawn(0, new Vector3(stop.x, 1.5f, stop.z + 4f), Vector3.up);
        yield return 2.2f; ShotAt(cam, new Vector3(stop.x - 14f, 1.8f, stop.z - 22f), new Vector3(stop.x, 18f, stop.z + 4f), "03b_bengala");
        // 4) faro y muelle (faro encendido)
        w.lighthouse.SetOn(true);
        Look(p, new Vector3(55.2f, OceanWorld.PierY + 0.02f, 247f), new Vector3(0f, 14f, 250f));
        yield return 1.5f; Shot(cam, "04_faro_muelle");
        // 5) jaula y cascos
        Look(p, new Vector3(46f, OceanWorld.PierY + 0.02f, 249.4f), new Vector3(56f, 2.2f, 251.5f));
        yield return 1.0f; Shot(cam, "05_jaula_cascos");
        // 5b) cascos de buzo de cerca (soporte del muelle)
        {
            Vector3 rk = w.rack.transform.position;
            ShotAt(cam, rk + new Vector3(1.1f, 1.55f, -1.5f), rk + new Vector3(0f, 1.05f, 0f), "05b_cascos_cerca");
        }
        // 6) bajo el agua con El Farolero (casco y lámpara): de tres cuartos a ~9 m
        op.SetHelmet(true);
        Vector3 me = new Vector3(60f, -47f, 252f);
        w.angler.ForceForTests(new Vector3(65f, -45.8f, 256.8f), OceanAngler.AState.Patrol);
        w.angler.transform.position = new Vector3(65f, -45.8f, 256.8f);
        w.angler.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, 0.45f));
        Look(p, me, w.angler.transform.position);
        yield return 1.5f;
        w.angler.transform.position = new Vector3(65f, -45.8f, 256.8f);
        w.angler.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, 0.45f));
        Look(p, me, w.angler.transform.position);
        yield return 0.1f; Shot(cam, "06_fondo_farolero");
        // 6b) lámpara apagada: solo el señuelo y los fotóforos en la negrura
        op.flashlight.SetOn(false);
        w.angler.transform.position = new Vector3(70f, -44.5f, 263f);
        w.angler.transform.rotation = Quaternion.LookRotation(new Vector3(-0.8f, 0f, -0.6f));
        Look(p, me, w.angler.transform.position + Vector3.up * 1.5f);
        yield return 0.1f; Shot(cam, "06b_farolero_a_oscuras");
        op.flashlight.SetOn(true);
        // 7) túnel de la gruta
        Vector3 tp = OceanWorld.TunnelPath[2];
        Look(p, tp + new Vector3(2.0f, -0.9f, 0f), OceanWorld.TunnelPath[4] + Vector3.up * 1.5f);
        yield return 1.2f;
        Look(p, tp + new Vector3(2.0f, -0.9f, 0f), OceanWorld.TunnelPath[4] + Vector3.up * 1.5f);
        yield return 0.3f; Shot(cam, "07_tunel_gruta");
        if (System.Environment.GetEnvironmentVariable("V730_DEBUG") == "1")
        {
            RenderSettings.fog = false; Shot(cam, "07b_sin_niebla"); RenderSettings.fog = true;
            foreach (Light L in Object.FindObjectsOfType<Light>()) if (L.enabled && L.type == LightType.Spot) Info("spot " + L.name + " pos " + L.transform.position + " fwd " + L.transform.forward + " I " + L.intensity + " R " + L.range + " cookie " + (L.cookie != null) + " camfwd " + cam.transform.forward + " cullmask " + L.cullingMask);
            Info("cam near " + cam.nearClipPlane + " far " + cam.farClipPlane + " mask " + cam.cullingMask + " clear " + cam.clearFlags + " bg " + cam.backgroundColor);
            Color amb0 = RenderSettings.ambientLight; RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.6f); Shot(cam, "07c_ambiente"); RenderSettings.ambientLight = amb0;
            RaycastHit rh; if (Physics.Raycast(cam.transform.position, cam.transform.forward, out rh, 100f, ~0, QueryTriggerInteraction.Ignore)) Info("ray fwd -> " + rh.collider.name + " d=" + rh.distance.ToString("F2")); else Info("ray fwd -> nada");
            if (Physics.Raycast(cam.transform.position, Vector3.down, out rh, 100f, ~0, QueryTriggerInteraction.Ignore)) Info("ray down -> " + rh.collider.name + " d=" + rh.distance.ToString("F2"));
            Info("ambientMode " + RenderSettings.ambientMode + " sky " + RenderSettings.ambientSkyColor);
            Light spotL = null; foreach (Light L in Object.FindObjectsOfType<Light>()) if (L.enabled && L.type == LightType.Spot && L.name == "FlashlightSpot") spotL = L;
            if (spotL != null) { LightShadows sh = spotL.shadows; Info("spot shadows " + sh + " renderMode " + spotL.renderMode); spotL.shadows = LightShadows.None; Shot(cam, "07e_sin_sombras"); spotL.shadows = sh; }
            GameObject pl = new GameObject("DbgPoint"); pl.transform.position = cam.transform.position; Light pll = pl.AddComponent<Light>(); pll.type = LightType.Point; pll.range = 25f; pll.intensity = 3f; pll.renderMode = LightRenderMode.ForcePixel; Shot(cam, "07f_luz_punto"); Object.Destroy(pl);
            GameObject os = GameObject.Find("OceanSurface"); if (os != null) { os.GetComponent<Renderer>().enabled = false; Shot(cam, "07d_sin_superficie"); os.GetComponent<Renderer>().enabled = true; }
            foreach (Renderer rr in Object.FindObjectsOfType<Renderer>()) if (rr.enabled && rr.isVisible) Info("visible: " + rr.name + " " + rr.GetType().Name + " " + (rr.sharedMaterial != null ? rr.sharedMaterial.shader.name : "-"));
            int on = 0, tot = 0; foreach (Renderer rr in Object.FindObjectsOfType<Renderer>()) { tot++; if (rr.enabled && rr.gameObject.activeInHierarchy) on++; }
            Info("renderers activos " + on + "/" + tot);
            GameObject tun = GameObject.Find("Tunnel"); Info("Tunnel: " + (tun != null ? tun.activeInHierarchy + " " + tun.GetComponent<Renderer>().enabled + " bounds " + tun.GetComponent<Renderer>().bounds : "null"));
        }
        // 8) poza de la gruta (salida) vista desde dentro
        Look(p, OceanWorld.PocketCenter + new Vector3(-2.3f, -1.42f, 0.6f), OceanWorld.PocketCenter + new Vector3(3f, 1.2f, -1f));
        yield return 1.2f; Shot(cam, "08_poza_salida");
        Info("FIN");
    }
}
