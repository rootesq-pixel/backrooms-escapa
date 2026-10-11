using System.IO;
using UnityEditor;
using UnityEngine;

// Ajustes de importación de las texturas horneadas con Blender (Assets/Resources/Textures):
//   *_a  albedo (sRGB)   *_n  normal (tipo NormalMap)   *_m  máscara Standard (lineal, alfa = suavidad)
// Mipmaps, anisotrópico x8, repetición, no legibles (memoria) y compresión con crunch para reducir el tamaño de la build.
public class TextureImportRules : AssetPostprocessor
{
    public const string Folder = "Assets/Resources/Textures/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder)) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        string n = Path.GetFileNameWithoutExtension(assetPath);
        bool normal = n.EndsWith("_n"), mask = n.EndsWith("_m");
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !(normal || mask);
        ti.alphaSource = mask ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = false;
        ti.mipmapEnabled = true;
        ti.streamingMipmaps = false;
        ti.anisoLevel = 8;
        ti.filterMode = FilterMode.Trilinear;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.isReadable = false;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 2048;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.crunchedCompression = true;
        ti.compressionQuality = 75;
    }
}
