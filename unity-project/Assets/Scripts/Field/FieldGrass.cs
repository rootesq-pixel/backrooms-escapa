using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.0 Campo infinito: hierba realista con instancias en GPU (DrawMeshInstancedIndirect).
// · Matas escaneadas reales (Poly Haven, CC0) con su textura con alfa; varias especies, alturas, colores y puntas secas.
// · Trozos de 8 m alrededor de la cámara, regenerados/reciclados al moverse (posiciones deterministas por trozo: el campo se repite).
// · 3 niveles de detalle: cerca (densa), media (matas más grandes y menos) y lejos (tarjetas grandes y escasas); más allá, el suelo.
// · Viento (ruido + ráfagas), flexión al pasar jugadores/monstruos, AO en la base, translucidez a contraluz (shader FieldGrass).
// · Calidad: Bajo = menos densidad y radio; Medio; Alto.
public class FieldGrass : MonoBehaviour
{
    public static FieldGrass Active;

    private class Kind
    {
        public string name; public Mesh mesh; public Material mat; public float maxH; public float weight; public float sMin, sMax; public int lodMax; public float dry;
    }
    private class Chunk
    {
        public int lod; public float lastUsed; public int total;
        public ComputeBuffer[] inst; public ComputeBuffer[] args; public MaterialPropertyBlock[] mpb; public int[] counts; public Bounds bounds;
    }

    public const float CS = 8f;
    private readonly List<Kind> kinds = new List<Kind>();
    private readonly Dictionary<long, Chunk> chunks = new Dictionary<long, Chunk>();
    private readonly List<long> evict = new List<long>();
    private readonly List<Object> owned = new List<Object>();
    private readonly Vector4[] pushers = new Vector4[8];
    private readonly Plane[] planes = new Plane[6];
    private Texture2D windNoise;
    private bool canDraw;
    private Transform levelRoot;
    private BackroomsGenerator gen;
    private readonly List<Vector3> shortSpots = new List<Vector3>();

    // estadísticas (pruebas / rendimiento)
    public int VisibleInstances { get; private set; }
    public int VisibleTriangles { get; private set; }
    public int DrawCalls { get; private set; }
    public int ChunkCount { get { return chunks.Count; } }
    public int KindCount { get { return kinds.Count; } }
    public float Radius { get; private set; }
    public float Density { get; private set; }
    private int builtQuality = -1;

    public static FieldGrass Create(Transform parent, BackroomsGenerator g)
    {
        GameObject go = new GameObject("FieldGrass");
        go.transform.SetParent(parent, false);
        FieldGrass fg = go.AddComponent<FieldGrass>();
        fg.gen = g; fg.levelRoot = g.Root;
        fg.Init();
        Active = fg;
        return fg;
    }

