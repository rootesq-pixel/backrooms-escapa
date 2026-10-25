using UnityEngine;
using UnityEngine.Rendering;

// v7.7.0 Desierto Cálido: ciclo real de día y noche (comprimido: un día entero en 20 minutos) y el clima, que cambia cada
// «6 horas» (5 minutos): despejado, nublado gris, lluvia de agua de almendras, lluvia de «dolor líquido», tormenta de arena,
// nieve (rarísima) y el «Clima Hermoso» (auroras y arena de colores). Todo sale de la semilla + el reloj del nivel, y el
// reloj lo manda el anfitrión en el snapshot → en multijugador todos ven el mismo cielo, la misma hora y el mismo clima.
// Controla el sol/la luna (luz direccional con sombras suaves), la luz ambiente, la niebla y los parámetros del cielo.
public class DesertSky : MonoBehaviour
{
    public static DesertSky Active;
    private float savedSat = 0.86f, savedCon = 1.08f; private Color savedLift = new Color(0.010f, 0.016f, 0.020f);
    public enum Weather { Clear = 0, Overcast = 1, AlmondRain = 2, PainRain = 3, Sandstorm = 4, Snow = 5, Beautiful = 6 }
    public static readonly string[] WeatherName = { "Cielo despejado", "Nublado", "Lluvia de agua de almendras", "Lluvia de dolor líquido", "Tormenta de arena", "Nieve", "Clima Hermoso" };

    public const float DayLength = 1200f;         // s por día completo (24 h)
    public const float SegLength = 300f;          // s por «6 horas»: cambia el clima
    public const float Blend = 45f;               // s de transición entre climas
    // pruebas / capturas: hora (0-24) y clima forzados (-1 = no)
    public static float ForceHour = -1f;
    public static int ForceWeather = -1;
    public static bool ForceHourNet;              // pruebas MP: el anfitrión fija la hora

    public int seed;
    public float startHour;
    public float Clock { get; private set; }      // s desde que empezó el nivel (lo corrige el anfitrión)
    public float Hour { get; private set; }
    public float SunElev { get; private set; }    // grados sobre el horizonte
    public Weather Current { get; private set; }
    public Weather Previous { get; private set; }
    public float WeatherT { get; private set; }   // 0-1 transición del anterior al actual
    public int Segment { get; private set; }
    public float TempC { get; private set; }
    // estado mezclado (lo leen la arena, las partículas, el sonido y los riesgos)
    public float Cover, Dark, Rain, PainK, Storm, Snow, SnowCover, Wet, Beauty;
    public float Night { get; private set; }     // 0 de día, 1 noche cerrada
    public Vector3 SunDir { get; private set; }   // hacia el sol (mundo)
    public Vector3 MoonDir { get; private set; }
    public Vector2 WindDir { get; private set; }

    private Light sun;
    private Material sky;
    private float moonPhase, reflT, lastReflElev = -999f;
    private float savedShadowDist; private int savedCascades; private ShadowResolution savedRes;
    private Transform worldRoot;

