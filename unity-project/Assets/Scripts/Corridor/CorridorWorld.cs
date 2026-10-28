using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Nivel 7460 — El pasillo de las puertas. Un pasillo recto de 340 m (100 módulos de 3,4 m), simétrico, con paredes gris
// pizarra, zócalos y molduras blancas, una puerta blanca a cada lado de cada módulo y un plafón blanco en el techo.
// El módulo, la hoja de puerta y la habitación lateral vienen de Blender (Tools/c7460/build_corridor.py) con la luz horneada
// en Cycles (irradiancia en UV2, EXR); el suelo de resina refleja con un reflejo planar (CorridorPlanar) y la especular de
// paredes y puertas sale de una sonda de reflexión con proyección de caja que se mueve de módulo en módulo con el jugador
// (el pasillo es periódico, así que la misma captura vale en todos). Sin luces en tiempo real (solo la linterna).
// Puertas: cerradas (la manilla no cede), habitación vacía (se abren al acercarse), tapiadas (un metro y ladrillo) y una
// salida (ExitLock). Colisión con cajas. Todo determinista con la semilla (multijugador: misma huella).
public class CorridorWorld : MonoBehaviour
{
    public static CorridorWorld Active { get; private set; }
    // medidas del módulo de Blender (corridor.json)
    public const float CS = 2.4f, HW = 1.55f, H = 3.0f, DW = 0.92f, DH = 2.12f, JT = 0.16f, XF = 1.58f, XB = 1.62f, XR = 1.71f, RD = 3.0f, RW = 2.2f, RH = 2.8f;   // v2: una puerta cada 2,4 m (como la foto)
    // v3: muebles de la habitación (x desde el eje del pasillo, z desde el centro de la puerta): mesa, silla, archivador, cajas
    public static readonly Vector4[] Furn = { new Vector4(4.05f, 4.68f, -0.62f, 0.52f), new Vector4(3.52f, 3.97f, -0.30f, 0.12f), new Vector4(4.175f, 4.69f, 0.63f, 1.08f), new Vector4(3.26f, 4.16f, -1.07f, -0.67f) };
    public static readonly float[] FurnH = { 0.74f, 0.92f, 1.02f, 0.64f };
    public const float Gap = 0.003f, HandleY = 1.0f, HandleZ = 0.39f;
    public const int ChunkModules = 10;
    public const float BakeExposure = 0.32f, RoomExposure = 0.95f;   // v2: módulo más denso (irradiancia mediana 3,5 en vez de 2,1)   // irradiancia horneada -> pantalla (con el ACES del PostStack)
    public const float PanelEmission = 2.4f;   // v3: plafones nítidos, sin halo (la foto no tiene bloom)
    public const float ProbeK = 0f;   // v3: sin especular de sonda (en el ejecutable en -batchmode/D3D11 la sonda sale negra y en el editor no: mismo aspecto en todas partes)
    public const float WallGloss = 0.4f, TrimGloss = 0.7f;   // v3: lacado satinado (sin chispas del plafón en el relieve)
      // v3: pintura mate (antes casi todo el brillo de la pared venía de la sonda)
    public static readonly Color WallTint = new Color(1.49f, 1.43f, 1.42f), RoomWallTint = new Color(0.95f, 0.95f, 0.97f), FloorTint = new Color(1.27f, 1.29f, 1.38f), CeilTint = new Color(2.47f, 2.50f, 2.58f);   // v3: medidos frente a la foto (mediana de píxeles de pared/techo/suelo)
    public const float WallBump = 0.06f, TrimMinLM = 0.95f, LeafMinLM = 2.4f;   // pizarra menos azul (como la foto) y resina algo más clara
    public const float FarClip = 420f;
    public static bool HasAssets { get { return Resources.Load<TextAsset>("C7460/C7460Module") != null && Shader.Find("Backrooms/CorridorBaked") != null; } }

    public BackroomsGenerator gen; public LevelConfig cfg; public CorridorPlan plan; public Transform levelRoot;
    public Vector3 StartLocal { get; private set; }
    public Transform ExitTransform { get; private set; }
    public readonly List<Vector3> BatterySpots = new List<Vector3>(), ExtraSpots = new List<Vector3>(), KeySpots = new List<Vector3>();
    public readonly List<HotelDoorLeaf> OpenableDoors = new List<HotelDoorLeaf>();
    public Transform ExitLeaf { get; private set; }
    public int LockedBlocks { get; private set; }
    private BoxCollider exitBlock;
    public Collider ExitBlock { get { return exitBlock; } }
    public float ExitAngle { get; private set; }
    public int Rattles { get; private set; }
    public int ChunkCount { get; private set; }
    public int RoomCount { get; private set; }
    public int LinkCount { get; private set; }
    public float Length { get { return plan.rows * CS; } }
    public float CenterX { get { return (plan.Col + 0.5f) * CS; } }
    public ReflectionProbe Probe { get; private set; }
    public int ProbeRenders { get; private set; }
    public Material FloorMat { get; private set; }
    public Material PanelMat { get; private set; }

    private System.Action<Object> track;
    private Material[] modMats, roomMats; private Material metalMat, emitMat, brickMat, endMat, glowMat;
    private AudioSource rattleSrc; private AudioClip rattleClip;
    private float rattleCd, toastCd; private int toasts;
    private float exitOpen;

    struct Loaded { public Vector3[] v, n; public Vector2[] a, b; public List<int[]> subs; public List<int> ids; }

