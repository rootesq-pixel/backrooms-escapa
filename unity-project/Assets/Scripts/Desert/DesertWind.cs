using UnityEngine;

// v7.8.0: síntesis periódica (todo procedimental, sin muestras de terceros): el espectro se construye solo con frecuencias
// k/T (T = duración del bucle) y se pasa al tiempo con una FFT inversa → el bucle empalma sin costura por construcción.
// Todo queda por debajo de ~1,2 kHz (viento) / ~2 kHz (motor): nada de siseo de ruido blanco.
public static class DesertSynth
{
    public const int SR = 22050;

    public static void FFT(float[] re, float[] im, bool inverse)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
            if (i < j) { float t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2.0 * System.Math.PI / len * (inverse ? 1 : -1);
            float wr = (float)System.Math.Cos(ang), wi = (float)System.Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1f, ci = 0f;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = a + len / 2;
                    float xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                    float nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                }
            }
        }
        if (inverse) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }

    private static float Gauss(System.Random r)
    {
        double u = 1.0 - r.NextDouble(), v = r.NextDouble();
        return (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u)) * System.Math.Cos(2.0 * System.Math.PI * v));
    }

    // ruido de banda con forma de espectro mag(f), periódico en n muestras (n potencia de 2)
    public static float[] Periodic(int n, System.Func<float, float> mag, int seed)
    {
        float[] re = new float[n], im = new float[n];
        System.Random r = new System.Random(seed);
        for (int k = 1; k < n / 2; k++)
        {
            float f = k * (float)SR / n, a = mag(f);
            if (a <= 0f) continue;
            float gr = Gauss(r) * a, gi = Gauss(r) * a;
            re[k] = gr; im[k] = gi; re[n - k] = gr; im[n - k] = -gi;
        }
        FFT(re, im, true);
        return re;
    }
    // líneas (armónicos) en bins exactos + un poco de anchura (rumor)
    public static float[] Lines(int n, float f0, int count, System.Func<int, float, float> amp, float jitterBins, int seed)
    {
        float[] re = new float[n], im = new float[n];
        System.Random r = new System.Random(seed);
        float bin = (float)SR / n;
        for (int h = 1; h <= count; h++)
        {
            float f = h * f0; int k0 = Mathf.RoundToInt(f / bin);
            float a = amp(h, f); if (a <= 0f || k0 <= 0 || k0 >= n / 2 - 3) continue;
            int spread = Mathf.Max(0, Mathf.RoundToInt(jitterBins));
            for (int dk = -spread; dk <= spread; dk++)
            {
                int k = k0 + dk; if (k <= 0 || k >= n / 2) continue;
                float w = dk == 0 ? 1f : 0.35f / (1f + Mathf.Abs(dk));
                float ph = (float)(r.NextDouble() * 2.0 * System.Math.PI);
                re[k] += Mathf.Cos(ph) * a * w; im[k] += Mathf.Sin(ph) * a * w;
            }
        }
        for (int k = 1; k < n / 2; k++) { re[n - k] = re[k]; im[n - k] = -im[k]; }
        FFT(re, im, true);
        return re;
    }
    public static void Normalize(float[] d, float peak) { float mx = 1e-6f; foreach (float v in d) mx = Mathf.Max(mx, Mathf.Abs(v)); for (int i = 0; i < d.Length; i++) d[i] *= peak / mx; }
    // modulación lenta periódica (sin costura: periodos que dividen el bucle)
    public static void SlowAM(float[] d, int seed, float depth)
    {
        System.Random r = new System.Random(seed); int n = d.Length;
        float p1 = (float)r.NextDouble() * 6.28f, p2 = (float)r.NextDouble() * 6.28f;
        for (int i = 0; i < n; i++) { float u = i / (float)n * 2f * Mathf.PI; d[i] *= 1f + depth * (0.6f * Mathf.Sin(u * 3f + p1) + 0.4f * Mathf.Sin(u * 7f + p2)); }
    }
    public static AudioClip Clip(string name, float[] d) { AudioClip c = AudioClip.Create(name, d.Length, 1, SR, false); c.SetData(d, 0); return c; }

    // ---- medidas para las pruebas
    public static float EnergyAbove(float[] d, float hz) { return EnergyAbove(d, hz, SR); }
    // mezcla fuera de línea (como el motor de audio: tono = remuestreo lineal, volumen, paso bajo de un polo opcional)
    public static float[] Mix(float[][] clips, float[] vol, float[] pitch, float[] lowpassHz, int outSr, float seconds)
    {
        int n = Mathf.RoundToInt(outSr * seconds); float[] o = new float[n];
        for (int c = 0; c < clips.Length; c++)
        {
            float[] d = clips[c]; double pos = 0, step = pitch[c] * SR / (double)outSr; float lp = 0f;
            float a = lowpassHz[c] > 0f ? 1f - Mathf.Exp(-2f * Mathf.PI * lowpassHz[c] / outSr) : 1f;
            for (int i = 0; i < n; i++)
            {
                int k = (int)pos; float f = (float)(pos - k); float s = d[k % d.Length] * (1f - f) + d[(k + 1) % d.Length] * f;
                lp += a * (s - lp); o[i] += lp * vol[c];
                pos += step; if (pos >= d.Length) pos -= d.Length;
            }
        }
        return o;
    }
    public static float EnergyAbove(float[] d, float hz, int sr)
    {
        int n = 1; while (n < d.Length && n < 65536) n <<= 1; if (n > d.Length) n >>= 1;
        float[] re = new float[n], im = new float[n]; System.Array.Copy(d, re, n);
        FFT(re, im, false);
        double tot = 0, hi = 0; float bin = (float)sr / n;
        for (int k = 1; k < n / 2; k++) { double e = re[k] * re[k] + im[k] * im[k]; tot += e; if (k * bin >= hz) hi += e; }
        return tot > 0 ? (float)(hi / tot) : 0f;
    }
    // salto en la costura del bucle comparado con los saltos normales entre muestras (percentil 99,9): ≤ 1 = no se nota
    public static float SeamRatio(float[] d)
    {
        float[] st = new float[d.Length - 1]; for (int i = 1; i < d.Length; i++) st[i - 1] = Mathf.Abs(d[i] - d[i - 1]);
        System.Array.Sort(st); float p99 = st[(int)(st.Length * 0.999f)];
        return Mathf.Abs(d[0] - d[d.Length - 1]) / Mathf.Max(1e-6f, p99);
    }
    public static float Rms(float[] d, int a, int b) { double s = 0; for (int i = a; i < b; i++) s += d[i] * d[i]; return Mathf.Sqrt((float)(s / Mathf.Max(1, b - a))); }

    // ---- sonidos del desierto
    private static float Smooth(float f, float lo, float hi, int ord)
    {
        float hp = Mathf.Pow(f / lo, 2f) / (1f + Mathf.Pow(f / lo, 2f));
        return hp / (1f + Mathf.Pow(f / hi, ord));
    }
    // viento: rumor grave + «aullido» suave de banda estrecha que ondula; nada por encima de ~1,1 kHz
    public static float[] Wind(int seed)
    {
        const int N = 1 << 18;   // ~11,9 s
        float[] d = Periodic(N, f =>
        {
            float b = Smooth(f, 55f, 900f, 8) / (1f + Mathf.Pow(f / 220f, 1.4f));
            float howl = 1.6f * Mathf.Exp(-Mathf.Pow((f - 330f) / 26f, 2f)) + 0.9f * Mathf.Exp(-Mathf.Pow((f - 505f) / 22f, 2f)) + 0.5f * Mathf.Exp(-Mathf.Pow((f - 190f) / 30f, 2f));
            return b + howl * 0.35f / (1f + Mathf.Pow(f / 1000f, 10f));
        }, seed);
        SlowAM(d, seed + 1, 0.45f);
        Normalize(d, 0.7f); return d;
    }
    // tormenta: más ancha y fuerte, sigue sin pasar de ~1,3 kHz
    public static float[] WindStorm(int seed)
    {
        const int N = 1 << 18;
        float[] d = Periodic(N, f =>
        {
            float b = Smooth(f, 45f, 1150f, 10) / (1f + Mathf.Pow(f / 420f, 1.1f));
            float howl = 1.2f * Mathf.Exp(-Mathf.Pow((f - 270f) / 35f, 2f)) + 0.8f * Mathf.Exp(-Mathf.Pow((f - 640f) / 40f, 2f));
            return b + howl * 0.4f / (1f + Mathf.Pow(f / 1200f, 10f));
        }, seed);
        SlowAM(d, seed + 2, 0.5f);
        Normalize(d, 0.75f); return d;
    }
    // motor diésel de 4 cilindros: armónicos de medio orden del cigüeñal (los del encendido más fuertes), resonancia del escape
    public static float[] Engine(int seed, float rpm, bool loaded)
    {
        const int N = 1 << 15;   // ~1,49 s
        float h0 = rpm / 60f / 2f;
        float[] d = Lines(N, h0, 220, (h, f) =>
        {
            float a = (h % 4 == 0 ? 1f : h % 2 == 0 ? 0.42f : 0.16f) / Mathf.Pow(1f + f / 180f, loaded ? 0.9f : 1.25f);
            a *= 1f + 0.8f * Mathf.Exp(-Mathf.Pow((f - (loaded ? 320f : 240f)) / 90f, 2f));   // escape
            return a / (1f + Mathf.Pow(f / (loaded ? 1900f : 1500f), 8f));
        }, 2f, seed);
        // traqueteo mecánico grave (ruido de banda 80-600 Hz, poco)
        float[] rumble = Periodic(N, f => Smooth(f, 80f, 600f, 6) * 0.05f, seed + 5);
        float m1 = 1e-6f, m2 = 1e-6f; foreach (float v in d) m1 = Mathf.Max(m1, Mathf.Abs(v)); foreach (float v in rumble) m2 = Mathf.Max(m2, Mathf.Abs(v));
        for (int i = 0; i < N; i++) d[i] = d[i] / m1 + rumble[i] / m2 * (loaded ? 0.22f : 0.15f);
        Normalize(d, 0.75f); return d;
    }
    // arena bajo los neumáticos: crujido grave (120-700 Hz) que va y viene
    public static float[] Tyres(int seed)
    {
        const int N = 1 << 16;
        float[] d = Periodic(N, f => Smooth(f, 110f, 720f, 6) * (1f + 0.5f * Mathf.Exp(-Mathf.Pow((f - 260f) / 80f, 2f))), seed);
        SlowAM(d, seed + 3, 0.3f);
        Normalize(d, 0.7f); return d;
    }
}

