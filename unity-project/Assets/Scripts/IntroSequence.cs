using System;
using UnityEngine;
using UnityEngine.UI;

// Intro de arranque (found footage, ~9,4 s, siempre antes del menú principal): pantalla negra con estática VHS, inserción de cinta
// con glitch, «REC» + código de tiempo, aviso (luces parpadeantes, sustos, sonidos fuertes), estudio («PIXEL») y título
// «BACKROOMS: ESCAPA» con parpadeo y zumbido grave; después se desvanece hacia el menú con el pasillo en vivo de fondo.
// Se puede saltar con cualquier tecla / clic pasados 2 s. Todo procedural (sin ficheros).
// Fotosensibilidad: estática suave, sin destellos de pantalla completa, parpadeos ≤ 3 por segundo; con «Susto suave» todavía menos
// (sin desplazamientos de color, estática y parpadeo mínimos, volumen más bajo).
public class IntroSequence : MonoBehaviour
{
    public static IntroSequence Current { get; private set; }
    public static bool Active { get { return Current != null; } }
    public static bool Completed { get; private set; }
    public static int PlayedCount { get; private set; }
    public static event Action Finished;

    // Línea de tiempo (s)
    public const float TInsertEnd = 0.9f, TWarn0 = 0.9f, TWarn1 = 3.7f, TStudio0 = 3.7f, TStudio1 = 5.7f, TTitle0 = 5.7f, TFade0 = 8.5f, TEnd = 9.4f;
    public const float SkipAfter = 2f, SkipFade = 0.5f;
    public const string WarningText = "ESTE JUEGO CONTIENE LUCES PARPADEANTES,\nSUSTOS Y SONIDOS FUERTES";
    public const string StudioText = "PIXEL";
    public const string TitleText = "BACKROOMS: ESCAPA";

    public enum Stage { Insert, Warning, Studio, Title, Fade, Skipping }

    // ¿debe reproducirse en este arranque? (no en pruebas automáticas del editor salvo que la prueba lo pida)
    public static bool ShouldPlay()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++) if (args[i] == "-nointro") return false;
#if UNITY_EDITOR
        if (!UnityEditor.SessionState.GetBool("intro_real", false) &&
            (UnityEditor.SessionState.GetString("smoke_phase", "") != "" || UnityEditor.SessionState.GetString("mp_role", "") != "")) return false;
