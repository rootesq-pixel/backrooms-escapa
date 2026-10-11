using UnityEngine;
using UnityEngine.Rendering;

// Utilidades estáticas para crear materiales y primitivas desde código.
// Así el proyecto no necesita ningún asset externo (modelos, texturas, sonidos).
// Las texturas están en ProceduralTextures.cs y el audio en ProceduralAudio.cs.
public static class ProceduralAssets
{
    // Fuente por defecto de la UI (se cachea para no pedirla varias veces)
    private static Font cachedFont;

    // Busca un shader "Lit" compatible con el pipeline actual.
    // Built-in => "Standard". Si el proyecto usa URP => "Universal Render Pipeline/Lit".
    public static Shader FindLitShader()
    {
        string[] candidates;
        if (GraphicsSettings.currentRenderPipeline != null)
        {
            candidates = new string[] { "Universal Render Pipeline/Lit", "Standard", "Sprites/Default" };
        }
        else
        {
            candidates = new string[] { "Standard", "Legacy Shaders/Diffuse", "Sprites/Default" };
        }

        for (int i = 0; i < candidates.Length; i++)
        {
            Shader s = Shader.Find(candidates[i]);
            if (s != null)
            {
                return s;
            }
        }
        return null;
    }

    // Crea un material con color, textura opcional (con tiling) y brillo.
    public static Material CreateMaterial(Color color, Texture2D texture, Vector2 tiling, float smoothness)
    {
        Shader shader = FindLitShader();
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material m = new Material(shader);
        m.color = color;

        if (texture != null)
        {
            m.mainTexture = texture;
            m.mainTextureScale = tiling;
        }

        // Standard usa _Glossiness y URP usa _Smoothness
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
        return m;
    }

    // Material PBR (Standard) con albedo + normal map + máscara (metálico/oclusión/suavidad).
    // Las UV ya vienen escaladas en metros desde MeshBuilder, así que el tiling del material es (1,1).
    public static Material CreateSurfaceMaterial(SurfaceSet set, Color tint, float normalScale, float occlusionStrength)
    {
        Material m = CreateMaterial(tint, set != null ? set.albedo : null, Vector2.one, 0.1f);
        if (set == null) return m;

        if (m.HasProperty("_BumpMap") && set.normal != null)
        {
            m.SetTexture("_BumpMap", set.normal);
            m.SetFloat("_BumpScale", normalScale);
            m.EnableKeyword("_NORMALMAP");
        }
        if (m.HasProperty("_MetallicGlossMap") && set.mask != null)
        {
            m.SetTexture("_MetallicGlossMap", set.mask);
            m.SetFloat("_GlossMapScale", 1f);
            m.EnableKeyword("_METALLICGLOSSMAP");
        }
        if (m.HasProperty("_OcclusionMap") && set.mask != null)
        {
            m.SetTexture("_OcclusionMap", set.mask);
            m.SetFloat("_OcclusionStrength", occlusionStrength);
        }
        return m;
    }

    // Material semitransparente (modo "Transparent" del Standard: la especular no se atenúa)
    public static Material CreateWaterMaterial(SurfaceSet ripples, Color color)
    {
        Material m = CreateMaterial(color, null, Vector2.one, 0.95f);
        if (m.HasProperty("_Mode"))
        {
            m.SetFloat("_Mode", 3f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.One);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        if (ripples != null && m.HasProperty("_BumpMap") && ripples.normal != null)
        {
            m.SetTexture("_BumpMap", ripples.normal);
            m.SetFloat("_BumpScale", 0.6f);
            m.EnableKeyword("_NORMALMAP");
        }
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.1f);
        return m;
    }

    // Activa la emisión de un material (para paneles fluorescentes, ojos del monstruo, puerta...)
    public static void SetEmission(Material m, Color emission)
    {
        if (m != null && m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
    }

    // Fuente integrada de Unity para el texto legacy (UI)
    public static Font GetDefaultFont()
    {
        if (cachedFont == null)
        {
            // Desde Unity 2022.2 "Arial.ttf" ya no existe: se usa "LegacyRuntime.ttf"
            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }
        if (cachedFont == null)
        {
            cachedFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
        }
        return cachedFont;
    }

    // Crea una primitiva (cubo, cápsula...) como hijo de un transform, con material y sombras desactivadas.
    public static GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent,
        Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null)
        {
            if (material != null) r.sharedMaterial = material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        if (!keepCollider)
        {
            Collider c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c); // válido también en modo edición (tests)
        }
        return go;
    }

    // Destruye un objeto de Unity tanto en juego como en modo edición
    public static void SafeDestroy(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}
