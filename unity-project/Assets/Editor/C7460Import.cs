using UnityEditor;

// Nivel 7460: lightmaps horneados en Cycles (EXR HDR lineal) del pasillo y de la habitación: sin sRGB, BC6H, clamp, mipmaps.
public class C7460LightmapImport : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/C7460/") || !assetPath.EndsWith(".exr")) return;
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
