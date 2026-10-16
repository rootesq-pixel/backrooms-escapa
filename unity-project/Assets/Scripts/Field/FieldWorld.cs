using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.0 Campo infinito: construye el terreno (colinas suaves por trozos con collider), las casas abandonadas, los postes,
// las puertas sueltas y los árboles secos (todo determinista con la semilla → idéntico en multijugador), y en ejecución:
//  · da la vuelta al campo (si sales por un lado reapareces por el opuesto: el campo no se acaba nunca),
//  · dibuja copias «fantasma» del terreno y las casas al otro lado del borde para que no se vea ninguna costura,
//  · viento ambiental con ráfagas (las mismas ráfagas que mueven la hierba).
public class FieldWorld : MonoBehaviour
{
    public static FieldWorld Active;
    public FieldPlan plan;
    public float L;
    public int chunkCount, houseCount, propCount;
    public Transform levelRoot;
    public BackroomsGenerator gen;
    public float WindStrength { get; private set; }
    public float Gust { get; private set; }
    public Vector2 WindDir { get; private set; }
    public int WrapCount { get; private set; }

    private struct Ghost { public Mesh mesh; public Matrix4x4 local; public Material[] mats; public Vector3 center; public float radius; }
    private readonly List<Ghost> ghosts = new List<Ghost>();
    private readonly List<Object> owned = new List<Object>();
    private FirstPersonController fpc;
    private AudioSource wind, gustSrc;
    private float gustT, gustLen, nextGust;
    private System.Random rnd;

    public const float ChunkSize = 24f;

    // ------------------------------------------------------------------ construcción (la llama BackroomsGenerator)
    public static FieldWorld Build(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot, FieldPlan plan, System.Action<Object> track)
    {
        GameObject root = new GameObject("Field");
        root.transform.SetParent(levelRoot, false);
        FieldWorld fw = root.AddComponent<FieldWorld>();
        fw.plan = plan; fw.gen = gen; fw.levelRoot = levelRoot;
        fw.L = FieldHeight.L;
        fw.rnd = new System.Random(gen.UsedSeed * 13 + 1);
        fw.BuildTerrain(track);
        fw.BuildHouses(cfg, track);
        fw.BuildProps(cfg, track);
        Active = fw;
        return fw;
    }

    private Material Mat(string shader, System.Action<Object> track)
    {
        Shader s = Shader.Find(shader);
        Material m = new Material(s != null ? s : Shader.Find("Standard"));
        track(m);
        return m;
    }

    private static Texture2D Tex(string n) { return Resources.Load<Texture2D>("Field/" + n); }

