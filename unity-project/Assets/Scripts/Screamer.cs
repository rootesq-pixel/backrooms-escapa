using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

// Susto de muerte ("screamer"), dirigido por tiempo (independiente de Time.timeScale):
//   0.00-0.32  EMBESTIDA: la cámara se clava en la cara del monstruo con ease-in, golpe de FOV, sacudida y la linterna parpadea
//   0.32-0.44  CORTE A NEGRO (más corto con "Susto suave")
//   0.44-2.45  CARA (v6.6): la cámara principal pasa a renderizar SOLO al monstruo (capa propia 29: sin muros, cabina, niebla ni sombras
//              que lo oculten), con luz principal fría fuerte + luz de contorno roja, sobre fondo oscuro. Tres planos con cortes rápidos:
//              A) cara y hombros a ~1-1,4 m (según la altura del modelo), B) primerísimo primer plano de los ojos, C) plano abierto con
//              embestida hacia la cámara. Además fogonazo de foto, grito en capas, glitch VHS reducido (la cara debe leerse),
//              destellos rojos y sacudida.
//   2.45-2.95  fundido a negro con estática, acúfeno + latido
//   2.95-      pantalla "SIGNAL LOST / HAS MUERTO" con estática, botones Reintentar / Menú principal y reinicio automático
// El volumen respeta el volumen general (AudioListener.volume). GameSettings.SoftScare reduce todo (volumen, flashes, sacudida, glitch).
public class Screamer : MonoBehaviour
{
    public static bool Enabled = true;          // false = comportamiento antiguo (solo cámara + rojo)
    public static Screamer Active { get; private set; }

    public enum Phase { Idle, Lunge, BlackCut, Face, Fade, DeathScreen }

    // Capa donde se mueve el monstruo durante el plano de la cara; solo la cámara del susto la dibuja
    public const int ScreamLayer = 29;
    public const float ShotBEnd = 1.36f, ShotAEnd = 0.72f;

    public float cutStart = 0.32f;
    public float cutLength = 0.12f;
    public float faceEnd = 2.45f;
    public float fadeEnd = 2.95f;
    public float autoRestartAt = 9.5f;

    public Phase CurrentPhase { get; private set; }
    public float T { get; private set; }
    public float PreviewSeek = -1f;     // solo vistas previas/pruebas: fija el tiempo de la secuencia
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
    // ---- estado del plano de la cara (para pruebas y vistas previas)
    public bool FaceMode { get; private set; }
    public int Shot { get; private set; }                 // 0 = ninguno, 1 = A (cara), 2 = B (ojos), 3 = C (plano abierto)
    public float FaceDistance { get; private set; }       // distancia cámara-cara (m)
    public float HeadHeight { get; private set; }         // altura del modelo (m)
    public Vector3 HeadPoint { get { return headPoint; } }
    public string HeadBoneName { get { return eyesT != null ? "Eyes" : headBone != null ? headBone.name : "(bounds)"; } }
    public Vector3 FaceDir { get { return FaceForward(); } }
    public Camera FaceCamera { get { return camComp; } }
    public Light KeyLight { get { return keyLight; } }
    public Light RimLight { get { return rimLight; } }
    public bool MonsterVisibleToCamera { get; private set; }   // algún renderer del monstruo está dentro del frustum y en la máscara
    public float HeadScreenFraction { get; private set; }      // altura de la cabeza en pantalla (0..1)
    public int ShotsSeen { get { return shotsSeen; } }

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
    private Light photoLight, keyLight, rimLight;
    private Vector3 headPoint, faceDirFixed, faceSmooth;
    private Transform eyesT;
    private Transform headBone;
    private int shotsSeen, lastShot;
    private readonly System.Collections.Generic.List<Transform> layerTs = new System.Collections.Generic.List<Transform>();
    private readonly System.Collections.Generic.List<int> layerOld = new System.Collections.Generic.List<int>();
    private int savedMask; private CameraClearFlags savedClear; private Color savedBg; private float savedNear;
    private bool savedFog; private Color savedAmbient; private UnityEngine.Rendering.AmbientMode savedAmbientMode;
    private Vector3 savedModelScale; private float cutDip;
    private readonly System.Collections.Generic.List<Light> dimmedLights = new System.Collections.Generic.List<Light>();
    private readonly System.Collections.Generic.List<int> dimmedMasks = new System.Collections.Generic.List<int>();
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
        savedNear = camComp != null ? camComp.nearClipPlane : 0.1f;

        baseLocalPos = cam.localPosition;
        baseWorldPos = cam.position;
        baseWorldRot = cam.rotation;
        baseFov = camComp != null ? camComp.fieldOfView : 70f;

