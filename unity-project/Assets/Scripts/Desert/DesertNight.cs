using UnityEngine;

// v7.9.0 (usuario): de noche, en el Nivel 114, de vez en cuando suenan cosas inquietantes a lo lejos: un zumbido grave que crece
// y se apaga, una llamada como de animal que aúlla muy lejos, un golpe metálico con eco y unos murmullos lejanos. Suenan en 3D,
// apagados dentro de las casas, las cuevas y el coche. No hay ningún monstruo (el nivel es de clase 0) ni bajan la cordura.
// Todo es síntesis tonal y grave (sin siseo de aire y sin crujidos). El calendario sale de la semilla y del reloj del desierto
// (que en el multijugador manda el anfitrión): los mismos sucesos, a la vez y desde la misma dirección, para todos.
public class DesertNight : MonoBehaviour
{
    public static DesertNight Active;
    public const float Slot = 9f, Chance = 0.34f, NightMin = 0.6f;
    public static readonly string[] Names = { "DN_ZumbidoLejano", "DN_LlamadaLejana", "DN_MetalLejano", "DN_MurmullosLejanos" };
    public struct Ev { public bool on; public int kind; public float offset, azimuth, dist, pitch; }

    public int Seed { get; private set; }
    public int Played { get; private set; }
    public string Last { get; private set; }
    public int LastSlot { get; private set; }
    public AudioSource Source { get { return src; } }
    public AudioLowPassFilter Filter { get { return lp; } }
    public static bool DisableForTests;
    private static AudioClip[] clips;
    private AudioSource src; private AudioLowPassFilter lp;
    private Transform listener;
    private float lastClock = -1f;
    private float baseVol = 1f;

    public static DesertNight Create(Transform parent, int seed, Transform listener)
    {
        GameObject g = new GameObject("DesertNight"); g.transform.SetParent(parent, false);
        DesertNight n = g.AddComponent<DesertNight>(); n.Seed = seed; n.listener = listener; n.LastSlot = -1;
        if (clips == null) { clips = new AudioClip[Names.Length]; for (int i = 0; i < clips.Length; i++) clips[i] = DesertSynth.Clip(Names[i], Synth(i)); }
        n.src = g.AddComponent<AudioSource>();
        n.src.playOnAwake = false; n.src.spatialBlend = 1f; n.src.rolloffMode = AudioRolloffMode.Linear; n.src.minDistance = 25f; n.src.maxDistance = 160f; n.src.dopplerLevel = 0f;
        n.lp = g.AddComponent<AudioLowPassFilter>(); n.lp.cutoffFrequency = 1500f;
        Active = n;
        return n;
    }
    private void OnDestroy() { if (Active == this) Active = null; }

    // suceso de la ranura n (determinista: misma semilla → mismos sucesos en todos los equipos)
    public static Ev EventAt(int seed, int slot)
    {
        System.Random r = new System.Random(seed * 92821 + slot * 7919 + 17);
        Ev e = new Ev();
        e.on = r.NextDouble() < Chance;
        e.kind = r.Next(Names.Length);
        e.offset = (float)r.NextDouble() * (Slot - 1f);
        e.azimuth = (float)r.NextDouble() * 360f;
        e.dist = 45f + (float)r.NextDouble() * 70f;
        e.pitch = 0.88f + (float)r.NextDouble() * 0.22f;
        return e;
    }

    private void Update()
    {
        DesertSky sky = DesertSky.Active;
        if (sky == null || listener == null) return;
        float c = sky.Clock;
        float muff = DesertWind.Active != null ? DesertWind.Active.Muffle : 0f;
        lp.cutoffFrequency = Mathf.Lerp(1500f, 420f, muff);
        src.volume = Mathf.Lerp(1f, 0.45f, muff) * baseVol;
        if (lastClock < 0f || c < lastClock || c - lastClock > 30f) { lastClock = c; return; }   // inicio o salto de reloj: no dispara atrasados
        int s0 = Mathf.FloorToInt(lastClock / Slot), s1 = Mathf.FloorToInt(c / Slot);
        for (int s = s0; s <= s1; s++)
        {
            Ev e = EventAt(Seed, s);
            float at = s * Slot + e.offset;
            if (!e.on || at <= lastClock || at > c) continue;
            if (sky.Night < NightMin || DisableForTests) continue;
            Fire(e, s);
        }
        lastClock = c;
    }

    public void Fire(Ev e, int slot)
    {
        if (listener == null || src == null) return;
        Vector3 d = Quaternion.Euler(0f, e.azimuth, 0f) * Vector3.forward;   // misma dirección (rumbo) para todos
        src.transform.position = listener.position + d * e.dist + Vector3.up * (e.kind == 0 ? 6f : 1.5f);
        src.pitch = e.pitch;
        float v = e.kind == 0 ? 0.55f : e.kind == 3 ? 0.42f : 0.6f;
        src.PlayOneShot(clips[e.kind], AudioMix.Event(v));
        Played++; Last = clips[e.kind].name; LastSlot = slot; AudioEventLog.Note(Last);
    }
    public static AudioClip ClipFor(int k) { return clips != null ? clips[k] : null; }

