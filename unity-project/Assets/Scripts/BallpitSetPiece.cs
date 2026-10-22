using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.1.0 Parque de bolas: sala de juegos fija (Tools/ballpit/build_hall.py) horneada en Blender Cycles: paredes acolchadas con
// mural, suelo de goma EVA, piscinas de bolas hundidas, estructura de juego de dos pisos con redes y toboganes, columnas
// acolchadas, bloques de espuma y fluorescentes. Se coloca una copia en cada bloque de 5x4 celdas (gen.Halls).
public static class BallpitSetPiece
{
    public const string Res = "Ballpit/";
    public const float BakeExposure = 0.62f;
    public const int M_WALL = 0, M_FLOOR = 1, M_CEIL = 2, M_RED = 3, M_YEL = 4, M_BLU = 5, M_GRN = 6, M_PLY = 7, M_PLB = 8, M_NET = 9, M_EMIT = 10, M_BALL = 11, M_METAL = 12, M_ATLAS = 13;
    // tintes lineales de build_hall.py (PAD_TINT, plásticos); el proyecto está en espacio gamma -> se convierten
    public static readonly Color[] PadTintLinear = { new Color(0.62f, 0.16f, 0.13f), new Color(0.80f, 0.58f, 0.16f), new Color(0.15f, 0.30f, 0.58f), new Color(0.20f, 0.44f, 0.22f) };
    public static readonly Color PlasticYLinear = new Color(0.85f, 0.62f, 0.14f), PlasticBLinear = new Color(0.20f, 0.36f, 0.66f);
    public static readonly List<GameObject> Roots = new List<GameObject>();

    [System.Serializable] public class PitJ { public float x0, x1, z0, z1, floor, ball, rim; public float[] irr; public string open; public float oa, ob; }
    [System.Serializable] public class LightJ { public float x, z; public int dead; }
    [System.Serializable] public class HallJ { public float rim, H; public PitJ[] pits; public LightJ[] lights; public float[] party; }

    private static HallJ data;
    public static HallJ Data
    {
        get
        {
            if (data == null)
            {
                TextAsset ta = Resources.Load<TextAsset>(Res + "BallpitHall");
                if (ta != null) data = JsonUtility.FromJson<HallJ>(ta.text);
                if (data == null || data.pits == null || data.pits.Length == 0)   // mismos valores que build_hall.py (PITS, RIM)
                    data = new HallJ { rim = 0.3f, H = 4.6f, pits = new[] {
                        new PitJ { x0 = -6.6f, x1 = 0.4f, z0 = 0.4f, z1 = 7.4f, floor = -0.55f, ball = 0.14f, rim = 0.48f, irr = new[] { 0.5f, 0.5f, 0.5f }, open = "s", oa = -3.6f, ob = -2.4f },
                        new PitJ { x0 = -8.8f, x1 = -5.4f, z0 = -6.8f, z1 = -4.0f, floor = -0.35f, ball = 0.12f, rim = 0.36f, irr = new[] { 0.5f, 0.5f, 0.5f }, open = "e", oa = -5.9f, ob = -4.9f } } };
            }
            return data;
        }
    }

    // atrezo (comprobado con una malla de navegación del tamaño del jugador): junto al mostrador, encima del mostrador, mesa y
    // rincón de la sala de fiestas, bajo la estructura, rincones de la zona infantil y de la sala
    public static readonly Vector3[] PropSpots = {
        new Vector3(2.3f, 0f, -7.5f),   // v7.1.1: antes (1.3, -7.4), encajado entre la pared y un bloque de espuma que se movió al quitar los solapes
        new Vector3(-1.5f, 1.05f, -6.32f), new Vector3(6.75f, 0.6f, -6.4f), new Vector3(9.45f, 0f, -7.45f),
        new Vector3(8.0f, 0.1f, -3.75f), new Vector3(-9.3f, 0f, -7.35f), new Vector3(3.0f, 0f, -3.0f), new Vector3(-8.0f, 0f, -3.5f) };
    // llave enterrada: dentro de una piscina, lejos de los escalones; asoma por la superficie de bolas
    public static Vector3 PitKeySpot(System.Random rng)
    {
        HallJ d = Data;
        PitJ p = d.pits[rng.NextDouble() < 0.7 ? 0 : Mathf.Min(1, d.pits.Length - 1)];
        Rect r = PitInner(p, d.rim);
        for (int t = 0; t < 30; t++)
        {
            float x = Mathf.Lerp(r.xMin + 0.5f, r.xMax - 0.5f, (float)rng.NextDouble()), z = Mathf.Lerp(r.yMin + 0.5f, r.yMax - 0.5f, (float)rng.NextDouble());
            bool nearOpen = p.open == "s" ? (x > p.oa - 0.4f && x < p.ob + 0.4f && z < r.yMin + 1.4f) : (z > p.oa - 0.4f && z < p.ob + 0.4f && x > r.xMax - 1.4f);
            if (!nearOpen) return new Vector3(x, p.ball - 0.01f, z);
        }
        return new Vector3(r.center.x, p.ball - 0.01f, r.center.y);
    }

    private static int bakedState = -1;
    public static bool HasBakedAssets
    {
        get
        {
            if (bakedState < 0)
                bakedState = Resources.Load<TextAsset>(Res + "BallpitHallMesh") != null && Resources.Load<Texture2D>(Res + "Ballpit_LM") != null
                    && Shader.Find("Backrooms/PlayBaked") != null ? 1 : 0;
            return bakedState == 1;
        }
    }

    // interior de la piscina (sin el borde)
    public static Rect PitInner(PitJ p, float rim) { return Rect.MinMaxRect(p.x0 + rim, p.z0 + rim, p.x1 - rim, p.z1 - rim); }

    // altura del suelo pisable (local a la sala): fondo de la piscina dentro de ella, 0 fuera
    public static float FloorY(float lx, float lz)
    {
        HallJ d = Data;
        foreach (PitJ p in d.pits)
            if (PitInner(p, d.rim).Contains(new Vector2(lx, lz))) return p.floor;
        return 0f;
    }
    // dentro de la huella de la piscina (borde incluido)
    public static bool InPitFootprint(float lx, float lz, float margin)
    {
        foreach (PitJ p in Data.pits)
            if (lx > p.x0 - margin && lx < p.x1 + margin && lz > p.z0 - margin && lz < p.z1 + margin) return true;
        return false;
    }


