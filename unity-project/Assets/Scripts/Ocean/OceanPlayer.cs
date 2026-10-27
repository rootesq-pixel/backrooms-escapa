using UnityEngine;
using UnityEngine.UI;

// v7.3.0 Talasofobia: lo que el mar le hace al jugador local.
//  · Aire: sin casco, bajo el agua aguantas ~12 s; luego te ahogas (muerte / derribo en multijugador). En la superficie se recupera.
//  · Cansancio: nadar gasta resistencia; agotado nadas más despacio (no te ahogas por cansancio en la superficie).
//  · Casco de buzo: aire ilimitado, nado libre en 3D (hacia donde miras; Espacio sube, Ctrl baja) y lámpara propia (F).
//  · Interacción: E sube/baja de la barca; mantener clic izquierdo coge un casco del soporte; escaleras desde el agua.
public class OceanPlayer : MonoBehaviour
{
    public static OceanPlayer Local;
    public FirstPersonController fpc;
    public Flashlight flashlight;
    public Camera cam;

    public const float BreathSeconds = 12f, DrownGrace = 1.5f;
    public float Air01 { get; private set; } = 1f;
    public bool HasHelmet { get; private set; }
    public bool LampOn { get { return flashlight != null && flashlight.IsOn; } }
    public bool Dead { get; private set; }
    public float HelmetHold01 { get; private set; }
    public int DrownCount { get; private set; }
    public string Prompt { get; private set; }
    public const float ObjectiveSeconds = 10f;
    public float ObjectiveT { get; private set; }
    private bool bottomSeen;
    [System.NonSerialized] public bool debugHold, debugInteract;

    private float drownT, holdT, savedRange, savedAngle, savedIntensity, savedDrain;
    private bool interactPrev;
    private Text promptText, airText;
    public RectTransform PromptRect { get { return promptText != null ? promptText.rectTransform : null; } }
    public RectTransform AirRect { get { return airBarBg != null ? airBarBg.rectTransform : null; } }
    public RectTransform AirTextRect { get { return airText != null ? airText.rectTransform : null; } }
    private Image vignette, airBar, airBarBg;
    private GameObject hudRoot;

    public static OceanPlayer Attach(FirstPersonController fpc, Flashlight fl, Camera cam)
    {
        OceanPlayer p = fpc.gameObject.AddComponent<OceanPlayer>();
        p.fpc = fpc; p.flashlight = fl; p.cam = cam;
        fpc.swimStaminaDrain = 3.2f;   // ~30 s de brazadas seguidas antes de agotarse
        Local = p;
        p.BuildHud();
        return p;
    }

    private void OnDestroy()
    {
        if (Local == this) Local = null;
        InventoryUI.LevelObjective = null;
        if (hudRoot != null) Destroy(hudRoot);
    }

    public void SetHelmet(bool on)
    {
        if (HasHelmet == on) return;
        HasHelmet = on;
        fpc.freeSwim = on;
        if (flashlight != null)
        {
            if (on)
            {
                savedRange = flashlight.range; savedAngle = flashlight.spotAngle; savedIntensity = flashlight.maxIntensity; savedDrain = flashlight.drainPerSecond;
                flashlight.range = 22f; flashlight.spotAngle = 64f; flashlight.maxIntensity = Mathf.Max(3.2f, savedIntensity); flashlight.drainPerSecond = 0f;
                flashlight.AddBattery(100f);
                flashlight.Reconfigure();
                if (!flashlight.IsOn) flashlight.Toggle();
            }
            else
            {
                flashlight.range = savedRange; flashlight.spotAngle = savedAngle; flashlight.maxIntensity = savedIntensity; flashlight.drainPerSecond = savedDrain;
                flashlight.Reconfigure();
            }
        }
        if (vignette != null) vignette.enabled = on;
        if (on && halo == null && cam != null)
        {
            // halo de la lámpara del casco: luz difusa corta que simula la dispersión en el agua (la cónica sola deja negras las paredes cercanas)
            GameObject hg = new GameObject("HelmetHalo");
            hg.transform.SetParent(cam.transform, false);
            hg.transform.localPosition = new Vector3(0f, 0.05f, 0.6f);
            halo = hg.AddComponent<Light>();
            halo.type = LightType.Point; halo.range = 14f; halo.intensity = 1.6f;
            halo.color = new Color(0.8f, 0.95f, 1f); halo.shadows = LightShadows.None;
            halo.renderMode = LightRenderMode.ForcePixel;
        }
        if (halo != null) halo.enabled = on && flashlight != null && flashlight.IsOn;
        Air01 = 1f;
    }

