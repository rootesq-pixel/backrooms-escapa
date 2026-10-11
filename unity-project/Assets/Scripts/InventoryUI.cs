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

    // --- Vista de una casilla ---
    private class SlotView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public InventoryUI ui; public int index;
        public Image border, bg, icon; public Text count, key;
        public void OnPointerEnter(PointerEventData e) { ui.Hover(index); }
        public void OnPointerExit(PointerEventData e) { ui.Hover(-1); }
        public void OnPointerClick(PointerEventData e) { ui.Select(index); }
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
    public void Build(UIManager mgr)
    {
        manager = mgr;
        group = gameObject.AddComponent<CanvasGroup>();
        Image dim = GetComponent<Image>();
        if (dim != null) dim.raycastTarget = true;

        // Marco principal
        Image border = UiStyle.Rect(transform, "PanelBorder", 0f, 0f, 1580f, 900f, UiStyle.Border);
        panel = border.rectTransform;
        Image back = UiStyle.Rect(border.transform, "Panel", 0f, 0f, 1574f, 894f, UiStyle.Panel);
        Transform P = back.transform;
        // líneas de escaneo muy tenues
        for (int i = 0; i < 30; i++)
            UiStyle.Rect(P, "Scan", 0f, 430f - i * 30f, 1570f, 2f, new Color(1f, 0.95f, 0.5f, 0.025f));

        titleText = UiStyle.Label(P, "INVENTARIO", 54, TextAnchor.MiddleLeft, -480f, 392f, 700f, 70f, UiStyle.Gold, FontStyle.Bold);
        levelText = UiStyle.Label(P, "", 28, TextAnchor.MiddleRight, 380f, 395f, 800f, 50f, UiStyle.Text);
        UiStyle.Rect(P, "TitleLine", 0f, 345f, 1500f, 2f, UiStyle.Border);

        // Pestañas
        BuildTab(P, "Objetos", -690f, 302f, out tabItemsBg, out tabItemsLabel, () => SetTab(false));
        BuildTab(P, "Notas", -480f, 302f, out tabNotesBg, out tabNotesLabel, () => SetTab(true));

        BuildItemsPage(P);
        BuildNotesPage(P);
        BuildDetail(P);
        BuildVitals(P);

        objectiveText = UiStyle.Label(P, "", 21, TextAnchor.MiddleLeft, -230f, -336f, 1160f, 70f, UiStyle.Text);
        UiStyle.Rect(P, "FootLine", 0f, -375f, 1500f, 2f, UiStyle.Border);
        hintText = UiStyle.Label(P, "1-5: barra rápida  ·  clic / flechas: seleccionar  ·  I / Esc: cerrar", 22, TextAnchor.MiddleLeft, -400f, -410f, 1000f, 36f, UiStyle.Dim);
        MakeButton(P, "Cerrar (I / Esc)", 560f, -410f, 330f, 46f, () => { manager.PlayClick(); manager.SetScreen(UIManager.Screen.None); }, 24, out Image _, out Text _);

        // Tooltip (flotante, sobre todo)
        tooltip = new GameObject("Tooltip", typeof(RectTransform));
        tooltip.transform.SetParent(transform, false);
        Image tb = UiStyle.Rect(tooltip.transform, "Border", 0f, 0f, 330f, 150f, UiStyle.BorderHi);
        tipRect = (RectTransform)tooltip.transform;
        tipRect.anchorMin = tipRect.anchorMax = new Vector2(0.5f, 0.5f);
        tipRect.sizeDelta = new Vector2(330f, 150f);
        tipRect.pivot = new Vector2(0f, 1f);
        tb.rectTransform.anchorMin = tb.rectTransform.anchorMax = new Vector2(0f, 1f);
        tb.rectTransform.pivot = new Vector2(0f, 1f);
        tb.rectTransform.anchoredPosition = Vector2.zero;
        Image ti = UiStyle.Rect(tb.transform, "Bg", 0f, 0f, 326f, 146f, new Color(0.03f, 0.027f, 0.012f, 0.97f));
        tipName = UiStyle.Label(ti.transform, "", 26, TextAnchor.UpperLeft, 0f, 48f, 300f, 34f, UiStyle.Gold, FontStyle.Bold);
        tipBody = UiStyle.Label(ti.transform, "", 19, TextAnchor.UpperLeft, 0f, -18f, 300f, 100f, UiStyle.Text);
        tooltip.SetActive(false);

        // Raycast: el panel recibe eventos
        foreach (Graphic g in GetComponentsInChildren<Graphic>(true))
            if (g.GetComponent<SlotView>() != null || g.GetComponent<NoteRow>() != null || g.GetComponent<Button>() != null) g.raycastTarget = true;

        SetTab(false);
        gameObject.SetActive(false);
    }

    private void BuildTab(Transform P, string label, float x, float y, out Image bg, out Text lbl, UnityEngine.Events.UnityAction act)
    {
        Image b = UiStyle.Rect(P, "Tab_" + label, x, y, 190f, 42f, UiStyle.Border);
        Image inner = UiStyle.Rect(b.transform, "Bg", 0f, 0f, 186f, 38f, UiStyle.Slot);
        Button btn = b.gameObject.AddComponent<Button>();
        b.raycastTarget = true;
        btn.targetGraphic = b;
        btn.transition = Selectable.Transition.None;
        btn.onClick.AddListener(() => { manager.PlayClick(); act(); });
        bg = inner;
        lbl = UiStyle.Label(inner.transform, label.ToUpperInvariant(), 24, TextAnchor.MiddleCenter, 0f, 0f, 186f, 38f, UiStyle.Text, FontStyle.Bold);
    }

    private Button MakeButton(Transform P, string text, float x, float y, float w, float h, UnityEngine.Events.UnityAction act, int size, out Image inner, out Text label)
    {
        Image b = UiStyle.Rect(P, "Btn_" + text, x, y, w, h, UiStyle.Border);
        b.raycastTarget = true;
        inner = UiStyle.Rect(b.transform, "Bg", 0f, 0f, w - 4f, h - 4f, new Color(0.12f, 0.10f, 0.05f, 1f));
        label = UiStyle.Label(inner.transform, text, size, TextAnchor.MiddleCenter, 0f, 0f, w - 8f, h - 4f, UiStyle.Text, FontStyle.Bold);
        Button btn = b.gameObject.AddComponent<Button>();
        btn.targetGraphic = inner;
        inner.raycastTarget = true;
        ColorBlock cb = btn.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = new Color(1.8f, 1.55f, 0.9f, 1f);
        cb.pressedColor = new Color(2.2f, 1.9f, 1.1f, 1f);
        cb.selectedColor = Color.white;
        cb.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.7f);
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
        // marco de la rejilla
        UiStyle.Rect(itemsPage.transform, "GridFrame", left + gridW * 0.5f, top - gridH * 0.5f, gridW + 24f, gridH + 24f, new Color(0.3f, 0.25f, 0.1f, 0.6f));
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
        usedText = UiStyle.Label(itemsPage.transform, "", 20, TextAnchor.MiddleRight, left + gridW - 150f, top + 24f, 300f, 28f, UiStyle.Dim);
    }

    private SlotView BuildSlot(Transform parent, int idx, float x, float y, float size)
    {
        Image b = UiStyle.Rect(parent, "Slot" + idx, x, y, size, size, UiStyle.Border);
        b.raycastTarget = true;
        Image inner = UiStyle.Rect(b.transform, "Bg", 0f, 0f, size - 4f, size - 4f, UiStyle.Slot);
        // degradado sutil: franja superior más clara
        UiStyle.Rect(inner.transform, "Sheen", 0f, (size - 4f) * 0.5f - 10f, size - 4f, 18f, new Color(1f, 0.95f, 0.6f, 0.04f));
        Image ic = UiStyle.Rect(inner.transform, "Icon", 0f, 4f, size - 26f, size - 26f, Color.white);
        ic.preserveAspect = true; ic.enabled = false;
        Text cnt = UiStyle.Label(inner.transform, "", 26, TextAnchor.LowerRight, 0f, -size * 0.5f + 25f, size - 14f, 30f, UiStyle.Text, FontStyle.Bold);
        Text key = UiStyle.Label(inner.transform, idx < Inventory.HotbarSize ? (idx + 1).ToString() : "", 20, TextAnchor.UpperLeft, -size * 0.5f + 18f, size * 0.5f - 18f, 30f, 26f, UiStyle.Gold, FontStyle.Bold);
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

        int total = LoreNotes.PerLevel * 6;
        float rowW = 336f, rowH = 34f, gap = 5f;
        float left = -690f, top = 250f;
        UiStyle.Rect(notesPage.transform, "ListFrame", left + rowW + gap * 0.5f, top - (9 * (rowH + gap)) * 0.5f + gap * 0.5f, rowW * 2f + gap + 24f, 9 * (rowH + gap) + 18f, new Color(0.3f, 0.25f, 0.1f, 0.6f));
        for (int i = 0; i < total; i++)
        {
            int col = i / 9, row = i % 9;
            int level = i / LoreNotes.PerLevel, k = i % LoreNotes.PerLevel;
            // orden: nivel 0..4, 3 notas por nivel -> 2 columnas (9 + 6)
            float x = left + rowW * 0.5f + col * (rowW + gap);
            float y = top - rowH * 0.5f - row * (rowH + gap);
            Image b = UiStyle.Rect(notesPage.transform, "Note" + i, x, y, rowW, rowH, UiStyle.Border);
            b.raycastTarget = true;
            Image inner = UiStyle.Rect(b.transform, "Bg", 0f, 0f, rowW - 4f, rowH - 4f, UiStyle.Slot);
            Text t = UiStyle.Label(inner.transform, "", 19, TextAnchor.MiddleLeft, 8f, 0f, rowW - 24f, rowH - 4f, UiStyle.Text);
            NoteRow nr = b.gameObject.AddComponent<NoteRow>();
            nr.ui = this; nr.noteId = LoreNotes.IdFor(level, k); nr.bg = inner; nr.label = t;
            noteRows.Add(nr);
        }
        noteCount = UiStyle.Label(notesPage.transform, "", 20, TextAnchor.MiddleRight, left + rowW * 2f - 150f, top + 24f, 330f, 28f, UiStyle.Dim);
        notesPage.SetActive(false);
    }

    private void BuildDetail(Transform P)
    {
        float cx = 420f;
        UiStyle.Rect(P, "DetailFrame", cx, 85f, 690f, 500f, new Color(0.3f, 0.25f, 0.1f, 0.6f));
        Image inner = UiStyle.Rect(P, "DetailBg", cx, 85f, 684f, 494f, new Color(0.06f, 0.052f, 0.026f, 0.96f));
        Transform D = inner.transform;

        // --- Detalle de objeto ---
        Image iconFrame = UiStyle.Rect(D, "BigIconBorder", -230f, 150f, 176f, 176f, UiStyle.Border);
        Image iconBg = UiStyle.Rect(iconFrame.transform, "Bg", 0f, 0f, 172f, 172f, UiStyle.Slot);
        bigIcon = UiStyle.Rect(iconBg.transform, "Icon", 0f, 0f, 160f, 160f, Color.white);
        bigIcon.preserveAspect = true;
        detailName = UiStyle.Label(D, "", 38, TextAnchor.MiddleLeft, 70f, 190f, 440f, 56f, UiStyle.Gold, FontStyle.Bold);
        detailQty = UiStyle.Label(D, "", 26, TextAnchor.MiddleLeft, 70f, 135f, 440f, 40f, UiStyle.Text);
        detailKeyHint = UiStyle.Label(D, "", 21, TextAnchor.MiddleLeft, 70f, 92f, 440f, 34f, UiStyle.Dim);
        UiStyle.Rect(D, "Sep", 0f, 40f, 640f, 2f, UiStyle.Border);
        detailDesc = UiStyle.Label(D, "", 25, TextAnchor.UpperLeft, 0f, -55f, 630f, 190f, UiStyle.Text);
        Image ui1, ui2;
        useButton = MakeButton(D, "Usar", -170f, -195f, 300f, 66f, OnUse, 32, out ui1, out useLabel);
        dropButton = MakeButton(D, "Soltar", 170f, -195f, 300f, 66f, OnDrop, 32, out ui2, out dropLabel);

        // --- Lectura de notas ---
        noteTitle = UiStyle.Label(D, "", 34, TextAnchor.MiddleLeft, 0f, 195f, 630f, 56f, UiStyle.Gold, FontStyle.Bold);
        noteBody = UiStyle.Label(D, "", 27, TextAnchor.UpperLeft, 0f, -20f, 620f, 330f, new Color(0.9f, 0.86f, 0.66f));
        noteTitle.gameObject.SetActive(false);
        noteBody.gameObject.SetActive(false);
    }

    private void BuildVitals(Transform P)
    {
        // Medidores bajo la rejilla
        float x0 = -690f + 380f;       // centro de los medidores
        string[] names = { "Linterna", "Resistencia", "Cordura" };
        Color[] cols = { new Color(0.95f, 0.85f, 0.35f), new Color(0.55f, 0.85f, 0.5f), new Color(0.55f, 0.7f, 1f) };
        for (int i = 0; i < 3; i++)
        {
            float y = -170f - i * 44f;
            UiStyle.Label(P, names[i], 22, TextAnchor.MiddleLeft, -690f + 130f, y, 270f, 34f, UiStyle.Dim);
            Image back = UiStyle.Rect(P, "Meter" + i, -690f + 390f, y, 330f, 26f, UiStyle.Border);
            Image bi = UiStyle.Rect(back.transform, "Bg", 0f, 0f, 326f, 22f, new Color(0.02f, 0.02f, 0.01f, 1f));
            Image fill = UiStyle.Rect(bi.transform, "Fill", 0f, 0f, 320f, 16f, cols[i]);
            RectTransform fr = fill.rectTransform;
            fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = new Vector2(3f, 3f); fr.offsetMax = new Vector2(-3f, -3f);
            Text tx = UiStyle.Label(P, "", 22, TextAnchor.MiddleLeft, -690f + 635f, y, 110f, 34f, UiStyle.Text, FontStyle.Bold);
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
            float s = Mathf.Lerp(0.9f, 1f, e);
            panel.localScale = new Vector3(s, s, 1f);
        }
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
        tabItemsBg.color = !notes ? new Color(0.30f, 0.24f, 0.08f, 1f) : UiStyle.Slot;
        tabNotesBg.color = notes ? new Color(0.30f, 0.24f, 0.08f, 1f) : UiStyle.Slot;
        bool items = !notes;
        detailName.gameObject.SetActive(items); detailQty.gameObject.SetActive(items); detailKeyHint.gameObject.SetActive(items);
        detailDesc.gameObject.SetActive(items); useButton.gameObject.SetActive(items); dropButton.gameObject.SetActive(items);
        bigIcon.transform.parent.parent.gameObject.SetActive(items);
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
        objectiveText.text = "OBJETIVO: encuentra la puerta de salida de este nivel sin que te atrape.\n"
            + "Notas de este nivel: " + foundHere + "/" + LoreNotes.PerLevel + "   ·   Pilas: " + inv.BatteryCount;

        tabNotesLabel.text = "NOTAS " + inv.NotesFound + "/" + LoreNotes.Total;

        if (!notesTab) ApplyItems(inv);
        else ApplyNotes(inv);
    }

    private void ApplyItems(Inventory inv)
    {
        usedText.text = "Casillas " + inv.UsedSlots + "/" + Inventory.SlotCount;
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
            sv.border.color = sel ? UiStyle.BorderHi : (i == hovered ? new Color(0.7f, 0.58f, 0.22f) : UiStyle.Border);
            sv.bg.color = sel ? new Color(0.17f, 0.14f, 0.06f, 0.98f) : UiStyle.Slot;
            sv.icon.color = has && s.type == ItemType.Flashlight && manager != null && GameManager.Instance != null && GameManager.Instance.flashlight != null && !GameManager.Instance.flashlight.IsOn
                ? new Color(0.65f, 0.65f, 0.65f, 1f) : Color.white;
        }

        Inventory.Slot cur = inv.GetSlot(selected);
        if (cur.Empty)
        {
            bigIcon.enabled = false;
            detailName.text = "Casilla vacía";
            detailQty.text = "";
            detailKeyHint.text = selected < Inventory.HotbarSize ? "Barra rápida: tecla " + (selected + 1) : "";
            detailDesc.text = "Aquí irá lo que recojas por las Backrooms: pilas, agua de almendras, barras luminosas, cintas de vídeo...";
            useButton.interactable = false; dropButton.interactable = false;
            useLabel.text = "Usar"; dropLabel.text = "Soltar";
            return;
        }
        ItemDefs.Def def = ItemDefs.Get(cur.type);
        bigIcon.enabled = true;
        bigIcon.sprite = ItemIcons.Get(cur.type);
        detailName.text = def.name;
        if (cur.type == ItemType.Flashlight)
        {
            float b = GameManager.Instance != null && GameManager.Instance.flashlight != null ? GameManager.Instance.flashlight.Battery01 : 0f;
            bool on = GameManager.Instance != null && GameManager.Instance.flashlight != null && GameManager.Instance.flashlight.IsOn;
            detailQty.text = "Batería: " + Mathf.RoundToInt(b * 100f) + " %   " + (on ? "[ENCENDIDA]" : "[APAGADA]");
        }
        else detailQty.text = "Cantidad: " + cur.count + " / " + def.maxStack;
        detailKeyHint.text = selected < Inventory.HotbarSize ? "Barra rápida: tecla " + (selected + 1) : "Mueve el objeto a la barra rápida recogiéndolo en las casillas 1-5";
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
        noteCount.text = "Halladas " + inv.NotesFound + "/" + LoreNotes.Total;
        for (int i = 0; i < noteRows.Count; i++)
        {
            NoteRow nr = noteRows[i];
            bool found = inv.HasNote(nr.noteId);
            LoreNotes.Note n = LoreNotes.Get(nr.noteId);
            string t = found ? "N" + n.level + "·" + (nr.noteId % 10 + 1) + "  " + n.title : "N" + n.level + "·" + (nr.noteId % 10 + 1) + "  — sin descubrir —";
            if (nr.label.text != t) nr.label.text = t;
            nr.label.color = found ? UiStyle.Text : new Color(0.45f, 0.42f, 0.32f);
            nr.bg.color = nr.noteId == selectedNote ? new Color(0.22f, 0.18f, 0.07f, 1f) : UiStyle.Slot;
        }
        if (selectedNote >= 0 && inv.HasNote(selectedNote))
        {
            LoreNotes.Note n = LoreNotes.Get(selectedNote);
            noteTitle.text = n.title;
            noteBody.text = "“" + n.text + "”";
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
        tipName.text = def.name + (s.type != ItemType.Flashlight && s.count > 1 ? "  x" + s.count : "");
        string d = def.description;
        if (d.Length > 110) d = d.Substring(0, 107) + "...";
        tipBody.text = d;
        if (!tooltip.activeSelf) tooltip.SetActive(true);
        RectTransform rootRt = (RectTransform)transform;
        Vector2 lp;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rootRt, Input.mousePosition, null, out lp))
        {
            lp += new Vector2(22f, -18f);
            Rect r = rootRt.rect;
            lp.x = Mathf.Min(lp.x, r.xMax - 340f);
            lp.y = Mathf.Max(lp.y, r.yMin + 160f);
            tipRect.anchoredPosition = lp;
        }
        tooltip.transform.SetAsLastSibling();
    }

    // Para pruebas: simula el cursor sobre una casilla
    public void DebugHover(int i) { hovered = i; UpdateTooltip(); }
}
