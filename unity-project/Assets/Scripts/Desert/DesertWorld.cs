using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.7.0 Nivel 114 «El Desierto Cálido» (Backrooms Wiki en español: nivel de Sonicelmaspro2, ampliado por Omaigad12).
// Construye el desierto infinito (periódico, como el Campo infinito) a partir de DesertPlan (solo semilla → idéntico en MP):
//  · dunas por trozos de 32 m con collider, LOD lejano y copias «fantasma» al otro lado del borde,
//  · aldeas abandonadas con casas de adobe y piedra en ruinas (muros rotos, techos de vigas hundidos, escombros, arena
//    metida dentro), muebles rotos escaneados (CC0), pozos de piedra, la torre en ruinas de la aldea de la salida y la casa
//    cuya puerta de madera se sustituyó por una PUERTA DE PIEDRA (la salida),
//  · afloramientos de roca, dos cuevas con vetas de hierro, oro, plomo y diamante, matas secas y piedras sueltas.
// En ejecución: vuelta al desierto (te teletransportas L metros al cruzar el borde) y dibujo de las copias del borde.
public class DesertWorld : MonoBehaviour
{
    public static DesertWorld Active;
    public DesertPlan plan;
    public float L;
    public Transform levelRoot;
    public BackroomsGenerator gen;
    public int chunkCount, houseCount, propCount, rockCount, plantCount, caveCount, wellCount, furnitureCount, veinCount;
    public Vector3 StartLocal { get; private set; }
    public float StartYaw { get; private set; }
    public readonly List<Vector3> BatterySpots = new List<Vector3>();
    public readonly List<Vector3> ExtraSpots = new List<Vector3>();
    public readonly List<Vector3> SandSpots = new List<Vector3>();     // objetos tirados en la arena (mundo)
    public Transform ExitTransform { get; private set; }
    public Vector3 ExitLocal { get; private set; }                       // delante de la puerta de piedra (pruebas de alcance)
    public Vector3 TowerLocal { get; private set; }
    public int WrapCount { get; private set; }
    public Material SandMat { get; private set; }

    public struct Slot { public Vector3 pos; public int house; public int kind; }   // local, superficie + 0,1 m
    public readonly List<Slot> Slots = new List<Slot>();
    public struct HouseBox { public Vector2 c; public float yaw; public Vector2 half; public bool roofed; }
    public readonly List<HouseBox> HouseBoxes = new List<HouseBox>();

    private struct Ghost { public Mesh mesh, far; public Matrix4x4 local; public Material[] mats; public Vector3 center; public float radius; }
    private readonly List<Ghost> ghosts = new List<Ghost>();
    private readonly List<Object> owned = new List<Object>();
    private System.Action<Object> track;
    private FirstPersonController fpc;

    public const float ChunkSize = 32f, WallT = 0.45f, DoorW = 1.1f, DoorH = 2.2f;
    public static float FarClip { get { return GameSettings.Quality == 0 ? 300f : 460f; } }
    public static float GhostRange { get { return FarClip + 12f; } }

    // ------------------------------------------------------------------ construcción
    public static DesertWorld Build(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot, DesertPlan plan, System.Action<Object> track)
    {
        GameObject root = new GameObject("Desert");
        root.transform.SetParent(levelRoot, false);
        DesertWorld dw = root.AddComponent<DesertWorld>();
        dw.plan = plan; dw.gen = gen; dw.levelRoot = levelRoot; dw.L = plan.L; dw.track = o => { track(o); };
        FieldWrap.OtherRoot = levelRoot;
        LoadDiag.Step("desierto: materiales");
        dw.MakeMaterials();
        LoadDiag.Step("desierto: dunas");
        dw.BuildTerrain();
        LoadDiag.Step("desierto: aldeas");
        dw.BuildVillages();
        LoadDiag.Step("desierto: rocas y cuevas");
        dw.BuildOutcrops();
        LoadDiag.Step("desierto: plantas");
        dw.BuildPlants();
        LoadDiag.Step("desierto: puntos");
        dw.PickSpots(cfg);
        Vector2 s = plan.start;
        dw.StartLocal = new Vector3(s.x, DesertHeight.H(s.x, s.y), s.y);
        // mirando hacia la aldea más cercana (se ve a lo lejos entre las dunas)
        float best = float.MaxValue; Vector2 to = Vector2.up;
        foreach (DesertPlan.Village v in plan.villages)
        {
            float d = plan.Dist(v.c, s); if (d >= best) continue;
            best = d; to = new Vector2(DesertHeight.Wrap(v.c.x - s.x), DesertHeight.Wrap(v.c.y - s.y));
        }
        dw.StartYaw = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
        Active = dw;
        return dw;
    }

    // ------------------------------------------------------------------ materiales (texturas CC0 de Poly Haven, tamaño real)
    private Material sandM, plasterM, clayM, stoneM, stone2M, woodM, mudM, floorM, rockM, sandstoneM, blocksM, rubbleM, wornM, caveM;
    private Material goldM, ironM, leadM, diamondM;
    private readonly Dictionary<string, Material[]> modelMats = new Dictionary<string, Material[]>();

    private Material Pbr(string name, Color tint, float meters, float smooth = 1f, float bump = 1f)
    {
        Texture2D a = Resources.Load<Texture2D>("Desert/Tex/" + name + "_a");
        Material m = ProceduralAssets.CreateMaterial(tint, a, Vector2.one / meters, 0.2f); track(m); m.name = "D_" + name;
        Texture2D n = Resources.Load<Texture2D>("Desert/Tex/" + name + "_n"), k = Resources.Load<Texture2D>("Desert/Tex/" + name + "_m");
        if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.SetFloat("_BumpScale", bump); m.EnableKeyword("_NORMALMAP"); }
        if (k != null && m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", k); m.SetFloat("_GlossMapScale", smooth); m.EnableKeyword("_METALLICGLOSSMAP"); }
        if (k != null && m.HasProperty("_OcclusionMap")) { m.SetTexture("_OcclusionMap", k); m.SetFloat("_OcclusionStrength", 1f); }
        return m;
    }

    private void MakeMaterials()
    {
        Shader ss = Shader.Find("Backrooms/DesertSand");
        sandM = new Material(ss != null ? ss : Shader.Find("Standard")) { name = "DesertSand" }; track(sandM);
        sandM.SetTexture("_GrainA", Resources.Load<Texture2D>("Desert/Tex/grain_a"));
        sandM.SetTexture("_GrainN", Resources.Load<Texture2D>("Desert/Tex/grain_n"));
        sandM.SetTexture("_MacroA", Resources.Load<Texture2D>("Desert/Tex/macro_a"));
        sandM.SetFloat("_L", L);
        SandMat = sandM;
        plasterM = Pbr("plaster", new Color(0.95f, 0.86f, 0.72f), 2.0f, 0.6f);
        clayM = Pbr("clay", new Color(0.92f, 0.78f, 0.62f), 2.0f, 0.6f);
        stoneM = Pbr("stone", new Color(0.98f, 0.86f, 0.70f), 2.5f, 0.7f);
        stone2M = Pbr("stone2", new Color(0.95f, 0.82f, 0.68f), 2.0f, 0.7f);
        woodM = Pbr("wood", new Color(0.78f, 0.70f, 0.60f), 2.0f, 0.6f);
        mudM = Pbr("mud", new Color(0.86f, 0.72f, 0.56f), 2.0f, 0.5f);
        floorM = Pbr("floor", new Color(0.86f, 0.74f, 0.60f), 3.0f, 0.5f);
        rockM = Pbr("rock", new Color(0.92f, 0.70f, 0.52f), 4.0f, 0.7f);
        sandstoneM = Pbr("sandstone", new Color(0.95f, 0.82f, 0.66f), 3.0f, 0.7f);
        blocksM = Pbr("blocks", new Color(1.05f, 0.92f, 0.76f), 2.4f, 0.7f);
        rubbleM = Pbr("rubble", new Color(0.92f, 0.80f, 0.64f), 3.0f, 0.6f);
        wornM = Pbr("worn", new Color(0.95f, 0.86f, 0.74f), 2.0f, 0.6f);
        caveM = Pbr("rock", new Color(0.74f, 0.60f, 0.48f), 3.0f, 0.6f);
        goldM = Ore(new Color(1f, 0.76f, 0.33f), 1f, 0.78f, Color.black);
        ironM = Ore(new Color(0.46f, 0.26f, 0.16f), 0.45f, 0.35f, Color.black);
        leadM = Ore(new Color(0.44f, 0.46f, 0.50f), 0.9f, 0.5f, Color.black);
        diamondM = Ore(new Color(0.92f, 0.97f, 1f), 0.1f, 0.97f, new Color(0.10f, 0.13f, 0.16f));
    }

    private Material Ore(Color c, float metal, float smooth, Color emission)
    {
        Material m = ProceduralAssets.CreateMaterial(c, null, Vector2.one, smooth); track(m);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        if (emission.maxColorComponent > 0f) ProceduralAssets.SetEmission(m, emission);
        return m;
    }

    // materiales de un modelo convertido (Resources/Desert/Props/<Nombre>_m<i>_a|_n|_m)
    private Material[] ModelMats(string name, Color tint, bool cutout)
    {
        string key = name + (cutout ? "|c" : "") + tint.ToString();
        if (modelMats.TryGetValue(key, out Material[] ms)) return ms;
        List<Material> list = new List<Material>();
        for (int i = 0; i < 4; i++)
        {
            Texture2D a = Resources.Load<Texture2D>("Desert/Props/" + name + "_m" + i + "_a");
            if (a == null) break;
            Material m = ProceduralAssets.CreateMaterial(tint, a, Vector2.one, 0.2f); track(m); m.name = name + "_m" + i;
            Texture2D n = Resources.Load<Texture2D>("Desert/Props/" + name + "_m" + i + "_n"), k = Resources.Load<Texture2D>("Desert/Props/" + name + "_m" + i + "_m");
            if (n != null && m.HasProperty("_BumpMap")) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
            if (k != null && m.HasProperty("_MetallicGlossMap")) { m.SetTexture("_MetallicGlossMap", k); m.SetFloat("_GlossMapScale", 0.8f); m.EnableKeyword("_METALLICGLOSSMAP"); }
            if (k != null && m.HasProperty("_OcclusionMap")) { m.SetTexture("_OcclusionMap", k); m.SetFloat("_OcclusionStrength", 1f); }
            if (cutout)
            {
                m.SetFloat("_Mode", 1f); m.SetFloat("_Cutoff", 0.5f); m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout"); m.renderQueue = 2450; m.doubleSidedGI = true;
                if (m.HasProperty("_Cull")) m.SetInt("_Cull", 0);
            }
            list.Add(m);
        }
        ms = list.ToArray(); modelMats[key] = ms;
        return ms;
    }