    // Los accesorios pequeños rellenos con una muestra del atlas (platos, tarta, gorros, vasos...) reciben islas de lightmap de
    // pocos texeles (manchas negras/blancas al filtrar). Cada pieza toma la luz cocida de la superficie grande sobre la que descansa.
    public static int FlattenedProps { get; private set; }
    private static bool[] FlattenPropLightmap(Vector3[] v, Vector3[] n, Vector2[] a, Vector2[] b, int[][] subs)
    {
        int[] at = subs[M_ATLAS];
        int nv = v.Length;
        int[] par = new int[nv]; for (int i = 0; i < nv; i++) par[i] = i;
        System.Func<int, int> find = null; find = x => { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; };
        bool[] fill = new bool[nv];
        for (int t = 0; t + 2 < at.Length; t += 3)
        {
            int i0 = at[t], i1 = at[t + 1], i2 = at[t + 2];
            if (a[i0] != a[i1] || a[i0] != a[i2]) continue;
            fill[i0] = fill[i1] = fill[i2] = true;
            int r0 = find(i0); par[find(i1)] = r0; par[find(i2)] = r0;
        }
        var comps = new Dictionary<int, List<int>>();
        for (int i = 0; i < nv; i++) if (fill[i]) { int r = find(i); if (!comps.TryGetValue(r, out var l)) comps[r] = l = new List<int>(); l.Add(i); }
        // superficies grandes casi horizontales hacia arriba (sin bolas, tubos ni atlas)
        var cand = new List<int>();
        for (int k = 0; k < subs.Length; k++)
        {
            if (k == M_BALL || k == M_EMIT || k == M_NET) continue;
            int[] s = subs[k];
            for (int t = 0; t + 2 < s.Length; t += 3)
            {
                if (k == M_ATLAS && fill[s[t]] && a[s[t]] == a[s[t + 1]] && a[s[t]] == a[s[t + 2]]) continue;
                Vector3 p0 = v[s[t]], p1 = v[s[t + 1]], p2 = v[s[t + 2]];
                Vector3 cr = Vector3.Cross(p1 - p0, p2 - p0);
                if (cr.magnitude * 0.5f < 0.02f || cr.normalized.y < 0.7f) continue;
                cand.Add(k); cand.Add(t);
            }
        }
        int done = 0;
        foreach (var kv in comps)
        {
            Vector3 mn = Vector3.positiveInfinity, mx = Vector3.negativeInfinity;
            foreach (int i in kv.Value) { mn = Vector3.Min(mn, v[i]); mx = Vector3.Max(mx, v[i]); }
            Vector3 c = (mn + mx) * 0.5f;
            float best = float.MaxValue; Vector2 uv = Vector2.zero; bool ok = false;
            for (int q = 0; q < cand.Count; q += 2)
            {
                int[] s = subs[cand[q]]; int t = cand[q + 1];
                Vector3 cc = (v[s[t]] + v[s[t + 1]] + v[s[t + 2]]) / 3f;
                if (cc.y > mn.y + 0.05f || cc.y < mn.y - 3f) continue;
                float dh = new Vector2(cc.x - c.x, cc.z - c.z).sqrMagnitude, score = dh + 0.05f * (mn.y - cc.y) * (mn.y - cc.y);
                if (score < best) { best = score; uv = (b[s[t]] + b[s[t + 1]] + b[s[t + 2]]) / 3f; ok = true; }
            }
            // mejor: un punto del soporte justo al lado de la pieza (no debajo: ahí la oclusión lo oscurece)
            float ex = (mx.x - mn.x) * 0.5f + 0.07f, ez = (mx.z - mn.z) * 0.5f + 0.07f;
            Vector3[] offs = { new Vector3(ex, 0, 0), new Vector3(-ex, 0, 0), new Vector3(0, 0, ez), new Vector3(0, 0, -ez) };
            float supY = float.NegativeInfinity; Vector2 uvS = Vector2.zero;
            foreach (Vector3 o in offs)
            {
                Vector3 pt = c + o;
                for (int q = 0; q < cand.Count; q += 2)
                {
                    int[] s = subs[cand[q]]; int t = cand[q + 1];
                    Vector3 p0 = v[s[t]], p1 = v[s[t + 1]], p2 = v[s[t + 2]];
                    float cy = (p0.y + p1.y + p2.y) / 3f;
                    if (cy > mn.y + 0.05f || cy < mn.y - 3f || cy <= supY) continue;
                    float d = (p1.z - p2.z) * (p0.x - p2.x) + (p2.x - p1.x) * (p0.z - p2.z);
                    if (Mathf.Abs(d) < 1e-8f) continue;
                    float w0 = ((p1.z - p2.z) * (pt.x - p2.x) + (p2.x - p1.x) * (pt.z - p2.z)) / d;
                    float w1 = ((p2.z - p0.z) * (pt.x - p2.x) + (p0.x - p2.x) * (pt.z - p2.z)) / d, w2 = 1f - w0 - w1;
                    const float e = 0.03f;
                    if (w0 > e && w1 > e && w2 > e) { supY = cy; uvS = b[s[t]] * w0 + b[s[t + 1]] * w1 + b[s[t + 2]] * w2; }
                }
                if (supY > mn.y - 0.15f) break;   // soporte a la altura de la pieza: vale
            }
            if (supY > float.NegativeInfinity) { uv = uvS; ok = true; }
            // UV0: una sola muestra exacta deja derivadas nulas (algunos drivers devuelven NaN al elegir el mip): leve proyección
            // dentro de la muestra de color
            foreach (int i in kv.Value)
            {
                Vector3 d = v[i] - c;
                a[i] += new Vector2(Mathf.Clamp((d.x + d.z) * 0.04f, -0.009f, 0.009f), Mathf.Clamp(d.y * 0.08f + (d.x - d.z) * 0.02f, -0.02f, 0.02f));
            }
            if (!ok) continue;
            foreach (int i in kv.Value)
            {   // misma luz para toda la pieza, con una variación mínima (sin derivadas nulas al muestrear el lightmap)
                Vector3 d = v[i] - c;
                b[i] = uv + new Vector2(Mathf.Clamp((d.x + d.z) * 0.002f, -0.0007f, 0.0007f), Mathf.Clamp((d.y + d.x - d.z) * 0.002f, -0.0007f, 0.0007f));
            }
            done++;
        }
        FlattenedProps = done;
        return fill;
    }
    public static int FixedTangents { get; private set; }
    public static int OpenedPitMouths { get; private set; }
    struct MV { public Vector3 v, n; public Vector2 a, b; }
    static MV LerpMV(MV p, MV q, float t) { return new MV { v = Vector3.Lerp(p.v, q.v, t), n = Vector3.Lerp(p.n, q.n, t), a = Vector2.Lerp(p.a, q.a, t), b = Vector2.Lerp(p.b, q.b, t) }; }
    // recorta un polígono por el semiplano coord(axis) <= lim (keepBelow) o >= lim
    static List<MV> ClipHalf(List<MV> poly, int axis, float lim, bool keepBelow)
    {
        List<MV> o = new List<MV>();
        for (int i = 0; i < poly.Count; i++)
        {
            MV p = poly[i], q = poly[(i + 1) % poly.Count];
            float dp = keepBelow ? lim - p.v[axis] : p.v[axis] - lim, dq = keepBelow ? lim - q.v[axis] : q.v[axis] - lim;
            if (dp >= 0f) o.Add(p);
            if ((dp >= 0f) != (dq >= 0f)) o.Add(LerpMV(p, q, dp / (dp - dq)));
        }
        return o;
    }
    static int OpenPitMouths(ref Vector3[] v, ref Vector3[] n, ref Vector2[] a, ref Vector2[] b, int[][] subs)
    {
        HallJ d = Data; if (d == null || d.pits == null) return 0;
        List<Vector3> V = new List<Vector3>(v), N = new List<Vector3>(n); List<Vector2> A = new List<Vector2>(a), B = new List<Vector2>(b);
        int cut = 0;
        foreach (PitJ p in d.pits)
        {
            if (p.open != "s" && p.open != "n" && p.open != "e" && p.open != "w") continue;
            bool sn = p.open == "s" || p.open == "n";
            int planeAxis = sn ? 2 : 0, runAxis = sn ? 0 : 2;
            float plane = p.open == "s" ? p.z0 + d.rim : p.open == "n" ? p.z1 - d.rim : p.open == "e" ? p.x1 - d.rim : p.x0 + d.rim;
            for (int k = 0; k < subs.Length; k++)
            {
                List<int> outT = new List<int>(subs[k].Length + 12); bool changed = false;
                int[] s = subs[k];
                for (int t = 0; t + 2 < s.Length; t += 3)
                {
                    int i0 = s[t], i1 = s[t + 1], i2 = s[t + 2];
                    Vector3 p0 = V[i0], p1 = V[i1], p2 = V[i2];
                    bool onPlane = Mathf.Abs(p0[planeAxis] - plane) < 0.005f && Mathf.Abs(p1[planeAxis] - plane) < 0.005f && Mathf.Abs(p2[planeAxis] - plane) < 0.005f;
                    float ylo = Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y)), yhi = Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y));
                    float rlo = Mathf.Min(p0[runAxis], Mathf.Min(p1[runAxis], p2[runAxis])), rhi = Mathf.Max(p0[runAxis], Mathf.Max(p1[runAxis], p2[runAxis]));
                    bool wall = onPlane && Mathf.Abs(N[i0][planeAxis]) > 0.99f && ylo < p.floor + 0.02f && yhi > p.rim - 0.02f && rlo < p.oa - 0.01f && rhi > p.ob + 0.01f;
                    if (!wall) { outT.Add(i0); outT.Add(i1); outT.Add(i2); continue; }
                    changed = true; cut++;
                    List<MV> tri = new List<MV> { new MV { v = p0, n = N[i0], a = A[i0], b = B[i0] }, new MV { v = p1, n = N[i1], a = A[i1], b = B[i1] }, new MV { v = p2, n = N[i2], a = A[i2], b = B[i2] } };
                    foreach (List<MV> piece in new[] { ClipHalf(tri, runAxis, p.oa, true), ClipHalf(tri, runAxis, p.ob, false) })
                    {
                        if (piece.Count < 3) continue;
                        int baseI = V.Count;
                        foreach (MV m in piece) { V.Add(m.v); N.Add(m.n); A.Add(m.a); B.Add(m.b); }
                        for (int j = 1; j + 1 < piece.Count; j++) { outT.Add(baseI); outT.Add(baseI + j); outT.Add(baseI + j + 1); }
                    }
                }
                if (changed) subs[k] = outT.ToArray();
            }
        }
        if (cut > 0) { v = V.ToArray(); n = N.ToArray(); a = A.ToArray(); b = B.ToArray(); }
        return cut;
    }
    public const float BackingDrop = 0.30f;   // v7.1.1 (antes 0.085): capa plana a 33 cm bajo la superficie de bolas
    public static Mesh LoadBakedMesh(out Mesh collision)
    {
        collision = null;
        TextAsset ta = Resources.Load<TextAsset>(Res + "BallpitHallMesh");
        if (ta == null) return null;
        using (var br = new System.IO.BinaryReader(new System.IO.MemoryStream(ta.bytes)))
        {
            int nv = br.ReadInt32(), ns = br.ReadInt32();
            Vector3[] v = new Vector3[nv], n = new Vector3[nv]; Vector2[] a = new Vector2[nv], b = new Vector2[nv];
            for (int i = 0; i < nv; i++)
            {
                v[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                n[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                a[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                b[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            }
            int[][] subs = new int[ns][];
            for (int k = 0; k < ns; k++)
            {
                int c = br.ReadInt32(); subs[k] = new int[c];
                for (int i = 0; i < c; i++) subs[k][i] = br.ReadInt32();
            }
            // v7.1.0: la pared interior de cada piscina se exportó entera, también delante de la abertura con escalones:
            // se recorta ahí (render y colisión) para poder entrar y salir andando
            OpenedPitMouths = OpenPitMouths(ref v, ref n, ref a, ref b, subs); nv = v.Length;
            // la superficie plana de bolas baja: v7.1.1 queda como fondo oscuro muy por debajo de las 3 capas de bolas (nunca se ve como «foto»)
            bool[] fillV = M_ATLAS < ns ? FlattenPropLightmap(v, n, a, b, subs) : null;
            if (M_BALL < ns) { var moved = new HashSet<int>(subs[M_BALL]); foreach (int i in moved) v[i].y -= BackingDrop; }
            Mesh m = new Mesh { name = "BallpitHall" };
            if (nv > 65000) m.indexFormat = IndexFormat.UInt32;
            m.vertices = v; m.normals = n; m.uv = a; m.uv2 = b; m.subMeshCount = ns;
            for (int k = 0; k < ns; k++) m.SetTriangles(subs[k], k);
            m.RecalculateBounds(); m.RecalculateTangents();
            // caras del atlas rellenas con una sola muestra (UV degenerada): tangente NaN -> negro con la linterna. Se repara.
            Vector4[] tg = m.tangents; int fixedT = 0;
            for (int i = 0; i < tg.Length; i++)
            {
                Vector3 nn = n[i].normalized;
                Vector3 t3 = new Vector3(tg[i].x, tg[i].y, tg[i].z);
                bool bad = float.IsNaN(t3.x) || float.IsNaN(t3.y) || float.IsNaN(t3.z) || float.IsInfinity(t3.x) || float.IsInfinity(t3.y) || float.IsInfinity(t3.z) || (fillV != null && fillV[i]);
                if (!bad) { t3 -= nn * Vector3.Dot(nn, t3); bad = t3.sqrMagnitude < 1e-6f; }
                if (bad)
                {
                    Vector3 a0 = Mathf.Abs(nn.y) < 0.9f ? Vector3.up : Vector3.right;
                    t3 = Vector3.Cross(a0, nn); fixedT++;
                }
                t3.Normalize();
                float w = tg[i].w < 0f ? -1f : 1f;
                tg[i] = new Vector4(t3.x, t3.y, t3.z, w);
            }
            m.tangents = tg;
            FixedTangents = fixedT;
            // colisión: todo menos superficie de bolas y tubos emisivos
            List<int> col = new List<int>();
            for (int k = 0; k < ns; k++) if (k != M_BALL && k != M_EMIT) col.AddRange(subs[k]);
            collision = new Mesh { name = "BallpitHallCol" };
            if (nv > 65000) collision.indexFormat = IndexFormat.UInt32;
            collision.vertices = v; collision.SetTriangles(col, 0); collision.RecalculateBounds();
            return m;
        }
    }

    static Material Mat(string tex, string texDir, Texture lm, Color tintLinear, float smooth, float cutoff = 0f, bool twoSided = false)
    {
        Material m = new Material(Shader.Find("Backrooms/PlayBaked")) { name = "PlayBaked_" + tex };
        if (tex != null)
        {
            m.SetTexture("_MainTex", Resources.Load<Texture2D>(texDir + tex + "_a"));
            Texture2D nm = Resources.Load<Texture2D>(texDir + tex + "_n"); if (nm != null) m.SetTexture("_BumpMap", nm);
            Texture2D mk = Resources.Load<Texture2D>(texDir + tex + "_m"); if (mk != null) m.SetTexture("_MaskMap", mk); else m.SetFloat("_MetalScale", 0f);
        }
        else
        {
            m.SetFloat("_MetalScale", 0f);
        }
        m.SetTexture("_LightMap", lm); m.SetFloat("_LMScale", BakeExposure); m.SetFloat("_Glossiness", smooth);
        m.SetColor("_Color", tintLinear.gamma); m.SetFloat("_Cutoff", cutoff); m.SetFloat("_Cull", twoSided ? 0f : 2f);
        return m;
    }

    private static Material[] sharedMats; private static Mesh sharedMesh, sharedCol; private static Texture sharedLm; private static int sharedLmTier = -1;
    // v7.1.1: lightmap de 4096 horneado en GPU; con gráficos Bajo se usa la copia reducida a 2048 (Ballpit_LM_Low, 4x menos memoria)
    public static Texture2D LoadLightmap(string full, string low)
    {
        Texture2D t = GameSettings.Quality == 0 ? Resources.Load<Texture2D>(low) : null;
        return t != null ? t : Resources.Load<Texture2D>(full);
    }
    public static int LightmapSize { get { return sharedLm != null ? sharedLm.width : 0; } }
    static void EnsureShared()
    {
        int tier = GameSettings.Quality == 0 ? 0 : 1;
        if (sharedMats != null && sharedMesh != null)
        {
            if (tier != sharedLmTier)
            {   // cambió la calidad entre partidas: cambia solo el lightmap de los materiales
                sharedLm = LoadLightmap(Res + "Ballpit_LM", Res + "Ballpit_LM_Low"); sharedLmTier = tier;
                foreach (Material mm in sharedMats) if (mm != null && mm.HasProperty("_LightMap") && mm.GetTexture("_LightMap") != Texture2D.blackTexture) mm.SetTexture("_LightMap", sharedLm);
            }
            return;
        }
        sharedLm = LoadLightmap(Res + "Ballpit_LM", Res + "Ballpit_LM_Low"); sharedLmTier = tier;
        const string T = "Textures/", TB = "Textures/Ballpit/";
        Material emit = Mat(null, null, Texture2D.blackTexture, new Color(0.95f, 0.97f, 1f), 0.2f);
        emit.SetColor("_EmitAdd", new Color(2.3f, 2.35f, 2.4f)); emit.SetFloat("_LMScale", 0f);
        Material metal = Mat(null, null, sharedLm, new Color(0.55f, 0.56f, 0.57f), 0.6f); metal.SetFloat("_MetalScale", 0.6f);
        sharedMats = new Material[] {
            Mat("PlayWall", T, sharedLm, Color.white, 0.25f),
            Mat("PlayFloor", T, sharedLm, Color.white, 0.35f),
            Mat("CeilingBeige", T, sharedLm, new Color(0.92f, 0.92f, 0.95f), 0.1f),
            Mat("PadVinyl", TB, sharedLm, PadTintLinear[0], 0.55f), Mat("PadVinyl", TB, sharedLm, PadTintLinear[1], 0.55f),
            Mat("PadVinyl", TB, sharedLm, PadTintLinear[2], 0.55f), Mat("PadVinyl", TB, sharedLm, PadTintLinear[3], 0.55f),
            Mat("PlayPlastic", TB, sharedLm, PlasticYLinear, 0.65f), Mat("PlayPlastic", TB, sharedLm, PlasticBLinear, 0.65f),
            Mat("PlayNet", TB, sharedLm, Color.white, 0.1f, 0.45f, true),
            emit,
            Mat(null, null, sharedLm, new Color(0.09f, 0.08f, 0.08f), 0.05f),   // v7.1.2: fondo oscuro liso, sin la textura de bolas (antes «BallSurface»: parecía una foto/moqueta)
            metal,
            Mat("PartyAtlas", TB, sharedLm, Color.white, 0.15f, 0.5f, true) };
        sharedMesh = LoadBakedMesh(out sharedCol);
    }

    // La NavMesh se hornea con la malla renderizada: durante el horneado las salas usan la malla de colisión (sin la superficie
    // plana de bolas), así los monstruos caminan por el fondo de las piscinas y no por encima de las bolas.
    public static void BeginNavBuild()
    {
        if (sharedCol == null) return;
        foreach (GameObject r in Roots) if (r != null) foreach (MeshFilter mf in r.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh == sharedMesh) mf.sharedMesh = sharedCol;
    }
    public static void EndNavBuild()
    {
        if (sharedMesh == null) return;
        foreach (GameObject r in Roots) if (r != null) foreach (MeshFilter mf in r.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh == sharedCol) mf.sharedMesh = sharedMesh;
    }

    public static void Build(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot)
    {
        Roots.RemoveAll(r => r == null);
        EnsureShared();
        if (sharedMesh == null) return;
        float wh = cfg.wallHeight;
        for (int k = 0; k < gen.Halls.Count; k++)
        {
            RectInt b = gen.Halls[k];
            Vector3 ctr = gen.HallCenterLocal(k);
            GameObject root = new GameObject("BallpitHall" + k);
            root.transform.SetParent(levelRoot, false);
            root.transform.localPosition = ctr;
            Roots.Add(root);
            GameObject g = new GameObject("BallpitHallBaked");
            g.transform.SetParent(root.transform, false);
            g.AddComponent<MeshFilter>().sharedMesh = sharedMesh;
            MeshRenderer mr = g.AddComponent<MeshRenderer>();
            Material[] mats = new Material[sharedMesh.subMeshCount];
            for (int i = 0; i < mats.Length; i++) mats[i] = sharedMats[Mathf.Min(i, sharedMats.Length - 1)];
            mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            g.AddComponent<MeshCollider>().sharedMesh = sharedCol;

            HallFlicker.Attach(root, mr, Data, gen.UsedSeed * 7 + k * 101);
            BallPitField field = root.AddComponent<BallPitField>();
            field.Init(Data, 1000 + 37 * k + gen.UsedSeed);

            PoolRoomProbes probes = root.AddComponent<PoolRoomProbes>();
            float H = Data.H > 0 ? Data.H : 4.6f;
            probes.Init(levelRoot, ctr, new Bounds(levelRoot.TransformPoint(ctr + new Vector3(0f, H * 0.5f, 0f)), new Vector3(b.width * cfg.cellSize, H + 2f, b.height * cfg.cellSize)),
                Res + "BallpitProbes", BakeExposure);
        }
    }
}

// Miles de bolas por piscina, dibujadas con DrawMeshInstanced por baldosas de 1,2 m (color por instancia), LOD por distancia
// y 3 capas (Bajo: 1). Vadear: menos velocidad, ruido de plástico y bolas apartadas alrededor de las piernas.
public class BallPitField : MonoBehaviour
{
    public const float BallR = 0.037f, Spacing = 0.066f, Tile = 1.2f;   // v7.1.1: 0.066 (antes 0.071): 2 capas tapan el fondo
    static readonly Color[] BallColors = {
        new Color(0.78f, 0.17f, 0.14f), new Color(0.90f, 0.72f, 0.18f), new Color(0.16f, 0.38f, 0.74f),
        new Color(0.22f, 0.60f, 0.28f), new Color(0.88f, 0.42f, 0.12f), new Color(0.55f, 0.28f, 0.66f) };
    class TileData { public Matrix4x4[] m; public int[] layerEnd = new int[3]; public Bounds b; public MaterialPropertyBlock mpb; public int pit; }
    readonly List<TileData> tiles = new List<TileData>();
    public BallpitSetPiece.PitJ[] pits; public float rim;
    public int BallCount { get; private set; }
    public static readonly List<BallPitField> All = new List<BallPitField>();
    static Mesh lod0, lod1; static Material mat;
    public static Material SharedMaterial { get { return mat; } }
    public static bool MaterialFromKeeper { get; private set; }
    static readonly int PushersId = Shader.PropertyToID("_BallPushers");
    static readonly Vector4[] pushers = new Vector4[6];
    static int pusherFrame = -1, lowFrame = -1; static bool lowQ;
    readonly Matrix4x4[] scratch = new Matrix4x4[1023];
    readonly Plane[] planes = new Plane[6];

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public void Init(BallpitSetPiece.HallJ d, int seed)
    {
        pits = d.pits; rim = d.rim;
        if (lod0 == null) { lod0 = Ico(1); lod1 = Ico(0); }
        if (mat == null)
        {
            // v7.1.2: el material sale de un .mat de Resources con instancing activado. Antes se creaba solo en runtime y el build
            // (GraphicsSettings: Instancing Variants = Strip Unused) eliminaba la variante INSTANCING_ON: en el ejecutable no se
            // dibujaba ninguna bola y se veía el fondo plano. Con el .mat referenciado la variante va siempre en el build.
            Material keep = Resources.Load<Material>("Ballpit/BallInstancedKeeper");
            Shader sh = Shader.Find("Backrooms/BallInstanced");
            mat = keep != null ? new Material(keep) : new Material(sh != null ? sh : Shader.Find("Standard"));
            mat.name = "BallInstanced"; mat.enableInstancing = true; MaterialFromKeeper = keep != null;
        }
        System.Random rnd = new System.Random(seed);
        Transform tr = transform;
        BallCount = 0;
        for (int pi = 0; pi < pits.Length; pi++)
        {
            var p = pits[pi];
            Rect inner = BallpitSetPiece.PitInner(p, rim);
            Color irr = p.irr != null && p.irr.Length >= 3 ? new Color(p.irr[0], p.irr[1], p.irr[2]) * BallpitSetPiece.BakeExposure : Color.gray;
            int tx = Mathf.CeilToInt(inner.width / Tile), tz = Mathf.CeilToInt(inner.height / Tile);
            for (int a = 0; a < tx; a++)
                for (int c = 0; c < tz; c++)
                {
                    Rect tr2 = Rect.MinMaxRect(inner.xMin + a * Tile, inner.yMin + c * Tile, Mathf.Min(inner.xMax, inner.xMin + (a + 1) * Tile), Mathf.Min(inner.yMax, inner.yMin + (c + 1) * Tile));
                    List<Matrix4x4> ms = new List<Matrix4x4>(); List<Vector4> cols = new List<Vector4>();
                    TileData td = new TileData { pit = pi };
                    for (int layer = 0; layer < 3; layer++)
                    {   // v7.1.1: capas 0 y 1 completas (tapan todo desde arriba), la 2 al 30 % (solo de cerca, para el hueco al vadear)
                        float off = layer == 1 ? Spacing * 0.5f : 0f;
                        float y0 = p.ball - BallR - layer * BallR * 1.55f;
                        // v7.1.1: rejilla continua de toda la piscina (antes cada baldosa empezaba de nuevo y dejaba una franja hueca en sus bordes)
                        float gx0 = inner.xMin + BallR + off, gz0 = inner.yMin + BallR + off;
                        float xs = gx0 + Mathf.Ceil((tr2.xMin - gx0) / Spacing - 1e-4f) * Spacing, zs = gz0 + Mathf.Ceil((tr2.yMin - gz0) / Spacing - 1e-4f) * Spacing;
                        for (float x = xs; x < tr2.xMax - 1e-4f && x < inner.xMax - BallR * 0.3f; x += Spacing)
                            for (float z = zs; z < tr2.yMax - 1e-4f && z < inner.yMax - BallR * 0.3f; z += Spacing)
                            {
                                if (layer == 0 && rnd.NextDouble() < 0.02) continue;   // algún hueco en la superficie (lo tapa la capa 1)
                                if (layer == 2 && rnd.NextDouble() < 0.70) continue;   // 3.ª capa al 30 %
                                float jx = (float)(rnd.NextDouble() - 0.5) * Spacing * 0.42f, jz = (float)(rnd.NextDouble() - 0.5) * Spacing * 0.42f;
                                float jy = (float)(rnd.NextDouble() - 0.6) * BallR * (layer == 0 ? 0.55f : 0.4f);
                                Vector3 lp = new Vector3(Mathf.Clamp(x + jx, inner.xMin + BallR, inner.xMax - BallR), y0 + jy, Mathf.Clamp(z + jz, inner.yMin + BallR, inner.yMax - BallR));
                                Quaternion q = Quaternion.Euler((float)rnd.NextDouble() * 360f, (float)rnd.NextDouble() * 360f, 0f);
                                float s = BallR * 2f * (0.97f + (float)rnd.NextDouble() * 0.05f);
                                ms.Add(tr.localToWorldMatrix * Matrix4x4.TRS(lp, q, Vector3.one * s));
                                double cr = rnd.NextDouble();
                                int ci = cr < 0.24 ? 0 : cr < 0.44 ? 1 : cr < 0.66 ? 2 : cr < 0.82 ? 3 : cr < 0.93 ? 4 : 5;
                                Color bc = BallColors[ci] * (0.86f + (float)rnd.NextDouble() * 0.16f);
                                cols.Add(new Vector4(bc.r, bc.g, bc.b, 1f));
                                if (ms.Count >= 1023) break;
                            }
                        td.layerEnd[layer] = ms.Count;
                    }
                    if (ms.Count == 0) continue;
                    td.m = ms.ToArray();
                    Vector3 wc = tr.TransformPoint(new Vector3(tr2.center.x, p.ball - 0.08f, tr2.center.y));
                    td.b = new Bounds(wc, new Vector3(tr2.width + 0.6f, 0.6f, tr2.height + 0.6f));
                    td.mpb = new MaterialPropertyBlock();
                    td.mpb.SetVectorArray("_Color", cols);
                    td.mpb.SetColor("_Irr", irr);
                    td.mpb.SetFloat("_TopY", tr.TransformPoint(new Vector3(0f, p.ball, 0f)).y);
                    td.mpb.SetFloat("_Dyn", 1f);   // v7.1.1: todas las bolas de la piscina se mecen
                    tiles.Add(td);
                    BallCount += ms.Count;
                }
        }
    }

    // icosfera de radio 0,5 (escala = diámetro)
    static Mesh Ico(int sub)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        List<Vector3> v = new List<Vector3> {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
            new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
        List<int> f = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].normalized;
        for (int s = 0; s < sub; s++)
        {
            var cache = new Dictionary<long, int>(); List<int> nf = new List<int>();
            System.Func<int, int, int> mid = (a, b) =>
            {
                long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                int r; if (cache.TryGetValue(key, out r)) return r;
                v.Add(((v[a] + v[b]) * 0.5f).normalized); cache[key] = v.Count - 1; return v.Count - 1;
            };
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2], ab = mid(a, b), bc = mid(b, c), ca = mid(c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = nf;
        }
        Mesh m = new Mesh { name = "Ball" + sub };
        Vector3[] vv = new Vector3[v.Count]; for (int i = 0; i < v.Count; i++) vv[i] = v[i] * 0.5f;
        m.vertices = vv; m.normals = v.ToArray(); m.triangles = f.ToArray(); m.RecalculateBounds();
        return m;
    }

    // empujadores: jugador local, cuerpos remotos y monstruos (xyz pies, w radio; w < 0 = bulto que levanta las bolas desde abajo)
    static float nextRemoteScan; static readonly List<Transform> remotes = new List<Transform>();
    static void UpdatePushers()
    {
        if (pusherFrame == Time.frameCount) return;
        pusherFrame = Time.frameCount;
        int n = 0;
        for (int i = 0; i < pushers.Length; i++) pushers[i] = Vector4.zero;
        FirstPersonController fpc = FirstPersonController.LocalInstance;
        if (fpc != null) { Vector3 p = fpc.transform.position; pushers[n++] = new Vector4(p.x, p.y, p.z, fpc.IsCrouching ? 0.56f : 0.48f); }
        foreach (MonsterAI m in MonsterAI.Instances)
        {
            if (n >= pushers.Length) break;
            if (m == null || !m.gameObject.activeInHierarchy) continue;
            Vector3 p = m.transform.position;
            if (DepthAt(p + Vector3.up * 1.9f) > 0f && p.y < SurfaceY(p) - 1.2f) pushers[n++] = new Vector4(p.x, SurfaceY(p), p.z, -0.95f);   // sumergido: bulto
            else if (DepthAt(p) > 0f) pushers[n++] = new Vector4(p.x, p.y, p.z, 0.62f);
        }
        if (Time.time >= nextRemoteScan) { nextRemoteScan = Time.time + 1f; remotes.Clear(); foreach (RemotePlayer rp in Object.FindObjectsOfType<RemotePlayer>()) remotes.Add(rp.transform); }
        foreach (Transform t in remotes)
        {
            if (n >= pushers.Length) break;
            if (t == null || !t.gameObject.activeInHierarchy) continue;
            Vector3 p = t.position; if (DepthAt(p) <= 0f) continue;
            pushers[n++] = new Vector4(p.x, p.y, p.z, 0.48f);
        }
        Shader.SetGlobalVectorArray(PushersId, pushers);
        PusherCount = n;
        UpdateRipples(n);
    }

    // v7.1.1: ondas y bolas rodando. Cada empujador que se mueve dentro de una piscina (jugador, compañeros, monstruo; también
    // el bulto del monstruo sumergido) suelta una onda cada ~0,3 s y, si va deprisa, alguna bola que sale rodando.
    public static int PusherCount { get; private set; }
    public static int RippleCount { get { int c = 0; foreach (Vector4 q in ripples) if (q.w > 0f && Time.timeSinceLevelLoad - q.w < 2.6f) c++; return c; } }
    public static int RipplesEmitted { get; private set; }
    static readonly Vector4[] ripples = new Vector4[8]; static int rippleNext;
    static readonly Vector3[] lastP = new Vector3[6]; static readonly float[] lastEmit = new float[6];
    static readonly int RipplesId = Shader.PropertyToID("_BallRipples"), AmbientId = Shader.PropertyToID("_BallAmbient"), RippleNId = Shader.PropertyToID("_BallRippleN");
    static System.Random rollRnd = new System.Random(7111);
    static float lastT = -1f;
    static void UpdateRipples(int n)
    {
        float now = Time.timeSinceLevelLoad;   // = _Time.y del shader
        if (now < lastT) { for (int i = 0; i < ripples.Length; i++) ripples[i] = Vector4.zero; lastT = -1f; }   // nivel recargado
        float dt = lastT < 0f ? 0f : Mathf.Max(1e-4f, now - lastT); lastT = now;
        for (int i = 0; i < pushers.Length; i++)
        {
            Vector4 pw = pushers[i];
            if (i >= n || pw.w == 0f) { lastP[i] = Vector3.zero; continue; }
            Vector3 p = new Vector3(pw.x, pw.y, pw.z);
            Vector3 vel = lastP[i] == Vector3.zero || dt <= 0f ? Vector3.zero : (p - lastP[i]) / dt; vel.y = 0f;
            if (vel.magnitude > 12f) vel = Vector3.zero;   // teletransporte o cambio de hueco
            lastP[i] = p;
            float sp = vel.magnitude;
            bool sub = pw.w < 0f;
            if ((sp > 0.25f || sub) && now - lastEmit[i] > (sub ? 0.55f : Mathf.Lerp(0.42f, 0.22f, Mathf.Clamp01(sp / 3f))))
            {
                lastEmit[i] = now;
                ripples[rippleNext] = new Vector4(p.x, SurfaceY(p), p.z, Mathf.Max(1e-3f, now)); rippleNext = (rippleNext + 1) % (lowQ ? 4 : ripples.Length); RipplesEmitted++;
                if (!sub && sp > 0.6f && !lowQ) { int k = sp > 2f ? 2 : 1; for (int j = 0; j < k; j++) SpawnRoll(p, vel, SurfaceY(p), rollRnd); }
            }
        }
        Shader.SetGlobalVectorArray(RipplesId, ripples);
        Shader.SetGlobalFloat(AmbientId, lowQ ? 0.8f : 1f);
        Shader.SetGlobalFloat(RippleNId, lowQ ? 4f : 8f);   // Bajo: solo las 4 primeras ondas en el shader
    }

    // altura (mundo) de la superficie de bolas sobre p (o p.y si no hay piscina)
    public static float SurfaceY(Vector3 wp)
    {
        foreach (BallPitField f in All)
        {
            if (f == null) continue;
            Vector3 lp = f.transform.InverseTransformPoint(wp);
            foreach (var p in f.pits)
                if (BallpitSetPiece.PitInner(p, f.rim).Contains(new Vector2(lp.x, lp.z))) return f.transform.TransformPoint(new Vector3(0f, p.ball, 0f)).y;
        }
        return wp.y;
    }

    // bolas que saltan (cuando sale el monstruo o alguien se tira): balística simple, dibujadas instanciadas
    struct Flyer { public Vector3 p, v; public float life, floorY; public Color c; public bool roll; public float life0; }
    static readonly List<Flyer> flyers = new List<Flyer>();
    static readonly Matrix4x4[] flyM = new Matrix4x4[256]; static readonly Vector4[] flyC = new Vector4[256];
    static MaterialPropertyBlock flyMpb;
    public static void SpawnBurst(Vector3 at, int count)
    {
        System.Random r = new System.Random(Time.frameCount * 7 + (int)(at.x * 13));
        for (int i = 0; i < count && flyers.Count < 256; i++)
        {
            float a = (float)r.NextDouble() * Mathf.PI * 2f, sp = 1.2f + (float)r.NextDouble() * 2.8f;
            flyers.Add(new Flyer { p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.3f, v = new Vector3(Mathf.Cos(a) * sp * 0.6f, 2.5f + (float)r.NextDouble() * 3.5f, Mathf.Sin(a) * sp * 0.6f),
                life = 3f, floorY = at.y, c = BallColors[r.Next(BallColors.Length)] });
        }
    }
    static int flyFrame = -1;
    static void UpdateFlyers()
    {
        if (flyFrame == Time.frameCount || flyers.Count == 0) return;
        flyFrame = Time.frameCount;
        float dt = Time.deltaTime; int n = 0;
        for (int i = flyers.Count - 1; i >= 0; i--)
        {
            Flyer f = flyers[i];
            f.life -= dt;
            if (f.roll)
            {   // v7.1.1: bola que rueda por encima de las demás y se hunde al pararse
                f.v *= Mathf.Max(0f, 1f - 1.8f * dt); f.p += f.v * dt;
                float bob = Mathf.Abs(Mathf.Sin((f.life0 - f.life) * 9f)) * 0.012f * Mathf.Clamp01(f.v.magnitude * 2f);
                f.p.y = f.floorY + BallR * 0.55f + bob - Mathf.Clamp01((0.6f - f.life) / 0.6f) * 0.09f;
            }
            else
            {
                f.v.y -= 9.81f * dt; f.p += f.v * dt;
                if (f.p.y < f.floorY - 0.02f && f.v.y < 0f) { f.p.y = f.floorY - 0.02f; f.v *= 0.35f; f.v.y = -f.v.y * 0.5f; }
            }
            if (f.life <= 0f) { flyers.RemoveAt(i); continue; }
            flyers[i] = f;
        }
        foreach (Flyer f in flyers)
        {
            Quaternion rq = f.roll ? Quaternion.Euler(f.p.z * 1550f, 0f, -f.p.x * 1550f) : Quaternion.Euler(f.life * 300f, f.life * 170f, 0f);
            flyM[n] = Matrix4x4.TRS(f.p, rq, Vector3.one * BallR * 2f);
            flyC[n] = new Vector4(f.c.r, f.c.g, f.c.b, 1f); n++;
        }
        if (n == 0 || mat == null) return;
        if (flyMpb == null) flyMpb = new MaterialPropertyBlock();
        flyMpb.SetVectorArray("_Color", flyC); flyMpb.SetColor("_Irr", new Color(0.55f, 0.53f, 0.5f)); flyMpb.SetFloat("_TopY", -1000f); flyMpb.SetFloat("_Dyn", 0f);   // sin oscurecer por profundidad ni oleaje (ya se mueven solas)
        Graphics.DrawMeshInstanced(lod0, 0, mat, flyM, n, flyMpb, UnityEngine.Rendering.ShadowCastingMode.Off, false, 0, null, UnityEngine.Rendering.LightProbeUsage.Off);
    }
    public static int FlyingCount { get { return flyers.Count; } }
    public static int RollingCount { get { int c = 0; foreach (Flyer f in flyers) if (f.roll) c++; return c; } }
    // v7.1.1: al vadear, algunas bolas de la superficie salen rodando por delante y a los lados
    public static void SpawnRoll(Vector3 at, Vector3 vel, float surfaceY, System.Random r)
    {
        if (flyers.Count >= 256) return;
        Vector3 side = new Vector3(-vel.z, 0f, vel.x).normalized * ((float)r.NextDouble() - 0.5f) * 0.9f;
        Vector3 v = vel * (0.55f + (float)r.NextDouble() * 0.5f) + side;
        float life = 1.3f + (float)r.NextDouble() * 0.9f;
        flyers.Add(new Flyer { p = new Vector3(at.x, surfaceY, at.z) + vel.normalized * 0.32f, v = new Vector3(v.x, 0f, v.z), life = life, life0 = life,
            floorY = surfaceY, c = BallColors[r.Next(BallColors.Length)] * (0.86f + (float)r.NextDouble() * 0.16f), roll = true });
    }

    // v7.1.1 (prueba): fracción de puntos de las piscinas tapados desde arriba por las capas siempre dibujadas (0..layers-1),
    // con margen para el vaivén. 1 = no se ve nada debajo (ni el fondo ni la capa plana).
    public float TopCoverage(int samples, int layers, float margin)
    {
        var grid = new Dictionary<long, List<Vector2>>(); const float G = 0.08f;
        foreach (TileData td in tiles)
        {
            int end = td.layerEnd[Mathf.Clamp(layers, 1, 3) - 1];
            for (int i = 0; i < end; i++)
            {
                Vector3 c = transform.InverseTransformPoint(new Vector3(td.m[i].m03, td.m[i].m13, td.m[i].m23));
                long key = ((long)Mathf.FloorToInt(c.x / G) << 32) ^ (uint)Mathf.FloorToInt(c.z / G);
                if (!grid.TryGetValue(key, out List<Vector2> l)) grid[key] = l = new List<Vector2>();
                l.Add(new Vector2(c.x, c.z));
            }
        }
        System.Random rr = new System.Random(5); int hit = 0, tot = 0; float rc = BallR - margin;
        foreach (var p in pits)
        {
            Rect inner = BallpitSetPiece.PitInner(p, rim);
            for (int k = 0; k < samples; k++)
            {
                Vector2 q = new Vector2(inner.xMin + BallR + (float)rr.NextDouble() * (inner.width - 2 * BallR), inner.yMin + BallR + (float)rr.NextDouble() * (inner.height - 2 * BallR));
                tot++; bool cov = false; int gx = Mathf.FloorToInt(q.x / G), gz = Mathf.FloorToInt(q.y / G);
                for (int dx = -1; dx <= 1 && !cov; dx++)
                    for (int dz = -1; dz <= 1 && !cov; dz++)
                        if (grid.TryGetValue(((long)(gx + dx) << 32) ^ (uint)(gz + dz), out List<Vector2> l))
                            foreach (Vector2 b in l) if ((b - q).sqrMagnitude < rc * rc) { cov = true; break; }
                if (cov) hit++;
            }
        }
        return tot > 0 ? hit / (float)tot : 0f;
    }
    // v7.1.2 (pruebas): réplica exacta en CPU del desplazamiento que hace BallInstanced.shader en el vértice (bola de centro c)
    public static Vector3 OffsetAt(Vector3 c, float t, float topY, float dyn)
    {
        Vector3 off = Vector3.zero;
        float h = Frac(Mathf.Sin(c.x * 12.9898f + c.z * 78.233f) * 43758.5453f);
        float sink = Mathf.Clamp01((topY - c.y) / 0.2f);
        float amb = Shader.GetGlobalFloat(AmbientId) * dyn * (1f - 0.55f * sink);
        float sw = Mathf.Sin(c.x * 1.9f + c.z * 1.2f - t * 0.85f) * 0.6f + Mathf.Sin(-c.x * 1.1f + c.z * 2.6f - t * 1.27f + 1.3f) * 0.4f;
        off.y += amb * (0.016f * sw + 0.009f * Mathf.Sin(t * (1.5f + h * 1.6f) + h * 6.2832f));
        off.x += amb * 0.007f * Mathf.Sin(t * (1.1f + h) + h * 12.1f); off.z += amb * 0.007f * Mathf.Cos(t * (1.35f + h * 0.8f) + h * 9.3f);
        int rn = (int)Shader.GetGlobalFloat(RippleNId);
        for (int j = 0; j < 8 && j < rn; j++)
        {
            Vector4 q = ripples[j];
            if (q.w <= 0f || Mathf.Abs(c.y - q.y) > 1.2f) continue;
            float age = t - q.w; if (age < 0f || age > 2.6f) continue;
            Vector2 dq = new Vector2(c.x - q.x, c.z - q.z); float rq = dq.magnitude + 1e-4f;
            float x = rq - (0.35f + age * 1.15f);
            float env = Mathf.Exp(-x * x * 22f) * Mathf.Exp(-age * 1.35f) * Mathf.Clamp01(1.5f - rq * 0.32f) * dyn * (1f - 0.5f * sink);
            off.y += 0.032f * env * Mathf.Cos(x * 16f);
            off.x += dq.x / rq * 0.014f * env; off.z += dq.y / rq * 0.014f * env;
        }
        for (int i = 0; i < 6; i++)
        {
            Vector4 p = pushers[i];
            if (p.w == 0f || Mathf.Abs(c.y - p.y) > 1.2f) continue;
            Vector2 d = new Vector2(c.x - p.x, c.z - p.z); float r = d.magnitude + 1e-4f, R = Mathf.Abs(p.w);
            float k = Mathf.Clamp01(1f - r / R);
            if (p.w < 0f) { off.y += k * k * (3f - 2f * k) * 0.24f * dyn; off.x += d.x / r * k * (1f - k) * 0.12f * dyn; off.z += d.y / r * k * (1f - k) * 0.12f * dyn; continue; }
            float ring = Mathf.Clamp01(1f - Mathf.Abs(r - R * 1.1f) / (R * 0.5f));
            float k2 = Mathf.Clamp01(1f - r / (R * 2.2f));
            float hor = (Mathf.Pow(k, 1.3f) * R * 0.85f + k2 * k2 * 0.05f) * dyn;
            off.x += d.x / r * hor; off.z += d.y / r * hor;
            off.y += (ring * 0.07f - k * 0.17f + k2 * k2 * 0.02f) * dyn;
        }
        return off;
    }
    static float Frac(float v) { return v - Mathf.Floor(v); }
    // centros (mundo) de las bolas de la capa de arriba a menos de rmax (en planta) de p, con la altura de su superficie
    public void TopBallsNear(Vector3 p, float rmax, List<Vector3> centers, List<float> tops)
    {
        foreach (TileData td in tiles)
        {
            if (td.b.SqrDistance(new Vector3(p.x, td.b.center.y, p.z)) > rmax * rmax) continue;
            float top = td.mpb.GetFloat("_TopY");
            for (int i = 0; i < td.layerEnd[0]; i++)
            {
                Vector3 c = new Vector3(td.m[i].m03, td.m[i].m13, td.m[i].m23);
                if (new Vector2(c.x - p.x, c.z - p.z).sqrMagnitude <= rmax * rmax) { centers.Add(c); tops.Add(top); }
            }
        }
    }
    public bool TilesAnimated { get { foreach (TileData td in tiles) if (td.mpb.GetFloat("_Dyn") < 0.5f) return false; return tiles.Count > 0; } }
    public int LastVisible { get; private set; }
    public int LastCalls { get; private set; }

    void Update() { Submit(); }

    public void Submit()
    {
        UpdatePushers();
        UpdateFlyers();
        LastVisible = 0; LastCalls = 0;
        Camera cam = Camera.main;
        if (cam == null || tiles.Count == 0 || mat == null) return;
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        Vector3 cp = cam.transform.position;
        if (Time.frameCount != lowFrame) { lowFrame = Time.frameCount; lowQ = GameSettings.Quality == 0; }
        bool low = lowQ;
        foreach (TileData td in tiles)
        {
            float d = Vector3.Distance(cp, td.b.ClosestPoint(cp));
            if (d > (low ? 28f : 38f) || !GeometryUtility.TestPlanesAABB(planes, td.b)) continue;
            int layers = !low && d < 7f ? 3 : 2;   // v7.1.1: siempre 2 capas como mínimo (antes 1 de lejos y en Bajo: se veía el fondo plano); Bajo sin la 3.ª
            int count = td.layerEnd[layers - 1];
            Mesh mesh = !low && d < 9f ? lod0 : lod1;   // v7.1.1: Bajo siempre con la bola de 20 triángulos
            LastVisible += count; LastCalls++;
            if (count == td.m.Length)
                Graphics.DrawMeshInstanced(mesh, 0, mat, td.m, count, td.mpb, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
            else
            {
                System.Array.Copy(td.m, scratch, count);
                Graphics.DrawMeshInstanced(mesh, 0, mat, scratch, count, td.mpb, ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off);
            }
        }
    }

    // ruido de plástico al vadear (jugador local): volumen según velocidad y profundidad
    static AudioSource rustle; static AudioClip rustleClip;
    public static AudioClip RustleClip { get { if (rustleClip == null) rustleClip = ProceduralAudio.CreateBallRustle(71); return rustleClip; } }
    void LateUpdate()
    {
        if (All.Count == 0 || All[0] != this) return;
        FirstPersonController fpc = FirstPersonController.LocalInstance;
        if (fpc == null) return;
        if (rustle == null)
        {
            rustleClip = RustleClip;
            GameObject go = new GameObject("BallRustle");
            go.transform.SetParent(fpc.transform, false); go.transform.localPosition = new Vector3(0f, 0.4f, 0f);
            rustle = go.AddComponent<AudioSource>();
            rustle.clip = rustleClip; rustle.loop = true; rustle.spatialBlend = 0.6f; rustle.volume = 0f; rustle.playOnAwake = false;
            rustle.minDistance = 1f; rustle.maxDistance = 12f;
        }
        float depth = fpc.BallDepth;
        float sp = fpc.FlatSpeed;
        float target = depth > 0.05f ? Mathf.Clamp01(sp / 2.5f) * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(depth / 0.6f)) * 0.9f * GameSettings.SfxVolume : 0f;
        rustle.volume = Mathf.MoveTowards(rustle.volume, target, Time.deltaTime * 3f);
        rustle.pitch = 0.9f + Mathf.Clamp01(sp / 4f) * 0.25f;
        if (rustle.volume > 0.001f) { if (!rustle.isPlaying) { rustle.time = Random.Range(0f, rustleClip.length * 0.9f); rustle.Play(); } }
        else if (rustle.isPlaying) rustle.Pause();
    }

    // ¿está p (mundo) dentro de una piscina, por debajo de la superficie de bolas? devuelve profundidad de bolas sobre los pies
    public float BallDepthAt(Vector3 wp)
    {
        Vector3 lp = transform.InverseTransformPoint(wp);
        foreach (var p in pits)
            if (BallpitSetPiece.PitInner(p, rim).Contains(new Vector2(lp.x, lp.z)) && lp.y < p.ball) return p.ball - lp.y;
        return 0f;
    }
    public static float DepthAt(Vector3 wp)
    {
        float best = 0f;
        foreach (BallPitField f in All) if (f != null) best = Mathf.Max(best, f.BallDepthAt(wp));
        return best;
    }
}

// objetos que apartan bolas además del jugador local (cuerpos remotos, monstruo)
public static class BallPitPushers
{
    public static readonly List<Transform> List = new List<Transform>();
    public static void Add(Transform t) { if (t != null && !List.Contains(t)) List.Add(t); }
    public static void Remove(Transform t) { List.Remove(t); }
}

// v7.1.0: uno o dos fluorescentes de cada sala parpadean: tapa emisiva propia que se apaga a ratos, zumbido y una ligera caída
// de la luz horneada de toda la sala mientras está apagado
public class HallFlicker : MonoBehaviour
{
    private Renderer hall; private MaterialPropertyBlock mpb, tubeMpb;
    private readonly List<Renderer> tubes = new List<Renderer>();
    private readonly List<AudioSource> buzz = new List<AudioSource>();
    private System.Random rng; private float t, offFor, nextBurst; private bool off;
    private static Material tubeMat; private static AudioClip buzzClip;
    public static int Count { get; private set; }
    public float Level { get; private set; } = 1f;

    public static void Attach(GameObject root, Renderer hallRenderer, BallpitSetPiece.HallJ d, int seed)
    {
        if (d.lights == null || d.lights.Length == 0) return;
        HallFlicker f = root.AddComponent<HallFlicker>();
        f.hall = hallRenderer; f.rng = new System.Random(seed);
        if (tubeMat == null)
        {
            tubeMat = new Material(Shader.Find("Backrooms/PlayBaked")) { name = "FlickerTube" };
            tubeMat.SetColor("_Color", Color.black); tubeMat.SetFloat("_LMScale", 0f); tubeMat.SetFloat("_MetalScale", 0f); tubeMat.SetFloat("_Glossiness", 0f);
            tubeMat.SetTexture("_LightMap", Texture2D.blackTexture);
        }
        if (buzzClip == null) buzzClip = ProceduralAudio.CreateBuzzClip();
        List<int> alive = new List<int>();
        for (int i = 0; i < d.lights.Length; i++) if (d.lights[i].dead == 0) alive.Add(i);
        int want = 1 + f.rng.Next(2);
        float H = d.H > 0 ? d.H : 4.6f;
        for (int k = 0; k < want && alive.Count > 0; k++)
        {
            int idx = alive[f.rng.Next(alive.Count)]; alive.Remove(idx);
            var L = d.lights[idx];
            GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "FlickerTube"; q.transform.SetParent(root.transform, false);
            q.transform.localPosition = new Vector3(L.x, H - 0.068f, L.z);
            q.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);   // mira hacia abajo
            q.transform.localScale = new Vector3(1.17f, 0.55f, 1f);
            Renderer r = q.GetComponent<Renderer>(); r.sharedMaterial = tubeMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            f.tubes.Add(r);
            AudioSource a = q.AddComponent<AudioSource>();
            a.clip = buzzClip; a.loop = true; a.spatialBlend = 1f; a.minDistance = 1.5f; a.maxDistance = 14f; a.rolloffMode = AudioRolloffMode.Linear; a.volume = 0.12f; a.dopplerLevel = 0f;
            a.time = (float)f.rng.NextDouble() * 0.9f; a.playOnAwake = false;
            f.buzz.Add(a); AudioBuzzLimiter.RegisterExternal(a);
        }
        f.nextBurst = 2f + (float)f.rng.NextDouble() * 6f;
        Count++;
    }

    private void Update()
    {
        if (tubes.Count == 0) return;
        float dt = Time.deltaTime; t += dt;
        if (off) { offFor -= dt; if (offFor <= 0f) off = false; }
        else if (t >= nextBurst)
        {
            // ráfaga: varios apagones cortos seguidos, o uno largo de vez en cuando
            if (rng.NextDouble() < 0.12) { off = true; offFor = 0.6f + (float)rng.NextDouble() * 1.6f; nextBurst = t + 3f + (float)rng.NextDouble() * 8f; }
            else { off = true; offFor = 0.04f + (float)rng.NextDouble() * 0.12f; nextBurst = t + (rng.NextDouble() < 0.6 ? 0.08f + (float)rng.NextDouble() * 0.25f : 2f + (float)rng.NextDouble() * 7f); }
        }
        float target = off ? 0f : 1f;
        Level = Mathf.MoveTowards(Level, target, dt * (off ? 40f : 25f));
        if (mpb == null) { mpb = new MaterialPropertyBlock(); tubeMpb = new MaterialPropertyBlock(); }
        tubeMpb.SetColor("_EmitAdd", new Color(2.3f, 2.35f, 2.4f) * Level);
        foreach (Renderer r in tubes) r.SetPropertyBlock(tubeMpb);
        AudioBuzzLimiter.Tick();
        foreach (AudioSource a in buzz)
        {
            a.volume = (0.06f + 0.14f * (1f - Level) + (off ? 0.05f : 0f)) * GameSettings.SfxVolume;
            bool on = AudioBuzzLimiter.ExternalOn(a);   // solo los zumbidos más cercanos (máx. 3 en total con los de los pasillos)
            if (on && !a.isPlaying) a.Play(); else if (!on && a.isPlaying) a.Pause();
        }
        if (hall != null)
        {
            hall.GetPropertyBlock(mpb);
            mpb.SetFloat("_LMScale", BallpitSetPiece.BakeExposure * (1f - 0.08f * (1f - Level) * tubes.Count));
            hall.SetPropertyBlock(mpb);
        }
    }
}
