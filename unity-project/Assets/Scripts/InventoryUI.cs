using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Pantalla de inventario (tecla I): rejilla 5x3 de casillas con iconos, detalle del objeto con botones Usar / Soltar,
// medidores (batería, resistencia, cordura), objetivo del nivel y pestaña de notas. Animación de apertura y cierre.
// Se construye desde código (UnityEngine.UI) dentro del panel raíz que crea UIManager.
public class InventoryUI : MonoBehaviour
{
    public const int Cols = 5, Rows = 3;

    // --- Vista de una casilla (clic = seleccionar; arrastrar y soltar = mover / intercambiar) ---
    private class SlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        public InventoryUI ui; public int index;
        public Image border, bg, icon; public Text count, key;
        public void OnPointerEnter(PointerEventData e) { ui.Hover(index); }
        public void OnPointerExit(PointerEventData e) { ui.Hover(-1); }
        public void OnPointerClick(PointerEventData e) { if (!e.dragging) ui.Select(index); }
        public void OnBeginDrag(PointerEventData e) { ui.BeginDrag(index, e); }
        public void OnDrag(PointerEventData e) { ui.DragMove(e); }
        public void OnEndDrag(PointerEventData e) { ui.EndDrag(); }
        public void OnDrop(PointerEventData e) { ui.DropOn(index); }
    }

    private class NoteRow : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        public InventoryUI ui; public int noteId; public Image bg; public Text label;
        public void OnPointerEnter(PointerEventData e) { }
        public void OnPointerClick(PointerEventData e) { ui.SelectNote(noteId); }
    }

    private UIManager manager;
    private RectTransform panel;
    private CanvasGroup group;
    private GameObject itemsPage, notesPage;
    private readonly List<SlotView> slotViews = new List<SlotView>();
    private readonly List<NoteRow> noteRows = new List<NoteRow>();
    private Text titleText, levelText, objectiveText, hintText, tabItemsLabel, tabNotesLabel;
    private Image tabItemsBg, tabNotesBg;
    private Image bigIcon;
    private Text detailName, detailQty, detailDesc, detailKeyHint, useLabel, dropLabel;
    private Button useButton, dropButton;
    private RectTransform batFill, staFill, sanFill;
    private Text batText, staText, sanText, usedText;
    private GameObject tooltip; private Text tipName, tipBody; private RectTransform tipRect;
    private Text noteTitle, noteBody, noteCount;

    private float anim;          // 0 = cerrado, 1 = abierto
    private bool opening, closing;
    private int selected = 1, hovered = -1, selectedNote = -1;
    private bool notesTab;

    public int SelectedIndex { get { return selected; } }
    public bool NotesTabOpen { get { return notesTab; } }
    public int SlotViewCount { get { return slotViews.Count; } }
    public int SelectedNoteId { get { return selectedNote; } }
    public bool IsClosing { get { return closing; } }
    public float Openness { get { return anim; } }
    public string DetailName { get { return detailName != null ? detailName.text : ""; } }
    public string DetailText { get { return detailDesc != null ? detailDesc.text : ""; } }
    public string NoteBodyText { get { return noteBody != null ? noteBody.text : ""; } }
    public bool UseEnabled { get { return useButton != null && useButton.interactable; } }
    public bool DropEnabled { get { return dropButton != null && dropButton.interactable; } }
    public int VisibleNoteRows { get { int n = 0; foreach (NoteRow r in noteRows) if (r.noteId >= 0 && Inventory.Instance != null && Inventory.Instance.HasNote(r.noteId)) n++; return n; } }
    public bool SlotShowsIcon(int i) { return i >= 0 && i < slotViews.Count && slotViews[i].icon.enabled; }
    public string SlotCountText(int i) { return i >= 0 && i < slotViews.Count ? slotViews[i].count.text : ""; }
    public Image BigIconImage { get { return bigIcon; } }
    public bool TooltipVisible { get { return tooltip != null && tooltip.activeSelf; } }
    public string TooltipName { get { return tipName != null ? tipName.text : ""; } }

    // ------------------------------------------------------------------
    // Construcción
    // ------------------------------------------------------------------
    // Paleta del maletín: pintura de plantilla amarilla sobre metal y tinta sobre papel
    private static readonly Color Ink = new Color(0.13f, 0.11f, 0.09f, 1f);
    private static readonly Color InkDim = new Color(0.36f, 0.31f, 0.24f, 1f);
    private static readonly Color Paint = new Color(0.90f, 0.79f, 0.42f, 1f);
    private static readonly Color PaintDim = new Color(0.66f, 0.62f, 0.49f, 1f);
    private static readonly Color TabOn = new Color(0.86f, 0.80f, 0.62f, 1f);
    private static readonly Color TabOff = new Color(0.13f, 0.14f, 0.11f, 1f);
    private Text detailStats;
    private GameObject detailSep;
    private Image dragGhost;
    private int dragFrom = -1, dragTarget = -1;

    private static Image Sprite(Image img, string name, float border = 0f)
    {
        Sprite sp = UiStyle.UiSprite(name, border);
        if (sp != null) { img.sprite = sp; if (border > 0f) img.type = Image.Type.Sliced; }
        return img;
    }

    private static Text Shadowed(Text t, float a = 0.85f)
    {
        Shadow sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = new Color(0f, 0f, 0f, a); sh.effectDistance = new Vector2(2f, -2f);
        return t;
    }

    public void Build(UIManager mgr)
    {
        manager = mgr;
        group = gameObject.AddComponent<CanvasGroup>();
        Image dim = GetComponent<Image>();
        if (dim != null) dim.raycastTarget = true;

        // Maletín metálico (borde oscuro + chapa)
        Image border = UiStyle.Rect(transform, "PanelBorder", 0f, 0f, 1580f, 900f, new Color(0.02f, 0.02f, 0.015f, 1f));
        panel = border.rectTransform;
        Image back = Sprite(UiStyle.Rect(border.transform, "Panel", 0f, 0f, 1576f, 896f, Color.white), "case_metal");
        Transform P = back.transform;

        titleText = Shadowed(UiStyle.SetFont(UiStyle.Label(P, "INVENTARIO", 58, TextAnchor.MiddleLeft, -440f, 384f, 640f, 70f, Paint), UiStyle.SansBold()));
        UiStyle.SetFont(UiStyle.Label(P, "KIT DE SUPERVIVENCIA  ·  EQUIPO PERSONAL", 17, TextAnchor.MiddleLeft, -440f, 345f, 640f, 26f, PaintDim), UiStyle.Mono());
        levelText = Shadowed(UiStyle.SetFont(UiStyle.Label(P, "", 24, TextAnchor.MiddleRight, 360f, 384f, 760f, 44f, Paint), UiStyle.Mono()));

        // Pestañas
        BuildTab(P, "Objetos", -680f, 300f, out tabItemsBg, out tabItemsLabel, () => SetTab(false));
        BuildTab(P, "Notas", -495f, 300f, out tabNotesBg, out tabNotesLabel, () => SetTab(true));

        BuildItemsPage(P);
        BuildNotesPage(P);
        BuildDetail(P);
        BuildVitals(P);

        Image objBox = UiStyle.Rect(P, "ObjectiveBox", -2f, -330f, 1480f, 66f, new Color(0f, 0f, 0f, 0.38f));
        objectiveText = UiStyle.SetFont(UiStyle.Label(objBox.transform, "", 20, TextAnchor.MiddleLeft, 0f, 0f, 1452f, 60f, Paint), UiStyle.Mono());
        UiStyle.Rect(P, "FootGroove", 0f, -372f, 1500f, 2f, new Color(0f, 0f, 0f, 0.55f));
        UiStyle.Rect(P, "FootGrooveHi", 0f, -374f, 1500f, 1f, new Color(1f, 1f, 0.9f, 0.08f));
        hintText = UiStyle.SetFont(UiStyle.Label(P, "1-5: barra rápida  ·  clic: seleccionar  ·  arrastrar: mover  ·  Tab: notas  ·  I / Esc: cerrar", 19, TextAnchor.MiddleLeft, -245f, -404f, 890f, 36f, PaintDim), UiStyle.Sans());
        MakeButton(P, "Cerrar (I / Esc)", 580f, -404f, 290f, 48f, () => { manager.PlayClick(); manager.SetScreen(UIManager.Screen.None); }, 24, out Image _, out Text _);

        // Tooltip: etiqueta de papel flotante
        tooltip = new GameObject("Tooltip", typeof(RectTransform));
        tooltip.transform.SetParent(transform, false);
        Image tb = UiStyle.Rect(tooltip.transform, "Border", 0f, 0f, 370f, 160f, new Color(0f, 0f, 0f, 0.6f));
        tipRect = (RectTransform)tooltip.transform;
        tipRect.anchorMin = tipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tipRect.sizeDelta = new Vector2(370f, 160f);
        tipRect.pivot = new Vector2(0f, 1f);
        tb.rectTransform.anchorMin = tb.rectTransform.anchorMax = new Vector2(0f, 1f);
        tb.rectTransform.pivot = new Vector2(0f, 1f);
        tb.rectTransform.anchoredPosition = Vector2.zero;
        Image ti = Sprite(UiStyle.Rect(tb.transform, "Bg", 0f, 0f, 364f, 154f, Color.white), "paper_tag");
        tipName = UiStyle.SetFont(UiStyle.Label(ti.transform, "", 24, TextAnchor.UpperLeft, 0f, 50f, 330f, 32f, Ink), UiStyle.TypewriterBold());
        tipBody = UiStyle.SetFont(UiStyle.Label(ti.transform, "", 18, TextAnchor.UpperLeft, 0f, -20f, 330f, 100f, Ink), UiStyle.Typewriter());
        tooltip.SetActive(false);

        // Fantasma del objeto arrastrado
        dragGhost = UiStyle.Rect(transform, "DragGhost", 0f, 0f, 120f, 120f, new Color(1f, 1f, 1f, 0.85f));
        dragGhost.preserveAspect = true; dragGhost.raycastTarget = false;
        dragGhost.gameObject.SetActive(false);

        // Todos los textos se ajustan a su caja: salto de línea + reducción automática (mínimo legible) y sin desbordar
        foreach (Text t in GetComponentsInChildren<Text>(true)) FitText(t);

        // Raycast: el panel recibe eventos
        foreach (Graphic g in GetComponentsInChildren<Graphic>(true))
            if (g.GetComponent<SlotView>() != null || g.GetComponent<NoteRow>() != null || g.GetComponent<Button>() != null) g.raycastTarget = true;

        SetTab(false);
        gameObject.SetActive(false);
    }

    private static void FitText(Text t)
    {
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        t.resizeTextForBestFit = true;
        t.resizeTextMaxSize = t.fontSize;
        t.resizeTextMinSize = Mathf.Max(11, Mathf.RoundToInt(t.fontSize * 0.6f));
    }

    // Para pruebas: sustituye el texto del objetivo / nombre del nivel (cadenas largas)
    public static string DebugObjective, DebugLevelName;

    // Para pruebas: ¿todos los textos visibles caben en su caja?
    public string OverflowingTexts()
    {
        Canvas.ForceUpdateCanvases();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (Text t in GetComponentsInChildren<Text>(false))
        {
            if (string.IsNullOrEmpty(t.text)) continue;
            TextGenerationSettings g = t.GetGenerationSettings(t.rectTransform.rect.size);
            TextGenerator gen = new TextGenerator();
            gen.Populate(t.text, g);
            int shown = gen.characterCountVisible;
            int need = t.text.Replace("\n", "").Replace(" ", "").Length;
            string vis = t.text.Substring(0, Mathf.Min(shown, t.text.Length)).Replace("\n", "").Replace(" ", "");
            if (vis.Length < need) sb.Append(t.name).Append(":'").Append(t.text.Length > 30 ? t.text.Substring(0, 30) : t.text).Append("' ");
        }
        return sb.ToString();
    }

    private void BuildTab(Transform P, string label, float x, float y, out Image bg, out Text lbl, UnityEngine.Events.UnityAction act)
    {
        Image b = UiStyle.Rect(P, "Tab_" + label, x, y, 176f, 44f, new Color(0f, 0f, 0f, 0.7f));
        Image inner = UiStyle.Rect(b.transform, "Bg", 0f, 0f, 172f, 40f, TabOff);
        Button btn = b.gameObject.AddComponent<Button>();
        b.raycastTarget = true;
        btn.targetGraphic = b;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => { manager.PlayClick(); act(); });
        bg = inner;
        lbl = UiStyle.SetFont(UiStyle.Label(inner.transform, label.ToUpperInvariant(), 21, TextAnchor.MiddleCenter, 0f, 0f, 170f, 40f, Ink), UiStyle.SansBold());
    }

    private Button MakeButton(Transform P, string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction act, int size, out Image inner, out Text label)
    {
        Image b = UiStyle.Rect(P, "Btn_" + text, x, y, w, h, new Color(0.03f, 0.03f, 0.02f, 1f));
        b.raycastTarget = true;
        inner = UiStyle.Rect(b.transform, "Bg", 0f, 0f, w - 4f, h - 4f, new Color(0.17f, 0.18f, 0.14f, 1f));
        UiStyle.Rect(inner.transform, "Hi", 0f, (h - 4f) * 0.5f - 1f, w - 4f, 2f, new Color(1f, 1f, 0.9f, 0.12f));
        label = Shadowed(UiStyle.SetFont(UiStyle.Label(inner.transform, text, size, TextAnchor.MiddleCenter, 0f, 0f, w - 12f, h - 4f, Paint), UiStyle.SansBold()));
        Button btn = b.gameObject.AddComponent<Button>();
        btn.targetGraphic = inner;
        inner.raycastTarget = true;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.6f, 1.5f, 1.1f, 1f);
        cb.pressedColor = new Color(2.0f, 1.8f, 1.2f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
        cb.fadeDuration = 0.06f;
        btn.colors = cb;
        btn.onClick.AddListener(act);
        return btn;
    }

    private void BuildItemsPage(Transform P)
    {
        itemsPage = new GameObject("ItemsPage", typeof(RectTransform));
        itemsPage.transform.SetParent(P, false);
        RectTransform pr = (RectTransform)itemsPage.transform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.sizeDelta = Vector2.zero; pr.anchoredPosition = Vector2.zero;

        float cell = 118f, gap = 10f;
        float gridW = Cols * cell + (Cols - 1) * gap, gridH = Rows * cell + (Rows - 1) * gap;
        float left = -690f, top = 250f;
        // bandeja de espuma recortada
        Image tray = UiStyle.Rect(itemsPage.transform, "TrayEdge", left + gridW * 0.5f, top - gridH * 0.5f, gridW + 28f, gridH + 28f, new Color(0.02f, 0.02f, 0.02f, 0.9f));
        Sprite(UiStyle.Rect(tray.transform, "Foam", 0f, 0f, gridW + 22f, gridH + 22f, new Color(0.9f, 0.9f, 0.9f, 1f)), "foam_panel");
        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                int idx = r * Cols + c;
                float x = left + cell * 0.5f + c * (cell + gap);
                float y = top - cell * 0.5f - r * (cell + gap);
                SlotView sv = BuildSlot(itemsPage.transform, idx, x, y, cell);
                slotViews.Add(sv);
            }
        }
        usedText = UiStyle.SetFont(UiStyle.Label(itemsPage.transform, "", 16, TextAnchor.MiddleRight, left + gridW - 160f, top + 50f, 320f, 26f, PaintDim), UiStyle.Mono());
    }

    private SlotView BuildSlot(Transform parent, int idx, float x, float y, float size)
    {
        Image b = UiStyle.Rect(parent, "Slot" + idx, x, y, size, size, new Color(0f, 0f, 0f, 0.75f));
        b.raycastTarget = true;
        Image inner = Sprite(UiStyle.Rect(b.transform, "Bg", 0f, 0f, size - 6f, size - 6f, Color.white), "foam_slot");
        Image ic = UiStyle.Rect(inner.transform, "Icon", 0f, 2f, size - 14f, size - 14f, Color.white);
        ic.preserveAspect = true; ic.enabled = false;
        Text cnt = Shadowed(UiStyle.Label(inner.transform, "", 22, TextAnchor.LowerRight, 0f, -size * 0.5f + 22f, size - 16f, 28f, new Color(0.96f, 0.93f, 0.82f), FontStyle.Bold), 1f);
        Text key = UiStyle.SetFont(UiStyle.Label(inner.transform, idx < Inventory.HotbarSize ? (idx + 1).ToString() : "", 17, TextAnchor.UpperLeft, -size * 0.5f + 17f, size * 0.5f - 16f, 30f, 24f, Paint), UiStyle.MonoBold());
        SlotView sv = b.gameObject.AddComponent<SlotView>();
        sv.ui = this; sv.index = idx; sv.border = b; sv.bg = inner; sv.icon = ic; sv.count = cnt; sv.key = key;
        return sv;
    }

    private void BuildNotesPage(Transform P)
    {
        notesPage = new GameObject("NotesPage", typeof(RectTransform));
        notesPage.transform.SetParent(P, false);
        RectTransform pr = (RectTransform)notesPage.transform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.sizeDelta = Vector2.zero; pr.anchoredPosition = Vector2.zero;

        int total = LoreNotes.Total;                  // 5 niveles x 3 notas = 15 (2 columnas: 9 + 6)
        float rowW = 304f, rowH = 34f, gap = 6f;
        float left = -690f, top = 250f;
        Image tray = UiStyle.Rect(notesPage.transform, "ListFrame", left + rowW + gap * 0.5f, top - (9 * (rowH + gap)) * 0.5f + gap * 0.5f, rowW * 2f + gap + 28f, 9 * (rowH + gap) + 22f, new Color(0.02f, 0.02f, 0.02f, 0.9f));
        Sprite(UiStyle.Rect(tray.transform, "Foam", 0f, 0f, rowW * 2f + gap + 22f, 9 * (rowH + gap) + 16f, new Color(0.9f, 0.9f, 0.9f, 1f)), "foam_panel");
        for (int i = 0; i < total; i++)
        {
            int col = i / 9, row = i % 9;
            int level = i / LoreNotes.PerLevel, k = i % LoreNotes.PerLevel;
            float x = left + rowW * 0.5f + col * (rowW + gap);
            float y = top - rowH * 0.5f - row * (rowH + gap);
            Image b = UiStyle.Rect(notesPage.transform, "Note" + i, x, y, rowW, rowH, new Color(0f, 0f, 0f, 0.5f));
            b.raycastTarget = true;
            Image inner = UiStyle.Rect(b.transform, "Bg", 0f, 0f, rowW - 4f, rowH - 4f, TabOn);
            Text t = UiStyle.SetFont(UiStyle.Label(inner.transform, "", 18, TextAnchor.MiddleLeft, 6f, 0f, rowW - 22f, rowH - 4f, Ink), UiStyle.Typewriter());
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            NoteRow nr = b.gameObject.AddComponent<NoteRow>();
            nr.ui = this; nr.noteId = LoreNotes.IdFor(level, k); nr.bg = inner; nr.label = t;
            noteRows.Add(nr);
        }
        noteCount = UiStyle.SetFont(UiStyle.Label(notesPage.transform, "", 16, TextAnchor.MiddleRight, left + rowW * 2f + gap - 130f, top + 50f, 260f, 26f, PaintDim), UiStyle.Mono());
        notesPage.SetActive(false);
    }

    private void BuildDetail(Transform P)
    {
        float cx = 420f;
        // Etiqueta de papel sujeta con cinta, ligeramente torcida
        Image shadow = UiStyle.Rect(P, "TagShadow", cx + 8f, 75f, 680f, 500f, new Color(0f, 0f, 0f, 0.45f));
        shadow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.2f);
        Image paper = Sprite(UiStyle.Rect(P, "DetailBg", cx, 85f, 680f, 500f, Color.white), "paper_tag");
        paper.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -0.8f);
        Transform D = paper.transform;
        Image tape = Sprite(UiStyle.Rect(D, "Tape", 0f, 248f, 190f, 46f, new Color(1f, 1f, 1f, 0.92f)), "tape");
        tape.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 3f);

        // --- Detalle de objeto ---
        bigIcon = UiStyle.Rect(D, "BigIcon", -205f, 120f, 210f, 210f, Color.white);
        bigIcon.preserveAspect = true;
        detailName = UiStyle.SetFont(UiStyle.Label(D, "", 36, TextAnchor.MiddleLeft, 105f, 180f, 420f, 50f, Ink), UiStyle.TypewriterBold());
        detailQty = UiStyle.SetFont(UiStyle.Label(D, "", 23, TextAnchor.MiddleLeft, 105f, 132f, 420f, 34f, Ink), UiStyle.Typewriter());
        detailStats = UiStyle.SetFont(UiStyle.Label(D, "", 20, TextAnchor.MiddleLeft, 105f, 96f, 420f, 30f, InkDim), UiStyle.TypewriterBold());
        detailKeyHint = UiStyle.SetFont(UiStyle.Label(D, "", 19, TextAnchor.UpperLeft, 105f, 52f, 420f, 50f, InkDim), UiStyle.Typewriter());
        detailSep = UiStyle.Rect(D, "Sep", 0f, 4f, 620f, 2f, new Color(0.25f, 0.2f, 0.15f, 0.5f)).gameObject;
        detailDesc = UiStyle.SetFont(UiStyle.Label(D, "", 22, TextAnchor.UpperLeft, 0f, -72f, 610f, 140f, Ink), UiStyle.Typewriter());
        Image ui1, ui2;
        useButton = MakeButton(D, "Usar", -160f, -195f, 290f, 62f, OnUse, 28, out ui1, out useLabel);
        dropButton = MakeButton(D, "Soltar", 160f, -195f, 290f, 62f, OnDrop, 28, out ui2, out dropLabel);

        // --- Lectura de notas ---
        noteTitle = UiStyle.SetFont(UiStyle.Label(D, "", 32, TextAnchor.MiddleLeft, 0f, 185f, 610f, 50f, Ink), UiStyle.TypewriterBold());
        noteBody = UiStyle.SetFont(UiStyle.Label(D, "", 24, TextAnchor.UpperLeft, 0f, -30f, 610f, 360f, Ink), UiStyle.Typewriter());
        noteTitle.gameObject.SetActive(false);
        noteBody.gameObject.SetActive(false);
    }

    private void BuildVitals(Transform P)
    {
        string[] names = { "Linterna", "Resistencia", "Cordura" };
        Color[] cols = { new Color(0.95f, 0.82f, 0.35f), new Color(0.55f, 0.82f, 0.48f), new Color(0.55f, 0.70f, 0.98f) };
        for (int i = 0; i < 3; i++)
        {
            float y = -172f - i * 44f;
            Shadowed(UiStyle.SetFont(UiStyle.Label(P, names[i].ToUpperInvariant(), 20, TextAnchor.MiddleLeft, -690f + 110f, y, 220f, 34f, PaintDim), UiStyle.SansBold()));
            Image back = UiStyle.Rect(P, "Meter" + i, -690f + 380f, y, 340f, 24f, new Color(0f, 0f, 0f, 0.85f));
            Image bi = UiStyle.Rect(back.transform, "Bg", 0f, 0f, 336f, 20f, new Color(0.06f, 0.06f, 0.05f, 1f));
            Image fill = UiStyle.Rect(bi.transform, "Fill", 0f, 0f, 330f, 14f, cols[i]);
            RectTransform fr = fill.rectTransform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = new Vector2(3f, 3f); fr.offsetMax = new Vector2(-3f, -3f);
            Text tx = Shadowed(UiStyle.Label(P, "", 20, TextAnchor.MiddleRight, -690f + 610f, y, 90f, 34f, Paint, FontStyle.Bold));
            if (i == 0) { batFill = fr; batText = tx; } else if (i == 1) { staFill = fr; staText = tx; } else { sanFill = fr; sanText = tx; }
        }
    }

    // ------------------------------------------------------------------
    // Apertura / cierre animados
    // ------------------------------------------------------------------
    public void Open()
    {
        gameObject.SetActive(true);
        opening = true; closing = false;
        if (anim <= 0f) anim = 0.001f;
        hovered = -1;
        tooltip.SetActive(false);
        // selecciona algo útil: la primera casilla con objeto tras la linterna
        Inventory inv = Inventory.Instance;
        if (inv != null && (selected <= 0 || inv.GetSlot(selected).Empty))
        {
            selected = 0;
            for (int i = 1; i < Inventory.SlotCount; i++) if (!inv.GetSlot(i).Empty) { selected = i; break; }
        }
        selectedNote = -1;
        Apply(true);
        ApplyAnim();
    }

    public void Close()
    {
        if (!gameObject.activeSelf) return;
        opening = false; closing = true;
        tooltip.SetActive(false);
    }

    public void CloseImmediate()
    {
        opening = false; closing = false; anim = 0f;
        gameObject.SetActive(false);
    }

    private void ApplyAnim()
    {
        float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(anim), 3f);
        group.alpha = e;
        if (panel != null)
        {
            float s = Mathf.Lerp(0.9f, 1f, e) * FitScale();
            panel.localScale = new Vector3(s, s, 1f);
        }
    }

    // El maletín (1580x900) cabe siempre en pantalla: con el escalado 1920x1080 (match 0.5) las pantallas
    // muy anchas (21:9, 32:9) o muy altas (5:4) dejarían el panel cortado; se reduce con 2 % de margen.
    public bool PanelFits()
    {
        RectTransform root = (RectTransform)transform;
        Vector3[] c = new Vector3[4]; panel.GetWorldCorners(c);
        Rect rr = root.rect;
        for (int i = 0; i < 4; i++) { Vector3 l = root.InverseTransformPoint(c[i]); if (!rr.Contains(new Vector2(l.x, l.y))) return false; }
        return true;
    }

    public static float ForceFit;   // >0: escala fija (grabación del tráiler, donde la pantalla de batchmode no es la del render)
    public float FitScale()
    {
        if (ForceFit > 0f) return ForceFit;
        RectTransform root = (RectTransform)transform;
        Rect r = root.rect;
        if (r.width <= 1f || r.height <= 1f) return 1f;
        return Mathf.Min(1f, r.width / (1580f * 1.04f), r.height / (900f * 1.04f));
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (opening) { anim = Mathf.MoveTowards(anim, 1f, dt / 0.18f); if (anim >= 1f) opening = false; }
        if (closing)
        {
            anim = Mathf.MoveTowards(anim, 0f, dt / 0.13f);
            if (anim <= 0f) { closing = false; gameObject.SetActive(false); return; }
        }
        ApplyAnim();
        if (closing) return;

        // flechas para moverse por la rejilla
        if (!notesTab)
        {
            int c = selected % Cols, r = selected / Cols;
            if (Input.GetKeyDown(KeyCode.RightArrow)) c = Mathf.Min(Cols - 1, c + 1);
            if (Input.GetKeyDown(KeyCode.LeftArrow)) c = Mathf.Max(0, c - 1);
            if (Input.GetKeyDown(KeyCode.DownArrow)) r = Mathf.Min(Rows - 1, r + 1);
            if (Input.GetKeyDown(KeyCode.UpArrow)) r = Mathf.Max(0, r - 1);
            int ni = r * Cols + c;
            if (ni != selected) { selected = ni; manager.PlayClick(); }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) OnUse();
        }
        if (Input.GetKeyDown(KeyCode.Tab)) { SetTab(!notesTab); manager.PlayClick(); }
        Apply(false);
        UpdateTooltip();
    }

    // ------------------------------------------------------------------
    // Acciones
    // ------------------------------------------------------------------
    public void SetTab(bool notes)
    {
        notesTab = notes;
        itemsPage.SetActive(!notes);
        notesPage.SetActive(notes);
        tabItemsBg.color = !notes ? TabOn : TabOff;
        tabNotesBg.color = notes ? TabOn : TabOff;
        tabItemsLabel.color = !notes ? Ink : PaintDim;
        tabNotesLabel.color = notes ? Ink : PaintDim;
        bool items = !notes;
        detailName.gameObject.SetActive(items); detailQty.gameObject.SetActive(items); detailKeyHint.gameObject.SetActive(items);
        detailDesc.gameObject.SetActive(items); detailStats.gameObject.SetActive(items); detailSep.SetActive(items); useButton.gameObject.SetActive(items); dropButton.gameObject.SetActive(items);
        bigIcon.gameObject.SetActive(items);
        noteTitle.gameObject.SetActive(notes); noteBody.gameObject.SetActive(notes);
        tooltip.SetActive(false);
        if (notes && selectedNote < 0) selectedNote = FirstFoundNote();
        Apply(true);
    }

    private int FirstFoundNote()
    {
        Inventory inv = Inventory.Instance;
        if (inv == null || inv.notes.Count == 0) return -1;
        return inv.notes[0];
    }

    public void Select(int i)
    {
        if (i < 0 || i >= slotViews.Count) return;
        if (selected != i) manager.PlayClick();
        selected = i;
        Apply(true);
    }

    public void Hover(int i) { hovered = i; }

    public void SelectNote(int id)
    {
        Inventory inv = Inventory.Instance;
        if (inv == null || !inv.HasNote(id)) return;
        selectedNote = id;
        manager.PlayClick();
        Apply(true);
    }

    public void OnUse()
    {
        Inventory inv = Inventory.Instance;
        if (inv == null) return;
        if (inv.UseSlot(selected)) manager.PlayClick();
        Apply(true);
    }

    public void OnDrop()
    {
        Inventory inv = Inventory.Instance;
        if (inv == null) return;
        if (inv.DropSlot(selected)) manager.PlayClick();
        Apply(true);
    }

    // ------------------------------------------------------------------
    // Refresco
    // ------------------------------------------------------------------
    private void Apply(bool force)
    {
        Inventory inv = Inventory.Instance;
        GameManager gm = GameManager.Instance;
        if (inv == null) return;

        string nm = gm != null && gm.config != null ? gm.config.name : "";
        if (DebugLevelName != null) nm = DebugLevelName;
        levelText.text = gm != null ? "Nivel " + gm.levelIndex + " — " + nm : "";

        // Medidores
        float b = gm != null && gm.flashlight != null ? gm.flashlight.Battery01 : 0f;
        float st = inv.fpc != null ? inv.fpc.Stamina01 : 0f;
        float sa = inv.sanity != null ? inv.sanity.Sanity01 : 0f;
        batFill.anchorMax = new Vector2(Mathf.Clamp01(b), 1f); batText.text = Mathf.RoundToInt(b * 100f) + " %";
        staFill.anchorMax = new Vector2(Mathf.Clamp01(st), 1f); staText.text = Mathf.RoundToInt(st * 100f) + " %";
        sanFill.anchorMax = new Vector2(Mathf.Clamp01(sa), 1f); sanText.text = Mathf.RoundToInt(sa * 100f) + " %";

        // Objetivo
        int foundHere = 0;
        if (gm != null) for (int k = 0; k < LoreNotes.PerLevel; k++) if (inv.HasNote(LoreNotes.IdFor(gm.levelIndex, k))) foundHere++;
        objectiveText.text = "OBJETIVO: " + (DebugObjective ?? "encuentra la salida de este nivel sin que te atrapen.") + "\n"
            + "Notas de este nivel: " + foundHere + "/" + LoreNotes.PerLevel + "   ·   Pilas: " + inv.BatteryCount;

        tabNotesLabel.text = "NOTAS (" + inv.NotesFound + "/" + LoreNotes.Total + ")";

        if (!notesTab) ApplyItems(inv);
        else ApplyNotes(inv);
    }

    private void ApplyItems(Inventory inv)
    {
        usedText.text = "CASILLAS " + inv.UsedSlots + "/" + Inventory.SlotCount + "  ·  CARGA " + FormatKg(TotalWeight(inv));
        for (int i = 0; i < slotViews.Count; i++)
        {
            SlotView sv = slotViews[i];
            Inventory.Slot s = inv.GetSlot(i);
            bool has = !s.Empty;
            sv.icon.enabled = has;
            if (has) sv.icon.sprite = ItemIcons.Get(s.type);
            string ct = has && s.count > 1 ? "x" + s.count : "";
            if (sv.count.text != ct) sv.count.text = ct;
            bool sel = i == selected;
            sv.border.color = sel ? UiStyle.BorderHi : (i == hovered || i == dragTarget ? new Color(0.75f, 0.62f, 0.28f, 1f) : new Color(0f, 0f, 0f, 0.75f));
            sv.bg.color = sel ? new Color(1f, 0.93f, 0.75f, 1f) : new Color(0.82f, 0.82f, 0.82f, 1f);
            sv.icon.color = has && s.type == ItemType.Flashlight && manager != null && GameManager.Instance != null && GameManager.Instance.flashlight != null && !GameManager.Instance.flashlight.IsOn
                ? new Color(0.65f, 0.65f, 0.65f, 1f) : Color.white;
            if (has && inv.IsWrongKey(s.type)) sv.icon.color = new Color(1f, 0.42f, 0.38f, 0.75f);   // v7.0.4: llave que no abre
            if (i == dragFrom) sv.icon.color = new Color(1f, 1f, 1f, 0.25f);
        }

        Inventory.Slot cur = inv.GetSlot(selected);
        if (cur.Empty)
        {
            bigIcon.enabled = false;
            detailName.text = "Casilla vacía";
            detailQty.text = "";
            detailStats.text = "";
            detailKeyHint.text = selected < Inventory.HotbarSize ? "Barra rápida: tecla " + (selected + 1) : "";
            detailDesc.text = "Aquí irá lo que recojas por las Backrooms: pilas, agua de almendras, barras luminosas, cintas de vídeo...";
            useButton.interactable = false; dropButton.interactable = false;
            useLabel.text = "Usar"; dropLabel.text = "Soltar";
            return;
        }
        ItemDefs.Def def = ItemDefs.Get(cur.type);
        bigIcon.enabled = true;
        bigIcon.sprite = ItemIcons.Get(cur.type);
        detailName.text = def.name + (inv.IsWrongKey(cur.type) ? "  (no abre la salida)" : "");
        if (cur.type == ItemType.Flashlight)
        {
            float b = GameManager.Instance != null && GameManager.Instance.flashlight != null ? GameManager.Instance.flashlight.Battery01 : 0f;
            bool on = GameManager.Instance != null && GameManager.Instance.flashlight != null && GameManager.Instance.flashlight.IsOn;
            detailQty.text = "Batería: " + Mathf.RoundToInt(b * 100f) + " %  ·  " + (on ? "encendida" : "apagada");
        }
        else detailQty.text = "Cantidad: " + cur.count + " / " + def.maxStack;
        detailStats.text = "PESO: " + FormatKg(def.weight * cur.count) + "    ESTADO: " + Condition(cur.type, def);
        detailKeyHint.text = selected < Inventory.HotbarSize ? "Barra rápida: tecla " + (selected + 1) : "Arrástralo a las casillas 1-5 para usarlo desde la barra rápida";
        detailDesc.text = def.description;
        useLabel.text = def.usable ? (cur.type == ItemType.Flashlight ? (GameManager.Instance != null && GameManager.Instance.flashlight != null && GameManager.Instance.flashlight.IsOn ? "Apagar" : "Encender") : def.useLabel) : "Usar";
        dropLabel.text = cur.type == ItemType.Glowstick ? "Colocar" : "Soltar";
        bool canUse = def.usable;
        if (cur.type == ItemType.Battery) canUse = inv.flashlight != null && inv.flashlight.Battery01 <= 0.985f;
        useButton.interactable = canUse;
        dropButton.interactable = def.droppable;
    }

    private void ApplyNotes(Inventory inv)
    {
        noteCount.text = "ENCONTRADAS " + inv.NotesFound + "/" + LoreNotes.Total;
        for (int i = 0; i < noteRows.Count; i++)
        {
            NoteRow nr = noteRows[i];
            bool found = inv.HasNote(nr.noteId);
            LoreNotes.Note n = LoreNotes.Get(nr.noteId);
            string t = found ? "N" + n.level + "·" + (nr.noteId % 10 + 1) + "  " + n.title : "N" + n.level + "·" + (nr.noteId % 10 + 1) + "  (sin descubrir)";
            if (nr.label.text != t) nr.label.text = t;
            nr.label.color = found ? Ink : new Color(0.27f, 0.25f, 0.21f, 1f);
            nr.bg.color = nr.noteId == selectedNote ? new Color(0.98f, 0.86f, 0.50f, 1f) : (found ? TabOn : new Color(0.55f, 0.52f, 0.44f, 1f));
        }
        if (selectedNote >= 0 && inv.HasNote(selectedNote))
        {
            LoreNotes.Note n = LoreNotes.Get(selectedNote);
            noteTitle.text = n.title;
            noteBody.text = n.text;
        }
        else
        {
            noteTitle.text = inv.NotesFound == 0 ? "Sin notas todavía" : "Elige una nota";
            noteBody.text = inv.NotesFound == 0
                ? "Busca papeles por los niveles (3 por nivel). Aquí podrás releerlos cuando quieras."
                : "Haz clic en una nota de la lista para leerla.";
        }
    }

    private void UpdateTooltip()
    {
        Inventory inv = Inventory.Instance;
        bool show = !notesTab && hovered >= 0 && inv != null && !inv.GetSlot(hovered).Empty;
        if (!show) { if (tooltip.activeSelf) tooltip.SetActive(false); return; }
        Inventory.Slot s = inv.GetSlot(hovered);
        ItemDefs.Def def = ItemDefs.Get(s.type);
        tipName.text = def.name + (inv.IsWrongKey(s.type) ? " (no abre)" : "") + (s.type != ItemType.Flashlight && s.count > 1 ? "  x" + s.count : "");
        string d = def.description;
        if (d.Length > 120) { int cut = d.LastIndexOf(' ', 116); d = d.Substring(0, cut > 60 ? cut : 116) + "…"; }
        tipBody.text = d;
        if (!tooltip.activeSelf) tooltip.SetActive(true);
        RectTransform rootRt = (RectTransform)transform;
        Vector2 lp;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, Input.mousePosition, null, out lp))
        {
            lp += new Vector2(22f, -18f);
            Rect r = rootRt.rect;
            lp.x = Mathf.Min(lp.x, r.xMax - 380f);
            lp.y = Mathf.Max(lp.y, r.yMin + 160f);
            tipRect.anchoredPosition = lp;
        }
        tooltip.transform.SetAsLastSibling();
    }

    // ------------------------------------------------------------------
    // Arrastrar y soltar
    // ------------------------------------------------------------------
    public void BeginDrag(int index, PointerEventData e)
    {
        Inventory inv = Inventory.Instance;
        if (notesTab || inv == null || index <= 0 || inv.GetSlot(index).Empty) { if (e != null) e.pointerDrag = null; return; }
        dragFrom = index; dragTarget = -1;
        dragGhost.sprite = ItemIcons.Get(inv.GetSlot(index).type);
        dragGhost.gameObject.SetActive(true);
        dragGhost.transform.SetAsLastSibling();
        tooltip.SetActive(false);
        if (e != null) DragMove(e);
        Select(index);
    }

    public void DragMove(PointerEventData e)
    {
        if (dragFrom < 0) return;
        dragTarget = hovered;
        Vector2 lp;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, e.position, e.pressEventCamera, out lp))
            dragGhost.rectTransform.anchoredPosition = lp;
    }

    public void DropOn(int index)
    {
        Inventory inv = Inventory.Instance;
        if (dragFrom < 0 || inv == null) return;
        if (inv.Swap(dragFrom, index)) { selected = index; manager.PlayClick(); }
        EndDrag();
    }

    public void EndDrag()
    {
        dragFrom = -1; dragTarget = -1;
        if (dragGhost != null) dragGhost.gameObject.SetActive(false);
        Apply(true);
    }

    // Para pruebas: arrastra de una casilla a otra
    public bool DebugDrag(int from, int to)
    {
        BeginDrag(from, null);
        if (dragFrom != from) return false;
        int before = selected;
        DropOn(to);
        return selected == to && before == from;
    }

    private static string FormatKg(float kg)
    {
        if (kg <= 0f) return "0 g";
        if (kg < 0.1f) return Mathf.RoundToInt(kg * 1000f) + " g";
        return kg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',') + " kg";
    }

    private static float TotalWeight(Inventory inv)
    {
        float w = 0f;
        for (int i = 0; i < Inventory.SlotCount; i++) { Inventory.Slot s = inv.GetSlot(i); if (!s.Empty) w += ItemDefs.Get(s.type).weight * s.count; }
        return w;
    }

    private static string Condition(ItemType t, ItemDefs.Def def)
    {
        if (t == ItemType.Flashlight && GameManager.Instance != null && GameManager.Instance.flashlight != null)
        {
            float b = GameManager.Instance.flashlight.Battery01;
            return b < 0.2f ? "Casi sin batería" : (b < 0.5f ? "Batería a medias" : def.condition);
        }
        return string.IsNullOrEmpty(def.condition) ? "-" : def.condition;
    }

    // Para pruebas: simula el cursor sobre una casilla
    public void DebugHover(int i) { hovered = i; UpdateTooltip(); }
}
