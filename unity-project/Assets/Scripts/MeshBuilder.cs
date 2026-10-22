using System.Collections.Generic;
using UnityEngine;

// Constructor de mallas con UV en coordenadas de MUNDO (metros / tamaño de baldosa).
// Así una sola textura se repite con la escala correcta en cualquier pared, suelo o techo,
// sin importar sus dimensiones (equivalente a un mapeado "triplanar" de ejes fijos).
// Cada cara lleva normal y tangente propios (para el normal map del shader Standard).
public class MeshBuilder
{
    public readonly List<Vector3> verts = new List<Vector3>();
    public readonly List<Vector3> normals = new List<Vector3>();
    public readonly List<Vector4> tangents = new List<Vector4>();
    public readonly List<Vector2> uvs = new List<Vector2>();
    public readonly List<int>[] tris;

    public MeshBuilder(int submeshCount)
    {
        tris = new List<int>[submeshCount];
        for (int i = 0; i < submeshCount; i++) tris[i] = new List<int>();
    }

    public int VertexCount { get { return verts.Count; } }

    public bool IsEmpty
    {
        get { return verts.Count == 0; }
    }

    // Quad plano. p0..p3 en orden alrededor del borde; n = normal hacia fuera.
    // uAxis / vAxis: ejes del mundo (unitarios) a lo largo de los que crecen u y v.
    // tileU / tileV: metros por repetición de la textura. uOff / vOff: desplazamiento (en repeticiones).
    public void AddQuad(int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n,
        Vector3 uAxis, Vector3 vAxis, float tileU, float tileV, float uOff, float vOff)
    {
        int b = verts.Count;
        Vector3[] p = { p0, p1, p2, p3 };

        // w de la tangente: cross(n, t) * w debe apuntar hacia +v
        float w = Vector3.Dot(Vector3.Cross(n, uAxis), vAxis) >= 0f ? 1f : -1f;
        for (int i = 0; i < 4; i++)
        {
            verts.Add(p[i]);
            normals.Add(n);
            tangents.Add(new Vector4(uAxis.x, uAxis.y, uAxis.z, w));
            uvs.Add(new Vector2(Vector3.Dot(p[i], uAxis) / tileU + uOff, Vector3.Dot(p[i], vAxis) / tileV + vOff));
        }

        // Unity: la cara frontal es la de sentido horario; cross(p1-p0, p2-p0) apunta hacia el observador
        Vector3 tn = Vector3.Cross(p1 - p0, p2 - p0);
        List<int> t = tris[sub];
        bool tri = (p3 - p2).sqrMagnitude < 1e-10f;   // v7.3.0: triángulo (quad degenerado): sin la segunda mitad vacía
        if (Vector3.Dot(tn, n) >= 0f)
        {
            t.Add(b); t.Add(b + 1); t.Add(b + 2);
            if (!tri) { t.Add(b); t.Add(b + 2); t.Add(b + 3); }
        }
        else
        {
            t.Add(b); t.Add(b + 2); t.Add(b + 1);
            if (!tri) { t.Add(b); t.Add(b + 3); t.Add(b + 2); }
        }
    }

    // Caja vertical (pared o pilar) con 4 caras laterales. UV: u a lo largo de la cara,
    // v = y / tileV (si tileV <= 0 se normaliza a la altura: v = y / height).
    public void AddWallBox(int sub, Vector3 center, Vector3 size, float tileU, float tileV, float uOff)
    {
        float h = size.y;
        float y0 = center.y - h * 0.5f, y1 = center.y + h * 0.5f;
        float x0 = center.x - size.x * 0.5f, x1 = center.x + size.x * 0.5f;
        float z0 = center.z - size.z * 0.5f, z1 = center.z + size.z * 0.5f;
        float tv = tileV > 0f ? tileV : h;

        // Cara +Z (u a lo largo de +X, vista desde fuera hacia -Z)
        AddQuad(sub, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1),
            Vector3.forward, Vector3.right, Vector3.up, tileU, tv, uOff, 0f);
        // Cara -Z
        AddQuad(sub, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0),
            Vector3.back, Vector3.right, Vector3.up, tileU, tv, uOff + 0.37f, 0f);
        // Cara +X (u a lo largo de +Z)
        AddQuad(sub, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0),
            Vector3.right, Vector3.forward, Vector3.up, tileU, tv, uOff + 0.61f, 0f);
        // Cara -X
        AddQuad(sub, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0),
            Vector3.left, Vector3.forward, Vector3.up, tileU, tv, uOff + 0.13f, 0f);
    }

    // Tubo octogonal entre dos puntos (tuberías)
    public void AddTube(int sub, Vector3 a, Vector3 b, float radius, float tileU)
    {
        Vector3 axis = (b - a);
        float len = axis.magnitude;
        if (len < 0.01f) return;
        axis /= len;
        Vector3 side = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.right : Vector3.Cross(axis, Vector3.up).normalized;
        Vector3 up2 = Vector3.Cross(side, axis).normalized;
        const int seg = 8;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            Vector3 n0 = side * Mathf.Cos(a0) + up2 * Mathf.Sin(a0);
            Vector3 n1 = side * Mathf.Cos(a1) + up2 * Mathf.Sin(a1);
            Vector3 nm = (n0 + n1).normalized;
            // u a lo largo del eje; v alrededor del tubo (uso del eje 'tangente' aprox.)
            Vector3 tangentV = Vector3.Cross(nm, axis).normalized;
            AddQuad(sub, a + n0 * radius, b + n0 * radius, b + n1 * radius, a + n1 * radius, nm,
                axis, tangentV, tileU, 0.5f, 0f, i * 0.125f);
        }
    }

    // Construye la malla final; cada submalla es un material
    public Mesh ToMesh(string name)
    {
        Mesh m = new Mesh();
        m.name = name;
        if (verts.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(verts);
        m.SetNormals(normals);
        m.SetTangents(tangents);
        m.SetUVs(0, uvs);
        m.subMeshCount = tris.Length;
        for (int i = 0; i < tris.Length; i++) m.SetTriangles(tris[i], i);
        m.RecalculateBounds();
        return m;
    }
}
