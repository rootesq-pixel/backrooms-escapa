using UnityEngine;
using UnityEditor;

// v7.3.0: El Farolero, el sofá y la barca (Blender): las aletas son finas y en sus bordes la normal suavizada queda del lado contrario de la cara.
// Al importar, esos triángulos reciben vértices propios con la normal de su cara (misma forma y UV).
public class V730AnglerImport : AssetPostprocessor
{
    void OnPostprocessModel(GameObject g)
    {
        string ap = assetPath.Replace('\\', '/');
        if (!ap.StartsWith("Assets/Resources/Ocean/Angler/") && !ap.StartsWith("Assets/Resources/Ocean/Props/") && !ap.StartsWith("Assets/Resources/Ocean/Sculpt/")) return;
        foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true))
            OceanGeo.SplitFlippedTriangles(mf.sharedMesh);
    }
}
