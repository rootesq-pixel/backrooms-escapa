using UnityEngine;

// v7.0 Campo infinito: sonido de la hierba compartido por el jugador local (FootstepAudio) y los remotos (RemotePlayer).
//  - Pasos: 5 muestras CC0 reales de pisadas en hierba (Kenney «Impact Sounds», footstep_grass_000-004), elegidas al azar
//    sin repetir la anterior. Pasan por el mismo volumen que los demás pasos (AudioMix.StepMaster = 0,40, la rebaja de −8 dB).
//  - Roce: bucle suave de hierba apartada al andar; sube con la velocidad (más al correr, casi nada agachado).
//  - Dentro de las casas (sin hierba) se usa el clip procedural de suelo de madera y el roce se apaga.
public static class GrassAudio
{
    private static AudioClip[] steps;
    private static AudioClip rustle;
    public const float RustleMax = 0.55f;   // relativo a Sfx·StepMaster (≈ igual de fuerte que una pisada al correr)

    public static int StepClipCount { get { Load(); int n = 0; foreach (AudioClip c in steps) if (c != null) n++; return n; } }
    public static AudioClip Rustle { get { Load(); return rustle; } }

    private static void Load()
    {
        if (steps != null) return;
        steps = new AudioClip[5];
        for (int i = 0; i < 5; i++) steps[i] = Resources.Load<AudioClip>("Field/step_grass_" + i);
        rustle = Resources.Load<AudioClip>("Field/grass_rustle");
    }

    public static AudioClip RandomStep(ref int last)
    {
        Load();
        int i = Random.Range(0, steps.Length);
        if (i == last) i = (i + 1 + Random.Range(0, steps.Length - 1)) % steps.Length;
        last = i;
        return steps[i];
    }

    // ¿hay hierba bajo este punto del mundo? (fuera de las casas/claros y en el Campo infinito)
    public static bool OnGrass(Vector3 world)
    {
        FieldWorld fw = FieldWorld.Active;
        if (fw == null || fw.plan == null || fw.levelRoot == null) return false;
        Vector3 lp = fw.levelRoot.InverseTransformPoint(world);
        float x = FieldHeight.Mod(lp.x), z = FieldHeight.Mod(lp.z);
        if (lp.y - FieldHeight.H(x, z) > 0.45f) return false;   // sobre el zócalo/suelo de una casa
        return !fw.plan.NoGrass(x, z);
    }

    // volumen objetivo del roce (sin el multiplicador de mezcla): 0 parado; agachado ≈ 0,2; andando ≈ 0,55; corriendo 1
    public static float RustleTarget(float speed, bool sprint, bool crouch)
    {
        if (speed < 0.25f) return 0f;
        float k = Mathf.Clamp01((speed - 0.25f) / 4.5f);
        float mode = sprint ? 1f : (crouch ? 0.35f : 0.75f);
        return Mathf.Clamp01(0.25f + 0.75f * k) * mode;
    }

    public static AudioSource MakeRustleSource(GameObject go, bool spatial)
    {
        AudioClip c = Rustle;
        if (c == null) return null;
        AudioSource s = go.AddComponent<AudioSource>();
        s.clip = c; s.loop = true; s.playOnAwake = false; s.volume = 0f;
        s.spatialBlend = spatial ? 1f : 0f;
        if (spatial) { s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 1.5f; s.maxDistance = 16f; s.dopplerLevel = 0f; }
        s.time = Random.value * c.length * 0.9f;
        s.Play();
        return s;
    }
}