    private Material SurfMat(string tex, Color tint, float smooth, System.Action<Object> track)
    {
        Material m = ProceduralAssets.CreateMaterial(tint, Tex(tex + "_a"), Vector2.one, smooth);
        Texture2D nm = Tex(tex + "_n");
        if (nm != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP"); }
        track(m);
        return m;
    }

    private void BuildTerrain(System.Action<Object> track)
    {
        Material ground = Mat("Backrooms/FieldGround", track);
        ground.SetTexture("_MainTex", Tex("ground_a"));
        ground.SetTexture("_BumpMap", Tex("ground_n"));
        int nc = Mathf.Max(1, Mathf.RoundToInt(L / ChunkSize));
        float cs = L / nc;
        int res = Mathf.RoundToInt(cs);            // 1 vértice por metro
        Transform troot = new GameObject("Terrain").transform;
        troot.SetParent(transform, false);
        for (int cx = 0; cx < nc; cx++)
            for (int cz = 0; cz < nc; cz++)
            {
                float x0 = cx * cs, z0 = cz * cs, step = cs / res;
                int vn = res + 1;
                Vector3[] v = new Vector3[vn * vn]; Vector3[] nr = new Vector3[vn * vn]; Vector2[] uv = new Vector2[vn * vn];
                for (int j = 0; j < vn; j++)
                    for (int i = 0; i < vn; i++)
                    {
                        float x = x0 + i * step, z = z0 + j * step;
                        v[j * vn + i] = new Vector3(x, FieldHeight.H(x, z), z);
                        nr[j * vn + i] = FieldHeight.Normal(x, z);
                        uv[j * vn + i] = new Vector2(x, z) * 0.4f;
                    }
                int[] tri = new int[res * res * 6]; int t = 0;
                for (int j = 0; j < res; j++)
                    for (int i = 0; i < res; i++)
                    {
                        int a = j * vn + i, b = a + 1, c = a + vn, d = c + 1;
                        tri[t++] = a; tri[t++] = c; tri[t++] = b; tri[t++] = b; tri[t++] = c; tri[t++] = d;
                    }
                Mesh m = new Mesh { name = "FieldTerrain_" + cx + "_" + cz };
                m.vertices = v; m.normals = nr; m.uv = uv; m.triangles = tri; m.RecalculateBounds(); m.RecalculateTangents();
                track(m);
                GameObject go = new GameObject(m.name);
                go.transform.SetParent(troot, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = ground; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                go.AddComponent<MeshCollider>().sharedMesh = m;
                AddGhost(m, Matrix4x4.identity, new[] { ground }, m.bounds.center, cs * 0.75f);
                chunkCount++;
            }
    }

    private void AddGhost(Mesh m, Matrix4x4 local, Material[] mats, Vector3 center, float radius)
    {
        ghosts.Add(new Ghost { mesh = m, local = local, mats = mats, center = center, radius = radius });
    }

    // ------------------------------------------------------------------ casas
    private static readonly Color[] Tints = { new Color(0.86f, 0.82f, 0.70f), new Color(0.78f, 0.79f, 0.76f), new Color(0.84f, 0.76f, 0.62f) };

    private void BuildHouses(LevelConfig cfg, System.Action<Object> track)
    {
        Material[] walls = new Material[3];
        for (int i = 0; i < 3; i++) walls[i] = SurfMat("house_wall", Tints[i], 0.08f, track);
        Material roof = SurfMat("house_roof", new Color(0.62f, 0.62f, 0.62f), 0.15f, track);
        Material floor = SurfMat("house_floor", new Color(0.62f, 0.56f, 0.5f), 0.2f, track);
        Material trim = ProceduralAssets.CreateMaterial(new Color(0.24f, 0.2f, 0.17f), Tex("house_floor_a"), Vector2.one, 0.1f); track(trim);
        Dictionary<int, Mesh> cache = new Dictionary<int, Mesh>();
        Transform hroot = new GameObject("Houses").transform;
        hroot.SetParent(transform, false);
        int k = 0;
        foreach (FieldPlan.House h in plan.houses)
        {
            int key = h.template * 2 + (h.enterable ? 1 : 0);
            if (!cache.TryGetValue(key, out Mesh mesh)) { mesh = HouseMesh(h.template, h.enterable); track(mesh); cache[key] = mesh; }
            Vector2 sz = FieldPlan.TemplateSize[h.template];
            Quaternion rot = Quaternion.Euler(0f, h.rot * 90f, 0f);
            // apoyo: el suelo de la casa a la altura de la esquina más alta; zócalo hasta la más baja
            float hi = -99f, lo = 99f;
            for (int c = 0; c < 4; c++)
            {
                Vector3 corner = rot * new Vector3((c & 1) == 0 ? -sz.x * 0.5f : sz.x * 0.5f, 0f, (c & 2) == 0 ? -sz.y * 0.5f : sz.y * 0.5f);
                float y = FieldHeight.H(h.pos.x + corner.x, h.pos.y + corner.z);
                hi = Mathf.Max(hi, y); lo = Mathf.Min(lo, y);
            }
            Vector3 pos = new Vector3(h.pos.x, hi + 0.02f, h.pos.y);
            GameObject go = new GameObject("House_" + k++ + (h.enterable ? "_abierta" : "_cerrada"));
            go.transform.SetParent(hroot, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material[] mats = { walls[h.tint], roof, floor, trim };
            mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            AddGhost(mesh, Matrix4x4.TRS(pos, rot, Vector3.one), mats, pos, 8f);
            // zócalo de hormigón hasta el terreno más bajo
            float plinth = pos.y - lo + 0.4f;
            GameObject pl = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Plinth", go.transform, new Vector3(0f, 0.02f - plinth * 0.5f, 0f),
                new Vector3(sz.x + 0.1f, plinth, sz.y + 0.1f), trim, true);
            pl.GetComponent<Renderer>().sharedMaterial = walls[(h.tint + 1) % 3];
            // escalones delante de la puerta si queda alta
            if (h.enterable)
            {
                Vector3 doorOut = go.transform.TransformPoint(new Vector3(DoorX(h.template), 0f, sz.y * 0.5f + 0.45f));
                float gy = FieldHeight.H(doorOut.x, doorOut.z);
                float rise = pos.y + 0.15f - gy;
                int steps = Mathf.Clamp(Mathf.CeilToInt(rise / 0.2f), 0, 5);
                for (int s = 0; s < steps; s++)
                {
                    float top = 0.15f - (s * rise / steps);
                    GameObject st = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Step" + s, go.transform,
                        new Vector3(DoorX(h.template), top - 0.5f, sz.y * 0.5f + 0.2f + s * 0.32f), new Vector3(1.4f, 1f, 0.34f), trim, true);
                    st.GetComponent<Renderer>().sharedMaterial = walls[(h.tint + 1) % 3];
                }
            }
            houseCount++;
        }
    }

    public static float DoorX(int tpl) { return tpl == 1 ? 0f : -FieldPlan.TemplateSize[tpl].x * 0.22f; }

    // casa en espacio local: suelo en y=0..0.15, puerta en +Z. Submeshes: 0 paredes, 1 tejado, 2 suelo, 3 madera
    private static Mesh HouseMesh(int tpl, bool enterable)
    {
        Vector2 sz = FieldPlan.TemplateSize[tpl];
        float W = sz.x, D = sz.y, H = 2.7f, t = 0.2f, y0 = 0.15f, y1 = y0 + H, ridge = 1.45f, o = 0.35f;
        FieldMesh fm = new FieldMesh(4); fm.uvScale = 0.4f;
        fm.Box(2, new Vector3(-W / 2, 0f, -D / 2), new Vector3(W / 2, y0, D / 2));
        float dx = DoorX(tpl);
        float winX = dx < 0f ? W * 0.22f : -W * 0.28f;
        // fachada (+Z): puerta + ventana
        List<Vector4> front = new List<Vector4> { new Vector4(dx - 0.5f, dx + 0.5f, y0, y0 + 2.1f), new Vector4(winX - 0.55f, winX + 0.55f, y0 + 0.9f, y0 + 2.0f) };
        WallX(fm, D / 2 - t, D / 2, -W / 2, W / 2, y0, y1, front);
        // trasera: dos ventanas
        List<Vector4> back = new List<Vector4> { new Vector4(-W * 0.25f - 0.5f, -W * 0.25f + 0.5f, y0 + 0.9f, y0 + 1.9f), new Vector4(W * 0.25f - 0.5f, W * 0.25f + 0.5f, y0 + 0.9f, y0 + 1.9f) };
        WallX(fm, -D / 2, -D / 2 + t, -W / 2, W / 2, y0, y1, back);
        // laterales: una ventana
        List<Vector4> side = new List<Vector4> { new Vector4(-0.5f, 0.5f, y0 + 0.9f, y0 + 1.9f) };
        WallZ(fm, -W / 2, -W / 2 + t, -D / 2 + t, D / 2 - t, y0, y1, side);
        WallZ(fm, W / 2 - t, W / 2, -D / 2 + t, D / 2 - t, y0, y1, side);
        // hastiales
        foreach (float x in new[] { -W / 2, W / 2 })
        {
            Vector3 a = new Vector3(x, y1, -D / 2), b = new Vector3(x, y1, D / 2), c = new Vector3(x, y1 + ridge, 0f);
            fm.Tri(0, a, b, c, new Vector3(Mathf.Sign(x), 0f, 0f), Vector3.forward, Vector3.up);
            Vector3 off = new Vector3(-Mathf.Sign(x) * t, 0f, 0f);
            fm.Tri(0, a + off, b + off, c + off, new Vector3(-Mathf.Sign(x), 0f, 0f), Vector3.forward, Vector3.up);
        }
        // tejado a dos aguas (cara superior e inferior)
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 e0 = new Vector3(-W / 2 - o, y1 - 0.18f, s * (D / 2 + o)), e1 = new Vector3(W / 2 + o, y1 - 0.18f, s * (D / 2 + o));
            Vector3 r0 = new Vector3(-W / 2 - o, y1 + ridge, 0f), r1 = new Vector3(W / 2 + o, y1 + ridge, 0f);
            Vector3 nrm = Vector3.Cross(r0 - e0, e1 - e0).normalized; if (nrm.y < 0f) nrm = -nrm;
            Vector3 vAx = (r0 - e0).normalized;
            fm.Quad(1, e0, e1, r1, r0, nrm, Vector3.right, vAx);
            Vector3 d = -nrm * 0.08f;
            fm.Quad(0, e0 + d, e1 + d, r1 + d, r0 + d, -nrm, Vector3.right, vAx);
        }
        // casas cerradas: puerta de madera y ventanas tapiadas con tablas
        if (!enterable)
        {
            fm.Box(3, new Vector3(dx - 0.5f, y0, D / 2 - t * 0.6f), new Vector3(dx + 0.5f, y0 + 2.1f, D / 2 - t * 0.2f));
            foreach (Vector4 wv in new List<Vector4> { front[1] })
                for (int p = 0; p < 4; p++)
                {
                    float yy = wv.z + 0.12f + p * 0.26f;
                    fm.Box(3, new Vector3(wv.x - 0.12f, yy, D / 2), new Vector3(wv.y + 0.12f, yy + 0.18f, D / 2 + 0.04f));
                }
        }
        return fm.ToMesh(enterable ? "HouseOpen_" + tpl : "HouseClosed_" + tpl);
    }

