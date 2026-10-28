using UnityEngine;

// Nivel 7460: ruidos a tu ESPALDA. Cada cierto tiempo (al azar, 18–45 s) suena detrás del oyente, dentro del pasillo, a
// 6–30 m: pasos sobre la resina que te siguen (y se callan si te giras a mirar), una puerta que se abre y se cierra con su
// picaporte, golpes de nudillos lejanos en una puerta y susurros apagados. Sin siseo continuo y sin crujidos de madera: todo
// sintetizado, grave y corto, con un paso bajo de «detrás de la cabeza». Son locales de cada jugador (cada uno oye los suyos)
// y aceleran el latido (Adrenaline.Scare). En el multijugador no viajan por la red (no cambian el protocolo).
public class CorridorEvents : MonoBehaviour
{
    public static CorridorEvents Active { get; private set; }
    public static readonly string[] NoiseNames = { "C7460_PasosDetras", "C7460_Puerta", "C7460_Golpes", "C7460_Susurro" };
    public static bool DisableForTests;
    public const float GapMin = 18f, GapMax = 45f, FirstMin = 14f, FirstMax = 28f, DistMin = 6f, DistMax = 20f;   // v3: más cerca (se oyen claros)
    public int NoiseEvents { get; private set; }
    public string LastNoise { get; private set; }
    public Vector3 LastPos { get; private set; }
    public float LastBehindDot { get; private set; }   // -1 = justo detrás
    public int StoppedByLook { get; private set; }
    public AudioSource Source { get { return src; } }
    public AudioClip[] Clips { get { return clips; } }

    private Transform listener; private Camera cam; private BackroomsGenerator gen;
    private AudioSource src; private AudioLowPassFilter lp; private AudioClip[] clips;
    private System.Random rng; private float t; private float playingSteps;

