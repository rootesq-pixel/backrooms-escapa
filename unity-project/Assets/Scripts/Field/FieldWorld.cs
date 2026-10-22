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
        LoadDiag.Step("campo: terreno");
        fw.BuildTerrain(track);
        LoadDiag.Step("campo: casas");
        fw.BuildHouses(cfg, track);
        LoadDiag.Step("campo: copias del borde");
        foreach (GameObject hg in fw.houseGos) foreach (Transform ch in hg.transform) if (ch.name != "Furniture") fw.GhostTree(ch);   // zócalos y escalones también al otro lado del borde
        LoadDiag.Step("campo: accesorios");
        fw.BuildProps(cfg, track);
        LoadDiag.Step("campo: puntos y salida");
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

    // v7.0.1: copias del otro lado del borde también para postes, árboles, puertas, cables y la salida
    // (antes solo terreno y casas: cerca del borde los accesorios desaparecían y reaparecían al dar la vuelta)
    public int GhostTree(Transform root)
    {
        if (root == null || levelRoot == null) return 0;
        int n = 0; Matrix4x4 inv = levelRoot.worldToLocalMatrix;
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.name.EndsWith("_LOD0") || r.name.EndsWith("_LOD2")) continue;   // en las copias basta el LOD medio
            MeshFilter mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
            if (r.GetComponent<Light>() != null || !r.enabled) continue;
            Bounds b = r.bounds; Vector3 c = levelRoot.InverseTransformPoint(b.center);
            AddGhost(mf.sharedMesh, inv * r.transform.localToWorldMatrix, r.sharedMaterials, c, b.extents.magnitude + 1f); n++;
        }
        return n;
    }
    public int GhostCount { get { return ghosts.Count; } }
    public static float GhostRange { get { return FieldAtmosphere.FarClip + 10f; } }
    public static bool GhostInRange(Vector3 camLocal, Vector3 center, float radius, Vector3 off, float range)
    {
        Vector3 p = center + off;
        return Mathf.Abs(p.x - camLocal.x) <= range + radius && Mathf.Abs(p.z - camLocal.z) <= range + radius;
    }
    // pruebas: copias (real o desplazada) a menos de 'dist' de la cámara que NO se dibujarían
    public int MissingCopies(Vector3 camLocal, float dist)
    {
        int miss = 0; float range = GhostRange;
        for (int i = 0; i < ghosts.Count; i++)
            for (int ox = -1; ox <= 1; ox++)
                for (int oz = -1; oz <= 1; oz++)
                {
                    if (ox == 0 && oz == 0) continue;
                    Vector3 off = new Vector3(ox * L, 0f, oz * L);
                    Vector3 p = ghosts[i].center + off;
                    float d = new Vector2(p.x - camLocal.x, p.z - camLocal.z).magnitude - ghosts[i].radius;
                    if (d > dist) continue;
                    bool region = !(Mathf.Abs(camLocal.x - (L * 0.5f + off.x)) > L * 0.5f + range || Mathf.Abs(camLocal.z - (L * 0.5f + off.z)) > L * 0.5f + range);
                    if (!region || !GhostInRange(camLocal, ghosts[i].center, ghosts[i].radius, off, range)) miss++;
                }
        return miss;
    }

    private void AddGhost(Mesh m, Matrix4x4 local, Material[] mats, Vector3 center, float radius)
    {
        ghosts.Add(new Ghost { mesh = m, local = local, mats = mats, center = center, radius = radius });
    }

    // ------------------------------------------------------------------ casas
    private static readonly Color[] Tints = { new Color(0.86f, 0.82f, 0.70f), new Color(0.78f, 0.79f, 0.76f), new Color(0.84f, 0.76f, 0.62f) };
    // v7.2.0: tamaño real (m) de cada textura de Poly Haven: hw0..2 paredes, hr0..2 tejados
    private static readonly float[] HouseTexM = { 1.57f, 1.6f, 1.8f, 3f, 2f, 3f };
    private static readonly Color[] WallTint = { new Color(1f, 0.97f, 0.92f), new Color(0.95f, 0.97f, 0.95f), new Color(1f, 0.98f, 0.94f) };
    public static bool HouseBaked { get; private set; }
    public static float HouseLMScale = 1f, HouseInteriorLM = 0.3f;   // exposición del horneado; luz media del interior (muebles)
    // la malla usa UV0 = metros x 0,4: escala = 2,5 m / tamaño real de la textura
    private Material HouseMat(string tex, Color tint, float metres, float gloss, Texture lm, System.Action<Object> track)
    {
        Shader sh = Shader.Find("Backrooms/FieldHouse");
        Material m = sh != null ? new Material(sh) : ProceduralAssets.CreateMaterial(tint, null, Vector2.one, gloss);
        m.name = "House_" + (tex ?? "plain");
        if (tex != null)
        {
            m.SetTexture("_MainTex", Resources.Load<Texture2D>("Field/House/" + tex + "_a"));
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", Resources.Load<Texture2D>("Field/House/" + tex + "_n"));
            if (m.HasProperty("_RoughTex")) m.SetTexture("_RoughTex", Resources.Load<Texture2D>("Field/House/" + tex + "_m"));
        }
        m.SetTextureScale("_MainTex", Vector2.one * (2.5f / metres));
        m.SetColor("_Color", tint);
        if (m.HasProperty("_Gloss")) m.SetFloat("_Gloss", gloss);
        if (m.HasProperty("_UseLM")) { m.SetFloat("_UseLM", lm != null ? 1f : 0f); if (lm != null) { m.SetTexture("_LightMap", lm); m.SetFloat("_LMScale", HouseLMScale); } }
        if (lm == null && m.HasProperty("_DirtH")) m.SetFloat("_DirtH", 0.35f);
        track(m);
        return m;
    }

    private readonly List<GameObject> houseGos = new List<GameObject>();
    private void BuildHouses(LevelConfig cfg, System.Action<Object> track)
    {
        // v7.2.0: texturas PBR CC0 de Poly Haven por variante (tinte): tablas de madera, tablas pintadas o yeso; tejado de tablillas,
        // chapa oxidada o teja gris; carpintería con pintura levantada; zócalo y chimenea de piedra. Luz del cielo horneada (HouseLM).
        Texture lm = Resources.Load<Texture>("Field/House/HouseLM");
        HouseBaked = lm != null && Shader.Find("Backrooms/FieldHouse") != null;
        Material[] walls = new Material[3], roofs = new Material[3];
        for (int i = 0; i < 3; i++)
        {
            walls[i] = HouseMat("hw" + i, WallTint[i], HouseTexM[i], 0.3f, lm, track);
            roofs[i] = HouseMat("hr" + i, Color.white, HouseTexM[3 + i], i == 1 ? 0.55f : 0.3f, lm, track);
        }
        Material floor = HouseMat(null, new Color(0.62f, 0.56f, 0.5f), 2.5f, 0.2f, lm, track);
        if (floor.HasProperty("_MainTex")) { floor.SetTexture("_MainTex", Tex("house_floor_a")); floor.SetTexture("_BumpMap", Tex("house_floor_n")); }
        Material trim = HouseMat("htrim", new Color(0.85f, 0.85f, 0.85f), 0.76f, 0.3f, lm, track);
        Material glass = HouseMat(null, new Color(0.10f, 0.12f, 0.11f), 1f, 0.85f, lm, track);
        Material stone = HouseMat("hfound", Color.white, 1.5f, 0.2f, lm, track);
        Material plinthM = HouseMat("hfound", Color.white, 1.5f, 0.2f, null, track);   // zócalo enterrado (fuera del horneado)
        // v7.0.2: muebles en madera más clara (con el tono del zócalo casi negros: los objetos no se distinguían)
        Material furnWood = ProceduralAssets.CreateMaterial(new Color(0.66f, 0.52f, 0.38f), Tex("house_floor_a"), Vector2.one, 0.22f); track(furnWood);
        if (HouseBaked)
        {   // v7.2.0: muebles con la luz media del interior horneado (sin ambiente ni sol: si no, «brillan» en la casa oscura)
            Material fw = HouseMat(null, new Color(0.66f, 0.52f, 0.38f), 2.5f, 0.22f, Texture2D.whiteTexture, track);
            fw.SetTexture("_MainTex", Tex("house_floor_a")); fw.SetTexture("_BumpMap", Tex("house_floor_n")); fw.SetTextureScale("_MainTex", Vector2.one);
            fw.SetFloat("_LMScale", HouseInteriorLM); furnWood = fw;
        }
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
            Material[] mats = { walls[h.tint], roofs[h.tint], floor, trim, glass, stone };
            mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            AddGhost(mesh, Matrix4x4.TRS(pos, rot, Vector3.one), mats, pos, 8f);
            houseGos.Add(go);
            // zócalo de hormigón hasta el terreno más bajo
            float plinth = pos.y - lo + 0.4f;
            GameObject pl = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Plinth", go.transform, new Vector3(0f, 0.02f - plinth * 0.5f, 0f),
                new Vector3(sz.x + 0.1f, plinth, sz.y + 0.1f), trim, true);
            pl.GetComponent<Renderer>().sharedMaterial = plinthM;
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
                    st.GetComponent<Renderer>().sharedMaterial = plinthM;
                }
            }
            if (h.enterable) BuildFurniture(go.transform, h, k - 1, furnWood, floor);
            houseCount++;
        }
    }

    // v7.0.2: muebles sencillos en las casas abiertas (mesa, estantería, caja) y huecos para objetos sobre ellos y sobre la tarima.
    //   Los objetos del Campo infinito (pilas, notas, agua, barras, cinta) solo aparecen aquí, nunca en la hierba.
    public struct ItemSlot { public Vector3 pos; public int house; public int kind; }   // pos: superficie + 0,1 m (como los demás puntos); kind 0 mesa, 1 estante, 2 suelo
    public const int SlotTable = 0, SlotShelf = 1, SlotFloor = 2;
    public readonly List<ItemSlot> ItemSlots = new List<ItemSlot>();
    public int FurnitureCount { get; private set; }

    private void BuildFurniture(Transform house, FieldPlan.House h, int hi, Material wood, Material floorMat)
    {
        Vector2 sz = FieldPlan.TemplateSize[h.template];
        float W = sz.x, D = sz.y, t = 0.2f, y0 = 0.15f;
        Transform fr = new GameObject("Furniture").transform;
        fr.SetParent(house, false);
        float dx = DoorX(h.template);
        System.Action<string, Vector3, Vector3> box = (n, c, s3) => { ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, n, fr, c, s3, wood, true).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off; FurnitureCount++; };
        System.Action<Vector3, int> slot = (lp, kind) => ItemSlots.Add(new ItemSlot { pos = house.TransformPoint(lp + Vector3.up * 0.1f), house = hi, kind = kind });
        // mesa (lado contrario a la puerta)
        float tx = dx < -0.1f ? W * 0.14f : -W * 0.2f, tz = -0.25f, top = y0 + 0.74f;
        box("TableTop", new Vector3(tx, top, tz), new Vector3(1.2f, 0.05f, 0.7f));
        foreach (float lx in new[] { -0.52f, 0.52f }) foreach (float lz in new[] { -0.27f, 0.27f })
            box("TableLeg", new Vector3(tx + lx, y0 + 0.36f, tz + lz), new Vector3(0.06f, 0.72f, 0.06f));
        slot(new Vector3(tx - 0.3f, top + 0.025f, tz + 0.12f), SlotTable);
        slot(new Vector3(tx + 0.32f, top + 0.025f, tz + 0.1f), SlotTable);
        // estantería contra la pared lateral derecha, hacia el fondo
        float sx = W / 2 - t - 0.18f, zc = -D / 2 + t + 0.75f;
        box("ShelfBack", new Vector3(W / 2 - t - 0.02f, y0 + 0.72f, zc), new Vector3(0.03f, 1.44f, 1.1f));
        foreach (float ez in new[] { -0.54f, 0.54f }) box("ShelfSide", new Vector3(sx, y0 + 0.72f, zc + ez), new Vector3(0.32f, 1.44f, 0.03f));
        foreach (float by in new[] { 0.06f, 0.5f, 0.95f, 1.42f }) box("ShelfBoard", new Vector3(sx, y0 + by, zc), new Vector3(0.32f, 0.025f, 1.06f));
        foreach (float by in new[] { 0.5f, 0.95f })
            foreach (float oz in new[] { -0.26f, 0.26f })
                slot(new Vector3(sx - 0.02f, y0 + by + 0.0125f, zc + oz), SlotShelf);
        // caja de madera en la esquina del fondo izquierda + tarima
        float cx = -W / 2 + t + 0.4f, cz = -D / 2 + t + 0.4f;
        box("Crate", new Vector3(cx, y0 + 0.25f, cz), new Vector3(0.5f, 0.5f, 0.5f));
        slot(new Vector3(cx, y0 + 0.5f, cz), SlotTable);
        slot(new Vector3(-W / 2 + t + 0.5f, y0, D * 0.12f), SlotFloor);
        slot(new Vector3(tx, y0, tz - 0.85f), SlotFloor);
    }

    public static float DoorX(int tpl) { return tpl == 1 ? 0f : -FieldPlan.TemplateSize[tpl].x * 0.22f; }

    // casa en espacio local: suelo en y=0..0.15, puerta en +Z. Submeshes: 0 paredes, 1 tejado, 2 suelo, 3 madera, 4 cristal, 5 piedra
    // v7.2.0: más detalle (techo interior, tejado con grosor, tablas de remate y cumbrera, canalones y bajante, chimenea,
    // zócalo de piedra, cuarterones y cristal sucio en las cerradas, tejadillo sobre la puerta) y UV2 de lightmap para el horneado
    // de Blender (Tools/field/house_bake.py). Mismos huecos, tamaños y puerta que v7.0 (colisiones, muebles y objetos no cambian).
    public const int HouseSubs = 6, SubGlass = 4;
    public static int HouseVariant(int tpl, bool enterable) { return tpl * 2 + (enterable ? 1 : 0); }
    public static Rect HouseTile(int variant) { return new Rect((variant % 3) / 3f, (variant / 3) / 2f, 1f / 3f, 0.5f); }   // atlas 3x2 (3072x2048)
    public static Mesh HouseMesh(int tpl, bool enterable)
    {
        Vector2 sz = FieldPlan.TemplateSize[tpl];
        float W = sz.x, D = sz.y, H = 2.7f, t = 0.2f, y0 = 0.15f, y1 = y0 + H, ridge = 1.45f, o = 0.35f;
        FieldMesh fm = new FieldMesh(HouseSubs); fm.uvScale = 0.4f;
        fm.lightmapUV = true; fm.lmTile = HouseTile(HouseVariant(tpl, enterable));
        fm.Box(2, new Vector3(-W / 2, 0f, -D / 2), new Vector3(W / 2, y0, D / 2));
        float dx = DoorX(tpl);
        float winX = dx < 0f ? W * 0.22f : -W * 0.28f;
        // fachada (+Z): puerta + ventana
        List<Vector4> front = new List<Vector4> { new Vector4(dx - 0.5f, dx + 0.5f, y0, y0 + 2.1f), new Vector4(winX - 0.55f, winX + 0.55f, y0 + 0.9f, y0 + 2.0f) };
        WallX(fm, D / 2 - t, D / 2, -W / 2, W / 2, y0, y1, new List<Vector4>(front));
        // trasera: dos ventanas
        List<Vector4> back = new List<Vector4> { new Vector4(-W * 0.25f - 0.5f, -W * 0.25f + 0.5f, y0 + 0.9f, y0 + 1.9f), new Vector4(W * 0.25f - 0.5f, W * 0.25f + 0.5f, y0 + 0.9f, y0 + 1.9f) };
        WallX(fm, -D / 2, -D / 2 + t, -W / 2, W / 2, y0, y1, new List<Vector4>(back));
        // laterales: una ventana
        List<Vector4> side = new List<Vector4> { new Vector4(-0.5f, 0.5f, y0 + 0.9f, y0 + 1.9f) };
        WallZ(fm, -W / 2, -W / 2 + t, -D / 2 + t, D / 2 - t, y0, y1, side);
        WallZ(fm, W / 2 - t, W / 2, -D / 2 + t, D / 2 - t, y0, y1, side);
        // techo interior (plano, bajo el tejado)
        fm.Quad(0, new Vector3(-W / 2 + t, y1 - 0.01f, -D / 2 + t), new Vector3(W / 2 - t, y1 - 0.01f, -D / 2 + t), new Vector3(W / 2 - t, y1 - 0.01f, D / 2 - t), new Vector3(-W / 2 + t, y1 - 0.01f, D / 2 - t), Vector3.down, Vector3.right, Vector3.forward);
        // hastiales
        foreach (float x in new[] { -W / 2, W / 2 })
        {
            Vector3 a = new Vector3(x, y1, -D / 2), b = new Vector3(x, y1, D / 2), c = new Vector3(x, y1 + ridge, 0f);
            fm.Tri(0, a, b, c, new Vector3(Mathf.Sign(x), 0f, 0f), Vector3.forward, Vector3.up);
            Vector3 off = new Vector3(-Mathf.Sign(x) * t, 0f, 0f);
            fm.Tri(0, a + off, b + off, c + off, new Vector3(-Mathf.Sign(x), 0f, 0f), Vector3.forward, Vector3.up);
        }
        // tejado a dos aguas con grosor: cara superior, inferior y canto
        const float rth = 0.1f;
        for (int s = -1; s <= 1; s += 2)
        {
            Vector3 e0 = new Vector3(-W / 2 - o, y1 - 0.18f, s * (D / 2 + o)), e1 = new Vector3(W / 2 + o, y1 - 0.18f, s * (D / 2 + o));
            Vector3 r0 = new Vector3(-W / 2 - o, y1 + ridge, 0f), r1 = new Vector3(W / 2 + o, y1 + ridge, 0f);
            Vector3 nrm = Vector3.Cross(r0 - e0, e1 - e0).normalized; if (nrm.y < 0f) nrm = -nrm;
            Vector3 vAx = (r0 - e0).normalized;
            Vector3 up = nrm * rth;
            fm.Quad(1, e0 + up, e1 + up, r1 + up, r0 + up, nrm, Vector3.right, vAx);
            fm.Quad(0, e0, e1, r1, r0, -nrm, Vector3.right, vAx);
            Vector3 eo = new Vector3(0f, 0f, s);
            fm.Quad(3, e0, e1, e1 + up, e0 + up, (eo - vAx * Vector3.Dot(eo, vAx)).normalized, Vector3.right, Vector3.up);   // canto del alero
            foreach (float xe in new[] { -W / 2 - o, W / 2 + o })
            {   // canto lateral (hastial) + tabla de remate
                Vector3 p0 = new Vector3(xe, e0.y, e0.z), p1 = new Vector3(xe, r0.y, 0f);
                fm.Quad(3, p0, p1, p1 + up, p0 + up, new Vector3(Mathf.Sign(xe), 0f, 0f), Vector3.forward, Vector3.up);
                Vector3 bo = new Vector3(Mathf.Sign(xe) * 0.03f, 0f, 0f), bd = -nrm * 0.2f;
                fm.Quad(3, p0 + bo + up * 0.5f, p1 + bo + up * 0.5f, p1 + bo + bd, p0 + bo + bd, new Vector3(Mathf.Sign(xe), 0f, 0f), Vector3.forward, Vector3.up);
            }
        }
        fm.Box(3, new Vector3(-W / 2 - o - 0.02f, y1 + ridge + rth * 0.6f, -0.09f), new Vector3(W / 2 + o + 0.02f, y1 + ridge + rth + 0.06f, 0.09f));   // cumbrera
        // v7.0.1: carpintería (marcos de ventanas con alféizar, marco de puerta, esquineras y tabla de alero): menos aspecto de caja
        const float tw = 0.09f, tp = 0.05f;
        foreach (Vector4 hv in front) FrameX(fm, hv, D / 2, 1f, tw, tp, hv.z > y0 + 0.01f);
        foreach (Vector4 hv in back) FrameX(fm, hv, -D / 2, -1f, tw, tp, true);
        foreach (Vector4 hv in side) { FrameZ(fm, hv, -W / 2, -1f, tw, tp); FrameZ(fm, hv, W / 2, 1f, tw, tp); }
        foreach (float sx in new[] { -1f, 1f }) foreach (float sz2 in new[] { -1f, 1f })
        { float cx = sx * (W / 2 + 0.01f), cz = sz2 * (D / 2 + 0.01f); fm.Box(3, new Vector3(cx - 0.07f, y0 + 0.3f, cz - 0.07f), new Vector3(cx + 0.07f, y1, cz + 0.07f)); }
        foreach (float sz2 in new[] { -1f, 1f })
        {
            fm.Box(3, new Vector3(-W / 2 - o, y1 - 0.34f, sz2 * (D / 2 + o) - 0.03f), new Vector3(W / 2 + o, y1 - 0.18f, sz2 * (D / 2 + o) + 0.03f));
            // canalón bajo el alero
            float gz = sz2 * (D / 2 + o + 0.07f);
            fm.Box(3, new Vector3(-W / 2 - o + 0.05f, y1 - 0.36f, gz - 0.06f), new Vector3(W / 2 + o - 0.05f, y1 - 0.24f, gz + 0.06f));
        }
        // bajante en una esquina trasera (cuelga del canalón hasta el zócalo)
        { float bx = W / 2 - 0.25f, bz = -(D / 2 + 0.06f); fm.Box(3, new Vector3(bx - 0.04f, y0 + 0.1f, bz - 0.04f), new Vector3(bx + 0.04f, y1 - 0.3f, bz + 0.04f)); }
        // zócalo de piedra (sobresale 4 cm)
        foreach (float sz2 in new[] { -1f, 1f }) fm.Box(5, new Vector3(-W / 2 - 0.04f, -0.25f, sz2 > 0 ? D / 2 - 0.02f : -D / 2 - 0.04f), new Vector3(W / 2 + 0.04f, y0 + 0.3f, sz2 > 0 ? D / 2 + 0.04f : -D / 2 + 0.02f));
        foreach (float sx in new[] { -1f, 1f }) fm.Box(5, new Vector3(sx > 0 ? W / 2 - 0.02f : -W / 2 - 0.04f, -0.25f, -D / 2 + 0.02f), new Vector3(sx > 0 ? W / 2 + 0.04f : -W / 2 + 0.02f, y0 + 0.3f, D / 2 - 0.02f));
        // chimenea de piedra (plantillas 0 y 2)
        if (tpl != 1)
        {
            float chx = W * 0.27f, chz = -D * 0.2f, top = y1 + ridge + 0.55f;
            fm.Box(5, new Vector3(chx - 0.3f, y1, chz - 0.25f), new Vector3(chx + 0.3f, top, chz + 0.25f));
            fm.Box(5, new Vector3(chx - 0.34f, top, chz - 0.29f), new Vector3(chx + 0.34f, top + 0.08f, chz + 0.29f));
        }
        // tejadillo sobre la puerta con dos ménsulas
        {
            float py = y0 + 2.35f, pd = 0.75f;
            Vector3 a = new Vector3(dx - 0.85f, py, D / 2), b = new Vector3(dx + 0.85f, py, D / 2), c = new Vector3(dx + 0.85f, py - 0.18f, D / 2 + pd), d = new Vector3(dx - 0.85f, py - 0.18f, D / 2 + pd);
            Vector3 pn = Vector3.Cross(b - a, d - a).normalized; if (pn.y < 0f) pn = -pn;
            fm.Quad(1, a + pn * 0.06f, b + pn * 0.06f, c + pn * 0.06f, d + pn * 0.06f, pn, Vector3.right, (d - a).normalized);
            fm.Quad(3, a, b, c, d, -pn, Vector3.right, (d - a).normalized);
            fm.Quad(3, d, c, c + pn * 0.06f, d + pn * 0.06f, Vector3.forward, Vector3.right, Vector3.up);
            foreach (float bxp in new[] { dx - 0.75f, dx + 0.75f }) fm.Box(3, new Vector3(bxp - 0.04f, py - 0.55f, D / 2), new Vector3(bxp + 0.04f, py - 0.12f, D / 2 + 0.5f));
        }
        // casas cerradas: puerta de madera, cristal sucio con cuarterones y la ventana delantera tapiada con tablas
        if (!enterable)
        {
            fm.Box(3, new Vector3(dx - 0.5f, y0, D / 2 - t * 0.6f), new Vector3(dx + 0.5f, y0 + 2.1f, D / 2 - t * 0.2f));
            foreach (Vector4 wv in new List<Vector4> { front[1] })
                for (int p = 0; p < 4; p++)
                {
                    float yy = wv.z + 0.12f + p * 0.26f;
                    fm.Box(3, new Vector3(wv.x - 0.12f, yy, D / 2), new Vector3(wv.y + 0.12f, yy + 0.18f, D / 2 + 0.04f));
                }
            System.Action<Vector4, float, bool> pane = (h, f, alongX) =>
            {
                float mxc = (h.x + h.y) * 0.5f, myc = (h.z + h.w) * 0.5f;
                if (alongX)
                {
                    fm.Quad(SubGlass, new Vector3(h.x, h.z, f), new Vector3(h.y, h.z, f), new Vector3(h.y, h.w, f), new Vector3(h.x, h.w, f), new Vector3(0f, 0f, Mathf.Sign(f)), Vector3.right, Vector3.up);
                    fm.Box(3, new Vector3(mxc - 0.025f, h.z, f - 0.025f), new Vector3(mxc + 0.025f, h.w, f + 0.025f));
                    fm.Box(3, new Vector3(h.x, myc - 0.025f, f - 0.025f), new Vector3(h.y, myc + 0.025f, f + 0.025f));
                }
                else
                {
                    fm.Quad(SubGlass, new Vector3(f, h.z, h.x), new Vector3(f, h.z, h.y), new Vector3(f, h.w, h.y), new Vector3(f, h.w, h.x), new Vector3(Mathf.Sign(f), 0f, 0f), Vector3.forward, Vector3.up);
                    fm.Box(3, new Vector3(f - 0.025f, h.z, mxc - 0.025f), new Vector3(f + 0.025f, h.w, mxc + 0.025f));
                    fm.Box(3, new Vector3(f - 0.025f, myc - 0.025f, h.x), new Vector3(f + 0.025f, myc + 0.025f, h.y));
                }
            };
            pane(front[1], D / 2 - 0.1f, true);
            foreach (Vector4 hv in back) pane(hv, -D / 2 + 0.1f, true);
            foreach (Vector4 hv in side) { pane(hv, -W / 2 + 0.1f, false); pane(hv, W / 2 - 0.1f, false); }
        }
        return fm.ToMesh(enterable ? "HouseOpen_" + tpl : "HouseClosed_" + tpl);
    }

    // marco de hueco (x0, x1, ya, yb) en una fachada perpendicular a Z (cara exterior en zf, hacia 'dir')
    private static void FrameX(FieldMesh fm, Vector4 h, float zf, float dir, float tw, float tp, bool sill)
    {
        float za = dir > 0 ? zf : zf - tp, zb = dir > 0 ? zf + tp : zf;
        fm.Box(3, new Vector3(h.x - tw, h.z, za), new Vector3(h.x, h.w, zb));
        fm.Box(3, new Vector3(h.y, h.z, za), new Vector3(h.y + tw, h.w, zb));
        fm.Box(3, new Vector3(h.x - tw, h.w, za), new Vector3(h.y + tw, h.w + tw * 1.2f, zb));
        if (sill) fm.Box(3, new Vector3(h.x - tw * 1.6f, h.z - 0.06f, dir > 0 ? zf : zf - tp * 2.2f), new Vector3(h.y + tw * 1.6f, h.z, dir > 0 ? zf + tp * 2.2f : zf));
    }
    // igual para fachadas perpendiculares a X (hueco en coordenadas z)
    private static void FrameZ(FieldMesh fm, Vector4 h, float xf, float dir, float tw, float tp)
    {
        float xa = dir > 0 ? xf : xf - tp, xb = dir > 0 ? xf + tp : xf;
        fm.Box(3, new Vector3(xa, h.z, h.x - tw), new Vector3(xb, h.w, h.x));
        fm.Box(3, new Vector3(xa, h.z, h.y), new Vector3(xb, h.w, h.y + tw));
        fm.Box(3, new Vector3(xa, h.w, h.x - tw), new Vector3(xb, h.w + tw * 1.2f, h.y + tw));
        fm.Box(3, new Vector3(dir > 0 ? xf : xf - tp * 2.2f, h.z - 0.06f, h.x - tw * 1.6f), new Vector3(dir > 0 ? xf + tp * 2.2f : xf, h.z, h.y + tw * 1.6f));
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
    // v7.0.1: modelos de Blender (Tools/field/field_props.py) con LOD0-2 y corteza/madera triplanar PBR CC0
    public static Material BarkMat(string name, Color tint, float scale, float stretch, float gloss, System.Action<Object> track)
    {
        Shader sh = Shader.Find("Backrooms/FieldBark");
        if (sh == null) return ProceduralAssets.CreateMaterial(tint * 0.5f, null, Vector2.one, 0.1f);
        Material m = new Material(sh); track(m);
        m.SetColor("_Color", tint);
        m.SetTexture("_MainTex", Resources.Load<Texture2D>("Field/Props/" + name + "_diff"));
        m.SetTexture("_BumpMap", Resources.Load<Texture2D>("Field/Props/" + name + "_nor"));
        m.SetTexture("_Mask", Resources.Load<Texture2D>("Field/Props/" + name + "_mask"));
        m.SetFloat("_Scale", scale); m.SetFloat("_Stretch", stretch); m.SetFloat("_Gloss", gloss);
        return m;
    }

    static GameObject SpawnModel(string res, Transform parent, Material[] byName, string[] names, System.Action<Object> track)
    {
        GameObject src = Resources.Load<GameObject>("Field/Props/" + res);
        if (src == null) return null;
        GameObject g = Object.Instantiate(src, parent, false); g.name = res;
        foreach (MeshRenderer r in g.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material[] ms = r.sharedMaterials;
            for (int i = 0; i < ms.Length; i++)
            {
                Material pick = byName[0];
                for (int k = 0; k < names.Length; k++) if (ms[i] != null && ms[i].name.StartsWith(names[k])) pick = byName[k];
                ms[i] = pick;
            }
            r.sharedMaterials = ms; r.shadowCastingMode = ShadowCastingMode.On;
        }
        // LOD: cambios dentro de la niebla y sin desaparecer nunca (el último LOD no se recorta)
        LODGroup lg = g.GetComponent<LODGroup>();
        if (lg != null)
        {
            LOD[] l = lg.GetLODs();
            float[] t = { 0.11f, 0.045f, 0.0001f };
            for (int i = 0; i < l.Length; i++) l[i].screenRelativeTransitionHeight = t[Mathf.Min(i, 2)];
            lg.SetLODs(l); lg.fadeMode = LODFadeMode.None;
        }
        return g;
    }

    public int ModelProps { get; private set; }

    private void BuildProps(LevelConfig cfg, System.Action<Object> track)
    {
        Material bark = BarkMat("prop_bark", new Color(0.95f, 0.92f, 0.88f), 0.9f, 0.45f, 0.35f, track);
        Material poleWood = BarkMat("prop_pole", new Color(0.62f, 0.52f, 0.44f), 0.7f, 0.3f, 0.35f, track);
        Material dirtM = BarkMat("prop_bark", new Color(0.25f, 0.21f, 0.15f), 1.6f, 1f, 0.1f, track);
        Material metal = ProceduralAssets.CreateMaterial(new Color(0.22f, 0.21f, 0.20f), null, Vector2.one, 0.35f); track(metal); metal.SetFloat("_Metallic", 0.75f);
        Material ceramic = ProceduralAssets.CreateMaterial(new Color(0.42f, 0.36f, 0.30f), null, Vector2.one, 0.75f); track(ceramic);
        Material wire = ProceduralAssets.CreateMaterial(new Color(0.05f, 0.05f, 0.05f), null, Vector2.one, 0.1f); track(wire);
        Transform proot = new GameObject("Props").transform;
        proot.SetParent(transform, false);
        Vector3 prevA = Vector3.zero, prevB = Vector3.zero; bool hasPrev = false;
        foreach (FieldPlan.Prop p in plan.props)
        {
            float gy = FieldHeight.H(p.pos.x, p.pos.y);
            GameObject go = new GameObject(p.kind == FieldPlan.PropPole ? "Pole" : p.kind == FieldPlan.PropDoor ? "LoneDoor" : "DeadTree");
            go.transform.SetParent(proot, false);
            go.transform.localPosition = new Vector3(p.pos.x, gy, p.pos.y);
            go.transform.localRotation = Quaternion.Euler(0f, p.yaw, 0f);
            System.Random r = new System.Random(Mathf.RoundToInt(p.pos.x * 31 + p.pos.y * 17));
            if (p.kind == FieldPlan.PropPole)
            {
                go.transform.localRotation = Quaternion.Euler((float)r.NextDouble() * 3f - 1.5f, p.yaw, (float)r.NextDouble() * 3f - 1.5f);
                GameObject m = SpawnModel("UtilityPole", go.transform, new[] { poleWood, metal, ceramic, dirtM }, new[] { "PoleWood", "PoleMetal", "PoleCeramic", "PoleDirt" }, track);
                CapsuleCollider cc = go.AddComponent<CapsuleCollider>(); cc.radius = 0.16f; cc.height = 8.2f; cc.center = new Vector3(0f, 4.1f, 0f);
                // cables desde los aisladores exteriores (eje de la cruceta = eje horizontal más largo del modelo)
                Vector3 axis = Vector3.forward; float topY = 7.84f;
                if (m != null) { MeshFilter mf = m.GetComponentInChildren<MeshFilter>(); if (mf != null) { Bounds mb = mf.sharedMesh.bounds; axis = mb.extents.x > mb.extents.z ? Vector3.right : Vector3.forward; topY = mb.max.y; } }
                Vector3 a = go.transform.TransformPoint(new Vector3(0f, topY - 0.02f, 0f) + axis * 0.85f), b = go.transform.TransformPoint(new Vector3(0f, topY - 0.02f, 0f) - axis * 0.85f);
                if (hasPrev && (a - prevA).magnitude < 40f)
                {
                    bool swap = (a - prevA).sqrMagnitude + (b - prevB).sqrMagnitude > (a - prevB).sqrMagnitude + (b - prevA).sqrMagnitude;
                    Wire(proot, prevA, swap ? b : a, wire); Wire(proot, prevB, swap ? a : b, wire);
                }
                prevA = a; prevB = b; hasPrev = true;
                if (m != null) ModelProps++;
            }
            else if (p.kind == FieldPlan.PropDoor) BuildLoneDoor(go.transform, cfg, track);
            else
            {
                go.transform.localRotation = Quaternion.Euler((float)r.NextDouble() * 4f - 2f, p.yaw, (float)r.NextDouble() * 4f - 2f);
                float sc = 0.85f + (float)r.NextDouble() * 0.4f;
                go.transform.localScale = Vector3.one * sc;
                GameObject m = SpawnModel("DeadTree" + r.Next(3), go.transform, new[] { bark }, new[] { "Bark" }, track);
                CapsuleCollider cc = go.AddComponent<CapsuleCollider>(); cc.radius = 0.28f; cc.height = 3.2f; cc.center = new Vector3(0f, 1.6f, 0f);
                if (m != null) ModelProps++;
            }
            propCount++;
        }
        GhostTree(proot);
    }

    // cable combado como malla de tubo (4 caras): sin depender de la cámara y con copia en el borde
    private void Wire(Transform parent, Vector3 a, Vector3 b, Material m)
    {
        const int N = 14; const float R = 0.012f;
        GameObject go = new GameObject("Wire");
        go.transform.SetParent(parent, false);
        Vector3 la = parent.InverseTransformPoint(a), lb = parent.InverseTransformPoint(b);
        Vector3[] pts = new Vector3[N];
        for (int i = 0; i < N; i++) { float t = i / (N - 1f); pts[i] = Vector3.Lerp(la, lb, t) + Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.9f); }
        List<Vector3> v = new List<Vector3>(); List<Vector3> n = new List<Vector3>(); List<int> tri = new List<int>();
        for (int i = 0; i < N; i++)
        {
            Vector3 f = (pts[Mathf.Min(i + 1, N - 1)] - pts[Mathf.Max(i - 1, 0)]).normalized;
            Vector3 r = Vector3.Cross(Vector3.up, f).normalized, u = Vector3.Cross(f, r);
            for (int k = 0; k < 4; k++) { float ang = k * Mathf.PI * 0.5f; Vector3 d = r * Mathf.Cos(ang) + u * Mathf.Sin(ang); v.Add(pts[i] + d * R); n.Add(d); }
            if (i > 0) for (int k = 0; k < 4; k++) { int a0 = (i - 1) * 4 + k, a1 = (i - 1) * 4 + (k + 1) % 4, b0 = i * 4 + k, b1 = i * 4 + (k + 1) % 4; tri.Add(a0); tri.Add(a1); tri.Add(b0); tri.Add(a1); tri.Add(b1); tri.Add(b0); }
        }
        Mesh mesh = new Mesh { name = "Wire" }; mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(tri, 0); mesh.RecalculateBounds(); owned.Add(mesh);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    // v7.0.1: materiales PBR CC0 (Poly Haven) desde Resources/Field/<dir>/<name>_diff|_nor|_mask (mask: G=AO, A=suavidad)
    public static Material PbrMat(string dir, string name, Color tint, Vector2 tiling, float normalScale, float glossScale, System.Action<Object> track)
    {
        Texture2D d = Resources.Load<Texture2D>("Field/" + dir + "/" + name + "_diff");
        Material m = ProceduralAssets.CreateMaterial(tint, d, tiling, 0.3f); track(m);
        Texture2D n = Resources.Load<Texture2D>("Field/" + dir + "/" + name + "_nor"), k = Resources.Load<Texture2D>("Field/" + dir + "/" + name + "_mask");
        if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.SetTextureScale("_BumpMap", tiling); m.SetFloat("_BumpScale", normalScale); m.EnableKeyword("_NORMALMAP"); }
        if (k != null && m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", k); m.SetFloat("_GlossMapScale", glossScale); m.EnableKeyword("_METALLICGLOSSMAP"); }
        if (k != null && m.HasProperty("_OcclusionMap")) { m.SetTexture("_OcclusionMap", k); m.SetFloat("_OcclusionStrength", 1f); }
        m.SetFloat("_SpecularHighlights", 1f);
        return m;
    }

    // material con transparencia (Standard en modo Fade) para la sombra de contacto y el barro
    public static Material FadeMat(string res, Color tint, System.Action<Object> track)
    {
        Material m = ProceduralAssets.CreateMaterial(tint, Resources.Load<Texture2D>(res), Vector2.one, 0f); track(m);
        m.SetFloat("_Mode", 2f); m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON"); m.EnableKeyword("_ALPHABLEND_ON"); m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetFloat("_Glossiness", 0f); m.renderQueue = 3000;
        return m;
    }

    static GameObject Part(PrimitiveType t, string n, Transform p, Vector3 pos, Vector3 size, Material m, bool col)
    {
        GameObject g = ProceduralAssets.CreatePrimitive(t, n, p, pos, size, m, col);
        Renderer r = g.GetComponent<Renderer>(); if (r != null) { r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true; }
        return g;
    }

    // Puerta suelta (señuelo y salida, idénticas salvo las pistas de la salida):
    // hoja de madera pintada y desgastada con plafones en relieve, marco con profundidad, umbral, pomo y escudo de latón,
    // tres bisagras, barro en la base y sombra de contacto en el suelo. Albedo medio (sin blancos quemados con bloom/exposición).
    public static void BuildLoneDoor(Transform parent, LevelConfig cfg, System.Action<Object> track)
    {
        const float doorH = 2.15f, doorW = 0.92f, th = 0.045f, jamb = 0.10f, depth = 0.16f;
        Color paint = new Color(0.58f, 0.60f, 0.55f);   // multiplica la textura (gris verdoso desgastado): albedo ~0.3, no se quema con la linterna
        Material slabMat = PbrMat("Door", "door_paint", paint, new Vector2(0.9f, 2.1f), 1.0f, 0.3f, track);
        Material moldMat = PbrMat("Door", "door_paint", paint * 0.93f, new Vector2(0.25f, 0.6f), 1.0f, 0.3f, track);
        Material frameMat = PbrMat("Door", "door_frame", new Color(0.82f, 0.76f, 0.68f), new Vector2(0.18f, 2.2f), 1.2f, 0.5f, track);
        Material sillMat = PbrMat("Door", "door_frame", new Color(0.6f, 0.55f, 0.5f), new Vector2(1.2f, 0.25f), 1.2f, 0.4f, track);
        Material brass = ProceduralAssets.CreateMaterial(new Color(0.52f, 0.42f, 0.24f), null, Vector2.one, 0.55f); track(brass); brass.SetFloat("_Metallic", 0.9f);
        Material iron = ProceduralAssets.CreateMaterial(new Color(0.16f, 0.14f, 0.12f), null, Vector2.one, 0.3f); track(iron); iron.SetFloat("_Metallic", 0.7f);

        // hoja (colisión) y plafones en relieve: 4 largueros + 3 travesaños por cara; los plafones quedan rehundidos
        Part(PrimitiveType.Cube, "DoorSlab", parent, new Vector3(0f, doorH * 0.5f + 0.01f, 0f), new Vector3(doorW, doorH - 0.02f, th), slabMat, true);
        foreach (float side in new[] { 1f, -1f })
        {
            float z = side * (th * 0.5f + 0.006f);
            Part(PrimitiveType.Cube, "StileL", parent, new Vector3(-doorW * 0.5f + 0.06f, doorH * 0.5f, z), new Vector3(0.12f, doorH - 0.04f, 0.012f), moldMat, false);
            Part(PrimitiveType.Cube, "StileR", parent, new Vector3(doorW * 0.5f - 0.06f, doorH * 0.5f, z), new Vector3(0.12f, doorH - 0.04f, 0.012f), moldMat, false);
            Part(PrimitiveType.Cube, "StileM", parent, new Vector3(0f, doorH * 0.5f, z), new Vector3(0.08f, doorH - 0.1f, 0.012f), moldMat, false);
            foreach (float y in new[] { 0.10f, 1.02f, doorH - 0.08f })
                Part(PrimitiveType.Cube, "Rail", parent, new Vector3(0f, y, z), new Vector3(doorW - 0.02f, y < 0.5f ? 0.2f : 0.14f, 0.012f), moldMat, false);
            // pomo + escudo con bocallave
            float hx = doorW * 0.5f - 0.11f;
            Part(PrimitiveType.Cube, "Escutcheon", parent, new Vector3(hx, 0.98f, side * (th * 0.5f + 0.004f)), new Vector3(0.045f, 0.16f, 0.008f), brass, false);
            Part(PrimitiveType.Cylinder, "Spindle", parent, new Vector3(hx, 1.0f, side * (th * 0.5f + 0.03f)), new Vector3(0.018f, 0.025f, 0.018f), brass, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Part(PrimitiveType.Sphere, "Knob", parent, new Vector3(hx, 1.0f, side * (th * 0.5f + 0.062f)), new Vector3(0.058f, 0.058f, 0.042f), brass, false);
            Part(PrimitiveType.Cube, "Keyhole", parent, new Vector3(hx, 0.93f, side * (th * 0.5f + 0.0085f)), new Vector3(0.008f, 0.022f, 0.002f), iron, false);
        }
        // bisagras (lado izquierdo, visibles en el canto)
        foreach (float y in new[] { 0.25f, 1.08f, doorH - 0.25f })
        {
            Part(PrimitiveType.Cylinder, "Hinge", parent, new Vector3(-doorW * 0.5f - 0.004f, y, th * 0.5f), new Vector3(0.016f, 0.05f, 0.016f), iron, false);
            Part(PrimitiveType.Cube, "HingeLeaf", parent, new Vector3(-doorW * 0.5f + 0.02f, y, th * 0.5f + 0.002f), new Vector3(0.045f, 0.09f, 0.003f), iron, false);
        }
        // marco con profundidad (jambas más anchas que la hoja, tapajuntas en ambas caras) + umbral
        float fx = doorW * 0.5f + jamb * 0.5f;
        Part(PrimitiveType.Cube, "FrameL", parent, new Vector3(-fx, (doorH + jamb) * 0.5f, 0f), new Vector3(jamb, doorH + jamb, depth), frameMat, true);
        Part(PrimitiveType.Cube, "FrameR", parent, new Vector3(fx, (doorH + jamb) * 0.5f, 0f), new Vector3(jamb, doorH + jamb, depth), frameMat, true);
        Part(PrimitiveType.Cube, "FrameTop", parent, new Vector3(0f, doorH + jamb * 0.5f, 0f), new Vector3(doorW + jamb * 2f, jamb, depth), frameMat, false);
        foreach (float side in new[] { 1f, -1f })
        {
            float z = side * (depth * 0.5f + 0.008f), cx = fx + 0.025f;
            Part(PrimitiveType.Cube, "CasingL", parent, new Vector3(-cx, (doorH + jamb + 0.06f) * 0.5f, z), new Vector3(0.07f, doorH + jamb + 0.06f, 0.016f), frameMat, false);
            Part(PrimitiveType.Cube, "CasingR", parent, new Vector3(cx, (doorH + jamb + 0.06f) * 0.5f, z), new Vector3(0.07f, doorH + jamb + 0.06f, 0.016f), frameMat, false);
            Part(PrimitiveType.Cube, "CasingTop", parent, new Vector3(0f, doorH + jamb + 0.03f, z), new Vector3(doorW + jamb * 2f + 0.19f, 0.08f, 0.016f), frameMat, false);
        }
        Part(PrimitiveType.Cube, "Sill", parent, new Vector3(0f, -0.02f, 0f), new Vector3(doorW + jamb * 2f + 0.1f, 0.06f, depth + 0.12f), sillMat, false);
        // barro salpicado en la base de la hoja y las jambas (ambas caras)
        Material dirt = FadeMat("Field/Door/door_dirt", new Color(1f, 1f, 1f, 0.9f), track);
        foreach (float side in new[] { 1f, -1f })
        {
            GameObject q = ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "Dirt", parent, new Vector3(0f, 0.2f, side * (depth * 0.5f + 0.018f)), new Vector3(doorW + jamb * 2f + 0.2f, 0.42f, 1f), dirt, false);
            q.transform.localRotation = Quaternion.Euler(0f, side > 0 ? 180f : 0f, 0f);
            q.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            GameObject q2 = ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "DirtSlab", parent, new Vector3(0f, 0.2f, side * (th * 0.5f + 0.014f)), new Vector3(doorW - 0.02f, 0.42f, 1f), dirt, false);
            q2.transform.localRotation = q.transform.localRotation; q2.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        // sombra de contacto / oclusión en el suelo (proyectada por encima de la hierba baja)
        Material blob = FadeMat("Field/Door/door_contact", new Color(0.05f, 0.05f, 0.04f, 0.75f), track);
        GameObject sh = ProceduralAssets.CreatePrimitive(PrimitiveType.Quad, "ContactShadow", parent, new Vector3(0f, 0.025f, 0f), new Vector3(doorW + 1.1f, 1.3f, 1f), blob, false);
        sh.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        sh.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
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

    public static float LastGhostMs; public static int GhostDraws;
    private readonly Plane[] gplanes = new Plane[6];
    private static readonly System.Diagnostics.Stopwatch gsw = new System.Diagnostics.Stopwatch();
    public void DrawGhosts()
    {
        gsw.Restart(); GhostDraws = 0;
        DrawGhostsInner();
        LastGhostMs = (float)gsw.Elapsed.TotalMilliseconds;
    }
    private void DrawGhostsInner()
    {
        Camera cam = Camera.main;
        if (cam == null || levelRoot == null) return;
        Vector3 c = levelRoot.InverseTransformPoint(cam.transform.position);
        float range = GhostRange;   // v7.0.1: hasta el plano lejano (antes 110/170 m: saltos visibles entre la niebla)
        Matrix4x4 lw = levelRoot.localToWorldMatrix;
        GeometryUtility.CalculateFrustumPlanes(cam, gplanes);   // v7.0.2: solo las copias dentro de la vista
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
                    if (!GhostInRange(c, gh.center, gh.radius, off, range)) continue;
                    Vector3 wp = lw.MultiplyPoint3x4(p); bool inView = true;
                    for (int k = 0; k < 6 && inView; k++) if (gplanes[k].GetDistanceToPoint(wp) < -gh.radius) inView = false;
                    if (!inView) continue;
                    Matrix4x4 m = sh * gh.local;
                    for (int s = 0; s < gh.mats.Length && s < gh.mesh.subMeshCount; s++)
                        { Graphics.DrawMesh(gh.mesh, m, gh.mats[s], 0, null, s, null, ShadowCastingMode.Off, false); GhostDraws++; }
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
