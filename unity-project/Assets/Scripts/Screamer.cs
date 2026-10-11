using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

// Susto de muerte ("screamer"), dirigido por tiempo (independiente de Time.timeScale):
//   0.00-0.32  EMBESTIDA: la cámara se clava en la cara del monstruo con ease-in, golpe de FOV, sacudida y la linterna parpadea
//   0.32-0.44  CORTE A NEGRO (más corto con "Susto suave")
//   0.44-2.00  CARA: cara a centímetros, fogonazo de foto (fotograma de la cara a pantalla completa), grito en capas,
//              glitch VHS extremo (RGB split, bloques de estática, barra rodante, congelados), destellos rojos, sacudida violenta
//   2.00-2.60  fundido a negro con estática, acúfeno + latido
//   2.60-      pantalla "SIGNAL LOST / HAS MUERTO" con estática, botones Reintentar / Menú principal y reinicio automático
// El volumen respeta el volumen general (AudioListener.volume). GameSettings.SoftScare reduce todo (volumen, flashes, sacudida, glitch).
public class Screamer : MonoBehaviour
{
    public static bool Enabled = true;          // false = comportamiento antiguo (solo cámara + rojo)
    public static Screamer Active { get; private set; }

    public enum Phase { Idle, Lunge, BlackCut, Face, Fade, DeathScreen }

    public float cutStart = 0.32f;
    public float cutLength = 0.12f;
    public float faceEnd = 2.0f;
    public float fadeEnd = 2.6f;
    public float autoRestartAt = 9.5f;

    public Phase CurrentPhase { get; private set; }
    public float T { get; private set; }
    public bool Soft { get; private set; }
    public float Intensity { get { return Soft ? 0.45f : 1f; } }
    public bool PhotoFlashShown { get; private set; }
    public bool BlackCutShown { get; private set; }
    public bool DeathScreenShown { get; private set; }
    public float MaxShakeSeen { get; private set; }
    public float MaxFovPunch { get; private set; }
    public float PeakScreamLevel { get; private set; }
    public int FreezeCount { get; private set; }
    public float ScreamVolume { get { return screamSrc != null ? screamSrc.volume : 0f; } }
    public bool ScreamPlaying { get { return screamSrc != null && screamSrc.isPlaying; } }
    public bool AftermathPlaying { get { return afterSrc != null && afterSrc.isPlaying; } }
    public AudioClip ScreamClip { get { return screamClip; } }
    public MonsterProfile Profile { get; private set; }

    private GameManager gm;
    private Transform cam;
    private Camera camComp;
    private Transform killer;
    private MonsterVisual killerVisual;
    private Flashlight flashlight;
    private VhsEffect vhs;

    private Vector3 baseLocalPos;
    private Quaternion baseWorldRot;
    private Vector3 baseWorldPos;
    private float baseFov;
    private Vector3 closeWorldPos;
    private Vector3 headWorld;
    private bool prepared;
    private float seed;
    private float nextFreeze;

    private Canvas canvas;
    private Image black, red, white;
    private RawImage photo;
    private RawImage staticImg;
    private GameObject deathRoot;
    private Text signalText, deadText, subText, countText;
    private Texture2D noiseTex;
    private Color32[] noisePx;
    private float noiseTimer;
    private RenderTexture photoRt;
    private GameObject faceCamGo;
    private Light photoLight;
    private AudioSource screamSrc, afterSrc, boomSrc;
    private AudioClip screamClip, afterClip;
    private bool screamStarted, afterStarted, photoTaken;
    private bool buttonsMade;
    private Font mono;

    // ------------------------------------------------------------------
    public static Screamer Begin(GameManager gm, Transform camera, MonsterAI by)
    {
        if (!Enabled || gm == null || camera == null) return null;
        Screamer s = gm.gameObject.GetComponent<Screamer>();
        if (s != null) return s;
        s = gm.gameObject.AddComponent<Screamer>();
        s.gm = gm;
        s.Init(camera, by);
        return s;
    }

