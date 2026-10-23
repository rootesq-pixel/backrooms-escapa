using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Menús del juego (UnityEngine.UI legacy, construidos desde código, en español):
//   Menú principal (Jugar, Seleccionar nivel, Opciones, Créditos, Salir), menú de pausa (Esc), opciones, créditos,
//   selección de nivel, inventario (I) y panel de victoria con "Menú principal".
// Gestiona el cursor, Time.timeScale y la pausa del audio. Persiste durante toda la sesión (hijo de GameBootstrap).
public class UIManager : MonoBehaviour
{
    public enum Screen { None, Main, Levels, Options, Credits, Pause, Inventory, End, Multi, Lobby }

    public static UIManager Instance { get; private set; }

    // true cuando hay un menú abierto y el jugador no debe moverse/mirar
    public static bool InputBlocked { get { return Instance != null && (Instance.current != Screen.None || MultiplayerUI.ChatTyping); } }

    public Screen Current { get { return current; } }
    public bool GameplayActive { get { return Instance != null && bootstrap != null && !bootstrap.InMenu && bootstrap.IsLoaded && GameManager.Instance != null; } }

    private GameBootstrap bootstrap;
    private Screen current = Screen.None;
    private Screen optionsReturn = Screen.Main;
    private Canvas canvas;
    private GameObject pMain, pLevels, pOptions, pCredits, pPause, pInventory, pEnd, pMulti, pLobby;
    private MultiplayerUI mpUi;
    private Text pauseTitle;
    public MultiplayerUI Multiplayer { get { return mpUi; } }
    private InventoryUI invUi;
    private Font font;
    private AudioSource clickSrc;
    private AudioClip clickClip;
    private Text endText;
    private readonly List<Action> refreshers = new List<Action>();
    private Image fader;

    // Colores coherentes con el HUD
    private static readonly Color TextCol = new Color(0.95f, 0.92f, 0.75f);
    private static readonly Color Dim = new Color(0.7f, 0.66f, 0.5f);
    private static readonly Color PanelCol = new Color(0.03f, 0.03f, 0.02f, 0.82f);
    private static readonly Color BtnCol = new Color(0.10f, 0.09f, 0.05f, 0.92f);
    private static readonly Color BtnHi = new Color(0.32f, 0.27f, 0.10f, 1f);

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------
    public void Init(GameBootstrap b)
    {
        bootstrap = b;
    }