    public static DesertSky Create(Transform worldRoot, int seed, LevelConfig cfg, System.Collections.Generic.List<Object> owned)
    {
        GameObject go = new GameObject("DesertSky"); go.transform.SetParent(worldRoot, false);
        DesertSky s = go.AddComponent<DesertSky>();
        s.worldRoot = worldRoot; s.seed = seed;
        System.Random r = new System.Random(seed * 41 + 9);
        s.startHour = 8.6f + (float)r.NextDouble() * 2.2f;       // por la mañana (se ve el desierto al salir del ascensor)
        s.moonPhase = (float)r.NextDouble();
        Shader sh = Shader.Find("Backrooms/DesertSky");
        if (sh != null) { s.sky = new Material(sh) { name = "DesertSky" }; owned.Add(s.sky); RenderSettings.skybox = s.sky; }
        GameObject sg = new GameObject("DesertSun"); sg.transform.SetParent(go.transform, false);
        s.sun = sg.AddComponent<Light>();
        s.sun.type = LightType.Directional; s.sun.shadows = LightShadows.Soft; s.sun.shadowStrength = 0.9f;
        s.sun.shadowBias = 0.06f; s.sun.shadowNormalBias = 0.5f;
        RenderSettings.sun = s.sun;
        RenderSettings.ambientMode = AmbientMode.Custom;   // v7.7.0 b: sonda propia (ver Tick)
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = 0.6f;
        s.savedSat = PostStack.Saturation; s.savedCon = PostStack.Contrast; s.savedLift = PostStack.Lift;
        PostStack.Lift = new Color(0.0025f, 0.0024f, 0.0026f);   // v7.7.0 a: sin el negro verdoso de las Poolrooms (la noche y las sombras salían color petróleo)
        s.savedShadowDist = QualitySettings.shadowDistance; s.savedCascades = QualitySettings.shadowCascades; s.savedRes = QualitySettings.shadowResolution;
        QualitySettings.shadowDistance = GameSettings.Quality == 0 ? 70f : 130f;
        QualitySettings.shadowCascades = GameSettings.Quality == 0 ? 2 : 4;
        if (GameSettings.Quality > 0) QualitySettings.shadowResolution = ShadowResolution.High;
        Shader.SetGlobalFloat("_GradeWarm", 0.55f);
        Shader.SetGlobalFloat("_GradeContrast", 1.12f);
        Shader.SetGlobalFloat("_GradeExposure", 1.0f);
        Active = s;
        s.Tick(0f);
        return s;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        QualitySettings.shadowDistance = savedShadowDist; QualitySettings.shadowCascades = savedCascades; QualitySettings.shadowResolution = savedRes;
        PostStack.Saturation = savedSat; PostStack.Contrast = savedCon; PostStack.Lift = savedLift;
        Shader.SetGlobalVector("_DesertWeather", Vector4.zero);
    }

    // el anfitrión corrige el reloj (snapshot): saltos grandes de golpe, pequeños suavizados
    public void NetClock(float hostClock)
    {
        float d = hostClock - Clock;
        if (Mathf.Abs(d) > 2f) Clock = hostClock; else Clock += d * 0.2f;
    }

    // ---- clima determinista por tramos
    public static Weather WeatherOf(int seed, int seg)
    {
        if (seg <= 0) return Weather.Clear;   // al llegar, despejado
        float h = Hash01(seed * 7919 + seg * 104729);
        // pesos: despejado 40, nublado 20, almendras 9, dolor 7, arena 14, nieve 4, hermoso 6
        if (h < 0.40f) return Weather.Clear;
        if (h < 0.60f) return Weather.Overcast;
        if (h < 0.69f) return Weather.AlmondRain;
        if (h < 0.76f) return Weather.PainRain;
        if (h < 0.90f) return Weather.Sandstorm;
        if (h < 0.94f) return Weather.Snow;
        return Weather.Beautiful;
    }
    private static float Hash01(int x) { unchecked { uint h = (uint)x * 2654435761u; h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; return (h & 0xffffff) / 16777216f; } }

    private struct WP { public float cover, dark, rain, pain, storm, snow, beauty, fog, temp; }
    private static WP Params(Weather w)
    {
        switch (w)
        {
            case Weather.Overcast: return new WP { cover = 0.92f, dark = 0.55f, fog = 1.5f, temp = 12.5f };
            case Weather.AlmondRain: return new WP { cover = 0.97f, dark = 0.8f, rain = 1f, fog = 2.0f, temp = 12f };
            case Weather.PainRain: return new WP { cover = 0.98f, dark = 1.0f, rain = 1f, pain = 1f, fog = 2.2f, temp = 11f };
            case Weather.Sandstorm: return new WP { cover = 0.55f, dark = 0.35f, storm = 1f, fog = 9f, temp = 15f };
            case Weather.Snow: return new WP { cover = 0.9f, dark = 0.45f, snow = 1f, fog = 2.6f, temp = 10f };
            case Weather.Beautiful: return new WP { cover = 0.08f, beauty = 1f, fog = 0.8f, temp = 16.5f };
            default: return new WP { cover = 0.18f, fog = 1f, temp = 15.5f };
        }
    }

    private void Update() { Tick(Time.deltaTime); }