// viento del desierto: un bucle de viento (con rachas que suben y bajan el volumen, el tono y el filtro) y una capa de
// tormenta; dentro de casas y cuevas suena apagado (filtro paso bajo), dentro del todoterreno algo menos.
public class DesertWind : MonoBehaviour
{
    public static DesertWind Active;
    public AudioSource baseSrc, stormSrc;
    private AudioLowPassFilter lpBase, lpStorm;
    public float Gust { get; private set; }
    public float Muffle { get; private set; }
    public float BaseVolume { get; private set; }
    public float StormVolume { get; private set; }
    public float Cutoff { get; private set; }
    public System.Func<bool> Indoors;
    private int seed;
    public static AudioClip WindClip, StormClip;   // se comparten entre partidas (no cambian)

    public static DesertWind Create(Transform parent, int seed, System.Func<bool> indoors)
    {
        GameObject go = new GameObject("DesertWind"); go.transform.SetParent(parent, false);
        DesertWind w = go.AddComponent<DesertWind>(); w.seed = seed; w.Indoors = indoors;
        if (WindClip == null) WindClip = DesertSynth.Clip("DesertWind", DesertSynth.Wind(9101));
        if (StormClip == null) StormClip = DesertSynth.Clip("DesertWindStorm", DesertSynth.WindStorm(9202));
        w.baseSrc = w.Layer("WindBase", WindClip, out w.lpBase);
        w.stormSrc = w.Layer("WindStorm", StormClip, out w.lpStorm);
        w.baseSrc.timeSamples = (seed & 0x7fff) % WindClip.samples;
        w.baseSrc.Play();
        Active = w;
        return w;
    }
    private AudioSource Layer(string n, AudioClip c, out AudioLowPassFilter lp)
    {
        GameObject g = new GameObject(n); g.transform.SetParent(transform, false);
        AudioSource s = g.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
        lp = g.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 1400f; lp.lowpassResonanceQ = 1f;
        return s;
    }
    private void OnDestroy() { if (Active == this) Active = null; }

