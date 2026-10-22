using System.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.3.0: ejecución rápida solo del recorrido de Talasofobia (nivel 7) sin pasar por los niveles 0-6.
//   Unity -batchmode -nographics -projectPath <p> -executeMethod V730Run.Run   -> /workspace/logs/v730-ocean.txt
[InitializeOnLoad]
public static class V730Run
{
    private static IEnumerator routine; private static double waitUntil;
    public const string ReportPath = "/workspace/logs/v730-ocean.txt";
    private static SmokeTest.Report rep;
    static V730Run() { if (SessionState.GetString("smoke_phase", "") != "v730ocean") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v730ocean");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) { rep = new SmokeTest.Report(ReportPath); routine = Main(); }
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { rep.Fail("excepción: " + e); Debug.LogError("[V730Run] " + e); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; rep.Finish(); }

    private static IEnumerator Main()
    {
        GameBootstrap gb = GameBootstrap.Instance;
        float w0 = 0f;
        while ((gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) && w0 < 120f) { yield return 0.2f; w0 += 0.2f; gb = GameBootstrap.Instance; }
        ElevatorCabin.InstantForTests = true;
        gb.useRandomSeed = false; gb.seed = 7300 + int.Parse(System.Environment.GetEnvironmentVariable("V730_SEED") ?? "0");
        int ld = gb.LoadCount;
        float t0 = Time.realtimeSinceStartup;
        UIManager.Instance.StartGame(7);
        float wt = 0f;
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        rep.Info("[Talasofobia] carga del nivel " + (Time.realtimeSinceStartup - t0).ToString("F1") + " s");
        rep.Check(gb.CurrentLevel == 7 && GameManager.Instance != null, "[Talasofobia] nivel 7 cargado desde el menú");
        yield return 1.5f;
        IEnumerator oc = V730Checks.OceanPlay(rep, "[Nivel 7] ", GameManager.Instance);
        while (oc.MoveNext()) yield return oc.Current;
        rep.Flush();
    }
}
