using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Nivel 7460 (El pasillo de las puertas): pruebas de edición (datos, recursos, plano por semilla, geometría) y de juego
// (ruidos a la espalda, latido, manilla de las cerradas, habitaciones, llave buena y salida, VIDA en el HUD).
public static class V7460Checks
{
    const int Lv = 9;

    public static void Edit(SmokeTest.Report r)
    {
        string T = "[7460] ";
        LevelConfig c = LevelConfig.Get(Lv);
        r.Check(c.layout == LayoutKind.Corridor && c.DisplayNumber == "7460" && c.name == "El pasillo de las puertas" && !c.HasMonsterAI && c.monsterModel == LevelConfig.NoMonsterId && c.monsterCount == 0, T + "nivel 9 = Nivel 7460 «" + c.name + "», sin entidades");
        r.Check(!string.IsNullOrEmpty(c.subtitle) && c.subtitle.Length > 40, T + "tarjeta del nivel con texto: «" + c.subtitle + "»");
        r.Check(Resources.Load<Texture2D>("UI/LevelThumbs/level_9") != null, T + "miniatura del selector de niveles (level_9)");
        r.Check(CorridorWorld.HasAssets && Resources.Load<TextAsset>("C7460/C7460Leaf") != null && Resources.Load<TextAsset>("C7460/C7460Room") != null, T + "módulo, hoja y habitación horneados en Blender (.bytes) y shader del pasillo");
        Texture2D lm = Resources.Load<Texture2D>("C7460/C7460_LM"), lr = Resources.Load<Texture2D>("C7460/C7460Room_LM");
        r.Check(lm != null && lr != null && lm.width >= 512 && lr.width >= 256, T + "mapas de luz horneados (Cycles + OIDN): " + (lm != null ? lm.width : 0) + " / " + (lr != null ? lr.width : 0) + " px");
        Shader sh = Shader.Find("Backrooms/CorridorBaked");
        r.Check(sh != null && sh.isSupported, T + "shader Backrooms/CorridorBaked compila");
        foreach (string n in new[] { "C7460Floor", "C7460Wall", "C7460Trim", "C7460Ceil", "C7460Brick" })
            r.Check(Resources.Load<Texture2D>("Textures/C7460/" + n + "_a") != null && Resources.Load<Texture2D>("Textures/C7460/" + n + "_n") != null && Resources.Load<Texture2D>("Textures/C7460/" + n + "_m") != null, T + "texturas PBR " + n + " (albedo, normal, máscara)");
        r.Check(LoreNotes.Valid(LoreNotes.IdFor(Lv, 2)) && LoreNotes.Get(LoreNotes.IdFor(Lv, 0)).text.Contains("puertas"), T + "3 notas de ambiente del nivel");

        // v7.10.2 (t500u): plano por semilla: un laberinto de pasillos largos unidos por transversales; determinista, una
        // salida, alcanzable y al menos 3 veces más camino que la v7.10.1 (salida en el 45–88 % de un único pasillo)
        bool det = true, okExit = true, differ = false, reach = true, longer = true, links = true; string ex = ""; int N = c.gridH, W = c.gridW;
        r.Check(W == CorridorPlan.GridWidthFor(5) && N == 140, T + "rejilla " + W + "×" + N + " (5 pasillos largos de 140 módulos)");
        int pmin = int.MaxValue; long psum = 0; int ns = 0;
        for (int seed = 1; seed <= 120; seed++)
        {
            int sd = seed == 1 ? 12345 : seed * 7919 + 1;
            bool[,] wv = new bool[W + 1, N], wh = new bool[W, N + 1];
            for (int x = 0; x <= W; x++) for (int z = 0; z < N; z++) wv[x, z] = true; for (int x = 0; x < W; x++) for (int z = 0; z <= N; z++) wh[x, z] = true;
            CorridorPlan a = CorridorPlan.Make(sd, W, N, wv, wh);
            if (seed <= 6)
            {
                CorridorPlan b = CorridorPlan.Make(sd, W, N, new bool[W + 1, N], new bool[W, N + 1]);
                if (a.runs.Count != b.runs.Count || a.exitRun != b.exitRun || a.exitRow != b.exitRow) det = false;
                else for (int q = 0; q < a.runs.Count; q++) for (int s = 0; s < 2; s++) for (int k = 0; k < a.runs[q].rows; k++) if (a.runs[q].doors[s, k] != b.runs[q].doors[s, k]) det = false;
                CorridorPlan o = CorridorPlan.Make(sd + 1, W, N, new bool[W + 1, N], new bool[W, N + 1]);
                if (o.exitRow != a.exitRow || o.exitRun != a.exitRun || o.CrossCount != a.CrossCount) differ = true;
                ex += " " + a.exitRun + "/" + a.exitRow + ":" + a.PathCells + "c/" + a.CrossCount + "x";
            }
            if (a.Count(CorridorPlan.Door.Exit) != 1) okExit = false;
            int[,] d = CorridorPlan.Bfs(wv, wh, W, N, new Vector2Int(a.Col, 0));
            Vector2Int ec = a.ExitCell;
            if (d[ec.x, ec.y] <= 0 || d[ec.x, ec.y] != a.PathCells) reach = false;
            for (int i = 0; i < a.Mains; i++) for (int k = 0; k < N; k++) if (d[a.ColOf(i), k] < 0) reach = false;   // todo el laberinto conectado
            if (a.PathCells < 3 * CorridorPlan.OldAvgPathCells) longer = false;
            // cada puerta que se abre (enlace) tiene su transversal y su pareja en el pasillo vecino
            int lk = a.Count(CorridorPlan.Door.Link); if (lk != 2 * a.CrossCount - a.DeadEnds || a.CrossCount - a.DeadEnds < 4 * 5 || a.DeadEnds < 4) links = false;
            pmin = Mathf.Min(pmin, a.PathCells); psum += a.PathCells; ns++;
        }
        r.Check(det && differ, T + "plano sale de la semilla (igual en todos los equipos, distinto en cada partida):" + ex);
        r.Check(okExit && reach, T + "120 semillas: una sola salida y todo el laberinto alcanzable desde el ascensor");
        r.Check(longer, T + "camino ascensor→salida ≥ 3× el de la v7.10.1 (mín " + pmin + " celdas, media " + (psum / (float)ns).ToString("F0") + " frente a ~" + CorridorPlan.OldAvgPathCells + ")");
        r.Check(links, T + "cada puerta que se abre da a un pasillo transversal que se une al pasillo vecino (≥ 5 por hueco) o, en los cruces y señuelos, acaba en una puerta cerrada (sin salida)");

        // ruidos a la espalda: graves, sin siseo ni crujidos
        string bad = ""; float worst = 0f;
        for (int k = 0; k < CorridorEvents.NoiseNames.Length; k++)
        {
            AudioClip cl = CorridorEvents.CreateNoise(k);
            float[] d = new float[cl.samples]; cl.GetData(d, 0);
            float hr = V790Checks.HighRatio(d, cl.frequency, 3000f), pk = 0f, tail = 0f;
            for (int i = 0; i < d.Length; i++) pk = Mathf.Max(pk, Mathf.Abs(d[i]));
            for (int i = d.Length - 200; i < d.Length; i++) tail = Mathf.Max(tail, Mathf.Abs(d[i]));
            // «sin siseo continuo»: la energía no está repartida por todo el clip (hay silencios entre golpes/sílabas)
            int quiet = 0, win = cl.frequency / 50; for (int w = 0; w + win < d.Length; w += win) { float e = 0f; for (int i = w; i < w + win; i++) e = Mathf.Max(e, Mathf.Abs(d[i])); if (e < 0.03f) quiet++; }
            float quietFrac = quiet / (float)(d.Length / win);
            worst = Mathf.Max(worst, hr);
            if (cl.length < 1f || cl.length > 5f || pk < 0.5f || pk > 0.95f || hr > 0.01f || tail > 0.02f || quietFrac < 0.25f) bad += " " + cl.name + "(" + (hr * 100f).ToString("F2") + " %, silencio " + (quietFrac * 100f).ToString("F0") + " %)";
            string lo = cl.name.ToLower(); if (lo.Contains("cruj") || lo.Contains("creak") || lo.Contains("hiss") || lo.Contains("sise")) bad += " (nombre)" + lo;
            Object.DestroyImmediate(cl);
        }
        r.Check(bad == "", T + "[Ruidos] pasos, puerta, golpes y susurro: graves, sin siseo (máx. " + (worst * 100f).ToString("F2") + " % por encima de 3 kHz), sin crujidos ni chasquido final" + bad);
        r.Check(CorridorEvents.GapMin >= 15f && CorridorEvents.GapMax <= 60f && CorridorEvents.DistMin >= 5f, T + "[Ruidos] cada " + CorridorEvents.GapMin + "–" + CorridorEvents.GapMax + " s, a " + CorridorEvents.DistMin + "–" + CorridorEvents.DistMax + " m a tu espalda");
    }

