using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Interfaz multijugador (UnityEngine.UI legacy, en español, construida desde código):
//   Menú «Multijugador» → «Alojar partida» (puerto, IPs locales, nota de reenvío de puertos/VPN) / «Unirse» (IP + puerto) → Sala (lista de jugadores,
//   selector de nivel y «Empezar» solo para el anfitrión, «Volver»). Durante la partida: lista del equipo, «X/Y jugadores en la salida»,
//   aviso de derribado/reanimación y chat de texto (Enter para escribir).
public class MultiplayerUI : MonoBehaviour
{
    public enum Page { Menu, Host, Join, JoinCode }

    public static bool ChatTyping;
    public const string KName = "mp_name", KPort = "mp_port", KIp = "mp_ip";

    public GameObject MultiRoot { get; private set; }
    public GameObject LobbyRoot { get; private set; }
    public Page CurrentPage { get; private set; }
    public InputField NameField { get; private set; }
    public InputField HostPortField { get; private set; }
    public InputField JoinIpField { get; private set; }
    public InputField JoinPortField { get; private set; }
    public InputField RelayField { get; private set; }
    public InputField CodeField { get; private set; }
    public Button RelayHostButton { get { return btnRelayHost; } }
    public Button RelayJoinButton { get { return btnRelayJoin; } }
    public string HostInfoText { get { return hostIps != null ? hostIps.text : ""; } }
    public string LobbyAddrText { get { return lobbyAddr != null ? lobbyAddr.text : ""; } }
    public string RelayHintText { get { return relayHint != null ? relayHint.text : ""; } }
    public string CodeStatusText { get { return (codeStatus != null ? codeStatus.text : "") + " " + (codeError != null ? codeError.text : ""); } }
    public InputField ChatField { get; private set; }
    public int LobbyLevel { get; private set; }
    public int ChatLineCount { get { return chatLines.Count; } }
    public string LastChatLine { get { return chatLines.Count > 0 ? chatLines[chatLines.Count - 1].text : ""; } }
    public string LobbyText { get { return lobbyPlayers != null ? lobbyPlayers.text : ""; } }
    public string StatusText { get { return joinStatus != null ? joinStatus.text : ""; } }
    public string NoticeText { get { return notice != null ? notice.text : ""; } }
    public string HudTeamText { get { return teamText != null ? teamText.text : ""; } }
    public string HudExitText { get { return exitText != null ? exitText.text : ""; } }
    public string HudDownedText { get { return downedText != null ? downedText.text : ""; } }
    public bool HudVisible { get { return hudCanvas != null && hudCanvas.gameObject.activeSelf; } }
    public Button StartButton { get { return btnStart; } }

    private UIManager ui;
    private Transform menuParent;
    private GameObject pgMenu, pgHost, pgJoin, pgCode;
    private Text hostIps, hostError, joinError, joinStatus, lobbyTitle, lobbyInfo, lobbyPlayers, lobbyLevelText, lobbyWait, notice;
    private Button btnConnect, btnCancel, btnStart, btnPrev, btnNext;
    private Button btnRelayHost, btnRelayJoin, btnCodeConnect, btnCodeCancel, btnCopyHost, btnCopyLobby, btnLobbyBack;
    private Text relayHint, codeStatus, codeError, lobbyAddr, copyNoteHost, copyNoteLobby;
    private float copyNoteUntil, infoTimer;
    private List<string> localIps = new List<string>();
    private string lastIpText = "";
    private bool splitting;
    private bool lobbyDirty = true;
    private float noticeUntil;

    // HUD en partida
    private Canvas hudCanvas;
    private Text teamText, exitText, downedText, chatLog, pauseNote;
    private GameObject chatBox;
    private struct ChatLine { public string text; public float time; public Color color; }
    private readonly List<ChatLine> chatLines = new List<ChatLine>();

    private static readonly Color Gold = new Color(0.96f, 0.9f, 0.55f);
    private static readonly Color Dim = new Color(0.7f, 0.66f, 0.5f);
    private static readonly Color Err = new Color(1f, 0.5f, 0.4f);

    // ------------------------------------------------------------------
    public static string SavedName { get { return PlayerPrefs.GetString(KName, "Jugador"); } }

    public void Build(UIManager manager, Transform canvasParent)
    {
        ui = manager;
        menuParent = canvasParent;
        BuildMulti(canvasParent);
        BuildLobby(canvasParent);
        BuildNotice(canvasParent);
        BuildHud();
        NetSession ns = NetSession.Instance;
        if (ns != null)
        {
            ns.LobbyChanged += () => lobbyDirty = true;
            ns.ChatReceived += OnChat;
            ns.SystemMessage += OnSystem;
        }
        ShowPage(Page.Menu);
    }

