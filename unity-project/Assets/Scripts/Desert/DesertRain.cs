using UnityEngine;

// v7.9.0: lluvia del Nivel 114 por capas, todo procedimental (trabajo propio, CC0): un lecho continuo de gotitas sobre la
// arena, gotas sueltas cerca, golpeteo en el tejado (dentro de casas), gotas sobre la chapa del todoterreno (3D, en el
// propio coche: dentro suena el techo) y truenos lejanos y flojos. Cada bucle se escribe en círculo (las gotas que pasan
// del final siguen al principio) → sin punto de bucle audible; todas las capas tienen longitudes distintas. Nada de ruido
// blanco ni siseo: solo gotas (senos amortiguados) por debajo de ~4 kHz. Intensidad = fuerza de la lluvia; apagada a
// cubierto (casas, cuevas, coche), mezclada con el viento (DesertWind sube con la lluvia).
public class DesertRain : MonoBehaviour
{
    public static DesertRain Active;
    const int SR = DesertSynth.SR;
    public static float[] BedData, NearData, RoofData, MetalData; public static float[][] ThunderData;
    static AudioClip bedC, nearC, roofC, metalC; static AudioClip[] thunderC;
    public AudioSource bed, near, roof, thunder, metal;
    private AudioLowPassFilter bedLp, thunderLp;
    public float BedVolume { get; private set; }
    public float NearVolume { get; private set; }
    public float RoofVolume { get; private set; }
    public float MetalVolume { get; private set; }
    public float Cutoff { get; private set; }
    public int Thunders { get; private set; }
    public string Where { get; private set; }
    public static float ForceThunderIn = -1f;   // pruebas
    private float thunderT = 20f; private System.Random rng;
    private System.Func<int> place;   // 0 fuera, 1 bajo techo, 2 cueva, 3 coche
    private Transform metalCar;

    public static DesertRain Create(Transform parent, int seed, System.Func<int> where)
    {
        GameObject go = new GameObject("DesertRain"); go.transform.SetParent(parent, false);
        DesertRain r = go.AddComponent<DesertRain>(); r.place = where; r.rng = new System.Random(seed ^ 0x5a17);
        Build();
        r.bed = r.Layer("RainBed", bedC, true, out r.bedLp);
        r.near = r.Layer("RainNear", nearC, true, out _);
        r.roof = r.Layer("RainRoof", roofC, false, out _);
        r.thunder = r.Layer("Thunder", thunderC[0], true, out r.thunderLp); r.thunder.loop = false; r.thunder.spatialBlend = 0.45f;
        r.thunder.minDistance = 60f; r.thunder.maxDistance = 2000f; r.thunder.rolloffMode = AudioRolloffMode.Linear;
        r.bed.timeSamples = (seed & 0xffff) % bedC.samples; r.near.timeSamples = (seed * 7 & 0xffff) % nearC.samples;
        Active = r; return r;
    }
    private void OnDestroy() { if (Active == this) Active = null; if (metal != null) Destroy(metal.gameObject); }
    private AudioSource Layer(string n, AudioClip c, bool lpOn, out AudioLowPassFilter lp)
    {
        GameObject g = new GameObject(n); g.transform.SetParent(transform, false);
        AudioSource s = g.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
        lp = null; if (lpOn) { lp = g.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 5000f; lp.lowpassResonanceQ = 1f; }
        return s;
    }

