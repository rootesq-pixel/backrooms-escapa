using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Efecto de cámara VHS / camcorder (OnRenderImage + Graphics.Blit con un material creado en runtime a partir del shader
// 'Resources/VHS/VhsPost'). Si el shader no carga o no es compatible, se usa un fallback con superposiciones de UI
// (scanlines, ruido y viñeta). Intensidad = ajuste del jugador + "glitch" por cercanía del monstruo.
// Incluye siseo de cinta y filtro paso-bajo del audio, más fuertes cuanto más cerca está el monstruo.
[RequireComponent(typeof(Camera))]
public class VhsEffect : MonoBehaviour
{
    public const string ShaderResource = "VHS/VhsPost";

    public static VhsEffect Active { get; private set; }

    public bool EffectEnabled = true;
    [Range(0f, 1f)] public float intensityOverride = -1f;   // <0 = usar GameSettings
    public bool UsingFallback { get; private set; }
    public Material EffectMaterial { get { return material; } }
    public float Glitch { get { return glitch; } }
    public float CurrentIntensity { get; private set; }
    [HideInInspector] public float ScreamLevel;      // 0..1: glitch extremo del susto de muerte (fuerza el efecto aunque el VHS esté apagado)
    private RenderTexture frozen;
    private float freezeUntil;
    public bool IsFrozen { get { return Time.unscaledTime < freezeUntil; } }
    // Congela la imagen unos instantes (stutter de cinta)
    public void Freeze(float seconds) { freezeUntil = Time.unscaledTime + seconds; }
    [HideInInspector] public float extraMuffle;
    [HideInInspector] public float sanityGlitch;   // 0..1: la poca cordura añade interferencia   // 0..1: filtro paso-bajo extra (bajo el agua)

    private Material material;
    private Shader shader;
    private float glitch, spike;
    private AudioSource hiss;
    private AudioClip hissClip, humClip;
    private AudioSource hum;
    private AudioLowPassFilter lowPass;
    private Transform monsterTarget;
    private GameObject fallbackCanvas;
    private RawImage fbScan, fbNoise, fbVignette;
    private Texture2D scanTex, noiseTex, vigTex;

    // Carga el shader (Resources) y crea un material; null si falla
    public static Material CreateMaterial(out Shader sh)
    {
        sh = Resources.Load<Shader>(ShaderResource);
        if (sh == null) sh = Shader.Find("Hidden/BackroomsVHS");
        if (sh == null) return null;
        return new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
    }

    private void OnEnable()
    {
        Active = this;
        material = CreateMaterial(out shader);
        UsingFallback = material == null || !shader.isSupported;
        if (UsingFallback && !Application.isBatchMode) BuildFallback();
        SetupAudio();
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
        if (material != null) ProceduralAssets.SafeDestroy(material);
        material = null;
        if (frozen != null) { frozen.Release(); ProceduralAssets.SafeDestroy(frozen); frozen = null; }
        if (fallbackCanvas != null) ProceduralAssets.SafeDestroy(fallbackCanvas);
        if (lowPass != null) ProceduralAssets.SafeDestroy(lowPass);   // primero el filtro (depende del AudioSource)
        if (hiss != null) ProceduralAssets.SafeDestroy(hiss);
        if (hum != null) ProceduralAssets.SafeDestroy(hum);
        ProceduralAssets.SafeDestroy(hissClip);
        ProceduralAssets.SafeDestroy(humClip);
        ProceduralAssets.SafeDestroy(scanTex); ProceduralAssets.SafeDestroy(noiseTex); ProceduralAssets.SafeDestroy(vigTex);
    }

    public void SetMonsterTarget(Transform t) { monsterTarget = t; }

    // Pico de interferencia (susto, daño...)
    public void Spike(float amount) { spike = Mathf.Max(spike, Mathf.Clamp01(amount)); }

    private float UserIntensity()
    {
        if (!EffectEnabled) return 0f;
        if (intensityOverride >= 0f) return intensityOverride;
        return GameSettings.VhsOn ? GameSettings.VhsIntensity : 0f;
    }

    private void SetupAudio()
    {
        AudioListener al = GetComponent<AudioListener>();
        if (al == null) return;
        lowPass = GetComponent<AudioLowPassFilter>();
        if (lowPass == null) lowPass = gameObject.AddComponent<AudioLowPassFilter>();
        lowPass.cutoffFrequency = 22000f;
        hissClip = ProceduralAudio.CreateTapeHiss();
        hiss = gameObject.AddComponent<AudioSource>();
        hiss.clip = hissClip;
        hiss.loop = true;
        hiss.spatialBlend = 0f;
        hiss.volume = 0f;
        hiss.bypassEffects = true;
        hiss.playOnAwake = false;
        hiss.Play();
        humClip = ProceduralAudio.CreateTapeHum();
        hum = gameObject.AddComponent<AudioSource>();
        hum.clip = humClip; hum.loop = true; hum.spatialBlend = 0f; hum.volume = 0f; hum.bypassEffects = true; hum.playOnAwake = false;
        hum.Play();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        // Glitch por proximidad de monstruos
        float target = 0f;
        List<MonsterAI> ms = MonsterAI.Instances;
        Vector3 p = transform.position;
        for (int i = 0; i < ms.Count; i++)
        {
            if (ms[i] == null) continue;
            float d = Vector3.Distance(p, ms[i].transform.position);
            float g = Mathf.Clamp01(1f - d / 14f);
            if (ms[i].CurrentState == MonsterAI.State.Chase) g = Mathf.Max(g, Mathf.Clamp01(1.2f - d / 20f) * 0.8f);
            if (g > target) target = g;
        }
        target = target * target;
        spike = Mathf.MoveTowards(spike, 0f, dt * 1.2f);
        float tgt = Mathf.Max(Mathf.Max(target, spike), sanityGlitch);
        glitch = Mathf.Lerp(glitch, tgt, 1f - Mathf.Exp(-6f * dt));
        CurrentIntensity = UserIntensity();

        // Audio: paso-bajo + siseo
        if (lowPass != null)
        {
            float cut = Mathf.Lerp(22000f, 7500f, CurrentIntensity * 0.6f + glitch * 0.6f);
            cut = Mathf.Lerp(cut, 520f, Mathf.Clamp01(extraMuffle));
            lowPass.cutoffFrequency = Mathf.Lerp(lowPass.cutoffFrequency, cut, 1f - Mathf.Exp(-5f * dt));
        }
        if (hiss != null)
        {
            hiss.volume = AudioMix.Hiss(CurrentIntensity, glitch) * (EffectEnabled ? 1f : 0f);
            if (hum != null) hum.volume = AudioMix.Hum(CurrentIntensity) * (EffectEnabled ? 1f : 0f);
        }

        UpdateFallback();
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        float I = Mathf.Max(CurrentIntensity, ScreamLevel * 0.9f);
        if (material == null || UsingFallback || I <= 0.001f)
        {
            Graphics.Blit(src, dst);
            return;
        }
        if (IsFrozen)
        {
            if (frozen == null)
            {
                frozen = new RenderTexture(src.width, src.height, 0, src.format);
                Graphics.Blit(src, frozen);
            }
            src = frozen;
        }
        else if (frozen != null) { frozen.Release(); Destroy(frozen); frozen = null; }
        material.SetFloat("_Scream", ScreamLevel);
        material.SetFloat("_Intensity", I);
        material.SetFloat("_Glitch", glitch);
        material.SetFloat("_VhsTime", Time.unscaledTime % 1000f);
        Graphics.Blit(src, dst, material);
    }

    // ------------------------------------------------------------------
    // Fallback con UI si el shader no está disponible
    // ------------------------------------------------------------------
    private void BuildFallback()
    {
        fallbackCanvas = new GameObject("VhsFallbackCanvas");
        Canvas c = fallbackCanvas.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 40;

        scanTex = new Texture2D(1, 4, TextureFormat.RGBA32, false);
        scanTex.SetPixels32(new[] { new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 70), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 20) });
        scanTex.wrapMode = TextureWrapMode.Repeat; scanTex.filterMode = FilterMode.Point; scanTex.Apply();
        noiseTex = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        Color32[] px = new Color32[128 * 128];
        for (int i = 0; i < px.Length; i++) { byte v = (byte)Random.Range(0, 255); px[i] = new Color32(v, v, v, 40); }
        noiseTex.SetPixels32(px); noiseTex.wrapMode = TextureWrapMode.Repeat; noiseTex.Apply();
        vigTex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Color32[] vp = new Color32[64 * 64];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        {
            float dx = x / 63f * 2f - 1f, dy = y / 63f * 2f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
            vp[y * 64 + x] = new Color32(0, 0, 0, (byte)(Mathf.SmoothStep(0.4f, 1f, r) * 150f));
        }
        vigTex.SetPixels32(vp); vigTex.wrapMode = TextureWrapMode.Clamp; vigTex.Apply();

        fbScan = MakeRaw("Scan", scanTex);
        fbNoise = MakeRaw("Noise", noiseTex);
        fbVignette = MakeRaw("Vig", vigTex);
    }

    private RawImage MakeRaw(string n, Texture2D tex)
    {
        GameObject go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(fallbackCanvas.transform, false);
        RawImage ri = go.AddComponent<RawImage>();
        ri.texture = tex; ri.raycastTarget = false;
        RectTransform rt = ri.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return ri;
    }

    private void UpdateFallback()
    {
        if (fallbackCanvas == null) return;
        float I = CurrentIntensity;
        fallbackCanvas.SetActive(I > 0.001f);
        if (fbScan != null)
        {
            fbScan.uvRect = new Rect(0f, 0f, 1f, Screen.height / 4f);
            Color c = fbScan.color; c.a = Mathf.Clamp01(I * 1.1f); fbScan.color = c;
        }
        if (fbNoise != null)
        {
            fbNoise.uvRect = new Rect(Random.value, Random.value, Screen.width / 128f, Screen.height / 128f);
            Color c = fbNoise.color; c.a = Mathf.Clamp01(I * 0.5f + glitch * 0.8f); fbNoise.color = c;
        }
        if (fbVignette != null)
        {
            Color c = fbVignette.color; c.a = Mathf.Clamp01(0.4f + I * 0.6f); fbVignette.color = c;
        }
    }
}