    private Light halo;

    private void Update()
    {
        if (halo != null) halo.enabled = HasHelmet && flashlight != null && flashlight.IsOn;
        if (fpc == null) return;
        float dt = Time.deltaTime;
        GameManager gm = GameManager.Instance;
        bool playing = gm == null || gm.State == GameManager.GameState.Playing;
        bool downed = NetSession.Active && NetSession.Instance.LocalDowned;
        Prompt = null;
        if (!playing || Dead) { UpdateHud(); return; }

        // ---- aire
        bool under = fpc.IsUnderwater && !HasHelmet && !fpc.IsMounted;
        if (under && !downed)
        {
            Air01 = Mathf.Max(0f, Air01 - dt / BreathSeconds);
            if (Air01 <= 0f)
            {
                drownT += dt;
                if (drownT > DrownGrace) Drown();
            }
        }
        else
        {
            Air01 = Mathf.Min(1f, Air01 + dt * (fpc.IsUnderwater ? 0f : 0.33f));
            drownT = 0f;
        }

        // ---- interacción
        OceanWorld w = OceanWorld.Active;
        bool interact = debugInteract || (!UIManager.InputBlocked && Input.GetKey(KeyCode.E));
        bool interactDown = interact && !interactPrev; interactPrev = interact;
        if (w != null && !downed)
        {
            // barca
            if (w.boat != null)
            {
                if (w.boat.LocalSeat >= 0)
                {
                    Prompt = w.boat.LocalSeat == 0 ? "W/S: avanzar · A/D: girar · E: bajar de la barca" : "E: bajar de la barca  (la barca va a la deriva hacia la siguiente casa)";
                    if (interactDown && !ReviveTargetNear()) OceanNet.Request(OceanNet.OpLeave, 0, 0f, 0f);
                    if (w.boat.LocalSeat == 0)
                    {
                        float th = UIManager.InputBlocked ? 0f : Input.GetAxisRaw("Vertical"), st = UIManager.InputBlocked ? 0f : Input.GetAxisRaw("Horizontal");
                        if (fpc.debugInput) { th = fpc.debugMove.y; st = fpc.debugMove.x; }
                        w.boat.LocalDrive(th, st);
                    }
                }
                else if (!fpc.IsMounted && w.boat.CanBoardFrom(transform.position))
                {
                    Prompt = "E: subir a la barca";
                    if (interactDown && !ReviveTargetNear()) OceanNet.Request(OceanNet.OpBoard, 0, 0f, 0f);
                }
            }
            // cascos
            if (!HasHelmet && w.rack != null && !fpc.IsMounted && w.rack.LookingAt(cam != null ? cam.transform : transform, out int slot))
            {
                bool hold = debugHold || (!UIManager.InputBlocked && Input.GetMouseButton(0));
                holdT = hold ? holdT + dt : 0f;
                HelmetHold01 = Mathf.Clamp01(holdT / 1.1f);
                Prompt = "Mantén clic izquierdo: ponerte el casco de buzo" + (HelmetHold01 > 0.01f ? "  " + Mathf.RoundToInt(HelmetHold01 * 100f) + "%" : "");
                if (HelmetHold01 >= 1f) { holdT = 0f; HelmetHold01 = 0f; OceanNet.Request(OceanNet.OpHelmet, slot, 0f, 0f); }
            }
            else { holdT = 0f; HelmetHold01 = 0f; }
            // jaula
            if (w.cage != null && w.cage.LocalInside && Prompt == null) Prompt = w.cage.PromptFor(HasHelmet);
            // v7.4.0: al llegar al fondo, el objetivo se queda unos segundos en pantalla (también al salir de la jaula) y en el inventario
            if (w.cage != null)
            {
                bool atBottom = w.cage.State == OceanCage.CageState.BottomOpen && (w.cage.LocalInside || fpc.transform.position.y < OceanWorld.PocketCenter.y - 15f);
                if (atBottom && !bottomSeen) { bottomSeen = true; ObjectiveT = ObjectiveSeconds; InventoryUI.LevelObjective = OceanCage.BottomObjective.ToLowerInvariant().Substring(0, 1) + OceanCage.BottomObjective.Substring(1) + "."; }
            }
            if (ObjectiveT > 0f) { ObjectiveT -= dt; if (Prompt == null || (w.cage != null && w.cage.LocalInside)) Prompt = OceanCage.BottomObjective; }
            // escaleras desde el agua
            foreach (OceanLadder l in w.Ladders) if (l.TryClimb(fpc)) break;
            if (Prompt == null && fpc.IsSwimming && !HasHelmet && Air01 < 0.6f) Prompt = "¡Sal a respirar!";
            if (Prompt == null && fpc.SwimExhausted) Prompt = "Agotado: busca dónde agarrarte";
        }
        UpdateHud();
    }

