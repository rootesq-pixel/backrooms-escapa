using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Volumen de agua de un nivel (nivel 2: Poolrooms).
//  - Superficie: mallas por chunk (rejilla de 0,5 m) con el shader "BackroomsWater" (olas Gerstner, fresnel, reflejo con
//    ReflectionProbe, brillos de las luces del techo, tinte por profundidad y espuma junto a las paredes en el color de vértice).
//    Si el shader no carga se usa el material Standard transparente de antes (sin olas) como alternativa.
//  - Interacción: ondas circulares (ripples) y salpicaduras en pisadas del jugador y del monstruo, profundidad para frenar/nadar,
//    objetos flotantes con flotabilidad, estado "bajo el agua" de la cámara.
public class WaterVolume : MonoBehaviour
{
    public static WaterVolume Active { get; private set; }
    public const string ShaderResource = "Water/BackroomsWater";
    public const string ParticleShaderResource = "Water/BackroomsParticle";
    public const int WaterLayer = 4;

    public float surfaceY;                 // Y mundo de la superficie en reposo
    public float surfaceLocalY;
    public BackroomsGenerator generator;
    public Material material;
    public bool usingFallback;
    public ReflectionProbe probe;
    public int chunkCount;
    public float waveAmp = 1f;
    // v7.3.0: mar abierto (Talasofobia): sin rejilla del generador; fondo a decenas de metros; superficie que sigue a la cámara;
    // bolsas de aire (la gruta de la salida) con su propia superficie a otra altura
    public bool ocean;
    public System.Func<Vector3, float> surfaceOverride;   // devuelve NaN si no hay bolsa en ese punto
    public float oceanFloorFallback = -45f;
    public readonly List<FloatingDebris> debris = new List<FloatingDebris>();

    // Ondas circulares (se envían al shader como _WRip[16])
    private const int MaxRipples = 16;
    private readonly Vector4[] ripples = new Vector4[MaxRipples];
    private int nextRipple;
    public int RippleCount { get; private set; }

    private readonly Vector4[] lightPos = new Vector4[4];
    private readonly Vector4[] lightCol = new Vector4[4];
    private readonly List<Light> lights = new List<Light>();
    private Light flashSpot;
    private float lightRefreshT;

    private ParticleSystem splashPs;
    private Material particleMat;
    private Texture2D dropTex;
    private Texture2D noiseTex;
    private AudioClip splashClip;
    private AudioSource[] splashSources;
    private int splashSrcIndex;
    private float probeTimer;
    private readonly List<Object> owned = new List<Object>();

    // ------------------------------------------------------------------
    // Creación
    // ------------------------------------------------------------------
    public static WaterVolume Create(BackroomsGenerator gen, LevelConfig cfg, Transform parent, SurfaceSet normalSet)
    {
        GameObject go = new GameObject("WaterVolume");
        go.transform.SetParent(parent, false);
        WaterVolume v = go.AddComponent<WaterVolume>();
        v.generator = gen;
        v.surfaceLocalY = cfg.waterLevel;
        v.surfaceY = gen.Root.TransformPoint(new Vector3(0f, cfg.waterLevel, 0f)).y;
        v.BuildMaterial(cfg, normalSet);
        if (cfg.index == 2 && !v.usingFallback)
        {   // v7.1.3: toda el agua del nivel Poolrooms con el aspecto ajustado a Cycles (no solo la sala de piscina)
            PoolLook.ApplyLevelWide();
            PoolPlanarReflection.Ensure(go, v.surfaceY);
        }
        v.BuildSurface(gen, cfg, go.transform);
        v.BuildDebris(gen, cfg);
        v.GatherLights(gen);
        if (Application.isPlaying)
        {
            v.BuildFx(gen, cfg);
            Active = v;
        }
        return v;
    }

