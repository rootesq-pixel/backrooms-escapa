using UnityEngine;

// v7.3.0: sonidos procedurales del mar. Sólo tonales (nada de siseos/olas de banda ancha: ver V720Checks.AudioNoiseChecks):
// motor de la barca, torno y golpes de la jaula, sirena del faro, aviso grave de los peces y el zumbido del Farolero.
public class OceanAudio : MonoBehaviour
{
    const int SR = 44100;
    public static OceanAudio Instance;
    private OceanWorld world;
    private AudioSource deep, cue;
    private AudioClip deepClip, cueClip;
    public float DeepVolume { get { return deep != null ? deep.volume : 0f; } }

    public static OceanAudio Create(OceanWorld w)
    {
        OceanAudio a = w.gameObject.AddComponent<OceanAudio>();
        a.world = w; Instance = a;
        a.deep = w.gameObject.AddComponent<AudioSource>(); a.deep.loop = true; a.deep.spatialBlend = 0f; a.deep.volume = 0f;
        a.deepClip = DeepClip(); a.deep.clip = a.deepClip;
        a.cue = w.gameObject.AddComponent<AudioSource>(); a.cue.spatialBlend = 0f; a.cue.playOnAwake = false;
        a.cueClip = FishCueClip();
        if (Application.isPlaying) a.deep.Play();
        return a;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; Object.Destroy(deepClip); Object.Destroy(cueClip); }

    public void FishWarning() { if (cue != null && !cue.isPlaying) { cue.PlayOneShot(cueClip, 0.55f * AudioMix.Sfx); AudioEventLog.Note("FishWarning"); } }

    private void Update()
    {
        // presión del fondo: un zumbido grave que crece con la profundidad (y más aún cerca del Farolero)
        OceanPlayer p = OceanPlayer.Local;
        float depth = p != null ? Mathf.Clamp01(-p.transform.position.y / 45f) : 0f;
        if (p != null && !p.fpc.IsUnderwater) depth *= 0.2f;
        float ang = world != null && world.angler != null && p != null ? Mathf.Clamp01(1f - Vector3.Distance(world.angler.transform.position, p.transform.position) / 30f) : 0f;
        float target = (depth * 0.18f + ang * 0.25f) * AudioMix.Ambience;
        deep.volume = Mathf.Lerp(deep.volume, target, 1f - Mathf.Exp(-1.5f * Time.deltaTime));
        deep.pitch = 1f - ang * 0.15f;
    }

    static AudioClip Make(string name, float seconds, System.Func<float, float> f)
    {
        int n = Mathf.CeilToInt(seconds * SR);
        float[] d = new float[n];
        for (int i = 0; i < n; i++) d[i] = f(i / (float)SR);
        AudioClip c = AudioClip.Create(name, n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    static float S(float hz, float t) { return Mathf.Sin(2f * Mathf.PI * hz * t); }

    // bucle sin costura: frecuencias múltiplo de 1/duración
    public static AudioClip MotorClip()
    {
        const float L = 2f;
        return Make("BoatMotor", L, t => 0.35f * (S(37.5f, t) * 0.6f + S(75f, t) * 0.3f + S(112.5f, t) * 0.18f + S(150f, t) * 0.08f) * (0.85f + 0.15f * S(6.5f, t)));
    }
    public static AudioClip WinchClip()
    {
        const float L = 2f;
        return Make("CageWinch", L, t => 0.3f * (S(140f, t) * 0.5f + S(210f, t) * 0.2f + S(70f, t) * 0.4f) * (0.8f + 0.2f * S(3f, t)));
    }
    public static AudioClip DeepClip()
    {
        const float L = 4f;
        return Make("DeepPressure", L, t => 0.4f * (S(29.5f, t) * 0.7f + S(30.25f, t) * 0.5f + S(59f, t) * 0.18f + S(88.5f, t) * 0.06f));
    }
    public static AudioClip ClankClip()
    {
        return Make("CageClank", 1.2f, t => Mathf.Exp(-t * 5f) * 0.5f * (S(311f, t) * 0.5f + S(523f, t) * 0.3f + S(797f, t) * 0.2f + S(1181f, t) * 0.12f) + Mathf.Exp(-t * 40f) * 0.3f * S(95f, t));
    }
    public static AudioClip HornClip()
    {
        return Make("LighthouseHorn", 4.2f, t =>
        {
            float env = Mathf.Clamp01(t / 0.35f) * Mathf.Clamp01((4.2f - t) / 1.2f);
            return env * 0.45f * (S(82f, t) * 0.6f + S(164f, t) * 0.32f + S(246f, t) * 0.16f + S(328f, t) * 0.07f) * (0.9f + 0.1f * S(4.5f, t));
        });
    }
    public static AudioClip FishCueClip()
    {
        return Make("FishCue", 2.6f, t =>
        {
            float env = Mathf.Clamp01(t / 0.05f) * Mathf.Exp(-t * 1.3f);
            float p = (t % 0.65f) < 0.32f ? 1f : 0.35f;
            return env * p * 0.5f * (S(46f, t) * 0.7f + S(92f, t) * 0.25f + S(61f, t) * 0.3f);
        });
    }
}
