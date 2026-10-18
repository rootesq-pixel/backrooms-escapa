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

    private void Awake()
    {
        Local = this;
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        GameObject h = new GameObject("Heartbeat"); h.transform.SetParent(transform, false);
        heartClip = CreateHeartbeat();
        heart = h.AddComponent<AudioSource>();
        heart.playOnAwake = false;
        heart.clip = heartClip; heart.loop = true; heart.spatialBlend = 0f; heart.volume = 0f;
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
        // latido: discreto, más rápido y algo más fuerte con la intensidad
        if (heart != null)
        {
            float v = AudioMix.A(0.22f) * Level01;
            heart.volume = v;
            heart.pitch = Mathf.Lerp(0.9f, 1.25f, Level01);
            if (v > 0.001f && !heart.isPlaying) heart.Play();
            else if (v <= 0.001f && heart.isPlaying) heart.Stop();
        }
    }

    // RES teñida: amarillo verdoso normal → rojo anaranjado con adrenalina
    public static Color Tint(Color normal) { Adrenaline a = Local; return a == null ? normal : Color.Lerp(normal, new Color(1f, 0.42f, 0.22f), a.Level01); }

    // latido doble (lub-dub) a ~72 ppm, 2 latidos por bucle sin costura
    public static AudioClip CreateHeartbeat()
    {
        const int SR = 22050; float beat = 60f / 72f; int n = Mathf.RoundToInt(SR * beat * 2f);
        float[] d = new float[n];
        for (int b = 0; b < 2; b++)
        {
            float t0 = b * beat;
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
