using System.Collections.Generic;
using UnityEngine;

// v7.9.0 Nivel 0 (Oficinas amarillas): sucesos del nivel.
//  · Ruidos aleatorios en 3D lejos del jugador: un portazo lejano, pasos al fondo, golpes apagados en una pared y un golpe
//    metálico sordo. Todos generados por código, graves y amortiguados: sin crujidos de madera y sin siseo de aire.
//    Son locales de cada jugador (cada uno oye los suyos).
//  · Alarma roja: cada cierto tiempo TODA la sala se ilumina de rojo durante 10–30 s y luego vuelve la luz de siempre
//    (v7.9.1: como la luz de emergencia de una alarma: todos los fluorescentes y paneles en rojo, ambiente y niebla rojos,
//    paredes y suelo claramente rojos, con un pulso lento de alarma y un zumbido grave de alarma; sin siseo). El calendario sale de la semilla del nivel y del reloj del nivel; en el
//    multijugador el anfitrión manda su reloj en el snapshot, así que el rojo empieza y acaba a la vez para todos.
public class Level0Events : MonoBehaviour
{
    public static Level0Events Active { get; private set; }

    // calendario del rojo
    public const float RedMin = 10f, RedMax = 30f, FirstMin = 50f, FirstMax = 100f, GapMin = 75f, GapMax = 160f, Fade = 0.9f;
    public static readonly Color RedLight = new Color(1f, 0.05f, 0.03f);
    public static readonly Color RedEmission = new Color(1f, 0.05f, 0.03f);
    public const float RedIntensity = 1.15f;           // v7.9.1: el rojo alumbra toda la sala (antes 0,72 y casi a oscuras)
    public static readonly Color RedAmbient = new Color(0.34f, 0.028f, 0.020f);   // v7.9.1: la sala entera roja, también rincones y lejos de las luces
    public static readonly Color RedFog = new Color(0.30f, 0.022f, 0.016f);
    public const float PulsePeriod = 2.0f, PulseMin = 0.70f;   // pulso lento de alarma (mismo reloj → igual para todos)
    public const float AlarmVolume = 0.28f, AlarmHz = 500f, AlarmHz2 = 1200f, AlarmSweep = 3.5f;   // v7.10.3: sirena real de evacuación (tono «slow whoop» NEN 2575: 500→1200 Hz en 3,5 s + 0,5 s de silencio, con timbre de bocina y sala); 0,22 → 0,28: suena ≈ 1,3 dB más fuerte que la v7.10.2 (−21,1 frente a −22,4 LUFS en el juego).   // v7.9.4 (t473u): antes 0,20 × sucesos (0,75) y 180 Hz: unos −32 dB, por debajo del drone y casi inaudible en altavoces pequeños
    public static float AlarmGain { get { return AudioMix.A(AlarmVolume); } }
    // pulso de la alarma: 1 en el pico, PulseMin en el valle (en el reloj del nivel)
    public static float PulseAt(float clock) { float s = Mathf.Sin(Mathf.PI * clock / PulsePeriod); return PulseMin + (1f - PulseMin) * s * s; }

    public struct RedEvent { public float start, dur; public float End { get { return start + dur; } } }
    public readonly List<RedEvent> Schedule = new List<RedEvent>();

    public float Clock { get; private set; }            // segundos de nivel (anfitrión / solo: propio; clientes: el del anfitrión)
    public float Red01 { get; private set; }            // 0 normal … 1 todo rojo (con parpadeo al cambiar)
    public bool RedOn { get; private set; }
    public int RedStarts { get; private set; }
    public int NetClocks { get; private set; }
    public int Seed { get; private set; }
    public int NoiseEvents { get; private set; }
    public string LastNoise { get; private set; }
    public AudioSource NoiseSource { get { return noiseSrc; } }
    public AudioLowPassFilter NoiseFilter { get { return noiseLp; } }
    public static readonly string[] NoiseNames = { "L0_GolpeLejano", "L0_PasosLejanos", "L0_GolpesPared", "L0_GolpeMetalico" };
    public AudioClip[] NoiseClips { get { return clips; } }
    public static bool DisableNoiseForTests;

