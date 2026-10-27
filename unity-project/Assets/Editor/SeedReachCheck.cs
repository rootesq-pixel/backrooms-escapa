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
    public static readonly string Report = TestPaths.Logs + "/smoke-seedreach.txt";
    public static readonly int[] Level5Seeds = { 7110, 1, 42, 1234, 98765, 20261024 };
    public static readonly int[] Level3Seeds = { 1, 7, 42, 100, 1234, 5555, 98765, 31337, 271828, 8675309, 20261024, 777 };   // v7.5.0
    public static readonly int[] Level8Seeds = { 7110, 1, 42, 1234, 98765, 20261024, 31337, 777 };   // v7.7.0
    public static readonly int[] Level9Seeds = { 7460, 1, 42, 98765 };   // Nivel 7460
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
            string l3s = Environment.GetEnvironmentVariable("SEEDREACH_L3SEEDS");
            int[] l3list = string.IsNullOrEmpty(l3s) ? Level3Seeds : Array.ConvertAll(l3s.Split(','), int.Parse);
            foreach (int s3 in l3list)
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
                int bOk = 0; foreach (Vector3 b in gen.BatterySpots) { if (PathLen(gen.PlayerStart, b) > 0f) bOk++; else if (gen.Hotel != null) { Vector3 lb = gen.Root.InverseTransformPoint(b); int bx = Mathf.FloorToInt(lb.x / gen.CellSize), bz = Mathf.FloorToInt(lb.z / gen.CellSize); L("INFO pila sin camino en " + lb.ToString("F2") + " celda " + bx + "," + bz + " " + gen.Hotel.At(bx, bz) + " plano " + gen.Hotel.CellReachable(bx, bz) + " navmesh cerca " + NavMesh.SamplePosition(b, out NavMeshHit bh, 0.4f, NavMesh.AllAreas)); } }
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
                            Vector3 dW = R.TransformPoint(dl);
                            if (NavMesh.SamplePosition(dW + HotelPlan.Dir3(rm.doorDir) * 0.9f, out NavMeshHit c0, 1f, NavMesh.AllAreas) && NavMesh.SamplePosition(bc, out NavMeshHit c1, 1f, NavMesh.AllAreas))
                            {
                                bool blockedRay = NavMesh.Raycast(c0.position, c1.position, out NavMeshHit rh, NavMesh.AllAreas);
                                dg += " | rayo pasillo->cama " + (blockedRay ? "corta en " + R.InverseTransformPoint(rh.position).ToString("F2") : "libre");
                                if (blockedRay)
                                {
                                    foreach (float hy in new[] { 0.1f, 0.5f, 1.2f, 2.0f })
                                    {
                                        Vector3 a0 = c0.position + Vector3.up * hy, a1 = c1.position + Vector3.up * hy;
                                        if (Physics.Raycast(a0, (a1 - a0).normalized, out RaycastHit ph, (a1 - a0).magnitude)) dg += " fis" + hy + "=" + R.InverseTransformPoint(ph.point).ToString("F2") + "n" + ph.normal.ToString("F1");
                                        else dg += " fis" + hy + "=libre";
                                    }
                                    Bounds cb2 = new Bounds(rh.position + Vector3.up * 0.8f, new Vector3(1.6f, 1.6f, 1.6f));
                                    foreach (Renderer rr in UnityEngine.Object.FindObjectsOfType<Renderer>()) if (rr.bounds.Intersects(cb2) && rr.bounds.size.magnitude < 12f) dg += " {" + rr.name + "@" + rr.transform.parent?.name + " " + rr.bounds.center.ToString("F1") + rr.bounds.size.ToString("F1") + "}";
                                    foreach (Collider cc in UnityEngine.Object.FindObjectsOfType<Collider>()) if (cc.bounds.Intersects(cb2) && cc.bounds.size.magnitude < 12f) dg += " [col " + cc.name + "@" + cc.transform.parent?.name + "]";
                                }
                                NavMeshPath pp = new NavMeshPath(); NavMesh.CalculatePath(c0.position, c1.position, NavMesh.AllAreas, pp);
                                dg += " camino " + pp.status;
                                NavMeshPath pq = new NavMeshPath(); NavMesh.CalculatePath(gen.PlayerStart, c0.position, NavMesh.AllAreas, pq);
                                dg += " inicio->pasillo " + pq.status;
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
                if (s3 == l3list[0]) L("INFO huella real - plano (m) por tipo:" + HotelDecor.OverReport());
                int longest = 0; foreach (var co in gen.Hotel.corridors) longest = Mathf.Max(longest, co.b - co.a + 1);
                Check(HotelDecor.Doors >= 60 && longest >= 12 && HotelDecor.Leaves.Count == gen.Hotel.doorways.Count, T + "puertas numeradas " + HotelDecor.Doors + " (que se abren " + HotelDecor.Leaves.Count + "), muebles " + HotelDecor.ItemCount + ", pasillos " + gen.Hotel.corridors.Count + " (el más largo " + (longest * cs).ToString("F0") + " m)");
            }
        }
        if (only == "" || only == "8")
        {   // v7.7.0: Nivel 114 (desierto): desde el ascensor se llega a pie a la puerta de piedra del pueblo de la salida y se cruza
            DesertFx.HazardsOn = false;
            foreach (int s8 in Level8Seeds)
            {
                gb.useRandomSeed = false; gb.seed = s8 - 8 * 101;
                int lc8 = gb.LoadCount; gb.LoadLevel(8);
                double t8 = EditorApplication.timeSinceStartup;
                while (!(gb.LoadCount > lc8 && gb.IsLoaded && GameManager.Instance != null && DesertWorld.Active != null) && EditorApplication.timeSinceStartup - t8 < 120) yield return 0.1f;
                yield return 1.0f;
                if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
                double tw = EditorApplication.timeSinceStartup; while (ElevatorCabin.PlayerLocked && EditorApplication.timeSinceStartup - tw < 20) yield return 0.25f;
                GameManager gm8 = GameManager.Instance; FirstPersonController pw = gm8.player; DesertWorld dw = DesertWorld.Active;
                string T8 = "[nivel 8 semilla " + gm8.generator.UsedSeed + "] ";
                Check(gm8.generator.UsedSeed == s8 && dw.ExitTransform != null && dw.plan.exitVillage >= 0, T8 + "pueblo de la salida " + dw.plan.exitVillage + " con puerta de piedra");
                // rejilla de 2 m sobre el tramo periódico: pendiente transitable y sin chocar (cápsula del jugador)
                const float G = 2f; int N = Mathf.RoundToInt(dw.L / G);
                // v7.7.0: sin máscara (el jugador está en Default): se ignoran solo sus colliders (su raíz es BackroomsWorld: no sirve)
                Collider[] hitBuf = new Collider[16];
                Transform dunes = dw.transform.Find("Dunes");
                Transform R = dw.levelRoot;
                System.Func<int, int, bool> free = (ix, iz) =>
                {
                    float x = (ix + 0.5f) * G, z = (iz + 0.5f) * G, y = DesertHeight.H(x, z);
                    Vector3 a0 = R.TransformPoint(new Vector3(x, y + 0.75f, z)), a1 = R.TransformPoint(new Vector3(x, y + 1.6f, z));
                    int nh = Physics.OverlapCapsuleNonAlloc(a0, a1, 0.32f, hitBuf, ~0, QueryTriggerInteraction.Ignore);
                    for (int q = 0; q < nh; q++) if (!hitBuf[q].transform.IsChildOf(pw.transform) && (dunes == null || !hitBuf[q].transform.IsChildOf(dunes))) return false;   // la arena no cuenta (pendiente aparte)
                    return true;
                };
                Vector3 st = dw.StartLocal, ex = dw.ExitLocal;
                int sx = ((Mathf.FloorToInt(st.x / G) % N) + N) % N, sz = ((Mathf.FloorToInt(st.z / G) % N) + N) % N;
                int ex0 = ((Mathf.FloorToInt(ex.x / G) % N) + N) % N, ez0 = ((Mathf.FloorToInt(ex.z / G) % N) + N) % N;
                var seen = new int[N * N]; for (int i = 0; i < seen.Length; i++) seen[i] = -1;
                var qx = new Queue<int>(); seen[sx * N + sz] = 0; qx.Enqueue(sx * N + sz); bool found = false; int steps = 0, blocked = 0;
                int[] DX = { 1, -1, 0, 0 }, DZ = { 0, 0, 1, -1 };
                while (qx.Count > 0)
                {
                    int c = qx.Dequeue(); int cx = c / N, cz = c % N;
                    if (Mathf.Abs(cx - ex0) <= 1 && Mathf.Abs(cz - ez0) <= 1) { found = true; steps = seen[c]; break; }
                    float hc = DesertHeight.H((cx + 0.5f) * G, (cz + 0.5f) * G);
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = (cx + DX[d] + N) % N, nz = (cz + DZ[d] + N) % N, ni = nx * N + nz;
                        if (seen[ni] != -1) continue;
                        float hn = DesertHeight.H((nx + 0.5f) * G, (nz + 0.5f) * G);
                        if (Mathf.Abs(hn - hc) > G * 0.8f) continue;   // < ~38° (límite del CharacterController 45°)
                        if (!free(nx, nz)) { seen[ni] = -2; blocked++; continue; }
                        seen[ni] = seen[c] + 1; qx.Enqueue(ni);
                    }
                }
                Check(found, T8 + "camino a pie inicio -> puerta de piedra por la arena (" + (found ? (steps * G).ToString("F0") + " m de rejilla" : "SIN CAMINO") + ", " + blocked + " celdas con obstáculo)");
                // cruzar la puerta: desde 5 m delante, andando hacia la losa, se completa el nivel
                Vector3 door = dw.ExitTransform.position, ew = R.TransformPoint(ex);
                Vector3 dir = ew - door; dir.y = 0f; dir.Normalize();
                Vector3 from = ew + dir * 4f; Vector3 fl = R.InverseTransformPoint(from); from = R.TransformPoint(new Vector3(fl.x, DesertHeight.H(fl.x, fl.z) + 0.15f, fl.z));
                if (UIManager.Instance != null && UIManager.Instance.Current != UIManager.Screen.None) UIManager.Instance.SetScreen(UIManager.Screen.None);   // con SEEDREACH_ONLY=8 el menú principal sigue abierto (bloquea la entrada)
                pw.Teleport(from, Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg);
                yield return 0.4f;
                pw.debugInput = true; pw.debugMove = new Vector2(0f, 1f);
                double tg = EditorApplication.timeSinceStartup;
                while (gm8.State == GameManager.GameState.Playing && EditorApplication.timeSinceStartup - tg < 12) yield return 0.1f;
                pw.debugInput = false; pw.debugMove = Vector2.zero;
                Vector3 endW = pw.transform.position, dd = endW - door; dd.y = 0f;
                int colAll = 0, colDune = 0; foreach (Collider cc in dw.GetComponentsInChildren<Collider>(true)) { colAll++; if (dunes != null && cc.transform.IsChildOf(dunes)) colDune++; }
                string diag8 = " fin " + R.InverseTransformPoint(endW).ToString("F1") + " desde " + fl.ToString("F1") + " a la losa " + dd.magnitude.ToString("F2") + " m, gm.exit=" + (gm8.exit != null ? gm8.exit.name : "null") + (gm8.exit == dw.ExitTransform ? "(losa)" : "") +
                    ", bloqueado " + ElevatorCabin.PlayerLocked + ", cc " + (pw.GetComponent<CharacterController>() != null && pw.GetComponent<CharacterController>().enabled) + ", colliders " + colAll + " (dunas " + colDune + "), dw==root " + (dw.transform == R) + ", input " + pw.InputEnabled + ", ui " + (UIManager.Instance != null ? UIManager.Instance.Current.ToString() : "-") + ", walk " + pw.walkSpeed.ToString("F2") + ", fx " + (DesertFx.Active != null ? DesertFx.Active.Busy + "/" + DesertFx.Active.Slips : "-") + ", ts " + Time.timeScale + ", st " + gm8.State;
                Check(gm8.State == GameManager.GameState.Won || gm8.State == GameManager.GameState.LevelComplete, T8 + "andando de pie hacia la puerta de piedra se sale del nivel (" + gm8.State + ")" + (gm8.State == GameManager.GameState.Playing ? diag8 : ""));
                yield return 1.5f;
            }
            DesertFx.HazardsOn = true;
        }
        if (only == "" || only == "9")
        {   // Nivel 7460: andando de verdad (CharacterController): salir del ascensor, recorrer el pasillo, entrar en una habitación y cruzar la salida
            foreach (int s9 in Level9Seeds)
            {
                gb.useRandomSeed = false; gb.seed = s9 - 9 * 101;
                int lc9 = gb.LoadCount; gb.LoadLevel(9);
                double t9 = EditorApplication.timeSinceStartup;
                while (!(gb.LoadCount > lc9 && gb.IsLoaded && GameManager.Instance != null && CorridorWorld.Active != null) && EditorApplication.timeSinceStartup - t9 < 120) yield return 0.1f;
                yield return 1.0f;
                if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
                double tw = EditorApplication.timeSinceStartup; while (ElevatorCabin.PlayerLocked && EditorApplication.timeSinceStartup - tw < 20) yield return 0.25f;
                if (UIManager.Instance != null && UIManager.Instance.Current != UIManager.Screen.None) UIManager.Instance.SetScreen(UIManager.Screen.None);
                GameManager gm9 = GameManager.Instance; FirstPersonController pw = gm9.player; CorridorWorld cw = CorridorWorld.Active; Transform R = cw.levelRoot;
                string T9 = "[nivel 9 semilla " + gm9.generator.UsedSeed + "] ";
                float cs = CorridorWorld.CS;
                Check(gm9.generator.UsedSeed == s9 && gm9.exit != null && cw.plan.exitRow > 0, T9 + "pasillo con salida en el módulo " + cw.plan.exitRow + " (" + (cw.plan.exitRow * cs).ToString("F0") + " m)");
                // 1) del ascensor al pasillo, andando
                pw.debugInput = true; pw.debugMove = new Vector2(0f, 1f);
                double tg = EditorApplication.timeSinceStartup; while (EditorApplication.timeSinceStartup - tg < 4.0) yield return 0.1f;
                pw.debugMove = Vector2.zero; yield return 0.2f;
                Vector3 l1 = R.InverseTransformPoint(pw.transform.position);
                Check(l1.z > 6f && Mathf.Abs(l1.x - cw.CenterX) < CorridorWorld.HW, T9 + "se sale del ascensor andando al pasillo (z " + l1.z.ToString("F1") + " m)");
                // 2) un tramo largo andando por el centro (corriendo) sin quedarse enganchado
                int r0 = Mathf.Max(3, cw.plan.exitRow - 26);   // el tramo (unos 12 módulos) acaba antes de la salida: con AutoKeyForTests, pasar a su lado ya gana el nivel
                pw.Teleport(R.TransformPoint(new Vector3(cw.CenterX, 0.1f, r0 * cs)), 0f); yield return 0.3f;
                pw.debugMove = new Vector2(0f, 1f); pw.debugSprint = true;
                tg = EditorApplication.timeSinceStartup; while (EditorApplication.timeSinceStartup - tg < 6.0) yield return 0.1f;
                pw.debugMove = Vector2.zero; pw.debugSprint = false; yield return 0.2f;
                Vector3 l2 = R.InverseTransformPoint(pw.transform.position);
                Check(l2.z - r0 * cs > 12f && l2.y > -0.2f, T9 + "corriendo por el pasillo se avanza (" + (l2.z - r0 * cs).ToString("F1") + " m en 6 s)");
                // 3) entrar en una habitación por su puerta (se abre al acercarse)
                int rr = -1, rs = 0; for (int j = 2; j < cw.plan.rows && rr < 0; j++) for (int sd = 0; sd < 2; sd++) if (cw.plan.doors[sd, j] == CorridorPlan.Door.Room) { rr = j; rs = sd; break; }
                if (rr >= 0)
                {
                    float sg = rs == CorridorPlan.Left ? -1f : 1f;
                    pw.Teleport(R.TransformPoint(new Vector3(cw.CenterX - sg * 0.6f, 0.1f, (rr + 0.5f) * cs)), rs == CorridorPlan.Left ? 270f : 90f); yield return 0.3f;
                    pw.debugMove = new Vector2(0f, 1f);
                    tg = EditorApplication.timeSinceStartup; while (EditorApplication.timeSinceStartup - tg < 4.0) yield return 0.1f;
                    pw.debugMove = Vector2.zero; yield return 0.2f;
                    Vector3 l3 = R.InverseTransformPoint(pw.transform.position);
                    Check(sg * (l3.x - cw.CenterX) > CorridorWorld.XR + 0.4f, T9 + "se entra andando en una habitación (x " + (l3.x - cw.CenterX).ToString("F2") + " m del centro; z " + (l3.z - (rr + 0.5f) * cs).ToString("F2") + ", yaw " + pw.transform.eulerAngles.y.ToString("F0") + ", ui " + (UIManager.Instance != null ? UIManager.Instance.Current.ToString() : "-") + ", estado " + gm9.State + ")");
                }
                // 4) salida: la llave buena y andar hacia la puerta
                Inventory.Instance.Add(ExitLock.Active.CorrectKey, 1); ExitLock.Active.ResetCooldownForTests();
                float eg = cw.plan.exitSide == CorridorPlan.Left ? -1f : 1f;
                pw.Teleport(R.TransformPoint(new Vector3(cw.CenterX - eg * 0.7f, 0.1f, (cw.plan.exitRow + 0.5f) * cs)), cw.plan.exitSide == CorridorPlan.Left ? 270f : 90f); yield return 0.3f;
                pw.debugMove = new Vector2(0f, 1f);
                tg = EditorApplication.timeSinceStartup; while (gm9.State == GameManager.GameState.Playing && EditorApplication.timeSinceStartup - tg < 10) yield return 0.1f;
                pw.debugInput = false; pw.debugMove = Vector2.zero;
                Check(gm9.State == GameManager.GameState.Won || gm9.State == GameManager.GameState.LevelComplete, T9 + "con la llave buena, andando hacia la salida se sale del nivel (" + gm9.State + ")");
                yield return 1.5f;
            }
        }
        if (only == "8" || only == "9") yield break;
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
