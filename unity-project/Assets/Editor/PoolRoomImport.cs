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

// v6.8: lightmap horneado (EXR HDR lineal) de la sala fija: sin sRGB, BC6H, clamp, mipmaps.
public class PoolRoomLightmapImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/PoolRoom/") || !assetPath.EndsWith(".exr")) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = false;
        ti.mipmapEnabled = true;
        ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
        ti.filterMode = UnityEngine.FilterMode.Trilinear;
        ti.anisoLevel = 4;
        ti.maxTextureSize = 4096;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.isReadable = false;
    }
}