    private void Init()
    {
        canDraw = SystemInfo.supportsComputeShaders && SystemInfo.supportsInstancing && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        Shader sh = Shader.Find("Backrooms/FieldGrass");
        if (sh == null || !sh.isSupported) canDraw = false;
        windNoise = MakeNoise(64, 11); owned.Add(windNoise);
        Shader.SetGlobalTexture("_WindNoise", windNoise);
        //       nombre (Resources/Field)       textura                peso  escala      lodMax seco
        // las matas «tall» (≈300 triángulos, las más altas) forman la masa de la pradera; las bajas y densas rellenan de cerca
        Add("grass_medium_01_tall_a", "grass_medium_01", 3.0f, 2.3f, 3.6f, 2, 0.35f, sh);
        Add("grass_medium_01_tall_b", "grass_medium_01", 3.0f, 2.3f, 3.6f, 2, 0.35f, sh);
        Add("grass_medium_01_tall_c", "grass_medium_01", 2.6f, 2.4f, 3.8f, 2, 0.4f, sh);
        Add("grass_medium_02_e", "grass_medium_02", 1.0f, 1.7f, 2.6f, 2, 0.15f, sh);
        Add("grass_medium_02_c", "grass_medium_02", 0.6f, 2.0f, 3.0f, 1, 0.1f, sh);
        Add("grass_medium_02_d", "grass_medium_02", 0.6f, 1.9f, 2.8f, 1, 0.1f, sh);
        Add("grass_medium_01_mid_a", "grass_medium_01", 0.5f, 2.0f, 3.0f, 1, 0.2f, sh);
        Add("grass_medium_01_mid_b", "grass_medium_01", 0.4f, 2.0f, 3.0f, 1, 0.25f, sh);
        Add("grass_medium_01_large_a", "grass_medium_01", 0.35f, 2.0f, 3.0f, 0, 0.15f, sh);
        Add("grass_medium_01_large_b", "grass_medium_01", 0.35f, 2.0f, 3.0f, 0, 0.15f, sh);
        Add("grass_medium_01_large_c", "grass_medium_01", 0.3f, 2.0f, 2.8f, 0, 0.2f, sh);
        Add("grass_bermuda_01_seedling_a", "grass_bermuda_01", 0.25f, 1.8f, 2.8f, 0, 0.6f, sh);
        Add("grass_bermuda_01_seedling_b", "grass_bermuda_01", 0.25f, 1.8f, 2.8f, 0, 0.6f, sh);
        Add("dandelion_01_a", "dandelion_01", 0.05f, 1.6f, 2.4f, 0, 0f, sh);
        Add("dandelion_01_b", "dandelion_01", 0.04f, 1.6f, 2.4f, 0, 0f, sh);
        // sitios donde la hierba es corta (objetos, puerta) para que se puedan encontrar
        if (gen != null)
        {
            if (gen.BatterySpots != null) foreach (Vector3 p in gen.BatterySpots) shortSpots.Add(levelRoot.InverseTransformPoint(p));
            if (gen.ExtraSpots != null) foreach (Vector3 p in gen.ExtraSpots) shortSpots.Add(levelRoot.InverseTransformPoint(p));
        }
    }

    public void AddShortSpot(Vector3 world) { if (levelRoot != null) shortSpots.Add(levelRoot.InverseTransformPoint(world)); }

    private void Add(string mesh, string tex, float weight, float sMin, float sMax, int lodMax, float dry, Shader sh)
    {
        TextAsset ta = Resources.Load<TextAsset>("Field/" + mesh);
        if (ta == null) return;
        Mesh m = LoadMesh(ta.bytes, mesh); owned.Add(m);
        Kind k = new Kind { name = mesh, mesh = m, weight = weight, sMin = sMin, sMax = sMax, lodMax = lodMax, dry = dry, maxH = Mathf.Max(0.02f, m.bounds.max.y) };
        if (sh != null)
        {
            k.mat = new Material(sh) { name = "Grass_" + mesh, enableInstancing = true };
            k.mat.SetTexture("_MainTex", Resources.Load<Texture2D>("Field/" + tex + "_rgba"));
            k.mat.SetFloat("_MaxH", k.maxH);
            owned.Add(k.mat);
        }
        kinds.Add(k);
    }

