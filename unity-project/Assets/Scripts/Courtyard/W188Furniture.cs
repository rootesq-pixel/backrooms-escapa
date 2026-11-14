using System.Collections.Generic;
using UnityEngine;

// v7.12.1 Nivel 188: muebles de las habitaciones modelados en Blender (Tools/w188/room_build.py -> Resources/W188/W188Room.fbx):
// cama con somier, cabecero tapizado, colchón, almohadas y colcha que cae por los lados; mesilla, lamparita con pantalla de tela,
// armario, escritorio, silla, alfombra con cenefa y flecos, radiador de columnas, lámpara de techo, marco del cuadro y barra de la
// cortina. Como HotelFurniture: no se instancian; sus vértices se copian en las mallas por planta de W188World (sin objetos ni
// draw calls nuevos; la colisión sigue siendo la de cajas de antes). Cada objeto del FBX se llama <Modelo>__<Material>; las
// coordenadas del FBX (ya importado) son x = u - a (medianera de la cama), y arriba, z = d; en el juego el marco del lado tiene
// z = -d, así que se colocan con FlipD (espejo en z: Append invierte el orden de los triángulos y el signo de la tangente).
public static class W188Furniture
{
    public const string Resource = "W188/W188Room";
    struct Part { public string mat; public Vector3[] v, n; public Vector4[] t; public Vector2[] uv; public int[] tri; }
    static Dictionary<string, List<Part>> models;
    public static bool Available { get { Load(); return models.Count > 0; } }
    public static int ModelCount { get { Load(); return models.Count; } }
    public static bool Has(string model) { Load(); return models.ContainsKey(model); }
    public static int TriCount(string model) { Load(); int n = 0; if (models.TryGetValue(model, out var l)) foreach (Part p in l) n += p.tri.Length / 3; return n; }
    public static readonly Matrix4x4 FlipD = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
    public static IEnumerable<string> Models { get { Load(); return models.Keys; } }

    static void Load()
    {
        if (models != null) return;
        models = new Dictionary<string, List<Part>>();
        GameObject root = Resources.Load<GameObject>(Resource);
        if (root == null) { Debug.LogWarning("W188Furniture: falta " + Resource); return; }
        Matrix4x4 inv = root.transform.worldToLocalMatrix;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh m = mf.sharedMesh; if (m == null || !m.isReadable) continue;
            string nm = mf.gameObject.name; int k = nm.IndexOf("__"); if (k < 0) continue;
            string model = nm.Substring(0, k), mat = nm.Substring(k + 2); int dot = mat.IndexOf('.'); if (dot >= 0) mat = mat.Substring(0, dot);
            Matrix4x4 M = inv * mf.transform.localToWorldMatrix, N = M.inverse.transpose;
            Vector3[] v = m.vertices, n = m.normals; Vector4[] t = m.tangents; Vector2[] uv = m.uv;
            for (int i = 0; i < v.Length; i++) v[i] = M.MultiplyPoint3x4(v[i]);
            for (int i = 0; i < n.Length; i++) n[i] = N.MultiplyVector(n[i]).normalized;
            for (int i = 0; i < t.Length; i++) { Vector3 d = M.MultiplyVector(new Vector3(t[i].x, t[i].y, t[i].z)).normalized; t[i] = new Vector4(d.x, d.y, d.z, t[i].w); }
            if (!models.TryGetValue(model, out List<Part> l)) models[model] = l = new List<Part>();
            l.Add(new Part { mat = mat, v = v, n = n, t = t, uv = uv, tri = m.triangles });
        }
    }

    // copia el modelo en b con la matriz M; matIndex traduce el nombre del material del FBX a la submalla (-1: se omite)
    public static bool Append(MeshBuilder b, string model, Matrix4x4 M, System.Func<string, int> matIndex)
    {
        Load();
        if (!models.TryGetValue(model, out List<Part> parts)) return false;
        Matrix4x4 N = M.inverse.transpose;
        bool mirror = M.determinant < 0f;
        foreach (Part p in parts)
        {
            int s = matIndex(p.mat); if (s < 0 || s >= b.tris.Length) continue;
            int o = b.verts.Count;
            for (int i = 0; i < p.v.Length; i++)
            {
                b.verts.Add(M.MultiplyPoint3x4(p.v[i]));
                b.normals.Add(i < p.n.Length ? N.MultiplyVector(p.n[i]).normalized : Vector3.up);
                if (i < p.t.Length) { Vector3 d = M.MultiplyVector(new Vector3(p.t[i].x, p.t[i].y, p.t[i].z)).normalized; b.tangents.Add(new Vector4(d.x, d.y, d.z, mirror ? -p.t[i].w : p.t[i].w)); }
                else b.tangents.Add(new Vector4(1, 0, 0, 1));
                b.uvs.Add(i < p.uv.Length ? p.uv[i] : Vector2.zero);
            }
            List<int> tr = b.tris[s];
            if (!mirror) for (int i = 0; i < p.tri.Length; i++) tr.Add(o + p.tri[i]);
            else for (int i = 0; i + 2 < p.tri.Length; i += 3) { tr.Add(o + p.tri[i]); tr.Add(o + p.tri[i + 2]); tr.Add(o + p.tri[i + 1]); }
        }
        return true;
    }
}
