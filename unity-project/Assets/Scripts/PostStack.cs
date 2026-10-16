using UnityEngine;

// v6.9: post-procesado tipo POOLS en built-in (sin paquetes): bloom suave, AO en pantalla, ACES y gradación.
// Lo llama VhsEffect.OnRenderImage antes del efecto VHS. Por calidad: Bajo = apagado, Medio = bloom + ACES + gradación,
// Alto = además AO. Activo en los niveles que lo piden (Poolrooms) y desactivable con GameSettings.PostFx.
public static class PostStack
{
    public static bool LevelActive;          // lo pone GameBootstrap según el nivel
    public static float Exposure = 0.78f, Contrast = 1.08f, Saturation = 0.86f, BloomInt = 0.10f, Threshold = 1.0f, AOInt = 0.55f;
    public static Color Tint = new Color(1.0f, 1.0f, 1.0f), Lift = new Color(0.010f, 0.016f, 0.020f);
    private static Material mat;
    private static readonly RenderTexture[] mips = new RenderTexture[6];

    public static bool Enabled { get { return LevelActive && GameSettings.PostFx && GameSettings.Quality >= 1 && Material != null; } }
    public static bool AOEnabled { get { return GameSettings.Quality >= 2; } }

    public static Material Material
    {
        get
        {
            if (mat == null)
            {
                Shader s = Resources.Load<Shader>("Post/BackroomsPost");
                if (s == null || !s.isSupported) return null;
                mat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            }
            return mat;
        }
    }

    // src -> dst con bloom/AO/ACES
    public static void Render(RenderTexture src, RenderTexture dst, Camera cam)
    {
        Material m = Material;
        bool ao = AOEnabled && cam != null && (cam.depthTextureMode & DepthTextureMode.Depth) != 0;
        m.SetFloat("_Threshold", Threshold); m.SetFloat("_Knee", 0.5f); m.SetFloat("_BloomInt", BloomInt);
        m.SetFloat("_Exposure", Exposure); m.SetFloat("_Contrast", Contrast); m.SetFloat("_Saturation", Saturation);
        m.SetColor("_Tint", Tint); m.SetColor("_Lift", Lift); m.SetFloat("_ToneOn", 1f);
        m.SetFloat("_AOOn", ao ? 1f : 0f); m.SetFloat("_AOInt", AOInt); m.SetFloat("_AORadius", 0.06f);
        int w = src.width / 2, h = src.height / 2, n = 0;
        RenderTextureFormat fmt = RenderTextureFormat.DefaultHDR;
        for (; n < mips.Length && w >= 8 && h >= 8; n++, w /= 2, h /= 2) mips[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
        Graphics.Blit(src, mips[0], m, 0);
        for (int k = 1; k < n; k++) Graphics.Blit(mips[k - 1], mips[k], m, 1);
        for (int k = n - 1; k > 0; k--) Graphics.Blit(mips[k], mips[k - 1], m, 2);
        m.SetTexture("_BloomTex", mips[0]);
        Graphics.Blit(src, dst, m, 3);
        for (int k = 0; k < n; k++) { RenderTexture.ReleaseTemporary(mips[k]); mips[k] = null; }
    }
}
