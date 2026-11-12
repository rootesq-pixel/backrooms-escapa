using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.12.0 Nivel 188 — El Patio de las Ventanas (canon: «The Windows», foto de @zerstoerer).
// Un patio a cielo abierto rodeado por cinco plantas de ventanitas cuadradas con cortinas. Dentro del edificio, pasillos que
// dan la vuelta al patio en cada planta, habitaciones de hotel detrás de cada ventana y dos escaleras (esquinas SO y NE).
//   · Ventanas «abiertas»: sin cortina, enseñan otro sitio (otros niveles). Su puerta no se abre nunca.
//   · La Ventana: cuartos a oscuras con una silueta que te hace señas. Su puerta está entornada. No entres.
//   · Habitaciones abiertas: si descorres su cortina (mantener clic) desapareces y apareces en otro punto del edificio.
//   · Salida: la única ventana con sol. Su puerta está cerrada con llave (ExitLock); dentro, asomarte a la ventana te saca.
// Geometría de cajas (MeshBuilder: UV en metros, caras hacia fuera) agrupada por planta; colisión con BoxCollider.
// Todo sale de la semilla (W188Plan): mismo edificio, misma huella y mismas entidades en todos los equipos (multijugador).
public class W188World : MonoBehaviour
{
    public static W188World Active { get; private set; }
    public float RoomK { get; private set; }
    public BackroomsGenerator gen; public LevelConfig cfg; public W188Plan plan; public Transform levelRoot;
    System.Action<Object> track;

    // materiales (submallas)
    public const int FACADE = 0, PAVING = 1, CARPET = 2, RCARPET = 3, WALL = 4, CEIL = 5, FRAME = 6, CONC = 7, DOOR = 8, SKIRT = 9,
        LAMP = 10, METAL = 11, LINEN = 12, WOOD = 13, DARK = 14, CURT_LIT = 15, CURT_DARK = 16, CURT_COLD = 17, GLOW = 18, CORNICE = 19, CURT_GREEN = 20, CURT_ROSE = 21, BLIND = 22, RUG = 23, PAINT = 24, NMAT = 25;
    public Material[] mats;
    public Material GlassMat { get; private set; }
    public Material FigureMat { get; private set; }
    public Material ExitViewMat { get; private set; }
    readonly Dictionary<int, Material> viewMats = new Dictionary<int, Material>();

    // grupos de malla: 0 = siempre visibles (fachadas, patio, cuartos que se ven desde el patio), 1..5 = interior de cada planta
    MeshBuilder[] mb; MeshBuilder glassMb;
    public readonly List<GameObject> FloorGroups = new List<GameObject>();   // índice = planta (interiores)
    public GameObject ExtGroup { get; private set; }
    Transform colRoot, propRoot;
    public int ColliderCount { get; private set; }

    // resultado para el generador
    public Vector3 StartLocal; public float StartYaw = 180f;
    public Transform ExitTransform { get; private set; }
    public Vector2Int ExitCell { get; private set; }
    public readonly List<Vector3> BatterySpots = new List<Vector3>(), ExtraSpots = new List<Vector3>(), KeySpots = new List<Vector3>();
    public int LampCount { get; private set; }

    // dinámicos
    public class Lamp { public Light l; public int f0, f1; public bool always; public float baseI; }
    public readonly List<Lamp> Lamps = new List<Lamp>();
    public readonly List<W188Door> Doors = new List<W188Door>();
    public class Curtain { public Transform left, right; public Vector3 center; public Vector3 inward; public int f, s, i; public float open; public bool busy; }
    public readonly List<Curtain> Curtains = new List<Curtain>();
    public class Figure { public Transform root, arm, elbow, wrist, head, chest; public readonly List<Transform> fingers = new List<Transform>(); public Vector3 roomCenter, doorWorld; public int f, s, i; public float phase; }
    public readonly List<Figure> Figures = new List<Figure>();
    public class LockedDoor { public Vector3 pos; public Vector3 outward; public int f; public bool view; }
    public readonly List<LockedDoor> Locked = new List<LockedDoor>();
    public Transform ExitLeaf { get; private set; }
    public float ExitAngle { get; private set; }
    BoxCollider exitBlocker; float exitOpen;
    ReflectionProbe probe; int probeFrames = 3;
    AudioSource rattleSrc; AudioClip rattleClip; float rattleCd, toastCd; int rattleToasts;
    public int Rattles { get; private set; }
    public float CourtK { get; private set; }     // 1 = a cielo abierto en el patio
    public int PlayerFloor { get; private set; }
    public int ActiveLamps { get; private set; }

    public static W188World Build(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot, W188Plan plan, System.Action<Object> track)
    {
        GameObject g = new GameObject("Courtyard188");
        g.transform.SetParent(levelRoot, false);
        W188World w = g.AddComponent<W188World>();
        w.gen = gen; w.cfg = cfg; w.plan = plan; w.levelRoot = levelRoot; w.track = track;
        Active = w;
        w.BuildAll();
        return w;
    }

    void OnDestroy()
    {
        if (Active == this) Active = null;
        if (GameManager.ExitTest == (System.Func<Vector3, bool>)AtExit) GameManager.ExitTest = null;
        if (rattleClip != null) ProceduralAssets.SafeDestroy(rattleClip);
    }