    private bool ReviveTargetNear()
    {
        return NetSession.Active && NetSession.Instance.LocalReviveCandidate();
    }

    private void Drown()
    {
        drownT = 0f; DrownCount++;
        if (!NetSession.Active) { Dead = true; OceanNet.HostKill(0, OceanNet.KillDrown); return; }
        if (NetSession.IsHost) OceanNet.HostKill(OceanNet.LocalId, OceanNet.KillDrown);
        else OceanNet.Request(OceanNet.OpDrown, 0, 0f, 0f);
        Air01 = 1f;
    }

    // ---------------------------------------------------------------- HUD: aviso, barra de aire y viñeta del casco
    private void BuildHud()
    {
        hudRoot = new GameObject("OceanHud");
        Canvas c = hudRoot.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 40;
        CanvasScaler cs = hudRoot.AddComponent<CanvasScaler>(); cs.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; cs.referenceResolution = new Vector2(1920f, 1080f); cs.matchWidthOrHeight = 0.5f;
        Font f = ProceduralAssets.GetDefaultFont();
        vignette = NewImage(hudRoot.transform, "HelmetVignette", Vector2.zero, Vector2.one, HelmetVignette());
        vignette.raycastTarget = false; vignette.enabled = false;
        // v7.9.2: aviso y aire por encima de la barra rápida y del aviso breve (antes en % de la pantalla: pisaban los medidores)
        promptText = NewText(hudRoot.transform, f, 30, Vector2.zero, Vector2.zero);
        promptText.horizontalOverflow = HorizontalWrapMode.Wrap; promptText.verticalOverflow = VerticalWrapMode.Truncate;
        promptText.resizeTextForBestFit = true; promptText.resizeTextMinSize = 14; promptText.resizeTextMaxSize = 30; promptText.alignment = TextAnchor.LowerCenter;
        HudBottom.Place(promptText.rectTransform, HudBottom.OceanPromptY, HudBottom.PromptMaxWidth, HudBottom.PromptHeight);
        airBarBg = NewImage(hudRoot.transform, "AirBg", Vector2.zero, Vector2.zero, null); airBarBg.color = new Color(0f, 0f, 0f, 0.45f);
        HudBottom.Place(airBarBg.rectTransform, HudBottom.OceanAirY, HudBottom.AirBarWidth, HudBottom.AirBarHeight);
        airBar = NewImage(airBarBg.transform, "Air", Vector2.zero, Vector2.one, null); airBar.color = new Color(0.7f, 0.9f, 1f, 0.85f);
        airText = NewText(hudRoot.transform, f, 22, Vector2.zero, Vector2.zero);
        HudBottom.Place(airText.rectTransform, HudBottom.OceanAirTextY, HudBottom.AirBarWidth, HudBottom.AirTextHeight);
    }

