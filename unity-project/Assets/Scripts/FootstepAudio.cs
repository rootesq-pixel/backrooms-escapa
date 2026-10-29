using UnityEngine;

// Pasos según la superficie del nivel (clips generados por código), sincronizados con el balanceo de cabeza:
// el FirstPersonController avisa en cada pisada. En el nivel inundado se añade un chapoteo extra al correr.
public class FootstepAudio : MonoBehaviour
{
    public StepKind kind = StepKind.Carpet;
    public float volume = 0.45f;
    private float Vol { get { return volume * AudioMix.Sfx * AudioMix.StepMaster; } }

    private AudioSource source;
    private AudioClip clip;
    private AudioClip splash;
    private FirstPersonController fpc;
    private bool left;
    private int lastGrass = -1;
    private int lastTake = -1;
    public int RealStepsPlayed { get; private set; }   // v7.10.3: pisadas con grabación real (tests)
    private AudioSource rustleSrc;
    public float RustleLevel { get; private set; }   // v7.0: volumen actual del roce (tests)
    public int GrassStepsPlayed { get; private set; }

    public void Setup(StepKind k)
    {
        kind = k;
        ProceduralAssets.SafeDestroy(clip);
        ProceduralAssets.SafeDestroy(splash);
        clip = ProceduralAudio.CreateFootstep(k);
        splash = k == StepKind.Water ? ProceduralAudio.CreateSplash() : null;
        if (k == StepKind.Grass && rustleSrc == null) rustleSrc = GrassAudio.MakeRustleSource(gameObject, false);
    }

    // v7.0: roce continuo de la hierba según la velocidad (más al correr, casi nada agachado)
    private void Update()
    {
        if (rustleSrc == null || fpc == null) return;
        bool grass = kind == StepKind.Grass && fpc.IsGrounded && GrassAudio.OnGrass(fpc.transform.position) && fpc.InputEnabled;
        float target = grass ? GrassAudio.RustleTarget(fpc.FlatSpeed, fpc.IsSprinting, fpc.IsCrouching) : 0f;
        RustleLevel = Mathf.MoveTowards(RustleLevel, target, Time.deltaTime * (target > RustleLevel ? 3f : 1.6f));
        rustleSrc.volume = RustleLevel * GrassAudio.RustleMax * Vol / Mathf.Max(volume, 1e-3f) * 0.45f;
        rustleSrc.pitch = Mathf.Lerp(0.9f, 1.12f, Mathf.Clamp01(fpc.FlatSpeed / 6f));
    }

    // clip de la pisada actual: hierba real fuera de las casas en el Campo infinito
    private AudioClip StepClip(out bool grass)
    {
        grass = kind == StepKind.Grass && fpc != null && GrassAudio.OnGrass(fpc.transform.position);
        if (grass) { AudioClip g = GrassAudio.RandomStep(ref lastGrass); if (g != null) { GrassStepsPlayed++; return g; } grass = false; }
        AudioClip real = StepBank.Next(kind, ref lastTake);   // v7.10.3: grabación real de la superficie (5 tomas)
        if (real != null) { RealStepsPlayed++; return real; }
        return clip;
    }

    private void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
        source = gameObject.AddComponent<AudioSource>();
        source.spatialBlend = 0f;
        source.playOnAwake = false;
    }

    private void OnEnable()
    {
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        if (fpc != null) { fpc.Footstep += OnStep; fpc.Landed += OnLanded; }
    }

    private void OnDisable()
    {
        if (fpc != null) { fpc.Footstep -= OnStep; fpc.Landed -= OnLanded; }
    }

    private void OnDestroy()
    {
        ProceduralAssets.SafeDestroy(clip);
        ProceduralAssets.SafeDestroy(splash);
    }

    private void OnStep(float intensity)
    {
        if (clip == null || fpc == null || !fpc.InputEnabled) return;
        left = !left;
        source.panStereo = left ? -0.12f : 0.12f;
        bool real = StepBank.Real(kind);
        source.pitch = (real ? StepBank.Pitch() : Random.Range(0.88f, 1.1f)) * (fpc.IsCrouching ? 1.06f : 1f);
        WaterVolume wv = WaterVolume.Active;
        float depth = fpc.WaterDepth;
        bool inWater = wv != null && depth > 0.03f;
        // pisada sobre suelo mojado (charco): clip del nivel con menos graves + chapoteo; en agua profunda solo chapoteo
        bool grassStep;
        AudioClip sc = StepClip(out grassStep);
        if (grassStep) source.pitch = Random.Range(0.92f, 1.08f) * (fpc.IsCrouching ? 1.06f : 1f);
        source.PlayOneShot(sc, Vol * intensity * (real && !grassStep ? StepBank.Gain() : 1f) * (inWater ? Mathf.Lerp(0.8f, 0.3f, Mathf.Clamp01(depth / 0.5f)) : 1f));
        if (inWater)
        {
            if (splash == null) splash = ProceduralAudio.CreateSplash();
            source.pitch = Random.Range(0.9f, 1.25f) * Mathf.Lerp(1.1f, 0.75f, Mathf.Clamp01(depth / 0.9f));
            source.PlayOneShot(splash, Vol * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(depth / 0.5f)) * intensity);
            wv.Splash(fpc.transform.position + fpc.transform.forward * 0.25f, Mathf.Clamp01(0.25f * intensity + depth * 0.5f), false);
        }
        else if (splash != null && (fpc.IsSprinting || Random.value < 0.3f))
        {
            source.pitch = Random.Range(0.9f, 1.25f);
            source.PlayOneShot(splash, Vol * 0.55f * intensity);
        }
    }

    private void OnLanded(float intensity)
    {
        if (clip == null) return;
        source.pitch = StepBank.Real(kind) ? 0.88f : 0.8f;
        bool grassStep;
        source.PlayOneShot(StepClip(out grassStep), Mathf.Min(1f, Vol * intensity * 1.4f));
        WaterVolume wv = WaterVolume.Active;
        if (wv != null && fpc != null && fpc.WaterDepth > 0.03f)
        {
            if (splash == null) splash = ProceduralAudio.CreateSplash();
            source.PlayOneShot(splash, Vol * 1.1f);
            wv.Splash(fpc.transform.position, Mathf.Clamp01(intensity), false);
        }
        else if (splash != null) source.PlayOneShot(splash, Vol * 0.8f);
    }
}
