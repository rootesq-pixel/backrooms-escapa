using UnityEngine;

// v7.9.2 (usuario: «el texto de los controles del coche pisa las barras de la barra rápida»): bandas del borde inferior del HUD,
// en unidades del lienzo de referencia (1920x1080, match 0,5), medidas desde abajo y ancladas al centro inferior:
//   barra rápida + medidores (hasta HotbarHud.TopEdge) → aviso breve (toast) → avisos de mandos (coche, barca) y aire (buceo).
// Todo lo que va encima de la barra se coloca aquí, así nada se solapa en ninguna resolución (prueba en V704Checks).
public static class HudBottom
{
    public const float Margin = 10f;
    // banda libre por encima del aviso breve
    public static float AboveToast { get { return HotbarHud.TopEdge + GameManager.ToastGap + GameManager.ToastHeight + Margin; } }
    public const float PromptHeight = 76f, PromptMaxWidth = 1500f;   // dos líneas de 30 como mucho
    public const float AirBarHeight = 15f, AirBarWidth = 384f, AirTextHeight = 32f;
    public static float CarPromptY { get { return AboveToast; } }
    public static float OceanAirY { get { return AboveToast; } }
    public static float OceanAirTextY { get { return AboveToast + AirBarHeight + 3f; } }
    public static float OceanPromptY { get { return OceanAirTextY + AirTextHeight + 4f; } }

    // rect centrado abajo: x centro = 0, y = borde inferior, ancho w, alto h
    public static void Place(RectTransform rt, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(w, h); rt.anchoredPosition = new Vector2(0f, y);
    }
    // ancho de un aviso según el ancho del lienzo (con margen a los lados)
    public static float PromptWidth(float canvasW) { return Mathf.Min(PromptMaxWidth, canvasW - 48f); }

    // velocímetro del coche: abajo a la derecha, por encima del contador y la fecha de la cámara VHS y a la derecha de los medidores
    public const float SpeedW = 300f, SpeedH = 64f, SpeedRight = 44f, SpeedBottom = 146f;
    public static void PlaceSpeed(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(SpeedW, SpeedH); rt.anchoredPosition = new Vector2(-SpeedRight, SpeedBottom);
    }
}
