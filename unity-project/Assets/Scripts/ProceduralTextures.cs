using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// Estilos de superficie generados por código (sin assets externos).
public enum SurfaceStyle
{
    WallpaperYellow,
    WallpaperHotel,
    CarpetBrown,
    CarpetRed,
    CeilingBeige,
    CeilingPool,
    CeilingDark,
    ConcreteWall,
    ConcreteFloor,
    ConcreteCeiling,
    PoolWall,
    PoolFloor,
    MetalWall,
    MetalFloor,
    WaterNormal,
    PlayWall,       // v7.1.0 Parque de bolas (texturas horneadas en Tools/ballpit/textures.py)
    PlayFloor,
    WallpaperL0,    // v7.3.0 nivel 0 «fotorrealista» (Tools/blender/styles.py, horneadas en GPU); reserva: las clásicas
    CarpetL0,
    CeilingL0,
    ParkingFloor,   // v7.4.0 nivel 1: hormigón PBR CC0 (ambientCG) + líneas/óxido/humedad propios (Tools/v740/compose_l1.py)
    ParkingWall,
    ParkingCeiling,
    HotelWall,      // v7.5.0 nivel 3: damasco, zócalo de madera, dado y cornisa (CC0 ambientCG + dibujo propio, Tools/v750/compose_l3.py)
    HotelCarpet,
    HotelCeiling,
    IndustrialWall,     // v7.6.0 nivel 4: chapas remachadas, chapa lagrimada, forjado grecado y tuberías (CC0 ambientCG + propio, Tools/v760/compose_l4.py)
    IndustrialFloor,
    IndustrialCeiling,
    IndustrialPipe
}

// Conjunto de texturas PBR para el shader Standard:
//   albedo (sRGB), normal (_BumpMap, lineal) y máscara (R = metálico, G = oclusión, A = suavidad; lineal)
public class SurfaceSet
{
    public SurfaceStyle style;
    public Texture2D albedo;
    public Texture2D normal;
    public Texture2D mask;
    public bool baked;     // texturas horneadas en Blender (assets de Resources): se descargan, no se destruyen

    public void DestroyTextures()
    {
        if (baked)
        {
            if (albedo != null) Resources.UnloadAsset(albedo);
            if (normal != null) Resources.UnloadAsset(normal);
            if (mask != null) Resources.UnloadAsset(mask);
            albedo = normal = mask = null;
            return;
        }
        Kill(albedo);
        Kill(normal);
        Kill(mask);
        albedo = normal = mask = null;
    }

    private static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}

// Caché de conjuntos de texturas: reiniciar un nivel no regenera nada;
// al cambiar de nivel se liberan las que ya no se usan.
public static class TextureLibrary
{
    // Si es true las texturas siguen siendo legibles (lo usa el test del editor para volcar PNG)
    public static bool KeepReadable = false;
    // Multiplicador de resolución (1 = 1024/512; 0.5 = mitad). Útil para tests rápidos.
    public static float ResolutionScale = 1f;

    private static readonly Dictionary<string, SurfaceSet> cache = new Dictionary<string, SurfaceSet>();

    public static SurfaceSet Get(SurfaceStyle style, float wallHeight)
    {
        string key = Key(style, wallHeight);
        SurfaceSet set;
        if (cache.TryGetValue(key, out set) && set.albedo != null)
        {
            return set;
        }
        set = ProceduralTextures.Generate(style, wallHeight);
        cache[key] = set;
        return set;
    }

    public static string Key(SurfaceStyle style, float wallHeight)
    {
        return style + "_" + wallHeight.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static void ReleaseAllExcept(HashSet<string> keep)
    {
        List<string> remove = new List<string>();
        foreach (KeyValuePair<string, SurfaceSet> kv in cache)
        {
            if (keep == null || !keep.Contains(kv.Key)) remove.Add(kv.Key);
        }
        for (int i = 0; i < remove.Count; i++)
        {
            cache[remove[i]].DestroyTextures();
            cache.Remove(remove[i]);
        }
    }

    public static void ReleaseAll()
    {
        ReleaseAllExcept(null);
    }
}

// Generadores de texturas con ruido (Perlin/fBm periódico + ruido de valor por píxel).
// Todas las texturas son tileables (repetición sin costuras visibles).
public static class ProceduralTextures
{
    private class Canvas
    {
        public int n;
        public float[] r, g, b, smooth, ao, metal, h;

        public Canvas(int n)
        {
            this.n = n;
            int c = n * n;
            r = new float[c]; g = new float[c]; b = new float[c];
            smooth = new float[c]; ao = new float[c]; metal = new float[c]; h = new float[c];
        }

        public void Set(int i, float cr, float cg, float cb, float sm, float occ, float met, float height)
        {
            r[i] = cr; g[i] = cg; b[i] = cb;
            smooth[i] = sm; ao[i] = occ; metal[i] = met; h[i] = height;
        }
    }

    private static float Cl(float x) { return x < 0f ? 0f : (x > 1f ? 1f : x); }
    private static float Fr(float x) { return x - Mathf.Floor(x); }
    private static float Lp(float a, float b, float t) { return a + (b - a) * t; }
    private static float Sm(float a, float b, float x) { return Noise.Smooth(a, b, x); }

    private static int Res(int baseRes)
    {
        int r = Mathf.RoundToInt(baseRes * TextureLibrary.ResolutionScale);
        // potencia de dos, mínimo 64
        int p = 64;
        while (p < r) p <<= 1;
        return p;
    }

    // v6.6: texturas horneadas con Blender (materiales procedurales por nodos) en Assets/Resources/Textures:
    //   <Estilo>_a.jpg albedo, <Estilo>_n.png normal, <Estilo>_m.png máscara Standard (R metálico, G oclusión, A suavidad).
    // Si faltan (o para otra altura de pared) se usa la generación por CPU de abajo. Son solo cosméticas: no afectan al mundo ni a la red.
    public static bool UseBaked = true;
    private static readonly Dictionary<SurfaceStyle, float> BakedWallHeight = new Dictionary<SurfaceStyle, float>
    {
        { SurfaceStyle.WallpaperYellow, 3.0f }, { SurfaceStyle.WallpaperL0, 3.0f }, { SurfaceStyle.WallpaperHotel, 2.8f }, { SurfaceStyle.ConcreteWall, 3.6f }, { SurfaceStyle.ParkingWall, 3.6f }, { SurfaceStyle.HotelWall, 2.8f }, { SurfaceStyle.IndustrialWall, 3.4f },
    };

