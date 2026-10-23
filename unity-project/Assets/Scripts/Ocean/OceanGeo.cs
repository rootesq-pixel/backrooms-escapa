using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.3.0 Talasofobia: utilidades de geometría (cajas, muros con huecos, escaleras, tubos, tejados) sobre MeshBuilder.
// Todo en coordenadas locales del objeto que recibe la malla; UV proyectadas en metros (tile = metros por repetición).
public static class OceanGeo
{
    // v7.3.0: triángulos cuya normal de vértice (suavizada) apunta al lado contrario de su cara (bordes finos de aletas,
    // pliegues del casco). Se les dan vértices propios con la normal de la cara, sin tocar la forma ni las UV.
    public static int SplitFlippedTriangles(Mesh m)
    {
        if (m == null || !m.isReadable) return 0;
        Vector3[] v = m.vertices, n = m.normals; if (n == null || n.Length != v.Length) return 0;
        var V = new System.Collections.Generic.List<Vector3>(v); var N = new System.Collections.Generic.List<Vector3>(n);
        var UV = new System.Collections.Generic.List<Vector2>(); m.GetUVs(0, UV); bool hasUv = UV.Count == v.Length;
        var Ta = new System.Collections.Generic.List<Vector4>(); m.GetTangents(Ta); bool hasT = Ta.Count == v.Length;
        var C = new System.Collections.Generic.List<Color>(); m.GetColors(C); bool hasC = C.Count == v.Length;
        int fixedTris = 0; int sub = m.subMeshCount; int[][] tris = new int[sub][];
        for (int s = 0; s < sub; s++)
        {
            int[] t = m.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 g = Vector3.Cross(V[t[i + 1]] - V[t[i]], V[t[i + 2]] - V[t[i]]);
                if (g.sqrMagnitude < 1e-16f) continue;
                if (Vector3.Dot(g, N[t[i]]) > 0f && Vector3.Dot(g, N[t[i + 1]]) > 0f && Vector3.Dot(g, N[t[i + 2]]) > 0f) continue;
                Vector3 gn = g.normalized; fixedTris++;
                for (int k = 0; k < 3; k++)
                {
                    int o = t[i + k];
                    if (Vector3.Dot(N[o], gn) > 0f) continue;
                    V.Add(V[o]); N.Add(gn); if (hasUv) UV.Add(UV[o]); if (hasT) Ta.Add(Ta[o]); if (hasC) C.Add(C[o]);
                    t[i + k] = V.Count - 1;
                }
            }
            tris[s] = t;
        }
        if (fixedTris == 0) return 0;
        if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(V); m.SetNormals(N); if (hasUv) m.SetUVs(0, UV); if (hasT) m.SetTangents(Ta); if (hasC) m.SetColors(C);
        for (int s = 0; s < sub; s++) m.SetTriangles(tris[s], s, false);
        m.RecalculateBounds();
        return fixedTris;
    }
    public static void Box(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float tile)
    {
        Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b);
        if (mx.x - mn.x < 0.001f || mx.y - mn.y < 0.001f || mx.z - mn.z < 0.001f) return;
        float x0 = mn.x, x1 = mx.x, y0 = mn.y, y1 = mx.y, z0 = mn.z, z1 = mx.z;
        mb.AddQuad(sub, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, Vector3.right, Vector3.up, tile, tile, 0f, 0f);
        mb.AddQuad(sub, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, Vector3.right, Vector3.up, tile, tile, 0.37f, 0f);
        mb.AddQuad(sub, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right, Vector3.forward, Vector3.up, tile, tile, 0.61f, 0f);
        mb.AddQuad(sub, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, Vector3.forward, Vector3.up, tile, tile, 0.13f, 0f);
        mb.AddQuad(sub, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.up, Vector3.right, Vector3.forward, tile, tile, 0f, 0f);
        mb.AddQuad(sub, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, Vector3.right, Vector3.forward, tile, tile, 0f, 0f);
    }

    public struct Hole { public float a0, a1, y0, y1; public Hole(float a0, float a1, float y0, float y1) { this.a0 = a0; this.a1 = a1; this.y0 = y0; this.y1 = y1; } }

    // muro recto con huecos (puertas/ventanas). alongX: el muro va en X a la altura z = c (grosor t hacia +c); si no, en Z a x = c.
    public static void Wall(MeshBuilder mb, int sub, bool alongX, float c, float t, float from, float to, float y0, float y1, float tile, params Hole[] holes)
    {
        List<Hole> hs = new List<Hole>(holes ?? new Hole[0]);
        hs.Sort((p, q) => p.a0.CompareTo(q.a0));
        float cur = from;
        foreach (Hole h in hs)
        {
            float a0 = Mathf.Clamp(h.a0, from, to), a1 = Mathf.Clamp(h.a1, from, to);
            if (a0 > cur) Seg(mb, sub, alongX, c, t, cur, a0, y0, y1, tile);
            if (h.y0 > y0) Seg(mb, sub, alongX, c, t, a0, a1, y0, h.y0, tile);
            if (h.y1 < y1) Seg(mb, sub, alongX, c, t, a0, a1, h.y1, y1, tile);
            cur = Mathf.Max(cur, a1);
        }
        if (to > cur) Seg(mb, sub, alongX, c, t, cur, to, y0, y1, tile);
    }

    private static void Seg(MeshBuilder mb, int sub, bool alongX, float c, float t, float a0, float a1, float y0, float y1, float tile)
    {
        if (alongX) Box(mb, sub, new Vector3(a0, y0, c), new Vector3(a1, y1, c + t), tile);
        else Box(mb, sub, new Vector3(c, y0, a0), new Vector3(c + t, y1, a1), tile);
    }

    // marco de ventana/puerta (cuatro listones) en un muro alongX a z=c
    public static void Frame(MeshBuilder mb, int sub, bool alongX, float c, float t, Hole h, float w)
    {
        float e = 0.03f;
        if (alongX)
        {
            Box(mb, sub, new Vector3(h.a0 - w, h.y0 - w, c - e), new Vector3(h.a0, h.y1 + w, c + t + e), 0.5f);
            Box(mb, sub, new Vector3(h.a1, h.y0 - w, c - e), new Vector3(h.a1 + w, h.y1 + w, c + t + e), 0.5f);
            Box(mb, sub, new Vector3(h.a0, h.y1, c - e), new Vector3(h.a1, h.y1 + w, c + t + e), 0.5f);
            Box(mb, sub, new Vector3(h.a0, h.y0 - w, c - e), new Vector3(h.a1, h.y0, c + t + e), 0.5f);
        }
        else
        {
            Box(mb, sub, new Vector3(c - e, h.y0 - w, h.a0 - w), new Vector3(c + t + e, h.y1 + w, h.a0), 0.5f);
            Box(mb, sub, new Vector3(c - e, h.y0 - w, h.a1), new Vector3(c + t + e, h.y1 + w, h.a1 + w), 0.5f);
            Box(mb, sub, new Vector3(c - e, h.y1, h.a0), new Vector3(c + t + e, h.y1 + w, h.a1), 0.5f);
            Box(mb, sub, new Vector3(c - e, h.y0 - w, h.a0), new Vector3(c + t + e, h.y0, h.a1), 0.5f);
        }
    }

    // escalera recta de peldaños macizos (cada peldaño llega hasta el suelo): de 'start' (pie del primer peldaño) en dirección 'dir' (X o Z)
    public static void Stairs(MeshBuilder mb, int sub, Vector3 start, Vector3 dir, float width, float rise, int steps, float run, float tile)
    {
        Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
        float h = rise / steps;
        for (int i = 0; i < steps; i++)
        {
            Vector3 p0 = start + dir * (run * i);
            Vector3 p1 = start + dir * (run * (i + 1)) + side * width + Vector3.up * (h * (i + 1));
            Box(mb, sub, new Vector3(p0.x, start.y, p0.z), p1, tile);
        }
    }

    // tejado a dos aguas con la cumbrera en X (de x0 a x1), alero 'over', del borde z0..z1 a la altura y0 hasta yRidge
    public static void Gable(MeshBuilder mb, int roofSub, int wallSub, float x0, float x1, float z0, float z1, float y0, float yRidge, float over, float tile)
    {
        float zm = (z0 + z1) * 0.5f, th = 0.12f;
        Vector3 nS = new Vector3(0f, zm - z0, -(yRidge - y0)).normalized, nN = new Vector3(0f, zm - z0, (yRidge - y0)).normalized;
        float k = (yRidge - y0) / (zm - z0);
        float zo0 = z0 - over, zo1 = z1 + over, yo = y0 - over * k;
        Vector3 dS = new Vector3(0f, yRidge - yo, zm - zo0).normalized;
        // faldón sur y norte (cara superior y cara inferior)
        mb.AddQuad(roofSub, new Vector3(x0 - over, yo, zo0), new Vector3(x1 + over, yo, zo0), new Vector3(x1 + over, yRidge, zm), new Vector3(x0 - over, yRidge, zm), nS, Vector3.right, dS, tile, tile, 0f, 0f);
        mb.AddQuad(roofSub, new Vector3(x0 - over, yo - th, zo0), new Vector3(x1 + over, yo - th, zo0), new Vector3(x1 + over, yRidge - th, zm), new Vector3(x0 - over, yRidge - th, zm), -nS, Vector3.right, dS, tile, tile, 0f, 0f);
        Vector3 dN = new Vector3(0f, yRidge - yo, zm - zo1).normalized;
        mb.AddQuad(roofSub, new Vector3(x0 - over, yo, zo1), new Vector3(x1 + over, yo, zo1), new Vector3(x1 + over, yRidge, zm), new Vector3(x0 - over, yRidge, zm), nN, Vector3.right, dN, tile, tile, 0f, 0f);
        mb.AddQuad(roofSub, new Vector3(x0 - over, yo - th, zo1), new Vector3(x1 + over, yo - th, zo1), new Vector3(x1 + over, yRidge - th, zm), new Vector3(x0 - over, yRidge - th, zm), -nN, Vector3.right, dN, tile, tile, 0f, 0f);
        // hastiales (triángulos: quad degenerado)
        foreach (float x in new[] { x0, x1 })
        {
            Vector3 n = x == x0 ? Vector3.left : Vector3.right;
            mb.AddQuad(wallSub, new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, yRidge, zm), new Vector3(x, yRidge, zm), n, Vector3.forward, Vector3.up, tile, tile, 0f, 0f);
            mb.AddQuad(wallSub, new Vector3(x, y0, z0), new Vector3(x, y0, z1), new Vector3(x, yRidge, zm), new Vector3(x, yRidge, zm), -n, Vector3.forward, Vector3.up, tile, tile, 0f, 0f);
        }
    }

    // tubo de n lados entre a y b (pilotes, barandillas, cables)
    public static void Tube(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float r, int sides, float tileAlong)
    {
        Vector3 ax = b - a; float len = ax.magnitude; if (len < 0.001f) return; ax /= len;
        Vector3 s = Mathf.Abs(ax.y) > 0.95f ? Vector3.right : Vector3.Cross(ax, Vector3.up).normalized;
        Vector3 u = Vector3.Cross(s, ax).normalized;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 n0 = s * Mathf.Cos(a0) + u * Mathf.Sin(a0), n1 = s * Mathf.Cos(a1) + u * Mathf.Sin(a1);
            Vector3 nm = (n0 + n1).normalized;
            Vector3 tv = Vector3.Cross(nm, ax).normalized;
            mb.AddQuad(sub, a + n0 * r, b + n0 * r, b + n1 * r, a + n1 * r, nm, ax, tv, tileAlong, r * 6.28f, 0f, i / (float)sides);
        }
    }

    // v7.3.0 (muelle realista): tabla/viga como caja con UV de la textura de tablas (2,4 m a lo largo, 4 variantes en v).
    // axis: 0 = X, 1 = Y, 2 = Z (dirección de la longitud); row 0..3 elige la variante; uOff desplaza a lo largo
    public static void Plank(MeshBuilder mb, int sub, Vector3 a, Vector3 b, int axis, int row, float uOff)
    {
        Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b), c = (mn + mx) * 0.5f, e = (mx - mn) * 0.5f;
        if (e.x < 0.0005f || e.y < 0.0005f || e.z < 0.0005f) return;
        Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
        int i1 = (axis + 1) % 3, i2 = (axis + 2) % 3;
        int wi = e[i1] >= e[i2] ? i1 : i2, ti = wi == i1 ? i2 : i1;   // ancho y grosor
        Vector3 L = ax[axis], Wd = ax[wi], T = ax[ti];
        float len = 2.4f, w2 = e[wi] * 2f, t2 = e[ti] * 2f;
        // caras anchas (normal = ±grosor)
        foreach (float sg in new[] { 1f, -1f })
        {
            Vector3 n = T * sg, fc = c + n * e[ti];
            Face(mb, sub, fc, n, L, e[axis], Wd, e[wi], len, w2 * 4f, uOff, row * 0.25f - Vector3.Dot(c - Wd * e[wi], Wd) / (w2 * 4f));
            // cantos (normal = ±ancho)
            Vector3 n2 = Wd * sg, fc2 = c + n2 * e[wi];
            Face(mb, sub, fc2, n2, L, e[axis], T, e[ti], len, Mathf.Max(t2, 0.01f) * 4f, uOff + 0.31f, row * 0.25f - Vector3.Dot(c - T * e[ti], T) / (Mathf.Max(t2, 0.01f) * 4f));
            // testas
            Vector3 n3 = L * sg, fc3 = c + n3 * e[axis];
            Face(mb, sub, fc3, n3, Wd, e[wi], T, e[ti], len, Mathf.Max(t2, 0.01f) * 4f, 0.7f, row * 0.25f - Vector3.Dot(c - T * e[ti], T) / (Mathf.Max(t2, 0.01f) * 4f));
        }
    }
    static void Face(MeshBuilder mb, int sub, Vector3 fc, Vector3 n, Vector3 u, float hu, Vector3 v, float hv, float tu, float tv, float uo, float vo)
    {
        mb.AddQuad(sub, fc - u * hu - v * hv, fc + u * hu - v * hv, fc + u * hu + v * hv, fc - u * hu + v * hv, n, u, v, tu, tv, uo, vo);
    }

    // pilote redondo con normales suaves y v = altura del mundo (y = -4 m → 0, y = 1,6 m → 1): algas y musgo en la línea de flotación
    public static void PostTube(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float r, int sides)
    {
        Vector3 ax = b - a; float len = ax.magnitude; if (len < 0.001f) return; ax /= len;
        Vector3 s = Mathf.Abs(ax.y) > 0.95f ? Vector3.right : Vector3.Cross(ax, Vector3.up).normalized;
        Vector3 u = Vector3.Cross(s, ax).normalized;
        float uo = Mathf.Repeat(a.x * 0.37f + a.z * 0.61f, 1f);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            Vector3 n0 = s * Mathf.Cos(a0) + u * Mathf.Sin(a0), n1 = s * Mathf.Cos(a1) + u * Mathf.Sin(a1);
            Vector3 t0 = -s * Mathf.Sin(a0) + u * Mathf.Cos(a0), t1 = -s * Mathf.Sin(a1) + u * Mathf.Cos(a1);
            Vector3[] P = { a + n0 * r, b + n0 * r, b + n1 * r, a + n1 * r }; Vector3[] Nn = { n0, n0, n1, n1 }; Vector3[] Tt = { t0, t0, t1, t1 };
            float[] U = { uo + i / (float)sides, uo + i / (float)sides, uo + (i + 1) / (float)sides, uo + (i + 1) / (float)sides };
            int bi = mb.verts.Count;
            for (int k = 0; k < 4; k++)
            {
                mb.verts.Add(P[k]); mb.normals.Add(Nn[k]);
                float w = Vector3.Dot(Vector3.Cross(Nn[k], Tt[k]), ax) >= 0f ? 1f : -1f;
                mb.tangents.Add(new Vector4(Tt[k].x, Tt[k].y, Tt[k].z, w));
                mb.uvs.Add(new Vector2(U[k], (P[k].y + 4f) / 5.6f));
            }
            List<int> t = mb.tris[sub];
            if (Vector3.Dot(Vector3.Cross(P[1] - P[0], P[2] - P[0]), n0 + n1) >= 0f) { t.Add(bi); t.Add(bi + 1); t.Add(bi + 2); t.Add(bi); t.Add(bi + 2); t.Add(bi + 3); }
            else { t.Add(bi); t.Add(bi + 2); t.Add(bi + 1); t.Add(bi); t.Add(bi + 3); t.Add(bi + 2); }
        }
    }

    // cuerda (o anillo de metal): tubo a lo largo de una polilínea; u alrededor, v = metros / 0,08
    public static void RopePath(MeshBuilder mb, int sub, Vector3[] path, bool closed, float r, int segs)
    {
        int n = path.Length; if (n < 2) return;
        float acc = 0f;
        for (int k = 0; k < (closed ? n : n - 1); k++)
        {
            Vector3 p0 = path[k], p1 = path[(k + 1) % n];
            Vector3 d = p1 - p0; float l = d.magnitude; if (l < 1e-4f) continue; d /= l;
            Vector3 s = Mathf.Abs(Vector3.Dot(d, Vector3.up)) > 0.9f ? Vector3.right : Vector3.Cross(d, Vector3.up).normalized;
            Vector3 u = Vector3.Cross(s, d);
            for (int i = 0; i < segs; i++)
            {
                float a0 = i * Mathf.PI * 2f / segs, a1 = (i + 1) * Mathf.PI * 2f / segs;
                Vector3 n0 = s * Mathf.Cos(a0) + u * Mathf.Sin(a0), n1 = s * Mathf.Cos(a1) + u * Mathf.Sin(a1);
                Vector3 t0 = Vector3.Cross(d, n0), t1 = Vector3.Cross(d, n1);
                Vector3[] P = { p0 + n0 * r, p1 + n0 * r * 1.0f, p1 + n1 * r, p0 + n1 * r }; Vector3[] Nn = { n0, n0, n1, n1 };
                Vector2[] UV = { new Vector2(i / (float)segs, acc / 0.08f), new Vector2(i / (float)segs, (acc + l) / 0.08f), new Vector2((i + 1) / (float)segs, (acc + l) / 0.08f), new Vector2((i + 1) / (float)segs, acc / 0.08f) };
                int bi = mb.verts.Count;
                for (int q = 0; q < 4; q++) { mb.verts.Add(P[q]); mb.normals.Add(Nn[q]); Vector3 tt = q < 2 ? t0 : t1; mb.tangents.Add(new Vector4(tt.x, tt.y, tt.z, 1f)); mb.uvs.Add(UV[q]); }
                List<int> t = mb.tris[sub];
                if (Vector3.Dot(Vector3.Cross(P[1] - P[0], P[2] - P[0]), n0 + n1) >= 0f) { t.Add(bi); t.Add(bi + 1); t.Add(bi + 2); t.Add(bi); t.Add(bi + 2); t.Add(bi + 3); }
                else { t.Add(bi); t.Add(bi + 2); t.Add(bi + 1); t.Add(bi); t.Add(bi + 3); t.Add(bi + 2); }
            }
            acc += l;
        }
    }

    // material PBR propio (Resources: <base>_a, _n, _m con R metal, G oclusión, A suavidad)
    public static Material PbrMat(string bas, Color tint, float occl, System.Action<Object> track)
    {
        Texture2D a = Resources.Load<Texture2D>(bas + "_a"), n = Resources.Load<Texture2D>(bas + "_n"), k = Resources.Load<Texture2D>(bas + "_m");
        Material m = ProceduralAssets.CreateMaterial(tint, a, Vector2.one, 0.2f); track(m);
        if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", 1f); m.EnableKeyword("_NORMALMAP"); }
        if (k != null && m.HasProperty("_MetallicGlossMap"))
        {
            m.SetTexture("_MetallicGlossMap", k); m.SetFloat("_GlossMapScale", 1f); m.EnableKeyword("_METALLICGLOSSMAP");
            m.SetTexture("_OcclusionMap", k); m.SetFloat("_OcclusionStrength", occl);
        }
        return m;
    }

    // sólido de revolución alrededor de Y (faro): perfil de (radio, altura); caras hacia fuera (o hacia dentro si inward)
    public static void Lathe(MeshBuilder mb, int sub, Vector3 c, Vector2[] profile, int sides, float tileU, float tileV, bool inward)
    {
        for (int j = 0; j < profile.Length - 1; j++)
        {
            Vector2 p = profile[j], q = profile[j + 1];
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                Vector3 dm = (d0 + d1).normalized;
                Vector2 e = q - p; Vector3 n = (dm * e.y - Vector3.up * (e.x)).normalized; if (inward) n = -n;
                if (n.sqrMagnitude < 0.5f) n = inward ? -dm : dm;
                Vector3 tU = Vector3.Cross(Vector3.up, dm).normalized;
                Vector3 v0 = c + d0 * p.x + Vector3.up * p.y, v1 = c + d1 * p.x + Vector3.up * p.y, v2 = c + d1 * q.x + Vector3.up * q.y, v3 = c + d0 * q.x + Vector3.up * q.y;
                int b = mb.verts.Count;
                float u0 = (a0 * Mathf.Max(p.x, 0.5f)) / tileU, u1 = (a1 * Mathf.Max(p.x, 0.5f)) / tileU;
                mb.verts.Add(v0); mb.verts.Add(v1); mb.verts.Add(v2); mb.verts.Add(v3);
                for (int k = 0; k < 4; k++) { mb.normals.Add(n); mb.tangents.Add(new Vector4(tU.x, tU.y, tU.z, 1f)); }
                mb.uvs.Add(new Vector2(u0, p.y / tileV)); mb.uvs.Add(new Vector2(u1, p.y / tileV)); mb.uvs.Add(new Vector2(u1, q.y / tileV)); mb.uvs.Add(new Vector2(u0, q.y / tileV));
                Vector3 tn = Vector3.Cross(v1 - v0, v2 - v0);
                List<int> t = mb.tris[sub];
                if (Vector3.Dot(tn, n) >= 0f) { t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3); }
                else { t.Add(b); t.Add(b + 2); t.Add(b + 1); t.Add(b); t.Add(b + 3); t.Add(b + 2); }
            }
        }
    }

    // objeto con la malla y sus materiales (y collider de malla si 'collide')
    public static GameObject Emit(MeshBuilder mb, string name, Transform parent, Material[] mats, bool collide, bool shadows, System.Action<Object> track)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        Mesh m = mb.ToMesh(name); track(m);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats;
        mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        mr.receiveShadows = true;
        if (collide) { MeshCollider mc = g.AddComponent<MeshCollider>(); mc.sharedMesh = m; }
        return g;
    }

    // material Standard con albedo + normal (Resources), tinte, repetición y suavidad
    public static Material Mat(string albedo, string normal, Color tint, float smooth, float normalScale, System.Action<Object> track)
    {
        Texture2D a = string.IsNullOrEmpty(albedo) ? null : Resources.Load<Texture2D>(albedo);
        Material m = ProceduralAssets.CreateMaterial(tint, a, Vector2.one, smooth); track(m);
        Texture2D n = string.IsNullOrEmpty(normal) ? null : Resources.Load<Texture2D>(normal);
        if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", normalScale); m.EnableKeyword("_NORMALMAP"); }
        return m;
    }

    public static Material Unlit(string shader, Color c, System.Action<Object> track)
    {
        Shader s = Shader.Find(shader);
        if (s == null) s = Shader.Find("Unlit/Color");
        Material m = new Material(s); track(m);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        return m;
    }
}