    // pared a lo largo de X (grosor en Z de z0 a z1) con huecos (x0, x1, ya, yb)
    private static void WallX(FieldMesh fm, float z0, float z1, float xa, float xb, float y0, float y1, List<Vector4> holes)
    {
        holes.Sort((a, b) => a.x.CompareTo(b.x));
        float x = xa;
        foreach (Vector4 h in holes)
        {
            if (h.x > x) fm.Box(0, new Vector3(x, y0, z0), new Vector3(h.x, y1, z1));
            if (h.z > y0) fm.Box(0, new Vector3(h.x, y0, z0), new Vector3(h.y, h.z, z1));
            if (h.w < y1) fm.Box(0, new Vector3(h.x, h.w, z0), new Vector3(h.y, y1, z1));
            x = h.y;
        }
        if (x < xb) fm.Box(0, new Vector3(x, y0, z0), new Vector3(xb, y1, z1));
    }

    private static void WallZ(FieldMesh fm, float x0, float x1, float za, float zb, float y0, float y1, List<Vector4> holes)
    {
        float z = za;
        foreach (Vector4 h in holes)
        {
            if (h.x > z) fm.Box(0, new Vector3(x0, y0, z), new Vector3(x1, y1, h.x));
            if (h.z > y0) fm.Box(0, new Vector3(x0, y0, h.x), new Vector3(x1, h.z, h.y));
            if (h.w < y1) fm.Box(0, new Vector3(x0, h.w, h.x), new Vector3(x1, y1, h.y));
            z = h.y;
        }
        if (z < zb) fm.Box(0, new Vector3(x0, y0, z), new Vector3(x1, y1, zb));
    }

