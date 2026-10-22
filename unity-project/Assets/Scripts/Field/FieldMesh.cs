using System.Collections.Generic;
using UnityEngine;

// v7.0: ayudante de mallas sencillo (cajas con UV en metros, quads, triángulos) con varios submeshes.
public class FieldMesh
{
    public readonly List<Vector3> v = new List<Vector3>();
    public readonly List<Vector3> n = new List<Vector3>();
    public readonly List<Vector2> uv = new List<Vector2>();
    public readonly List<int>[] idx;
    public float uvScale = 0.5f;   // 1 / metros por repetición
    // v7.2.0: UV2 de lightmap (casas horneadas en Blender): cada quad/triángulo es una carta rectangular en metros,
    // empaquetada por estantes en ToMesh (determinista: el horneado y el juego generan la misma malla)
    public bool lightmapUV;
    public Rect lmTile = new Rect(0f, 0f, 1f, 1f);   // zona del atlas para esta malla
    public int lmRes = 1024; public float lmPad = 3f;
    private struct Chart { public int v0, nv; public float w, h; public Vector2[] loc; }
    private readonly List<Chart> charts = new List<Chart>();
    public float LmTexelsPerMeter { get; private set; }
    private void AddChart(int v0, Vector2[] loc)
    {
        float w = 0f, h = 0f; foreach (Vector2 q in loc) { w = Mathf.Max(w, q.x); h = Mathf.Max(h, q.y); }
        charts.Add(new Chart { v0 = v0, nv = loc.Length, w = w, h = h, loc = loc });
    }

    public FieldMesh(int submeshes) { idx = new List<int>[submeshes]; for (int i = 0; i < submeshes; i++) idx[i] = new List<int>(); }

