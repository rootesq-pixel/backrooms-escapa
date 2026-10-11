using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Censo de audio en juego: menú, cada nivel, reinicios y vuelta al menú -> /workspace/logs/audio-census-<modo>.txt
//   ... -executeMethod AudioCensusRun.Before   (zumbidos sin límite = comportamiento v6.4)
//   ... -executeMethod AudioCensusRun.After    (con AudioBuzzLimiter)
[InitializeOnLoad]
public static class AudioCensusRun
{
    private static IEnumerator routine;
    private static double waitUntil;
    private static StringBuilder sb;

    static AudioCensusRun()
    {
        if (SessionState.GetString("smoke_phase", "") != "audiocensus") return;
        EditorApplication.update += Tick;
    }

    public static void Before() { Start(true); }
    public static void After() { Start(false); }

    private static void Start(bool baseline)
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "audiocensus");
        SessionState.SetBool("census_baseline", baseline);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) { AudioBuzzLimiter.Disabled = SessionState.GetBool("census_baseline", false); sb = new StringBuilder(); routine = Run(); }
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try
        {
            if (!routine.MoveNext()) Finish();
            else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current;
        }
        catch (System.Exception e) { Debug.LogError("[AudioCensusRun] " + e); Finish(); }
    }

    private static void Finish()
    {
        File.WriteAllText("/workspace/logs/audio-census-" + (SessionState.GetBool("census_baseline", false) ? "before" : "after") + ".txt", sb.ToString());
        SessionState.SetString("smoke_phase", "");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    private static IEnumerator Run()
    {
        ElevatorCabin.InstantForTests = true;
        yield return 2f;
        sb.AppendLine(AudioCensus.Dump("MENÚ"));
        GameBootstrap gb = GameBootstrap.Instance;
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            int ld = gb.LoadCount;
            UIManager.Instance.StartGame(lv);
            while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu)) yield return 0.2f;
            yield return 3f;
            sb.AppendLine(AudioCensus.Dump("NIVEL " + lv));
            // jugador en un punto medio del laberinto: cuántos zumbidos quedan dentro del alcance
            if (lv == 1 || lv == 4)
            {
                ld = gb.LoadCount;
                GameManager.Instance.Restart();
                while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null)) yield return 0.2f;
                yield return 3f;
                sb.AppendLine(AudioCensus.Dump("NIVEL " + lv + " tras REINICIAR"));
            }
        }
        {
            UIManager.Instance.OnMainMenu();
            yield return 3f;
            sb.AppendLine(AudioCensus.Dump("MENÚ tras jugar"));
        }
    }
}
