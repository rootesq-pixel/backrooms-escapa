using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.5.0: todo el atrezo del hotel (nivel 3) a partir de HotelPlan: puertas numeradas (cerradas con llave o que se abren),
// marcos, ascensores muertos, escaleras de incendios, muebles de las habitaciones (cama, mesillas, lámparas, armario, escritorio,
// sillón, maletas), recepción con casillero de llaves, comedor, salón de baile (mesas, sillas, columnas, piano, arañas), carritos,
// plantas secas, cuadros (y caídos), estanterías de lencería, escombros, y el acabado de los pasillos de servicio (pintura y linóleo).
// Mallas combinadas por bloques de 8 x 8 celdas: «sólidas» (bajo el nivel: colisión y NavMesh) y «decorado» (sin colisión).
// Las lámparas, apliques y arañas encendidas tienen su luz (LightCuller deja encendidas solo las más cercanas).
public static class HotelDecor
{
    public static int Doors { get; private set; }
    public static int OpenDoors { get; private set; }
    public static int ItemCount { get; private set; }
    public static int LiveLamps { get; private set; }
    public static readonly List<Vector3> DoorCenters = new List<Vector3>();   // local al nivel (capturas y pruebas)
    public static readonly List<Vector3> DoorNormals = new List<Vector3>();
    public static readonly List<HotelDoorLeaf> Leaves = new List<HotelDoorLeaf>();
    public static readonly Dictionary<int, Vector2> Over = new Dictionary<int, Vector2>();   // por tipo: cuánto sobresale la malla de la huella del plano (x, z)
    public static string OverReport() { var sb = new System.Text.StringBuilder(); foreach (var kv in Over) sb.Append(" k" + kv.Key + ":" + kv.Value.x.ToString("F2") + "/" + kv.Value.y.ToString("F2")); return sb.ToString(); }
    public const float DoorW = 1.5f, DoorH = 2.2f;      // hueco en el muro (las jambas van en la malla del nivel; 1,5 m deja paso en el NavMesh con radio 0,5)
    const float LW = 1.38f, LH = 2.15f;                  // hoja
    const int MDoor = 0, MWood = 1, MBrass = 2, MNum = 3, MBed = 4, MSheet = 5, MCloth = 6, MUph = 7, MPaint = 8, MSteel = 9, MIron = 10,
        MPlant = 11, MPot = 12, MCrystal = 13, MSvc = 14, MLino = 15, MMarble = 16, MDebris = 17, MBlack = 18, MGlass = 19, NM = 20;

