using System.Collections.Generic;
using UnityEngine;

// v7.5.0: mallas de los muebles del hotel modelados en Blender (Tools/v750/build_hotel_furn.py -> Resources/Hotel/HotelFurniture750.fbx):
// mesas redondas de banquete y cuadradas de comedor con manteles caídos, sillas de comedor y de banquete, columna clásica y mostrador
// de recepción. No se instancian: sus vértices se copian en los bloques combinados de HotelDecor (misma colisión, NavMesh y huella
// que antes; el mismo resultado en todos los equipos, así que el multijugador no cambia). Cada objeto del FBX se llama
// <Modelo>__<Material>.
public static class HotelFurniture
{
    public const string Resource = "Hotel/HotelFurniture750";
    public struct Part { public string mat; public Vector3[] v, n; public Vector4[] t; public Vector2[] uv; public int[] tri; }
    static Dictionary<string, List<Part>> models;
    public static int ModelCount { get { Load(); return models.Count; } }
    public static int TriCount(string model) { Load(); int n = 0; if (models.TryGetValue(model, out var l)) foreach (Part p in l) n += p.tri.Length / 3; return n; }
    public static bool Has(string model) { Load(); return models.ContainsKey(model); }

    static void Load()
    {
        if (models != null) return;
        models = new Dictionary<string, List<Part>>();
        GameObject root = Resources.Load<GameObject>(Resource);
        if (root == null) { Debug.LogWarning("HotelFurniture: falta " + Resource); return; }
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

    // copia el modelo en b con la matriz M; matIndex traduce el nombre del material; uvOff desplaza las UV de los manteles
    public static bool Append(MeshBuilder b, string model, Matrix4x4 M, System.Func<string, int> matIndex, Vector2 uvOff)
    {
        Load();
        if (!models.TryGetValue(model, out List<Part> parts)) return false;
        Matrix4x4 N = M.inverse.transpose;
        foreach (Part p in parts)
        {
            int s = matIndex(p.mat); if (s < 0) continue;
            int o = b.verts.Count; bool shift = p.mat == "TCloth";
            for (int i = 0; i < p.v.Length; i++)
            {
                b.verts.Add(M.MultiplyPoint3x4(p.v[i]));
                b.normals.Add(i < p.n.Length ? N.MultiplyVector(p.n[i]).normalized : Vector3.up);
                if (i < p.t.Length) { Vector3 d = M.MultiplyVector(new Vector3(p.t[i].x, p.t[i].y, p.t[i].z)).normalized; b.tangents.Add(new Vector4(d.x, d.y, d.z, p.t[i].w)); }
                else b.tangents.Add(new Vector4(1, 0, 0, 1));
                Vector2 u = i < p.uv.Length ? p.uv[i] : Vector2.zero; b.uvs.Add(shift ? u + uvOff : u);
            }
            List<int> tr = b.tris[s];
            for (int i = 0; i < p.tri.Length; i++) tr.Add(o + p.tri[i]);
        }
        return true;
    }
}