    static Loaded Load(string res)
    {
        TextAsset ta = Resources.Load<TextAsset>(res);
        Loaded L = new Loaded { subs = new List<int[]>(), ids = new List<int>() };
        using (var br = new System.IO.BinaryReader(new System.IO.MemoryStream(ta.bytes)))
        {
            int nv = br.ReadInt32(), ns = br.ReadInt32();
            L.v = new Vector3[nv]; L.n = new Vector3[nv]; L.a = new Vector2[nv]; L.b = new Vector2[nv];
            for (int i = 0; i < nv; i++)
            {
                L.v[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                L.n[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                L.a[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                L.b[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            }
            for (int k = 0; k < ns; k++)
            {
                int c = br.ReadInt32(); int[] idx = new int[c];
                for (int i = 0; i < c; i++) idx[i] = br.ReadInt32();
                if (c > 0) { L.subs.Add(idx); L.ids.Add(k); }
            }
        }
        return L;
    }

    // submallas de Blender (build_corridor.py)
    public const int MFloor = 0, MWall = 1, MCeil = 2, MTrim = 3, MDoor = 4, MMetal = 5, MEmit = 6, MBrick = 7, MWood = 8, MPaint = 9, MCard = 10, MFabric = 11;
    static readonly float[] Tile = { 6.8f, 2.0f, 2.0f, 1f, 1f, 1f, 1f, 2f };

    // combinador de mallas con transformación (espejo en x opcional) y desplazamiento de UV0 por material
    class Combiner
    {
        public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<Vector2> a = new List<Vector2>(), b = new List<Vector2>();
        public readonly Dictionary<int, List<int>> subs = new Dictionary<int, List<int>>();
        public void Add(Loaded L, Vector3 off, bool mirror, float alongShift, Vector3 pivot = default(Vector3))
        {
            int baseV = v.Count;
            int[] mat = new int[L.v.Length];
            for (int s = 0; s < L.subs.Count; s++) foreach (int i in L.subs[s]) mat[i] = L.ids[s];
            for (int i = 0; i < L.v.Length; i++)
            {
                Vector3 p = L.v[i] + pivot, nn = L.n[i];
                if (mirror) { p.x = -p.x; nn.x = -nn.x; }
                v.Add(p + off); n.Add(nn);
                Vector2 uv = L.a[i]; int m = mat[i];
                if (m == MFloor || m == MCeil) uv.y += alongShift / Tile[m];
                else if (m == MWall) uv.x += alongShift / Tile[m];
                a.Add(uv); b.Add(L.b[i]);
            }
            for (int s = 0; s < L.subs.Count; s++)
            {
                if (!subs.TryGetValue(L.ids[s], out List<int> dst)) { dst = new List<int>(); subs[L.ids[s]] = dst; }
                int[] src = L.subs[s];
                for (int t = 0; t < src.Length; t += 3)
                {
                    if (mirror) { dst.Add(baseV + src[t]); dst.Add(baseV + src[t + 2]); dst.Add(baseV + src[t + 1]); }
                    else { dst.Add(baseV + src[t]); dst.Add(baseV + src[t + 1]); dst.Add(baseV + src[t + 2]); }
                }
            }
        }
        public Mesh Build(string name, out List<int> ids)
        {
            Mesh m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, a); m.SetUVs(1, b);
            ids = new List<int>(subs.Keys); ids.Sort();
            m.subMeshCount = ids.Count;
            for (int k = 0; k < ids.Count; k++) m.SetTriangles(subs[ids[k]], k);
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }
    }

    Material Baked(string tex, Texture lm, float exposure, float gloss, float planar, Color tint)
    {
        Material m = new Material(Shader.Find("Backrooms/CorridorBaked")) { name = "C7460_" + tex };
        m.SetTexture("_MainTex", Resources.Load<Texture2D>("Textures/C7460/" + tex + "_a"));
        m.SetTexture("_BumpMap", Resources.Load<Texture2D>("Textures/C7460/" + tex + "_n"));
        m.SetTexture("_MaskMap", Resources.Load<Texture2D>("Textures/C7460/" + tex + "_m"));
        m.SetTexture("_LightMap", lm); m.SetFloat("_LMScale", exposure); m.SetFloat("_Glossiness", gloss); m.SetFloat("_PlanarK", planar);
        m.SetColor("_Color", tint); m.SetFloat("_ProbeK", ProbeK);
        track(m); return m;
    }

    Material[] MatsFor(List<int> ids, Material[] set)
    {
        Material[] r = new Material[ids.Count];
        for (int k = 0; k < ids.Count; k++)
        {
            int id = ids[k];
            r[k] = id == MMetal ? metalMat : id == MEmit ? emitMat : id == MBrick ? brickMat : set[Mathf.Clamp(id, 0, set.Length - 1)];
        }
        return r;
    }

    GameObject MeshObj(string name, Transform parent, Mesh mesh, Material[] mats, Vector3 localPos, Vector3 scale)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false); g.transform.localPosition = localPos; g.transform.localScale = scale;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        return g;
    }

    BoxCollider Box(string name, Vector3 center, Vector3 size)
    {
        GameObject g = new GameObject(name); g.transform.SetParent(cur != null ? cur : transform, false); g.transform.localPosition = center;
        BoxCollider b = g.AddComponent<BoxCollider>(); b.size = size; return b;
    }

    public static CorridorWorld Build(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot, CorridorPlan plan, System.Action<Object> track)
    {
        GameObject g = new GameObject("Corridor7460");
        g.transform.SetParent(levelRoot, false);
        CorridorWorld w = g.AddComponent<CorridorWorld>();
        w.gen = gen; w.cfg = cfg; w.plan = plan; w.levelRoot = levelRoot; w.track = track;
        Active = w;
        w.BuildAll();
        return w;
    }

    private void OnDestroy() { if (probeCube != null) { probeCube.Release(); Destroy(probeCube); } if (probeCam != null) Destroy(probeCam.gameObject); if (Active == this) Active = null; if (rattleClip != null) ProceduralAssets.SafeDestroy(rattleClip); }

    void BuildAll()
    {
        Texture lm = Resources.Load<Texture2D>("C7460/C7460_LM"), lmr = Resources.Load<Texture2D>("C7460/C7460Room_LM");
        Color white = Color.white;
        modMats = new Material[] {
            Baked("C7460Floor", lm, BakeExposure, 1f, 1f, FloorTint), Baked("C7460Wall", lm, BakeExposure, WallGloss, 0f, WallTint),
            Baked("C7460Ceil", lm, BakeExposure, 1f, 0f, CeilTint), Baked("C7460Trim", lm, BakeExposure, TrimGloss, 0f, white),
            Baked("C7460Trim", lm, BakeExposure, TrimGloss, 0f, white) };
        FloorMat = modMats[0];
        modMats[1].SetFloat("_BumpScale", WallBump);                                     // pintura mate lisa (como la foto)
        modMats[3].SetFloat("_LMMin", TrimMinLM); modMats[4].SetFloat("_LMMin", TrimMinLM);   // jambas y cantos de hoja sin manchas negras
        roomMats = new Material[] {
            Baked("C7460Floor", lmr, RoomExposure, 1f, 1f, FloorTint), Baked("C7460Wall", lmr, RoomExposure, WallGloss, 0f, RoomWallTint),
            Baked("C7460Ceil", lmr, RoomExposure, 1f, 0f, CeilTint), Baked("C7460Trim", lmr, RoomExposure, 1f, 0f, white),
            Baked("C7460Trim", lmr, RoomExposure, 1.05f, 0f, white), null, null, null,
            Baked("C7460Wood", lmr, RoomExposure, 1f, 0f, white), Baked("C7460Paint", lmr, RoomExposure, 1f, 0f, white),
            Baked("C7460Card", lmr, RoomExposure, 1f, 0f, white), Baked("C7460Fabric", lmr, RoomExposure, 1f, 0f, white) };   // v3: muebles horneados con la habitación
        roomMats[1].SetFloat("_BumpScale", WallBump);
        metalMat = ProceduralAssets.CreateMaterial(new Color(0.80f, 0.80f, 0.82f), null, Vector2.one, 0.86f); metalMat.SetFloat("_Metallic", 1f); metalMat.name = "C7460_Chrome"; track(metalMat);
        emitMat = ProceduralAssets.CreateMaterial(new Color(0.95f, 0.95f, 0.95f), null, Vector2.one, 0.2f); ProceduralAssets.SetEmission(emitMat, new Color(1f, 0.99f, 0.97f) * PanelEmission); emitMat.name = "C7460_Panel"; track(emitMat);
        PanelMat = emitMat;
        // superficies sin hornear (extremos, nichos tapiados): irradiancia constante en una textura 1×1 (mismo shader)
        Texture2D flat = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true) { name = "C7460_FlatLM" };
        Color fl = new Color(1.55f, 1.56f, 1.60f, 1f); flat.SetPixels(new[] { fl, fl, fl, fl }); flat.Apply(false, false); track(flat);
        // el nicho tapiado recibe la luz del pasillo (antes casi negro)
        Texture2D dim = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true) { name = "C7460_NicheLM" };
        Color dl = new Color(1.15f, 1.16f, 1.19f, 1f); dim.SetPixels(new[] { dl, dl, dl, dl }); dim.Apply(false, false); track(dim);
        endMat = Baked("C7460Wall", flat, BakeExposure, 1f, 0f, WallTint);
        // manillas y bisagras: acero satinado con irradiancia constante (el cromo de verdad necesitaba la sonda y salía negro en el ejecutable)
        metalMat = Baked("C7460Trim", flat, BakeExposure, 0.9f, 0f, new Color(0.70f, 0.71f, 0.74f)); metalMat.name = "C7460_Steel";
        brickMat = Baked("C7460Brick", dim, BakeExposure, 1f, 0f, white);
        Material nicheTrim = Baked("C7460Trim", dim, BakeExposure, 1f, 0f, white);
        Material endTrim = Baked("C7460Trim", flat, BakeExposure, 1f, 0f, white);
        glowMat = ProceduralAssets.CreateMaterial(Color.white, null, Vector2.one, 0f); ProceduralAssets.SetEmission(glowMat, new Color(1f, 0.98f, 0.94f) * 1.9f); glowMat.name = "C7460_ExitGlow"; track(glowMat);