    public static WaterVolume CreateOcean(BackroomsGenerator gen, LevelConfig cfg, Transform parent, SurfaceSet normalSet, float halfSize)
    {
        GameObject go = new GameObject("WaterVolume");
        go.transform.SetParent(parent, false);
        WaterVolume v = go.AddComponent<WaterVolume>();
        v.generator = gen;
        v.ocean = true;
        v.surfaceLocalY = cfg.waterLevel;
        v.surfaceY = gen.Root.TransformPoint(new Vector3(0f, cfg.waterLevel, 0f)).y;
        v.waveAmp = OceanWaveAmp;
        v.BuildMaterial(cfg, normalSet);
        if (!v.usingFallback)
        {   // mismo aspecto que el agua de las Poolrooms (v7.1.3), con más absorción (agua profunda)
            PoolLook.ApplyLevelWide();
            Shader.SetGlobalVector("_PoolWaterAbs", new Vector4(0.16f, 0.06f, 0.05f, 1f));
            Shader.SetGlobalVector("_PoolCausticK", Vector4.zero);
            Shader.SetGlobalVector("_PoolEdgeP", Vector4.zero);
            PoolPlanarReflection.Ensure(go, v.surfaceY);
        }
        v.BuildOceanSurface(go.transform, halfSize);
        if (Application.isPlaying)
        {
            v.BuildFx(gen, cfg);
            Active = v;
        }
        return v;
    }

    public const float OceanWaveAmp = 7f;
    public Transform oceanSurface;

    // malla de superficie: densa en el centro (1 m) y cada vez más gruesa hacia fuera; sigue a la cámara a saltos de 8 m
    private void BuildOceanSurface(Transform parent, float halfSize)
    {
        List<float> ticks = new List<float>();
        float x = 0f; ticks.Add(0f);
        while (x < halfSize) { float st = x < 48f ? 1f : (x < 120f ? 3f : (x < 260f ? 10f : 40f)); x = Mathf.Min(halfSize, x + st); ticks.Add(x); }
        List<float> axis = new List<float>();
        for (int i = ticks.Count - 1; i > 0; i--) axis.Add(-ticks[i]);
        axis.AddRange(ticks);
        int n = axis.Count;
        Vector3[] vs = new Vector3[n * n]; Vector3[] ns = new Vector3[vs.Length]; Color32[] cols = new Color32[vs.Length];
        for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
        {
            int k = j * n + i;
            vs[k] = new Vector3(axis[i], 0f, axis[j]); ns[k] = Vector3.up; cols[k] = new Color32(0, 255, 0, 255);
        }
        int[] tris = new int[(n - 1) * (n - 1) * 6]; int t = 0;
        for (int j = 0; j < n - 1; j++) for (int i = 0; i < n - 1; i++)
        {
            int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
            tris[t++] = a; tris[t++] = c; tris[t++] = d; tris[t++] = a; tris[t++] = d; tris[t++] = b;
        }
        Mesh m = Own(new Mesh());
        m.name = "OceanSurface";
        m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.vertices = vs; m.normals = ns; m.colors32 = cols; m.triangles = tris;
        m.bounds = new Bounds(Vector3.zero, new Vector3(halfSize * 2f, 2f, halfSize * 2f));
        GameObject g = new GameObject("OceanSurface");
        g.layer = WaterLayer;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = new Vector3(0f, surfaceLocalY, 0f);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        oceanSurface = g.transform;
        chunkCount = 1;
    }

    // superficie extra (bolsa de aire de la gruta): un plano pequeño con el mismo material
    public GameObject AddPocketSurface(Vector3 center, Vector2 size)
    {
        GameObject g = new GameObject("PocketSurface");
        g.layer = WaterLayer;
        g.transform.SetParent(transform, false);
        g.transform.position = center;
        int qx = Mathf.Max(1, Mathf.CeilToInt(size.x)), qz = Mathf.Max(1, Mathf.CeilToInt(size.y));
        Vector3[] vs = new Vector3[(qx + 1) * (qz + 1)]; Vector3[] ns = new Vector3[vs.Length]; Color32[] cols = new Color32[vs.Length];
        for (int j = 0; j <= qz; j++) for (int i = 0; i <= qx; i++)
        {
            int k = j * (qx + 1) + i;
            vs[k] = new Vector3((i / (float)qx - 0.5f) * size.x, 0f, (j / (float)qz - 0.5f) * size.y); ns[k] = Vector3.up; cols[k] = new Color32(0, 200, 0, 255);
        }
        int[] tris = new int[qx * qz * 6]; int t = 0;
        for (int j = 0; j < qz; j++) for (int i = 0; i < qx; i++)
        {
            int a = j * (qx + 1) + i, b = a + 1, c = a + qx + 1, d = c + 1;
            tris[t++] = a; tris[t++] = c; tris[t++] = d; tris[t++] = a; tris[t++] = d; tris[t++] = b;
        }
        Mesh m = Own(new Mesh()); m.name = "PocketSurface";
        m.vertices = vs; m.normals = ns; m.colors32 = cols; m.triangles = tris; m.RecalculateBounds();
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        return g;
    }