    public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, Vector3 uAxis, Vector3 vAxis)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { Vector3 t = b; b = d; d = t; }   // orden horario visto desde la normal
        int s = v.Count;
        foreach (Vector3 p in new[] { a, b, c, d }) { v.Add(p); n.Add(nrm); uv.Add(new Vector2(Vector3.Dot(p, uAxis), Vector3.Dot(p, vAxis)) * uvScale); }
        idx[sub].AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
        if (lightmapUV)
        {
            Vector3 eu = (b - a), ev = Vector3.Cross(nrm, eu).normalized; float L = eu.magnitude; eu /= Mathf.Max(L, 1e-6f);
            Vector2[] loc = new Vector2[4]; Vector3[] pts = { a, b, c, d }; float mu = 1e9f, mv = 1e9f;
            for (int i = 0; i < 4; i++) { loc[i] = new Vector2(Vector3.Dot(pts[i] - a, eu), Vector3.Dot(pts[i] - a, ev)); mu = Mathf.Min(mu, loc[i].x); mv = Mathf.Min(mv, loc[i].y); }
            for (int i = 0; i < 4; i++) loc[i] -= new Vector2(mu, mv);
            AddChart(s, loc);
        }
    }

    public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 nrm, Vector3 uAxis, Vector3 vAxis)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { Vector3 t = b; b = c; c = t; }
        int s = v.Count;
        foreach (Vector3 p in new[] { a, b, c }) { v.Add(p); n.Add(nrm); uv.Add(new Vector2(Vector3.Dot(p, uAxis), Vector3.Dot(p, vAxis)) * uvScale); }
        idx[sub].AddRange(new[] { s, s + 1, s + 2 });
        if (lightmapUV)
        {
            Vector3 eu = (b - a), ev = Vector3.Cross(nrm, eu).normalized; float L = eu.magnitude; eu /= Mathf.Max(L, 1e-6f);
            Vector2[] loc = new Vector2[3]; Vector3[] pts = { a, b, c }; float mu = 1e9f, mv = 1e9f;
            for (int i = 0; i < 3; i++) { loc[i] = new Vector2(Vector3.Dot(pts[i] - a, eu), Vector3.Dot(pts[i] - a, ev)); mu = Mathf.Min(mu, loc[i].x); mv = Mathf.Min(mv, loc[i].y); }
            for (int i = 0; i < 3; i++) loc[i] -= new Vector2(mu, mv);
            AddChart(s, loc);
        }
    }

    // caja alineada a los ejes (min, max), las 6 caras hacia fuera
    public void Box(int sub, Vector3 mn, Vector3 mx)
    {
        Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;
        Quad(sub, new Vector3(mn.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mx.z), Z, -X, Y); // +Z
        Quad(sub, new Vector3(mx.x, mn.y, mn.z), new Vector3(mn.x, mn.y, mn.z), new Vector3(mn.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mn.z), -Z, X, Y); // -Z
        Quad(sub, new Vector3(mx.x, mn.y, mx.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mx.y, mn.z), new Vector3(mx.x, mx.y, mx.z), X, Z, Y);  // +X
        Quad(sub, new Vector3(mn.x, mn.y, mn.z), new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mn.x, mx.y, mn.z), -X, -Z, Y); // -X
        Quad(sub, new Vector3(mn.x, mx.y, mn.z), new Vector3(mn.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mx.z), new Vector3(mx.x, mx.y, mn.z), Y, X, Z);  // +Y
        Quad(sub, new Vector3(mn.x, mn.y, mx.z), new Vector3(mn.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mn.z), new Vector3(mx.x, mn.y, mx.z), -Y, X, Z); // -Y
    }

    public Mesh ToMesh(string name)
    {
        Mesh m = new Mesh { name = name };
        if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
        if (lightmapUV && charts.Count > 0) m.SetUVs(1, PackLightmap());
        m.subMeshCount = idx.Length;
        for (int i = 0; i < idx.Length; i++) m.SetTriangles(idx[i], i);
        m.RecalculateBounds();
        m.RecalculateTangents();
        return m;
    }

    // estantes: cartas ordenadas por altura; se busca la densidad (texeles/m) más alta que cabe en lmRes x lmRes
    private List<Vector2> PackLightmap()
    {
        int[] order = new int[charts.Count]; for (int i = 0; i < order.Length; i++) order[i] = i;
        System.Array.Sort(order, (x, y) => { int c = charts[y].h.CompareTo(charts[x].h); return c != 0 ? c : x.CompareTo(y); });
        float area = 0f; foreach (Chart c in charts) area += (c.w + 0.05f) * (c.h + 0.05f);
        float dens = Mathf.Sqrt(lmRes * (float)lmRes * 0.8f / Mathf.Max(area, 1e-3f));
        Vector2[] org = new Vector2[charts.Count];
        for (int attempt = 0; attempt < 200; attempt++, dens *= 0.97f)
        {
            float x = lmPad, y = lmPad, rowH = 0f; bool ok = true;
            foreach (int ci in order)
            {
                Chart c = charts[ci]; float w = Mathf.Ceil(c.w * dens) + 1f, h = Mathf.Ceil(c.h * dens) + 1f;
                if (x + w + lmPad > lmRes) { x = lmPad; y += rowH + 2f * lmPad; rowH = 0f; }
                if (y + h + lmPad > lmRes || w + 2f * lmPad > lmRes) { ok = false; break; }
                org[ci] = new Vector2(x, y); x += w + 2f * lmPad; rowH = Mathf.Max(rowH, h);
            }
            if (ok) break;
        }
        LmTexelsPerMeter = dens;
        List<Vector2> uv2 = new List<Vector2>(new Vector2[v.Count]);
        for (int ci = 0; ci < charts.Count; ci++)
        {
            Chart c = charts[ci];
            for (int k = 0; k < c.nv; k++)
            {
                Vector2 px = org[ci] + new Vector2(0.5f, 0.5f) + c.loc[k] * dens;
                uv2[c.v0 + k] = new Vector2(lmTile.x + px.x / lmRes * lmTile.width, lmTile.y + px.y / lmRes * lmTile.height);
            }
        }
        return uv2;
    }
}
