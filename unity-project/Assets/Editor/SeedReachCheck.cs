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
    public static readonly int[] Level5Seeds = { 7110, 1, 42, 1234, 98765, 20261024 };
    public static readonly int[] Level3Seeds = { 1, 7, 42, 100, 1234, 5555, 98765, 31337, 271828, 8675309, 20261024, 777 };   // v7.5.0
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
        GameBootstrap gb = GameBootstrap.Instance;
        string only = Environment.GetEnvironmentVariable("SEEDREACH_ONLY") ?? "";
        if (only == "" || only == "5")
        {   // v7.4.0: casas del Campo infinito (nivel 5): se entra de pie por todas las puertas, se anda dentro y se puede estar de pie
            var tpls = new HashSet<int>(); int houses = 0;
            string l5env = Environment.GetEnvironmentVariable("SEEDREACH_L5SEEDS");
            int[] l5 = string.IsNullOrEmpty(l5env) ? Level5Seeds : Array.ConvertAll(l5env.Split(','), int.Parse);
            foreach (int s5 in l5)
            {
                gb.useRandomSeed = false; gb.seed = s5 - 5 * 101;
                int lc5 = gb.LoadCount; gb.LoadLevel(5);
                double t5 = EditorApplication.timeSinceStartup;
                while (!(gb.LoadCount > lc5 && gb.IsLoaded && GameManager.Instance != null && FieldWorld.Active != null) && EditorApplication.timeSinceStartup - t5 < 90) yield return 0.1f;
                yield return 1.0f;
                if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
                double tw = EditorApplication.timeSinceStartup; while (ElevatorCabin.PlayerLocked && EditorApplication.timeSinceStartup - tw < 20) yield return 0.25f;
                GameManager gm5 = GameManager.Instance; FirstPersonController pw = gm5.player;
                foreach (MonsterAI m in gm5.monsters) if (m != null) m.Stop();
                FieldWorld fw = FieldWorld.Active;
                string T5 = "[nivel 5 semilla " + gm5.generator.UsedSeed + "] ";

                for (int k = 0; k < fw.plan.houses.Count; k++)
                {
                    FieldPlan.House h = fw.plan.houses[k]; if (!h.enterable) continue;
                    Transform ht = HouseEntryProbe.HouseTransform(fw, k);
                    if (ht == null) { Check(false, T5 + "casa " + k + ": no se encuentra su objeto"); continue; }
                    var res = new HouseEntryProbe.Result[1];
                    gm5.StartCoroutine(HouseEntryProbe.Run(pw, ht, h, res));   // corrutina de verdad (fotogramas y WaitForSeconds del juego)
                    double th = EditorApplication.timeSinceStartup; while (!res[0].done && EditorApplication.timeSinceStartup - th < 30) yield return 0.1f;
                    tpls.Add(h.template); houses++;
                    Check(res[0].Ok(true), T5 + "casa " + k + " (plantilla " + h.template + "): de pie se entra por la puerta sin agacharse y ningún monstruo tiene camino dentro — " + res[0].Describe());
                }
                // persecución: un monstruo persigue al jugador desde 9 m mientras entra en la primera casa abierta; luego 12 s dentro
                MonsterAI m0 = null; foreach (MonsterAI m in gm5.monsters) if (m != null && m.UsesNav) { m0 = m; break; }
                int kc = -1; for (int k = 0; k < fw.plan.houses.Count; k++) if (fw.plan.houses[k].enterable) { kc = k; break; }
                if (m0 != null && kc >= 0)
                {
                    FieldPlan.House hc = fw.plan.houses[kc]; Transform htc = HouseEntryProbe.HouseTransform(fw, kc);
                    float D = FieldPlan.TemplateSize[hc.template].y, dx = FieldWorld.DoorX(hc.template);
                    var res = new HouseEntryProbe.Result[1];
                    gm5.StartCoroutine(HouseEntryProbe.Run(pw, htc, hc, res));
                    yield return 0.05f;
                    Vector3 mp = htc.TransformPoint(new Vector3(dx, 0f, D * 0.5f + 2.3f + 9f));
                    if (UnityEngine.AI.NavMesh.SamplePosition(mp, out UnityEngine.AI.NavMeshHit mh, 4f, UnityEngine.AI.NavMesh.AllAreas)) mp = mh.position;
                    m0.WarpTo(mp, 0f); m0.ChaseForTests();
                    bool inside = false, killed = false; float minD = 99f; double tc = EditorApplication.timeSinceStartup; bool chased = false;
                    while (EditorApplication.timeSinceStartup - tc < 30)
                    {
                        if (fw.InsideHouse(m0.transform.position)) inside = true;
                        if (m0.CurrentState == MonsterAI.State.Kill || gm5.State != GameManager.GameState.Playing) killed = true;
                        if (m0.CurrentState == MonsterAI.State.Chase) chased = true;
                        minD = Mathf.Min(minD, Vector3.Distance(m0.transform.position, pw.transform.position));
                        if (res[0].done && EditorApplication.timeSinceStartup - tc > 2) break;
                        yield return 0.1f;
                    }
                    double t2 = EditorApplication.timeSinceStartup; Vector3 last = m0.transform.position; float jitter = 0f;
                    while (EditorApplication.timeSinceStartup - t2 < 12)
                    {
                        if (fw.InsideHouse(m0.transform.position)) inside = true;
                        if (m0.CurrentState == MonsterAI.State.Kill || gm5.State != GameManager.GameState.Playing) killed = true;
                        minD = Mathf.Min(minD, Vector3.Distance(m0.transform.position, pw.transform.position));
                        if (EditorApplication.timeSinceStartup - t2 > 9) { jitter += Vector3.Distance(last, m0.transform.position); }
                        last = m0.transform.position;
                        yield return 0.1f;
                    }
                    string st = m0.CurrentState.ToString();
                    m0.Stop();
                    Check(chased && res[0].entered && !inside && !killed && m0.CurrentState != MonsterAI.State.Chase,
                        T5 + "casa " + kc + " como refugio: con un monstruo persiguiendo se entra, el monstruo no entra ni atrapa y deja la persecución (persiguió " + chased +
                        ", entra " + res[0].entered + ", monstruo dentro " + inside + ", atrapado " + killed + ", distancia mín. " + minD.ToString("F1") + " m, estado a los 12 s " + st + ", movimiento últimos 3 s " + jitter.ToString("F1") + " m)");
                }
                else Check(false, T5 + "persecución: sin monstruo con NavMesh o sin casa abierta");
            }
            Check(tpls.Count == FieldPlan.TemplateSize.Length && houses >= (l5 == Level5Seeds ? 8 : 1), "casas abiertas probadas: " + houses + ", plantillas " + tpls.Count + "/" + FieldPlan.TemplateSize.Length);
        }
        if (only == "" || only == "3")
        {   // v7.5.0: hotel largo (nivel 3): salida, pilas, llaves y salas grandes alcanzables a pie desde el ascensor; tamaño 2-3 veces el antiguo
            foreach (int s3 in Level3Seeds)
            {
                gb.useRandomSeed = false; gb.seed = s3 - 3 * 101;
                int lc3 = gb.LoadCount; double tl = EditorApplication.timeSinceStartup; gb.LoadLevel(3);
                while (!(gb.LoadCount > lc3 && gb.IsLoaded && GameManager.Instance != null && GameManager.Instance.generator != null) && EditorApplication.timeSinceStartup - tl < 90) yield return 0.1f;
                double loadS = EditorApplication.timeSinceStartup - tl;
                yield return 1.0f;
                BackroomsGenerator gen = GameManager.Instance.generator; LevelConfig c3 = LevelConfig.Get(3);
                string T = "[nivel 3 semilla " + gen.UsedSeed + "] ";
                int walk = 0; if (gen.Hotel != null) for (int x = 0; x < gen.GridWidth; x++) for (int z = 0; z < gen.GridHeight; z++) if (gen.Hotel.Walkable(x, z)) walk++;
                float area = walk * gen.CellSize * gen.CellSize, oldArea = 17f * 17f * 3.2f * 3.2f;
                Check(gen.Hotel != null && area >= 2f * oldArea && area <= 3.2f * oldArea, T + "hotel grande: " + gen.GridWidth + "x" + gen.GridHeight + " celdas, transitable " + area.ToString("F0") + " m² (" + (area / oldArea).ToString("F1") + " veces el antiguo), carga " + loadS.ToString("F1") + " s");
                float pl = PathLen(gen.PlayerStart, gen.ExitPosition);
                Check(gen.UsedSeed == s3 && pl > 0f, T + "camino a pie inicio -> salida por el NavMesh (" + pl.ToString("F0") + " m)");
                int bOk = 0; foreach (Vector3 b in gen.BatterySpots) if (PathLen(gen.PlayerStart, b) > 0f) bOk++;
                int kOk = 0; foreach (Vector3 k in gen.KeySpots) if (PathLen(gen.PlayerStart, k) > 0f) kOk++;
                Check(gen.BatterySpots.Count == c3.batteries && bOk == gen.BatterySpots.Count && kOk == gen.KeySpots.Count && gen.KeySpots.Count == ExitLock.KeyCount,
                    T + "pilas " + bOk + "/" + gen.BatterySpots.Count + " (config " + c3.batteries + ") y llaves " + kOk + "/" + gen.KeySpots.Count + " alcanzables a pie");
                int hOk = 0; string kinds = "";
                float cs = gen.CellSize; Transform R = gen.Root;
                foreach (var hl in gen.Hotel.halls)
                {
                    kinds += " " + hl.kind;
                    Vector3 hc = R.TransformPoint(gen.Hotel.FreePoint(hl.r.x + hl.r.width / 2, hl.r.y + hl.r.height / 2, gen.Hotel.Center(hl.r.x + hl.r.width / 2, hl.r.y + hl.r.height / 2)));
                    if (PathLen(gen.PlayerStart, hc) > 0f) hOk++;
                }
                Check(gen.Hotel.halls.Count >= 3 && hOk == gen.Hotel.halls.Count, T + "salas grandes alcanzables " + hOk + "/" + gen.Hotel.halls.Count + ":" + kinds);
                int rOpen = 0, rOk = 0; string rBad = "";
                foreach (var rm in gen.Hotel.rooms)
                {
                    if (!rm.open) continue; rOpen++;
                    Vector3 bc = R.TransformPoint(gen.Hotel.FreePoint(rm.bed.x, rm.bed.y, gen.Hotel.Center(rm.bed.x, rm.bed.y)));
                    if (PathLen(gen.PlayerStart, bc) > 0f) rOk++;
                    else
                    {
                        if (rBad.Length < 80) rBad += " " + rm.number;
                        if (Environment.GetEnvironmentVariable("SEEDREACH_DIAG") == "1" && rBad.Length < 30)
                        {   // diagnóstico: navmesh a lo largo del hueco y qué hay en el nivel delante de la puerta
                            Vector3 dl = gen.Hotel.Center(rm.entry.x, rm.entry.y) + HotelPlan.Dir3(rm.doorDir) * (cs * 0.5f);
                            string dg = " [" + rm.number + " puerta " + dl + " bed " + bc + ":";
                            for (float o = -1.2f; o <= 1.21f; o += 0.3f)
                            {
                                Vector3 q = R.TransformPoint(dl + HotelPlan.Dir3(rm.doorDir) * o);
                                bool ok = NavMesh.SamplePosition(q, out NavMeshHit nh, 0.25f, NavMesh.AllAreas);
                                dg += (ok ? "+" : "-");
                            }
                            Bounds zb = new Bounds(R.TransformPoint(dl) + Vector3.up, new Vector3(1.8f, 1.9f, 1.8f));
                            foreach (Renderer rr in R.GetComponentsInChildren<Renderer>()) if (rr.bounds.Intersects(zb) && rr.bounds.size.magnitude < 30f) dg += " " + rr.name + rr.bounds.size.ToString("F1");
                            dg += " | bedside: ";
                            for (float o = -1.5f; o <= 1.51f; o += 0.3f)
                            {
                                Vector3 q = bc + HotelPlan.Dir3(rm.doorDir) * o;
                                dg += NavMesh.SamplePosition(q, out NavMeshHit nh2, 0.25f, NavMesh.AllAreas) ? "+" : "-";
                            }
                            L("DIAG" + dg + "]");
                        }
                    }
                }
                int cOk = 0, planBad = 0; string cBad = "";
                for (int x = 0; x < gen.GridWidth; x++) for (int z = 0; z < gen.GridHeight; z++)
                {
                    if (!gen.Hotel.Walkable(x, z) || gen.Hotel.kind[x, z] == HotelPlan.K.Start) continue;
                    if (!gen.Hotel.CellReachable(x, z)) { planBad++; continue; }
                    if (PathLen(gen.PlayerStart, R.TransformPoint(gen.Hotel.FreePoint(x, z, gen.Hotel.Center(x, z)))) > 0f) cOk++; else if (cBad.Length < 90) cBad += " " + x + "," + z + ":" + gen.Hotel.kind[x, z];
                }
                Check(rOpen >= 20 && rOk == rOpen && cOk >= walk - 1 - planBad - 1 && planBad <= walk / 40, T + "habitaciones abiertas alcanzables " + rOk + "/" + rOpen + rBad + "; celdas con camino por el NavMesh " + cOk + "/" + (walk - 1 - planBad) + cBad + " (el plano da " + planBad + " celdas sin paso, tras derrumbes o muebles)");
                int longest = 0; foreach (var co in gen.Hotel.corridors) longest = Mathf.Max(longest, co.b - co.a + 1);
                Check(HotelDecor.Doors >= 60 && longest >= 12 && HotelDecor.Leaves.Count == gen.Hotel.doorways.Count, T + "puertas numeradas " + HotelDecor.Doors + " (que se abren " + HotelDecor.Leaves.Count + "), muebles " + HotelDecor.ItemCount + ", pasillos " + gen.Hotel.corridors.Count + " (el más largo " + (longest * cs).ToString("F0") + " m)");
            }
        }
        if (only == "5" || only == "3") yield break;
        const int lv = 6;
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