    private static Texture2D vigTex;
    private static Sprite vigSprite;
    private static Sprite HelmetVignette()
    {
        if (vigSprite != null) return vigSprite;
        const int N = 256;
        vigTex = new Texture2D(N, N, TextureFormat.RGBA32, false); vigTex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
            float r = Mathf.Sqrt(u * u * 0.62f + v * v);   // ojo de buey algo apaisado
            float a = Mathf.SmoothStep(0f, 1f, (r - 0.78f) / 0.2f);
            float rim = Mathf.Exp(-Mathf.Pow((r - 0.86f) / 0.035f, 2f)) * 0.35f;
            px[y * N + x] = new Color32((byte)(40 * rim * 255 / 40), (byte)(36 * rim * 255 / 40), (byte)(30 * rim * 255 / 40), (byte)(Mathf.Clamp01(a * 0.96f + rim) * 255));
        }
        vigTex.SetPixels32(px); vigTex.Apply();
        vigSprite = Sprite.Create(vigTex, new Rect(0, 0, N, N), new Vector2(0.5f, 0.5f));
        return vigSprite;
    }

    private static Image NewImage(Transform p, string n, Vector2 a0, Vector2 a1, Sprite s)
    {
        GameObject g = new GameObject(n); g.transform.SetParent(p, false);
        RectTransform rt = g.AddComponent<RectTransform>(); rt.anchorMin = a0; rt.anchorMax = a1; rt.offsetMin = rt.offsetMax = Vector2.zero;
        Image i = g.AddComponent<Image>(); i.sprite = s; i.raycastTarget = false;
        return i;
    }

    private static Text NewText(Transform p, Font f, int size, Vector2 a0, Vector2 a1)
    {
        GameObject g = new GameObject("Text"); g.transform.SetParent(p, false);
        RectTransform rt = g.AddComponent<RectTransform>(); rt.anchorMin = a0; rt.anchorMax = a1; rt.offsetMin = rt.offsetMax = Vector2.zero;
        Text t = g.AddComponent<Text>(); t.font = f; t.fontSize = size; t.alignment = TextAnchor.MiddleCenter; t.color = new Color(0.95f, 0.95f, 0.92f, 0.95f);
        t.raycastTarget = false;
        Outline o = g.AddComponent<Outline>(); o.effectColor = new Color(0f, 0f, 0f, 0.7f); o.effectDistance = new Vector2(1.5f, -1.5f);
        return t;
    }

    private void UpdateHud()
    {
        if (hudRoot == null) return;
        bool hide = UIManager.InputBlocked || (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing);
        if (promptText != null) promptText.text = hide ? "" : (Prompt ?? "");
        if (promptText != null) promptText.rectTransform.sizeDelta = new Vector2(HudBottom.PromptWidth(((RectTransform)hudRoot.transform).rect.width), HudBottom.PromptHeight);
        bool showAir = !hide && !HasHelmet && Air01 < 0.999f;
        if (airBarBg != null) airBarBg.enabled = showAir;
        if (airBar != null)
        {
            airBar.enabled = showAir;
            airBar.rectTransform.anchorMax = new Vector2(Air01, 1f);
            airBar.color = Air01 < 0.3f ? new Color(1f, 0.45f, 0.35f, 0.9f) : new Color(0.7f, 0.9f, 1f, 0.85f);
        }
        if (airText != null) airText.text = showAir ? "AIRE" : "";
        if (vignette != null) vignette.enabled = HasHelmet && !hide;
    }
}