    // ---- síntesis
    static void Drop(float[] d, int at, float f, float dec, float amp)
    {
        int n = d.Length, len = Mathf.Min(n - 1, Mathf.CeilToInt(SR * 6.5f / dec));   // hasta e^-6,5
        float w = 2f * Mathf.PI * f / SR, e = Mathf.Exp(-dec / SR), a = amp, ph = 0f;
        for (int j = 0; j < len; j++) { float atk = j < 12 ? j / 12f : 1f; d[(at + j) % n] += Mathf.Sin(ph) * a * atk; ph += w; a *= e; }
    }
    static float R(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }
    static void LowPass(float[] d, float hz)
    {   // dos pasadas de un polo, en círculo (dos vueltas para que el principio herede el final)
        float k = 1f - Mathf.Exp(-2f * Mathf.PI * hz / SR); int n = d.Length;
        for (int pass = 0; pass < 2; pass++) { float y = 0f; for (int it = 0; it < 2; it++) for (int i = 0; i < n; i++) { y += k * (d[i] - y); if (it == 1) d[i] = y; } }
    }
    public static void Build()
    {
        if (bedC != null) return;
        System.Random r = new System.Random(4711);
        // lecho: muchas gotitas pequeñas sobre arena (seco y suave), 150 por segundo
        int n = SR * 12; float[] d = new float[n];
        for (int k = 0; k < 150 * 12; k++) Drop(d, r.Next(n), R(r, 650f, 2300f), R(r, 260f, 520f), R(r, 0.08f, 0.45f));
        for (int k = 0; k < 55 * 12; k++) Drop(d, r.Next(n), R(r, 180f, 560f), R(r, 60f, 120f), R(r, 0.05f, 0.22f));
        LowPass(d, 3200f); DesertSynth.SlowAM(d, 77, 0.18f); DesertSynth.Normalize(d, 0.55f); BedData = d;
        // gotas sueltas cerca: chasquido + cuerpo
        n = SR * 9 + 1237; d = new float[n];
        for (int k = 0; k < 14 * 9; k++) { int at = r.Next(n); float a = R(r, 0.25f, 1f); Drop(d, at, R(r, 1400f, 2900f), R(r, 700f, 1100f), a * 0.6f); Drop(d, at + 30, R(r, 260f, 700f), R(r, 45f, 90f), a); }
        LowPass(d, 3800f); DesertSynth.Normalize(d, 0.6f); NearData = d;
        // tejado (desde dentro): golpecitos sordos de madera/yeso
        n = SR * 10 + 611; d = new float[n];
        for (int k = 0; k < 90 * 10; k++) { int at = r.Next(n); float a = R(r, 0.2f, 1f); Drop(d, at, R(r, 120f, 380f), R(r, 40f, 85f), a); Drop(d, at, R(r, 800f, 1100f), R(r, 500f, 800f), a * 0.18f); }
        LowPass(d, 1300f); DesertSynth.SlowAM(d, 78, 0.2f); DesertSynth.Normalize(d, 0.6f); RoofData = d;
        // chapa del coche: «tin» con un parcial inarmónico (por debajo de 4,5 kHz) y algo de golpe
        n = SR * 8 + 389; d = new float[n];
        for (int k = 0; k < 60 * 8; k++)
        {
            int at = r.Next(n); float f0 = R(r, 1050f, 2300f), a = R(r, 0.2f, 1f);
            Drop(d, at, f0, R(r, 26f, 46f), a * 0.55f);
            if (f0 * 1.93f < 4400f) Drop(d, at, f0 * 1.93f, R(r, 50f, 80f), a * 0.2f);
            Drop(d, at, R(r, 240f, 420f), R(r, 70f, 110f), a * 0.5f);
        }
        LowPass(d, 4200f); DesertSynth.Normalize(d, 0.6f); MetalData = d;
        // truenos lejanos: retumbo grave (< 200 Hz) con un par de golpes, subida lenta y caída larga
        ThunderData = new float[3][];
        for (int t = 0; t < 3; t++)
        {
            int tn = 1 << 17;   // 5,9 s
            float[] th = DesertSynth.Periodic(tn, f => f < 22f ? 0f : f < 190f ? 1f / Mathf.Sqrt(f) : 0f, 900 + t);
            float hit1 = R(r, 0.3f, 0.9f), hit2 = hit1 + R(r, 0.6f, 1.6f);
            for (int i = 0; i < tn; i++)
            {
                float s = i / (float)SR;
                float env = Mathf.Clamp01(s / 0.7f) * Mathf.Exp(-Mathf.Max(0f, s - 0.7f) / R(r, 1.4f, 1.5f));
                env *= 1f + 1.2f * Mathf.Exp(-Mathf.Pow((s - hit1) / 0.18f, 2f)) + 0.7f * Mathf.Exp(-Mathf.Pow((s - hit2) / 0.25f, 2f));
                th[i] *= env;
            }
            for (int i = 0; i < 2000; i++) th[tn - 1 - i] *= i / 2000f;
            DesertSynth.Normalize(th, 0.8f); ThunderData[t] = th;
        }
        bedC = DesertSynth.Clip("RainBed", BedData); nearC = DesertSynth.Clip("RainNear", NearData); roofC = DesertSynth.Clip("RainRoof", RoofData); metalC = DesertSynth.Clip("RainMetal", MetalData);
        thunderC = new AudioClip[3]; for (int t = 0; t < 3; t++) thunderC[t] = DesertSynth.Clip("Thunder" + t, ThunderData[t]);
    }

