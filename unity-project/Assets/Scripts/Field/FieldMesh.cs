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

    public FieldMesh(int submeshes) { idx = new List<int>[submeshes]; for (int i = 0; i < submeshes; i++) idx[i] = new List<int>(); }

    public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nrm, Vector3 uAxis, Vector3 vAxis)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { Vector3 t = b; b = d; d = t; }   // orden horario visto desde la normal
        int s = v.Count;
        foreach (Vector3 p in new[] { a, b, c, d }) { v.Add(p); n.Add(nrm); uv.Add(new Vector2(Vector3.Dot(p, uAxis), Vector3.Dot(p, vAxis)) * uvScale); }
        idx[sub].AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 });
    }

    public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 nrm, Vector3 uAxis, Vector3 vAxis)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), nrm) < 0f) { Vector3 t = b; b = c; c = t; }
        int s = v.Count;
        foreach (Vector3 p in new[] { a, b, c }) { v.Add(p); n.Add(nrm); uv.Add(new Vector2(Vector3.Dot(p, uAxis), Vector3.Dot(p, vAxis)) * uvScale); }
        idx[sub].AddRange(new[] { s, s + 1, s + 2 });
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
        m.subMeshCount = idx.Length;
        for (int i = 0; i < idx.Length; i++) m.SetTriangles(idx[i], i);
        m.RecalculateBounds();
        m.RecalculateTangents();
        return m;
    }
}
