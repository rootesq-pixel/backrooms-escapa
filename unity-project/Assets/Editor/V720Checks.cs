// v7.2.0: comprobaciones en juego. Campo: el monstruo NO atrapa a través de la pared de una casa (antes bastaba la distancia
// y la línea de los ojos, que pasaba por ventanas/huecos); al aire libre sí atraparía.
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public static class V720Checks
{
    public static IEnumerator LevelChecks(SmokeTest.Report r, string tag, int lv, LevelConfig cfg, GameManager gm)
    {
        if (gm == null || gm.player == null || gm.monsters == null || gm.monsters.Length == 0 || gm.monsters[0] == null) yield break;
        if (lv != 5 || FieldWorld.Active == null) yield break;
        string T = tag + "[v7.2.0 captura] ";
        FieldWorld fw = FieldWorld.Active; MonsterAI m = gm.monsters[0]; FirstPersonController pw = gm.player;
        Transform root = gm.generator.Root;

        // ---- v7.2.0: sin música junto a la salida del Campo (antes, la «radio» del farol de la salida tocaba una melodía) ----
        {
            string TM = tag + "[v7.2.0 salida sin música] ";
            FieldExitBeacon bc = FieldExitBeacon.Active;
            r.Check(bc != null && bc.hum != null, TM + "farol de la salida con su zumbido");
            if (bc != null)
            {
                Vector3 pp = pw.transform.position; float py = pw.transform.eulerAngles.y;
                Vector3 bpos = bc.transform.position;
                Vector3 stand = bpos + (pp - bpos).normalized * 6f;
                if (Physics.Raycast(stand + Vector3.up * 3f, Vector3.down, out RaycastHit fh, 8f, ~0, QueryTriggerInteraction.Ignore)) stand.y = fh.point.y + 0.05f;
                pw.Teleport(stand, py);
                yield return 0.8f;
                int playing = 0, music = 0; string names = "";
                foreach (AudioSource a in Object.FindObjectsOfType<AudioSource>())
                {
                    if (a == null || !a.isPlaying || a.clip == null) continue;
                    float dist = Vector3.Distance(a.transform.position, stand);
                    bool audible = a.spatialBlend < 0.5f || dist < a.maxDistance;
                    if (!audible) continue;
                    playing++; names += a.clip.name + " ";
                    string cn = a.clip.name.ToLowerInvariant();
                    if (cn.Contains("music") || cn.Contains("melod") || cn.Contains("song") || cn.Contains("radio") || cn.Contains("tune")) music++;
                    if (a.clip.name == "FieldExitHum" && MelodyRatio(a.clip) > 0.02f) music++;
                }
                r.Check(playing > 0 && music == 0, TM + "ninguna fuente de música suena a 6 m del farol de la puerta (" + playing + " fuentes: " + names.Trim() + ")");
                float mr = bc.hum != null && bc.hum.clip != null ? MelodyRatio(bc.hum.clip) : 1f;
                r.Check(!FieldExitBeacon.HasMelody && mr < 0.02f, TM + "el zumbido del farol ya no lleva melodía (energía en las notas / 50 Hz = " + mr.ToString("0.000") + ")");
                r.Check(bc.hum != null && bc.hum.isPlaying, TM + "el zumbido eléctrico (ambiente) se mantiene");
                r.Check(gm.State == GameManager.GameState.Playing, TM + "sigue la partida (no se ha completado el nivel al acercarse)");
                pw.Teleport(pp, py);
                yield return 0.3f;
            }
        }
        Vector3 p0 = pw.transform.position, m0 = m.transform.position;
        // pared maciza de una casa: punto dentro y punto fuera a 0,55 m de la pared (1,1 m en planta)
        Vector3 inP = Vector3.zero, outP = Vector3.zero; bool found = false;
        foreach (FieldPlan.House h in fw.plan.houses)
        {
            Quaternion rot = Quaternion.Euler(0f, h.rot * 90f, 0f); Vector2 sz = FieldPlan.TemplateSize[h.template];
            Vector3 c = new Vector3(h.pos.x, 0f, h.pos.y);
            for (int side = -1; side <= 1 && !found; side += 2)
                for (float z = -sz.y * 0.5f + 0.9f; z <= sz.y * 0.5f - 0.9f && !found; z += 0.4f)
                {
                    Vector3 wl = c + rot * new Vector3(side * sz.x * 0.5f, 0f, z);
                    Vector3 n = rot * new Vector3(side, 0f, 0f);
                    Vector3 a = root.TransformPoint(wl - n * 0.55f), b = root.TransformPoint(wl + n * 0.55f);
                    // suelo dentro (casa elevada) y fuera
                    if (!Physics.Raycast(a + Vector3.up * 2.4f, Vector3.down, out RaycastHit fa, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (!Physics.Raycast(b + Vector3.up * 2.4f, Vector3.down, out RaycastHit fb, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    a.y = fa.point.y; b.y = fb.point.y;
                    bool solid = true;
                    foreach (float hh in new[] { 0.5f, 1.0f, 1.5f }) { Vector3 pa = a + Vector3.up * hh, pb = b + Vector3.up * hh; if (!Physics.Linecast(pa, pb) && !Physics.Linecast(pb, pa)) solid = false; }
                    if (!solid || fa.point.y - fb.point.y > 1.5f) continue;
                    if (!NavMesh.SamplePosition(b, out NavMeshHit nb, 0.6f, NavMesh.AllAreas)) continue;
                    inP = a; outP = nb.position; found = true;
                }
            if (found) break;
        }
        r.Check(found, T + "hay una pared maciza de casa para la prueba");
        if (!found) yield break;
        pw.Teleport(inP + Vector3.up * 0.05f, 0f);
        m.WarpTo(outP, Quaternion.LookRotation(inP - outP).eulerAngles.y);
        yield return 0.3f;
        float fd = new Vector2(inP.x - outP.x, inP.z - outP.z).magnitude;
        bool would = m.WouldCatchNow();
        r.Check(fd < m.killDistance && !would, T + "jugador dentro de una casa, monstruo fuera a " + fd.ToString("F2") + " m (radio " + m.killDistance + "): no lo atrapa a través de la pared");
        m.ChaseForTests();
        float t = 0f; bool caught = false;
        while (t < 1.5f) { yield return 0.1f; t += 0.1f; pw.Teleport(inP + Vector3.up * 0.05f, 0f); if (gm.State != GameManager.GameState.Playing) { caught = true; break; } }
        r.Check(!caught, T + "persiguiéndolo 1,5 s pegado a la pared: sigue vivo (estado " + gm.State + ")");
        // al aire libre: monstruo a 1 m del jugador, sin nada en medio -> sí lo atraparía
        m.Stop();
        Vector3 open = outP + (outP - inP).normalized * 6f;
        if (NavMesh.SamplePosition(open, out NavMeshHit no, 2f, NavMesh.AllAreas)) open = no.position;
        Vector3 mo = open + (outP - inP).normalized * 0.9f;
        if (NavMesh.SamplePosition(mo, out NavMeshHit nm2, 1f, NavMesh.AllAreas)) mo = nm2.position;
        pw.Teleport(open + Vector3.up * 0.05f, 0f);
        m.WarpTo(mo, 0f);
        yield return 0.3f;
        r.Check(m.WouldCatchNow(), T + "al aire libre a " + Vector3.Distance(open, mo).ToString("F2") + " m: sí lo atraparía (camino NavMesh " + MonsterAI.LastCatchPath.ToString("F2") + " m)");
        // restaurar
        m.WarpTo(m0, 0f); pw.Teleport(p0, 0f); m.Resume();
        yield return 0.2f;
    }

    // energía (Goertzel) en las notas de la antigua melodía respecto al zumbido de 50 Hz
    public static float MelodyRatio(AudioClip c)
    {
        float[] d = new float[c.samples * c.channels];
        if (!c.GetData(d, 0)) return 1f;
        float sr = c.frequency;
        float hum = Goertzel(d, sr, 50f);
        float mel = 0f;
        foreach (float f in new[] { 220f, 246.9f, 261.6f, 196f, 174.6f, 164.8f }) mel = Mathf.Max(mel, Goertzel(d, sr, f));
        return hum > 1e-6f ? mel / hum : 1f;
    }
    private static float Goertzel(float[] d, float sr, float f)
    {
        double w = 2.0 * Mathf.PI * f / sr, cw = 2.0 * System.Math.Cos(w), s1 = 0, s2 = 0;
        for (int i = 0; i < d.Length; i++) { double s0 = d[i] + cw * s1 - s2; s2 = s1; s1 = s0; }
        return (float)System.Math.Sqrt(System.Math.Max(0.0, s1 * s1 + s2 * s2 - cw * s1 * s2)) / d.Length;
    }
}