    private Transform listener;
    private Camera cam;
    private FlickeringLight[] lights = new FlickeringLight[0];
    private Material panelMat; private Color panelBase;
    private Color baseAmbient, baseFog;
    private float appliedRed = -1f;
    private AudioClip[] clips;
    private AudioSource noiseSrc, relaySrc, alarmSrc; private AudioLowPassFilter noiseLp;
    private AudioClip alarmClip;
    public AudioSource AlarmSource { get { return alarmSrc; } }
    public float Pulse01 { get; private set; } = 1f;
    private float noiseT;
    private System.Random noiseRng;

    public static Level0Events Create(Transform parent, BackroomsGenerator gen, LevelConfig cfg, Transform listener, Camera cam)
    {
        GameObject g = new GameObject("Level0Events");
        g.transform.SetParent(parent, false);
        Level0Events e = g.AddComponent<Level0Events>();
        e.Setup(gen, cfg, listener, cam);
        return e;
    }

    private void Setup(BackroomsGenerator gen, LevelConfig cfg, Transform l, Camera c)
    {
        Active = this;
        listener = l; cam = c;
        Seed = gen != null ? gen.UsedSeed : 0;
        BuildSchedule(Seed, Schedule);
        List<FlickeringLight> ls = new List<FlickeringLight>();
        if (gen != null) foreach (FlickeringLight f in gen.GetComponentsInChildren<FlickeringLight>(true)) ls.Add(f);
        if (ls.Count == 0) foreach (FlickeringLight f in FindObjectsOfType<FlickeringLight>()) ls.Add(f);
        lights = ls.ToArray();
        panelMat = gen != null ? gen.PanelEmissiveMat : null;
        if (panelMat != null && panelMat.HasProperty("_EmissionColor")) panelBase = panelMat.GetColor("_EmissionColor"); else panelMat = null;
        baseAmbient = cfg.ambient; baseFog = cfg.fog;
        clips = new AudioClip[NoiseNames.Length];
        for (int i = 0; i < clips.Length; i++) clips[i] = CreateNoise(i);
        noiseSrc = Src("L0Noise", 6f, 48f);
        noiseLp = noiseSrc.gameObject.AddComponent<AudioLowPassFilter>(); noiseLp.cutoffFrequency = 1800f;
        AudioReverbFilter rv = noiseSrc.gameObject.AddComponent<AudioReverbFilter>(); rv.reverbPreset = AudioReverbPreset.Hallway;
        relaySrc = Src("L0Relay", 3f, 30f);
        alarmClip = CreateAlarm();
        alarmSrc = Src("L0Alarm", 4f, 40f); alarmSrc.spatialBlend = 0f; alarmSrc.loop = true; alarmSrc.clip = alarmClip; alarmSrc.volume = 0f;
        AudioLowPassFilter alp = alarmSrc.gameObject.AddComponent<AudioLowPassFilter>(); alp.cutoffFrequency = AlarmCutoff;   // v7.10.3: deja pasar los armónicos de la bocina (hasta ~5 kHz; nada de ruido)
        noiseRng = new System.Random(Seed * 613 + (int)(System.DateTime.Now.Ticks & 0xffff));   // local de cada jugador
        noiseT = 20f + (float)noiseRng.NextDouble() * 25f;
    }

    private AudioSource Src(string n, float min, float max)
    {
        GameObject g = new GameObject(n); g.transform.SetParent(transform, false);
        AudioSource a = g.AddComponent<AudioSource>();
        a.playOnAwake = false; a.loop = false; a.spatialBlend = 1f; a.rolloffMode = AudioRolloffMode.Linear;
        a.minDistance = min; a.maxDistance = max; a.dopplerLevel = 0f;
        return a;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (appliedRed > 0f) ApplyRed(0f, true);
        if (clips != null) foreach (AudioClip c in clips) if (c != null) ProceduralAssets.SafeDestroy(c);
        if (alarmClip != null) ProceduralAssets.SafeDestroy(alarmClip);
    }

    // calendario determinista: misma semilla → mismos apagones rojos (anfitrión y clientes)
    public static void BuildSchedule(int seed, List<RedEvent> list)
    {
        list.Clear();
        System.Random r = new System.Random(seed * 7919 + 4243);
        float t = FirstMin + (float)r.NextDouble() * (FirstMax - FirstMin);
        for (int i = 0; i < 120; i++)
        {
            float d = RedMin + (float)r.NextDouble() * (RedMax - RedMin);
            list.Add(new RedEvent { start = t, dur = d });
            t += d + GapMin + (float)r.NextDouble() * (GapMax - GapMin);
        }
    }

