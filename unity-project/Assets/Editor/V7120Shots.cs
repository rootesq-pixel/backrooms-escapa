using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.12.0: capturas reales del Nivel 188 (El Patio de las Ventanas) dentro del juego (xvfb con gráficos):
// patio desde el ascensor, fachadas, vista alta, ventana abierta (otro nivel), ventana con sol (salida), la Ventana
// (desde el patio y desde el pasillo por la puerta entornada), pasillo, habitación con la cortina descorrida, escalera
// y un Periférico de reojo. Salida en $V7120_OUT (por defecto /workspace/v7120/capturas).
//   V7120_OUT=<dir> xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath <p> -executeMethod V7120Shots.Export -force-glcore
[InitializeOnLoad]
public static class V7120Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V7120_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v7120/capturas" : o; } }
    static int Seed { get { int s; return int.TryParse(System.Environment.GetEnvironmentVariable("V7120_SEED"), out s) ? s : 188; } }
    static V7120Shots() { if (SessionState.GetString("smoke_phase", "") != "v7120shots") return; EditorApplication.update += Tick; }
    // en el PC del usuario (editor de Unity con su GPU): menú Backrooms > Capturas Nivel 188 (4K). Salida: <proyecto>/Capturas188
    [MenuItem("Backrooms/Capturas Nivel 188 (4K)")]
    public static void ExportMenu4K()
    {
        System.Environment.SetEnvironmentVariable("V7120_RES", "3840x2160");
        System.Environment.SetEnvironmentVariable("V7120_OUT", System.IO.Path.GetFullPath("Capturas188"));
        Export();
    }
    static int ResW { get { string r = System.Environment.GetEnvironmentVariable("V7120_RES"); int w; return r != null && r.Contains("x") && int.TryParse(r.Split('x')[0], out w) ? w : 1920; } }
    static int ResH { get { string r = System.Environment.GetEnvironmentVariable("V7120_RES"); int h; return r != null && r.Contains("x") && int.TryParse(r.Split('x')[1], out h) ? h : 1080; } }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v7120shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V7120Shots] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { Time.timeScale = 1f; SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; if (Application.isBatchMode) EditorApplication.Exit(0); else { EditorApplication.isPlaying = false; EditorUtility.RevealInFinder(Out); } }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V7120Shots] " + s); }

    private static void Shot(string name, Vector3 eye, Vector3 at, float fov = 0f)
    {
        int w = ResW, h = ResH;
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, Application.isBatchMode ? 1 : 4);
        Camera game = Camera.main;
        Vector3 p0 = game.transform.position; Quaternion r0 = game.transform.rotation; float f0 = game.fieldOfView;
        game.transform.position = eye; game.transform.rotation = Quaternion.LookRotation(at - eye);
        if (fov > 0f) game.fieldOfView = fov;
        RenderTexture o = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = o;
        game.transform.position = p0; game.transform.rotation = r0; game.fieldOfView = f0;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
        Log("captura " + name + " (ojo " + eye.ToString("F1") + ")");
    }

    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", "");
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        GameSettings.Quality = 2; yield return 0.3f;
        Log("versión " + GameVersion.Current + ", gráficos " + SystemInfo.graphicsDeviceType + ", " + System.DateTime.Now.ToString("u"));
        gb.useRandomSeed = false; gb.seed = Seed - 10 * 101;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(10);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu && W188World.Active != null) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        W188ShotPlan.ShotFn = Shot; W188ShotPlan.LogFn = Log;
        IEnumerator views = W188ShotPlan.Views(); while (views.MoveNext()) yield return views.Current;
        Log("FIN");
    }
}