    private T Own<T>(T o) where T : Object { owned.Add(o); return o; }

    private void BuildMaterial(LevelConfig cfg, SurfaceSet normalSet)
    {
        Shader sh = Resources.Load<Shader>(ShaderResource);
        bool ok = sh != null && sh.isSupported;
        if (ok)
        {
            material = Own(new Material(sh));
            Color shallow = cfg.waterColor;
            material.SetColor("_Color", new Color(shallow.r, shallow.g, shallow.b, Mathf.Clamp(shallow.a, 0.25f, 0.6f)));
            material.SetColor("_Deep", new Color(shallow.r * 0.12f, shallow.g * 0.22f, shallow.b * 0.28f, 0.96f));
            material.SetColor("_UnderCol", new Color(shallow.r * 0.35f, shallow.g * 0.62f, shallow.b * 0.7f, 0.88f));
            if (normalSet != null && normalSet.normal != null) material.SetTexture("_NormalMap", normalSet.normal);
            noiseTex = Own(MakeNoise(128));
            material.SetTexture("_NoiseTex", noiseTex);
            usingFallback = false;
        }
        else
        {
            usingFallback = true;
            material = Own(ProceduralAssets.CreateWaterMaterial(normalSet, cfg.waterColor));
            WaterScroller sc = gameObject.AddComponent<WaterScroller>();
            sc.material = material;
            Debug.LogWarning("[WaterVolume] Shader de agua no disponible: se usa el material Standard transparente.");
        }
    }

    private static Texture2D MakeNoise(int n)
    {
        Texture2D t = new Texture2D(n, n, TextureFormat.R8, false, true);
        t.name = "WaterNoise";
        byte[] px = new byte[n * n];
        float[] f = Noise.Fbm(n, 6, 6, 4, 0.55f, 77);
        for (int i = 0; i < px.Length; i++) px[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(f[i] * 255f), 0, 255);
        t.LoadRawTextureData(px);
        t.wrapMode = TextureWrapMode.Repeat;
        t.Apply(false, true);
        return t;
    }