        mod = Load("C7460/C7460Module"); Loaded leaf = Load("C7460/C7460Leaf"), room = Load("C7460/C7460Room");
        leafL = leaf; hinge = new Vector3(XB, 0f, -DW * 0.5f + Gap);
        float cx = CenterX;

        // --- hojas que se mueven (enlaces, tapiadas, salida) ---
        Combiner lc = new Combiner(); lc.Add(leaf, Vector3.zero, false, 0f);
        leafMesh = lc.Build("C7460Leaf", out List<int> leafIds); track(leafMesh);
        // v3: la cara de la hoja que da a la habitación se horneó a oscuras (puerta cerrada): mínimo de irradiancia propio, como la luz que entra del pasillo
        Material leafDoor = new Material(modMats[4]) { name = "C7460_LeafDoor" }; leafDoor.SetFloat("_LMMin", LeafMinLM); track(leafDoor);
        Material leafTrim = new Material(modMats[3]) { name = "C7460_LeafTrim" }; leafTrim.SetFloat("_LMMin", LeafMinLM); track(leafTrim);
        leafMats = MatsFor(leafIds, new[] { modMats[0], modMats[1], modMats[2], leafTrim, leafDoor });
        Combiner rc = new Combiner(); rc.Add(room, Vector3.zero, false, 0f);
        roomMesh = rc.Build("C7460Room", out List<int> roomIds); track(roomMesh);
        rMats = MatsFor(roomIds, roomMats);
        this.nicheTrim = nicheTrim; this.endTrim = endTrim;

        // v7.10.2 (t500u): un laberinto: cada pasillo (largo o transversal) se construye en su propio marco, donde corre a lo
        // largo de +z en x = CenterX (el pasillo 0 coincide con el marco del nivel, como antes)
        foreach (CorridorPlan.Run def in plan.runs)
        {
            RunView rv = new RunView { def = def };
            rv.F = new GameObject((def.cross ? "Cross" : "Main") + def.id).transform; rv.F.SetParent(transform, false);
            if (!def.cross) rv.F.localPosition = new Vector3(def.main * Spacing, 0f, 0f);
            else
            {
                rv.F.localPosition = new Vector3(def.a * Spacing + cx + CrossStart, 0f, (def.row + 0.5f) * CS + cx);
                rv.F.localRotation = Quaternion.Euler(0f, 90f, 0f);
            }
            Runs.Add(rv);
        }
        foreach (RunView rv in Runs) BuildRun(rv);
        cur = transform;
        // enlaces: hojas de cada transversal en sus dos extremos (para la visibilidad por portales)
        foreach (RunView rv in Runs)
        {
            if (!rv.def.cross) continue;
            rv.leafA = LeafAt(rv.def.a, CorridorPlan.Right, rv.def.row); rv.leafB = LeafAt(rv.def.b, CorridorPlan.Left, rv.def.row);
            Runs[rv.def.a].links.Add(rv); if (!rv.def.deadEnd) Runs[rv.def.b].links.Add(rv);
        }
        foreach (RunView rv in Runs)
        {
            foreach (Renderer rr in rv.F.GetComponentsInChildren<Renderer>(true)) if (rr.GetComponentInParent<ExitDoorCue>() == null) rv.rends.Add(rr);
        }

        // --- sonido de las manillas ---
        GameObject ra = new GameObject("HandleRattle"); ra.transform.SetParent(transform, false);
        rattleSrc = ra.AddComponent<AudioSource>(); rattleSrc.playOnAwake = false; rattleSrc.spatialBlend = 1f; rattleSrc.minDistance = 1.2f; rattleSrc.maxDistance = 14f; rattleSrc.rolloffMode = AudioRolloffMode.Linear; rattleSrc.dopplerLevel = 0f;
        rattleClip = ProceduralAudio.CreateLockRattle();

