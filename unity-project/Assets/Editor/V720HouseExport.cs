using System.IO;
using UnityEngine;

// v7.2.0: exporta las 6 mallas de casa del Campo infinito (3 plantillas x abierta/cerrada, FieldWorld.HouseMesh, con UV2 del atlas)
// para hornear su luz en Blender (Tools/field/house_bake.py, GPU del PC).
//   V720_HOUSE_OUT=/ruta/houses.bytes Unity -batchmode -nographics -quit -executeMethod V720HouseExport.Run
public static class V720HouseExport
{
    public static void Run()
    {
        string path = System.Environment.GetEnvironmentVariable("V720_HOUSE_OUT") ?? "/workspace/v720/houses.bytes";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            w.Write(6);
            for (int tpl = 0; tpl < 3; tpl++)
                for (int e = 0; e < 2; e++)
                {
                    Mesh m = FieldWorld.HouseMesh(tpl, e == 1);
                    Vector3[] v = m.vertices, n = m.normals; Vector2[] a = m.uv, b = m.uv2;
                    w.Write(FieldWorld.HouseVariant(tpl, e == 1)); w.Write(v.Length); w.Write(m.subMeshCount);
                    for (int i = 0; i < v.Length; i++)
                    {
                        w.Write(v[i].x); w.Write(v[i].y); w.Write(v[i].z); w.Write(n[i].x); w.Write(n[i].y); w.Write(n[i].z);
                        w.Write(a[i].x); w.Write(a[i].y); w.Write(b[i].x); w.Write(b[i].y);
                    }
                    for (int s = 0; s < m.subMeshCount; s++) { int[] t = m.GetTriangles(s); w.Write(t.Length); foreach (int x in t) w.Write(x); }
                    Debug.Log("[V720HouseExport] casa " + tpl + (e == 1 ? " abierta" : " cerrada") + ": " + v.Length + " vért, " + m.triangles.Length / 3 + " tri");
                    Object.DestroyImmediate(m);
                }
        }
        Debug.Log("[V720HouseExport] OK " + path);
    }
}
