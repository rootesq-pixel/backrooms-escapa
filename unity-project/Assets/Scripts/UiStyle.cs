using UnityEngine;
using UnityEngine.UI;

// Estilo común (amarillento oscuro, bordes finos, fuente monoespaciada tipo VHS) y helpers para construir UI desde código.
public static class UiStyle
{
    public static readonly Color Text = new Color(0.95f, 0.91f, 0.7f);
    public static readonly Color Dim = new Color(0.68f, 0.63f, 0.45f);
    public static readonly Color Gold = new Color(0.96f, 0.84f, 0.38f);
    public static readonly Color Panel = new Color(0.045f, 0.04f, 0.02f, 0.94f);
    public static readonly Color Slot = new Color(0.085f, 0.075f, 0.04f, 0.95f);
    public static readonly Color Border = new Color(0.42f, 0.35f, 0.14f, 1f);
    public static readonly Color BorderHi = new Color(0.98f, 0.82f, 0.30f, 1f);
    public static readonly Color Good = new Color(0.45f, 0.85f, 0.5f);

    private static Font mono;
    public static Font Mono()
    {
        if (mono != null) return mono;
        try { mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Courier New", "Lucida Console", "DejaVu Sans Mono", "Liberation Mono" }, 28); }
        catch (System.Exception) { mono = null; }
        if (mono == null) mono = ProceduralAssets.GetDefaultFont();
        return mono;
    }

    // Rectángulo centrado con el ancla en el centro del padre
    public static Image Rect(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        RectTransform rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
        return img;
    }

    // Rectángulo con borde fino: devuelve el interior (el borde es el padre)
    public static Image Framed(Transform parent, string name, float x, float y, float w, float h, Color fill, Color border, float thick)
    {
        Image b = Rect(parent, name + "_Border", x, y, w, h, border);
        Image inner = Rect(b.transform, name, 0f, 0f, w - thick * 2f, h - thick * 2f, fill);
        return inner;
    }

    public static Text Label(Transform parent, string content, int size, TextAnchor anchor, float x, float y, float w, float h, Color? color = null, FontStyle style = FontStyle.Normal)
    {
        GameObject go = new GameObject("Text", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = Mono(); t.fontSize = size; t.alignment = anchor; t.fontStyle = style;
        t.color = color ?? Text; t.text = content;
        t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, y);
        return t;
    }

    public static Canvas NewOverlayCanvas(Transform parent, string name, int sortingOrder)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        Canvas c = go.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = sortingOrder;
        CanvasScaler sc = go.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        return c;
    }
}
