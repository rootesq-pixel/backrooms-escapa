using System.Collections.Generic;
using UnityEngine;

// v7.10.1 (t499u): Nivel 7460: luz roja de emergencia, como la alarma del Nivel 0 (Level0Events, que no cambia).
// De vez en cuando TODO el pasillo y las habitaciones se bañan de rojo unos segundos (6–12 s) y vuelven a la luz de siempre.
// El pasillo está horneado (Backrooms/CorridorBaked: la luz es emisión del lightmap), así que el rojo se aplica en el propio
// shader: _C7460Red tiñe la irradiancia horneada de paredes, techo, suelo y muebles hacia _C7460RedCol (con el pulso de alarma).
// El reflejo del suelo (planar) refleja la sala ya roja; la sonda se vuelve a capturar al entrar y al salir del rojo; los
// plafones, la niebla y el fondo también pasan a rojo. Sonido: la sirena del Nivel 0 v7.9.4 (440–660 Hz, sin siseo ni crujidos).
// Calendario: semilla del nivel + reloj del nivel; en el multijugador el anfitrión manda su reloj en el snapshot (igual que el
// Nivel 0), así que empieza y acaba a la vez para todos. No toca la cordura (en el 7460 nunca baja); solo sube un poco el latido.
public class CorridorRed : MonoBehaviour
{
    public static CorridorRed Active { get; private set; }
    public const float RedMin = 6f, RedMax = 12f, FirstMin = 35f, FirstMax = 70f, GapMin = 60f, GapMax = 130f, Fade = 0.7f;
    public static readonly Color RedCol = new Color(1.0f, 0.06f, 0.035f);
    public const float RedGain = 1.25f;               // la irradiancia roja (luminancia × gain), para que se vea claramente roja
    public static readonly Color RedFog = new Color(0.20f, 0.012f, 0.008f);
    public static float ForceRed = -1f;              // pruebas/capturas: >= 0 fija el rojo
    public static readonly int RedId = Shader.PropertyToID("_C7460Red"), RedColId = Shader.PropertyToID("_C7460RedCol");

    public readonly List<Level0Events.RedEvent> Schedule = new List<Level0Events.RedEvent>();
    public float Clock { get; private set; }
    public float Red01 { get; private set; }
    public bool RedOn { get; private set; }
    public int RedStarts { get; private set; }
    public int NetClocks { get; private set; }
    public int Seed { get; private set; }
    public float Applied { get; private set; }
    public AudioSource AlarmSource { get { return alarmSrc; } }

    private CorridorWorld world; private Camera cam;
    private Material panelMat; private Color panelBase; private Color baseFog;
    private AudioSource alarmSrc; private AudioClip alarmClip;
    private float appliedRed = -1f, appliedPulse = -1f;

    public static CorridorRed Create(Transform parent, BackroomsGenerator gen, LevelConfig cfg, Camera cam)
    {
        GameObject g = new GameObject("CorridorRed"); g.transform.SetParent(parent, false);
        CorridorRed e = g.AddComponent<CorridorRed>();
        e.Setup(gen, cfg, cam);
        return e;
    }

    private void Setup(BackroomsGenerator gen, LevelConfig cfg, Camera c)
    {
        Active = this; cam = c; world = CorridorWorld.Active;
        Seed = gen != null ? gen.UsedSeed : 0;
        BuildSchedule(Seed, Schedule);
        panelMat = world != null ? world.PanelMat : null;
        if (panelMat != null && panelMat.HasProperty("_EmissionColor")) panelBase = panelMat.GetColor("_EmissionColor"); else panelMat = null;
        baseFog = cfg != null ? cfg.fog : RenderSettings.fogColor;
        alarmClip = Level0Events.CreateAlarm(); alarmClip.name = "C7460_Alarma";
        GameObject a = new GameObject("C7460Alarm"); a.transform.SetParent(transform, false);
        alarmSrc = a.AddComponent<AudioSource>();
        alarmSrc.playOnAwake = false; alarmSrc.loop = true; alarmSrc.spatialBlend = 0f; alarmSrc.clip = alarmClip; alarmSrc.volume = 0f; alarmSrc.dopplerLevel = 0f;
        AudioLowPassFilter alp = a.AddComponent<AudioLowPassFilter>(); alp.cutoffFrequency = Level0Events.AlarmCutoff;
        Shader.SetGlobalColor(RedColId, RedCol * RedGain); Shader.SetGlobalFloat(RedId, 0f);
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        Shader.SetGlobalFloat(RedId, 0f);
        if (panelMat != null) panelMat.SetColor("_EmissionColor", panelBase);
        if (appliedRed > 0f) { RenderSettings.fogColor = baseFog; if (cam != null) cam.backgroundColor = baseFog; }
        if (alarmClip != null) ProceduralAssets.SafeDestroy(alarmClip);
    }