        // posición de la cara (a partir de los renderers / hueso de la cabeza) y punto de cámara del plano A
        if (killer != null)
        {
            // el monstruo mira siempre hacia donde estaba el jugador (también en clientes, donde es un proxy de red)
            Vector3 fd = baseWorldPos - killer.position; fd.y = 0f;
            if (fd.sqrMagnitude > 0.01f) { faceDirFixed = fd.normalized; killer.rotation = Quaternion.LookRotation(faceDirFixed); }
        }
        if (killerVisual != null && killerVisual.animator != null) killerVisual.animator.applyRootMotion = false;
        FindHeadBone();
        ComputeHead();
        headWorld = headPoint;
        closeWorldPos = ShotPose(ShotAEnd * 0.5f, 1f).pos;

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
    // Cara del monstruo y planos de cámara
    // ------------------------------------------------------------------
    private void FindHeadBone()
    {
        headBone = null; eyesT = null;
        if (killer == null) return;
        foreach (Transform t in killer.GetComponentsInChildren<Transform>(true)) if (t.name == "Eyes") { eyesT = t; break; }
        float best = -1f;
        foreach (Transform t in killer.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("head") || n.Contains("end") || n.Contains("top") || n.Contains("eye") || n.Contains("light")) continue;
            if (t.position.y > best) { best = t.position.y; headBone = t; }
        }
    }

    // altura del modelo (renderers) y punto de la cara (centro de la cabeza)
    private void ComputeHead()
    {
        Bounds b = new Bounds(killer != null ? killer.position + Vector3.up : cam.position, Vector3.zero);
        bool any = false;
        if (killerVisual != null && killerVisual.Renderers != null)
            foreach (Renderer r in killerVisual.Renderers)
            {
                if (r == null || r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        float h = Profile != null ? Mathf.Clamp(Profile.size, 1.2f, 4f) : (any ? Mathf.Clamp(b.size.y, 1.2f, 5f) : 1.9f);
        HeadHeight = h;
        float topY = any ? b.max.y : (killer != null ? killer.position.y + h : cam.position.y);
        Vector3 c = any ? b.center : (killer != null ? killer.position : cam.position);
        Vector3 p = new Vector3(c.x, topY - 0.06f * h, c.z);
        if (killer != null)
        {
            // desplaza hacia el eje real del cuerpo (los brazos extendidos desplazan el centro de la caja)
            p.x = killer.position.x; p.z = killer.position.z;
        }
        if (eyesT == null && !(Profile != null && Profile.screamHeadTop > 0f))
        {
            if (headBone != null && headBone.position.y > (any ? b.min.y : 0f) + 0.55f * h) p = headBone.position + Vector3.up * 0.04f * h;
        }
        if (Profile != null && Profile.screamHeadTop > 0f && any) p = new Vector3(killer != null ? killer.position.x : c.x, topY - Profile.screamHeadTop * h, killer != null ? killer.position.z : c.z);
        if (Profile != null && Profile.screamHeadTop > 0f && any) headPoint = p;
        else if (eyesT != null)
        {
            // Los ojos están enganchados al hueso de la cabeza: su posición y su "adelante" son la cara real, también con la animación
            headPoint = eyesT.position;
            Vector3 want = eyesT.forward;
            want.y = Mathf.Clamp(want.y, -0.55f, 0.55f);   // mirando muy arriba/abajo la cámara no sigue del todo
            if (new Vector2(want.x, want.z).sqrMagnitude < 0.05f) want = faceSmooth.sqrMagnitude > 0.01f ? faceSmooth : faceDirFixed;
            want.Normalize();
            faceSmooth = faceSmooth.sqrMagnitude < 0.01f ? want : Vector3.Slerp(faceSmooth, want, 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime)).normalized;
        }
        else headPoint = p;
    }

    // Hacia dónde mira la cara: el monstruo se gira hacia donde estaba el jugador y en los planos de cara se mantiene en su pose
    // de reposo (Idle: los clips de ataque/carrera giran el torso respecto a la raíz), así que "adelante" del monstruo es la cara (los clips de ataque giran el torso y no sirven de referencia).
    private Mesh bakeMesh;
    // Centro de la cabeza a partir de los vértices más altos de la malla animada (sirve aunque el rig no tenga hueso "Head")
    private bool HeadFromMesh(float h, out Vector3 center)
    {
        center = Vector3.zero;
        if (killerVisual == null || killerVisual.Renderers == null) return false;
        SkinnedMeshRenderer best = null; float bestY = float.MinValue;
        foreach (Renderer r in killerVisual.Renderers)
        {
            SkinnedMeshRenderer sk = r as SkinnedMeshRenderer;
            if (sk == null || sk.sharedMesh == null || !sk.enabled) continue;
            if (sk.bounds.max.y > bestY) { bestY = sk.bounds.max.y; best = sk; }
        }
        if (best == null) return false;
        if (bakeMesh == null) bakeMesh = new Mesh();
        best.BakeMesh(bakeMesh, true);
        Vector3[] vs = bakeMesh.vertices;
        if (vs.Length == 0) return false;
        Transform t = best.transform;
        float maxY = float.MinValue;
        Vector3[] wv = new Vector3[vs.Length];
        for (int i = 0; i < vs.Length; i++) { wv[i] = t.position + t.rotation * vs[i]; if (wv[i].y > maxY) maxY = wv[i].y; }
        Vector3 sum = Vector3.zero; int n = 0;
        for (int i = 0; i < wv.Length; i++) if (wv[i].y > maxY - 0.09f * h) { sum += wv[i]; n++; }
        if (n < 8) return false;
        center = sum / n;
        return true;
    }

    private Vector3 FaceForward()
    {
        Vector3 f = faceDirFixed;
        if (f.sqrMagnitude < 0.01f) f = killer != null ? killer.forward : -cam.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 0.01f) f = new Vector3(baseWorldPos.x - headPoint.x, 0f, baseWorldPos.z - headPoint.z);
        return f.normalized;
    }

    public struct Pose { public Vector3 pos; public Quaternion rot; public float fov; public int shot; public float dist; }

    // f = segundos desde el inicio del plano de la cara
    private Pose ShotPose(float f, float k)
    {
        float H = HeadHeight;
        float up0 = Profile != null ? Profile.screamUp * H : 0f;
        float ds = Profile != null ? Profile.screamDist : 1f;
        Vector3 P = headPoint, fwd = FaceForward(), right = Vector3.Cross(Vector3.up, new Vector3(fwd.x, 0f, fwd.z).normalized);
        float faceDur = faceEnd - (cutStart + cutLength);
        Pose o = new Pose();
        Vector3 aim;
        if (f < ShotAEnd)
        {
            o.shot = 1; o.dist = Mathf.Clamp(0.5f * H, 0.9f, 1.4f) * ds; o.fov = 48f;
            aim = P - Vector3.up * 0.03f * H;
            o.pos = P + fwd * o.dist + right * 0.05f * H + Vector3.up * (0.03f * H + up0);
        }
        else if (f < ShotBEnd)
        {
            o.shot = 2; o.dist = Mathf.Clamp(0.36f * H, 0.55f, 1.1f) * Mathf.Lerp(1f, ds, 0.7f); o.fov = 30f;
            aim = P + Vector3.up * 0.01f * H;
            o.pos = P + fwd * o.dist - right * 0.03f * H + Vector3.up * (0.02f * H + up0);
        }
        else
        {
            float u = Mathf.Clamp01((f - ShotBEnd) / Mathf.Max(0.1f, faceDur - ShotBEnd));
            o.shot = 3;
            o.dist = Mathf.Lerp(Mathf.Clamp(0.55f * H, 1.3f, 1.9f), Mathf.Clamp(0.3f * H, 0.65f, 0.9f), EaseIn(u)) * ds;
            o.fov = Mathf.Lerp(62f, 54f, u);
            aim = P - Vector3.up * 0.07f * H;
            o.pos = P + fwd * o.dist + right * 0.1f * H * (1f - u) + Vector3.up * (0.05f * H + up0);
        }
        Vector3 d = aim - o.pos;
        o.rot = d.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(d) : cam.rotation;
        return o;
    }

    // La cámara principal pasa a dibujar solo la capa del monstruo (sin muros, cabina, niebla...), con luz propia
    private void EnterFaceMode()
    {
        if (FaceMode || camComp == null) return;
        FaceMode = true;
        savedMask = camComp.cullingMask; savedClear = camComp.clearFlags; savedBg = camComp.backgroundColor; savedNear = camComp.nearClipPlane;
        savedFog = RenderSettings.fog; savedAmbient = RenderSettings.ambientLight; savedAmbientMode = RenderSettings.ambientMode;
        camComp.cullingMask = 1 << ScreamLayer;
        camComp.clearFlags = CameraClearFlags.SolidColor;
        camComp.backgroundColor = new Color(0.035f, 0.004f, 0.006f);
        camComp.nearClipPlane = 0.03f;
        RenderSettings.fog = false;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.16f, 0.16f, 0.2f);
        if (killer != null)
        {
            foreach (Transform t in killer.GetComponentsInChildren<Transform>(true)) { layerTs.Add(t); layerOld.Add(t.gameObject.layer); t.gameObject.layer = ScreamLayer; }
            if (killerVisual != null && killerVisual.Renderers != null) foreach (Renderer r in killerVisual.Renderers) if (r != null) r.enabled = true;
            if (killerVisual != null && killerVisual.model != null) savedModelScale = killerVisual.model.localScale;
        }
        // las luces de la escena (techo, linterna...) no deben iluminar la capa del monstruo: solo la luz del susto
        foreach (Light l in Object.FindObjectsOfType<Light>())
        {
            if (l == null || (killer != null && l.transform.IsChildOf(killer))) continue;
            dimmedLights.Add(l); dimmedMasks.Add(l.cullingMask);
            l.cullingMask &= ~(1 << ScreamLayer);
        }
        if (killerVisual != null && killerVisual.animator != null)
        {
            killerVisual.animator.applyRootMotion = false;
            killerVisual.animator.CrossFadeInFixedTime((Profile != null ? Profile.screamAnim : MonsterVisual.Anim.Idle).ToString(), 0.05f);
        }
        GameObject kg = new GameObject("ScreamerKeyLight");
        kg.transform.SetParent(transform, false);
        keyLight = kg.AddComponent<Light>();
        keyLight.type = LightType.Spot; keyLight.spotAngle = 85f; keyLight.range = 9f; keyLight.intensity = 2.3f;
        keyLight.color = new Color(0.82f, 0.9f, 1f); keyLight.shadows = LightShadows.None; keyLight.cullingMask = 1 << ScreamLayer;
        GameObject rg = new GameObject("ScreamerRimLight");
        rg.transform.SetParent(transform, false);
        rimLight = rg.AddComponent<Light>();
        rimLight.type = LightType.Point; rimLight.range = 4f; rimLight.intensity = 1.4f;
        rimLight.color = new Color(1f, 0.16f, 0.1f); rimLight.shadows = LightShadows.None; rimLight.cullingMask = 1 << ScreamLayer;
    }

    private void ExitFaceMode()
    {
        if (!FaceMode) return;
        FaceMode = false;
        for (int i = 0; i < layerTs.Count; i++) if (layerTs[i] != null) layerTs[i].gameObject.layer = layerOld[i];
        layerTs.Clear(); layerOld.Clear();
        for (int i = 0; i < dimmedLights.Count; i++) if (dimmedLights[i] != null) dimmedLights[i].cullingMask = dimmedMasks[i];
        dimmedLights.Clear(); dimmedMasks.Clear();
        if (killerVisual != null && killerVisual.model != null) killerVisual.model.localScale = savedModelScale;
        if (camComp != null) { camComp.cullingMask = savedMask; camComp.clearFlags = savedClear; camComp.backgroundColor = savedBg; camComp.nearClipPlane = savedNear; }
        RenderSettings.fog = savedFog; RenderSettings.ambientLight = savedAmbient; RenderSettings.ambientMode = savedAmbientMode;
        if (keyLight != null) ProceduralAssets.SafeDestroy(keyLight.gameObject);
        if (rimLight != null) ProceduralAssets.SafeDestroy(rimLight.gameObject);
    }

    // ¿se ve el monstruo? (renderers dentro del frustum de la cámara y en su máscara) y cuánto ocupa la cabeza
    private void MeasureVisibility()
    {
        MonsterVisibleToCamera = false; HeadScreenFraction = 0f;
        if (camComp == null || killerVisual == null || killerVisual.Renderers == null) return;
        Plane[] pl = GeometryUtility.CalculateFrustumPlanes(camComp);
        foreach (Renderer r in killerVisual.Renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if ((camComp.cullingMask & (1 << r.gameObject.layer)) == 0) continue;
            if (GeometryUtility.TestPlanesAABB(pl, r.bounds)) { MonsterVisibleToCamera = true; break; }
        }
        Vector3 a = camComp.WorldToViewportPoint(headPoint + Vector3.up * 0.08f * HeadHeight), b2 = camComp.WorldToViewportPoint(headPoint - Vector3.up * 0.08f * HeadHeight);
        HeadScreenFraction = a.z > 0f ? Mathf.Abs(a.y - b2.y) : 0f;
    }

    // ------------------------------------------------------------------
    private void UpdateFaceLights(Vector3 camPos, Quaternion camRot, float k)
    {
        if (keyLight == null || rimLight == null) return;
        Vector3 fwd = FaceForward(), right = Vector3.Cross(Vector3.up, new Vector3(fwd.x, 0f, fwd.z).normalized);
        float H = HeadHeight;
        float lm = Profile != null ? Profile.screamLight : 1f;
        keyLight.transform.position = camPos + camRot * new Vector3(0.35f, 0.3f, -0.2f);
        keyLight.transform.rotation = Quaternion.LookRotation(headPoint - keyLight.transform.position);
        keyLight.intensity = (2.3f + (Soft ? 0f : 0.5f * Mathf.Sin(T * 17f))) * (Soft ? 0.85f : 1f) * lm;
        rimLight.transform.position = headPoint - fwd * 0.6f * (H / 2f) + right * 0.5f + Vector3.up * 0.25f * H;
        rimLight.intensity = (1.3f + (Soft ? 0f : 0.7f * Mathf.PerlinNoise(T * 13f, seed + 5f))) * (Soft ? 0.8f : 1f) * Mathf.Lerp(1f, lm, 0.6f);
    }

    private static float EaseIn(float x) { x = Mathf.Clamp01(x); return x * x * x; }
    private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }

    private void LateUpdate()
    {
        if (CurrentPhase == Phase.Idle || cam == null) return;
        float dt = Time.unscaledDeltaTime;
        if (PreviewSeek >= 0f) T = PreviewSeek; else T += dt;
        float k = Intensity;

        // actualizar posición de la cara (el monstruo se mueve / se anima)
        if (killer != null)
        {
            if (faceDirFixed.sqrMagnitude > 0.01f) killer.rotation = Quaternion.LookRotation(faceDirFixed);
            ComputeHead(); headWorld = headPoint;
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
                closeWorldPos = ShotPose(0f, 1f).pos;
                pos = Vector3.Lerp(baseWorldPos, closeWorldPos, move);
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
            {
                Pose pa = ShotPose(0f, k);
                pos = pa.pos; rot = pa.rot; fovNow = pa.fov;
                if (flashlight != null && flashlight.spot != null) flashlight.spot.enabled = false;
                BlackCutShown = true;
                EnterFaceMode();
                break;
            }
            default:
            {
                EnterFaceMode();
                float f = Mathf.Max(0f, T - cutEnd);
                float decay = Mathf.Exp(-f * 1.6f);
                Pose ps = ShotPose(f, k);
                pos = ps.pos; rot = ps.rot; fovNow = ps.fov;
                Shot = ps.shot; FaceDistance = ps.dist;
                if (ps.shot != lastShot) { lastShot = ps.shot; shotsSeen++; cutDip = Soft ? 0f : 0.05f; }
                // pulso de escala (embestida del plano C) y ligera deformación de la cabeza
                if (killerVisual != null && killerVisual.model != null)
                    killerVisual.model.localScale = savedModelScale * (1f + (ps.shot == 3 ? 0.07f : 0.025f) * Mathf.Sin(T * 23f) * k);
                if (headBone != null)
                    headBone.rotation = headBone.rotation * Quaternion.Euler((Mathf.PerlinNoise(T * 14f, seed + 3f) - 0.5f) * 16f * k, (Mathf.PerlinNoise(T * 12f, seed + 9f) - 0.5f) * 22f * k, 0f);
                float roll = ((Mathf.PerlinNoise(T * 9f, seed + 21f) - 0.5f) * 2f) * (ps.shot == 1 ? 3f : ps.shot == 2 ? 6f : 8f) * k;
                rot = rot * Quaternion.Euler(0f, 0f, roll);
                fovNow += (Mathf.PerlinNoise(T * 31f, seed) - 0.5f) * 5f * k * decay;
                shake = (0.004f + 0.02f * decay) * k;
                if (CurrentPhase == Phase.Fade) shake *= 0.5f;
                UpdateFaceLights(pos, rot, k);
                MeasureVisibility();
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
        else if (CurrentPhase == Phase.Face && cutDip > 0f) { blackA = 1f; cutDip -= dt; }   // corte seco entre planos
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
            r = (0.07f + 0.3f * Mathf.Abs(Mathf.Sin(T * 26f)) * decay) * k;   // la cara debe leerse: rojo moderado
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
            case Phase.Face: level = 0.3f - 0.1f * Mathf.Clamp01((T - cutStart - cutLength) / 1.5f); break;   // glitch reducido: la cara se lee
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
        if (bakeMesh != null) Destroy(bakeMesh);
        if (Active == this) Active = null;
        ExitFaceMode();
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