        StartLocal = new Vector3(cx, 0f, 0f);
        PickSpots();
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) SetupReflections();
    }

    // ---------------- v7.10.2: pasillos del laberinto ----------------
    public const float Spacing = CorridorPlan.Spacing * CS;                          // 28,8 m entre pasillos largos
    public const float CrossStart = (CorridorPlan.Spacing - CorridorPlan.CrossModules) * CS * 0.5f;   // 2,4 m: del eje del pasillo al arranque del transversal
    public class RunView
    {
        public CorridorPlan.Run def; public Transform F;
        public readonly List<Renderer> rends = new List<Renderer>();
        public readonly List<RunView> links = new List<RunView>();   // transversales que salen de este pasillo largo
        public HotelDoorLeaf leafA, leafB;                             // transversal: hoja en el pasillo a (derecha) y en el b (izquierda)
        public bool visible = true, shown = true;
        public float Length { get { return def.rows * CS; } }
    }
    public readonly List<RunView> Runs = new List<RunView>();
    public int CurrentRun { get; private set; }
    public int VisibleRuns { get; private set; }
    public int TotalModules { get { int n = 0; foreach (CorridorPlan.Run r in plan.runs) n += r.rows; return n; } }
    public static bool CullForTests = true;
    private Loaded mod, leafL; private Vector3 hinge; private Mesh leafMesh, roomMesh; private Material[] leafMats, rMats; private Material nicheTrim, endTrim;
    private Transform cur;
    private readonly Dictionary<long, HotelDoorLeaf> leafAt = new Dictionary<long, HotelDoorLeaf>();
    HotelDoorLeaf LeafAt(int run, int side, int row) { HotelDoorLeaf l; return leafAt.TryGetValue(((long)run * 4 + side) * 100000 + row, out l) ? l : null; }

    void BuildRun(RunView rv)
    {
        CorridorPlan.Run def = rv.def; cur = rv.F;
        float cx = CenterX; int rows = def.rows; float L = rows * CS;
        // --- tramos de 10 módulos (las hojas cerradas con llave van dentro de la malla del tramo) ---
        Transform vis = new GameObject("Visual").transform; vis.SetParent(rv.F, false);
        for (int c0 = 0; c0 < rows; c0 += ChunkModules)
        {
            Combiner cb = new Combiner();
            float zc0 = (c0 + ChunkModules * 0.5f) * CS;
            for (int k = c0; k < Mathf.Min(rows, c0 + ChunkModules); k++)
            {
                float zc = (k + 0.5f) * CS;
                Vector3 off = new Vector3(0f, 0f, zc - zc0);
                cb.Add(mod, off, false, k * CS);
                for (int s = 0; s < 2; s++)
                    if (def.doors[s, k] == CorridorPlan.Door.Locked) cb.Add(leafL, off, s == CorridorPlan.Left, 0f, hinge);
            }
            Mesh m = cb.Build("C7460Chunk" + def.id + "_" + c0, out List<int> ids); track(m);
            MeshObj("Chunk" + c0, vis, m, MatsFor(ids, modMats), new Vector3(cx, 0f, zc0), Vector3.one);
            ChunkCount++;
        }
        Transform doorsRoot = new GameObject("Doors").transform; doorsRoot.SetParent(rv.F, false);
        for (int k = 0; k < rows; k++)
            for (int s = 0; s < 2; s++)
            {
                CorridorPlan.Door d = def.doors[s, k];
                float zc = (k + 0.5f) * CS, sg = s == CorridorPlan.Left ? -1f : 1f;
                if (d == CorridorPlan.Door.Locked)
                {   // v7.10.1 (t497u): las puertas cerradas son sólidas (tan gruesas como el muro)
                    Box("DoorLocked_" + s + "_" + k, new Vector3(cx + sg * (HW + 0.25f), DH * 0.5f, zc), new Vector3(0.5f, DH + 0.02f, DW + 0.04f));
                    LockedBlocks++;
                    continue;
                }
                Transform side = new GameObject((d == CorridorPlan.Door.Exit ? "ExitDoorLeaf" : d == CorridorPlan.Door.Link ? "LinkDoor" : d == CorridorPlan.Door.Room ? "RoomDoor" : "BrickDoor") + "_" + s + "_" + k).transform;
                side.SetParent(doorsRoot, false); side.localPosition = new Vector3(cx, 0f, zc); side.localScale = new Vector3(sg, 1f, 1f);
                Transform piv = new GameObject("Pivot").transform; piv.SetParent(side, false); piv.localPosition = hinge;
                MeshObj("Leaf", piv, leafMesh, leafMats, Vector3.zero, Vector3.one);
                float xw = cx + sg * (HW + JT * 0.5f);
                BoxCollider block = Box("DoorBlock_" + s + "_" + k, new Vector3(xw, DH * 0.5f, zc), new Vector3(JT, DH, DW));
                if (d == CorridorPlan.Door.Link)
                {   // v7.10.2: la puerta se abre al acercarte y da a un pasillo transversal largo (un paso por el grueso del muro)
                    float nd = CrossStart - XR;
                    Transform tu = new GameObject("Tunnel").transform; tu.SetParent(side, false);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "TunnelS", tu, new Vector3(XR + nd * 0.5f, DH * 0.5f, -DW * 0.5f), new Vector3(nd, DH, 1f), nicheTrim, false).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "TunnelN", tu, new Vector3(XR + nd * 0.5f, DH * 0.5f, DW * 0.5f), new Vector3(nd, DH, 1f), nicheTrim, false).transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "TunnelTop", tu, new Vector3(XR + nd * 0.5f, DH, 0f), new Vector3(nd, DW, 1f), nicheTrim, false).transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "TunnelFloor", tu, new Vector3(XR + nd * 0.5f, 0.003f, 0f), new Vector3(nd, DW, 1f), nicheTrim, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    HotelDoorLeaf hl = piv.gameObject.AddComponent<HotelDoorLeaf>();
                    hl.center = rv.F.TransformPoint(new Vector3(cx + sg * (HW + JT * 0.5f), 0f, zc)); hl.openAngle = 98f; hl.radius = 1.9f; hl.holdTime = 4f; hl.blocker = block;
                    OpenableDoors.Add(hl); leafAt[((long)def.id * 4 + s) * 100000 + k] = hl;
                    float tx = cx + sg * (HW + 0.5f + (CrossStart - HW - 0.5f) * 0.5f), tl = CrossStart - HW - 0.5f + 0.15f;
                    Box("TunnelWS_" + s + "_" + k, new Vector3(tx, H * 0.5f, zc - DW * 0.5f - 0.15f), new Vector3(tl, H, 0.3f));
                    Box("TunnelWN_" + s + "_" + k, new Vector3(tx, H * 0.5f, zc + DW * 0.5f + 0.15f), new Vector3(tl, H, 0.3f));
                    Box("TunnelTop_" + s + "_" + k, new Vector3(tx, (DH + H) * 0.5f, zc), new Vector3(tl, H - DH, DW));
                    LinkCount++;
                }
                else if (d == CorridorPlan.Door.Bricked)
                {   // un metro de hueco y un muro de ladrillo pintado (la puerta no lleva a ninguna parte)
                    float nd = 1.0f;
                    Transform ni = new GameObject("Niche").transform; ni.SetParent(side, false);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "Bricks", ni, new Vector3(XR + nd, DH * 0.5f + 0.02f, 0f), new Vector3(DW + 0.04f, DH + 0.04f, 1f), brickMat, false).transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "NicheS", ni, new Vector3(XR + nd * 0.5f, DH * 0.5f, -DW * 0.5f), new Vector3(nd, DH, 1f), nicheTrim, false).transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "NicheN", ni, new Vector3(XR + nd * 0.5f, DH * 0.5f, DW * 0.5f), new Vector3(nd, DH, 1f), nicheTrim, false).transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "NicheTop", ni, new Vector3(XR + nd * 0.5f, DH, 0f), new Vector3(nd, DW, 1f), nicheTrim, false).transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "NicheFloor", ni, new Vector3(XR + nd * 0.5f, 0.003f, 0f), new Vector3(nd, DW, 1f), nicheTrim, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    HotelDoorLeaf hl = piv.gameObject.AddComponent<HotelDoorLeaf>();
                    hl.center = rv.F.TransformPoint(new Vector3(cx + sg * (HW + JT * 0.5f), 0f, zc)); hl.openAngle = 62f; hl.radius = 1.4f; hl.holdTime = 2.5f; hl.blocker = null;
                    OpenableDoors.Add(hl);
                    Box("Bricks_" + s + "_" + k, new Vector3(cx + sg * (XR + nd + 0.1f), DH * 0.5f, zc), new Vector3(0.2f, DH, DW));
                    block.center = Vector3.zero; block.size = new Vector3(JT, DH, DW);
                    block.transform.localPosition = new Vector3(cx + sg * (XR + nd + 0.25f), DH * 0.5f, zc);
                }
                else if (d == CorridorPlan.Door.Exit)
                {   // salida: detrás, luz blanca intensa (la puerta se abre cuando la llave buena la abre)
                    ExitLeaf = piv;
                    exitBlock = block;   // v7.10.1: sólida (detrás solo hay luz); con la llave buena el nivel termina en el punto de salida
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "ExitLight", side, new Vector3(XR + 0.6f, DH * 0.5f, 0f), new Vector3(DW + 0.3f, DH + 0.3f, 1f), glowMat, false).transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    GameObject ex = new GameObject("ExitDoor");
                    ex.transform.SetParent(rv.F, false);
                    ex.transform.localPosition = new Vector3(cx + sg * (HW - 0.05f), 0f, zc);
                    ex.transform.localRotation = Quaternion.Euler(0f, s == CorridorPlan.Left ? 90f : -90f, 0f);
                    // el punto de salida lógico queda 0,42 m dentro del pasillo (en la columna lógica del pasillo de la rejilla)
                    GameObject ep = new GameObject("ExitPoint"); ep.transform.SetParent(ex.transform, false); ep.transform.localPosition = new Vector3(0f, 0f, 0.42f);
                    ExitTransform = ep.transform;
                    BuildExitCue(ex.transform);
                }
            }

        Transform ends = new GameObject("Ends").transform; ends.SetParent(rv.F, false);
        const float EW = 2.72f, EH = 2.56f;
        if (def.cross)
        {   // los dos extremos: muro con el hueco de la puerta del pasillo largo (la hoja es la del pasillo largo)
            for (int e = 0; e < 2; e++)
            {
                if (e == 1 && def.deadEnd)
                {   // sin salida: un muro con una última puerta cerrada (como el fondo de los pasillos largos)
                    EndDoorWall(ends, L, 180f, cx);
                    Box("EndN", new Vector3(cx, H * 0.5f, L + 0.1f), new Vector3(2f * HW + 0.4f, H, 0.2f));
                    continue;
                }
                float z = e == 0 ? 0f : L, yaw = e == 0 ? 180f : 0f, zb = e == 0 ? -0.1f : L + 0.1f, zt = e == 0 ? 0.012f : L - 0.012f;
                Quad(ends, "End" + e + "_L", new Vector3(cx - (HW + DW * 0.5f) * 0.5f, H * 0.5f, z), new Vector2(HW - DW * 0.5f, H), yaw, endMat, 0f);
                Quad(ends, "End" + e + "_R", new Vector3(cx + (HW + DW * 0.5f) * 0.5f, H * 0.5f, z), new Vector2(HW - DW * 0.5f, H), yaw, endMat, 0f);
                Quad(ends, "End" + e + "_T", new Vector3(cx, (H + DH) * 0.5f, z), new Vector2(DW, H - DH), yaw, endMat, 0f);
                foreach (float sx in new[] { -1f, 1f })
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "EndCasing" + e, ends, new Vector3(cx + sx * (DW * 0.5f + 0.04f), (DH + 0.04f) * 0.5f, zt), new Vector3(0.075f, DH + 0.04f, 0.024f), endTrim, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "EndCasingTop" + e, ends, new Vector3(cx, DH + 0.04f, zt), new Vector3(DW + 0.155f, 0.075f, 0.024f), endTrim, false);
                Box("End" + e + "_L", new Vector3(cx - (HW + DW * 0.5f) * 0.5f - 0.2f, H * 0.5f, zb), new Vector3(HW - DW * 0.5f + 0.4f, H, 0.2f));
                Box("End" + e + "_R", new Vector3(cx + (HW + DW * 0.5f) * 0.5f + 0.2f, H * 0.5f, zb), new Vector3(HW - DW * 0.5f + 0.4f, H, 0.2f));
                Box("End" + e + "_T", new Vector3(cx, (H + DH) * 0.5f, zb), new Vector3(DW, H - DH, 0.2f));
            }
        }
        else
        {
            if (def.main == 0)
            {   // el ascensor al sur (z = 0)
                Quad(ends, "EndS_L", new Vector3(cx - (HW + EW * 0.5f) * 0.5f, H * 0.5f, 0f), new Vector2(HW - EW * 0.5f, H), 0f, endMat, CS * 0.2f);
                Quad(ends, "EndS_R", new Vector3(cx + (HW + EW * 0.5f) * 0.5f, H * 0.5f, 0f), new Vector2(HW - EW * 0.5f, H), 0f, endMat, CS * 0.2f);
                Quad(ends, "EndS_T", new Vector3(cx, (H + EH) * 0.5f, 0f), new Vector2(EW, H - EH), 0f, endMat, 0f);
                foreach (float sx in new[] { -1f, 1f })
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "LiftCasing", ends, new Vector3(cx + sx * (EW * 0.5f + 0.04f), EH * 0.5f, 0.01f), new Vector3(0.08f, EH, 0.03f), endTrim, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "LiftCasingTop", ends, new Vector3(cx, EH + 0.04f, 0.01f), new Vector3(EW + 0.16f, 0.08f, 0.03f), endTrim, false);
                Box("EndS_L", new Vector3(cx - (HW + EW * 0.5f) * 0.5f - 0.2f, H * 0.5f, -0.1f), new Vector3(HW - EW * 0.5f + 0.4f, H, 0.2f));
                Box("EndS_R", new Vector3(cx + (HW + EW * 0.5f) * 0.5f + 0.2f, H * 0.5f, -0.1f), new Vector3(HW - EW * 0.5f + 0.4f, H, 0.2f));
                Box("EndS_T", new Vector3(cx, (H + EH) * 0.5f, -0.1f), new Vector3(EW, H - EH, 0.2f));
            }
            else
            {   // los otros pasillos largos: al sur, también una última puerta cerrada
                EndDoorWall(ends, 0f, 0f, cx);
                Box("EndS", new Vector3(cx, H * 0.5f, -0.1f), new Vector3(2f * HW + 0.4f, H, 0.2f));
            }
            EndDoorWall(ends, L, 180f, cx);   // al norte, un muro con una última puerta cerrada, perdido en la niebla
            Box("EndN", new Vector3(cx, H * 0.5f, L + 0.1f), new Vector3(2f * HW + 0.4f, H, 0.2f));
        }

        // --- colisión ---
        float big = def.cross ? 2f * HW + 1.2f : 3f * CS + 4f;
        Box("Floor", new Vector3(cx, -0.1f, L * 0.5f), new Vector3(big, 0.2f, L + (def.cross ? 1.0f : 4f)));
        Box("Ceiling", new Vector3(cx, H + 0.1f, L * 0.5f), new Vector3(2f * HW + 0.4f, 0.2f, L + (def.cross ? 0.4f : 4f)));
        for (int s = 0; s < 2; s++)
        {
            float sg = s == CorridorPlan.Left ? -1f : 1f, xw = cx + sg * (HW + 0.25f);
            float z0 = -0.2f;
            for (int k = 0; k <= rows; k++)
            {
                float z1 = k < rows ? (k + 0.5f) * CS - DW * 0.5f : L + 0.2f;
                Box("Wall_" + s + "_" + k, new Vector3(xw, H * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.5f, H, z1 - z0));
                if (k < rows) Box("Lintel_" + s + "_" + k, new Vector3(xw, (DH + H) * 0.5f, (k + 0.5f) * CS), new Vector3(0.5f, H - DH, DW));
                z0 = (k + 0.5f) * CS + DW * 0.5f;
            }
        }
    }

    void EndDoorWall(Transform ends, float z, float yaw, float cx)
    {
        float dz = yaw == 0f ? 1f : -1f;
        Quad(ends, "EndWall" + z, new Vector3(cx, H * 0.5f, z), new Vector2(2f * HW, H), yaw + 180f, endMat, 0f);   // v7.10.2: el Quad se ve desde -forward: así se ve desde dentro del pasillo
        Transform nd2 = new GameObject("EndDoor").transform; nd2.SetParent(ends, false);
        nd2.localPosition = new Vector3(cx - dz * (DW * 0.5f - Gap), 0f, z - dz * 0.03f); nd2.localRotation = Quaternion.Euler(0f, yaw + 90f, 0f);
        MeshObj("Leaf", nd2, leafMesh, leafMats, Vector3.zero, Vector3.one);
        foreach (float sx in new[] { -1f, 1f })
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "EndCasing", ends, new Vector3(cx + sx * (DW * 0.5f + 0.04f), (DH + 0.04f) * 0.5f, z + dz * 0.012f), new Vector3(0.075f, DH + 0.04f, 0.024f), endTrim, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "EndCasingTop", ends, new Vector3(cx, DH + 0.04f, z + dz * 0.012f), new Vector3(DW + 0.155f, 0.075f, 0.024f), endTrim, false);
    }

    void Quad(Transform parent, string name, Vector3 pos, Vector2 size, float yaw, Material m, float uShift)
    {
        if (size.x <= 0.001f || size.y <= 0.001f) return;
        GameObject q = ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, name, parent, pos, new Vector3(size.x, size.y, 1f), m, false);
        q.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
    }

    void BuildExitCue(Transform door)
    {   // pista sutil a < 8 m: la rendija bajo la puerta deja pasar una luz blanca tenue (como el LED de otros niveles)
        GameObject led = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "DoorGapLight", door, new Vector3(0f, 0.004f, -0.06f), new Vector3(DW - 0.04f, 0.006f, 0.03f), glowMat, false);
        GameObject gl = new GameObject("GapGlow"); gl.transform.SetParent(door, false); gl.transform.localPosition = new Vector3(0f, 0.12f, 0.25f);
        Light l = gl.AddComponent<Light>(); l.type = LightType.Point; l.range = 1.6f; l.color = new Color(0.95f, 0.97f, 1f); l.intensity = 0.4f; l.shadows = LightShadows.None;
        ExitDoorCue cue = door.gameObject.AddComponent<ExitDoorCue>();
        cue.ledRenderer = led.GetComponent<Renderer>(); cue.glow = l; cue.visibleDistance = 7f; cue.glowIntensity = 0.5f;
        cue.ledRenderer.enabled = false; l.enabled = false;
    }

    // puntos de objetos: llaves, notas y la mayoría de pilas dentro de las habitaciones; el resto, junto a los zócalos del pasillo
    void PickSpots()
    {   // v7.10.2: sin habitaciones: todo junto a los zócalos de los pasillos; las 5 llaves, lejos del ascensor por el camino
        System.Random r = new System.Random(gen.UsedSeed * 389 + 7460);
        int maxD = 1; for (int x = 0; x < plan.Dist.GetLength(0); x++) for (int z = 0; z < plan.Dist.GetLength(1); z++) maxD = Mathf.Max(maxD, plan.Dist[x, z]);
        System.Func<float, float, int, Vector3> spot = (minF, maxF, avoidRun) =>
        {
            for (int t = 0; t < 400; t++)
            {
                RunView rv = Runs[r.Next(Runs.Count)];
                if (rv.def.id == avoidRun) continue;
                int row = rv.def.cross ? 1 + r.Next(rv.def.rows - 2) : 3 + r.Next(rv.def.rows - 5);
                int d = DistOf(rv.def, row);
                if (d < minF * maxD || d > maxF * maxD) continue;
                float sg = r.Next(2) == 0 ? -1f : 1f;
                float dz = (DW * 0.5f + 0.35f + (float)r.NextDouble() * (CS * 0.5f - DW * 0.5f - 0.6f)) * (r.Next(2) == 0 ? -1f : 1f);
                lastSpotRun = rv.def.id;
                return rv.F.TransformPoint(new Vector3(CenterX + sg * Mathf.Min(HW - 0.32f, CS * 0.5f - 0.1f), 0.1f, (row + 0.5f) * CS + dz));
            }
            lastSpotRun = 0;
            return Runs[0].F.TransformPoint(new Vector3(CenterX, 0.1f, 6f));
        };
        int prev = -1;
        for (int i = 0; i < ExitLock.KeyCount; i++) { KeySpots.Add(spot(0.55f, 1.01f, prev)); KeyRuns.Add(lastSpotRun); prev = lastSpotRun; }
        int extra = Mathf.Max(1, cfg.extraSpots);
        for (int i = 0; i < extra; i++) ExtraSpots.Add(spot(0.04f, 1.01f, -1));
        for (int i = 0; i < cfg.batteries; i++) BatterySpots.Add(spot(0.1f, 1.01f, -1));
    }
    private int lastSpotRun;
    public readonly List<int> KeyRuns = new List<int>();
    // distancia lógica (celdas) desde el ascensor a un módulo de un pasillo
    public int DistOf(CorridorPlan.Run d, int row)
    {
        int x = d.cross ? plan.ColOf(d.a) + Mathf.Clamp(row + 2, 1, CorridorPlan.Spacing - 1) : plan.ColOf(d.main), z = d.cross ? d.row : row;
        if (x < 0 || z < 0 || x >= plan.Dist.GetLength(0) || z >= plan.Dist.GetLength(1)) return 0;
        return Mathf.Max(0, plan.Dist[x, z]);
    }
    // pasillo en el que está un punto (local de ese pasillo); -1 si está en el grueso de un muro o fuera
    public int RunAt(Vector3 world, out Vector3 local)
    {
        local = Vector3.zero;
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < Runs.Count; i++)
            {
                if (pass == 0 && i != CurrentRun) continue;
                RunView rv = Runs[i]; Vector3 l = rv.F.InverseTransformPoint(world);
                if (l.z >= -0.3f && l.z <= rv.Length + 0.3f && Mathf.Abs(l.x - CenterX) <= HW + 0.05f) { local = l; return i; }
            }
        return -1;
    }
    public Transform FrameOf(int run) { return Runs[Mathf.Clamp(run, 0, Runs.Count - 1)].F; }
    public Vector3 DoorWorld(int run, int side, int row, float inward)
    {
        float sg = side == CorridorPlan.Left ? -1f : 1f;
        return FrameOf(run).TransformPoint(new Vector3(CenterX + sg * (HW - inward), 0f, (row + 0.5f) * CS));
    }
    public Vector3 ExitDoorWorld(float inward) { return DoorWorld(plan.exitRun, plan.exitSide, plan.exitRow, inward); }
    public float ExitYaw { get { return Runs[plan.exitRun].F.eulerAngles.y + (plan.exitSide == CorridorPlan.Left ? 270f : 90f); } }
    // centro de una celda lógica (mundo)
    public Vector3 CellWorld(int x, int z) { return levelRoot.TransformPoint(new Vector3((x + 0.5f) * CS, 0f, (z + 0.5f) * CS)); }

    // visibilidad por portales: el pasillo en el que estás, y los transversales cuya puerta a tu pasillo está abierta (y, a
    // través de ellos, el pasillo largo del otro lado si su puerta también lo está). Lo demás no se dibuja (detrás de muros).
    void UpdateVisibility(Vector3 camPos)
    {
        Vector3 l; int ri = RunAt(camPos, out l);
        if (ri >= 0) CurrentRun = ri;
        bool cull = CullForTests;
        foreach (RunView rv in Runs) rv.visible = !cull;
        if (cull)
        {
            RunView c = Runs[CurrentRun];
            c.visible = true;
            if (c.def.cross) { Runs[c.def.a].visible = true; Runs[c.def.b].visible = !c.def.deadEnd; foreach (RunView o in Runs[c.def.a].links) if (Near(o, camPos)) o.visible = true; foreach (RunView o in Runs[c.def.b].links) if (Near(o, camPos)) o.visible = true; }
            else
                foreach (RunView x in c.links)
                {
                    HotelDoorLeaf mine = x.def.a == c.def.id ? x.leafA : x.leafB, other = x.def.a == c.def.id ? x.leafB : x.leafA;
                    bool open = (mine != null && Mathf.Abs(mine.Angle) > 1f) || Near(x, camPos);
                    if (!open) continue;
                    x.visible = true;
                    if (other != null && Mathf.Abs(other.Angle) > 1f && !x.def.deadEnd) Runs[x.def.a == c.def.id ? x.def.b : x.def.a].visible = true;
                }
        }
        int n = 0;
        foreach (RunView rv in Runs)
        {
            if (rv.visible) n++;
            if (rv.shown == rv.visible) continue;
            rv.shown = rv.visible;
            foreach (Renderer rr in rv.rends) if (rr != null) rr.forceRenderingOff = !rv.visible;
        }
        VisibleRuns = n;
    }
    bool Near(RunView cross, Vector3 p)
    {   // a menos de 4 m de una de las dos bocas del transversal (dentro del paso del muro)
        Vector3 a = cross.F.TransformPoint(new Vector3(CenterX, 0f, 0f)), b = cross.F.TransformPoint(new Vector3(CenterX, 0f, cross.Length));
        a.y = b.y = p.y; return Vector3.Distance(a, p) < 4f || Vector3.Distance(b, p) < 4f;
    }

    // ---------------- reflejos ----------------
    void SetupReflections()
    {
        GameObject pg = new GameObject("CorridorProbe"); pg.transform.SetParent(transform, false);
        Probe = pg.AddComponent<ReflectionProbe>();
        Probe.mode = ReflectionProbeMode.Realtime; Probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting; Probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        Probe.boxProjection = true; Probe.size = new Vector3(2f * HW, H, 60f); Probe.center = Vector3.zero; Probe.resolution = 256; Probe.hdr = true;
        Probe.nearClipPlane = 0.05f; Probe.farClipPlane = 200f; Probe.clearFlags = ReflectionProbeClearFlags.SolidColor; Probe.backgroundColor = cfg.fog; Probe.importance = 10;
        Probe.cullingMask = ~((1 << 2) | (1 << 5) | (1 << 8) | (1 << 9));
        Probe.intensity = 1f;
        pg.transform.localPosition = new Vector3(CenterX, 1.6f, (Mathf.Floor(plan.rows * 0.5f) + 0.5f) * CS);
        CorridorPlanar.Ensure(gameObject, levelRoot.TransformPoint(Vector3.zero).y);
        probeDirty = 2;
    }
    private int probeDirty, probeRun;
    public void RefreshProbe() { if (probeDirty <= 0) probeDirty = 2; }   // v7.10.1: luz roja
    private RenderTexture probeCube; private Camera probeCam;
    // v3: la captura de la sonda se hace a mano (RenderToCubemap) y se le da como cubemap propio: funciona igual en el juego normal,
    // en el editor y en el ejecutable en -batchmode (allí Unity no procesa las sondas en tiempo real: manillas negras y paredes sin brillo)
    void RenderProbeCube()
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) { Probe.RenderProbe(); ProbeRenders++; return; }
        if (probeCube == null)
        {
            probeCube = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32)   // LDR: los plafones se recortan a 1 y los mips (sin convolución) no dejan chispas
                 { name = "C7460ProbeCube", dimension = UnityEngine.Rendering.TextureDimension.Cube, useMipMap = true, autoGenerateMips = true };
            probeCube.Create();
            GameObject g = new GameObject("C7460ProbeCam"); g.hideFlags = HideFlags.HideAndDontSave;
            probeCam = g.AddComponent<Camera>(); probeCam.enabled = false; probeCam.cameraType = CameraType.Reflection;
            probeCam.nearClipPlane = 0.05f; probeCam.farClipPlane = 200f; probeCam.clearFlags = CameraClearFlags.SolidColor; probeCam.backgroundColor = cfg.fog;
            probeCam.cullingMask = Probe.cullingMask; probeCam.allowHDR = false; probeCam.allowMSAA = false; probeCam.useOcclusionCulling = false;
        }
        probeCam.transform.position = Probe.transform.position; probeCam.transform.rotation = Quaternion.identity;
        float on = Shader.GetGlobalFloat("_C7460PlanarOn"); Shader.SetGlobalFloat("_C7460PlanarOn", 0f);
        probeCam.RenderToCubemap(probeCube, 63);
        Shader.SetGlobalFloat("_C7460PlanarOn", on);
        Probe.mode = ReflectionProbeMode.Custom; Probe.customBakedTexture = probeCube; ProbeRenders++;
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null) UpdateVisibility(cam.transform.position);
        if (Probe != null && cam != null)
        {   // el pasillo es periódico: la sonda salta de módulo en módulo y la misma captura vale (la del centro del pasillo);
            // v7.10.2: en un transversal la caja de la sonda gira con él (y se vuelve a capturar al cambiar de pasillo)
            RunView rv = Runs[CurrentRun];
            Vector3 lp = rv.F.InverseTransformPoint(cam.transform.position);
            float row = rv.def.cross ? Mathf.Floor(rv.def.rows * 0.5f) : Mathf.Clamp(Mathf.Floor(lp.z / CS), 8f, rv.def.rows - 9f);
            Probe.transform.position = rv.F.TransformPoint(new Vector3(CenterX, 1.6f, (row + 0.5f) * CS));
            Probe.size = rv.def.cross ? new Vector3(rv.Length, H, 2f * HW) : new Vector3(2f * HW, H, 60f);
            if (probeRun != CurrentRun) { probeRun = CurrentRun; if (probeDirty <= 0) probeDirty = 2; }
            if (probeDirty > 0 && --probeDirty == 0) RenderProbeCube();
        }
        // puerta de salida: se abre cuando la llave buena la abre
        if (ExitLeaf != null)
        {
            bool open = ExitLock.Active != null && ExitLock.Active.Unlocked;
            exitOpen = Mathf.MoveTowards(exitOpen, open ? 1f : 0f, Time.deltaTime * 0.9f);
            ExitAngle = 100f * Mathf.SmoothStep(0f, 1f, exitOpen);
            ExitLeaf.localRotation = Quaternion.Euler(0f, ExitAngle, 0f);
        }
        TickRattle();
    }

    // manillas: si te pegas a una puerta cerrada mirándola, la manilla baja y no cede
    void TickRattle()
    {
        if (rattleCd > 0f) rattleCd -= Time.deltaTime;
        if (toastCd > 0f) toastCd -= Time.deltaTime;
        FirstPersonController p = FirstPersonController.LocalInstance;
        if (p == null || rattleCd > 0f) return;
        GameManager gm = GameManager.Instance;
        if (gm != null && gm.State != GameManager.GameState.Playing) return;
        Vector3 lp; int ri = RunAt(p.transform.position, out lp); if (ri < 0) return;
        RunView rv = Runs[ri];
        int row = Mathf.FloorToInt(lp.z / CS); if (row < 0 || row >= rv.def.rows) return;
        float dz = lp.z - (row + 0.5f) * CS; if (Mathf.Abs(dz) > DW * 0.5f + 0.15f) return;
        float dx = lp.x - CenterX; int side = dx < 0f ? CorridorPlan.Left : CorridorPlan.Right;
        if (HW - Mathf.Abs(dx) > 0.75f) return;
        if (rv.def.doors[side, row] != CorridorPlan.Door.Locked) return;
        Vector3 fwd = rv.F.InverseTransformDirection(p.transform.forward); if (fwd.x * Mathf.Sign(dx) < 0.45f) return;
        Rattle(ri, side, row);
        if (toastCd <= 0f && toasts < 3 && gm != null) { gm.ShowToast(toasts == 0 ? "La manilla baja, pero la puerta no se abre." : "Cerrada."); toasts++; toastCd = 15f; }
    }

    public void Rattle(int side, int row) { Rattle(0, side, row); }
    public void Rattle(int run, int side, int row)
    {
        float sg = side == CorridorPlan.Left ? -1f : 1f;
        rattleSrc.transform.position = FrameOf(run).TransformPoint(new Vector3(CenterX + sg * XF, HandleY, (row + 0.5f) * CS + HandleZ));
        rattleSrc.pitch = 1.05f + (row % 5) * 0.03f;
        rattleSrc.PlayOneShot(rattleClip, AudioMix.Event(0.55f));
        Rattles++; rattleCd = 2.2f; AudioEventLog.Note("C7460_Manilla");
    }

    // ---------------- utilidades para pruebas ----------------
    public Vector3 DoorWorld(int side, int row, float inward = 0.6f)
    {
        float sg = side == CorridorPlan.Left ? -1f : 1f;
        return levelRoot.TransformPoint(new Vector3(CenterX + sg * (HW - inward), 0f, (row + 0.5f) * CS));
    }
    public Vector3 RoomCenterWorld(int side, int row)
    {
        float sg = side == CorridorPlan.Left ? -1f : 1f;
        return levelRoot.TransformPoint(new Vector3(CenterX + sg * (XR + RD * 0.5f), 0f, (row + 0.5f) * CS));
    }
}