    // ---------------- materiales ----------------
    Material Pbr(string n, Color tint, float occl) { Material m = OceanGeo.PbrMat("W188/" + n, tint, occl, track); m.name = "W188_" + n; return m; }
    Material Plain(string name, Color c, float smooth)
    {
        Material m = ProceduralAssets.CreateMaterial(c, null, Vector2.one, smooth); m.name = "W188_" + name; track(m); return m;
    }
    Material curtainIn;
    Material GrimeMat, PuddleMat; MeshBuilder grimeMb;
    // manchas de suciedad: chorretones verticales bajo los alféizares (textura generada, alfa; v arriba = 1)
    Material MakeGrime()
    {
        const int W = 128, H = 256; Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, true); t.wrapMode = TextureWrapMode.Clamp; track(t);
        System.Random r = new System.Random(188); Color32[] px = new Color32[W * H];
        float[] len = new float[W], str = new float[W];
        float run = 0.5f, sr = 0.5f;
        for (int x = 0; x < W; x++) { if (r.NextDouble() < 0.18) { run = 0.25f + (float)r.NextDouble() * 0.75f; sr = 0.3f + (float)r.NextDouble() * 0.7f; } len[x] = run; str[x] = sr; }
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float v = y / (float)(H - 1), fromTop = 1f - v;
                float edge = Mathf.Clamp01(Mathf.Min(x, W - 1 - x) / 14f);
                float a = Mathf.Clamp01(1f - fromTop / len[x]); a = a * a * str[x] * edge;
                a *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.21f, y * 0.05f);
                a = Mathf.Max(a, 0.55f * Mathf.Clamp01((v - 0.9f) / 0.1f) * edge);   // borde de arriba (bajo el alféizar), más oscuro
                px[y * W + x] = new Color32(48, 44, 38, (byte)(a * 150f));
            }
        t.SetPixels32(px); t.Apply(true);
        Material m = ProceduralAssets.CreateMaterial(Color.white, t, Vector2.one, 0.05f); m.name = "W188_Grime"; track(m);
        if (m.HasProperty("_Mode"))
        {
            m.SetFloat("_Mode", 2f); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)RenderQueue.Transparent - 10;
        }
        return m;
    }
    static int H3(int f, int s, int i, int salt) { unchecked { int h = f * 73856093 ^ s * 19349663 ^ i * 83492791 ^ salt * 2654435; h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return h & 0x7fffffff; } }
    Material LitCurtain(string name, Color tint, Color emit)
    {
        Material m = Pbr("W188Curtain", tint, 0.6f); m.name = "W188_" + name;
        ProceduralAssets.SetEmission(m, emit);
        if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", m.mainTexture);
        return m;
    }
    public Material ViewMat(int level)
    {
        Material m;
        if (viewMats.TryGetValue(level, out m)) return m;
        Texture2D t = Resources.Load<Texture2D>("UI/LevelThumbs/level_" + level);
        m = EmissiveImage("W188_View" + level, t, 0.85f);
        viewMats[level] = m; return m;
    }
    Material EmissiveImage(string name, Texture t, float k)
    {
        Material m = ProceduralAssets.CreateMaterial(Color.black, null, Vector2.one, 0f); m.name = name; track(m);
        if (m.HasProperty("_SpecularHighlights")) { m.SetFloat("_SpecularHighlights", 0f); m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF"); }
        if (m.HasProperty("_GlossyReflections")) { m.SetFloat("_GlossyReflections", 0f); m.EnableKeyword("_GLOSSYREFLECTIONS_OFF"); }
        ProceduralAssets.SetEmission(m, new Color(k, k, k));
        if (t != null && m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", t);
        return m;
    }

    void MakeMaterials()
    {
        mats = new Material[NMAT];
        mats[FACADE] = Pbr("W188Facade", new Color(0.93f, 0.90f, 0.84f), 1f);
        mats[PAVING] = Pbr("W188Paving", new Color(0.86f, 0.85f, 0.83f), 1f);
        mats[CARPET] = Pbr("W188Carpet", Color.white, 1f);
        mats[RCARPET] = Pbr("W188RoomCarpet", new Color(0.92f, 0.88f, 0.84f), 1f);
        mats[WALL] = Pbr("W188Wall", new Color(0.96f, 0.93f, 0.86f), 0.8f);
        mats[CEIL] = Pbr("W188Facade", new Color(0.97f, 0.96f, 0.93f), 0.6f); mats[CEIL].name = "W188_Ceiling";
        mats[FRAME] = Pbr("W188Door", new Color(0.97f, 0.97f, 0.95f), 0.6f); mats[FRAME].name = "W188_Frame";
        mats[CONC] = Pbr("W188Concrete", new Color(0.80f, 0.79f, 0.77f), 1f);
        mats[DOOR] = Pbr("W188Door", new Color(0.93f, 0.91f, 0.86f), 0.8f);
        mats[SKIRT] = Pbr("W188Door", new Color(0.30f, 0.24f, 0.19f), 0.8f); mats[SKIRT].name = "W188_Skirting";
        mats[LAMP] = Plain("Lamp", new Color(0.95f, 0.93f, 0.88f), 0.3f); ProceduralAssets.SetEmission(mats[LAMP], new Color(1f, 0.86f, 0.62f) * 1.6f);
        mats[METAL] = Plain("Brass", new Color(0.78f, 0.62f, 0.36f), 0.72f); mats[METAL].SetFloat("_Metallic", 0.9f);
        mats[LINEN] = Pbr("W188Curtain", new Color(0.92f, 0.91f, 0.88f), 0.6f); mats[LINEN].name = "W188_Linen";
        mats[WOOD] = Pbr("W188Door", new Color(0.42f, 0.30f, 0.21f), 0.8f); mats[WOOD].name = "W188_Wood";
        mats[DARK] = Pbr("W188Wall", new Color(0.30f, 0.27f, 0.25f), 0.2f); mats[DARK].name = "W188_Dark";   // papel pintado sucio y apagado: la luz roja del cuarto de la Ventana se lee
        mats[CURT_LIT] = LitCurtain("CurtainLit", new Color(0.93f, 0.86f, 0.70f), new Color(0.95f, 0.70f, 0.38f) * 0.55f);
        mats[CURT_DARK] = Pbr("W188Curtain", new Color(0.78f, 0.74f, 0.64f), 0.7f); mats[CURT_DARK].name = "W188_CurtainDark";
        curtainIn = LitCurtain("CurtainDaylight", new Color(0.92f, 0.91f, 0.88f), new Color(0.66f, 0.67f, 0.66f) * 0.34f);   // luz de día del patio a través de la tela
        mats[CURT_COLD] = LitCurtain("CurtainCold", new Color(0.86f, 0.88f, 0.86f), new Color(0.55f, 0.62f, 0.66f) * 0.42f);
        mats[GLOW] = Plain("Backlight", new Color(0.05f, 0.02f, 0.02f), 0.1f); ProceduralAssets.SetEmission(mats[GLOW], new Color(0.30f, 0.07f, 0.04f));
        mats[CORNICE] = Pbr("W188Facade", new Color(0.82f, 0.79f, 0.74f), 1f); mats[CORNICE].name = "W188_Cornice";
        // v2: más variedad en las ventanas y en los cuartos
        mats[CURT_GREEN] = Pbr("W188Curtain", new Color(0.52f, 0.56f, 0.44f), 0.7f); mats[CURT_GREEN].name = "W188_CurtainOlive";
        mats[CURT_ROSE] = LitCurtain("CurtainRose", new Color(0.86f, 0.70f, 0.66f), new Color(0.90f, 0.55f, 0.45f) * 0.42f);
        mats[BLIND] = Pbr("W188Door", new Color(0.86f, 0.84f, 0.78f), 0.7f); mats[BLIND].name = "W188_Blind";
        mats[RUG] = Pbr("W188Carpet", new Color(0.72f, 0.42f, 0.34f), 1f); mats[RUG].name = "W188_Rug";
        mats[PAINT] = Pbr("W188Door", new Color(0.88f, 0.87f, 0.83f), 0.8f); mats[PAINT].name = "W188_PaintedMetal";
        GrimeMat = MakeGrime();
        PuddleMat = Plain("Puddle", new Color(0.40f, 0.41f, 0.42f), 0.9f);   // agua quieta: refleja el cielo y las fachadas

        Material g = ProceduralAssets.CreateMaterial(new Color(0.30f, 0.34f, 0.36f, 0.16f), null, Vector2.one, 0.96f); g.name = "W188_Glass"; track(g);
        if (g.HasProperty("_Mode"))
        {
            g.SetFloat("_Mode", 3f); g.SetOverrideTag("RenderType", "Transparent");
            g.SetInt("_SrcBlend", (int)BlendMode.One); g.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); g.SetInt("_ZWrite", 0);
            g.DisableKeyword("_ALPHATEST_ON"); g.DisableKeyword("_ALPHABLEND_ON"); g.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            g.renderQueue = (int)RenderQueue.Transparent;
        }
        GlassMat = g;
        FigureMat = Plain("Silhouette", new Color(0.035f, 0.032f, 0.031f), 0.42f);   // negro mate con un leve brillo: la contraluz le dibuja el borde
        ExitViewMat = EmissiveImage("W188_ExitView", Resources.Load<Texture2D>("W188/W188ExitView"), 1.25f);
    }

    // ---------------- utilidades de geometría (marco de cada lado) ----------------
    static Vector3 P(int s, float u, float y, float d) { return W188Plan.ToLevel(s, u, y, d); }
    static Vector3 Ud(int s) { return W188Plan.SideMatrix(s).MultiplyVector(Vector3.right); }
    static Vector3 Dd(int s) { return W188Plan.SideMatrix(s).MultiplyVector(Vector3.back); }   // hacia fuera del patio
    void Box(int grp, int mat, int s, float u0, float u1, float y0, float y1, float d0, float d1, float tile)
    {
        OceanGeo.Box(mb[grp], mat, P(s, u0, y0, d0), P(s, u1, y1, d1), tile);
    }
    void BoxL(int grp, int mat, Vector3 a, Vector3 b, float tile) { OceanGeo.Box(mb[grp], mat, a, b, tile); }
    void Col(int s, float u0, float u1, float y0, float y1, float d0, float d1) { ColL(P(s, u0, y0, d0), P(s, u1, y1, d1)); }
    BoxCollider ColL(Vector3 a, Vector3 b)
    {
        Vector3 mn = Vector3.Min(a, b), mx = Vector3.Max(a, b);
        GameObject c = new GameObject("Col"); c.transform.SetParent(colRoot, false);
        c.transform.localPosition = (mn + mx) * 0.5f;
        BoxCollider bc = c.AddComponent<BoxCollider>(); bc.size = mx - mn;
        ColliderCount++;
        return bc;
    }
    void BoxCol(int grp, int mat, int s, float u0, float u1, float y0, float y1, float d0, float d1, float tile)
    {
        Box(grp, mat, s, u0, u1, y0, y1, d0, d1, tile); Col(s, u0, u1, y0, y1, d0, d1);
    }
    // quad vertical en el plano d (normal hacia -d si faceCourt, si no hacia +d) con UV de 0..1 (o recorte)
    static void QuadD(MeshBuilder m, int sub, int s, float u0, float u1, float y0, float y1, float d, bool faceCourt, Rect uv)
    {
        Vector3 a = P(s, u0, y0, d), b = P(s, u1, y0, d), c = P(s, u1, y1, d), e = P(s, u0, y1, d);
        Vector3 n = faceCourt ? -Dd(s) : Dd(s);
        Vector3 ua = faceCourt ? Ud(s) : -Ud(s);
        Vector2 t0 = new Vector2(uv.xMin, uv.yMin), t1 = new Vector2(uv.xMax, uv.yMin), t2 = new Vector2(uv.xMax, uv.yMax), t3 = new Vector2(uv.xMin, uv.yMax);
        // desde el patio (mirando hacia +d) la u crece hacia la izquierda: la imagen va de u1 (izquierda) a u0
        if (faceCourt) m.AddQuadUV(sub, b, a, e, c, n, ua, Vector3.up, t0, t1, t2, t3);
        else m.AddQuadUV(sub, a, b, c, e, n, ua, Vector3.up, t0, t1, t2, t3);
    }
    // cortina con pliegues: tira ondulada en el plano d (en el marco del lado), por una o las dos caras
    static void Pleats(MeshBuilder m, int subFront, int subBack, int s, float u0, float u1, float y0, float y1, float d, float amp, int folds)
    {
        int n = folds * 8;
        for (int k = 0; k < n; k++)
        {
            float ua = Mathf.Lerp(u0, u1, k / (float)n), ub = Mathf.Lerp(u0, u1, (k + 1) / (float)n);
            float da = d + amp * Mathf.Sin(k * Mathf.PI * 0.25f), db = d + amp * Mathf.Sin((k + 1) * Mathf.PI * 0.25f);
            Vector3 a0 = P(s, ua, y0, da), b0 = P(s, ub, y0, db), b1 = P(s, ub, y1, db), a1 = P(s, ua, y1, da);
            Vector3 along = (b0 - a0).normalized;
            Vector3 nc = Vector3.Cross(Vector3.up, along).normalized;            // una de las dos normales horizontales
            if (Vector3.Dot(nc, -Dd(s)) < 0f) nc = -nc;                             // la de la cara que mira al patio
            if (subFront >= 0) m.AddQuad(subFront, a0, b0, b1, a1, nc, along, Vector3.up, 0.9f, 0.9f, 0f, 0f);
            if (subBack >= 0) m.AddQuad(subBack, a0, b0, b1, a1, -nc, along, Vector3.up, 0.9f, 0.9f, 0f, 0f);
        }
    }

    // ---------------- construcción ----------------
    void BuildAll()
    {
        MakeMaterials();
        mb = new MeshBuilder[1 + W188Plan.Floors];
        for (int i = 0; i < mb.Length; i++) mb[i] = new MeshBuilder(NMAT);
        glassMb = new MeshBuilder(1); grimeMb = new MeshBuilder(1);
        colRoot = new GameObject("Colliders").transform; colRoot.SetParent(transform, false);
        propRoot = new GameObject("Props").transform; propRoot.SetParent(transform, false);
        rattleClip = ProceduralAudio.CreateLockRattle();
        GameObject ra = new GameObject("RattleAudio"); ra.transform.SetParent(transform, false);
        rattleSrc = ra.AddComponent<AudioSource>(); rattleSrc.playOnAwake = false; rattleSrc.spatialBlend = 1f; rattleSrc.minDistance = 1.2f; rattleSrc.maxDistance = 14f; rattleSrc.rolloffMode = AudioRolloffMode.Linear; rattleSrc.dopplerLevel = 0f;

        BuildCourtyard();
        for (int s = 0; s < 4; s++)
            for (int f = 0; f < W188Plan.Floors; f++) BuildSideFloor(s, f);
        for (int s = 0; s < 4; s++) BuildCornerBlock(s);
        BuildRoof();

        // mallas
        ExtGroup = OceanGeo.Emit(mb[0], "Facades", transform, mats, false, false, track);
        for (int f = 0; f < W188Plan.Floors; f++) FloorGroups.Add(OceanGeo.Emit(mb[1 + f], "Floor" + f, transform, mats, false, false, track));
        foreach (var kv in floorParents) if (kv.Key >= 1 && kv.Key <= FloorGroups.Count) kv.Value.SetParent(FloorGroups[kv.Key - 1].transform, true);   // cuadros: se ocultan con su planta
        GameObject gl = OceanGeo.Emit(glassMb, "Glazing", transform, new[] { GlassMat }, false, false, track);
        gl.GetComponent<MeshRenderer>().receiveShadows = false;
        GameObject gr = OceanGeo.Emit(grimeMb, "FacadeGrime", transform, new[] { GrimeMat }, false, false, track);
        gr.GetComponent<MeshRenderer>().receiveShadows = false;

        PickSpots();
        // sonda de reflejos del patio (cristales y losas mojadas reflejan el cielo y las fachadas)
        // (sin gráficos —pruebas con -nographics— no se crea: renderizar la sonda con el dispositivo nulo cuelga Unity)
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) probe = new GameObject("CourtProbe").AddComponent<ReflectionProbe>();
        if (probe != null) {
        probe.transform.SetParent(transform, false);
        probe.transform.localPosition = new Vector3(W188Plan.Width * 0.5f, 3f, W188Plan.Depth * 0.5f);
        probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 128; probe.size = new Vector3(W188Plan.NX * W188Plan.M + 0.6f, 40f, W188Plan.NZ * W188Plan.M + 0.6f);
        probe.boxProjection = false; probe.importance = 1; probe.intensity = 1f; probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        probe.cullingMask = ~0; probe.farClipPlane = 120f; }

        GameManager.ExitTest = AtExit;
    }

    // ---------------- patio ----------------
    void BuildCourtyard()
    {
        float x0 = W188Plan.R, x1 = W188Plan.R + W188Plan.NX * W188Plan.M, z0 = W188Plan.R, z1 = W188Plan.R + W188Plan.NZ * W188Plan.M;
        BuildPaving(x0, x1, z0, z1);
        // suelo general de la planta baja (colisión de todo el edificio)
        ColL(new Vector3(0f, -0.3f, 0f), new Vector3(W188Plan.Width, 0f, W188Plan.Depth));
        float cx = (x0 + x1) * 0.5f;
        // rejillas de desagüe y bancos de listones a lo largo de los lados largos, dos jardineras vacías en el eje
        foreach (float z in new[] { 17.6f, 36.8f })
        {
            float gy = PavingH(cx, z); BoxL(0, METAL, new Vector3(cx - 0.3f, gy - 0.01f, z - 0.3f), new Vector3(cx + 0.3f, gy + 0.003f, z + 0.3f), 1f);
            float[] bx = { x0 + 1.1f, x1 - 1.1f };
            foreach (float bxx in bx)
            {
                for (int k = 0; k < 4; k++) BoxL(0, WOOD, new Vector3(bxx - 0.23f + k * 0.12f, 0.42f, z - 0.9f), new Vector3(bxx - 0.14f + k * 0.12f, 0.46f, z + 0.9f), 1f);
                foreach (float lz in new[] { z - 0.75f, z + 0.75f }) BoxL(0, CONC, new Vector3(bxx - 0.25f, 0f, lz - 0.07f), new Vector3(bxx + 0.25f, 0.42f, lz + 0.07f), 1f);
                ColL(new Vector3(bxx - 0.25f, 0f, z - 0.9f), new Vector3(bxx + 0.25f, 0.46f, z + 0.9f));
            }
        }
        foreach (float z in new[] { 24.0f, 30.4f })
        {
            BoxL(0, CONC, new Vector3(cx - 0.8f, 0f, z - 0.8f), new Vector3(cx + 0.8f, 0.55f, z + 0.8f), 1.6f);
            BoxL(0, DARK, new Vector3(cx - 0.68f, 0.55f, z - 0.68f), new Vector3(cx + 0.68f, 0.5501f, z + 0.68f), 1f);
            ColL(new Vector3(cx - 0.8f, 0f, z - 0.8f), new Vector3(cx + 0.8f, 0.6f, z + 0.8f));
        }
    }

    // v2: losas del patio con hundimientos suaves (más hacia los desagües), charcos en lo hondo y bordillo junto a las fachadas
    static readonly Vector3[] Dips = { new Vector3(0f, 17.6f, 2.2f), new Vector3(0f, 36.8f, 2.2f), new Vector3(-6.5f, 11.0f, 1.4f), new Vector3(7.0f, 27.0f, 1.6f), new Vector3(-5.0f, 44.0f, 1.2f), new Vector3(4.5f, 9.0f, 1.0f) };
    public static float PavingH(float x, float z)
    {
        float x0 = W188Plan.R, x1 = W188Plan.R + W188Plan.NX * W188Plan.M, z0 = W188Plan.R, z1 = W188Plan.R + W188Plan.NZ * W188Plan.M, cx = (x0 + x1) * 0.5f;
        float h = 0.010f * (Mathf.PerlinNoise(x * 0.31f + 3.1f, z * 0.31f + 7.7f) - 0.5f) + 0.006f * (Mathf.PerlinNoise(x * 1.3f, z * 1.3f) - 0.5f);
        // cada losa (1,6 m) un pelín girada: escalones de milímetros entre losas
        int ix = Mathf.FloorToInt((x - x0) / 1.6f), iz = Mathf.FloorToInt((z - z0) / 1.6f);
        float rs = Mathf.Sin(ix * 12.9898f + iz * 78.233f) * 43758.5453f; rs -= Mathf.Floor(rs);
        h += (rs - 0.5f) * 0.007f;
        foreach (Vector3 dp in Dips) { float dx = x - (cx + dp.x), dz = z - dp.y; h -= 0.032f * Mathf.Exp(-(dx * dx + dz * dz) / (dp.z * dp.z)); }
        float edge = Mathf.Min(Mathf.Min(x - x0, x1 - x), Mathf.Min(z - z0, z1 - z));
        return h * Mathf.Clamp01((edge - 0.25f) / 0.6f);
    }
    void BuildPaving(float x0, float x1, float z0, float z1)
    {
        float st = 0.2f; int nx = Mathf.RoundToInt((x1 - x0) / st), nz = Mathf.RoundToInt((z1 - z0) / st);
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var tr = new List<int>();
        for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
            {
                float x = x0 + i * st, z = z0 + j * st;
                v.Add(new Vector3(x, PavingH(x, z), z)); uv.Add(new Vector2(x / 3.2f, z / 3.2f));
            }
        for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
            {
                int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
                tr.Add(a); tr.Add(c); tr.Add(b); tr.Add(b); tr.Add(c); tr.Add(d);
            }
        Mesh m = new Mesh { name = "W188Paving" }; m.indexFormat = IndexFormat.UInt32;
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(tr, 0); m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds(); track(m);
        GameObject g = new GameObject("CourtPaving"); g.transform.SetParent(transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = m; MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mats[PAVING]; mr.shadowCastingMode = ShadowCastingMode.Off;
        // base oscura por debajo (por si se ve algún borde)
        BoxL(0, CONC, new Vector3(x0, -0.3f, z0), new Vector3(x1, -0.04f, z1), 3.2f);
        // charcos: un disco plano que corta el hundimiento (solo se ve donde el suelo queda por debajo)
        float cx = (x0 + x1) * 0.5f;
        MeshBuilder pm = new MeshBuilder(1);
        foreach (Vector3 dp in Dips)
        {
            Vector3 c = new Vector3(cx + dp.x, PavingH(cx + dp.x, dp.y) + 0.016f, dp.y);
            float rad = dp.z * 1.1f;
            int seg = 28;
            for (int q = 0; q < seg; q++)
            {
                float a0 = q * Mathf.PI * 2f / seg, a1 = (q + 1) * Mathf.PI * 2f / seg;
                Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * rad, p1 = c + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * rad;
                pm.AddQuad(0, c, p1, p0, p0, Vector3.up, Vector3.right, Vector3.forward, 2f, 2f, 0f, 0f);
            }
        }
        GameObject pg = OceanGeo.Emit(pm, "Puddles", transform, new[] { PuddleMat }, false, false, track);
        pg.GetComponent<MeshRenderer>().receiveShadows = true;
        // bordillo de hormigón al pie de las fachadas
        BoxL(0, CONC, new Vector3(x0, -0.02f, z0), new Vector3(x1, 0.025f, z0 + 0.22f), 1.6f);
        BoxL(0, CONC, new Vector3(x0, -0.02f, z1 - 0.22f), new Vector3(x1, 0.025f, z1), 1.6f);
        BoxL(0, CONC, new Vector3(x0, -0.02f, z0 + 0.22f), new Vector3(x0 + 0.22f, 0.025f, z1 - 0.22f), 1.6f);
        BoxL(0, CONC, new Vector3(x1 - 0.22f, -0.02f, z0 + 0.22f), new Vector3(x1, 0.025f, z1 - 0.22f), 1.6f);
        // hierbajos en algunas juntas
        System.Random r = new System.Random(plan.seed * 13 + 5);
        for (int q = 0; q < 70; q++)
        {
            float x = x0 + 0.6f + (float)r.NextDouble() * (x1 - x0 - 1.2f), z = z0 + 0.6f + (float)r.NextDouble() * (z1 - z0 - 1.2f);
            x = x0 + Mathf.Round((x - x0) / 1.6f) * 1.6f; float hh = 0.03f + (float)r.NextDouble() * 0.06f, y = PavingH(x, z);
            BoxL(0, CURT_GREEN, new Vector3(x - 0.012f, y - 0.01f, z - 0.05f - (float)r.NextDouble() * 0.1f), new Vector3(x + 0.012f, y + hh, z + 0.05f), 1f);
        }
    }

    // ---------------- una planta de un lado: fachada, ventanas, habitaciones, pasillo ----------------
    void BuildSideFloor(int s, int f)
    {
        int n = W188Plan.Len(s), G = 1 + f;
        float y0 = f * W188Plan.FH, yc = y0 + W188Plan.FH - 0.25f, yTop = y0 + W188Plan.FH;
        float L = n * W188Plan.M, R = W188Plan.R, CI = W188Plan.CorrIn, CO = W188Plan.CorrOut;
        float ys = y0 + W188Plan.Sill, yt = ys + W188Plan.WinH;
        for (int i = 0; i < n; i++)
        {
            W188Plan.Kind k = plan.K(f, s, i);
            float u0 = i * W188Plan.M, u1 = u0 + W188Plan.M, uc = W188Plan.UC(i);
            float wl = uc - W188Plan.WinW * 0.5f, wr = uc + W188Plan.WinW * 0.5f;
            bool built = k == W188Plan.Kind.Room || k == W188Plan.Kind.Exit || k == W188Plan.Kind.Entity || k == W188Plan.Kind.Passage || k == W188Plan.Kind.Lift;
            int rg = k == W188Plan.Kind.Room ? G : 0;   // cuartos que se ven desde el patio: siempre visibles
            // --- fachada ---
            if (k == W188Plan.Kind.Passage || k == W188Plan.Kind.Lift)
            {
                float hw = k == W188Plan.Kind.Lift ? 1.45f : 0.78f, hh = k == W188Plan.Kind.Lift ? 2.6f : 2.45f;
                BoxCol(0, FACADE, s, u0, uc - hw, y0, yTop, 0f, W188Plan.FacadeT, 2f);
                BoxCol(0, FACADE, s, uc + hw, u1, y0, yTop, 0f, W188Plan.FacadeT, 2f);
                BoxCol(0, FACADE, s, uc - hw, uc + hw, y0 + hh, yTop, 0f, W188Plan.FacadeT, 2f);
                // marco
                Box(0, FRAME, s, uc - hw - 0.08f, uc - hw, y0, y0 + hh + 0.08f, -0.03f, W188Plan.FacadeT + 0.02f, 1f);
                Box(0, FRAME, s, uc + hw, uc + hw + 0.08f, y0, y0 + hh + 0.08f, -0.03f, W188Plan.FacadeT + 0.02f, 1f);
                Box(0, FRAME, s, uc - hw - 0.08f, uc + hw + 0.08f, y0 + hh, y0 + hh + 0.08f, -0.03f, W188Plan.FacadeT + 0.02f, 1f);
                if (k == W188Plan.Kind.Passage)
                {   // puerta acristalada de dos hojas, abiertas hacia el vestíbulo (estáticas, sin colisión)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        float hu = uc + side * hw;                      // bisagra
                        float leafW = hw - 0.02f; float ang = 78f * Mathf.Deg2Rad;
                        // la hoja gira hacia dentro (d+): punto final
                        float eu = hu - side * leafW * Mathf.Cos(ang), ed = 0.15f + leafW * Mathf.Sin(ang);
                        Vector3 a = P(s, hu, y0 + 0.02f, 0.15f), b = P(s, eu, y0 + 0.02f, ed);
                        Vector3 dir = (b - a); float len = dir.magnitude; dir /= len;
                        Vector3 nrm = Vector3.Cross(Vector3.up, dir);
                        Vector3 c = (a + b) * 0.5f + Vector3.up * 1.2f;
                        mb[0].AddOrientedBox(FRAME, c, dir, Vector3.up, nrm, new Vector3(len * 0.5f, 1.2f, 0.025f), new Vector2(0.1f, 0.1f), 0.2f);
                        AddGlassQuad(c + nrm * 0.03f, dir, nrm, len - 0.16f, 2.2f);
                        AddGlassQuad(c - nrm * 0.03f, dir, -nrm, len - 0.16f, 2.2f);
                    }
                }
            }
            else
            {
                Box(0, FACADE, s, u0, u1, y0, ys, 0f, W188Plan.FacadeT, 2f);
                Box(0, FACADE, s, u0, u1, yt, yTop, 0f, W188Plan.FacadeT, 2f);
                Box(0, FACADE, s, u0, wl, ys, yt, 0f, W188Plan.FacadeT, 2f);
                Box(0, FACADE, s, wr, u1, ys, yt, 0f, W188Plan.FacadeT, 2f);
                Col(s, u0, u1, y0, yTop, 0f, W188Plan.FacadeT);
                // marco blanco, montante central y alféizar
                float fd0 = 0.06f, fd1 = 0.13f;
                Box(0, FRAME, s, wl, wl + 0.06f, ys, yt, fd0, fd1, 1f);
                Box(0, FRAME, s, wr - 0.06f, wr, ys, yt, fd0, fd1, 1f);
                Box(0, FRAME, s, wl, wr, yt - 0.06f, yt, fd0, fd1, 1f);
                Box(0, FRAME, s, wl, wr, ys, ys + 0.06f, fd0, fd1, 1f);
                Box(0, FRAME, s, uc - 0.022f, uc + 0.022f, ys + 0.06f, yt - 0.06f, 0.085f, 0.115f, 1f);
                // alféizar de piedra con goterón (v2) y, en algunas ventanas, jardinera o aparato de aire
                Box(0, CORNICE, s, wl - 0.1f, wr + 0.1f, ys - 0.065f, ys + 0.005f, -0.09f, 0.07f, 1f);
                Box(0, CORNICE, s, wl - 0.1f, wr + 0.1f, ys - 0.085f, ys - 0.065f, -0.09f, -0.06f, 1f);
                int hv = H3(f, s, i, 7);
                if (k == W188Plan.Kind.Closed && hv % 9 == 0)
                {   // jardinera con plantas secas
                    Box(0, RUG, s, wl + 0.05f, wr - 0.05f, ys + 0.005f, ys + 0.17f, -0.3f, -0.08f, 1f);
                    for (int q = 0; q < 5; q++) { float qu = Mathf.Lerp(wl + 0.12f, wr - 0.12f, q / 4f); Box(0, CURT_GREEN, s, qu - 0.05f, qu + 0.05f, ys + 0.17f, ys + 0.27f + 0.06f * ((hv >> q) & 1), -0.25f, -0.13f, 1f); }
                }
                else if (k == W188Plan.Kind.Closed && hv % 13 == 1 && f > 0)
                {   // aparato de aire acondicionado viejo bajo la ventana
                    float au = uc + ((hv >> 4) % 2 == 0 ? -0.35f : 0.35f);
                    Box(0, PAINT, s, au - 0.38f, au + 0.38f, ys - 0.62f, ys - 0.12f, -0.34f, -0.02f, 1f);
                    for (int q = 0; q < 6; q++) Box(0, METAL, s, au - 0.3f, au + 0.3f, ys - 0.56f + q * 0.07f, ys - 0.545f + q * 0.07f, -0.345f, -0.335f, 1f);
                    Box(0, METAL, s, au - 0.42f, au - 0.38f, ys - 0.64f, ys - 0.1f, -0.36f, 0f, 1f); Box(0, METAL, s, au + 0.38f, au + 0.42f, ys - 0.64f, ys - 0.1f, -0.36f, 0f, 1f);
                }
                // chorretones bajo el alféizar y polvo en la junta del forjado
                if (hv % 10 < 7)
                {
                    float gl1 = 0.5f + (hv % 7) * 0.2f;
                    float cU = (hv >> 3) % 3 == 0 ? wl - 0.02f : (hv >> 3) % 3 == 1 ? wr + 0.02f : uc;
                    float wU = 0.32f + ((hv >> 6) % 4) * 0.1f, col = ((hv >> 8) % 5) * 0.2f;
                    QuadD(grimeMb, 0, s, cU - wU, cU + wU, Mathf.Max(y0 + 0.02f, ys - 0.085f - gl1), ys - 0.085f, -0.004f, true, new Rect(col, 0f, 0.2f, 1f));
                    if ((hv >> 11) % 2 == 0) QuadD(grimeMb, 0, s, wl - 0.1f, wr + 0.1f, Mathf.Max(y0 + 0.02f, ys - 0.085f - 0.35f), ys - 0.085f, -0.0045f, true, new Rect(((hv >> 12) % 5) * 0.2f, 0.4f, 0.2f, 0.6f));
                }
                QuadD(grimeMb, 0, s, u0, u1, yTop - 0.45f - 0.25f * ((hv >> 14) % 2), yTop, -0.003f, true, new Rect(((hv >> 15) % 5) * 0.2f, 0.55f, 0.2f, 0.45f));
                // cristal (por fuera siempre; por dentro si el cuarto existe)
                QuadD(glassMb, 0, s, wl + 0.06f, wr - 0.06f, ys + 0.06f, yt - 0.06f, 0.10f, true, new Rect(0, 0, 1, 1));
                if (built) QuadD(glassMb, 0, s, wl + 0.06f, wr - 0.06f, ys + 0.06f, yt - 0.06f, 0.10f, false, new Rect(0, 0, 1, 1));
                BuildWindowContent(s, f, i, k, uc, wl, wr, ys, yt);
            }
            // --- cuarto ---
            if (built) BuildRoomShell(rg, s, f, i, k, u0, u1, y0, yc, uc, wl, wr, ys, yt);
            // --- muro del pasillo con su puerta ---
            BuildCorridorWall(G, s, f, i, k, u0, u1, y0, yc, yTop);
        }
        if (f == 0) for (int q = 0; q < n; q++) QuadD(grimeMb, 0, s, q * W188Plan.M, (q + 1) * W188Plan.M, 0f, 0.55f + 0.2f * (H3(q, s, 0, 3) % 3), -0.0035f, true, new Rect((H3(q, s, 0, 4) % 5) * 0.2f, 0f, 0.2f, 1f));
        // --- pasillo: suelo, techo, muro exterior, zócalo y lámparas ---
        float uS = -CO;
        Box(G, CARPET, s, uS, L, y0 - 0.03f, y0, CI, CO, 2f);
        Box(G, CARPET, s, -CO, -CI, y0 - 0.03f, y0, 0f, CI, 2f);
        Box(G, CEIL, s, -R, L, yc, yc + 0.02f, CI - 0.15f, R, 2f);
        Box(G, CEIL, s, -R, -CI + 0.15f, yc, yc + 0.02f, 0f, CI, 2f);
        if (f > 0)
        {   // forjado (colisión) del pasillo y de los cuartos; el hueco de la escalera queda libre
            Col(s, 0f, L, y0 - 0.25f, y0, W188Plan.FacadeT, R);
            Col(s, -R, 0f, y0 - 0.25f, y0, W188Plan.RoomEnd, R);
            Col(s, -R, -W188Plan.RoomEnd, y0 - 0.25f, y0, 0f, W188Plan.RoomEnd);
        }
        BoxCol(G, WALL, s, -R, L, y0, yTop, CO, R, 1.5f);
        BoxCol(G, WALL, s, -R, -CO, y0, yTop, 0f, CO, 1.5f);
        Box(G, SKIRT, s, -CO, L, y0, y0 + 0.1f, CO - 0.015f, CO, 1f);
        Box(G, SKIRT, s, -CO + 0.0f, -CO + 0.015f, y0, y0 + 0.1f, 0f, CO, 1f);
        for (int i = 0; i < n; i += 1)
        {
            float u = W188Plan.UC(i) - 0.55f;
            AddCeilingLamp(G, s, f, u, W188Plan.UC(i) - 0.55f, true);   // v7.12.0: una luz por módulo (el culler deja las 8 más cercanas)
        }
        AddCeilingLamp(G, s, f, -5.15f, -5.15f, true, 2.4f);
    }

    void AddCeilingLamp(int G, int s, int f, float u, float uu, bool light, float dd = 5.15f)
    {
        float y0 = f * W188Plan.FH, yc = y0 + W188Plan.FH - 0.25f;
        Box(G, METAL, s, u - 0.07f, u + 0.07f, yc - 0.03f, yc, dd - 0.07f, dd + 0.07f, 1f);
        Box(G, LAMP, s, u - 0.17f, u + 0.17f, yc - 0.13f, yc - 0.03f, dd - 0.17f, dd + 0.17f, 1f);
        if (!light) return;
        GameObject g = new GameObject("CorrLamp"); g.transform.SetParent(propRoot, false);
        g.transform.localPosition = P(s, u, yc - 0.16f, dd); g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Light l = g.AddComponent<Light>(); l.type = LightType.Spot; l.spotAngle = 150f; l.innerSpotAngle = 70f; l.range = 5.8f;
        l.color = cfg != null ? Color.Lerp(cfg.lightColor, new Color(1f, 0.82f, 0.58f), 0.5f) : new Color(1f, 0.85f, 0.65f);
        l.intensity = 2.0f; l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
        Lamps.Add(new Lamp { l = l, f0 = f, f1 = f, baseI = l.intensity }); LampCount++;
    }

    void AddGlassQuad(Vector3 c, Vector3 right, Vector3 n, float w, float h)
    {
        Vector3 hr = right * (w * 0.5f), hu = Vector3.up * (h * 0.5f);
        glassMb.AddQuad(0, c - hr - hu, c + hr - hu, c + hr + hu, c - hr + hu, n, right, Vector3.up, 1f, 1f, 0f, 0f);
    }

    // lo que se ve detrás del cristal
    void BuildWindowContent(int s, int f, int i, W188Plan.Kind k, float uc, float wl, float wr, float ys, float yt)
    {
        switch (k)
        {
            case W188Plan.Kind.Closed:
                {
                    int v = plan.variant[f, s, i], hv = H3(f, s, i, 11);
                    bool lit = (v & 1) != 0; int tint = v >> 1;
                    int m = lit ? (tint == 2 ? CURT_COLD : tint == 3 ? CURT_ROSE : CURT_LIT) : (hv % 4 == 2 ? CURT_GREEN : CURT_DARK);
                    if (!lit && hv % 4 == 3)
                    {   // persiana de lamas, alguna torcida
                        int nsl = 22; float hgt = (yt - ys - 0.14f) / nsl;
                        int down = (hv >> 3) % 3 == 0 ? nsl : nsl - (hv >> 5) % 8;
                        for (int q = 0; q < down; q++)
                        {
                            float yy = yt - 0.07f - (q + 1) * hgt; float tilt = (q % 7 == 3 && (hv >> 9) % 2 == 0) ? 0.03f : 0f;
                            Box(0, BLIND, s, wl + 0.08f, wr - 0.08f, yy + hgt * 0.15f, yy + hgt * 0.85f, 0.17f + tilt, 0.185f + tilt, 1f);
                        }
                        Box(0, DARK, s, wl + 0.07f, wr - 0.07f, ys + 0.07f, yt - 0.07f, 0.24f, 0.25f, 1f);
                    }
                    else
                    {   // cortinas: a veces una descorrida, mostrando el cuarto oscuro detrás
                        float gap = (hv >> 2) % 5 == 0 ? 0.28f + ((hv >> 7) % 3) * 0.1f : 0f;
                        int folds = 4 + (hv >> 4) % 3; float amp = 0.018f + ((hv >> 6) % 3) * 0.006f;
                        if (gap > 0f)
                        {
                            Pleats(mb[0], m, -1, s, wl + 0.07f, uc - gap * 0.5f, ys + 0.07f, yt - 0.07f, 0.19f, amp * 1.4f, folds);
                            Pleats(mb[0], m, -1, s, uc + gap * 0.5f + 0.1f, wr - 0.07f, ys + 0.07f, yt - 0.07f, 0.19f, amp * 1.4f, folds - 1);
                            Box(0, DARK, s, wl + 0.07f, wr - 0.07f, ys + 0.07f, yt - 0.07f, 0.6f, 0.61f, 1f);
                        }
                        else Pleats(mb[0], m, -1, s, wl + 0.07f, wr - 0.07f, ys + 0.07f, yt - 0.07f, 0.19f, amp, folds);
                    }
                    break;
                }
            case W188Plan.Kind.Open:
            case W188Plan.Kind.Exit:
                {   // caja oscura y, al fondo, la imagen del otro sitio (emisiva); la salida, con un prado al sol
                    float e0 = wl - 0.62f, e1 = wr + 0.62f, ey0 = ys - 0.5f, ey1 = yt + 0.55f, dd = 0.56f;
                    Material vm = k == W188Plan.Kind.Exit ? ExitViewMat : ViewMat(plan.variant[f, s, i]);
                    MeshBuilder vmb = new MeshBuilder(1);
                    float aspect = (e1 - e0) / (ey1 - ey0); float uw = Mathf.Clamp01(aspect * 9f / 16f);
                    if (k == W188Plan.Kind.Exit) uw = Mathf.Clamp01(aspect * 0.75f);
                    QuadD(vmb, 0, s, e0, e1, ey0, ey1, dd, true, new Rect(0.5f - uw * 0.5f, 0f, uw, 1f));
                    if (k == W188Plan.Kind.Exit)
                    {   // desde dentro: el mismo prado, en un plano al otro lado del cristal que mira hacia el cuarto
                        float m0 = wl - 0.45f, m1 = wr + 0.45f, my0 = ys - 0.4f, my1 = yt + 0.4f;
                        float a2 = (m1 - m0) / (my1 - my0), u2 = Mathf.Clamp01(a2 * 0.75f);
                        QuadD(vmb, 0, s, m0, m1, my0, my1, -0.035f, false, new Rect(0.5f - u2 * 0.5f, 0f, u2, 1f));
                    }
                    GameObject vo = OceanGeo.Emit(vmb, k == W188Plan.Kind.Exit ? "SunnyView" : "OtherPlace" + plan.variant[f, s, i], transform, new[] { vm }, false, false, track);
                    vo.GetComponent<MeshRenderer>().receiveShadows = false;
                    if (k == W188Plan.Kind.Open)
                    {
                        Box(0, DARK, s, e0, e1, ey0 - 0.02f, ey0, W188Plan.FacadeT, dd, 1f);
                        Box(0, DARK, s, e0, e1, ey1, ey1 + 0.02f, W188Plan.FacadeT, dd, 1f);
                        Box(0, DARK, s, e0 - 0.02f, e0, ey0, ey1, W188Plan.FacadeT, dd, 1f);
                        Box(0, DARK, s, e1, e1 + 0.02f, ey0, ey1, W188Plan.FacadeT, dd, 1f);
                    }
                    break;
                }
            case W188Plan.Kind.Room:
                BuildCurtain(s, f, i, uc, wl, wr, ys, yt);
                break;
        }
    }

    void BuildCurtain(int s, int f, int i, float uc, float wl, float wr, float ys, float yt)
    {
        // dos paños colgados de una barra, por dentro del cuarto; se descorren escalando hacia los lados
        float y0 = ys - 0.12f, y1 = yt + 0.28f, d = 0.34f, half = (wr - wl) * 0.5f + 0.2f;
        Box(1 + f, METAL, s, wl - 0.3f, wr + 0.3f, y1 + 0.02f, y1 + 0.045f, d - 0.012f, d + 0.012f, 1f);
        Curtain c = new Curtain { f = f, s = s, i = i };
        for (int side = -1; side <= 1; side += 2)
        {
            Transform t = new GameObject(side < 0 ? "CurtainL" : "CurtainR").transform; t.SetParent(propRoot, false);
            float hu = uc + side * (half + 0.0f);           // anclaje en el extremo exterior de cada paño
            t.localPosition = P(s, hu, 0f, 0f); t.localRotation = Quaternion.Euler(0f, W188Plan.SideYaw(s), 0f);
            MeshBuilder m = new MeshBuilder(2);
            // en el marco del lado, relativo al anclaje: u de 0 a -side*half
            int nn = 40;
            System.Func<int, float> fold = kk => 0.045f * (0.65f * Mathf.Sin(kk * 0.78f + side) + 0.35f * Mathf.Sin(kk * 1.9f + 0.7f * side)) * (0.6f + 0.4f * kk / nn);
            for (int k = 0; k < nn; k++)
            {
                float ua = -side * half * k / nn, ub = -side * half * (k + 1) / nn;
                float da = d + fold(k), db = d + fold(k + 1);
                Vector3 a0 = new Vector3(ua, y0, -da), b0 = new Vector3(ub, y0, -db), b1 = new Vector3(ub, y1, -db), a1 = new Vector3(ua, y1, -da);
                Vector3 along = (b0 - a0).normalized, nc = Vector3.Cross(Vector3.up, along).normalized;
                if (nc.z < 0f) nc = -nc;   // en el marco del lado, +z = hacia el patio
                m.AddQuad(0, a0, b0, b1, a1, nc, along, Vector3.up, 0.9f, 0.9f, 0f, 0f);
                m.AddQuad(1, a0, b0, b1, a1, -nc, along, Vector3.up, 0.9f, 0.9f, 0f, 0f);
            }
            GameObject go = OceanGeo.Emit(m, "Panel", t, new[] { mats[CURT_LIT], curtainIn }, false, false, track);
            if (side < 0) c.left = t; else c.right = t;
        }
        c.center = levelRoot.TransformPoint(P(s, uc, (ys + yt) * 0.5f, d));
        c.inward = levelRoot.TransformDirection(Dd(s));
        Curtains.Add(c);
    }

    void BuildRoomShell(int g, int s, int f, int i, W188Plan.Kind k, float u0, float u1, float y0, float yc, float uc, float wl, float wr, float ys, float yt)
    {
        bool dark = k == W188Plan.Kind.Entity;
        int wallM = dark ? DARK : WALL, floorM = dark ? DARK : k == W188Plan.Kind.Passage ? PAVING : RCARPET, ceilM = dark ? DARK : CEIL;
        float F = W188Plan.FacadeT, E = W188Plan.RoomEnd;
        Box(g, floorM, s, u0, u1, y0 - 0.03f, y0, F, E, k == W188Plan.Kind.Passage ? 3.2f : 2f);
        Box(g, ceilM, s, u0, u1, yc, yc + 0.02f, F, E, 2f);
        BoxCol(g, wallM, s, u0, u0 + 0.06f, y0, yc, F, E, 1.5f);
        BoxCol(g, wallM, s, u1 - 0.06f, u1, y0, yc, F, E, 1.5f);
        float a = u0 + 0.06f, b = u1 - 0.06f, t = F + 0.02f;
        if (k == W188Plan.Kind.Passage || k == W188Plan.Kind.Lift)
        {
            float hw = k == W188Plan.Kind.Lift ? 1.45f : 0.78f, hh = k == W188Plan.Kind.Lift ? 2.6f : 2.45f;
            Box(g, wallM, s, a, uc - hw, y0, yc, F, t, 1.5f); Box(g, wallM, s, uc + hw, b, y0, yc, F, t, 1.5f); Box(g, wallM, s, uc - hw, uc + hw, y0 + hh, yc, F, t, 1.5f);
            // lámpara de techo del vestíbulo / rellano del ascensor
            Box(g, LAMP, s, uc - 0.3f, uc + 0.3f, yc - 0.04f, yc, 1.8f, 2.4f, 1f);
            Light l = MakePoint(P(s, uc, yc - 0.2f, 2.1f), 4.2f, 1.1f, new Color(1f, 0.88f, 0.70f));
            Lamps.Add(new Lamp { l = l, f0 = f, f1 = f, always = true, baseI = l.intensity });
        }
        else
        {
            Box(g, wallM, s, a, b, y0, ys, F, t, 1.5f); Box(g, wallM, s, a, b, yt, yc, F, t, 1.5f);
            Box(g, wallM, s, a, wl, ys, yt, F, t, 1.5f); Box(g, wallM, s, wr, b, ys, yt, F, t, 1.5f);
            Box(g, FRAME, s, wl - 0.05f, wr + 0.05f, ys - 0.04f, ys, F - 0.02f, F + 0.12f, 1f);   // alféizar interior
        }
        if (k == W188Plan.Kind.Room || k == W188Plan.Kind.Exit) BuildFurniture(g, s, f, i, k, u0, u1, y0, yc, uc);
        if (k == W188Plan.Kind.Exit)
        {   // luz de sol que entra por la ventana
            Light l = MakePoint(P(s, uc, ys + 0.6f, 0.9f), 5.5f, 1.9f, new Color(1f, 0.93f, 0.78f));
            Lamps.Add(new Lamp { l = l, f0 = f, f1 = f, always = true, baseI = l.intensity });
        }
        if (dark) BuildEntityRoom(s, f, i, u0, u1, y0, yc, uc);
    }

    Light MakePoint(Vector3 local, float range, float intensity, Color c)
    {
        GameObject g = new GameObject("RoomLight"); g.transform.SetParent(propRoot, false); g.transform.localPosition = local;
        Light l = g.AddComponent<Light>(); l.type = LightType.Point; l.range = range; l.intensity = intensity; l.color = c; l.shadows = LightShadows.None;
        LampCount++;
        return l;
    }

    // habitación de hotel modesta: cama, mesilla con lamparita, armario; moqueta y papel pintado
    void BuildFurniture(int g, int s, int f, int i, W188Plan.Kind k, float u0, float u1, float y0, float yc, float uc)
    {
        float a = u0 + 0.06f;
        // cama (cabecero contra la medianera u0)
        Box(g, WOOD, s, a, a + 0.06f, y0, y0 + 1.05f, 1.0f, 2.45f, 1f);                       // cabecero
        Box(g, WOOD, s, a + 0.06f, a + 1.98f, y0 + 0.1f, y0 + 0.32f, 1.02f, 2.43f, 1f);       // somier
        Box(g, LINEN, s, a + 0.08f, a + 1.96f, y0 + 0.32f, y0 + 0.52f, 1.04f, 2.41f, 0.9f);   // colchón
        Box(g, CURT_DARK, s, a + 0.55f, a + 1.98f, y0 + 0.52f, y0 + 0.57f, 0.99f, 2.46f, 0.9f); // colcha
        Box(g, LINEN, s, a + 0.12f, a + 0.48f, y0 + 0.52f, y0 + 0.64f, 1.12f, 1.66f, 0.9f);   // almohadas
        Box(g, LINEN, s, a + 0.12f, a + 0.48f, y0 + 0.52f, y0 + 0.64f, 1.8f, 2.34f, 0.9f);
        foreach (float lu in new[] { a + 0.1f, a + 1.9f }) foreach (float ld in new[] { 1.06f, 2.36f })
            Box(g, WOOD, s, lu - 0.03f, lu + 0.03f, y0, y0 + 0.1f, ld - 0.03f, ld + 0.03f, 1f);
        Col(s, a, a + 1.98f, y0, y0 + 0.57f, 1.0f, 2.46f);
        // mesilla con lamparita (entre la cama y la ventana)
        Box(g, WOOD, s, a, a + 0.42f, y0, y0 + 0.55f, 0.45f, 0.92f, 1f);
        Box(g, METAL, s, a + 0.18f, a + 0.24f, y0 + 0.55f, y0 + 0.78f, 0.66f, 0.72f, 1f);
        Box(g, LAMP, s, a + 0.08f, a + 0.34f, y0 + 0.78f, y0 + 0.98f, 0.56f, 0.82f, 1f);
        Col(s, a, a + 0.42f, y0, y0 + 0.98f, 0.45f, 0.92f);
        if (k == W188Plan.Kind.Room)
        {
            Light l = MakePoint(P(s, a + 0.21f, y0 + 1.15f, 0.69f), 3.6f, 1.05f, new Color(1f, 0.80f, 0.52f));
            Lamps.Add(new Lamp { l = l, f0 = f, f1 = f, baseI = l.intensity });
        }
        // armario junto a la puerta: dos puertas con junta, tiradores y zócalo
        Box(g, WOOD, s, a, a + 0.6f, y0 + 0.06f, y0 + 2.0f, 3.3f, 3.98f, 1f);
        Box(g, SKIRT, s, a + 0.02f, a + 0.58f, y0, y0 + 0.06f, 3.32f, 3.96f, 1f);
        Box(g, SKIRT, s, a + 0.6f, a + 0.605f, y0 + 0.08f, y0 + 1.98f, 3.635f, 3.645f, 1f);
        Box(g, METAL, s, a + 0.6f, a + 0.62f, y0 + 0.95f, y0 + 1.1f, 3.56f, 3.58f, 1f);
        Box(g, METAL, s, a + 0.6f, a + 0.62f, y0 + 0.95f, y0 + 1.1f, 3.70f, 3.72f, 1f);
        Box(g, WOOD, s, a - 0.01f, a + 0.62f, y0 + 2.0f, y0 + 2.04f, 3.28f, 4.0f, 1f);
        Col(s, a, a + 0.6f, y0, y0 + 2.0f, 3.3f, 3.98f);
        float b = u1 - 0.06f;
        // alfombra gastada a los pies de la cama, con flecos
        Box(g, RUG, s, a + 0.7f, b - 0.5f, y0, y0 + 0.012f, 1.25f, 3.0f, 1.2f);
        for (int q = 0; q < 14; q++) { float fu = Mathf.Lerp(a + 0.74f, b - 0.54f, q / 13f); Box(g, LINEN, s, fu - 0.01f, fu + 0.01f, y0, y0 + 0.006f, 3.0f, 3.06f, 1f); }
        // radiador de hierro bajo la ventana
        float rw0 = uc - 0.55f, rw1 = uc + 0.55f;
        for (int q = 0; q < 10; q++) { float ru = Mathf.Lerp(rw0, rw1, q / 9f); Box(g, PAINT, s, ru - 0.035f, ru + 0.035f, y0 + 0.14f, y0 + 0.68f, 0.3f, 0.4f, 1f); }
        Box(g, PAINT, s, rw0 - 0.04f, rw1 + 0.04f, y0 + 0.17f, y0 + 0.2f, 0.33f, 0.37f, 1f); Box(g, PAINT, s, rw0 - 0.04f, rw1 + 0.04f, y0 + 0.62f, y0 + 0.65f, 0.33f, 0.37f, 1f);
        Box(g, METAL, s, rw1 + 0.04f, rw1 + 0.07f, y0, y0 + 0.2f, 0.34f, 0.37f, 1f);
        // cuadro sobre la cama (un paisaje desvaído: otra vista del prado)
        Box(g, WOOD, s, a, a + 0.035f, y0 + 1.25f, y0 + 1.85f, 1.35f, 2.15f, 1f);
        QuadUside(g, s, a + 0.036f, y0 + 1.3f, y0 + 1.8f, 1.4f, 2.1f);
        // lámpara de techo con pantalla de tela
        Box(g, METAL, s, uc - 0.01f, uc + 0.01f, yc - 0.35f, yc, 2.0f, 2.02f, 1f);
        Box(g, LINEN, s, uc - 0.2f, uc + 0.2f, yc - 0.55f, yc - 0.35f, 1.81f, 2.21f, 1f);
        if (k == W188Plan.Kind.Room)
        {   // escritorio estrecho y silla contra la medianera (no en el cuarto de la salida: su paso queda libre)
            Box(g, WOOD, s, b - 0.42f, b, y0 + 0.72f, y0 + 0.76f, 2.0f, 3.0f, 1f);
            foreach (float ld in new[] { 2.03f, 2.97f }) Box(g, WOOD, s, b - 0.4f, b - 0.36f, y0, y0 + 0.72f, ld - 0.02f, ld + 0.02f, 1f);
            Box(g, WOOD, s, b - 0.42f, b - 0.02f, y0 + 0.5f, y0 + 0.72f, 2.6f, 2.98f, 1f);   // cajonera
            Col(s, b - 0.42f, b, y0, y0 + 0.76f, 2.0f, 3.0f);
            Box(g, LINEN, s, b - 0.3f, b - 0.08f, y0 + 0.76f, y0 + 0.79f, 2.2f, 2.5f, 1f);   // papeles
            // silla de madera, un poco girada (sin colisión)
            float cu = b - 0.7f, cd = 2.45f;
            Box(g, WOOD, s, cu - 0.21f, cu + 0.21f, y0 + 0.44f, y0 + 0.48f, cd - 0.21f, cd + 0.21f, 1f);
            foreach (float lu in new[] { cu - 0.18f, cu + 0.18f }) foreach (float ld in new[] { cd - 0.18f, cd + 0.18f }) Box(g, WOOD, s, lu - 0.02f, lu + 0.02f, y0, y0 + 0.44f, ld - 0.02f, ld + 0.02f, 1f);
            Box(g, WOOD, s, cu - 0.21f, cu - 0.18f, y0 + 0.48f, y0 + 0.92f, cd - 0.21f, cd + 0.21f, 1f);
        }
    }
    void QuadUside(int g, int s, float u, float yA, float yB, float dA, float dB)
    {   // cuadro: imagen del prado en la pared u (mira a +u)
        MeshBuilder q = new MeshBuilder(1);
        Vector3 p0 = P(s, u, yA, dA), p1 = P(s, u, yA, dB), p2 = P(s, u, yB, dB), p3 = P(s, u, yB, dA);
        q.AddQuadUV(0, p0, p1, p2, p3, Ud(s), Dd(s), Vector3.up, new Vector2(0.2f, 0.25f), new Vector2(0.7f, 0.25f), new Vector2(0.7f, 0.75f), new Vector2(0.2f, 0.75f));
        if (paintingMat == null) { paintingMat = Pbr("W188Facade", Color.white, 1f); paintingMat.name = "W188_Painting"; Texture2D t = Resources.Load<Texture2D>("W188/W188ExitView"); if (t != null) paintingMat.mainTexture = t; paintingMat.color = new Color(0.62f, 0.6f, 0.55f); if (paintingMat.HasProperty("_BumpMap")) paintingMat.SetTexture("_BumpMap", null); }
        GameObject go = OceanGeo.Emit(q, "Painting", g == 0 ? transform : FloorParent(g), new[] { paintingMat }, false, false, track);
    }
    Material paintingMat;
    readonly Dictionary<int, Transform> floorParents = new Dictionary<int, Transform>();
    Transform FloorParent(int g) { Transform t; if (!floorParents.TryGetValue(g, out t)) { t = new GameObject("FloorProps" + g).transform; t.SetParent(transform, false); floorParents[g] = t; } return t; }

    // la Ventana: cuarto a oscuras, contraluz rojizo junto a la puerta y una silueta que hace señas desde el cristal
    void BuildEntityRoom(int s, int f, int i, float u0, float u1, float y0, float yc, float uc)
    {
        Box(0, GLOW, s, u0 + 0.4f, u0 + 1.4f, y0 + 0.3f, y0 + 2.1f, W188Plan.RoomEnd - 0.012f, W188Plan.RoomEnd - 0.002f, 1f);
        Light l = MakePoint(P(s, uc - 0.6f, y0 + 1.9f, 2.6f), 4.8f, 1.15f, new Color(0.85f, 0.32f, 0.18f)); l.renderMode = LightRenderMode.ForcePixel;   // si no, con 8 lámparas de pasillo por píxel queda en vértices y el cuarto se ve negro
        Lamps.Add(new Lamp { l = l, f0 = f, f1 = f, always = true, baseI = l.intensity });
        Figure fig = new Figure { f = f, s = s, i = i, phase = (f * 7 + s * 3 + i) * 0.37f };
        Transform root = new GameObject("WindowFigure").transform; root.SetParent(propRoot, false);
        root.localPosition = P(s, uc, y0, 1.05f);
        root.localRotation = Quaternion.Euler(0f, W188Plan.SideYaw(s), 0f);   // mira al patio (+z del marco)
        Rig rig = BuildFigure(root, FigureMat, true);
        fig.arm = rig.shoulder; fig.elbow = rig.elbow; fig.wrist = rig.wrist; fig.head = rig.head; fig.chest = rig.chest; fig.fingers.AddRange(rig.fingers);
        // contraluz: una luz fría y débil detrás de la figura (hacia el fondo del cuarto) recorta su silueta desde el patio
        Light rim = MakePoint(P(s, uc + 0.5f, y0 + 2.1f, 1.9f), 2.4f, 0.55f, new Color(0.55f, 0.62f, 0.75f)); rim.renderMode = LightRenderMode.ForcePixel;
        Lamps.Add(new Lamp { l = rim, f0 = f, f1 = f, always = true, baseI = rim.intensity });
        fig.root = root;
        fig.roomCenter = levelRoot.TransformPoint(P(s, uc, y0, 2.1f));
        fig.doorWorld = levelRoot.TransformPoint(P(s, W188Plan.DoorU(i), y0, W188Plan.RoomEnd + 0.07f));
        Figures.Add(fig);
    }

    // la Ventana (v2): figura humanoide demasiado alta y delgada, con volumen (pecho, pelvis, hombros, codos, rodillas, manos con
    // dedos), cabeza alargada y un poco ladeada. Jerarquía de brazo derecho: hombro -> codo -> muñeca -> dedos (para llamar con la mano).
    public class Rig { public Transform shoulder, elbow, wrist, head, chest; public readonly List<Transform> fingers = new List<Transform>(); }
    static GameObject Part(PrimitiveType t, string n, Transform parent, Vector3 pos, Vector3 scale, Material m, Vector3 euler = default(Vector3))
    {
        GameObject g = ProceduralAssets.CreatePrimitive(t, n, parent, pos, scale, m, false);
        g.transform.localRotation = Quaternion.Euler(euler);
        MeshRenderer r = g.GetComponent<MeshRenderer>(); if (r != null) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = false; }
        return g;
    }
    public static Rig BuildFigure(Transform root, Material m, bool beckon)
    {
        Rig rig = new Rig();
        Transform body = new GameObject("Body").transform; body.SetParent(root, false);
        // piernas largas, rodillas marcadas, pies
        foreach (float sx in new[] { -0.1f, 0.1f })
        {
            Part(PrimitiveType.Capsule, "Thigh", body, new Vector3(sx, 0.86f, 0f), new Vector3(0.15f, 0.27f, 0.15f), m, new Vector3(0f, 0f, sx * 8f));
            Part(PrimitiveType.Sphere, "Knee", body, new Vector3(sx * 1.08f, 0.58f, 0.01f), new Vector3(0.1f, 0.11f, 0.1f), m);
            Part(PrimitiveType.Capsule, "Shin", body, new Vector3(sx * 1.1f, 0.31f, -0.01f), new Vector3(0.11f, 0.27f, 0.11f), m);
            Part(PrimitiveType.Capsule, "Foot", body, new Vector3(sx * 1.12f, 0.035f, 0.07f), new Vector3(0.075f, 0.1f, 0.075f), m, new Vector3(90f, 0f, 0f));
        }
        Part(PrimitiveType.Sphere, "Pelvis", body, new Vector3(0f, 1.12f, 0f), new Vector3(0.32f, 0.2f, 0.19f), m);
        Transform chest = new GameObject("Chest").transform; chest.SetParent(body, false); chest.localPosition = new Vector3(0f, 1.2f, 0f); rig.chest = chest;
        Part(PrimitiveType.Capsule, "Waist", chest, new Vector3(0f, 0.12f, 0f), new Vector3(0.22f, 0.16f, 0.15f), m);
        Part(PrimitiveType.Sphere, "Ribs", chest, new Vector3(0f, 0.36f, 0.005f), new Vector3(0.34f, 0.36f, 0.2f), m);
        Part(PrimitiveType.Capsule, "Shoulders", chest, new Vector3(0f, 0.52f, -0.01f), new Vector3(0.1f, 0.21f, 0.1f), m, new Vector3(0f, 0f, 90f));
        Part(PrimitiveType.Capsule, "Neck", chest, new Vector3(0f, 0.66f, 0.01f), new Vector3(0.065f, 0.11f, 0.065f), m, new Vector3(-10f, 0f, 0f));
        Transform head = new GameObject("Head").transform; head.SetParent(chest, false); head.localPosition = new Vector3(0f, 0.74f, 0.01f);
        head.localRotation = Quaternion.Euler(6f, 0f, 9f); rig.head = head;
        Part(PrimitiveType.Sphere, "Skull", head, new Vector3(0f, 0.14f, -0.005f), new Vector3(0.175f, 0.245f, 0.2f), m);
        Part(PrimitiveType.Sphere, "Jaw", head, new Vector3(0f, 0.035f, 0.025f), new Vector3(0.105f, 0.11f, 0.12f), m);
        // brazo izquierdo colgando, demasiado largo
        Transform ls = new GameObject("ShoulderL").transform; ls.SetParent(chest, false); ls.localPosition = new Vector3(-0.21f, 0.52f, 0f); ls.localRotation = Quaternion.Euler(4f, 0f, -4f);
        Part(PrimitiveType.Capsule, "UpperArmL", ls, new Vector3(0f, -0.2f, 0f), new Vector3(0.085f, 0.2f, 0.085f), m);
        Part(PrimitiveType.Capsule, "ForearmL", ls, new Vector3(-0.01f, -0.6f, 0.03f), new Vector3(0.07f, 0.21f, 0.07f), m, new Vector3(-6f, 0f, 0f));
        Part(PrimitiveType.Sphere, "HandL", ls, new Vector3(-0.015f, -0.88f, 0.05f), new Vector3(0.06f, 0.13f, 0.035f), m);
        for (int q = 0; q < 4; q++) Part(PrimitiveType.Capsule, "FingerL", ls, new Vector3(-0.03f + q * 0.016f, -0.99f, 0.055f), new Vector3(0.014f, 0.055f, 0.014f), m);
        // brazo derecho: hombro -> codo -> muñeca -> 4 dedos (llama con la mano)
        Transform sh = new GameObject("ShoulderR").transform; sh.SetParent(chest, false); sh.localPosition = new Vector3(0.21f, 0.52f, 0f); rig.shoulder = sh;
        Part(PrimitiveType.Capsule, "UpperArmR", sh, new Vector3(0f, -0.2f, 0f), new Vector3(0.085f, 0.2f, 0.085f), m);
        Transform el = new GameObject("ElbowR").transform; el.SetParent(sh, false); el.localPosition = new Vector3(0f, -0.4f, 0f); rig.elbow = el;
        Part(PrimitiveType.Sphere, "ElbowJ", el, Vector3.zero, new Vector3(0.08f, 0.08f, 0.08f), m);
        Part(PrimitiveType.Capsule, "ForearmR", el, new Vector3(0f, -0.21f, 0f), new Vector3(0.07f, 0.21f, 0.07f), m);
        Transform wr = new GameObject("WristR").transform; wr.SetParent(el, false); wr.localPosition = new Vector3(0f, -0.43f, 0f); rig.wrist = wr;
        Part(PrimitiveType.Sphere, "PalmR", wr, new Vector3(0f, -0.06f, 0f), new Vector3(0.075f, 0.11f, 0.035f), m);
        for (int q = 0; q < 4; q++)
        {
            Transform fp = new GameObject("FingerR" + q).transform; fp.SetParent(wr, false); fp.localPosition = new Vector3(-0.027f + q * 0.018f, -0.11f, 0f);
            Part(PrimitiveType.Capsule, "Phal", fp, new Vector3(0f, -0.04f, 0f), new Vector3(0.015f, 0.045f + (q == 1 || q == 2 ? 0.01f : 0f), 0.015f), m);
            rig.fingers.Add(fp);
        }
        if (beckon) { sh.localRotation = Quaternion.Euler(-40f, 0f, 14f); el.localRotation = Quaternion.Euler(-75f, 0f, 0f); }
        return rig;
    }
    // compatibilidad: los Periféricos usan la misma figura, quieta
    public static Transform BuildSilhouette(Transform root, Material m, bool wave) { return BuildFigure(root, m, wave).shoulder; }

    void BuildCorridorWall(int G, int s, int f, int i, W188Plan.Kind k, float u0, float u1, float y0, float yc, float yTop)
    {
        float E = W188Plan.RoomEnd, CI = W188Plan.CorrIn;
        if (k == W188Plan.Kind.Lift) { BoxCol(G, WALL, s, u0, u1, y0, yTop, E, CI, 1.5f); SkirtCorr(G, s, y0, u0, u1); return; }
        float du = W188Plan.DoorU(i), dl = du - W188Plan.DoorW * 0.5f, dr = du + W188Plan.DoorW * 0.5f, dh = y0 + W188Plan.DoorH;
        BoxCol(G, WALL, s, u0, dl, y0, yTop, E, CI, 1.5f);
        BoxCol(G, WALL, s, dr, u1, y0, yTop, E, CI, 1.5f);
        BoxCol(G, WALL, s, dl, dr, dh, yTop, E, CI, 1.5f);
        SkirtCorr(G, s, y0, u0, dl - 0.07f); SkirtCorr(G, s, y0, dr + 0.07f, u1);
        // jambas (lado pasillo y lado cuarto si existe)
        foreach (float dd in new[] { CI, E - 0.025f })
        {
            if (dd < CI && !(k == W188Plan.Kind.Room || k == W188Plan.Kind.Exit || k == W188Plan.Kind.Passage)) continue;
            int gg = dd < CI && k != W188Plan.Kind.Room ? 0 : G;
            Box(gg, FRAME, s, dl - 0.07f, dl, y0, dh + 0.07f, dd, dd + 0.025f, 1f);
            Box(gg, FRAME, s, dr, dr + 0.07f, y0, dh + 0.07f, dd, dd + 0.025f, 1f);
            Box(gg, FRAME, s, dl - 0.07f, dr + 0.07f, dh, dh + 0.07f, dd, dd + 0.025f, 1f);
        }
        Box(G, CONC, s, dl, dr, y0 - 0.03f, y0 + 0.004f, E, CI, 1f);    // umbral
        Vector3 doorCenterW = levelRoot.TransformPoint(P(s, du, y0 + 1.0f, (E + CI) * 0.5f));
        switch (k)
        {
            case W188Plan.Kind.Passage: return;   // hueco abierto al vestíbulo
            case W188Plan.Kind.Closed:
            case W188Plan.Kind.Open:
                {   // hoja fija: no se abre nunca
                    Box(G, DOOR, s, dl + 0.01f, dr - 0.01f, y0 + 0.008f, dh - 0.005f, E + 0.05f, E + 0.095f, 1f);
                    Box(G, METAL, s, dr - 0.15f, dr - 0.07f, y0 + 1.0f, y0 + 1.03f, E + 0.095f, E + 0.15f, 1f);
                    Box(G, METAL, s, dr - 0.13f, dr - 0.09f, y0 + 0.95f, y0 + 1.1f, E + 0.095f, E + 0.105f, 1f);
                    Col(s, dl, dr, y0, dh, E, CI);
                    Locked.Add(new LockedDoor { pos = levelRoot.TransformPoint(P(s, du, y0, CI)), outward = levelRoot.TransformDirection(Dd(s)), f = f, view = k == W188Plan.Kind.Open });
                    return;
                }
            default:
                {   // hoja con bisagra en dl, se abre hacia el cuarto (d decreciente)
                    Transform hinge = new GameObject(k == W188Plan.Kind.Exit ? "ExitLeaf" : "RoomLeaf").transform; hinge.SetParent(propRoot, false);
                    hinge.localPosition = P(s, dl + 0.01f, y0, E + 0.075f);
                    hinge.localRotation = Quaternion.Euler(0f, W188Plan.SideYaw(s), 0f);
                    MeshBuilder lm = new MeshBuilder(2);
                    float w = W188Plan.DoorW - 0.02f;
                    OceanGeo.Box(lm, 0, new Vector3(0f, 0.008f, -0.0225f), new Vector3(w, W188Plan.DoorH - 0.013f, 0.0225f), 1f);
                    OceanGeo.Box(lm, 1, new Vector3(w - 0.15f, 1.0f, -0.08f), new Vector3(w - 0.07f, 1.03f, -0.0225f), 1f);   // manilla lado pasillo
                    OceanGeo.Box(lm, 1, new Vector3(w - 0.15f, 1.0f, 0.0225f), new Vector3(w - 0.07f, 1.03f, 0.08f), 1f);     // lado cuarto
                    OceanGeo.Emit(lm, "Leaf", hinge, new[] { mats[DOOR], mats[METAL] }, false, false, track);
                    if (k == W188Plan.Kind.Entity) { hinge.localRotation = Quaternion.Euler(0f, W188Plan.SideYaw(s) - 62f, 0f); return; }   // entornada (se ve dentro desde el pasillo)
                    BoxCollider bc = ColL(P(s, dl, y0, E), P(s, dr, dh, CI));
                    if (k == W188Plan.Kind.Exit)
                    {
                        ExitLeaf = hinge; exitBlocker = bc;
                        GameObject ex = new GameObject("ExitPoint"); ex.transform.SetParent(transform, false);
                        ex.transform.localPosition = P(s, du, y0, CI + 0.6f);
                        ex.transform.localRotation = Quaternion.LookRotation(-Dd(s), Vector3.up);
                        ExitTransform = ex.transform;
                        ExitCell = W188Plan.CellOf(P(s, W188Plan.UC(i), y0, 2f));
                        BuildExitCue(ex.transform, s, y0, du);
                        return;
                    }
                    W188Door door = hinge.gameObject.AddComponent<W188Door>();
                    door.center = doorCenterW; door.openAngle = -95f; door.blocker = bc; door.floorY = levelRoot.TransformPoint(new Vector3(0f, y0, 0f)).y;
                    Doors.Add(door);
                    return;
                }
        }
    }

    void SkirtCorr(int G, int s, float y0, float a, float b)
    {
        if (b - a < 0.02f) return;
        Box(G, SKIRT, s, a, b, y0, y0 + 0.1f, W188Plan.CorrIn, W188Plan.CorrIn + 0.015f, 1f);
    }

    // pista sutil: la rendija bajo la puerta de salida deja pasar luz de día (como el LED de otros niveles)
    void BuildExitCue(Transform ex, int s, float y0, float du)
    {
        Material glow = Plain("ExitGap", new Color(1f, 0.96f, 0.86f), 0.1f); ProceduralAssets.SetEmission(glow, new Color(1.6f, 1.45f, 1.15f));
        GameObject holder = new GameObject("ExitDoorGlow"); holder.transform.SetParent(transform, false);
        holder.transform.localPosition = P(s, du, y0, W188Plan.CorrIn);
        holder.transform.localRotation = ex.localRotation;
        GameObject led = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "DoorGapLight", holder.transform, new Vector3(0f, 0.004f, 0.01f), new Vector3(W188Plan.DoorW - 0.06f, 0.006f, 0.03f), glow, false);
        GameObject gl = new GameObject("GapGlow"); gl.transform.SetParent(holder.transform, false); gl.transform.localPosition = new Vector3(0f, 0.12f, 0.25f);
        Light l = gl.AddComponent<Light>(); l.type = LightType.Point; l.range = 1.6f; l.color = new Color(1f, 0.95f, 0.85f); l.intensity = 0.5f; l.shadows = LightShadows.None;
        ExitDoorCue cue = holder.AddComponent<ExitDoorCue>();
        cue.ledRenderer = led.GetComponent<Renderer>(); cue.glow = l; cue.visibleDistance = 7f; cue.glowIntensity = 0.55f;
        cue.ledRenderer.enabled = false; l.enabled = false;
    }

    // ---------------- esquinas: escaleras (SO y NE) o cuartos de servicio cerrados ----------------
    public const float StA0 = -3.95f, StA1 = -2.75f, StB0 = -1.3f, StB1 = -0.1f, StLand = 1.65f, StRise = 1.5f;
    public const int Steps = 9;
    void BuildCornerBlock(int s)
    {
        float CI = W188Plan.CorrIn, E = W188Plan.RoomEnd, H = W188Plan.Floors * W188Plan.FH;
        if (!W188Plan.HasStair(s))
        {
            for (int f = 0; f < W188Plan.Floors; f++)
            {
                float y0 = f * W188Plan.FH;
                BoxCol(1 + f, WALL, s, -CI, 0f, y0, y0 + W188Plan.FH, E, CI, 1.5f);
                BoxCol(1 + f, WALL, s, -CI, -E, y0, y0 + W188Plan.FH, 0f, E, 1.5f);
                SkirtCorr(1 + f, s, y0, -CI, 0f);
            }
            return;
        }
        // muros del núcleo
        for (int f = 0; f < W188Plan.Floors; f++)
        {
            float y0 = f * W188Plan.FH, yT = y0 + W188Plan.FH; int G = 1 + f;
            BoxCol(G, WALL, s, -CI, -E, y0, yT, 0f, CI, 1.5f);              // muro oeste (da al pasillo de la esquina)
            BoxCol(G, CONC, s, -E, 0f, y0, yT, 0f, 0.1f, 2f);               // contra la última habitación del lado contiguo
            BoxCol(G, CONC, s, -0.1f, 0f, y0, yT, 0.1f, E, 2f);             // contra la primera habitación de este lado
            BoxCol(G, CONC, s, -2.7f, -1.35f, y0, yT, StLand, E, 2f);       // núcleo entre tramos
            // muro sur (al pasillo) con los dos huecos
            bool openA = f < W188Plan.Floors - 1, openB = f >= 1;
            float oh = y0 + 2.2f;
            BoxCol(G, WALL, s, -E, StA0, y0, yT, E, CI, 1.5f);
            BoxCol(G, WALL, s, StA1, StB0, y0, yT, E, CI, 1.5f);
            BoxCol(G, WALL, s, StB1, 0f, y0, yT, E, CI, 1.5f);
            if (openA) { BoxCol(G, WALL, s, StA0, StA1, oh, yT, E, CI, 1.5f); Box(G, CONC, s, StA0, StA1, y0 - 0.03f, y0, E, CI, 1f); }
            else BoxCol(G, WALL, s, StA0, StA1, y0, yT, E, CI, 1.5f);
            if (openB) { BoxCol(G, WALL, s, StB0, StB1, oh, yT, E, CI, 1.5f); Box(G, CONC, s, StB0, StB1, y0 - 0.03f, y0, E, CI, 1f); Col(s, StB0, StB1, y0 - 0.25f, y0, E - 0.02f, CI); }
            else BoxCol(G, WALL, s, StB0, StB1, y0, yT, E, CI, 1.5f);
            SkirtCorr(G, s, y0, -CI, StA0); SkirtCorr(G, s, y0, StA1, StB0);
            // marco de los huecos (lado pasillo)
            foreach (bool isA in new[] { true, false })
            {
                if (isA ? !openA : !openB) continue;
                float a = isA ? StA0 : StB0, b = isA ? StA1 : StB1;
                Box(G, FRAME, s, a - 0.06f, a, y0, oh + 0.06f, CI, CI + 0.02f, 1f);
                Box(G, FRAME, s, b, b + 0.06f, y0, oh + 0.06f, CI, CI + 0.02f, 1f);
                Box(G, FRAME, s, a - 0.06f, b + 0.06f, oh, oh + 0.06f, CI, CI + 0.02f, 1f);
            }
            if (f == W188Plan.Floors - 1) continue;
            float T = (E - StLand) / Steps, Rz = StRise / Steps;
            // tramo A: sube desde el pasillo (d = 4) hacia el rellano (d = 1,65)
            for (int k = 0; k < Steps; k++)
            {
                float top = y0 + (k + 1) * Rz, bot = f == 0 ? 0f : top - 0.38f;
                Box(G, CONC, s, StA0, StA1, bot, top, E - (k + 1) * T, E - k * T, 1f);
            }
            // rellano
            float lt = y0 + StRise;
            BoxCol(G, CONC, s, -E, -0.1f, f == 0 ? 0f : lt - 0.25f, lt, 0.1f, StLand, 2f);
            // tramo B: del rellano a la planta de arriba (d de 1,65 a 4)
            for (int k = 0; k < Steps; k++)
            {
                float top = lt + (k + 1) * Rz, bot = f == 0 ? 0f : top - 0.38f;
                Box(1 + Mathf.Min(f + 1, W188Plan.Floors - 1), CONC, s, StB0, StB1, bot, top, StLand + k * T, StLand + (k + 1) * T, 1f);
            }
            if (f == 0) BoxCol(G, CONC, s, StB0, StB1, 0f, StRise, StLand, E, 1f);
            Ramp(s, StA0, StA1, E + 0.12f, y0 - 0.077f, StLand, lt);
            Ramp(s, StB0, StB1, StLand, lt, E, lt + StRise);
            // lámpara del rellano
            Box(G, LAMP, s, -2.3f, -1.7f, lt + 2.05f, lt + 2.12f, 0.1f, 0.16f, 1f);
            Light l = MakePoint(P(s, -2.0f, lt + 1.9f, 0.7f), 4.6f, 1.15f, new Color(1f, 0.86f, 0.66f));
            Lamps.Add(new Lamp { l = l, f0 = f, f1 = f + 1, baseI = l.intensity });
            // barandilla: pasamanos de latón en el núcleo
            Box(G, METAL, s, -2.75f, -2.7f, y0 + 0.9f, y0 + 0.95f, StLand, E, 1f);
        }
    }

    // rampa de colisión (BoxCollider girado) cuya cara de arriba pasa por las aristas de los peldaños
    void Ramp(int s, float u0, float u1, float dA, float yA, float dB, float yB)
    {
        Vector3 a = P(s, (u0 + u1) * 0.5f, yA, dA), b = P(s, (u0 + u1) * 0.5f, yB, dB);
        Vector3 dir = (b - a); float len = dir.magnitude; dir /= len;
        Vector3 right = Vector3.Cross(Vector3.up, dir).normalized; Vector3 up = Vector3.Cross(dir, right).normalized;
        GameObject c = new GameObject("StairRamp"); c.transform.SetParent(colRoot, false);
        c.transform.localRotation = Quaternion.LookRotation(dir, up);
        c.transform.localPosition = (a + b) * 0.5f - up * 0.1f;
        BoxCollider bc = c.AddComponent<BoxCollider>(); bc.size = new Vector3(u1 - u0, 0.2f, len);
        ColliderCount++;
    }

    // ---------------- cubierta: forjado, cornisa y antepecho ----------------
    void BuildRoof()
    {
        float H = W188Plan.Floors * W188Plan.FH;
        for (int s = 0; s < 4; s++)
        {
            float L = W188Plan.Len(s) * W188Plan.M;
            Box(0, CONC, s, -W188Plan.R, L, H, H + 0.3f, 0f, W188Plan.R, 2f);
            Col(s, -W188Plan.R, L, H, H + 0.3f, 0f, W188Plan.R);
            Box(0, CORNICE, s, -0.14f, L, H - 0.02f, H + 0.32f, -0.14f, 0.3f, 1f);
            Box(0, FACADE, s, -0.08f, L, H + 0.32f, H + 0.95f, -0.08f, 0.18f, 2f);
            Box(0, CONC, s, -0.12f, L, H + 0.95f, H + 1.02f, -0.12f, 0.22f, 1f);
            // impostas entre plantas (molduras finas de la foto)
            for (int f = 1; f < W188Plan.Floors; f++)
                Box(0, CORNICE, s, -0.04f, L, f * W188Plan.FH - 0.12f, f * W188Plan.FH - 0.02f, -0.04f, 0.05f, 1f);
        }
    }

    // ---------------- objetos ----------------
    void PickSpots()
    {
        System.Random r = new System.Random(gen.UsedSeed * 389 + 188);
        // llaves: en el suelo, a los pies de la cama de 5 habitaciones que se abren
        foreach (Vector3Int c in plan.KeyRooms)
            KeySpots.Add(levelRoot.TransformPoint(P(c.y, c.z * W188Plan.M + 2.45f, c.x * W188Plan.FH + 0.1f, 1.9f + (float)r.NextDouble() * 0.6f)));
        System.Func<Vector3> corr = () =>
        {
            int s = r.Next(4), f = r.Next(W188Plan.Floors), n = W188Plan.Len(s);
            float u = 0.5f + (float)r.NextDouble() * (n * W188Plan.M - 1f);
            return levelRoot.TransformPoint(P(s, u, f * W188Plan.FH + 0.1f, W188Plan.CorrOut - 0.3f));
        };
        System.Func<Vector3> court = () =>
        {
            float x = W188Plan.R + 1.5f + (float)r.NextDouble() * (W188Plan.NX * W188Plan.M - 3f), z = W188Plan.R + 1.5f + (float)r.NextDouble() * (W188Plan.NZ * W188Plan.M - 3f);
            if (Mathf.Abs(x - W188Plan.Width * 0.5f) < 1.2f) x += 2f;
            return levelRoot.TransformPoint(new Vector3(x, 0.1f, z));
        };
        int extra = Mathf.Max(1, cfg.extraSpots);
        for (int i = 0; i < extra; i++)
        {
            if (i < 3) ExtraSpots.Add(court());
            else if (i < 7) ExtraSpots.Add(corr());
            else
            {
                Vector3Int c = plan.Rooms[r.Next(plan.Rooms.Count)];
                ExtraSpots.Add(levelRoot.TransformPoint(P(c.y, c.z * W188Plan.M + 2.6f, c.x * W188Plan.FH + 0.1f, 1.3f)));
            }
        }
        for (int i = 0; i < cfg.batteries; i++) BatterySpots.Add(i == 0 ? court() : corr());
        // inicio: delante del ascensor, en el patio
        Vector3Int lc = W188Plan.LiftCell;
        StartLocal = P(lc.y, W188Plan.UC(lc.z), 0f, -0.02f);
        StartYaw = W188Plan.SideYaw(lc.y);   // el lado N mira al sur (yaw 180): de espaldas a la fachada
    }

    // ---------------- salida ----------------
    public bool AtExit(Vector3 world)
    {
        Vector3 lp = levelRoot.InverseTransformPoint(world);
        Vector3 q = W188Plan.FromLevel(plan.exitS, lp);
        float y0 = plan.exitF * W188Plan.FH, u0 = plan.exitI * W188Plan.M;
        return Mathf.Abs(q.y - y0) < 1.0f && q.z > W188Plan.FacadeT - 0.05f && q.z < 1.95f && q.x > u0 + 0.05f && q.x < u0 + W188Plan.M - 0.05f;
    }
    public Vector3 ExitRoomWorld(float d, float du)
    {
        return levelRoot.TransformPoint(P(plan.exitS, W188Plan.UC(plan.exitI) + du, plan.exitF * W188Plan.FH + 0.1f, d));
    }
    public Vector3 ExitDoorWorld(float d) { return levelRoot.TransformPoint(P(plan.exitS, W188Plan.DoorU(plan.exitI), plan.exitF * W188Plan.FH + 0.1f, d)); }
    public float FacadeYaw(int s) { return W188Plan.SideYaw(s); }
    public Vector3 CellWorld(int f, int s, int i, float du, float d) { return levelRoot.TransformPoint(P(s, W188Plan.UC(i) + du, f * W188Plan.FH + 0.1f, d)); }
    public Vector3 DoorWorld(int f, int s, int i, float d) { return levelRoot.TransformPoint(P(s, W188Plan.DoorU(i), f * W188Plan.FH + 0.1f, d)); }
    // yaw que mira desde el pasillo hacia el patio (hacia -d) en el lado s
    public static float YawToCourt(int s) { Vector3 v = -Dd(s); return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }
    public static float YawFromCourt(int s) { Vector3 v = Dd(s); return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }
    public static Vector3 LevelDir(int s, float du, float dd) { return Ud(s) * du + Dd(s) * dd; }

    // ---------------- cada fotograma ----------------
    void Update()
    {
        if (ExitLeaf != null)
        {
            bool open = ExitLock.Active == null || ExitLock.Active.Unlocked;
            exitOpen = Mathf.MoveTowards(exitOpen, open ? 1f : 0f, Time.deltaTime * 0.9f);
            ExitAngle = -100f * Mathf.SmoothStep(0f, 1f, exitOpen);
            ExitLeaf.localRotation = Quaternion.Euler(0f, W188Plan.SideYaw(plan.exitS) + ExitAngle, 0f);
            if (exitBlocker != null) exitBlocker.enabled = exitOpen < 0.6f;
        }
        TickDoors();
    }

    // puerta de salida (la prueba la llave aunque el test de salida sea propio) y manillas de las cerradas
    void TickDoors()
    {
        if (rattleCd > 0f) rattleCd -= Time.deltaTime;
        if (toastCd > 0f) toastCd -= Time.deltaTime;
        FirstPersonController p = FirstPersonController.LocalInstance;
        GameManager gm = GameManager.Instance;
        if (p == null || (gm != null && gm.State != GameManager.GameState.Playing)) return;
        Vector3 pos = p.transform.position;
        Vector3 lp = levelRoot.InverseTransformPoint(pos);
        int pf = W188Plan.FloorOf(lp.y);
        if (ExitLock.Active != null && !ExitLock.Active.Unlocked && pf == plan.exitF)
        {
            Vector3 q = W188Plan.FromLevel(plan.exitS, lp);
            if (Mathf.Abs(q.x - W188Plan.DoorU(plan.exitI)) < 0.9f && q.z > W188Plan.CorrIn - 0.05f && q.z < W188Plan.CorrIn + 1.1f)
                ExitLock.Active.AtDoor(Inventory.Instance);
        }
        if (rattleCd > 0f) return;
        Vector3 fwd = p.transform.forward; fwd.y = 0f;
        foreach (LockedDoor d in Locked)
        {
            if (d.f != pf) continue;
            Vector3 dp = pos - d.pos; dp.y = 0f;
            if (dp.magnitude > 0.85f || Vector3.Dot(fwd.normalized, -d.outward) < 0.6f) continue;
            rattleCd = 2.2f; Rattles++;
            rattleSrc.transform.position = d.pos + Vector3.up * 1f;
            rattleSrc.PlayOneShot(rattleClip, AudioMix.A(0.8f));
            if (toastCd <= 0f && rattleToasts < 4 && gm != null)
            {
                gm.ShowToast(d.view ? "La manilla no cede. Al otro lado suena otro sitio." : "Cerrada. La manilla no cede.");
                toastCd = 8f; rattleToasts++;
            }
            break;
        }
    }

    // luz ambiente según dónde está la cámara (patio a cielo abierto o dentro), luces cercanas de la planta y grupos visibles
    void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        if (probe != null && probeFrames > 0 && --probeFrames == 0) probe.RenderProbe();
        Vector3 lp = levelRoot.InverseTransformPoint(cam.transform.position);
        float inset = Mathf.Min(Mathf.Min(lp.x - W188Plan.R, W188Plan.R + W188Plan.NX * W188Plan.M - lp.x), Mathf.Min(lp.z - W188Plan.R, W188Plan.R + W188Plan.NZ * W188Plan.M - lp.z));
        float target = Mathf.Clamp01((inset + 0.2f) / 1.2f);
        CourtK = Mathf.MoveTowards(CourtK, target, Time.deltaTime * 2.5f);
        int pf = W188Plan.FloorOf(lp.y); PlayerFloor = pf;
        // dentro de una habitación entra la luz del día por su ventana: ambiente intermedio (en el pasillo, solo lámparas)
        float back = -inset; float roomT = back > 0.05f && back < W188Plan.RoomEnd - 0.1f ? Mathf.Lerp(0.42f, 0.18f, back / W188Plan.RoomEnd) : 0f;
        RoomK = Mathf.MoveTowards(RoomK, roomT, Time.deltaTime * 2.5f);
        ApplyAmbient(Mathf.Max(CourtK, RoomK));
        // grupos: la planta del jugador y las contiguas; en el patio, la baja (se ve por el vestíbulo)
        for (int f = 0; f < FloorGroups.Count; f++)
        {
            bool on = target > 0.5f ? f == 0 : Mathf.Abs(f - pf) <= 1;
            if (FloorGroups[f].activeSelf != on) FloorGroups[f].SetActive(on);
        }
        // luces: las 8 más cercanas de la planta (más las fijas de los cuartos que se ven desde el patio)
        int budget = cfg != null ? Mathf.Max(4, cfg.maxRealtimeLights) : 8;
        Vector3 cp = cam.transform.position;
        tmpLamps.Clear();
        foreach (Lamp L in Lamps)
        {
            if (L.l == null) continue;
            if (L.always) { if (!L.l.enabled) L.l.enabled = true; continue; }
            bool floorOk = target > 0.5f ? false : (pf >= L.f0 && pf <= L.f1);
            if (!floorOk) { if (L.l.enabled) L.l.enabled = false; continue; }
            tmpLamps.Add(L);
        }
        tmpLamps.Sort((a, b) => (a.l.transform.position - cp).sqrMagnitude.CompareTo((b.l.transform.position - cp).sqrMagnitude));
        int on2 = 0;
        for (int k = 0; k < tmpLamps.Count; k++) { bool e = k < budget; if (tmpLamps[k].l.enabled != e) tmpLamps[k].l.enabled = e; if (e) on2++; }
        ActiveLamps = on2;
    }
    readonly List<Lamp> tmpLamps = new List<Lamp>();

    public static readonly Color SkyAmb = new Color(0.56f, 0.59f, 0.64f), EqAmb = new Color(0.46f, 0.47f, 0.48f), GroundAmb = new Color(0.22f, 0.21f, 0.20f);
    void ApplyAmbient(float k)
    {
        Color inA = cfg != null ? cfg.ambient : new Color(0.075f, 0.068f, 0.06f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = Color.Lerp(inA * 1.15f, SkyAmb, k);
        RenderSettings.ambientEquatorColor = Color.Lerp(inA, EqAmb, k);
        RenderSettings.ambientGroundColor = Color.Lerp(inA * 0.8f, GroundAmb, k);
        RenderSettings.reflectionIntensity = Mathf.Lerp(0.15f, 0.75f, k);
    }

    // huella para el multijugador (además de la del generador): tipos de todas las ventanas
    public int PlanHash()
    {
        int h = 17;
        for (int f = 0; f < W188Plan.Floors; f++) for (int s = 0; s < 4; s++) for (int i = 0; i < W188Plan.Len(s); i++)
            h = h * 31 + (int)plan.kind[f, s, i] * 7 + plan.variant[f, s, i];
        return h;
    }
}

