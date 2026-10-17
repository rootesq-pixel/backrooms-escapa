using System.IO;
using UnityEditor;
using UnityEngine;

// v7.0.2: ajustes de importación de los modelos de objetos (Resources/Items) y creación de sus materiales Standard PBR.
//   Unity -batchmode -quit -executeMethod ItemModelImport.Run
public class ItemModelImport : AssetPostprocessor
{
    public static readonly string[] Ids = { "Battery", "AlmondWater", "Glowstick", "VhsTape", "Note", "Flashlight" };
    const string Dir = "Assets/Resources/Items/";

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Dir)) return;
        ModelImporter mi = (ModelImporter)assetImporter;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
        mi.importCameras = false; mi.importLights = false; mi.addCollider = false;
        mi.globalScale = 1f; mi.useFileScale = true;
        mi.importNormals = ModelImporterNormals.Import; mi.importTangents = ModelImporterTangents.CalculateMikk;
        mi.meshCompression = ModelImporterMeshCompression.Off; mi.isReadable = false;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Dir)) return;
        TextureImporter ti = (TextureImporter)assetImporter;
        ti.mipmapEnabled = true; ti.maxTextureSize = 512;
        ti.textureCompression = TextureImporterCompression.Compressed;
        if (assetPath.EndsWith("_normal.png")) { ti.textureType = TextureImporterType.NormalMap; ti.sRGBTexture = false; }
        else if (assetPath.EndsWith("_ms.png")) { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.FromInput; }
        else { ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; }
    }

    public static void Run()
    {
        AssetDatabase.Refresh();
        foreach (string id in Ids)
        {
            foreach (string suf in new[] { ".fbx", "_albedo.png", "_ms.png", "_normal.png", "_emit.png" })
                if (File.Exists(Dir + id + suf)) AssetDatabase.ImportAsset(Dir + id + suf, ImportAssetOptions.ForceUpdate);
            string mp = Dir + id + "_mat.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (m == null) { m = new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(m, mp); }
            m.shader = Shader.Find("Standard");
            m.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + id + "_albedo.png"));
            m.color = Color.white;
            m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + id + "_ms.png"));
            m.EnableKeyword("_METALLICGLOSSMAP"); m.SetFloat("_GlossMapScale", 1f); m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + id + "_normal.png"));
            m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f);
            Texture2D em = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + id + "_emit.png");
            // emisión: la propia textura (o el mapa de emisión) para el brillo sutil; el color lo controla ItemHighlight
            m.SetTexture("_EmissionMap", em != null ? em : AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + id + "_albedo.png"));
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[ItemModelImport] OK");
    }
}
