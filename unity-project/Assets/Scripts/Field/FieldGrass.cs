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
    // v7.0.2: cada trozo (por trozo «real» del campo que se repite + LOD) ocupa una ranura fija del búfer de instancias de GPU.
    private class Chunk
    {
        public int lod, wx, wz, slot = -1, total; public float lastUsed; public int[] counts; public Bounds bounds;   // bounds en coords del trozo real
        public bool ready, queued; public long key;
    }
    private class Job { public int ep; public FieldPlan plan; public Chunk ch; public int seed; public float density; public bool light; public Vector3 cab; public Inst[] data; public int n; public float ymin, ymax; public int[] counts; }
    private struct Inst { public Vector4 pos; public Vector4 prm; }
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
        chunks.Clear();
        ReleaseGpu();
        foreach (Object o in owned) ProceduralAssets.SafeDestroy(o);
    }

    private void ReleaseGpu()
    {
        foreach (ComputeBuffer b in new[] { srcBuf, dstBuf, argsBuf, slotBuf, bandBuf }) if (b != null) b.Release();
        srcBuf = dstBuf = argsBuf = slotBuf = bandBuf = null;
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
    // v7.0.2: métricas para la prueba de tirones
    public static float LastSubmitMs, MaxSubmitMs; public static int BuiltThisFrame, BuiltTotal;
    private static readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
    public void Submit()
    {
        sw.Restart(); BuiltThisFrame = 0;
        SubmitInner();
        LastSubmitMs = (float)sw.Elapsed.TotalMilliseconds; if (LastSubmitMs > MaxSubmitMs) MaxSubmitMs = LastSubmitMs;
    }
    // ---------------- v7.0.2: núcleo sin tirones ----------------
    // · Los trozos se identifican por su trozo «real» (el campo se repite): al dar la vuelta no se rehace nada.
    // · Las matas se generan en hilos de fondo (System.Threading) y se suben a una ranura fija de un único búfer de GPU
    //   (sin crear ni liberar búferes, sin basura por fotograma); como mucho UploadBudget trozos por fotograma.
    // · Un compute shader descarta por instancia lo que queda fuera de la vista o de su banda de LOD: 1 llamada por especie.
    public static bool Synchronous;               // capturas del editor: generar al momento
    public const int SlotCap = 704, MaxSlots = 640, KindCap = 16384, UploadBudget = 4;
    private ComputeShader cull;
    private ComputeBuffer srcBuf, dstBuf, argsBuf, slotBuf, bandBuf;
    private int kCull = -1, kClear, kClamp;
    private readonly Stack<int> freeSlots = new Stack<int>();
    private readonly Vector4[] slotData = new Vector4[MaxSlots], bandData = new Vector4[MaxSlots], planeVec = new Vector4[6];
    private MaterialPropertyBlock[] kindMpb;
    private readonly bool[] kindUsed = new bool[32];
    private readonly System.Collections.Concurrent.ConcurrentQueue<Job> done = new System.Collections.Concurrent.ConcurrentQueue<Job>();
    private readonly System.Collections.Concurrent.ConcurrentBag<Inst[]> pool = new System.Collections.Concurrent.ConcurrentBag<Inst[]>();
    private struct KindInfo { public float weight, sMin, sMax, dry; public int lodMax; public bool heavy; }
    private KindInfo[] kinfo;
    private Vector3[] shortArr = new Vector3[0];
    private int epoch, pending;
    private float syncUntil;
    public int PendingJobs { get { return pending; } }
    public int FreeSlots { get { return freeSlots.Count; } }
    public static int SlotStarved; public static float MaxPartTotal; public static Vector3 MaxParts;
    public int ReadyChunks { get; private set; }

    private bool InitGpu()
    {
        if (kCull >= 0) return true;
        if (!canDraw) return false;
        cull = Resources.Load<ComputeShader>("Field/Shaders/FieldGrassCull");
        if (cull == null) { canDraw = false; return false; }
        kCull = cull.FindKernel("Cull"); kClear = cull.FindKernel("Clear"); kClamp = cull.FindKernel("Clamp");
        srcBuf = new ComputeBuffer(SlotCap * MaxSlots, 32);
        dstBuf = new ComputeBuffer(KindCap * Mathf.Max(1, kinds.Count), 32);
        argsBuf = new ComputeBuffer(5 * Mathf.Max(1, kinds.Count), sizeof(uint), ComputeBufferType.IndirectArguments);
        slotBuf = new ComputeBuffer(MaxSlots, 16); bandBuf = new ComputeBuffer(MaxSlots, 16);
        uint[] args = new uint[5 * kinds.Count];
        kindMpb = new MaterialPropertyBlock[kinds.Count];
        for (int k = 0; k < kinds.Count; k++)
        {
            args[k * 5] = kinds[k].mesh.GetIndexCount(0); args[k * 5 + 2] = kinds[k].mesh.GetIndexStart(0); args[k * 5 + 3] = (uint)kinds[k].mesh.GetBaseVertex(0);
            kindMpb[k] = new MaterialPropertyBlock(); kindMpb[k].SetBuffer("_Inst", dstBuf); kindMpb[k].SetFloat("_Base", k * KindCap);
        }
        argsBuf.SetData(args);
        foreach (int kk in new[] { kCull, kClear, kClamp }) { cull.SetBuffer(kk, "_Args", argsBuf); }
        cull.SetBuffer(kCull, "_Src", srcBuf); cull.SetBuffer(kCull, "_Dst", dstBuf); cull.SetBuffer(kCull, "_Slots", slotBuf); cull.SetBuffer(kCull, "_Bands", bandBuf);
        cull.SetInt("_KindCap", KindCap); cull.SetInt("_Kinds", kinds.Count);
        return true;
    }

    private void Snapshot()
    {
        kinfo = new KindInfo[kinds.Count];
        for (int k = 0; k < kinds.Count; k++) kinfo[k] = new KindInfo { weight = kinds[k].weight, sMin = kinds[k].sMin, sMax = kinds[k].sMax, dry = kinds[k].dry, lodMax = kinds[k].lodMax, heavy = Heavy(kinds[k]) };
        shortArr = shortSpots.ToArray();
    }

    private void ClearAll()
    {
        chunks.Clear(); freeSlots.Clear();
        for (int i = MaxSlots - 1; i >= 0; i--) freeSlots.Push(i);
        epoch++; ReadyChunks = 0;
        syncUntil = Time.unscaledTime + 0.25f;   // relleno inicial (pantalla de carga / cambio de calidad): de golpe
    }

    private void SubmitInner()
    {
        Camera cam = Camera.main;
        if (cam == null || levelRoot == null) return;
        Settings(out float radius, out float density);
        if (kinfo == null || shortArr.Length != shortSpots.Count) Snapshot();
        if (density != Density || GameSettings.Quality != builtQuality)
        {
            ClearAll();
            builtQuality = GameSettings.Quality;
        }
        Radius = radius; Density = density;
        bool gpu = InitGpu();
        bool sync = Synchronous || Time.unscaledTime < syncUntil;
        Vector3 c = levelRoot.InverseTransformPoint(cam.transform.position);
        GeometryUtility.CalculateFrustumPlanes(cam, planes);
        UpdatePushers();
        float L = FieldHeight.L; int ncx = Mathf.Max(1, Mathf.RoundToInt(L / CS));
        float pr = radius + CS * 1.5f;   // radio de precarga
        int cx0 = Mathf.FloorToInt((c.x - pr) / CS), cx1 = Mathf.FloorToInt((c.x + pr) / CS);
        int cz0 = Mathf.FloorToInt((c.z - pr) / CS), cz1 = Mathf.FloorToInt((c.z + pr) / CS);
        float now = Time.unscaledTime;
        int vis = 0, tris = 0, active = 0;
        for (int k = 0; k < kindUsed.Length; k++) kindUsed[k] = false;
        bool light = GameSettings.Quality == 0;
        FieldPlan plan = FieldWorld.Active != null ? FieldWorld.Active.plan : null;
        Vector3 cab = ElevatorCabin.Current != null ? levelRoot.InverseTransformPoint(ElevatorCabin.Current.transform.position) : new Vector3(-999f, 0f, -999f);
        int seed = gen != null ? gen.UsedSeed : 0;

        // primero los trozos cercanos (si falta alguno, se piden antes)
        for (int pass = 0; pass < 2; pass++)
        for (int cx = cx0; cx <= cx1; cx++)
            for (int cz = cz0; cz <= cz1; cz++)
            {
                float mx = (cx + 0.5f) * CS - c.x, mz = (cz + 0.5f) * CS - c.z;
                float dcen = Mathf.Sqrt(mx * mx + mz * mz);
                if ((pass == 0) != (dcen < 24f)) continue;
                float dmin = Mathf.Max(0f, dcen - CS * 0.71f), dmax = dcen + CS * 0.71f;
                if (dmin > radius + CS) continue;
                int wx = ((cx % ncx) + ncx) % ncx, wz = ((cz % ncx) + ncx) % ncx;
                for (int lod = 0; lod < 3; lod++)
                {
                    Vector2 band = Band(lod, radius);
                    if (band.y <= band.x) continue;
                    bool draw = dmax >= band.x - FadeW && dmin <= band.y + FadeW;
                    // precarga: el trozo se pide un paso antes de entrar en su banda
                    bool keep = draw || (dmax >= band.x - FadeW - CS * 1.5f && dmin <= band.y + FadeW + CS * 1.5f);
                    if (!keep) continue;
                    long key = ((long)(wx + 1000) << 24) ^ ((long)(wz + 1000) << 2) ^ lod;
                    if (!chunks.TryGetValue(key, out Chunk ch))
                    {
                        ch = new Chunk { lod = lod, wx = wx, wz = wz, key = key, counts = new int[kinds.Count] };
                        chunks[key] = ch;
                        Job j = new Job { ch = ch, seed = seed, density = density, light = light, cab = cab, counts = new int[kinds.Count] };
                        j.ep = epoch; j.plan = plan;
                        ch.queued = true; pending++;
                        if (sync) { Generate(j); done.Enqueue(j); }
                        else System.Threading.ThreadPool.QueueUserWorkItem(o => { Job jj = (Job)o; try { Generate(jj); } catch (System.Exception) { jj.n = 0; } done.Enqueue(jj); }, j);
                    }
                    ch.lastUsed = now;
                    if (!ch.ready || !draw || ch.total == 0) continue;
                    Vector3 off = new Vector3((cx - wx) * CS, 0f, (cz - wz) * CS);
                    Bounds wb = ch.bounds; wb.center = levelRoot.TransformPoint(wb.center + off);
                    if (!GeometryUtility.TestPlanesAABB(planes, wb)) continue;
                    if (active < MaxSlots)
                    {
                        slotData[active] = new Vector4(ch.slot * SlotCap, ch.total, off.x, off.z);
                        bandData[active] = new Vector4(band.x, band.y, FadeW, 0f);
                        active++;
                    }
                    for (int k = 0; k < kinds.Count; k++)
                    {
                        if (ch.counts[k] == 0) continue;
                        kindUsed[k] = true; vis += ch.counts[k]; tris += ch.counts[k] * (int)(kinds[k].mesh.GetIndexCount(0) / 3);
                    }
                }
            }

        // subir resultados terminados (presupuesto por fotograma: sin picos)
        float tLoop = (float)sw.Elapsed.TotalMilliseconds;
        int up = 0;
        while ((sync || (up < UploadBudget && (up == 0 || sw.Elapsed.TotalMilliseconds - tLoop < 1.2))) && done.TryDequeue(out Job r))
        {
            pending--;
            Chunk ch = r.ch;
            bool alive = r.ep == epoch && chunks.TryGetValue(ch.key, out Chunk cur) && cur == ch;
            if (alive && freeSlots.Count == 0) StealSlot(now);   // tras un teletransporte: reciclar el trozo más antiguo que ya no se usa
            if (alive && freeSlots.Count > 0)
            {
                ch.slot = freeSlots.Pop();
                ch.total = r.n; ch.counts = r.counts; ch.queued = false; ch.ready = true; ReadyChunks++;
                float x0 = ch.wx * CS, z0 = ch.wz * CS;
                float ymin = r.ymin, ymax = r.ymax; if (ymin > ymax) { ymin = 0f; ymax = 0f; }
                ch.bounds = new Bounds(new Vector3(x0 + CS * 0.5f, (ymin + ymax) * 0.5f + 0.6f, z0 + CS * 0.5f), new Vector3(CS + 3f, ymax - ymin + 3f, CS + 3f));
                if (gpu && r.n > 0) srcBuf.SetData(r.data, 0, ch.slot * SlotCap, r.n);
                BuiltThisFrame++; BuiltTotal++; up++;
            }
            else if (alive) { chunks.Remove(ch.key); SlotStarved++; }
            if (r.data != null) pool.Add(r.data);
            r.data = null;
        }

        float tUp = (float)sw.Elapsed.TotalMilliseconds;
        int calls = 0;
        if (gpu && active > 0)
        {
            slotBuf.SetData(slotData, 0, 0, active); bandBuf.SetData(bandData, 0, 0, active);
            for (int i = 0; i < 6; i++) planeVec[i] = new Vector4(planes[i].normal.x, planes[i].normal.y, planes[i].normal.z, planes[i].distance);
            cull.SetVectorArray("_Planes", planeVec);
            cull.SetVector("_Cam", new Vector4(c.x, c.y, c.z, active));
            cull.Dispatch(kClear, 1, 1, 1);
            cull.Dispatch(kCull, active, 1, 1);
            cull.Dispatch(kClamp, 1, 1, 1);
            Bounds big = new Bounds(cam.transform.position, new Vector3(radius * 2f + 30f, 300f, radius * 2f + 30f));
            for (int k = 0; k < kinds.Count; k++)
            {
                if (!kindUsed[k] || kinds[k].mat == null) continue;
                Graphics.DrawMeshInstancedIndirect(kinds[k].mesh, 0, kinds[k].mat, big, argsBuf, k * 20, kindMpb[k], ShadowCastingMode.Off, false, 0, null);
                calls++;
            }
        }
        else if (active > 0) { for (int k = 0; k < kinds.Count; k++) if (kindUsed[k]) calls++; }

        float tGpu = (float)sw.Elapsed.TotalMilliseconds;
        if (tGpu > MaxPartTotal) { MaxPartTotal = tGpu; MaxParts = new Vector3(tLoop, tUp - tLoop, tGpu - tUp); }
        VisibleInstances = vis; VisibleTriangles = tris; DrawCalls = calls;
        // reciclar trozos lejanos (la ranura vuelve a la pila; nada se libera en GPU)
        evict.Clear();
        foreach (KeyValuePair<long, Chunk> kv in chunks) if (now - kv.Value.lastUsed > 3.0f && !kv.Value.queued) evict.Add(kv.Key);
        foreach (long k in evict) { Chunk ch = chunks[k]; if (ch.ready && ch.slot >= 0) { freeSlots.Push(ch.slot); ReadyChunks--; } chunks.Remove(k); }
    }

    private void StealSlot(float now)
    {
        Chunk best = null;
        foreach (KeyValuePair<long, Chunk> kv in chunks)
        {
            Chunk c = kv.Value;
            if (!c.ready || c.slot < 0 || c.lastUsed >= now) continue;
            if (best == null || c.lastUsed < best.lastUsed) best = c;
        }
        if (best == null) return;
        freeSlots.Push(best.slot); ReadyChunks--; chunks.Remove(best.key);
    }

    // en un hilo de fondo: solo matemáticas deterministas (misma semilla => mismas matas en todos los equipos)
    private void Generate(Job j)
    {
        Chunk ch = j.ch; int lod = ch.lod;
        Inst[] buf; if (!pool.TryTake(out buf)) buf = new Inst[SlotCap];
        j.data = buf; j.n = 0; j.ymin = 999f; j.ymax = -999f;
        System.Random r = new System.Random(ch.wx * 73856093 ^ ch.wz * 19349663 ^ lod * 83492791 ^ j.seed);
        float total = 0f;
        KindInfo[] ki = kinfo; Vector3[] spots = shortArr;
        for (int k = 0; k < ki.Length; k++) if (ki[k].lodMax >= lod && !(j.light && ki[k].heavy)) total += ki[k].weight;
        int count = Mathf.RoundToInt(CS * CS * LodDensity[lod] * j.density);
        float x0 = ch.wx * CS, z0 = ch.wz * CS;
        FieldPlan plan = j.plan;
        for (int i = 0; i < count; i++)
        {
            float x = x0 + (float)r.NextDouble() * CS, z = z0 + (float)r.NextDouble() * CS;
            float xw = x, zw = z;
            float patch = Patch(xw, zw);
            if (r.NextDouble() > 0.6 + 0.4 * patch) { r.NextDouble(); r.NextDouble(); r.NextDouble(); continue; }
            if (plan != null && plan.NoGrass(xw, zw)) continue;
            if (FieldHeight.WrappedDist(j.cab.x, j.cab.z, xw, zw) < 2.6f) continue;
            float shortK = 1f;
            for (int s = 0; s < spots.Length; s++)
            {
                float dd = FieldHeight.WrappedDist(spots[s].x, spots[s].z, xw, zw);
                if (dd < 1.6f) shortK = Mathf.Min(shortK, Mathf.Lerp(0.25f, 1f, dd / 1.6f));
            }
            double pick = r.NextDouble() * total; int kk = 0;
            for (int k = 0; k < ki.Length; k++) { if (ki[k].lodMax < lod || (j.light && ki[k].heavy)) continue; pick -= ki[k].weight; kk = k; if (pick <= 0) break; }
            KindInfo K = ki[kk];
            float sc = Mathf.Lerp(K.sMin, K.sMax, (float)r.NextDouble()) * LodScale[lod] * Mathf.Lerp(0.75f, 1.2f, patch);
            float hmul = Mathf.Lerp(0.8f, 1.3f, (float)r.NextDouble()) * shortK;
            float dryN = Mathf.Clamp01(K.dry + (Dry(xw, zw) - 0.5f) * 0.9f + (float)(r.NextDouble() - 0.5) * 0.3f);
            float y = FieldHeight.H(x, z);
            float rot = (float)r.NextDouble() * 6.2832f, tone = (float)r.NextDouble();
            if (j.n >= SlotCap) continue;
            j.ymin = Mathf.Min(j.ymin, y); j.ymax = Mathf.Max(j.ymax, y);
            buf[j.n++] = new Inst { pos = new Vector4(x, y - 0.02f, z, sc), prm = new Vector4(rot, kk + Mathf.Min(tone, 0.999f), dryN, hmul) };
            j.counts[kk]++;
        }
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