    private void BuildSurface(BackroomsGenerator gen, LevelConfig cfg, Transform parent)
    {
        float cs = cfg.cellSize;
        float totalW = Mathf.Max(3, cfg.gridW) * cs, totalH = Mathf.Max(3, cfg.gridH) * cs;
        const float chunk = 8f, step = 0.5f;
        int nx = Mathf.CeilToInt(totalW / chunk), nz = Mathf.CeilToInt(totalH / chunk);
        float maxDepth = 2.3f;
        for (int cx = 0; cx < nx; cx++)
        {
            for (int cz = 0; cz < nz; cz++)
            {
                float x0 = cx * chunk, z0 = cz * chunk;
                float x1 = Mathf.Min(totalW, x0 + chunk), z1 = Mathf.Min(totalH, z0 + chunk);
                int qx = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / step)), qz = Mathf.Max(1, Mathf.CeilToInt((z1 - z0) / step));
                Vector3[] vs = new Vector3[(qx + 1) * (qz + 1)];
                Color32[] cols = new Color32[vs.Length];
                Vector3[] ns = new Vector3[vs.Length];
                for (int j = 0; j <= qz; j++)
                {
                    for (int i = 0; i <= qx; i++)
                    {
                        float x = Mathf.Lerp(x0, x1, i / (float)qx), z = Mathf.Lerp(z0, z1, j / (float)qz);
                        int k = j * (qx + 1) + i;
                        vs[k] = new Vector3(x, cfg.waterLevel, z);
                        ns[k] = Vector3.up;
                        float wd = gen.WallDistance(x, z);
                        float foam = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((wd - 0.04f) / 0.55f));
                        float depth = Mathf.Clamp01((cfg.waterLevel - gen.FloorHeightLocal(x, z)) / maxDepth);
                        cols[k] = new Color32((byte)(foam * 255f), (byte)(depth * 255f), 0, 255);
                    }
                }
                int[] tris = new int[qx * qz * 6];
                int t = 0;
                for (int j = 0; j < qz; j++)
                {
                    for (int i = 0; i < qx; i++)
                    {
                        int a = j * (qx + 1) + i, b = a + 1, c = a + (qx + 1), d = c + 1;
                        tris[t++] = a; tris[t++] = c; tris[t++] = d;
                        tris[t++] = a; tris[t++] = d; tris[t++] = b;
                    }
                }
                Mesh m = Own(new Mesh());
                m.name = "WaterChunk_" + cx + "_" + cz;
                m.vertices = vs; m.normals = ns; m.colors32 = cols; m.triangles = tris;
                m.bounds = new Bounds(new Vector3((x0 + x1) * 0.5f, cfg.waterLevel, (z0 + z1) * 0.5f), new Vector3(x1 - x0 + 1f, 0.6f, z1 - z0 + 1f));
                GameObject g = new GameObject("Water_" + cx + "_" + cz);
                g.layer = WaterLayer;
                g.transform.SetParent(parent, false);
                g.AddComponent<MeshFilter>().sharedMesh = m;
                MeshRenderer mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = material;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off;
                mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                chunkCount++;
            }
        }
    }

    private void GatherLights(BackroomsGenerator gen)
    {
        lights.Clear();
        Light[] all = gen.Root.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < all.Length; i++) if (all[i].type == LightType.Point) lights.Add(all[i]);
    }

    private void BuildFx(BackroomsGenerator gen, LevelConfig cfg)
    {
        // partículas de salpicadura
        Shader ps = Resources.Load<Shader>(ParticleShaderResource);
        if (ps != null)
        {
            particleMat = Own(new Material(ps));
            dropTex = Own(MakeDropTexture(32));
            particleMat.mainTexture = dropTex;
            GameObject g = new GameObject("Splashes");
            g.transform.SetParent(transform, false);
            splashPs = g.AddComponent<ParticleSystem>();
            splashPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = splashPs.main;
            main.loop = false; main.playOnAwake = false;
            main.startLifetime = 0.75f; main.startSpeed = 0f; main.startSize = 0.05f;
            main.gravityModifier = 1.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            ParticleSystem.EmissionModule em = splashPs.emission; em.enabled = false;
            ParticleSystem.ShapeModule sh = splashPs.shape; sh.enabled = false;
            ParticleSystem.ColorOverLifetimeModule col = splashPs.colorOverLifetime;
            col.enabled = true;
            Gradient gr = new Gradient();
            gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.85f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = gr;
            ParticleSystemRenderer pr = g.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = particleMat;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            pr.receiveShadows = false;
            splashPs.Play();
        }

        // sonido de chapoteo (fuentes 3D en pool)
        splashClip = Own(ProceduralAudio.CreateSplash());
        splashSources = new AudioSource[4];
        for (int i = 0; i < splashSources.Length; i++)
        {
            GameObject sg = new GameObject("SplashSrc" + i);
            sg.transform.SetParent(transform, false);
            AudioSource a = sg.AddComponent<AudioSource>();
            a.clip = splashClip; a.playOnAwake = false; a.spatialBlend = 1f; a.minDistance = 1.2f; a.maxDistance = 22f;
            a.rolloffMode = AudioRolloffMode.Linear;
            splashSources[i] = a;
        }

        BuildCaustics(cfg);

        // sonda de reflexión en tiempo real (reflejos de las luces del techo)
        GameObject pg = new GameObject("WaterReflectionProbe");
        pg.transform.SetParent(transform, false);
        float totalW = Mathf.Max(3, cfg.gridW) * cfg.cellSize, totalH = Mathf.Max(3, cfg.gridH) * cfg.cellSize;
        pg.transform.localPosition = new Vector3(totalW * 0.5f, cfg.waterLevel + 1.3f, totalH * 0.5f);
        probe = pg.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 64;
        probe.hdr = true;
        probe.boxProjection = false;
        probe.size = new Vector3(totalW + 4f, 30f, totalH + 4f);
        probe.intensity = 1f;
        probe.nearClipPlane = 0.1f;
        probe.farClipPlane = 40f;
        probe.cullingMask = ~((1 << WaterLayer) | (1 << 2) | (1 << 5));
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = RenderSettings.fogColor;
        probeTimer = 0.2f;
    }

    // ---------------- Cáusticas (Projector aditivo con flipbook de 64 fotogramas de Blender) ----------------
    public const string CausticShaderResource = "Water/BackroomsCaustics";
    public const int CausticFrames = 64;
    public Projector caustics;
    private Material causticMat;
    private Texture2D[] causticTex;

    private void BuildCaustics(LevelConfig cfg)
    {
        Shader cs = Resources.Load<Shader>(CausticShaderResource);
        if (cs == null || !cs.isSupported) return;
        causticTex = new Texture2D[CausticFrames];
        for (int i = 0; i < CausticFrames; i++) causticTex[i] = Resources.Load<Texture2D>("Water/Caustics/caustics_" + i.ToString("000"));
        if (causticTex[0] == null) return;
        causticMat = Own(new Material(cs));
        causticMat.SetFloat("_WaterY", surfaceY);
        Color lc = cfg.lightColor;
        causticMat.SetColor("_CausticCol", new Color(0.30f + lc.r * 0.15f, 0.42f + lc.g * 0.2f, 0.45f + lc.b * 0.2f, 1f) * 0.75f);
        float totalW = Mathf.Max(3, cfg.gridW) * cfg.cellSize, totalH = Mathf.Max(3, cfg.gridH) * cfg.cellSize;
        GameObject g = new GameObject("WaterCaustics");
        g.transform.SetParent(transform, false);
        g.transform.position = generator.Root.TransformPoint(new Vector3(totalW * 0.5f, cfg.waterLevel + 3.7f, totalH * 0.5f));   // v6.9: por encima del techo (cáusticas también en techo/paredes de la sala)
        g.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        caustics = g.AddComponent<Projector>();
        caustics.orthographic = true;
        caustics.orthographicSize = totalH * 0.5f + 1f;
        caustics.aspectRatio = (totalW + 2f) / (totalH + 2f);
        caustics.nearClipPlane = 0.05f;
        caustics.farClipPlane = 6.7f;   // desde el techo hasta el fondo más hondo
        caustics.material = causticMat;
        caustics.ignoreLayers = (1 << WaterLayer) | (1 << 8) | (1 << 5) | (1 << 2);
    }

    private void UpdateCaustics()
    {
        if (causticMat == null) return;
        float ft = Time.time * 20f;
        int f0 = Mathf.FloorToInt(ft) % CausticFrames, f1 = (f0 + 1) % CausticFrames;
        causticMat.SetTexture("_CausticTex", causticTex[f0]);
        causticMat.SetTexture("_CausticTex2", causticTex[f1]);
        causticMat.SetFloat("_Blend", ft - Mathf.Floor(ft));
        // Bajo: sin cáusticas (el Projector vuelve a dibujar el suelo y las paredes)
        if (caustics != null) caustics.enabled = GameSettings.Quality >= 1;
    }

    private static Texture2D MakeDropTexture(int n)
    {
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.name = "DropSprite";
        Color32[] px = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                px[y * n + x] = new Color32(235, 245, 255, (byte)(a * 255f));
            }
        t.SetPixels32(px);
        t.wrapMode = TextureWrapMode.Clamp;
        t.Apply(false, true);
        return t;
    }

    private void BuildDebris(BackroomsGenerator gen, LevelConfig cfg)
    {
        if (cfg.debrisCount <= 0) return;
        System.Random rng = new System.Random(gen.UsedSeed ^ 0x5eed);
        Color[] cols = { new Color(0.45f, 0.33f, 0.2f), new Color(0.55f, 0.55f, 0.5f), new Color(0.2f, 0.35f, 0.5f), new Color(0.6f, 0.2f, 0.15f) };
        int placed = 0, guard = 0;
        while (placed < cfg.debrisCount && guard++ < 400)
        {
            int cx = rng.Next(gen.GridWidth), cz = rng.Next(gen.GridHeight);
            if (cx == gen.StartCell.x && cz == gen.StartCell.y) continue;
            if (cx == gen.ExitCell.x && cz == gen.ExitCell.y) continue;
            Vector3 c = gen.CellCenterWorld(cx, cz);
            c += new Vector3((float)(rng.NextDouble() - 0.5) * cfg.cellSize * 0.5f, 0f, (float)(rng.NextDouble() - 0.5) * cfg.cellSize * 0.5f);
            int kind = rng.Next(3);
            Material mat = Own(ProceduralAssets.CreateMaterial(cols[rng.Next(cols.Length)], null, Vector2.one, 0.25f));
            GameObject go;
            float halfH;
            if (kind == 0)        // caja
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.localScale = new Vector3(0.55f, 0.4f, 0.55f); halfH = 0.2f;
            }
            else if (kind == 1)   // barril
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.transform.localScale = new Vector3(0.5f, 0.38f, 0.5f); halfH = 0.38f;
            }
            else                  // tablón
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.transform.localScale = new Vector3(1.3f, 0.06f, 0.28f); halfH = 0.03f;
            }
            go.name = "Debris" + placed;
            go.layer = 2; // Ignore Raycast: no estorba a la visión del monstruo
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(c.x, surfaceY + 0.05f, c.z);
            go.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = kind == 1 ? 14f : (kind == 0 ? 9f : 4f);
            rb.drag = 0.4f; rb.angularDrag = 0.6f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            FloatingDebris fd = go.AddComponent<FloatingDebris>();
            fd.water = this;
            fd.halfHeight = halfH;
            debris.Add(fd);
            placed++;
        }
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        for (int i = 0; i < owned.Count; i++) ProceduralAssets.SafeDestroy(owned[i]);
        owned.Clear();
    }

    // ------------------------------------------------------------------
    // Actualización
    // ------------------------------------------------------------------
    private void Update()
    {
        if (!Application.isPlaying) return;
        float t = Time.time;
        Shader.SetGlobalFloat("_WTime", t);
        if (material != null && !usingFallback) material.SetFloat("_WaveAmp", waveAmp);

        // ondas: se desvanecen solas en el shader; aquí se descartan las muy viejas
        int live = 0;
        for (int i = 0; i < MaxRipples; i++)
        {
            if (ripples[i].w > 0f && t - ripples[i].z > 3.3f) ripples[i].w = 0f;
            if (ripples[i].w > 0f) live++;
        }
        RippleCount = live;
        Shader.SetGlobalVectorArray("_WRip", ripples);

        UpdateSpecularLights();
        UpdateCaustics();
        if (oceanSurface != null)
        {
            Camera oc = Camera.main;
            if (oc != null)
            {
                Vector3 cp = oc.transform.position;
                oceanSurface.position = new Vector3(Mathf.Round(cp.x / 8f) * 8f, surfaceY, Mathf.Round(cp.z / 8f) * 8f);
            }
        }
        // profundidad de escena para el grosor del agua (refracción/absorción)
        Camera mc = Camera.main;
        if (mc != null && !usingFallback && (mc.depthTextureMode & DepthTextureMode.Depth) == 0) mc.depthTextureMode |= DepthTextureMode.Depth;
        Shader.SetGlobalFloat("_WDepthOn", mc != null && (mc.depthTextureMode & DepthTextureMode.Depth) != 0 ? 1f : 0f);

        // reflexión: se refresca cada ~2 s (suficiente para el parpadeo de las luces y barato: 64 px)
        // (sin dispositivo gráfico — -nographics — no hay nada que renderizar y el motor puede fallar al dibujar la sonda)
        if (probe != null && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            probeTimer -= Time.unscaledDeltaTime;
            if (probeTimer <= 0f)
            {
                probeTimer = 2f;
                try { probe.RenderProbe(); } catch { probe = null; }
            }
        }
    }

    private void UpdateSpecularLights()
    {
        Camera cam = Camera.main;
        Vector3 cp = cam != null ? cam.transform.position : Vector3.zero;
        if (flashSpot == null)
        {
            Flashlight f = Object.FindObjectOfType<Flashlight>();
            if (f != null) flashSpot = f.spot;
        }
        // las 4 luces más cercanas (cuadrado de la distancia) entre fluorescentes encendidos y la linterna
        float[] best = { 1e9f, 1e9f, 1e9f, 1e9f };
        int[] idx = { -1, -1, -1, -1 };
        for (int i = 0; i < lights.Count; i++)
        {
            Light l = lights[i];
            if (l == null || !l.enabled || !l.gameObject.activeInHierarchy || l.intensity <= 0.01f) continue;
            float d = (l.transform.position - cp).sqrMagnitude;
            for (int k = 0; k < 4; k++)
            {
                if (d < best[k])
                {
                    for (int q = 3; q > k; q--) { best[q] = best[q - 1]; idx[q] = idx[q - 1]; }
                    best[k] = d; idx[k] = i;
                    break;
                }
            }
        }
        int n = 0;
        if (flashSpot != null && flashSpot.enabled && flashSpot.gameObject.activeInHierarchy && flashSpot.intensity > 0.05f)
        {
            lightPos[n] = new Vector4(flashSpot.transform.position.x, flashSpot.transform.position.y, flashSpot.transform.position.z, flashSpot.range * 0.8f);
            Color fc = flashSpot.color * flashSpot.intensity * 0.5f;
            lightCol[n] = new Vector4(fc.r, fc.g, fc.b, 1f);
            n++;
        }
        for (int k = 0; k < 4 && n < 4; k++)
        {
            if (idx[k] < 0) continue;
            Light l = lights[idx[k]];
            lightPos[n] = new Vector4(l.transform.position.x, l.transform.position.y, l.transform.position.z, l.range);
            Color c = l.color * l.intensity;
            lightCol[n] = new Vector4(c.r, c.g, c.b, 1f);
            n++;
        }
        for (; n < 4; n++) { lightPos[n] = Vector4.zero; lightCol[n] = Vector4.zero; }
        Shader.SetGlobalVectorArray("_WLPos", lightPos);
        Shader.SetGlobalVectorArray("_WLCol", lightCol);
    }

    // ------------------------------------------------------------------
    // Consultas
    // ------------------------------------------------------------------

    // Misma fórmula que el shader (solo la componente vertical de las 3 olas Gerstner)
    private static readonly Vector2[] WaveDir = { new Vector2(0.80f, 0.60f), new Vector2(-0.50f, 0.866f), new Vector2(0.20f, -0.98f) };
    private static readonly float[] WaveLen = { 5.3f, 2.9f, 1.7f };
    private static readonly float[] WaveAmpl = { 0.012f, 0.008f, 0.0045f };

    public float WaveHeight(float x, float z)
    {
        float t = Time.time, y = 0f;
        for (int i = 0; i < 3; i++)
        {
            Vector2 d = WaveDir[i].normalized;
            float k = 2f * Mathf.PI / WaveLen[i];
            float w = Mathf.Sqrt(9.8f * k) * 0.55f;
            y += WaveAmpl[i] * waveAmp * Mathf.Sin(k * (d.x * x + d.y * z) - w * t);
        }
        return y;
    }

    public float SurfaceHeightAt(Vector3 world)
    {
        if (surfaceOverride != null)
        {
            float o = surfaceOverride(world);
            if (!float.IsNaN(o)) return o + WaveHeight(world.x, world.z) * 0.15f;
        }
        return surfaceY + WaveHeight(world.x, world.z);
    }

    // superficie en reposo en ese punto (mar o bolsa de aire)
    public float RestSurfaceAt(Vector3 world)
    {
        if (surfaceOverride != null)
        {
            float o = surfaceOverride(world);
            if (!float.IsNaN(o)) return o;
        }
        return surfaceY;
    }

    // Altura del suelo bajo un punto (raycast hacia abajo; ignora capas "Ignore Raycast" y agua)
    public float GroundY(Vector3 world)
    {
        RaycastHit hit;
        int mask = ~((1 << 2) | (1 << WaterLayer));
        if (Physics.Raycast(new Vector3(world.x, world.y + 0.7f, world.z), Vector3.down, out hit, ocean ? 120f : 6f, mask, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        if (ocean) return oceanFloorFallback;
        return generator != null && generator.HasLevel ? generator.Root.position.y + generator.FloorHeightLocal(
            generator.Root.InverseTransformPoint(world).x, generator.Root.InverseTransformPoint(world).z) : surfaceY - 5f;
    }

    // Profundidad del agua en el punto (metros; 0 si no hay agua)
    public float DepthAt(Vector3 feetWorld)
    {
        float sy = ocean ? RestSurfaceAt(feetWorld) : surfaceY;
        if (feetWorld.y > sy + 2.2f) return 0f;   // altillo, escaleras altas...
        float g = GroundY(feetWorld);
        return Mathf.Max(0f, sy - g);
    }

    public bool IsUnderwater(Vector3 cameraWorld)
    {
        if (ocean) return cameraWorld.y < SurfaceHeightAt(cameraWorld) - 0.02f;
        return cameraWorld.y < SurfaceHeightAt(cameraWorld) - 0.02f && DepthAt(new Vector3(cameraWorld.x, cameraWorld.y - 1.6f, cameraWorld.z) ) >= 0f
            && cameraWorld.y > surfaceY - 6f;
    }

    // Factor de velocidad al vadear: 1 en seco, hasta ~0,45 a 1,2 m de profundidad
    public static float WadeFactor(float depth)
    {
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((depth - 0.06f) / 1.1f));
        return Mathf.Lerp(1f, 0.45f, k);
    }

    // ------------------------------------------------------------------
    // Efectos
    // ------------------------------------------------------------------
    public void AddRipple(Vector3 world, float amp)
    {
        amp = Mathf.Clamp(amp, 0.15f, 1.6f);
        ripples[nextRipple] = new Vector4(world.x, world.z, Time.time, amp);
        nextRipple = (nextRipple + 1) % MaxRipples;
        // empuja los objetos flotantes cercanos
        for (int i = 0; i < debris.Count; i++)
        {
            FloatingDebris d = debris[i];
            if (d == null) continue;
            Vector3 dv = d.transform.position - world; dv.y = 0f;
            float dist = dv.magnitude;
            if (dist < 2.4f && dist > 0.01f) d.Nudge(dv / dist, amp * (1f - dist / 2.4f));
        }
    }

    // Salpicadura: onda + gotas + (opcional) sonido espacial
    public void Splash(Vector3 world, float strength, bool sound)
    {
        AddRipple(world, 0.35f + strength * 0.8f);
        if (splashPs != null)
        {
            int count = Mathf.RoundToInt(Mathf.Lerp(5f, 22f, Mathf.Clamp01(strength)));
            ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                float ang = Random.value * Mathf.PI * 2f;
                float sp = Random.Range(0.4f, 1.1f) * (0.6f + strength);
                ep.position = new Vector3(world.x, RestSurfaceAt(world) + 0.02f, world.z) + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * Random.Range(0.02f, 0.18f);
                ep.velocity = new Vector3(Mathf.Cos(ang) * sp * 0.5f, Random.Range(1.2f, 2.4f) * (0.5f + strength * 0.6f), Mathf.Sin(ang) * sp * 0.5f);
                ep.startSize = Random.Range(0.025f, 0.055f);
                ep.startLifetime = Random.Range(0.45f, 0.8f);
                ep.startColor = new Color(0.85f, 0.95f, 1f, 0.9f);
                splashPs.Emit(ep, 1);
            }
        }
        if (sound && splashSources != null)
        {
            AudioSource a = splashSources[splashSrcIndex];
            splashSrcIndex = (splashSrcIndex + 1) % splashSources.Length;
            a.transform.position = new Vector3(world.x, RestSurfaceAt(world), world.z);
            a.pitch = Random.Range(0.85f, 1.25f);
            a.volume = Mathf.Clamp01(0.25f + strength * 0.6f) * AudioMix.Sfx;
            a.Play();
        }
    }
}
