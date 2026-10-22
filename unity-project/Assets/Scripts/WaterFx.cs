using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Efectos de agua en la cámara del jugador:
//  - bajo la superficie: niebla/fondo con tinte, filtro paso-bajo (audio apagado) vía VhsEffect, burbujas y velo azul
//  - al salir del agua: gotas en la "lente" (UI) que resbalan y se secan
//  - linterna: cookie animado de cáusticas (reflejos de luz ondulantes) cuando el nivel tiene agua
public class WaterFx : MonoBehaviour
{
    public static bool CausticsEnabled = true;

    public FirstPersonController fpc;
    public Flashlight flashlight;
    public Camera cam;
    public VhsEffect vhs;

    public float Underwater01 { get; private set; }
    public float Wet01 { get { return wet; } }
    public int ActiveDrops { get; private set; }
    public bool CausticCookieActive { get { return flashlight != null && flashlight.spot != null && flashlight.spot.cookie != null; } }
    public bool BubblesEmitting { get { return bubbles != null && bubbles.emission.rateOverTime.constant > 0.5f; } }

    private Color baseFogColor, baseBg;
    private float baseFogDensity;
    private Color baseAmbient;
    private float baseSun = -1f;
    public float Depth01 { get; private set; }   // v7.3.0: mar abierto: 0 en la superficie, 1 a ~45 m
    private bool captured;
    private float wet;
    private float prevDepth;
    private bool prevUnder;

    private Canvas canvas;
    private Image tint;
    private Image[] dropImgs;
    private float[] dropLife, dropSpeed;
    private Texture2D dropTex;
    private Sprite dropSprite;
    private ParticleSystem bubbles;
    private Material bubbleMat;

    private static Texture2D[] cookies;
    private float cookieT;
    private int cookieIndex;
    private readonly List<Object> owned = new List<Object>();

    private void Start()
    {
        if (fpc == null) fpc = GetComponentInParent<FirstPersonController>();
        if (cam == null) cam = GetComponent<Camera>();
        if (flashlight == null) flashlight = GetComponent<Flashlight>();
        if (vhs == null) vhs = GetComponent<VhsEffect>();
        CaptureFog();
        BuildOverlay();
        BuildBubbles();
        if (CausticsEnabled) SetupCaustics();
    }

    private void CaptureFog()
    {
        baseFogColor = RenderSettings.fogColor;
        baseFogDensity = RenderSettings.fogDensity;
        baseAmbient = RenderSettings.ambientLight;
        baseBg = cam != null ? cam.backgroundColor : baseFogColor;
        captured = true;
    }