    private void EnsureMetal()
    {
        DesertCars dc = DesertCars.Active; DesertCar car = dc != null ? dc.Drivable : null;
        if (car == null || car.transform == metalCar) return;
        metalCar = car.transform;
        if (metal != null) Destroy(metal.gameObject);
        GameObject g = new GameObject("RainOnCar"); g.transform.SetParent(car.transform, false); g.transform.localPosition = new Vector3(0f, 2.0f, -0.4f);
        metal = g.AddComponent<AudioSource>(); metal.clip = metalC; metal.loop = true; metal.playOnAwake = false; metal.volume = 0f;
        metal.spatialBlend = 1f; metal.minDistance = 2.5f; metal.maxDistance = 30f; metal.rolloffMode = AudioRolloffMode.Logarithmic; metal.dopplerLevel = 0f;
        metal.timeSamples = metalC.samples / 3;
    }

    private static void Run(AudioSource s, float v)
    {
        if (s == null) return;
        s.volume = AudioMix.A(v);
        if (v > 0.002f && !s.isPlaying) s.Play(); else if (v <= 0.001f && s.isPlaying) s.Stop();
    }

    private void Update()
    {
        DesertSky sky = DesertSky.Active; if (sky == null) return;
        float dt = Time.deltaTime;
        EnsureMetal();
        int w = place != null ? place() : 0; Where = w == 0 ? "fuera" : w == 1 ? "casa" : w == 2 ? "cueva" : "coche";
        float rain = sky.Rain;
        float tb = rain * (w == 0 ? 0.40f : w == 1 ? 0.13f : w == 2 ? 0.08f : 0.12f);
        float tn = rain * (w == 0 ? 0.26f : 0f);
        float tr = rain * (w == 1 ? 0.46f : 0f);
        float tm = rain * (w == 3 ? 0.55f : 0.42f);
        float tc = w == 0 ? 5200f : w == 1 ? 650f : w == 2 ? 420f : 720f;
        float k = dt * 0.35f;
        BedVolume = Mathf.MoveTowards(BedVolume, tb, k); NearVolume = Mathf.MoveTowards(NearVolume, tn, k);
        RoofVolume = Mathf.MoveTowards(RoofVolume, tr, k); MetalVolume = Mathf.MoveTowards(MetalVolume, tm, k);
        Cutoff = Mathf.Lerp(Cutoff <= 0f ? tc : Cutoff, tc, 1f - Mathf.Exp(-4f * dt));
        if (bedLp != null) bedLp.cutoffFrequency = Cutoff;
        Run(bed, BedVolume); Run(near, NearVolume); Run(roof, RoofVolume); Run(metal, MetalVolume);
        // truenos (con lluvia fuerte o gran tormenta), lejos y flojos
        bool stormy = rain > 0.5f || sky.Storm > 0.6f;
        if (ForceThunderIn >= 0f) { thunderT = ForceThunderIn; ForceThunderIn = -1f; }
        thunderT -= dt;
        if (thunderT <= 0f)
        {
            thunderT = R(rng, 18f, 55f);
            if (stormy && thunder != null)
            {
                float az = R(rng, 0f, 6.283f);
                thunder.transform.localPosition = new Vector3(Mathf.Cos(az), 0.3f, Mathf.Sin(az)) * 420f;
                thunder.clip = thunderC[rng.Next(3)];
                thunder.volume = AudioMix.A(0.22f * (w == 0 ? 1f : 0.6f) * Mathf.Max(rain, sky.Storm));
                if (thunderLp != null) thunderLp.cutoffFrequency = w == 0 ? 900f : 380f;
                thunder.Play(); Thunders++;
            }
        }
    }
}