    public int EventAt(float clock)
    {
        for (int i = 0; i < Schedule.Count; i++)
        {
            if (clock < Schedule[i].start) return -1;
            if (clock < Schedule[i].End) return i;
        }
        return -1;
    }

    // rojo (0..1) en un instante: fundido con parpadeo al principio y al final; igual para todos (depende del reloj y la semilla)
    public float RedAt(float clock)
    {
        int i = EventAt(clock);
        if (i < 0) return 0f;
        RedEvent e = Schedule[i];
        float a = clock - e.start, b = e.End - clock;
        float edge = Mathf.Min(a, b);
        if (edge >= Fade) return 1f;
        float k = edge / Fade;
        float n = Mathf.PerlinNoise(clock * 13f, (Seed & 1023) * 0.37f);
        return n > 0.62f - 0.4f * k ? k : k * 0.15f;   // chisporroteo del cambio: encendidos y apagones breves
    }

    // ---- red: reloj del anfitrión ----
    public void NetClock(float c)
    {
        NetClocks++;
        if (Mathf.Abs(c - Clock) > 1.0f) Clock = c; else Clock = Mathf.Lerp(Clock, c, 0.35f);
    }
    public void DebugSetClock(float c) { Clock = c; }

    private void Update()
    {
        float dt = Time.deltaTime;
        GameManager gm = GameManager.Instance;
        bool playing = gm == null || gm.State == GameManager.GameState.Playing;
        if (playing && dt > 0f) Clock += dt;   // el cliente sigue avanzando entre snapshots (y se corrige con el siguiente)
        float red = RedAt(Clock);
        bool on = red > 0.5f;
        if (on && !RedOn) { RedStarts++; Relay(true); }
        else if (!on && RedOn) Relay(false);
        RedOn = on;
        Red01 = red;
        if (Screamer.Active == null) ApplyRed(red, false);   // el susto guarda y restaura el ambiente: no se pisa
        TickAlarm(red);
        if (playing && !DisableNoiseForTests) TickNoise(dt);
    }

    private void Relay(bool toRed)
    {
        if (relaySrc == null || listener == null || clips == null) return;
        relaySrc.transform.position = listener.position + new Vector3(1.5f, 2.6f, -2f);
        relaySrc.pitch = toRed ? 0.62f : 0.70f;
        relaySrc.PlayOneShot(clips[3], AudioMix.Event(0.35f)); AudioEventLog.Note("L0_Rele");
    }

    private float appliedPulse = -1f;
    public void ApplyRed(float red, bool force)
    {
        float pulse = red > 0f ? PulseAt(Clock) : 1f;
        Pulse01 = pulse;
        if (!force && Mathf.Abs(red - appliedRed) < 0.004f && Mathf.Abs(pulse - appliedPulse) < 0.02f) return;
        appliedRed = red; appliedPulse = pulse;
        foreach (FlickeringLight f in lights) if (f != null) f.SetRed(red, RedLight, RedEmission, RedIntensity * pulse);
        if (panelMat != null)
        {
            float lum = panelBase.maxColorComponent;
            panelMat.SetColor("_EmissionColor", Color.Lerp(panelBase, RedEmission * lum * pulse, red));
        }
        float ap = Mathf.Lerp(1f, 0.55f + 0.45f * pulse, red);   // el ambiente también late (los rincones se oscurecen un poco en el valle)
        RenderSettings.ambientLight = Color.Lerp(baseAmbient, RedAmbient * ap, red);
        RenderSettings.fogColor = Color.Lerp(baseFog, RedFog * ap, red);
        if (cam != null) cam.backgroundColor = RenderSettings.fogColor;
    }