    // ¿Hay textura horneada para este estilo a esta altura de pared? (las paredes horneadas llevan el zócalo a escala)
    public static bool BakedHeightMatches(SurfaceStyle style, float wallHeight)
    {
        float bh;
        return !BakedWallHeight.TryGetValue(style, out bh) || Mathf.Abs(bh - wallHeight) <= 0.02f;
    }

    public static SurfaceSet TryLoadBaked(SurfaceStyle style, float wallHeight)
    {
        if (!UseBaked || style == SurfaceStyle.WaterNormal) return null;
        float bh;
        if (BakedWallHeight.TryGetValue(style, out bh) && Mathf.Abs(bh - wallHeight) > 0.02f) return null;   // las paredes llevan zócalo a escala
        string n = "Textures/" + style;
        Texture2D a = null, nm = null, m = null;
        try { a = Resources.Load<Texture2D>(n + "_a"); nm = Resources.Load<Texture2D>(n + "_n"); m = Resources.Load<Texture2D>(n + "_m"); }
        catch (System.Exception e) { Debug.LogWarning("[Texturas] " + style + ": " + e.Message); }
        if (a == null || nm == null || m == null) return null;
        SurfaceSet set = new SurfaceSet();
        set.style = style; set.albedo = a; set.normal = nm; set.mask = m; set.baked = true;
        return set;
    }

    public static SurfaceSet Generate(SurfaceStyle style, float wallHeight)
    {
        SurfaceSet bakedSet = TryLoadBaked(style, wallHeight);
        if (bakedSet != null) return bakedSet;
        Canvas c;
        float normalStrength = 2f;
        int aniso = 8;
        switch (style)
        {
            case SurfaceStyle.WallpaperYellow: c = WallpaperYellow(Res(1024), wallHeight); normalStrength = 2.2f; break;
            case SurfaceStyle.WallpaperHotel: c = WallpaperHotel(Res(1024), wallHeight); normalStrength = 2.6f; break;
            case SurfaceStyle.HotelWall: c = WallpaperHotel(Res(1024), wallHeight); normalStrength = 2.6f; break;   // v7.5.0: reserva si faltan las horneadas
            case SurfaceStyle.HotelCarpet: c = Carpet(Res(512), true); normalStrength = 3f; break;
            case SurfaceStyle.HotelCeiling: c = CeilingTile(Res(512), new Color(0.25f, 0.23f, 0.21f), false); normalStrength = 2.5f; break;
            case SurfaceStyle.CarpetBrown: c = Carpet(Res(512), false); normalStrength = 3f; break;
            case SurfaceStyle.CarpetRed: c = Carpet(Res(512), true); normalStrength = 3f; break;
            case SurfaceStyle.CeilingBeige: c = CeilingTile(Res(512), new Color(0.84f, 0.81f, 0.68f), true); normalStrength = 2.5f; break;
            case SurfaceStyle.CeilingPool: c = CeilingTile(Res(512), new Color(0.72f, 0.80f, 0.80f), true); normalStrength = 2.5f; break;
            case SurfaceStyle.CeilingDark: c = CeilingTile(Res(512), new Color(0.25f, 0.23f, 0.21f), false); normalStrength = 2.5f; break;
            case SurfaceStyle.ConcreteWall: c = Concrete(Res(1024), 0, wallHeight); normalStrength = 2.4f; break;
            case SurfaceStyle.ConcreteFloor: c = Concrete(Res(1024), 1, wallHeight); normalStrength = 2f; break;
            case SurfaceStyle.ConcreteCeiling: c = Concrete(Res(512), 2, wallHeight); normalStrength = 2f; break;
            case SurfaceStyle.ParkingWall: c = Concrete(Res(1024), 0, wallHeight); normalStrength = 2.4f; break;      // reserva si faltan las horneadas
            case SurfaceStyle.ParkingFloor: c = Concrete(Res(1024), 1, wallHeight); normalStrength = 2f; break;
            case SurfaceStyle.ParkingCeiling: c = Concrete(Res(512), 2, wallHeight); normalStrength = 2f; break;
            case SurfaceStyle.PoolWall: c = PoolTile(Res(1024), true); normalStrength = 2.8f; break;
            case SurfaceStyle.PoolFloor: c = PoolTile(Res(1024), false); normalStrength = 2.8f; break;
            case SurfaceStyle.MetalWall: c = MetalPlate(Res(1024)); normalStrength = 2.6f; break;
            case SurfaceStyle.MetalFloor: c = DiamondPlate(Res(512)); normalStrength = 3.2f; break;
            case SurfaceStyle.IndustrialWall: c = MetalPlate(Res(1024)); normalStrength = 2.6f; break;       // v7.6.0: reserva si faltan las horneadas
            case SurfaceStyle.IndustrialFloor: c = DiamondPlate(Res(512)); normalStrength = 3.2f; break;
            case SurfaceStyle.IndustrialCeiling: c = CeilingTile(Res(512), new Color(0.25f, 0.23f, 0.21f), false); normalStrength = 2.5f; break;
            case SurfaceStyle.IndustrialPipe: c = MetalPlate(Res(512)); normalStrength = 2f; break;
            case SurfaceStyle.PlayWall: c = WallpaperYellow(Res(1024), wallHeight); normalStrength = 2.2f; break;   // reserva si faltan las horneadas
            case SurfaceStyle.PlayFloor: c = Carpet(Res(512), false); normalStrength = 3f; break;
            case SurfaceStyle.WallpaperL0: c = WallpaperYellow(Res(1024), wallHeight); normalStrength = 2.2f; break;
            case SurfaceStyle.CarpetL0: c = Carpet(Res(512), false); normalStrength = 3f; break;
            case SurfaceStyle.CeilingL0: c = CeilingTile(Res(512), new Color(0.86f, 0.86f, 0.80f), true); normalStrength = 2.5f; break;
            default: c = WaterRipples(Res(512)); normalStrength = 5f; break;
        }
        return Finish(c, style, normalStrength, aniso);
    }

    private static SurfaceSet Finish(Canvas c, SurfaceStyle style, float normalStrength, int aniso)
    {
        int n = c.n;
        Color32[] alb = new Color32[n * n];
        Color32[] msk = new Color32[n * n];
        Parallel.For(0, n, y =>
        {
            for (int x = 0; x < n; x++)
            {
                int i = y * n + x;
                alb[i] = new Color32(ToByte(c.r[i]), ToByte(c.g[i]), ToByte(c.b[i]), 255);
                msk[i] = new Color32(ToByte(c.metal[i]), ToByte(c.ao[i]), 0, ToByte(c.smooth[i]));
            }
        });
        Color32[] nrm = Noise.NormalFromHeight(c.h, n, normalStrength);

        SurfaceSet set = new SurfaceSet();
        set.style = style;
        bool readable = TextureLibrary.KeepReadable;
        set.albedo = MakeTexture(alb, n, false, aniso, readable, style + "_Albedo");
        set.normal = MakeTexture(nrm, n, true, aniso, readable, style + "_Normal");
        set.mask = MakeTexture(msk, n, true, aniso, readable, style + "_Mask");
        return set;
    }

