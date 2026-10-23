using UnityEngine;

// v7.1.0 Parque de bolas: una caja de música suena a ratos desde la sala de fiestas más cercana y, de vez en cuando, risas de
// niños lejanas. Todo con mucha reverberación (nave grande y vacía) y filtrado por la distancia y las paredes.
public class PlayparkAudio : MonoBehaviour
{
    private AudioSource box, laugh;
    private AudioLowPassFilter boxLp, laughLp;
    private AudioClip boxClip; private AudioClip[] laughs;
    private Transform listener;
    private float boxTimer, laughTimer;
    public int BoxPlays { get; private set; }
    public int Laughs { get; private set; }
    public static PlayparkAudio Active { get; private set; }

    public static PlayparkAudio Create(Transform parent, Transform listener)
    {
        GameObject go = new GameObject("PlayparkAudio");
        go.transform.SetParent(parent, false);
        PlayparkAudio a = go.AddComponent<PlayparkAudio>();
        a.listener = listener;
        a.boxClip = ProceduralAudio.CreateMusicBox();
        a.laughs = new[] { ProceduralAudio.CreateChildLaugh(3), ProceduralAudio.CreateChildLaugh(11), ProceduralAudio.CreateChildLaugh(27), ProceduralAudio.CreateChildLaugh(40) };
        a.box = Src(go.transform, "MusicBox", 4f, 55f, out a.boxLp);
        a.laugh = Src(go.transform, "Laughter", 6f, 60f, out a.laughLp);
        a.boxTimer = 8f; a.laughTimer = 25f;
        Active = a;
        return a;
    }

    static AudioSource Src(Transform parent, string name, float min, float max, out AudioLowPassFilter lp)
    {
        GameObject g = new GameObject(name); g.transform.SetParent(parent, false);
        AudioSource s = g.AddComponent<AudioSource>();
        s.spatialBlend = 1f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.minDistance = min; s.maxDistance = max; s.dopplerLevel = 0f; s.playOnAwake = false;
        lp = g.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 4000f;
        AudioReverbFilter rv = g.AddComponent<AudioReverbFilter>(); rv.reverbPreset = AudioReverbPreset.Hangar;
        return s;
    }

    public void ForceForTests() { boxTimer = 0f; laughTimer = 0f; }
    public AudioClip BoxClip { get { return boxClip; } }
    public AudioClip[] LaughClips { get { return laughs; } }
    public AudioSource BoxSource { get { return box; } }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        ProceduralAssets.SafeDestroy(boxClip);
        if (laughs != null) foreach (AudioClip c in laughs) ProceduralAssets.SafeDestroy(c);
    }

    static float Occlusion(Vector3 from, Vector3 to)
    {
        return Physics.Linecast(from, to, ~0, QueryTriggerInteraction.Ignore) ? 1f : 0f;
    }

    private void Update()
    {
        if (listener == null || GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing) { if (box.isPlaying) box.Pause(); return; }
        float dt = Time.deltaTime;
        // caja de música: en la sala de fiestas de la sala más cercana
        if (!box.isPlaying)
        {
            boxTimer -= dt;
            if (boxTimer <= 0f && BallpitSetPiece.Roots.Count > 0)
            {
                GameObject best = null; float bd = float.MaxValue;
                foreach (GameObject r in BallpitSetPiece.Roots) { if (r == null) continue; float dd = Vector3.Distance(r.transform.position, listener.position); if (dd < bd) { bd = dd; best = r; } }
                if (best != null)
                {
                    box.transform.position = best.transform.TransformPoint(new Vector3(7.8f, 0.8f, -6.4f));
                    box.clip = boxClip; box.volume = AudioMix.Event(0.55f); box.pitch = Random.Range(0.97f, 1.02f); box.Play(); BoxPlays++;
                }
                boxTimer = Random.Range(35f, 75f);
            }
        }
        // risas: lejos, en una dirección al azar
        laughTimer -= dt;
        if (laughTimer <= 0f)
        {
            laughTimer = Random.Range(28f, 70f);
            Vector2 dir = Random.insideUnitCircle.normalized * Random.Range(14f, 28f);
            laugh.transform.position = listener.position + new Vector3(dir.x, 1.0f, dir.y);
            laugh.pitch = Random.Range(0.92f, 1.06f);
            laugh.PlayOneShot(laughs[Random.Range(0, laughs.Length)], AudioMix.Event(Random.Range(0.25f, 0.45f))); AudioEventLog.Note("Laugh");
            Laughs++;
        }
        Vector3 ear = listener.position + Vector3.up * 1.6f;
        boxLp.cutoffFrequency = Mathf.Lerp(boxLp.cutoffFrequency, Occlusion(ear, box.transform.position) > 0f ? 1100f : 5000f, 1f - Mathf.Exp(-3f * dt));
        laughLp.cutoffFrequency = Mathf.Lerp(laughLp.cutoffFrequency, Occlusion(ear, laugh.transform.position) > 0f ? 1300f : 3500f, 1f - Mathf.Exp(-3f * dt));
    }
}