    // calendario determinista (misma semilla → mismos rojos en anfitrión y clientes); distinto del Nivel 0
    public static void BuildSchedule(int seed, List<Level0Events.RedEvent> list)
    {
        list.Clear();
        System.Random r = new System.Random(seed * 7457 + 7460);
        float t = FirstMin + (float)r.NextDouble() * (FirstMax - FirstMin);
        for (int i = 0; i < 160; i++)
        {
            float d = RedMin + (float)r.NextDouble() * (RedMax - RedMin);
            list.Add(new Level0Events.RedEvent { start = t, dur = d });
            t += d + GapMin + (float)r.NextDouble() * (GapMax - GapMin);
        }
    }

    public int EventAt(float clock)
    {
        for (int i = 0; i < Schedule.Count; i++) { if (clock < Schedule[i].start) return -1; if (clock < Schedule[i].End) return i; }
        return -1;
    }

    public float RedAt(float clock)
    {
        int i = EventAt(clock);
        if (i < 0) return 0f;
        Level0Events.RedEvent e = Schedule[i];
        float edge = Mathf.Min(clock - e.start, e.End - clock);
        if (edge >= Fade) return 1f;
        float k = edge / Fade;
        float n = Mathf.PerlinNoise(clock * 13f, (Seed & 1023) * 0.41f);
        return n > 0.62f - 0.4f * k ? k : k * 0.15f;   // chisporroteo al cambiar
    }

    public void NetClock(float c) { NetClocks++; if (Mathf.Abs(c - Clock) > 1.0f) Clock = c; else Clock = Mathf.Lerp(Clock, c, 0.35f); }
    public void DebugSetClock(float c) { Clock = c; }

    private void Update()
    {
        float dt = Time.deltaTime;
        GameManager gm = GameManager.Instance;
        bool playing = gm == null || gm.State == GameManager.GameState.Playing;
        if (playing && dt > 0f) Clock += dt;
        float red = ForceRed >= 0f ? Mathf.Clamp01(ForceRed) : RedAt(Clock);
        bool on = red > 0.5f;
        if (on != RedOn)
        {
            if (on) { RedStarts++; Adrenaline.Scare(0.3f); AudioEventLog.Note("C7460_Rojo"); }
            if (world != null) world.RefreshProbe();   // el brillo de las manillas y paredes sigue al rojo
        }
        RedOn = on; Red01 = red;
        Apply(red, false);
        float target = playing ? red * Level0Events.AlarmVolume : 0f;
        alarmSrc.volume = AudioMix.A(target);
        if (target > 0.001f) { if (!alarmSrc.isPlaying) { alarmSrc.time = Mathf.Repeat(Clock, Level0Events.AlarmLoop); alarmSrc.Play(); } }
        else if (alarmSrc.isPlaying) alarmSrc.Stop();
    }

    public void Apply(float red, bool force)
    {
        float pulse = red > 0f ? Level0Events.PulseAt(Clock) : 1f;
        if (!force && Mathf.Abs(red - appliedRed) < 0.004f && Mathf.Abs(pulse - appliedPulse) < 0.02f) return;
        appliedRed = red; appliedPulse = pulse;
        float k = red * Mathf.Lerp(0.82f, 1f, pulse);
        Applied = k;
        Shader.SetGlobalColor(RedColId, RedCol * RedGain * Mathf.Lerp(0.75f, 1f, pulse));
        Shader.SetGlobalFloat(RedId, red);
        if (panelMat != null) panelMat.SetColor("_EmissionColor", Color.Lerp(panelBase, RedCol * panelBase.maxColorComponent * pulse, red));
        RenderSettings.fogColor = Color.Lerp(baseFog, RedFog, k);
        if (cam != null) cam.backgroundColor = RenderSettings.fogColor;
    }
}
