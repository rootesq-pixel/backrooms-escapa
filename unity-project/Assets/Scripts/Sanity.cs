using UnityEngine;

// Cordura del jugador (0..100). Baja con monstruos cerca, a oscuras (linterna apagada) y bajo el agua; sube despacio con la linterna
// encendida y lejos de peligro. Poca cordura => más interferencia VHS. El agua de almendras la restaura.
// Se conserva al cambiar de nivel (igual que el inventario) y se revierte al reintentar.
public class Sanity : MonoBehaviour
{
    public static Sanity Instance { get; private set; }
    public static float Carry = 100f;
    private float startOfLevel = 100f;

    public float value = 100f;
    public Flashlight flashlight;
    public FirstPersonController fpc;
    public VhsEffect vhs;
    public float Sanity01 { get { return Mathf.Clamp01(value / 100f); } }
    public float DrainRate { get; private set; }

    private void Awake()
    {
        Instance = this;
        value = Carry; startOfLevel = Carry;
    }

    private void OnDestroy()
    {
        if (vhs != null) vhs.sanityGlitch = 0f;
        if (Instance == this) Instance = null;
    }

    public void Add(float amount) { value = Mathf.Clamp(value + amount, 0f, 100f); }
    public void Commit() { Carry = value; }
    public void Revert() { Carry = startOfLevel; }
    public static void ResetAll() { Carry = 100f; }
    public static bool NoDrainLevel { get { return DesertWorld.Active != null; } }

    // v7.0.2: miedo por cercanía. Empieza a ~15 m, es máximo a ≤5 m y se dobla si lo estás mirando (con línea de visión).
    //   El mímico disfrazado inquieta poco; revelado, como un monstruo. La aparición de El Vigía en el horizonte resta un poco.
    //   Lejos del peligro se recupera despacio (tras unos segundos). Todo es local de cada jugador (también en clientes MP).
    public const float FearStart = 15f, FearFull = 5f, FearRate = 5f, LookMult = 2f, DisguisedWeight = 0.25f;
    public const float ApparitionRate = 0.35f, RecoverRate = 0.35f, RecoverDelay = 3f;
    public float MonsterDrain { get; private set; }      // parte de la tasa debida a monstruos (positivo = drenaje)
    public bool LookingAtMonster { get; private set; }
    public float NearestMonster { get; private set; }
    public float NearGlitch { get; private set; }
    public static float FearFactor(float d) { return d >= FearStart ? 0f : d <= FearFull ? 1f : Mathf.SmoothStep(0f, 1f, (FearStart - d) / (FearStart - FearFull)); }
    private float sinceThreat = 99f, losTimer;
    private MonsterAI losTarget; private bool losOk;

    private Transform View { get { Camera c = Camera.main; return c != null ? c.transform : transform; } }

    private bool LooksAt(Transform view, Vector3 p, float maxAngle)
    {
        Vector3 to = p - view.position;
        return to.sqrMagnitude > 0.01f && Vector3.Angle(view.forward, to) < maxAngle;
    }

    private bool ClearLine(Transform view, MonsterAI m)
    {
        Vector3 target = FieldWrap.Image(m.transform.position, view.position) + Vector3.up * Mathf.Min(1.5f, m.eyeHeight * 0.8f);   // v7.0.4: imagen más cercana
        RaycastHit hit;
        if (!Physics.Linecast(view.position, target, out hit, ~0, QueryTriggerInteraction.Ignore)) return true;
        return hit.transform != null && (hit.transform.IsChildOf(m.transform) || hit.transform.IsChildOf(transform));
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float rate = 0f;
        Transform view = View;

        float nearest = 999f, worst = 0f; bool looking = false; MonsterAI worstM = null;
        System.Collections.Generic.List<MonsterAI> ms = MonsterAI.Instances;
        for (int i = 0; i < ms.Count; i++)
        {
            MonsterAI m = ms[i];
            if (m == null || !m.isActiveAndEnabled) continue;
            float d = FieldWrap.Distance(m.transform.position, transform.position);
            if (d < nearest) nearest = d;
            float f = FearFactor(d);
            if (f <= 0f) continue;
            if (m.Disguised) f *= DisguisedWeight;
            if (f > worst) { worst = f; worstM = m; }
        }
        // línea de visión solo con el más temible, y no cada fotograma
        losTimer -= dt;
        if (worstM != losTarget || losTimer <= 0f) { losTarget = worstM; losOk = worstM != null && ClearLine(view, worstM); losTimer = 0.2f; }
        if (worstM != null && losOk && LooksAt(view, FieldWrap.Image(worstM.transform.position, view.position) + Vector3.up, 35f)) { looking = true; worst *= LookMult; }
        float monster = worst * FearRate;

        FieldWatcherApparition ap = FieldWatcherApparition.Active;
        if (ap != null && ap.Visible && ap.model != null)
            monster += LooksAt(view, ap.model.position + Vector3.up * 2f, 20f) ? ApparitionRate : ApparitionRate * 0.3f;

        NearestMonster = nearest; LookingAtMonster = looking; MonsterDrain = monster;
        rate -= monster;
        bool lightOn = flashlight != null && flashlight.IsOn;
        if (!lightOn) rate -= 0.9f;
        if (fpc != null && fpc.IsUnderwater) rate -= 0.6f;
        // v7.7.1: en el Nivel 114 (desierto, sin entidades) la cordura nunca baja: ni la oscuridad, ni la linterna apagada, ni el clima
        if (NoDrainLevel) { rate = 0f; MonsterDrain = 0f; sinceThreat = RecoverDelay; }
        if (rate < 0f) sinceThreat = 0f; else sinceThreat += dt;
        if (rate == 0f && sinceThreat >= RecoverDelay) rate = RecoverRate;   // luz encendida y sin peligro: se recupera despacio
        DrainRate = rate;
        value = Mathf.Clamp(value + rate * dt, 0f, 100f);

        if (vhs == null && Camera.main != null) vhs = Camera.main.GetComponent<VhsEffect>();
        // interferencia: poca cordura (desde el 60 %) + un pulso mientras el miedo actúa
        float low = 1f - Sanity01;
        float lowG = low > 0.4f ? Mathf.Pow((low - 0.4f) / 0.6f, 1.3f) * 0.5f : 0f;
        float tgtNear = Mathf.Clamp01(monster / (FearRate * LookMult)) * (0.12f + 0.08f * Mathf.Sin(Time.time * 9f));
        NearGlitch = Mathf.MoveTowards(NearGlitch, tgtNear, dt * 0.6f);
        if (vhs != null) vhs.sanityGlitch = Mathf.Max(lowG, NearGlitch);
    }

}
