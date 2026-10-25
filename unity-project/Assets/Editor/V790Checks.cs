// v7.9.0 «Nivel 0»: ruidos aleatorios (sin crujidos ni siseo), apagones rojos de 10–30 s iguales para todos (semilla + reloj
// del anfitrión), el nivel sigue oscuro, y latido por amenaza en los niveles con monstruos (no en el Nivel 114).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class V790Checks
{
    static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }

    // fracción de energía por encima de f0 (paso-alto biquad de 2.º orden)
    public static float HighRatio(float[] d, int sr, float f0)
    {
        float w0 = 2f * Mathf.PI * f0 / sr, cs = Mathf.Cos(w0), al = Mathf.Sin(w0) / (2f * 0.7071f);
        float b0 = (1f + cs) / 2f, b1 = -(1f + cs), b2 = (1f + cs) / 2f, a0 = 1f + al, a1 = -2f * cs, a2 = 1f - al;
        double eh = 0, et = 0; float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < d.Length; i++)
        {
            float x = d[i], y = (b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            eh += y * y; et += x * x;
        }
        return et > 0 ? (float)(eh / et) : 0f;
    }

    public static void Edit(SmokeTest.Report r)
    {
        string T = "[v7.9.0] ";
        r.Check(GameVersion.Current == "7.9.0" && NetSession.Protocol == "BackroomsEscape-MP-12", T + "versión " + GameVersion.Current + ", protocolo " + NetSession.Protocol);
        // ruidos
        string bad = ""; float worst = 0f;
        for (int k = 0; k < Level0Events.NoiseNames.Length; k++)
        {
            AudioClip c = Level0Events.CreateNoise(k);
            float[] d = new float[c.samples]; c.GetData(d, 0);
            float hr = HighRatio(d, c.frequency, 3000f), pk = 0f, tail = 0f;
            for (int i = 0; i < d.Length; i++) pk = Mathf.Max(pk, Mathf.Abs(d[i]));
            for (int i = d.Length - 200; i < d.Length; i++) tail = Mathf.Max(tail, Mathf.Abs(d[i]));
            worst = Mathf.Max(worst, hr);
            if (c.name != Level0Events.NoiseNames[k] || c.length < 1f || c.length > 5f || pk < 0.5f || pk > 0.95f || hr > 0.01f || tail > 0.02f) bad += " " + c.name + "(" + (hr * 100f).ToString("F2") + " %)";
            Object.DestroyImmediate(c);
        }
        r.Check(bad == "", T + "[Ruidos] 4 ruidos (portazo lejano, pasos, golpes en la pared, golpe metálico): graves, sin siseo (máx. " + (worst * 100f).ToString("F2") + " % por encima de 3 kHz), sin chasquido final" + bad);
        foreach (string n in Level0Events.NoiseNames) r.Check(!n.ToLower().Contains("cruj") && !n.ToLower().Contains("creak") && !n.ToLower().Contains("hiss"), T + "[Ruidos] " + n + ": no es un crujido ni un siseo");
        // calendario del rojo
        bool okDur = true, okGap = true, det = true, differ = false; string first = "";
        foreach (int seed in new[] { 1, 7, 12345, 144139097 })
        {
            var a = new List<Level0Events.RedEvent>(); var b = new List<Level0Events.RedEvent>();
            Level0Events.BuildSchedule(seed, a); Level0Events.BuildSchedule(seed, b);
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].dur < Level0Events.RedMin - 1e-3f || a[i].dur > Level0Events.RedMax + 1e-3f) okDur = false;
                if (i > 0 && a[i].start - a[i - 1].End < Level0Events.GapMin - 1e-3f) okGap = false;
                if (a[i].start != b[i].start || a[i].dur != b[i].dur) det = false;
            }
            if (a[0].start < Level0Events.FirstMin || a[0].start > Level0Events.FirstMax) okGap = false;
            first += " " + a[0].start.ToString("F0") + "+" + a[0].dur.ToString("F0") + "s";
            var o = new List<Level0Events.RedEvent>(); Level0Events.BuildSchedule(seed + 1, o);
            if (o[0].start != a[0].start) differ = true;
        }
        r.Check(okDur, T + "[Rojo] cada apagón rojo dura 10–30 s");
        r.Check(okGap, T + "[Rojo] el primero llega a los 50–100 s y entre uno y otro pasan 75–160 s (" + first.Trim() + ")");
        r.Check(det && differ, T + "[Rojo] el calendario sale de la semilla: igual en todos los equipos, distinto en cada partida");
        // latido
        AudioClip hb = Adrenaline.CreateHeartbeat();
        r.Check(hb.length > 0.3f && hb.length < 0.5f, T + "[Latido] un latido suelto (" + hb.length.ToString("F2") + " s) que se dispara a su ritmo");
        Object.DestroyImmediate(hb);
        r.Check(Adrenaline.HeartMinBpm < 75f && Adrenaline.HeartMaxBpm >= 140f, T + "[Latido] ritmo " + Adrenaline.HeartMinBpm + "–" + Adrenaline.HeartMaxBpm + " ppm");
    }

    // recorrido en el nivel 0 (monstruos ya parados por la prueba)
    public static IEnumerator Level0Play(SmokeTest.Report r, string tag, GameManager gm)
    {
        Level0Events le = Level0Events.Active;
        r.Check(le != null, tag + "[Nivel 0] sucesos del nivel (ruidos y apagones rojos) activos");
        if (le == null) yield break;
        FlickeringLight[] fls = Object.FindObjectsOfType<FlickeringLight>();
        FlickeringLight lit = null; foreach (FlickeringLight f in fls) if (f.targetLight != null && f.targetLight.type == LightType.Point) { lit = f; break; }
        BackroomsGenerator gen = gm.generator;
        r.Check(lit != null && gen != null && gen.PanelEmissiveMat != null, tag + "[Rojo] luces (" + fls.Length + ") y paneles del techo localizados");
        r.Check(le.Clock < Level0Events.FirstMin && !le.RedOn, tag + "[Rojo] al empezar el nivel la luz es la normal (reloj " + le.Clock.ToString("F0") + " s)");
        Color amb0 = RenderSettings.ambientLight, col0 = lit != null ? lit.targetLight.color : Color.white;
        Color pan0 = gen != null && gen.PanelEmissiveMat != null ? gen.PanelEmissiveMat.GetColor("_EmissionColor") : Color.white;
        // latido en calma: no suena
        Adrenaline adr = Adrenaline.Local;
        Adrenaline.ForceThreatForTests = 0f; yield return 2.5f;
        int bc = adr != null ? adr.Beats : 0; yield return 1.5f;
        r.Check(adr != null && adr.HeartVolume == 0f && adr.Beats == bc, tag + "[Latido] en calma no suena");
        Adrenaline.ForceThreatForTests = 0.25f; yield return 3.0f;
        float bpmLo = adr.Bpm, volLo = adr.HeartVolume; int b1 = adr.Beats; yield return 2.0f; int nLo = adr.Beats - b1;
        Adrenaline.ForceThreatForTests = 1f; yield return 2.5f;
        float bpmHi = adr.Bpm, volHi = adr.HeartVolume; int b2 = adr.Beats; yield return 2.0f; int nHi = adr.Beats - b2;
        Adrenaline.ForceThreatForTests = -1f;
        r.Check(volLo > 0.005f && bpmLo < 100f && bpmHi > 135f && volHi > volLo * 2f && nHi > nLo,
            tag + "[Latido] más rápido y más fuerte con más amenaza: " + bpmLo.ToString("F0") + " → " + bpmHi.ToString("F0") + " ppm, vol " + volLo.ToString("F2") + " → " + volHi.ToString("F2") + ", latidos en 2 s " + nLo + " → " + nHi);
        // amenaza real: el monstruo cerca persiguiendo > lejos
        MonsterAI main = null; foreach (MonsterAI m in gm.monsters) if (m != null && m.mimic == null) { main = m; break; }
        if (main != null && adr != null)
        {
            Transform me = adr.transform; Vector3 dir = me.forward; dir.y = 0f; dir.Normalize();
            float kd = main.killDistance; main.killDistance = 0.05f;
            main.ChaseForTests();
            main.WarpTo(me.position + dir * 26f, 0f); float tFar = Adrenaline.ThreatFor(me);
            main.WarpTo(me.position + dir * 5f, 0f); float tNear = Adrenaline.ThreatFor(me);
            main.Stop(); main.WarpTo(me.position + new Vector3(500f, 0f, 500f), 0f); main.killDistance = kd;
            float tStop = Adrenaline.ThreatFor(me);
            r.Check(tNear > tFar && tNear > 0.9f && tFar > 0.5f && tStop == 0f, tag + "[Latido] amenaza por distancia y persecución: a 26 m " + tFar.ToString("F2") + ", a 5 m " + tNear.ToString("F2") + ", sin monstruo " + tStop.ToString("F2"));
        }
        yield return 4.5f;
        // ruidos
        Level0Events.DisableNoiseForTests = true;
        string heard = ""; bool okSrc = true;
        for (int k = 0; k < Level0Events.NoiseNames.Length; k++)
        {
            int n0 = le.NoiseEvents;
            le.PlayNoise(k);
            float d = Vector3.Distance(le.NoiseSource.transform.position, adr.transform.position);
            if (le.NoiseEvents != n0 + 1 || le.LastNoise != Level0Events.NoiseNames[k] || d < 13f || d > 33f || le.NoiseSource.spatialBlend < 0.99f || le.NoiseFilter.cutoffFrequency > 2000f) okSrc = false;
            heard += " " + le.LastNoise + " a " + d.ToString("F0") + " m";
            yield return 0.6f;
        }
        Level0Events.DisableNoiseForTests = false;
        r.Check(okSrc, tag + "[Ruidos] suenan en 3D, lejos (13–33 m) y apagados (paso-bajo ≤ 2 kHz):" + heard);
        // apagón rojo: se adelanta el reloj del nivel
        Level0Events.RedEvent ev = le.Schedule[0];
        le.DebugSetClock(ev.start - 1.5f); yield return 0.3f;
        bool before = !le.RedOn && le.Red01 == 0f;
        int rs0 = le.RedStarts;
        le.DebugSetClock(ev.start + 2f); yield return 0.3f;
        Color amb1 = RenderSettings.ambientLight, col1 = lit != null ? lit.targetLight.color : Color.black;
        Color pan1 = gen.PanelEmissiveMat.GetColor("_EmissionColor");
        r.Check(before && le.RedOn && le.Red01 > 0.99f && le.RedStarts == rs0 + 1, tag + "[Rojo] empieza a su hora (" + ev.start.ToString("F0") + " s) y dura " + ev.dur.ToString("F0") + " s");
        r.Check(col1.r > 4f * col1.g && col1.r > 4f * col1.b && pan1.r > 4f * pan1.g, tag + "[Rojo] fluorescentes y paneles en rojo (luz " + col1 + ", panel " + pan1 + ")");
        r.Check(Lum(amb1) < Lum(amb0) * 0.6f && lit.targetLight.intensity <= lit.baseIntensity * 0.75f + 1e-3f && Lum(pan1) < Lum(pan0), tag + "[Rojo] el nivel sigue oscuro: ambiente " + Lum(amb0).ToString("F3") + " → " + Lum(amb1).ToString("F3") + ", luces ×" + Level0Events.RedIntensity);
        if (V704Checks.Shots) { Camera cam = adr.GetComponentInChildren<Camera>(); if (cam != null) V704Checks.CaptureHud(cam, "l0_rojo.png"); }
        le.DebugSetClock(ev.End - 0.3f); yield return 0.1f;
        le.DebugSetClock(ev.End + 1.5f); yield return 0.3f;
        Color amb2 = RenderSettings.ambientLight, col2 = lit != null ? lit.targetLight.color : Color.black;
        Color pan2 = gen.PanelEmissiveMat.GetColor("_EmissionColor");
        bool back = !le.RedOn && le.Red01 == 0f && Mathf.Abs(amb2.r - amb0.r) + Mathf.Abs(amb2.g - amb0.g) < 0.002f && Mathf.Abs(col2.g - col0.g) < 0.01f && Mathf.Abs(pan2.g - pan0.g) < 0.01f;
        r.Check(back, tag + "[Rojo] al acabar vuelve la luz de siempre");
        le.DebugSetClock(1f); yield return 0.2f;
    }

    // Nivel 114: sin latido (no hay monstruos)
    public static IEnumerator DesertHeart(SmokeTest.Report r, string tag)
    {
        Adrenaline adr = Adrenaline.Local;
        yield return 1.0f;
        r.Check(adr != null && adr.HeartVolume == 0f && Adrenaline.ThreatFor(adr.transform) == 0f && Level0Events.Active == null, tag + "[Latido] en el Nivel 114 no hay latido ni sucesos del nivel 0");
    }
}
