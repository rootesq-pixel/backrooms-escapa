// v7.2.0: capturas EN EL EJECUTABLE (no en el editor), para comparar con el editor y con Blender. Solo con «-shotcheck <carpeta>»
// (y -level 2 o 5 -nointro). Semilla fija 7110. Nivel 2: las 3 cámaras de Blender en la sala de piscina + 2 pasillos inundados;
// nivel 5: prado, colinas y casas (fuera y dentro). Escribe PPM (el juego no lleva conversión a PNG), shotcheck.txt y cierra.
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public class ShotBuildCheck : MonoBehaviour
{
    int savedQuality = -2;
    void RestoreQuality()
    {
        if (savedQuality == -2) return;
        if (savedQuality < 0) PlayerPrefs.DeleteKey("opt_quality"); else PlayerPrefs.SetInt("opt_quality", savedQuality);
        PlayerPrefs.Save(); savedQuality = -2;
    }
    void OnApplicationQuit() { RestoreQuality(); }
    string dir; readonly StringBuilder rep = new StringBuilder(); int pass, fail;

    public static void MaybeStart(GameObject host)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == "-shotcheck")
            {
                ShotBuildCheck c = host.AddComponent<ShotBuildCheck>(); c.dir = a[i + 1];
                GameBootstrap gb = host.GetComponent<GameBootstrap>(); if (gb != null) { gb.useRandomSeed = false; gb.seed = 7110; }
                // v7.3.0: «-shotquality N» (0 Bajo, 1 Medio, 2 Alto) solo para esta ejecución: se restaura la preferencia del jugador al salir
                for (int k = 0; k < a.Length - 1; k++)
                    if (a[k] == "-shotquality" && int.TryParse(a[k + 1], out int q)) { c.savedQuality = PlayerPrefs.HasKey("opt_quality") ? PlayerPrefs.GetInt("opt_quality") : -1; GameSettings.Quality = q; }
            }
    }
    void Check(bool ok, string m) { if (ok) pass++; else fail++; rep.AppendLine((ok ? "PASS " : "FAIL ") + m); Debug.Log("[ShotBuildCheck] " + (ok ? "PASS " : "FAIL ") + m); }
    void Info(string m) { rep.AppendLine("INFO " + m); Debug.Log("[ShotBuildCheck] INFO " + m); }

    const float SX = (16f - 0.2f) / 14f, SZ = (12f - 0.2f) / 10f, DECK = 0.28f;
    static Vector3 B(float x, float y, float z) { return new Vector3(x * SX, z + DECK, y * SZ); }
    static float VFov(float lensMm) { return 2f * Mathf.Atan(Mathf.Tan(Mathf.Atan(18f / lensMm)) * 9f / 16f) * Mathf.Rad2Deg; }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        float t0 = Time.realtimeSinceStartup;
        while ((GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded || GameManager.Instance == null || FirstPersonController.LocalInstance == null) && Time.realtimeSinceStartup - t0 < 300f) yield return null;
        Info("cargado en " + (Time.realtimeSinceStartup - t0).ToString("F1") + " s | por pasos " + LoadDiag.LastTotalSeconds.ToString("F2") + " s");
        if (ElevatorCabin.Current != null) ElevatorCabin.Current.Skip();
        float w = 0f; while (ElevatorCabin.PlayerLocked && w < 20f) { yield return new WaitForSeconds(0.25f); w += 0.25f; }
        GameManager gm = GameManager.Instance; FirstPersonController pw = gm.player;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        Flashlight fl = pw.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(false);
        GameManager.HideControlsHint = true;
        Camera mc = Camera.main;
        Info("GPU " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + ", calidad " + GameSettings.Quality + ", nivel " + LevelConfig.Get(GameBootstrap.Instance.CurrentLevel).name);
        yield return new WaitForSeconds(1.5f);
        if (mc == null) { Check(false, "cámara del jugador"); Finish(); yield break; }
        mc.cullingMask &= ~(1 << 8);
        if (PoolRoomSetPiece.Root != null) yield return Pool(gm, pw, mc);
        else if (FieldWorld.Active != null) yield return Field(gm, pw, mc);
        else if (OceanWorld.Active != null && System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-swimseq") >= 0) yield return SwimSeq(gm, pw, mc);
        else if (OceanWorld.Active != null) yield return Ocean(gm, pw, mc);
        else if (GameBootstrap.Instance.CurrentLevel == 0) yield return Level0(gm, pw, mc);
        else if (GameBootstrap.Instance.CurrentLevel == 1) yield return Level1(gm, pw, mc);
        else if (BallPitField.All.Count > 0) yield return Ballpit(gm, pw, mc);
        else Check(false, "nivel con sala de piscina o campo");
        Finish();
    }

    IEnumerator View(FirstPersonController pw, Camera mc, string n, Vector3 p, Vector3 t, float fov, Vector3 stand)
    {
        pw.Teleport(stand, Mathf.Atan2(t.x - p.x, t.z - p.z) * Mathf.Rad2Deg);
        // al menos 2 s reales: en -batchmode los fotogramas duran ~2 ms y las luces (LightCuller) tardan en encenderse
        float t0 = Time.realtimeSinceStartup;
        for (int k = 0; k < 30 || Time.realtimeSinceStartup - t0 < 2f; k++) { mc.transform.position = p; mc.transform.rotation = Quaternion.LookRotation(t - p); mc.fieldOfView = fov; yield return null; }
        yield return new WaitForSecondsRealtime(0.3f);
        mc.transform.position = p; mc.transform.rotation = Quaternion.LookRotation(t - p); mc.fieldOfView = fov;
        if (PoolPlanarReflection.Instance != null) PoolPlanarReflection.Instance.Refresh(mc);
        if (FieldGrass.Active != null) FieldGrass.Active.Submit();
        Texture2D tex = Grab(mc, n);
        float minMean = n.Contains("a_oscuras") ? 0.008f : 0.03f;   // el plano a oscuras es negro a propósito (solo señuelo y fotóforos)
        Check(Mean(tex) > minMean, "captura " + n + " no negra (media " + Mean(tex).ToString("F3") + ", mínimo " + minMean.ToString("F3") + ")");
        Destroy(tex);
        // tiempo real de fotograma del ejecutable en esta vista (sin vsync), mediana de 120 fotogramas
        int vs0 = QualitySettings.vSyncCount, tf0 = Application.targetFrameRate; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        var ft = new System.Collections.Generic.List<float>();
        for (int k = 0; k < 150; k++) { mc.transform.position = p; mc.transform.rotation = Quaternion.LookRotation(t - p); mc.fieldOfView = fov; yield return null; if (k >= 30) ft.Add(Time.unscaledDeltaTime * 1000f); }
        QualitySettings.vSyncCount = vs0; Application.targetFrameRate = tf0;
        ft.Sort(); Info("fotograma " + n + ": mediana " + ft[ft.Count / 2].ToString("F2") + " ms, p90 " + ft[ft.Count * 9 / 10].ToString("F2") + " ms (" + Screen.width + "x" + Screen.height + ")");
    }

    // v7.3.0: nivel 0 desde un ángulo parecido al de la foto clásica: de pie en una sala abierta, mirando en diagonal entre pilares
    IEnumerator Level0(GameManager gm, FirstPersonController pw, Camera mc)
    {
        VhsEffect vx = mc.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        BackroomsGenerator gen = gm.generator; float cs = gen.CellSize; int mask = ~((1 << 2) | (1 << 8));
        Info("nivel 0: pilares " + gen.PillarCount + (LevelConfig.OldL0 ? " (look antiguo, -oldl0)" : ""));
        var cand = new System.Collections.Generic.List<(float s, Vector3 p, Vector3 d)>();
        for (int cx = 1; cx < gen.GridWidth - 1; cx++) for (int cz = 1; cz < gen.GridHeight - 1; cz++)
        {
            Vector3 wp = gen.Root.TransformPoint(new Vector3((cx + 0.5f) * cs, 1.55f, (cz + 0.5f) * cs));
            for (int k = 0; k < 4; k++)
            {
                Vector3 d = Quaternion.Euler(0f, 45f + 90f * k + 12f, 0f) * Vector3.forward;
                float L = Physics.Raycast(wp, d, out RaycastHit h, 40f, mask, QueryTriggerInteraction.Ignore) ? h.distance : 40f;
                float La = Physics.Raycast(wp, Quaternion.Euler(0f, -30f, 0f) * d, out h, 40f, mask, QueryTriggerInteraction.Ignore) ? h.distance : 40f;
                float Lb = Physics.Raycast(wp, Quaternion.Euler(0f, 30f, 0f) * d, out h, 40f, mask, QueryTriggerInteraction.Ignore) ? h.distance : 40f;
                float Lback = Physics.Raycast(wp, -d, out h, 3f, mask, QueryTriggerInteraction.Ignore) ? h.distance : 3f;
                if (Lback < 1.2f) continue;
                cand.Add((Mathf.Min(L, 24f) + 0.6f * Mathf.Min(La, 16f) + 0.6f * Mathf.Min(Lb, 16f), wp - d * 1.0f, d));
            }
        }
        cand.Sort((a, b) => b.s.CompareTo(a.s));
        var used = new System.Collections.Generic.List<Vector3>(); int n = 0;
        Vector3 st2 = Vector3.zero, p2 = Vector3.zero, d2 = Vector3.forward;
        foreach (var c in cand)
        {
            bool far = true; foreach (Vector3 u in used) if ((u - c.p).sqrMagnitude < 100f) far = false;
            if (!far) continue; used.Add(c.p);
            Vector3 stand = new Vector3(c.p.x, gen.Root.TransformPoint(Vector3.zero).y + 0.02f, c.p.z);
            yield return View(pw, mc, "l0_0" + (n + 1) + "_sala_amarilla", c.p, c.p + c.d * 8f + Vector3.down * 0.75f, 58f, stand);
            if (n == 1) { st2 = stand; p2 = c.p; d2 = c.d; }
            if (++n == 3) break;
        }
        // v7.3.0c: de cerca mirando hacia arriba a las pantallas (marco, difusor, tubos), desde la vista l0_02
        if (n >= 2)
        {
            Info("nivel 0: pantallas empotradas " + (gen.UsesTroffers ? "sí" : "no") + ", lotes de marcos " + gen.TrofferFrameBatches + ", paneles de relleno " + gen.FillerPanelCount);
            yield return View(pw, mc, "l0_04_techo_pantallas", p2, p2 + d2 * 3.2f + Vector3.up * 1.9f, 55f, st2);
            // un grupo apagado: difusor gris sin luz con los tubos a contraluz
            Transform fx = gen.Root.Find("Fixtures"); Renderer best = null; float bd = 1e9f;
            if (fx != null) foreach (Transform ch in fx)
                if (ch.name.StartsWith("PanelBatch_") && int.TryParse(ch.name.Substring(11), out int key) && key >= 500000)
                {
                    Renderer r = ch.GetComponent<Renderer>(); float dd = (r.bounds.center - p2).sqrMagnitude;
                    if (dd < bd) { bd = dd; best = r; }
                }
            if (best != null)
            {   // el panel apagado más cercano de ese lote, visto de cerca desde abajo y algo de lado, con la vista despejada
                Vector3[] vv = best.GetComponent<MeshFilter>().sharedMesh.vertices; Transform bt = best.transform;
                Vector3 tgt = bt.TransformPoint(vv[0]); float md = 1e9f;
                for (int q = 0; q < vv.Length; q += 4)
                {
                    Vector3 c4 = bt.TransformPoint((vv[q] + vv[q + 1] + vv[q + 2] + vv[q + 3]) * 0.25f);
                    float dd = (c4 - p2).sqrMagnitude; if (dd < md) { md = dd; tgt = c4; }
                }
                Vector3 cp = tgt + Vector3.down * 1.35f;
                foreach (Vector3 dir in new[] { Vector3.forward, Vector3.right, Vector3.back, Vector3.left })
                {
                    Vector3 c2 = tgt + Vector3.down * 1.35f + dir * 1.5f;
                    if (!Physics.Linecast(c2, tgt + Vector3.down * 0.05f, mask, QueryTriggerInteraction.Ignore) && !Physics.Linecast(tgt + Vector3.down * 1.35f, c2, mask, QueryTriggerInteraction.Ignore)) { cp = c2; break; }
                }
                yield return View(pw, mc, "l0_05_techo_apagado", cp, tgt, 55f, new Vector3(cp.x, gen.Root.TransformPoint(Vector3.zero).y + 0.02f, cp.z));
            }
        }
        if (vx != null) vx.intensityOverride = -1f;
    }

    IEnumerator Pool(GameManager gm, FirstPersonController pw, Camera mc)
    {
        Transform root = PoolRoomSetPiece.Root.transform; Transform bk = root.Find("PoolRoomBaked"); if (bk != null) root = bk;
        WaterVolume wv = WaterVolume.Active;
        float wl = wv != null ? root.InverseTransformPoint(new Vector3(0f, wv.surfaceY, 0f)).y : DECK - 0.08f;
        float lowY = wl + 0.08f;
        Vector3 corner = root.TransformPoint(B(-6.2f, -4.3f, 0.02f));
        yield return View(pw, mc, "exe_pool_1", root.TransformPoint(B(-6.3f, -4.4f, 1.65f)), root.TransformPoint(B(1.5f, 0.8f, -0.4f)), VFov(20f), corner);
        yield return View(pw, mc, "exe_pool_2", root.TransformPoint(new Vector3(-3.7f * SX, lowY, 0.9f * SZ)), root.TransformPoint(new Vector3(2f * SX, lowY - 0.2f, -0.4f * SZ)), VFov(24f), corner);
        yield return View(pw, mc, "exe_pool_3", root.TransformPoint(B(-1.8f, -0.9f, 2.6f)), root.TransformPoint(B(-0.6f, 0.1f, -1.4f)), VFov(28f), corner);
        // pasillos inundados: las 2 celdas con el pasillo recto más largo lejos de la sala
        BackroomsGenerator gen = gm.generator; float cs = gen.CellSize; RectInt pb = gen.PoolBlock; int shots = 0;
        Vector3[] dirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
        var best = new System.Collections.Generic.List<(float L, Vector3 p, Vector3 d)>();
        for (int cx = 0; cx < gen.GridWidth; cx++) for (int cz = 0; cz < gen.GridHeight; cz++)
        {
            if (cx >= pb.x - 1 && cx <= pb.xMax && cz >= pb.y - 1 && cz <= pb.yMax) continue;
            Vector3 wp = gen.Root.TransformPoint(new Vector3((cx + 0.5f) * cs, 1.6f, (cz + 0.5f) * cs));
            foreach (Vector3 d in dirs) { float L = Physics.Raycast(wp, d, out RaycastHit hit, 40f, ~((1 << 2) | (1 << WaterVolume.WaterLayer) | (1 << 8))) ? hit.distance : 40f; best.Add((L, wp, d)); }
        }
        best.Sort((a, b) => b.L.CompareTo(a.L));
        var used = new System.Collections.Generic.List<Vector3>();
        foreach (var c in best)
        {
            bool far = true; foreach (Vector3 u in used) if ((u - c.p).sqrMagnitude < 144f) far = false;
            if (!far) continue; used.Add(c.p);
            Vector3 stand = new Vector3(c.p.x, gen.Root.TransformPoint(Vector3.zero).y + 0.02f, c.p.z);
            yield return View(pw, mc, "exe_pasillo_" + (char)('a' + shots), c.p, c.p + c.d * 4f + Vector3.down * 1.9f, 60f, stand);
            if (++shots == 2) break;
        }
    }

    IEnumerator Field(GameManager gm, FirstPersonController pw, Camera mc)
    {
        FieldGrass.Synchronous = true;
        Transform root = gm.generator.Root; FieldWorld fw = FieldWorld.Active;
        System.Func<float, float, float, Vector3> P = (x, z, up) => root.TransformPoint(new Vector3(x, FieldHeight.H(x, z) + up, z));
        Vector3 a = root.InverseTransformPoint(pw.transform.position);
        Info("casas horneadas " + FieldWorld.HouseBaked + ", casas " + fw.houseCount);
        yield return View(pw, mc, "exe_campo_vista", P(a.x + 4.8f, a.z + 6.4f, 1.65f), P(a.x + 22.8f, a.z + 30.4f, 1.2f), 60f, P(a.x + 4.8f, a.z + 6.4f, 0.05f));
        yield return View(pw, mc, "exe_campo_colinas", P(a.x - 10f, a.z - 8f, 3.2f), P(a.x + 40f, a.z + 50f, 0f), 60f, P(a.x - 10f, a.z - 8f, 0.05f));
        Transform rocks = fw.transform.Find("Props/Rocks"); Transform best = null; float bd = 1e9f;
        if (rocks != null) foreach (Transform t in rocks) { if (!t.name.EndsWith("1")) continue; float dd = (t.localPosition - a).sqrMagnitude; if (dd < bd) { bd = dd; best = t; } }
        Info("rocas " + fw.RockCount);
        if (best != null) { Vector3 q = best.localPosition; yield return View(pw, mc, "exe_campo_rocas", P(q.x - 3.2f, q.z - 2.4f, 1.6f), P(q.x, q.z, 0.15f), 60f, P(q.x - 3.2f, q.z - 2.4f, 0.05f)); }
        foreach (FieldPlan.House h in fw.plan.houses)
        {
            Quaternion rot = Quaternion.Euler(0f, h.rot * 90f, 0f); Vector2 sz = FieldPlan.TemplateSize[h.template];
            Vector3 c = new Vector3(h.pos.x, 0f, h.pos.y), fwd = rot * Vector3.forward, right = rot * Vector3.right;
            Vector3 eye = c + fwd * (sz.y * 0.5f + 8.5f) + right * 3.5f;
            yield return View(pw, mc, "exe_casa_" + (h.enterable ? "abierta" : "cerrada"), P(eye.x, eye.z, 1.65f), P(c.x, c.z, 1.6f), 60f, P(eye.x, eye.z, 0.05f));
            break;
        }
        FieldGrass.Synchronous = false;
    }

    // v7.3.0: Talasofobia (nivel 7): casa inundada, mar con niebla, barca, bengala, faro, jaula y cascos, fondo con El Farolero, túnel y poza
    // v7.4.0 («-swimseq»): fondo del mar con casco. (1) desde la puerta de la jaula abierta en el fondo, hacia la boca de la gruta
    // (señuelo de algas); (2) de pie en el fondo, Espacio mantenido: secuencia de fotogramas subiendo; (3) la boca de la gruta de cerca.
    IEnumerator SwimSeq(GameManager gm, FirstPersonController pw, Camera mc)
    {
        OceanWorld w = OceanWorld.Active; OceanPlayer op = OceanPlayer.Local;
        if (w.fish != null) w.fish.enabled = false;
        if (w.angler != null) { w.angler.ForceForTests(OceanAngler.Region.center + new Vector3(25f, 0f, 30f), OceanAngler.AState.Patrol); w.angler.enabled = false; }
        gm.lostRestartSeconds = 999f;
        op.SetHelmet(true);
        Flashlight fl = pw.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(true);
        w.cage.ForceStateForTests(OceanCage.CageState.BottomOpen, w.CageBottom.y);
        Vector3 cb = w.CageBottom; Vector3 mouth = OceanWorld.TunnelPath[0];
        Vector3 inCage = new Vector3(cb.x - 0.3f, cb.y + 0.02f, cb.z);
        Vector3 eye = inCage + Vector3.up * 1.55f;
        hero = true;
        yield return View(pw, mc, "l7_50_puerta_jaula_hacia_gruta", eye, mouth + Vector3.up * 0.3f, 62f, inCage);
        Info("aviso en pantalla: " + op.Prompt);
        Vector3 mid = Vector3.Lerp(inCage, mouth, 0.55f); mid.y = w.SeabedY(mid.x, mid.z) + 1.6f;
        yield return View(pw, mc, "l7_51_boca_gruta_algas", mid, mouth + Vector3.up * 0.2f, 58f, inCage);
        // de pie en el fondo, fuera de la jaula, mirando la pared (algo hacia arriba); Espacio mantenido
        Vector3 sb = new Vector3(cb.x - 3.2f, 0f, cb.z); sb.y = WaterVolume.Active.GroundY(new Vector3(sb.x, cb.y + 1.5f, sb.z)) + 0.05f;
        pw.Teleport(sb, 270f); pw.SetPitch(55f);
        float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 1.5f) yield return null;
        float y0 = pw.transform.position.y;
        pw.debugInput = true; pw.debugMove = Vector2.zero; pw.debugJump = true;
        float tg = Time.time; int k = 0;
        while (k < 6)
        {
            if (Time.time - tg >= k * 0.6f)
            {
                pw.SetPitch(55f);
                Destroy(Grab(mc, "l7_52_subiendo_" + k));
                Info("subiendo " + k + ": t " + (Time.time - tg).ToString("F1") + " s, pies " + pw.transform.position.y.ToString("F2") + " (+" + (pw.transform.position.y - y0).ToString("F2") + " m), nadando " + pw.IsSwimming + ", libre " + pw.freeSwim);
                k++;
            }
            yield return null;
        }
        pw.debugJump = false; pw.debugInput = false;
        float rise = pw.transform.position.y - y0;
        Check(rise > 2.5f, "con el casco, Espacio desde el fondo sube nadando (" + rise.ToString("F1") + " m en 3 s)");
        hero = false;
        pw.SetPitch(0f);
    }

    IEnumerator Ocean(GameManager gm, FirstPersonController pw, Camera mc)
    {
        OceanWorld w = OceanWorld.Active; OceanPlayer op = OceanPlayer.Local;
        if (w.fish != null) w.fish.enabled = false;
        if (w.angler != null) w.angler.enabled = false;
        gm.lostRestartSeconds = 999f;
        const float F = 62f; Vector3 up = Vector3.up * 1.55f;
        Vector3 s1 = new Vector3(18.6f, -0.85f, 21.3f);
        Flashlight fl0 = pw.GetComponentInChildren<Flashlight>();
        if (fl0 != null) fl0.SetOn(true);   // dentro de la casa inundada se juega con la linterna
        yield return View(pw, mc, "l7_01_casa_inundada", s1 + up, new Vector3(15.6f, -0.1f, 26.6f), F, s1);
        // v7.3.0: sofá modelado (de frente y de cerca), con la linterna
        {
            GameObject so = null; foreach (OceanFloat of in Object.FindObjectsOfType<OceanFloat>()) if (of.name == "FloatingSofa") so = of.gameObject;
            if (so != null)
            {
                Transform st = so.transform;
                yield return View(pw, mc, "l7_40_sofa_frente", st.TransformPoint(new Vector3(0.9f, 1.05f, -2.4f)), st.TransformPoint(new Vector3(0f, 0.32f, 0f)), 55f, s1);
                yield return View(pw, mc, "l7_41_sofa_cerca", st.TransformPoint(new Vector3(0.55f, 0.85f, -1.25f)), st.TransformPoint(new Vector3(0.1f, 0.38f, 0.05f)), 55f, s1);
            }
            else Check(false, "sofá flotante presente");
        }
        if (fl0 != null) fl0.SetOn(false);
        Vector3 porch = new Vector3(15f, -0.3f, 30.6f);
        yield return View(pw, mc, "l7_02_mar_niebla", porch + up, new Vector3(13f, 1.2f, 70f), F, porch);
        Vector3 stop = w.BoatStops.Count > 1 ? w.BoatStops[1] : OceanWorld.BoatHome;
        w.boat.ApplyNet(new Vector2(stop.x + 1.5f, stop.z), 200f, 0f, new[] { -1, -1, -1, -1 });
        yield return View(pw, mc, "l7_03_barca_casa_pilotes", new Vector3(stop.x - 6f, 2.4f, stop.z - 9f), new Vector3(stop.x + 4f, 1.2f, stop.z + 6f), F, porch);
        // v7.3.0: barca nueva: exterior, costado en la línea de flotación, asiento del conductor (interior seco) y tercera persona
        {
            Transform hl = w.boat.hull;
            System.Func<Vector3, Vector3> H = v => hl.TransformPoint(v);
            yield return View(pw, mc, "l7_42_barca_exterior", H(new Vector3(3.4f, 1.5f, 1.9f)), H(new Vector3(0f, 0.05f, -0.2f)), 55f, porch);
            yield return View(pw, mc, "l7_43_barca_costado", H(new Vector3(2.3f, 0.4f, -0.6f)), H(new Vector3(0f, -0.02f, -0.4f)), 55f, porch);
            Vector3 eyeL = OceanBoat.SeatLocal[0] + new Vector3(0f, 0.95f, -0.08f);
            yield return View(pw, mc, "l7_44_barca_asiento_conductor", H(eyeL), H(new Vector3(0f, -0.28f, 1.1f)), 66f, porch);
            yield return View(pw, mc, "l7_45_barca_tercera_persona", H(new Vector3(0.9f, 2.4f, -3.9f)), H(new Vector3(0f, -0.15f, 0.2f)), 55f, porch);
            float wl = WaterVolume.Active != null ? WaterVolume.Active.SurfaceHeightAt(hl.position) : 0f;
            Info("barca: suelo interior local -0.34 -> mundo y " + H(new Vector3(0f, -0.34f, 0f)).y.ToString("F2") + ", agua " + wl.ToString("F2") + " (máscara de agua " + (hl.Find("WaterMask") != null) + ")");
        }
        // v7.3.0: muebles de una casa sobre pilotes (piso de arriba), con la linterna
        {
            Transform sh = w.root.Find("StiltHouse0"); Transform tb = null;
            if (sh != null) foreach (Transform c in sh.GetComponentsInChildren<Transform>(true)) if (c.name == "Table" && c.parent != null && c.parent.name == "Furniture") tb = c;
            Flashlight flm = pw.GetComponentInChildren<Flashlight>();
            if (tb != null)
            {
                Transform T = tb.parent.parent; float fy = tb.localPosition.y;
                if (flm != null) flm.SetOn(true);
                Vector3 standM = T.TransformPoint(new Vector3(-1.3f, fy + 0.02f, -0.9f));   // v7.3.0b: fuera del hueco de la escalera
                yield return View(pw, mc, "l7_46_muebles_casa_pilotes", T.TransformPoint(new Vector3(0.1f, fy + 1.65f, -1.25f)), T.TransformPoint(new Vector3(-0.3f, fy + 0.35f, 1.6f)), 62f, standM);
                {   // diagnóstico: qué hay entre la cámara y la cama (antes salían rectángulos oscuros)
                    Vector3 c0 = T.TransformPoint(new Vector3(-0.5f, fy + 1.5f, 0.0f));
                    foreach (Vector3 tq in new[] { new Vector3(1.95f, fy + 0.35f, 1.5f), new Vector3(0.6f, fy + 0.2f, 1.2f), new Vector3(1.0f, fy + 1.2f, 2.0f), new Vector3(2.6f, fy + 0.3f, 0.6f) })
                    {
                        Vector3 tw = T.TransformPoint(tq);
                        foreach (RaycastHit hh in Physics.RaycastAll(c0, (tw - c0).normalized, (tw - c0).magnitude, ~0, QueryTriggerInteraction.Collide))
                            Info("l7_47 rayo " + tq + ": " + hh.collider.name + " (" + (hh.collider.transform.parent != null ? hh.collider.transform.parent.name : "-") + ") a " + hh.distance.ToString("F2"));
                    }
                }
                yield return View(pw, mc, "l7_47_muebles_cama", T.TransformPoint(new Vector3(-0.5f, fy + 1.5f, 0.0f)), T.TransformPoint(new Vector3(1.95f, fy + 0.35f, 1.5f)), 58f, standM);
                yield return View(pw, mc, "l7_48_muebles_mesa_silla", T.TransformPoint(new Vector3(-0.6f, fy + 1.3f, -0.2f)), T.TransformPoint(new Vector3(-2.2f, fy + 0.5f, 1.3f)), 55f, standM);
                if (flm != null) flm.SetOn(false);
            }
            else Info("casa sobre pilotes sin muebles nuevos (¿-oldprops?)");
            if (tb == null && sh != null)
            {   // antes: misma cámara con los muebles antiguos (cajas)
                Transform T = sh; foreach (Transform c in sh.GetComponentsInChildren<Transform>(true)) if (c.name == "Furniture") T = c.parent;
                float fy = OceanWorld.DeckY + 2.6f;
                if (flm != null) flm.SetOn(true);
                Vector3 standM = T.TransformPoint(new Vector3(-1.3f, fy + 0.02f, -0.9f));   // v7.3.0b: fuera del hueco de la escalera
                yield return View(pw, mc, "l7_46_muebles_casa_pilotes", T.TransformPoint(new Vector3(0.1f, fy + 1.65f, -1.25f)), T.TransformPoint(new Vector3(-0.3f, fy + 0.35f, 1.6f)), 62f, standM);
                yield return View(pw, mc, "l7_47_muebles_cama", T.TransformPoint(new Vector3(-0.5f, fy + 1.5f, 0.0f)), T.TransformPoint(new Vector3(1.95f, fy + 0.35f, 1.5f)), 58f, standM);
                yield return View(pw, mc, "l7_48_muebles_mesa_silla", T.TransformPoint(new Vector3(-0.6f, fy + 1.3f, -0.2f)), T.TransformPoint(new Vector3(-2.2f, fy + 0.5f, 1.3f)), 55f, standM);
                if (flm != null) flm.SetOn(false);
            }
        }
        OceanFlare.Spawn(0, new Vector3(stop.x, 1.5f, stop.z + 4f), Vector3.up);
        yield return View(pw, mc, "l7_04_bengala", new Vector3(stop.x - 14f, 1.8f, stop.z - 22f), new Vector3(stop.x, 18f, stop.z + 4f), F, porch);
        w.lighthouse.SetOn(true);
        Vector3 pier = new Vector3(55.2f, OceanWorld.PierY + 0.02f, 247f);
        yield return View(pw, mc, "l7_05_faro_muelle", pier + up, new Vector3(0f, 14f, 250f), F, pier);
        Vector3 pier2 = new Vector3(46f, OceanWorld.PierY + 0.02f, 249.4f);
        yield return View(pw, mc, "l7_06_jaula_cascos", pier2 + up, new Vector3(56f, 2.2f, 251.5f), F, pier2);
        Vector3 rk = w.rack.transform.position;
        yield return View(pw, mc, "l7_07_cascos_cerca", rk + new Vector3(1.1f, 1.55f, -1.5f), rk + new Vector3(0f, 1.05f, 0f), F, pier2);
        // casco de cerca (macro) y compañero con casco (vista del avatar remoto)
        Transform hz = null; float hd = 1e9f;
        foreach (Transform c in w.rack.GetComponentsInChildren<Transform>(true)) if (c.name == "DivingHelmet" && c.gameObject.activeInHierarchy) { float dd = (c.position - (rk + new Vector3(1.1f, 1.55f, -1.5f))).sqrMagnitude; if (dd < hd) { hd = dd; hz = c; } }
        if (hz != null)
        {
            Vector3 hc = hz.position + Vector3.up * 0.22f;
            MeshRenderer hmr = hz.GetComponentInChildren<MeshRenderer>();
            if (hmr != null)
            {
                var lst = new System.Collections.Generic.List<UnityEngine.Rendering.ReflectionProbeBlendInfo>();
                hmr.GetClosestReflectionProbes(lst);
                string pr = ""; foreach (var bi in lst) pr += bi.probe.name + " w" + bi.weight.ToString("0.00") + " tex " + (bi.probe.texture != null ? bi.probe.texture.name : "null") + "; ";
                Material bm = hmr.sharedMaterial;
                Debug.Log("[ShotBuildCheck] DIAG casco: sondas [" + pr + "] usage " + hmr.reflectionProbeUsage + " rtProbes " + QualitySettings.realtimeReflectionProbes + " q " + QualitySettings.GetQualityLevel() + " defRefl " + RenderSettings.defaultReflectionMode + " int " + RenderSettings.reflectionIntensity + " shader " + bm.shader.name + " kw [" + string.Join(",", bm.shaderKeywords) + "] albedo " + (bm.mainTexture != null ? bm.mainTexture.name : "null") + " mask " + (bm.GetTexture("_MetallicGlossMap") != null) + " amb " + RenderSettings.ambientLight);
            }
            yield return View(pw, mc, "l7_07b_casco_macro", hc + new Vector3(0.28f, 0.12f, -0.62f), hc, 45f, pier2);
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-helmetdiag") >= 0 && hmr != null)
            {
                Material bm = hmr.sharedMaterial;
                Texture nt = bm.GetTexture("_BumpMap"), mt = bm.GetTexture("_MetallicGlossMap"), at = bm.mainTexture; Color c0 = bm.color;
                bm.DisableKeyword("_NORMALMAP");
                yield return View(pw, mc, "l7_07x_diag_sin_normal", hc + new Vector3(0.28f, 0.12f, -0.62f), hc, 45f, pier2);
                bm.EnableKeyword("_NORMALMAP"); bm.DisableKeyword("_METALLICGLOSSMAP"); bm.SetTexture("_OcclusionMap", null); bm.SetFloat("_Metallic", 0f); bm.SetFloat("_Glossiness", 0.5f);
                yield return View(pw, mc, "l7_07y_diag_sin_mascara", hc + new Vector3(0.28f, 0.12f, -0.62f), hc, 45f, pier2);
                bm.DisableKeyword("_NORMALMAP"); bm.mainTexture = null; bm.color = new Color(0.78f, 0.58f, 0.3f);
                yield return View(pw, mc, "l7_07z_diag_plano", hc + new Vector3(0.28f, 0.12f, -0.62f), hc, 45f, pier2);
                bm.mainTexture = at; bm.color = c0; bm.EnableKeyword("_NORMALMAP"); bm.EnableKeyword("_METALLICGLOSSMAP"); bm.SetTexture("_OcclusionMap", mt);
            }
        }
        RemotePlayer mate = RemotePlayer.CreateMimic(w.root, 98, "Compañero", StepKind.Metal); mate.ShowTag = false;
        Vector3 mp = new Vector3(40f, OceanWorld.PierY + 0.02f, 250f);
        mate.transform.position = mp; mate.transform.rotation = Quaternion.Euler(0f, 250f, 0f);
        for (int k = 0; k < 6; k++) { mate.Push(mp, 250f, 0f, 0f, 0); yield return null; }
        w.AttachHelmetTo(mate.transform);
        yield return null;
        Vector3 hp = mate.transform.position + Vector3.up * 1.62f;
        Vector3 camP = hp + Quaternion.Euler(0f, 250f, 0f) * new Vector3(0.45f, 0.05f, 1.35f);
        yield return View(pw, mc, "l7_07c_companero_casco", camP, hp - Vector3.up * 0.05f, 50f, pier2);
        Destroy(mate.gameObject);
        // v7.3.0: traje (compañero sin casco: cuerpo entero y de cerca) y manos en primera persona
        {
            RemotePlayer m2 = RemotePlayer.CreateMimic(w.root, 97, "Compañero", StepKind.Metal); m2.ShowTag = false;
            m2.transform.position = mp; m2.transform.rotation = Quaternion.Euler(0f, 250f, 0f);
            for (int k = 0; k < 6; k++) { m2.Push(mp, 250f, 0f, 0f, 0); yield return null; }
            Vector3 f2 = Quaternion.Euler(0f, 250f, 0f) * Vector3.forward, r2 = Quaternion.Euler(0f, 250f, 0f) * Vector3.right;
            yield return View(pw, mc, "l7_13_traje_cuerpo", mp + f2 * 3.4f + r2 * 0.3f + Vector3.up * 1.05f, mp + Vector3.up * 0.98f, 42f, pier2);
            yield return View(pw, mc, "l7_14_traje_cerca", mp + f2 * 2.3f + r2 * 0.6f + Vector3.up * 1.75f, mp + Vector3.up * 1.45f, 44f, pier2);
            Destroy(m2.gameObject);
            Vector3 st = new Vector3(44f, OceanWorld.PierY + 0.02f, 250f);
            pw.Teleport(st, 70f); pw.SetPitch(32f);
            PlayerBody pbd = pw.GetComponentInChildren<PlayerBody>(true); if (pbd == null) pbd = Object.FindObjectOfType<PlayerBody>();
            var fShow = typeof(PlayerBody).GetField("showWanted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            object show0 = (pbd != null && fShow != null) ? fShow.GetValue(pbd) : null;
            if (show0 != null) fShow.SetValue(pbd, true);   // solo para esta captura (no toca las preferencias del usuario)
            for (int k = 0; k < 20; k++) yield return null;
            Vector3 eye = mc.transform.position, look = eye + mc.transform.forward;
            Debug.Log("[ShotBuildCheck] manos: cuerpo " + (pbd != null) + " visible " + (pbd != null && pbd.Visible) + " antes " + show0);
            int cm0 = mc.cullingMask; mc.cullingMask |= 1 << 8;   // capa del cuerpo propio (oculta en el resto de capturas)
            yield return View(pw, mc, "l7_15_manos_primera_persona", eye, look, 62f, st);
            mc.cullingMask = cm0;
            // v7.3.0: reanimar con las manos a escala: compañero agachado sobre un derribado y la mano propia alargada en primera persona
            {
                Vector3 dp = new Vector3(42.6f, OceanWorld.PierY + 0.02f, 250.3f);
                RemotePlayer dn = RemotePlayer.CreateMimic(w.root, 96, "Derribado", StepKind.Metal); dn.ShowTag = false;
                RemotePlayer rv = RemotePlayer.CreateMimic(w.root, 95, "Reanima", StepKind.Metal); rv.ShowTag = false;
                dn.SetDowned(true);
                Vector3 rp = dp + new Vector3(-0.9f, 0f, -0.35f);
                float ryaw = Mathf.Atan2(dp.x - rp.x, dp.z - rp.z) * Mathf.Rad2Deg;
                for (int k = 0; k < 90; k++) { dn.Push(dp, 160f, 0f, 0f, RemotePlayer.FlagDowned); rv.Push(rp, ryaw, 35f, 0f, RemotePlayer.FlagRevive | RemotePlayer.FlagFlash); yield return null; }
                Vector3 side = Quaternion.Euler(0f, ryaw + 90f, 0f) * Vector3.forward;
                Vector3 mid = (dp + rp) * 0.5f;
                yield return View(pw, mc, "l7_21_reanimar_companero", mid + side * 3.4f + Vector3.up * 1.0f + (rp - dp).normalized * 0.6f, mid + Vector3.up * 0.5f, 50f, pier2);
                // primera persona: el jugador alarga la mano hacia el derribado
                bool rvRev = rv.Reviving; rv.gameObject.SetActive(false);   // el jugador local ocupa su sitio
                Vector3 st2 = dp + new Vector3(-1.35f, 0f, -0.1f);
                pw.Teleport(st2, 90f); pw.SetPitch(40f);
                if (show0 != null) fShow.SetValue(pbd, true);
                for (int k = 0; k < 40; k++) { PlayerBody.ReachK = 1f; dn.Push(dp, 160f, 0f, 0f, RemotePlayer.FlagDowned); yield return null; }
                Vector3 eye2 = mc.transform.position, look2 = eye2 + mc.transform.forward;
                mc.cullingMask |= 1 << 8;
                PlayerBody.ReachK = 1f;
                yield return View(pw, mc, "l7_22_reanimar_primera_persona", eye2, look2, 62f, st2);
                Debug.Log("[ShotBuildCheck] reanimar: ReachK " + PlayerBody.ReachK.ToString("F2") + " reviving " + rvRev + " downed " + dn.Downed);
                mc.cullingMask = cm0; PlayerBody.ReachK = 0f;
                if (show0 != null) fShow.SetValue(pbd, show0);
                Destroy(dn.gameObject); Destroy(rv.gameObject);
            }
            if (show0 != null) fShow.SetValue(pbd, show0);
            pw.SetPitch(0f);
        }
        // v7.3.0: muelle de cerca (tablas, barandilla, cuerda) y pilotes en la línea de flotación
        yield return View(pw, mc, "l7_16_muelle_cerca", new Vector3(27.2f, OceanWorld.PierY + 1.25f, 248.1f), new Vector3(33f, OceanWorld.PierY + 0.2f, 250.4f), 55f, pier2);
        yield return View(pw, mc, "l7_17_muelle_pilotes", new Vector3(30.6f, 0.85f, 246.6f), new Vector3(29f, 0.05f, 248.9f), 55f, pier2);
        op.SetHelmet(true);
        Vector3 me = new Vector3(60f, -47f, 252f), ap = new Vector3(65f, -45.8f, 256.8f);
        w.angler.ForceForTests(ap, OceanAngler.AState.Patrol); w.angler.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0f, 0.45f));
        yield return View(pw, mc, "l7_08_fondo_farolero", me + up, ap, F, me);
        Flashlight fl = pw.GetComponentInChildren<Flashlight>();
        if (fl != null) fl.SetOn(false);
        Vector3 ap2 = new Vector3(70f, -44.5f, 263f);
        w.angler.ForceForTests(ap2, OceanAngler.AState.Patrol); w.angler.transform.rotation = Quaternion.LookRotation(new Vector3(-0.8f, 0f, -0.6f));
        yield return View(pw, mc, "l7_09_farolero_a_oscuras", me + up, ap2 + Vector3.up * 1.5f, F, me);
        if (fl != null) fl.SetOn(true);
        // El Farolero bien iluminado (cuerpo entero y señuelo): luces de estudio temporales y menos niebla, solo para esta captura
        {
            float K = OceanAngler.Size; Vector3 ap3 = new Vector3(66f, -44.3f, 257f), fw = new Vector3(-1f, 0f, 0.45f).normalized, sideV = Vector3.Cross(Vector3.up, fw).normalized;
            w.angler.ForceForTests(ap3, OceanAngler.AState.Patrol); w.angler.transform.rotation = Quaternion.LookRotation(fw);
            Vector3 camP3 = ap3 + (sideV * 6.3f + fw * 1.8f + Vector3.up * 0.6f) * K;
            float fd = RenderSettings.fogDensity;
            GameObject studio = new GameObject("ShotStudioLights");
            Light key = new GameObject("Key").AddComponent<Light>(); key.transform.SetParent(studio.transform); key.type = LightType.Point; key.range = 32f; key.intensity = 4.5f; key.color = new Color(0.75f, 0.88f, 0.95f); key.renderMode = LightRenderMode.ForcePixel; key.transform.position = camP3 + Vector3.up * 2.5f + fw * 1.5f;
            Light rim = new GameObject("Rim").AddComponent<Light>(); rim.transform.SetParent(studio.transform); rim.type = LightType.Point; rim.range = 16f; rim.intensity = 1.6f; rim.color = new Color(0.55f, 0.8f, 0.85f); rim.renderMode = LightRenderMode.ForcePixel; rim.transform.position = ap3 - sideV * 4f + Vector3.up * 3f;
            Light fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(studio.transform); fill.type = LightType.Point; fill.range = 16f; fill.intensity = 2.0f; fill.color = new Color(0.9f, 0.85f, 0.75f); fill.renderMode = LightRenderMode.ForcePixel; fill.transform.position = ap3 + sideV * 3f - Vector3.up * 1.5f - fw * 3f;
            Light key2 = new GameObject("Key2").AddComponent<Light>(); key2.transform.SetParent(studio.transform); key2.type = LightType.Point; key2.range = 20f; key2.intensity = 2.6f; key2.color = new Color(0.8f, 0.85f, 0.85f); key2.renderMode = LightRenderMode.ForcePixel; key2.transform.position = camP3 - Vector3.up * 1.8f - fw * 1.0f;
            Color amb0 = RenderSettings.ambientLight; float ai0 = RenderSettings.ambientIntensity;
            RenderSettings.ambientLight = new Color(0.16f, 0.2f, 0.22f); RenderSettings.ambientIntensity = Mathf.Max(ai0, 1f);
            RenderSettings.fogDensity = fd * 0.3f;
            yield return View(pw, mc, "l7_12_farolero_iluminado", camP3, ap3 + fw * 0.6f * K, 55f, camP3);
            RenderSettings.fogDensity = fd; RenderSettings.ambientLight = amb0; RenderSettings.ambientIntensity = ai0;
            Destroy(studio);
        }
        // v7.3.0: El Farolero nuevo: plano heroico iluminado, boca abierta de frente y vista de juego con la lámpara del casco
        {
            OceanAngler an = w.angler;
            float K4 = OceanAngler.Size; Vector3 ap4 = new Vector3(66f, -43.4f, 257f), fw4 = new Vector3(-1f, 0f, 0.45f).normalized, sd4 = Vector3.Cross(Vector3.up, fw4).normalized;
            an.FreezeMotion = true; an.PoseJaw = 40f;
            an.ForceForTests(ap4, OceanAngler.AState.Patrol); an.transform.rotation = Quaternion.LookRotation(fw4);
            float fd = RenderSettings.fogDensity; Color amb0 = RenderSettings.ambientLight; float ai0 = RenderSettings.ambientIntensity;
            GameObject studio = new GameObject("ShotStudioLights2");
            System.Func<string, Vector3, float, float, Color, Light> PL = (nm, pos, range, inten, c) =>
            {
                Light l = new GameObject(nm).AddComponent<Light>(); l.transform.SetParent(studio.transform); l.type = LightType.Point; l.range = range; l.intensity = inten; l.color = c;
                l.renderMode = LightRenderMode.ForcePixel; l.transform.position = pos; return l;
            };
            Vector3 camH = ap4 + (sd4 * 8.4f + fw4 * 0.9f + Vector3.up * 1.2f) * K4;
            Light k1 = PL("Key", camH + Vector3.up * 3f + fw4 * 2.5f, 40f, 5.5f, new Color(0.75f, 0.88f, 0.95f));
            PL("Rim", ap4 + (-sd4 * 5f + Vector3.up * 3.5f - fw4 * 2f) * K4, 20f * K4, 4.5f, new Color(0.55f, 0.8f, 0.85f));
            PL("Fill", ap4 + (sd4 * 4f - Vector3.up * 2f - fw4 * 3f) * K4, 20f * K4, 3.2f, new Color(0.9f, 0.85f, 0.75f));
            PL("Key2", camH - Vector3.up * 2f + fw4 * 2f, 26f, 4.0f, new Color(0.8f, 0.85f, 0.85f));
            RenderSettings.ambientLight = new Color(0.13f, 0.16f, 0.18f); RenderSettings.ambientIntensity = Mathf.Max(ai0, 1f);
            RenderSettings.fogDensity = fd * 0.25f;
            yield return View(pw, mc, "l7_18_farolero2_heroe", camH, ap4 + (fw4 * 0.2f - Vector3.up * 0.1f) * K4, 58f, camH);
            // boca abierta de frente, algo por debajo: agujas a contraluz de la esca
            an.PoseJaw = 52f;
            Vector3 camM = ap4 + (fw4 * 7.0f - Vector3.up * 0.8f + sd4 * 1.1f) * K4;
            k1.transform.position = camM + Vector3.up * 2.2f - fw4 * 0.5f + sd4 * 1.5f; k1.intensity = 5.5f; k1.range = 24f;
            yield return View(pw, mc, "l7_19_farolero2_boca", camM, ap4 + (fw4 * 2.2f - Vector3.up * 0.6f) * K4, 55f, camM);
            RenderSettings.fogDensity = fd; RenderSettings.ambientLight = amb0; RenderSettings.ambientIntensity = ai0;
            Destroy(studio);
            // en juego: sin luces de estudio, niebla normal, la lámpara del casco del jugador encendida
            if (fl != null) fl.SetOn(true);
            Vector3 me3 = new Vector3(60f, -47f, 252f), dirP = new Vector3(0.55f, 0f, 0.84f).normalized;
            Vector3 ap5 = me3 + dirP * 7.2f * OceanAngler.Size + Vector3.up * 3.4f;
            an.PoseJaw = 46f; an.ForceForTests(ap5, OceanAngler.AState.Hunt); an.transform.rotation = Quaternion.LookRotation(-dirP);
            yield return View(pw, mc, "l7_20_farolero2_lampara", me3 + up, ap5 - dirP * 2.0f * OceanAngler.Size - Vector3.up * 0.6f, F, me3);
            an.PoseJaw = -1f; an.FreezeMotion = false; an.ForceForTests(ap4, OceanAngler.AState.Patrol);
        }
        w.angler.ForceForTests(new Vector3(95f, -40f, 280f), OceanAngler.AState.Patrol);
        // v7.3.0: esculturas del fondo con la lámpara del casco
        {
            if (fl != null) fl.SetOn(true);
            System.Func<Vector3, float, Vector3> SB = (q, dy) => new Vector3(q.x, w.SeabedY(q.x, q.z) + dy, q.z);
            // con un buzo (compañero con casco) al pie de cada una para que se lea la escala
            Vector3 cageP = new Vector3(OceanWorld.CageTop.x, 0f, OceanWorld.CageTop.z);
            RemotePlayer dv = RemotePlayer.CreateMimic(w.root, 93, "Buzo", StepKind.Metal); dv.ShowTag = false;
            w.AttachHelmetTo(dv.transform);
            string[] sn = { "l7_50_escultura_cabeza", "l7_51_escultura_mano", "l7_52_escultura_rostro" };
            // v7.3.0b: más cerca (a 15 m la niebla del fondo se las tragaba): ~8-9 m, buzo a 3 m delante para la escala
            float[] back = { 9.0f, 9.0f, 7.5f }, lift = { 2.6f, 2.8f, 2.2f }, aim = { 3.8f, 4.2f, 1.8f }, sideK = { 2.4f, -2.2f, 2.0f };
            for (int i = 0; i < 3; i++)
            {
                Vector3 sp = OceanWorld.SculptSpots[i];
                Vector3 toC = cageP - new Vector3(sp.x, 0f, sp.z); toC.y = 0f; toC.Normalize();
                Vector3 sd = Vector3.Cross(Vector3.up, toC);
                Vector3 dpos = SB(sp + toC * 3.4f + sd * 1.6f, 0.02f);
                float dyaw = Mathf.Atan2(-toC.x, -toC.z) * Mathf.Rad2Deg;
                for (int k = 0; k < 8; k++) { dv.Push(dpos, dyaw, 0f, 0.6f, 0); yield return null; }
                Vector3 cam = SB(sp + toC * back[i] + sd * sideK[i], lift[i]);
                yield return View(pw, mc, sn[i], cam, SB(sp, aim[i]), 62f, cam - up);
            }
            // desde el fondo de la jaula: el compañero dentro en primer plano, la cabeza colosal al fondo
            {
                Vector3 hd0 = OceanWorld.SculptSpots[0];
                Vector3 dir = new Vector3(hd0.x - cageP.x, 0f, hd0.z - cageP.z).normalized;
                Vector3 cb = w.CageBottom;
                for (int k = 0; k < 8; k++) { dv.Push(cb + dir * 1.6f + Vector3.Cross(Vector3.up, dir) * 0.8f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f, 0.6f, 0); yield return null; }
                Vector3 c4 = cb - dir * 1.2f + Vector3.up * 1.6f;
                yield return View(pw, mc, "l7_53_esculturas_desde_jaula", c4, SB(hd0, 4.0f), 64f, c4 - up);
            }
            Destroy(dv.gameObject);
        }
        // v7.3.0: la jaula bajando con los dos jugadores dentro (pueden andar y mirar): vista desde dentro y desde fuera
        {
            OceanCage cg = w.cage;
            cg.ForceStateForTests(OceanCage.CageState.Descending, -24f);
            yield return null; yield return null;
            cg.enabled = false;   // congelada a media bajada solo para la captura
            Vector3 cp = cg.transform.position;
            RemotePlayer mt = RemotePlayer.CreateMimic(w.root, 94, "Compañero", StepKind.Metal); mt.ShowTag = false;
            Vector3 mp2 = cp + new Vector3(0.45f, 0.02f, 0.5f);
            for (int k = 0; k < 6; k++) { mt.Push(mp2, 230f, 0f, 0.6f, 0); yield return null; }
            w.AttachHelmetTo(mt.transform);
            if (fl != null) fl.SetOn(true);
            yield return View(pw, mc, "l7_54_jaula_dentro_bajando", cp + new Vector3(-0.55f, 1.55f, -0.6f), cp + new Vector3(0.9f, 0.9f, 1.4f), 66f, cp + new Vector3(-0.55f, 0.02f, -0.6f));
            yield return View(pw, mc, "l7_55_jaula_fuera_bajando", cp + new Vector3(5.5f, 2.2f, -4.5f), cp + new Vector3(0f, 1.2f, 0f), 58f, cp + new Vector3(-0.55f, 0.02f, -0.6f));
            Destroy(mt.gameObject);
            cg.enabled = true;
        }
        Vector3 ts = OceanWorld.TunnelPath[2] + new Vector3(2f, -0.9f, 0f);
        yield return View(pw, mc, "l7_10_tunel_gruta", ts + up, OceanWorld.TunnelPath[4] + Vector3.up * 1.5f, F, ts);
        Vector3 ps = OceanWorld.PocketCenter + new Vector3(-2.3f, -1.42f, 0.6f);
        yield return View(pw, mc, "l7_11_poza_salida", ps + up, OceanWorld.PocketCenter + new Vector3(3f, 1.2f, -1f), F, ps);
        Check(gm.State == GameManager.GameState.Playing || gm.State == GameManager.GameState.Won, "nivel 7 sigue en juego durante las capturas (" + gm.State + ")");
    }

    // v7.4.0: cabina del ascensor (interior, rellano abierto/cerrado) y Nivel 1 (Aparcamiento) sin VHS, desde los mismos ángulos
    // que las capturas del editor (V740Shots). Solo usa API que ya existía en v7.3.0 para poder capturar «antes» con el mismo código.
    // Con «-perf»: tiempos de fotograma en la cabina, el rellano y el aparcamiento (VHS por defecto 0,7, 1920x1080, sin vsync).
    // v7.4.0: nivel 6, un monstruo de pie en una piscina de bolas (como la captura del usuario: antes se abría un círculo hasta una
    // «foto» plana de bolas). Cámaras fijas desde el borde; el jugador queda fuera de la piscina.
    IEnumerator Ballpit(GameManager gm, FirstPersonController pw, Camera mc)
    {
        BallPitLurker.DisabledForTests = true;
        BallPitField f0 = BallPitField.All[0]; BallpitSetPiece.PitJ pit = f0.pits[0]; Transform tr = f0.transform;
        Rect inn = BallpitSetPiece.PitInner(pit, f0.rim);
        MonsterAI m0 = null; foreach (MonsterAI m in gm.monsters) if (m != null && m0 == null) m0 = m;
        Vector3 mp = tr.TransformPoint(new Vector3(inn.xMin + 1.4f, pit.floor + 0.02f, inn.center.y));
        Vector3 stand = tr.TransformPoint(new Vector3(inn.xMin - 1.2f, 0.05f, inn.center.y - 3.0f));
        if (m0 != null) { m0.WarpTo(mp, -90f + tr.eulerAngles.y); m0.transform.position = mp; }
        Check(m0 != null, "monstruo disponible para ponerlo en la piscina");
        hero = true;
        Vector3 tgt = mp + Vector3.up * 0.55f;
        yield return View(pw, mc, "l6_01_monstruo_piscina", tr.TransformPoint(new Vector3(inn.xMin - 0.5f, pit.ball + 1.85f, inn.center.y - 0.9f)), tgt, 60f, stand);
        if (m0 != null) m0.transform.position = mp;
        yield return View(pw, mc, "l6_02_monstruo_desde_arriba", tr.TransformPoint(new Vector3(inn.xMin + 0.3f, pit.ball + 2.3f, inn.center.y - 1.3f)), mp + Vector3.up * 0.25f, 55f, stand);
        if (m0 != null) m0.transform.position = mp;
        yield return View(pw, mc, "l6_03_pies_del_monstruo", tr.TransformPoint(new Vector3(inn.xMin + 0.55f, pit.ball + 1.3f, inn.center.y - 0.6f)), mp + Vector3.up * 0.05f, 50f, stand);
        Info("monstruo en " + (m0 != null ? m0.transform.position.ToString() : "-") + ", empujadores " + BallPitField.PusherCount);
        hero = false;
    }

    IEnumerator Level1(GameManager gm, FirstPersonController pw, Camera mc)
    {
        VhsEffect vx = mc.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        ElevatorCabin cab = ElevatorCabin.Current;
        if (cab == null) { Check(false, "cabina en el nivel 1"); yield break; }
        Transform ct = cab.transform; float F = mc.fieldOfView; hero = true;
        System.Func<float, float, float, Vector3> L = (x, y, z) => ct.TransformPoint(new Vector3(x, y, z));
        Vector3 inStand = cab.InteriorCenter;
        cab.SeekPreview(2.2f, false); yield return null; cab.SeekPreview(2.2f, false);
        yield return View(pw, mc, "e01_interior_puertas", L(0.75f, 1.55f, -1.0f), L(-0.35f, 1.05f, 1.35f), F, inStand);
        yield return View(pw, mc, "e02_interior_fondo_botonera", L(-0.55f, 1.6f, 0.9f), L(0.55f, 1.05f, -1.15f), F, inStand);
        yield return View(pw, mc, "e03_indicador_planta", L(-0.1f, 1.65f, 0.2f), L(0f, 2.14f, 1.3f), F, inStand);
        yield return View(pw, mc, "e05_pasamanos_pared", L(0.4f, 1.3f, 0.6f), L(1.17f, 1.0f, -0.6f), F, inStand);
        cab.SeekPreview(8.2f, false); yield return null;
        Vector3 op = cab.ExitPoint + cab.Forward * 3.0f + Vector3.up * 1.5f + ct.right * 0.8f, opT = cab.InteriorCenter + Vector3.up * 1.1f;
        Vector3 opStand = new Vector3(op.x, cab.ExitPoint.y, op.z);
        yield return View(pw, mc, "e08_exterior_abierto", op, opT, F, opStand);
        Vector3 b = cab.ExitPoint + cab.Forward * 2.5f; Vector3 f = cab.Forward, r = ct.right;
        yield return View(pw, mc, "e10_rellano_lateral", b + Vector3.up * 1.45f + r * 1.6f - f * 0.6f, L(0.2f, 1.25f, 1.45f), F, b + r * 1.6f - f * 0.6f);
        yield return View(pw, mc, "p01_pasillo_frente", b + Vector3.up * 1.6f, b + f * 14f + Vector3.up * 1.0f, F, b);
        yield return View(pw, mc, "p02_lado_derecho", b + Vector3.up * 1.6f, b + r * 14f + Vector3.up * 0.6f, F, b);
        yield return View(pw, mc, "p03_lado_izquierdo", b + Vector3.up * 1.6f, b - r * 14f + Vector3.up * 0.6f, F, b);
        yield return View(pw, mc, "p04_suelo_lineas", b + Vector3.up * 1.7f + f * 2f, b + f * 5f + Vector3.down * 0.1f, F, b + f * 2f);
        cab.SeekClosed(); yield return null;
        yield return View(pw, mc, "e09_exterior_cerrado", op, opT, F, opStand);
        hero = false;
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-perf") >= 0)
        {
            if (vx != null) { vx.enabled = true; vx.intensityOverride = 0.7f; }
            cab.SeekPreview(2.2f, false); yield return null;
            yield return Perf(pw, mc, "cabina", L(0.75f, 1.55f, -1.0f), L(-0.35f, 1.05f, 1.35f), F, inStand);
            cab.SeekPreview(8.2f, false); yield return null;
            yield return Perf(pw, mc, "rellano", op, opT, F, opStand);
            yield return Perf(pw, mc, "aparcamiento", b + Vector3.up * 1.6f, b + f * 14f + Vector3.up * 1.0f, F, b);
            yield return Perf(pw, mc, "aparcamiento_lado", b + Vector3.up * 1.6f, b - r * 14f + Vector3.up * 0.6f, F, b);
        }
        Check(gm.State == GameManager.GameState.Playing, "nivel 1 sigue en juego durante las capturas (" + gm.State + ")");
    }

    // 900 fotogramas: cada uno = bucle del juego + render de la cámara a 1920x1080 + espera a la GPU (lectura de 1 píxel)
    IEnumerator Perf(FirstPersonController pw, Camera mc, string n, Vector3 p, Vector3 t, float fov, Vector3 stand)
    {
        pw.Teleport(stand, Mathf.Atan2(t.x - p.x, t.z - p.z) * Mathf.Rad2Deg);
        int vs0 = QualitySettings.vSyncCount, tf0 = Application.targetFrameRate; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        RenderTexture rt = new RenderTexture(1920, 1080, 24); rt.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing); rt.Create();
        Texture2D one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        bool en = mc.enabled; mc.enabled = false;
        var ft = new System.Collections.Generic.List<double>(); var sw = new System.Diagnostics.Stopwatch();
        for (int k = 0; k < 960; k++)
        {
            mc.transform.position = p; mc.transform.rotation = Quaternion.LookRotation(t - p); mc.fieldOfView = fov;
            mc.targetTexture = rt; mc.Render(); mc.targetTexture = null;
            RenderTexture.active = rt; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); RenderTexture.active = null;
            if (k >= 60) ft.Add(sw.Elapsed.TotalMilliseconds);
            yield return null;
            sw.Restart();
        }
        mc.enabled = en; QualitySettings.vSyncCount = vs0; Application.targetFrameRate = tf0;
        Destroy(one); rt.Release(); Destroy(rt);
        double sum = 0; foreach (double x in ft) sum += x; double mean = sum / ft.Count;
        ft.Sort(); double p99 = ft[(int)(ft.Count * 0.99)];
        int worst = Mathf.Max(1, ft.Count / 100); double ws = 0; for (int i = ft.Count - worst; i < ft.Count; i++) ws += ft[i];
        Info("perf " + n + " calidad " + GameSettings.Quality + ": media " + mean.ToString("F2") + " ms = " + (1000.0 / mean).ToString("F0") + " FPS, 1% bajo " + (1000.0 / (ws / worst)).ToString("F0")
            + " FPS (media del 1% peor " + (ws / worst).ToString("F2") + " ms; p99 " + p99.ToString("F2") + " ms), mediana " + ft[ft.Count / 2].ToString("F2") + " ms, " + ft.Count + " fotogramas, AA " + rt.antiAliasing + ", " + SystemInfo.graphicsDeviceName);
    }

    bool hero;
    static float Mean(Texture2D t) { Color32[] px = t.GetPixels32(); double s = 0; for (int i = 0; i < px.Length; i += 17) s += (px[i].r + px[i].g + px[i].b) / 765.0; return (float)(s / (px.Length / 17)); }

    Texture2D Grab(Camera c, string name)
    {
        const int W = 1920, H = 1080;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.Default, RenderTextureReadWrite.Default, hero ? 8 : 1); RenderTexture prev = c.targetTexture;
        c.targetTexture = rt; c.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
        RenderTexture.active = null; c.targetTexture = prev; RenderTexture.ReleaseTemporary(rt);
        int w = t.width, h = t.height; Color32[] px = t.GetPixels32();
        byte[] head = Encoding.ASCII.GetBytes("P6\n" + w + " " + h + "\n255\n");
        byte[] b = new byte[head.Length + w * h * 3]; System.Buffer.BlockCopy(head, 0, b, 0, head.Length);
        int o = head.Length;
        for (int y = h - 1; y >= 0; y--) for (int x = 0; x < w; x++) { Color32 q = px[y * w + x]; b[o++] = q.r; b[o++] = q.g; b[o++] = q.b; }
        File.WriteAllBytes(Path.Combine(dir, name + ".ppm"), b);
        return t;
    }

    void Finish()
    {
        RestoreQuality();
        rep.AppendLine("RESULT passed=" + pass + " failed=" + fail);
        File.WriteAllText(Path.Combine(dir, "shotcheck.txt"), rep.ToString());
        Application.Quit(fail == 0 ? 0 : 3);
    }
}