    private InputField MakeInput(Transform parent, string initial, float x, float y, float w, float h, int fontSize, int maxLen, bool numeric)
    {
        Image bg = ui.Panel(parent, "Input", new Color(0.12f, 0.11f, 0.07f, 1f), false).GetComponent<Image>();
        RectTransform rt = bg.rectTransform;
        rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(x, y);
        GameObject tgo = new GameObject("Text", typeof(RectTransform));
        tgo.transform.SetParent(bg.transform, false);
        Text t = tgo.AddComponent<Text>();
        t.font = UiStyle.Mono(); t.fontSize = fontSize; t.alignment = TextAnchor.MiddleLeft; t.color = UiStyle.Text; t.supportRichText = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Truncate;
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = new Vector2(12f, 4f); t.rectTransform.offsetMax = new Vector2(-12f, -4f);
        GameObject pgo = new GameObject("Placeholder", typeof(RectTransform));
        pgo.transform.SetParent(bg.transform, false);
        Text p = pgo.AddComponent<Text>();
        p.font = t.font; p.fontSize = fontSize; p.alignment = TextAnchor.MiddleLeft; p.color = new Color(0.5f, 0.47f, 0.35f); p.fontStyle = FontStyle.Italic; p.text = "…";
        p.rectTransform.anchorMin = Vector2.zero; p.rectTransform.anchorMax = Vector2.one;
        p.rectTransform.offsetMin = new Vector2(12f, 4f); p.rectTransform.offsetMax = new Vector2(-12f, -4f);
        InputField f = bg.gameObject.AddComponent<InputField>();
        f.textComponent = t; f.placeholder = p; f.characterLimit = maxLen;
        f.contentType = numeric ? InputField.ContentType.IntegerNumber : InputField.ContentType.Standard;
        f.text = initial;
        f.targetGraphic = bg;
        return f;
    }