    // v7.10.2 (t500u): muchas semillas con el nivel generado de verdad: inicio → cada llave → salida por el laberinto
    public static void ManySeeds(SmokeTest.Report r, int count)
    {
        string T = "[7460 semillas] ";
        LevelConfig c = LevelConfig.Get(Lv);
        int ok = 0, farOk = 0, minPath = int.MaxValue, maxPath = 0; long sumPath = 0; string bad = "";
        System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            int seed = i == 0 ? 7460 : 1000003 * i + 17;
            GameObject go = new GameObject("SeedGen7460");
            BackroomsGenerator gen = go.AddComponent<BackroomsGenerator>();
            gen.config = c; gen.useRandomSeed = false; gen.seed = seed;
            gen.Generate();
            CorridorWorld w = go.GetComponentInChildren<CorridorWorld>(true);
            CorridorPlan P = gen.Corridor;
            bool good = w != null && P != null && gen.KeySpots.Count == ExitLock.KeyCount;
            int W = P != null ? P.Dist.GetLength(0) : 0, worst = int.MaxValue, maxD = 0;
            if (good)
            {
                for (int x = 0; x < W; x++) for (int z = 0; z < P.rows; z++) maxD = Mathf.Max(maxD, P.Dist[x, z]);
                Vector2Int st = new Vector2Int(P.Col, 0);
                foreach (Vector3 k in gen.KeySpots)
                {
                    Vector2Int kc = V7102Look.OpenNear(P, W, V7102Look.CellOf(w, k));
                    List<Vector2Int> a = V7102Look.Path(P, W, st, kc), b = V7102Look.Path(P, W, kc, P.ExitCell);
                    if (a.Count < 2 || b.Count < 2) { good = false; break; }
                    int tot = a.Count + b.Count - 2; worst = Mathf.Min(worst, tot);
                    if (P.Dist[kc.x, kc.y] < 0.5f * maxD) good = false;
                }
            }
            if (good) { ok++; if (worst >= 3 * CorridorPlan.OldAvgPathCells) farOk++; minPath = Mathf.Min(minPath, worst); maxPath = Mathf.Max(maxPath, worst); sumPath += worst; }
            else bad += " " + seed;
            gen.Clear(); Object.DestroyImmediate(go);
            if (i % 5 == 4) { UnityEditor.EditorUtility.UnloadUnusedAssetsImmediate(); System.GC.Collect(); }
        }
        r.Check(ok == count, T + count + " semillas generadas: desde el ascensor se llega a las 5 llaves y desde cada llave a la salida (" + ok + "/" + count + ")" + (bad != "" ? " fallan:" + bad : ""));
        r.Check(farOk == ok && ok > 0, T + "inicio → llave → salida, el más corto: " + minPath + "–" + maxPath + " celdas (media " + (ok > 0 ? (sumPath / (float)ok).ToString("F0") : "-") + "; ≥ 3× las ~" + CorridorPlan.OldAvgPathCells + " de la v7.10.1); " + (sw.ElapsedMilliseconds / 1000f).ToString("F0") + " s");
    }

    // geometría generada (en edición, tras gen.Generate())
    public static void Geometry(SmokeTest.Report r, string tag, BackroomsGenerator gen)
    {
        string T = tag + "[7460] ";
        CorridorWorld w = gen.Root.GetComponentInChildren<CorridorWorld>(true);
        r.Check(w != null && gen.Corridor != null, T + "pasillo construido por CorridorWorld");
        if (w == null) return;
        CorridorPlan P = gen.Corridor; int chunks = 0; foreach (CorridorPlan.Run ru in P.runs) chunks += (ru.rows + CorridorWorld.ChunkModules - 1) / CorridorWorld.ChunkModules;
        r.Check(w.ChunkCount == chunks && w.Runs.Count == P.runs.Count && w.RoomCount == 0 && w.LinkCount == P.Count(CorridorPlan.Door.Link), T + w.Runs.Count + " pasillos (" + P.Mains + " largos + " + P.CrossCount + " transversales, " + P.DeadEnds + " sin salida), " + w.ChunkCount + " tramos de 10 módulos, " + w.LinkCount + " puertas que se abren, sin habitaciones");
        r.Check(w.Length > 300f && gen.FixtureCount == w.TotalModules && w.TotalModules * CorridorWorld.CS > 2000f, T + "pasillos largos de " + w.Length.ToString("F0") + " m; " + (w.TotalModules * CorridorWorld.CS / 1000f).ToString("F2") + " km de pasillo con " + gen.FixtureCount + " plafones");
        r.Check(gen.KeySpots.Count == ExitLock.KeyCount, T + "5 sitios de llave");
        {   // llaves: en un pasillo (dentro de los muros) y lejos del ascensor por el camino (≥ 55 % del más lejano)
            int maxD = 0; for (int x = 0; x < P.Dist.GetLength(0); x++) for (int z = 0; z < P.Dist.GetLength(1); z++) maxD = Mathf.Max(maxD, P.Dist[x, z]);
            int inRun = 0, far = 0; string kd = "";
            foreach (Vector3 k in gen.KeySpots)
            {
                Vector3 l; int ri = w.RunAt(k, out l); if (ri >= 0 && Mathf.Abs(l.x - w.CenterX) < CorridorWorld.HW - 0.1f) inRun++;
                Vector2Int kc = V7102Look.OpenNear(P, gen.Corridor.Dist.GetLength(0), V7102Look.CellOf(w, k)); int dk = P.Dist[kc.x, kc.y];
                kd += " " + dk; if (dk >= 0.5f * maxD) far++;
            }
            r.Check(inRun == gen.KeySpots.Count && far == gen.KeySpots.Count, T + "las 5 llaves, junto a los zócalos y lejos del ascensor (a" + kd + " celdas; el punto más lejano a " + maxD + ")");
        }
        // recorrido a pie por el centro de cada pasillo sin chocar; paredes cerradas (un rayo lateral entre puertas da en pared)
        Physics.SyncTransforms();
        int blocked = 0, holes = 0; string firstHit = "";
        foreach (CorridorWorld.RunView rv in w.Runs)
        {
            Vector3 a = rv.F.TransformPoint(new Vector3(w.CenterX, 0.95f, 0.6f)), b = rv.F.TransformPoint(new Vector3(w.CenterX, 0.95f, rv.Length - 0.6f));
            foreach (RaycastHit h in Physics.SphereCastAll(a, 0.34f, (b - a).normalized, (b - a).magnitude)) { blocked++; if (firstHit == "") firstHit = rv.F.name + ": " + h.collider.name + " " + h.point; break; }
            for (int j = 0; j < rv.def.rows; j++) for (int s = -1; s <= 1; s += 2)
            {
                Vector3 o = rv.F.TransformPoint(new Vector3(w.CenterX, 1.2f, (j + 0.5f) * CorridorWorld.CS + 1.0f));
                if (!Physics.Raycast(o, rv.F.TransformDirection(new Vector3(s, 0, 0)), CorridorWorld.HW + 0.6f)) holes++;
            }
        }
        r.Check(blocked == 0, T + "se recorre cada pasillo entero por el centro sin chocar (" + w.Runs.Count + " pasillos) " + firstHit);
        r.Check(holes == 0, T + "paredes sin huecos (" + holes + " rayos que se escapan)");
        {   // cada transversal: por sus dos bocas se pasa (con la hoja abierta) del pasillo largo al transversal; cerrada, no
            int bad = 0; string bs = "";
            foreach (CorridorWorld.RunView rv in w.Runs)
            {
                if (!rv.def.cross) continue;
                foreach (int e in new[] { 0, 1 })
                {
                    if (e == 1 && rv.def.deadEnd) continue;
                    int main = e == 0 ? rv.def.a : rv.def.b, side = e == 0 ? CorridorPlan.Right : CorridorPlan.Left;
                    Vector3 from = w.DoorWorld(main, side, rv.def.row, 0.8f) + Vector3.up * 1.0f, to = rv.F.TransformPoint(new Vector3(w.CenterX, 1.0f, e == 0 ? 1.5f : rv.Length - 1.5f));
                    HotelDoorLeaf leaf = e == 0 ? rv.leafA : rv.leafB;
                    bool closedBlocks = leaf != null && leaf.blocker != null && leaf.blocker.enabled && Physics.Linecast(from, to);
                    if (leaf != null && leaf.blocker != null) leaf.blocker.enabled = false;
                    RaycastHit hh; bool free = !Physics.SphereCast(from, 0.3f, (to - from).normalized, out hh, (to - from).magnitude);
                    if (leaf != null && leaf.blocker != null) leaf.blocker.enabled = true;
                    if (!free || !closedBlocks) { bad++; if (bs == "") bs = " " + rv.F.name + "/" + e + (free ? "" : " choca con " + hh.collider.name) + (closedBlocks ? "" : " (cerrada no bloquea)"); }
                }
            }
            r.Check(bad == 0, T + "las " + (2 * P.CrossCount - P.DeadEnds) + " bocas de los transversales: abiertas se pasa, cerradas no (" + bad + " fallos)" + bs);
        }
        // salida: de pie delante, se llega a < 1,6 m
        Vector3 st = w.ExitDoorWorld(0.75f);
        r.Check(!Physics.CheckSphere(st + Vector3.up * 0.95f, 0.34f) && Vector3.Distance(Flat(st), Flat(gen.ExitPosition)) < 1.6f, T + "delante de la salida hay sitio y queda a <1,6 m (pasillo " + P.exitRun + ", módulo " + P.exitRow + ")");
        ExitDoorCue cue = gen.Root.GetComponentInChildren<ExitDoorCue>(true);
        r.Check(cue != null && cue.visibleDistance <= 8.01f && !cue.ledRenderer.enabled && !cue.glow.enabled, T + "pista de la salida apagada y solo a < 8 m");
        // presupuesto
        int tris = 0, rends = 0; foreach (MeshFilter mf in gen.Root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) { tris += mf.sharedMesh.triangles.Length / 3; rends++; }
        r.Info(T + "triángulos " + tris + ", mallas " + rends);
        r.Check(tris < 4500000 && rends < 2400, T + "presupuesto: " + tris + " triángulos en " + rends + " mallas en todo el laberinto (v7.10.2: solo se dibuja el pasillo en el que estás y los que se ven por puertas abiertas)");
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // recorrido en juego. Termina en la puerta de salida abierta (el llamador sigue en OceanExit)
    static float Lateral(CorridorWorld w, Vector3 pos) { return Mathf.Abs(w.levelRoot.InverseTransformPoint(pos).x - w.CenterX); }
    // media RGB de lo que ve la cámara (64×36)
    static Vector3 MeanRgb(Camera cam)
    {
        if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return Vector3.zero;
        RenderTexture rt = RenderTexture.GetTemporary(256, 144, 24, RenderTextureFormat.ARGB32); RenderTexture o = cam.targetTexture;
        cam.targetTexture = rt; if (CorridorPlanar.Instance != null) CorridorPlanar.Instance.Refresh(cam); cam.Render(); cam.targetTexture = o;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tx = new Texture2D(256, 144, TextureFormat.RGB24, false); tx.ReadPixels(new Rect(0, 0, 256, 144), 0, 0); tx.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        Vector3 m = Vector3.zero; Color[] px = tx.GetPixels(); foreach (Color c in px) m += new Vector3(c.r, c.g, c.b); Object.DestroyImmediate(tx);
        return m / Mathf.Max(1, px.Length);
    }

    public static IEnumerator Play(SmokeTest.Report r, string tag, GameManager gm)
    {
        string T = tag + "[7460] ";
        CorridorWorld w = CorridorWorld.Active; CorridorEvents ev = CorridorEvents.Active;
        r.Check(w != null && ev != null, T + "pasillo y ruidos activos");
        if (w == null || ev == null) yield break;
        FirstPersonController p = gm.player; Camera cam = gm.playerCamera.GetComponent<Camera>();
        r.Check(gm.monsters.Length == 0 && MonsterAI.Instances.Count == 0, T + "sin entidades");
        r.Check(cam.farClipPlane >= 400f, T + "se ve el fondo del pasillo (plano lejano " + cam.farClipPlane + " m)");
        r.Check(ExitLock.Active != null && gm.exit != null, T + "salida con cerradura (sistema de llaves)");
        r.Check(gm.healthFill != null && gm.healthFill.gameObject.activeInHierarchy && Health.Instance != null, T + "VIDA en el HUD");
        if (Health.Instance != null && gm.healthFill != null)
        {
            Health.Instance.Damage(30f, "prueba"); yield return 0.3f;
            r.Check(Mathf.Abs(gm.healthFill.anchorMax.x - Health.Instance.Health01) < 0.02f && Health.Instance.Health01 < 0.75f, T + "la barra de VIDA baja con el daño (" + gm.healthFill.anchorMax.x.ToString("F2") + ")");
            Health.Instance.Heal(100f); yield return 0.2f;
            r.Check(gm.healthFill.anchorMax.x > 0.98f, T + "y se llena al curarse");
        }

        // ruidos: siempre a tu espalda, aceleran el latido; los pasos se callan si te giras
        Vector3 mid = w.levelRoot.TransformPoint(new Vector3(w.CenterX, 0.1f, 40f * CorridorWorld.CS));
        p.Teleport(mid, 0f); yield return 0.4f;
        CorridorEvents.DisableForTests = true;
        bool allBehind = true; string dots = "";
        for (int k = 0; k < 4; k++)
        {
            Adrenaline.ScareThreat = 0f;
            ev.Play(k); yield return 0.1f;
            dots += " " + ev.LastNoise + " " + ev.LastBehindDot.ToString("F2");
            if (ev.LastBehindDot > -0.8f || !ev.Source.isPlaying || Adrenaline.ScareThreat < 0.3f) allBehind = false;
            yield return 0.4f;
        }
        r.Check(allBehind && ev.NoiseEvents >= 4, T + "[Ruidos] los 4 suenan detrás de ti (3D) y aceleran el latido:" + dots);
        { string rmsS = ""; bool loud = true; foreach (AudioClip c in ev.Clips) { float v = CorridorEvents.Rms(c); rmsS += " " + c.name + " " + v.ToString("F2"); if (v < 0.16f) loud = false; }
          r.Check(loud && ev.Source.maxDistance <= 40f && CorridorEvents.DistMax <= 20f, T + "[Ruidos] se oyen claros (RMS de la parte con sonido >= 0,16; a 6-20 m):" + rmsS); }
        p.Teleport(mid, 90f); yield return 0.3f;
        ev.Play(1); yield return 0.1f;
        r.Check(ev.LastBehindDot < -0.8f, T + "[Ruidos] mirando a la pared, también a la espalda (" + ev.LastBehindDot.ToString("F2") + ")");
        p.Teleport(mid, 0f); yield return 0.3f;
        int stopped = ev.StoppedByLook;
        ev.Play(0); yield return 0.3f;
        p.Teleport(mid, 180f); yield return 0.3f;
        r.Check(ev.StoppedByLook == stopped + 1 && !ev.Source.isPlaying, T + "[Ruidos] los pasos se callan cuando te giras a mirar");
        {
            Adrenaline ad = p.GetComponent<Adrenaline>(); Adrenaline.ScareThreat = 0f; yield return 4f; float bpm0 = ad != null ? ad.Bpm : 0f;
            ev.Play(3); yield return 2.5f;
            float bpm1 = ad != null ? ad.Bpm : 0f;
            r.Check(bpm1 > bpm0 + 8f, T + "[Latido] un susurro a la espalda acelera el corazón (" + bpm0.ToString("F0") + " → " + bpm1.ToString("F0") + " ppm)");
        }

        // v7.10.1 (t498u): la cordura NO baja nunca en el 7460: linterna apagada, ruidos a la espalda y luz roja; el latido sí sube
        {
            Sanity sn = Sanity.Instance; Flashlight fl = p.GetComponentInChildren<Flashlight>();
            r.Check(sn != null && Sanity.NoDrainLevel, T + "[Cordura] el 7460 es nivel sin drenaje");
            if (sn != null)
            {
                if (fl != null) fl.SetOn(false);
                float v0 = 70f; sn.value = v0; float sMin = v0, rateMin = 0f; int plays = 0; float bmax = 0f;
                CorridorRed cr = CorridorRed.Active;
                for (int i = 0; i < 60; i++)
                {
                    if (i % 8 == 0) { ev.Play((i / 8) % 4); plays++; }
                    if (cr != null && i == 20) CorridorRed.ForceRed = 1f;
                    if (cr != null && i == 40) CorridorRed.ForceRed = -1f;
                    yield return 0.25f;
                    sMin = Mathf.Min(sMin, sn.value); rateMin = Mathf.Min(rateMin, sn.DrainRate); bmax = Mathf.Max(bmax, Adrenaline.ScareThreat);
                }
                r.Check(sMin >= v0 - 0.001f && rateMin >= 0f && sn.value >= v0 - 0.001f && plays >= 7 && bmax > 0.3f,
                    T + "[Cordura] 15 s a oscuras con " + plays + " ruidos y luz roja: la cordura nunca baja (70 → mín " + sMin.ToString("F2") + ", ahora " + sn.value.ToString("F2") + "; tasa mín " + rateMin.ToString("F2") + "/s) y el latido sí sube (" + bmax.ToString("F2") + ")");
                if (fl != null) fl.SetOn(true);
                sn.value = 100f;
            }
        }

        // v7.10.1 (t499u): luz roja de emergencia (como el Nivel 0): calendario por semilla, todo el pasillo rojo, sirena, y vuelve
        {
            CorridorRed cr = CorridorRed.Active;
            r.Check(cr != null && cr.Schedule.Count > 10, T + "[Rojo] sucesos de luz roja en el pasillo");
            if (cr != null)
            {
                var s2 = new System.Collections.Generic.List<Level0Events.RedEvent>(); CorridorRed.BuildSchedule(cr.Seed, s2);
                bool same = s2.Count == cr.Schedule.Count; bool durOk = true;
                for (int i = 0; i < s2.Count && same; i++) same = s2[i].start == cr.Schedule[i].start && s2[i].dur == cr.Schedule[i].dur;
                foreach (Level0Events.RedEvent e in cr.Schedule) if (e.dur < CorridorRed.RedMin - 1e-3f || e.dur > CorridorRed.RedMax + 1e-3f) durOk = false;
                r.Check(same && durOk && cr.Schedule[0].start >= CorridorRed.FirstMin && cr.Schedule[0].start <= CorridorRed.FirstMax,
                    T + "[Rojo] calendario determinista por semilla (1.º a " + cr.Schedule[0].start.ToString("F0") + " s, " + CorridorRed.RedMin + "–" + CorridorRed.RedMax + " s cada uno; el anfitrión manda el reloj)");
                CorridorRed.ForceRed = -1f;
                Vector3 eye = mid + Vector3.up * 1.5f;
                p.Teleport(mid, 0f); cr.DebugSetClock(1f); yield return 0.6f;
                float rN = Shader.GetGlobalFloat(CorridorRed.RedId); Color fogN = RenderSettings.fogColor; Vector3 pixN = MeanRgb(cam);
                Level0Events.RedEvent e0 = cr.Schedule[0];
                cr.DebugSetClock(e0.start + e0.dur * 0.5f); yield return 0.6f;
                float rR = Shader.GetGlobalFloat(CorridorRed.RedId); Color fogR = RenderSettings.fogColor; Vector3 pixR = MeanRgb(cam);
                AudioSource al = cr.AlarmSource;
                bool alarm = al != null && al.isPlaying && al.volume > 0.05f && al.clip != null && al.clip.name == "C7460_Alarma";
                r.Check(cr.RedOn && rR > 0.99f && rN < 0.001f && fogR.r > fogR.g * 4f && fogN.g > fogN.r * 0.8f, T + "[Rojo] durante el suceso el shader horneado y la niebla pasan a rojo (_C7460Red " + rN.ToString("F2") + " → " + rR.ToString("F2") + ")");
                if (SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null && pixN.x > 0.01f)
                    r.Check(pixR.x > pixR.y * 3f && pixR.x > pixR.z * 3f && pixN.x < pixN.y * 1.6f, T + "[Rojo] la imagen entera es roja (media RGB normal " + pixN.ToString("F3") + " → rojo " + pixR.ToString("F3") + ")");
                r.Check(alarm, T + "[Rojo] suena la sirena (la del Nivel 0 v7.9.4, 440–660 Hz; vol " + (al != null ? al.volume.ToString("F2") : "-") + ")");
                r.Check(Sanity.Instance == null || Sanity.Instance.DrainRate >= 0f, T + "[Rojo] el rojo no baja la cordura");
                cr.DebugSetClock(e0.End + 1f); yield return 0.6f;
                r.Check(!cr.RedOn && Shader.GetGlobalFloat(CorridorRed.RedId) < 0.001f && (al == null || !al.isPlaying) && Mathf.Abs(RenderSettings.fogColor.r - fogN.r) < 0.01f, T + "[Rojo] al acabar vuelve la luz de siempre y calla la sirena");
                cr.NetClock(e0.start + 1.5f); yield return 0.3f;
                r.Check(cr.RedOn && cr.NetClocks > 0, T + "[Rojo] con el reloj del anfitrión (red) el cliente entra en rojo a la vez");
                cr.DebugSetClock(1f); yield return 0.3f;
                r.Check(Level0Events.Active == null, T + "[Rojo] el Nivel 0 no se toca (sus sucesos no existen aquí)");
            }
        }
        CorridorEvents.DisableForTests = false;

        // manilla de una puerta cerrada
        int lr = -1, ls = 0;
        for (int j = 6; j < w.plan.rows && lr < 0; j++) for (int s = 0; s < 2; s++) if (w.plan.doors[s, j] == CorridorPlan.Door.Locked) { lr = j; ls = s; break; }
        if (lr >= 0)
        {
            int r0 = w.Rattles; Vector3 dp = w.DoorWorld(ls, lr, 0.55f);
            p.Teleport(dp, ls == CorridorPlan.Left ? 270f : 90f); yield return 0.8f;
            r.Check(w.Rattles > r0, T + "una puerta cerrada: la manilla baja pero no abre (" + (w.Rattles - r0) + " intentos)");
        }
        // v7.10.2: una puerta que se abre: se abre al acercarte y da a un pasillo transversal; por él se llega al pasillo vecino
        {
            CorridorWorld.RunView cr = null; foreach (CorridorWorld.RunView rv in w.Runs) if (rv.def.cross && rv.def.a == 0 && !rv.def.deadEnd) { cr = rv; break; }
            r.Check(cr != null, T + "[Laberinto] el pasillo del ascensor tiene transversales");
            if (cr != null)
            {
                CorridorEvents.DisableForTests = true;
                Vector3 dp = w.DoorWorld(0, CorridorPlan.Right, cr.def.row, 0.6f);
                p.Teleport(dp, 90f); yield return 1.2f;
                r.Check(cr.leafA != null && cr.leafA.IsOpen, T + "[Laberinto] la puerta se abre al acercarte (" + (cr.leafA != null ? cr.leafA.Angle.ToString("F0") : "-") + "°)");
                r.Check(cr.visible && w.VisibleRuns >= 2, T + "[Laberinto] con la puerta abierta se dibuja el transversal (" + w.VisibleRuns + " de " + w.Runs.Count + " pasillos)");
                // cruzar a pie (corriendo) todo el transversal hasta el pasillo 1
                List<Vector2Int> path = V7102Look.Path(w.plan, w.plan.Dist.GetLength(0), new Vector2Int(w.plan.ColOf(0), cr.def.row), new Vector2Int(w.plan.ColOf(1), cr.def.row + 1));
                float[] res = new float[3]; float ts = Time.timeScale; Time.timeScale = 1.5f;
                { IEnumerator wi = V7102Look.Walk(p, w, path, res, 40f); while (wi.MoveNext()) yield return wi.Current; }
                Time.timeScale = ts;
                Vector3 lp; int ri = w.RunAt(p.transform.position, out lp);
                r.Check(res[0] > 0f && ri == 1 && w.CurrentRun == 1, T + "[Laberinto] a pie por el transversal (" + res[1].ToString("F0") + " m) se llega al pasillo largo vecino (pasillo " + ri + ")");
                // en el transversal: reflejo (la sonda va con él), ruidos a la espalda dentro de él y luz roja
                p.Teleport(cr.F.TransformPoint(new Vector3(w.CenterX, 0.1f, cr.Length * 0.5f)), cr.F.eulerAngles.y); yield return 0.6f;
                r.Check(w.CurrentRun == cr.def.id, T + "[Laberinto] dentro del transversal, es el pasillo actual (" + w.CurrentRun + ")");
                if (w.Probe != null)
                {
                    Vector3 pl = cr.F.InverseTransformPoint(w.Probe.transform.position);
                    r.Check(Mathf.Abs(pl.x - w.CenterX) < 0.1f && pl.z > 0f && pl.z < cr.Length && w.Probe.size.x > 20f, T + "[Reflejo] la sonda del suelo se va al transversal y gira con él (tamaño " + w.Probe.size + ")");
                }
                int bin = 0; string bs = "";
                for (int k = 0; k < 4; k++) { ev.Play(k); yield return 0.1f; Vector3 lb; int rb = w.RunAt(ev.Source.transform.position, out lb); if (rb == cr.def.id && ev.LastBehindDot < -0.8f) bin++; bs += " " + ev.LastBehindDot.ToString("F2"); yield return 0.3f; }
                r.Check(bin == 4, T + "[Ruidos] en el transversal también suenan a tu espalda, dentro del pasillo (" + bin + "/4:" + bs + ")");
                CorridorRed.ForceRed = 1f; yield return 0.6f;
                r.Check(Shader.GetGlobalFloat(CorridorRed.RedId) > 0.99f && RenderSettings.fogColor.r > RenderSettings.fogColor.g * 4f, T + "[Rojo] la luz roja también tiñe los transversales (es global)");
                CorridorRed.ForceRed = -1f; yield return 0.3f;
                CorridorEvents.DisableForTests = false;
            }
        }

        // v7.10.1 (t497u): las puertas cerradas son sólidas: andando y corriendo contra cada tipo no se atraviesa ninguna
        {
            CorridorEvents.DisableForTests = true;
            ExitLock.AutoKeyForTests = false;
            foreach (ItemType kk in ItemDefs.Keys) { int guard = 20; while (Inventory.Instance.HasKey(kk) && guard-- > 0) Inventory.Instance.RemoveOne(kk); }
            r.Check(w.LockedBlocks == w.plan.Count(CorridorPlan.Door.Locked) && w.LockedBlocks > 20, T + "[Puertas] cada puerta cerrada lleva colisión (" + w.LockedBlocks + " de " + w.plan.Count(CorridorPlan.Door.Locked) + ")");
            r.Check(w.ExitBlock != null && w.ExitBlock.enabled, T + "[Puertas] la salida lleva colisión");
            string res = ""; bool okAll = true; int tested = 0;
            CorridorPlan.Door[] kinds = { CorridorPlan.Door.Locked, CorridorPlan.Door.Bricked, CorridorPlan.Door.Exit, CorridorPlan.Door.Link };
            foreach (CorridorPlan.Door kind in kinds)
            {
                int jr = -1, js = 0, jrun = 0;
                for (int q = 0; q < w.plan.Mains && jr < 0; q++)
                    for (int j = 4; j < w.plan.rows - 2 && jr < 0; j++) for (int sd = 0; sd < 2; sd++) if (w.plan.runs[q].doors[sd, j] == kind) { jr = j; js = sd; jrun = q; break; }
                if (jr < 0) { res += " " + kind + ":-"; continue; }
                float limit = kind == CorridorPlan.Door.Bricked ? CorridorWorld.XR + 1.0f : CorridorWorld.HW + 0.02f;   // tapiada: nicho de 1 m y ladrillo
                for (int sp = 0; sp < 2; sp++)
                {
                    p.SetStaminaForTests(p.maxStamina);
                    p.Teleport(w.DoorWorld(jrun, js, jr, 1.2f) + Vector3.up * 0.05f, w.FrameOf(jrun).eulerAngles.y + (js == CorridorPlan.Left ? 270f : 90f)); yield return 0.3f;
                    p.debugInput = true; p.debugMove = new Vector2(0f, 1f); p.debugSprint = sp == 1;
                    float lat = 0f, tw = 0f; bool sprinted = false;
                    while (tw < 2.5f) { yield return 0.05f; tw += 0.05f; lat = Mathf.Max(lat, Mathf.Abs(w.FrameOf(jrun).InverseTransformPoint(p.transform.position).x - w.CenterX)); if (p.IsSprinting) sprinted = true; }
                    p.debugInput = false; p.debugSprint = false; p.debugMove = Vector2.zero;
                    bool ok = kind == CorridorPlan.Door.Link ? lat > CorridorWorld.XR + 0.3f : lat < limit && gm.State == GameManager.GameState.Playing;
                    if (sp == 1 && !sprinted) ok = false;
                    okAll &= ok; tested++;
                    res += " " + kind + (sp == 1 ? "/correr " : "/andar ") + lat.ToString("F2") + (ok ? "" : "!");
                }
            }
            r.Check(okAll && tested >= 8, T + "[Puertas] contra cerrada, tapiada y salida sin llave no se pasa; la que se abre (al transversal) sí (distancia lateral máx. al centro, muro " + CorridorWorld.HW + " m):" + res);
            CorridorEvents.DisableForTests = false;
        }

        // salida: sin llave no abre, con la llave buena sí
        ExitLock.AutoKeyForTests = false;
        foreach (ItemType k in ItemDefs.Keys) { int guard = 20; while (Inventory.Instance.HasKey(k) && guard-- > 0) Inventory.Instance.RemoveOne(k); }
        ExitLock.Active.ResetCooldownForTests();
        Vector3 st = w.ExitDoorWorld(0.75f);
        p.Teleport(st + Vector3.up * 0.1f, w.ExitYaw); yield return 0.8f;
        r.Check(gm.State == GameManager.GameState.Playing && !ExitLock.Active.Unlocked && ExitLock.Active.LockedNotices > 0, T + "la salida está cerrada con llave");
        p.Teleport(mid, 0f); yield return 0.3f;
        Inventory.Instance.Add(ExitLock.Active.CorrectKey, 1);
        ExitLock.Active.ResetCooldownForTests();
        p.Teleport(st + Vector3.up * 0.1f, w.ExitYaw);
        float t0 = Time.realtimeSinceStartup;
        while (gm.State == GameManager.GameState.Playing && Time.realtimeSinceStartup - t0 < 5f) yield return 0.1f;
        r.Check(ExitLock.Active == null || ExitLock.Active.Unlocked, T + "la llave buena abre la salida");
        r.Check(w.ExitLeaf == null || Mathf.Abs(w.ExitAngle) > 1f || gm.State != GameManager.GameState.Playing, T + "la hoja de la salida se abre");
    }
}