#endif
        return true;
    }

    public static IntroSequence Begin(Transform parent)
    {
        if (Current != null) return Current;
        GameObject go = new GameObject("IntroSequence");
        go.transform.SetParent(parent, false);
        return go.AddComponent<IntroSequence>();
    }

    // ---- estado
    public float T { get; private set; }
    public bool Skipped { get; private set; }
    public bool Fading { get { return fading; } }
    public Stage CurrentStage { get; private set; }
    public string VisibleText { get { return mainText != null && mainText.color.a > 0.02f ? mainText.text : ""; } }
    public float Alpha { get { return group != null ? group.alpha : 1f; } }
    public float StaticAlpha { get; private set; }
    public float MaxFlickerRate { get { return maxFlicker; } }
    public bool BlocksInput { get { return group != null && group.blocksRaycasts; } }
    public string TimecodeText { get { return tcText != null ? tcText.text : ""; } }
    public bool RecVisible { get { return recText != null && recText.enabled; } }
    public bool Soft { get; private set; }
    public bool SkipHintVisible { get { return hint != null && hint.color.a > 0.05f; } }
    public int AudioSourceCount { get { return GetComponents<AudioSource>().Length; } }

    private Canvas canvas;
    private CanvasGroup group;
    private RawImage staticImg, scanImg;
    private Image bar, black;
    private Text mainText, ghostR, ghostC, subText, recText, playText, tcText, hint;
    private Texture2D staticTex, scanTex;
    private Color32[] staticPix;
    private AudioSource one, hum;
    private AudioClip clunk, hit, humClip;
    private bool fading, skipRequested, playedHit, playedClunk2;
    private float fadeT, fadeLen, nextStatic, maxFlicker, lastFlickerState = -1f, flickerChangeTimer, flickerChanges, flickerWindow;
    private const int SW = 192, SH = 108;

    private void Awake()
    {
        Current = this;
        Completed = false;
        PlayedCount++;
        Build();
        BuildAudio();
        Evaluate(0f);
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        ProceduralAssets.SafeDestroy(staticTex); ProceduralAssets.SafeDestroy(scanTex);
        ProceduralAssets.SafeDestroy(clunk); ProceduralAssets.SafeDestroy(hit); ProceduralAssets.SafeDestroy(humClip);
        if (canvas != null) ProceduralAssets.SafeDestroy(canvas.gameObject);
    }

    // ------------------------------------------------------------------ construcción
    private static RectTransform Stretch(GameObject go)
    {
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return rt;
    }

    private void Build()
    {
        canvas = UiStyle.NewOverlayCanvas(transform, "IntroCanvas", 110);
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = true; group.interactable = false;
        GameObject b = new GameObject("Black", typeof(RectTransform));
        b.transform.SetParent(canvas.transform, false); Stretch(b);
        black = b.AddComponent<Image>(); black.color = Color.black; black.raycastTarget = true;

        staticTex = new Texture2D(SW, SH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        staticPix = new Color32[SW * SH];
        GameObject s = new GameObject("Static", typeof(RectTransform));
        s.transform.SetParent(canvas.transform, false); Stretch(s);
        staticImg = s.AddComponent<RawImage>(); staticImg.texture = staticTex; staticImg.raycastTarget = false;
        RefreshStatic();

        scanTex = new Texture2D(1, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
        scanTex.SetPixels32(new[] { new Color32(0, 0, 0, 90), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0) });
        scanTex.Apply(false);
        GameObject sc = new GameObject("Scanlines", typeof(RectTransform));
        sc.transform.SetParent(canvas.transform, false); Stretch(sc);
        scanImg = sc.AddComponent<RawImage>(); scanImg.texture = scanTex; scanImg.uvRect = new Rect(0f, 0f, 1f, 270f); scanImg.raycastTarget = false;

        bar = UiStyle.Rect(canvas.transform, "RollBar", 0f, 0f, 2400f, 90f, new Color(1f, 1f, 1f, 0.05f));

        ghostR = UiStyle.Label(canvas.transform, "", 74, TextAnchor.MiddleCenter, 0f, 30f, 1700f, 360f, new Color(1f, 0.1f, 0.1f, 0f), FontStyle.Bold);
        ghostC = UiStyle.Label(canvas.transform, "", 74, TextAnchor.MiddleCenter, 0f, 30f, 1700f, 360f, new Color(0.1f, 0.9f, 1f, 0f), FontStyle.Bold);
        mainText = UiStyle.Label(canvas.transform, "", 74, TextAnchor.MiddleCenter, 0f, 30f, 1700f, 360f, new Color(0.96f, 0.93f, 0.8f, 0f), FontStyle.Bold);
        subText = UiStyle.Label(canvas.transform, "", 36, TextAnchor.MiddleCenter, 0f, -190f, 1500f, 90f, new Color(0.7f, 0.66f, 0.5f, 0f));

        recText = UiStyle.Label(canvas.transform, "● REC", 44, TextAnchor.MiddleLeft, -720f, 470f, 360f, 60f, new Color(0.95f, 0.15f, 0.12f, 0.95f));
        playText = UiStyle.Label(canvas.transform, "PLAY ►", 36, TextAnchor.MiddleRight, 720f, 470f, 360f, 60f, new Color(0.96f, 0.96f, 0.92f, 0.9f));
        tcText = UiStyle.Label(canvas.transform, "00:00:00:00", 44, TextAnchor.MiddleRight, 640f, -470f, 520f, 60f, new Color(0.96f, 0.96f, 0.92f, 0.9f));
        hint = UiStyle.Label(canvas.transform, "Pulsa cualquier tecla para saltar", 28, TextAnchor.MiddleCenter, 0f, -470f, 900f, 50f, new Color(0.6f, 0.58f, 0.5f, 0f));
        recText.enabled = false; playText.enabled = false;
    }

    private void BuildAudio()
    {
        clunk = IntroAudio.CreateClunk();
        hit = IntroAudio.CreateTitleHit();
        humClip = ProceduralAudio.CreateTapeHum();
        one = gameObject.AddComponent<AudioSource>();
        one.spatialBlend = 0f; one.playOnAwake = false; one.loop = false;
        hum = gameObject.AddComponent<AudioSource>();
        hum.clip = humClip; hum.spatialBlend = 0f; hum.playOnAwake = false; hum.loop = true; hum.volume = 0f;
        hum.Play();
        one.PlayOneShot(clunk, AudioMix.S(IntroAudio.ClunkGain));
    }

    // ------------------------------------------------------------------ utilidades de la línea de tiempo
    public static Stage StageAt(float t, bool skipping)
    {
        if (skipping) return Stage.Skipping;
        if (t < TInsertEnd) return Stage.Insert;
        if (t < TWarn1) return Stage.Warning;
        if (t < TStudio1) return Stage.Studio;
        if (t < TFade0) return Stage.Title;
        return Stage.Fade;
    }

    private static float Pulse(float t, float a, float b, float fin, float fout)
    {
        return Mathf.Clamp01((t - a) / fin) * Mathf.Clamp01((b - t) / fout);
    }

    private static float Hash(int i) { return Noise.Hash01(i, 77, 5); }

    public void RequestSkip() { skipRequested = true; }

    // ------------------------------------------------------------------ bucle
    private void Update()
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);   // los tirones de carga del menú no se comen la intro
        Soft = GameSettings.SoftScare;
        if (!fading)
        {
            T += dt;
            bool pressed = Input.anyKeyDown || skipRequested;
            skipRequested = false;
            if (pressed && T >= SkipAfter) { Skipped = true; BeginFade(SkipFade); }
            else if (T >= TFade0) BeginFade(TEnd - TFade0);
        }
        else
        {
            fadeT += dt / fadeLen;
            if (!Skipped) T += dt;
            if (fadeT >= 1f) { Finish(); return; }
        }
        Evaluate(dt);
    }

    private void BeginFade(float len) { fading = true; fadeT = 0f; fadeLen = Mathf.Max(0.05f, len); }

    private void Finish()
    {
        Completed = true;
        Action f = Finished;
        Destroy(gameObject);
        Current = null;
        if (f != null) f();
    }

    private void Evaluate(float dt)
    {
        float master = fading ? 1f - Mathf.SmoothStep(0f, 1f, fadeT) : 1f;
        CurrentStage = StageAt(T, Skipped && fading);
        group.alpha = master;
        group.blocksRaycasts = master > 0.55f;
        float softK = Soft ? 0.4f : 1f;

        // ---- estática
        float sa;
        if (T < TInsertEnd) sa = Mathf.Lerp(0.55f, 0.22f, T / TInsertEnd);
        else sa = 0.12f + 0.04f * Mathf.Sin(T * 3f);
        if (!Soft && T >= TTitle0 && T < TTitle0 + 0.5f) sa += 0.12f * (1f - (T - TTitle0) / 0.5f);   // golpe del título (estática suave, sin destello)
        StaticAlpha = sa * softK;
        staticImg.color = new Color(1f, 1f, 1f, StaticAlpha);
        if (T >= nextStatic || dt == 0f) { nextStatic = T + 0.07f; RefreshStatic(); }

        // ---- barra de tracking que rueda
        float barY = Mathf.Repeat(-T * 380f, 1400f) - 380f;
        if (T < TInsertEnd) barY = Mathf.Lerp(520f, -520f, (T / TInsertEnd) * (T / TInsertEnd));
        bar.rectTransform.anchoredPosition = new Vector2(0f, barY);
        bar.color = new Color(1f, 1f, 1f, (T < TInsertEnd ? 0.14f : 0.05f) * softK);

        // ---- texto de cada fase
        string txt = ""; string sub = ""; float a = 0f; int size = 74; Color tint = new Color(0.96f, 0.93f, 0.8f);
        if (T >= TWarn0 && T < TWarn1) { txt = WarningText; a = Pulse(T, TWarn0, TWarn1, 0.5f, 0.5f); size = 54; sub = ""; }
        else if (T >= TStudio0 && T < TStudio1) { txt = StudioText; sub = "presenta"; a = Pulse(T, TStudio0, TStudio1, 0.5f, 0.5f); size = 120; }
        else if (T >= TTitle0) { txt = TitleText; sub = "— una grabación encontrada —"; a = Mathf.Clamp01((T - TTitle0) / 0.6f); size = 96; tint = new Color(0.97f, 0.88f, 0.5f); }
        // parpadeo del texto: pasos de 1/3 s (≤ 3 cambios por segundo), amplitud moderada; con «Susto suave» casi nada
        float flick = 1f;
        if (T >= TTitle0 || (T >= TWarn0 && T < TWarn1 - 0.6f))
        {
            int step = Mathf.FloorToInt(T * 3f);
            float depth = Soft ? 0.12f : 0.4f;
            if (Hash(step) < 0.35f) flick = 1f - depth;
        }
        TrackFlicker(flick, dt);
        mainText.fontSize = ghostR.fontSize = ghostC.fontSize = size;
        mainText.text = ghostR.text = ghostC.text = txt;
        mainText.color = new Color(tint.r, tint.g, tint.b, a * flick);
        subText.text = sub; subText.color = new Color(0.7f, 0.66f, 0.5f, a * 0.85f * (Soft ? 1f : flick));
        // RGB split (desactivado con «Susto suave»)
        float split = Soft ? 0f : (T >= TTitle0 ? 3f + 5f * Mathf.Clamp01(1f - (T - TTitle0) / 1.2f) : 2f);
        if (!Soft && T >= TTitle0 && Hash(Mathf.FloorToInt(T * 6f) + 900) < 0.2f) split += 6f;
        ghostR.rectTransform.anchoredPosition = new Vector2(-split, 30f);
        ghostC.rectTransform.anchoredPosition = new Vector2(split, 30f);
        ghostR.color = new Color(1f, 0.1f, 0.1f, a * flick * (split > 0f ? 0.4f : 0f));
        ghostC.color = new Color(0.1f, 0.9f, 1f, a * flick * (split > 0f ? 0.4f : 0f));

        // ---- REC, PLAY, código de tiempo, pista de saltar
        recText.enabled = T >= 0.5f && Mathf.FloorToInt(T * 2f) % 2 == 0;     // parpadea a 1 Hz
        playText.enabled = T >= TInsertEnd;
        int frames = Mathf.FloorToInt(Mathf.Max(0f, T - 0.5f) * 25f);
        tcText.text = string.Format("{0:00}:{1:00}:{2:00}:{3:00}", 0, (frames / 25) / 60, (frames / 25) % 60, frames % 25);
        hint.color = new Color(0.6f, 0.58f, 0.5f, (T >= SkipAfter && !fading) ? 0.7f : 0f);

        // ---- audio
        if (T >= TTitle0 && !playedHit) { playedHit = true; one.PlayOneShot(hit, AudioMix.S(IntroAudio.HitGain * (Soft ? 0.6f : 1f))); }
        if (T >= 0.3f && !playedClunk2) { playedClunk2 = true; }
        float humTarget = Mathf.Clamp01(T / 1.5f) * (T >= TTitle0 ? 1.8f : 1f);
        hum.volume = AudioMix.A(IntroAudio.HumGain * humTarget) * master * (Soft ? 0.7f : 1f);
    }

    private void TrackFlicker(float state, float dt)
    {
        flickerWindow += dt;
        if (lastFlickerState >= 0f && !Mathf.Approximately(state, lastFlickerState)) flickerChanges++;
        lastFlickerState = state;
        if (flickerWindow >= 1f)
        {
            maxFlicker = Mathf.Max(maxFlicker, flickerChanges / flickerWindow);
            flickerChanges = 0f; flickerWindow = 0f;
        }
    }

    private void RefreshStatic()
    {
        System.Random rng = new System.Random(Environment.TickCount + Mathf.FloorToInt(T * 100f));
        for (int i = 0; i < staticPix.Length; i++)
        {
            byte v = (byte)(rng.Next(0, 150));
            staticPix[i] = new Color32(v, v, v, 255);
        }
        // tira horizontal desplazada (tracking) mientras se inserta la cinta
        if (T < TInsertEnd)
        {
            int row = rng.Next(0, SH - 6);
            for (int y = row; y < row + 5; y++) for (int x = 0; x < SW; x++) { byte v = (byte)rng.Next(60, 200); staticPix[y * SW + x] = new Color32(v, v, v, 255); }
        }
        staticTex.SetPixels32(staticPix);
        staticTex.Apply(false);
    }
}