    private void BuildMulti(Transform parent)
    {
        MultiRoot = ui.Panel(parent, "MultiMenu", new Color(0f, 0f, 0f, 0.6f), true).gameObject;
        Transform r = MultiRoot.transform;

        // --- página principal ---
        pgMenu = ui.Panel(r, "PageMenu", new Color(0, 0, 0, 0), true).gameObject;
        pgMenu.GetComponent<Image>().raycastTarget = false;
        ui.Label(pgMenu.transform, "MULTIJUGADOR", 84, TextAnchor.MiddleCenter, 0f, 380f, 1500f, 120f, Gold);
        ui.Label(pgMenu.transform, "Cooperativo de 2 a 4 jugadores · un jugador aloja y los demás se unen con su IP", 28, TextAnchor.MiddleCenter, 0f, 300f, 1500f, 50f, Dim);
        ui.Label(pgMenu.transform, "Tu nombre", 30, TextAnchor.MiddleRight, -330f, 200f, 300f, 44f);
        NameField = MakeInput(pgMenu.transform, SavedName, 90f, 200f, 520f, 54f, 30, 16, false);
        NameField.name = "NameField";
        NameField.onEndEdit.AddListener(v => SaveName());
        ui.MakeButton(pgMenu.transform, "Alojar partida", -290f, 110f, 560f, 70f, () => { ui.PlayClick(); SaveName(); ShowPage(Page.Host); });
        ui.MakeButton(pgMenu.transform, "Unirse", 290f, 110f, 560f, 70f, () => { ui.PlayClick(); SaveName(); ShowPage(Page.Join); });
        btnRelayHost = ui.MakeButton(pgMenu.transform, "Alojar con código", -290f, 30f, 560f, 70f, OnCreateRelayRoom);
        btnRelayJoin = ui.MakeButton(pgMenu.transform, "Unirse con código", 290f, 30f, 560f, 70f, () => { ui.PlayClick(); SaveName(); ShowPage(Page.JoinCode); });
        ui.Label(pgMenu.transform, "Servidor relay", 28, TextAnchor.MiddleRight, -470f, -55f, 300f, 44f);
        RelayField = MakeInput(pgMenu.transform, RelayConfig.FieldText, 110f, -55f, 760f, 54f, 28, 100, false);
        RelayField.name = "RelayField";
        ((Text)RelayField.placeholder).text = RelayDefault.HasBuiltin ? RelayConfig.BuiltinLabel : "opcional: host:puerto";
        RelayField.onValueChanged.AddListener(v => { RelayConfig.Save(v); RefreshRelayUi(); });
        relayHint = ui.Label(pgMenu.transform, "", 22, TextAnchor.MiddleCenter, 0f, -115f, 1500f, 40f, Dim);
        ui.MakeButton(pgMenu.transform, "Volver", 0f, -195f, 560f, 70f, () => { ui.PlayClick(); ui.SetScreen(UIManager.Screen.Main); });
        ui.Label(pgMenu.transform, "Directo: IP + puerto TCP (7777 por defecto; el juego intenta abrir el puerto en tu router con UPnP).\nCon código: un servidor relay empareja a los jugadores sin abrir puertos (hay que configurar su dirección).", 22, TextAnchor.UpperCenter, 0f, -280f, 1600f, 100f, Dim);
        RefreshRelayUi();

        // --- alojar ---
        pgHost = ui.Panel(r, "PageHost", new Color(0, 0, 0, 0), true).gameObject;
        pgHost.GetComponent<Image>().raycastTarget = false;
        ui.Label(pgHost.transform, "ALOJAR PARTIDA", 76, TextAnchor.MiddleCenter, 0f, 400f, 1500f, 110f, Gold);
        ui.Label(pgHost.transform, "Puerto (TCP)", 30, TextAnchor.MiddleRight, -300f, 305f, 300f, 44f);
        HostPortField = MakeInput(pgHost.transform, PlayerPrefs.GetInt(KPort, NetSession.DefaultPort).ToString(), 100f, 305f, 220f, 54f, 30, 5, true);
        HostPortField.name = "HostPortField";
        hostIps = ui.Label(pgHost.transform, "", 26, TextAnchor.UpperLeft, 0f, 110f, 1200f, 330f, UiStyle.Text);
        ui.Label(pgHost.transform,
            "Con UPnP el puerto se abre solo en la mayoría de routers. Si no, abre/redirige TCP en tu router hacia este PC, usa una VPN\n" +
            "(Radmin VPN, ZeroTier, Hamachi) o el modo «Alojar con código». Windows pedirá permitir el juego en el Firewall: acepta.",
            21, TextAnchor.UpperLeft, 0f, -125f, 1500f, 80f, Dim);
        hostError = ui.Label(pgHost.transform, "", 28, TextAnchor.MiddleCenter, 0f, -210f, 1500f, 50f, Err);
        ui.MakeButton(pgHost.transform, "Crear sala", -370f, -300f, 340f, 70f, OnCreateRoom);
        btnCopyHost = ui.MakeButton(pgHost.transform, "Copiar dirección", 0f, -300f, 340f, 70f, () => CopyAddress(copyNoteHost));
        ui.MakeButton(pgHost.transform, "Volver", 370f, -300f, 340f, 70f, () => { ui.PlayClick(); ShowPage(Page.Menu); });
        copyNoteHost = ui.Label(pgHost.transform, "", 24, TextAnchor.MiddleCenter, 0f, -365f, 1500f, 40f, Gold);

        // --- unirse ---
        pgJoin = ui.Panel(r, "PageJoin", new Color(0, 0, 0, 0), true).gameObject;
        pgJoin.GetComponent<Image>().raycastTarget = false;
        ui.Label(pgJoin.transform, "UNIRSE A UNA PARTIDA", 76, TextAnchor.MiddleCenter, 0f, 400f, 1500f, 110f, Gold);
        ui.Label(pgJoin.transform, "IP del anfitrión", 30, TextAnchor.MiddleRight, -330f, 270f, 360f, 44f);
        JoinIpField = MakeInput(pgJoin.transform, PlayerPrefs.GetString(KIp, ""), 130f, 270f, 460f, 54f, 30, 64, false);
        JoinIpField.name = "JoinIpField";
        ((Text)JoinIpField.placeholder).text = "192.168.1.20  o  ip:puerto";
        JoinIpField.onValueChanged.AddListener(v =>
        {
            // pegado (o texto puesto de golpe): no es escribir/borrar un carácter => separamos «ip:puerto»
            bool typed = (v.Length == lastIpText.Length + 1 && v.StartsWith(lastIpText)) || (v.Length == lastIpText.Length - 1 && lastIpText.StartsWith(v));
            if (!splitting && !typed && v.IndexOf(':') >= 0) SplitIpPort();
            lastIpText = JoinIpField.text;
        });
        JoinIpField.onEndEdit.AddListener(v => SplitIpPort());
        ui.Label(pgJoin.transform, "Puerto", 30, TextAnchor.MiddleRight, -330f, 195f, 360f, 44f);
        JoinPortField = MakeInput(pgJoin.transform, PlayerPrefs.GetInt(KPort, NetSession.DefaultPort).ToString(), 40f, 195f, 280f, 54f, 30, 5, true);
        JoinPortField.name = "JoinPortField";
        btnConnect = ui.MakeButton(pgJoin.transform, "Conectar", -250f, 60f, 440f, 70f, OnConnect);
        btnCancel = ui.MakeButton(pgJoin.transform, "Cancelar", -250f, 60f, 440f, 70f, () => { ui.PlayClick(); if (NetSession.Instance != null) NetSession.Instance.Leave(null); RefreshJoin(); });
        ui.MakeButton(pgJoin.transform, "Volver", 250f, 60f, 440f, 70f, () => { ui.PlayClick(); if (NetSession.Instance != null && NetSession.Instance.Connecting) NetSession.Instance.Leave(null); ShowPage(Page.Menu); });
        joinStatus = ui.Label(pgJoin.transform, "", 28, TextAnchor.MiddleCenter, 0f, -50f, 1500f, 60f, Gold);
        joinError = ui.Label(pgJoin.transform, "", 28, TextAnchor.UpperCenter, 0f, -150f, 1500f, 160f, Err);
        ui.Label(pgJoin.transform, "Pide al anfitrión su dirección (LAN, pública o de la VPN) y pégala aquí: vale «ip:puerto», por ejemplo 83.12.34.56:7777.", 24, TextAnchor.MiddleCenter, 0f, -330f, 1500f, 50f, Dim);

        // --- unirse con código (relay) ---
        pgCode = ui.Panel(r, "PageCode", new Color(0, 0, 0, 0), true).gameObject;
        pgCode.GetComponent<Image>().raycastTarget = false;
        ui.Label(pgCode.transform, "UNIRSE CON CÓDIGO", 76, TextAnchor.MiddleCenter, 0f, 400f, 1500f, 110f, Gold);
        ui.Label(pgCode.transform, "Código de sala", 30, TextAnchor.MiddleRight, -330f, 270f, 360f, 44f);
        CodeField = MakeInput(pgCode.transform, "", 130f, 270f, 360f, 54f, 34, 7, false);
        CodeField.name = "CodeField";
        ((Text)CodeField.placeholder).text = "ABC234";
        CodeField.onValueChanged.AddListener(v => { string u = v.ToUpperInvariant(); if (u != v) CodeField.text = u; });
        btnCodeConnect = ui.MakeButton(pgCode.transform, "Conectar con código", -250f, 120f, 440f, 70f, OnConnectCode);
        btnCodeCancel = ui.MakeButton(pgCode.transform, "Cancelar", -250f, 120f, 440f, 70f, () => { ui.PlayClick(); if (NetSession.Instance != null) NetSession.Instance.Leave(null); RefreshJoin(); });
        ui.MakeButton(pgCode.transform, "Volver", 250f, 120f, 440f, 70f, () => { ui.PlayClick(); if (NetSession.Instance != null && NetSession.Instance.Connecting) NetSession.Instance.Leave(null); ShowPage(Page.Menu); });
        codeStatus = ui.Label(pgCode.transform, "", 28, TextAnchor.MiddleCenter, 0f, 10f, 1500f, 60f, Gold);
        codeError = ui.Label(pgCode.transform, "", 28, TextAnchor.UpperCenter, 0f, -90f, 1500f, 160f, Err);
        ui.Label(pgCode.transform, "El anfitrión ve el código de 6 caracteres en su sala (Multijugador → Alojar con código).", 24, TextAnchor.MiddleCenter, 0f, -300f, 1500f, 50f, Dim);
    }

