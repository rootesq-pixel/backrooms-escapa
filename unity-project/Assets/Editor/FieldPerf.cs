using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.0.2: tirones en el Campo infinito (calidad Alto). Corre por el campo cruzando trozos y el borde de vuelta,
// y mide por fotograma: tiempo de fotograma, coste de la hierba (Submit), copias de borde, trozos creados y memoria GC asignada.
//   xvfb-run -a Unity -batchmode -projectPath <p> -executeMethod FieldPerf.Run -force-glcore   -> /workspace/logs/fieldperf.txt
[InitializeOnLoad]
public static class FieldPerf
{
    public const string Out = "/workspace/logs/fieldperf.txt";
    private static IEnumerator routine; private static double waitUntil;
    static FieldPerf() { if (SessionState.GetString("smoke_phase", "") != "fieldperf") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        File.WriteAllText(Out, "");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "fieldperf");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) { Debug.Log("[FieldPerf] inicio"); routine = Main(); }
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { File.AppendAllText(Out, "FAIL excepción " + e + "\n"); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }

    public struct Stats { public float p50, p95, max, gcKbMax, gcKbAvg, submitMax, submitAvg, ghostMax; public int spikes33, spikesRel, built, wraps, frames, ghostDraws; }

    // mide mientras el jugador corre; lo usa también SmokeTest
    public static IEnumerator Measure(FirstPersonController pw, float seconds, System.Action<Stats> done)
    {
        List<float> ft = new List<float>(); List<float> sub = new List<float>(); float gmax = 0f; long gsum = 0, gmx = 0; int built0 = FieldGrass.BuiltTotal;
        int w0 = FieldWorld.Active != null ? FieldWorld.Active.WrapCount : 0; int gd = 0;
        ProfilerRecorder gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
        pw.debugInput = true; pw.debugSprint = true; pw.debugMove = new Vector2(0f, 1f);
        float t = 0f; int fr = 0; float last = Time.realtimeSinceStartup;
        while (t < seconds)
        {
            yield return null;
            float now = Time.realtimeSinceStartup; float dt = now - last; last = now; t += dt; fr++;
            pw.SetStaminaForTests(100f);
            if (fr < 5) continue;   // descartar el arranque
            ft.Add(dt * 1000f); sub.Add(FieldGrass.LastSubmitMs); gmax = Mathf.Max(gmax, FieldWorld.LastGhostMs); gd = Mathf.Max(gd, FieldWorld.GhostDraws);
            if (gc.Valid) { long v = gc.LastValue; gsum += v; gmx = System.Math.Max(gmx, v); }
        }
        pw.debugSprint = false; pw.debugMove = Vector2.zero; pw.debugInput = false;
        gc.Dispose();
        Stats s = new Stats(); s.frames = ft.Count;
        List<float> so = new List<float>(ft); so.Sort();
        s.p50 = so[so.Count / 2]; s.p95 = so[(int)(so.Count * 0.95f)]; s.max = so[so.Count - 1];
        foreach (float f in ft) { if (f > 33.3f) s.spikes33++; if (f > s.p50 * 2.5f && f > s.p50 + 8f) s.spikesRel++; }
        float sm = 0f, ss = 0f; foreach (float f in sub) { sm = Mathf.Max(sm, f); ss += f; }
        s.submitMax = sm; s.submitAvg = ss / Mathf.Max(1, sub.Count); s.ghostMax = gmax; s.ghostDraws = gd;
        s.gcKbMax = gmx / 1024f; s.gcKbAvg = gsum / 1024f / Mathf.Max(1, ft.Count);
        s.built = FieldGrass.BuiltTotal - built0; s.wraps = (FieldWorld.Active != null ? FieldWorld.Active.WrapCount : 0) - w0;
        done(s);
    }

    public static string Fmt(Stats s)
    {
        return string.Format("frames {0} | fotograma p50 {1:F1} ms p95 {2:F1} max {3:F1} | >33ms {4} | picos relativos {5} | hierba Submit media {6:F2} max {7:F2} ms | copias borde max {8:F2} ms ({9} draws) | trozos creados {10} | vueltas {11} | GC/fotograma media {12:F2} KB max {13:F1} KB",
            s.frames, s.p50, s.p95, s.max, s.spikes33, s.spikesRel, s.submitAvg, s.submitMax, s.ghostMax, s.ghostDraws, s.built, s.wraps, s.gcKbAvg, s.gcKbMax);
    }

    // coloca al jugador a ~25 m del borde mirando hacia él (cruza trozos y la vuelta)
    public static void AimAtEdge(FirstPersonController pw, BackroomsGenerator gen)
    {
        Transform root = gen.Root;
        Vector3 lp = root.InverseTransformPoint(pw.transform.position);
        float L = FieldHeight.L;
        Vector3 np = new Vector3(L - 26f, 0f, Mathf.Clamp(lp.z, 30f, L - 30f));
        np.y = FieldHeight.H(np.x, np.z) + 0.1f;
        float yaw = Quaternion.LookRotation(root.TransformDirection(Vector3.right)).eulerAngles.y;
        pw.Teleport(root.TransformPoint(np), yaw);
    }

    private static IEnumerator Main()
    {
        StringBuilder sb = new StringBuilder();
        int q = int.Parse(System.Environment.GetEnvironmentVariable("PERF_Q") ?? "2");
        GameSettings.Quality = q;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        Debug.Log("[FieldPerf] menú listo, cargando nivel 5");
        int ld = gb.LoadCount;
        float tl = Time.realtimeSinceStartup;
        UIManager.Instance.StartGame(5);
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu)) yield return 0.2f;
        sb.AppendLine("INFO carga nivel 5: " + (Time.realtimeSinceStartup - tl).ToString("F1") + " s (calidad " + q + ") | sin ranura tras cargar " + FieldGrass.SlotStarved + " trozos " + (FieldGrass.Active != null ? FieldGrass.Active.ChunkCount : -1));
        if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
        float w = 0f; while (ElevatorCabin.PlayerLocked && w < 20f) { yield return 0.25f; w += 0.25f; }
        Debug.Log("[FieldPerf] nivel listo");
        GameManager gm = GameManager.Instance;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        FirstPersonController pw = gm.player;
        AimAtEdge(pw, gb.Generator);
        yield return 1.0f;
        for (int pass = 0; pass < 3; pass++)
        {
            Stats st = new Stats();
            IEnumerator me = Measure(pw, 14f, s => st = s);
            while (me.MoveNext()) yield return me.Current;
            sb.AppendLine("PERF pasada " + (pass + 1) + ": " + Fmt(st));
            FieldGrass fgp = FieldGrass.Active;
            if (fgp != null) sb.AppendLine("INFO hierba: trozos " + fgp.ChunkCount + " listos " + fgp.ReadyChunks + " ranuras libres " + fgp.FreeSlots + " pendientes " + fgp.PendingJobs + " sin ranura " + FieldGrass.SlotStarved + " | peor Submit: bucle/subida/GPU " + FieldGrass.MaxParts.ToString("F2") + " | llamadas " + fgp.DrawCalls + " matas " + fgp.VisibleInstances);
            File.WriteAllText(Out, sb.ToString());
            // girar 90° y seguir (nuevos trozos)
            FieldGrass.MaxPartTotal = 0f;
            pw.Teleport(pw.transform.position, pw.transform.eulerAngles.y + 90f);
            yield return 0.3f;
        }
        File.WriteAllText(Out, sb.ToString());
    }
}
