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
        else if (OceanWorld.Active != null) yield return Ocean(gm, pw, mc);
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
        if (fl0 != null) fl0.SetOn(false);
        Vector3 porch = new Vector3(15f, -0.3f, 30.6f);
        yield return View(pw, mc, "l7_02_mar_niebla", porch + up, new Vector3(13f, 1.2f, 70f), F, porch);
        Vector3 stop = w.BoatStops.Count > 1 ? w.BoatStops[1] : OceanWorld.BoatHome;
        w.boat.ApplyNet(new Vector2(stop.x + 1.5f, stop.z), 200f, 0f, new[] { -1, -1, -1, -1 });
        yield return View(pw, mc, "l7_03_barca_casa_pilotes", new Vector3(stop.x - 6f, 2.4f, stop.z - 9f), new Vector3(stop.x + 4f, 1.2f, stop.z + 6f), F, porch);
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
            Vector3 ap3 = new Vector3(66f, -45.5f, 257f), fw = new Vector3(-1f, 0f, 0.45f).normalized, sideV = Vector3.Cross(Vector3.up, fw).normalized;
            w.angler.ForceForTests(ap3, OceanAngler.AState.Patrol); w.angler.transform.rotation = Quaternion.LookRotation(fw);
            Vector3 camP3 = ap3 + sideV * 6.3f + fw * 1.8f + Vector3.up * 0.6f;
            float fd = RenderSettings.fogDensity;
            GameObject studio = new GameObject("ShotStudioLights");
            Light key = new GameObject("Key").AddComponent<Light>(); key.transform.SetParent(studio.transform); key.type = LightType.Point; key.range = 32f; key.intensity = 4.5f; key.color = new Color(0.75f, 0.88f, 0.95f); key.renderMode = LightRenderMode.ForcePixel; key.transform.position = camP3 + Vector3.up * 2.5f + fw * 1.5f;
            Light rim = new GameObject("Rim").AddComponent<Light>(); rim.transform.SetParent(studio.transform); rim.type = LightType.Point; rim.range = 16f; rim.intensity = 1.6f; rim.color = new Color(0.55f, 0.8f, 0.85f); rim.renderMode = LightRenderMode.ForcePixel; rim.transform.position = ap3 - sideV * 4f + Vector3.up * 3f;
            Light fill = new GameObject("Fill").AddComponent<Light>(); fill.transform.SetParent(studio.transform); fill.type = LightType.Point; fill.range = 16f; fill.intensity = 2.0f; fill.color = new Color(0.9f, 0.85f, 0.75f); fill.renderMode = LightRenderMode.ForcePixel; fill.transform.position = ap3 + sideV * 3f - Vector3.up * 1.5f - fw * 3f;
            Light key2 = new GameObject("Key2").AddComponent<Light>(); key2.transform.SetParent(studio.transform); key2.type = LightType.Point; key2.range = 20f; key2.intensity = 2.6f; key2.color = new Color(0.8f, 0.85f, 0.85f); key2.renderMode = LightRenderMode.ForcePixel; key2.transform.position = camP3 - Vector3.up * 1.8f - fw * 1.0f;
            Color amb0 = RenderSettings.ambientLight; float ai0 = RenderSettings.ambientIntensity;
            RenderSettings.ambientLight = new Color(0.16f, 0.2f, 0.22f); RenderSettings.ambientIntensity = Mathf.Max(ai0, 1f);
            RenderSettings.fogDensity = fd * 0.3f;
            yield return View(pw, mc, "l7_12_farolero_iluminado", camP3, ap3 + fw * 0.6f, 55f, camP3);
            RenderSettings.fogDensity = fd; RenderSettings.ambientLight = amb0; RenderSettings.ambientIntensity = ai0;
            Destroy(studio);
        }
        w.angler.ForceForTests(new Vector3(95f, -40f, 280f), OceanAngler.AState.Patrol);
        Vector3 ts = OceanWorld.TunnelPath[2] + new Vector3(2f, -0.9f, 0f);
        yield return View(pw, mc, "l7_10_tunel_gruta", ts + up, OceanWorld.TunnelPath[4] + Vector3.up * 1.5f, F, ts);
        Vector3 ps = OceanWorld.PocketCenter + new Vector3(-2.3f, -1.42f, 0.6f);
        yield return View(pw, mc, "l7_11_poza_salida", ps + up, OceanWorld.PocketCenter + new Vector3(3f, 1.2f, -1f), F, ps);
        Check(gm.State == GameManager.GameState.Playing || gm.State == GameManager.GameState.Won, "nivel 7 sigue en juego durante las capturas (" + gm.State + ")");
    }

    static float Mean(Texture2D t) { Color32[] px = t.GetPixels32(); double s = 0; for (int i = 0; i < px.Length; i += 17) s += (px[i].r + px[i].g + px[i].b) / 765.0; return (float)(s / (px.Length / 17)); }

    Texture2D Grab(Camera c, string name)
    {
        const int W = 1920, H = 1080;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24); RenderTexture prev = c.targetTexture;
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
