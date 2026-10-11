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

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        float rate = 0f;

        float nearest = 999f;
        System.Collections.Generic.List<MonsterAI> ms = MonsterAI.Instances;
        for (int i = 0; i < ms.Count; i++)
        {
            if (ms[i] == null) continue;
            float d = Vector3.Distance(transform.position, ms[i].transform.position);
            if (d < nearest) nearest = d;
        }
        if (nearest < 10f) rate -= (1f - nearest / 10f) * 6f;
        bool lightOn = flashlight != null && flashlight.IsOn;
        if (!lightOn) rate -= 0.9f;
        if (fpc != null && fpc.IsUnderwater) rate -= 0.6f;
        if (rate == 0f) rate = 0.45f;       // luz encendida y sin peligro: se recupera despacio
        DrainRate = rate;
        value = Mathf.Clamp(value + rate * dt, 0f, 100f);

        if (vhs == null && Camera.main != null) vhs = Camera.main.GetComponent<VhsEffect>();
        if (vhs != null)
        {
            float low = 1f - Sanity01;
            vhs.sanityGlitch = low > 0.5f ? (low - 0.5f) * 2f * 0.35f : 0f;
        }
    }
}
