using UnityEngine;
using UnityEditor;

// v7.3.0: comprobación rápida del modelo nuevo del Farolero (jerarquía, orientación, mallas legibles para las aletas)
public static class V730AnglerProbe
{
    public static void Run()
    {
        string p = "Assets/Resources/Ocean/Angler/Farolero.fbx";
        ModelImporter mi = AssetImporter.GetAtPath(p) as ModelImporter;
        if (mi != null && (!mi.isReadable || mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation))
        {
            mi.isReadable = true; mi.materialImportMode = ModelImporterMaterialImportMode.None; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.SaveAndReimport();
        }
        GameObject g = AssetDatabase.LoadAssetAtPath<GameObject>(p);
        if (g == null) { Debug.LogError("[AnglerProbe] FAIL sin modelo"); return; }
        GameObject inst = Object.Instantiate(g);
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            Debug.Log("[AnglerProbe] " + r.name + " parent " + r.transform.parent.name + " lpos " + r.transform.localPosition.ToString("F2") + " lrot " + r.transform.localEulerAngles.ToString("F1") + " lscale " + r.transform.localScale.ToString("F2") +
                      " center " + r.bounds.center.ToString("F2") + " size " + r.bounds.size.ToString("F2") + " readable " + (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable) + " tris " + (mf != null && mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0));
        }
        Object.DestroyImmediate(inst);
        Debug.Log("[AnglerProbe] OK");
    }
}
