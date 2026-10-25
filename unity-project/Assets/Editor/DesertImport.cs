using UnityEditor;
using UnityEngine;

// v7.7.0 Desierto Cálido: ajustes de importación de Assets/Resources/Desert (modelos y texturas CC0 de Poly Haven)
//  · *_n.jpg  → mapa de normales; *_m.png → lineal (R metal, G AO, A suavidad); plantas *_a.png → alfa con cobertura en los mips
//  · modelos: sin animación, legibles (colisiones de las rocas grandes) y con el giro de los triángulos corregido
public class DesertImport : AssetPostprocessor
{
    private bool InDesert { get { return assetPath.Replace('\\', '/').Contains("Assets/Resources/Desert/"); } }

    private void OnPreprocessTexture()
    {
        if (!InDesert) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        string p = assetPath.ToLowerInvariant();
        ti.anisoLevel = 4;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Repeat;
        ti.textureCompression = TextureImporterCompression.Compressed;
        ti.maxTextureSize = 2048;
        if (p.EndsWith("_n.jpg") || p.EndsWith("_n.png")) ti.textureType = TextureImporterType.NormalMap;
        else if (p.EndsWith("_m.png")) { ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.maxTextureSize = 512; }
        else if (p.EndsWith("_a.png"))
        {
            ti.alphaSource = TextureImporterAlphaSource.FromInput;
            ti.alphaIsTransparency = true;
            ti.mipMapsPreserveCoverage = true;
            ti.alphaTestReferenceValue = 0.5f;
        }
    }

    private void OnPreprocessModel()
    {
        if (!InDesert) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false;
        mi.isReadable = true; mi.meshCompression = ModelImporterMeshCompression.Off;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.importCameras = false; mi.importLights = false;
    }

    private void OnPostprocessModel(GameObject g)
    {
        if (!InDesert) return;
        foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true)) FieldPropImport.FixWinding(mf.sharedMesh);
    }
}