    public void Tick(float dt)
    {
        Clock += dt;
        Hour = ForceHour >= 0f ? ForceHour : Mathf.Repeat(startHour + Clock / DayLength * 24f, 24f);
        Segment = Mathf.FloorToInt(Clock / SegLength);
        if (ForceWeather >= 0) { Current = Previous = (Weather)ForceWeather; WeatherT = 1f; }
        else
        {
            Current = WeatherOf(seed, Segment); Previous = WeatherOf(seed, Segment - 1);
            WeatherT = Mathf.Clamp01((Clock - Segment * SegLength) / Blend);
            WeatherT = WeatherT * WeatherT * (3f - 2f * WeatherT);
        }
        WP a = Params(Previous), b = Params(Current); float t = WeatherT;
        Cover = Mathf.Lerp(a.cover, b.cover, t); Dark = Mathf.Lerp(a.dark, b.dark, t);
        Rain = Mathf.Lerp(a.rain, b.rain, t); PainK = b.rain > 0f ? b.pain : a.pain;
        Storm = Mathf.Lerp(a.storm, b.storm, t); Snow = Mathf.Lerp(a.snow, b.snow, t); Beauty = Mathf.Lerp(a.beauty, b.beauty, t);
        float fogMul = Mathf.Lerp(a.fog, b.fog, t);
        // acumulación: la arena se moja con la lluvia y se seca; la nieve cuaja despacio y se derrite
        if (ForceWeather >= 0) { Wet = Rain; SnowCover = Snow; }
        else
        {
            Wet = Mathf.MoveTowards(Wet, Rain, dt * (Rain > Wet ? 1f / 40f : 1f / 120f));
            SnowCover = Mathf.MoveTowards(SnowCover, Snow, dt * (Snow > SnowCover ? 1f / 90f : 1f / 150f));
        }

        // ---- sol y luna: el sol sale por el este (+x) a las 6 y se pone por el oeste a las 18; altura máxima 64°
        float dayAng = (Hour - 6f) / 24f * Mathf.PI * 2f;            // 0 al amanecer, π/2 a mediodía
        Vector3 sd = new Vector3(Mathf.Cos(dayAng), Mathf.Sin(dayAng) * 0.9f, 0.32f);
        sd = Quaternion.Euler(0f, 18f, 0f) * sd;                      // el sol de mediodía, algo hacia el sur
        sd.Normalize();
        Vector3 md = (-sd + new Vector3(0.15f, 0.25f, -0.35f)).normalized;   // la luna, más o menos en el lado opuesto
        if (worldRoot != null) { sd = worldRoot.TransformDirection(sd); md = worldRoot.TransformDirection(md); }
        SunDir = sd; MoonDir = md;
        SunElev = Mathf.Asin(Mathf.Clamp(sd.y, -1f, 1f)) * Mathf.Rad2Deg;
        float e = SunElev;
        Night = Mathf.Clamp01(Mathf.InverseLerp(-1f, -12f, e));
        float day = Mathf.Clamp01(Mathf.InverseLerp(-4f, 10f, e));
        WindDir = new Vector2(0.86f, 0.5f);

        // ---- colores del cielo según la altura del sol (mediodía, tarde dorada, puesta, crepúsculo, noche)
        Color zen, hor, sunC; float sunI;
        Gradient5(e, out zen, out hor, out sunC, out sunI);
        // nublado / lluvia / nieve: cielo gris; tormenta de arena: todo ocre; Clima Hermoso: más limpio y rosado al atardecer
        Color grey = Color.Lerp(new Color(0.60f, 0.62f, 0.64f), new Color(0.36f, 0.37f, 0.40f), Dark) * Mathf.Lerp(0.03f, 1f, day);
        float ov = Mathf.Clamp01((Cover - 0.3f) / 0.6f);
        zen = Color.Lerp(zen, grey * 0.92f, ov); hor = Color.Lerp(hor, grey * 1.05f, ov * 0.9f);
        Color dust = new Color(0.78f, 0.58f, 0.38f) * Mathf.Lerp(0.04f, 1f, day);
        zen = Color.Lerp(zen, dust * 0.85f, Storm * 0.85f); hor = Color.Lerp(hor, dust, Storm);
        float pink = Beauty * Mathf.Clamp01(1f - Mathf.Abs(e - 4f) / 14f);
        hor = Color.Lerp(hor, new Color(0.96f, 0.56f, 0.66f), pink * 0.35f);
        sunI *= Mathf.Lerp(1f, 0.22f, ov) * Mathf.Lerp(1f, 0.45f, Storm);

        // niebla: el horizonte del cielo es exactamente el color de la niebla
        Color fogC = Color.Lerp(hor, new Color(0.80f, 0.70f, 0.58f) * Mathf.Lerp(0.05f, 1f, day), 0.25f * (1f - Night));
        fogC = Color.Lerp(fogC, dust, Storm);
        float fogD = 0.0024f * fogMul * Mathf.Lerp(1f, 1.6f, Night);
        RenderSettings.fogColor = fogC;
        RenderSettings.fogDensity = GameSettings.Quality == 0 ? fogD * 1.3f : fogD;
        Camera cam = Camera.main;
        if (cam != null) cam.backgroundColor = fogC;

        // luz: el sol de día; de noche la luna (débil y azulada: la noche es muy oscura, hace falta la linterna)
        float moonVis = Mathf.Clamp01(Mathf.InverseLerp(-2f, -8f, e)) * (1f - ov * 0.85f) * (1f - Storm);
        float moonBright = 0.35f + 0.65f * (1f - Mathf.Abs(moonPhase - 0.5f) * 2f);
        if (sun != null)
        {
            if (e > -3f)
            {
                sun.transform.rotation = Quaternion.LookRotation(-sd);
                sun.color = sunC; sun.intensity = sunI * Mathf.Clamp01((e + 3f) / 4f);
                sun.shadowStrength = Mathf.Lerp(0.9f, 0.45f, ov);
            }
            else
            {
                sun.transform.rotation = Quaternion.LookRotation(-md);
                sun.color = new Color(0.60f, 0.70f, 1f); sun.intensity = 0.11f * moonBright * moonVis;   // luz de luna: se ven las dunas, pero hace falta linterna
                sun.shadowStrength = 0.6f;
            }
            sun.shadows = sun.intensity > 0.01f ? LightShadows.Soft : LightShadows.None;
        }
        // luz ambiente (cielo, horizonte, rebote de la arena)
        Color nightAmb = new Color(0.016f, 0.020f, 0.034f) * (0.6f + 0.4f * moonBright);
        Color sky0 = Color.Lerp(nightAmb, zen * 0.85f + hor * 0.15f, day);
        Color bounce = new Color(0.62f, 0.46f, 0.31f) * (0.35f + 0.25f * sunI) * (1f - ov * 0.5f);   // rebote cálido de la arena en las caras a la sombra
        Color eq0 = Color.Lerp(nightAmb * 0.9f, Color.Lerp(hor * 0.75f, bounce, 0.45f), day);
        Color gr0 = Color.Lerp(nightAmb * 0.6f, new Color(0.55f, 0.42f, 0.30f) * (0.18f + 0.30f * sunI), day);
        RenderSettings.ambientSkyColor = sky0; RenderSettings.ambientEquatorColor = eq0; RenderSettings.ambientGroundColor = gr0;
        // v7.7.0 b: el «Trilight» no llegaba a los shaders al cambiarlo en juego (las sombras salían negras): la sonda de
        // ambiente se construye aquí cada vez (cielo arriba, rebote de la arena en el horizonte, suelo abajo)
        UnityEngine.Rendering.SphericalHarmonicsL2 shA = new UnityEngine.Rendering.SphericalHarmonicsL2();
        shA.AddAmbientLight(eq0);
        shA.AddDirectionalLight(Vector3.up, sky0 - eq0, 0.9f);
        shA.AddDirectionalLight(Vector3.down, gr0 - eq0, 0.9f);
        RenderSettings.ambientProbe = shA;

        // tiempo para el cielo (estrellas que titilan, nubes con el viento)
        if (sky != null)
        {
            sky.SetVector("_SkySunDir", sd); sky.SetVector("_SkyMoonDir", md);
            sky.SetColor("_SkySunCol", sunC * Mathf.Lerp(1f, 0.25f, ov));
            sky.SetColor("_SkyZenith", zen); sky.SetColor("_SkyHorizon", hor); sky.SetColor("_SkyFog", fogC);
            Color cl = Color.Lerp(Color.Lerp(new Color(0.03f, 0.035f, 0.05f), hor * 1.15f + sunC * 0.15f, day), dust, Storm * 0.6f);
            sky.SetColor("_SkyCloudLit", cl);
            sky.SetColor("_SkyCloudDark", Color.Lerp(cl * 0.45f, grey * 0.55f, ov));
            float stars = Night * (1f - ov) * (1f - Storm);
            sky.SetVector("_SkyParams", new Vector4(Cover, stars, moonPhase, Beauty * Night));
            sky.SetVector("_SkyParams2", new Vector4(Time.time + Clock * 0.5f, Dark, Storm, moonVis * moonBright));
            sky.SetFloat("_Exposure", 1f);
        }
        // exposición «del ojo»: de día más baja (arena clara sin quemarse), de noche sube (se intuyen las dunas a la luz de la luna)
        PostStack.Exposure = Mathf.Lerp(0.74f, 1.25f, Night) * Mathf.Lerp(1f, 1.1f, ov * (1f - Night));
        PostStack.Saturation = 1.0f; PostStack.Contrast = 1.1f;
        // la arena (destellos al sol, mojado, nieve, colores del Clima Hermoso)
        Shader.SetGlobalVector("_DesertWeather", new Vector4(SnowCover, Wet, Beauty, day * (1f - ov)));
        TempC = Mathf.Lerp(Params(Previous).temp, Params(Current).temp, t) - Night * 2.5f + day * 0.8f;
        TempC = Mathf.Clamp(TempC, 10f, 17f);
        // reflejos del cielo: se rehacen cuando cambia la luz
        reflT -= dt;
        if (reflT <= 0f && (Mathf.Abs(e - lastReflElev) > 1.5f || dt == 0f)) { DynamicGI.UpdateEnvironment(); lastReflElev = e; reflT = 3f; }
    }

