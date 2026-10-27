using System.Collections;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.9.2: capturas (xvfb con gráficos) del todoterreno en 3.ª persona con el conductor, de las rodadas y de la arena del
// Nivel 114 a varias distancias del ascensor, y del HUD en el coche a varias resoluciones. Salida en $V794_OUT.
[InitializeOnLoad]
public static class V794Look
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V794_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v794/shots" : o; } }
    static V794Look() { if (SessionState.GetString("smoke_phase", "") != "v794look") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v794look");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V794Look] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V794Look] " + s); }

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
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
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
        bool oldF = System.Environment.GetEnvironmentVariable("V794_OLDFOLLOW") == "1";
        DesertCarCam.DebugOldFollow = oldF;
        GameSettings.Quality = 2;
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
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; DesertCars dc = DesertCars.Active; DesertWorld dw = DesertWorld.Active;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        DesertCar car = dc.Drivable;
        if (car == null) { Log("SIN COCHE"); yield break; }
        Log("calidad " + GameSettings.Quality + " MSAA " + QualitySettings.antiAliasing + " aniso " + QualitySettings.anisotropicFiltering + " seguimiento antiguo " + oldF);
        p.Teleport(car.transform.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.6f;
        DesertCars.DebugInteract = true; yield return 0.1f; yield return 2.0f;
        DesertCars.DebugDrive = true; DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true; DesertCars.DbgSteer = 0f;
        DesertCars.CamView = 0; yield return 1.5f;
        PlayerBody pb = p.GetComponentInChildren<PlayerBody>();
        foreach (bool old in new[] { true, false })
        {   // primera persona: arrancar recto sin tocar el volante
            PlayerBody.DebugOldWheelArm = old; string tag = old ? "antes" : "ahora";
            DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true; yield return 1.5f;
            Shot("manos_" + tag + "_parado", 1920, 1080, false);
            Quaternion r0 = pb.WheelHandR, l0 = pb.WheelHandL; Vector3 pr0 = pb.WheelHandPosR, pl0 = pb.WheelHandPosL;
            float mr = 0f, ml = 0f, mpr = 0f, mpl = 0f, st = 0f;
            DesertCars.DbgHandbrake = false; DesertCars.DbgThrottle = 1f;
            double th0 = EditorApplication.timeSinceStartup; bool shot = false;
            while (EditorApplication.timeSinceStartup - th0 < 3.0)
            {
                yield return null;
                mr = Mathf.Max(mr, Quaternion.Angle(r0, pb.WheelHandR)); ml = Mathf.Max(ml, Quaternion.Angle(l0, pb.WheelHandL));
                mpr = Mathf.Max(mpr, Vector3.Distance(pr0, pb.WheelHandPosR)); mpl = Mathf.Max(mpl, Vector3.Distance(pl0, pb.WheelHandPosL));
                st = Mathf.Max(st, Mathf.Abs(car.steerAngle));
                if (!shot && EditorApplication.timeSinceStartup - th0 > 1.2) { Shot("manos_" + tag + "_arrancando", 1920, 1080, false); shot = true; }
            }
            Log("manos " + tag + ": volante " + st.ToString("F2") + "°, velocidad " + (car.Speed * 3.6f).ToString("F0") + " km/h; mano dcha. giro máx. " + mr.ToString("F1") + "° y " + (mpr * 100f).ToString("F1") + " cm; mano izq. " + ml.ToString("F1") + "° y " + (mpl * 100f).ToString("F1") + " cm (respecto al volante)");
            DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true; yield return 2.5f;
            {   // girar el volante parado: las dos manos siguen al aro (mismo giro respecto al coche)
                System.Func<Quaternion, Quaternion> toCar = q => Quaternion.Inverse(car.transform.rotation) * car.steering.rotation * q;
                Quaternion cr0 = toCar(pb.WheelHandR), cl0 = toCar(pb.WheelHandL); Quaternion sr0 = pb.WheelHandR, sl0 = pb.WheelHandL;
                DesertCars.DbgSteer = 0.6f; yield return 1.2f;
                Log("manos " + tag + " con el volante girado (" + car.steerAngle.ToString("F1") + "°): respecto al coche dcha. " + Quaternion.Angle(cr0, toCar(pb.WheelHandR)).ToString("F1") + "°, izq. " + Quaternion.Angle(cl0, toCar(pb.WheelHandL)).ToString("F1") + "°; respecto al volante dcha. " + Quaternion.Angle(sr0, pb.WheelHandR).ToString("F1") + "°, izq. " + Quaternion.Angle(sl0, pb.WheelHandL).ToString("F1") + "°");
                Shot("manos_" + tag + "_girando", 1920, 1080, false);
                DesertCars.DbgSteer = 0f; yield return 1.2f;
            }
            car.DebugPlace(dw.levelRoot.InverseTransformPoint(car.transform.position) - car.transform.forward * 0f, car.transform.eulerAngles.y - dw.levelRoot.eulerAngles.y); yield return 0.5f;
        }
        PlayerBody.DebugOldWheelArm = false;
        DesertCars.DebugDrive = true; DesertCars.DbgThrottle = 0.7f; DesertCars.DbgHandbrake = false; DesertCars.DbgSteer = 0f;
        DesertCars.CamView = 1; yield return 2.5f;
        DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true; yield return 2.5f;
        foreach (float hr in new[] { 12.5f, 9f, 17.5f })
        {
            DesertSky.ForceHour = hr; yield return 1.2f;
            Shot("arena_detras_" + hr.ToString("F1").Replace('.', '_'), 1920, 1080, true);
        }
        DesertSky.ForceHour = 12.5f;
        // borde del desierto: coche a 14 m de x = 0, hacia −x, a tope
        float zb = 40f;
        for (int k = 0; k < 24; k++)
        {
            float z = 30f + k * 19f; bool clear = true;
            for (float x = -20f; x <= 30f && clear; x += 10f)
            {
                Vector3 q = new Vector3(Mathf.Repeat(x, dw.L), 0f, z); q.y = DesertHeight.H(q.x, q.z);
                foreach (Collider c in Physics.OverlapSphere(dw.levelRoot.TransformPoint(q + Vector3.up * 1.5f), 7f, ~0, QueryTriggerInteraction.Ignore))
                    if (!(c.name.StartsWith("Dune_") || c.transform.IsChildOf(car.transform) || c is CharacterController)) { clear = false; break; }
            }
            if (clear && dw.plan.QuickAt(new Vector2(0f, z), 30f) < 0) { zb = z; break; }
        }
        car.DebugPlace(new Vector3(14f, 0f, zb), -90f); yield return 1.0f;
        DesertCarCam.MaxCarDist = 0f; DesertCarCam.Frames = 0; DesertCarCam.OutFrames = 0;
        int w0 = car.Wraps; DesertCars.DbgHandbrake = false; DesertCars.DbgThrottle = 1f;
        double t0 = EditorApplication.timeSinceStartup; int shots = 0; double tw = -1;
        while (EditorApplication.timeSinceStartup - t0 < 10.0)
        {
            yield return null;
            Transform cm = Camera.main.transform; float d = Vector3.Distance(cm.position, car.transform.position + Vector3.up);
            if (tw < 0 && car.Wraps > w0) { tw = EditorApplication.timeSinceStartup; Log("vuelta del borde a " + (tw - t0).ToString("F2") + " s, velocidad " + (car.Speed * 3.6f).ToString("F0") + " km/h"); }
            if (tw >= 0)
            {
                double a = EditorApplication.timeSinceStartup - tw;
                if (shots == 0 && a > 0.15) { Log("tras la vuelta +" + a.ToString("F2") + " s: cámara a " + d.ToString("F1") + " m del coche"); Shot("borde_mas0_15s", 1920, 1080, true); shots++; }
                else if (shots == 1 && a > 1.0) { Log("tras la vuelta +" + a.ToString("F2") + " s: cámara a " + d.ToString("F1") + " m del coche"); Shot("borde_mas1s", 1920, 1080, true); shots++; }
                else if (shots == 2 && a > 3.0) { Log("tras la vuelta +" + a.ToString("F2") + " s: cámara a " + d.ToString("F1") + " m del coche"); shots++; break; }
            }
        }
        Log("borde: vueltas " + (car.Wraps - w0) + ", máx. distancia cámara–coche " + DesertCarCam.MaxCarDist.ToString("F1") + " m, fotogramas sin el coche en la imagen " + DesertCarCam.OutFrames + " de " + DesertCarCam.Frames + ", saltos seguidos " + DesertCarCam.Jumps);
        DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true;
        DesertCars.CamView = 0; DesertCars.DebugDrive = false; DesertCarCam.DebugOldFollow = false;
        DesertSky.ForceWeather = -1; DesertSky.ForceHour = -1f; Health.DisableForTests = false;
        Log("FIN");
    }
}