    private void BuildLobby(Transform parent)
    {
        LobbyRoot = ui.Panel(parent, "Lobby", new Color(0f, 0f, 0f, 0.62f), true).gameObject;
        Transform r = LobbyRoot.transform;
        lobbyTitle = ui.Label(r, "SALA", 80, TextAnchor.MiddleCenter, 0f, 400f, 1500f, 110f, Gold);
        lobbyInfo = ui.Label(r, "", 24, TextAnchor.MiddleCenter, 0f, 320f, 1600f, 70f, Dim);
        Image frame = UiStyle.Framed(r, "PlayersFrame", 0f, 120f, 900f, 300f, new Color(0.04f, 0.035f, 0.02f, 0.9f), UiStyle.Border, 2f);
        lobbyPlayers = UiStyle.Label(frame.transform, "", 32, TextAnchor.MiddleLeft, 0f, 0f, 840f, 250f, UiStyle.Text);
        lobbyPlayers.lineSpacing = 1.25f;   // 4 filas caben en el marco con cualquier fuente
        lobbyLevelText = ui.Label(r, "", 32, TextAnchor.MiddleCenter, 0f, -70f, 800f, 50f, UiStyle.Text);
        btnPrev = ui.MakeButton(r, "◄", -470f, -70f, 80f, 56f, () => { ui.PlayClick(); LobbyLevel = (LobbyLevel + LevelConfig.Count - 1) % LevelConfig.Count; lobbyDirty = true; }, 30);
        btnNext = ui.MakeButton(r, "►", 470f, -70f, 80f, 56f, () => { ui.PlayClick(); LobbyLevel = (LobbyLevel + 1) % LevelConfig.Count; lobbyDirty = true; }, 30);
        btnStart = ui.MakeButton(r, "Empezar", 0f, -170f, 520f, 74f, OnStartGame);
        lobbyWait = ui.Label(r, "Esperando a que el anfitrión empiece la partida...", 30, TextAnchor.MiddleCenter, 0f, -170f, 1200f, 60f, Gold);
        btnLobbyBack = ui.MakeButton(r, "Volver", 0f, -280f, 520f, 64f, OnLeaveLobby);
        btnCopyLobby = ui.MakeButton(r, "Copiar dirección", -250f, -280f, 440f, 64f, () => CopyAddress(copyNoteLobby));
        lobbyAddr = ui.Label(r, "", 22, TextAnchor.UpperCenter, 0f, -405f, 1700f, 170f, Dim);
        copyNoteLobby = ui.Label(r, "", 24, TextAnchor.MiddleCenter, 0f, -330f, 1500f, 36f, Gold);
    }

    private void BuildNotice(Transform parent)
    {
        Image bg = ui.Panel(parent, "Notice", new Color(0.25f, 0.04f, 0.02f, 0.92f), false).GetComponent<Image>();
        bg.rectTransform.sizeDelta = new Vector2(1500f, 80f);
        bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        bg.rectTransform.pivot = new Vector2(0.5f, 1f);
        bg.rectTransform.anchoredPosition = new Vector2(0f, -30f);
        bg.raycastTarget = false;
        notice = ui.Label(bg.transform, "", 28, TextAnchor.MiddleCenter, 0f, 0f, 1460f, 76f, UiStyle.Text);
        notice.rectTransform.anchorMin = Vector2.zero; notice.rectTransform.anchorMax = Vector2.one;
        notice.rectTransform.offsetMin = notice.rectTransform.offsetMax = Vector2.zero;
        bg.gameObject.SetActive(false);
        noticeBg = bg.gameObject;
    }
    private GameObject noticeBg;

