using System.Globalization;
using UnityEngine;

// v7.1.3: parámetros de aspecto del agua y la playa de la sala de piscina, ajustados contra los renders de Cycles
// (pool-render/pool_1..3.png) con capturas desde las mismas cámaras (Editor/V713Shots.cs).
public static class PoolLook
{
    public static Vector4 WaterTint = new Vector4(1.5f, 1.42f, 1.55f, 1f);
    public static Vector4 WaterAbs = new Vector4(0.10f, 0.03f, 0.025f, 1f);       // w = 1: modo v7.1.3 activo
    public static Vector4 WaterScatter = new Vector4(0.05f, 0.12f, 0.18f, 1f);
    public static Vector4 WaveP = new Vector4(1.0f, 1.0f, 0.06f, 1.0f);          // normales finas, refracción, distorsión y brillo del reflejo
    public static Vector4 CausticK = new Vector4(0.12f, 0.35f, 0f, 1f);           // fondo desde fuera del agua, paredes, (cámara bajo el agua), activo
    public static Vector4 DeckP = new Vector4(1.0f, 0.04f, 0.02f, 6f);            // reflejo planar del suelo: fuerza, F0, distorsión, mip por rugosidad
    public static Color WindowEmission = new Color(3.4f, 3.55f, 3.7f);
    public const float DeckPlanar = 1f, PoolWaterBand = 0.85f;

    public static void Apply()
    {
        Shader.SetGlobalVector("_PoolWaterTint", WaterTint);
        Shader.SetGlobalVector("_PoolWaterAbs", WaterAbs);
        Shader.SetGlobalVector("_PoolWaterScatter", WaterScatter);
        Shader.SetGlobalVector("_PoolWaveP", WaveP);
        Shader.SetGlobalVector("_PoolCausticK", CausticK);
        Shader.SetGlobalVector("_PoolDeckP", DeckP);
    }

    // Fuera de la sala (otros niveles): sin efecto (w = 0 deja el agua y las cáusticas como antes).
    public static void Clear()
    {
        Shader.SetGlobalVector("_PoolWaterAbs", Vector4.zero);
        Shader.SetGlobalVector("_PoolCausticK", Vector4.zero);
    }

    // Para el ajuste: "clave=a,b,c,d;clave2=..." (claves: WaterTint, WaterAbs, WaterScatter, WaveP, CausticK, DeckP, Window)
    public static void Override(string spec)
    {
        if (string.IsNullOrEmpty(spec)) return;
        foreach (string part in spec.Split(';'))
        {
            int e = part.IndexOf('='); if (e <= 0) continue;
            string k = part.Substring(0, e).Trim();
            string[] f = part.Substring(e + 1).Split(',');
            float[] v = new float[4];
            for (int i = 0; i < 4; i++) v[i] = i < f.Length ? float.Parse(f[i], CultureInfo.InvariantCulture) : 1f;
            Vector4 x = new Vector4(v[0], v[1], v[2], v[3]);
            switch (k)
            {
                case "WaterTint": WaterTint = x; break;
                case "WaterAbs": WaterAbs = x; break;
                case "WaterScatter": WaterScatter = x; break;
                case "WaveP": WaveP = x; break;
                case "CausticK": CausticK = x; break;
                case "DeckP": DeckP = x; break;
                case "Window": WindowEmission = new Color(x.x, x.y, x.z); break;
            }
        }
        Apply();
    }
}
