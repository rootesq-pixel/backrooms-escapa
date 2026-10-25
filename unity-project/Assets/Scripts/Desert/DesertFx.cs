using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.7.0 Desierto Cálido: lo que se ve, se oye y se siente del clima y del desierto (solo el jugador local; el clima y la hora
// son los mismos para todos porque salen de la semilla y del reloj del anfitrión):
//  · lluvia (agua de almendras: lechosa y «de buena suerte»; dolor líquido: rojiza, duele pero no hace daño), tormenta de
//    arena (rachas de granos y polvo que lo tapan todo, sin colisión), nieve (copos lentos; la arena se cubre de blanco),
//  · pájaros casi reales, escasos, solo de día: al hacerse de noche desaparecen al instante,
//  · sonido: SIN soplido de aire: granos de arena que corren, gotas sobre la arena, algún pájaro lejano y silencio de noche,
//  · riesgos: arenas movedizas (te hunden y te llevan a otra zona, sin morir), muy rara vez la arena se abre y caes al Vacío
//    (reapareces en otra parte del desierto) y de noche, corriendo cuesta abajo, puedes resbalar.
public class DesertFx : MonoBehaviour
{
    public static DesertFx Active;
    public static bool HazardsOn = true;         // pruebas: se apagan los riesgos al azar (el Vacío)
    public static bool VoidOn = true;
    public int QuickSinks { get; private set; }
    public int VoidFalls { get; private set; }
    public int Slips { get; private set; }
    public float Sink01 { get; private set; }     // 0-1: cuánto te ha tragado la arena
    public bool Busy { get { return phase != 0; } }

    private BackroomsGenerator gen;
    private FirstPersonController fpc;
    private Camera cam;
    private Transform root;
    private DesertWorld dw;
    private ParticleSystem rain, dust, grains, snow;
    private AudioSource sandSrc, rainSrc, birdSrc;
    private AudioClip[] birdCalls;
    private readonly List<Object> owned = new List<Object>();
    private float eye0, walk0, sprint0;
    private System.Random rnd;

    public static DesertFx Create(Transform worldRoot, BackroomsGenerator gen, FirstPersonController fpc, Camera cam, Flashlight fl)
    {
        GameObject go = new GameObject("DesertFx"); go.transform.SetParent(worldRoot, false);
        DesertFx fx = go.AddComponent<DesertFx>();
        fx.gen = gen; fx.fpc = fpc; fx.cam = cam; fx.root = gen.Root; fx.dw = DesertWorld.Active;
        fx.rnd = new System.Random(gen.UsedSeed * 13 + 77 + (NetSession.Instance != null ? NetSession.Instance.LocalId * 7 : 0));
        if (fpc != null) { fx.eye0 = fpc.standEyeHeight; fx.walk0 = fpc.walkSpeed; fx.sprint0 = fpc.sprintSpeed; }
        fx.BuildParticles();
        fx.BuildAudio();
        fx.BuildBirds();
        Active = fx;
        return fx;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        foreach (Object o in owned) ProceduralAssets.SafeDestroy(o);
    }

    // ------------------------------------------------------------------ partículas (siguen a la cámara)
    // partículas con el shader del juego (Hidden/BackroomsParticle: color de vértice + niebla); el color va en startColor
    private Material PMat(Texture2D tex, Color c, bool additive)
    {
        Shader sh = Resources.Load<Shader>(WaterVolume.ParticleShaderResource);
        if (sh == null) sh = Shader.Find("Sprites/Default");
        Material m = new Material(sh); owned.Add(m);
        m.mainTexture = tex; pendingColor = c;
        return m;
    }
    private Color pendingColor = Color.white;

    private static Texture2D SoftDot(int n, float hard)
    {
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01((1f - d) / Mathf.Max(0.01f, 1f - hard)); a *= a;
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply(true); return t;
    }
    private static Texture2D Streak(int w, int h)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + 0.5f) / w * 2f - 1f, v = (y + 0.5f) / h;
                float a = Mathf.Clamp01(1f - Mathf.Abs(u) * 1.4f) * Mathf.Sin(v * Mathf.PI);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
            }
        t.Apply(true); return t;
    }
    private static Texture2D Puff(int n, int seed)
    {
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float nz = Mathf.PerlinNoise(seed + x * 0.07f, y * 0.07f) * 0.6f + Mathf.PerlinNoise(seed + 9 + x * 0.19f, y * 0.19f) * 0.4f;
                float a = Mathf.Clamp01(1f - d) * Mathf.Clamp01(nz * 1.6f - 0.25f);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        t.Apply(true); return t;
    }

    private ParticleSystem MakePS(string name, Material m, int max, ParticleSystemRenderMode mode)
    {
        GameObject g = new GameObject(name); g.transform.SetParent(transform, false);
        ParticleSystem ps = g.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.playOnAwake = false; main.maxParticles = max; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        ParticleSystemRenderer r = g.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = m; r.renderMode = mode; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
        main.startColor = pendingColor;
        var em = ps.emission; em.rateOverTime = 0f;
        ps.Play();
        return ps;
    }

    private void BuildParticles()
    {
        Texture2D streak = Streak(8, 64), dot = SoftDot(32, 0.4f), puff = Puff(64, 3);
        owned.Add(streak); owned.Add(dot); owned.Add(puff);
        // lluvia: gotas alargadas que caen en una caja alrededor de la cámara; chocan con techos y muros (no llueve dentro)
        rain = MakePS("Rain", PMat(streak, new Color(0.8f, 0.82f, 0.85f, 0.35f), false), 5000, ParticleSystemRenderMode.Stretch);
        {
            var m = rain.main; m.startLifetime = 1.4f; m.startSpeed = 0f; m.startSize = 0.018f; m.gravityModifier = 0f;
            var sh = rain.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(34f, 1f, 34f); sh.position = new Vector3(0f, 11f, 0f);
            var vel = rain.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = 1.2f; vel.y = -11f; vel.z = 0.6f;
            ParticleSystemRenderer r = rain.GetComponent<ParticleSystemRenderer>(); r.velocityScale = 0.045f; r.lengthScale = 1f;
            var col = rain.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.quality = ParticleSystemCollisionQuality.Low;
            col.lifetimeLoss = 1f; col.bounce = 0f; col.radiusScale = 0.5f; col.collidesWith = ~(1 << 8);
        }
        // tormenta de arena: nubes de polvo grandes (sin colisión) + granos que vuelan rasantes
        dust = MakePS("SandDust", PMat(puff, new Color(0.80f, 0.60f, 0.40f, 0.30f), false), 260, ParticleSystemRenderMode.Billboard);
        {
            var m = dust.main; m.startLifetime = 5f; m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(5f, 11f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var sh = dust.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(60f, 5f, 60f); sh.position = new Vector3(0f, 2f, 0f);
            var vel = dust.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = 9f; vel.y = 0.3f; vel.z = 5f;
            var c = dust.colorOverLifetime; c.enabled = true;
            Gradient g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            c.color = g;
            var rot = dust.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        }
        grains = MakePS("SandGrains", PMat(streak, new Color(0.86f, 0.70f, 0.50f, 0.55f), false), 2600, ParticleSystemRenderMode.Stretch);
        {
            var m = grains.main; m.startLifetime = 1.6f; m.startSpeed = 0f; m.startSize = 0.012f;
            var sh = grains.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(30f, 2.4f, 30f); sh.position = new Vector3(0f, 0.6f, 0f);
            var vel = grains.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(13f, 19f); vel.y = new ParticleSystem.MinMaxCurve(-0.6f, 0.8f); vel.z = new ParticleSystem.MinMaxCurve(6f, 10f);
            ParticleSystemRenderer r = grains.GetComponent<ParticleSystemRenderer>(); r.velocityScale = 0.03f;
        }
        // nieve: copos que caen despacio y se mecen
        snow = MakePS("Snow", PMat(dot, new Color(1f, 1f, 1f, 0.9f), false), 4000, ParticleSystemRenderMode.Billboard);
        {
            var m = snow.main; m.startLifetime = 9f; m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            var sh = snow.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(36f, 1f, 36f); sh.position = new Vector3(0f, 9f, 0f);
            var vel = snow.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.x = new ParticleSystem.MinMaxCurve(0.2f, 1.0f); vel.y = new ParticleSystem.MinMaxCurve(-1.1f, -0.7f); vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.5f);
            var no = snow.noise; no.enabled = true; no.strength = 0.6f; no.frequency = 0.25f; no.scrollSpeed = 0.2f; no.quality = ParticleSystemNoiseQuality.Low;
            var col = snow.collision; col.enabled = true; col.type = ParticleSystemCollisionType.World; col.quality = ParticleSystemCollisionQuality.Low; col.lifetimeLoss = 1f; col.bounce = 0f; col.collidesWith = ~(1 << 8);
        }
    }

    // ------------------------------------------------------------------ sonido procedimental (sin soplidos de aire)
    private void BuildAudio()
    {
        int sd = gen != null ? gen.UsedSeed : 1;
        sandSrc = Src(SandTrickle(sd), 0f); rainSrc = Src(RainPatter(sd + 1), 0f);
        birdCalls = new[] { BirdCall(sd + 2, 0), BirdCall(sd + 3, 1), BirdCall(sd + 4, 2) };
        foreach (AudioClip c in birdCalls) owned.Add(c);
        // v7.8.0: viento fluido (síntesis periódica sin costura, sin siseo); apagado dentro de casas y cuevas
        DesertWind.Create(transform, sd, () => cam != null && ((dw != null && dw.UnderRoof(cam.transform.position)) || InCave(cam.transform.position)));
        if (cam != null) DesertNight.Create(transform, sd, cam.transform);   // v7.9.0: sonidos lejanos de noche
        birdSrc = gameObject.AddComponent<AudioSource>(); birdSrc.spatialBlend = 0.85f; birdSrc.playOnAwake = false; birdSrc.minDistance = 8f; birdSrc.maxDistance = 120f; birdSrc.rolloffMode = AudioRolloffMode.Linear;
    }
    private AudioSource Src(AudioClip c, float v)
    {
        owned.Add(c);
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.clip = c; s.loop = true; s.spatialBlend = 0f; s.volume = v; s.playOnAwake = false; s.Play();
        return s;
    }
    // granos de arena que corren: chasquidos secos muy cortos (no ruido continuo), en ráfagas suaves
    private static AudioClip SandTrickle(int seed)
    {
        const int SR = 22050; int n = SR * 8; float[] d = new float[n];
        System.Random r = new System.Random(seed);
        float hp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float dens = 0.0025f + 0.004f * (0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 8f * 3f)) * (0.5f + 0.5f * Mathf.Sin(t * 2f * Mathf.PI / 8f + 1f));
            float x = r.NextDouble() < dens ? (float)(r.NextDouble() * 2.0 - 1.0) * (0.3f + 0.7f * (float)r.NextDouble()) : 0f;
            hp = 0.6f * (hp + x - prev); prev = x;   // paso alto suave: «tic» de grano
            d[i] = hp;
        }
        // decaimiento corto de cada chasquido (resonador)
        float y1 = 0f, y2 = 0f; float w = 2f * Mathf.PI * 3200f / SR, rr = 0.82f;
        for (int i = 0; i < n; i++) { float y = d[i] + 2f * rr * Mathf.Cos(w) * y1 - rr * rr * y2; y2 = y1; y1 = y; d[i] = y * 0.25f; }
        Normalize(d, 0.6f);
        AudioClip c = AudioClip.Create("DesertSand", n, 1, SR, false); c.SetData(d, 0); return c;
    }
    // gotas sobre la arena: golpes graves y amortiguados, muchos y desordenados
    private static AudioClip RainPatter(int seed)
    {
        const int SR = 22050; int n = SR * 6; float[] d = new float[n];
        System.Random r = new System.Random(seed);
        for (int k = 0; k < 2600; k++)
        {
            int at = r.Next(n); float f = 900f + (float)r.NextDouble() * 1800f, a = 0.2f + (float)r.NextDouble() * 0.8f, dec = 140f + (float)r.NextDouble() * 160f;
            for (int j = 0; j < 500 && at + j < n; j++) { float t = j / (float)SR; d[at + j] += Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * dec) * a; }
        }
        float lp = 0f; for (int i = 0; i < n; i++) { lp += 0.35f * (d[i] - lp); d[i] = lp; }
        // costura del bucle
        for (int i = 0; i < 400; i++) { float u = i / 400f; d[i] = d[i] * u + d[n - 400 + i] * (1f - u); }
        Normalize(d, 0.6f);
        AudioClip c = AudioClip.Create("DesertRain", n, 1, SR, false); c.SetData(d, 0); return c;
    }
    // canto corto de pájaro (silbidos con glissando), tres variantes
    private static AudioClip BirdCall(int seed, int kind)
    {
        const int SR = 22050; int n = SR * 2; float[] d = new float[n];
        System.Random r = new System.Random(seed);
        int notes = kind == 0 ? 2 : kind == 1 ? 4 : 3; float at = 0.05f;
        for (int k = 0; k < notes; k++)
        {
            float len = 0.09f + (float)r.NextDouble() * 0.16f, f0 = 2400f + (float)r.NextDouble() * 1600f, f1 = f0 * (kind == 2 ? 0.7f : 1.25f), ph = 0f;
            int a = (int)(at * SR), m = (int)(len * SR);
            for (int j = 0; j < m && a + j < n; j++)
            {
                float u = j / (float)m, f = Mathf.Lerp(f0, f1, u) * (1f + 0.04f * Mathf.Sin(u * 60f));
                ph += 2f * Mathf.PI * f / SR;
                d[a + j] += Mathf.Sin(ph) * Mathf.Sin(u * Mathf.PI) * 0.6f;
            }
            at += len + 0.04f + (float)r.NextDouble() * 0.12f;
        }
        Normalize(d, 0.5f);
        AudioClip c = AudioClip.Create("DesertBird" + kind, n, 1, SR, false); c.SetData(d, 0); return c;
    }
    private static void Normalize(float[] d, float peak) { float mx = 1e-5f; foreach (float v in d) mx = Mathf.Max(mx, Mathf.Abs(v)); for (int i = 0; i < d.Length; i++) d[i] *= peak / mx; }

    // ------------------------------------------------------------------ pájaros (de día, escasos; de noche desaparecen al instante)
    private class Bird { public Transform t, wl, wr; public Vector3 c; public float r, h, w, ph, flap; }
    private readonly List<Bird> birds = new List<Bird>();
    private Transform birdRoot;
    private int birdSeg = -999;
    public int BirdsVisible { get; private set; }

    private void BuildBirds()
    {
        birdRoot = new GameObject("Birds").transform; birdRoot.SetParent(transform, false);
        Material m = ProceduralAssets.CreateMaterial(new Color(0.10f, 0.09f, 0.08f), null, Vector2.one, 0.15f); owned.Add(m);
        Mesh body = BirdBody(), wing = BirdWing(); owned.Add(body); owned.Add(wing);
        for (int i = 0; i < 5; i++)
        {
            Bird b = new Bird();
            b.t = new GameObject("Bird" + i).transform; b.t.SetParent(birdRoot, false);
            GameObject bg = new GameObject("Body"); bg.transform.SetParent(b.t, false); bg.AddComponent<MeshFilter>().sharedMesh = body; bg.AddComponent<MeshRenderer>().sharedMaterial = m;
            b.wl = new GameObject("WingL").transform; b.wl.SetParent(b.t, false); b.wl.localPosition = new Vector3(-0.04f, 0.02f, 0.02f);
            GameObject wl = new GameObject("Mesh"); wl.transform.SetParent(b.wl, false); wl.transform.localScale = new Vector3(-1f, 1f, 1f); wl.AddComponent<MeshFilter>().sharedMesh = wing; wl.AddComponent<MeshRenderer>().sharedMaterial = m;
            b.wr = new GameObject("WingR").transform; b.wr.SetParent(b.t, false); b.wr.localPosition = new Vector3(0.04f, 0.02f, 0.02f);
            GameObject wr = new GameObject("Mesh"); wr.transform.SetParent(b.wr, false); wr.AddComponent<MeshFilter>().sharedMesh = wing; wr.AddComponent<MeshRenderer>().sharedMaterial = m;
            foreach (MeshRenderer mr in b.t.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.Off;
            b.t.gameObject.SetActive(false);
            birds.Add(b);
        }
    }
    private static Mesh BirdBody()
    {
        DesertMesh dm = new DesertMesh(1);
        dm.Log(0, Matrix4x4.identity, new Vector3(0f, 0f, -0.22f), new Vector3(0f, 0.01f, 0.16f), 0.05f, 6);
        dm.Log(0, Matrix4x4.identity, new Vector3(0f, 0.02f, 0.16f), new Vector3(0f, 0.03f, 0.25f), 0.035f, 6);
        dm.Box(0, Matrix4x4.TRS(new Vector3(0f, 0f, -0.27f), Quaternion.identity, Vector3.one), Vector3.zero, new Vector3(0.12f, 0.012f, 0.12f));
        return dm.Build("BirdBody");
    }
    private static Mesh BirdWing()
    {
        DesertMesh dm = new DesertMesh(1);
        Vector3 a = new Vector3(0f, 0f, 0.07f), b = new Vector3(0.42f, 0f, 0.0f), c = new Vector3(0.55f, 0f, -0.12f), d = new Vector3(0f, 0f, -0.08f);
        dm.QuadO(0, Vector3.up, a, b, c, d, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        dm.QuadO(0, Vector3.down, a, b, c, d, Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        return dm.Build("BirdWing");
    }

    private void UpdateBirds(DesertSky sky, Vector3 camLocal)
    {
        // pocos y no siempre: en cada tramo de clima, ~40% de probabilidad de una bandada de 1-5 (igual para todos: semilla + tramo)
        bool day = sky.SunElev > 0.5f && sky.Storm < 0.3f && sky.Rain < 0.3f && sky.Snow < 0.3f;
        if (sky.Segment != birdSeg)
        {
            birdSeg = sky.Segment;
            System.Random r = new System.Random((gen != null ? gen.UsedSeed : 1) * 31 + sky.Segment * 977);
            int n = r.NextDouble() < 0.4 || DesertSky.ForceHour >= 0f ? 1 + r.Next(5) : 0;
            Vector3 c = new Vector3((float)r.NextDouble() * dwL, 0f, (float)r.NextDouble() * dwL);
            for (int i = 0; i < birds.Count; i++)
            {
                Bird b = birds[i]; b.c = c + new Vector3((float)r.NextDouble() - 0.5f, 0f, (float)r.NextDouble() - 0.5f) * 30f;
                b.r = 18f + (float)r.NextDouble() * 30f; b.h = 22f + (float)r.NextDouble() * 30f; b.w = (r.NextDouble() < 0.5 ? -1f : 1f) * (0.12f + (float)r.NextDouble() * 0.1f);
                b.ph = (float)r.NextDouble() * 6.28f; b.flap = (float)r.NextDouble() * 6.28f;
                b.t.gameObject.SetActive(i < n);
            }
            // si la bandada cae lejos, se acerca al jugador (se ven pasar)
            if (DesertSky.ForceHour >= 0f) foreach (Bird b in birds) b.c = camLocal + (b.c - c) + new Vector3(10f, 0f, 25f);
        }
        int vis = 0;
        float t = sky.Clock;
        for (int i = 0; i < birds.Count; i++)
        {
            Bird b = birds[i];
            bool on = b.t.gameObject.activeSelf && day;
            MeshRenderer[] mrs = null;
            if (!on) { if (b.t.gameObject.activeSelf) SetVis(b, false); continue; }
            SetVis(b, true); vis++;
            float a = b.ph + t * b.w;
            Vector3 cc = b.c + new Vector3(Mathf.Sin(t * 0.013f + i) * 40f, 0f, Mathf.Cos(t * 0.011f + i) * 40f);
            Vector3 lp = cc + new Vector3(Mathf.Cos(a) * b.r, 0f, Mathf.Sin(a) * b.r);
            // la imagen más cercana del desierto (se ven por encima del borde)
            lp.x = camLocal.x + DesertHeight.Wrap(lp.x - camLocal.x); lp.z = camLocal.z + DesertHeight.Wrap(lp.z - camLocal.z);
            lp.y = DesertHeight.H(Mathf.Repeat(lp.x, dwL), Mathf.Repeat(lp.z, dwL)) + b.h + Mathf.Sin(t * 0.3f + i) * 3f;
            Vector3 dir = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * Mathf.Sign(b.w);
            b.t.position = root.TransformPoint(lp);
            b.t.rotation = root.rotation * Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 0f, -Mathf.Sign(b.w) * 18f);
            // aleteo a ratos, planeo el resto
            float glide = Mathf.PerlinNoise(i * 3.1f, t * 0.25f);
            float ang = glide > 0.55f ? Mathf.Sin(b.flap + t * 14f) * 38f : 4f + Mathf.Sin(t * 1.3f + i) * 3f;
            b.wl.localRotation = Quaternion.Euler(0f, 0f, ang); b.wr.localRotation = Quaternion.Euler(0f, 0f, -ang);
            if (mrs != null) { }
        }
        BirdsVisible = vis;
        // algún canto lejano de vez en cuando (de día)
        if (vis > 0 && !birdSrc.isPlaying && Random.value < Time.deltaTime * 0.06f)
        {
            Bird b = birds[Random.Range(0, vis)];
            birdSrc.transform.position = b.t.position; birdSrc.clip = birdCalls[Random.Range(0, birdCalls.Length)];
            birdSrc.volume = AudioMix.A(0.22f); birdSrc.pitch = 0.9f + Random.value * 0.2f; birdSrc.Play();
        }
    }
    private void SetVis(Bird b, bool on)
    {
        foreach (MeshRenderer mr in b.t.GetComponentsInChildren<MeshRenderer>(true)) mr.enabled = on;
    }
    private float dwL { get { return dw != null ? dw.L : 512f; } }

    // ------------------------------------------------------------------ cada fotograma
    private float painPulse, painT, overlayA, lastToastSeg = -1, almondT;
    private Color overlayC;
    private void Update()
    {
        DesertSky sky = DesertSky.Active;
        if (sky == null || cam == null) return;
        float dt = Time.deltaTime;
        Vector3 cp = cam.transform.position;
        bool roof = dw != null && dw.UnderRoof(cp);
        bool inCave = InCave(cp);
        float exposed = roof || inCave ? 0f : 1f;
        transform.position = new Vector3(cp.x, cp.y, cp.z);
        float q = GameSettings.Quality == 0 ? 0.45f : GameSettings.Quality == 1 ? 0.75f : 1f;
        SetRate(rain, sky.Rain * 3200f * q);
        SetRate(snow, sky.Snow * 900f * q);
        SetRate(dust, sky.Storm * 55f * q);
        SetRate(grains, sky.Storm * 1600f * q);
        // color de la lluvia: lechosa (almendras) o rojiza (dolor líquido)
        if (rain != null)
        {
            Color rc = sky.PainK > 0.5f ? new Color(0.85f, 0.45f, 0.42f, 0.42f) : new Color(0.92f, 0.90f, 0.84f, 0.40f);
            float lum = Mathf.Lerp(0.12f, 1f, 1f - sky.Night);
            var rmn = rain.main; rmn.startColor = new Color(rc.r * lum, rc.g * lum, rc.b * lum, rc.a);
            var dmn = dust.main; float dl = Mathf.Lerp(0.06f, 1f, 1f - sky.Night); dmn.startColor = new Color(0.80f * dl, 0.60f * dl, 0.40f * dl, 0.30f);
            var gmn = grains.main; gmn.startColor = new Color(0.86f * dl, 0.70f * dl, 0.50f * dl, 0.55f);
            var smn = snow.main; smn.startColor = new Color(dl, dl, dl * 1.02f, 0.9f);
        }
        // sonido: arena con la tormenta (y un poco siempre de día), lluvia; de noche, casi silencio
        float sandV = sky.Storm * 0.5f + (1f - sky.Night) * 0.035f;
        sandSrc.volume = Mathf.MoveTowards(sandSrc.volume, AudioMix.A(sandV * (roof ? 0.45f : 1f)), dt * 0.3f);
        rainSrc.volume = Mathf.MoveTowards(rainSrc.volume, AudioMix.A(sky.Rain * (roof ? 0.25f : 0.45f)), dt * 0.3f);
        Vector3 camLocal = root != null ? root.InverseTransformPoint(cp) : cp;
        UpdateBirds(sky, camLocal);
        // avisos del clima al cambiar (con la temperatura)
        if (sky.Segment != lastToastSeg && DesertSky.ForceWeather < 0)
        {
            bool first = lastToastSeg < 0; lastToastSeg = sky.Segment;
            if (!first || sky.Current != DesertSky.Weather.Clear)
                if (GameManager.Instance != null) GameManager.Instance.ShowToast(DesertSky.WeatherName[(int)sky.Current] + " · " + Mathf.RoundToInt(sky.TempC) + " °C");
        }
        // lluvia de almendras: «buena suerte» (bajo la lluvia recuperas resistencia y cordura poco a poco)
        if (sky.Rain > 0.5f && sky.PainK < 0.5f && exposed > 0f)
        {
            almondT += dt;
            if (fpc != null) fpc.RestoreStamina(dt * 0.08f);
            if (Sanity.Instance != null) Sanity.Instance.Add(dt * 0.6f);
        }
        // dolor líquido: duele (pulsos rojos, sacudidas) pero NO hace daño
        painT -= dt;
        if (sky.Rain > 0.5f && sky.PainK > 0.5f && exposed > 0f && painT <= 0f) { painPulse = 1f; painT = 1.2f + Random.value * 2.2f; }
        painPulse = Mathf.MoveTowards(painPulse, 0f, dt * 1.6f);
        UpdateHazards(sky, dt, camLocal);
        // capa de pantalla: fundido (arenas movedizas / Vacío), dolor, tormenta
        Color c = new Color(0f, 0f, 0f, fade);
        if (painPulse > 0f && fade < 0.01f) c = new Color(0.55f, 0.03f, 0.02f, painPulse * 0.32f);
        else if (sky.Storm > 0.05f && fade < 0.01f) c = new Color(0.62f, 0.45f, 0.28f, sky.Storm * 0.22f * exposed);
        overlayC = c; overlayA = c.a;
        if (painPulse > 0.2f && cam != null) cam.transform.localRotation *= Quaternion.Euler((Random.value - 0.5f) * painPulse * 1.2f, (Random.value - 0.5f) * painPulse * 1.2f, 0f);
    }
    private static void SetRate(ParticleSystem ps, float r) { if (ps == null) return; var em = ps.emission; em.rateOverTime = r; }

    public bool InCave(Vector3 world)
    {
        if (dw == null || root == null) return false;
        Vector3 lp = root.InverseTransformPoint(world);
        foreach (DesertPlan.Outcrop o in dw.plan.outcrops)
        {
            if (!o.cave) continue;
            Vector2 d = new Vector2(DesertHeight.Wrap(lp.x - o.c.x), DesertHeight.Wrap(lp.z - o.c.y));
            if (d.magnitude < 5.5f) return true;
        }
        return false;
    }

    private void OnGUI()
    {
        if (overlayA <= 0.002f || Event.current.type != EventType.Repaint) return;
        Color old = GUI.color; GUI.color = overlayC;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;
    }

    // ------------------------------------------------------------------ riesgos
    private int phase;             // 0 nada, 1 hundiéndose, 2 fundido a negro, 3 aparecer
    private float phaseT, fade, sinkT, voidCheckT = 20f, slipT;
    private Vector3 targetLocal;
    private int lastQuick = -1;
    public static float LevelTimeForVoid = 360f;   // el Vacío no puede pasar antes de 6 min en el nivel
    private float levelT;

    private void UpdateHazards(DesertSky sky, float dt, Vector3 camLocal)
    {
        levelT += dt;
        if (fpc == null || dw == null) return;
        Vector3 pl = root.InverseTransformPoint(fpc.transform.position);
        Vector2 p2 = new Vector2(Mathf.Repeat(pl.x, dw.L), Mathf.Repeat(pl.z, dw.L));
        bool downed = NetSession.Instance != null && NetSession.Instance.LocalDowned;
        switch (phase)
        {
            case 0:
            {
                int qi = HazardsOn && !downed ? dw.plan.QuickAt(p2, -0.6f) : -1;
                if (qi >= 0 && fpc.IsGrounded)
                {
                    sinkT += dt;
                    // te hunde poco a poco: más lento, los ojos cada vez más cerca de la arena
                    Sink01 = Mathf.Clamp01(sinkT / 5.5f);
                    if (sinkT > 0.4f && lastQuick != qi) { lastQuick = qi; if (GameManager.Instance != null) GameManager.Instance.ShowToast("¡Arenas movedizas! Sal de ahí"); }
                    if (Sink01 >= 1f) { phase = 2; phaseT = 0f; QuickSinks++; targetLocal = ZoneTarget(dw.plan.quick[qi].target, 1); }
                }
                else
                {
                    sinkT = Mathf.Max(0f, sinkT - dt * 1.5f); Sink01 = Mathf.Clamp01(sinkT / 5.5f);
                    if (sinkT <= 0f) lastQuick = -1;
                }
                ApplySink();
                // muy rara vez, la arena se abre bajo tus pies y caes al Vacío (no antes de 6 min; ~1 vez cada 40 min de media)
                voidCheckT -= dt;
                if (voidCheckT <= 0f)
                {
                    voidCheckT = 20f;
                    if (VoidOn && HazardsOn && !downed && levelT > LevelTimeForVoid && fpc.IsGrounded && dw.plan.FreeOf(p2, 6f, 8f) && rnd.NextDouble() < 1.0 / 120.0)
                    { phase = 4; phaseT = 0f; VoidFalls++; targetLocal = ZoneTarget(rnd.Next(dw.plan.zones.Count), 2); if (GameManager.Instance != null) GameManager.Instance.ShowToast("La arena cede bajo tus pies..."); }
                }
                // de noche, corriendo cuesta abajo por una duna empinada, puedes resbalar
                slipT -= dt;
                if (HazardsOn && sky.Night > 0.6f && fpc.IsSprinting && slipT <= 0f)
                {
                    Vector3 n = DesertHeight.Normal(p2.x, p2.y);
                    Vector3 v = fpc.transform.forward; float downhill = Vector3.Dot(new Vector3(n.x, 0f, n.z), new Vector3(v.x, 0f, v.z));
                    if (n.y < 0.9f && downhill > 0.12f && rnd.NextDouble() < dt * 0.25f) { slipT = 8f; Slips++; StartCoroutine(Slip()); }
                }
                break;
            }
            case 2:   // tragado: fundido a negro y a otra zona del desierto
                phaseT += dt; fade = Mathf.Clamp01(phaseT / 0.8f);
                if (phaseT > 1.2f) { Arrive(); }
                break;
            case 3:   // reapareces: escupido por la arena
                phaseT += dt; fade = 1f - Mathf.Clamp01(phaseT / 1.4f); Sink01 = Mathf.Clamp01(1f - phaseT / 1.2f) * 0.5f; ApplySink();
                if (phaseT > 1.6f) { phase = 0; fade = 0f; sinkT = 0f; Sink01 = 0f; ApplySink(); }
                break;
            case 4:   // caída al Vacío: la vista se hunde de golpe, negro un rato, y reapareces lejos
                phaseT += dt; Sink01 = Mathf.Clamp01(phaseT / 0.5f); ApplySink(); fade = Mathf.Clamp01((phaseT - 0.2f) / 0.5f);
                if (phaseT > 3.2f) { if (GameManager.Instance != null) GameManager.Instance.ShowToast("Has caído al Vacío... y el desierto te devuelve a otra parte"); Arrive(); }
                break;
        }
    }

    private Vector3 ZoneTarget(int zone, int salt)
    {
        Vector2 z = dw.plan.zones[Mathf.Clamp(zone, 0, dw.plan.zones.Count - 1)];
        System.Random r = new System.Random((gen != null ? gen.UsedSeed : 1) * 7 + zone * 31 + salt + QuickSinks * 3 + VoidFalls * 5);
        for (int k = 0; k < 30; k++)
        {
            Vector2 q = new Vector2(Mathf.Repeat(z.x + ((float)r.NextDouble() - 0.5f) * 14f, dw.L), Mathf.Repeat(z.y + ((float)r.NextDouble() - 0.5f) * 14f, dw.L));
            if (dw.plan.FreeOf(q, 2f, 6f, false) && DesertHeight.Normal(q.x, q.y).y > 0.9f) return new Vector3(q.x, DesertHeight.H(q.x, q.y) + 0.15f, q.y);
        }
        return new Vector3(z.x, DesertHeight.H(z.x, z.y) + 0.15f, z.y);
    }

    private void Arrive()
    {
        fpc.Teleport(root.TransformPoint(targetLocal), fpc.transform.eulerAngles.y + 120f);
        phase = 3; phaseT = 0f; lastQuick = -1;
    }

    private void ApplySink()
    {
        if (fpc == null) return;
        fpc.standEyeHeight = Mathf.Lerp(eye0, 0.25f, Sink01);
        float sp = Mathf.Lerp(1f, 0.25f, Mathf.Clamp01(Sink01 * 1.6f));
        fpc.walkSpeed = walk0 * sp; fpc.sprintSpeed = sprint0 * sp;
    }

    private System.Collections.IEnumerator Slip()
    {
        if (GameManager.Instance != null) GameManager.Instance.ShowToast("Resbalas en la arena");
        float t = 0f; bool was = fpc.InputEnabled; fpc.InputEnabled = false;
        while (t < 0.9f)
        {
            t += Time.deltaTime;
            float u = t / 0.9f;
            fpc.standEyeHeight = Mathf.Lerp(eye0, 0.6f, Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI));
            if (cam != null) cam.transform.localRotation *= Quaternion.Euler(0f, 0f, Mathf.Sin(u * Mathf.PI) * 9f);
            yield return null;
        }
        fpc.standEyeHeight = eye0; fpc.InputEnabled = was;
    }
}