    // instancia un modelo con sus materiales y LOD que no desaparecen (el último se queda hasta la niebla)
    private GameObject Model(string name, Transform parent, Vector3 pos, Quaternion rot, float scale, Color tint, bool shadows = true, bool cutout = false, bool collider = false)
    {
        GameObject src = Resources.Load<GameObject>("Desert/Props/" + name);
        if (src == null) return null;
        GameObject g = Object.Instantiate(src, parent, false); g.name = name;
        g.transform.localPosition = pos; g.transform.localRotation = rot; g.transform.localScale = Vector3.one * scale;
        Material[] ms = ModelMats(name, tint, cutout);
        foreach (MeshRenderer r in g.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material[] cur = r.sharedMaterials;
            for (int i = 0; i < cur.Length; i++)
            {
                int idx = 0;
                if (cur[i] != null) { string nm = cur[i].name; int p = nm.LastIndexOf('M'); if (p >= 0 && p + 1 < nm.Length && char.IsDigit(nm[p + 1])) idx = nm[p + 1] - '0'; }
                cur[i] = ms.Length > 0 ? ms[Mathf.Min(idx, ms.Length - 1)] : cur[i];
            }
            r.sharedMaterials = cur; r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off; r.receiveShadows = true;
        }
        LODGroup lg = g.GetComponent<LODGroup>();
        if (lg != null)
        {
            LOD[] l = lg.GetLODs();
            float big = Mathf.Clamp01(scale * 0.25f);
            float[] t = { 0.16f - 0.08f * big, 0.05f - 0.03f * big, 0.0001f };
            for (int i = 0; i < l.Length; i++) l[i].screenRelativeTransitionHeight = t[Mathf.Min(i, 2)];
            lg.SetLODs(l); lg.fadeMode = LODFadeMode.None;
        }
        if (collider)
        {
            // colisión con la malla más simple (LOD2)
            MeshFilter best = null; int bt = int.MaxValue;
            foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.triangles.Length < bt) { bt = mf.sharedMesh.triangles.Length; best = mf; }
            if (best != null) best.gameObject.AddComponent<MeshCollider>().sharedMesh = best.sharedMesh;
        }
        propCount++;
        return g;
    }

    // ------------------------------------------------------------------ dunas
    private void BuildTerrain()
    {
        int nc = Mathf.Max(1, Mathf.RoundToInt(L / ChunkSize));
        float cs = L / nc;
        int res = Mathf.RoundToInt(cs);   // 1 m
        Transform troot = new GameObject("Dunes").transform; troot.SetParent(transform, false);
        for (int cx = 0; cx < nc; cx++)
            for (int cz = 0; cz < nc; cz++)
            {
                float x0 = cx * cs, z0 = cz * cs;
                Mesh near = ChunkMesh(x0, z0, cs, res, false), far = ChunkMesh(x0, z0, cs, res / 4, true);
                near.name = "Dune_" + cx + "_" + cz; far.name = near.name + "_far";
                track(near); track(far);
                GameObject go = new GameObject(near.name); go.transform.SetParent(troot, false);
                GameObject g0 = new GameObject("LOD0"); g0.transform.SetParent(go.transform, false);
                g0.AddComponent<MeshFilter>().sharedMesh = near;
                MeshRenderer r0 = g0.AddComponent<MeshRenderer>(); r0.sharedMaterial = sandM; r0.shadowCastingMode = ShadowCastingMode.On; r0.receiveShadows = true;
                GameObject g1 = new GameObject("LOD1"); g1.transform.SetParent(go.transform, false);
                g1.AddComponent<MeshFilter>().sharedMesh = far;
                MeshRenderer r1 = g1.AddComponent<MeshRenderer>(); r1.sharedMaterial = sandM; r1.shadowCastingMode = ShadowCastingMode.Off; r1.receiveShadows = true;
                LODGroup lg = go.AddComponent<LODGroup>();
                lg.SetLODs(new[] { new LOD(0.16f, new Renderer[] { r0 }), new LOD(0.0001f, new Renderer[] { r1 }) });
                lg.fadeMode = LODFadeMode.None; lg.RecalculateBounds();
                go.AddComponent<MeshCollider>().sharedMesh = near;
                ghosts.Add(new Ghost { mesh = near, far = far, local = Matrix4x4.identity, mats = new[] { sandM }, center = near.bounds.center, radius = cs * 0.75f });
                chunkCount++;
            }
    }

    // trozo de dunas: normales de las diferencias de la rejilla (con un borde extra) y, en el LOD lejano, faldón hacia abajo
    private Mesh ChunkMesh(float x0, float z0, float cs, int res, bool skirt)
    {
        float step = cs / res; int vn = res + 1, gn = vn + 2;
        float[] h = new float[gn * gn];
        for (int j = 0; j < gn; j++)
            for (int i = 0; i < gn; i++) h[j * gn + i] = DesertHeight.H(x0 + (i - 1) * step, z0 + (j - 1) * step);
        int extra = skirt ? res * 4 * 2 + 8 : 0;
        List<Vector3> v = new List<Vector3>(vn * vn + extra); List<Vector3> nr = new List<Vector3>(vn * vn + extra); List<Vector2> uv = new List<Vector2>(vn * vn + extra);
        for (int j = 0; j < vn; j++)
            for (int i = 0; i < vn; i++)
            {
                int gi = (j + 1) * gn + (i + 1);
                float x = x0 + i * step, z = z0 + j * step;
                v.Add(new Vector3(x, h[gi], z));
                nr.Add(new Vector3(h[gi - 1] - h[gi + 1], 2f * step, h[gi - gn] - h[gi + gn]).normalized);
                uv.Add(new Vector2(x, z));
            }
        List<int> tri = new List<int>(res * res * 6 + extra * 3);
        for (int j = 0; j < res; j++)
            for (int i = 0; i < res; i++)
            {
                int a = j * vn + i, b = a + 1, c = a + vn, d = c + 1;
                // diagonal alternada según la pendiente (crestas más limpias)
                if (Mathf.Abs(v[a].y - v[d].y) < Mathf.Abs(v[b].y - v[c].y)) { tri.Add(a); tri.Add(c); tri.Add(d); tri.Add(a); tri.Add(d); tri.Add(b); }
                else { tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d); }
            }
        if (skirt)
        {
            // borde del trozo: faldón de 1,5 m hacia abajo (tapa las rendijas con los trozos cercanos de más detalle)
            List<int> edge = new List<int>();
            for (int i = 0; i < res; i++) edge.Add(i);
            for (int j = 0; j < res; j++) edge.Add(j * vn + res);
            for (int i = res; i > 0; i--) edge.Add(res * vn + i);
            for (int j = res; j > 0; j--) edge.Add(j * vn);
            // v7.7.0 a: una sola cara hacia fuera (se ve desde los trozos vecinos) con la normal algo inclinada hacia fuera
            Vector3 ctr = Vector3.zero; foreach (int e in edge) ctr += v[e]; ctr /= edge.Count;
            int b0 = v.Count;
            foreach (int e in edge) { v.Add(v[e] + Vector3.down * 1.5f); uv.Add(uv[e]); }
            for (int k = 0; k < edge.Count; k++)
            {
                int e = edge[k]; Vector3 o = v[e] - ctr; o.y = 0f; o.Normalize();
                nr.Add((o + Vector3.up * 0.4f).normalized);   // v7.7.0 g: hacia fuera de verdad (con la normal del terreno, en laderas hacia dentro quedaba al revés)
            }
            for (int k = 0; k < edge.Count; k++)
            {
                int a = edge[k], b = edge[(k + 1) % edge.Count], c = b0 + k, d = b0 + (k + 1) % edge.Count;
                Vector3 o = (v[a] + v[b]) * 0.5f - ctr; o.y = 0f;
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), o) >= 0f) { tri.Add(a); tri.Add(b); tri.Add(c); tri.Add(b); tri.Add(d); tri.Add(c); }
                else { tri.Add(a); tri.Add(c); tri.Add(b); tri.Add(b); tri.Add(c); tri.Add(d); }
            }
        }
        Mesh m = new Mesh();
        m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        m.RecalculateBounds(); m.RecalculateTangents();
        return m;
    }

    // ------------------------------------------------------------------ aldeas
    private void BuildVillages()
    {
        Transform vroot = new GameObject("Villages").transform; vroot.SetParent(transform, false);
        for (int i = 0; i < plan.houses.Count; i++) BuildHouse(vroot, plan.houses[i], i);
        for (int vi = 0; vi < plan.villages.Count; vi++)
        {
            DesertPlan.Village v = plan.villages[vi];
            System.Random r = new System.Random(gen.UsedSeed * 71 + vi * 13 + 5);
            if (v.hasWell) BuildWell(vroot, v.well, r);
            // plaza: hoguera apagada, cántaros, cubos, piedras y matas pisoteadas
            Transform pr = new GameObject("Plaza" + vi).transform; pr.SetParent(vroot, false);
            Vector2 fp = Mod(v.c + Rot(new Vector2(4.5f, -2f), v.yaw));
            if (plan.FreeOf(fp, 1.5f, 2f, false)) Model("FFirePit", pr, Ground(fp, -0.03f), Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f), 1f, new Color(0.9f, 0.85f, 0.78f));
            int deco = 4 + r.Next(5);
            for (int k = 0; k < deco; k++)
            {
                float a = (float)r.NextDouble() * Mathf.PI * 2f, d = 3f + (float)r.NextDouble() * (v.r - 2f);
                Vector2 q = Mod(v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d);
                if (!FreeFromHouses(q, 1.2f)) continue;
                double kr = r.NextDouble();
                string nm = kr < 0.3 ? "DStones" : kr < 0.5 ? "DRocks" : kr < 0.62 ? "FPot" : kr < 0.74 ? "FCrate_broken" : kr < 0.86 ? "FBucket" : "DPebbles";
                Quaternion rot = Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f);
                if (nm == "FPot" || nm == "FBucket") if (r.NextDouble() < 0.5) rot = rot * Quaternion.Euler(0f, 0f, 90f);   // volcados
                Model(nm, pr, Ground(q, nm == "FPot" || nm == "FBucket" ? (rot.eulerAngles.z > 45f ? 0.14f : -0.02f) : -0.02f), rot, nm.StartsWith("D") ? 0.9f + (float)r.NextDouble() * 0.6f : 1f, new Color(0.92f, 0.86f, 0.78f));
            }
            if (v.exit) BuildTower(vroot, v, r);
        }
        GhostTree(vroot);
    }

    private static Vector2 Rot(Vector2 p, float yawDeg) { float a = -yawDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector2(p.x * c - p.y * s, p.x * s + p.y * c); }
    private Vector2 Mod(Vector2 p) { return new Vector2(Mathf.Repeat(p.x, L), Mathf.Repeat(p.y, L)); }
    private Vector3 Ground(Vector2 p, float dy) { return new Vector3(p.x, DesertHeight.H(p.x, p.y) + dy, p.y); }
    private bool FreeFromHouses(Vector2 q, float m)
    {
        foreach (DesertPlan.House h in plan.houses) if (plan.Dist(q, h.pos) < DesertPlan.TemplateSize[h.tpl].magnitude * 0.5f + m) return false;
        foreach (DesertPlan.Village v in plan.villages) if (v.hasWell && plan.Dist(q, v.well) < 2.2f) return false;
        return true;
    }

    private static float Hash(int a, int b) { unchecked { uint h = (uint)(a * 374761393 + b * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xffffff) / 16777215f; } }
    private static float VNoise(float x, int seed) { int i = Mathf.FloorToInt(x); float f = x - i; f = f * f * (3f - 2f * f); return Mathf.Lerp(Hash(i, seed), Hash(i + 1, seed), f); }

    // perfil del borde superior de un muro: entero (adobe hecho a mano), con mordiscos (ruina 1) o derruido (ruina 2)
    private System.Func<float, float> WallTop(System.Random r, float len, float Hw, int ruin, int seed, float keepAbove = 0f)
    {
        int nb = ruin == 0 ? 0 : ruin == 1 ? 1 + r.Next(2) : 0;
        float[] bc = new float[nb], bw = new float[nb], bd = new float[nb];
        for (int i = 0; i < nb; i++) { bc[i] = (float)r.NextDouble() * len; bw[i] = 0.8f + (float)r.NextDouble() * 1.8f; bd[i] = 0.7f + (float)r.NextDouble() * 1.4f; }
        float low = 0.35f + (float)r.NextDouble() * 1.1f, hi = low + 0.4f + (float)r.NextDouble() * 1.2f, ph = (float)r.NextDouble() * 10f;
        return x =>
        {
            float h = Hw + (VNoise(x * 1.3f, seed) - 0.5f) * 0.05f;
            if (ruin == 1)
                for (int i = 0; i < nb; i++)
                {
                    float u = Mathf.Abs(x - bc[i]) / bw[i];
                    if (u < 1f) h -= bd[i] * (1f - u * u) * (0.75f + 0.5f * VNoise(x * 4f, seed + i * 7));
                }
            if (ruin == 2) h = Mathf.Lerp(low, hi, VNoise(x * 0.55f + ph, seed + 3)) + (VNoise(x * 3.1f, seed + 5) - 0.5f) * 0.35f;
            // mordisco: bordes desmoronados (ruido fino) donde el muro está roto
            float broken = Mathf.Clamp01((Hw - h) / 0.5f);
            h += (VNoise(x * 7.3f, seed + 11) - 0.5f) * 0.16f * broken;
            return Mathf.Max(Mathf.Max(0.18f, keepAbove), h);
        };
    }

    private void BuildHouse(Transform vroot, DesertPlan.House h, int hi)
    {
        System.Random r = new System.Random(h.seed);
        Vector2 sz = DesertPlan.TemplateSize[h.tpl];
        float W = sz.x, D = sz.y, t = WallT, hp = DesertHeight.H(h.pos.x, h.pos.y);
        bool stone = !h.exit && r.NextDouble() < 0.35;          // casas de piedra (si no, adobe con zócalo de piedra)
        float Hw = 2.75f + (float)r.NextDouble() * 0.45f;
        GameObject go = new GameObject((h.exit ? "ExitHouse" : "House") + hi);
        go.transform.SetParent(vroot, false);
        go.transform.localPosition = new Vector3(h.pos.x, hp, h.pos.y);
        go.transform.localRotation = Quaternion.Euler(0f, h.yaw, 0f);
        Transform ht = go.transform;
        // submallas: 0 zócalo, 1 muro, 2 madera, 3 barro (techo), 4 suelo, 5 arena metida, 6 escombros
        DesertMesh dm = new DesertMesh(7);
        float doorX = W * 0.5f + (W > 5f ? ((float)r.NextDouble() - 0.5f) * (W - 3.2f) * 0.6f : 0f);
        float y0 = -0.4f, baseH = stone ? 99f : 0.45f + (float)r.NextDouble() * 0.2f;
        int subBase = 0, subBody = stone ? 0 : 1;
        // muros: delante (+z, con la puerta), detrás, izquierda, derecha
        var front = new List<DesertMesh.Opening> { new DesertMesh.Opening { x0 = doorX - DoorW * 0.5f, x1 = doorX + DoorW * 0.5f, y0 = y0 - 0.1f, y1 = DoorH } };
        if (W > 6f) { float wx = doorX < W * 0.5f ? W - 1.3f : 1.3f; front.Add(new DesertMesh.Opening { x0 = wx - 0.4f, x1 = wx + 0.4f, y0 = 1.0f, y1 = 1.75f }); }
        var back = new List<DesertMesh.Opening>();
        if (r.NextDouble() < 0.6) { float wx = W * (0.3f + (float)r.NextDouble() * 0.4f); back.Add(new DesertMesh.Opening { x0 = wx - 0.35f, x1 = wx + 0.35f, y0 = 1.1f, y1 = 1.75f }); }
        float sideLen = D - 2f * t;
        var left = new List<DesertMesh.Opening>(); var right = new List<DesertMesh.Opening>();
        if (sideLen > 3f) { float wz = sideLen * 0.5f; (r.NextDouble() < 0.5 ? left : right).Add(new DesertMesh.Opening { x0 = wz - 0.38f, x1 = wz + 0.38f, y0 = 1.0f, y1 = 1.75f }); }
        int ruin = h.ruin;
        int sd = h.seed & 0xffff;
        Matrix4x4 mf = Matrix4x4.Translate(new Vector3(-W * 0.5f, 0f, D * 0.5f - t * 0.5f));
        Matrix4x4 mb = Matrix4x4.Translate(new Vector3(-W * 0.5f, 0f, -D * 0.5f + t * 0.5f));
        Matrix4x4 ml = Matrix4x4.TRS(new Vector3(-W * 0.5f + t * 0.5f, 0f, -D * 0.5f + t), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
        Matrix4x4 mr = Matrix4x4.TRS(new Vector3(W * 0.5f - t * 0.5f, 0f, -D * 0.5f + t), Quaternion.Euler(0f, -90f, 0f), Vector3.one);
        // la casa de la salida conserva el muro de la fachada por encima de la puerta (el dintel de piedra)
        dm.Wall(mf, W, t, y0, WallTop(r, W, Hw, ruin, sd + 1, h.exit ? Hw - 0.05f : 0f), front, baseH, subBase, subBody);
        dm.Wall(mb, W, t, y0, WallTop(r, W, Hw, ruin, sd + 2), back, baseH, subBase, subBody);
        dm.Wall(ml, sideLen, t, y0, WallTop(r, sideLen, Hw, ruin, sd + 3), left, baseH, subBase, subBody);
        dm.Wall(mr, sideLen, t, y0, WallTop(r, sideLen, Hw, ruin, sd + 4), right, baseH, subBase, subBody);
        // dinteles de madera sobre las ventanas (y sobre la puerta en las casas normales)
        foreach (var o in front) if (o.y1 < Hw - 0.25f && !(h.exit && o.y0 < 0f)) dm.Box(2, mf, new Vector3((o.x0 + o.x1) * 0.5f, o.y1 + 0.09f, 0f), new Vector3(o.x1 - o.x0 + 0.5f, 0.17f, t + 0.04f));
        foreach (var o in back) dm.Box(2, mb, new Vector3((o.x0 + o.x1) * 0.5f, o.y1 + 0.09f, 0f), new Vector3(o.x1 - o.x0 + 0.5f, 0.17f, t + 0.04f));
        // suelo de tierra apisonada
        float ix = W * 0.5f - t, iz = D * 0.5f - t;
        dm.QuadO(4, Vector3.up, new Vector3(-ix - 0.05f, 0.02f, -iz - 0.05f), new Vector3(ix + 0.05f, 0.02f, -iz - 0.05f), new Vector3(ix + 0.05f, 0.02f, iz + 0.05f), new Vector3(-ix - 0.05f, 0.02f, iz + 0.05f),
            new Vector2(-ix, -iz), new Vector2(ix, -iz), new Vector2(ix, iz), new Vector2(-ix, iz));
        // techo de vigas (rollizos que asoman por fuera) + tablas + barro. Ruina 1: hundido en parte; ruina 2: sin techo
        bool alongZ = W >= D;   // las vigas cruzan el lado corto
        float span = alongZ ? D : W, run = alongZ ? W : D, beamY = Hw - 0.34f;
        float roofFrac = ruin == 0 ? 1f : ruin == 1 ? 0.35f + (float)r.NextDouble() * 0.3f : 0f;
        int nBeams = Mathf.FloorToInt((run - 2f * t) / 0.62f);
        for (int k = 0; k < nBeams; k++)
        {
            float u = -run * 0.5f + t + 0.31f + k * 0.62f;
            float frac = (u + run * 0.5f) / run;
            bool keep = frac <= roofFrac + 0.02f || (ruin == 1 && r.NextDouble() < 0.25);
            if (!keep) continue;
            float over = 0.25f + (float)r.NextDouble() * 0.25f;
            Vector3 a = alongZ ? new Vector3(u, beamY, -span * 0.5f - over) : new Vector3(-span * 0.5f - over, beamY, u);
            Vector3 b = alongZ ? new Vector3(u, beamY, span * 0.5f + over) : new Vector3(span * 0.5f + over, beamY, u);
            dm.Log(2, Matrix4x4.identity, a + Vector3.up * ((float)r.NextDouble() - 0.5f) * 0.03f, b, 0.085f + (float)r.NextDouble() * 0.025f);
        }
        if (roofFrac > 0f)
        {
            float rl = (run - 2f * t) * roofFrac + 0.06f, rc = -run * 0.5f + t + rl * 0.5f - 0.03f;
            Vector3 c1 = alongZ ? new Vector3(rc, beamY + 0.11f, 0f) : new Vector3(0f, beamY + 0.11f, rc);
            Vector3 s1 = alongZ ? new Vector3(rl, 0.04f, span - 2f * t + 0.1f) : new Vector3(span - 2f * t + 0.1f, 0.04f, rl);
            dm.Box(2, Matrix4x4.identity, c1, s1);
            dm.Box(3, Matrix4x4.identity, c1 + Vector3.up * 0.12f, new Vector3(s1.x, 0.2f, s1.z));
        }
        // vigas caídas y montones de escombros (adobes deshechos) donde se hundió el techo o los muros
        if (ruin > 0)
        {
            int nf = ruin == 1 ? 2 + r.Next(2) : 1 + r.Next(3);
            for (int k = 0; k < nf; k++)
            {
                float u = ((float)r.NextDouble() * 2f - 1f) * (alongZ ? ix : iz) * 0.7f;
                Vector3 top = alongZ ? new Vector3(u, beamY - 0.1f - (float)r.NextDouble() * (ruin == 2 ? 1.6f : 0.4f), (r.NextDouble() < 0.5 ? -1f : 1f) * (iz + 0.1f)) : new Vector3((r.NextDouble() < 0.5 ? -1f : 1f) * (ix + 0.1f), beamY - 0.1f, u);
                top.y = Mathf.Max(0.5f, Mathf.Min(top.y, 1.4f + (float)r.NextDouble()));
                Vector3 bot = new Vector3(top.x * 0.2f + ((float)r.NextDouble() - 0.5f) * 1.2f, 0.08f, top.z * -0.15f + ((float)r.NextDouble() - 0.5f) * 1.0f);
                bot.x = Mathf.Clamp(bot.x, -ix + 0.3f, ix - 0.3f); bot.z = Mathf.Clamp(bot.z, -iz + 0.3f, iz - 0.3f);
                dm.Log(2, Matrix4x4.identity, bot, top, 0.08f);
            }
            int nm = ruin == 1 ? 1 + r.Next(2) : 2 + r.Next(2);
            for (int k = 0; k < nm; k++)
            {
                Vector3 c = new Vector3(((float)r.NextDouble() * 2f - 1f) * (ix - 0.6f), 0f, ((float)r.NextDouble() * 2f - 1f) * (iz - 0.6f));
                if (ruin == 2 && r.NextDouble() < 0.5) { c.x = (r.NextDouble() < 0.5 ? -1f : 1f) * (ix + t * 0.5f + 0.5f); }   // fuera, al pie del muro caído
                if (Mathf.Abs(c.x - (doorX - W * 0.5f)) < 1.1f && c.z > iz - 1.5f) c.z -= 1.6f;          // no tapar la puerta
                dm.Mound(6, Matrix4x4.TRS(c, Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f), Vector3.one), 0.8f + (float)r.NextDouble() * 0.7f, 0.6f + (float)r.NextDouble() * 0.5f, 0.22f + (float)r.NextDouble() * 0.28f, r);
                // adobes sueltos encima y alrededor
                for (int q = 0; q < 5; q++)
                {
                    Vector3 bp = c + new Vector3(((float)r.NextDouble() - 0.5f) * 1.6f, 0.06f + (float)r.NextDouble() * 0.12f, ((float)r.NextDouble() - 0.5f) * 1.4f);
                    dm.Box(stone ? 0 : 1, Matrix4x4.TRS(bp, Quaternion.Euler(((float)r.NextDouble() - 0.5f) * 30f, (float)r.NextDouble() * 360f, ((float)r.NextDouble() - 0.5f) * 30f), Vector3.one), Vector3.zero, new Vector3(0.36f, 0.11f, 0.18f) * (0.7f + (float)r.NextDouble() * 0.5f));
                }
            }
        }
        // arena metida por la puerta y en las esquinas
        dm.Mound(5, Matrix4x4.TRS(new Vector3(doorX - W * 0.5f, 0f, iz - 0.4f), Quaternion.identity, Vector3.one), 0.9f, 0.7f, 0.12f + 0.05f * ruin, r, 0.05f);
        for (int k = 0; k < 1 + ruin; k++)
        {
            Vector3 c = new Vector3((r.NextDouble() < 0.5 ? -1f : 1f) * (ix - 0.4f), 0f, (r.NextDouble() < 0.5 ? -1f : 1f) * (iz - 0.4f));
            dm.Mound(5, Matrix4x4.TRS(c, Quaternion.identity, Vector3.one), 0.9f + 0.3f * ruin, 0.8f, 0.18f + 0.1f * ruin, r, 0.05f);
        }
        // por fuera: arena acumulada contra los muros (el viento la amontona), sobre todo a sotavento
        for (int k = 0; k < 3; k++)
        {
            int side = r.Next(4); Vector3 c;
            if (side == 0) c = new Vector3(((float)r.NextDouble() - 0.5f) * W, 0f, -D * 0.5f - 0.3f);
            else if (side == 1) c = new Vector3(-W * 0.5f - 0.3f, 0f, ((float)r.NextDouble() - 0.5f) * D);
            else if (side == 2) c = new Vector3(W * 0.5f + 0.3f, 0f, ((float)r.NextDouble() - 0.5f) * D);
            else { c = new Vector3(((float)r.NextDouble() - 0.5f) * W, 0f, D * 0.5f + 0.3f); if (Mathf.Abs(c.x - (doorX - W * 0.5f)) < 1.4f) continue; }
            dm.Mound(5, Matrix4x4.TRS(c, Quaternion.Euler(0f, side == 1 || side == 2 ? 90f : 0f, 0f), Vector3.one), 1.6f + (float)r.NextDouble(), 0.7f, 0.3f + (float)r.NextDouble() * 0.35f, r, 0.2f);
        }
        Mesh m = dm.Build(go.name); track(m);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer rend = go.AddComponent<MeshRenderer>();
        rend.sharedMaterials = new[] { stoneM, stone ? stoneM : (r.NextDouble() < 0.6 ? plasterM : clayM), woodM, mudM, floorM, sandM, rubbleM };
        rend.shadowCastingMode = ShadowCastingMode.On; rend.receiveShadows = true;
        go.AddComponent<MeshCollider>().sharedMesh = m;
        HouseBoxes.Add(new HouseBox { c = h.pos, yaw = h.yaw, half = new Vector2(W * 0.5f, D * 0.5f), roofed = ruin == 0 });
        houseCount++;
        // puerta: la de madera (abierta, colgando o tirada) o la de PIEDRA de la salida
        Vector3 dc = new Vector3(doorX - W * 0.5f, 0f, D * 0.5f - t * 0.5f);
        if (h.exit) BuildStoneDoor(ht, dc, r);
        else if (ruin < 2 || r.NextDouble() < 0.4) WoodDoor(ht, dc, r, ruin);
        if (ruin < 2) Furnish(ht, h, hi, W, D, doorX - W * 0.5f, r);
    }

    private void WoodDoor(Transform ht, Vector3 dc, System.Random r, int ruin)
    {
        DesertMesh dm = new DesertMesh(1);
        double k = r.NextDouble();
        Matrix4x4 tr;
        if (k < 0.45 && ruin == 0)
        {
            // colgando de una bisagra, entreabierta hacia dentro
            float ang = 70f + (float)r.NextDouble() * 35f;
            tr = Matrix4x4.TRS(dc + new Vector3(-DoorW * 0.5f + 0.04f, 0.03f, -0.12f), Quaternion.Euler(0f, ang, (float)r.NextDouble() * 2f), Vector3.one) * Matrix4x4.Translate(new Vector3(DoorW * 0.5f - 0.04f, 0f, 0f));
        }
        else
        {
            // arrancada: tirada en el suelo delante de la casa, medio cubierta de arena
            tr = Matrix4x4.TRS(dc + new Vector3(((float)r.NextDouble() - 0.5f) * 1.5f, 0.03f, 1.4f + (float)r.NextDouble() * 0.8f), Quaternion.Euler(-90f + ((float)r.NextDouble() - 0.5f) * 6f, (float)r.NextDouble() * 360f, 0f), Vector3.one) * Matrix4x4.Translate(new Vector3(0f, -1.05f, 0f));
        }
        // hoja de tablas verticales con travesaños (algunas tablas rotas o que faltan)
        int planks = 6; float pw = (DoorW - 0.08f) / planks;
        for (int p = 0; p < planks; p++)
        {
            if (r.NextDouble() < 0.12) continue;
            float len = r.NextDouble() < 0.2 ? 1.1f + (float)r.NextDouble() * 0.8f : 2.12f;
            dm.Box(0, tr, new Vector3(-DoorW * 0.5f + 0.04f + pw * (p + 0.5f), len * 0.5f, 0f), new Vector3(pw - 0.008f, len, 0.035f));
        }
        dm.Box(0, tr, new Vector3(0f, 0.45f, -0.03f), new Vector3(DoorW - 0.1f, 0.12f, 0.03f));
        dm.Box(0, tr, new Vector3(0f, 1.65f, -0.03f), new Vector3(DoorW - 0.1f, 0.12f, 0.03f));
        Mesh m = dm.Build("WoodDoor"); track(m);
        GameObject g = new GameObject("WoodDoor"); g.transform.SetParent(ht, false);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = woodM; mr.shadowCastingMode = ShadowCastingMode.On;
    }

    // la salida: la puerta de madera de esta casa la sustituyó una losa de piedra tallada, encajada en un marco de sillares
    private void BuildStoneDoor(Transform ht, Vector3 dc, System.Random r)
    {
        DesertMesh dm = new DesertMesh(2);
        Matrix4x4 I = Matrix4x4.identity;
        float t = WallT;
        // marco: jambas y dintel de sillares que sobresalen del adobe
        dm.Box(0, I, dc + new Vector3(-DoorW * 0.5f - 0.17f, (DoorH + 0.4f) * 0.5f - 0.2f, 0.02f), new Vector3(0.34f, DoorH + 0.4f, t + 0.1f));
        dm.Box(0, I, dc + new Vector3(DoorW * 0.5f + 0.17f, (DoorH + 0.4f) * 0.5f - 0.2f, 0.02f), new Vector3(0.34f, DoorH + 0.4f, t + 0.1f));
        dm.Box(0, I, dc + new Vector3(0f, DoorH + 0.2f, 0.02f), new Vector3(DoorW + 0.9f, 0.4f, t + 0.12f));
        dm.Box(0, I, dc + new Vector3(0f, 0.03f, 0.25f), new Vector3(DoorW + 0.9f, 0.1f, 0.5f));   // umbral
        Mesh fm = dm.Build("StoneFrame"); track(fm);
        GameObject frame = new GameObject("StoneFrame"); frame.transform.SetParent(ht, false);
        frame.AddComponent<MeshFilter>().sharedMesh = fm;
        MeshRenderer fr = frame.AddComponent<MeshRenderer>(); fr.sharedMaterial = blocksM; fr.shadowCastingMode = ShadowCastingMode.On;
        frame.AddComponent<MeshCollider>().sharedMesh = fm;
        // la losa (con un marco tallado en relieve y una argolla de hierro)
        DesertMesh sm = new DesertMesh(2);
        sm.Box(0, I, Vector3.zero, new Vector3(DoorW - 0.02f, DoorH - 0.02f, 0.2f));
        sm.Box(0, I, new Vector3(0f, 0.62f, 0.11f), new Vector3(DoorW - 0.24f, 0.07f, 0.03f));
        sm.Box(0, I, new Vector3(0f, -0.72f, 0.11f), new Vector3(DoorW - 0.24f, 0.07f, 0.03f));
        sm.Box(0, I, new Vector3(-DoorW * 0.5f + 0.15f, -0.05f, 0.11f), new Vector3(0.07f, 1.41f, 0.03f));
        sm.Box(0, I, new Vector3(DoorW * 0.5f - 0.15f, -0.05f, 0.11f), new Vector3(0.07f, 1.41f, 0.03f));
        for (int k = 0; k < 10; k++)
        {
            float a0 = k * Mathf.PI * 2f / 10f, a1 = (k + 1) * Mathf.PI * 2f / 10f;
            sm.Log(1, I, new Vector3(0.3f + Mathf.Cos(a0) * 0.07f, -0.05f + Mathf.Sin(a0) * 0.07f, 0.13f), new Vector3(0.3f + Mathf.Cos(a1) * 0.07f, -0.05f + Mathf.Sin(a1) * 0.07f, 0.13f), 0.012f, 6);
        }
        Mesh smesh = sm.Build("StoneDoor"); track(smesh);
        GameObject slab = new GameObject("StoneDoor"); slab.transform.SetParent(ht, false);
        slab.transform.localPosition = dc + new Vector3(0f, DoorH * 0.5f, 0.04f);
        slab.AddComponent<MeshFilter>().sharedMesh = smesh;
        MeshRenderer srd = slab.AddComponent<MeshRenderer>(); srd.sharedMaterials = new[] { sandstoneM, ironM }; srd.shadowCastingMode = ShadowCastingMode.On;
        slab.AddComponent<BoxCollider>().size = new Vector3(DoorW, DoorH, 0.22f);
        ExitTransform = slab.transform;
        Vector3 front = ht.localRotation * (dc + new Vector3(0f, 0f, 1.0f)) + ht.localPosition;
        ExitLocal = front;
        // la puerta de madera que había, apoyada contra la fachada
        DesertMesh wd = new DesertMesh(1);
        Matrix4x4 tr = Matrix4x4.TRS(dc + new Vector3(DoorW + 0.55f, 0f, 0.55f), Quaternion.Euler(-12f, 8f, 0f), Vector3.one);
        for (int p = 0; p < 6; p++) wd.Box(0, tr, new Vector3(-0.5f + 0.17f * p + 0.08f, p == 4 ? 0.7f : 1.06f, 0f), new Vector3(0.16f, p == 4 ? 1.4f : 2.12f, 0.035f));
        wd.Box(0, tr, new Vector3(0f, 0.45f, -0.03f), new Vector3(1.0f, 0.12f, 0.03f));
        wd.Box(0, tr, new Vector3(0f, 1.65f, -0.03f), new Vector3(1.0f, 0.12f, 0.03f));
        Mesh wm = wd.Build("OldDoor"); track(wm);
        GameObject od = new GameObject("OldWoodDoor"); od.transform.SetParent(ht, false);
        od.AddComponent<MeshFilter>().sharedMesh = wm;
        MeshRenderer omr = od.AddComponent<MeshRenderer>(); omr.sharedMaterial = woodM; omr.shadowCastingMode = ShadowCastingMode.On;
    }

    // muebles rotos (modelos escaneados CC0) y huecos para los objetos
    private void Furnish(Transform ht, DesertPlan.House h, int hi, float W, float D, float doorLX, System.Random r)
    {
        float ix = W * 0.5f - WallT - 0.08f, iz = D * 0.5f - WallT - 0.08f;
        Color tint = new Color(0.86f, 0.80f, 0.72f);
        Transform fr = new GameObject("Furniture").transform; fr.SetParent(ht, false);
        System.Action<Vector3, int> slot = (lp, kind) =>
        {
            Vector3 w = ht.localPosition + ht.localRotation * lp + Vector3.up * 0.1f;
            Slots.Add(new Slot { pos = w, house = hi, kind = kind });
        };
        float sc = W < 5f ? 0.75f : 0.9f;
        // mesa contra el fondo (rota: volcada sobre un lado en las casas más hundidas) con sillas
        bool tableUp = h.ruin == 0 || r.NextDouble() < 0.5;
        Vector3 tp = new Vector3(((float)r.NextDouble() - 0.5f) * (ix - 1.2f), 0.02f, -iz + 0.75f);
        if (tableUp)
        {
            Model(h.ruin == 0 ? "FTable0" : "FTable0_broken", fr, tp, Quaternion.Euler(0f, ((float)r.NextDouble() - 0.5f) * 8f, 0f), sc, tint, true, false, true);
            slot(tp + new Vector3(-0.35f * sc, 0.96f * sc, 0f), DesertSlotTable); slot(tp + new Vector3(0.45f * sc, 0.96f * sc, 0.05f), DesertSlotTable);
            Model("FChair0", fr, tp + new Vector3(-0.5f, 0f, 0.65f), Quaternion.Euler(0f, 180f + ((float)r.NextDouble() - 0.5f) * 30f, 0f), 1f, tint);
        }
        else
        {
            Model("FTable0_broken", fr, tp + new Vector3(0f, 0.45f * sc, 0.1f), Quaternion.Euler(-88f, (float)r.NextDouble() * 20f - 10f, 0f), sc, tint, true, false, true);
            slot(tp + new Vector3(0.6f, 0f, 0.9f), DesertSlotFloor);
        }
        // silla volcada (tumbada en el suelo)
        Model(r.NextDouble() < 0.5 ? "FChair0_broken" : "FChair1_broken", fr, new Vector3(((float)r.NextDouble() - 0.5f) * ix, 0.22f, ((float)r.NextDouble() - 0.3f) * iz), Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 90f), 1f, tint);
        // contra los muros laterales: cama rota, estantería, aparador
        float sideX = r.NextDouble() < 0.5 ? -1f : 1f;
        double k = r.NextDouble();
        if (k < 0.4 && D > 4.6f) Model("FBed_broken", fr, new Vector3(sideX * (ix - 0.5f), 0.02f, 0.1f), Quaternion.Euler(0f, ((float)r.NextDouble() - 0.5f) * 10f, 0f), 1f, tint);
        else if (k < 0.75)
        {
            Vector3 sp = new Vector3(sideX * (ix - 0.32f), 0.02f, ((float)r.NextDouble() - 0.5f) * (iz - 1f));
            Model("FShelf_broken", fr, sp, Quaternion.Euler(0f, sideX > 0 ? -90f : 90f, 0f), 0.95f, tint, true, false, true);
            slot(sp + new Vector3(-sideX * 0.05f, 0.42f, 0f), DesertSlotShelf); slot(sp + new Vector3(-sideX * 0.05f, 1.12f, 0f), DesertSlotShelf);
        }
        else
        {
            Vector3 cp = new Vector3(sideX * (ix - 0.35f), 0.02f, ((float)r.NextDouble() - 0.5f) * (iz - 1f));
            Model("FCabinet_broken", fr, cp, Quaternion.Euler(0f, sideX > 0 ? -90f : 90f, 0f), 1f, tint, true, false, true);
            slot(cp + new Vector3(-sideX * 0.05f, 1.18f, 0f), DesertSlotTable);
        }
        // cosas sueltas: cántaros, cubos, ollas, cajas rotas, escalera
        string[] small = { "FJug", "FPot", "FBucket", "FCrate_broken", "FStool1_broken", "FStool0", "FLadder_broken" };
        int ns = 2 + r.Next(3);
        for (int i = 0; i < ns; i++)
        {
            string nm = small[r.Next(small.Length)];
            Vector3 p = new Vector3(((float)r.NextDouble() * 2f - 1f) * (ix - 0.3f), 0.02f, ((float)r.NextDouble() * 2f - 1f) * (iz - 0.3f));
            if (Mathf.Abs(p.x - doorLX) < 0.9f && p.z > iz - 1.3f) continue;      // la entrada, libre
            Quaternion q = Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f);
            if (nm == "FLadder_broken") { p.x = (r.NextDouble() < 0.5 ? -1f : 1f) * (ix - 0.15f); q = Quaternion.Euler(0f, p.x > 0 ? -90f : 90f, 0f) * Quaternion.Euler(-14f, 0f, 0f); }
            else if ((nm == "FJug" || nm == "FBucket" || nm == "FPot") && r.NextDouble() < 0.4) { q = q * Quaternion.Euler(0f, 0f, 90f); p.y += nm == "FPot" ? 0.25f : 0.15f; }
            Model(nm, fr, p, q, 1f, tint);
        }
        // huecos en el suelo (rincones)
        slot(new Vector3(-ix + 0.45f, 0f, -iz + 0.45f), DesertSlotFloor);
        slot(new Vector3(ix - 0.45f, 0f, ((float)r.NextDouble() - 0.5f) * iz), DesertSlotFloor);
        furnitureCount++;
    }
    public const int DesertSlotTable = 0, DesertSlotShelf = 1, DesertSlotFloor = 2;

    // pozo de piedra: brocal, dos postes, travesaño con polea, cuerda y cubo; el agua al fondo, a ~6 m
    private void BuildWell(Transform vroot, Vector2 p, System.Random r)
    {
        float hy = DesertHeight.H(p.x, p.y);
        GameObject go = new GameObject("Well"); go.transform.SetParent(vroot, false);
        go.transform.localPosition = new Vector3(p.x, hy, p.y);
        go.transform.localRotation = Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f);
        DesertMesh dm = new DesertMesh(4);   // 0 piedra, 1 pozo (interior), 2 madera, 3 agua
        const int S = 28; float ro = 0.98f, ri = 0.70f, top = 0.82f;
        for (int k = 0; k < S; k++)
        {
            float a0 = k * Mathf.PI * 2f / S, a1 = (k + 1) * Mathf.PI * 2f / S;
            float wob0 = 1f + (VNoise(k * 0.9f, 3) - 0.5f) * 0.05f, wob1 = 1f + (VNoise((k + 1) * 0.9f, 3) - 0.5f) * 0.05f;
            Vector3 d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
            float u0 = a0 * ro, u1 = a1 * ro;
            dm.QuadO(0, (d0 + d1), d0 * ro * wob0 + Vector3.down * 0.3f, d1 * ro * wob1 + Vector3.down * 0.3f, d1 * ro * wob1 + Vector3.up * top, d0 * ro * wob0 + Vector3.up * top,
                new Vector2(u0, -0.3f), new Vector2(u1, -0.3f), new Vector2(u1, top), new Vector2(u0, top));
            dm.QuadO(0, -(d0 + d1), d0 * ri + Vector3.up * top, d1 * ri + Vector3.up * top, d1 * ri + Vector3.down * 0.6f, d0 * ri + Vector3.down * 0.6f,
                new Vector2(u0, top), new Vector2(u1, top), new Vector2(u1, -0.6f), new Vector2(u0, -0.6f));
            dm.QuadO(1, -(d0 + d1), d0 * ri + Vector3.down * 0.6f, d1 * ri + Vector3.down * 0.6f, d1 * ri + Vector3.down * 6.2f, d0 * ri + Vector3.down * 6.2f,
                new Vector2(u0, -0.6f), new Vector2(u1, -0.6f), new Vector2(u1, -6.2f), new Vector2(u0, -6.2f));
            // corona con losas (algunas movidas)
            float lift = Hash(k, 17) < 0.2f ? 0.03f : 0f;
            dm.QuadO(0, Vector3.up, d0 * (ri - 0.03f) + Vector3.up * (top + lift), d1 * (ri - 0.03f) + Vector3.up * (top + lift), d1 * (ro + 0.04f) * wob1 + Vector3.up * (top + lift), d0 * (ro + 0.04f) * wob0 + Vector3.up * (top + lift),
                new Vector2(u0, 0f), new Vector2(u1, 0f), new Vector2(u1, 0.3f), new Vector2(u0, 0.3f));
        }
        dm.QuadO(3, Vector3.up, new Vector3(-ri, -5.6f, -ri), new Vector3(ri, -5.6f, -ri), new Vector3(ri, -5.6f, ri), new Vector3(-ri, -5.6f, ri), Vector2.zero, Vector2.right, Vector2.one, Vector2.up);
        // postes, travesaño y polea
        Matrix4x4 I = Matrix4x4.identity;
        dm.Log(2, I, new Vector3(-1.12f, -0.3f, 0f), new Vector3(-1.1f, 2.25f, 0.02f), 0.075f);
        dm.Log(2, I, new Vector3(1.12f, -0.3f, 0f), new Vector3(1.1f, 2.25f, -0.02f), 0.075f);
        dm.Log(2, I, new Vector3(-1.32f, 2.12f, 0f), new Vector3(1.32f, 2.14f, 0f), 0.065f);
        dm.Log(2, I, new Vector3(0f, 2.13f, -0.06f), new Vector3(0f, 2.13f, 0.06f), 0.14f, 12);
        Mesh m = dm.Build("Well"); track(m);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        Material water = ProceduralAssets.CreateMaterial(new Color(0.02f, 0.03f, 0.03f), null, Vector2.one, 0.96f); track(water);
        Material shaft = Pbr("stone2", new Color(0.32f, 0.27f, 0.23f), 2.0f, 0.5f);
        mr.sharedMaterials = new[] { stone2M, shaft, woodM, water };
        mr.shadowCastingMode = ShadowCastingMode.On;
        // colisión: el brocal como cilindro macizo (no te caes dentro) y los postes
        CapsuleCollider cc = go.AddComponent<CapsuleCollider>(); cc.radius = ro + 0.02f; cc.height = 3.2f; cc.center = new Vector3(0f, top - 1.6f + 0.02f, 0f);
        BoxCollider bl = go.AddComponent<BoxCollider>(); bl.center = new Vector3(-1.11f, 1f, 0f); bl.size = new Vector3(0.16f, 2.6f, 0.16f);
        BoxCollider br = go.AddComponent<BoxCollider>(); br.center = new Vector3(1.11f, 1f, 0f); br.size = new Vector3(0.16f, 2.6f, 0.16f);
        // cuerda y cubo colgando, otro cubo en el brocal
        DesertMesh rope = new DesertMesh(1); rope.Log(0, I, new Vector3(0.02f, 2.0f, 0.13f), new Vector3(0.02f, 1.05f, 0.13f), 0.011f, 5);
        Mesh rm = rope.Build("Rope"); track(rm);
        GameObject rg = new GameObject("Rope"); rg.transform.SetParent(go.transform, false); rg.AddComponent<MeshFilter>().sharedMesh = rm;
        Material ropeM = ProceduralAssets.CreateMaterial(new Color(0.42f, 0.34f, 0.24f), null, Vector2.one, 0.1f); track(ropeM);
        rg.AddComponent<MeshRenderer>().sharedMaterial = ropeM;
        Model("FBucket", go.transform, new Vector3(0.02f, 0.5f, 0.13f), Quaternion.Euler(0f, 30f, 0f), 1f, new Color(0.85f, 0.8f, 0.72f));
        Model("FBucket", go.transform, new Vector3(-0.15f, top + 0.01f, ro - 0.12f), Quaternion.Euler(0f, 200f, 0f), 0.95f, new Color(0.85f, 0.8f, 0.72f));
        wellCount++;
    }

    // torre de vigía en ruinas junto a la casa de la salida: se ve de lejos entre las dunas (la aldea de la salida)
    private void BuildTower(Transform vroot, DesertPlan.Village v, System.Random r)
    {
        // a un lado de la plaza, sin pisar casas
        Vector2 p = v.c; bool ok = false;
        for (int k = 0; k < 24 && !ok; k++)
        {
            float a = (float)r.NextDouble() * Mathf.PI * 2f, d = v.r * 0.3f + (float)r.NextDouble() * v.r * 0.5f;
            p = Mod(v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d);
            ok = FreeFromHouses(p, 2.6f) && (!v.hasWell || plan.Dist(p, v.well) > 4.5f);
        }
        if (!ok) return;
        float hy = DesertHeight.H(p.x, p.y), S = 3.4f, t = 0.55f, Ht = 8.5f + (float)r.NextDouble() * 1.5f;
        GameObject go = new GameObject("Tower"); go.transform.SetParent(vroot, false);
        go.transform.localPosition = new Vector3(p.x, hy, p.y); go.transform.localRotation = Quaternion.Euler(0f, (float)r.NextDouble() * 90f, 0f);
        DesertMesh dm = new DesertMesh(2);
        int sd = r.Next(9999);
        for (int w = 0; w < 4; w++)
        {
            Matrix4x4 tr = Matrix4x4.TRS(Quaternion.Euler(0f, w * 90f, 0f) * new Vector3(-S * 0.5f, 0f, S * 0.5f - t * 0.5f), Quaternion.Euler(0f, w * 90f, 0f), Vector3.one);
            var ops = new List<DesertMesh.Opening>();
            if (w == 0) ops.Add(new DesertMesh.Opening { x0 = S * 0.5f - 0.55f, x1 = S * 0.5f + 0.55f, y0 = -1f, y1 = 2.2f });
            ops.Add(new DesertMesh.Opening { x0 = S * 0.5f - 0.25f, x1 = S * 0.5f + 0.25f, y0 = 4.2f + w * 0.3f, y1 = 5.1f + w * 0.3f });
            ops.Add(new DesertMesh.Opening { x0 = S * 0.5f - 0.25f, x1 = S * 0.5f + 0.25f, y0 = 7f, y1 = 7.8f });
            int ww = w;
            System.Func<float, float> top = x => Ht - (ww == 1 ? 2.2f * Mathf.Clamp01(x / S) : 0f) + (VNoise(x * 3f, sd + ww) - 0.5f) * 0.6f;
            dm.Wall(tr, S, t, -0.5f, top, ops, 0.6f, 0, 0, 0.2f);
        }
        dm.Box(1, Matrix4x4.identity, new Vector3(0f, 2.2f + 0.1f, S * 0.5f - t * 0.5f), new Vector3(1.6f, 0.2f, t + 0.05f));
        Mesh m = dm.Build("Tower"); track(m);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { stoneM, woodM }; mr.shadowCastingMode = ShadowCastingMode.On;
        go.AddComponent<MeshCollider>().sharedMesh = m;
        TowerLocal = go.transform.localPosition;
    }

    // ------------------------------------------------------------------ rocas, cuevas y vetas
    private static readonly string[] BigRock = { "DCliff0", "DCliff1", "DRock1", "DRock2" };
    private void BuildOutcrops()
    {
        Transform orot = new GameObject("Outcrops").transform; orot.SetParent(transform, false);
        Color tint = new Color(0.98f, 0.80f, 0.62f);
        for (int i = 0; i < plan.outcrops.Count; i++)
        {
            DesertPlan.Outcrop o = plan.outcrops[i];
            System.Random r = new System.Random(gen.UsedSeed * 977 + i * 31 + 3);
            Transform g = new GameObject("Outcrop" + i).transform; g.SetParent(orot, false);
            if (o.cave) { BuildCave(g, o, r, tint); continue; }
            string main = BigRock[o.kind];
            float sc = (o.kind < 2 ? 0.7f : 1.4f) * o.scale * (0.9f + (float)r.NextDouble() * 0.4f);
            Vector3 p = Ground(o.c, -0.35f * sc);
            Model(main, g, p, Quaternion.Euler(((float)r.NextDouble() - 0.5f) * 8f, o.yaw, ((float)r.NextDouble() - 0.5f) * 8f), sc, tint, true, false, true);
            rockCount++;
            int sat = 2 + r.Next(4);
            for (int k = 0; k < sat; k++)
            {
                float a = (float)r.NextDouble() * Mathf.PI * 2f, d = (o.kind < 2 ? 5f : 2.5f) * sc * (0.6f + (float)r.NextDouble() * 0.6f);
                Vector2 q = Mod(o.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d);
                if (plan.QuickAt(q, 2f) >= 0) continue;
                float s2 = 0.5f + (float)r.NextDouble() * 1.2f;
                Model("DRock" + r.Next(4), g, Ground(q, -0.2f * s2), Quaternion.Euler(((float)r.NextDouble() - 0.5f) * 20f, (float)r.NextDouble() * 360f, ((float)r.NextDouble() - 0.5f) * 20f), s2, tint, true, false, s2 > 0.8f);
                rockCount++;
            }
            for (int k = 0; k < 3; k++)
            {
                float a = (float)r.NextDouble() * Mathf.PI * 2f, d = 2f + (float)r.NextDouble() * 6f * sc;
                Vector2 q = Mod(o.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d);
                Model(k == 0 ? "DPebbles" : k == 1 ? "DStones" : "DRocks", g, Ground(q, -0.03f), Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f), 0.8f + (float)r.NextDouble() * 0.6f, tint, false);
            }
        }
        // piedras sueltas por todo el desierto (sin colisión): solo en llano
        System.Random rs = new System.Random(gen.UsedSeed * 59 + 17);
        for (int k = 0, guard = 0; k < 70 && guard < 700; guard++)
        {
            Vector2 q = new Vector2((float)rs.NextDouble() * L, (float)rs.NextDouble() * L);
            if (!plan.FreeOf(q, 2f, 3f) || DesertHeight.Normal(q.x, q.y).y < 0.97f) continue;
            double kr = rs.NextDouble();
            Model(kr < 0.45 ? "DStones" : kr < 0.8 ? "DRocks" : "DPebbles", orot, Ground(q, -0.03f), Quaternion.Euler(0f, (float)rs.NextDouble() * 360f, 0f), 0.8f + (float)rs.NextDouble() * 0.7f, tint, false);
            k++;
        }
        GhostTree(orot);
    }

    // cueva: bóveda de roca (malla con ruido, vista por dentro y por fuera, con la boca abierta hacia 'yaw'), rocas grandes
    // por encima y vetas de hierro, oro, plomo y diamante en las paredes de dentro
    private void BuildCave(Transform g, DesertPlan.Outcrop o, System.Random r, Color tint)
    {
        float R = 6.5f + (float)r.NextDouble() * 1.5f, Hc = 3.8f + (float)r.NextDouble() * 0.6f;
        float baseY = DesertHeight.H(o.c.x, o.c.y);
        g.localPosition = new Vector3(o.c.x, 0f, o.c.y); g.localRotation = Quaternion.Euler(0f, o.yaw, 0f);
        const int RINGS = 12, SEG = 36; const float OPEN = 26f, OPEN_E = 0.62f;   // boca: ±26°, hasta ~62% de la altura
        DesertMesh dm = new DesertMesh(1);
        Vector3[,] pin = new Vector3[RINGS + 1, SEG + 1], pout = new Vector3[RINGS + 1, SEG + 1];
        int sd = r.Next(9999);
        for (int i = 0; i <= RINGS; i++)
            for (int k = 0; k <= SEG; k++)
            {
                float e = i / (float)RINGS * Mathf.PI * 0.5f, a = k / (float)SEG * Mathf.PI * 2f;
                float n = 1f + (Mathf.PerlinNoise(sd + k * 0.33f, i * 0.33f) - 0.5f) * 0.35f + (Mathf.PerlinNoise(sd + 50 + k * 1.1f, i * 1.1f) - 0.5f) * 0.12f;
                if (k == SEG) n = 1f + (Mathf.PerlinNoise(sd, i * 0.33f) - 0.5f) * 0.35f + (Mathf.PerlinNoise(sd + 50, i * 1.1f) - 0.5f) * 0.12f;
                float x = Mathf.Sin(a) * Mathf.Cos(e) * R * n, z = Mathf.Cos(a) * Mathf.Cos(e) * R * n;
                Vector3 wq = g.localRotation * new Vector3(x, 0f, z) + g.localPosition;
                float yb = DesertHeight.H(Mathf.Repeat(wq.x, L), Mathf.Repeat(wq.z, L)) - baseY - 0.4f;
                float y = Mathf.Lerp(yb, Hc * n, Mathf.Sin(e));
                pin[i, k] = new Vector3(x, y + baseY, z);
                float th = 0.9f + 0.6f * (1f - Mathf.Sin(e));
                pout[i, k] = new Vector3(x * (1f + th / R), y + baseY + th * Mathf.Sin(e), z * (1f + th / R));
            }
        System.Func<int, int, bool> open = (i, k) =>
        {
            float a = (k + 0.5f) / SEG * 360f; if (a > 180f) a -= 360f;
            float e = (i + 0.5f) / RINGS;
            return Mathf.Abs(a) < OPEN * (1f - e * 0.6f) && e < OPEN_E;
        };
        for (int i = 0; i < RINGS; i++)
            for (int k = 0; k < SEG; k++)
            {
                if (open(i, k)) continue;
                Vector3 c = (pin[i, k] + pin[i + 1, k + 1]) * 0.5f, cen = new Vector3(0f, baseY + 1f, 0f);
                dm.QuadO(0, cen - c, pin[i, k], pin[i, k + 1], pin[i + 1, k + 1], pin[i + 1, k], new Vector2(k * 1.1f, i), new Vector2((k + 1) * 1.1f, i), new Vector2((k + 1) * 1.1f, i + 1), new Vector2(k * 1.1f, i + 1));
                Vector3 co = (pout[i, k] + pout[i + 1, k + 1]) * 0.5f;
                dm.QuadO(0, co - cen, pout[i, k], pout[i, k + 1], pout[i + 1, k + 1], pout[i + 1, k], new Vector2(k * 1.2f, i), new Vector2((k + 1) * 1.2f, i), new Vector2((k + 1) * 1.2f, i + 1), new Vector2(k * 1.2f, i + 1));
                // bordes de la boca: cara entre la piel de dentro y la de fuera
                if (k + 1 < SEG && open(i, k + 1)) dm.QuadO(0, (pin[i, k + 1] + pin[i + 1, k + 1]) * 0.5f - c, pin[i, k + 1], pout[i, k + 1], pout[i + 1, k + 1], pin[i + 1, k + 1], new Vector2(0, i), new Vector2(1, i), new Vector2(1, i + 1), new Vector2(0, i + 1));
                if (k > 0 && open(i, k - 1)) dm.QuadO(0, (pin[i, k] + pin[i + 1, k]) * 0.5f - c, pin[i, k], pout[i, k], pout[i + 1, k], pin[i + 1, k], new Vector2(0, i), new Vector2(1, i), new Vector2(1, i + 1), new Vector2(0, i + 1));
                if (i > 0 && open(i - 1, k)) dm.QuadO(0, Vector3.down, pin[i, k], pin[i, k + 1], pout[i, k + 1], pout[i, k], new Vector2(k, 0), new Vector2(k + 1, 0), new Vector2(k + 1, 1), new Vector2(k, 1));
            }
        Mesh m = dm.Build("Cave"); track(m);
        GameObject cg = new GameObject("CaveShell"); cg.transform.SetParent(g, false);
        cg.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = cg.AddComponent<MeshRenderer>(); mr.sharedMaterial = caveM; mr.shadowCastingMode = ShadowCastingMode.TwoSided;
        cg.AddComponent<MeshCollider>().sharedMesh = m;
        // rocas grandes encima y a los lados de la boca (que no parezca una cúpula)
        for (int k = 0; k < 5; k++)
        {
            float a = (k == 0 ? 0f : 70f + k * 55f) * Mathf.Deg2Rad + ((float)r.NextDouble() - 0.5f) * 0.3f;
            float d = k == 0 ? R * 0.15f : R * 0.85f;
            Vector3 p = new Vector3(Mathf.Sin(a) * d, baseY + (k == 0 ? Hc * 0.3f : -0.6f), Mathf.Cos(a) * d);   // v7.7.0 a: el risco de arriba hundido en la cúpula (antes asomaba como una aguja)
            Model(k == 0 ? "DCliff0" : "DRock" + r.Next(3), g, p, Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f), k == 0 ? 0.75f : 1.6f + (float)r.NextDouble(), tint, true, false, k != 0);
        }
        // vetas: grupos de cristales/pepitas a lo largo de grietas en las paredes de dentro
        Material[] ores = { ironM, goldM, leadM, diamondM };
        DesertMesh[] vm = { new DesertMesh(1), new DesertMesh(1), new DesertMesh(1), new DesertMesh(1) };
        int nv = 9 + r.Next(5);
        for (int v = 0; v < nv; v++)
        {
            int ore = v < 4 ? v : r.Next(4);
            if (ore == 3 && v >= 4 && r.NextDouble() < 0.6) ore = 0;   // el diamante, escaso
            int i = 1 + r.Next(RINGS / 2), k = r.Next(SEG);
            if (open(i, k) || open(i, (k + 1) % SEG)) { v--; if (r.NextDouble() < 0.1) break; continue; }
            Vector3 p0 = pin[i, k], p1 = pin[i + 1, (k + 1) % (SEG + 1)];
            Vector3 cen = new Vector3(0f, baseY + 1f, 0f);
            int nshard = ore == 3 ? 5 : 9;
            for (int s = 0; s < nshard; s++)
            {
                float u = (float)r.NextDouble();
                Vector3 sp = Vector3.Lerp(p0, p1, u); Vector3 nin = (cen - sp).normalized;
                sp += nin * 0.02f;
                float sz = ore == 3 ? 0.05f + (float)r.NextDouble() * 0.05f : 0.06f + (float)r.NextDouble() * 0.09f;
                Quaternion q = Quaternion.LookRotation(nin) * Quaternion.Euler((float)r.NextDouble() * 60f - 30f, (float)r.NextDouble() * 60f - 30f, (float)r.NextDouble() * 360f);
                vm[ore].Box(0, Matrix4x4.TRS(sp, q, Vector3.one), Vector3.zero, ore == 3 ? new Vector3(sz * 0.6f, sz * 0.6f, sz * 1.6f) : new Vector3(sz, sz * 0.6f, sz * 0.5f));
            }
            veinCount++;
        }
        for (int ore = 0; ore < 4; ore++)
        {
            if (vm[ore].v.Count == 0) continue;
            Mesh om = vm[ore].Build("Vein" + ore); track(om);
            GameObject og = new GameObject("Vein" + ore); og.transform.SetParent(g, false);
            og.AddComponent<MeshFilter>().sharedMesh = om;
            MeshRenderer orr = og.AddComponent<MeshRenderer>(); orr.sharedMaterial = ores[ore]; orr.shadowCastingMode = ShadowCastingMode.Off;
        }
        caveCount++;
    }

    // ------------------------------------------------------------------ plantas secas (pocas, como dice el nivel)
    private void BuildPlants()
    {
        Transform proot = new GameObject("Plants").transform; proot.SetParent(transform, false);
        Color dry = new Color(0.86f, 0.80f, 0.66f), green = new Color(0.78f, 0.80f, 0.62f);
        for (int i = 0; i < plan.plants.Count; i++)
        {
            DesertPlan.Plant p = plan.plants[i];
            Quaternion q = Quaternion.Euler(0f, p.yaw, 0f);
            if (p.kind <= 1) Model("DPlant1", proot, Ground(p.p, -0.04f), q, p.scale, dry, true, true);
            else if (p.kind == 2) Model("DPlant0", proot, Ground(p.p, -0.02f), q, p.scale * 1.1f, dry, true);
            else if ((i & 1) == 0) Model("DPlant3", proot, Ground(p.p, -0.01f), q, p.scale * 2.2f, green, false, true);
            else Model("DPlant2", proot, Ground(p.p, 0.0f), q * Quaternion.Euler(0f, 0f, 80f), p.scale * 1.3f, dry, false);
            plantCount++;
        }
        GhostTree(proot);
    }

    // ------------------------------------------------------------------ puntos de objetos (pilas y extra en las casas, objetos de la arena)
    private void PickSpots(LevelConfig cfg)
    {
        System.Random r = new System.Random(gen.UsedSeed * 53 + 11);
        List<Slot> pool = new List<Slot>(Slots);
        for (int i = pool.Count - 1; i > 0; i--) { int j = r.Next(i + 1); Slot t = pool[i]; pool[i] = pool[j]; pool[j] = t; }
        pool.Sort((a, b) => (a.kind == DesertSlotFloor ? 1 : 0).CompareTo(b.kind == DesertSlotFloor ? 1 : 0));
        List<Slot> order = new List<Slot>(); Dictionary<int, int> taken = new Dictionary<int, int>();
        while (pool.Count > 0)
        {
            int minT = int.MaxValue;
            foreach (Slot s in pool) { taken.TryGetValue(s.house, out int tk); if (tk < minT) minT = tk; }
            for (int i = 0; i < pool.Count; i++) { taken.TryGetValue(pool[i].house, out int tk); if (tk != minT) continue; order.Add(pool[i]); taken[pool[i].house] = tk + 1; pool.RemoveAt(i); break; }
        }
        int k = 0, ne = Mathf.Max(1, cfg.extraSpots), nb = cfg.batteries;
        List<Vector3> sand = new List<Vector3>();
        foreach (Vector2 s in plan.sandItems) sand.Add(Ground(s, 0.1f));
        int si = 0;
        for (int i = 0; i < ne; i++) ExtraSpots.Add(levelRoot.TransformPoint(k < order.Count ? order[k++].pos : sand[si++ % sand.Count]));
        for (int i = 0; i < nb; i++) BatterySpots.Add(levelRoot.TransformPoint(k < order.Count ? order[k++].pos : sand[si++ % sand.Count]));
        foreach (Vector3 s in sand) SandSpots.Add(levelRoot.TransformPoint(s));
    }

    // ¿p (mundo) dentro de una casa con techo? (refugio para la noche y la lluvia)
    public bool UnderRoof(Vector3 world)
    {
        if (levelRoot == null) return false;
        Vector3 lp = levelRoot.InverseTransformPoint(world);
        foreach (HouseBox h in HouseBoxes)
        {
            if (!h.roofed) continue;
            Vector2 d = new Vector2(DesertHeight.Wrap(lp.x - h.c.x), DesertHeight.Wrap(lp.z - h.c.y));
            Vector2 q = Rot(d, -h.yaw);
            if (Mathf.Abs(q.x) < h.half.x && Mathf.Abs(q.y) < h.half.y) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------ copias del borde
    public int GhostTree(Transform root)
    {
        if (root == null || levelRoot == null) return 0;
        int n = 0; Matrix4x4 inv = levelRoot.worldToLocalMatrix;
        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (r.name.EndsWith("_LOD0") || r.name.EndsWith("_LOD2")) continue;
            MeshFilter mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null || !r.enabled) continue;
            Bounds b = r.bounds; Vector3 c = levelRoot.InverseTransformPoint(b.center);
            ghosts.Add(new Ghost { mesh = mf.sharedMesh, local = inv * r.transform.localToWorldMatrix, mats = r.sharedMaterials, center = c, radius = b.extents.magnitude + 1f }); n++;
        }
        return n;
    }
    public int GhostCount { get { return ghosts.Count; } }

    // ------------------------------------------------------------------ ejecución
    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (FieldWrap.OtherRoot == levelRoot) FieldWrap.OtherRoot = null;
        foreach (Object o in owned) ProceduralAssets.SafeDestroy(o);
        FieldHeight.Reset();
        DesertHeight.Pads.Clear();
    }

    private readonly Vector4[] quickV = new Vector4[12];
    private void Update()
    {
        if (levelRoot != null) Shader.SetGlobalVector("_DesertOrigin", levelRoot.position);
        int n = Mathf.Min(12, plan.quick.Count);
        for (int i = 0; i < n; i++) quickV[i] = new Vector4(plan.quick[i].c.x, plan.quick[i].c.y, plan.quick[i].r, 0f);
        Shader.SetGlobalVectorArray("_DesertQuick", quickV);
        Shader.SetGlobalFloat("_DesertQuickN", n);
        Shader.SetGlobalFloat("_DesertTime", Time.time);
    }

    private void LateUpdate()
    {
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
        Camera cam = Camera.main;
        if (cam != null && levelRoot != null)
        {
            Vector3 c = levelRoot.InverseTransformPoint(cam.transform.position);
            float range = GhostRange, nearLod = 95f;
            Matrix4x4 lw = levelRoot.localToWorldMatrix;
            GeometryUtility.CalculateFrustumPlanes(cam, gplanes);
            for (int ox = -1; ox <= 1; ox++)
                for (int oz = -1; oz <= 1; oz++)
                {
                    if (ox == 0 && oz == 0) continue;
                    Vector3 off = new Vector3(ox * L, 0f, oz * L);
                    if (Mathf.Abs(c.x - (L * 0.5f + off.x)) > L * 0.5f + range || Mathf.Abs(c.z - (L * 0.5f + off.z)) > L * 0.5f + range) continue;
                    Matrix4x4 sh = lw * Matrix4x4.Translate(off);
                    for (int i = 0; i < ghosts.Count; i++)
                    {
                        Ghost gh = ghosts[i];
                        Vector3 p = gh.center + off;
                        if (Mathf.Abs(p.x - c.x) > range + gh.radius || Mathf.Abs(p.z - c.z) > range + gh.radius) continue;
                        Vector3 wp = lw.MultiplyPoint3x4(p); bool inView = true;
                        for (int k = 0; k < 6 && inView; k++) if (gplanes[k].GetDistanceToPoint(wp) < -gh.radius) inView = false;
                        if (!inView) continue;
                        Mesh mesh = gh.far != null && new Vector2(p.x - c.x, p.z - c.z).magnitude - gh.radius > nearLod ? gh.far : gh.mesh;
                        Matrix4x4 m = sh * gh.local;
                        for (int s = 0; s < gh.mats.Length && s < mesh.subMeshCount; s++)
                        { Graphics.DrawMesh(mesh, m, gh.mats[s], 0, null, s, null, ShadowCastingMode.Off, true); GhostDraws++; }
                    }
                }
        }
        LastGhostMs = (float)gsw.Elapsed.TotalMilliseconds;
    }
}