    public static Mesh LoadMesh(byte[] b, string name)
    {
        System.IO.BinaryReader r = new System.IO.BinaryReader(new System.IO.MemoryStream(b));
        int nv = r.ReadInt32(), ni = r.ReadInt32();
        Vector3[] v = new Vector3[nv]; Vector3[] n = new Vector3[nv]; Vector2[] uv = new Vector2[nv];
        for (int i = 0; i < nv; i++)
        {
            v[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            n[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
        }
        int[] idx = new int[ni];
        for (int i = 0; i < ni; i++) idx[i] = r.ReadInt32();
        Mesh m = new Mesh { name = name };
        m.vertices = v; m.normals = n; m.uv = uv; m.triangles = idx; m.RecalculateBounds();
        return m;
    }

    private static Texture2D MakeNoise(int size, int seed)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "WindNoise", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size, s = 0f, a = 0.5f;
                for (int o = 1; o <= 8; o *= 2) { s += a * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (o * u + seed * 0.1f)) * Mathf.Sin(2f * Mathf.PI * (o * v + o * u * 0.5f + seed * 0.3f))); a *= 0.5f; }
                byte b = (byte)Mathf.Clamp(s * 255f, 0f, 255f);
                px[y * size + x] = new Color32(b, b, b, 255);
            }
        t.SetPixels32(px); t.Apply(true, true);
        return t;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        foreach (Chunk c in chunks.Values) Release(c);
        chunks.Clear();
        foreach (Object o in owned) ProceduralAssets.SafeDestroy(o);
    }

    private static void Release(Chunk c)
    {
        if (c.inst != null) foreach (ComputeBuffer b in c.inst) if (b != null) b.Release();
        if (c.args != null) foreach (ComputeBuffer b in c.args) if (b != null) b.Release();
    }

    public void Settings(out float radius, out float density)
    {
        int q = GameSettings.Quality;
        radius = q == 0 ? 22f : (q == 1 ? 40f : 56f);
        density = q == 0 ? 0.4f : (q == 1 ? 0.75f : 1f);
    }

    private static bool Heavy(Kind k) { return k.mesh.GetIndexCount(0) / 3 > 400; }   // Bajo: solo matas de <=400 triángulos

    public static int LodFor(float d) { return d < 14f ? 0 : (d < 30f ? 1 : 2); }
    // v7.0.1: bandas de LOD solapadas con fundido (encogido) de FadeW m a cada lado; el último LOD se apaga antes del radio
    public const float FadeW = 3f;
    public static Vector2 Band(int lod, float radius)
    {
        float lo = lod == 0 ? -100f : (lod == 1 ? 14f : 30f);
        float hi = lod == 0 ? 14f : (lod == 1 ? 30f : 1000f);
        return new Vector2(lo, Mathf.Min(hi, radius - FadeW));
    }
    public float CurrentRadius { get { return Radius; } }
    private static readonly float[] LodDensity = { 10.0f, 4.5f, 1.8f };   // matas / m²
    private static readonly float[] LodScale = { 1f, 1.3f, 1.7f };

    private void LateUpdate() { Submit(); }

    // también lo llaman las capturas del editor antes de Camera.Render() (los Graphics.Draw* solo valen para el fotograma en curso)
    public void Submit()
    {
        Camera cam = Camera.main;
        if (cam == null || levelRoot == null) return;
        Settings(out float radius, out float density);
        if (density != Density || GameSettings.Quality != builtQuality)
        {
            // cambio de calidad: se rehacen todos los trozos con la nueva densidad (y en Bajo solo matas ligeras)
            foreach (Chunk old in chunks.Values) Release(old);
            chunks.Clear();
            builtQuality = GameSettings.Quality;
        }
        Radius = radius; Density = density;
        Vector3 c = levelRoot.InverseTransformPoint(cam.transform.position);
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        UpdatePushers();
        float pr = radius + CS * 1.5f;   // radio de precarga
        int cx0 = Mathf.FloorToInt((c.x - pr) / CS), cx1 = Mathf.FloorToInt((c.x + pr) / CS);
        int cz0 = Mathf.FloorToInt((c.z - pr) / CS), cz1 = Mathf.FloorToInt((c.z + pr) / CS);
        float now = Time.unscaledTime;
        int vis = 0, tris = 0, calls = 0;
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cz = cz0; cz <= cz1; cz++)
            {
                float mx = (cx + 0.5f) * CS - c.x, mz = (cz + 0.5f) * CS - c.z;
                float dcen = Mathf.Sqrt(mx * mx + mz * mz);
                float dmin = Mathf.Max(0f, dcen - CS * 0.71f), dmax = dcen + CS * 0.71f;
                if (dmin > radius + CS) continue;
                for (int lod = 0; lod < 3; lod++)
                {
                Vector2 band = Band(lod, radius);
                if (band.y <= band.x) continue;
                bool draw = dmax >= band.x - FadeW && dmin <= band.y + FadeW;
                // precarga: el trozo se crea un paso antes de entrar en su banda (sin tirones ni huecos al caminar)
                bool keep = draw || (dmax >= band.x - FadeW - CS && dmin <= band.y + FadeW + CS);
                if (!keep) continue;
                long key = ((long)(cx + 100000) << 24) ^ ((long)(cz + 100000) << 2) ^ lod;
                if (!chunks.TryGetValue(key, out Chunk ch))
                {
                    ch = MakeChunk(cx, cz, lod, density);
                    for (int k = 0; k < kinds.Count; k++) if (ch.mpb[k] != null) ch.mpb[k].SetVector("_LodBand", new Vector4(band.x, band.y, FadeW, 0f));
                    chunks[key] = ch;
                }
                ch.lastUsed = now;
                Bounds wb = ch.bounds; wb.center = levelRoot.TransformPoint(wb.center);
                if (!draw || !GeometryUtility.TestPlanesAABB(planes, wb)) continue;
                for (int k = 0; k < kinds.Count; k++)
                {
                    if (ch.counts[k] == 0) continue;
                    vis += ch.counts[k]; tris += ch.counts[k] * (int)(kinds[k].mesh.GetIndexCount(0) / 3); calls++;
                    if (canDraw && kinds[k].mat != null)
                        Graphics.DrawMeshInstancedIndirect(kinds[k].mesh, 0, kinds[k].mat, wb, ch.args[k], 0, ch.mpb[k], ShadowCastingMode.Off, false, 0, null);
                }
            }
            }

        VisibleInstances = vis; VisibleTriangles = tris; DrawCalls = calls;
        // reciclar trozos lejanos o con otro detalle (se liberan los buffers)
        evict.Clear();
        foreach (KeyValuePair<long, Chunk> kv in chunks) if (now - kv.Value.lastUsed > 3.0f) evict.Add(kv.Key);
        foreach (long k in evict) { Release(chunks[k]); chunks.Remove(k); }
    }

    // jugador local, otros jugadores y monstruos doblan la hierba a su paso
    private void UpdatePushers()
    {
        int n = 0;
        FirstPersonController f = FirstPersonController.LocalInstance;
        if (f != null) pushers[n++] = Push(f.transform.position, 0.75f);
        foreach (RemotePlayer rp in RemotePlayer.All) if (rp != null && n < 5) pushers[n++] = Push(rp.transform.position, 0.75f);
        if (GameManager.Instance != null && GameManager.Instance.monsters != null)
            foreach (MonsterAI m in GameManager.Instance.monsters) if (m != null && n < 8) pushers[n++] = Push(m.transform.position, 1.0f);
        for (int i = n; i < 8; i++) pushers[i] = Vector4.zero;
        Shader.SetGlobalVectorArray("_GrassPush", pushers);
    }

    private static Vector4 Push(Vector3 p, float r) { return new Vector4(p.x, p.y, p.z, r); }

    private struct Inst { public Vector4 pos; public Vector4 prm; }

    private Chunk MakeChunk(int cx, int cz, int lod, float density)
    {
        float L = FieldHeight.L;
        int ncx = Mathf.Max(1, Mathf.RoundToInt(L / CS));
        int wx = ((cx % ncx) + ncx) % ncx, wz = ((cz % ncx) + ncx) % ncx;   // trozo «real» (el campo se repite)
        System.Random r = new System.Random(wx * 73856093 ^ wz * 19349663 ^ lod * 83492791 ^ (gen != null ? gen.UsedSeed : 0));
        List<Inst>[] per = new List<Inst>[kinds.Count];
        for (int k = 0; k < kinds.Count; k++) per[k] = new List<Inst>();
        float total = 0f;
        bool light = GameSettings.Quality == 0;
        foreach (Kind k in kinds) if (k.lodMax >= lod && !(light && Heavy(k))) total += k.weight;
        int count = Mathf.RoundToInt(CS * CS * LodDensity[lod] * density);
        float x0 = cx * CS, z0 = cz * CS, ymin = 999f, ymax = -999f;
        FieldPlan plan = FieldWorld.Active != null ? FieldWorld.Active.plan : null;
        Vector3 cab = ElevatorCabin.Current != null && levelRoot != null ? levelRoot.InverseTransformPoint(ElevatorCabin.Current.transform.position) : new Vector3(-999f, 0f, -999f);
        for (int i = 0; i < count; i++)
        {
            float x = x0 + (float)r.NextDouble() * CS, z = z0 + (float)r.NextDouble() * CS;
            float xw = FieldHeight.Mod(x), zw = FieldHeight.Mod(z);
            // manchas: zonas más densas/altas y claros (ruido de baja frecuencia periódico)
            float patch = Patch(xw, zw);
            if (r.NextDouble() > 0.6 + 0.4 * patch) { r.NextDouble(); r.NextDouble(); r.NextDouble(); continue; }
            if (plan != null && plan.NoGrass(xw, zw)) continue;
            if (FieldHeight.WrappedDist(cab.x, cab.z, xw, zw) < 2.6f) continue;
            float shortK = 1f;
            for (int s = 0; s < shortSpots.Count; s++)
            {
                float dd = FieldHeight.WrappedDist(shortSpots[s].x, shortSpots[s].z, xw, zw);
                if (dd < 1.6f) shortK = Mathf.Min(shortK, Mathf.Lerp(0.25f, 1f, dd / 1.6f));
            }
            double pick = r.NextDouble() * total; int kk = 0;
            for (int k = 0; k < kinds.Count; k++) { if (kinds[k].lodMax < lod || (light && Heavy(kinds[k]))) continue; pick -= kinds[k].weight; kk = k; if (pick <= 0) break; }
            Kind K = kinds[kk];
            float sc = Mathf.Lerp(K.sMin, K.sMax, (float)r.NextDouble()) * LodScale[lod] * Mathf.Lerp(0.75f, 1.2f, patch);
            float hmul = Mathf.Lerp(0.8f, 1.3f, (float)r.NextDouble()) * shortK;
            float dryN = Mathf.Clamp01(K.dry + (Dry(xw, zw) - 0.5f) * 0.9f + (float)(r.NextDouble() - 0.5) * 0.3f);
            float y = FieldHeight.H(x, z);
            ymin = Mathf.Min(ymin, y); ymax = Mathf.Max(ymax, y);
            per[kk].Add(new Inst
            {
                pos = new Vector4(x, y - 0.02f, z, sc),
                prm = new Vector4((float)r.NextDouble() * 6.2832f, (float)r.NextDouble(), dryN, hmul),
            });
        }
        Chunk ch = new Chunk { lod = lod, counts = new int[kinds.Count], inst = new ComputeBuffer[kinds.Count], args = new ComputeBuffer[kinds.Count], mpb = new MaterialPropertyBlock[kinds.Count] };
        if (ymin > ymax) { ymin = 0f; ymax = 0f; }
        ch.bounds = new Bounds(new Vector3(x0 + CS * 0.5f, (ymin + ymax) * 0.5f + 0.6f, z0 + CS * 0.5f), new Vector3(CS + 3f, ymax - ymin + 3f, CS + 3f));
        for (int k = 0; k < kinds.Count; k++)
        {
            ch.counts[k] = per[k].Count; ch.total += per[k].Count;
            if (per[k].Count == 0 || !canDraw) continue;
            ch.inst[k] = new ComputeBuffer(per[k].Count, 32);
            ch.inst[k].SetData(per[k]);
            ch.args[k] = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            ch.args[k].SetData(new uint[] { kinds[k].mesh.GetIndexCount(0), (uint)per[k].Count, kinds[k].mesh.GetIndexStart(0), kinds[k].mesh.GetBaseVertex(0), 0 });
            ch.mpb[k] = new MaterialPropertyBlock();
            ch.mpb[k].SetBuffer("_Inst", ch.inst[k]);
        }
        return ch;
    }

    // ruido periódico (periodo L) para manchas de hierba y de hierba seca
    public static float Patch(float x, float z)
    {
        float k = 2f * Mathf.PI / FieldHeight.L;
        float s = Mathf.Sin(k * 7f * x + 1.3f) * Mathf.Sin(k * 5f * z + 0.4f) * 0.5f + Mathf.Sin(k * (11f * x + 13f * z) + 2.1f) * 0.3f + Mathf.Sin(k * (23f * x - 17f * z)) * 0.2f;
        return Mathf.Clamp01(0.55f + s * 0.6f);
    }

    public static float Dry(float x, float z)
    {
        float k = 2f * Mathf.PI / FieldHeight.L;
        float s = Mathf.Sin(k * (4f * x + 3f * z) + 0.7f) * 0.6f + Mathf.Sin(k * (9f * x - 6f * z) + 2.4f) * 0.4f;
        return Mathf.Clamp01(0.5f + s * 0.5f);
    }
}