// Reflejo planar del suelo del pasillo (resina pulida): cámara espejo en y = 0 con plano de recorte oblicuo, a media
// resolución con mips (borrosidad por rugosidad). Bajo: sin reflejo planar (queda la sonda).
public class CorridorPlanar : MonoBehaviour
{
    public float floorY;
    public static CorridorPlanar Instance { get; private set; }
    public static bool ForceOff;
    public int Renders { get; private set; }
    public static readonly Vector4 Params = new Vector4(0.85f, 0.04f, 0.010f, 6.5f);
    public static CorridorPlanar Ensure(GameObject host, float y)
    {
        CorridorPlanar p = host.GetComponent<CorridorPlanar>(); if (p == null) p = host.AddComponent<CorridorPlanar>();
        p.floorY = y; Instance = p; return p;
    }
    private Camera rc; private RenderTexture rt; private int size;
    public static float Streak = 0.011f;   // v3: separación vertical de las 9 muestras del reflejo (pantalla)
    private static readonly int StreakId = Shader.PropertyToID("_C7460Streak");
    private static readonly int TexId = Shader.PropertyToID("_C7460PlanarTex"), OnId = Shader.PropertyToID("_C7460PlanarOn"), PId = Shader.PropertyToID("_C7460PlanarP");
    private int lastFrame = -10;
    private void OnEnable() { Camera.onPreCull += PreCull; }
    private void OnDisable()
    {
        Camera.onPreCull -= PreCull;
        if (Instance == this) { Shader.SetGlobalFloat(OnId, 0f); Instance = null; }
        if (rt != null) { rt.Release(); Destroy(rt); rt = null; }
        if (rc != null) Destroy(rc.gameObject);
    }
    private void PreCull(Camera c)
    {
        if (c != null && c.cameraType == CameraType.Reflection) { Shader.SetGlobalFloat(OnId, 0f); return; }   // la sonda no usa el reflejo planar
        if (c == null || c != Camera.main || c.cameraType != CameraType.Game || lastFrame == Time.frameCount) return;
        lastFrame = Time.frameCount; Refresh(c);
    }
    private void LateUpdate() { if (Time.frameCount - lastFrame > 2) Refresh(Camera.main); }
    public void Refresh(Camera cam)
    {
        if (Instance != this) return;
        int q = GameSettings.Quality;
        int want = q >= 2 ? 720 : q == 1 ? 432 : 0;
        Shader.SetGlobalVector(PId, Params); Shader.SetGlobalFloat(StreakId, Streak);
        if (ForceOff || cam == null || want == 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) { Shader.SetGlobalFloat(OnId, 0f); return; }
        float aspect = Mathf.Max(1f, cam.aspect);
        if (rt == null || size != want)
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
            size = want; rt = new RenderTexture(Mathf.RoundToInt(want * aspect), want, 16, RenderTextureFormat.DefaultHDR) { name = "C7460Planar", useMipMap = true, autoGenerateMips = true };
        }
        if (rc == null)
        {
            GameObject g = new GameObject("C7460PlanarCam"); g.hideFlags = HideFlags.HideAndDontSave;
            rc = g.AddComponent<Camera>(); rc.enabled = false;
        }
        rc.CopyFrom(cam);
        rc.targetTexture = rt;
        rc.cullingMask = ~((1 << 2) | (1 << 5) | (1 << 8) | (1 << 9));
        rc.depthTextureMode = DepthTextureMode.None; rc.useOcclusionCulling = false; rc.allowMSAA = false; rc.allowHDR = true;
        Matrix4x4 refl = new Matrix4x4();
        refl.SetRow(0, new Vector4(1, 0, 0, 0)); refl.SetRow(1, new Vector4(0, -1, 0, 2f * floorY));
        refl.SetRow(2, new Vector4(0, 0, 1, 0)); refl.SetRow(3, new Vector4(0, 0, 0, 1));
        rc.worldToCameraMatrix = cam.worldToCameraMatrix * refl;
        Vector3 cp = rc.worldToCameraMatrix.MultiplyPoint(new Vector3(0f, floorY + 0.003f, 0f));
        Vector3 cn = rc.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
        rc.projectionMatrix = cam.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn)));
        Shader.SetGlobalFloat(OnId, 0f);   // el suelo no se refleja a sí mismo
        bool inv = GL.invertCulling; GL.invertCulling = !inv;
        try { rc.Render(); Renders++; } finally { GL.invertCulling = inv; }
        Shader.SetGlobalTexture(TexId, rt); Shader.SetGlobalFloat(OnId, 1f);
    }
}
