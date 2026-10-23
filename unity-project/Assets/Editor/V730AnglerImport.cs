using UnityEngine;
using UnityEditor;

// v7.3.0: El Farolero (Blender): las aletas son finas y en sus bordes la normal suavizada queda del lado contrario de la cara.
// Al importar, esos triángulos reciben vértices propios con la normal de su cara (misma forma y UV).
public class V730AnglerImport : AssetPostprocessor
{
    void OnPostprocessModel(GameObject g)
    {
        if (!assetPath.Replace('\\', '/').StartsWith("Assets/Resources/Ocean/Angler/")) return;
        foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true))
            OceanGeo.SplitFlippedTriangles(mf.sharedMesh);
    }
}
