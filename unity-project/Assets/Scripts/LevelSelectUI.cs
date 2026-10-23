using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// v7.3.0: miniaturas de los niveles para el menú «Seleccionar nivel» y la sala multijugador. Son capturas reales del juego (sin VHS) hechas
// con «-thumbs <carpeta>» (ThumbBuildCheck) y guardadas en Resources/UI/LevelThumbs/level_N.png. Override: las de esa misma ejecución.
public static class LevelThumbs
{
    public static readonly Texture2D[] Override = new Texture2D[16];
    private static readonly Texture2D[] cache = new Texture2D[16];
    public static Texture2D Get(int lv)
    {
        if (lv < 0 || lv >= cache.Length) return null;
        if (Override[lv] != null) return Override[lv];
        if (cache[lv] == null) cache[lv] = Resources.Load<Texture2D>("UI/LevelThumbs/level_" + lv);
        return cache[lv];
    }
}

// Tarjeta de un nivel: el ratón por encima o la selección con teclado/mando la resaltan (borde dorado, un poco más grande) y
// actualizan la vista previa grande. Clic / Intro / botón A: empieza ese nivel.
public class LevelCard : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler
{
    public int Level;
    public Image Border;
    public Text Title;
    public System.Action<int> OnFocus;
    bool hover, selected;
    float k = 0f;

    public void OnSelect(BaseEventData e) { selected = true; if (OnFocus != null) OnFocus(Level); }
    public void OnDeselect(BaseEventData e) { selected = false; }
    public void OnPointerEnter(PointerEventData e) { hover = true; if (OnFocus != null) OnFocus(Level); }
    public void OnPointerExit(PointerEventData e) { hover = false; }
    public bool Highlighted { get { return hover || selected; } }

    void OnDisable() { hover = false; selected = false; k = 0f; Apply(); }
    void Update()
    {
        float target = Highlighted ? 1f : 0f;
        k = Mathf.MoveTowards(k, target, Time.unscaledDeltaTime * 8f);
        Apply();
    }
    void Apply()
    {
        transform.localScale = Vector3.one * (1f + 0.045f * k);
        if (Border != null) Border.color = Color.Lerp(UiStyle.Border, UiStyle.BorderHi, k);
        if (Title != null) Title.color = Color.Lerp(UiStyle.Text, UiStyle.Gold, k);
    }
}