    static Material Surface(string n, Color tint, System.Func<Object, Object> track, float smoothMul = 1f)
    {
        Texture2D a = Resources.Load<Texture2D>("Textures/Hotel/" + n + "_a"), nm = Resources.Load<Texture2D>("Textures/Hotel/" + n + "_n"), m = Resources.Load<Texture2D>("Textures/Hotel/" + n + "_m");
        SurfaceSet s = a != null && nm != null && m != null ? new SurfaceSet { albedo = a, normal = nm, mask = m, baked = true } : null;
        Material mat = ProceduralAssets.CreateSurfaceMaterial(s, s != null ? tint : tint * 0.5f, 1f, 1f);
        if (smoothMul != 1f && mat.HasProperty("_GlossMapScale")) mat.SetFloat("_GlossMapScale", smoothMul);
        track(mat); return mat;
    }
    static Material Plain(Color c, float smooth, float metal, System.Func<Object, Object> track)
    {
        Material m = ProceduralAssets.CreateMaterial(c, null, Vector2.one, smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        track(m); return m;
    }

    // ------------------------------------------------------------------ emisor de geometría con matriz
    class E
    {
        public MeshBuilder b; public Matrix4x4 M = Matrix4x4.identity;
        Vector3 P(Vector3 p) { return M.MultiplyPoint3x4(p); }
        Vector3 Dv(Vector3 d) { return M.MultiplyVector(d).normalized; }
        public void Quad(int s, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d, Vector3 n, Vector3 u, Vector3 v, float tile)
        { b.AddQuad(s, P(a), P(c1), P(c2), P(d), Dv(n), Dv(u), Dv(v), tile, tile, 0f, 0f); }
        public void QuadUV(int s, Vector3 a, Vector3 c1, Vector3 c2, Vector3 d, Vector3 n, Vector3 u, Vector3 v, Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3)
        { b.AddQuadUV(s, P(a), P(c1), P(c2), P(d), Dv(n), Dv(u), Dv(v), t0, t1, t2, t3); }
        public void Box(int s, Vector3 c, Vector3 sz, float tile = 1f, bool bottom = false)
        {
            Vector3 h = sz * 0.5f; float x0 = c.x - h.x, x1 = c.x + h.x, y0 = c.y - h.y, y1 = c.y + h.y, z0 = c.z - h.z, z1 = c.z + h.z;
            Vector3 F = Vector3.forward, R = Vector3.right, U = Vector3.up;
            Quad(s, new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), F, R, U, tile);
            Quad(s, new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), -F, -R, U, tile);
            Quad(s, new Vector3(x1, y0, z1), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), R, -F, U, tile);
            Quad(s, new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), -R, F, U, tile);
            Quad(s, new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), U, R, -F, tile);
            if (bottom) Quad(s, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), -U, R, F, tile);
        }
        public void BoxR(int s, Vector3 c, Vector3 sz, Quaternion q, float tile = 1f)
        {
            Matrix4x4 keep = M; M = M * Matrix4x4.TRS(c, q, Vector3.one); Box(s, Vector3.zero, sz, tile, true); M = keep;
        }
        public void Cyl(int s, Vector3 b0, float r0, float r1, float hh, int seg, bool top, bool bot, float tile = 1f)
        {
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg, am = (a0 + a1) * 0.5f;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)), dm = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                Vector3 p00 = b0 + d0 * r0, p10 = b0 + d1 * r0, p11 = b0 + d1 * r1 + Vector3.up * hh, p01 = b0 + d0 * r1 + Vector3.up * hh;
                Vector3 n = (dm * hh + Vector3.up * (r0 - r1)).normalized;
                Vector3 tg = new Vector3(-dm.z, 0f, dm.x);
                Quad(s, p00, p10, p11, p01, n, tg, Vector3.up, tile);
                if (top && r1 > 0.001f) Quad(s, b0 + Vector3.up * hh, p01, p11, p11, Vector3.up, Vector3.right, Vector3.forward, tile);
                if (bot && r0 > 0.001f) Quad(s, b0, p10, p00, p00, Vector3.down, Vector3.right, Vector3.forward, tile);
            }
        }
        public void Tube(int s, Vector3 a, Vector3 c, float r) { b.AddTube(s, P(a), P(c), r, 1f); }
    }

    class Blocks
    {
        public readonly Dictionary<int, MeshBuilder> map = new Dictionary<int, MeshBuilder>();
        public float cs;
        public MeshBuilder At(Vector3 p)
        {
            int k = Mathf.Clamp(Mathf.FloorToInt(p.x / (cs * 8f)), 0, 99) * 100 + Mathf.Clamp(Mathf.FloorToInt(p.z / (cs * 8f)), 0, 99);
            if (!map.TryGetValue(k, out MeshBuilder m)) map[k] = m = new MeshBuilder(NM);
            return m;
        }
    }

    static BackroomsGenerator gen; static HotelPlan plan; static Material[] mats; static System.Func<Object, Object> trk;
    static Material shadeOn, shadeOff, bulbOn, bulbOff; static Vector2Int exitC;

    public static void Build(BackroomsGenerator g, HotelPlan p, LevelConfig cfg, Transform levelRoot, Transform decorRoot, System.Func<Object, Object> track)
    {
        gen = g; plan = p; trk = track;
        Doors = 0; OpenDoors = 0; ItemCount = 0; LiveLamps = 0; DoorCenters.Clear(); DoorNormals.Clear(); Leaves.Clear(); Over.Clear(); HotelDoorLeaf.OpenCount = 0;
        System.Random r = new System.Random(g.UsedSeed * 733 + 19);
        float cs = p.cs; exitC = g.ExitCell;
        Blocks sol = new Blocks { cs = cs }, dec = new Blocks { cs = cs };
        // materiales
        mats = new Material[NM];
        mats[MDoor] = Surface("HotelDoor", Color.white, track);
        mats[MWood] = Surface("HotelWood", Color.white, track);
        mats[MBrass] = Plain(new Color(0.58f, 0.43f, 0.22f), 0.58f, 0.9f, track);
        mats[MNum] = Surface("HotelNumbers", Color.white, track);
        mats[MBed] = Surface("HotelFabric", new Color(0.58f, 0.11f, 0.11f), track);
        mats[MSheet] = Surface("HotelFabric", new Color(0.80f, 0.76f, 0.66f), track);
        mats[MCloth] = Surface("HotelFabric", new Color(0.88f, 0.85f, 0.76f), track);
        mats[MUph] = Surface("HotelFabric", new Color(0.24f, 0.34f, 0.24f), track);
        mats[MPaint] = Surface("HotelPaintings", Color.white, track);
        Texture2D sa = Resources.Load<Texture2D>("Elevator/V740/ElevSteel_a"), sn = Resources.Load<Texture2D>("Elevator/V740/ElevSteel_n"), sm = Resources.Load<Texture2D>("Elevator/V740/ElevSteel_m");
        mats[MSteel] = (Material)track(ProceduralAssets.CreateSurfaceMaterial(sa != null && sn != null && sm != null ? new SurfaceSet { albedo = sa, normal = sn, mask = sm, baked = true } : null, new Color(0.62f, 0.62f, 0.6f), 1f, 1f));
        mats[MIron] = Plain(new Color(0.07f, 0.065f, 0.06f), 0.35f, 0.6f, track);
        mats[MPlant] = Plain(new Color(0.20f, 0.13f, 0.06f), 0.08f, 0f, track);
        mats[MPot] = Plain(new Color(0.30f, 0.15f, 0.09f), 0.25f, 0f, track);
        mats[MCrystal] = Plain(new Color(0.55f, 0.55f, 0.50f), 0.92f, 0.3f, track);
        mats[MSvc] = Surface("HotelService", Color.white, track);
        mats[MLino] = Surface("HotelLino", Color.white, track);
        mats[MMarble] = Plain(new Color(0.50f, 0.47f, 0.42f), 0.55f, 0f, track);
        mats[MDebris] = Plain(new Color(0.33f, 0.30f, 0.25f), 0.05f, 0f, track);
        mats[MBlack] = Plain(new Color(0.022f, 0.02f, 0.018f), 0.75f, 0f, track);
        mats[MGlass] = Plain(new Color(0.05f, 0.06f, 0.08f), 0.9f, 0f, track); ProceduralAssets.SetEmission(mats[MGlass], new Color(0.10f, 0.15f, 0.26f));   // noche fuera: cristal con luna
        Color warm = cfg.emissionColor;
        shadeOn = Plain(new Color(0.9f, 0.78f, 0.55f), 0.1f, 0f, track); ProceduralAssets.SetEmission(shadeOn, warm * 0.55f);
        shadeOff = Plain(new Color(0.42f, 0.36f, 0.27f), 0.08f, 0f, track);
        bulbOn = Plain(new Color(1f, 0.9f, 0.7f), 0.6f, 0f, track); ProceduralAssets.SetEmission(bulbOn, warm * 0.9f);
        bulbOff = Plain(new Color(0.35f, 0.33f, 0.3f), 0.8f, 0f, track);

        Transform lampRoot = new GameObject("HotelLamps").transform; lampRoot.SetParent(decorRoot, false);
        Transform leafRoot = new GameObject("HotelDoorLeaves").transform; leafRoot.SetParent(decorRoot, false);

        // puertas
        foreach (HotelPlan.Edge e in p.fakeDoors)
        {
            if (e.c == exitC) continue;
            FakeDoor(dec.At(p.Center(e.c.x, e.c.y)), e);
        }
        foreach (HotelPlan.Edge e in p.doorways) DoorwayLeaf(dec.At(p.Center(e.c.x, e.c.y)), e, leafRoot);
        foreach (HotelPlan.Edge e in p.liftDoors) LiftDoor(dec.At(p.Center(e.c.x, e.c.y)), e, r);
        // acabado de servicio
        ServiceFinish(dec);
        // objetos
        foreach (HotelPlan.Item it in p.items)
        {
            Vector2Int c = new Vector2Int(Mathf.FloorToInt(it.p.x / cs), Mathf.FloorToInt(it.p.z / cs));
            if (c == exitC) continue;
            E e = new E { b = (it.solid ? sol : dec).At(it.p) };
            Quaternion q = Quaternion.Euler(0f, it.yaw, 0f);
            Vector3 pos = it.p;
            if (it.k == HotelPlan.IChair && it.tilt > 1f) { q = q * Quaternion.Euler(-it.tilt, 0f, 0f); pos += Vector3.up * 0.24f; }
            if (it.k == HotelPlan.IFallenFrame) q = q * Quaternion.Euler(-(90f - it.tilt), 0f, 0f);
            if (it.k == HotelPlan.IPainting) q = q * Quaternion.Euler(0f, 0f, ((it.v * 37 + Mathf.RoundToInt(it.p.x * 13f)) % 7 - 3) * 0.7f);
            e.M = Matrix4x4.TRS(pos, q, Vector3.one);
            int v0 = e.b.verts.Count;
            BuildItem(e, it, r, lampRoot, cfg);
            ItemCount++;
            if (it.solid)
            {   // huella real (por debajo de 1,9 m) frente a la del plano: lo que el NavMesh ve de verdad
                Matrix4x4 inv = Matrix4x4.TRS(it.p, Quaternion.Euler(0f, it.yaw, 0f), Vector3.one).inverse;
                float ex = 0f, ez = 0f;
                for (int vi = v0; vi < e.b.verts.Count; vi++) { Vector3 lv = inv.MultiplyPoint3x4(e.b.verts[vi]); if (lv.y > 1.9f || lv.y < 0.15f) continue; ex = Mathf.Max(ex, Mathf.Abs(lv.x)); ez = Mathf.Max(ez, Mathf.Abs(lv.z)); }
                float ox = ex - it.s.x * 0.5f, oz = ez - it.s.z * 0.5f;
                if (!Over.ContainsKey(it.k)) Over[it.k] = Vector2.zero;
                Over[it.k] = new Vector2(Mathf.Max(Over[it.k].x, ox), Mathf.Max(Over[it.k].y, oz));
            }
        }
        // mallas
        Material[] all = mats;
        Emit(sol, levelRoot, "HotelSolid", true, all);
        Emit(dec, decorRoot, "HotelDecor", false, all);
    }

    static void Emit(Blocks bl, Transform parent, string name, bool collide, Material[] all)
    {
        foreach (var kv in bl.map)
        {
            MeshBuilder b = kv.Value; if (b.IsEmpty) continue;
            List<int> subs = new List<int>(); for (int i = 0; i < NM; i++) if (!b.SubEmpty(i)) subs.Add(i);
            Mesh m = new Mesh { name = name + kv.Key };
            if (b.verts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(b.verts); m.SetNormals(b.normals); m.SetTangents(b.tangents); m.SetUVs(0, b.uvs);
            m.subMeshCount = subs.Count; Material[] ms = new Material[subs.Count];
            for (int i = 0; i < subs.Count; i++) { m.SetTriangles(b.tris[subs[i]], i); ms[i] = all[subs[i]]; }
            m.RecalculateBounds(); trk(m);
            GameObject go = new GameObject(name + "_" + kv.Key); go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = ms;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            if (collide) go.AddComponent<MeshCollider>().sharedMesh = m;
        }
    }

    // marco del muro de la celda c, lado d: origen en el suelo sobre la cara interior del muro, +z hacia dentro de la celda, +x a lo largo
    static Matrix4x4 WallFrame(Vector2Int c, int d, float along, float inset)
    {
        Vector3 dir = HotelPlan.Dir3(d), n = -dir, u = new Vector3(-n.z, 0f, n.x);
        Vector3 o = plan.Center(c.x, c.y) + dir * (plan.cs * 0.5f - plan.t * 0.5f - inset) + u * along;
        return Matrix4x4.TRS(o, Quaternion.LookRotation(n, Vector3.up), Vector3.one);
    }

    // ------------------------------------------------------------------ puertas
    static void Slab(E e, float w, float hgt, float th, int number, bool numbered, bool frontOnly)
    {
        // hoja de x = 0..w, y = 0..hgt, z = -th/2..th/2; cara delantera (+z) con la textura entera
        Vector3 F = Vector3.forward, R = Vector3.right, U = Vector3.up; float z1 = th * 0.5f, z0 = -th * 0.5f;
        e.QuadUV(MDoor, new Vector3(0, 0, z1), new Vector3(w, 0, z1), new Vector3(w, hgt, z1), new Vector3(0, hgt, z1), F, R, U, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        if (!frontOnly)
        {
            e.QuadUV(MDoor, new Vector3(w, 0, z0), new Vector3(0, 0, z0), new Vector3(0, hgt, z0), new Vector3(w, hgt, z0), -F, -R, U, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            e.Box(MWood, new Vector3(0.005f, hgt * 0.5f, 0), new Vector3(0.01f, hgt, th * 0.98f));
            e.Box(MWood, new Vector3(w - 0.005f, hgt * 0.5f, 0), new Vector3(0.01f, hgt, th * 0.98f));
            e.Box(MWood, new Vector3(w * 0.5f, hgt - 0.005f, 0), new Vector3(w, 0.01f, th * 0.98f));
        }
        // manillas de latón (las dos caras), placa y palanca
        float hx = w - 0.09f;
        for (int s = 0; s < (frontOnly ? 1 : 2); s++)
        {
            float sg = s == 0 ? 1f : -1f, zf = sg * (th * 0.5f);
            e.Box(MBrass, new Vector3(hx, 1.02f, zf + sg * 0.004f), new Vector3(0.05f, 0.22f, 0.008f));
            e.Box(MBrass, new Vector3(hx, 0.98f, zf + sg * 0.04f), new Vector3(0.022f, 0.022f, 0.07f));
            e.Box(MBrass, new Vector3(hx - 0.055f, 0.98f, zf + sg * 0.072f), new Vector3(0.13f, 0.02f, 0.02f));
        }
        if (numbered)
        {
            string s = number.ToString("000"); float cw = 0.055f, chh = 0.11f; Vector3 pc = new Vector3(w * 0.5f, 1.58f, z1 + 0.004f);
            for (int i = 0; i < 3; i++)
            {
                int dg = s[i] - '0'; Vector3 c0 = pc + new Vector3((i - 1) * cw - cw * 0.5f, -chh * 0.5f, 0f);
                float u0 = dg / 10f + 0.004f, u1 = (dg + 1) / 10f - 0.004f;
                e.QuadUV(MNum, c0, c0 + new Vector3(cw, 0, 0), c0 + new Vector3(cw, chh, 0), c0 + new Vector3(0, chh, 0), F, R, U, new Vector2(u0, 0.03f), new Vector2(u1, 0.03f), new Vector2(u1, 0.97f), new Vector2(u0, 0.97f));
            }
            e.Box(MBrass, new Vector3(w * 0.5f, 1.43f, z1 + 0.006f), new Vector3(0.022f, 0.022f, 0.012f));
        }
    }
    static void Casing(E e, float w, float hgt, float y0)
    {   // marco (jambas y dintel) sobre la cara z=0 del muro, sobresale hacia +z
        float fw = 0.09f, fd = 0.03f;
        e.Box(MWood, new Vector3(-w * 0.5f - fw * 0.5f, y0 + (hgt + fw) * 0.5f, fd * 0.5f), new Vector3(fw, hgt + fw, fd));
        e.Box(MWood, new Vector3(w * 0.5f + fw * 0.5f, y0 + (hgt + fw) * 0.5f, fd * 0.5f), new Vector3(fw, hgt + fw, fd));
        e.Box(MWood, new Vector3(0f, y0 + hgt + fw * 0.5f, fd * 0.5f), new Vector3(w + 2f * fw + 0.04f, fw, fd + 0.01f));
    }
    static void FakeDoor(MeshBuilder b, HotelPlan.Edge ed)
    {
        E e = new E { b = b, M = WallFrame(ed.c, ed.d, 0f, 0f) };
        float y0 = ed.kind == HotelPlan.DoorFire ? 0.72f : 0f;
        if (ed.kind == HotelPlan.DoorFire)
        {   // puerta cortafuegos de chapa con barra antipánico (bloqueada con cadena)
            e.Box(MSteel, new Vector3(0, y0 + 1.0f, 0.02f), new Vector3(1.0f, 2.0f, 0.04f));
            e.Box(MIron, new Vector3(0, y0 + 1.0f, 0.06f), new Vector3(0.86f, 0.06f, 0.05f));
            for (int i = 0; i < 7; i++) e.BoxR(MIron, new Vector3(-0.3f + i * 0.1f, y0 + 1.0f + Mathf.Sin(i * 0.9f) * 0.03f, 0.1f), new Vector3(0.07f, 0.03f, 0.02f), Quaternion.Euler(0, 0, (i % 2) * 90f));
            e.Box(MBrass, new Vector3(0.25f, y0 + 0.92f, 0.11f), new Vector3(0.07f, 0.09f, 0.04f));
            e.Box(MBlack, new Vector3(0, y0 + 2.04f, 0.03f), new Vector3(0.36f, 0.12f, 0.03f));
            Casing(e, 1.0f, 2.0f, y0);
            return;
        }
        bool numbered = ed.kind == HotelPlan.DoorRoom;
        Matrix4x4 keep = e.M; e.M = keep * Matrix4x4.Translate(new Vector3(-LW * 0.5f, 0f, 0.022f));
        Slab(e, LW, LH, 0.044f, ed.number, numbered, true);
        e.M = keep;
        Casing(e, LW + 0.02f, LH + 0.01f, 0f);
        if (ed.kind == HotelPlan.DoorOffice) e.Box(MBrass, new Vector3(0, 1.6f, 0.05f), new Vector3(0.22f, 0.07f, 0.006f));
        if (numbered) { Doors++; DoorCenters.Add(e.M.MultiplyPoint3x4(new Vector3(0, 1.2f, 0))); DoorNormals.Add(e.M.MultiplyVector(Vector3.forward)); }
    }
    static void DoorwayLeaf(MeshBuilder b, HotelPlan.Edge ed, Transform leafRoot)
    {
        // lado del pasillo: la celda vecina (ed.c + d); la hoja va por la cara de la habitación/servicio y se abre hacia dentro
        Vector2Int cIn = ed.c, cOut = ed.c + new Vector2Int(Mathf.RoundToInt(HotelPlan.Dir3(ed.d).x), Mathf.RoundToInt(HotelPlan.Dir3(ed.d).z));
        int dOut = (ed.d + 2) % 4;
        // marcos por las dos caras
        E e = new E { b = b, M = WallFrame(cIn, ed.d, 0f, 0f) }; Casing(e, DoorW, DoorH, 0f);
        // forro de las jambas dentro del grueso del muro
        float t = plan.t;
        e.Box(MWood, new Vector3(-DoorW * 0.5f + 0.012f, DoorH * 0.5f, -t * 0.5f), new Vector3(0.024f, DoorH, t + 0.004f));
        e.Box(MWood, new Vector3(DoorW * 0.5f - 0.012f, DoorH * 0.5f, -t * 0.5f), new Vector3(0.024f, DoorH, t + 0.004f));
        e.Box(MWood, new Vector3(0f, DoorH - 0.012f, -t * 0.5f), new Vector3(DoorW, 0.024f, t + 0.004f));
        E e2 = new E { b = b, M = WallFrame(cOut, dOut, 0f, 0f) }; Casing(e2, DoorW, DoorH, 0f);
        bool numbered = ed.kind == HotelPlan.DoorRoom;
        // hoja con bisagra: pivote en el borde, la cara numerada mira al pasillo (+z del pivote)
        Matrix4x4 wf = WallFrame(cOut, dOut, 0f, 0f);   // +z hacia el pasillo, cara del muro del lado del pasillo
        Vector3 nCorr = wf.MultiplyVector(Vector3.forward), uAx = wf.MultiplyVector(Vector3.right);
        Vector3 hinge = wf.MultiplyPoint3x4(new Vector3(-LW * 0.5f, 0f, -t + 0.03f));
        GameObject pv = new GameObject("DoorLeaf_" + ed.number); pv.transform.SetParent(leafRoot, false);
        pv.transform.localPosition = hinge; pv.transform.localRotation = Quaternion.LookRotation(nCorr, Vector3.up);
        MeshBuilder lb = new MeshBuilder(NM); E le = new E { b = lb };
        Slab(le, LW, LH, 0.044f, ed.number, numbered, false);
        List<int> subs = new List<int>(); for (int i = 0; i < NM; i++) if (!lb.SubEmpty(i)) subs.Add(i);
        Mesh m = new Mesh { name = "DoorLeaf" }; m.SetVertices(lb.verts); m.SetNormals(lb.normals); m.SetTangents(lb.tangents); m.SetUVs(0, lb.uvs);
        m.subMeshCount = subs.Count; Material[] ms = new Material[subs.Count];
        for (int i = 0; i < subs.Count; i++) { m.SetTriangles(lb.tris[subs[i]], i); ms[i] = mats[subs[i]]; }
        m.RecalculateBounds(); trk(m);
        GameObject lg = new GameObject("Leaf"); lg.transform.SetParent(pv.transform, false);
        lg.AddComponent<MeshFilter>().sharedMesh = m; MeshRenderer mr = lg.AddComponent<MeshRenderer>(); mr.sharedMaterials = ms;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        BoxCollider bc = pv.AddComponent<BoxCollider>(); bc.center = new Vector3(LW * 0.5f, LH * 0.5f, 0f); bc.size = new Vector3(LW, LH, 0.06f);
        HotelDoorLeaf hl = pv.AddComponent<HotelDoorLeaf>(); hl.blocker = bc;
        hl.center = gen.Root.TransformPoint(wf.MultiplyPoint3x4(new Vector3(0f, 0f, -t * 0.5f)));
        Leaves.Add(hl);
        if (numbered) { Doors++; OpenDoors++; DoorCenters.Add(wf.MultiplyPoint3x4(new Vector3(0, 1.2f, 0))); DoorNormals.Add(nCorr); }
    }
    static void LiftDoor(MeshBuilder b, HotelPlan.Edge ed, System.Random r)
    {
        E e = new E { b = b, M = WallFrame(ed.c, ed.d, 0f, 0f) };
        float w = 1.1f, hgt = 2.15f; bool pried = r.NextDouble() < 0.3; float gap = pried ? 0.10f : 0.004f;
        float lw = (w - gap) * 0.5f;
        e.Box(MSteel, new Vector3(-gap * 0.5f - lw * 0.5f, hgt * 0.5f, 0.012f), new Vector3(lw, hgt, 0.024f), 0.8f);
        e.Box(MSteel, new Vector3(gap * 0.5f + lw * 0.5f, hgt * 0.5f, 0.012f), new Vector3(lw, hgt, 0.024f), 0.8f);
        e.Box(MBlack, new Vector3(0, hgt * 0.5f, 0.004f), new Vector3(gap + 0.01f, hgt, 0.006f));
        // marco de latón y dintel con indicador de planta muerto
        e.Box(MBrass, new Vector3(-w * 0.5f - 0.05f, (hgt + 0.1f) * 0.5f, 0.025f), new Vector3(0.1f, hgt + 0.1f, 0.05f));
        e.Box(MBrass, new Vector3(w * 0.5f + 0.05f, (hgt + 0.1f) * 0.5f, 0.025f), new Vector3(0.1f, hgt + 0.1f, 0.05f));
        e.Box(MBrass, new Vector3(0, hgt + 0.05f, 0.025f), new Vector3(w + 0.2f, 0.1f, 0.05f));
        e.Box(MBrass, new Vector3(0, hgt + 0.30f, 0.02f), new Vector3(0.5f, 0.22f, 0.04f));
        e.Box(MBlack, new Vector3(0, hgt + 0.30f, 0.042f), new Vector3(0.42f, 0.14f, 0.004f));
        e.Box(MBrass, new Vector3(0.12f, hgt + 0.36f, 0.046f), new Vector3(0.012f, 0.05f, 0.004f));
        // botonera
        e.Box(MBrass, new Vector3(w * 0.5f + 0.32f, 1.12f, 0.008f), new Vector3(0.12f, 0.26f, 0.016f));
        e.Cyl(MBlack, new Vector3(w * 0.5f + 0.32f, 1.17f, 0.016f), 0.022f, 0.022f, 0.01f, 10, true, false);
        e.Cyl(MBlack, new Vector3(w * 0.5f + 0.32f, 1.07f, 0.016f), 0.022f, 0.022f, 0.01f, 10, true, false);
    }

    static void ServiceFinish(Blocks dec)
    {
        float cs = plan.cs, t = plan.t, wh = plan.wh, hl = cs * 0.5f - t * 0.5f;
        for (int x = 0; x < plan.w; x++) for (int z = 0; z < plan.h; z++)
        {
            HotelPlan.K k = plan.kind[x, z];
            if (k != HotelPlan.K.Linen && k != HotelPlan.K.Stair) continue;
            Vector3 c = plan.Center(x, z); MeshBuilder b = dec.At(c);
            b.AddQuad(MLino, c + new Vector3(-cs * 0.5f, 0.004f, -cs * 0.5f), c + new Vector3(cs * 0.5f, 0.004f, -cs * 0.5f), c + new Vector3(cs * 0.5f, 0.004f, cs * 0.5f), c + new Vector3(-cs * 0.5f, 0.004f, cs * 0.5f),
                Vector3.up, Vector3.right, Vector3.forward, 2f, 2f, 0f, 0f);
            for (int d = 0; d < 4; d++)
            {
                bool open = plan.IsOpen(x, z, d);
                Vector2Int n = new Vector2Int(x, z) + new Vector2Int(Mathf.RoundToInt(HotelPlan.Dir3(d).x), Mathf.RoundToInt(HotelPlan.Dir3(d).z));
                HotelPlan.K kn = plan.At(n.x, n.y);
                bool svcNeighbour = kn == HotelPlan.K.Linen || kn == HotelPlan.K.Stair;
                if (open && svcNeighbour) continue;
                Matrix4x4 f = WallFrame(new Vector2Int(x, z), d, 0f, 0.008f);
                E e = new E { b = b, M = f };
                if (!open) SvcQuad(e, -hl, hl, 0f, wh);
                else
                {   // hueco de puerta de personal: paneles a los lados y encima
                    bool isDoor = false; foreach (HotelPlan.Edge dw in plan.doorways) if ((dw.c == new Vector2Int(x, z) && dw.d == d) || (dw.c == n && dw.d == (d + 2) % 4)) isDoor = true;
                    if (!isDoor) continue;
                    SvcQuad(e, -hl, -DoorW * 0.5f, 0f, wh); SvcQuad(e, DoorW * 0.5f, hl, 0f, wh); SvcQuad(e, -DoorW * 0.5f, DoorW * 0.5f, DoorH, wh);
                }
            }
        }
    }
    static void SvcQuad(E e, float a, float b, float y0, float y1)
    {
        float wh = plan.wh; Vector3 F = Vector3.forward, R = Vector3.right, U = Vector3.up;
        e.QuadUV(MSvc, new Vector3(a, y0, 0), new Vector3(b, y0, 0), new Vector3(b, y1, 0), new Vector3(a, y1, 0), F, R, U,
            new Vector2(a / 1.6f, y0 / wh), new Vector2(b / 1.6f, y0 / wh), new Vector2(b / 1.6f, y1 / wh), new Vector2(a / 1.6f, y1 / wh));
    }

    // ------------------------------------------------------------------ muebles y atrezo
    // cookie del haz de luna: 2 x 4 cristales con travesaños oscuros y borde suave (se ve el ventanal proyectado en la moqueta)
    static Texture2D winCookie;
    static Texture2D WindowCookie()
    {
        if (winCookie != null) return winCookie;
        const int N = 128; winCookie = new Texture2D(N, N, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp, name = "MoonWindowCookie" };
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float u = (x + 0.5f) / N, v = (y + 0.5f) / N;
            float ex = Mathf.Abs(u - 0.5f) * 2f, ey = Mathf.Abs(v - 0.5f) * 2f;
            float win = Mathf.Clamp01((0.86f - ex) / 0.08f) * Mathf.Clamp01((0.92f - ey) / 0.06f);   // silueta del ventanal
            float fu = Mathf.Repeat(u * 2f, 1f), fv = Mathf.Repeat(v * 4f, 1f);
            float bar = Mathf.Min(Mathf.Clamp01(Mathf.Min(fu, 1f - fu) / 0.05f), Mathf.Clamp01(Mathf.Min(fv, 1f - fv) / 0.08f));   // travesaños
            float a = win * Mathf.Lerp(0.18f, 1f, bar);
            px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
        }
        winCookie.SetPixels32(px); winCookie.Apply(false, true);
        return winCookie;
    }
    static float H(int v, int salt) { unchecked { uint n = (uint)(v * 374761393 + salt * 668265263); n = (n ^ (n >> 13)) * 1274126177u; return (n & 0xffff) / 65535f; } }

    static void Chair(E e, int v)
    {
        int fr = v == 1 ? MBrass : MWood, seat = v == 1 ? MBed : MUph;
        foreach (float sx in new[] { -0.19f, 0.19f }) foreach (float sz in new[] { -0.19f, 0.19f }) e.Box(fr, new Vector3(sx, 0.22f, sz), new Vector3(0.035f, 0.44f, 0.035f));
        e.Box(seat, new Vector3(0, 0.465f, 0), new Vector3(0.46f, 0.06f, 0.46f), 0.5f);
        foreach (float sx in new[] { -0.19f, 0.19f }) e.Box(fr, new Vector3(sx, 0.72f, -0.205f), new Vector3(0.035f, 0.52f, 0.035f));
        if (v == 1) { for (int i = 0; i < 3; i++) e.Box(fr, new Vector3(0, 0.62f + i * 0.12f, -0.205f), new Vector3(0.38f, 0.02f, 0.02f)); }
        else e.Box(seat, new Vector3(0, 0.75f, -0.21f), new Vector3(0.34f, 0.34f, 0.03f), 0.5f);
        e.Box(fr, new Vector3(0, 0.975f, -0.205f), new Vector3(0.44f, 0.045f, 0.04f));
    }
    static void Upholstered(E e, float w, float d)
    {
        e.Box(MWood, new Vector3(0, 0.04f, 0), new Vector3(w - 0.06f, 0.08f, d - 0.06f));
        e.Box(MUph, new Vector3(0, 0.25f, 0.02f), new Vector3(w, 0.3f, d - 0.04f), 0.5f);
        int nc = w > 1.2f ? 3 : 1; float cw = (w - 0.28f) / nc;
        for (int i = 0; i < nc; i++) e.Box(MUph, new Vector3(-w * 0.5f + 0.14f + cw * (i + 0.5f), 0.45f, 0.07f), new Vector3(cw - 0.02f, 0.11f, d - 0.24f), 0.5f);
        e.Box(MUph, new Vector3(0, 0.64f, -d * 0.5f + 0.09f), new Vector3(w, 0.5f, 0.18f), 0.5f);
        e.Box(MUph, new Vector3(-w * 0.5f + 0.07f, 0.52f, 0.02f), new Vector3(0.14f, 0.24f, d - 0.06f), 0.5f);
        e.Box(MUph, new Vector3(w * 0.5f - 0.07f, 0.52f, 0.02f), new Vector3(0.14f, 0.24f, d - 0.06f), 0.5f);
    }
    static void Frame(E e, Vector3 s, int v, float y0)
    {
        float fw = 0.06f, dp = s.z;
        e.Box(MBrass, new Vector3(0, y0 + s.y * 0.5f - fw * 0.5f, 0), new Vector3(s.x, fw, dp));
        e.Box(MBrass, new Vector3(0, y0 - s.y * 0.5f + fw * 0.5f, 0), new Vector3(s.x, fw, dp));
        e.Box(MBrass, new Vector3(-s.x * 0.5f + fw * 0.5f, y0, 0), new Vector3(fw, s.y - 2f * fw, dp));
        e.Box(MBrass, new Vector3(s.x * 0.5f - fw * 0.5f, y0, 0), new Vector3(fw, s.y - 2f * fw, dp));
        float u0 = (v % 4) / 4f, u1 = u0 + 0.25f, v1 = 1f - (v / 4 % 2) * 0.5f, v0 = v1 - 0.5f;
        float hx = s.x * 0.5f - fw, hy = s.y * 0.5f - fw;
        // recorte al formato del lienzo (el atlas es vertical 0,8): cuadros apaisados usan la franja central
        float asp = (hx / hy) / 0.8f; if (asp > 1f) { float dv = (v1 - v0) * (1f - 1f / asp) * 0.5f; v0 += dv; v1 -= dv; } else { float du = (u1 - u0) * (1f - asp) * 0.5f; u0 += du; u1 -= du; }
        e.QuadUV(MPaint, new Vector3(-hx, y0 - hy, dp * 0.2f), new Vector3(hx, y0 - hy, dp * 0.2f), new Vector3(hx, y0 + hy, dp * 0.2f), new Vector3(-hx, y0 + hy, dp * 0.2f),
            Vector3.forward, Vector3.right, Vector3.up, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1));
    }
    static void Lamp(Transform root, Vector3 local, bool on, float intensity, float range, float y, Vector3 scale, PrimitiveType pt)
    {
        GameObject sh = ProceduralAssets.CreatePrimitive(pt, "LampShade", root, local + Vector3.up * y, scale, on ? shadeOn : shadeOff, false);
        if (!on) return;
        LiveLamps++;
        gen.AddHotelLight(sh.transform.parent, local + Vector3.up * (y - 0.05f), sh.GetComponent<Renderer>(), intensity, range);
    }

    static void BuildItem(E e, HotelPlan.Item it, System.Random r, Transform lampRoot, LevelConfig cfg)
    {
        float wh = plan.wh; int v = it.v;
        switch (it.k)
        {
            case HotelPlan.IBed:
            {
                e.Box(MWood, new Vector3(0, 0.17f, 0), new Vector3(1.6f, 0.22f, 2.04f), 1f);
                e.Box(v == 2 ? MSheet : MSheet, new Vector3(0, 0.38f, 0.01f), new Vector3(1.52f, 0.2f, 1.98f), 0.5f);
                if (v != 2)
                {
                    float len = v == 1 ? 1.0f : 1.5f, zc = v == 1 ? 0.48f : 0.25f;
                    e.Box(MBed, new Vector3(0, 0.505f, zc), new Vector3(1.62f, 0.06f, len), 0.5f);
                    e.Box(MBed, new Vector3(-0.815f, 0.37f, zc), new Vector3(0.025f, 0.28f, len), 0.5f);
                    e.Box(MBed, new Vector3(0.815f, 0.37f, zc), new Vector3(0.025f, 0.28f, len), 0.5f);
                    e.Box(MBed, new Vector3(0, 0.37f, zc + len * 0.5f), new Vector3(1.66f, 0.28f, 0.025f), 0.5f);
                    if (v == 3) e.BoxR(MBed, new Vector3(1.15f, 0.04f, 0.2f), new Vector3(0.7f, 0.06f, 1.3f), Quaternion.Euler(0, 8f, 0), 0.5f);
                }
                e.BoxR(MSheet, new Vector3(-0.37f, 0.54f, -0.76f), new Vector3(0.6f, 0.13f, 0.38f), Quaternion.Euler(-12f, 4f, 0), 0.5f);
                if (v != 1) e.BoxR(MSheet, new Vector3(0.38f, 0.54f, -0.75f), new Vector3(0.6f, 0.13f, 0.38f), Quaternion.Euler(-10f, -6f, 0), 0.5f);
                e.Box(MWood, new Vector3(0, 0.62f, -1.055f), new Vector3(1.74f, 1.24f, 0.07f));
                e.Box(MWood, new Vector3(0, 1.27f, -1.05f), new Vector3(1.84f, 0.06f, 0.1f));
                e.Box(MBed, new Vector3(0, 0.86f, -1.015f), new Vector3(1.4f, 0.5f, 0.03f), 0.5f);
                break;
            }
            case HotelPlan.INight:
                e.Box(MWood, new Vector3(0, 0.28f, 0), new Vector3(0.46f, 0.56f, 0.42f));
                e.Box(MWood, new Vector3(0, 0.40f, 0.215f), new Vector3(0.40f, 0.15f, 0.012f));
                e.Box(MBrass, new Vector3(0, 0.40f, 0.226f), new Vector3(0.05f, 0.015f, 0.012f));
                break;
            case HotelPlan.ILamp:
            {
                e.Cyl(MBrass, Vector3.zero, 0.075f, 0.06f, 0.035f, 12, true, false);
                e.Cyl(MBrass, new Vector3(0, 0.035f, 0), 0.016f, 0.016f, 0.22f, 8, false, false);
                e.Cyl(MBrass, new Vector3(0, 0.12f, 0), 0.05f, 0.04f, 0.08f, 10, false, false);
                Lamp(lampRoot, it.p, v == 1, 0.5f, 3.6f, 0.36f, new Vector3(0.27f, 0.1f, 0.27f), PrimitiveType.Cylinder);
                break;
            }
            case HotelPlan.IWardrobe:
                e.Box(MWood, new Vector3(0, 1.0f, 0), new Vector3(1.15f, 2.0f, 0.6f));
                e.Box(MWood, new Vector3(0, 2.03f, 0.01f), new Vector3(1.22f, 0.06f, 0.64f));
                e.Box(MBlack, new Vector3(0, 1.02f, 0.301f), new Vector3(0.008f, 1.86f, 0.004f));
                foreach (float sx in new[] { -0.28f, 0.28f })
                {
                    e.Box(MWood, new Vector3(sx, 1.38f, 0.305f), new Vector3(0.42f, 0.78f, 0.012f));
                    e.Box(MWood, new Vector3(sx, 0.52f, 0.305f), new Vector3(0.42f, 0.66f, 0.012f));
                    e.Box(MBrass, new Vector3(sx * 0.16f, 1.02f, 0.315f), new Vector3(0.016f, 0.16f, 0.016f));
                }
                if (v == 1) e.BoxR(MSheet, new Vector3(0.2f, 0.06f, 0.55f), new Vector3(0.5f, 0.05f, 0.35f), Quaternion.Euler(0, 20, 0), 0.5f);
                break;
            case HotelPlan.IDesk:
            {
                e.Box(MWood, new Vector3(0, 0.74f, 0), new Vector3(1.1f, 0.04f, 0.55f));
                foreach (float sx in new[] { -0.51f, 0.51f }) foreach (float sz in new[] { -0.23f, 0.23f }) e.Box(MWood, new Vector3(sx, 0.36f, sz), new Vector3(0.045f, 0.72f, 0.045f));
                e.Box(MWood, new Vector3(0.3f, 0.63f, 0), new Vector3(0.44f, 0.16f, 0.5f));
                e.Box(MBrass, new Vector3(0.3f, 0.63f, 0.255f), new Vector3(0.06f, 0.015f, 0.012f));
                e.Box(MSheet, new Vector3(-0.2f, 0.765f, 0.02f), new Vector3(0.22f, 0.01f, 0.3f), 0.5f);
                Matrix4x4 keep = e.M; e.M = keep * Matrix4x4.TRS(new Vector3(-0.1f, 0, 0.24f), Quaternion.Euler(0, 180f + 8f, 0), Vector3.one); Chair(e, 0);   // metida bajo la mesa (deja paso al NavMesh) e.M = keep;
                break;
            }
            case HotelPlan.IChair: Chair(e, v); break;
            case HotelPlan.IArmchair: Upholstered(e, 0.78f, 0.78f); break;
            case HotelPlan.ISofa: Upholstered(e, 2.1f, 0.85f); break;
            case HotelPlan.ICoffee:
                e.Box(MWood, new Vector3(0, 0.40f, 0), new Vector3(1.2f, 0.04f, 0.7f));
                e.Box(MMarble, new Vector3(0, 0.421f, 0), new Vector3(1.0f, 0.004f, 0.5f));
                foreach (float sx in new[] { -0.55f, 0.55f }) foreach (float sz in new[] { -0.3f, 0.3f }) e.Box(MWood, new Vector3(sx, 0.19f, sz), new Vector3(0.05f, 0.38f, 0.05f));
                break;
            case HotelPlan.IPainting: Frame(e, it.s, v, 0f); break;
            case HotelPlan.IWindow:
            {   // ventanal: cristal (luna), marco y travesaños de caoba, alféizar, cortinas raídas a los lados
                float W = it.s.x, Hh = it.s.y, y0 = 0.42f, yc = y0 + Hh * 0.5f;
                e.Box(MGlass, new Vector3(0f, yc, 0.006f), new Vector3(W - 0.08f, Hh - 0.08f, 0.008f));
                e.Box(MWood, new Vector3(-W * 0.5f + 0.04f, yc, 0.04f), new Vector3(0.08f, Hh, 0.08f));
                e.Box(MWood, new Vector3(W * 0.5f - 0.04f, yc, 0.04f), new Vector3(0.08f, Hh, 0.08f));
                e.Box(MWood, new Vector3(0f, y0 + Hh - 0.04f, 0.04f), new Vector3(W, 0.08f, 0.08f));
                e.Box(MWood, new Vector3(0f, y0 - 0.02f, 0.09f), new Vector3(W + 0.16f, 0.05f, 0.18f));
                e.Box(MWood, new Vector3(0f, yc, 0.02f), new Vector3(0.035f, Hh - 0.1f, 0.03f));
                for (int k = 1; k <= 3; k++) e.Box(MWood, new Vector3(0f, y0 + Hh * k / 4f, 0.02f), new Vector3(W - 0.1f, 0.03f, 0.03f));
                float ct = 0.05f, cy = Mathf.Min(wh - 0.05f, y0 + Hh + 0.25f);
                for (int sd = -1; sd <= 1; sd += 2)
                {
                    float sway = H(sd + 5, v + Mathf.RoundToInt(it.p.x)) * 0.12f;
                    e.BoxR(MCloth, new Vector3(sd * (W * 0.5f + 0.12f), cy * 0.5f + 0.02f, 0.1f + ct), new Vector3(0.42f, cy - 0.04f, 0.07f), Quaternion.Euler(0f, sd * (4f + sway * 20f), 0f));
                }
                e.Box(MBrass, new Vector3(0f, cy + 0.01f, 0.12f), new Vector3(W + 0.9f, 0.03f, 0.03f));
                if (v == 1)
                {   // haz de luna: foco frío desde el cristal hacia el suelo de la sala
                    Vector3 lp = e.M.MultiplyPoint3x4(new Vector3(0f, y0 + Hh - 0.2f, 0.25f)), ld = e.M.MultiplyVector(new Vector3(0f, -0.70f, 1f)).normalized;
                    gen.AddHotelSpot(lampRoot, lp, ld, new Color(0.60f, 0.68f, 0.92f), 3.0f, 18f, 52f, WindowCookie());   // v7.5.0: el dibujo del ventanal en el suelo, tenue (alcance 18 m y algo más inclinada, para no atravesar la sala; con 13 m la caída de Unity dejaba el suelo a ~17 %)
                }
                break;
            }
            case HotelPlan.IFallenFrame: Frame(e, it.s, v, it.s.y * 0.5f); break;
            case HotelPlan.IPlant:
            {
                e.Cyl(MPot, Vector3.zero, 0.16f, 0.22f, 0.46f, 12, false, false);
                e.Cyl(MPot, new Vector3(0, 0.44f, 0), 0.235f, 0.235f, 0.05f, 12, false, false);
                e.Quad(MPlant, new Vector3(-0.2f, 0.42f, -0.2f), new Vector3(0.2f, 0.42f, -0.2f), new Vector3(0.2f, 0.42f, 0.2f), new Vector3(-0.2f, 0.42f, 0.2f), Vector3.up, Vector3.right, Vector3.forward, 1f);
                int n = v == 2 ? 3 : 6; float ht = it.s.y;
                for (int i = 0; i < n; i++)
                {
                    float a = (i * 137.5f + v * 40f) * Mathf.Deg2Rad, lean = 8f + 22f * H(i, v + 3);
                    Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    float len = ht * (0.5f + 0.5f * H(i, v + 7));
                    Quaternion q = Quaternion.AngleAxis(lean, Vector3.Cross(Vector3.up, dir));
                    Vector3 top = new Vector3(0, 0.44f, 0) + q * Vector3.up * len;
                    e.Tube(MPlant, new Vector3(0, 0.44f, 0), top, 0.008f);
                    int nl = v == 1 ? 3 : 4;
                    for (int l = 0; l < nl; l++)
                    {   // hojas secas colgando
                        Vector3 at = Vector3.Lerp(new Vector3(0, 0.44f, 0), top, 0.45f + 0.55f * l / nl);
                        float la = a + (l % 2 == 0 ? 0.9f : -0.9f); Vector3 ld = new Vector3(Mathf.Cos(la), 0, Mathf.Sin(la));
                        float ll = v == 1 ? 0.42f : 0.16f;
                        Vector3 tip = at + ld * ll * 0.7f + Vector3.down * ll * 0.7f, side = Vector3.Cross(ld, Vector3.up) * (v == 1 ? 0.03f : 0.045f);
                        e.Quad(MPlant, at - side, at + side, tip + side * 0.2f, tip - side * 0.2f, Vector3.Cross(side, tip - at).normalized, side.normalized, (tip - at).normalized, 1f);
                        e.Quad(MPlant, at + side, at - side, tip - side * 0.2f, tip + side * 0.2f, -Vector3.Cross(side, tip - at).normalized, -side.normalized, (tip - at).normalized, 1f);
                    }
                }
                // hojas caídas en el suelo
                for (int i = 0; i < 4; i++)
                {
                    float a = (i * 90f + v * 23f) * Mathf.Deg2Rad; Vector3 c = new Vector3(Mathf.Cos(a), 0.006f, Mathf.Sin(a)) * (0.32f + 0.15f * H(i, v));
                    c.y = 0.006f; e.Quad(MPlant, c + new Vector3(-0.05f, 0, -0.02f), c + new Vector3(0.05f, 0, -0.02f), c + new Vector3(0.05f, 0, 0.02f), c + new Vector3(-0.05f, 0, 0.02f), Vector3.up, Vector3.right, Vector3.forward, 1f);
                }
                break;
            }
            case HotelPlan.ICart:
            {
                e.Box(MCloth, new Vector3(0, 0.78f, 0), new Vector3(0.9f, 0.03f, 0.55f), 0.5f);
                e.Box(MCloth, new Vector3(0, 0.52f, 0.285f), new Vector3(0.92f, 0.5f, 0.01f), 0.5f);
                e.Box(MCloth, new Vector3(0, 0.52f, -0.285f), new Vector3(0.92f, 0.5f, 0.01f), 0.5f);
                foreach (float sx in new[] { -0.42f, 0.42f }) foreach (float sz in new[] { -0.24f, 0.24f })
                { e.Box(MBrass, new Vector3(sx, 0.42f, sz), new Vector3(0.02f, 0.74f, 0.02f)); e.Cyl(MIron, new Vector3(sx, 0f, sz), 0.035f, 0.035f, 0.05f, 8, true, false); }
                e.Cyl(MSteel, new Vector3(0.18f, 0.795f, 0.02f), 0.17f, 0.17f, 0.012f, 14, true, false);
                e.Cyl(MSteel, new Vector3(0.18f, 0.807f, 0.02f), 0.15f, 0.04f, 0.13f, 14, true, false);
                e.Cyl(MCloth, new Vector3(-0.22f, 0.795f, -0.05f), 0.12f, 0.12f, 0.01f, 12, true, false);
                e.BoxR(MCrystal, new Vector3(-0.25f, 0.83f, 0.14f), new Vector3(0.06f, 0.06f, 0.14f), Quaternion.Euler(0, 30, 0));
                break;
            }
            case HotelPlan.IReception:
            {
                float L = it.s.x;
                e.Box(MWood, new Vector3(0, 0.55f, 0), new Vector3(L, 1.1f, 0.7f));
                for (int i = 0; i < 5; i++) e.Box(MWood, new Vector3(-L * 0.5f + L * (i + 0.5f) / 5f, 0.56f, 0.355f), new Vector3(L / 5f - 0.12f, 0.75f, 0.018f));
                e.Box(MMarble, new Vector3(0, 1.125f, 0.06f), new Vector3(L + 0.1f, 0.05f, 0.88f));
                e.Box(MBrass, new Vector3(0, 0.1f, 0.36f), new Vector3(L, 0.04f, 0.015f));
                e.Box(MBrass, new Vector3(0, 1.08f, 0.36f), new Vector3(L, 0.03f, 0.015f));
                e.Box(MWood, new Vector3(0, 0.78f, -0.6f), new Vector3(L - 0.2f, 0.04f, 0.5f));
                e.Cyl(MBrass, new Vector3(0.7f, 1.15f, 0.15f), 0.05f, 0.012f, 0.05f, 12, true, false);
                e.Box(MBed, new Vector3(-0.6f, 1.17f, 0.1f), new Vector3(0.42f, 0.04f, 0.3f), 0.5f);
                e.Box(MSheet, new Vector3(-0.6f, 1.192f, 0.1f), new Vector3(0.38f, 0.004f, 0.27f), 0.5f);
                e.Cyl(MBrass, new Vector3(1.5f, 1.15f, -0.05f), 0.07f, 0.05f, 0.02f, 10, true, false);
                e.Cyl(MBrass, new Vector3(1.5f, 1.17f, -0.05f), 0.012f, 0.012f, 0.3f, 6, false, false);
                e.BoxR(MUph, new Vector3(1.5f, 1.5f, 0.0f), new Vector3(0.36f, 0.08f, 0.14f), Quaternion.Euler(0, 0, 0), 0.5f);
                break;
            }
            case HotelPlan.IKeyBoard:
            {
                e.Box(MWood, new Vector3(0, 0, -0.06f), new Vector3(it.s.x, it.s.y, 0.12f));
                int nx = 10, ny = 5; float cw = (it.s.x - 0.12f) / nx, ch = (it.s.y - 0.12f) / ny;
                for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++)
                {
                    Vector3 c = new Vector3(-it.s.x * 0.5f + 0.06f + cw * (i + 0.5f), -it.s.y * 0.5f + 0.06f + ch * (j + 0.5f), 0.002f);
                    e.Quad(MBlack, c + new Vector3(-cw * 0.4f, -ch * 0.4f, 0), c + new Vector3(cw * 0.4f, -ch * 0.4f, 0), c + new Vector3(cw * 0.4f, ch * 0.4f, 0), c + new Vector3(-cw * 0.4f, ch * 0.4f, 0), Vector3.forward, Vector3.right, Vector3.up, 1f);
                    if (H(i * 7 + j, 11) < 0.45f) e.Box(MBrass, c + new Vector3(0, -ch * 0.15f, 0.01f), new Vector3(0.018f, 0.07f, 0.008f));
                }
                break;
            }
            case HotelPlan.IRoundTable:
            {
                e.Cyl(MCloth, Vector3.zero, 0.80f, 0.79f, 0.765f, 18, true, false, 0.5f);
                e.Cyl(MBrass, new Vector3(0, 0.765f, 0), 0.07f, 0.05f, 0.03f, 10, true, false);
                e.Cyl(MBrass, new Vector3(0, 0.795f, 0), 0.012f, 0.012f, 0.32f, 6, false, false);
                for (int i = 0; i < 3; i++) { float a = i * 2.094f; e.Cyl(MCloth, new Vector3(Mathf.Cos(a) * 0.08f, 1.06f, Mathf.Sin(a) * 0.08f), 0.012f, 0.012f, 0.09f - 0.03f * i, 6, true, false); }
                for (int i = 0; i < 6; i++) { if (H(i, v + 5) < 0.3f) continue; float a = i * 1.047f; e.Cyl(MCloth, new Vector3(Mathf.Cos(a) * 0.58f, 0.766f, Mathf.Sin(a) * 0.58f), 0.12f, 0.12f, 0.008f, 12, true, false); }
                if (v == 1) e.BoxR(MCrystal, new Vector3(0.3f, 0.79f, -0.2f), new Vector3(0.06f, 0.06f, 0.16f), Quaternion.Euler(0, 40, 0));
                break;
            }
            case HotelPlan.ISquareTable:
                e.Box(MCloth, new Vector3(0, 0.38f, 0), new Vector3(1.0f, 0.76f, 1.0f), 0.5f);
                e.Box(MCloth, new Vector3(0, 0.765f, 0), new Vector3(1.04f, 0.012f, 1.04f), 0.5f);
                for (int i = 0; i < 4; i++) { if (H(i, v + 9) < 0.35f) continue; Vector3 d = HotelPlan.Dir3(i) * 0.3f; e.Cyl(MCloth, new Vector3(d.x, 0.772f, d.z), 0.11f, 0.11f, 0.008f, 12, true, false); }
                e.Cyl(MBrass, new Vector3(0, 0.772f, 0), 0.04f, 0.03f, 0.02f, 8, true, false); e.Cyl(MCloth, new Vector3(0, 0.79f, 0), 0.012f, 0.012f, 0.07f, 6, true, false);
                break;
            case HotelPlan.IChandelier:
            case HotelPlan.ISmallChand:
            {
                float rad = it.s.x * 0.5f, drop = it.s.y; float top = wh, yb = wh - drop;
                e.Cyl(MBrass, new Vector3(0, top - 0.04f, 0), 0.10f, 0.08f, 0.04f, 12, false, true);
                e.Cyl(MBrass, new Vector3(0, yb + 0.25f, 0), 0.01f, 0.01f, drop - 0.29f, 6, false, false);
                e.Cyl(MBrass, new Vector3(0, yb, 0), 0.02f, 0.09f, 0.18f, 12, true, true);
                e.Cyl(MBrass, new Vector3(0, yb + 0.18f, 0), 0.09f, 0.03f, 0.07f, 12, true, false);
                int arms = it.k == HotelPlan.IChandelier ? 8 : 5;
                MeshBuilder bulbs = new MeshBuilder(1); E eb = new E { b = bulbs, M = Matrix4x4.Translate(it.p) };
                for (int i = 0; i < arms; i++)
                {
                    float a = i * Mathf.PI * 2f / arms; Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    Vector3 tip = d * rad * 0.8f + Vector3.up * (yb + 0.12f);
                    e.Tube(MBrass, new Vector3(0, yb + 0.08f, 0), tip - Vector3.up * 0.03f, 0.008f);
                    e.Cyl(MBrass, tip - Vector3.up * 0.03f, 0.03f, 0.035f, 0.03f, 8, true, true);
                    e.Cyl(MCloth, tip, 0.012f, 0.012f, 0.07f, 6, false, false);
                    eb.Cyl(0, tip + Vector3.up * 0.07f, 0.014f, 0.0f, 0.05f, 6, false, true);
                    for (int k = 0; k < 3; k++)
                    {   // lágrimas de cristal
                        Vector3 dp = d * rad * (0.35f + 0.22f * k) + Vector3.up * (yb - 0.02f - 0.05f * k);
                        e.BoxR(MCrystal, dp, new Vector3(0.025f, 0.06f, 0.025f), Quaternion.Euler(0, a * Mathf.Rad2Deg + 45f, 0));
                    }
                }
                e.Tube(MCrystal, new Vector3(0, yb - 0.01f, 0), new Vector3(0, yb - 0.12f, 0), 0.03f);
                Mesh bm = bulbs.ToMesh("ChandBulbs"); trk(bm);
                GameObject bg = new GameObject("ChandelierBulbs"); bg.transform.SetParent(lampRoot, false);
                bg.AddComponent<MeshFilter>().sharedMesh = bm; MeshRenderer br = bg.AddComponent<MeshRenderer>(); br.sharedMaterial = v == 1 ? bulbOn : bulbOff;
                br.shadowCastingMode = ShadowCastingMode.Off; br.receiveShadows = false;
                if (v == 1) { LiveLamps++; gen.AddHotelLight(lampRoot, it.p + Vector3.up * (yb + 0.05f), br, it.k == HotelPlan.IChandelier ? 1.4f : 0.9f, it.k == HotelPlan.IChandelier ? 9f : 5.5f); }
                break;
            }
            case HotelPlan.IColumn:
                e.Box(MMarble, new Vector3(0, 0.1f, 0), new Vector3(0.82f, 0.2f, 0.82f));
                e.Cyl(MMarble, new Vector3(0, 0.2f, 0), 0.3f, 0.3f, wh - 0.45f, 16, false, false);
                e.Cyl(MMarble, new Vector3(0, wh - 0.25f, 0), 0.3f, 0.38f, 0.1f, 16, false, false);
                e.Box(MMarble, new Vector3(0, wh - 0.075f, 0), new Vector3(0.86f, 0.15f, 0.86f));
                e.Box(MBrass, new Vector3(0, 0.21f, 0), new Vector3(0.66f, 0.02f, 0.66f));
                break;
            case HotelPlan.IStairs:
            {
                float L = it.s.z, W = it.s.x, land = 1.0f; int ns = 4; float rise = it.s.y / ns, run = 0.3f;
                float z0 = L * 0.5f - land - ns * run;
                for (int i = 0; i < ns; i++)
                {
                    float zs = z0 + i * run, ht = rise * (i + 1);
                    e.Box(MWood, new Vector3(0, ht * 0.5f, (zs + L * 0.5f - land) * 0.5f), new Vector3(W, ht, L * 0.5f - land - zs));
                    e.Quad(MBed, new Vector3(-0.55f, ht + 0.003f, zs), new Vector3(0.55f, ht + 0.003f, zs), new Vector3(0.55f, ht + 0.003f, zs + run), new Vector3(-0.55f, ht + 0.003f, zs + run), Vector3.up, Vector3.right, Vector3.forward, 0.5f);
                    e.Box(MBrass, new Vector3(0, ht - 0.01f, zs + 0.01f), new Vector3(1.12f, 0.02f, 0.025f));
                }
                e.Box(MWood, new Vector3(0, it.s.y * 0.5f, L * 0.5f - land * 0.5f), new Vector3(W, it.s.y, land));
                e.Quad(MBed, new Vector3(-0.55f, it.s.y + 0.003f, L * 0.5f - land), new Vector3(0.55f, it.s.y + 0.003f, L * 0.5f - land), new Vector3(0.55f, it.s.y + 0.003f, L * 0.5f), new Vector3(-0.55f, it.s.y + 0.003f, L * 0.5f), Vector3.up, Vector3.right, Vector3.forward, 0.5f);
                foreach (float sx in new[] { -W * 0.5f + 0.06f, W * 0.5f - 0.06f })
                {
                    e.Tube(MBrass, new Vector3(sx, 0.95f, z0 - 0.1f), new Vector3(sx, 0.95f + it.s.y, L * 0.5f - land + 0.05f), 0.022f);
                    e.Tube(MBrass, new Vector3(sx, 0.95f + it.s.y, L * 0.5f - land + 0.05f), new Vector3(sx, 0.95f + it.s.y, L * 0.5f - 0.1f), 0.022f);
                    e.Box(MBrass, new Vector3(sx, 0.48f, z0 - 0.1f), new Vector3(0.05f, 0.96f, 0.05f));
                    for (int i = 0; i < ns; i++) e.Box(MBrass, new Vector3(sx, rise * (i + 1) + 0.45f, z0 + (i + 0.5f) * run), new Vector3(0.018f, 0.9f, 0.018f));
                }
                break;
            }
            case HotelPlan.ILinenShelf:
            {
                float W = it.s.x, Hh = it.s.y, Dd = it.s.z;
                foreach (float sx in new[] { -W * 0.5f + 0.02f, W * 0.5f - 0.02f }) foreach (float sz in new[] { -Dd * 0.5f + 0.02f, Dd * 0.5f - 0.02f }) e.Box(MIron, new Vector3(sx, Hh * 0.5f, sz), new Vector3(0.03f, Hh, 0.03f));
                for (int s = 0; s < 4; s++)
                {
                    float y = 0.12f + s * (Hh - 0.2f) / 3f; e.Box(MIron, new Vector3(0, y, 0), new Vector3(W, 0.02f, Dd));
                    int stacks = 1 + (int)(H(s, v + 17) * 3f);
                    for (int k = 0; k < stacks; k++)
                    {
                        float hh = 0.06f + 0.2f * H(s * 5 + k, v + 21);
                        e.BoxR(MSheet, new Vector3(-W * 0.5f + 0.2f + k * 0.38f, y + 0.01f + hh * 0.5f, 0.02f), new Vector3(0.32f, hh, Dd - 0.1f), Quaternion.Euler(0, (H(k, s + 3) - 0.5f) * 10f, 0), 0.5f);
                    }
                }
                break;
            }
            case HotelPlan.ILaundry:
                e.Box(MIron, new Vector3(0, 0.08f, 0), new Vector3(0.9f, 0.04f, 0.6f));
                e.Box(MCloth, new Vector3(0, 0.5f, 0.29f), new Vector3(0.88f, 0.8f, 0.015f), 0.5f); e.Box(MCloth, new Vector3(0, 0.5f, -0.29f), new Vector3(0.88f, 0.8f, 0.015f), 0.5f);
                e.Box(MCloth, new Vector3(0.44f, 0.5f, 0), new Vector3(0.015f, 0.8f, 0.58f), 0.5f); e.Box(MCloth, new Vector3(-0.44f, 0.5f, 0), new Vector3(0.015f, 0.8f, 0.58f), 0.5f);
                e.BoxR(MSheet, new Vector3(0.05f, 0.75f, 0.0f), new Vector3(0.7f, 0.2f, 0.45f), Quaternion.Euler(6, 10, 4), 0.5f);
                foreach (float sx in new[] { -0.4f, 0.4f }) foreach (float sz in new[] { -0.26f, 0.26f }) e.Cyl(MIron, new Vector3(sx, 0f, sz), 0.04f, 0.04f, 0.06f, 8, true, false);
                break;
            case HotelPlan.IDebris:
                for (int i = 0; i < 9; i++)
                {
                    Vector3 c = new Vector3((H(i, v) - 0.5f) * it.s.x, 0.02f, (H(i, v + 1) - 0.5f) * it.s.z); float s = 0.04f + 0.12f * H(i, v + 2);
                    e.BoxR(MDebris, c + Vector3.up * s * 0.3f, new Vector3(s * 1.4f, s * 0.6f, s), Quaternion.Euler(H(i, v + 3) * 30f, H(i, v + 4) * 360f, 0));
                }
                e.BoxR(MDebris, new Vector3(0.1f, 0.03f, 0.1f), new Vector3(0.6f, 0.02f, 0.45f), Quaternion.Euler(3, H(9, v) * 180f, 2));
                break;
            case HotelPlan.IRubble:
            {
                for (int i = 0; i < 16; i++)
                {
                    float fx = (H(i, v + 30) - 0.5f) * it.s.x * 0.95f, fz = (H(i, v + 31) - 0.5f) * 1.0f;
                    float s = 0.25f + 0.45f * H(i, v + 32), hgt = Mathf.Max(0.1f, (1f - Mathf.Abs(fz) / 0.6f) * 1.3f * H(i, v + 33) + 0.1f);
                    e.BoxR(MDebris, new Vector3(fx, hgt * 0.4f, fz), new Vector3(s, hgt, s * 0.8f), Quaternion.Euler((H(i, v + 34) - 0.5f) * 30f, H(i, v + 35) * 360f, (H(i, v + 36) - 0.5f) * 30f));
                }
                e.BoxR(MWood, new Vector3(0.2f, 1.2f, 0f), new Vector3(it.s.x * 1.05f, 0.22f, 0.3f), Quaternion.Euler(0, 6f, 24f));
                e.BoxR(MDebris, new Vector3(-0.3f, 0.9f, 0.15f), new Vector3(1.6f, 0.04f, 0.9f), Quaternion.Euler(28f, 15f, -10f));
                break;
            }
            case HotelPlan.ISuitcase:
                if (v == 1) { e.Box(MBed, new Vector3(0, 0.36f, 0), new Vector3(0.45f, 0.7f, 0.22f), 0.5f); e.Box(MIron, new Vector3(0, 0.74f, 0), new Vector3(0.18f, 0.04f, 0.04f)); }
                else { e.Box(MPot, new Vector3(0, 0.11f, 0), new Vector3(0.7f, 0.22f, 0.45f), 0.5f); e.Box(MBrass, new Vector3(0, 0.11f, 0.228f), new Vector3(0.12f, 0.03f, 0.01f)); e.Box(MWood, new Vector3(0, 0.221f, 0), new Vector3(0.68f, 0.004f, 0.43f)); }
                break;
            case HotelPlan.IPiano:
            {
                e.Box(MBlack, new Vector3(0, 0.82f, 0), new Vector3(1.48f, 0.32f, 1.6f));
                e.Cyl(MBlack, new Vector3(0.2f, 0.66f, -0.75f), 0.55f, 0.55f, 0.32f, 16, true, true);
                e.BoxR(MBlack, new Vector3(-0.25f, 1.32f, -0.2f), new Vector3(1.0f, 0.03f, 1.6f), Quaternion.Euler(0, 0, 32f));
                e.Box(MBlack, new Vector3(0.5f, 1.12f, -0.2f), new Vector3(0.02f, 0.62f, 0.02f));
                foreach (Vector3 lp in new[] { new Vector3(-0.6f, 0, 0.7f), new Vector3(0.6f, 0, 0.7f), new Vector3(0.2f, 0, -1.1f) }) e.Cyl(MBlack, lp, 0.05f, 0.06f, 0.66f, 8, false, false);
                e.Box(MSheet, new Vector3(0, 0.9f, 0.86f), new Vector3(1.28f, 0.04f, 0.16f));
                for (int k = 0; k < 18; k++) e.Box(MBlack, new Vector3(-0.6f + k * 0.07f, 0.93f, 0.83f), new Vector3(0.025f, 0.02f, 0.09f));
                e.Box(MBlack, new Vector3(0, 0.47f, 1.35f), new Vector3(0.8f, 0.06f, 0.34f));
                foreach (float sx in new[] { -0.35f, 0.35f }) e.Box(MBlack, new Vector3(sx, 0.22f, 1.35f), new Vector3(0.05f, 0.44f, 0.3f));
                break;
            }
            case HotelPlan.IBench:
                e.Box(MUph, new Vector3(0, 0.44f, 0), new Vector3(1.4f, 0.08f, 0.44f), 0.5f);
                foreach (float sx in new[] { -0.62f, 0.62f }) e.Box(MWood, new Vector3(sx, 0.2f, 0), new Vector3(0.06f, 0.4f, 0.4f));
                break;
            case HotelPlan.IAshtray:
                e.Cyl(MBrass, Vector3.zero, 0.15f, 0.12f, 0.04f, 12, true, false);
                e.Cyl(MBrass, new Vector3(0, 0.04f, 0), 0.025f, 0.025f, 0.58f, 8, false, false);
                e.Cyl(MBrass, new Vector3(0, 0.62f, 0), 0.08f, 0.14f, 0.07f, 12, false, true);
                e.Cyl(MDebris, new Vector3(0, 0.66f, 0), 0.13f, 0.13f, 0.005f, 12, true, false);
                break;
            case HotelPlan.IBellCart:
            {
                e.Box(MBed, new Vector3(0, 0.2f, 0), new Vector3(1.0f, 0.06f, 0.6f), 0.5f);
                e.Box(MBrass, new Vector3(0, 0.16f, 0), new Vector3(1.04f, 0.04f, 0.64f));
                foreach (float sx in new[] { -0.48f, 0.48f })
                {
                    e.Tube(MBrass, new Vector3(sx, 0.2f, 0), new Vector3(sx, 1.75f, 0), 0.02f);
                    foreach (float sz in new[] { -0.26f, 0.26f }) e.Cyl(MIron, new Vector3(sx, 0f, sz), 0.07f, 0.07f, 0.05f, 10, true, false);
                }
                e.Tube(MBrass, new Vector3(-0.48f, 1.75f, 0), new Vector3(0.48f, 1.75f, 0), 0.02f);
                e.Tube(MBrass, new Vector3(-0.48f, 1.2f, 0), new Vector3(0.48f, 1.2f, 0), 0.012f);
                e.Box(MPot, new Vector3(-0.15f, 0.43f, 0.02f), new Vector3(0.6f, 0.4f, 0.25f), 0.5f);
                e.Box(MBed, new Vector3(0.25f, 0.36f, -0.05f), new Vector3(0.4f, 0.26f, 0.3f), 0.5f);
                break;
            }
        }
    }
}
