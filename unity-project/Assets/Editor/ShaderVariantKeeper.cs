using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// El juego crea sus materiales Standard en runtime y activa palabras clave (_NORMALMAP, _METALLICGLOSSMAP,
// _EMISSION, _ALPHAPREMULTIPLY_ON). En un build, Unity elimina las variantes de shader que ningún material
// del proyecto usa. Este generador crea materiales en Assets/Resources/ShaderVariantKeeper con todas las
// combinaciones usadas, para que sus variantes se incluyan siempre en el build.
public static class ShaderVariantKeeper
{
    private const string Folder = "Assets/Resources/ShaderVariantKeeper";

    [MenuItem("Backrooms/Generar materiales de variantes de shader")]
    public static void Generate()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogError("[ShaderVariantKeeper] No se encontró el shader Standard");
            return;
        }

        if (!Directory.Exists(Folder)) Directory.CreateDirectory(Folder);

        int created = 0;
        for (int mask = 0; mask < 8; mask++)
        {
            bool normal = (mask & 1) != 0, gloss = (mask & 2) != 0, emission = (mask & 4) != 0;
            Material m = new Material(standard);
            if (normal) m.EnableKeyword("_NORMALMAP");
            if (gloss) m.EnableKeyword("_METALLICGLOSSMAP");
            if (emission)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", Color.white);
            }
            Save(m, "Opaque_N" + (normal ? 1 : 0) + "_G" + (gloss ? 1 : 0) + "_E" + (emission ? 1 : 0));
            created++;
        }

        // v7.4.0: detalle x2 (oclusión horneada de la cabina del ascensor en UV2)
        for (int mask = 0; mask < 8; mask++)
        {
            bool normal = (mask & 1) != 0, gloss = (mask & 2) != 0, emission = (mask & 4) != 0;
            Material m = new Material(standard);
            if (normal) m.EnableKeyword("_NORMALMAP");
            if (gloss) m.EnableKeyword("_METALLICGLOSSMAP");
            if (emission) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.white); }
            m.EnableKeyword("_DETAIL_MULX2"); m.SetFloat("_UVSec", 1f);
            Save(m, "Opaque_N" + (normal ? 1 : 0) + "_G" + (gloss ? 1 : 0) + "_E" + (emission ? 1 : 0) + "_D1");
            created++;
        }

        // v7.7.0: recorte por alfa (matas del Desierto Cálido), con y sin normal map / máscara
        for (int mask = 0; mask < 4; mask++)
        {
            bool normal = (mask & 1) != 0, gloss = (mask & 2) != 0;
            Material m = new Material(standard);
            m.SetFloat("_Mode", 1f); m.SetOverrideTag("RenderType", "TransparentCutout");
            m.EnableKeyword("_ALPHATEST_ON"); m.renderQueue = 2450;
            if (normal) m.EnableKeyword("_NORMALMAP");
            if (gloss) m.EnableKeyword("_METALLICGLOSSMAP");
            Save(m, "Cutout_N" + (normal ? 1 : 0) + "_G" + (gloss ? 1 : 0));
            created++;
        }

        // Transparente premultiplicado (agua), con y sin normal map; v7.4.0: + máscara de brillo (calcomanías del aparcamiento)
        for (int n = 0; n < 3; n++)
        {
            Material m = new Material(standard);
            m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            if (n == 1) m.EnableKeyword("_NORMALMAP");
            if (n == 2) m.EnableKeyword("_METALLICGLOSSMAP");
            m.renderQueue = (int)RenderQueue.Transparent;
            Save(m, n == 2 ? "Transparent_N0_G1" : "Transparent_N" + n);
            created++;
        }

        // Efecto VHS: el shader vive en Resources/VHS y se referencia desde un material para que nunca se elimine del build
        Shader vhs = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/VHS/VhsPost.shader");
        if (vhs != null)
        {
            Directory.CreateDirectory("Assets/Resources/VHS");
            string vp = "Assets/Resources/VHS/VhsPostKeeper.mat";
            if (File.Exists(vp)) AssetDatabase.DeleteAsset(vp);
            AssetDatabase.CreateAsset(new Material(vhs), vp);
            created++;
        }
        else Debug.LogWarning("[ShaderVariantKeeper] No se encontró Assets/Resources/VHS/VhsPost.shader");

        // v7.1.2: bolas instanciadas del Parque de bolas (el material se crea en runtime a partir de este .mat: sin él el build
        // quitaba la variante INSTANCING_ON y en el ejecutable no se dibujaba ninguna bola)
        Shader balls = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/Ballpit/BallInstanced.shader");
        if (balls != null)
        {
            string bp = "Assets/Resources/Ballpit/BallInstancedKeeper.mat";
            Material ex = AssetDatabase.LoadAssetAtPath<Material>(bp);   // se conserva (mismo GUID) y solo se corrige
            if (ex == null) AssetDatabase.CreateAsset(new Material(balls) { enableInstancing = true }, bp);
            else { ex.shader = balls; ex.enableInstancing = true; EditorUtility.SetDirty(ex); }
            created++;
        }
        else Debug.LogError("[ShaderVariantKeeper] No se encontró Assets/Resources/Ballpit/BallInstanced.shader");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[ShaderVariantKeeper] Materiales de variantes: " + created);
    }

    private static void Save(Material m, string name)
    {
        string path = Folder + "/" + name + ".mat";
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
    }
}
