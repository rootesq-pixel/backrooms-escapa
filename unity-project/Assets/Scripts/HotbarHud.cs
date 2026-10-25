using UnityEngine;
using UnityEngine.UI;

// Barra rápida (teclas 1-5) y medidores de batería / resistencia / cordura, en la parte inferior de la pantalla durante el juego.
public class HotbarHud : MonoBehaviour
{
    public const int Slots = Inventory.HotbarSize;

    private Inventory inv;
    private Canvas canvas;
    private readonly Image[] icon = new Image[Slots];
    private readonly Image[] border = new Image[Slots];
    private readonly Text[] count = new Text[Slots];
    private readonly float[] flash = new float[Slots];
    private readonly ItemType[] lastType = new ItemType[Slots];
    private readonly int[] lastCount = new int[Slots];
    private RectTransform batFill, staFill, sanFill;
    private RectTransform hpFill;
    private Image sanFillImg, staFillImg;

    public Canvas HudCanvas { get { return canvas; } }
    public bool Visible { get { return canvas != null && canvas.enabled; } }
    public int SlotCountBuilt { get { return Slots; } }
    public ItemType SlotType(int i) { return lastType[i]; }
    public int SlotAmount(int i) { return lastCount[i]; }
    public Color StaminaColor { get { return staFillImg != null ? staFillImg.color : Color.clear; } }
    // v7.0.4: todo el bloque inferior va anclado al borde inferior (antes al centro: en pantallas muy anchas se salía por abajo).
    // TopEdge = borde superior del bloque (etiquetas BAT/RES/COR) en unidades del lienzo de referencia, medido desde abajo.
    public const float BaseY = -470f, TopEdge = BaseY + 540f + 146f;
    public readonly System.Collections.Generic.List<RectTransform> LayoutRects = new System.Collections.Generic.List<RectTransform>();
    public float Meter(int which) { RectTransform r = which == 0 ? batFill : which == 1 ? staFill : sanFill; return r != null ? r.anchorMax.x : -1f; }

    public static HotbarHud Create(Inventory inv)
    {
        HotbarHud h = inv.gameObject.AddComponent<HotbarHud>();
        h.inv = inv;
        h.Build();
        return h;
    }

    private void Build()
    {
        canvas = UiStyle.NewOverlayCanvas(transform, "HotbarCanvas", 15);
        Transform root = canvas.transform;

        // Medidores
        float baseY = BaseY;      // respecto al centro de un lienzo de 1080 de alto; luego se reancla abajo
        float mx = -247.5f;
        string[] names = { "VIDA", "BAT", "RES", "COR" };   // v7.9.0: vida (roja)
        for (int j = 0; j < 4; j++)
        {
            float x = mx + j * 165f; int i = j - 1;
            Image back = UiStyle.Rect(root, "Meter" + names[j], x, baseY + 118f, 150f, 12f, new Color(0f, 0f, 0f, 0.55f));
            Image fill = UiStyle.Rect(back.transform, "Fill", 0f, 0f, 146f, 8f, i < 0 ? new Color(0.88f, 0.22f, 0.18f) : i == 0 ? new Color(0.95f, 0.85f, 0.35f) : i == 1 ? new Color(0.55f, 0.85f, 0.5f) : new Color(0.55f, 0.7f, 1f));
            RectTransform fr = fill.rectTransform;
            fr.anchorMin = new Vector2(0f, 0f); fr.anchorMax = new Vector2(1f, 1f);
            fr.offsetMin = new Vector2(2f, 2f); fr.offsetMax = new Vector2(-2f, -2f);
            if (i < 0) hpFill = fr; else if (i == 0) batFill = fr; else if (i == 1) { staFill = fr; staFillImg = fill; } else { sanFill = fr; sanFillImg = fill; }
            UiStyle.Label(root, names[j], 17, TextAnchor.MiddleCenter, x, baseY + 136f, 150f, 20f, UiStyle.Dim);
        }

        // Casillas 1-5
        float sw = 78f, gap = 8f;
        float total = Slots * sw + (Slots - 1) * gap;
        for (int i = 0; i < Slots; i++)
        {
            float x = -total * 0.5f + sw * 0.5f + i * (sw + gap);
            Image bor = UiStyle.Rect(root, "HotSlot" + (i + 1), x, baseY + 52f, sw, sw, new Color(UiStyle.Border.r, UiStyle.Border.g, UiStyle.Border.b, 0.9f));
            Image inner = UiStyle.Rect(bor.transform, "Bg", 0f, 0f, sw - 4f, sw - 4f, new Color(0.05f, 0.045f, 0.02f, 0.78f));
            Image ic = UiStyle.Rect(inner.transform, "Icon", 0f, 2f, sw - 14f, sw - 14f, Color.white);
            ic.preserveAspect = true; ic.enabled = false;
            icon[i] = ic; border[i] = bor;
            UiStyle.Label(inner.transform, (i + 1).ToString(), 18, TextAnchor.UpperLeft, -sw * 0.5f + 17f, sw * 0.5f - 17f, 30f, 24f, UiStyle.Gold);
            count[i] = UiStyle.Label(inner.transform, "", 22, TextAnchor.LowerRight, 0f, -sw * 0.5f + 22f, sw - 12f, 28f, UiStyle.Text, FontStyle.Bold);
        }

        // v7.0.4: reanclar al borde inferior (misma posición en 16:9; pegado abajo en cualquier otra proporción)
        foreach (Transform ch in root)
        {
            RectTransform rt = ch as RectTransform; if (rt == null) continue;
            Vector2 p = rt.anchoredPosition;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(p.x, p.y + 540f);
            LayoutRects.Add(rt);
        }
    }