    private void Init(Transform camera, MonsterAI by)
    {
        Active = this;
        Soft = GameSettings.SoftScare;
        cam = camera;
        camComp = camera.GetComponent<Camera>();
        killer = by != null ? by.transform : null;
        killerVisual = by != null ? by.visual : null;
        Profile = killerVisual != null ? killerVisual.profile : null;
        flashlight = camera.GetComponent<Flashlight>();
        vhs = camera.GetComponent<VhsEffect>();
        seed = Random.value * 100f;
        if (Soft) { cutLength = 0.06f; }

        baseLocalPos = cam.localPosition;
        baseWorldPos = cam.position;
        baseWorldRot = cam.rotation;
        baseFov = camComp != null ? camComp.fieldOfView : 70f;

        // posición de la cara y punto de cámara muy cerca de ella
        float headY = Profile != null ? Profile.ai_eyeHeight * 0.95f : 1.7f;
        if (killer != null)
        {
            headWorld = killer.position + Vector3.up * headY;
            Vector3 away = baseWorldPos - headWorld; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -killer.forward;
            away.Normalize();
            float d = 0.55f + (Profile != null ? Mathf.Clamp(Profile.size - 1.9f, 0f, 3f) * 0.12f : 0f);
            closeWorldPos = headWorld + away * d + Vector3.up * 0.05f;
        }
        else
        {
            headWorld = baseWorldPos + cam.forward * 1.5f;
            closeWorldPos = baseWorldPos + cam.forward * 0.9f;
        }

        // audio (clips distintos por tipo de monstruo)
        GrowlKind kind = Profile != null ? Profile.growl : GrowlKind.Rasp;
        float pitch = Profile != null ? Profile.growlPitch : 1f;
        int sd = Profile != null ? Profile.id.GetHashCode() & 0xffff : 7;
        screamClip = ProceduralAudio.CreateScreamerScream(kind, sd, pitch);
        afterClip = ProceduralAudio.CreateScreamerAftermath();
        screamSrc = NewSource("ScreamSrc", screamClip);
        afterSrc = NewSource("AftermathSrc", afterClip);
        boomSrc = NewSource("BoomSrc", null);
        screamSrc.volume = Mathf.Lerp(1f, 0.4f, Soft ? 1f : 0f) * AudioMix.Sfx;
        afterSrc.volume = (Soft ? 0.25f : 0.5f) * AudioMix.Sfx;

        BuildUi();
        BuildPhotoRig();
        CurrentPhase = Phase.Lunge;
        if (gm.player != null) gm.player.InputEnabled = false;
        if (vhs != null) vhs.Spike(1f);
    }