// Sonidos de la intro (tonales y transitorios; sin ruido continuo).
public static class IntroAudio
{
    private const int SR = 22050;
    public const float ClunkGain = 0.6f, HitGain = 0.5f, HumGain = 0.05f;

    public static AudioClip CreateClunk()
    {
        int n = (int)(SR * 1.0f);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float v = Mathf.Sin(2f * Mathf.PI * 62f * t) * Mathf.Exp(-t * 14f) * 0.9f + Mathf.Sin(2f * Mathf.PI * 131f * t) * Mathf.Exp(-t * 22f) * 0.5f;
            if (t < 0.003f) v += (1f - t / 0.003f) * 0.6f * Mathf.Sin(2f * Mathf.PI * 1800f * t);   // clic del mecanismo
            float t2 = t - 0.32f;
            if (t2 > 0f) v += Mathf.Sin(2f * Mathf.PI * 88f * t2) * Mathf.Exp(-t2 * 20f) * 0.5f + (t2 < 0.002f ? 0.3f : 0f);
            float w = t - 0.4f;   // zumbido del mecanismo (tonal, con modulación) que se apaga
            if (w > 0f) v += (Mathf.Sin(2f * Mathf.PI * 214f * w) + 0.4f * Mathf.Sin(2f * Mathf.PI * 428f * w)) * 0.16f * (1f + 0.5f * Mathf.Sin(2f * Mathf.PI * 24f * w)) * Mathf.Clamp01(1f - w / 0.55f);
            d[i] = v;
        }
        Normalize(d, 0.8f);
        return Make("IntroClunk", d);
    }

    public static AudioClip CreateTitleHit()
    {
        int n = (int)(SR * 2.4f);
        float[] d = new float[n];
        float[] f = { 55f, 82.5f, 110f, 130.8f, 155.6f };
        float[] a = { 1f, 0.5f, 0.45f, 0.25f, 0.18f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Min(1f, t * 25f) * Mathf.Exp(-t * 1.6f);
            float v = 0f;
            for (int k = 0; k < f.Length; k++) v += a[k] * Mathf.Sin(2f * Mathf.PI * f[k] * t + k);
            d[i] = v * env;
        }
        Normalize(d, 0.8f);
        return Make("IntroTitleHit", d);
    }

    private static AudioClip Make(string name, float[] d)
    {
        AudioClip c = AudioClip.Create(name, d.Length, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    private static void Normalize(float[] d, float peak)
    {
        float m = 1e-4f;
        for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
        float k = peak / m;
        for (int i = 0; i < d.Length; i++) d[i] *= k;
    }
}
