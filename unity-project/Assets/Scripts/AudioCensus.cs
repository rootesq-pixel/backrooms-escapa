using System.Collections.Generic;
using System.Text;
using UnityEngine;

// Censo de TODAS las fuentes de audio de la escena (para auditoría y pruebas): clip, bucle, volumen, distancia al oyente,
// ganancia por atenuación y volumen efectivo (volumen × AudioListener.volume × atenuación). No cambia nada.
public static class AudioCensus
{
    public struct Row
    {
        public string owner, clip;
        public bool loop, playing;
        public float volume, blend, minD, maxD, dist, rolloffGain, effective;
        public AudioRolloffMode mode;
    }

    public static string PathOf(Transform t)
    {
        string s = t.name;
        int guard = 0;
        while (t.parent != null && guard++ < 4) { t = t.parent; s = t.name + "/" + s; }
        return s;
    }

    public static List<Row> Take()
    {
        List<Row> rows = new List<Row>();
        AudioListener al = Object.FindObjectOfType<AudioListener>();
        Vector3 lp = al != null ? al.transform.position : Vector3.zero;
        foreach (AudioSource a in Object.FindObjectsOfType<AudioSource>(true))
        {
            Row r = new Row();
            r.owner = PathOf(a.transform);
            r.clip = a.clip != null ? a.clip.name : "(sin clip)";
            r.loop = a.loop; r.playing = a.isPlaying && a.enabled && a.gameObject.activeInHierarchy && !a.mute;
            r.volume = a.volume; r.blend = a.spatialBlend; r.minD = a.minDistance; r.maxD = a.maxDistance; r.mode = a.rolloffMode;
            r.dist = Vector3.Distance(lp, a.transform.position);
            float ro = 1f;
            if (a.rolloffMode == AudioRolloffMode.Linear) ro = Mathf.Clamp01(1f - (r.dist - a.minDistance) / Mathf.Max(0.01f, a.maxDistance - a.minDistance));
            else ro = Mathf.Clamp01(a.minDistance / Mathf.Max(a.minDistance, r.dist));
            r.rolloffGain = Mathf.Lerp(1f, ro, a.spatialBlend);
            r.effective = r.volume * AudioListener.volume * r.rolloffGain;
            rows.Add(r);
        }
        return rows;
    }

    // fuentes en bucle que están sonando y se oyen (volumen efectivo > umbral)
    public static int AudibleLoops(float threshold)
    {
        int n = 0;
        foreach (Row r in Take()) if (r.loop && r.playing && r.effective > threshold) n++;
        return n;
    }

    public static string Dump(string title)
    {
        List<Row> rows = Take();
        rows.Sort((a, b) => b.effective.CompareTo(a.effective));
        StringBuilder sb = new StringBuilder();
        int loops = 0, audible = 0;
        foreach (Row r in rows) { if (r.loop && r.playing) loops++; if (r.loop && r.playing && r.effective > 0.001f) audible++; }
        sb.AppendLine("== " + title + ": " + rows.Count + " AudioSources, " + loops + " bucles sonando, " + audible + " audibles (>0.001)");
        foreach (Row r in rows)
            if (r.playing || r.loop)
                sb.AppendLine(string.Format("   {0,-46} clip={1,-18} loop={2} play={3} vol={4:0.000} blend={5:0.0} d={6,5:0.0} max={7,4:0} rolloff={8:0.00} efectivo={9:0.0000}",
                    r.owner, r.clip, r.loop ? "S" : "n", r.playing ? "S" : "n", r.volume, r.blend, r.dist, r.maxD, r.rolloffGain, r.effective));
        return sb.ToString();
    }
}
