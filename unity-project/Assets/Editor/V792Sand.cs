using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.9.2 (t453u): uniformidad del color de la arena con la cámara exterior del todoterreno en marcha. Coloca el coche a 0, 100,
// 300, 600 y 1000 m del ascensor a mediodía, conduce y captura con la cámara por detrás (dos fotogramas en marcha por punto).
// En cada captura mide el color medio de la arena (solo píxeles cuyo rayo da en una duna a < 60 m, sin el coche ni el cielo)
// en una rejilla de 6×3 parches y comprueba que el tono (B/R y G/R) es el mismo en todos los parches y en toda la secuencia.
// V792_SAND_OLD=1 reproduce el antes (la sonda de reflejos del coche tiñe la arena, como en la v7.8.0–v7.9.1).
// Salida en $V792_OUT (png + report.txt con PASS/FAIL).
[InitializeOnLoad]
public static class V792Sand
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V792_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v792/sand" : o; } }
    static bool Old { get { return System.Environment.GetEnvironmentVariable("V792_SAND_OLD") == "1"; } }
    static V792Sand() { if (SessionState.GetString("smoke_phase", "") != "v792sand") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v792sand");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V792Sand] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V792Sand] " + s); }

    const int GX = 6, GY = 4, W = 1920, H = 1080, Stride = 6; const float MaxDist = 150f;
    public struct Patch { public bool ok; public int row; public float r, g, b, n; public float BR { get { return b / Mathf.Max(1e-4f, r); } } public float GR { get { return g / Mathf.Max(1e-4f, r); } } public float L { get { return (r + g + b) / 3f; } } }
    static readonly List<Patch> all = new List<Patch>();

    static Patch[] ShotAndMeasure(string name, Camera cam, Transform carT)
    {
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        RenderTexture o = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = o;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG());
        Patch[] ps = new Patch[GX * GY]; int[] tot = new int[GX * GY];
        List<int> sx = new List<int>(), sy = new List<int>(), sc = new List<int>(); List<float> sl = new List<float>();
        // la rejilla cubre el 55 % inferior de la imagen (el suelo); cada muestra solo cuenta si su rayo da en una duna cercana
        float y1 = H * 0.55f;
        for (int py = 0; py < (int)y1; py += Stride)
            for (int px = 0; px < W; px += Stride)
            {
                int cx = Mathf.Min(GX - 1, px * GX / W), cy = Mathf.Min(GY - 1, (int)(py * GY / y1)), ci = cy * GX + cx; tot[ci]++;
                Ray ray = cam.ScreenPointToRay(new Vector3(px + 0.5f, py + 0.5f, 0f));
                if (!Physics.Raycast(ray, out RaycastHit hit, MaxDist, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.collider == null || !hit.collider.name.StartsWith("Dune_")) continue;
                if (carT != null && hit.collider.transform.IsChildOf(carT)) continue;
                Color c0 = tex.GetPixel(px, py);
                sx.Add(px); sy.Add(py); sc.Add(ci); sl.Add(c0.r + c0.g + c0.b);
            }
        // las sombras (del coche, de las ruinas) son más oscuras y azuladas por el cielo: no cuentan como «otro color de arena»
        List<float> sorted = new List<float>(sl); sorted.Sort(); float med = sorted.Count > 0 ? sorted[sorted.Count / 2] : 0f; int shadowN = 0;
        for (int i = 0; i < sx.Count; i++)
        {
            if (sl[i] < med * 0.78f) { shadowN++; continue; }
            Color c = tex.GetPixel(sx[i], sy[i]); int ci = sc[i];
            ps[ci].r += c.r; ps[ci].g += c.g; ps[ci].b += c.b; ps[ci].n += 1f;
        }
        Object.DestroyImmediate(tex);
        StringBuilder sb = new StringBuilder(name + " parches (fila de abajo a arriba; B/R G/R luz; " + shadowN + " de " + sx.Count + " muestras en sombra, fuera):");
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].n > 0) { ps[i].r /= ps[i].n; ps[i].g /= ps[i].n; ps[i].b /= ps[i].n; }
            ps[i].ok = ps[i].n >= tot[i] * 0.3f && ps[i].n >= 30; ps[i].row = i / GX;
            if (i % GX == 0) sb.Append("\n   ");
            sb.Append(ps[i].ok ? string.Format(" {0:F3}/{1:F3}/{2:F2}", ps[i].BR, ps[i].GR, ps[i].L) : "      -           ");
            if (ps[i].ok) all.Add(ps[i]);
        }
        Log(sb.ToString());
        return ps;
    }
    static bool Diag { get { return System.Environment.GetEnvironmentVariable("V792_SAND_DIAG") == "1"; } }
    static void Diagnose(string nm, Camera cam, Transform carT)
    {
        int before = all.Count; Patch[] ps = ShotAndMeasure(nm, cam, carT); all.RemoveRange(before, all.Count - before);
        Spread(ps, out float a0, out float a1, out float g0, out float g1, out int n);
        Log("DIAG " + nm + ": B/R " + a0.ToString("F3") + "–" + a1.ToString("F3") + " (dif " + (a1 - a0).ToString("F3") + "), G/R dif " + (g1 - g0).ToString("F3"));
    }
    // por filas: cada fila de la rejilla está a una distancia parecida (misma niebla), así que dentro de una fila el tono debe ser igual
    static void Spread(Patch[] ps, out float brMin, out float brMax, out float grMin, out float grMax, out int n)
    {
        brMin = grMin = 9f; brMax = grMax = 0f; n = 0; float worstB = 0f, worstG = 0f;
        for (int row = 0; row < GY; row++)
        {
            float a0 = 9f, a1 = 0f, g0 = 9f, g1 = 0f;
            foreach (Patch q in ps) { if (!q.ok || q.row != row) continue; a0 = Mathf.Min(a0, q.BR); a1 = Mathf.Max(a1, q.BR); g0 = Mathf.Min(g0, q.GR); g1 = Mathf.Max(g1, q.GR); }
            if (a1 > 0f) { worstB = Mathf.Max(worstB, a1 - a0); worstG = Mathf.Max(worstG, g1 - g0); }
        }
        foreach (Patch q in ps) { if (!q.ok) continue; n++; brMin = Mathf.Min(brMin, q.BR); brMax = Mathf.Max(brMax, q.BR); grMin = Mathf.Min(grMin, q.GR); grMax = Mathf.Max(grMax, q.GR); }
        RowB = worstB; RowG = worstG;
    }
    static float RowB, RowG;

    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", ""); all.Clear();
        Log("modo " + (Old ? "ANTES (sonda del coche sobre la arena, v7.8.0–v7.9.1)" : "AHORA (v7.9.2)"));
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        gb.useRandomSeed = false; gb.seed = 12345;
        int ld = gb.LoadCount; int lv = 0; for (int i = 0; i < LevelConfig.Count; i++) if (LevelConfig.Get(i).layout == LayoutKind.Desert) lv = i;
        UIManager.Instance.StartGame(lv);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 12.5f;
        if (Health.Instance != null) Health.DisableForTests = true;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; DesertWorld w = DesertWorld.Active; DesertCars dc = DesertCars.Active;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        if (Old)
        {
            DesertWorld.DebugOldSandProbe = true;
            foreach (MeshRenderer mr in w.GetComponentsInChildren<MeshRenderer>(true)) if (mr.sharedMaterial == w.SandMat) mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbes;
        }
        int probes = 0; foreach (ReflectionProbe rp in Object.FindObjectsOfType<ReflectionProbe>()) if (rp.isActiveAndEnabled) { probes++; Log("sonda activa: " + rp.name + " (" + rp.mode + ", " + rp.size + ")"); }
        int sandR = 0, sandProbe = 0; foreach (MeshRenderer mr in w.GetComponentsInChildren<MeshRenderer>(true)) if (mr.sharedMaterial == w.SandMat) { sandR++; if (mr.reflectionProbeUsage != UnityEngine.Rendering.ReflectionProbeUsage.Off) sandProbe++; }
        Log("dunas: " + sandR + " renderers, " + sandProbe + " con sondas");

        DesertCar car = dc.Drivable;
        if (car == null) { Log("FAIL sin coche"); yield break; }
        p.Teleport(car.transform.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.6f;
        DesertCars.DebugInteract = true; yield return 0.1f; yield return 2.0f;
        Log("montado " + p.IsMounted);
        DesertCars.DebugDrive = true; DesertCars.CamView = 1;
        Vector3 s = w.StartLocal; float yaw = w.StartYaw * Mathf.Deg2Rad; Vector3 f = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)), rgt = new Vector3(f.z, 0f, -f.x);
        float[] ds = { 0f, 100f, 300f, 600f, 1000f };
        bool okAll = true; float seqBrMin = 9f, seqBrMax = 0f, seqGrMin = 9f, seqGrMax = 0f;
        Camera cam = Camera.main;
        for (int k = 0; k < ds.Length; k++)
        {
            // a 0 m el coche sale junto al ascensor (a 12 m a un lado para no chocar con la cabina)
            Vector3 q = s + f * Mathf.Max(ds[k], 14f) + rgt * 12f;
            DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true;
            car.DebugPlace(q, w.StartYaw); yield return 1.2f;
            DesertCars.DbgHandbrake = false; DesertCars.DbgThrottle = 1f; DesertCars.DbgSteer = 0.15f;
            for (int fr = 0; fr < 2; fr++)
            {
                yield return fr == 0 ? 1.4f : 1.2f;
                string nm = string.Format("arena_{0:0000}m_{1}", ds[k], fr == 0 ? "a" : "b");
                Patch[] ps = ShotAndMeasure(nm, cam, car.transform);
                Spread(ps, out float a0, out float a1, out float g0, out float g1, out int n);
                foreach (Patch pq in ps) if (pq.ok && pq.row == 0) { seqBrMin = Mathf.Min(seqBrMin, pq.BR); seqBrMax = Mathf.Max(seqBrMax, pq.BR); seqGrMin = Mathf.Min(seqGrMin, pq.GR); seqGrMax = Mathf.Max(seqGrMax, pq.GR); }
                bool ok = n >= 6 && RowB <= 0.08f && RowG <= 0.05f;
                okAll &= ok;
                if (Diag && fr == 1)
                {   // diagnóstico: la misma imagen sin polvo, sin rodadas y sin el coche (ni su sombra)
                    List<Renderer> dust = new List<Renderer>(), trk = new List<Renderer>(), body = new List<Renderer>();
                    foreach (Renderer rr in car.GetComponentsInChildren<Renderer>(true)) if (rr.enabled) { if (rr is ParticleSystemRenderer) dust.Add(rr); else body.Add(rr); }
                    foreach (Renderer rr in Object.FindObjectsOfType<Renderer>()) if (rr.enabled && rr.sharedMaterial != null && rr.sharedMaterial.shader != null && rr.sharedMaterial.shader.name.Contains("SandDecal")) trk.Add(rr);
                    PlayerBody pbd = p.GetComponentInChildren<PlayerBody>(); if (pbd != null) foreach (Renderer rr in pbd.GetComponentsInChildren<Renderer>()) if (rr.enabled) body.Add(rr);
                    Time.timeScale = 0f;
                    foreach (Renderer rr in dust) rr.enabled = false; Diagnose(nm + "_sinpolvo", cam, car.transform);
                    foreach (Renderer rr in trk) rr.enabled = false; Diagnose(nm + "_sinpolvo_sinrodadas", cam, car.transform);
                    foreach (Renderer rr in body) rr.enabled = false; Diagnose(nm + "_sinpolvo_sinrodadas_sincoche", cam, car.transform);
                    foreach (Renderer rr in dust) rr.enabled = true; foreach (Renderer rr in trk) rr.enabled = true; foreach (Renderer rr in body) rr.enabled = true;
                    Time.timeScale = 1f;
                    Log("   (polvo " + dust.Count + ", rodadas " + trk.Count + ", coche " + body.Count + " renderers)");
                }
                Log((ok ? "PASS " : "FAIL ") + nm + ": " + n + " parches de arena, peor fila B/R dif " + RowB.ToString("F3") + " · todo B/R " + a0.ToString("F3") + "–" + a1.ToString("F3") + " (dif " + (a1 - a0).ToString("F3") + "), G/R " + g0.ToString("F3") + "–" + g1.ToString("F3") + " (dif " + (g1 - g0).ToString("F3") + "), coche a " + car.Speed.ToString("F1") + " m/s");
            }
        }
        DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true; DesertCars.DbgSteer = 0f;
        // ninguna mancha más blanca (o más tostada) que la arena normal: cada parche a ≤ 0.08 de la mediana de su fila en toda la secuencia
        // (v7.9.1: 30 de 120 parches por encima, el peor a 0.104; la luz normal de las laderas da hasta ~0.06)
        float[] medRow = new float[GY];
        for (int row = 0; row < GY; row++) { List<float> brs = new List<float>(); foreach (Patch q in all) if (q.row == row) brs.Add(q.BR); brs.Sort(); medRow[row] = brs.Count > 0 ? brs[brs.Count / 2] : 0f; }
        float medBr = medRow[0];
        int outl = 0; float worst = 0f; foreach (Patch q in all) { float dv = Mathf.Abs(q.BR - medRow[q.row]); worst = Mathf.Max(worst, dv); if (dv > 0.08f) outl++; }
        Log((outl == 0 ? "PASS " : "FAIL ") + "sin manchas de otro tono (frente a la mediana de su fila): mediana B/R fila cercana " + medBr.ToString("F3") + ", peor parche a " + worst.ToString("F3") + ", " + outl + " de " + all.Count + " parches a más de 0.08");
        bool seqOk = outl == 0 && seqBrMax - seqBrMin <= 0.10f && seqGrMax - seqGrMin <= 0.06f;
        Log((seqOk ? "PASS " : "FAIL ") + "secuencia 0–1000 m (fila cercana): B/R " + seqBrMin.ToString("F3") + "–" + seqBrMax.ToString("F3") + " (dif " + (seqBrMax - seqBrMin).ToString("F3") + "), G/R " + seqGrMin.ToString("F3") + "–" + seqGrMax.ToString("F3") + " (dif " + (seqGrMax - seqGrMin).ToString("F3") + "), " + all.Count + " parches");
        Log((okAll && seqOk ? "RESULT PASS" : "RESULT FAIL") + " arena uniforme");
        DesertCars.DebugDrive = false; DesertWorld.DebugOldSandProbe = false;
        DesertSky.ForceWeather = -1; DesertSky.ForceHour = -1f; Health.DisableForTests = false;
        Log("FIN");
    }
}
