using UnityEngine;

// Objeto flotante simple: flotabilidad respecto a la superficie del WaterVolume (con olas), amortiguación en el agua,
// par de estabilización y empujones de las ondas / del jugador.
[RequireComponent(typeof(Rigidbody))]
public class FloatingDebris : MonoBehaviour
{
    public WaterVolume water;
    public float halfHeight = 0.2f;
    public float density = 1.6f;        // >1: flota con 1/density de su altura sumergida
    public float waterDrag = 2.2f;
    public float stabilise = 5f;

    private Rigidbody rb;
    public float Submerged { get; private set; }

    private void Awake() { rb = GetComponent<Rigidbody>(); }

    private void FixedUpdate()
    {
        if (water == null || rb == null) return;
        Vector3 p = transform.position;
        float surf = water.SurfaceHeightAt(p);
        float bottom = p.y - halfHeight;
        float sub = Mathf.Clamp01((surf - bottom) / (2f * halfHeight));
        Submerged = sub;
        if (sub <= 0f) return;
        rb.AddForce(Vector3.up * (-Physics.gravity.y) * rb.mass * density * sub, ForceMode.Force);
        rb.AddForce(-rb.velocity * waterDrag * sub * rb.mass * 0.5f, ForceMode.Force);
        rb.AddTorque(-rb.angularVelocity * 0.8f * sub * rb.mass, ForceMode.Force);
        Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
        rb.AddTorque(axis * stabilise * sub * rb.mass, ForceMode.Force);
    }

    // Empujón de una onda cercana
    public void Nudge(Vector3 dirFlat, float amount)
    {
        if (rb == null) return;
        rb.AddForce(dirFlat * amount * rb.mass * 1.8f, ForceMode.Impulse);
        rb.AddTorque(Vector3.Cross(Vector3.up, dirFlat) * amount * rb.mass * 0.5f, ForceMode.Impulse);
    }
}
