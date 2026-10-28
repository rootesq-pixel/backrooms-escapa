using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.10.1: capturas antes/después: puerta cerrada (sin y con colisión) y luz roja de emergencia del 7460.
// Uso: xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath <p> -executeMethod V7101Look.Export
[InitializeOnLoad]
public static class V7101Look
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V7101_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v7101/shots" : o; } }
    static V7101Look() { if (SessionState.GetString("smoke_phase", "") != "v7101look") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v7101look");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V7101Look] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V7101Look] " + s); }

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
    private static bool showBody;
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
            if (pl != null && !showBody) foreach (Renderer rr in pl.GetComponentsInChildren<Renderer>()) if (rr.enabled) { rr.enabled = false; hidden.Add(rr); }
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

    static IEnumerator WalkInto(FirstPersonController p, CorridorWorld w, int s, int k, float[] res)
    {
        p.SetStaminaForTests(p.maxStamina);
        p.Teleport(w.DoorWorld(s, k, 1.2f) + Vector3.up * 0.05f, s == CorridorPlan.Left ? 270f : 90f); yield return 0.4f;
        p.debugInput = true; p.debugMove = new Vector2(0f, 1f); p.debugSprint = true;
        float tw = 0f, lat = 0f; while (tw < 2.0f && lat < CorridorWorld.HW - 0.05f) { yield return 0.02f; tw += 0.02f; lat = Mathf.Max(lat, Mathf.Abs(w.levelRoot.InverseTransformPoint(p.transform.position).x - w.CenterX)); }   // antes: se para a medio cruzar la puerta (para la foto)
        p.debugInput = false; p.debugSprint = false; p.debugMove = Vector2.zero; yield return 0.1f;
        res[0] = lat;
    }

    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", "");
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        int qOrig = GameSettings.Quality; GameSettings.Quality = 2; yield return 0.5f;
        Log("calidad " + GameSettings.Quality + ", gráficos " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
        gb.useRandomSeed = false; gb.seed = 12345;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(9);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; CorridorWorld w = CorridorWorld.Active;
        if (w == null) { Log("SIN PASILLO"); yield break; }
        Health.DisableForTests = true; CorridorEvents.DisableForTests = true; CorridorRed.ForceRed = 0f;
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        System.Func<float, float, float, Vector3> L = (x, y, z) => w.levelRoot.TransformPoint(new Vector3(w.CenterX + x, y, z));
        float cs = CorridorWorld.CS;
        // --- puerta cerrada: antes (sin colisión, como v7.10.0) y después ---
        int lr = -1, ls = 0; for (int j = 12; j < w.plan.rows && lr < 0; j++) for (int s = 0; s < 2; s++) if (w.plan.doors[s, j] == CorridorPlan.Door.Locked && w.plan.doors[1 - s, j] == CorridorPlan.Door.Locked) { lr = j; ls = s; break; }
        float sg = ls == CorridorPlan.Left ? -1f : 1f, zc = (lr + 0.5f) * cs;
        Transform blk = w.transform.Find("DoorLocked_" + ls + "_" + lr);
        Collider bc = blk != null ? blk.GetComponent<Collider>() : null;
        Log("puerta " + ls + "/" + lr + " colisión " + (bc != null));
        float[] res = new float[1];
        Vector3 eye3 = L(-sg * 0.9f, 1.7f, zc - 2.6f), look3 = L(sg * 1.4f, 1.0f, zc);
        for (int pass = 0; pass < 2; pass++)
        {
            if (bc != null) bc.enabled = pass == 1;
            { IEnumerator wi = WalkInto(p, w, ls, lr, res); while (wi.MoveNext()) yield return wi.Current; }
            string tag = pass == 0 ? "antes" : "despues";
            Log("puerta " + tag + ": distancia lateral máx. " + res[0].ToString("F2") + " m (muro a " + CorridorWorld.HW + " m)");
            showBody = true; Shot("puerta_" + tag + "_3p", false, eye3, look3); showBody = false;
            Shot("puerta_" + tag + "_1p", false);
        }
        if (bc != null) bc.enabled = true;
        // --- luz roja: pasillo y habitación ---
        Flashlight fl = p.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(false);
        p.Teleport(L(0f, 0.1f, 20f * cs), 0f); yield return 1.5f;
        int rr = -1, rs = 0; for (int j = 5; j < w.plan.rows && rr < 0; j++) for (int s = 0; s < 2; s++) if (w.plan.doors[s, j] == CorridorPlan.Door.Room) { rr = j; rs = s; break; }
        for (int pass = 0; pass < 2; pass++)
        {
            CorridorRed.ForceRed = pass; yield return 1.0f;
            string tag = pass == 0 ? "normal" : "rojo";
            p.Teleport(L(0f, 0.1f, 20f * cs), 0f); yield return 0.8f;
            Shot("rojo_pasillo_" + tag, false, L(0f, 1.55f, 20.5f * cs), L(0f, 1.45f, 60f * cs));
            if (rr >= 0)
            {
                float rg = rs == CorridorPlan.Left ? -1f : 1f; Vector3 rc = w.RoomCenterWorld(rs, rr);
                p.Teleport(w.DoorWorld(rs, rr, 0.6f), rs == CorridorPlan.Left ? 270f : 90f); yield return 1.5f;
                Shot("rojo_habitacion_" + tag, false, L(-rg * 0.9f, 1.55f, (rr + 0.5f) * cs - 0.9f), L(rg * 3.4f, 1.1f, (rr + 0.5f) * cs));
            }
            Log("rojo " + tag + ": _C7460Red " + Shader.GetGlobalFloat(CorridorRed.RedId).ToString("F2") + " niebla " + RenderSettings.fogColor);
        }
        CorridorRed.ForceRed = -1f;
        GameSettings.Quality = qOrig;
        Health.DisableForTests = false; CorridorEvents.DisableForTests = false;
        Log("fin");
    }
}
