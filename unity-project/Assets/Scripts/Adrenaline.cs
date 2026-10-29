using UnityEngine;

// v7.0.4: adrenalina. Mientras un monstruo te persigue de verdad (incluido el imitador descubierto y El Vigía), la
// resistencia (RES) se gasta un 45 % más despacio, corres un 7 % más rápido y, al empezar la persecución, recibes un pequeño
// empujón de resistencia (una vez cada 25 s). Termina unos segundos después de dejar de perseguirte. Latido sutil y barra
// RES teñida de rojo anaranjado. Es local de cada jugador (también en los clientes del multijugador): no cambia el protocolo.
public class Adrenaline : MonoBehaviour
{
    public const float DrainMul = 0.55f, SprintMul = 1.07f, Boost = 0.20f, Linger = 4f, BoostCooldown = 25f, ChaseRange = 35f;
    public static Adrenaline Local { get; private set; }

    public FirstPersonController fpc;
    public bool Active { get; private set; }          // con efecto (persecución o en los segundos siguientes)
    public bool Chased { get; private set; }          // persecución en curso
    public float Level01 { get; private set; }        // intensidad (fundido) para audio / HUD
    public int Boosts { get; private set; }
    public MonsterAI Chaser { get; private set; }
    public static int ForceChasedForTests = -1;       // -1 normal, 0 nunca, 1 siempre

    private float sinceChase = 99f, lastBoostAt = -99f;
    public void ResetBoostCooldownForTests() { lastBoostAt = -99f; }
    private AudioSource heart;
    private AudioClip heartClip;
    private AudioClip[] heartClips;   // v7.10.3: 4 latidos (2 en reposo, 2 rápidos) que se alternan
    // v7.9.0: latido por amenaza (todos los niveles con monstruos, no el Nivel 114): en calma no suena; con un monstruo cerca
    // empieza lento y bajo, y se acelera y sube cuanto más cerca está o si te persigue. Local de cada jugador.
    public const float HeartMinBpm = 66f, HeartMaxBpm = 150f, HeartRange = 30f;
    public float Threat01 { get; private set; }       // 0 calma … 1 encima / persiguiéndote
    public float Bpm { get; private set; }
    public float HeartVolume { get; private set; }
    // v7.9.4 (t474u): más fuerte y más presente cuanto más cerca (antes 0,10–0,55 con el cuadrado de la amenaza: con un
    // monstruo a media distancia apenas 0,2, y el latido de 52–64 Hz casi no sale por altavoces ni auriculares normales)
    public const float HeartGainLo = 0.35f, HeartGainHi = 1.0f;
    public static float HeartGain(float threat) { return Mathf.Lerp(HeartGainLo, HeartGainHi, Mathf.Pow(Mathf.Clamp01(threat), 1.3f)); }
    public int Beats { get; private set; }
    public float ChaseMuffle01 { get; private set; }  // paso-bajo del resto de sonidos durante la persecución (VhsEffect)
    public static float ForceThreatForTests = -1f;    // >= 0: amenaza fija (pruebas)
    // Nivel 7460: un ruido a tu espalda (CorridorEvents) acelera el latido un rato aunque no haya monstruos
    public static float ScareThreat;                  // 0..1, decae solo
    public static void Scare(float k) { ScareThreat = Mathf.Max(ScareThreat, Mathf.Clamp01(k)); }
    private float beatPhase;

    private void Awake()
    {
        Local = this;
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        GameObject h = new GameObject("Heartbeat"); h.transform.SetParent(transform, false);
        heartClips = new AudioClip[HeartVariants]; for (int k = 0; k < HeartVariants; k++) heartClips[k] = CreateHeartbeatVariant(k);
        heartClip = heartClips[0];
        heart = h.AddComponent<AudioSource>();
        heart.playOnAwake = false;
        heart.clip = heartClip; heart.loop = false; heart.spatialBlend = 0f; heart.volume = 1f;
        heart.bypassEffects = true; heart.bypassListenerEffects = true; heart.priority = 8;   // fuera del paso-bajo de la persecución
    }