    // racha (0-1) en el instante t: suave y sin patrón (dos ruidos de Perlin lentos)
    public static float GustAt(float t, int seed)
    {
        float g = Mathf.PerlinNoise(t * 0.085f, seed * 0.013f + 0.5f) * 0.65f + Mathf.PerlinNoise(t * 0.29f, seed * 0.017f + 3.1f) * 0.35f;
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.78f, g));
    }

    private void Update()
    {
        DesertSky sky = DesertSky.Active;
        float dt = Time.deltaTime, t = Time.time;
        float storm = sky != null ? sky.Storm : 0f, night = sky != null ? sky.Night : 0f;
        Gust = GustAt(t, seed);
        bool inside = Indoors != null && Indoors();
        bool inCar = DesertCars.LocalInCar;
        float muffT = inside ? 1f : inCar ? 0.6f : 0f;
        Muffle = Mathf.MoveTowards(Muffle, muffT, dt * 1.5f);
        float g = Gust;
        float bv = (0.045f + 0.15f * g) * (1f - 0.45f * night) * (1f + 0.7f * storm);
        float sv = storm * (0.32f + 0.38f * g);
        float mv = Mathf.Lerp(1f, 0.32f, Muffle);
        BaseVolume = bv * mv; StormVolume = sv * mv;
        baseSrc.volume = AudioMix.A(BaseVolume);
        baseSrc.pitch = 0.9f + 0.2f * g + storm * 0.05f;
        Cutoff = Mathf.Lerp(650f + 750f * g + storm * 350f, 360f, Muffle);
        lpBase.cutoffFrequency = Cutoff; lpStorm.cutoffFrequency = Cutoff + 150f;
        stormSrc.volume = AudioMix.A(StormVolume);
        stormSrc.pitch = 0.95f + 0.15f * g;
        if (StormVolume > 0.003f && !stormSrc.isPlaying) { stormSrc.timeSamples = Random.Range(0, StormClip.samples); stormSrc.Play(); }
        else if (StormVolume <= 0.002f && stormSrc.isPlaying) stormSrc.Stop();
    }
}
