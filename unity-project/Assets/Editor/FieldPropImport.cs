using UnityEditor;
using UnityEngine;

// v7.0.1: modelos de Assets/Resources/Field/Props (Blender, modificador Skin + decimado): sin animación, y los pocos
// triángulos plegados en las horquillas de las ramas se reordenan para que su giro coincida con la normal suavizada.
public class FieldPropImport : AssetPostprocessor
{
    void OnPreprocessModel()
    {
        if (!assetPath.Contains("Resources/Field/Props/")) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false;
        mi.isReadable = true; mi.meshCompression = ModelImporterMeshCompression.Off;
    }

    void OnPostprocessModel(GameObject g)
    {
        if (!assetPath.Contains("Resources/Field/Props/")) return;
        foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true)) FixWinding(mf.sharedMesh);
    }

    public static int FixWinding(Mesh m)
    {
        if (m == null) return 0;
        Vector3[] v = m.vertices, n = m.normals; int fixedN = 0;
        for (int s = 0; s < m.subMeshCount; s++)
        {
            int[] t = m.GetTriangles(s);
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 gn = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
                if (Vector3.Dot(gn, n[t[i]]) > 0f) continue;
                int a = t[i], b = t[i + 1], c = t[i + 2];
                if (Vector3.Dot(gn, n[b]) > 0f) { t[i] = b; t[i + 1] = c; t[i + 2] = a; }
                else if (Vector3.Dot(gn, n[c]) > 0f) { t[i] = c; t[i + 1] = a; t[i + 2] = b; }
                else { t[i + 1] = c; t[i + 2] = b; }
                fixedN++;
            }
            m.SetTriangles(t, s);
        }
        return fixedN;
    }
}
