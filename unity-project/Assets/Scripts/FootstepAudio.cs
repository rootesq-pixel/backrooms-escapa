using UnityEngine;

// Pasos según la superficie del nivel (clips generados por código), sincronizados con el balanceo de cabeza:
// el FirstPersonController avisa en cada pisada. En el nivel inundado se añade un chapoteo extra al correr.
public class FootstepAudio : MonoBehaviour
{
    public StepKind kind = StepKind.Carpet;
    public float volume = 0.45f;

    private AudioSource source;
    private AudioClip clip;
    private AudioClip splash;
    private FirstPersonController fpc;
    private bool left;

    public void Setup(StepKind k)
    {
        kind = k;
        ProceduralAssets.SafeDestroy(clip);
        ProceduralAssets.SafeDestroy(splash);
        clip = ProceduralAudio.CreateFootstep(k);
        splash = k == StepKind.Water ? ProceduralAudio.CreateSplash() : null;
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
        source.pitch = Random.Range(0.88f, 1.1f) * (fpc.IsCrouching ? 1.1f : 1f);
        WaterVolume wv = WaterVolume.Active;
        float depth = fpc.WaterDepth;
        bool inWater = wv != null && depth > 0.03f;
        // pisada sobre suelo mojado (charco): clip del nivel con menos graves + chapoteo; en agua profunda solo chapoteo
        source.PlayOneShot(clip, volume * intensity * (inWater ? Mathf.Lerp(0.8f, 0.3f, Mathf.Clamp01(depth / 0.5f)) : 1f));
        if (inWater)
        {
            if (splash == null) splash = ProceduralAudio.CreateSplash();
            source.pitch = Random.Range(0.9f, 1.25f) * Mathf.Lerp(1.1f, 0.75f, Mathf.Clamp01(depth / 0.9f));
            source.PlayOneShot(splash, volume * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(depth / 0.5f)) * intensity);
            wv.Splash(fpc.transform.position + fpc.transform.forward * 0.25f, Mathf.Clamp01(0.25f * intensity + depth * 0.5f), false);
        }
        else if (splash != null && (fpc.IsSprinting || Random.value < 0.3f))
        {
            source.pitch = Random.Range(0.9f, 1.25f);
            source.PlayOneShot(splash, volume * 0.55f * intensity);
        }
    }

    private void OnLanded(float intensity)
    {
        if (clip == null) return;
        source.pitch = 0.8f;
        source.PlayOneShot(clip, Mathf.Min(1f, volume * intensity * 1.4f));
        WaterVolume wv = WaterVolume.Active;
        if (wv != null && fpc != null && fpc.WaterDepth > 0.03f)
        {
            if (splash == null) splash = ProceduralAudio.CreateSplash();
            source.PlayOneShot(splash, volume * 1.1f);
            wv.Splash(fpc.transform.position, Mathf.Clamp01(intensity), false);
        }
        else if (splash != null) source.PlayOneShot(splash, volume * 0.8f);
    }
}
