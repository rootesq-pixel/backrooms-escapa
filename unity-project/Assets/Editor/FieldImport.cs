using UnityEditor;
using UnityEngine;

// v7.0 Campo infinito: ajustes de importación de Resources/Field (texturas escaneadas CC0 de Poly Haven y sonidos de Kenney)
public class FieldImport : AssetPostprocessor
{
    private bool InField { get { return assetPath.Replace('\\', '/').Contains("Assets/Resources/Field/"); } }

    private void OnPreprocessTexture()
    {
        if (!InField) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        string p = assetPath.ToLowerInvariant();
        ti.anisoLevel = 4;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.textureCompression = TextureImporterCompression.Compressed;
        if (p.EndsWith("_rgba.png"))
        {
            // matas de hierba: el alfa conserva la cobertura en los mips (si no, la hierba lejana «se adelgaza» y desaparece)
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = 0.45f;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.maxTextureSize = 1024;
        }
        else if (p.EndsWith("_n.jpg"))
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.maxTextureSize = 2048;
        }
        else if (p.EndsWith("_m.jpg"))
        {
            ti.sRGBTexture = false;
            ti.maxTextureSize = 1024;
        }
        else if (p.EndsWith(".hdr"))
        {
            ti.mipmapEnabled = false;
            ti.wrapModeU = TextureWrapMode.Repeat;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.maxTextureSize = 2048;
        }
        else ti.maxTextureSize = 2048;
    }

    private void OnPreprocessAudio()
    {
        if (!InField) return;
        AudioImporter ai = (AudioImporter)assetImporter;
        AudioImporterSampleSettings s = ai.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad;
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.7f;
        ai.defaultSampleSettings = s;
        ai.forceToMono = true;
    }
}
