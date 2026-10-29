using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// v7.10.3: capturas reales para las Novedades: pantalla de créditos (atribuciones de sonido) y el Nivel 0 con la alarma roja
// sonando (comprueba que la fuente de la alarma suena). Uso: V7103_OUT=<dir> xvfb-run -a -s "-screen 0 1920x1080x24" Unity
// -batchmode -projectPath <p> -executeMethod V7103Shots.Export
[InitializeOnLoad]
public static class V7103Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V7103_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v7103/shots" : o; } }
    static V7103Shots() { if (SessionState.GetString("smoke_phase", "") != "v7103shots") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v7103shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V7103Shots] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { Time.timeScale = 1f; SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V7103Shots] " + s); }
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
        if (game != null)
        {
            RenderTexture o = game.targetTexture; game.targetTexture = rt;
            if (CorridorPlanar.Instance != null) CorridorPlanar.Instance.Refresh(game);
            game.Render(); game.targetTexture = o;
        }
        else { RenderTexture pa = RenderTexture.active; RenderTexture.active = rt; GL.Clear(true, true, Color.black); RenderTexture.active = pa; }
        if (hud) { SetupUi(); Canvas.ForceUpdateCanvases(); uiCam.targetTexture = rt; uiCam.Render(); uiCam.targetTexture = null; }
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; Save(tex, name); Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
    }
    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", "");
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        GameSettings.Quality = 2; yield return 0.5f;
        Log("versión " + GameVersion.Current + ", protocolo " + NetSession.Protocol + ", gráficos " + SystemInfo.graphicsDeviceType);
        UIManager.Instance.SetScreen(UIManager.Screen.Credits); yield return 0.6f;
        foreach (Text ct in Object.FindObjectsOfType<Text>(false)) if (ct.name == "CreditsBody") Log("créditos: alto del texto " + ct.preferredHeight.ToString("F0") + " px de " + ct.rectTransform.rect.height.ToString("F0") + " (fuente " + ct.fontSize + ")");
        Shot("creditos_1920x1080", true);
        UIManager.Instance.SetScreen(UIManager.Screen.Main); yield return 0.4f;
        gb.useRandomSeed = false; gb.seed = 4242;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(0);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        Health.DisableForTests = true;
        Level0Events ev = Level0Events.Active;
        if (ev == null || ev.Schedule.Count == 0) { Log("SIN SUCESOS DEL NIVEL 0"); yield break; }
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player;
        FootstepAudio fa = p.GetComponent<FootstepAudio>();
        // sale de la cabina andando (con la orientación inicial) y luego llega el apagón rojo
        int r0 = fa != null ? fa.RealStepsPlayed : 0; p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
        yield return 3.5f; p.debugInput = false; p.debugMove = Vector2.zero;
        if (fa != null) Log("pasos reales al andar 3,5 s: " + (fa.RealStepsPlayed - r0) + " (superficie " + fa.kind + ")");
        ev.DebugSetClock(ev.Schedule[0].start + 4f);
        yield return 3.0f;
        AudioSource al = ev.AlarmSource;
        Log("alarma: sonando " + (al != null && al.isPlaying) + ", volumen " + (al != null ? al.volume.ToString("F3") : "-") + ", clip " + (al != null && al.clip != null ? al.clip.name + " " + al.clip.length.ToString("F1") + " s" : "-"));
        // mira hacia donde la vista llega más lejos (rayos cada 10°), para que se vea la oficina en rojo y no una pared
        Vector3 eye = Camera.main.transform.position; float bestD = 0f, bestY = 0f;
        for (int yaw = 0; yaw < 360; yaw += 10)
        {
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward; RaycastHit hit;
            float d = Physics.Raycast(eye, dir, out hit, 60f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 60f;
            if (d > bestD) { bestD = d; bestY = yaw; }
        }
        p.transform.rotation = Quaternion.Euler(0f, bestY, 0f); yield return 0.6f;
        Log("vista más larga: " + bestD.ToString("F1") + " m a " + bestY + "°");
        Shot("nivel0_alarma_roja_1920x1080", true);
        p.transform.rotation = Quaternion.Euler(0f, bestY + 25f, 0f); yield return 0.5f;
        Shot("nivel0_alarma_roja_b_1920x1080", true);
        Health.DisableForTests = false;
    }
}
