using UnityEngine;

// v7.9.0: VIDA (0-100), en todos los niveles. Daño suave y justo: la lluvia del Nivel 114 a la intemperie (la de dolor
// líquido más; a cubierto en casas, cuevas o el coche no), caídas grandes. Se recupera despacio cuando estás a salvo.
// A 0: el flujo de muerte de siempre (un jugador) o derribado/reanimar (multijugador, lo decide el anfitrión).
// Las tormentas de arena NO quitan vida (empujan: ver DesertFx). La cordura es aparte (Sanity.NoDrainLevel solo es cordura).
public class Health : MonoBehaviour
{
    public static Health Instance { get; private set; }
    public static float Carry = 100f;
    private float startOfLevel = 100f;
    public float value = 100f;
    public FirstPersonController fpc;
    public float Health01 { get { return Mathf.Clamp01(value / 100f); } }
    public float DrainRate { get; private set; }       // vida/s que se pierde ahora (positivo)
    public float RegenRate { get; private set; }
    public string LastCause { get; private set; }
    public int Zeroes { get; private set; }
    public static bool DisableForTests;

    public const float PainRainRate = 0.35f;     // dolor líquido: de 100 a 0 en ~5 min a la intemperie
    public const float AlmondRainRate = 0.10f;   // lluvia de almendras: casi nada (~17 min)
    public const float RegenDelay = 8f, Regen = 0.5f;
    public const float FallSafe = 18f, FallK = 5f;   // m/s al tocar el suelo (≈ 8 m de caída con g = 20) y vida por m/s de más
    public const float ReviveHealth = 35f;
    private float sinceHurt = 99f, levelT;
    private bool zeroSent, wasDowned; private float resendT;

    private void Awake() { Instance = this; value = Carry; startOfLevel = Carry; }
    private void Start() { if (fpc == null) fpc = GetComponent<FirstPersonController>(); if (fpc != null) fpc.Landed += OnLanded; }
    private void OnDestroy() { if (fpc != null) fpc.Landed -= OnLanded; if (Instance == this) Instance = null; }
    public void Commit() { Carry = value; }
    public void Revert() { Carry = Mathf.Max(startOfLevel, 60f); }   // al reintentar el nivel no se empieza casi sin vida
    public static void ResetAll() { Carry = 100f; }

    public void Damage(float amount, string cause)
    {
        if (amount <= 0f || DisableForTests || Downed) return;
        value = Mathf.Clamp(value - amount, 0f, 100f); sinceHurt = 0f; LastCause = cause;
    }
    public void Heal(float amount) { value = Mathf.Clamp(value + amount, 0f, 100f); }

    private static bool Downed { get { return NetSession.Active && NetSession.Instance != null && NetSession.Instance.LocalDowned; } }

    private void OnLanded(float k)
    {
        if (fpc == null || levelT < 3f || ElevatorCabin.PlayerLocked) return;
        float v = fpc.LastImpactSpeed;
        if (v > FallSafe) Damage((v - FallSafe) * FallK, "caída");
    }

    // ¿lluvia sobre el jugador? (Nivel 114, a la intemperie: no bajo techo, en cueva ni en el coche)
    public static float RainRateAt(Vector3 pos, FirstPersonController f)
    {
        DesertSky sky = DesertSky.Active; DesertFx fx = DesertFx.Active; DesertWorld dw = DesertWorld.Active;
        if (sky == null || dw == null || sky.Rain < 0.5f) return 0f;
        if (f != null && f.IsMounted) return 0f;
        if (dw.UnderRoof(pos + Vector3.up * 1.5f) || (fx != null && fx.InCave(pos))) return 0f;
        return (sky.PainK > 0.5f ? PainRainRate : AlmondRainRate) * Mathf.Clamp01((sky.Rain - 0.5f) * 2f + 0.5f);
    }

    private void Update()
    {
        float dt = Time.deltaTime; if (dt <= 0f) return;
        levelT += dt;
        GameManager gm = GameManager.Instance;
        bool playing = gm != null && gm.State == GameManager.GameState.Playing;
        bool down = Downed;
        if (wasDowned && !down) { value = Mathf.Max(value, ReviveHealth); zeroSent = false; sinceHurt = 99f; }   // reanimado
        wasDowned = down;
        if (!playing || down) { DrainRate = 0f; RegenRate = 0f; return; }
        float rain = DisableForTests ? 0f : RainRateAt(transform.position, fpc);
        DrainRate = rain;
        if (rain > 0f) Damage(rain * dt, "lluvia");
        sinceHurt += dt;
        RegenRate = sinceHurt > RegenDelay && value < 100f ? Regen : 0f;
        if (RegenRate > 0f) value = Mathf.Min(100f, value + RegenRate * dt);
        if (value <= 0f && zeroSent && NetSession.Active) { resendT -= dt; if (resendT <= 0f) zeroSent = false; }   // MP: el anfitrión aún no lo ha derribado (gracia tras reanimar): repetir
        if (value <= 0f && !zeroSent) { zeroSent = true; resendT = 2f; Zeroes++; OnZero(); }
        if (value > 0f) zeroSent = false;
    }

    public const string DeathText = "TE HAS QUEDADO SIN VIDA";
    private void OnZero()
    {
        GameManager gm = GameManager.Instance;
        if (NetSession.Active && NetSession.Instance != null) { NetSession.Instance.ReportHealthZero(); return; }
        if (gm != null) gm.PlayerKilled(LastCause == "lluvia" ? "LA LLUVIA HA PODIDO CONTIGO" : DeathText);
    }
}