    // amenaza: distancia al monstruo más cercano (que no esté disfrazado) y su estado
    public static float ThreatFor(Transform me)
    {
        if (me == null) return 0f;
        GameManager gm = GameManager.Instance;
        if (DesertWorld.Active != null || (gm != null && gm.config != null && gm.config.layout == LayoutKind.Desert)) return 0f;   // Nivel 114: sin latido
        float best = 0f;
        foreach (MonsterAI m in MonsterAI.Instances)
        {
            if (m == null || !m.isActiveAndEnabled || m.Disguised || (!m.netProxy && m.IsStopped)) continue;
            float d = FieldWrap.Distance(me.position, m.transform.position);
            if (d > HeartRange) continue;
            float near = 1f - Mathf.Clamp01((d - 3f) / (HeartRange - 3f));
            float st = m.CurrentState == MonsterAI.State.Chase || m.CurrentState == MonsterAI.State.Kill ? 1f : m.CurrentState == MonsterAI.State.Search ? 0.75f : 0.5f;
            float t = near * st;
            if (st >= 1f) t = Mathf.Max(t, 0.55f + 0.45f * near);   // te persigue: siempre deprisa
            best = Mathf.Max(best, t);
        }
        return best;
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
        if (fpc != null) { fpc.staminaDrainMul = 1f; fpc.sprintMul = 1f; }
        if (heartClips != null) foreach (AudioClip hc in heartClips) if (hc != null) ProceduralAssets.SafeDestroy(hc);
    }

