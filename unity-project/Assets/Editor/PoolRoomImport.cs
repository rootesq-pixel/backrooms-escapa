using UnityEditor;

// Importación del set de piscina de Blender (Assets/Resources/PoolRoom): malla legible (MeshCollider en runtime), sin materiales importados.
public class PoolRoomImport : AssetPostprocessor
{
    private void OnPreprocessModel()
    {
        if (!assetPath.StartsWith("Assets/Resources/PoolRoom/")) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        mi.isReadable = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = false;
        mi.importCameras = false; mi.importLights = false;
        mi.generateSecondaryUV = false;
        mi.globalScale = 1f;
        mi.importNormals = ModelImporterNormals.Import;
    }
}
