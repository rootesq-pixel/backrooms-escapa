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
    // v7.9.0: latido por amenaza (todos los niveles con monstruos, no el Nivel 114): en calma no suena; con un monstruo cerca
    // empieza lento y bajo, y se acelera y sube cuanto más cerca está o si te persigue. Local de cada jugador.
    public const float HeartMinBpm = 66f, HeartMaxBpm = 150f, HeartRange = 30f;
    public float Threat01 { get; private set; }       // 0 calma … 1 encima / persiguiéndote
    public float Bpm { get; private set; }
    public float HeartVolume { get; private set; }
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
        heartClip = CreateHeartbeat();
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
        if (heartClip != null) ProceduralAssets.SafeDestroy(heartClip);
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
        HeartVolume = scare || Threat01 < 0.06f ? 0f : AudioMix.A(Mathf.Lerp(0.10f, 0.55f, Threat01 * Threat01));
        ChaseMuffle01 = Mathf.MoveTowards(ChaseMuffle01, scare ? 0f : (Chased ? Mathf.Clamp01(0.4f + 0.6f * Threat01) : 0f), dt * (scare ? 6f : 1.2f));
        if (heart != null)
        {
            beatPhase += dt * Bpm / 60f;
            if (beatPhase >= 1f)
            {
                beatPhase -= Mathf.Floor(beatPhase);
                if (HeartVolume > 0.001f)
                {
                    heart.pitch = Mathf.Lerp(0.96f, 1.12f, Threat01);   // casi sin cambio de timbre: el ritmo lo da el intervalo
                    heart.PlayOneShot(heartClip, HeartVolume); Beats++;
                }
            }
        }
    }

    // RES teñida: amarillo verdoso normal → rojo anaranjado con adrenalina
    public static Color Tint(Color normal) { Adrenaline a = Local; return a == null ? normal : Color.Lerp(normal, new Color(1f, 0.42f, 0.22f), a.Level01); }

    // un latido (lub-dub) suelto: se dispara a su ritmo (v7.9.0; antes un bucle de 2 latidos a 72 ppm)
    public static AudioClip CreateHeartbeat()
    {
        const int SR = 22050; int n = Mathf.RoundToInt(SR * 0.40f);
        float[] d = new float[n];
        for (int b = 0; b < 1; b++)
        {
            float t0 = 0f;
            for (int k = 0; k < 2; k++)
            {
                float tk = t0 + k * 0.17f, amp = k == 0 ? 1f : 0.7f, f = k == 0 ? 52f : 64f;
                int i0 = Mathf.RoundToInt(tk * SR), len = Mathf.RoundToInt(0.16f * SR);
                for (int i = 0; i < len && i0 + i < n; i++)
                {
                    float t = i / (float)SR;
                    float env = Mathf.Exp(-t * 28f) * (1f - Mathf.Exp(-t * 400f));
                    d[i0 + i] += amp * env * (Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2.1f * t));
                }
            }
        }
        float mx = 0.001f; for (int i = 0; i < n; i++) mx = Mathf.Max(mx, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] *= 0.8f / mx;
        AudioClip c = AudioClip.Create("Heartbeat", n, 1, SR, false); c.SetData(d, 0);
        return c;
    }
}
