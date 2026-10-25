using System.Collections.Generic;
using UnityEngine;

// v7.7.0 Desierto Cálido: constructor de mallas con varias submallas (un material por submalla) para muros de adobe rotos,
// montones de escombros, el brocal de los pozos y la cueva. UV en metros (el material escala la textura a su tamaño real).
public class DesertMesh
{
    public readonly List<Vector3> v = new List<Vector3>();
    public readonly List<Vector3> n = new List<Vector3>();
    public readonly List<Vector2> uv = new List<Vector2>();
    public readonly List<int>[] tri;
    public DesertMesh(int subs) { tri = new List<int>[subs]; for (int i = 0; i < subs; i++) tri[i] = new List<int>(); }

    public int Vert(Vector3 p, Vector3 nr, Vector2 t) { v.Add(p); n.Add(nr); uv.Add(t); return v.Count - 1; }

    // cuadrilátero a-b-c-d (en orden, visto de frente: antihorario según Unity = horario en pantalla) con normal plana
    public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td)
    {
        Vector3 nr = Vector3.Cross(b - a, d - a); if (nr.sqrMagnitude < 1e-12f) nr = Vector3.Cross(c - b, d - b);
        nr.Normalize();
        int i0 = Vert(a, nr, ta), i1 = Vert(b, nr, tb), i2 = Vert(c, nr, tc), i3 = Vert(d, nr, td);
        List<int> t = tri[sub];
        AddTri(t, i0, i1, i2, nr); AddTri(t, i0, i2, i3, nr);
    }

    // v7.7.0 a: sin triángulos degenerados (área 0: bordes rotos que tocan una abertura) y, si el cuadrilátero se retuerce,
    // cada mitad mira al mismo lado que la normal del cuadrilátero
    private void AddTri(List<int> t, int i0, int i1, int i2, Vector3 nr)
    {
        Vector3 g = Vector3.Cross(v[i1] - v[i0], v[i2] - v[i0]);
        if (g.sqrMagnitude < 1e-12f) return;
        if (Vector3.Dot(g, nr) >= 0f) { t.Add(i0); t.Add(i1); t.Add(i2); } else { t.Add(i0); t.Add(i2); t.Add(i1); }
    }

    // igual que Quad pero orientado: la cara mira hacia 'outward' (se invierte el orden si hace falta)
    public void QuadO(int sub, Vector3 outward, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td)
    {
        Vector3 nr = Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a);
        if (Vector3.Dot(nr, outward) >= 0f) Quad(sub, a, b, c, d, ta, tb, tc, td); else Quad(sub, d, c, b, a, td, tc, tb, ta);
    }

    public Mesh Build(string name)
    {
        Mesh m = new Mesh { name = name };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
        m.subMeshCount = tri.Length;
        for (int i = 0; i < tri.Length; i++) m.SetTriangles(tri[i], i);
        m.RecalculateBounds(); m.RecalculateTangents();
        return m;
    }

    // ---------------------------------------------------------------- muro con aberturas y borde superior roto
    public struct Opening { public float x0, x1, y0, y1; }

    // Muro a lo largo de +X local (de x=0 a x=len), grosor t centrado en z=0, desde y=y0 (enterrado) hasta top(x).
    // Submallas: subBase por debajo de baseH (zócalo de piedra), subBody por encima (adobe o piedra).
    // Caras: delante (+z), detrás (-z), arriba (también los alféizares), abajo (dinteles) y jambas (bordes de aberturas y extremos).
    public void Wall(Matrix4x4 tr, float len, float t, float y0, System.Func<float, float> top, List<Opening> ops, float baseH, int subBase, int subBody, float step = 0.22f)
    {
        List<float> xs = new List<float>();
        int nst = Mathf.Max(1, Mathf.CeilToInt(len / step));
        for (int i = 0; i <= nst; i++) xs.Add(len * i / nst);
        foreach (Opening o in ops) { xs.Add(Mathf.Clamp(o.x0, 0f, len)); xs.Add(Mathf.Clamp(o.x1, 0f, len)); }
        xs.Sort();
        for (int i = xs.Count - 1; i > 0; i--) if (xs[i] - xs[i - 1] < 0.02f) xs.RemoveAt(i);
        int nc = xs.Count - 1;
        List<Vector2>[] cols = new List<Vector2>[nc];
        for (int c = 0; c < nc; c++)
        {
            float xm = (xs[c] + xs[c + 1]) * 0.5f;
            List<Vector2> iv = new List<Vector2> { new Vector2(y0, float.PositiveInfinity) };   // y=+inf: hasta el borde superior
            foreach (Opening o in ops)
            {
                if (xm < o.x0 || xm > o.x1) continue;
                List<Vector2> nv = new List<Vector2>();
                foreach (Vector2 s in iv)
                {
                    float sTop = float.IsPositiveInfinity(s.y) ? Mathf.Min(top(xs[c]), top(xs[c + 1])) : s.y;
                    if (o.y1 <= s.x || o.y0 >= sTop) { nv.Add(s); continue; }
                    if (o.y0 > s.x) nv.Add(new Vector2(s.x, o.y0));
                    if (o.y1 < sTop - 0.06f) nv.Add(new Vector2(o.y1, s.y));
                }
                iv = nv;
            }
            cols[c] = iv;
        }
        float hz = t * 0.5f;
        for (int c = 0; c < nc; c++)
        {
            float xa = xs[c], xb = xs[c + 1], ta = top(xa), tb = top(xb);
            foreach (Vector2 s in cols[c])
            {
                bool toTop = float.IsPositiveInfinity(s.y);
                float ya0 = s.x, yb0 = s.x, ya1 = toTop ? ta : s.y, yb1 = toTop ? tb : s.y;
                Band(tr, xa, xb, ya0, yb0, ya1, yb1, hz, baseH, subBase, subBody);
                // arriba (corona del muro o alféizar)
                Vector3 p0 = new Vector3(xa, ya1, hz), p1 = new Vector3(xb, yb1, hz), p2 = new Vector3(xb, yb1, -hz), p3 = new Vector3(xa, ya1, -hz);
                QuadO(ya1 < baseH ? subBase : subBody, tr.MultiplyVector(Vector3.up), tr.MultiplyPoint3x4(p3), tr.MultiplyPoint3x4(p2), tr.MultiplyPoint3x4(p1), tr.MultiplyPoint3x4(p0),
                    new Vector2(xa, -hz), new Vector2(xb, -hz), new Vector2(xb, hz), new Vector2(xa, hz));
                // abajo (dintel), solo si no arranca del suelo
                if (s.x > y0 + 0.01f)
                    QuadO(subBody, tr.MultiplyVector(Vector3.down), tr.MultiplyPoint3x4(new Vector3(xa, s.x, hz)), tr.MultiplyPoint3x4(new Vector3(xb, s.x, hz)), tr.MultiplyPoint3x4(new Vector3(xb, s.x, -hz)), tr.MultiplyPoint3x4(new Vector3(xa, s.x, -hz)),
                        new Vector2(xa, hz), new Vector2(xb, hz), new Vector2(xb, -hz), new Vector2(xa, -hz));
            }
        }
        // jambas: donde una columna tiene muro y la vecina no
        for (int b = 0; b <= nc; b++)
        {
            float x = xs[b], tp = top(x);
            List<Vector2> L = b > 0 ? Resolve(cols[b - 1], tp) : new List<Vector2>(), R = b < nc ? Resolve(cols[b], tp) : new List<Vector2>();
            List<float> ev = new List<float>();
            foreach (Vector2 s in L) { ev.Add(s.x); ev.Add(s.y); }
            foreach (Vector2 s in R) { ev.Add(s.x); ev.Add(s.y); }
            ev.Sort();
            for (int k = 0; k + 1 < ev.Count; k++)
            {
                float ya = ev[k], yb = ev[k + 1]; if (yb - ya < 0.005f) continue;
                float ym = (ya + yb) * 0.5f; bool inL = Inside(L, ym), inR = Inside(R, ym);
                if (inL == inR) continue;
                // la cara mira hacia el lado vacío
                Vector3 a = new Vector3(x, ya, inL ? -hz : hz), bq = new Vector3(x, ya, inL ? hz : -hz), cq = new Vector3(x, yb, inL ? hz : -hz), d = new Vector3(x, yb, inL ? -hz : hz);
                int sub = ym < baseH ? subBase : subBody;
                QuadO(sub, tr.MultiplyVector(inL ? Vector3.right : Vector3.left), tr.MultiplyPoint3x4(a), tr.MultiplyPoint3x4(bq), tr.MultiplyPoint3x4(cq), tr.MultiplyPoint3x4(d),
                    new Vector2(a.z + x, ya), new Vector2(bq.z + x, ya), new Vector2(cq.z + x, yb), new Vector2(d.z + x, yb));
            }
        }
    }

    private static List<Vector2> Resolve(List<Vector2> iv, float tp)
    {
        List<Vector2> r = new List<Vector2>();
        foreach (Vector2 s in iv) r.Add(new Vector2(s.x, float.IsPositiveInfinity(s.y) ? tp : s.y));
        return r;
    }
    private static bool Inside(List<Vector2> iv, float y) { foreach (Vector2 s in iv) if (y > s.x && y < s.y) return true; return false; }

    // franja vertical delante y detrás (partida en el zócalo)
    private void Band(Matrix4x4 tr, float xa, float xb, float ya0, float yb0, float ya1, float yb1, float hz, float baseH, int subBase, int subBody)
    {
        if (ya0 < baseH && yb0 < baseH)
        {
            float ma = Mathf.Min(baseH, ya1), mb = Mathf.Min(baseH, yb1);
            Faces(tr, xa, xb, ya0, yb0, ma, mb, hz, subBase);
            if (ya1 > baseH || yb1 > baseH) Faces(tr, xa, xb, ma, mb, Mathf.Max(ma, ya1), Mathf.Max(mb, yb1), hz, subBody);
        }
        else Faces(tr, xa, xb, ya0, yb0, ya1, yb1, hz, subBody);
    }
    private void Faces(Matrix4x4 tr, float xa, float xb, float ya0, float yb0, float ya1, float yb1, float hz, int sub)
    {
        if (ya1 - ya0 < 0.002f && yb1 - yb0 < 0.002f) return;
        // delante (+z): normal +z
        QuadO(sub, tr.MultiplyVector(Vector3.forward), tr.MultiplyPoint3x4(new Vector3(xb, yb0, hz)), tr.MultiplyPoint3x4(new Vector3(xb, yb1, hz)), tr.MultiplyPoint3x4(new Vector3(xa, ya1, hz)), tr.MultiplyPoint3x4(new Vector3(xa, ya0, hz)),
            new Vector2(-xb, yb0), new Vector2(-xb, yb1), new Vector2(-xa, ya1), new Vector2(-xa, ya0));
        // detrás (-z)
        QuadO(sub, tr.MultiplyVector(Vector3.back), tr.MultiplyPoint3x4(new Vector3(xa, ya0, -hz)), tr.MultiplyPoint3x4(new Vector3(xa, ya1, -hz)), tr.MultiplyPoint3x4(new Vector3(xb, yb1, -hz)), tr.MultiplyPoint3x4(new Vector3(xb, yb0, -hz)),
            new Vector2(xa, ya0), new Vector2(xa, ya1), new Vector2(xb, yb1), new Vector2(xb, yb0));
    }

    // caja (vigas, dinteles, losas, adobes sueltos) con UV en metros
    public void Box(int sub, Matrix4x4 tr, Vector3 c, Vector3 size)
    {
        Vector3 h = size * 0.5f;
        Vector3[] p = new Vector3[8];
        for (int i = 0; i < 8; i++) p[i] = tr.MultiplyPoint3x4(c + new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z));
        float sx = size.x, sy = size.y, sz = size.z;
        QuadO(sub, tr.MultiplyVector(Vector3.forward), p[4], p[6], p[7], p[5], new Vector2(0, 0), new Vector2(0, sy), new Vector2(sx, sy), new Vector2(sx, 0));   // +z
        QuadO(sub, tr.MultiplyVector(Vector3.back), p[1], p[3], p[2], p[0], new Vector2(0, 0), new Vector2(0, sy), new Vector2(sx, sy), new Vector2(sx, 0));   // -z
        QuadO(sub, tr.MultiplyVector(Vector3.right), p[5], p[7], p[3], p[1], new Vector2(0, 0), new Vector2(0, sy), new Vector2(sz, sy), new Vector2(sz, 0));   // +x
        QuadO(sub, tr.MultiplyVector(Vector3.left), p[0], p[2], p[6], p[4], new Vector2(0, 0), new Vector2(0, sy), new Vector2(sz, sy), new Vector2(sz, 0));   // -x
        QuadO(sub, tr.MultiplyVector(Vector3.up), p[2], p[3], p[7], p[6], new Vector2(0, 0), new Vector2(sx, 0), new Vector2(sx, sz), new Vector2(0, sz));   // +y
        QuadO(sub, tr.MultiplyVector(Vector3.down), p[4], p[5], p[1], p[0], new Vector2(0, 0), new Vector2(sx, 0), new Vector2(sx, sz), new Vector2(0, sz));   // -y
    }

    // viga redonda (cilindro de 8 lados) de a a b
    public void Log(int sub, Matrix4x4 tr, Vector3 a, Vector3 b, float r, int sides = 8)
    {
        Vector3 f = (b - a); float len = f.magnitude; f /= Mathf.Max(1e-4f, len);
        Vector3 side = Vector3.Cross(f, Mathf.Abs(f.y) > 0.9f ? Vector3.right : Vector3.up).normalized, up = Vector3.Cross(side, f);
        int b0 = v.Count;
        for (int k = 0; k <= sides; k++)
        {
            float ang = k * Mathf.PI * 2f / sides; Vector3 d = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
            Vector3 nw = tr.MultiplyVector(d).normalized;
            Vert(tr.MultiplyPoint3x4(a + d * r), nw, new Vector2(k * 0.08f, 0f));
            Vert(tr.MultiplyPoint3x4(b + d * r), nw, new Vector2(k * 0.08f, len));
        }
        List<int> t = tri[sub];
        for (int k = 0; k < sides; k++) { int i = b0 + k * 2; t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i + 1); t.Add(i + 3); t.Add(i + 2); }
        // tapas (cortes de la viga)
        Vector3 na = tr.MultiplyVector(-f).normalized, nb = tr.MultiplyVector(f).normalized;
        int ca = Vert(tr.MultiplyPoint3x4(a), na, new Vector2(0.5f, 0.5f)), cb = Vert(tr.MultiplyPoint3x4(b), nb, new Vector2(0.5f, 0.5f));
        int ra = v.Count; for (int k = 0; k <= sides; k++) { float ang = k * Mathf.PI * 2f / sides; Vector3 d = side * Mathf.Cos(ang) + up * Mathf.Sin(ang); Vert(tr.MultiplyPoint3x4(a + d * r), na, new Vector2(0.5f + Mathf.Cos(ang) * r, 0.5f + Mathf.Sin(ang) * r)); }
        int rb = v.Count; for (int k = 0; k <= sides; k++) { float ang = k * Mathf.PI * 2f / sides; Vector3 d = side * Mathf.Cos(ang) + up * Mathf.Sin(ang); Vert(tr.MultiplyPoint3x4(b + d * r), nb, new Vector2(0.5f + Mathf.Cos(ang) * r, 0.5f + Mathf.Sin(ang) * r)); }
        for (int k = 0; k < sides; k++) { t.Add(ca); t.Add(ra + k); t.Add(ra + k + 1); t.Add(cb); t.Add(rb + k + 1); t.Add(rb + k); }
    }

    // montón (escombros, arena acumulada): elipse con altura h, perfil suave y ruido; base 'sink' bajo el suelo
    public void Mound(int sub, Matrix4x4 tr, float rx, float rz, float h, System.Random r, float sink = 0.15f, int rings = 6, int segs = 18)
    {
        int b0 = v.Count; float ph1 = (float)r.NextDouble() * 6.28f, ph2 = (float)r.NextDouble() * 6.28f;
        for (int i = 0; i <= rings; i++)
            for (int k = 0; k < segs; k++)
            {
                float u = i / (float)rings, a = k * Mathf.PI * 2f / segs;
                float wob = 1f + 0.18f * Mathf.Sin(a * 3f + ph1) + 0.1f * Mathf.Sin(a * 5f + ph2);
                float x = Mathf.Cos(a) * rx * u * wob, z = Mathf.Sin(a) * rz * u * wob;
                float y = h * (1f - u * u) * (1f - u * 0.15f) * (0.85f + 0.3f * (float)r.NextDouble()) - sink * u * u;
                if (i == 0) y = h;
                Vert(tr.MultiplyPoint3x4(new Vector3(x, y, z)), Vector3.up, new Vector2(x, z));
            }
        List<int> t = tri[sub];
        for (int i = 0; i < rings; i++)
            for (int k = 0; k < segs; k++)
            {
                int a0 = b0 + i * segs + k, a1 = b0 + i * segs + (k + 1) % segs, c0 = a0 + segs, c1 = a1 + segs;
                if (i > 0) { t.Add(a0); t.Add(a1); t.Add(c0); }   // v7.7.0 g: en el anillo 0 (cúspide) ese triángulo tiene área 0
                t.Add(a1); t.Add(c1); t.Add(c0);
            }
        // normales suaves del montón
        Vector3[] acc = new Vector3[(rings + 1) * segs];
        for (int i = 0; i < rings; i++)
            for (int k = 0; k < segs; k++)
            {
                int a0 = i * segs + k, a1 = i * segs + (k + 1) % segs, c0 = a0 + segs, c1 = a1 + segs;
                Vector3 n1 = Vector3.Cross(v[b0 + a1] - v[b0 + a0], v[b0 + c0] - v[b0 + a0]), n2 = Vector3.Cross(v[b0 + c1] - v[b0 + a1], v[b0 + c0] - v[b0 + a1]);
                acc[a0] += n1; acc[a1] += n1 + n2; acc[c0] += n1 + n2; acc[c1] += n2;
            }
        for (int i = 0; i < acc.Length; i++) n[b0 + i] = acc[i].sqrMagnitude > 1e-10f ? acc[i].normalized : Vector3.up;
    }
}
