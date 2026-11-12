using UnityEditor;

// v7.12.0 Nivel 188: texturas del patio (Resources/W188): albedo sRGB, normal OpenGL como NormalMap, máscara lineal con alfa.
public class W188TextureImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string p = assetPath.Replace('\\', '/');
        if (!p.StartsWith("Assets/Resources/W188/")) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        string l = p.ToLowerInvariant();
        ti.mipmapEnabled = true; ti.anisoLevel = 4; ti.wrapMode = UnityEngine.TextureWrapMode.Repeat;
        ti.textureCompression = TextureImporterCompression.Compressed;
        if (l.EndsWith("_n.png")) { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; ti.maxTextureSize = 1024; }
        else if (l.EndsWith("_m.png")) { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.maxTextureSize = 1024; }
        else if (l.Contains("exitview")) { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.wrapMode = UnityEngine.TextureWrapMode.Clamp; ti.maxTextureSize = 1024; }
        else { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.maxTextureSize = 2048; }
    }
}