    public static CorridorEvents Create(Transform parent, BackroomsGenerator gen, LevelConfig cfg, Transform listener, Camera cam)
    {
        GameObject g = new GameObject("CorridorEvents"); g.transform.SetParent(parent, false);
        CorridorEvents e = g.AddComponent<CorridorEvents>();
        e.gen = gen; e.listener = cam != null ? cam.transform : listener; e.cam = cam;
        e.clips = new AudioClip[NoiseNames.Length];
        for (int i = 0; i < e.clips.Length; i++) e.clips[i] = CreateNoise(i);
        GameObject s = new GameObject("BehindNoise"); s.transform.SetParent(g.transform, false);
        e.src = s.AddComponent<AudioSource>();
        e.src.playOnAwake = false; e.src.loop = false; e.src.spatialBlend = 1f; e.src.rolloffMode = AudioRolloffMode.Linear;
        e.src.minDistance = 6f; e.src.maxDistance = 40f; e.src.dopplerLevel = 0f; e.src.spread = 0f;
        e.lp = s.AddComponent<AudioLowPassFilter>(); e.lp.cutoffFrequency = 1700f;
        AudioReverbFilter rv = s.AddComponent<AudioReverbFilter>(); rv.reverbPreset = AudioReverbPreset.Hallway;
        e.rng = new System.Random((gen != null ? gen.UsedSeed : 0) * 919 + (int)(System.DateTime.Now.Ticks & 0xffff));   // local de cada jugador
        e.t = FirstMin + (float)e.rng.NextDouble() * (FirstMax - FirstMin);
        Active = e;
        return e;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (clips != null) foreach (AudioClip c in clips) if (c != null) ProceduralAssets.SafeDestroy(c);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        GameManager gm = GameManager.Instance;
        bool playing = gm == null || gm.State == GameManager.GameState.Playing;
        if (playingSteps > 0f)
        {   // pasos: si te giras y miras hacia ellos, se callan en seco
            playingSteps -= dt;
            if (listener != null && src.isPlaying)
            {
                Vector3 to = src.transform.position - listener.position; to.y = 0f;
                if (to.sqrMagnitude > 0.01f && Vector3.Dot(Flat(listener.forward), to.normalized) > 0.55f) { src.Stop(); playingSteps = 0f; StoppedByLook++; }
            }
        }
        if (!playing || DisableForTests || listener == null) return;
        if (NetSession.Active && NetSession.Instance != null && NetSession.Instance.LocalDowned) return;
        t -= dt;
        if (t > 0f) return;
        t = GapMin + (float)rng.NextDouble() * (GapMax - GapMin);
        Play(rng.Next(clips.Length));
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

    // punto detrás del oyente (±30° del «justo detrás»), dentro del pasillo
    public Vector3 BehindPoint(float dist)
    {
        Vector3 back = -Flat(listener.forward);
        float ang = ((float)rng.NextDouble() - 0.5f) * 60f;
        Vector3 dir = Quaternion.Euler(0f, ang, 0f) * back;
        Vector3 p = listener.position + dir * dist;
        CorridorWorld cw = CorridorWorld.Active;
        if (cw != null)
        {   // v7.10.2: en el pasillo (largo o transversal) en el que estás, con su marco y su largo
            Vector3 dummy; int ri = cw.RunAt(listener.position, out dummy); if (ri < 0) ri = cw.CurrentRun;
            Transform F = cw.FrameOf(ri); float len = cw.Runs.Count > 0 ? cw.Runs[ri].Length : cw.Length;
            Vector3 lp0 = F.InverseTransformPoint(listener.position), l = F.InverseTransformPoint(p);
            bool inRoom = Mathf.Abs(lp0.x - cw.CenterX) > CorridorWorld.HW;
            if (!inRoom)
            {   // a lo largo del pasillo: el que esté a tu espalda (si miras de lado, el extremo más cercano a «detrás»)
                Vector3 bl = F.InverseTransformDirection(back);
                if (Mathf.Abs(bl.z) < 0.6f)
                {   // mirando a una pared: el ruido sale de la puerta que tienes justo a la espalda (dentro de su cuarto)
                    float zc = (Mathf.Floor(lp0.z / CorridorWorld.CS) + 0.5f) * CorridorWorld.CS;
                    l = new Vector3(cw.CenterX + Mathf.Sign(bl.x) * (CorridorWorld.XR + CorridorWorld.RD * 0.75f), l.y, Mathf.Clamp(zc, 0.6f, len - 0.6f));
                    return F.TransformPoint(l);
                }
                float sz = Mathf.Abs(bl.z) > 0.2f ? Mathf.Sign(bl.z) : (lp0.z > len * 0.5f ? -1f : 1f);
                l.z = lp0.z + sz * Mathf.Max(dist * Mathf.Max(0.5f, Mathf.Abs(bl.z)), DistMin);
                l.x = cw.CenterX + Mathf.Clamp(l.x - cw.CenterX, -(CorridorWorld.HW - 0.3f), CorridorWorld.HW - 0.3f);
            }
            l.z = Mathf.Clamp(l.z, 0.6f, len - 0.6f);
            p = F.TransformPoint(l);
        }
        return p;
    }

    public void Play(int k)
    {
        if (listener == null || src == null || clips == null) return;
        float dist = DistMin + (float)rng.NextDouble() * (DistMax - DistMin);
        if (k == 0) dist = Mathf.Min(dist, 12f);            // pasos: cerca
        if (k == 3) dist = Mathf.Min(dist, 6.5f);             // susurro: muy cerca
        Vector3 p = BehindPoint(dist);
        p.y = listener.position.y + (k == 0 ? -1.4f : k == 3 ? 0.1f : -0.4f);
        src.transform.position = p;
        Vector3 to = p - listener.position; to.y = 0f;
        LastBehindDot = to.sqrMagnitude > 0.01f ? Vector3.Dot(Flat(listener.forward), to.normalized) : -1f;
        src.pitch = 0.94f + (float)rng.NextDouble() * 0.12f;
        lp.cutoffFrequency = k == 3 ? 3600f : k == 2 ? 2600f : 3000f;   // «detrás de la cabeza»: sin brillo
        float vol = k == 0 ? 1.0f : k == 1 ? 1.0f : k == 2 ? 0.95f : 0.85f;
        src.Stop();
        src.clip = clips[k]; src.volume = AudioMix.Event(vol); src.Play();
        playingSteps = k == 0 ? clips[k].length : 0f;
        NoiseEvents++; LastNoise = clips[k].name; LastPos = p; AudioEventLog.Note(clips[k].name);
        Adrenaline.Scare(k == 3 ? 0.55f : k == 0 ? 0.5f : 0.38f);
    }

    // ---- síntesis (22,05 kHz, todo grave y corto; sin ruido continuo) ----
    const int SR = 22050;
    public static AudioClip CreateNoise(int kind)
    {
        System.Random r = new System.Random(74600 + kind * 41);
        float len = kind == 0 ? 4.6f : kind == 1 ? 3.4f : kind == 2 ? 2.6f : 2.4f;
        int n = Mathf.RoundToInt(len * SR); float[] d = new float[n];
        switch (kind)
        {
            case 0:   // pasos de zapato sobre resina dura: tacón + suela, 7 pisadas que se acercan y se paran
                for (int i = 0; i < 7; i++)
                {
                    float tt = 0.2f + i * 0.56f + (float)(r.NextDouble() - 0.5) * 0.04f;
                    float a = 0.35f + 0.65f * (i / 6f);
                    Click(d, tt, a, 1150f + (i & 1) * 90f, 70f, r);               // tacón (seco)
                    Thud(d, tt + 0.03f, a * 0.55f, 120f, 38f, 0.05f, r, 420f);    // suela
                    Thud(d, tt + 0.01f, a * 0.35f, 700f, 60f, 0.25f, r, 1600f);   // v3: roce de la suela (medios: se oye en altavoces pequeños)
                }
                break;
            case 1:   // picaporte + puerta que se abre, pausa, y se cierra con golpe sordo y pestillo (sin crujido)
                Click(d, 0.10f, 0.7f, 900f, 90f, r); Click(d, 0.16f, 0.45f, 1300f, 110f, r);   // manilla y resbalón
                Thud(d, 0.22f, 0.25f, 95f, 12f, 0.06f, r, 300f);                             // la hoja se despega
                Thud(d, 2.2f, 1.0f, 70f, 10f, 0.18f, r, 320f);                               // cierre
                Click(d, 2.23f, 0.8f, 1000f, 85f, r);                                         // pestillo
                break;
            case 2:   // tres golpes de nudillos en una puerta lejana, pausa, dos más
                float[] ts = { 0.1f, 0.38f, 0.66f, 1.6f, 1.86f };
                foreach (float tt in ts) { Thud(d, tt, 1f, 160f, 30f, 0.10f, r, 650f); Thud(d, tt, 0.8f, 420f, 55f, 0.08f, r, 1400f); Click(d, tt, 0.35f, 820f, 120f, r); }   // v3: nudillo con más ataque
                break;
            default:  // susurro: sílabas cortas de ruido con formantes (vocal apagada), envolvente suave, nada continuo
                {
                    float[] syl = { 0.15f, 0.42f, 0.66f, 1.1f, 1.34f, 1.62f };
                    float[] f1 = { 520f, 700f, 450f, 600f, 380f, 650f }, f2 = { 1300f, 1100f, 1700f, 1200f, 1500f, 1000f };
                    for (int s = 0; s < syl.Length; s++)
                    {
                        int i0 = Mathf.RoundToInt(syl[s] * SR), L = Mathf.RoundToInt((0.16f + (float)r.NextDouble() * 0.08f) * SR);
                        float b1 = 0f, b2 = 0f, y1 = 0f, y2 = 0f, z1 = 0f, z2 = 0f;
                        for (int i = 0; i < L && i0 + i < n; i++)
                        {
                            float x = (float)(r.NextDouble() * 2.0 - 1.0);
                            float env = Mathf.Sin(Mathf.PI * i / L); env *= env;
                            // dos resonadores (formantes) sobre ruido: suena a voz sin tono, sin siseo agudo
                            float w1 = 2f * Mathf.PI * f1[s] / SR, w2 = 2f * Mathf.PI * f2[s] / SR, q = 0.985f;
                            float o1 = x + 2f * q * Mathf.Cos(w1) * y1 - q * q * y2; y2 = y1; y1 = o1;
                            float o2 = x + 2f * q * Mathf.Cos(w2) * z1 - q * q * z2; z2 = z1; z1 = o2;
                            b1 = o1 * 0.05f + o2 * 0.03f; b2 = b2 * 0.6f + b1 * 0.4f;
                            d[i0 + i] += b2 * env;
                        }
                    }
                }
                break;
        }
        // cola de pasillo (ecos tempranos) y paso bajo final: nada por encima de ~3 kHz
        float[] o = (float[])d.Clone();
        int[] taps = { 977, 1611, 2459, 3593 }; float[] g = { 0.28f, 0.2f, 0.14f, 0.09f };
        for (int k = 0; k < taps.Length; k++) for (int i = taps[k]; i < n; i++) o[i] += d[i - taps[k]] * g[k];
        LowPass(o, kind == 3 ? 3400f : 3000f); LowPass(o, kind == 3 ? 3400f : 3000f);   // v3: algo más de presencia, sin siseo
        float mx = 1e-4f; for (int i = 0; i < n; i++) mx = Mathf.Max(mx, Mathf.Abs(o[i]));
        for (int i = 0; i < n; i++) o[i] /= mx;
        const float drive = 2.4f; float tn = (float)System.Math.Tanh(drive);   // v3: compresión suave (más volumen percibido con el mismo pico)
        for (int i = 0; i < n; i++) o[i] = 0.9f * (float)System.Math.Tanh(o[i] * drive) / tn;
        int fo = Mathf.Min(n, SR / 5); for (int i = 0; i < fo; i++) o[n - 1 - i] *= i / (float)fo;
        AudioClip c = AudioClip.Create(NoiseNames[kind], n, 1, SR, false); c.SetData(o, 0);
        return c;
    }

    static void Click(float[] d, float t0, float amp, float f, float decay, System.Random r)
    {   // golpe corto y seco (tacón, pestillo): seno amortiguado + ráfaga de ruido muy corta
        int i0 = Mathf.RoundToInt(t0 * SR), len = Mathf.Min(d.Length - i0, Mathf.RoundToInt(SR * 6f / decay));
        float lpv = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)SR, env = Mathf.Exp(-t * decay) * (1f - Mathf.Exp(-t * 3000f));
            float wn = (float)(r.NextDouble() * 2.0 - 1.0); lpv = lpv * 0.5f + wn * 0.5f;
            d[i0 + i] += amp * env * (0.6f * Mathf.Sin(2f * Mathf.PI * f * t) + 0.5f * lpv * Mathf.Exp(-t * 300f));
        }
    }
    static void Thud(float[] d, float t0, float amp, float f, float decay, float noiseAmt, System.Random r, float noiseCut)
    {
        int i0 = Mathf.RoundToInt(t0 * SR), len = Mathf.Min(d.Length - i0, Mathf.RoundToInt(SR * 6f / decay));
        if (len <= 0) return;
        float a = Mathf.Exp(-2f * Mathf.PI * noiseCut / SR), l1 = 0f, l2 = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)SR, env = Mathf.Exp(-t * decay) * (1f - Mathf.Exp(-t * 900f));
            float fs = f * (1f + 0.25f * Mathf.Exp(-t * 30f));
            float wn = (float)(r.NextDouble() * 2.0 - 1.0); l1 = a * l1 + (1f - a) * wn; l2 = a * l2 + (1f - a) * l1;
            d[i0 + i] += amp * env * (Mathf.Sin(2f * Mathf.PI * fs * t) + noiseAmt * 6f * l2);
        }
    }
    public static float Rms(AudioClip c) { float[] d = new float[c.samples]; c.GetData(d, 0); double a = 0; int cnt = 0; for (int i = 0; i < d.Length; i++) if (Mathf.Abs(d[i]) > 0.01f) { a += d[i] * d[i]; cnt++; } return cnt > 0 ? Mathf.Sqrt((float)(a / cnt)) : 0f; }   // RMS de la parte con sonido
    static void LowPass(float[] d, float cut) { float a = Mathf.Exp(-2f * Mathf.PI * cut / SR), y = 0f; for (int i = 0; i < d.Length; i++) { y = a * y + (1f - a) * d[i]; d[i] = y; } }
}