    // ---- síntesis (22,05 kHz, solo tonal y grave) ----
    const int SR = DesertSynth.SR;
    public static float[] Synth(int kind)
    {
        float len = kind == 0 ? 7f : kind == 1 ? 4.5f : kind == 2 ? 3.5f : 4f;
        int n = Mathf.RoundToInt(len * SR); float[] d = new float[n];
        System.Random r = new System.Random(4400 + kind * 13);
        switch (kind)
        {
            case 0:   // zumbido grave que crece y se apaga (dos tonos que baten despacio)
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)SR, env = Mathf.Sin(Mathf.PI * t / len); env *= env;
                    d[i] = env * (Mathf.Sin(2f * Mathf.PI * 48f * t) + 0.8f * Mathf.Sin(2f * Mathf.PI * 50.6f * t + 1f) + 0.35f * Mathf.Sin(2f * Mathf.PI * 97f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 145f * t + 2f));
                }
                break;
            case 1:   // llamada lejana, como un aullido: sube, se sostiene con vibrato y cae
                {
                    float ph = 0f;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)SR, x = t / len;
                        float f = x < 0.25f ? Mathf.Lerp(260f, 470f, Mathf.SmoothStep(0f, 1f, x / 0.25f)) : x < 0.7f ? 470f + 12f * Mathf.Sin(2f * Mathf.PI * 5.2f * t) : Mathf.Lerp(470f, 300f, (x - 0.7f) / 0.3f);
                        ph += 2f * Mathf.PI * f / SR;
                        float env = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x / 0.12f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((1f - x) / 0.3f));
                        d[i] = env * (Mathf.Sin(ph) + 0.3f * Mathf.Sin(2f * ph) + 0.12f * Mathf.Sin(3f * ph));
                    }
                }
                break;
            case 2:   // golpe metálico lejano con eco (parciales inarmónicos graves)
                {
                    float[] fr = { 83f, 131f, 197f, 289f, 421f }, dc = { 1.6f, 2.1f, 2.9f, 4f, 5.5f }, am = { 1f, 0.75f, 0.5f, 0.3f, 0.16f };
                    for (int rep = 0; rep < 3; rep++)
                    {
                        int i0 = Mathf.RoundToInt(rep * 0.62f * SR); float g = rep == 0 ? 1f : rep == 1 ? 0.38f : 0.14f;
                        for (int i = 0; i + i0 < n; i++)
                        {
                            float t = i / (float)SR, s = 0f, att = 1f - Mathf.Exp(-t * 250f);
                            for (int k = 0; k < fr.Length; k++) s += am[k] * Mathf.Exp(-t * dc[k]) * Mathf.Sin(2f * Mathf.PI * fr[k] * t + k);
                            d[i + i0] += g * s * att;
                        }
                    }
                }
                break;
            default:  // murmullos lejanos: voz sorda (fundamental grave con formantes), sílabas sueltas, sin aire
                {
                    float ph = 0f; float[] syl = new float[14]; for (int k = 0; k < syl.Length; k++) syl[k] = 0.3f + (float)r.NextDouble() * 3.3f;
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)SR;
                        float f0 = 118f + 10f * Mathf.Sin(2f * Mathf.PI * 0.7f * t) + 6f * Mathf.Sin(2f * Mathf.PI * 1.9f * t);
                        ph += 2f * Mathf.PI * f0 / SR;
                        // formantes (vocal oscura «o/u»): armónicos cerca de 450 y 900 Hz más fuertes
                        float s = 0f;
                        for (int h = 1; h <= 9; h++) { float fh = h * f0; float a = Mathf.Exp(-Mathf.Pow((fh - 450f) / 180f, 2f)) + 0.5f * Mathf.Exp(-Mathf.Pow((fh - 900f) / 220f, 2f)) + 0.25f / h; s += a * Mathf.Sin(h * ph); }
                        float env = 0f;
                        foreach (float st in syl) { float u = (t - st) / 0.22f; if (u > 0f && u < 1f) env = Mathf.Max(env, Mathf.Sin(Mathf.PI * u)); }
                        d[i] = s * env * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((len - t) / 0.4f));
                    }
                }
                break;
        }
        // eco de la distancia + paso bajo (lejos, sin brillo)
        float[] o = (float[])d.Clone();
        int[] taps = { 2903, 5417, 8821 }; float[] gg = { 0.28f, 0.16f, 0.08f };
        for (int k = 0; k < taps.Length; k++) for (int i = taps[k]; i < n; i++) o[i] += d[i - taps[k]] * gg[k];
        float a1 = Mathf.Exp(-2f * Mathf.PI * 1300f / SR), y = 0f, y2 = 0f;
        for (int i = 0; i < n; i++) { y = a1 * y + (1f - a1) * o[i]; y2 = a1 * y2 + (1f - a1) * y; o[i] = y2; }
        int fo = SR / 4; for (int i = 0; i < fo && i < n; i++) o[n - 1 - i] *= i / (float)fo;
        DesertSynth.Normalize(o, 0.8f);
        return o;
    }
}
