// v7.4.0: prueba de alcanzabilidad con varias semillas fijas (modo juego). Nivel 6 (Parque de bolas): con la semilla 64257619
// la salida quedaba inalcanzable porque el generador contaba la puerta este de las salas como abierta y en la malla horneada
// no se podía pasar (planta alta del parque justo detrás). Comprueba para cada semilla: camino a pie inicio -> salida por el
// NavMesh, cada puerta de cada sala pasable y cada sala alcanzable desde el inicio.
// Uso: Unity -batchmode -nographics -projectPath ... -executeMethod SeedReachCheck.Run   -> /workspace/logs/smoke-seedreach.txt
using System; using System.Collections; using System.Collections.Generic; using System.IO;
using UnityEditor; using UnityEditor.SceneManagement; using UnityEngine; using UnityEngine.AI;
[InitializeOnLoad]
public static class SeedReachCheck
{
    public const string Report = "/workspace/logs/smoke-seedreach.txt";
    public static readonly int[] Level6Seeds = { 64257619, 100, 200, 300, 400, 1, 7, 42, 1234, 5555, 98765, 31337, 271828, 314159, 8675309, 20261024, 123456789, 99, 2024, 777 };
    static IEnumerator it; static double wait; static int pass, fail;
    static SeedReachCheck() { if (SessionState.GetString("seedreach", "") != "1") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Report)); File.WriteAllText(Report, "");
        SessionState.SetString("seedreach", "1"); EditorSceneManager.OpenScene("Assets/Scenes/Main.unity"); EditorApplication.EnterPlaymode();
    }
    static void L(string s) { File.AppendAllText(Report, s + "\n"); }
    static void Check(bool ok, string s) { if (ok) pass++; else fail++; L((ok ? "PASS " : "FAIL ") + s); }
    static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (it == null) { pass = fail = 0; it = Go(); }
        if (EditorApplication.timeSinceStartup < wait) return;
        try { if (!it.MoveNext()) { Done(); return; } if (it.Current is float f) wait = EditorApplication.timeSinceStartup + f; }
        catch (Exception e) { fail++; L("FAIL excepción " + e); Done(); }
    }
    static void Done()
    {
        EditorApplication.update -= Tick; SessionState.SetString("seedreach", "");
        L("RESULT passed=" + pass + " failed=" + fail); EditorApplication.Exit(fail == 0 ? 0 : 1);
    }
    static float PathLen(Vector3 a, Vector3 b)
    {
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 2.5f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out NavMeshHit hb, 3f, NavMesh.AllAreas)) return -1f;
        if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
        float s = 0f; for (int i = 1; i < path.corners.Length; i++) s += Vector3.Distance(path.corners[i - 1], path.corners[i]); return s;
    }
    static IEnumerator Go()
    {
        double t00 = EditorApplication.timeSinceStartup;
        while ((GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) && EditorApplication.timeSinceStartup - t00 < 120) yield return 0.2f;
        yield return 1f;
        GameSettings.ResetDefaults(); ElevatorCabin.InstantForTests = true; BallPitLurker.DisabledForTests = true; ExitLock.AutoKeyForTests = true;
        GameBootstrap gb = GameBootstrap.Instance; const int lv = 6;
        Check(!BackroomsGenerator.HallEastDoor && !BackroomsGenerator.HallDoorV(new RectInt(10, 10, 5, 4), 15, 11) && BackroomsGenerator.HallDoorV(new RectInt(10, 10, 5, 4), 10, 11),
            "salas del parque: la puerta este (sin paso en la malla horneada) no cuenta como abierta; la oeste sí");
        foreach (int s in Level6Seeds)
        {
            gb.useRandomSeed = false; gb.seed = s - lv * 101;
            int lc = gb.LoadCount; gb.LoadLevel(lv);
            double t0 = EditorApplication.timeSinceStartup;
            while (!(gb.LoadCount > lc && gb.IsLoaded && GameManager.Instance != null && GameManager.Instance.generator != null) && EditorApplication.timeSinceStartup - t0 < 60) yield return 0.1f;
            yield return 1.0f;
            BackroomsGenerator gen = GameManager.Instance.generator;
            string T = "[nivel 6 semilla " + gen.UsedSeed + "] ";
            float pl = PathLen(gen.PlayerStart, gen.ExitPosition);
            Check(gen.UsedSeed == s && pl > 0f, T + "camino a pie inicio -> salida por el NavMesh (" + pl.ToString("F0") + " m)");
            float cs = gen.CellSize; Transform R = gen.Root;
            Func<int, int, Vector3> C = (x, z) => R.TransformPoint(new Vector3((x + 0.5f) * cs, 0.05f, (z + 0.5f) * cs));
            int doorsOk = 0, doors = 0, hallsOk = 0; string bad = "";
            for (int k = 0; k < gen.Halls.Count; k++)
            {
                RectInt b = gen.Halls[k];
                var ds = new List<(Vector2Int a, Vector2Int o, string n)>();
                for (int i = b.x; i < b.xMax; i++)
                {
                    if (BackroomsGenerator.HallDoorH(b, i, b.y)) ds.Add((new Vector2Int(i, b.y), new Vector2Int(i, b.y - 1), "S"));
                    if (BackroomsGenerator.HallDoorH(b, i, b.yMax)) ds.Add((new Vector2Int(i, b.yMax - 1), new Vector2Int(i, b.yMax), "N"));
                }
                for (int j = b.y; j < b.yMax; j++)
                {
                    if (BackroomsGenerator.HallDoorV(b, b.x, j)) ds.Add((new Vector2Int(b.x, j), new Vector2Int(b.x - 1, j), "O"));
                    if (BackroomsGenerator.HallDoorV(b, b.xMax, j)) ds.Add((new Vector2Int(b.xMax - 1, j), new Vector2Int(b.xMax, j), "E"));
                }
                foreach (var d in ds)
                {
                    if (d.o.x < 0 || d.o.y < 0 || d.o.x >= gen.GridWidth || d.o.y >= gen.GridHeight) continue;   // puerta contra el borde del mapa
                    doors++; float q = PathLen(C(d.a.x, d.a.y), C(d.o.x, d.o.y));
                    if (q > 0f && q < 12f) doorsOk++; else bad += " sala" + k + d.n + "(" + q.ToString("F0") + ")";
                }
                float qh = PathLen(gen.PlayerStart, C(b.x + 2, b.y + 2));
                if (qh > 0f) hallsOk++; else bad += " sala" + k + " inalcanzable";
            }
            Check(doors > 0 && doorsOk == doors && hallsOk == gen.Halls.Count, T + "las " + doors + " puertas de las " + gen.Halls.Count + " salas se cruzan a pie y todas las salas se alcanzan desde el inicio" + (bad.Length > 0 ? " — falla:" + bad : ""));
        }
    }
}
