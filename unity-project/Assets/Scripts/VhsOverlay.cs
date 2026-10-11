using System;
using UnityEngine;
using UnityEngine.UI;

// Superposición de camcorder (UI): punto rojo parpadeante "REC", "PLAY ►", batería ligada a la linterna,
// código de tiempo y fecha en fuente monoespaciada tipo VCR. Tecla V: activa/desactiva el modo VHS.
public class VhsOverlay : MonoBehaviour
{
    private Canvas canvas;
    private Text recText, playText, timeText, dateText, batText;
    private Image[] batSegs;
    private Image batFrame;
    private Font mono;
    private string dateCache;
    private float timecodeSeconds;

    public bool Visible { get { return canvas != null && canvas.enabled; } }
    public string TimecodeText { get { return timeText != null ? timeText.text : string.Empty; } }

    private void Awake()
    {
        Build();
        dateCache = DateTime.Now.ToString("MMM dd yyyy", System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant();
        if (dateText != null) dateText.text = dateCache;
    }

    private Font MonoFont()
    {
        if (mono != null) return mono;
        try { mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Lucida Console", "DejaVu Sans Mono", "Liberation Mono" }, 28); }
        catch (Exception) { mono = null; }
        if (mono == null) mono = ProceduralAssets.GetDefaultFont();
        return mono;
    }

    private void Build()
    {
        GameObject go = new GameObject("VhsOverlayCanvas");
        go.transform.SetParent(transform, false);
        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        CanvasScaler sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;

        recText = MakeText(go.transform, "Rec", "● REC", 44, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-200f, -34f), new Vector2(360f, 60f));
        playText = MakeText(go.transform, "Play", "PLAY ►", 34, TextAnchor.UpperRight, new Vector2(1f, 1f), new Vector2(-44f, -88f), new Vector2(360f, 50f));
        timeText = MakeText(go.transform, "Timecode", "00:00:00", 44, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-44f, 78f), new Vector2(420f, 60f));
        dateText = MakeText(go.transform, "Date", "OCT 11 2026", 32, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-44f, 38f), new Vector2(420f, 44f));

        // Icono de batería (4 segmentos) junto a REC
        GameObject bat = new GameObject("Battery", typeof(RectTransform));
        bat.transform.SetParent(go.transform, false);
        RectTransform br = (RectTransform)bat.transform;
        br.anchorMin = br.anchorMax = new Vector2(1f, 1f);
        br.pivot = new Vector2(1f, 1f);
        br.sizeDelta = new Vector2(78f, 34f);
        br.anchoredPosition = new Vector2(-44f, -40f);
        batFrame = bat.AddComponent<Image>();
        batFrame.color = new Color(0.95f, 0.95f, 0.9f, 0.9f);
        batFrame.raycastTarget = false;
        GameObject inner = new GameObject("Inner", typeof(RectTransform));
        inner.transform.SetParent(bat.transform, false);
        RectTransform ir = (RectTransform)inner.transform;
        ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one; ir.offsetMin = new Vector2(4f, 4f); ir.offsetMax = new Vector2(-4f, -4f);
        Image ib = inner.AddComponent<Image>(); ib.color = new Color(0f, 0f, 0f, 0.85f); ib.raycastTarget = false;
        GameObject tip = new GameObject("Tip", typeof(RectTransform));
        tip.transform.SetParent(bat.transform, false);
        RectTransform tr = (RectTransform)tip.transform;
        tr.anchorMin = new Vector2(1f, 0.5f); tr.anchorMax = new Vector2(1f, 0.5f); tr.pivot = new Vector2(0f, 0.5f);
        tr.sizeDelta = new Vector2(6f, 14f); tr.anchoredPosition = Vector2.zero;
        Image tip2 = tip.AddComponent<Image>(); tip2.color = batFrame.color; tip2.raycastTarget = false;
        batSegs = new Image[4];
        for (int i = 0; i < 4; i++)
        {
            GameObject seg = new GameObject("Seg" + i, typeof(RectTransform));
            seg.transform.SetParent(inner.transform, false);
            RectTransform sr = (RectTransform)seg.transform;
            sr.anchorMin = new Vector2(i / 4f, 0f); sr.anchorMax = new Vector2((i + 1) / 4f, 1f);
            sr.offsetMin = new Vector2(2f, 2f); sr.offsetMax = new Vector2(-2f, -2f);
            batSegs[i] = seg.AddComponent<Image>();
            batSegs[i].raycastTarget = false;
        }
    }

    private Text MakeText(Transform parent, string name, string content, int size, TextAnchor anchor, Vector2 anchorPoint, Vector2 pos, Vector2 sizeDelta)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = MonoFont();
        t.fontSize = size;
        t.alignment = anchor;
        t.color = new Color(0.96f, 0.96f, 0.92f, 0.92f);
        t.text = content;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        Shadow sh = go.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, 0.8f);
        sh.effectDistance = new Vector2(2f, -2f);
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = anchorPoint;
        rt.pivot = anchorPoint;
        rt.sizeDelta = sizeDelta;
        rt.anchoredPosition = pos;
        return t;
    }

    // Tecla V (también la usan las pruebas)
    public void ToggleVhs()
    {
        if (UIManager.InputBlocked || UIManager.Instance == null || !UIManager.Instance.GameplayActive) return;
        GameSettings.VhsOn = !GameSettings.VhsOn;
        if (GameManager.Instance != null) GameManager.Instance.ShowToast(GameSettings.VhsOn ? "Modo VHS: ON" : "Modo VHS: OFF");
    }

    private void Update()
    {
        // V: alternar el modo VHS (no si hay un menú abierto)
        if (Input.GetKeyDown(KeyCode.V)) ToggleVhs();

        bool on = GameSettings.VhsOn && GameSettings.VhsIntensity > 0.001f;
        if (canvas.enabled != on) canvas.enabled = on;
        if (!on) return;

        // REC parpadeante (1 s)
        bool blink = (Time.unscaledTime % 1.2f) < 0.8f;
        recText.text = blink ? "<color=#ff2a20>●</color> REC" : "<color=#00000000>●</color> REC";
        recText.supportRichText = true;
        playText.text = "PLAY ►";

        // Código de tiempo: tiempo de partida (o reloj en el menú)
        float secs;
        if (GameManager.Instance != null && GameBootstrap.Instance != null && !GameBootstrap.Instance.InMenu)
            secs = GameBootstrap.Instance.RunElapsed + GameManager.Instance.Elapsed;
        else secs = Time.unscaledTime;
        int total = Mathf.FloorToInt(secs);
        timeText.text = string.Format("{0:00}:{1:00}:{2:00}", total / 3600, (total / 60) % 60, total % 60);

        // Batería (linterna)
        float b = 1f;
        if (GameManager.Instance != null && GameManager.Instance.flashlight != null && GameBootstrap.Instance != null && !GameBootstrap.Instance.InMenu)
            b = GameManager.Instance.flashlight.Battery01;
        int lit = Mathf.CeilToInt(b * 4f - 0.001f);
        Color on1 = b < 0.25f ? (blink ? new Color(1f, 0.2f, 0.15f, 0.95f) : new Color(1f, 0.2f, 0.15f, 0.2f)) : new Color(0.9f, 0.95f, 0.85f, 0.95f);
        for (int i = 0; i < 4; i++) batSegs[i].color = i < lit ? on1 : new Color(1f, 1f, 1f, 0.07f);
    }
}
