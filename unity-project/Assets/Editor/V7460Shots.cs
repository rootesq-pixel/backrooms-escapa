using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Nivel 7460: capturas a 1920x1080 con gráficos (xvfb) del pasillo, puertas, habitación, tapiada, salida, reflejos y HUD.
// Uso: xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath <p> -executeMethod V7460Shots.Export -screen-width 1920 -screen-height 1080
// Salida: $V7460_OUT (por defecto /workspace/v7460/shots/game)
[InitializeOnLoad]
public static class V7460Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V7460_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v7460/shots/game" : o; } }
    static V7460Shots() { if (SessionState.GetString("smoke_phase", "") != "v7460shots") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v7460shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V7460Shots] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V7460Shots] " + s); }

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
    private static void Shot(string name, bool hud, Vector3? eye = null, Vector3? look = null, int w = 1920, int h = 1080)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        Vector3 p0 = game.transform.position; Quaternion r0 = game.transform.rotation;
        System.Collections.Generic.List<Renderer> hidden = new System.Collections.Generic.List<Renderer>();
        if (eye.HasValue)
        {
            game.transform.position = eye.Value; game.transform.rotation = Quaternion.LookRotation(look.Value - eye.Value);
            FirstPersonController pl = FirstPersonController.LocalInstance;   // cámara libre: el cuerpo del jugador no sale en la foto
            if (pl != null) foreach (Renderer rr in pl.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hidden.Add(rr); }
        }
        RenderTexture o = game.targetTexture; game.targetTexture = rt;
        if (CorridorPlanar.Instance != null) CorridorPlanar.Instance.Refresh(game);
        game.Render(); game.targetTexture = o;
        game.transform.position = p0; game.transform.rotation = r0;
        foreach (Renderer rr in hidden) rr.enabled = true;
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
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        Log("calidad " + GameSettings.Quality + ", gráficos " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
        gb.useRandomSeed = false; gb.seed = 12345;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(9);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        // llegada en ascensor: captura con las puertas abriéndose
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; if (Mathf.Abs(wt - 9f) < 0.13f) Shot("01_ascensor_llegada_hud", true); }
        yield return 1.5f;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; CorridorWorld w = CorridorWorld.Active;
        if (w == null) { Log("SIN PASILLO"); yield break; }
        Health.DisableForTests = true; CorridorEvents.DisableForTests = true;
        Flashlight fl = p.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(false);
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        Shot("02_salida_del_ascensor_hud", true);
        System.Func<float, float, float, Vector3> L = (x, y, z) => w.levelRoot.TransformPoint(new Vector3(w.CenterX + x, y, z));
        float cs = CorridorWorld.CS;
        // pasillo: composición como la foto de referencia (centrado, a la altura de los ojos, hacia el punto de fuga)
        p.Teleport(L(0f, 0.1f, 20f * cs), 0f); yield return 2.0f;
        Shot("03_pasillo_centro_punto_de_fuga", false, L(0f, 1.55f, 20.5f * cs), L(0f, 1.45f, 60f * cs));
        Shot("04_pasillo_centro_hud", true, L(0f, 1.55f, 20.5f * cs), L(0f, 1.45f, 60f * cs));
        Shot("05_pasillo_hacia_el_ascensor", false, L(0f, 1.55f, 30.2f * cs), L(0f, 1.45f, 0f));
        Shot("06_suelo_reflejo", false, L(-0.5f, 1.6f, 22.0f * cs), L(0.2f, 0f, 24.5f * cs));
        // detalle de puerta cerrada (manilla, bisagras, jamba, zócalo)
        int lr = -1, ls = 0; for (int j = 12; j < w.plan.rows && lr < 0; j++) for (int s = 0; s < 2; s++) if (w.plan.doors[s, j] == CorridorPlan.Door.Locked && w.plan.doors[1 - s, j] == CorridorPlan.Door.Locked) { lr = j; ls = s; break; }
        float sg = ls == CorridorPlan.Left ? -1f : 1f;
        Shot("07_puerta_detalle", false, L(-sg * 0.6f, 1.45f, (lr + 0.5f) * cs - 1.6f), L(sg * 1.7f, 1.0f, (lr + 0.5f) * cs + 0.2f));
        // habitación
        int rr = -1, rs = 0; for (int j = 5; j < w.plan.rows && rr < 0; j++) for (int s = 0; s < 2; s++) if (w.plan.doors[s, j] == CorridorPlan.Door.Room) { rr = j; rs = s; break; }
        if (rr >= 0)
        {
            float rg = rs == CorridorPlan.Left ? -1f : 1f;
            p.Teleport(w.DoorWorld(rs, rr, 0.7f), rs == CorridorPlan.Left ? 270f : 90f); yield return 2.5f;
            Shot("08_habitacion_puerta_abierta", false, L(-rg * 0.9f, 1.55f, (rr + 0.5f) * cs - 0.9f), L(rg * 3.4f, 1.1f, (rr + 0.5f) * cs));
            p.Teleport(w.RoomCenterWorld(rs, rr) + Vector3.up * 0.1f, 0f); yield return 1.0f;
            Shot("09_habitacion_dentro", false, w.RoomCenterWorld(rs, rr) + w.levelRoot.TransformVector(new Vector3(rg * 1.2f, 1.55f, -1.2f)), w.RoomCenterWorld(rs, rr) + w.levelRoot.TransformVector(new Vector3(-rg * 1.6f, 1.2f, 0.6f)));
        }
        // tapiada
        int br = -1, bs = 0; for (int j = 0; j < w.plan.rows && br < 0; j++) for (int s = 0; s < 2; s++) if (w.plan.doors[s, j] == CorridorPlan.Door.Bricked) { br = j; bs = s; break; }
        if (br >= 0)
        {
            float bg = bs == CorridorPlan.Left ? -1f : 1f;
            p.Teleport(w.DoorWorld(bs, br, 0.7f), bs == CorridorPlan.Left ? 270f : 90f); yield return 2.5f;
            Shot("10_puerta_tapiada", false, L(-bg * 0.9f, 1.55f, (br + 0.5f) * cs - 1.0f), L(bg * 2.6f, 1.1f, (br + 0.5f) * cs));
        }
        else Log("sin puerta tapiada en esta semilla");
        // salida: cerrada (pista a < 8 m) y abierta con la llave buena
        float eg = w.plan.exitSide == CorridorPlan.Left ? -1f : 1f; float ez = (w.plan.exitRow + 0.5f) * cs;
        p.Teleport(L(-eg * 0.4f, 0.1f, ez - 4f), 0f); yield return 2.0f;
        Shot("11_salida_cerrada_pista", false, L(-eg * 0.5f, 1.55f, ez - 4.5f), L(eg * 1.7f, 1.0f, ez));
        Inventory.Instance.Add(ExitLock.Active.CorrectKey, 1); ExitLock.Active.Unlock(true, "prueba"); yield return 3.0f;
        Shot("12_salida_abierta", false, L(-eg * 0.5f, 1.55f, ez - 3.0f), L(eg * 2.2f, 1.0f, ez));
        // calidad baja (sin reflejo planar) para comparar
        int q0 = GameSettings.Quality; GameSettings.Quality = 0; yield return 1.0f;
        Shot("13_pasillo_calidad_baja", true, L(0f, 1.55f, 20.5f * cs), L(0f, 1.45f, 60f * cs));
        GameSettings.Quality = q0; yield return 0.5f;   // se deja como estaba (PlayerPrefs compartidas)
        Log("planar renders " + (CorridorPlanar.Instance != null ? CorridorPlanar.Instance.Renders : -1) + ", sonda " + w.ProbeRenders + ", tramos " + w.ChunkCount + ", habitaciones " + w.RoomCount);
        Health.DisableForTests = false; CorridorEvents.DisableForTests = false;
        Log("FIN");
    }
}