    // ¿algún monstruo me persigue a MÍ? Solo / anfitrión: su objetivo soy yo. Cliente: el más cercano a él entre los vivos.
    public static MonsterAI FindChaser(Transform me)
    {
        MonsterAI best = null; float bd = float.MaxValue;
        foreach (MonsterAI m in MonsterAI.Instances)
        {
            if (m == null || !m.isActiveAndEnabled || m.Disguised || (!m.netProxy && m.IsStopped)) continue;
            if (m.CurrentState != MonsterAI.State.Chase && m.CurrentState != MonsterAI.State.Kill) continue;
            float d = FieldWrap.Distance(me.position, m.transform.position);
            if (d > ChaseRange || d >= bd) continue;
            bool mine;
            if (!m.netProxy) mine = m.target == me;
            else
            {
                mine = true;
                foreach (RemotePlayer rp in RemotePlayer.All)
                {
                    if (rp == null || rp.Downed) continue;
                    if (FieldWrap.Distance(rp.transform.position, m.transform.position) < d - 1f) { mine = false; break; }
                }
            }
            if (!mine) continue;
            best = m; bd = d;
        }
        return best;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f || fpc == null) return;
        GameManager gm = GameManager.Instance;
        bool playing = gm != null && gm.State == GameManager.GameState.Playing;
        MonsterAI ch = playing && ForceChasedForTests != 0 ? FindChaser(transform) : null;
        bool chased = ForceChasedForTests == 1 ? playing : ch != null;
        if (NetSession.Active && NetSession.Instance != null && NetSession.Instance.LocalDowned) chased = false;
        if (chased && !Chased && Time.time - lastBoostAt >= BoostCooldown)
        {
            fpc.RestoreStamina(Boost); lastBoostAt = Time.time; Boosts++;
        }
        Chased = chased; Chaser = ch;
        sinceChase = chased ? 0f : sinceChase + dt;
        Active = playing && sinceChase <= Linger;
        float goal = Active ? (Chased ? 1f : Mathf.Clamp01(1f - sinceChase / Linger)) : 0f;
        Level01 = Mathf.MoveTowards(Level01, goal, dt * (goal > Level01 ? 2.5f : 0.6f));
        fpc.staminaDrainMul = Active ? DrainMul : 1f;
        fpc.sprintMul = Active ? SprintMul : 1f;
        // latido: por amenaza (y la adrenalina lo mantiene alto unos segundos); en calma, silencio
        float th = ForceThreatForTests >= 0f ? ForceThreatForTests : (playing ? ThreatFor(transform) : 0f);
        if (ForceChasedForTests == 1 && playing) th = Mathf.Max(th, 0.85f);
        if (NetSession.Active && NetSession.Instance != null && NetSession.Instance.LocalDowned) th = 0f;
        th = Mathf.Max(th, Level01 * 0.8f);
        if (ScareThreat > 0f) { if (playing) th = Mathf.Max(th, ScareThreat); ScareThreat = Mathf.Max(0f, ScareThreat - dt * 0.07f); }
        bool scare = Screamer.Active != null;
        Threat01 = Mathf.MoveTowards(Threat01, th, dt * (th > Threat01 ? 1.6f : 0.35f));
        Bpm = Mathf.Lerp(HeartMinBpm, HeartMaxBpm, Threat01);
        HeartVolume = scare || Threat01 < 0.06f ? 0f : AudioMix.A(HeartGain(Threat01));
        ChaseMuffle01 = Mathf.MoveTowards(ChaseMuffle01, scare ? 0f : (Chased ? Mathf.Clamp01(0.4f + 0.6f * Threat01) : 0f), dt * (scare ? 6f : 1.2f));
        if (heart != null)
        {
            beatPhase += dt * Bpm / 60f;
            if (beatPhase >= 1f)
            {
                beatPhase -= Mathf.Floor(beatPhase);
                if (HeartVolume > 0.001f)
                {
                    // v7.10.3: con el corazón acelerado el «dub» llega antes (variantes rápidas); se alternan dos tomas para que no suene a bucle
                    int v = (Bpm > HeartFastBpm ? 2 : 0) + (Beats & 1);
                    heart.pitch = Mathf.Lerp(0.98f, 1.05f, Threat01);   // casi sin cambio de timbre: el ritmo lo da el intervalo
                    heart.PlayOneShot(heartClips != null ? heartClips[v] : heartClip, HeartVolume * (0.94f + 0.06f * Random.value)); Beats++;
                }
            }
        }
    }

    // RES teñida: amarillo verdoso normal → rojo anaranjado con adrenalina
    public static Color Tint(Color normal) { Adrenaline a = Local; return a == null ? normal : Color.Lerp(normal, new Color(1f, 0.42f, 0.22f), a.Level01); }

    // un latido (lub-dub) suelto: se dispara a su ritmo (v7.9.0; antes un bucle de 2 latidos a 72 ppm)
    // v7.10.3 (pt522u): latido REALISTA. Antes eran senos de 55/68 Hz con armónicos y un «toc» a 160–330 Hz: sonaba a
    // sintetizador. Ahora sigue un latido real oído desde dentro (como con un fonendo o en el oído con miedo):
    //  · «lub» (1.er ruido, cierre de mitral y tricúspide): golpe grave de 46 Hz con una caída de tono rápida (de ~78 a 46 Hz en
    //    35 ms, el tejido se tensa y se afloja), ataque de 6 ms y caída de 50 ms, desdoblado en dos componentes a 24 ms;
    //  · «dub» (2.º ruido, válvulas aórtica y pulmonar): más corto (34 ms), algo más agudo (60 Hz) y más flojo (0,72);
    //  · textura de tejido: ruido muy grave (paso bajo de 2 polos a 220 Hz, nada de agudos ni siseo) con su propia envolvente,
    //    y la resonancia de la pared del pecho (140–175 Hz) para que se oiga en altavoces pequeños y auriculares;
    //  · saturación suave (los armónicos hasta ~400 Hz); nada por encima de 1 kHz.
    //  El intervalo lub-dub es de 0,29 s en reposo y 0,21 s con el corazón acelerado (> 105 ppm), como en la realidad (la
    //  diástole se acorta). Hay dos tomas de cada con pequeñas diferencias (tono y fuerza ±4 %).
    public const int HeartVariants = 4;
    public const float HeartFastBpm = 105f;
    public static AudioClip CreateHeartbeat() { return CreateHeartbeatVariant(0); }
    public static AudioClip CreateHeartbeatVariant(int k)
    {
        const int SR = 22050;
        float gap = k < 2 ? 0.29f : 0.21f;
        int n = Mathf.RoundToInt(SR * (k < 2 ? 0.48f : 0.40f));
        float[] d = new float[n];
        System.Random r = new System.Random(100 + k);
        System.Func<float> j = () => 1f + 0.04f * ((float)r.NextDouble() * 2f - 1f);
        float[,] th = { { 0.004f, 46f * j(), 0.050f, 1.0f, 1f, 140f }, { 0.028f, 52f * j(), 0.035f, 0.45f, 2f, 150f }, { 0.004f + gap, 60f * j(), 0.034f, 0.72f * j(), 3f, 175f } };
        for (int q = 0; q < 3; q++)
        {
            float[] v = Thump(th[q, 1], th[q, 2], th[q, 3], (int)th[q, 4] + 10 * k, th[q, 5], 0.85f);
            int i0 = Mathf.RoundToInt(th[q, 0] * SR);
            for (int i = 0; i < v.Length && i0 + i < n; i++) d[i0 + i] += v[i];
        }
        float t22 = (float)System.Math.Tanh(2.2), mx = 0.001f;
        for (int i = 0; i < n; i++) { d[i] = (float)System.Math.Tanh(2.2f * d[i]) / t22; mx = Mathf.Max(mx, Mathf.Abs(d[i])); }
        for (int i = 0; i < n; i++) d[i] *= 0.9f / mx;
        for (int i = n - 300; i < n; i++) d[i] *= (n - i) / 300f;   // sin chasquido al final
        AudioClip c = AudioClip.Create("Heartbeat_" + k, n, 1, SR, false); c.SetData(d, 0);
        return c;
    }
    // un golpe del corazón (ver arriba): tono grave con caída + ruido de tejido muy grave + resonancia del pecho
    private static float[] Thump(float f0, float tau, float amp, int seed, float kf, float ka)
    {
        const int SR = 22050; int n = Mathf.RoundToInt(0.25f * SR);
        float[] y = new float[n]; System.Random rng = new System.Random(seed);
        float a = Mathf.Exp(-2f * Mathf.PI * 220f / SR), z = 0f, z2 = 0f, ym = 1e-6f;
        for (int i = 0; i < n; i++)
        {   // ruido gaussiano (Box-Muller) con paso bajo de 2 polos
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            float g = (float)(System.Math.Sqrt(-2.0 * System.Math.Log(u1)) * System.Math.Cos(2.0 * System.Math.PI * u2));
            z = (1f - a) * g + a * z; z2 = (1f - a) * z + a * z2; y[i] = z2; ym = Mathf.Max(ym, Mathf.Abs(z2));
        }
        float[] o = new float[n]; double ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float f = f0 * (1f + 0.7f * Mathf.Exp(-t / 0.035f)); ph += f / (double)SR;
            float env = (1f - Mathf.Exp(-t / 0.006f)) * Mathf.Exp(-t / tau);
            float nenv = (1f - Mathf.Exp(-t / 0.003f)) * Mathf.Exp(-t / (tau * 0.5f));
            float kenv = (1f - Mathf.Exp(-t / 0.002f)) * Mathf.Exp(-t / (tau * 0.55f));
            float knock = ka * kenv * Mathf.Sin(2f * Mathf.PI * kf * t * (1f + 0.25f * Mathf.Exp(-t / 0.02f)));
            o[i] = amp * (env * (float)System.Math.Sin(2.0 * System.Math.PI * ph) + 0.45f * nenv * y[i] / ym + knock);
        }
        return o;
    }
}
