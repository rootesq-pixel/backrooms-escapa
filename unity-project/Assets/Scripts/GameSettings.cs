using UnityEngine;

// Opciones del jugador, persistidas en PlayerPrefs y aplicadas al juego.
public static class GameSettings
{
    private const string KVolume = "opt_volume", KSens = "opt_sens", KFull = "opt_fullscreen", KQuality = "opt_quality",
        KInvert = "opt_invertY", KBob = "opt_headbob", KSmooth = "opt_smooth", KVhs = "opt_vhs", KVhsInt = "opt_vhsIntensity", KSoft = "opt_softScare", KBody = "opt_showBody", KSfx = "opt_sfx", KAmb = "opt_ambience", KBreath = "opt_breath", KNoise = "opt_noise", KPost = "opt_postfx";

    public static event System.Action Changed;

    public static float MasterVolume { get { return PlayerPrefs.GetFloat(KVolume, 0.8f); } set { PlayerPrefs.SetFloat(KVolume, Mathf.Clamp01(value)); ApplyAudio(); Notify(); } }
    // Grupos de volumen (se multiplican por el volumen general): Efectos (pasos, respiración, monstruos, susto…) y Ambiente (drone, zumbidos, siseo VHS…)
    public static float SfxVolume { get { return PlayerPrefs.GetFloat(KSfx, 1f); } set { PlayerPrefs.SetFloat(KSfx, Mathf.Clamp01(value)); Notify(); } }
    public static float AmbienceVolume { get { return PlayerPrefs.GetFloat(KAmb, 1f); } set { PlayerPrefs.SetFloat(KAmb, Mathf.Clamp01(value)); Notify(); } }
    // Respiración del jugador (suave; solo suena tras correr o con poca stamina)
    public static bool BreathEnabled { get { return PlayerPrefs.GetInt(KBreath, 1) != 0; } set { PlayerPrefs.SetInt(KBreath, value ? 1 : 0); Notify(); } }
    // «Siseo VHS / ruido»: apaga el audio basado en ruido (siseo de cinta en las interferencias)
    public static bool NoiseEnabled { get { return PlayerPrefs.GetInt(KNoise, 1) != 0; } set { PlayerPrefs.SetInt(KNoise, value ? 1 : 0); Notify(); } }
    public static float MouseSensitivity { get { return PlayerPrefs.GetFloat(KSens, 2f); } set { PlayerPrefs.SetFloat(KSens, Mathf.Clamp(value, 0.2f, 8f)); Notify(); } }
    public static bool Fullscreen { get { return PlayerPrefs.GetInt(KFull, 1) != 0; } set { PlayerPrefs.SetInt(KFull, value ? 1 : 0); ApplyScreen(); Notify(); } }
    // 0 = Bajo, 1 = Medio, 2 = Alto
    public static int Quality { get { return Mathf.Clamp(PlayerPrefs.GetInt(KQuality, 2), 0, 2); } set { PlayerPrefs.SetInt(KQuality, Mathf.Clamp(value, 0, 2)); ApplyQuality(); Notify(); } }
    public static bool PostFx { get { return PlayerPrefs.GetInt(KPost, 1) != 0; } set { PlayerPrefs.SetInt(KPost, value ? 1 : 0); Notify(); } }   // v6.9: post-procesado (Medio/Alto)
    public static bool InvertY { get { return PlayerPrefs.GetInt(KInvert, 0) != 0; } set { PlayerPrefs.SetInt(KInvert, value ? 1 : 0); Notify(); } }
    public static bool HeadBob { get { return PlayerPrefs.GetInt(KBob, 1) != 0; } set { PlayerPrefs.SetInt(KBob, value ? 1 : 0); Notify(); } }
    public static bool SmoothLook { get { return PlayerPrefs.GetInt(KSmooth, 0) != 0; } set { PlayerPrefs.SetInt(KSmooth, value ? 1 : 0); Notify(); } }
    public static bool VhsOn { get { return PlayerPrefs.GetInt(KVhs, 1) != 0; } set { PlayerPrefs.SetInt(KVhs, value ? 1 : 0); Notify(); } }
    public static float VhsIntensity { get { return PlayerPrefs.GetFloat(KVhsInt, 0.7f); } set { PlayerPrefs.SetFloat(KVhsInt, Mathf.Clamp01(value)); Notify(); } }

    // "Susto suave": reduce la intensidad del susto de muerte (volumen, flashes, sacudida y glitch)
    public static bool SoftScare { get { return PlayerPrefs.GetInt(KSoft, 0) != 0; } set { PlayerPrefs.SetInt(KSoft, value ? 1 : 0); Notify(); } }
    // "Mostrar cuerpo": cuerpo en primera persona (manos, brazos, piernas)
    public static bool ShowBody { get { return PlayerPrefs.GetInt(KBody, 1) != 0; } set { PlayerPrefs.SetInt(KBody, value ? 1 : 0); Notify(); } }

    public static string[] QualityNames = { "Bajo", "Medio", "Alto" };

    private static void Notify()
    {
        if (Changed != null) Changed();
    }

    public static void Save()
    {
        PlayerPrefs.Save();
    }

    public static void ResetDefaults()
    {
        PlayerPrefs.DeleteKey(KVolume); PlayerPrefs.DeleteKey(KSens); PlayerPrefs.DeleteKey(KFull); PlayerPrefs.DeleteKey(KQuality);
        PlayerPrefs.DeleteKey(KNoise); PlayerPrefs.DeleteKey(KInvert); PlayerPrefs.DeleteKey(KBob); PlayerPrefs.DeleteKey(KSmooth); PlayerPrefs.DeleteKey(KVhs); PlayerPrefs.DeleteKey(KVhsInt); PlayerPrefs.DeleteKey(KSoft); PlayerPrefs.DeleteKey(KBody); PlayerPrefs.DeleteKey(KSfx); PlayerPrefs.DeleteKey(KAmb); PlayerPrefs.DeleteKey(KBreath); PlayerPrefs.DeleteKey(KPost);
        ApplyAll();
        Notify();
    }

    public static void ApplyAll()
    {
        ApplyAudio();
        ApplyScreen();
        ApplyQuality();
    }

    public static void ApplyAudio()
    {
        AudioListener.volume = MasterVolume;
    }

    public static void ApplyScreen()
    {
        if (!Application.isPlaying) return;
        bool want = Fullscreen;
        if (Screen.fullScreen != want)
        {
            if (want) Screen.fullScreenMode = FullScreenMode.FullScreenWindow;
            Screen.fullScreen = want;
            if (!want) Screen.fullScreenMode = FullScreenMode.Windowed;
        }
    }

    public static void ApplyQuality()
    {
        // Bajo/Medio/Alto: ajusta sombras, anisotrópico y calidad de las luces (el resto lo gestiona el proyecto)
        int q = Quality;
        QualitySettings.anisotropicFiltering = q == 0 ? AnisotropicFiltering.Disable : AnisotropicFiltering.ForceEnable;
        QualitySettings.pixelLightCount = q == 0 ? 4 : (q == 1 ? 6 : 8);
        QualitySettings.antiAliasing = q == 0 ? 0 : (q == 1 ? 2 : 4);
        QualitySettings.globalTextureMipmapLimit = q == 0 ? 1 : 0;
        QualitySettings.vSyncCount = q == 0 ? 0 : 1;
    }
}
