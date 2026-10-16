using System.Collections.Generic;
using UnityEngine;

// v7.0 Campo infinito: cielo nublado (HDRI CC0) con bruma en el horizonte, luz ambiente de tres colores (cielo/horizonte/suelo),
// un sol difuso sin sombras detrás de las nubes, niebla exponencial gris verdosa y cámara con más distancia de dibujo.
public static class FieldAtmosphere
{
    public static readonly Color Haze = new Color(0.66f, 0.69f, 0.69f);

    public static void Apply(LevelConfig cfg, Transform worldRoot, List<Object> owned)
    {
        Shader sh = Shader.Find("Backrooms/FieldSky");
        Texture2D hdr = Resources.Load<Texture2D>("Field/sky_overcast");
        if (sh != null)
        {
            Material sky = new Material(sh) { name = "FieldSky" };
            if (hdr != null) sky.SetTexture("_Tex", hdr);
            sky.SetColor("_HazeCol", cfg.fog);
            sky.SetFloat("_Exposure", 0.95f);
            owned.Add(sky);
            RenderSettings.skybox = sky;
        }
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.66f, 0.70f, 0.74f);
        RenderSettings.ambientEquatorColor = new Color(0.56f, 0.58f, 0.54f);
        RenderSettings.ambientGroundColor = new Color(0.24f, 0.25f, 0.19f);
        RenderSettings.fogColor = cfg.fog;
        RenderSettings.fogDensity = GameSettings.Quality == 0 ? cfg.fogDensity * 1.35f : cfg.fogDensity;

        GameObject sun = new GameObject("FieldSun");
        sun.transform.SetParent(worldRoot, false);
        sun.transform.rotation = Quaternion.Euler(38f, 128f, 0f);
        Light l = sun.AddComponent<Light>();
        l.type = LightType.Directional; l.color = new Color(1f, 0.97f, 0.9f); l.intensity = 0.55f; l.shadows = LightShadows.None;
        RenderSettings.sun = l;
        Shader.SetGlobalFloat("_GradeWarm", 0.6f);
        Shader.SetGlobalFloat("_GradeContrast", 1.15f);
        Shader.SetGlobalFloat("_GradeExposure", 1.0f);
    }

    public static float FarClip { get { return GameSettings.Quality == 0 ? 160f : 240f; } }

    public static void SetupCamera(Camera cam)
    {
        cam.clearFlags = CameraClearFlags.Skybox;
        cam.farClipPlane = FarClip;
        cam.backgroundColor = Haze;
    }
}