    private void BuildOverlay()
    {
        GameObject go = new GameObject("WaterOverlay");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 8;

        GameObject tg = new GameObject("UnderwaterTint");
        tg.transform.SetParent(go.transform, false);
        tint = tg.AddComponent<Image>();
        tint.color = new Color(0.05f, 0.3f, 0.36f, 0f);
        tint.raycastTarget = false;
        RectTransform rt = tint.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;

        dropTex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        dropTex.name = "LensDrop";
        Color32[] px = new Color32[32 * 32];
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float dx = (x + 0.5f) / 16f - 1f, dy = (y + 0.5f) / 16f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float edge = Mathf.Clamp01(1f - r);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.78f) * 6f);        // borde brillante
                float hi = Mathf.Clamp01(1f - Mathf.Sqrt((dx + 0.35f) * (dx + 0.35f) + (dy - 0.4f) * (dy - 0.4f)) * 4f); // reflejo
                float a = Mathf.Clamp01(edge * 0.35f + rim * 0.5f + hi * 0.8f);
                if (r > 1f) a = 0f;
                px[y * 32 + x] = new Color32(215, 235, 245, (byte)(a * 255f));
            }
        dropTex.SetPixels32(px);
        dropTex.Apply(false, true);
        dropSprite = Sprite.Create(dropTex, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
        owned.Add(dropTex); owned.Add(dropSprite);

        dropImgs = new Image[24];
        dropLife = new float[24];
        dropSpeed = new float[24];
        for (int i = 0; i < dropImgs.Length; i++)
        {
            GameObject dg = new GameObject("Drop" + i);
            dg.transform.SetParent(go.transform, false);
            Image im = dg.AddComponent<Image>();
            im.sprite = dropSprite;
            im.raycastTarget = false;
            im.enabled = false;
            dropImgs[i] = im;
        }
    }

    private void BuildBubbles()
    {
        Shader ps = Resources.Load<Shader>(WaterVolume.ParticleShaderResource);
        if (ps == null) return;
        GameObject g = new GameObject("Bubbles");
        g.transform.SetParent(transform, false);
        g.transform.localPosition = new Vector3(0f, -0.3f, 0.8f);
        bubbles = g.AddComponent<ParticleSystem>();
        bubbles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ParticleSystem.MainModule m = bubbles.main;
        m.loop = true; m.playOnAwake = false;
        m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.8f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.35f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.04f);
        m.gravityModifier = -0.06f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.maxParticles = 120;
        ParticleSystem.EmissionModule e = bubbles.emission; e.rateOverTime = 0f;
        ParticleSystem.ShapeModule sh = bubbles.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(2.4f, 1.2f, 2.4f);
        ParticleSystem.ColorOverLifetimeModule c = bubbles.colorOverLifetime; c.enabled = true;
        Gradient gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.55f, 0.2f), new GradientAlphaKey(0f, 1f) });
        c.color = gr;
        bubbleMat = new Material(ps);
        bubbleMat.mainTexture = dropTex;
        owned.Add(bubbleMat);
        ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = bubbleMat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        bubbles.Play();
    }

    // ------------------------------------------------------------------
    // Cáusticas: 8 fotogramas de un cookie (canal alfa) reutilizados entre niveles
    // ------------------------------------------------------------------
    private void SetupCaustics()
    {
        if (flashlight == null || flashlight.spot == null) return;
        if (WaterVolume.Active == null) return;
        if (cookies == null || cookies.Length == 0 || cookies[0] == null) cookies = BuildCookies(8, 96);
        flashlight.spot.cookie = cookies[0];
        flashlight.intensityMultiplier = 1.45f;   // el cookie quita luz de media
    }

    public static Texture2D[] BuildCookies(int frames, int n)
    {
        Texture2D[] res = new Texture2D[frames];
        for (int f = 0; f < frames; f++)
        {
            float ph = f / (float)frames * Mathf.PI * 2f;
            byte[] px = new byte[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    float w1 = Mathf.Sin(u * Mathf.PI * 6f + ph + Mathf.Sin(v * Mathf.PI * 4f + ph) * 1.3f);
                    float w2 = Mathf.Sin(v * Mathf.PI * 8f - ph + Mathf.Sin(u * Mathf.PI * 6f - ph) * 1.2f);
                    float w3 = Mathf.Sin((u + v) * Mathf.PI * 5f + ph * 2f + Mathf.Sin((u - v) * Mathf.PI * 5f - ph) * 1.1f);
                    float c = Mathf.Pow(1f - Mathf.Abs(w1), 6f) + Mathf.Pow(1f - Mathf.Abs(w2), 6f) + Mathf.Pow(1f - Mathf.Abs(w3), 6f);
                    float pat = Mathf.Clamp01(0.45f + c * 0.65f);
                    float dx = u * 2f - 1f, dy = v * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float mask = 1f - Mathf.SmoothStep(0.80f, 0.99f, r);
                    px[y * n + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(pat * mask) * 255f);
                }
            }
            Texture2D t = new Texture2D(n, n, TextureFormat.Alpha8, false);
            t.name = "Caustic" + f;
            t.LoadRawTextureData(px);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.Apply(false, true);
            res[f] = t;
        }
        return res;
    }

    // ------------------------------------------------------------------
    private void Update()
    {
        if (fpc == null) return;
        float dt = Time.deltaTime;
        bool under = fpc.IsUnderwater;
        Underwater01 = Mathf.MoveTowards(Underwater01, under ? 1f : 0f, dt * 4f);

        // salir del agua (profunda o bajo la superficie) = lente mojada
        if ((prevUnder && !under) || (prevDepth > 0.5f && fpc.WaterDepth < 0.35f)) wet = 1f;
        prevUnder = under;
        prevDepth = fpc.WaterDepth;
        wet = Mathf.MoveTowards(wet, 0f, dt * 0.11f);

        // niebla / fondo
        if (captured && Underwater01 > 0.001f || (captured && Underwater01 <= 0.001f && uwApplied))
        {
            Color uc = new Color(0.04f, 0.2f, 0.25f);
            float uwDensity = 0.22f;
            WaterVolume wvo = WaterVolume.Active;
            if (wvo != null && wvo.ocean && cam != null)
            {
                // v7.3.0: mar abierto: cuanto más hondo, más oscuro (verde azulado -> casi negro); la niebla deja ver ~10-15 m con la lámpara
                float depth = Mathf.Max(0f, wvo.RestSurfaceAt(cam.transform.position) - cam.transform.position.y);
                Depth01 = Mathf.Clamp01(depth / 45f);
                float d = Mathf.SmoothStep(0f, 1f, Depth01);
                uc = Color.Lerp(new Color(0.07f, 0.23f, 0.26f), new Color(0.003f, 0.012f, 0.02f), d);
                uwDensity = Mathf.Lerp(0.075f, 0.12f, d);
                RenderSettings.ambientLight = Color.Lerp(baseAmbient, baseAmbient * (1f - 0.93f * d), Underwater01);
                if (OceanWorld.Active != null && OceanWorld.Active.sun != null)
                {
                    if (baseSun < 0f) baseSun = OceanWorld.Active.sun.intensity;
                    OceanWorld.Active.sun.intensity = Mathf.Lerp(baseSun, baseSun * (1f - 0.97f * d), Underwater01);
                }
            }
            else Depth01 = 0f;
            RenderSettings.fogColor = Color.Lerp(baseFogColor, uc, Underwater01);
            RenderSettings.fogDensity = Mathf.Lerp(baseFogDensity, uwDensity, Underwater01);
            if (cam != null) cam.backgroundColor = Color.Lerp(baseBg, uc, Underwater01);
            uwApplied = Underwater01 > 0.001f;
            if (!uwApplied) { RenderSettings.ambientLight = baseAmbient; if (baseSun >= 0f && OceanWorld.Active != null && OceanWorld.Active.sun != null) OceanWorld.Active.sun.intensity = baseSun; }
        }
        if (tint != null) tint.color = new Color(0.05f, 0.3f, 0.36f, Underwater01 * 0.38f);
        if (vhs != null) vhs.extraMuffle = Underwater01 * 0.92f;
        if (bubbles != null)
        {
            ParticleSystem.EmissionModule e = bubbles.emission;
            e.rateOverTime = Underwater01 > 0.5f ? 16f : 0f;
        }

        UpdateDrops(dt);
        UpdateCaustics(dt);
    }
    private bool uwApplied;

    private void UpdateDrops(float dt)
    {
        if (dropImgs == null) return;
        int active = 0;
        float wantCount = wet * dropImgs.Length;
        for (int i = 0; i < dropImgs.Length; i++)
        {
            Image im = dropImgs[i];
            if (im.enabled)
            {
                dropLife[i] -= dt;
                RectTransform rt = im.rectTransform;
                Vector2 a = rt.anchorMin;
                a.y -= dropSpeed[i] * dt * (0.2f + (1f - wet) * 0.8f);
                rt.anchorMin = rt.anchorMax = a;
                Color c = im.color;
                c.a = Mathf.Clamp01(dropLife[i] * 0.8f) * Mathf.Clamp01(wet * 2f + 0.2f);
                im.color = c;
                if (dropLife[i] <= 0f || a.y < -0.05f) im.enabled = false;
                else active++;
            }
            else if (active < wantCount && Random.value < dt * 6f)
            {
                im.enabled = true;
                dropLife[i] = Random.Range(2.5f, 6f);
                dropSpeed[i] = Random.Range(0.01f, 0.06f);
                RectTransform rt = im.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(Random.value, Random.Range(0.25f, 1f));
                float s = Random.Range(14f, 46f);
                rt.sizeDelta = new Vector2(s, s * Random.Range(1f, 1.5f));
                rt.anchoredPosition = Vector2.zero;
                im.color = new Color(1f, 1f, 1f, 0.8f);
                active++;
            }
        }
        ActiveDrops = active;
    }

    private void UpdateCaustics(float dt)
    {
        if (!CausticCookieActive || cookies == null) return;
        cookieT += dt;
        if (cookieT > 0.09f)
        {
            cookieT = 0f;
            cookieIndex = (cookieIndex + 1) % cookies.Length;
            flashlight.spot.cookie = cookies[cookieIndex];
        }
    }

    // Lo usa el smoke test (simula salir del agua)
    public void ForceWet() { wet = 1f; }

    private void OnDestroy()
    {
        if (captured)
        {
            RenderSettings.fogColor = baseFogColor;
            RenderSettings.fogDensity = baseFogDensity;
            RenderSettings.ambientLight = baseAmbient;
        }
        if (flashlight != null)
        {
            flashlight.intensityMultiplier = 1f;
            if (flashlight.spot != null) flashlight.spot.cookie = null;
        }
        for (int i = 0; i < owned.Count; i++) ProceduralAssets.SafeDestroy(owned[i]);
        owned.Clear();
    }
}
