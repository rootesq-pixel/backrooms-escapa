using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.7.1: objetos del desierto con modelo de verdad (mallas de revolución, medidas reales):
//  · botellas de vidrio de 33 cl (fondo con picada, hombro, cuello y anillo de la boca), vidrio translúcido, líquido dentro,
//    etiqueta de papel impresa (textura generada: franja, dibujo, «texto», manchas) y tapón metálico o de corcho
//  · lata de raciones (cuerpo con nervios, rebordes enrollados, tapa con anillas, etiqueta de papel con sello real, abollada)
// Solo cambia el aspecto: el ItemPickup (recogida, id de red) es el mismo.
public static class DesertItemModels
{
    static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    public static int Built { get; private set; }

    public static bool Enabled = true;   // capturas «antes» (ShotBuildCheck): el aspecto de la v7.7.0
    public static bool Handles(ItemType t)
    {
        if (!Enabled) return false;
        if (t == ItemType.CashewWater || t == ItemType.NeonWater || t == ItemType.RoyalRations) return true;
        return t == ItemType.AlmondWater && DesertWorld.Active != null;   // en los demás niveles, el modelo de siempre
    }

    // perfil (radio, altura) -> malla de revolución con normales suaves; v de la UV = longitud de arco normalizada
    public static Mesh Lathe(string key, Vector2[] prof, int seg, bool inward = false, float u0 = 0f, float u1 = 1f)
    {
        if (meshes.TryGetValue(key, out Mesh cached) && cached != null) return cached;
        int n = prof.Length; var v = new List<Vector3>(); var nr = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
        float[] arc = new float[n]; for (int i = 1; i < n; i++) arc[i] = arc[i - 1] + Vector2.Distance(prof[i], prof[i - 1]);
        float tot = Mathf.Max(1e-5f, arc[n - 1]);
        for (int s = 0; s <= seg; s++)
        {
            float a = s * Mathf.PI * 2f / seg; float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            for (int i = 0; i < n; i++)
            {
                Vector2 d = (i == 0 ? prof[1] - prof[0] : i == n - 1 ? prof[n - 1] - prof[n - 2] : prof[i + 1] - prof[i - 1]).normalized;
                Vector2 pn = new Vector2(d.y, -d.x); if (inward) pn = -pn;      // normal del perfil (hacia fuera si el perfil sube)
                v.Add(new Vector3(prof[i].x * ca, prof[i].y, prof[i].x * sa));
                Vector3 nn = new Vector3(pn.x * ca, pn.y, pn.x * sa); nr.Add(nn.sqrMagnitude > 1e-8f ? nn.normalized : Vector3.up);
                uv.Add(new Vector2(Mathf.Lerp(u0, u1, s / (float)seg), arc[i] / tot));
            }
        }
        for (int s = 0; s < seg; s++)
            for (int i = 0; i < n - 1; i++)
            {
                int a0 = s * n + i, a1 = a0 + 1, b0 = a0 + n, b1 = b0 + 1;
                if (prof[i].x < 1e-5f && prof[i + 1].x < 1e-5f) continue;
                // la cara mira hacia la normal: comprobación geométrica por cuadrilátero (sin triángulos de área 0)
                Vector3 g = Vector3.Cross(v[a1] - v[a0], v[b0] - v[a0]);
                bool flip = Vector3.Dot(g, nr[a0] + nr[a1]) < 0f;
                if (prof[i].x > 1e-5f) { if (!flip) { tr.Add(a0); tr.Add(a1); tr.Add(b0); } else { tr.Add(a0); tr.Add(b0); tr.Add(a1); } }
                if (prof[i + 1].x > 1e-5f) { if (!flip) { tr.Add(a1); tr.Add(b1); tr.Add(b0); } else { tr.Add(a1); tr.Add(b0); tr.Add(b1); } }
            }
        Mesh m = new Mesh { name = key }; m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(tr, 0);
        m.RecalculateBounds(); m.RecalculateTangents(); meshes[key] = m; return m;
    }