    // ------------------------------------------------------------------ postes, puertas sueltas, árboles
    private void BuildProps(LevelConfig cfg, System.Action<Object> track)
    {
        Material wood = ProceduralAssets.CreateMaterial(new Color(0.30f, 0.25f, 0.21f), Tex("house_floor_a"), Vector2.one, 0.08f); track(wood);
        Material bark = ProceduralAssets.CreateMaterial(new Color(0.26f, 0.24f, 0.22f), null, Vector2.one, 0.05f); track(bark);
        Material wire = ProceduralAssets.CreateMaterial(new Color(0.05f, 0.05f, 0.05f), null, Vector2.one, 0.1f); track(wire);
        Transform proot = new GameObject("Props").transform;
        proot.SetParent(transform, false);
        Vector3 prevTop = Vector3.zero; bool hasPrev = false;
        foreach (FieldPlan.Prop p in plan.props)
        {
            float gy = FieldHeight.H(p.pos.x, p.pos.y);
            GameObject go = new GameObject(p.kind == FieldPlan.PropPole ? "Pole" : p.kind == FieldPlan.PropDoor ? "LoneDoor" : "DeadTree");
            go.transform.SetParent(proot, false);
            go.transform.localPosition = new Vector3(p.pos.x, gy, p.pos.y);
            go.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f);
            if (p.kind == FieldPlan.PropPole)
            {
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Post", go.transform, new Vector3(0f, 3.8f, 0f), new Vector3(0.26f, 4.2f, 0.26f), wood, true);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Bar", go.transform, new Vector3(0f, 7.3f, 0f), new Vector3(0.14f, 0.12f, 1.8f), wood, false);
                Vector3 top = go.transform.TransformPoint(new Vector3(0f, 7.42f, 0.75f));
                if (hasPrev && (top - prevTop).magnitude < 40f) Wire(proot, prevTop, top, wire);
                prevTop = top; hasPrev = true;
            }
            else if (p.kind == FieldPlan.PropDoor) BuildLoneDoor(go.transform, cfg, track);
            else
            {
                System.Random r = new System.Random(Mathf.RoundToInt(p.pos.x * 31 + p.pos.y * 17));
                go.transform.localRotation = Quaternion.Euler((float)r.NextDouble() * 6f - 3f, p.yaw, (float)r.NextDouble() * 6f - 3f);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Trunk", go.transform, new Vector3(0f, 2.6f, 0f), new Vector3(0.5f, 2.8f, 0.5f), bark, true);
                for (int b = 0; b < 6; b++)
                {
                    float a = b * 60f + (float)r.NextDouble() * 30f, up = 3.0f + b * 0.55f, len = 1.6f + (float)r.NextDouble() * 1.4f;
                    GameObject br = ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Branch" + b, go.transform, Vector3.zero, new Vector3(0.16f, len * 0.5f, 0.16f), bark, false);
                    br.transform.localRotation = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(50f + (float)r.NextDouble() * 20f, 0f, 0f);
                    br.transform.localPosition = new Vector3(0f, up, 0f) + br.transform.localRotation * new Vector3(0f, len * 0.5f, 0f);
                }
            }
            propCount++;
        }
    }

    private void Wire(Transform parent, Vector3 a, Vector3 b, Material m)
    {
        GameObject go = new GameObject("Wire");
        go.transform.SetParent(parent, true);
        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false; lr.positionCount = 12; lr.widthMultiplier = 0.03f; lr.sharedMaterial = m;
        lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
        Vector3 la = go.transform.InverseTransformPoint(a), lb = go.transform.InverseTransformPoint(b);
        for (int i = 0; i < 12; i++) { float t = i / 11f; lr.SetPosition(i, Vector3.Lerp(la, lb, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.9f)); }
    }

    // puerta suelta (señuelo): igual que la de salida pero sin LED ni luz
    public static void BuildLoneDoor(Transform parent, LevelConfig cfg, System.Action<Object> track)
    {
        Material doorMat = ProceduralAssets.CreateMaterial(cfg.doorColor, null, Vector2.one, 0.25f); track(doorMat);
        Material panelMat = ProceduralAssets.CreateMaterial(cfg.doorColor * 0.82f, null, Vector2.one, 0.2f); track(panelMat);
        Material frameMat = ProceduralAssets.CreateMaterial(cfg.doorFrameColor, null, Vector2.one, 0.3f); track(frameMat);
        float doorH = 2.15f;
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "DoorSlab", parent, new Vector3(0f, doorH * 0.5f, 0f), new Vector3(1.0f, doorH, 0.07f), doorMat, true);
        foreach (float side in new[] { 1f, -1f })
        {
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "PanelU", parent, new Vector3(0f, doorH * 0.72f, side * 0.038f), new Vector3(0.7f, doorH * 0.28f, 0.006f), panelMat, false);
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "PanelL", parent, new Vector3(0f, doorH * 0.30f, side * 0.038f), new Vector3(0.7f, doorH * 0.36f, 0.006f), panelMat, false);
        }
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "FrameL", parent, new Vector3(-0.56f, doorH * 0.5f + 0.05f, 0f), new Vector3(0.12f, doorH + 0.1f, 0.12f), frameMat, true);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "FrameR", parent, new Vector3(0.56f, doorH * 0.5f + 0.05f, 0f), new Vector3(0.12f, doorH + 0.1f, 0.12f), frameMat, true);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "FrameTop", parent, new Vector3(0f, doorH + 0.05f, 0f), new Vector3(1.24f, 0.12f, 0.12f), frameMat, false);
    }

    // ------------------------------------------------------------------ ejecución
    private void Start()
    {
        WindDir = new Vector2(0.8f, 0.6f).normalized;
        AudioClip loop = CreateWind(gen != null ? gen.UsedSeed : 1);
        owned.Add(loop);
        wind = gameObject.AddComponent<AudioSource>();
        wind.clip = loop; wind.loop = true; wind.spatialBlend = 0f; wind.playOnAwake = false;
        wind.volume = AudioMix.A(0.16f);
        wind.Play();
        nextGust = 6f;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        foreach (Object o in owned) ProceduralAssets.SafeDestroy(o);
        FieldHeight.Reset();
        Shader.SetGlobalVector("_WindDir", Vector4.zero);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        // ráfagas: cada 8-20 s una ráfaga de 3-6 s (mueve la hierba y sube el viento)
        nextGust -= dt;
        if (nextGust <= 0f && gustT <= 0f) { gustLen = 3f + Random.value * 3f; gustT = gustLen; nextGust = 8f + Random.value * 12f; }
        float g = 0f;
        if (gustT > 0f) { gustT -= dt; float u = 1f - gustT / gustLen; g = Mathf.Sin(u * Mathf.PI); g *= g; }
        Gust = Mathf.Lerp(Gust, g, 1f - Mathf.Exp(-dt * 3f));
        WindStrength = 0.35f + 0.08f * Mathf.Sin(Time.time * 0.21f);
        Shader.SetGlobalVector("_WindDir", new Vector4(WindDir.x, WindDir.y, WindStrength, Gust * 0.9f));
        Shader.SetGlobalFloat("_WTime", Time.time);
        if (wind != null) wind.volume = AudioMix.A(0.12f + 0.12f * Gust);
    }

    private void LateUpdate()
    {
        // vuelta al campo (solo el jugador local: los demás equipos hacen lo mismo con el suyo)
        if (fpc == null) fpc = FindObjectOfType<FirstPersonController>();
        if (fpc != null && levelRoot != null)
        {
            Vector3 lp = levelRoot.InverseTransformPoint(fpc.transform.position);
            Vector3 shift = Vector3.zero;
            if (lp.x < 0f) shift.x = L; else if (lp.x >= L) shift.x = -L;
            if (lp.z < 0f) shift.z = L; else if (lp.z >= L) shift.z = -L;
            if (shift != Vector3.zero)
            {
                Vector3 np = lp + shift;
                np.y = FieldHeight.H(np.x, np.z) + Mathf.Max(0.02f, lp.y - FieldHeight.H(lp.x, lp.z));
                fpc.Teleport(levelRoot.TransformPoint(np), fpc.transform.eulerAngles.y);
                WrapCount++;
            }
        }
        DrawGhosts();
    }

    public void DrawGhosts()
    {
        Camera cam = Camera.main;
        if (cam == null || levelRoot == null) return;
        Vector3 c = levelRoot.InverseTransformPoint(cam.transform.position);
        float range = GameSettings.Quality == 0 ? 110f : 170f;
        Matrix4x4 lw = levelRoot.localToWorldMatrix;
        for (int ox = -1; ox <= 1; ox++)
            for (int oz = -1; oz <= 1; oz++)
            {
                if (ox == 0 && oz == 0) continue;
                Vector3 off = new Vector3(ox * L, 0f, oz * L);
                // ¿la copia desplazada queda cerca de la cámara?
                if (Mathf.Abs(c.x - (L * 0.5f + off.x)) > L * 0.5f + range || Mathf.Abs(c.z - (L * 0.5f + off.z)) > L * 0.5f + range) continue;
                Matrix4x4 sh = lw * Matrix4x4.Translate(off);
                for (int i = 0; i < ghosts.Count; i++)
                {
                    Ghost gh = ghosts[i];
                    Vector3 p = gh.center + off;
                    if (Mathf.Abs(p.x - c.x) > range + gh.radius || Mathf.Abs(p.z - c.z) > range + gh.radius) continue;
                    Matrix4x4 m = sh * gh.local;
                    for (int s = 0; s < gh.mats.Length && s < gh.mesh.subMeshCount; s++)
                        Graphics.DrawMesh(gh.mesh, m, gh.mats[s], 0, null, s, null, ShadowCastingMode.Off, false);
                }
            }
    }

    // viento: ruido grave filtrado con soplidos lentos (bucle de 12 s sin costura)
    public static AudioClip CreateWind(int seed)
    {
        const int SR = 22050; int n = SR * 12;
        float[] d = new float[n];
        System.Random r = new System.Random(seed);
        float lp = 0f, lp2 = 0f, bp = 0f, bpv = 0f;
        float[] white = new float[n];
        for (int i = 0; i < n; i++) white[i] = (float)(r.NextDouble() * 2.0 - 1.0);
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR;
                float w = white[i];
                lp += 0.035f * (w - lp); lp2 += 0.035f * (lp - lp2);
                // silbido: resonador suave cuya frecuencia ondula despacio
                float f = 380f + 120f * Mathf.Sin(2f * Mathf.PI * t / 6f) + 60f * Mathf.Sin(2f * Mathf.PI * t / 4f + 1f);
                float wc = 2f * Mathf.Sin(Mathf.PI * f / SR);
                bp += wc * bpv; bpv += wc * (w * 0.02f - bp - 0.12f * bpv);
                float env = 0.65f + 0.35f * Mathf.Sin(2f * Mathf.PI * t / 12f * 2f + 0.5f) * Mathf.Sin(2f * Mathf.PI * t / 3f);
                if (pass == 1) d[i] = (lp2 * 6f + bp * 0.9f) * env;
            }
        float mx = 1e-4f; for (int i = 0; i < n; i++) mx = Mathf.Max(mx, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] *= 0.7f / mx;
        AudioClip c = AudioClip.Create("FieldWind", n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }
}
