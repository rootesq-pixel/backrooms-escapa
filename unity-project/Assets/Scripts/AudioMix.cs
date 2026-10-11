using UnityEngine;

// ---------------------------------------------------------------------------------------------------------------------
// Mezcla de audio en UN solo sitio (sin AudioMixer): tabla de ganancias de todos los sonidos «de ruido / aire» y los grupos de volumen.
//
//   Volumen final = AudioListener.volume (Volumen general) × grupo (Efectos / Ambiente) × ganancia de la tabla × volumen propio del sonido
//
//   · Efectos  : pasos, chapoteos, respiración, gruñidos y pasos de monstruos, recogidas, clics, susto de muerte.
//   · Ambiente : drone del nivel, sucesos (gotas, crujidos…), zumbido de los fluorescentes y siseo de cinta VHS.
//
// Objetivo de nivel (RMS efectivo con los deslizadores al máximo): pasos ≈ -24 dBFS · drones ≤ -28 · zumbidos ≤ -29 ·
// respiración ≤ -38 · siseo VHS ≤ -40 (reposo ≤ -52). Las pruebas (SmokeTest.TestAudioMix) lo comprueban.
// ---------------------------------------------------------------------------------------------------------------------
public static class AudioMix
{
    // ---- tabla de ganancias
    public const float DroneMaster = 0.24f;      // × LevelConfig.droneVolume
    public const float BuzzMaster = 0.50f;       // × volumen de zumbido de cada luz
    public const float EventMaster = 0.75f;      // gotas / crujidos / golpes aleatorios
    public const float BreathMax = 0.16f;        // volumen máximo de una respiración (agotado)
    public const float BreathMin = 0.04f;        // volumen de la respiración más suave
    public const float HissIdle = 0.010f;        // zumbido VHS (tonal, sin ruido) en reposo (× intensidad VHS)
    public const float HissGlitch = 0.050f;      // siseo VHS (ruido): SOLO por interferencia (× glitch); se puede apagar en Opciones
    public const float StepMaster = 1f;          // pasos (ya normalizados a ≈ -23 dBFS)

    private static int frame = -1;
    private static float sfx = 1f, amb = 1f;
    private static bool hooked;

    private static void Refresh()
    {
        if (!hooked) { hooked = true; GameSettings.Changed += Invalidate; }
        int f = Application.isPlaying ? Time.frameCount : -2;
        if (f == frame && f >= 0) return;
        frame = f;
        sfx = GameSettings.SfxVolume;
        amb = GameSettings.AmbienceVolume;
    }

    private static void Invalidate() { frame = -1; }

    // grupos de volumen (0..1)
    public static float Sfx { get { Refresh(); return sfx; } }
    public static float Ambience { get { Refresh(); return amb; } }

    // atajos: volumen de un sonido de efectos / ambiente ya escalado por su grupo
    public static float S(float v) { return v * Sfx; }
    public static float A(float v) { return v * Ambience; }

    public static float Drone(float configured) { return A(configured * DroneMaster); }
    public static float Buzz(float configured) { return A(configured * BuzzMaster); }
    public static float Event(float v) { return A(v * EventMaster); }

    // ruido (siseo VHS) activado en Opciones («Siseo VHS / ruido»)
    public static bool NoiseOn { get { return GameSettings.NoiseEnabled; } }

    // volumen del SISEO de cinta (ruido): solo por interferencia (glitch); 0 en reposo y con la opción apagada
    public static float Hiss(float intensity, float glitch)
    {
        if (!NoiseOn) return 0f;
        return A(glitch * HissGlitch * Mathf.Max(0.4f, intensity));
    }

    // volumen del zumbido tonal de la cinta (siempre presente, muy bajo)
    public static float Hum(float intensity) { return A(intensity * HissIdle); }

    // volumen de una respiración según la necesidad 0..1 (sin escalar por grupo)
    public static float BreathGain(float need) { return Mathf.Lerp(BreathMin, BreathMax, Mathf.Clamp01(need)); }

    public static float Db(float lin) { return 20f * Mathf.Log10(Mathf.Max(lin, 1e-6f)); }
}