    private AudioSource NewSource(string name, AudioClip clip)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(transform, false);
        AudioSource a = g.AddComponent<AudioSource>();
        a.clip = clip; a.spatialBlend = 0f; a.playOnAwake = false; a.loop = false; a.bypassEffects = true; a.bypassListenerEffects = true;
        a.priority = 0;
        return a;
    }

    // ------------------------------------------------------------------
    // UI
    // ------------------------------------------------------------------
    private Image MakeImage(Transform parent, string name, Color c)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        Image im = g.AddComponent<Image>();
        im.color = c; im.raycastTarget = false;
        Stretch(im.rectTransform);
        return im;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    private Text MakeText(Transform parent, string name, string text, int size, TextAnchor anchor, Vector2 aMin, Vector2 aMax, Color c)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        Text t = g.AddComponent<Text>();
        t.font = mono != null ? mono : ProceduralAssets.GetDefaultFont();
        t.text = text; t.fontSize = size; t.alignment = anchor; t.color = c; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return t;
    }

    private void BuildUi()
    {
        try { mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Lucida Console", "DejaVu Sans Mono", "Liberation Mono" }, 40); } catch { mono = null; }
        if (mono == null) mono = ProceduralAssets.GetDefaultFont();

        GameObject cg = new GameObject("ScreamerCanvas");
        cg.transform.SetParent(transform, false);
        canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        photo = MakeRaw(cg.transform, "Photo");
        photo.enabled = false;
        red = MakeImage(cg.transform, "RedFlash", new Color(0.7f, 0f, 0f, 0f));
        white = MakeImage(cg.transform, "WhiteFlash", new Color(1f, 1f, 1f, 0f));
        black = MakeImage(cg.transform, "Black", new Color(0f, 0f, 0f, 0f));

        // pantalla final
        deathRoot = new GameObject("DeathScreen", typeof(RectTransform));
        deathRoot.transform.SetParent(cg.transform, false);
        Stretch((RectTransform)deathRoot.transform);
        Image bg = MakeImage(deathRoot.transform, "Bg", new Color(0.02f, 0.02f, 0.03f, 1f));
        bg.raycastTarget = true;
        noiseTex = new Texture2D(96, 54, TextureFormat.RGBA32, false);
        noiseTex.filterMode = FilterMode.Point; noiseTex.wrapMode = TextureWrapMode.Clamp;
        noisePx = new Color32[96 * 54];
        staticImg = MakeRaw(deathRoot.transform, "Static");
        staticImg.texture = noiseTex;
        staticImg.color = new Color(1f, 1f, 1f, 0.55f);
        signalText = MakeText(deathRoot.transform, "SignalLost", "SIGNAL LOST", 44, TextAnchor.UpperLeft, new Vector2(0.04f, 0.78f), new Vector2(0.6f, 0.96f), new Color(0.95f, 0.95f, 0.95f, 0.9f));
        deadText = MakeText(deathRoot.transform, "Dead", "HAS MUERTO", 140, TextAnchor.MiddleCenter, new Vector2(0f, 0.5f), new Vector2(1f, 0.78f), new Color(0.85f, 0.05f, 0.05f, 1f));
        deadText.fontStyle = FontStyle.Bold;
        string who = Profile != null ? Profile.displayName : "algo";
        int p = who.IndexOf(" (");
        if (p > 0) who = who.Substring(0, p);
        subText = MakeText(deathRoot.transform, "Sub", "Te ha atrapado " + who + ".", 40, TextAnchor.MiddleCenter, new Vector2(0f, 0.42f), new Vector2(1f, 0.5f), new Color(0.9f, 0.9f, 0.9f, 0.9f));
        countText = MakeText(deathRoot.transform, "Count", "", 32, TextAnchor.MiddleCenter, new Vector2(0f, 0.05f), new Vector2(1f, 0.12f), new Color(0.8f, 0.8f, 0.8f, 0.8f));
        deathRoot.SetActive(false);
    }

    private RawImage MakeRaw(Transform parent, string name)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RawImage ri = g.AddComponent<RawImage>();
        ri.raycastTarget = false;
        Stretch(ri.rectTransform);
        return ri;
    }

    private void MakeButtons()
    {
        if (buttonsMade) return;
        buttonsMade = true;
        MakeButton("Reintentar", new Vector2(0.27f, 0.2f), new Vector2(0.48f, 0.3f), delegate { if (gm != null) gm.Restart(); });
        MakeButton("Menú principal", new Vector2(0.52f, 0.2f), new Vector2(0.73f, 0.3f), delegate
        {
            if (UIManager.Instance != null) UIManager.Instance.OnMainMenu();
            else if (gm != null && gm.bootstrap != null) gm.bootstrap.GoToMainMenu();
        });
    }

    private Button MakeButton(string label, Vector2 aMin, Vector2 aMax, UnityEngine.Events.UnityAction act)
    {
        GameObject g = new GameObject("Btn_" + label, typeof(RectTransform));
        g.transform.SetParent(deathRoot.transform, false);
        Image im = g.AddComponent<Image>();
        im.color = new Color(0.12f, 0.12f, 0.14f, 0.95f);
        Button b = g.AddComponent<Button>();
        ColorBlock cb = b.colors; cb.highlightedColor = new Color(0.55f, 0.1f, 0.1f); cb.pressedColor = new Color(0.8f, 0.15f, 0.15f); b.colors = cb;
        b.onClick.AddListener(act);
        RectTransform rt = im.rectTransform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = rt.offsetMax = Vector2.zero;
        Text t = MakeText(g.transform, "Label", label, 40, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one, Color.white);
        t.raycastTarget = false;
        return b;
    }

    // Fotograma de la cara (cámara auxiliar a textura) + luz de fogonazo
    private void BuildPhotoRig()
    {
        GameObject lg = new GameObject("PhotoFlashLight");
        lg.transform.SetParent(cam, false);
        photoLight = lg.AddComponent<Light>();
        photoLight.type = LightType.Point; photoLight.range = 5f; photoLight.intensity = 0f;
        photoLight.color = new Color(1f, 0.97f, 0.9f); photoLight.shadows = LightShadows.None;
    }

    private void TakePhoto()
    {
        photoTaken = true;
        if (camComp == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        try
        {
            photoRt = new RenderTexture(Mathf.Min(Screen.width, 960), Mathf.Min(Screen.height, 540), 16, RenderTextureFormat.ARGB32);
            faceCamGo = new GameObject("FaceFlashCam");
            faceCamGo.transform.SetParent(cam, false);
            Camera fc = faceCamGo.AddComponent<Camera>();
            fc.CopyFrom(camComp);
            fc.enabled = false;
            fc.targetTexture = photoRt;
            fc.fieldOfView = Mathf.Max(24f, camComp.fieldOfView);
            fc.Render();
            fc.targetTexture = null;
            photo.texture = photoRt;
            photo.color = new Color(1f, 1f, 1f, 1f);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Screamer] Foto de la cara no disponible: " + e.Message);
            photo.texture = null;
        }
    }

    // ------------------------------------------------------------------
    private static float EaseIn(float x) { x = Mathf.Clamp01(x); return x * x * x; }
    private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }

    private void LateUpdate()
    {
        if (CurrentPhase == Phase.Idle || cam == null) return;
        float dt = Time.unscaledDeltaTime;
        T += dt;
        float k = Intensity;

        // actualizar posición de la cara (el monstruo se mueve en la embestida)
        if (killer != null)
        {
            float headY = Profile != null ? Profile.ai_eyeHeight * 0.95f : 1.7f;
            headWorld = killer.position + Vector3.up * headY;
        }

        float cutEnd = cutStart + cutLength;
        if (T < cutStart) CurrentPhase = Phase.Lunge;
        else if (T < cutEnd) CurrentPhase = Phase.BlackCut;
        else if (T < faceEnd) CurrentPhase = Phase.Face;
        else if (T < fadeEnd) CurrentPhase = Phase.Fade;
        else CurrentPhase = Phase.DeathScreen;

        if (T >= cutStart) BlackCutShown = true;
        float shake = 0f, fovNow = baseFov;
        Vector3 pos = baseWorldPos;
        Quaternion rot = baseWorldRot;

        switch (CurrentPhase)
        {
            case Phase.Lunge:
            {
                float u = T / cutStart;
                float move = EaseIn(u);
                Vector3 target = Vector3.Lerp(baseWorldPos, closeWorldPos, 0.9f);
                pos = Vector3.Lerp(baseWorldPos, target, move);
                Quaternion look = LookAt(pos, headWorld);
                rot = Quaternion.Slerp(baseWorldRot, look, EaseIn(Mathf.Clamp01(T / (cutStart * 0.6f))));
                // golpe de FOV: se abre y luego se cierra de golpe
                float widen = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u * 1.15f)) * 24f * k;
                fovNow = baseFov + widen - 14f * k * EaseIn(u);
                shake = Mathf.Lerp(0.004f, 0.05f, u) * k;
                FlickerFlashlight(0.35f + 0.5f * (1f - u));
                break;
            }
            case Phase.BlackCut:
                pos = closeWorldPos; rot = LookAt(closeWorldPos, headWorld); fovNow = baseFov - 22f * k;
                if (flashlight != null && flashlight.spot != null) flashlight.spot.enabled = false;
                BlackCutShown = true;
                break;
            default:
            {
                float f = Mathf.Max(0f, T - cutEnd);
                float decay = Mathf.Exp(-f * 1.6f);
                // pequeño dolly hacia la cara
                Vector3 closer = Vector3.Lerp(closeWorldPos, headWorld, 0.12f * Mathf.Clamp01(f / 1.4f));
                pos = closer;
                rot = LookAt(closer, headWorld);
                fovNow = baseFov - 22f * k - 8f * Mathf.Clamp01(f / 1.5f) * k + (Mathf.PerlinNoise(T * 31f, seed) - 0.5f) * 8f * k * decay;
                shake = (0.012f + 0.06f * decay) * k;
                if (CurrentPhase == Phase.Fade) shake *= 0.5f;
                FlickerFlashlight(0.7f);
                break;
            }
        }

        // sacudida (ruido Perlin a ~35 Hz; el rotacional más violento al principio)
        float sh = shake;
        if (sh > MaxShakeSeen) MaxShakeSeen = sh;
        Vector3 off = new Vector3(Mathf.PerlinNoise(T * 37f, seed) - 0.5f, Mathf.PerlinNoise(T * 41f, seed + 3f) - 0.5f, Mathf.PerlinNoise(T * 29f, seed + 7f) - 0.5f) * 2f * sh;
        float rotAmp = sh * 90f;   // grados
        Quaternion jolt = Quaternion.Euler((Mathf.PerlinNoise(T * 33f, seed + 11f) - 0.5f) * 2f * rotAmp, (Mathf.PerlinNoise(T * 35f, seed + 13f) - 0.5f) * 2f * rotAmp,
            (Mathf.PerlinNoise(T * 27f, seed + 17f) - 0.5f) * 2f * rotAmp * 1.6f);
        cam.position = pos + off;
        cam.rotation = rot * jolt;
        if (camComp != null)
        {
            camComp.fieldOfView = Mathf.Clamp(fovNow, 18f, 120f);
            float punch = Mathf.Abs(camComp.fieldOfView - baseFov);
            if (punch > MaxFovPunch) MaxFovPunch = punch;
        }

        UpdateOverlays(dt, cutEnd, k);
        UpdateAudio(cutEnd);
        UpdateVhs(k);

        if (CurrentPhase == Phase.DeathScreen) UpdateDeathScreen(dt);
    }

    private static Quaternion LookAt(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        return d.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(d) : Quaternion.identity;
    }

    private void FlickerFlashlight(float chance)
    {
        if (flashlight == null || flashlight.spot == null) return;
        bool on = Random.value < chance;
        flashlight.spot.enabled = on;
        if (on) flashlight.spot.intensity = flashlight.maxIntensity * Random.Range(0.2f, 2.2f);
    }

    private void UpdateOverlays(float dt, float cutEnd, float k)
    {
        // negro: corte, fundido final
        float blackA = 0f;
        if (CurrentPhase == Phase.BlackCut) blackA = 1f;
        else if (CurrentPhase == Phase.Fade)
        {
            float u = (T - faceEnd) / (fadeEnd - faceEnd);
            blackA = Mathf.Clamp01(u) * (0.75f + 0.25f * Random.value);
        }
        else if (CurrentPhase == Phase.Face && T < cutEnd + 0.05f) blackA = 0f;
        black.color = new Color(0f, 0f, 0f, blackA);

        // foto: fotograma de la cara a pantalla completa durante ~0.14 s al volver de negro
        float f = T - cutEnd;
        if (CurrentPhase >= Phase.Face && f >= 0f && !photoTaken) { TakePhoto(); PhotoFlashShown = true; }
        bool showPhoto = f >= 0f && f < (Soft ? 0.08f : 0.14f) && CurrentPhase == Phase.Face;
        if (showPhoto)
        {
            PhotoFlashShown = true;
            photo.enabled = photo.texture != null;
        }
        else photo.enabled = false;

        // fogonazo blanco + luz puntual del flash
        float w = 0f;
        if (f >= 0f && f < 0.16f) w = (1f - f / 0.16f) * (Soft ? 0.45f : 0.95f);
        white.color = new Color(1f, 1f, 1f, w);
        if (photoLight != null)
        {
            float li = 0f;
            if (f >= 0f && f < 0.2f) li = Mathf.Lerp(9f, 1.5f, f / 0.2f) * k;
            else if (CurrentPhase == Phase.Face) li = 0.8f + Random.value * 0.8f;
            photoLight.intensity = li;
        }

        // rojo: parpadeos que decaen
        float r = 0f;
        if (CurrentPhase == Phase.Lunge) r = Mathf.Lerp(0f, 0.25f, T / cutStart) * k;
        else if (CurrentPhase == Phase.Face)
        {
            float decay = Mathf.Exp(-f * 1.3f);
            r = (0.16f + 0.5f * Mathf.Abs(Mathf.Sin(T * 26f)) * decay) * k;
        }
        else if (CurrentPhase == Phase.Fade) r = 0.18f * k;
        red.color = new Color(0.75f, 0f, 0f, r);
        // el HUD de GameManager tenía su propio overlay rojo: se anula
        if (gm != null && gm.overlay != null) gm.overlay.color = new Color(0f, 0f, 0f, 0f);
        if (gm != null && gm.messageText != null) gm.messageText.text = string.Empty;
    }

    private void UpdateAudio(float cutEnd)
    {
        if (!screamStarted && T >= cutStart - 0.03f)
        {
            screamStarted = true;
            screamSrc.Play();
            // golpe grave sincronizado con la vuelta de negro
            boomSrc.pitch = 1f;
        }
        if (!afterStarted && T >= faceEnd - 0.2f)
        {
            afterStarted = true;
            afterSrc.Play();
        }
        // el tinnitus/latido sube mientras el grito se apaga y se desvanece al final
        if (afterStarted && afterSrc != null)
        {
            float v = Mathf.Clamp01((T - (faceEnd - 0.2f)) / 0.8f) * (Soft ? 0.3f : 0.55f);
            afterSrc.volume = v * AudioMix.Sfx;
        }
    }

    private void UpdateVhs(float k)
    {
        if (vhs == null) vhs = cam.GetComponent<VhsEffect>();
        if (vhs == null) return;
        float level = 0f;
        switch (CurrentPhase)
        {
            case Phase.Lunge: level = Mathf.Clamp01(T / cutStart) * 0.8f; break;
            case Phase.BlackCut: level = 1f; break;
            case Phase.Face: level = 1f - 0.3f * Mathf.Clamp01((T - cutStart - cutLength) / 1.5f); break;
            case Phase.Fade: level = 0.8f; break;
            case Phase.DeathScreen: level = 0.55f; break;
        }
        level *= k;
        vhs.ScreamLevel = level;
        vhs.Spike(Mathf.Min(1f, level + 0.2f));
        if (level > PeakScreamLevel) PeakScreamLevel = level;
        // congelados de imagen aleatorios (stutter)
        if (CurrentPhase == Phase.Face && T >= nextFreeze)
        {
            nextFreeze = T + Random.Range(0.18f, 0.5f);
            if (Random.value < 0.6f * k) { vhs.Freeze(Random.Range(0.05f, 0.12f)); FreezeCount++; }
        }
    }

    private void UpdateDeathScreen(float dt)
    {
        if (!DeathScreenShown)
        {
            DeathScreenShown = true;
            deathRoot.SetActive(true);
            black.color = Color.clear;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            MakeButtons();
        }
        // estática animada (12 fps)
        noiseTimer += dt;
        if (noiseTimer > 0.08f)
        {
            noiseTimer = 0f;
            for (int i = 0; i < noisePx.Length; i++)
            {
                byte v = (byte)Random.Range(0, 200);
                noisePx[i] = new Color32(v, v, v, 255);
            }
            noiseTex.SetPixels32(noisePx);
            noiseTex.Apply(false);
        }
        float tt = T - fadeEnd;
        bool blink = Mathf.Repeat(tt, 1.1f) < 0.7f;
        signalText.enabled = blink;
        float pulse = 0.75f + 0.25f * Mathf.Sin(tt * 3f);
        deadText.color = new Color(0.85f * pulse, 0.04f, 0.04f, 1f);
        float left = Mathf.Max(0f, autoRestartAt - T);
        countText.text = "Reiniciando el nivel en " + Mathf.CeilToInt(left) + " s   (R para reintentar ya)";
        if (T >= autoRestartAt && gm != null)
        {
            gm.Restart();
        }
    }

    // Final de la secuencia (Restart): restaurar lo que se haya tocado
    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (vhs != null) vhs.ScreamLevel = 0f;
        if (camComp != null) camComp.fieldOfView = baseFov;
        if (photoRt != null) { photoRt.Release(); ProceduralAssets.SafeDestroy(photoRt); }
        ProceduralAssets.SafeDestroy(noiseTex);
        ProceduralAssets.SafeDestroy(screamClip);
        ProceduralAssets.SafeDestroy(afterClip);
        if (faceCamGo != null) ProceduralAssets.SafeDestroy(faceCamGo);
        if (photoLight != null) ProceduralAssets.SafeDestroy(photoLight.gameObject);
    }
}
