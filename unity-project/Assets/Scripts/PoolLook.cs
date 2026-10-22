using System.Globalization;
using UnityEngine;

// v7.1.3: parámetros de aspecto del agua y la playa de la sala de piscina, ajustados contra los renders de Cycles
// (pool-render/pool_1..3.png) con capturas desde las mismas cámaras (Editor/V713Shots.cs).
public static class PoolLook
{
    public static Vector4 WaterTint = new Vector4(0.82f, 0.92f, 1.08f, 1f);
    public static Vector4 WaterAbs = new Vector4(0.10f, 0.03f, 0.025f, 1f);       // w = 1: modo v7.1.3 activo
    public static Vector4 WaterScatter = new Vector4(0.05f, 0.12f, 0.18f, 1f);
    public static Vector4 WaveP = new Vector4(1.8f, 1.4f, 0.25f, 1.1f);          // normales finas, refracción, distorsión y brillo del reflejo
    public static Vector4 CausticK = new Vector4(0.12f, 0.35f, 0f, 1f);           // fondo desde fuera del agua, paredes, (cámara bajo el agua), activo
    public static Vector4 DeckP = new Vector4(2.5f, 0.04f, 0.02f, 5f);            // reflejo planar del suelo: fuerza, F0, distorsión, mip por rugosidad
    public static Vector4 WaveK = new Vector4(2.6f, 1.3f, 0.1f, 0f);          // oleaje: k inicial, razón (8 ondas: 2,6-16 rad/m), peso de los normal maps
    public static Color LMTint = new Color(0.94f, 1.03f, 1.4f);   // luz horneada algo más fría (Cycles: AgX, cielo Nishita)
    public static Vector4 EdgeP = new Vector4(0.08f, 0.92f, 0f, 0f);           // línea oscura del agua junto al vaso: ancho, oscuridad
    public static Color WallTint = new Color(0.86f, 0.89f, 0.93f);            // paredes algo más grises (Cycles: grime + AgX)
    public static float PoolTileScale = 0.45f;                                 // baldosa del vaso de ~11 cm como en los renders
    public static Color WindowEmission = new Color(2.2f, 2.45f, 2.95f);   // v7.2.0: blanco plano sin halo (el VHS calienta los blancos: algo de azul)
    // v7.2.0: paredes como en Blender (gris claro con manchas, no azulejo azulado) y ventanas sin halo
    public static Color WallLMTint = new Color(0.96f, 1f, 1.08f);                    // luz horneada de paredes y techo sin el tinte frío del agua
    public static float WallGrime = 1.15f, DeckGrime = 0.55f, CeilGrime = 1.1f;      // cantidad de suciedad (Grime_g, periodo GrimePeriod m)
    public static Color GrimeColor = new Color(0.68f, 0.66f, 0.6f);            // (0,55, 0,5, 0,4) lineal de Blender en espacio gamma
    public const float GrimePeriod = 16f;
    public const float DeckPlanar = 1f, PoolWaterBand = 0.85f;

    public static void Apply()
    {
        Shader.SetGlobalVector("_PoolWaterTint", WaterTint);
        Shader.SetGlobalVector("_PoolWaterAbs", WaterAbs);
        Shader.SetGlobalVector("_PoolWaterScatter", WaterScatter);
        Shader.SetGlobalVector("_PoolWaveP", WaveP);
        Shader.SetGlobalVector("_PoolWaveK", WaveK);
        Shader.SetGlobalVector("_PoolEdgeP", EdgeP);
        Shader.SetGlobalVector("_PoolCausticK", CausticK);
        Shader.SetGlobalVector("_PoolDeckP", DeckP);
    }

    public static void ApplyMaterials(Material[] mats)
    {
        if (mats == null) return;
        for (int i = 0; i < mats.Length; i++)
        {
            Material m = mats[i]; if (m == null) continue;
            if (m.HasProperty("_LMTint")) m.SetColor("_LMTint", i == 0 || i == 3 ? WallLMTint : LMTint);
            if (m.HasProperty("_GrimeTex"))
            {
                if (grime == null) grime = Resources.Load<Texture2D>("Textures/PoolRoom/Grime_g");
                m.SetTexture("_GrimeTex", grime); m.SetTextureScale("_GrimeTex", new Vector2(1f / GrimePeriod, 1f / GrimePeriod));
                m.SetFloat("_GrimeK", grime == null ? 0f : i == 0 ? WallGrime : i == 1 ? DeckGrime : i == 3 ? CeilGrime : 0f);
                m.SetColor("_GrimeColor", GrimeColor);
            }
            if (i == 0) m.SetColor("_Color", WallTint);
            if (i == 2) { m.SetTextureScale("_MainTex", new Vector2(PoolTileScale, PoolTileScale)); }
            else if (i == 4) ProceduralAssets.SetEmission(m, WindowEmission);
        }
    }

    static Texture2D grime;
    public static bool LevelWide { get; private set; }
    public static void ApplyLevelWide() { Apply(); Shader.SetGlobalFloat("_PoolLookAll", 1f); LevelWide = true; }

    // Fuera de la sala (otros niveles): sin efecto (w = 0 deja el agua y las cáusticas como antes).
    public static void Clear()
    {
        Shader.SetGlobalVector("_PoolWaterAbs", Vector4.zero);
        Shader.SetGlobalVector("_PoolCausticK", Vector4.zero);
        Shader.SetGlobalVector("_PoolEdgeP", Vector4.zero);
        Shader.SetGlobalFloat("_PoolLookAll", 0f); LevelWide = false;
    }

    // Para el ajuste: "clave=a,b,c,d;clave2=..." (claves: WaterTint, WaterAbs, WaterScatter, WaveP, CausticK, DeckP, Window; v7.2.0: WallLM, Grime, GrimeColor, Exposure, Bloom, Ambient, Fog)
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
                case "WaveK": WaveK = x; break;
                case "EdgeP": EdgeP = x; break;
                case "WallTint": WallTint = new Color(x.x, x.y, x.z); break;
                case "TileScale": PoolTileScale = x.x; break;
                case "CausticK": CausticK = x; break;
                case "DeckP": DeckP = x; break;
                case "LMTint": LMTint = new Color(x.x, x.y, x.z); break;
                case "LookAll": Shader.SetGlobalFloat("_PoolLookAll", x.x); break;
                case "PlanarOff": PoolPlanarReflection.ForceOff = x.x > 0.5f; break;
                case "Window": WindowEmission = new Color(x.x, x.y, x.z); break;
                case "WallLM": WallLMTint = new Color(x.x, x.y, x.z); break;
                case "Grime": WallGrime = x.x; DeckGrime = x.y; CeilGrime = x.z; break;
                case "GrimeColor": GrimeColor = new Color(x.x, x.y, x.z); break;
                case "Exposure": PostStack.Exposure = x.x; break;
                case "Bloom": PostStack.BloomInt = x.x; PostStack.Threshold = x.y; break;
                case "Ambient": RenderSettings.ambientLight = new Color(x.x, x.y, x.z); break;
                case "Fog": RenderSettings.fogDensity = x.x; break;
                case "Lights": foreach (FlickeringLight fl in Object.FindObjectsOfType<FlickeringLight>(true)) fl.ScaleBase(x.x); break;
            }
        }
        Apply();
    }
}
