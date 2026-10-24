using UnityEngine;

// Parpadeo aleatorio para luces de techo (y su panel emisivo).
// Opcionalmente controla un AudioSource de zumbido ("buzz") cuyo volumen sigue a la luz.
public class FlickeringLight : MonoBehaviour
{
    [Header("Referencias")]
    public Light targetLight;
    [Tooltip("Renderer del panel (opcional). Se le cambia el color de emisión con un MaterialPropertyBlock.")]
    public Renderer emissiveRenderer;
    public Color emissionColor = new Color(1.4f, 1.3f, 0.85f);

    [Header("Parpadeo")]
    [Tooltip("Intensidad base. Si es negativa se toma la de la luz al empezar.")]
    public float baseIntensity = -1f;
    [Range(0f, 1f)] public float minIntensityFactor = 0.05f;
    [Tooltip("Segundos entre una racha de parpadeo y la siguiente (mín, máx).")]
    public Vector2 timeBetweenFlickers = new Vector2(1.5f, 10f);
    [Tooltip("Duración de cada racha (mín, máx).")]
    public Vector2 flickerDuration = new Vector2(0.1f, 0.7f);
    [Tooltip("Tiempo medio entre cambios de intensidad dentro de una racha.")]
    public float flickerStepTime = 0.04f;
    [Range(0f, 1f)] public float blackoutChance = 0.25f;

    [Header("Modo")]
    [Tooltip("Si es false la luz no parpadea (solo se atenúa por LightCuller).")]
    public bool flickerEnabled = true;
    public float cullScale = 1f;   // v7.5.0: LightCuller ordena por distancia² × esto (<1 = prioridad: la luna de los ventanales)
    [Tooltip("Pulso lento (luces de emergencia): la intensidad oscila con un seno.")]
    public bool pulse = false;
    public float pulseSpeed = 1.6f;
    [Tooltip("Factor 0..1 que aplica LightCuller (solo las luces cercanas están realmente encendidas).")]
    [Range(0f, 1f)] public float externalFade = 1f;

    [Header("Zumbido (opcional)")]
    public AudioSource buzz;
    public float buzzVolume = 0.25f;
    [HideInInspector] public float buzzGateTarget, buzzGate;   // lo gobierna AudioBuzzLimiter (solo suenan los más cercanos)

    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private MaterialPropertyBlock block;
    private float factor = 1f;
    private float appliedFactor = -1f;
    private float nextFlickerTimer;
    private float flickerTimer;
    private float stepTimer;
    private bool flickering;
    private float phaseOffset;
    private float appliedFade = -1f;

    // La inicialización va en Start porque el generador asigna los campos justo después de AddComponent
    private void Start()
    {
        if (targetLight == null) targetLight = GetComponent<Light>();
        if (baseIntensity < 0f && targetLight != null) baseIntensity = targetLight.intensity;
        if (baseIntensity < 0f) baseIntensity = 1f;

        block = new MaterialPropertyBlock();
        phaseOffset = Random.Range(0f, 6.28f);
        ScheduleNextFlicker();
        // Primer valor: reparte el inicio para que no parpadeen todas a la vez
        nextFlickerTimer = Random.Range(0f, timeBetweenFlickers.y);
        Apply(1f);
    }

    private void OnEnable() { AudioBuzzLimiter.Register(this); }
    private void OnDisable() { AudioBuzzLimiter.Unregister(this); }

    private void UpdateBuzzGate(float dt)
    {
        if (buzz == null) return;
        AudioBuzzLimiter.Tick();
        buzzGate = Mathf.MoveTowards(buzzGate, buzzGateTarget, dt * 2f);
        if (buzzGate > 0.001f)
        {
            if (!buzz.isPlaying) buzz.Play();
            buzz.volume = AudioMix.Buzz(buzzVolume * Mathf.Lerp(0.4f, 1f, Mathf.Clamp01(factor))) * buzzGate;
        }
        else if (buzz.isPlaying) buzz.Stop();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        UpdateBuzzGate(dt);

        if (!flickerEnabled)
        {
            factor = 1f;
        }
        else if (!flickering)
        {
            nextFlickerTimer -= dt;
            if (nextFlickerTimer <= 0f)
            {
                flickering = true;
                flickerTimer = Random.Range(flickerDuration.x, flickerDuration.y);
                stepTimer = 0f;
            }
        }
        else
        {
            flickerTimer -= dt;
            stepTimer -= dt;

            if (stepTimer <= 0f)
            {
                stepTimer = Random.Range(flickerStepTime * 0.5f, flickerStepTime * 1.5f);
                factor = (Random.value < blackoutChance) ? 0f : Random.Range(minIntensityFactor, 1f);
            }

            if (flickerTimer <= 0f)
            {
                flickering = false;
                factor = 1f;
                ScheduleNextFlicker();
            }
        }

        float f = factor;
        if (pulse)
        {
            f *= 0.55f + 0.45f * Mathf.Sin(Time.time * pulseSpeed + phaseOffset);
        }
        Apply(f);
    }

    // v7.2.0 (ajuste de capturas): escala la intensidad base respecto a la original
    float origBase = -1f;
    public void ScaleBase(float s) { if (origBase < 0f) origBase = baseIntensity; baseIntensity = origBase * s; appliedFactor = -1f; }

    private void ScheduleNextFlicker()
    {
        nextFlickerTimer = Random.Range(timeBetweenFlickers.x, timeBetweenFlickers.y);
    }

    // Aplica el factor (0..1) a la luz, al panel emisivo y al zumbido solo si ha cambiado
    private void Apply(float f)
    {
        if (Mathf.Approximately(f, appliedFactor) && Mathf.Approximately(externalFade, appliedFade))
        {
            return;
        }
        bool factorChanged = !Mathf.Approximately(f, appliedFactor);
        appliedFactor = f;
        appliedFade = externalFade;

        if (targetLight != null)
        {
            // La luz real solo está activa si LightCuller la deja (externalFade > 0)
            targetLight.intensity = baseIntensity * f * externalFade;
            bool on = externalFade > 0.01f && f > 0.001f;
            if (targetLight.enabled != on) targetLight.enabled = on;
        }

        if (emissiveRenderer != null && factorChanged)
        {
            block.SetColor(EmissionColorId, emissionColor * f);
            emissiveRenderer.SetPropertyBlock(block);
        }

        if (buzz != null)
        {
            buzz.pitch = Mathf.Lerp(0.9f, 1.05f, f);
        }
    }
}