    static GameObject Part(Transform r, string name, Mesh mesh, Material mat, bool shadows = true)
    {
        GameObject g = new GameObject(name); g.transform.SetParent(r, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; mr.receiveShadows = true;
        return g;
    }

    // ---------------------------------------------------------------- materiales
    static Material Glass(string key, Color tint)
    {
        if (mats.TryGetValue(key, out Material m) && m != null) return m;
        m = ProceduralAssets.CreateWaterMaterial(null, tint); m.name = key;
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.93f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.05f);
        m.renderQueue = 3010; mats[key] = m; return m;
    }
    static Material Liquid(string key, Color c, Color emit)
    {
        if (mats.TryGetValue(key, out Material m) && m != null) return m;
        m = ProceduralAssets.CreateWaterMaterial(null, c); m.name = key; m.renderQueue = 3000;
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.8f);
        if (emit.maxColorComponent > 0f) ProceduralAssets.SetEmission(m, emit);
        mats[key] = m; return m;
    }
    static Material Solid(string key, Color c, float smooth, float metal, Texture2D tex = null)
    {
        if (mats.TryGetValue(key, out Material m) && m != null) return m;
        m = ProceduralAssets.CreateMaterial(tex != null ? Color.white : c, tex, Vector2.one, smooth); m.name = key;
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        mats[key] = m; return m;
    }

    // etiqueta impresa: papel envejecido, franjas, un dibujo (almendra / anacardo / estrella de neón / sello) y líneas de «texto»
    static Texture2D LabelTex(string key, Color paper, Color ink, Color band, int motif)
    {
        int W = 256, H = 96; Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = key, wrapMode = TextureWrapMode.Repeat, anisoLevel = 4 };
        var px = new Color32[W * H]; System.Random r = new System.Random(key.GetHashCode());
        float ph = (float)r.NextDouble() * 10f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                Color c = paper * (0.93f + 0.07f * Mathf.PerlinNoise(u * 40f + ph, v * 14f));
                if (v < 0.13f || v > 0.87f) c = band;                                         // franjas arriba y abajo
                else if (v < 0.17f || v > 0.83f) c = Color.Lerp(band, paper, 0.5f);
                // dibujo centrado en u=0.25 (y su gemelo en u=0.75): elipse / estrella
                for (int k = 0; k < 2; k++)
                {
                    float du = (u - (0.25f + 0.5f * k)) * 2.7f, dv = (v - 0.52f);
                    float e = motif == 0 ? (du * du) / 0.020f + (dv * dv) / 0.055f                 // almendra (alargada)
                            : motif == 1 ? Mathf.Pow(Mathf.Abs(du) / 0.16f, 2f) + Mathf.Pow(Mathf.Abs(dv + 0.25f * du * du * 9f) / 0.18f, 2f)   // anacardo (curvo)
                            : motif == 2 ? (Mathf.Abs(du) + Mathf.Abs(dv)) / 0.2f                     // rombo de neón
                            : (du * du + dv * dv) / 0.04f;                                            // sello redondo
                    if (e < 1f) c = Color.Lerp(ink, band, e * 0.5f);
                    if (motif == 3 && e > 0.75f && e < 0.95f) c = band;
                }
                // «texto»: renglones de trazos cortos a la derecha del dibujo
                float tu = Mathf.Repeat(u, 0.5f);
                if (tu > 0.34f && tu < 0.48f)
                {
                    int row = Mathf.FloorToInt((v - 0.3f) / 0.09f);
                    if (row >= 0 && row < 4 && Mathf.Repeat(v - 0.3f, 0.09f) < 0.045f && Mathf.PerlinNoise(x * 0.35f, row * 3.7f + ph) > 0.42f) c = Color.Lerp(c, ink, 0.85f);
                }
                if (tu > 0.08f && tu < 0.18f && v > 0.2f && v < 0.27f) c = Color.Lerp(c, ink, 0.9f);   // nombre
                // manchas, desgaste y arena pegada
                float st = Mathf.PerlinNoise(u * 6f + 3.1f + ph, v * 3f);
                if (st > 0.66f) c *= Mathf.Lerp(1f, 0.78f, (st - 0.66f) * 4f);
                if (r.NextDouble() < 0.004) c = Color.Lerp(c, new Color(0.78f, 0.66f, 0.46f), 0.7f);
                c.a = 1f; px[y * W + x] = c;
            }
        t.SetPixels32(px); t.Apply(true, true); return t;
    }
    static Material Label(string key, Color paper, Color ink, Color band, int motif)
    {
        if (mats.TryGetValue(key, out Material m) && m != null) return m;
        m = Solid(key, Color.white, 0.18f, 0f, LabelTex(key + "_tex", paper, ink, band, motif)); return m;
    }

    // ---------------------------------------------------------------- modelos
    // botella de 33 cl: 25 cm de alto, 6,6 cm de diámetro
    static readonly Vector2[] BottleOut = {
        new Vector2(0f, 0.006f), new Vector2(0.014f, 0.0048f), new Vector2(0.025f, 0.0022f), new Vector2(0.030f, 0.0005f), new Vector2(0.0325f, 0.003f),
        new Vector2(0.0332f, 0.012f), new Vector2(0.0332f, 0.150f), new Vector2(0.0326f, 0.160f), new Vector2(0.029f, 0.178f), new Vector2(0.021f, 0.196f),
        new Vector2(0.0145f, 0.212f), new Vector2(0.0128f, 0.228f), new Vector2(0.0128f, 0.238f), new Vector2(0.0150f, 0.240f), new Vector2(0.0152f, 0.247f),
        new Vector2(0.0136f, 0.249f), new Vector2(0.0108f, 0.249f) };
    static readonly Vector2[] LiquidProf = {
        new Vector2(0f, 0.0075f), new Vector2(0.0300f, 0.0075f), new Vector2(0.0312f, 0.012f),
        new Vector2(0.0312f, 0.150f), new Vector2(0.0300f, 0.164f), new Vector2(0.0262f, 0.176f), new Vector2(0.0f, 0.176f) };
    static readonly Vector2[] LabelProf = { new Vector2(0.03345f, 0.048f), new Vector2(0.03345f, 0.118f) };
    static readonly Vector2[] CapProf = {
        new Vector2(0f, 0.2525f), new Vector2(0.0150f, 0.2525f), new Vector2(0.0158f, 0.2505f), new Vector2(0.0160f, 0.2440f), new Vector2(0.0156f, 0.2405f), new Vector2(0.0142f, 0.2400f) };
    static readonly Vector2[] CorkProf = {
        new Vector2(0f, 0.2620f), new Vector2(0.0100f, 0.2620f), new Vector2(0.0112f, 0.2600f), new Vector2(0.0112f, 0.2490f), new Vector2(0.0104f, 0.2400f) };

    static Vector2[] Rev(Vector2[] a) { var b = (Vector2[])a.Clone(); System.Array.Reverse(b); return b; }   // perfil de abajo arriba

    static void Bottle(Transform r, ItemType t)
    {
        bool neon = t == ItemType.NeonWater, cashew = t == ItemType.CashewWater;
        string k = t.ToString();
        Color glassTint = neon ? new Color(0.70f, 0.88f, 0.92f, 0.34f) : cashew ? new Color(0.62f, 0.74f, 0.52f, 0.36f) : new Color(0.80f, 0.90f, 0.88f, 0.32f);
        Color liq = neon ? new Color(0.30f, 0.95f, 0.85f, 0.85f) : cashew ? new Color(0.72f, 0.54f, 0.30f, 0.92f) : new Color(0.95f, 0.92f, 0.84f, 0.85f);
        Color emit = neon ? new Color(0.20f, 0.95f, 0.80f) * 0.9f : Color.clear;
        Part(r, "Liquid", Lathe("dl_liquid", LiquidProf, 28), Liquid("dl_liq_" + k, liq, emit), false);
        Part(r, "Glass", Lathe("dl_bottle", BottleOut, 32), Glass("dl_glass_" + k, glassTint), true);
        Material lab = neon ? Label("dl_lab_" + k, new Color(0.10f, 0.09f, 0.14f), new Color(0.35f, 1f, 0.85f), new Color(0.75f, 0.18f, 0.62f), 2)
                    : cashew ? Label("dl_lab_" + k, new Color(0.88f, 0.84f, 0.70f), new Color(0.55f, 0.36f, 0.16f), new Color(0.20f, 0.36f, 0.20f), 1)
                    : Label("dl_lab_" + k, new Color(0.90f, 0.87f, 0.78f), new Color(0.60f, 0.42f, 0.24f), new Color(0.62f, 0.36f, 0.18f), 0);
        Part(r, "Label", Lathe("dl_label", LabelProf, 32), lab, false);
        if (cashew) Part(r, "Cork", Lathe("dl_cork", Rev(CorkProf), 20), Solid("dl_cork", new Color(0.62f, 0.47f, 0.30f), 0.12f, 0f), true);
        else Part(r, "Cap", Lathe("dl_cap", Rev(CapProf), 24), Solid("dl_cap_" + k, neon ? new Color(0.55f, 0.20f, 0.50f) : new Color(0.70f, 0.68f, 0.63f), 0.5f, 0.5f), true);
    }

    // lata de raciones: 10,2 cm de diámetro, 11,8 cm de alto, con nervios, rebordes y una abolladura
    static readonly Vector2[] TinProf = BuildTin();
    static Vector2[] BuildTin()
    {
        var p = new List<Vector2> { new Vector2(0f, 0.004f), new Vector2(0.044f, 0.004f), new Vector2(0.046f, 0.002f), new Vector2(0.0505f, 0.0f), new Vector2(0.0512f, 0.004f), new Vector2(0.0496f, 0.008f) };
        for (int i = 0; i <= 40; i++) { float y = 0.010f + i * (0.098f / 40f); float rib = (i > 3 && i < 37) ? 0.0006f * Mathf.Cos(i * Mathf.PI * 0.5f) : 0f; p.Add(new Vector2(0.0496f + rib, y)); }
        p.Add(new Vector2(0.0512f, 0.112f)); p.Add(new Vector2(0.0505f, 0.1165f)); p.Add(new Vector2(0.046f, 0.1145f)); p.Add(new Vector2(0.044f, 0.1125f));
        p.Add(new Vector2(0.030f, 0.1125f)); p.Add(new Vector2(0.029f, 0.1132f)); p.Add(new Vector2(0.020f, 0.1132f)); p.Add(new Vector2(0.019f, 0.1125f)); p.Add(new Vector2(0f, 0.1125f));
        return p.ToArray();
    }
    static readonly Vector2[] TinLabelProf = { new Vector2(0.0502f, 0.018f), new Vector2(0.0502f, 0.104f) };
    static Texture2D TinTex()
    {
        int W = 64, H = 128; Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "dl_tin_tex", anisoLevel = 2 };
        var px = new Color32[W * H];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = x / (float)W, v = y / (float)H;
                Color c = new Color(0.66f, 0.66f, 0.63f) * (0.9f + 0.1f * Mathf.PerlinNoise(u * 30f, v * 60f));
                float rust = Mathf.PerlinNoise(u * 7f + 2f, v * 5f) * Mathf.PerlinNoise(u * 23f, v * 19f + 5f);
                if (rust > 0.32f) c = Color.Lerp(c, new Color(0.45f, 0.24f, 0.12f), Mathf.Clamp01((rust - 0.32f) * 6f));
                c.a = rust > 0.32f ? 0.25f : 0.75f; px[y * W + x] = c;                              // alfa = suavidad (MetallicGlossMap no: se usa en albedo)
            }
        t.SetPixels32(px); t.Apply(true, true); return t;
    }
    static void Tin(Transform r)
    {
        Mesh m = Lathe("dl_tin", TinProf, 36);
        Mesh dented;
        if (!meshes.TryGetValue("dl_tin_dent", out dented) || dented == null)
        {   // abolladura: hunde una zona del costado
            dented = Object.Instantiate(m); dented.name = "dl_tin_dent";
            Vector3[] vv = dented.vertices; Vector3 c = new Vector3(0.05f, 0.07f, 0.0f);
            for (int i = 0; i < vv.Length; i++) { float d = Vector3.Distance(vv[i], c); if (d < 0.03f) { float k = (1f - d / 0.03f); vv[i] -= new Vector3(vv[i].x, 0f, vv[i].z).normalized * 0.006f * k * k; } }
            dented.vertices = vv; dented.RecalculateNormals(); dented.RecalculateTangents(); dented.RecalculateBounds(); meshes["dl_tin_dent"] = dented;
        }
        Part(r, "Tin", dented, Solid("dl_tin", Color.white, 0.42f, 0.45f, TinTex()), true);   // sin sonda de reflejos: con metal 0,85 se veía negra
        Part(r, "Label", Lathe("dl_tinlabel", TinLabelProf, 36, false, 0f, 1f), Label("dl_lab_rations", new Color(0.80f, 0.70f, 0.46f), new Color(0.50f, 0.12f, 0.10f), new Color(0.32f, 0.10f, 0.08f), 3), false);
    }

    public static bool Build(ItemType t, Transform root)
    {
        if (!Handles(t)) return false;
        Transform r = new GameObject("Visual").transform; r.SetParent(root, false);
        r.localScale = Vector3.one * 1.15f;    // algo por encima del tamaño real para que se vea en la arena (como los demás objetos)
        if (t == ItemType.RoyalRations) Tin(r); else Bottle(r, t);
        if (t == ItemType.NeonWater)
        {
            GameObject lg = new GameObject("NeonGlow"); lg.transform.SetParent(r, false); lg.transform.localPosition = new Vector3(0f, 0.09f, 0f);
            Light l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.3f, 1f, 0.85f); l.range = 1.8f; l.intensity = 0.55f; l.shadows = LightShadows.None;
        }
        Built++;
        return true;
    }
}