    // ---- zumbido de alarma (bucle de 4 s = dos pulsos, en fase con el reloj del nivel) ----
    private void TickAlarm(float red)
    {
        if (alarmSrc == null) return;
        GameManager gm = GameManager.Instance;
        bool playing = gm == null || gm.State == GameManager.GameState.Playing;
        float target = playing && Screamer.Active == null ? red * AlarmVolume : 0f;
        alarmSrc.volume = AudioMix.A(target);   // v7.9.4: en el grupo Ambiente, sin la rebaja de los sucesos
        if (target > 0.001f)
        {
            if (!alarmSrc.isPlaying) { alarmSrc.time = Mathf.Repeat(Clock, AlarmLoop); alarmSrc.Play(); }
        }
        else if (alarmSrc.isPlaying) alarmSrc.Stop();
    }
    public const float AlarmLoop = 2f * PulsePeriod;
    // v7.10.3 (pt522u): sirena de alarma REALISTA. Antes (v7.9.4) era un seno de 440–660 Hz con dos armónicos: sonaba a tono de
    // prueba («rara»). Ahora es el tono de evacuación real «slow whoop» de la norma neerlandesa NEN 2575 (tabla de tonos de
    // Eaton/Fulleon y Auer: 500 → 1200 Hz en 3,5 s y 0,5 s de silencio), que es justo el bucle de 4 s (dos pulsos de la luz).
    // La bocina se excita con una onda casi cuadrada (armónicos impares, algo de pares) y su respuesta (pasa banda 350 Hz–5 kHz
    // con resonancias en 1,4 y 2,9 kHz) le da el timbre metálico del altavoz (sin saturación: evitaría aliasing). Encima, reflexiones
    // tempranas y una cola corta de sala (Schroeder: 4 peines con amortiguación + 2 pasa todo, RT60 ≈ 1,1 s) calculadas en
    // bucle circular, así que el bucle no tiene costura. Todo tonal (sin ruido ni siseo), nada por encima de 9 kHz, sin crujidos.
    public const float AlarmCutoff = 6500f;
    public static AudioClip CreateAlarm() { float[] d = AlarmData(); AudioClip c = AudioClip.Create("L0_Alarma", d.Length, 1, SR, false); c.SetData(d, 0); return c; }
    public static float AlarmFreq(float t)
    {   // barrido lineal 500 → 1200 Hz en AlarmSweep s; luego se queda arriba mientras se apaga
        float c = Mathf.Repeat(t, AlarmLoop);
        return AlarmHz + (AlarmHz2 - AlarmHz) * Mathf.Clamp01(c / AlarmSweep);
    }
    public static float AlarmAmp(float t)
    {
        float c = Mathf.Repeat(t, AlarmLoop);
        float a = Mathf.Min(1f, c / 0.04f);                                // arranque del altavoz (40 ms, sin clic)
        if (c > AlarmSweep) a = Mathf.Exp(-(c - AlarmSweep) / 0.06f);       // corte al final del barrido
        return a * (0.8f + 0.2f * Mathf.Min(1f, c / AlarmSweep));
    }
    private static float Horn(float f)
    {
        float hp = 1f / (1f + Mathf.Pow(350f / f, 4f)), lp = 1f / (1f + Mathf.Pow(f / 5200f, 6f));
        float r = 1f + 0.8f * Mathf.Exp(-Sq((f - 1400f) / 450f)) + 0.5f * Mathf.Exp(-Sq((f - 2900f) / 600f));
        return hp * lp * r;
    }
    private static float Sq(float x) { return x * x; }
    public static float[] AlarmData()
    {
        int n = Mathf.RoundToInt(AlarmLoop * SR); float[] x = new float[n];
        double ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR, f = AlarmFreq(t);
            ph += f / (double)SR;
            float s = 0f;
            for (int h = 1; h <= 15; h++)
            {
                float fh = f * h; if (fh >= 9500f) break;
                float w = ((h & 1) == 1 ? 1f / h : 0.12f / h) * Mathf.Clamp01((9500f - fh) / 2500f);   // los armónicos entran y salen sin escalón (sin clics ni aliasing)
                s += w * Horn(fh) * (float)System.Math.Sin(2.0 * System.Math.PI * h * ph);
            }
            x[i] = s * AlarmAmp(t);   // sin saturación: tanh creaba armónicos por encima de 11 kHz que se doblaban (aliasing) en tonos falsos que bajaban
        }
        float[] d = RoomLoop(x, 3);
        float pk = 1e-4f; for (int i = 0; i < n; i++) pk = Mathf.Max(pk, Mathf.Abs(d[i]));
        float g = 0.9f / pk; for (int i = 0; i < n; i++) d[i] *= g;
        return d;
    }
    // sala (bucle circular): reflexiones tempranas + 4 peines amortiguados + 2 pasa todo; «passes» vueltas para que la cola
    // del final entre por el principio (bucle sin costura)
    public static float[] RoomLoop(float[] x, int passes)
    {
        int n = x.Length;
        float[] erD = { 0.011f, 0.019f, 0.027f, 0.041f, 0.057f }, erG = { 0.32f, 0.24f, 0.18f, 0.13f, 0.09f };
        float[] cD = { 0.0297f, 0.0371f, 0.0411f, 0.0437f };
        int[] ck = new int[4]; float[] cg = new float[4], clp = new float[4]; float[][] cy = new float[4][];
        for (int c = 0; c < 4; c++) { ck[c] = (int)(cD[c] * SR); cg[c] = Mathf.Pow(10f, -3f * cD[c] / 1.1f); cy[c] = new float[n]; }
        int k1 = (int)(0.005f * SR), k2 = (int)(0.0017f * SR); const float ag = 0.7f;
        float[] a1i = new float[n], a1o = new float[n], a2o = new float[n], outp = new float[n];
        for (int p = 0; p < passes; p++)
        {
            bool first = p == 0;
            for (int i = 0; i < n; i++)
            {
                float s = 0f;
                for (int c = 0; c < 4; c++)
                {
                    int j = i - ck[c]; float fb = j >= 0 ? cy[c][j] : (first ? 0f : cy[c][j + n]);
                    clp[c] += 0.45f * (fb - clp[c]);
                    cy[c][i] = x[i] + cg[c] * clp[c]; s += cy[c][i];
                }
                s *= 0.25f;
                int q = i - k1; float xd = q >= 0 ? a1i[q] : (first ? 0f : a1i[q + n]), yd = q >= 0 ? a1o[q] : (first ? 0f : a1o[q + n]);
                a1i[i] = s; float o1 = -ag * s + xd + ag * yd; a1o[i] = o1;
                q = i - k2; xd = q >= 0 ? a1o[q] : (first ? 0f : a1o[q + n]); yd = q >= 0 ? a2o[q] : (first ? 0f : a2o[q + n]);
                float o2 = -ag * o1 + xd + ag * yd; a2o[i] = o2;
                if (p == passes - 1)
                {
                    float e = 0f; for (int r = 0; r < erD.Length; r++) { int jj = i - (int)(erD[r] * SR); if (jj < 0) jj += n; e += erG[r] * x[jj]; }
                    outp[i] = x[i] + e + 0.616f * o2;
                }
            }
        }
        return outp;
    }

    // ---- ruidos ----
    private void TickNoise(float dt)
    {
        noiseT -= dt;
        if (noiseT > 0f) return;
        noiseT = 22f + (float)noiseRng.NextDouble() * 34f;
        PlayNoise(noiseRng.Next(clips.Length));
    }

    public void PlayNoise(int k)
    {
        if (listener == null || noiseSrc == null || clips == null) return;
        float ang = (float)noiseRng.NextDouble() * Mathf.PI * 2f, dist = 14f + (float)noiseRng.NextDouble() * 18f;
        noiseSrc.transform.position = listener.position + new Vector3(Mathf.Cos(ang) * dist, k == 1 ? 0.2f : 1.2f, Mathf.Sin(ang) * dist);
        noiseSrc.pitch = 0.9f + (float)noiseRng.NextDouble() * 0.18f;
        noiseLp.cutoffFrequency = k == 2 ? 1300f : 1900f;   // los golpes en la pared, más apagados (al otro lado)
        float vol = k == 1 ? 0.75f : 0.9f;
        noiseSrc.PlayOneShot(clips[k], AudioMix.Event(vol));
        NoiseEvents++; LastNoise = clips[k].name; AudioEventLog.Note(clips[k].name);
    }

    // ---- síntesis (22,05 kHz, todo grave: casi nada por encima de 3 kHz) ----
    const int SR = 22050;
    public static AudioClip CreateNoise(int kind)
    {
        System.Random r = new System.Random(9100 + kind * 31);
        float len = kind == 0 ? 2.2f : kind == 1 ? 4.2f : kind == 2 ? 1.8f : 2.4f;
        int n = Mathf.RoundToInt(len * SR);
        float[] d = new float[n];
        switch (kind)
        {
            case 0:   // portazo lejano: golpe grave con cola de sala
                Thud(d, 0f, 1f, 62f, 9f, 0.55f, r, 260f);
                Thud(d, 0.05f, 0.35f, 118f, 16f, 0.25f, r, 420f);
                break;
            case 1:   // pasos lejanos: 6 pisadas sordas, primero acercándose y luego alejándose
                for (int i = 0; i < 6; i++)
                {
                    float t = 0.15f + i * 0.58f + (float)(r.NextDouble() - 0.5) * 0.05f;
                    float a = 0.55f + 0.45f * Mathf.Sin(Mathf.PI * (i + 0.5f) / 6f);
                    Thud(d, t, a, 84f + (i & 1) * 9f, 30f, 0.10f, r, 340f);
                    Thud(d, t + 0.045f, a * 0.4f, 150f, 42f, 0.05f, r, 520f);
                }
                break;
            case 2:   // tres golpes apagados al otro lado de una pared (nudillos, sin crujido)
                for (int i = 0; i < 3; i++)
                {
                    float t = 0.1f + i * 0.29f;
                    Thud(d, t, i == 2 ? 0.8f : 1f, 150f, 34f, 0.12f, r, 600f);
                    Thud(d, t, 0.45f, 260f, 48f, 0.06f, r, 800f);
                }
                break;
            default:  // golpe metálico sordo: parciales inarmónicos graves con decaimiento largo
                {
                    float[] fr = { 96f, 151f, 227f, 341f, 498f }, dc = { 2.2f, 2.8f, 3.6f, 5.0f, 7.0f }, am = { 1f, 0.7f, 0.5f, 0.32f, 0.18f };
                    for (int i = 0; i < n; i++)
                    {
                        float t = i / (float)SR, s = 0f, att = 1f - Mathf.Exp(-t * 300f);
                        for (int k = 0; k < fr.Length; k++) s += am[k] * Mathf.Exp(-t * dc[k]) * Mathf.Sin(2f * Mathf.PI * fr[k] * t + k);
                        d[i] += 0.5f * s * att;
                    }
                    Thud(d, 0f, 0.7f, 70f, 14f, 0.2f, r, 300f);
                }
                break;
        }
        // cola de sala corta (ecos tempranos atenuados) y un último paso bajo para quitar cualquier brillo
        float[] o = (float[])d.Clone();
        int[] taps = { 1123, 1789, 2671, 3907 }; float[] g = { 0.30f, 0.22f, 0.15f, 0.10f };
        for (int k = 0; k < taps.Length; k++) for (int i = taps[k]; i < n; i++) o[i] += d[i - taps[k]] * g[k];
        LowPass(o, 1500f); LowPass(o, 1500f);
        float mx = 1e-4f; for (int i = 0; i < n; i++) mx = Mathf.Max(mx, Mathf.Abs(o[i]));
        for (int i = 0; i < n; i++) o[i] *= 0.85f / mx;
        int fo = Mathf.Min(n, SR / 5); for (int i = 0; i < fo; i++) o[n - 1 - i] *= i / (float)fo;   // sin chasquido final
        AudioClip c = AudioClip.Create(NoiseNames[kind], n, 1, SR, false); c.SetData(o, 0);
        return c;
    }

    // golpe: seno grave con caída + ruido marrón filtrado (cuerpo), con ataque suave (sin clic)
    static void Thud(float[] d, float t0, float amp, float f, float decay, float noiseAmt, System.Random r, float noiseCut)
    {
        int i0 = Mathf.RoundToInt(t0 * SR), len = Mathf.Min(d.Length - i0, Mathf.RoundToInt(SR * 6f / decay));
        if (len <= 0) return;
        float a = Mathf.Exp(-2f * Mathf.PI * noiseCut / SR), lp = 0f, lp2 = 0f;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Exp(-t * decay) * (1f - Mathf.Exp(-t * 900f));
            float fs = f * (1f + 0.25f * Mathf.Exp(-t * 30f));   // leve caída de tono al golpear
            float wn = (float)(r.NextDouble() * 2.0 - 1.0);
            lp = a * lp + (1f - a) * wn; lp2 = a * lp2 + (1f - a) * lp;
            d[i0 + i] += amp * env * (Mathf.Sin(2f * Mathf.PI * fs * t) + noiseAmt * 6f * lp2);
        }
    }

    static void LowPass(float[] d, float cut)
    {
        float a = Mathf.Exp(-2f * Mathf.PI * cut / SR), y = 0f;
        for (int i = 0; i < d.Length; i++) { y = a * y + (1f - a) * d[i]; d[i] = y; }
    }
}
