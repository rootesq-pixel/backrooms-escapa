using System.Collections.Generic;
using UnityEngine;

// v7.3.0: soporte de madera con cuatro cascos de buzo de latón (uno por jugador) al final del muelle.
// Mantener clic izquierdo mirándolo para ponerte uno. Cada casco lleva una lámpara (F la enciende/apaga).
public class OceanHelmetRack : MonoBehaviour
{
    public const int Count = 4;
    public OceanWorld world;
    public readonly int[] owner = { -1, -1, -1, -1 };
    private readonly GameObject[] helmets = new GameObject[Count];
    public ReflectionProbe probe;
    // cubemaps de reflejo (una vez por nivel): arriba claro, lados con bandas suaves, abajo oscuro; sólo dependen
    // de la cara y de la coordenada horizontal, así que no importa la orientación vertical de cada cara
    static Cubemap air, water;
    public static Cubemap AirCube(System.Action<Object> track)
    {
        if (air == null) { Color f = RenderSettings.fogColor; Color sd = new Color(Mathf.Clamp(f.r * 1.4f, 0.5f, 0.95f), Mathf.Clamp(f.g * 1.4f, 0.5f, 0.95f), Mathf.Clamp(f.b * 1.4f, 0.5f, 0.95f)); air = EnvCube("HelmetAir", new Color(1.25f, 1.25f, 1.2f), sd, new Color(0.2f, 0.27f, 0.3f), track); }
        return air;
    }
    public static Cubemap WaterCube(System.Action<Object> track)
    {
        if (water == null) water = EnvCube("HelmetWater", new Color(0.22f, 0.38f, 0.42f), new Color(0.09f, 0.17f, 0.19f), new Color(0.03f, 0.04f, 0.05f), track);
        return water;
    }
    static Cubemap EnvCube(string name, Color top, Color side, Color bottom, System.Action<Object> track)
    {
        const int N = 32;
        Cubemap c = new Cubemap(N, SystemInfo.SupportsTextureFormat(TextureFormat.RGBAHalf) ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, true) { name = name };
        Color[] px = new Color[N * N];
        foreach (CubemapFace face in new[] { CubemapFace.PositiveX, CubemapFace.NegativeX, CubemapFace.PositiveY, CubemapFace.NegativeY, CubemapFace.PositiveZ, CubemapFace.NegativeZ })
        {
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    Color col;
                    if (face == CubemapFace.PositiveY) col = top;
                    else if (face == CubemapFace.NegativeY) col = bottom;
                    else
                    {
                        float t = x / (float)(N - 1) + (int)face * 0.37f;
                        float band = 0.85f + 0.3f * Mathf.Pow(Mathf.Abs(Mathf.Sin(t * Mathf.PI * 1.5f)), 3f);
                        col = side * band;
                    }
                    col.a = 1f; px[y * N + x] = col;
                }
            c.SetPixels(px, face);
        }
        c.Apply(true, true);
        track(c);
        return c;
    }
    public int TakenCount { get { int n = 0; foreach (int o in owner) if (o >= 0) n++; return n; } }

    public static OceanHelmetRack Create(OceanWorld w, System.Action<Object> track)
    {
        GameObject g = new GameObject("HelmetRack"); g.transform.SetParent(w.root, false);
        g.transform.position = OceanWorld.HelmetRack;
        OceanHelmetRack r = g.AddComponent<OceanHelmetRack>();
        r.world = w;
        MeshBuilder mb = new MeshBuilder(1);
        OceanGeo.Box(mb, 0, new Vector3(-1.6f, 0f, -0.25f), new Vector3(-1.5f, 1.5f, 0.05f), 0.6f);
        OceanGeo.Box(mb, 0, new Vector3(1.5f, 0f, -0.25f), new Vector3(1.6f, 1.5f, 0.05f), 0.6f);
        OceanGeo.Box(mb, 0, new Vector3(-1.6f, 0.95f, -0.3f), new Vector3(1.6f, 1.02f, 0.1f), 0.6f);
        OceanGeo.Box(mb, 0, new Vector3(-1.6f, 1.45f, -0.05f), new Vector3(1.6f, 1.52f, 0.05f), 0.6f);
        OceanGeo.Emit(mb, "Rack", g.transform, new[] { w.WoodMat }, true, true, track);
        Material brass = BrassMaterial(track);
        Material glass = GlassMaterial(track);
        for (int i = 0; i < Count; i++)
        {
            GameObject h = BuildHelmet(g.transform, brass, glass, track);
            h.transform.localPosition = new Vector3(-1.15f + i * 0.77f, 1.02f, -0.1f);
            h.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            r.helmets[i] = h;
        }
        // sonda de reflejos del muelle con un cubemap propio (cielo con niebla, horizonte y mar): funciona en todas las calidades
        // y en compilaciones (sin cielo, el reflejo por defecto es negro y el latón metálico se veía negro)
        ReflectionProbe rp = new GameObject("HelmetProbe").AddComponent<ReflectionProbe>();
        rp.transform.SetParent(g.transform, false); rp.transform.localPosition = new Vector3(0f, 1.4f, 1.2f);
        rp.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom; rp.customBakedTexture = AirCube(track);
        rp.boxProjection = false; rp.importance = 2; rp.intensity = 1f; rp.size = new Vector3(40f, 24f, 40f);
        r.probe = rp;
        return r;
    }

    // casco de buzo clásico: esfera de latón, ojo de buey con rejilla, collar y la lámpara encima
    // v7.3.0 (cascos realistas): latón envejecido con mapas propios (Resources/Ocean/Helmet: albedo, normales y máscara
    // metal/oclusión/suavidad), cardenillo en las hendiduras y aristas pulidas; refleja la sonda del muelle.
    public static Material BrassMaterial(System.Action<Object> track)
    {
        Texture2D a = Resources.Load<Texture2D>("Ocean/Helmet/HelmetBrass_a");
        Material m = ProceduralAssets.CreateMaterial(a != null ? Color.white : new Color(0.78f, 0.58f, 0.3f), a, Vector2.one, 0.55f); track(m);
        Texture2D n = Resources.Load<Texture2D>("Ocean/Helmet/HelmetBrass_n");
        Texture2D k = Resources.Load<Texture2D>("Ocean/Helmet/HelmetBrass_m");
        if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
        if (k != null && m.HasProperty("_MetallicGlossMap"))
        {
            m.SetTexture("_MetallicGlossMap", k); m.SetFloat("_GlossMapScale", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f); m.EnableKeyword("_METALLICGLOSSMAP");
            m.SetTexture("_OcclusionMap", k); m.SetFloat("_OcclusionStrength", 1f);
        }
        else if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.85f);
        return m;
    }
    // vidrio de los ojos de buey: transparente (premultiplicado), casi negro y muy liso: deja ver el fondo oscuro y refleja
    public static Material GlassMaterial(System.Action<Object> track)
    {
        Material m = ProceduralAssets.CreateMaterial(new Color(0.42f, 0.55f, 0.56f, 0.18f), null, Vector2.one, 0.97f); track(m);
        if (m.HasProperty("_Mode"))
        {
            m.SetFloat("_Mode", 3f); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON"); m.DisableKeyword("_ALPHABLEND_ON"); m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return m;
    }

    // ---- malla del casco: torno con vértices compartidos (normales suaves), u = ángulo/2π (u = 0 delante, +z), v por tramos:
    //      0..0,30 peto, 0,30..0,40 collar, 0,40..1 cúpula (coinciden con las texturas)
    static readonly float[] ProfY = { -0.012f, -0.012f, -0.004f, 0.004f, 0.03f, 0.06f, 0.085f, 0.10f, 0.103f, 0.106f, 0.135f, 0.138f };
    static readonly float[] ProfR = { 0.246f, 0.262f, 0.268f, 0.266f, 0.252f, 0.232f, 0.212f, 0.198f, 0.200f, 0.207f, 0.207f, 0.199f };
    static readonly float[] ProfV = { 0.000f, 0.012f, 0.025f, 0.040f, 0.10f, 0.17f, 0.24f, 0.29f, 0.30f, 0.31f, 0.39f, 0.40f };
    const int Sides = 56, DomeSteps = 30;
    const float DomeA0 = -0.30f, DomeR = 0.2f, DomeCY = 0.2f;

    static Vector3 Around(float u, float r, float y) { float t = u * Mathf.PI * 2f; return new Vector3(Mathf.Sin(t) * r, y, Mathf.Cos(t) * r); }

    static void LatheShared(List<Vector3> V, List<Vector2> UV, List<int> T, List<Vector2> prof, List<float> vs, bool inward, float vOverride)
    {
        int ring = Sides + 1, b = V.Count;
        for (int j = 0; j < prof.Count; j++)
            for (int i = 0; i <= Sides; i++)
            {
                float u = i / (float)Sides;
                V.Add(Around(u, prof[j].y, prof[j].x)); UV.Add(new Vector2(u, vOverride >= 0f ? vOverride : vs[j]));
            }
        for (int j = 0; j < prof.Count - 1; j++)
            for (int i = 0; i < Sides; i++)
            {
                int i0 = b + j * ring + i, i1 = i0 + 1, i2 = i0 + ring + 1, i3 = i0 + ring;
                // (i0, i2, i3) mira hacia fuera (Unity: cara delantera = producto vectorial hacia el observador)
                if (inward) { Tri(T, V, i0, i3, i2); Tri(T, V, i0, i2, i1); } else { Tri(T, V, i0, i2, i3); Tri(T, V, i0, i1, i2); }
            }
    }
    static void Tri(List<int> T, List<Vector3> V, int a, int b, int c)
    {
        if (Vector3.Cross(V[b] - V[a], V[c] - V[a]).sqrMagnitude < 1e-14f) return;
        T.Add(a); T.Add(b); T.Add(c);
    }
    // tubo (anillo o barra) con UV en la banda pulida del collar
    static void Tube(List<Vector3> V, List<Vector2> UV, List<int> T, Vector3[] path, bool closed, float rad, int segs)
    {
        int n = path.Length, b = V.Count;
        for (int k = 0; k < n; k++)
        {
            Vector3 fwd = closed ? (path[(k + 1) % n] - path[(k - 1 + n) % n]) : (path[Mathf.Min(k + 1, n - 1)] - path[Mathf.Max(k - 1, 0)]);
            fwd.Normalize();
            Vector3 side = Vector3.Cross(fwd, Mathf.Abs(Vector3.Dot(fwd, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up).normalized, up2 = Vector3.Cross(side, fwd);
            for (int s = 0; s <= segs; s++)
            {
                float a = s * Mathf.PI * 2f / segs;
                V.Add(path[k] + (side * Mathf.Cos(a) + up2 * Mathf.Sin(a)) * rad);
                UV.Add(new Vector2(k / (float)(n - 1) * 0.25f + s / (float)segs * 0.02f, 0.30f + 0.1f * s / segs));
            }
        }
        int ring = segs + 1, last = closed ? n : n - 1;
        for (int k = 0; k < last; k++)
            for (int s = 0; s < segs; s++)
            {
                int k1 = (k + 1) % n;
                int a0 = b + k * ring + s, a1 = a0 + 1, b0 = b + k1 * ring + s, b1 = b0 + 1;
                Vector3 c = (V[a0] + V[a1] + V[b0] + V[b1]) * 0.25f, ax = (path[k] + path[k1]) * 0.5f;
                Vector3 nrm = Vector3.Cross(V[b0] - V[a0], V[a1] - V[a0]);
                if (Vector3.Dot(nrm, c - ax) >= 0f) { Tri(T, V, a0, b0, a1); Tri(T, V, a1, b0, b1); } else { Tri(T, V, a0, a1, b0); Tri(T, V, a1, b1, b0); }
            }
    }
    static void Disk(List<Vector3> V, List<Vector2> UV, List<int> T, Vector3 c, Vector3 n, float r, float bulge, int segs)
    {
        Vector3 rt = Vector3.Cross(Vector3.up, n); if (rt.sqrMagnitude < 0.01f) rt = Vector3.right; rt.Normalize();
        Vector3 up = Vector3.Cross(n, rt);
        int b = V.Count; V.Add(c + n * bulge); UV.Add(new Vector2(0.5f, 0.5f));
        for (int s = 0; s <= segs; s++)
        {
            float a = s * Mathf.PI * 2f / segs;
            V.Add(c + (rt * Mathf.Cos(a) + up * Mathf.Sin(a)) * r); UV.Add(new Vector2(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
        }
        for (int s = 0; s < segs; s++)
        {
            int a0 = b + 1 + s, a1 = a0 + 1;
            if (Vector3.Dot(Vector3.Cross(V[a0] - V[b], V[a1] - V[b]), n) >= 0f) { T.Add(b); T.Add(a0); T.Add(a1); } else { T.Add(b); T.Add(a1); T.Add(a0); }
        }
    }
    static void HexNut(List<Vector3> V, List<Vector2> UV, List<int> T, Vector3 c, Vector3 outward, float r, float h)
    {
        Vector3 n = outward.normalized;
        Vector3 rt = Vector3.Cross(Vector3.up, n); if (rt.sqrMagnitude < 0.01f) rt = Vector3.right; rt.Normalize();
        Vector3 up = Vector3.Cross(n, rt);
        Vector3[] bot = new Vector3[6], top = new Vector3[6];
        for (int s = 0; s < 6; s++) { float a = s * Mathf.PI / 3f + Mathf.PI / 6f; Vector3 d = rt * Mathf.Cos(a) + up * Mathf.Sin(a); bot[s] = c + d * r; top[s] = c + d * r * 0.92f + n * h; }
        for (int s = 0; s < 6; s++)
        {
            int s1 = (s + 1) % 6, b = V.Count;
            V.Add(bot[s]); V.Add(bot[s1]); V.Add(top[s1]); V.Add(top[s]);
            UV.Add(new Vector2(0.06f, 0.31f)); UV.Add(new Vector2(0.065f, 0.31f)); UV.Add(new Vector2(0.065f, 0.39f)); UV.Add(new Vector2(0.06f, 0.39f));
            Vector3 mid = (bot[s] + bot[s1] + top[s] + top[s1]) * 0.25f;
            if (Vector3.Dot(Vector3.Cross(V[b + 1] - V[b], V[b + 2] - V[b]), mid - (c + n * h * 0.5f)) >= 0f) { T.Add(b); T.Add(b + 1); T.Add(b + 2); T.Add(b); T.Add(b + 2); T.Add(b + 3); }
            else { T.Add(b); T.Add(b + 2); T.Add(b + 1); T.Add(b); T.Add(b + 3); T.Add(b + 2); }
        }
        Disk(V, UV, T, c + n * h, n, r * 0.92f, 0.002f, 6);
    }

    // casco de buzo clásico de latón: peto con pernos, collar atornillado, cúpula con costuras, tres ojos de buey con vidrio y rejilla, lámpara
    public static GameObject BuildHelmet(Transform p, Material brass, Material glass, System.Action<Object> track)
    {
        GameObject h = new GameObject("DivingHelmet"); h.transform.SetParent(p, false);
        List<Vector3> V = new List<Vector3>(); List<Vector2> UV = new List<Vector2>();
        List<int> TB = new List<int>(), TG = new List<int>(), TD = new List<int>();
        // exterior: peto + collar + cúpula
        List<Vector2> prof = new List<Vector2>(); List<float> vs = new List<float>();
        for (int k = 0; k < ProfY.Length; k++) { prof.Add(new Vector2(ProfY[k], ProfR[k])); vs.Add(ProfV[k]); }
        for (int k = 1; k <= DomeSteps; k++)
        {
            float a = Mathf.Lerp(DomeA0, Mathf.PI * 0.5f, k / (float)DomeSteps);
            prof.Add(new Vector2(DomeCY + Mathf.Sin(a) * DomeR, Mathf.Max(0.0005f, Mathf.Cos(a) * DomeR))); vs.Add(Mathf.Lerp(0.40f, 1f, k / (float)DomeSteps));
        }
        LatheShared(V, UV, TB, prof, vs, false, -1f);
        int extRings = prof.Count;
        // interior oscuro (para que por el vidrio se vea fondo y no el cielo)
        List<Vector2> inner = new List<Vector2>(); List<float> iv = new List<float>();
        for (int k = 0; k <= 12; k++) { float a = Mathf.Lerp(-0.9f, Mathf.PI * 0.5f, k / 12f); inner.Add(new Vector2(DomeCY + Mathf.Sin(a) * (DomeR - 0.012f), Mathf.Max(0.0005f, Mathf.Cos(a) * (DomeR - 0.012f)))); iv.Add(0.5f); }
        LatheShared(V, UV, TD, inner, iv, true, 0.5f);
        // ojos de buey: delante (u = 0) y a los lados (u = ±0,25)
        foreach (float pu in new[] { 0f, 0.25f, 0.75f })
        {
            bool front = pu == 0f;
            float pa = Mathf.Asin(0.02f / DomeR);
            Vector3 n = Around(pu, 1f, 0f).normalized; n = (n * Mathf.Cos(pa) + Vector3.up * Mathf.Sin(pa)).normalized;
            Vector3 c = new Vector3(0f, DomeCY, 0f) + n * DomeR;
            float r0 = front ? 0.082f : 0.056f;
            Vector3 rt = Vector3.Cross(Vector3.up, n).normalized, up = Vector3.Cross(n, rt);
            // marco: dos anillos (asiento y aro atornillado) y una virola corta
            for (int ring = 0; ring < 2; ring++)
            {
                Vector3[] path = new Vector3[40];
                float rr = r0 + (ring == 0 ? 0.016f : 0.006f), off = ring == 0 ? 0.004f : 0.022f;
                for (int k = 0; k < 40; k++) { float a = k * Mathf.PI * 2f / 40f; path[k] = c + n * off + (rt * Mathf.Cos(a) + up * Mathf.Sin(a)) * rr; }
                Tube(V, UV, TB, path, true, ring == 0 ? 0.013f : 0.009f, 10);
            }
            {
                Vector3[] path = new Vector3[2] { c - n * 0.01f, c + n * 0.022f };
                List<Vector3> pv = new List<Vector3>(); // virola: cilindro corto de radio r0+0.006
                int b = V.Count;
                for (int k = 0; k < 2; k++) for (int s = 0; s <= 32; s++) { float a = s * Mathf.PI * 2f / 32f; V.Add(path[k] + (rt * Mathf.Cos(a) + up * Mathf.Sin(a)) * (r0 + 0.008f)); UV.Add(new Vector2(s / 32f * 0.25f, 0.31f + 0.08f * k)); }
                for (int s = 0; s < 32; s++) { int a0 = b + s, a1 = a0 + 1, b0 = b + 33 + s, b1 = b0 + 1; Vector3 cc = (V[a0] + V[b1]) * 0.5f; Vector3 nn = Vector3.Cross(V[b0] - V[a0], V[a1] - V[a0]); if (Vector3.Dot(nn, cc - c - n * Vector3.Dot(cc - c, n)) >= 0f) { Tri(TB, V, a0, b0, a1); Tri(TB, V, a1, b0, b1); } else { Tri(TB, V, a0, a1, b0); Tri(TB, V, a1, b1, b0); } }
            }
            // tornillos del aro
            int nb = front ? 8 : 6;
            for (int k = 0; k < nb; k++) { float a = (k + 0.5f) * Mathf.PI * 2f / nb; HexNut(V, UV, TB, c + n * 0.03f + (rt * Mathf.Cos(a) + up * Mathf.Sin(a)) * (r0 + 0.006f), n, 0.0055f, 0.006f); }
            // fondo oscuro y vidrio abombado, hundido en el marco
            Disk(V, UV, TD, c + n * 0.002f, n, r0 + 0.004f, 0f, 32);
            Disk(V, UV, TG, c + n * 0.016f, n, r0 + 0.004f, 0.006f, 32);
            // rejilla delantera (tres barras y su aro)
            if (front)
                for (int k = -1; k <= 1; k++)
                {
                    float x = k * 0.036f, hh = Mathf.Sqrt(Mathf.Max(0f, (r0 + 0.01f) * (r0 + 0.01f) - x * x));
                    Tube(V, UV, TB, new[] { c + n * 0.046f + rt * x - up * hh, c + n * 0.05f + rt * x, c + n * 0.046f + rt * x + up * hh }, false, 0.0055f, 8);
                }
        }
        // pernos del collar (8, coinciden con la textura: u = k/8 + 1/16) y pernos del peto (12)
        for (int k = 0; k < 8; k++) { float uu = k / 8f + 1f / 16f; Vector3 o = Around(uu, 1f, 0f); HexNut(V, UV, TB, Around(uu, 0.207f, 0.1205f), o, 0.0085f, 0.009f); }
        for (int k = 0; k < 12; k++) { float uu = (k + 0.5f) / 12f; Vector3 o = Around(uu, 1f, 0f); float y = 0.083f; float r = 0.2145f; Vector3 nrm = (o * 0.78f + Vector3.up * 0.62f).normalized; HexNut(V, UV, TB, Around(uu, r, y), nrm, 0.0065f, 0.007f); }
        // lámpara en la coronilla: cuerpo, aro y cristal
        {
            Vector3 a0 = new Vector3(0f, 0.385f, 0.03f), a1 = new Vector3(0f, 0.42f, 0.1f), ax = (a1 - a0).normalized;
            Vector3[] body = new Vector3[8]; for (int k = 0; k < 8; k++) body[k] = Vector3.Lerp(a0, a1, k / 7f);
            Tube(V, UV, TB, body, false, 0.032f, 20);
            Vector3[] ring = new Vector3[28]; Vector3 rt = Vector3.Cross(Vector3.up, ax).normalized; if (rt.sqrMagnitude < 0.5f) rt = Vector3.right; Vector3 up = Vector3.Cross(ax, rt);
            for (int k = 0; k < 28; k++) { float a = k * Mathf.PI * 2f / 28f; ring[k] = a1 + (rt * Mathf.Cos(a) + up * Mathf.Sin(a)) * 0.034f; }
            Tube(V, UV, TB, ring, true, 0.007f, 8);
            Disk(V, UV, TG, a1 + ax * 0.002f, ax, 0.03f, 0.004f, 20);
            Disk(V, UV, TB, a0, -ax, 0.032f, 0f, 20);
        }
        Mesh m = new Mesh { name = "DivingHelmetMesh" };
        if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(V); m.SetUVs(0, UV); m.subMeshCount = 3; m.SetTriangles(TB, 0); m.SetTriangles(TG, 1); m.SetTriangles(TD, 2);
        m.RecalculateNormals();
        {   // costura del torno (u = 0 / u = 1): misma normal a ambos lados para que no se vea una raya
            Vector3[] nr = m.normals;
            for (int j = 0; j < extRings; j++) { int i0 = j * (Sides + 1), i1 = i0 + Sides; Vector3 a = (nr[i0] + nr[i1]).normalized; nr[i0] = a; nr[i1] = a; }
            m.normals = nr;
        }
        m.RecalculateTangents(); m.RecalculateBounds(); track(m);
        if (inside == null) { inside = ProceduralAssets.CreateMaterial(new Color(0.015f, 0.017f, 0.018f), null, Vector2.one, 0.2f); }
        GameObject g = new GameObject("Mesh"); g.transform.SetParent(h.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { brass, glass, inside };
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; mr.receiveShadows = true;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.BlendProbesAndSkybox;
        return h;
    }
    static Material inside;   // interior negro mate (compartido, no depende del nivel)

    public void SetOwner(int slot, int id)
    {
        if (slot < 0 || slot >= Count) return;
        owner[slot] = id;
        if (helmets[slot] != null) helmets[slot].SetActive(id < 0);
    }

    public int OwnerSlot(int id) { for (int i = 0; i < Count; i++) if (owner[i] == id) return i; return -1; }
    public int FreeSlot() { for (int i = 0; i < Count; i++) if (owner[i] < 0) return i; return -1; }

    public bool LookingAt(Transform cam, out int slot)
    {
        slot = FreeSlot();
        if (slot < 0 || cam == null) return false;
        Vector3 c = transform.position + Vector3.up * 1.1f;
        Vector3 d = c - cam.position;
        if (d.magnitude > 2.7f) return false;
        return Vector3.Dot(cam.forward, d.normalized) > 0.72f;
    }
}
