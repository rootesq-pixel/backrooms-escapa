using System.Collections.Generic;
using UnityEngine;

// v7.0.2: silbar (G). Sonido 3D (3 variantes sintetizadas aquí: dominio público / CC0), enfriamiento de 3 s.
// Los monstruos a menos de ~40 m (menos a través de paredes, más en las Poolrooms por el eco) van a investigar el sitio
// tras un momento (búsqueda normal, no detección instantánea). En multijugador lo oyen todos (el anfitrión decide los monstruos).
// El imitador a veces responde con un silbido un rato después.
public class Whistle : MonoBehaviour
{
    public const float Cooldown = 3f, Volume = 0.55f, MaxHear = 55f;
    public static int EmitCount, HeardCount, AlertCount; public static Vector3 LastPos; public static bool LastFromMimic; public static int LastPlayer = -99;
    private static AudioClip[] clips;
    private float cd;
    public FirstPersonController fpc;
    [HideInInspector] public bool debugPress;
    public float CooldownLeft { get { return cd; } }
    public int Count { get; private set; }

    private void Update()
    {
        if (cd > 0f) cd -= Time.deltaTime;
        bool press = debugPress || (Input.GetKeyDown(KeyCode.G) && Time.timeScale > 0f && !InventoryOpen());
        debugPress = false;
        if (!press || cd > 0f) return;
        if (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing) return;
        if (ElevatorCabin.PlayerLocked || (NetSession.Active && NetSession.Instance != null && NetSession.Instance.LocalDowned)) return;
        cd = Cooldown; Count++;
        int variant = Random.Range(0, 3);
        Vector3 pos = transform.position + Vector3.up * 1.55f;
        Emit(pos, NetSession.Active && NetSession.Instance != null ? NetSession.Instance.LocalId : 0, variant, false);
    }

    private static bool InventoryOpen() { return UIManager.Instance != null && !UIManager.Instance.CursorShouldBeLocked; }

    // origen local (jugador o imitador en el anfitrión / solo)
    public static void Emit(Vector3 pos, int playerId, int variant, bool fromMimic)
    {
        EmitCount++;
        Play(pos, variant, fromMimic, !fromMimic);
        if (NetSession.Active && NetSession.Instance != null)
        {
            NetSession.Instance.NotifyWhistle(pos, playerId, variant, fromMimic);
            if (NetSession.IsHost && !fromMimic) Alert(pos);
        }
        else if (!fromMimic) Alert(pos);
    }

    // anfitrión: silbido de un cliente (ya reenviado a los demás)
    public static void HostHeard(Vector3 pos, int playerId, int variant, bool fromMimic)
    {
        HeardCount++; LastPlayer = playerId;
        Play(pos, variant, fromMimic, false);
        if (!fromMimic) Alert(pos);
    }

    // cliente: silbido de otro jugador o del imitador (difundido por el anfitrión)
    public static void ClientHeard(Vector3 pos, int playerId, int variant, bool fromMimic)
    {
        HeardCount++; LastPlayer = playerId;
        if (NetSession.Instance != null && playerId == NetSession.Instance.LocalId && !fromMimic) return;   // el propio ya sonó
        Play(pos, variant, fromMimic, false);
    }

    private static void Alert(Vector3 pos)
    {
        foreach (MonsterAI m in MonsterAI.Instances) if (m != null && m.HearWhistle(pos)) AlertCount++;
        if (MimicDisguise.Active != null) MimicDisguise.Active.OnPlayerWhistle(pos);
    }

    private static void Play(Vector3 pos, int variant, bool fromMimic, bool own)
    {
        LastPos = pos; LastFromMimic = fromMimic;
        if (clips == null) clips = new[] { Create(0), Create(1), Create(2) };
        AudioClip c = clips[Mathf.Clamp(variant, 0, 2)];
        GameObject go = new GameObject(fromMimic ? "WhistleMimic" : "Whistle");
        go.transform.position = pos;
        AudioSource s = go.AddComponent<AudioSource>();
        s.clip = c; s.spatialBlend = own ? 0.6f : 1f; s.rolloffMode = AudioRolloffMode.Linear; s.minDistance = 2f; s.maxDistance = MaxHear;
        s.dopplerLevel = 0f; s.reverbZoneMix = 1.1f;
        s.pitch = fromMimic ? 0.94f : 1f;   // el del imitador, apenas más grave
        s.volume = AudioMix.S(Volume);
        if (WaterVolume.Active != null) { AudioReverbFilter rv = go.AddComponent<AudioReverbFilter>(); rv.reverbPreset = AudioReverbPreset.Bathroom; }
        s.Play();
        Destroy(go, c.length / s.pitch + 0.5f);
    }

    // silbido humano: tono puro con vibrato, deslizamientos y aire (soplido) filtrado
    public static AudioClip Create(int variant)
    {
        const int SR = 22050;
        float len = variant == 0 ? 0.95f : (variant == 1 ? 0.8f : 1.1f);
        int n = (int)(SR * len);
        float[] d = new float[n];
        System.Random r = new System.Random(4711 + variant * 31);
        double ph = 0; float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR, u = t / len;
            float f;
            if (variant == 0)      // dos notas: sube y baja («fiu-fiuu»)
                f = u < 0.38f ? Mathf.Lerp(1350f, 2350f, Mathf.SmoothStep(0f, 1f, u / 0.38f)) : Mathf.Lerp(2500f, 1500f, Mathf.SmoothStep(0f, 1f, (u - 0.45f) / 0.55f));
            else if (variant == 1) // dos toques cortos
                f = u < 0.45f ? 2050f + 120f * u : 2250f - 200f * (u - 0.55f);
            else                   // una nota larga que cae
                f = Mathf.Lerp(2200f, 1400f, u * u);
            f *= 1f + 0.012f * Mathf.Sin(2f * Mathf.PI * 5.5f * t);
            ph += 2.0 * System.Math.PI * f / SR;
            float env;
            if (variant == 1) { float a = Mathf.Clamp01(u / 0.05f) * Mathf.Clamp01((0.42f - u) / 0.06f), b = Mathf.Clamp01((u - 0.55f) / 0.05f) * Mathf.Clamp01((1f - u) / 0.12f); env = Mathf.Max(a, b); }
            else if (variant == 0) { float gap = 1f - 0.85f * Mathf.Exp(-Mathf.Pow((u - 0.415f) / 0.03f, 2f)); env = Mathf.Clamp01(u / 0.06f) * Mathf.Clamp01((1f - u) / 0.15f) * gap; }
            else env = Mathf.Clamp01(u / 0.05f) * Mathf.Clamp01((1f - u) / 0.3f);
            float noise = (float)(r.NextDouble() * 2.0 - 1.0);
            lp += 0.25f * (noise - lp);
            float tone = Mathf.Sin((float)ph) + 0.06f * Mathf.Sin((float)(2.0 * ph));
            d[i] = (tone * 0.8f + (noise - lp) * 0.12f) * env * (0.9f + 0.1f * Mathf.Sin(2f * Mathf.PI * 3f * t));
        }
        float mx = 1e-4f; for (int i = 0; i < n; i++) mx = Mathf.Max(mx, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] *= 0.6f / mx;
        AudioClip c = AudioClip.Create("Whistle" + variant, n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }
}