    private static byte ToByte(float v)
    {
        return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
    }

    private static Texture2D MakeTexture(Color32[] px, int n, bool linear, int aniso, bool readable, string name)
    {
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true, linear);
        t.name = name;
        t.SetPixels32(px);
        t.wrapMode = TextureWrapMode.Repeat;
        t.filterMode = FilterMode.Trilinear;
        t.anisoLevel = aniso;
        t.Apply(true, !readable);
        return t;
    }

    // ------------------------------------------------------------------
    // Papel pintado amarillo húmedo (nivel 0). v = 0 suelo, v = 1 techo (altura H).
    // ------------------------------------------------------------------
    private static Canvas WallpaperYellow(int n, float H)
    {
        Canvas c = new Canvas(n);
        float[] stain = Noise.Fbm(256, 4, 4, 5, 0.55f, 11);
        float[] streak = Noise.Fbm(256, 24, 3, 4, 0.5f, 12);
        float[] blot = Noise.Fbm(256, 3, 3, 4, 0.5f, 13);
        float[] mould = Noise.Fbm(512, 16, 16, 3, 0.6f, 14);
        float[] peel = Noise.Fbm(256, 3, 3, 5, 0.55f, 15);

        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            float my = v * H;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;

                // Rayas verticales (16 por baldosa) con línea fina oscura y borde claro
                float f = Fr(u * 16f);
                float band = f < 0.5f ? 1f : 0f;
                float line = 1f - Sm(0f, 0.035f, Mathf.Abs(f - 0.5f));
                float edge = 1f - Sm(0f, 0.02f, Mathf.Min(f, 1f - f));
                float cr = Lp(0.64f, 0.71f, band);
                float cg = Lp(0.56f, 0.62f, band);
                float cb = Lp(0.29f, 0.34f, band);
                float k = 1f - 0.14f * line + 0.05f * edge;
                cr *= k; cg *= k; cb *= k;

                // Grano del papel
                float grain = Noise.Hash01(x, y, 1) - 0.5f;
                float grain2 = Noise.Hash01(x / 3, y / 3, 2) - 0.5f;
                float gk = 1f + 0.07f * grain + 0.05f * grain2;
                cr *= gk; cg *= gk; cb *= gk;

                float s = Noise.Sample(stain, n, u, v);
                float mott = 0.86f + 0.28f * Noise.Sample(blot, n, u * 2f, v * 2f);
                cr *= mott; cg *= mott; cb *= mott;
                float st = Noise.Sample(streak, n, u, v);
                float bl = Noise.Sample(blot, n, u, v);
                float mo = Noise.Sample(mould, n, u, v);
                float p = Noise.Sample(peel, n, u, v);

                // Manchas de humedad (más fuertes arriba, chorreones verticales)
                float leak = Sm(0.55f, 0.78f, s * 0.55f + st * 0.45f) * (0.3f + 0.7f * Sm(0.35f, 1f, v));
                cr = Lp(cr, cr * 0.80f, leak * 0.85f);
                cg = Lp(cg, cg * 0.66f, leak * 0.85f);
                cb = Lp(cb, cb * 0.38f, leak * 0.85f);

                // Suciedad y humedad ascendente abajo
                float damp = (1f - Sm(0f, 0.38f, v)) * (0.45f + 0.55f * bl);
                cr = Lp(cr, cr * 0.62f, damp * 0.75f);
                cg = Lp(cg, cg * 0.55f, damp * 0.75f);
                cb = Lp(cb, cb * 0.45f, damp * 0.75f);

                // Moho
                float mold = Sm(0.67f, 0.74f, mo) * (1f - Sm(0f, 0.5f, v));
                cr = Lp(cr, 0.10f, mold * 0.8f);
                cg = Lp(cg, 0.12f, mold * 0.8f);
                cb = Lp(cb, 0.07f, mold * 0.8f);

                // Papel despegado: se ve el yeso debajo
                float peelMask = Sm(0.80f, 0.825f, p);
                float peelEdge = Cl(Sm(0.775f, 0.80f, p) - Sm(0.80f, 0.83f, p));
                float pg = 0.92f + 0.16f * (grain + 0.5f);
                float pl = 0.8f + 0.4f * s;
                cr = Lp(cr, 0.58f * pg * pl, peelMask);
                cg = Lp(cg, 0.53f * pg * pl, peelMask);
                cb = Lp(cb, 0.41f * pg * pl, peelMask);
                cr += peelEdge * 0.13f; cg += peelEdge * 0.12f; cb += peelEdge * 0.08f;

                // Rodapié (14 cm) con ranura de sombra
                float boardMask = 1f - Sm(0.135f, 0.142f, my);
                float groove = Sm(0.14f, 0.146f, my) * (1f - Sm(0.152f, 0.162f, my));
                float scuff = 0.8f + 0.35f * Noise.Hash01(x / 2, y / 6, 3);
                float dirtBoard = 0.7f + 0.3f * (1f - damp);
                cr = Lp(cr, 0.56f * scuff * dirtBoard, boardMask);
                cg = Lp(cg, 0.50f * scuff * dirtBoard, boardMask);
                cb = Lp(cb, 0.38f * scuff * dirtBoard, boardMask);
                float gk2 = 1f - 0.5f * groove;
                cr *= gk2; cg *= gk2; cb *= gk2;

                // Oclusión ambiental horneada: esquinas con suelo y techo, cornisa
                float ao = Lp(0.70f, 1f, Sm(0f, 0.3f, v)) * Lp(0.80f, 1f, 1f - Sm(0.82f, 1f, v));
                float bake = 0.82f + 0.18f * ao;
                cr *= bake; cg *= bake; cb *= bake;

                float height = 0.5f + 0.10f * band - 0.06f * line + 0.04f * grain + 0.02f * grain2
                    - 0.45f * peelMask + 0.10f * peelEdge + 0.38f * boardMask - 0.3f * groove;
                float smooth = 0.10f + 0.22f * leak + 0.18f * damp + 0.3f * boardMask - 0.06f * peelMask;
                c.Set(i, cr, cg, cb, Cl(smooth), ao, 0f, height);
            }
        });
        return c;
    }

    // ------------------------------------------------------------------
    // Papel de hotel oscuro con zócalo de madera (nivel 3)
    // ------------------------------------------------------------------
    private static Canvas WallpaperHotel(int n, float H)
    {
        Canvas c = new Canvas(n);
        float[] grainF = Noise.Fbm(256, 48, 2, 3, 0.5f, 31);
        float[] stain = Noise.Fbm(256, 4, 4, 5, 0.55f, 32);
        float[] streak = Noise.Fbm(256, 20, 3, 4, 0.5f, 33);

        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            float my = v * H;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;
                float grain = Noise.Hash01(x, y, 5) - 0.5f;
                float s = Noise.Sample(stain, n, u, v);
                float st = Noise.Sample(streak, n, u, v);
                float wg = Noise.Sample(grainF, n, u, v);

                float cr, cg, cb, smooth, height;

                float wainEnd = 0.36f;
                float railEnd = 0.405f;
                if (v < wainEnd)
                {
                    // Zócalo de madera con paneles hundidos (2 paneles por baldosa)
                    float pu = Fr(u * 2f);
                    float pvv = v / wainEnd;
                    float dEdge = Mathf.Min(Mathf.Min(pu - 0.08f, 0.92f - pu), Mathf.Min(pvv - 0.10f, 0.90f - pvv));
                    float inside = Sm(-0.01f, 0.03f, dEdge);
                    float bevel = Sm(0f, 0.04f, dEdge) * (1f - Sm(0.04f, 0.09f, dEdge));
                    float w = 0.75f + 0.5f * wg;
                    cr = 0.22f * w; cg = 0.115f * w; cb = 0.065f * w;
                    float shade = Lp(1f, 0.82f, inside) + 0.18f * bevel;
                    cr *= shade; cg *= shade; cb *= shade;
                    height = 0.5f - 0.35f * inside + 0.1f * bevel + 0.03f * grain;
                    smooth = 0.42f + 0.1f * wg;
                    // Rodapié
                    float bd = 1f - Sm(0.03f, 0.036f, v);
                    cr = Lp(cr, 0.14f, bd * 0.5f); cg = Lp(cg, 0.075f, bd * 0.5f); cb = Lp(cb, 0.045f, bd * 0.5f);
                }
                else if (v < railEnd)
                {
                    // Moldura (listón) superior del zócalo
                    float t = (v - wainEnd) / (railEnd - wainEnd);
                    float hi = 1f - Mathf.Abs(t - 0.5f) * 2f;
                    float w = 0.8f + 0.4f * wg;
                    cr = 0.34f * w; cg = 0.19f * w; cb = 0.10f * w;
                    height = 0.7f + 0.25f * hi;
                    smooth = 0.5f;
                }
                else
                {
                    // Papel damasco burdeos con rayas doradas y motivos ovalados
                    float f = Fr(u * 8f);
                    float cell = Mathf.Floor(u * 8f);
                    float yy = my / 0.45f + (((int)cell & 1) == 0 ? 0f : 0.5f);
                    float oy = Fr(yy) - 0.5f;
                    float ox = f - 0.5f;
                    float e = (ox * ox) / (0.26f * 0.26f) + (oy * oy) / (0.30f * 0.30f);
                    float inner = 1f - Sm(0.85f, 1f, e);
                    float ring = Cl(Sm(0.8f, 0.95f, e) - Sm(1f, 1.2f, e));
                    float pin = 1f - Sm(0f, 0.03f, Mathf.Abs(f - 0.5f) - 0.0f);
                    pin *= (e > 1.3f) ? 1f : 0f;

                    cr = 0.27f; cg = 0.055f; cb = 0.075f;
                    cr = Lp(cr, 0.37f, inner * 0.8f); cg = Lp(cg, 0.075f, inner * 0.8f); cb = Lp(cb, 0.10f, inner * 0.8f);
                    cr = Lp(cr, 0.46f, ring * 0.7f); cg = Lp(cg, 0.34f, ring * 0.7f); cb = Lp(cb, 0.15f, ring * 0.7f);
                    cr = Lp(cr, 0.40f, pin * 0.6f); cg = Lp(cg, 0.30f, pin * 0.6f); cb = Lp(cb, 0.13f, pin * 0.6f);
                    float gk = 1f + 0.12f * grain;
                    cr *= gk; cg *= gk; cb *= gk;
                    height = 0.5f + 0.15f * inner + 0.2f * ring + 0.1f * pin + 0.03f * grain;
                    smooth = 0.10f + 0.1f * ring;
                    // Cornisa
                    float corn = Sm(0.93f, 0.95f, v);
                    cr = Lp(cr, 0.30f, corn); cg = Lp(cg, 0.24f, corn); cb = Lp(cb, 0.16f, corn);
                    height += 0.3f * corn;
                }

                // Manchas y envejecimiento general
                float leak = Sm(0.58f, 0.8f, s * 0.5f + st * 0.5f) * (0.3f + 0.7f * v);
                cr *= 1f - 0.45f * leak; cg *= 1f - 0.5f * leak; cb *= 1f - 0.5f * leak;
                float ao = Lp(0.65f, 1f, Sm(0f, 0.25f, v)) * Lp(0.75f, 1f, 1f - Sm(0.85f, 1f, v));
                float bake = 0.8f + 0.2f * ao;
                cr *= bake; cg *= bake; cb *= bake;
                c.Set(i, cr, cg, cb, Cl(smooth), ao, 0f, height);
            }
        });
        return c;
    }

    // ------------------------------------------------------------------
    // Moqueta (marrón desgastada / roja con rombos dorados)
    // ------------------------------------------------------------------
    private static Canvas Carpet(int n, bool red)
    {
        Canvas c = new Canvas(n);
        float[] patches = Noise.Fbm(256, 6, 6, 4, 0.55f, red ? 41 : 51);
        float[] stains = Noise.Fbm(256, 5, 5, 5, 0.55f, red ? 42 : 52);
        float[] fibre = Noise.Fbm(512, 96, 96, 2, 0.6f, red ? 43 : 53);
        float[] wear = Noise.Fbm(256, 3, 3, 3, 0.5f, red ? 44 : 54);

        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;
                float t1 = Noise.Hash01(x, y, 7);
                float t2 = Noise.Hash01(x / 2, y / 2, 8);
                float fb = Noise.Sample(fibre, n, u, v);
                float tuft = 0.55f * t1 + 0.25f * t2 + 0.5f * fb;   // 0..1.3
                float pa = Noise.Sample(patches, n, u, v);
                float stn = Noise.Sample(stains, n, u, v);
                float wr = Noise.Sample(wear, n, u, v);

                float cr, cg, cb;
                if (red)
                {
                    cr = 0.40f; cg = 0.055f; cb = 0.065f;
                }
                else
                {
                    cr = 0.44f; cg = 0.37f; cb = 0.26f;
                }
                float shade = 0.72f + 0.55f * tuft;
                float pk = 0.88f + 0.24f * pa;
                float k = shade * pk;
                cr *= k; cg *= k; cb *= k;

                float height = tuft * 0.8f;

                if (red)
                {
                    // Rombos dorados (4 por baldosa)
                    float du = Mathf.Abs(Fr(u * 4f) - 0.5f);
                    float dv = Mathf.Abs(Fr(v * 4f) - 0.5f);
                    float d = du + dv;
                    float lineMask = 1f - Sm(0.008f, 0.02f, Mathf.Abs(d - 0.34f));
                    float dot = 1f - Sm(0.04f, 0.07f, d);
                    float inner = 1f - Sm(0.02f, 0.035f, Mathf.Abs(d - 0.2f));
                    float gold = Cl(lineMask + dot * 0.9f + inner * 0.6f);
                    float gk = 0.65f + 0.5f * tuft;
                    cr = Lp(cr, 0.55f * gk, gold * 0.85f);
                    cg = Lp(cg, 0.40f * gk, gold * 0.85f);
                    cb = Lp(cb, 0.14f * gk, gold * 0.85f);
                    height += gold * 0.12f;
                }

                // Zonas de paso (más claras) y manchas oscuras
                float worn = Sm(0.55f, 0.8f, wr) * 0.12f;
                cr += worn * 0.5f; cg += worn * 0.45f; cb += worn * 0.3f;
                float sm = Sm(0.70f, 0.80f, stn);
                cr = Lp(cr, cr * 0.72f, sm * 0.6f);
                cg = Lp(cg, cg * 0.68f, sm * 0.6f);
                cb = Lp(cb, cb * 0.66f, sm * 0.6f);

                float ao = 0.55f + 0.45f * Cl(tuft);
                c.Set(i, cr, cg, cb, 0.04f + 0.04f * sm, ao, 0f, height);
            }
        });
        return c;
    }

    // ------------------------------------------------------------------
    // Techo de placas acústicas: rejilla, puntitos, vetas y manchas
    // ------------------------------------------------------------------
    private static Canvas CeilingTile(int n, Color baseColor, bool joints)
    {
        Canvas c = new Canvas(n);
        float[] veins = Noise.Fbm(256, 20, 20, 3, 0.55f, 61);
        float[] stain = Noise.Fbm(256, 3, 3, 5, 0.55f, 62);
        float[] blot = Noise.Fbm(256, 8, 8, 3, 0.5f, 63);

        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;
                float grain = Noise.Hash01(x, y, 9) - 0.5f;
                float vn = Noise.Sample(veins, n, u, v);
                float st = Noise.Sample(stain, n, u, v);
                float bl = Noise.Sample(blot, n, u, v);

                float k = 0.93f + 0.1f * (bl - 0.5f) + 0.06f * grain;
                float cr = baseColor.r * k, cg = baseColor.g * k, cb = baseColor.b * k;
                float height = 0.5f + 0.05f * grain;
                float ao = 1f;

                // Puntitos (perforaciones) y fisuras
                float pin = Noise.Hash01(x, y, 10) < 0.018f ? 1f : 0f;
                float big = Noise.Hash01(x / 3, y / 3, 11) < 0.01f ? 1f : 0f;
                float ridge = 1f - Mathf.Abs(2f * vn - 1f);
                float fis = Sm(0.965f, 0.985f, ridge) * Sm(0.45f, 0.6f, bl);
                float holes = Cl(pin + big + fis);
                cr *= 1f - 0.55f * holes; cg *= 1f - 0.55f * holes; cb *= 1f - 0.5f * holes;
                height -= 0.5f * holes;
                ao -= 0.35f * holes;

                // Juntas de la rejilla (2x2 placas)
                if (joints)
                {
                    float du = Mathf.Min(Fr(u * 2f), 1f - Fr(u * 2f));
                    float dv = Mathf.Min(Fr(v * 2f), 1f - Fr(v * 2f));
                    float d = Mathf.Min(du, dv) * n * 0.5f;     // distancia en píxeles
                    float groove = 1f - Sm(0.5f, 3.5f, d);
                    float lip = Sm(3f, 4.5f, d) * (1f - Sm(4.5f, 7f, d));
                    cr *= 1f - 0.65f * groove; cg *= 1f - 0.65f * groove; cb *= 1f - 0.65f * groove;
                    cr += 0.03f * lip; cg += 0.03f * lip; cb += 0.03f * lip;
                    height += -0.8f * groove + 0.1f * lip;
                    ao -= 0.5f * groove;
                }

                // Manchas amarillo-marrón por humedad
                float sm = Sm(0.68f, 0.82f, st);
                cr = Lp(cr, cr * 0.82f, sm); cg = Lp(cg, cg * 0.68f, sm); cb = Lp(cb, cb * 0.42f, sm);
                float ring = Cl(Sm(0.58f, 0.6f, st) - Sm(0.6f, 0.64f, st));
                cr *= 1f - 0.12f * ring; cg *= 1f - 0.18f * ring; cb *= 1f - 0.25f * ring;

                c.Set(i, cr, cg, cb, 0.05f + 0.1f * sm, Cl(ao), 0f, height);
            }
        });
        return c;
    }

    // ------------------------------------------------------------------
    // Hormigón: mode 0 = pared (juntas de encofrado, agujeros de tirantes),
    //           1 = suelo (manchas de aceite, líneas de plaza de aparcamiento), 2 = techo
    // ------------------------------------------------------------------
    private static Canvas Concrete(int n, int mode, float H)
    {
        Canvas c = new Canvas(n);
        int sd = 70 + mode * 10;
        float[] mott = Noise.Fbm(256, 6, 6, 6, 0.55f, sd + 1);
        float[] pore = Noise.Fbm(512, 48, 48, 2, 0.5f, sd + 2);
        float[] crack = Noise.Fbm(512, 5, 5, 4, 0.5f, sd + 3);
        float[] streak = Noise.Fbm(256, 30, 2, 3, 0.5f, sd + 4);
        float[] oil = Noise.Fbm(256, 4, 4, 5, 0.55f, sd + 5);
        float[] paintWear = Noise.Fbm(256, 12, 12, 4, 0.55f, sd + 6);

        float br = mode == 0 ? 0.56f : (mode == 1 ? 0.47f : 0.40f);

        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;
                float grain = Noise.Hash01(x, y, 12) - 0.5f;
                float m = Noise.Sample(mott, n, u, v);
                float po = Noise.Sample(pore, n, u, v);
                float cr0 = Noise.Sample(crack, n, u, v);
                float stv = Noise.Sample(streak, n, u, v);
                float ol = Noise.Sample(oil, n, u, v);

                float k = br * (0.78f + 0.44f * m) * (1f + 0.10f * grain);
                float cr = k * 1.0f, cg = k * 1.0f, cb = k * 0.97f;
                float height = 0.5f + 0.06f * grain + 0.1f * (m - 0.5f);
                float ao = 0.78f + 0.22f * m;
                float smooth = mode == 1 ? 0.18f + 0.3f * Sm(0.35f, 0.75f, m) : 0.07f + 0.05f * m;

                // Poros y picaduras
                float poreMask = Sm(0.76f, 0.82f, po);
                float pit = Noise.Hash01(x, y, 13) < 0.003f ? 1f : 0f;
                float pm = Cl(poreMask + pit);
                cr *= 1f - 0.5f * pm; cg *= 1f - 0.5f * pm; cb *= 1f - 0.5f * pm;
                height -= 0.5f * pm; ao -= 0.3f * pm;

                // Grietas finas (escasas)
                float ridge = 1f - Mathf.Abs(2f * cr0 - 1f);
                float crackMask = Sm(0.975f, 0.992f, ridge) * Sm(0.5f, 0.65f, Noise.Sample(mott, n, u + 0.31f, v + 0.17f));
                cr *= 1f - 0.75f * crackMask; cg *= 1f - 0.75f * crackMask; cb *= 1f - 0.75f * crackMask;
                height -= 0.8f * crackMask; ao -= 0.4f * crackMask;

                if (mode == 0)
                {
                    // Juntas de encofrado y agujeros de tirantes
                    float dh = Mathf.Min(Mathf.Abs(v - 0.5f), Mathf.Min(v, 1f - v)) * n;
                    float dv = Mathf.Min(u, 1f - u) * n;
                    float seam = Cl((1f - Sm(0.5f, 2.5f, dh)) + (1f - Sm(0.5f, 2.2f, dv)));
                    cr *= 1f - 0.5f * seam; cg *= 1f - 0.5f * seam; cb *= 1f - 0.5f * seam;
                    height -= 0.6f * seam; ao -= 0.3f * seam;

                    float gu = Fr(u * 3f + 0.5f) - 0.5f;
                    float gv = Fr(v * 2f + 0.5f) - 0.5f;
                    float d = Mathf.Sqrt(gu * gu * 9f + gv * gv * 4f) * 0.33f;   // aprox. en unidades de baldosa
                    float hole = 1f - Sm(0.012f, 0.018f, d);
                    float rim = Cl(Sm(0.016f, 0.02f, d) - Sm(0.022f, 0.03f, d));
                    cr *= 1f - 0.6f * hole; cg *= 1f - 0.6f * hole; cb *= 1f - 0.6f * hole;
                    height += -0.9f * hole + 0.2f * rim;

                    // Chorreones oscuros y suciedad en la base
                    float dk = Sm(0.55f, 0.8f, stv) * (0.3f + 0.7f * (1f - v));
                    cr *= 1f - 0.35f * dk; cg *= 1f - 0.35f * dk; cb *= 1f - 0.35f * dk;
                    float dirt = (1f - Sm(0f, 0.18f, v));
                    cr *= 1f - 0.3f * dirt; cg *= 1f - 0.3f * dirt; cb *= 1f - 0.3f * dirt;
                    ao *= Lp(0.7f, 1f, Sm(0f, 0.25f, v)) * Lp(0.85f, 1f, 1f - Sm(0.85f, 1f, v));
                }
                else if (mode == 1)
                {
                    // Juntas de dilatación
                    float dh = Mathf.Min(v, 1f - v) * n * 0.5f;
                    float dv = Mathf.Min(u, 1f - u) * n * 0.5f;
                    float joint = Cl((1f - Sm(0.5f, 2.5f, dh * 0.5f)) * 0.5f + (1f - Sm(0.5f, 2.5f, dv * 0.5f)) * 0.5f);
                    cr *= 1f - 0.45f * joint; cg *= 1f - 0.45f * joint; cb *= 1f - 0.45f * joint;
                    height -= 0.4f * joint;

                    // Manchas de aceite (oscuras y brillantes)
                    float oilMask = Sm(0.72f, 0.80f, ol);
                    cr = Lp(cr, cr * 0.42f, oilMask * 0.85f);
                    cg = Lp(cg, cg * 0.42f, oilMask * 0.85f);
                    cb = Lp(cb, cb * 0.45f, oilMask * 0.85f);
                    smooth = Lp(smooth, 0.65f, oilMask);

                    // Líneas de plaza de aparcamiento desgastadas (baldosa 2.7 x 5.4 m)
                    float lineU = 1f - Sm(0.012f, 0.02f, Mathf.Min(u, 1f - u));
                    float lineV = 1f - Sm(0.006f, 0.011f, v);
                    float lineMask = Cl(lineU + lineV * (Mathf.Abs(u - 0.5f) < 0.48f ? 1f : 0f));
                    float wr = Noise.Sample(paintWear, n, u, v);
                    float paint = lineMask * (0.3f + 0.7f * Sm(0.3f, 0.6f, wr)) * (grain > -0.35f ? 1f : 0.6f);
                    cr = Lp(cr, 0.80f, paint); cg = Lp(cg, 0.72f, paint); cb = Lp(cb, 0.30f, paint);
                    smooth = Lp(smooth, 0.3f, paint);
                    height += 0.05f * paint;
                    // Desgaste por tránsito
                    float wearBand = Sm(0.55f, 0.8f, Noise.Sample(paintWear, n, u * 0.5f, v * 0.5f));
                    smooth += 0.12f * wearBand;
                }
                else
                {
                    // Techo: vigas y juntas
                    float db = Mathf.Min(Mathf.Abs(u - 0.5f), Mathf.Min(u, 1f - u)) * n;
                    float beam = 1f - Sm(0.5f, 3f, db);
                    cr *= 1f - 0.45f * beam; cg *= 1f - 0.45f * beam; cb *= 1f - 0.45f * beam;
                    height -= 0.5f * beam;
                    float dk = Sm(0.55f, 0.8f, stv) * 0.5f;
                    cr *= 1f - 0.3f * dk; cg *= 1f - 0.3f * dk; cb *= 1f - 0.3f * dk;
                }

                c.Set(i, cr, cg, cb, Cl(smooth), Cl(ao), 0f, height);
            }
        });
        return c;
    }

    // ------------------------------------------------------------------
    // Azulejos de piscina (25 cm, 8x8 por baldosa de 2 m): juntas, biselado, suciedad y algas
    // ------------------------------------------------------------------
    private static Canvas PoolTile(int n, bool wall)
    {
        Canvas c = new Canvas(n);
        float[] algae = Noise.Fbm(256, 5, 5, 5, 0.55f, wall ? 81 : 91);
        float[] grime = Noise.Fbm(256, 10, 10, 4, 0.55f, wall ? 82 : 92);
        float[] crackF = Noise.Fbm(512, 6, 6, 4, 0.5f, wall ? 83 : 93);
        int tiles = 8;
        float tilePx = n / (float)tiles;

        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;
                int tx = (int)(x / tilePx), ty = (int)(y / tilePx);
                float lx = (x - tx * tilePx) / tilePx;      // 0..1 dentro del azulejo
                float ly = (y - ty * tilePx) / tilePx;
                float grain = Noise.Hash01(x, y, 14) - 0.5f;
                float tv = Noise.Hash01(tx, ty, wall ? 15 : 16);
                float tv2 = Noise.Hash01(tx, ty, 17);

                float edge = Mathf.Min(Mathf.Min(lx, 1f - lx), Mathf.Min(ly, 1f - ly));
                float edgePx = edge * tilePx;
                float grout = 1f - Sm(2f, 4f, edgePx);
                float bevel = Sm(3f, 6f, edgePx) * (1f - Sm(6f, 11f, edgePx));

                float cr, cg, cb;
                if (wall)
                {
                    cr = 0.70f; cg = 0.84f; cb = 0.85f;
                    if (ty == 3)   // franja azul
                    {
                        cr = 0.20f; cg = 0.50f; cb = 0.68f;
                    }
                }
                else
                {
                    cr = 0.62f; cg = 0.74f; cb = 0.76f;
                }
                float vr = 0.9f + 0.14f * tv;
                cr *= vr; cg *= vr; cb *= vr;
                cr *= 1f + 0.04f * grain; cg *= 1f + 0.04f * grain; cb *= 1f + 0.04f * grain;

                // Gradiente dentro del azulejo (cristalino)
                float gl = 1f + 0.06f * (lx - ly);
                cr *= gl; cg *= gl; cb *= gl;

                // Azulejos perdidos / desportillados
                float missing = tv2 < 0.012f ? 1f : 0f;
                float chip = (tv2 > 0.9f && lx > 0.7f && ly > 0.7f) ? 1f : 0f;
                cr = Lp(cr, 0.27f, missing); cg = Lp(cg, 0.28f, missing); cb = Lp(cb, 0.26f, missing);
                

                float al = Noise.Sample(algae, n, u, v);
                float gr = Noise.Sample(grime, n, u, v);
                float cf = Noise.Sample(crackF, n, u, v);

                // Algas y suciedad (más en la junta)
                float algaeMask = Sm(0.72f, 0.85f, al) * (0.4f + 0.6f * grout);
                cr = Lp(cr, 0.28f, algaeMask * 0.7f); cg = Lp(cg, 0.42f, algaeMask * 0.7f); cb = Lp(cb, 0.22f, algaeMask * 0.7f);
                float gm = Sm(0.62f, 0.9f, gr) * 0.12f;
                cr *= 1f - gm; cg *= 1f - gm; cb *= 1f - gm * 0.9f;

                // Grietas en el esmalte
                float ridge = 1f - Mathf.Abs(2f * cf - 1f);
                float crackMask = Sm(0.992f, 0.998f, ridge) * (1f - grout) * Sm(0.6f, 0.8f, gr);
                cr *= 1f - 0.6f * crackMask; cg *= 1f - 0.6f * crackMask; cb *= 1f - 0.6f * crackMask;

                // Junta: mortero sucio
                float gk = 0.36f + 0.1f * grain + 0.15f * algaeMask;
                cr = Lp(cr, gk * 0.95f, grout);
                cg = Lp(cg, gk * 1.02f, grout);
                cb = Lp(cb, gk * 0.92f, grout);

                float height = 0.78f + 0.12f * bevel + 0.02f * grain - 0.8f * grout - 0.4f * crackMask - 0.5f * missing - 0.1f * chip;
                float smooth = wall ? 0.82f : 0.88f;
                smooth = Lp(smooth, 0.15f, grout);
                smooth -= 0.4f * algaeMask + 0.3f * gm;
                smooth = Lp(smooth, 0.1f, missing);
                float ao = 1f - 0.6f * grout - 0.2f * missing;
                c.Set(i, cr, cg, cb, Cl(smooth), Cl(ao), 0f, height);
            }
        });
        return c;
    }

    // ------------------------------------------------------------------
    // Chapa pintada y oxidada con remaches (paneles de 1.2 m, baldosa de 2.4 m)
    // ------------------------------------------------------------------
    private static Canvas MetalPlate(int n)
    {
        Canvas c = new Canvas(n);
        float[] mott = Noise.Fbm(256, 5, 5, 5, 0.55f, 101);
        float[] peel = Noise.Fbm(256, 4, 4, 5, 0.55f, 102);
        float[] streak = Noise.Fbm(256, 40, 2, 3, 0.5f, 103);
        float[] scr = Noise.Fbm(512, 3, 60, 2, 0.5f, 104);

        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;
                float grain = Noise.Hash01(x, y, 18) - 0.5f;
                float m = Noise.Sample(mott, n, u, v);
                float p = Noise.Sample(peel, n, u, v);
                float stv = Noise.Sample(streak, n, u, v);
                float sc = Noise.Sample(scr, n, u, v);

                // Coordenadas dentro del panel (metros)
                float pu = Fr(u * 2f), pv = Fr(v * 2f);
                float ax = pu * 1.2f, ay = pv * 1.2f;
                float dEdge = Mathf.Min(Mathf.Min(ax, 1.2f - ax), Mathf.Min(ay, 1.2f - ay));
                float seam = 1f - Sm(0.005f, 0.016f, dEdge);
                float bevel = Sm(0.012f, 0.03f, dEdge) * (1f - Sm(0.03f, 0.07f, dEdge));

                // Remaches cada 15 cm a 5.5 cm del borde
                float dxr = Fr(ax / 0.15f) * 0.15f - 0.075f;
                float dyr = Fr(ay / 0.15f) * 0.15f - 0.075f;
                float edgeX = Mathf.Min(ax, 1.2f - ax) - 0.055f;   // cerca de los bordes verticales
                float edgeY = Mathf.Min(ay, 1.2f - ay) - 0.055f;
                float r1 = Mathf.Sqrt(edgeX * edgeX + dyr * dyr);
                float r2 = Mathf.Sqrt(edgeY * edgeY + dxr * dxr);
                float rivet = 1f - Sm(0.008f, 0.014f, Mathf.Min(r1, r2));

                float k = 0.85f + 0.3f * m;
                float cr = 0.30f * k, cg = 0.36f * k, cb = 0.33f * k;       // pintura verde grisácea
                float metal = 0.15f;
                float smooth = 0.42f;

                // Pintura desconchada -> óxido; metal desnudo en el borde del desconchado
                float rust = Sm(0.68f, 0.72f, p);
                float bare = Cl(Sm(0.655f, 0.68f, p) - rust);
                float rg = 0.65f + 0.7f * (Noise.Hash01(x / 2, y / 2, 19));
                cr = Lp(cr, 0.46f * rg, rust); cg = Lp(cg, 0.22f * rg, rust); cb = Lp(cb, 0.09f * rg, rust);
                cr = Lp(cr, 0.55f, bare); cg = Lp(cg, 0.55f, bare); cb = Lp(cb, 0.56f, bare);
                metal = Lp(metal, 0.2f, rust); metal = Lp(metal, 0.9f, bare);
                smooth = Lp(smooth, 0.12f, rust); smooth = Lp(smooth, 0.65f, bare);

                // Chorreones de óxido desde los remaches / costuras
                float dr = Sm(0.62f, 0.85f, stv) * (0.25f + 0.75f * Sm(0f, 0.5f, 1f - pv));
                cr = Lp(cr, cr * 0.9f + 0.1f, dr * 0.5f); cg = Lp(cg, cg * 0.6f, dr * 0.5f); cb = Lp(cb, cb * 0.35f, dr * 0.5f);

                // Arañazos horizontales
                float scratch = Sm(0.93f, 0.97f, sc);
                cr = Lp(cr, 0.55f, scratch * 0.35f); cg = Lp(cg, 0.55f, scratch * 0.35f); cb = Lp(cb, 0.55f, scratch * 0.35f);

                cr *= 1f - 0.7f * seam; cg *= 1f - 0.7f * seam; cb *= 1f - 0.7f * seam;
                cr *= 1f + 0.35f * rivet; cg *= 1f + 0.35f * rivet; cb *= 1f + 0.35f * rivet;
                cr *= 1f + 0.06f * grain; cg *= 1f + 0.06f * grain; cb *= 1f + 0.06f * grain;

                float height = 0.5f + 0.15f * bevel - 0.8f * seam + 0.6f * rivet - 0.25f * rust + 0.03f * grain - 0.1f * scratch;
                float ao = Lp(0.55f, 1f, Sm(0f, 0.05f, dEdge / 1.2f * 6f)) * (1f - 0.2f * rust);
                c.Set(i, cr, cg, cb, Cl(smooth), Cl(ao), Cl(metal), height);
            }
        });
        return c;
    }

    // ------------------------------------------------------------------
    // Chapa lagrimada de suelo industrial (baldosa de 1 m)
    // ------------------------------------------------------------------
    private static Canvas DiamondPlate(int n)
    {
        Canvas c = new Canvas(n);
        float[] grime = Noise.Fbm(256, 6, 6, 5, 0.55f, 111);
        float[] rustF = Noise.Fbm(256, 4, 4, 5, 0.55f, 112);
        float[] oil = Noise.Fbm(256, 3, 3, 4, 0.5f, 113);

        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;
                float grain = Noise.Hash01(x, y, 20) - 0.5f;
                float gr = Noise.Sample(grime, n, u, v);
                float ru = Noise.Sample(rustF, n, u, v);
                float ol = Noise.Sample(oil, n, u, v);

                float cu = Fr(u * 8f), cv = Fr(v * 8f);
                int parity = ((int)(u * 8f) + (int)(v * 8f)) & 1;
                float a = parity == 0 ? (cu - 0.5f) + (cv - 0.5f) : (cu - 0.5f) - (cv - 0.5f);   // eje perpendicular a la barra
                float b = parity == 0 ? (cu - 0.5f) - (cv - 0.5f) : (cu - 0.5f) + (cv - 0.5f);   // eje a lo largo de la barra
                float bar = (1f - Sm(0.06f, 0.11f, Mathf.Abs(a))) * (1f - Sm(0.30f, 0.38f, Mathf.Abs(b)));

                float k = 0.30f * (0.75f + 0.5f * gr);
                float cr = k, cg = k * 1.02f, cb = k * 0.98f;
                cr += 0.12f * bar; cg += 0.12f * bar; cb += 0.12f * bar;      // barras más pulidas
                float rust = Sm(0.70f, 0.80f, ru);
                float rg = 0.7f + 0.6f * Noise.Hash01(x / 2, y / 2, 21);
                cr = Lp(cr, 0.40f * rg, rust * 0.8f); cg = Lp(cg, 0.19f * rg, rust * 0.8f); cb = Lp(cb, 0.08f * rg, rust * 0.8f);
                float oilM = Sm(0.72f, 0.80f, ol);
                cr *= 1f - 0.4f * oilM; cg *= 1f - 0.4f * oilM; cb *= 1f - 0.4f * oilM;
                cr *= 1f + 0.08f * grain; cg *= 1f + 0.08f * grain; cb *= 1f + 0.08f * grain;

                float height = 0.3f + 0.7f * bar + 0.03f * grain;
                float smooth = 0.28f + 0.3f * bar - 0.15f * rust + 0.3f * oilM;
                float metal = 0.7f * (1f - rust);
                float ao = 0.55f + 0.45f * bar;
                c.Set(i, cr, cg, cb, Cl(smooth), Cl(ao), Cl(metal), height);
            }
        });
        return c;
    }

    // Normal map de ondulaciones del agua (el albedo/máscara no se usan)
    private static Canvas WaterRipples(int n)
    {
        Canvas c = new Canvas(n);
        float[] f = Noise.Fbm(256, 5, 5, 5, 0.5f, 121);
        Parallel.For(0, n, y =>
        {
            float v = (y + 0.5f) / n;
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n;
                int i = y * n + x;
                float w = 0.5f * Mathf.Sin(2f * Mathf.PI * (3f * u + 2f * v))
                        + 0.35f * Mathf.Sin(2f * Mathf.PI * (-5f * u + 4f * v + 0.3f))
                        + 0.25f * Mathf.Sin(2f * Mathf.PI * (7f * u + 1f * v + 0.7f));
                float height = 0.5f + 0.06f * w + 0.5f * Noise.Sample(f, n, u, v) * 0.3f;
                c.Set(i, 0.2f, 0.4f, 0.5f, 0.95f, 1f, 0f, height);
            }
        });
        return c;
    }
}
