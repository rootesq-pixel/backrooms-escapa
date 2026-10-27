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
    public const float Gap = 0.003f, HandleY = 1.0f, HandleZ = 0.39f;
    public const int ChunkModules = 10;
    public const float BakeExposure = 0.40f, RoomExposure = 0.95f;   // v2: módulo más denso (irradiancia mediana 3,5 en vez de 2,1)   // irradiancia horneada -> pantalla (con el ACES del PostStack)
    public const float PanelEmission = 3.2f;
    public static readonly Color WallTint = new Color(0.88f, 0.88f, 0.89f), FloorTint = new Color(1.16f, 1.18f, 1.22f), CeilTint = new Color(1.40f, 1.40f, 1.40f);
    public const float WallBump = 0.3f, TrimMinLM = 0.95f;   // pizarra menos azul (como la foto) y resina algo más clara
    public const float FarClip = 420f;
    public static bool HasAssets { get { return Resources.Load<TextAsset>("C7460/C7460Module") != null && Shader.Find("Backrooms/CorridorBaked") != null; } }

    public BackroomsGenerator gen; public LevelConfig cfg; public CorridorPlan plan; public Transform levelRoot;
    public Vector3 StartLocal { get; private set; }
    public Transform ExitTransform { get; private set; }
    public readonly List<Vector3> BatterySpots = new List<Vector3>(), ExtraSpots = new List<Vector3>(), KeySpots = new List<Vector3>();
    public readonly List<HotelDoorLeaf> OpenableDoors = new List<HotelDoorLeaf>();
    public Transform ExitLeaf { get; private set; }
    public float ExitAngle { get; private set; }
    public int Rattles { get; private set; }
    public int ChunkCount { get; private set; }
    public int RoomCount { get; private set; }
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
    public const int MFloor = 0, MWall = 1, MCeil = 2, MTrim = 3, MDoor = 4, MMetal = 5, MEmit = 6, MBrick = 7;
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
        m.SetColor("_Color", tint);
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
        GameObject g = new GameObject(name); g.transform.SetParent(transform, false); g.transform.localPosition = center;
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

    private void OnDestroy() { if (Active == this) Active = null; if (rattleClip != null) ProceduralAssets.SafeDestroy(rattleClip); }

    void BuildAll()
    {
        Texture lm = Resources.Load<Texture2D>("C7460/C7460_LM"), lmr = Resources.Load<Texture2D>("C7460/C7460Room_LM");
        Color white = Color.white;
        modMats = new Material[] {
            Baked("C7460Floor", lm, BakeExposure, 1f, 1f, FloorTint), Baked("C7460Wall", lm, BakeExposure, 1f, 0f, WallTint),
            Baked("C7460Ceil", lm, BakeExposure, 1f, 0f, CeilTint), Baked("C7460Trim", lm, BakeExposure, 1f, 0f, white),
            Baked("C7460Trim", lm, BakeExposure, 1.05f, 0f, white) };
        FloorMat = modMats[0];
        modMats[1].SetFloat("_BumpScale", WallBump);                                     // pintura mate lisa (como la foto)
        modMats[3].SetFloat("_LMMin", TrimMinLM); modMats[4].SetFloat("_LMMin", TrimMinLM);   // jambas y cantos de hoja sin manchas negras
        roomMats = new Material[] {
            Baked("C7460Floor", lmr, RoomExposure, 1f, 1f, FloorTint), Baked("C7460Wall", lmr, RoomExposure, 1f, 0f, WallTint),
            Baked("C7460Ceil", lmr, RoomExposure, 1f, 0f, CeilTint), Baked("C7460Trim", lmr, RoomExposure, 1f, 0f, white),
            Baked("C7460Trim", lmr, RoomExposure, 1.05f, 0f, white) };
        roomMats[1].SetFloat("_BumpScale", WallBump);
        metalMat = ProceduralAssets.CreateMaterial(new Color(0.80f, 0.80f, 0.82f), null, Vector2.one, 0.86f); metalMat.SetFloat("_Metallic", 1f); metalMat.name = "C7460_Chrome"; track(metalMat);
        emitMat = ProceduralAssets.CreateMaterial(new Color(0.95f, 0.95f, 0.95f), null, Vector2.one, 0.2f); ProceduralAssets.SetEmission(emitMat, new Color(1f, 0.99f, 0.97f) * PanelEmission); emitMat.name = "C7460_Panel"; track(emitMat);
        PanelMat = emitMat;
        // superficies sin hornear (extremos, nichos tapiados): irradiancia constante en una textura 1×1 (mismo shader)
        Texture2D flat = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true) { name = "C7460_FlatLM" };
        Color fl = new Color(1.55f, 1.56f, 1.60f, 1f); flat.SetPixels(new[] { fl, fl, fl, fl }); flat.Apply(false, false); track(flat);
        Texture2D dim = new Texture2D(2, 2, TextureFormat.RGBAHalf, false, true) { name = "C7460_NicheLM" };
        Color dl = new Color(0.55f, 0.56f, 0.58f, 1f); dim.SetPixels(new[] { dl, dl, dl, dl }); dim.Apply(false, false); track(dim);
        endMat = Baked("C7460Wall", flat, BakeExposure, 1f, 0f, WallTint);
        brickMat = Baked("C7460Brick", dim, BakeExposure, 1f, 0f, white);
        Material nicheTrim = Baked("C7460Trim", dim, BakeExposure, 1f, 0f, white);
        Material endTrim = Baked("C7460Trim", flat, BakeExposure, 1f, 0f, white);
        glowMat = ProceduralAssets.CreateMaterial(Color.white, null, Vector2.one, 0f); ProceduralAssets.SetEmission(glowMat, new Color(1f, 0.98f, 0.94f) * 4.5f); glowMat.name = "C7460_ExitGlow"; track(glowMat);

        Loaded mod = Load("C7460/C7460Module"), leaf = Load("C7460/C7460Leaf"), room = Load("C7460/C7460Room");
        Vector3 hinge = new Vector3(XB, 0f, -DW * 0.5f + Gap);
        float cx = CenterX; int rows = plan.rows;

        // --- tramos de 10 módulos (las hojas cerradas con llave van dentro de la malla del tramo) ---
        Transform vis = new GameObject("Visual").transform; vis.SetParent(transform, false);
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
                    if (plan.doors[s, k] == CorridorPlan.Door.Locked) cb.Add(leaf, off, s == CorridorPlan.Left, 0f, hinge);
            }
            Mesh m = cb.Build("C7460Chunk" + c0, out List<int> ids); track(m);
            MeshObj("Chunk" + c0, vis, m, MatsFor(ids, modMats), new Vector3(cx, 0f, zc0), Vector3.one);
            ChunkCount++;
        }

        // --- hojas que se mueven (habitaciones, tapiadas, salida) y lo que hay detrás ---
        Combiner lc = new Combiner(); lc.Add(leaf, Vector3.zero, false, 0f);
        Mesh leafMesh = lc.Build("C7460Leaf", out List<int> leafIds); track(leafMesh);
        Material[] leafMats = MatsFor(leafIds, modMats);
        Combiner rc = new Combiner(); rc.Add(room, Vector3.zero, false, 0f);
        Mesh roomMesh = rc.Build("C7460Room", out List<int> roomIds); track(roomMesh);
        Material[] rMats = MatsFor(roomIds, roomMats);
        Transform doorsRoot = new GameObject("Doors").transform; doorsRoot.SetParent(transform, false);
        for (int k = 0; k < rows; k++)
            for (int s = 0; s < 2; s++)
            {
                CorridorPlan.Door d = plan.doors[s, k];
                float zc = (k + 0.5f) * CS, sg = s == CorridorPlan.Left ? -1f : 1f;
                if (d == CorridorPlan.Door.Locked) continue;
                Transform side = new GameObject((d == CorridorPlan.Door.Exit ? "ExitDoorLeaf" : d == CorridorPlan.Door.Room ? "RoomDoor" : "BrickDoor") + "_" + s + "_" + k).transform;
                side.SetParent(doorsRoot, false); side.localPosition = new Vector3(cx, 0f, zc); side.localScale = new Vector3(sg, 1f, 1f);
                Transform piv = new GameObject("Pivot").transform; piv.SetParent(side, false); piv.localPosition = hinge;
                MeshObj("Leaf", piv, leafMesh, leafMats, Vector3.zero, Vector3.one);
                // hueco detrás (colisión en espacio sin espejo)
                float xw = cx + sg * (HW + JT * 0.5f);
                BoxCollider block = Box("DoorBlock_" + s + "_" + k, new Vector3(xw, DH * 0.5f, zc), new Vector3(JT, DH, DW));
                if (d == CorridorPlan.Door.Room)
                {
                    MeshObj("Room", side, roomMesh, rMats, Vector3.zero, Vector3.one);
                    HotelDoorLeaf hl = piv.gameObject.AddComponent<HotelDoorLeaf>();
                    hl.center = levelRoot.TransformPoint(new Vector3(cx + sg * (HW + JT * 0.5f), 0f, zc)); hl.openAngle = 98f; hl.radius = 1.7f; hl.holdTime = 4f; hl.blocker = block;
                    OpenableDoors.Add(hl);
                    float rx0 = cx + sg * XR, rx1 = cx + sg * (XR + RD), rcx = (rx0 + rx1) * 0.5f;
                    Box("RoomBack_" + s + "_" + k, new Vector3(cx + sg * (XR + RD + 0.1f), RH * 0.5f, zc), new Vector3(0.2f, RH, RW + 0.4f));
                    Box("RoomS_" + s + "_" + k, new Vector3(rcx, RH * 0.5f, zc - RW * 0.5f - 0.1f), new Vector3(RD + 0.4f, RH, 0.2f));
                    Box("RoomN_" + s + "_" + k, new Vector3(rcx, RH * 0.5f, zc + RW * 0.5f + 0.1f), new Vector3(RD + 0.4f, RH, 0.2f));
                    Box("RoomCeil_" + s + "_" + k, new Vector3(rcx, RH + 0.1f, zc), new Vector3(RD + 0.4f, 0.2f, RW + 0.4f));
                    RoomCount++;
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
                    hl.center = levelRoot.TransformPoint(new Vector3(cx + sg * (HW + JT * 0.5f), 0f, zc)); hl.openAngle = 62f; hl.radius = 1.4f; hl.holdTime = 2.5f; hl.blocker = null;
                    OpenableDoors.Add(hl);
                    Box("Bricks_" + s + "_" + k, new Vector3(cx + sg * (XR + nd + 0.1f), DH * 0.5f, zc), new Vector3(0.2f, DH, DW));
                    block.center = Vector3.zero; block.size = new Vector3(JT, DH, DW);
                    block.transform.localPosition = new Vector3(cx + sg * (XR + nd + 0.25f), DH * 0.5f, zc);
                }
                else
                {   // salida: detrás, luz blanca intensa (la puerta se abre cuando la llave buena la abre)
                    ExitLeaf = piv;
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "ExitLight", side, new Vector3(XR + 0.6f, DH * 0.5f, 0f), new Vector3(DW + 0.3f, DH + 0.3f, 1f), glowMat, false).transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                    GameObject ex = new GameObject("ExitDoor");
                    ex.transform.SetParent(levelRoot, false);
                    ex.transform.localPosition = new Vector3(cx + sg * (HW - 0.05f), 0f, zc);
                    ex.transform.localRotation = Quaternion.Euler(0f, s == CorridorPlan.Left ? 90f : -90f, 0f);
                    // el punto de salida lógico queda 0,42 m dentro del pasillo (en la columna lógica del pasillo de la rejilla)
                    GameObject ep = new GameObject("ExitPoint"); ep.transform.SetParent(ex.transform, false); ep.transform.localPosition = new Vector3(0f, 0f, 0.42f);
                    ExitTransform = ep.transform;
                    BuildExitCue(ex.transform);
                }
            }

        // --- extremos: el ascensor al sur (z = 0) y un muro con una última puerta cerrada al norte, perdido en la niebla ---
        float L = Length;
        Transform ends = new GameObject("Ends").transform; ends.SetParent(transform, false);
        const float EW = 2.72f, EH = 2.56f;
        Quad(ends, "EndS_L", new Vector3(cx - (HW + EW * 0.5f) * 0.5f, H * 0.5f, 0f), new Vector2(HW - EW * 0.5f, H), 0f, endMat, CS * 0.2f);
        Quad(ends, "EndS_R", new Vector3(cx + (HW + EW * 0.5f) * 0.5f, H * 0.5f, 0f), new Vector2(HW - EW * 0.5f, H), 0f, endMat, CS * 0.2f);
        Quad(ends, "EndS_T", new Vector3(cx, (H + EH) * 0.5f, 0f), new Vector2(EW, H - EH), 0f, endMat, 0f);
        foreach (float sx in new[] { -1f, 1f })
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "LiftCasing", ends, new Vector3(cx + sx * (EW * 0.5f + 0.04f), EH * 0.5f, 0.01f), new Vector3(0.08f, EH, 0.03f), endTrim, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "LiftCasingTop", ends, new Vector3(cx, EH + 0.04f, 0.01f), new Vector3(EW + 0.16f, 0.08f, 0.03f), endTrim, false);
        Quad(ends, "EndN", new Vector3(cx, H * 0.5f, L), new Vector2(2f * HW, H), 180f, endMat, 0f);
        Transform nd2 = new GameObject("EndDoor").transform; nd2.SetParent(ends, false);
        nd2.localPosition = new Vector3(cx + DW * 0.5f - Gap, 0f, L + 0.03f); nd2.localRotation = Quaternion.Euler(0f, -90f, 0f);
        MeshObj("Leaf", nd2, leafMesh, leafMats, Vector3.zero, Vector3.one);
        foreach (float sx in new[] { -1f, 1f })
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "EndCasing", ends, new Vector3(cx + sx * (DW * 0.5f + 0.04f), (DH + 0.04f) * 0.5f, L - 0.012f), new Vector3(0.075f, DH + 0.04f, 0.024f), endTrim, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "EndCasingTop", ends, new Vector3(cx, DH + 0.04f, L - 0.012f), new Vector3(DW + 0.155f, 0.075f, 0.024f), endTrim, false);

        // --- colisión ---
        float big = 3f * CS + 4f;
        Box("Floor", new Vector3(cx, -0.1f, L * 0.5f), new Vector3(big, 0.2f, L + 4f));
        Box("Ceiling", new Vector3(cx, H + 0.1f, L * 0.5f), new Vector3(2f * HW + 0.4f, 0.2f, L + 4f));
        Box("EndS_L", new Vector3(cx - (HW + EW * 0.5f) * 0.5f - 0.2f, H * 0.5f, -0.1f), new Vector3(HW - EW * 0.5f + 0.4f, H, 0.2f));
        Box("EndS_R", new Vector3(cx + (HW + EW * 0.5f) * 0.5f + 0.2f, H * 0.5f, -0.1f), new Vector3(HW - EW * 0.5f + 0.4f, H, 0.2f));
        Box("EndS_T", new Vector3(cx, (H + EH) * 0.5f, -0.1f), new Vector3(EW, H - EH, 0.2f));
        Box("EndN", new Vector3(cx, H * 0.5f, L + 0.1f), new Vector3(2f * HW + 0.4f, H, 0.2f));
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

        // --- sonido de las manillas ---
        GameObject ra = new GameObject("HandleRattle"); ra.transform.SetParent(transform, false);
        rattleSrc = ra.AddComponent<AudioSource>(); rattleSrc.playOnAwake = false; rattleSrc.spatialBlend = 1f; rattleSrc.minDistance = 1.2f; rattleSrc.maxDistance = 14f; rattleSrc.rolloffMode = AudioRolloffMode.Linear; rattleSrc.dopplerLevel = 0f;
        rattleClip = ProceduralAudio.CreateLockRattle();

        StartLocal = new Vector3(cx, 0f, 0f);
        PickSpots();
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null) SetupReflections();
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
    {
        System.Random r = new System.Random(gen.UsedSeed * 389 + 7460);
        List<Vector2Int> rooms = new List<Vector2Int>(plan.rooms);
        for (int i = rooms.Count - 1; i > 0; i--) { int j = r.Next(i + 1); Vector2Int t = rooms[i]; rooms[i] = rooms[j]; rooms[j] = t; }
        int ri = 0;
        System.Func<Vector3> roomSpot = () =>
        {
            Vector2Int rm = rooms[ri++ % rooms.Count];
            float sg = rm.x == CorridorPlan.Left ? -1f : 1f;
            float x = CenterX + sg * (XR + 0.6f + (float)r.NextDouble() * (RD - 1.2f)), z = (rm.y + 0.5f) * CS + ((float)r.NextDouble() - 0.5f) * (RW - 1.2f);
            return levelRoot.TransformPoint(new Vector3(x, 0.1f, z));
        };
        System.Func<Vector3> hallSpot = () =>
        {
            int row = 3 + r.Next(plan.rows - 5); float sg = r.Next(2) == 0 ? -1f : 1f;
            float dz = (DW * 0.5f + 0.35f + (float)r.NextDouble() * (CS * 0.5f - DW * 0.5f - 0.6f)) * (r.Next(2) == 0 ? -1f : 1f);
            return levelRoot.TransformPoint(new Vector3(CenterX + sg * Mathf.Min(HW - 0.32f, CS * 0.5f - 0.1f), 0.1f, (row + 0.5f) * CS + dz));
        };
        for (int i = 0; i < ExitLock.KeyCount; i++) KeySpots.Add(roomSpot());
        int extra = Mathf.Max(1, cfg.extraSpots);
        for (int i = 0; i < extra; i++) ExtraSpots.Add(i < 3 ? roomSpot() : hallSpot());   // las 3 primeras son las notas
        for (int i = 0; i < cfg.batteries; i++) BatterySpots.Add(i % 2 == 0 ? roomSpot() : hallSpot());
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
    private int probeDirty;

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (Probe != null && cam != null)
        {   // el pasillo es periódico: la sonda salta de módulo en módulo y la misma captura vale (la del centro del pasillo)
            Vector3 lp = levelRoot.InverseTransformPoint(cam.transform.position);
            float row = Mathf.Clamp(Mathf.Floor(lp.z / CS), 8f, plan.rows - 9f);
            Probe.transform.localPosition = new Vector3(CenterX, 1.6f, (row + 0.5f) * CS);
            if (probeDirty > 0 && --probeDirty == 0) { Probe.RenderProbe(); ProbeRenders++; }
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
        Vector3 lp = levelRoot.InverseTransformPoint(p.transform.position);
        int row = Mathf.FloorToInt(lp.z / CS); if (row < 0 || row >= plan.rows) return;
        float dz = lp.z - (row + 0.5f) * CS; if (Mathf.Abs(dz) > DW * 0.5f + 0.15f) return;
        float dx = lp.x - CenterX; int side = dx < 0f ? CorridorPlan.Left : CorridorPlan.Right;
        if (HW - Mathf.Abs(dx) > 0.75f) return;
        if (plan.doors[side, row] != CorridorPlan.Door.Locked) return;
        Vector3 fwd = levelRoot.InverseTransformDirection(p.transform.forward); if (fwd.x * Mathf.Sign(dx) < 0.45f) return;
        Rattle(side, row);
        if (toastCd <= 0f && toasts < 3 && gm != null) { gm.ShowToast(toasts == 0 ? "La manilla baja, pero la puerta no se abre." : "Cerrada."); toasts++; toastCd = 15f; }
    }

    public void Rattle(int side, int row)
    {
        float sg = side == CorridorPlan.Left ? -1f : 1f;
        rattleSrc.transform.position = levelRoot.TransformPoint(new Vector3(CenterX + sg * XF, HandleY, (row + 0.5f) * CS + HandleZ));
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
    public static readonly Vector4 Params = new Vector4(1.0f, 0.045f, 0.012f, 5.5f);
    public static CorridorPlanar Ensure(GameObject host, float y)
    {
        CorridorPlanar p = host.GetComponent<CorridorPlanar>(); if (p == null) p = host.AddComponent<CorridorPlanar>();
        p.floorY = y; Instance = p; return p;
    }
    private Camera rc; private RenderTexture rt; private int size;
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
        Shader.SetGlobalVector(PId, Params);
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
