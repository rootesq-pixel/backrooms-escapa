using UnityEngine;

// Pista visual MUY sutil de la puerta de salida: un pequeño LED verde de emergencia y una luz verde tenue
// que solo existen cuando el jugador está a menos de 'visibleDistance' metros (~8 m).
// No hay texto, flecha ni indicador de distancia/dirección.
public class ExitDoorCue : MonoBehaviour
{
    public Transform viewer;
    public Renderer ledRenderer;
    public Light glow;
    public float visibleDistance = 8f;
    public float glowIntensity = 0.45f;

    private Material ledMaterial;
    private float time;

    private void Start()
    {
        time = Random.Range(0f, 10f);
    }

    private void Update()
    {
        if (viewer == null)
        {
            Camera cam = Camera.main;
            if (cam != null) viewer = cam.transform;
            if (viewer == null) return;
        }

        float d = Vector3.Distance(viewer.position, transform.position);
        bool near = d < visibleDistance;
        time += Time.deltaTime;

        if (ledRenderer != null && ledRenderer.enabled != near) ledRenderer.enabled = near;
        if (glow != null)
        {
            if (glow.enabled != near) glow.enabled = near;
            if (near)
            {
                float fade = 1f - Mathf.Clamp01((d - visibleDistance * 0.5f) / (visibleDistance * 0.5f));
                float breathe = 0.85f + 0.15f * Mathf.Sin(time * 1.7f);
                glow.intensity = glowIntensity * fade * breathe;
            }
        }
    }
}