    private void Update()
    {
        GameManager gm = GameManager.Instance;
        UIManager ui = UIManager.Instance;
        bool show = gm != null && gm.State == GameManager.GameState.Playing && (ui == null || ui.Current == UIManager.Screen.None);
        if (canvas.enabled != show) canvas.enabled = show;
        if (!show || inv == null) return;

        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < Slots; i++)
        {
            Inventory.Slot s = inv.GetSlot(i);
            if (s.type != lastType[i] || s.count != lastCount[i])
            {
                if (s.count < lastCount[i] || (s.type != lastType[i] && lastType[i] != ItemType.None) || s.count > lastCount[i]) flash[i] = 1f;
                lastType[i] = s.type; lastCount[i] = s.count;
                bool has = !s.Empty;
                icon[i].enabled = has;
                if (has) icon[i].sprite = ItemIcons.Get(s.type);
                count[i].text = has && s.type != ItemType.Flashlight && s.count > 1 ? "x" + s.count : "";
            }
            flash[i] = Mathf.MoveTowards(flash[i], 0f, dt * 2.5f);
            bool dimItem = lastType[i] == ItemType.Flashlight && gm.flashlight != null && !gm.flashlight.IsOn;
            Color c = Color.Lerp(UiStyle.Border, UiStyle.BorderHi, flash[i]);
            c.a = Mathf.Lerp(0.9f, 1f, flash[i]);
            border[i].color = c;
            if (icon[i].enabled) icon[i].color = dimItem ? new Color(0.6f, 0.6f, 0.6f, 0.9f) : (inv.IsWrongKey(lastType[i]) ? new Color(1f, 0.42f, 0.38f, 0.75f) : Color.white);
        }

        if (gm.flashlight != null) SetFill(batFill, gm.flashlight.Battery01);
        if (inv.fpc != null) SetFill(staFill, inv.fpc.Stamina01);
        if (hpFill != null) SetFill(hpFill, Health.Instance != null ? Health.Instance.Health01 : 1f);
        if (staFillImg != null) staFillImg.color = Adrenaline.Tint(new Color(0.55f, 0.85f, 0.5f));   // v7.0.4: RES teñida con adrenalina
        if (inv.sanity != null)
        {
            float s = inv.sanity.Sanity01;
            SetFill(sanFill, s);
            sanFillImg.color = Color.Lerp(new Color(0.9f, 0.3f, 0.25f), new Color(0.55f, 0.7f, 1f), Mathf.Clamp01(s * 1.4f));
        }
    }

    private static void SetFill(RectTransform r, float v) { if (r != null) r.anchorMax = new Vector2(Mathf.Clamp01(v), 1f); }
}