    // degradado del cielo según la altura del sol (grados)
    private static void Gradient5(float e, out Color zen, out Color hor, out Color sunC, out float sunI)
    {
        // claves: -16 noche, -6 crepúsculo, 0 puesta, 8 tarde dorada, 30 mañana, 60 mediodía
        float[] k = { -16f, -6f, 0f, 8f, 30f, 60f };
        Color[] Z = { new Color(0.0025f, 0.0045f, 0.013f), new Color(0.03f, 0.045f, 0.11f), new Color(0.13f, 0.19f, 0.36f), new Color(0.22f, 0.36f, 0.62f), new Color(0.20f, 0.40f, 0.76f), new Color(0.17f, 0.38f, 0.78f) };
        Color[] H = { new Color(0.020f, 0.026f, 0.046f), new Color(0.22f, 0.15f, 0.19f), new Color(0.95f, 0.50f, 0.27f), new Color(0.98f, 0.76f, 0.52f), new Color(0.80f, 0.84f, 0.88f), new Color(0.74f, 0.82f, 0.90f) };
        Color[] S = { new Color(1f, 0.4f, 0.2f), new Color(1f, 0.4f, 0.2f), new Color(1f, 0.46f, 0.22f), new Color(1f, 0.72f, 0.45f), new Color(1f, 0.92f, 0.80f), new Color(1f, 0.96f, 0.90f) };
        float[] I = { 0f, 0f, 0.25f, 0.80f, 1.08f, 1.15f };   // v7.7.0 a: antes 1,4 a mediodía (la arena se quemaba con el ACES)
        int i = 0; while (i < k.Length - 2 && e > k[i + 1]) i++;
        float t = Mathf.Clamp01((e - k[i]) / (k[i + 1] - k[i]));
        t = t * t * (3f - 2f * t);
        zen = Color.Lerp(Z[i], Z[i + 1], t); hor = Color.Lerp(H[i], H[i + 1], t); sunC = Color.Lerp(S[i], S[i + 1], t); sunI = Mathf.Lerp(I[i], I[i + 1], t);
    }

    public static float FarClip { get { return DesertWorld.FarClip; } }
}