// puerta de habitación del Nivel 188: se abre sola al acercarse alguien de esa misma planta (como las del hotel)
public class W188Door : MonoBehaviour
{
    public Vector3 center; public float floorY;
    public float openAngle = -95f, radius = 1.8f, holdTime = 3.5f, speed = 3.0f;
    public Collider blocker;
    public float Angle { get; private set; }
    public bool IsOpen { get { return Mathf.Abs(Angle) > 30f; } }
    public static int OpenCount;
    float target, hold, tick; Quaternion closedRot;
    void Start() { closedRot = transform.localRotation; tick = Random.value * 0.1f; }
    void Update()
    {
        float dt = Time.deltaTime; tick -= dt;
        if (tick <= 0f) { tick = 0.1f; if (Near()) hold = holdTime; }
        hold -= dt;
        target = hold > 0f ? openAngle : 0f;
        float prev = Angle;
        Angle = Mathf.MoveTowards(Angle, target, Mathf.Abs(openAngle) * speed * dt * (target == 0f ? 0.45f : 1f));
        if (Angle != prev) transform.localRotation = closedRot * Quaternion.Euler(0f, Angle, 0f);
        if (blocker != null) blocker.enabled = Mathf.Abs(Angle) < 4f && target == 0f;
        if (Mathf.Abs(Angle) > 30f && Mathf.Abs(prev) <= 30f) OpenCount++;
    }
    bool Near()
    {
        FirstPersonController p = FirstPersonController.LocalInstance;
        if (p != null && In(p.transform.position)) return true;
        foreach (RemotePlayer rp in RemotePlayer.All) if (rp != null && In(rp.transform.position)) return true;
        return false;
    }
    bool In(Vector3 q) { float dy = q.y - floorY; if (dy < -1.0f || dy > 1.6f) return false; float dx = q.x - center.x, dz = q.z - center.z; return dx * dx + dz * dz < radius * radius; }
    public void ForceOpen(float seconds) { hold = seconds; }
}