    // ------------------------------------------------------------------
    // HUD de partida
    // ------------------------------------------------------------------
    private Text Corner(Transform parent, string name, int size, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = UiStyle.Mono(); t.fontSize = size; t.alignment = align; t.color = UiStyle.Text;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.raycastTarget = false;
        t.supportRichText = true;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = sizeDelta;
        Shadow sh = go.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.9f); sh.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    private void BuildHud()
    {
        hudCanvas = UiStyle.NewOverlayCanvas(transform, "MultiplayerHud", 40);
        Transform r = hudCanvas.transform;
        teamText = Corner(r, "Team", 22, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-24f, -150f), new Vector2(520f, 160f));
        exitText = Corner(r, "ExitCount", 32, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(900f, 50f));
        exitText.color = UiStyle.Good;
        downedText = Corner(r, "Downed", 40, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 240f), new Vector2(1500f, 150f));
        downedText.color = new Color(1f, 0.45f, 0.35f);
        chatLog = Corner(r, "ChatLog", 22, TextAnchor.LowerLeft, new Vector2(0f, 0f), new Vector2(24f, 150f), new Vector2(900f, 260f));
        pauseNote = Corner(r, "PauseNote", 26, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(900f, 40f));
        pauseNote.color = Gold;
        // caja de escritura del chat
        chatBox = new GameObject("ChatBox", typeof(RectTransform));
        chatBox.transform.SetParent(r, false);
        RectTransform cb = (RectTransform)chatBox.transform;
        cb.anchorMin = cb.anchorMax = cb.pivot = new Vector2(0f, 0f);
        cb.anchoredPosition = new Vector2(24f, 100f); cb.sizeDelta = new Vector2(900f, 44f);
        Image bg = chatBox.AddComponent<Image>(); bg.color = new Color(0.04f, 0.035f, 0.02f, 0.9f);
        GameObject tgo = new GameObject("Text", typeof(RectTransform)); tgo.transform.SetParent(chatBox.transform, false);
        Text t = tgo.AddComponent<Text>(); t.font = UiStyle.Mono(); t.fontSize = 24; t.color = UiStyle.Text; t.alignment = TextAnchor.MiddleLeft; t.supportRichText = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one; t.rectTransform.offsetMin = new Vector2(10f, 2f); t.rectTransform.offsetMax = new Vector2(-10f, -2f);
        GameObject pgo = new GameObject("Placeholder", typeof(RectTransform)); pgo.transform.SetParent(chatBox.transform, false);
        Text p = pgo.AddComponent<Text>(); p.font = t.font; p.fontSize = 22; p.color = new Color(0.55f, 0.5f, 0.35f); p.alignment = TextAnchor.MiddleLeft; p.text = "Escribe un mensaje y pulsa Enter (Esc cancela)";
        p.rectTransform.anchorMin = Vector2.zero; p.rectTransform.anchorMax = Vector2.one; p.rectTransform.offsetMin = new Vector2(10f, 2f); p.rectTransform.offsetMax = new Vector2(-10f, -2f);
        ChatField = chatBox.AddComponent<InputField>();
        ChatField.textComponent = t; ChatField.placeholder = p; ChatField.characterLimit = 140; ChatField.targetGraphic = bg;
        chatBox.SetActive(false);
    }

    private void OnChat(string from, string text)
    {
        AddChatLine("<b>" + from + ":</b> " + text.Replace("<", "‹"), UiStyle.Text);
    }
    private void OnSystem(string text) { AddChatLine("<i>" + text + "</i>", UiStyle.Gold); }

    public void AddChatLine(string richText, Color c)
    {
        chatLines.Add(new ChatLine { text = richText, time = Time.unscaledTime, color = c });
        while (chatLines.Count > 8) chatLines.RemoveAt(0);
    }

    public void OpenChat()
    {
        if (ChatTyping) return;
        ChatTyping = true;
        chatBox.SetActive(true);
        ChatField.text = "";
        ChatField.ActivateInputField();
        ChatField.Select();
    }

    public void CloseChat()
    {
        ChatTyping = false;
        if (chatBox != null) chatBox.SetActive(false);
    }

    public void SubmitChat()
    {
        string txt = ChatField != null ? ChatField.text.Trim() : "";
        CloseChat();
        if (txt.Length > 0 && NetSession.Active) NetSession.Instance.SendChat(txt);
    }

    // ------------------------------------------------------------------
    // Acciones
    // ------------------------------------------------------------------
    private void SaveName()
    {
        string n = NetSession.CleanName(NameField != null ? NameField.text : "Jugador");
        PlayerPrefs.SetString(KName, n);
        if (NameField != null && NameField.text != n && !NameField.isFocused) NameField.text = n;
        PlayerPrefs.Save();
    }

    private static int ParsePort(InputField f)
    {
        int p;
        if (!int.TryParse(f.text.Trim(), out p)) p = NetSession.DefaultPort;
        return Mathf.Clamp(p, 1, 65535);
    }

    public void ShowPage(Page p)
    {
        CurrentPage = p;
        if (pgMenu == null) return;
        pgMenu.SetActive(p == Page.Menu);
        pgHost.SetActive(p == Page.Host);
        pgJoin.SetActive(p == Page.Join);
        pgCode.SetActive(p == Page.JoinCode);
        if (p == Page.Menu) RefreshRelayUi();
        if (p == Page.Host)
        {
            hostError.text = ""; copyNoteHost.text = "";
            localIps = NetUtil.LocalAddresses();
            PublicIpService.Refresh(false);
            RefreshHostInfo();
        }
        if (p == Page.Join) { joinError.text = ""; joinStatus.text = ""; RefreshJoin(); }
        if (p == Page.JoinCode) { codeError.text = ""; codeStatus.text = ""; RefreshJoin(); }
    }

    // Botones «con código» solo activos si hay un relay configurado (campo del menú, relay.txt o variable de entorno)
    public void RefreshRelayUi()
    {
        bool on = RelayConfig.Enabled;
        if (btnRelayHost != null) btnRelayHost.interactable = on;
        if (btnRelayJoin != null) btnRelayJoin.interactable = on;
        if (relayHint != null)
        {
            string h; int p;
            relayHint.text = RelayConfig.UsingBuiltin ? "Servidor oficial (integrado) activo. Para usar otro relay escribe su dirección arriba."
                : on && RelayConfig.TryGet(out h, out p) ? "Relay configurado: " + (RelayConfig.UserAddress.Length > 0 ? h + ":" + p : RelayConfig.DisplayAddress)
                : (string.IsNullOrEmpty(RelayConfig.Address) ? "Modo «con código» desactivado: no hay relay configurado (escribe su dirección aquí o ponla en relay.txt)." : "La dirección del relay no es válida (usa host:puerto).");
        }
    }

    private int HostPortValue() { return ParsePort(HostPortField); }

    private void RefreshHostInfo()
    {
        if (hostIps == null) return;
        NetSession ns = NetSession.Instance;
        bool hosting = NetSession.IsHost && ns != null && ns.Upnp != null;
        int port = hosting ? ns.Port : HostPortValue();
        string wan = hosting ? ns.Upnp.ExternalIp : "";
        hostIps.text = HostAddressInfo.Describe(port, localIps, PublicIpService.Status, PublicIpService.Ip,
            hosting ? ns.Upnp.Status : UpnpMapper.State.Idle, hosting ? ns.Upnp.StatusText : "", wan, hosting);
    }

    // Dirección para los amigos: relay -> código; directo -> IP pública (si se conoce) o IP local + puerto
    public string CurrentShareText()
    {
        NetSession ns = NetSession.Instance;
        if (NetSession.IsHost && ns.ViaRelay) return ns.RoomCode;
        int port = NetSession.IsHost ? ns.Port : HostPortValue();
        return HostAddressInfo.BestAddress(port, PublicIpService.Status == PublicIpService.State.Ok ? PublicIpService.Ip : "", HostAddressInfo.FirstLocalIp(localIps.Count > 0 ? localIps : NetUtil.LocalAddresses()));
    }

    public void CopyAddress(Text note)
    {
        ui.PlayClick();
        string t = CurrentShareText();
        if (string.IsNullOrEmpty(t)) { if (note != null) note.text = "Todavía no hay dirección que copiar."; copyNoteUntil = Time.unscaledTime + 3f; return; }
        GUIUtility.systemCopyBuffer = t;
        LastCopied = t;
        if (note != null) note.text = "Copiado al portapapeles: " + t;
        copyNoteUntil = Time.unscaledTime + 4f;
    }
    public string LastCopied { get; private set; }

    // «83.12.34.56:7777» pegado en el campo de IP -> IP y puerto por separado
    private void SplitIpPort()
    {
        if (JoinIpField == null || splitting) return;
        string h; int p;
        if (JoinIpField.text.IndexOf(':') >= 0 && NetAddress.TryParse(JoinIpField.text, NetSession.DefaultPort, out h, out p))
        {
            splitting = true;
            JoinIpField.text = h; JoinPortField.text = p.ToString(); lastIpText = h;
            splitting = false;
        }
    }

    public void OnCreateRoom()
    {
        ui.PlayClick();
        SaveName();
        NetSession ns = NetSession.Instance;
        if (ns == null) return;
        int port = ParsePort(HostPortField);
        string err;
        if (!ns.StartHost(port, NameField.text, out err)) { hostError.text = err; return; }
        PlayerPrefs.SetInt(KPort, port); PlayerPrefs.Save();
        LobbyLevel = 0;
        lobbyDirty = true;
        ui.SetScreen(UIManager.Screen.Lobby);
    }

    public void OnCreateRelayRoom()
    {
        ui.PlayClick();
        SaveName();
        NetSession ns = NetSession.Instance;
        if (ns == null) return;
        string err;
        if (!ns.StartHost(0, NameField.text, out err, true)) { LastRelayError = err; ns.LastNotice = err; return; }
        LobbyLevel = 0;
        lobbyDirty = true;
        ui.SetScreen(UIManager.Screen.Lobby);
    }
    public string LastRelayError { get; private set; }

    public void OnConnectCode()
    {
        ui.PlayClick();
        SaveName();
        NetSession ns = NetSession.Instance;
        if (ns == null) return;
        codeError.text = "";
        ns.StartClient(CodeField.text, 0, NameField.text, true);
        if (!string.IsNullOrEmpty(ns.LastError) && !ns.Connecting) codeError.text = ns.LastError;
        RefreshJoin();
    }

    public void OnConnect()
    {
        ui.PlayClick();
        SaveName();
        NetSession ns = NetSession.Instance;
        if (ns == null) return;
        joinError.text = "";
        SplitIpPort();
        string ip = JoinIpField.text.Trim();
        int port = ParsePort(JoinPortField);
        PlayerPrefs.SetString(KIp, ip); PlayerPrefs.SetInt(KPort, port); PlayerPrefs.Save();
        ns.StartClient(ip, port, NameField.text);
        if (!string.IsNullOrEmpty(ns.LastError) && !ns.Connecting) joinError.text = ns.LastError;
        RefreshJoin();
    }

    private void RefreshJoin()
    {
        NetSession ns = NetSession.Instance;
        bool busy = ns != null && ns.Connecting;
        if (btnConnect != null) btnConnect.gameObject.SetActive(!busy);
        if (btnCancel != null) btnCancel.gameObject.SetActive(busy);
        if (btnCodeConnect != null) btnCodeConnect.gameObject.SetActive(!busy);
        if (btnCodeCancel != null) btnCodeCancel.gameObject.SetActive(busy);
    }

    public void OnStartGame()
    {
        ui.PlayClick();
        NetSession ns = NetSession.Instance;
        if (ns == null || !NetSession.IsHost) return;
        ns.HostStartGame(LobbyLevel);
    }

    public void OnLeaveLobby()
    {
        ui.PlayClick();
        if (NetSession.Instance != null) NetSession.Instance.Leave(null);
        ShowPage(Page.Menu);
        ui.SetScreen(UIManager.Screen.Multi);
    }

    // Para pruebas: abre el menú multijugador
    public void ShowMulti() { ui.SetScreen(UIManager.Screen.Multi); ShowPage(Page.Menu); }

    // ------------------------------------------------------------------
    private void RefreshLobby()
    {
        NetSession ns = NetSession.Instance;
        if (ns == null) return;
        bool host = NetSession.IsHost;
        lobbyTitle.text = host ? "SALA (ANFITRIÓN)" : "SALA";
        if (host && ns.ViaRelay)
            lobbyInfo.text = "Sala por relay (" + RelayConfig.DisplayAddress + ")\nElige el nivel y pulsa «Empezar» cuando estéis todos.";
        else if (host)
            lobbyInfo.text = "Puerto TCP " + ns.Port + "  ·  IPs: " + string.Join("  |  ", NetUtil.LocalAddresses().ToArray()) + "\nElige el nivel y pulsa «Empezar» cuando estéis todos.";
        else
            lobbyInfo.text = "Conectado con el anfitrión. Tu nombre: " + ns.LocalName;
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < NetSession.MaxPlayers; i++)
        {
            if (i < ns.Players.Count)
            {
                NetPlayer p = ns.Players[i];
                sb.AppendLine((i + 1) + ". " + p.name + (p.id == 0 ? "  [anfitrión]" : "") + (p.isLocal ? "  (tú)" : ""));
            }
            else sb.AppendLine((i + 1) + ". — esperando jugador —");
        }
        lobbyPlayers.text = sb.ToString();
        lobbyLevelText.gameObject.SetActive(host);
        btnPrev.gameObject.SetActive(host); btnNext.gameObject.SetActive(host);
        btnStart.gameObject.SetActive(host);
        btnStart.interactable = !(ns.ViaRelay && ns.RoomCode.Length == 0);
        btnCopyLobby.gameObject.SetActive(host);
        lobbyAddr.gameObject.SetActive(host);
        RectTransform bb = (RectTransform)btnLobbyBack.transform;
        bb.anchoredPosition = new Vector2(host ? 250f : 0f, bb.anchoredPosition.y);
        bb.sizeDelta = new Vector2(host ? 440f : 520f, bb.sizeDelta.y);
        if (host) RefreshLobbyAddr(ns);
        lobbyWait.gameObject.SetActive(!host);
        if (host) lobbyLevelText.text = "Nivel " + LobbyLevel + " — " + LevelConfig.Get(LobbyLevel).name;
        lobbyDirty = false;
    }

    private void RefreshLobbyAddr(NetSession ns)
    {
        if (lobbyAddr == null) return;
        if (ns.ViaRelay)
        {
            string code = ns.RoomCode;
            string t = code.Length > 0
                ? "CÓDIGO DE SALA:  " + code + "   (díselo a tus amigos: Multijugador → Unirse con código)"
                : ns.TransportStatus;
            if (ns.TransportError == "relay_lost") t += "\n" + ns.TransportStatus;
            lobbyAddr.text = t;
            return;
        }
        string pub = PublicIpService.Status == PublicIpService.State.Ok ? PublicIpService.Ip : "";
        string best = HostAddressInfo.BestAddress(ns.Port, pub, HostAddressInfo.FirstLocalIp(localIps.Count > 0 ? localIps : NetUtil.LocalAddresses()));
        string up = ns.Upnp != null ? ns.Upnp.StatusText : "";
        string cg = ns.Upnp != null ? HostAddressInfo.CgnatWarning(ns.Upnp.ExternalIp, pub) : "";
        string net = pub.Length > 0 ? "IP pública " + pub + ":" + ns.Port + " (Internet)  ·  " : (PublicIpService.Status == PublicIpService.State.Failed ? "IP pública no disponible (¿sin Internet?)  ·  " : "detectando IP pública...  ·  ");
        lobbyAddr.text = net + "Dirección para copiar: " + best + "\n" + up + (cg.Length > 0 ? "\n" + cg : "");
    }

    private void Update()
    {
        if (copyNoteUntil > 0f && Time.unscaledTime > copyNoteUntil)
        {
            copyNoteUntil = 0f;
            if (copyNoteHost != null) copyNoteHost.text = "";
            if (copyNoteLobby != null) copyNoteLobby.text = "";
        }
        infoTimer += Time.unscaledDeltaTime;
        if (infoTimer > 0.4f)
        {
            infoTimer = 0f;
            if (ui != null && ui.Current == UIManager.Screen.Multi && CurrentPage == Page.Host) RefreshHostInfo();
            if (ui != null && ui.Current == UIManager.Screen.Lobby && NetSession.IsHost) { lobbyDirty = true; }
        }
        NetSession ns = NetSession.Instance;
        UIManager.Screen cur = ui != null ? ui.Current : UIManager.Screen.None;

        // aviso en el menú principal (error / desconexión)
        if (ns != null && !string.IsNullOrEmpty(ns.LastNotice) && (cur == UIManager.Screen.Main || cur == UIManager.Screen.Multi))
        {
            if (noticeUntil <= 0f) { noticeUntil = Time.unscaledTime + 10f; notice.text = ns.LastNotice; noticeBg.SetActive(true); }
            else if (Time.unscaledTime > noticeUntil) { ns.LastNotice = ""; noticeUntil = 0f; noticeBg.SetActive(false); }
        }
        else if (noticeUntil > 0f && (ns == null || string.IsNullOrEmpty(ns.LastNotice))) { noticeUntil = 0f; noticeBg.SetActive(false); }

        if (ns == null) return;

        // página «Unirse»: estado de la conexión
        if (cur == UIManager.Screen.Multi && (CurrentPage == Page.Join || CurrentPage == Page.JoinCode))
        {
            Text stT = CurrentPage == Page.Join ? joinStatus : codeStatus, erT = CurrentPage == Page.Join ? joinError : codeError;
            stT.text = ns.Connecting ? ns.ConnectStatus : "";
            if (!ns.Connecting && !string.IsNullOrEmpty(ns.LastError)) erT.text = ns.LastError;
            RefreshJoin();
            if (NetSession.IsClient && !ns.Connecting && ns.PlayerCount > 0 && !ns.InGame)
            {
                lobbyDirty = true;
                ui.SetScreen(UIManager.Screen.Lobby);
            }
        }
        if (cur == UIManager.Screen.Lobby)
        {
            if (!NetSession.Active) { ShowPage(Page.Menu); ui.SetScreen(UIManager.Screen.Multi); }
            else if (lobbyDirty) RefreshLobby();
        }

        UpdateHud(ns, cur);
    }

    private void UpdateHud(NetSession ns, UIManager.Screen cur)
    {
        bool show = ns != null && NetSession.Active && ns.InGame && ui != null && ui.GameplayActive && ns.WorldEpoch != 0;
        if (hudCanvas.gameObject.activeSelf != show) hudCanvas.gameObject.SetActive(show);
        if (!show) { if (ChatTyping) CloseChat(); return; }

        GameManager gm = GameManager.Instance;
        bool playing = gm != null && gm.State == GameManager.GameState.Playing;

        // equipo
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (NetPlayer p in ns.Players)
        {
            bool down = p.isLocal ? ns.LocalDowned : p.downed;
            sb.Append(down ? "<color=#ff6a5a>✖ " : "<color=#9dff9d>● ").Append(p.name).Append(p.isLocal ? " (tú)" : "").Append(down ? " · derribado" : "").AppendLine("</color>");
        }
        teamText.text = sb.ToString();
        exitText.text = ns.ExitCount > 0 ? "Jugadores en la salida: " + ns.ExitCount + "/" + ns.ExitTotal : "";

        if (ns.LocalDowned && playing)
        {
            downedText.text = "ESTÁS DERRIBADO\n<size=28>Espera a que un compañero se quede a tu lado para reanimarte" + (ns.LocalRevive01 > 0.01f ? "  ·  " + Mathf.RoundToInt(ns.LocalRevive01 * 100f) + "%" : "") + "</size>";
        }
        else downedText.text = "";

        pauseNote.text = cur == UIManager.Screen.Pause ? "Pausa (la partida sigue)" : "";

        // chat: líneas recientes
        System.Text.StringBuilder cs = new System.Text.StringBuilder();
        float now = Time.unscaledTime;
        foreach (ChatLine l in chatLines)
        {
            float age = now - l.time;
            if (age < 12f || ChatTyping) cs.AppendLine(l.text);
        }
        chatLog.text = cs.ToString();

        // abrir/enviar/cancelar chat
        if (ChatTyping)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) SubmitChat();
            else if (!playing) CloseChat();
        }
        else if (playing && cur == UIManager.Screen.None && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) OpenChat();
    }
}