    private void Awake()
    {
        Instance = this;
        font = UiStyle.Sans();   // Escapa Sans Condensed (IBM Plex) incluida en Resources/Fonts
        EnsureEventSystem();
        BuildCanvas();
        clickClip = ProceduralAudio.CreateUiClick();
        clickSrc = gameObject.AddComponent<AudioSource>();
        clickSrc.spatialBlend = 0f;
        clickSrc.ignoreListenerPause = true;
        clickSrc.playOnAwake = false;
        clickSrc.volume = 0.6f;
        SetScreen(Screen.None);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        ProceduralAssets.SafeDestroy(clickClip);
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    // Event System con StandaloneInputModule (Input Manager clásico)
    private void EnsureEventSystem()
    {
        EventSystem es = EventSystem.current != null ? EventSystem.current : FindObjectOfType<EventSystem>();
        if (es == null)
        {
            GameObject go = new GameObject("EventSystem");
            go.transform.SetParent(transform, false);
            es = go.AddComponent<EventSystem>();
        }
        if (es.GetComponent<StandaloneInputModule>() == null && es.GetComponent<BaseInputModule>() == null)
            es.gameObject.AddComponent<StandaloneInputModule>();
    }

    private void Update()
    {
        if (bootstrap == null) return;

        bool inGame = GameplayActive;
        GameManager gm = GameManager.Instance;

        // Panel de victoria
        if (inGame && gm.State == GameManager.GameState.Won)
        {
            if (current != Screen.End) { SetScreen(Screen.End); }
        }
        else if (current == Screen.End && !(inGame && gm.State == GameManager.GameState.Won))
        {
            SetScreen(Screen.None);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (MultiplayerUI.ChatTyping && mpUi != null) mpUi.CloseChat();
            else OnEscape(inGame, gm);
        }

        if (inGame && gm.State == GameManager.GameState.Playing)
        {
            if (!MultiplayerUI.ChatTyping && Input.GetKeyDown(KeyCode.I)) ToggleInventory();
            if (!MultiplayerUI.ChatTyping && current == Screen.None && Input.GetKeyDown(KeyCode.B)) UseBattery();
        }

        if (current == Screen.Pause && !inGame) SetScreen(Screen.None);
    }

    // Para pruebas: equivalentes a pulsar Esc / I
    public void PressEscape()
    {
        GameManager gm = GameManager.Instance;
        OnEscape(bootstrap != null && !bootstrap.InMenu && gm != null, gm);
    }

    public void ToggleInventory()
    {
        GameManager gm = GameManager.Instance;
        if (bootstrap == null || bootstrap.InMenu || gm == null || gm.State != GameManager.GameState.Playing) return;
        if (current == Screen.None) SetScreen(Screen.Inventory);
        else if (current == Screen.Inventory) SetScreen(Screen.None);
    }

    private void OnEscape(bool inGame, GameManager gm)
    {
        switch (current)
        {
            case Screen.None:
                if (inGame && gm.State == GameManager.GameState.Playing && bootstrap.IsLoaded) SetScreen(Screen.Pause);
                break;
            case Screen.Pause: SetScreen(Screen.None); break;
            case Screen.Inventory: SetScreen(Screen.None); break;
            case Screen.Options: SetScreen(optionsReturn); break;
            case Screen.Credits:
            case Screen.Levels: SetScreen(Screen.Main); break;
            case Screen.Multi:
                if (mpUi != null && mpUi.CurrentPage != MultiplayerUI.Page.Menu)
                {
                    if (NetSession.Instance != null && NetSession.Instance.Connecting) NetSession.Instance.Leave(null);
                    mpUi.ShowPage(MultiplayerUI.Page.Menu);
                }
                else SetScreen(Screen.Main);
                break;
            case Screen.Lobby: if (mpUi != null) mpUi.OnLeaveLobby(); break;
        }
    }

    // ------------------------------------------------------------------
    // Estados
    // ------------------------------------------------------------------
    public void SetScreen(Screen s)
    {
        Screen previous = current;
        current = s;
        if (pMain != null)
        {
            pMain.SetActive(s == Screen.Main);
            pLevels.SetActive(s == Screen.Levels);
            pOptions.SetActive(s == Screen.Options);
            pCredits.SetActive(s == Screen.Credits);
            pPause.SetActive(s == Screen.Pause);
            if (pMulti != null) pMulti.SetActive(s == Screen.Multi);
            if (pLobby != null) pLobby.SetActive(s == Screen.Lobby);
            if (s == Screen.Pause && pauseTitle != null) pauseTitle.text = NetSession.Active ? "PAUSA\n<size=34>(la partida sigue)</size>" : "PAUSA";
            if (s == Screen.Inventory) { if (invUi != null) invUi.Open(); }
            else if (invUi != null)
            {
                // desde el inventario hacia el juego: animación de cierre; hacia otra pantalla: se oculta ya
                if (previous == Screen.Inventory && s == Screen.None) invUi.Close(); else invUi.CloseImmediate();
            }
            pEnd.SetActive(s == Screen.End);
        }

        bool freezeTime = (s == Screen.Pause || s == Screen.Inventory ||
            ((s == Screen.Options) && optionsReturn == Screen.Pause)) && !NetSession.Active;   // en multijugador el mundo no se detiene
        Time.timeScale = freezeTime ? 0f : 1f;
        AudioListener.pause = freezeTime;

        bool showCursor = s != Screen.None;
        if (showCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (GameplayActive)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (s == Screen.Options) RefreshOptions();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    public void ShowMainMenu() { optionsReturn = Screen.Main; SetScreen(Screen.Main); }
    public void HideAll() { SetScreen(Screen.None); }

    public void UseBattery()
    {
        Inventory inv = Inventory.Instance;
        GameManager gm = GameManager.Instance;
        if (inv == null) return;
        bool ok = inv.UseBattery();
        if (ok)
        {
            Click();
            if (gm != null) gm.ShowToast("Pila usada: linterna recargada");
        }
        else if (gm != null)
        {
            gm.ShowToast(inv.BatteryCount <= 0 ? "No te quedan pilas" : "La linterna ya está llena");
        }
    }

    public InventoryUI InventoryUi { get { return invUi; } }
    public void PlayClick() { Click(); }

    // Acciones de los botones ---------------------------------------------
    private void Click()
    {
        if (clickSrc != null && clickClip != null) clickSrc.PlayOneShot(clickClip, AudioMix.S(1f));
    }

    public void OnPlay() { Click(); StartGame(0); }

    public void StartGame(int level)
    {
        SetScreen(Screen.None);
        Time.timeScale = 1f;
        AudioListener.pause = false;
        bootstrap.StartGame(level);
    }

    private void OnResume() { Click(); SetScreen(Screen.None); }

    private void OnRestartLevel()
    {
        Click();
        SetScreen(Screen.None);
        if (GameManager.Instance != null) GameManager.Instance.Restart();
    }

    public void OnMainMenu()
    {
        Click();
        if (NetSession.Active) NetSession.Instance.Leave(null);
        SetScreen(Screen.None);
        Time.timeScale = 1f;
        AudioListener.pause = false;
        bootstrap.GoToMainMenu();
    }

    private void OnRestartRun()
    {
        Click();
        SetScreen(Screen.None);
        if (GameManager.Instance != null) GameManager.Instance.RestartRun();
    }

    private void OnQuit()
    {
        Click();
        Time.timeScale = 1f;
        AudioListener.pause = false;
        GameSettings.Save();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OpenOptions(Screen from)
    {
        Click();
        optionsReturn = from;
        SetScreen(Screen.Options);
    }

    // ------------------------------------------------------------------
    // Construcción de la UI
    // ------------------------------------------------------------------
    private void BuildCanvas()
    {
        GameObject cg = new GameObject("MenuCanvas");
        cg.transform.SetParent(transform, false);
        canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        pMain = BuildMain(cg.transform);
        pLevels = BuildLevels(cg.transform);
        pOptions = BuildOptions(cg.transform);
        pCredits = BuildCredits(cg.transform);
        pPause = BuildPause(cg.transform);
        pInventory = BuildInventory(cg.transform);
        pEnd = BuildEnd(cg.transform);
        mpUi = gameObject.AddComponent<MultiplayerUI>();
        mpUi.Build(this, cg.transform);
        pMulti = mpUi.MultiRoot;
        pLobby = mpUi.LobbyRoot;
    }

    // --- helpers ---
    public RectTransform Panel(Transform parent, string name, Color color, bool fullscreen)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        RectTransform rt = img.rectTransform;
        if (fullscreen)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
        else
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
        }
        return rt;
    }

    public Text Label(Transform parent, string content, int size, TextAnchor anchor, float x, float y, float w, float h, Color? color = null)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.alignment = anchor;
        t.color = color ?? TextCol; t.text = content;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
        return t;
    }

    public Button MakeButton(Transform parent, string label, float x, float y, float w, float h, UnityEngine.Events.UnityAction onClick, int fontSize = 34)
    {
        GameObject go = new GameObject("Btn_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = BtnCol;
        Button b = go.AddComponent<Button>();
        ColorBlock cb = b.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.9f, 1.7f, 1.0f, 1f);
        cb.pressedColor = new Color(2.2f, 2.0f, 1.3f, 1f);
        cb.selectedColor = new Color(1.5f, 1.4f, 1f, 1f);
        cb.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        cb.fadeDuration = 0.08f;
        b.colors = cb;
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
        Text t = Label(go.transform, label, fontSize, TextAnchor.MiddleCenter, 0f, 0f, w, h);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        return b;
    }

    private Slider MakeSlider(Transform parent, float x, float y, float w, float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange)
    {
        GameObject go = new GameObject("Slider", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, 30f);
        rt.anchoredPosition = new Vector2(x, y);

        Image bg = Panel(go.transform, "Background", new Color(0.12f, 0.11f, 0.07f, 1f), true).GetComponent<Image>();
        bg.rectTransform.anchorMin = new Vector2(0f, 0.3f); bg.rectTransform.anchorMax = new Vector2(1f, 0.7f);

        GameObject fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        RectTransform fa = (RectTransform)fillArea.transform;
        fa.anchorMin = new Vector2(0f, 0.3f); fa.anchorMax = new Vector2(1f, 0.7f); fa.offsetMin = new Vector2(5f, 0f); fa.offsetMax = new Vector2(-5f, 0f);
        GameObject fill = new GameObject("Fill", typeof(RectTransform));
        fill.transform.SetParent(fillArea.transform, false);
        Image fi = fill.AddComponent<Image>(); fi.color = new Color(0.85f, 0.7f, 0.2f, 1f);
        RectTransform fr = fi.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(0.5f, 1f); fr.offsetMin = fr.offsetMax = Vector2.zero;

        GameObject handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        RectTransform ha = (RectTransform)handleArea.transform;
        ha.anchorMin = Vector2.zero; ha.anchorMax = Vector2.one; ha.offsetMin = new Vector2(10f, 0f); ha.offsetMax = new Vector2(-10f, 0f);
        GameObject handle = new GameObject("Handle", typeof(RectTransform));
        handle.transform.SetParent(handleArea.transform, false);
        Image hi = handle.AddComponent<Image>(); hi.color = new Color(1f, 0.93f, 0.6f, 1f);
        RectTransform hr = hi.rectTransform; hr.sizeDelta = new Vector2(20f, 0f); hr.anchorMin = new Vector2(0f, 0f); hr.anchorMax = new Vector2(0f, 1f);

        Slider s = go.AddComponent<Slider>();
        s.fillRect = fr; s.handleRect = hr; s.targetGraphic = hi;
        s.direction = Slider.Direction.LeftToRight;
        s.minValue = min; s.maxValue = max; s.value = value;
        s.onValueChanged.AddListener(onChange);
        return s;
    }

    private Toggle MakeToggle(Transform parent, string label, float x, float y, bool value, UnityEngine.Events.UnityAction<bool> onChange)
    {
        GameObject go = new GameObject("Toggle_" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(640f, 40f);
        rt.anchoredPosition = new Vector2(x, y);
        Image box = Panel(go.transform, "Box", new Color(0.12f, 0.11f, 0.07f, 1f), false).GetComponent<Image>();
        box.rectTransform.anchorMin = box.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        box.rectTransform.pivot = new Vector2(0f, 0.5f);
        box.rectTransform.sizeDelta = new Vector2(34f, 34f);
        box.rectTransform.anchoredPosition = Vector2.zero;
        Image check = Panel(box.transform, "Check", new Color(0.95f, 0.8f, 0.25f, 1f), true).GetComponent<Image>();
        check.rectTransform.offsetMin = new Vector2(6f, 6f); check.rectTransform.offsetMax = new Vector2(-6f, -6f);
        Text t = Label(go.transform, label, 30, TextAnchor.MiddleLeft, 0f, 0f, 580f, 40f);
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        t.rectTransform.pivot = new Vector2(0f, 0.5f);
        t.rectTransform.anchoredPosition = new Vector2(50f, 0f);
        Toggle tg = go.AddComponent<Toggle>();
        tg.targetGraphic = box; tg.graphic = check; tg.isOn = value;
        tg.onValueChanged.AddListener(onChange);
        return tg;
    }

    private GameObject NewPanelRoot(Transform parent, string name, Color bg)
    {
        RectTransform r = Panel(parent, name, bg, true);
        return r.gameObject;
    }

    // --- Menú principal ---
    // v7.2.0: el nº de niveles sale de LevelConfig.Count (antes «Cinco niveles» fijo, ya desfasado)
    public static string MainTagline { get { return CountWord(LevelConfig.Count) + " niveles. Una salida. Algo te está buscando."; } }
    public static string CountWord(int n)
    {
        string[] w = { "Cero", "Un", "Dos", "Tres", "Cuatro", "Cinco", "Seis", "Siete", "Ocho", "Nueve", "Diez", "Once", "Doce" };
        return n >= 0 && n < w.Length ? w[n] : n.ToString();
    }
    private GameObject BuildMain(Transform parent)
    {
        GameObject root = NewPanelRoot(parent, "MainMenu", new Color(0f, 0f, 0f, 0.35f));
        Text title = Label(root.transform, "BACKROOMS: ESCAPA", 118, TextAnchor.MiddleCenter, 0f, 330f, 1700f, 180f, new Color(0.96f, 0.9f, 0.55f));
        title.fontStyle = FontStyle.Bold;
        Shadow sh = title.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.9f); sh.effectDistance = new Vector2(4f, -4f);
        Label(root.transform, MainTagline, 32, TextAnchor.MiddleCenter, 0f, 235f, 1400f, 50f, Dim);

        float y = 150f;
        MakeButton(root.transform, "Jugar", 0f, y, 520f, 68f, OnPlay); y -= 78f;
        MakeButton(root.transform, "Seleccionar nivel", 0f, y, 520f, 68f, () => { Click(); SetScreen(Screen.Levels); }); y -= 78f;
        MakeButton(root.transform, "Multijugador", 0f, y, 520f, 68f, () => { Click(); SetScreen(Screen.Multi); if (mpUi != null) mpUi.ShowPage(MultiplayerUI.Page.Menu); }); y -= 78f;
        MakeButton(root.transform, "Opciones", 0f, y, 520f, 68f, () => OpenOptions(Screen.Main)); y -= 78f;
        MakeButton(root.transform, "Créditos", 0f, y, 520f, 68f, () => { Click(); SetScreen(Screen.Credits); }); y -= 78f;
        MakeButton(root.transform, "Salir", 0f, y, 520f, 68f, OnQuit);

        Text credit = Label(root.transform, CreditsLine(), 22, TextAnchor.LowerCenter, 0f, -490f, 1800f, 80f, Dim);
        credit.name = "CreditsLine";
        return root;
    }

    public static string CreditsLine()
    {
        List<string> authors = new List<string>();
        foreach (MonsterProfile p in MonsterProfile.All)
        {
            string a = p.modelAuthor;
            if (!authors.Contains(a)) authors.Add(a);
        }
        return "Modelos 3D (CC0): " + string.Join(", ", authors.ToArray()) + ", Quaternius (traje hazmat)  ·  Ver «Créditos»";
    }

    // --- Selección de nivel ---
    private GameObject BuildLevels(Transform parent)
    {
        GameObject root = NewPanelRoot(parent, "LevelSelect", new Color(0f, 0f, 0f, 0.55f));
        Label(root.transform, "SELECCIONAR NIVEL", 76, TextAnchor.MiddleCenter, 0f, 340f, 1500f, 120f, new Color(0.96f, 0.9f, 0.55f));
        float y = 210f;
        for (int i = 0; i < LevelConfig.Count; i++)
        {
            int lv = i;
            LevelConfig c = LevelConfig.Get(i);
            Button b = MakeButton(root.transform, "Nivel " + i + " — " + c.name, 0f, y, 900f, 70f, () => { Click(); StartGame(lv); }, 32);
            b.name = "LevelBtn_" + i;
            y -= 84f;
        }
        MakeButton(root.transform, "Volver", 0f, y - 20f, 400f, 64f, () => { Click(); SetScreen(Screen.Main); });
        return root;
    }

    // --- Opciones ---
    private Slider sVol, sSens, sVhs, sSfx, sAmb;
    private Toggle tFull, tInvert, tBob, tSmooth, tVhs, tSoft, tBody, tBreath, tNoise;
    public bool CursorShouldBeLocked { get { return current == Screen.None && GameplayActive; } }   // el editor sin ventana no refleja Cursor.lockState
    public Slider VhsSlider { get { return sVhs; } }
    public Slider VolumeSlider { get { return sVol; } }
    public Slider SfxSlider { get { return sSfx; } }
    public Slider AmbienceSlider { get { return sAmb; } }
    public Toggle BreathToggle { get { return tBreath; } }
    public Toggle NoiseToggle { get { return tNoise; } }
    public Toggle VhsToggle { get { return tVhs; } }
    public Toggle BobToggle { get { return tBob; } }
    public Toggle SmoothToggle { get { return tSmooth; } }
    public Toggle SoftScareToggle { get { return tSoft; } }
    public Toggle BodyToggle { get { return tBody; } }
    private Button[] qBtns;
    private Text vVol, vSens, vVhs, vSfx, vAmb;
    private bool refreshing;

    private GameObject BuildOptions(Transform parent)
    {
        GameObject root = NewPanelRoot(parent, "Options", new Color(0f, 0f, 0f, 0.82f));
        Label(root.transform, "OPCIONES", 76, TextAnchor.MiddleCenter, 0f, 400f, 1500f, 110f, new Color(0.96f, 0.9f, 0.55f));
        float y = 310f;
        const float lx = -430f, sx = 130f;
        const float rowS = 52f, rowT = 40f;

        Label(root.transform, "Volumen general", 30, TextAnchor.MiddleLeft, lx, y, 520f, 40f).rectTransform.pivot = new Vector2(0.5f, 0.5f);
        sVol = MakeSlider(root.transform, sx, y, 520f, 0f, 1f, GameSettings.MasterVolume, v => { if (!refreshing) GameSettings.MasterVolume = v; vVol.text = Mathf.RoundToInt(v * 100f) + "%"; });
        vVol = Label(root.transform, "", 28, TextAnchor.MiddleLeft, 470f, y, 120f, 40f);
        y -= rowS;

        Label(root.transform, "Efectos", 30, TextAnchor.MiddleLeft, lx, y, 520f, 40f);
        sSfx = MakeSlider(root.transform, sx, y, 520f, 0f, 1f, GameSettings.SfxVolume, v => { if (!refreshing) GameSettings.SfxVolume = v; vSfx.text = Mathf.RoundToInt(v * 100f) + "%"; });
        vSfx = Label(root.transform, "", 28, TextAnchor.MiddleLeft, 470f, y, 120f, 40f);
        y -= rowS;

        Label(root.transform, "Ambiente", 30, TextAnchor.MiddleLeft, lx, y, 520f, 40f);
        sAmb = MakeSlider(root.transform, sx, y, 520f, 0f, 1f, GameSettings.AmbienceVolume, v => { if (!refreshing) GameSettings.AmbienceVolume = v; vAmb.text = Mathf.RoundToInt(v * 100f) + "%"; });
        vAmb = Label(root.transform, "", 28, TextAnchor.MiddleLeft, 470f, y, 120f, 40f);
        y -= rowS;

        Label(root.transform, "Sensibilidad del ratón", 30, TextAnchor.MiddleLeft, lx, y, 520f, 40f);
        sSens = MakeSlider(root.transform, sx, y, 520f, 0.3f, 6f, GameSettings.MouseSensitivity, v => { if (!refreshing) GameSettings.MouseSensitivity = v; vSens.text = v.ToString("0.0"); });
        vSens = Label(root.transform, "", 28, TextAnchor.MiddleLeft, 470f, y, 120f, 40f);
        y -= rowS;

        Label(root.transform, "Calidad gráfica", 30, TextAnchor.MiddleLeft, lx, y, 520f, 40f);
        qBtns = new Button[3];
        for (int i = 0; i < 3; i++)
        {
            int q = i;
            qBtns[i] = MakeButton(root.transform, GameSettings.QualityNames[i], sx - 170f + i * 175f, y, 165f, 46f, () => { Click(); GameSettings.Quality = q; RefreshOptions(); }, 28);
        }
        y -= rowS;

        float tx = -90f;
        tFull = MakeToggle(root.transform, "Pantalla completa", tx, y, GameSettings.Fullscreen, v => { if (!refreshing) GameSettings.Fullscreen = v; }); y -= rowT;
        tInvert = MakeToggle(root.transform, "Invertir eje Y del ratón", tx, y, GameSettings.InvertY, v => { if (!refreshing) GameSettings.InvertY = v; }); y -= rowT;
        tBob = MakeToggle(root.transform, "Balanceo de cabeza (head-bob)", tx, y, GameSettings.HeadBob, v => { if (!refreshing) GameSettings.HeadBob = v; }); y -= rowT;
        tSmooth = MakeToggle(root.transform, "Suavizado de ratón", tx, y, GameSettings.SmoothLook, v => { if (!refreshing) GameSettings.SmoothLook = v; }); y -= rowT;
        tVhs = MakeToggle(root.transform, "Modo cámara VHS (tecla V)", tx, y, GameSettings.VhsOn, v => { if (!refreshing) GameSettings.VhsOn = v; }); y -= rowT;
        tBreath = MakeToggle(root.transform, "Respiración (suave, solo al cansarse)", tx, y, GameSettings.BreathEnabled, v => { if (!refreshing) GameSettings.BreathEnabled = v; }); y -= rowT;
        tNoise = MakeToggle(root.transform, "Siseo VHS / ruido (audio de ruido)", tx, y, GameSettings.NoiseEnabled, v => { if (!refreshing) GameSettings.NoiseEnabled = v; }); y -= rowT;
        tSoft = MakeToggle(root.transform, "Susto suave (reduce el susto de muerte)", tx, y, GameSettings.SoftScare, v => { if (!refreshing) GameSettings.SoftScare = v; }); y -= rowT;
        tBody = MakeToggle(root.transform, "Mostrar cuerpo (manos, brazos y piernas)", tx, y, GameSettings.ShowBody, v => { if (!refreshing) GameSettings.ShowBody = v; }); y -= rowT + 14f;

        Label(root.transform, "Intensidad VHS", 30, TextAnchor.MiddleLeft, lx, y, 520f, 40f);
        sVhs = MakeSlider(root.transform, sx, y, 520f, 0f, 1f, GameSettings.VhsIntensity, v => { if (!refreshing) GameSettings.VhsIntensity = v; vVhs.text = Mathf.RoundToInt(v * 100f) + "%"; });
        vVhs = Label(root.transform, "", 28, TextAnchor.MiddleLeft, 470f, y, 120f, 40f);
        y -= 70f;

        MakeButton(root.transform, "Restablecer", -230f, y, 360f, 60f, () => { Click(); GameSettings.ResetDefaults(); RefreshOptions(); }, 30);
        MakeButton(root.transform, "Volver", 230f, y, 360f, 60f, () => { Click(); GameSettings.Save(); SetScreen(optionsReturn); }, 30);
        return root;
    }

    public void RefreshOptions()
    {
        if (sVol == null) return;
        refreshing = true;
        sVol.value = GameSettings.MasterVolume; vVol.text = Mathf.RoundToInt(sVol.value * 100f) + "%";
        sSfx.value = GameSettings.SfxVolume; vSfx.text = Mathf.RoundToInt(sSfx.value * 100f) + "%";
        sAmb.value = GameSettings.AmbienceVolume; vAmb.text = Mathf.RoundToInt(sAmb.value * 100f) + "%";
        tBreath.isOn = GameSettings.BreathEnabled;
        tNoise.isOn = GameSettings.NoiseEnabled;
        sSens.value = GameSettings.MouseSensitivity; vSens.text = sSens.value.ToString("0.0");
        sVhs.value = GameSettings.VhsIntensity; vVhs.text = Mathf.RoundToInt(sVhs.value * 100f) + "%";
        tFull.isOn = GameSettings.Fullscreen; tInvert.isOn = GameSettings.InvertY; tBob.isOn = GameSettings.HeadBob;
        tSmooth.isOn = GameSettings.SmoothLook; tVhs.isOn = GameSettings.VhsOn;
        tSoft.isOn = GameSettings.SoftScare; tBody.isOn = GameSettings.ShowBody;
        for (int i = 0; i < qBtns.Length; i++)
        {
            Image img = qBtns[i].GetComponent<Image>();
            img.color = i == GameSettings.Quality ? BtnHi : BtnCol;
        }
        refreshing = false;
    }

    // --- Créditos ---
    private GameObject BuildCredits(Transform parent)
    {
        GameObject root = NewPanelRoot(parent, "Credits", new Color(0f, 0f, 0f, 0.86f));
        Label(root.transform, "CRÉDITOS", 76, TextAnchor.MiddleCenter, 0f, 410f, 1500f, 110f, new Color(0.96f, 0.9f, 0.55f));
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine("Juego y generación procedural (mallas, texturas, audio): código del proyecto.");
        sb.AppendLine();
        sb.AppendLine("Modelos de monstruos (todos con licencia CC0 / dominio público):");
        foreach (MonsterProfile p in MonsterProfile.All)
        {
            sb.AppendLine();
            sb.AppendLine(p.displayName + ": «" + p.id + "» — " + p.modelAuthor + " — " + p.licence);
            sb.AppendLine("   " + p.modelSource);
        }
        sb.AppendLine();
        sb.AppendLine("Cuerpo del jugador (traje hazmat): «Character Hazmat» — Quaternius — CC0 (Dominio público)");
        sb.AppendLine("   https://poly.pizza/m/z3TSQYx1Kn   (armas retiradas, animaciones Idle/Walk/Run/Duck; malla suavizada, guantes a escala y texturas propias horneadas en Blender)");
        sb.AppendLine("Nivel 7: El Farolero (modelo, texturas y animación), cascos de buzo y muelle: trabajo propio (Blender 4.2 y texturas procedurales), CC0 (dominio público)");
        sb.AppendLine();
        sb.AppendLine("Ascensor de entrada: «Elevator car interior» + «Elevator door pair» (Backrooms and Liminal Spaces Kit) — 3D Assets — CC0 1.0 (dominio público)");
        sb.AppendLine("   https://3dassets.dev/assets/liminal-spaces-kit-elevator-car-22251ff7   (indicador de planta, luces, sonidos y animación: procedurales)");
        sb.AppendLine();
        sb.AppendLine("Los modelos se han convertido a FBX con Blender y retexturizado donde se indica. Detalles en CREDITS.md.");
        Text t = Label(root.transform, sb.ToString(), 18, TextAnchor.UpperLeft, 0f, 20f, 1640f, 780f);
        t.name = "CreditsBody";
        MakeButton(root.transform, "Volver", 0f, -440f, 400f, 64f, () => { Click(); SetScreen(Screen.Main); });
        return root;
    }

    // --- Pausa ---
    private GameObject BuildPause(Transform parent)
    {
        GameObject root = NewPanelRoot(parent, "PauseMenu", new Color(0f, 0f, 0f, 0.7f));
        pauseTitle = Label(root.transform, "PAUSA", 96, TextAnchor.MiddleCenter, 0f, 330f, 1200f, 170f, new Color(0.96f, 0.9f, 0.55f));
        pauseTitle.supportRichText = true;
        float y = 190f;
        MakeButton(root.transform, "Continuar", 0f, y, 520f, 68f, OnResume); y -= 84f;
        MakeButton(root.transform, "Reiniciar nivel", 0f, y, 520f, 68f, OnRestartLevel); y -= 84f;
        MakeButton(root.transform, "Opciones", 0f, y, 520f, 68f, () => OpenOptions(Screen.Pause)); y -= 84f;
        MakeButton(root.transform, "Menú principal", 0f, y, 520f, 68f, OnMainMenu); y -= 84f;
        MakeButton(root.transform, "Salir", 0f, y, 520f, 68f, OnQuit);
        Label(root.transform, "Esc: continuar", 26, TextAnchor.MiddleCenter, 0f, -330f, 800f, 40f, Dim);
        return root;
    }

    // --- Inventario ---
    private GameObject BuildInventory(Transform parent)
    {
        GameObject root = NewPanelRoot(parent, "Inventory", new Color(0f, 0f, 0f, 0.72f));
        invUi = root.AddComponent<InventoryUI>();
        invUi.Build(this);
        return root;
    }

    // --- Victoria ---
    private GameObject BuildEnd(Transform parent)
    {
        GameObject root = Panel(parent, "EndPanel", new Color(0f, 0f, 0f, 0f), true).gameObject;
        // los botones van bajo el texto de victoria (que dibuja el HUD)
        MakeButton(root.transform, "Volver a empezar", -270f, -330f, 500f, 70f, OnRestartRun);
        MakeButton(root.transform, "Menú principal", 270f, -330f, 500f, 70f, OnMainMenu);
        root.GetComponent<Image>().raycastTarget = false;
        return root;
    }

    // Para pruebas: pulsa por nombre
    public bool ClickButton(string name)
    {
        foreach (Button b in canvas.GetComponentsInChildren<Button>(false))
        {
            if (b.name == "Btn_" + name || b.name == name) { if (!b.interactable) return false; b.onClick.Invoke(); return true; }
        }
        return false;
    }
}
