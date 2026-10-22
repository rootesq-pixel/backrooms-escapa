using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v6.7.1: sala de piscina de Blender (PoolSetPiece.fbx: vaso, playa y escalera cromada) dentro de un bloque abierto de las Poolrooms.
// Paredes del bloque con azulejo blanco de Blender, tragaluces y ventanas emisivas con luz fría, sonda de reflexión propia.
// Colisión: MeshCollider en vaso y playa (el agua usa raycast al suelo, así que se nada en el vaso y se vadea fuera).
public static class PoolRoomSetPiece
{
    public static GameObject Root { get; private set; }
    public static int LightmapSize { get; private set; }   // v7.1.1 (pruebas)

    static Material TileMat(string n, Color tint, float smooth)
    {
        Texture2D a = Resources.Load<Texture2D>("Textures/PoolRoom/" + n + "_a");
        Texture2D nm = Resources.Load<Texture2D>("Textures/PoolRoom/" + n + "_n");
        Texture2D m = Resources.Load<Texture2D>("Textures/PoolRoom/" + n + "_m");
        SurfaceSet set = new SurfaceSet { albedo = a, normal = nm, mask = m, baked = true };
        Material mat = ProceduralAssets.CreateSurfaceMaterial(set, tint, 1f, 1f);
        if (mat.HasProperty("_GlossMapScale")) mat.SetFloat("_GlossMapScale", smooth);
        mat.name = "PoolRoom_" + n;
        return mat;
    }

    // v6.8: sala fija horneada en Blender Cycles (pool-render/fixed/build_fixed.py): malla + lightmap EXR + sondas
    public const float BakeExposure = 0.62f;
    private static int bakedState = -1;
    public static bool HasBakedAssets
    {
        get
        {
            if (bakedState < 0)
                bakedState = Resources.Load<TextAsset>("PoolRoom/PoolRoomFixed") != null && Resources.Load<Texture2D>("PoolRoom/PoolRoom_LM") != null
                    && Shader.Find("Backrooms/PoolBaked") != null ? 1 : 0;
            return bakedState == 1;
        }
    }

    public static Mesh LoadBakedMesh()
    {
        TextAsset ta = Resources.Load<TextAsset>("PoolRoom/PoolRoomFixed");
        if (ta == null) return null;
        using (var br = new System.IO.BinaryReader(new System.IO.MemoryStream(ta.bytes)))
        {
            int nv = br.ReadInt32(), ns = br.ReadInt32();
            Vector3[] v = new Vector3[nv], n = new Vector3[nv]; Vector2[] a = new Vector2[nv], b = new Vector2[nv];
            for (int i = 0; i < nv; i++)
            {
                v[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                n[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());
                a[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
                b[i] = new Vector2(br.ReadSingle(), br.ReadSingle());
            }
            Mesh m = new Mesh { name = "PoolRoomFixed" };
            m.vertices = v; m.normals = n; m.uv = a; m.uv2 = b; m.subMeshCount = ns;
            for (int k = 0; k < ns; k++)
            {
                int c = br.ReadInt32(); int[] idx = new int[c];
                for (int i = 0; i < c; i++) idx[i] = br.ReadInt32();
                m.SetTriangles(idx, k);
            }
            m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }
    }

    static Material BakedMat(string n, Texture lm, float smooth, Color tint)
    {
        Material m = new Material(Shader.Find("Backrooms/PoolBaked")) { name = "PoolBaked_" + n };
        m.SetTexture("_MainTex", Resources.Load<Texture2D>("Textures/PoolRoom/" + n + "_a"));
        m.SetTexture("_BumpMap", Resources.Load<Texture2D>("Textures/PoolRoom/" + n + "_n"));
        m.SetTexture("_MaskMap", Resources.Load<Texture2D>("Textures/PoolRoom/" + n + "_m"));
        m.SetTexture("_LightMap", lm); m.SetFloat("_LMScale", BakeExposure); m.SetFloat("_Glossiness", smooth); m.SetColor("_Color", tint);
        return m;
    }

    static void BuildBaked(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot)
    {
        float cs = cfg.cellSize, wh = cfg.wallHeight, t = gen.wallThickness;
        RectInt b = gen.PoolBlock;
        GameObject root = new GameObject("PoolRoom");
        root.transform.SetParent(levelRoot, false);
        Root = root;
        Vector3 ctr = new Vector3((b.x + b.width * 0.5f) * cs, 0f, (b.y + b.height * 0.5f) * cs);
        Texture lm = BallpitSetPiece.LoadLightmap("PoolRoom/PoolRoom_LM", "PoolRoom/PoolRoom_LM_Low");   // v7.1.1: 4096 (Bajo: 2048)
        LightmapSize = lm != null ? lm.width : 0;
        Material sky = ProceduralAssets.CreateMaterial(new Color(0.95f, 0.98f, 1f), null, Vector2.one, 0.1f);
        ProceduralAssets.SetEmission(sky, PoolLook.WindowEmission);   // v7.1.3: ventanas casi blancas como en Cycles (antes 2,6-3,0)
        Material[] mats = {
            BakedMat("WallTile", lm, 0.85f, Color.white), BakedMat("DeckTile", lm, 0.9f, Color.white),
            BakedMat("PoolTile", lm, 0.9f, Color.white), BakedMat("WallTile", lm, 0.35f, new Color(0.97f, 0.97f, 0.97f)), sky };
        mats[1].SetFloat("_PlanarK", PoolLook.DeckPlanar);       // v7.1.3: playa brillante con reflejo planar
        mats[2].SetFloat("_PlanarK", PoolLook.DeckPlanar);       // (borde superior del vaso)
        mats[2].SetFloat("_WaterBand", PoolLook.PoolWaterBand);  // banda oscura sobre el agua
        PoolLook.Apply(); PoolLook.ApplyMaterials(mats);
        Mesh mesh = LoadBakedMesh();
        GameObject g = new GameObject("PoolRoomBaked");
        g.transform.SetParent(root.transform, false);
        g.transform.localPosition = ctr;
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
        g.AddComponent<MeshCollider>().sharedMesh = mesh;

        // escalera cromada del set anterior (solo la escalera; vaso y playa vienen de la malla horneada)
        GameObject prefab = Resources.Load<GameObject>("PoolRoom/PoolSetPiece");
        if (prefab != null)
        {
            GameObject sp = Object.Instantiate(prefab, root.transform, false);
            sp.name = "PoolLadder";
            sp.transform.localPosition = ctr + new Vector3(0f, BackroomsGenerator.PoolDeckY, 0f);
            sp.transform.localScale = new Vector3((b.width * cs - t) / 14f, 1f, (b.height * cs - t) / 10f);
            Material chrome = ProceduralAssets.CreateMaterial(new Color(0.85f, 0.87f, 0.9f), null, Vector2.one, 0.92f);
            if (chrome.HasProperty("_Metallic")) chrome.SetFloat("_Metallic", 1f);
            foreach (MeshRenderer r in sp.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("PoolShell") || r.name.StartsWith("Deck")) { r.gameObject.SetActive(false); continue; }
                Material[] arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < arr.Length; i++) arr[i] = chrome;
                r.sharedMaterials = arr; r.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        Vector3 w0 = levelRoot.TransformPoint(new Vector3(b.x * cs, 0f, b.y * cs)), w1 = levelRoot.TransformPoint(new Vector3(b.xMax * cs, 0f, b.yMax * cs));
        Shader.SetGlobalVector("_PoolRoomRect", new Vector4(Mathf.Min(w0.x, w1.x), Mathf.Min(w0.z, w1.z), Mathf.Max(w0.x, w1.x), Mathf.Max(w0.z, w1.z)));
        PoolPlanarReflection pl = root.AddComponent<PoolPlanarReflection>();
        pl.waterY = levelRoot.TransformPoint(new Vector3(0f, cfg.waterLevel, 0f)).y;
        pl.center = levelRoot.TransformPoint(ctr);

        // v6.8.1: datos para salir de la piscina (borde, escalera, rescate)
        {
            Rect pr = gen.PoolRectLocal;
            Vector3 a = levelRoot.TransformPoint(new Vector3(pr.xMin, 0f, pr.yMin)), c = levelRoot.TransformPoint(new Vector3(pr.xMax, 0f, pr.yMax));
            Bounds lb = new Bounds(); bool any = false;
            Transform lad = root.transform.Find("PoolLadder");
            if (lad != null)
                foreach (MeshRenderer r in lad.GetComponentsInChildren<MeshRenderer>(false))
                { if (!any) { lb = r.bounds; any = true; } else lb.Encapsulate(r.bounds); }
            PoolExit.Set(Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.z, c.z), Mathf.Max(a.x, c.x), Mathf.Max(a.z, c.z)),
                levelRoot.TransformPoint(new Vector3(0f, BackroomsGenerator.PoolDeckY, 0f)).y, any, lb, root);
        }

        // sondas de luz horneadas (Cycles) para jugador, cuerpos remotos y monstruos
        PoolRoomProbes probes = root.AddComponent<PoolRoomProbes>();
        probes.Init(levelRoot, ctr, new Bounds(levelRoot.TransformPoint(ctr + new Vector3(0f, wh * 0.5f, 0f)), new Vector3(b.width * cs, wh + 2f, b.height * cs)));

        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            ReflectionProbe rp = new GameObject("PoolRoomProbe").AddComponent<ReflectionProbe>();
            rp.transform.SetParent(root.transform, false);
            rp.transform.localPosition = ctr + new Vector3(0f, 1.6f, 0f);
            rp.mode = ReflectionProbeMode.Realtime; rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            rp.resolution = 256; rp.hdr = true; rp.boxProjection = true; rp.importance = 2; rp.intensity = 1f;
            rp.size = new Vector3(b.width * cs, wh + 1.6f, b.height * cs);
            rp.cullingMask = ~((1 << WaterVolume.WaterLayer) | (1 << 2) | (1 << 5) | (1 << 8));
            rp.clearFlags = ReflectionProbeClearFlags.SolidColor; rp.backgroundColor = new Color(0.6f, 0.7f, 0.75f);
            // v6.9: cubemap renderizado con Cycles desde este mismo punto (caja proyectada); si falta, sonda en tiempo real
            Cubemap cyc = Resources.Load<Cubemap>("PoolRoom/PoolRoom_Cube");
            if (cyc != null)
            {
                rp.mode = ReflectionProbeMode.Custom; rp.customBakedTexture = cyc; rp.intensity = BakeExposure;
            }
            else root.AddComponent<PoolRoomProbeRefresher>().probe = rp;
        }
    }

    public static void Build(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot, Mesh wallMesh)
    {
        if (gen.PoolFixed) { BuildBaked(gen, cfg, levelRoot); return; }
        float cs = cfg.cellSize, wh = cfg.wallHeight, t = gen.wallThickness;
        RectInt b = gen.PoolBlock;
        GameObject root = new GameObject("PoolRoom");
        root.transform.SetParent(levelRoot, false);
        Root = root;
        Vector3 ctr = new Vector3((b.x + b.width * 0.5f) * cs, 0f, (b.y + b.height * 0.5f) * cs);

        Material wallM = TileMat("WallTile", new Color(1.22f, 1.22f, 1.24f), 0.88f);
        Material deckM = TileMat("DeckTile", new Color(1.15f, 1.15f, 1.15f), 0.93f);
        Material poolM = TileMat("PoolTile", Color.white, 0.9f);
        Material chrome = ProceduralAssets.CreateMaterial(new Color(0.85f, 0.87f, 0.9f), null, Vector2.one, 0.92f);
        if (chrome.HasProperty("_Metallic")) chrome.SetFloat("_Metallic", 1f);

        // paredes del bloque (las genera el laberinto: mantienen sus huecos de paso)
        if (wallMesh != null)
        {
            GameObject wg = new GameObject("PoolRoomWalls");
            wg.transform.SetParent(root.transform, false);
            wg.AddComponent<MeshFilter>().sharedMesh = wallMesh;
            MeshRenderer wr = wg.AddComponent<MeshRenderer>();
            wr.sharedMaterial = wallM; wr.shadowCastingMode = ShadowCastingMode.Off;
            wg.AddComponent<MeshCollider>().sharedMesh = wallMesh;
        }

        // set de Blender escalado al interior del bloque
        GameObject prefab = Resources.Load<GameObject>("PoolRoom/PoolSetPiece");
        if (prefab != null)
        {
            GameObject sp = Object.Instantiate(prefab, root.transform, false);
            sp.name = "PoolSetPiece";
            sp.transform.localPosition = ctr + new Vector3(0f, BackroomsGenerator.PoolDeckY, 0f);
            sp.transform.localScale = new Vector3((b.width * cs - t) / 14f, 1f, (b.height * cs - t) / 10f);
            foreach (MeshRenderer r in sp.GetComponentsInChildren<MeshRenderer>(true))
            {
                string n = r.name;
                bool shell = n.StartsWith("PoolShell"), deck = n.StartsWith("Deck");
                Material mm = shell ? poolM : deck ? deckM : chrome;
                Material[] arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                for (int i = 0; i < arr.Length; i++) arr[i] = mm;
                r.sharedMaterials = arr;
                r.shadowCastingMode = ShadowCastingMode.Off;
                if (shell || deck)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) r.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                }
            }
        }
        else Debug.LogWarning("[PoolRoom] falta Resources/PoolRoom/PoolSetPiece");

        // tragaluces (techo) y ventanas (pared sur del bloque): emisivos + luz fría como en pool_1.png
        Material sky = ProceduralAssets.CreateMaterial(new Color(0.95f, 0.98f, 1f), null, Vector2.one, 0.1f);
        ProceduralAssets.SetEmission(sky, new Color(3.2f, 3.4f, 3.6f));
        float sx = (b.width * cs - t) / 14f, sz = (b.height * cs - t) / 10f;
        for (int k = -1; k <= 1; k += 2)
        {
            Vector3 p = ctr + new Vector3(k * 2.5f * sx, wh - 0.02f, 0f);
            Quad(root.transform, "Skylight", p, Quaternion.Euler(90f, 0f, 0f), new Vector3(2f * sx, 2f * sz, 1f), sky);
            Light l = new GameObject("SkylightLight").AddComponent<Light>();
            l.transform.SetParent(root.transform, false);
            l.transform.localPosition = p + Vector3.down * 0.4f;
            l.type = LightType.Point; l.range = 12f; l.intensity = 2.3f; l.color = new Color(0.86f, 0.95f, 1f);
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
        }
        // ventanas en la pared larga con más tramos cerrados (norte preferida), una por tramo sin hueco de paso
        int closedN = 0, closedS = 0;
        for (int i = b.x; i < b.xMax; i++) { if (!gen.IsCellOpen(i, b.yMax - 1, 0)) closedN++; if (!gen.IsCellOpen(i, b.y, 2)) closedS++; }
        bool north = closedN >= closedS;
        float wz = north ? b.yMax * cs - t * 0.5f - 0.012f : b.y * cs + t * 0.5f + 0.012f;
        Quaternion wrot = north ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
        // una ventana en cada tramo cerrado del perímetro (paredes largas y cortas)
        float y = 1.9f + BackroomsGenerator.PoolDeckY, off = t * 0.5f + 0.012f;
        for (int i = b.x; i < b.xMax; i++)
        {
            if (!gen.IsCellOpen(i, b.yMax - 1, 0)) Quad(root.transform, "Window", new Vector3((i + 0.5f) * cs, y, b.yMax * cs - off), Quaternion.Euler(0f, 180f, 0f), new Vector3(cs * 0.42f, 1.9f, 1f), sky);
            if (!gen.IsCellOpen(i, b.y, 2)) Quad(root.transform, "Window", new Vector3((i + 0.5f) * cs, y, b.y * cs + off), Quaternion.identity, new Vector3(cs * 0.42f, 1.9f, 1f), sky);
        }
        for (int j = b.y; j < b.yMax; j++)
        {
            if (!gen.IsCellOpen(b.xMax - 1, j, 1)) Quad(root.transform, "Window", new Vector3(b.xMax * cs - off, y, (j + 0.5f) * cs), Quaternion.Euler(0f, 90f, 0f), new Vector3(cs * 0.42f, 1.9f, 1f), sky);
            if (!gen.IsCellOpen(b.x, j, 3)) Quad(root.transform, "Window", new Vector3(b.x * cs + off, y, (j + 0.5f) * cs), Quaternion.Euler(0f, -90f, 0f), new Vector3(cs * 0.42f, 1.9f, 1f), sky);
        }
        Light wl = new GameObject("WindowLight").AddComponent<Light>();
        wl.transform.SetParent(root.transform, false);
        wl.transform.localPosition = new Vector3(ctr.x, 2.2f, wz + (north ? -0.6f : 0.6f));
        wl.transform.localRotation = Quaternion.Euler(28f, north ? 180f : 0f, 0f);
        wl.type = LightType.Spot; wl.spotAngle = 120f; wl.range = 15f; wl.intensity = 2.2f; wl.color = new Color(0.92f, 0.97f, 1f);
        wl.shadows = LightShadows.None; wl.renderMode = LightRenderMode.ForcePixel;

        Vector3 w0 = levelRoot.TransformPoint(new Vector3(b.x * cs, 0f, b.y * cs)), w1 = levelRoot.TransformPoint(new Vector3(b.xMax * cs, 0f, b.yMax * cs));
        Shader.SetGlobalVector("_PoolRoomRect", new Vector4(Mathf.Min(w0.x, w1.x), Mathf.Min(w0.z, w1.z), Mathf.Max(w0.x, w1.x), Mathf.Max(w0.z, w1.z)));

        // relleno suave (sustituye al GI horneado: el nivel es procedural y no se puede hornear en tiempo de ejecución)
        for (int k = 0; k < 2; k++)
        {
            Light f = new GameObject("PoolRoomFill").AddComponent<Light>();
            f.transform.SetParent(root.transform, false);
            f.transform.localPosition = ctr + new Vector3((k == 0 ? -1f : 1f) * b.width * cs * 0.3f, 0.9f, (k == 0 ? 1f : -1f) * b.height * cs * 0.25f);
            f.type = LightType.Point; f.range = 9f; f.intensity = 0.9f; f.color = new Color(0.85f, 0.95f, 1f);
            f.shadows = LightShadows.None; f.renderMode = LightRenderMode.ForcePixel;
        }
        PoolPlanarReflection pl = root.AddComponent<PoolPlanarReflection>();
        pl.waterY = levelRoot.TransformPoint(new Vector3(0f, cfg.waterLevel, 0f)).y;
        pl.center = levelRoot.TransformPoint(ctr);

        // sonda de reflexión de la sala (azulejos y agua brillantes); se refresca una vez al empezar
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            ReflectionProbe rp = new GameObject("PoolRoomProbe").AddComponent<ReflectionProbe>();
            rp.transform.SetParent(root.transform, false);
            rp.transform.localPosition = ctr + new Vector3(0f, 1.6f, 0f);
            rp.mode = ReflectionProbeMode.Realtime; rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            rp.resolution = 256; rp.hdr = true; rp.boxProjection = true; rp.importance = 2; rp.intensity = 1.4f;
            rp.size = new Vector3(b.width * cs, wh + 1.6f, b.height * cs);
            rp.cullingMask = ~((1 << WaterVolume.WaterLayer) | (1 << 2) | (1 << 5) | (1 << 8));
            rp.clearFlags = ReflectionProbeClearFlags.SolidColor; rp.backgroundColor = new Color(0.6f, 0.7f, 0.75f);
            root.AddComponent<PoolRoomProbeRefresher>().probe = rp;
        }
    }

    static void Quad(Transform parent, string n, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
    {
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = n;
        ProceduralAssets.SafeDestroy(q.GetComponent<Collider>());
        q.transform.SetParent(parent, false);
        q.transform.localPosition = pos; q.transform.localRotation = rot; q.transform.localScale = scale;
        MeshRenderer r = q.GetComponent<MeshRenderer>();
        r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
    }
}

public class PoolRoomProbeRefresher : MonoBehaviour
{
    public ReflectionProbe probe;
    private float t = 0.8f; private int left = 2;
    private void Update()
    {
        if (probe == null || left <= 0) return;
        t -= Time.unscaledDeltaTime;
        if (t > 0f) return;
        t = 3f; left--;
        try { probe.RenderProbe(); } catch { probe = null; }
    }
}

// Reflexión planar de la sala de piscina (técnica clásica de cámara espejo con plano de recorte oblicuo, como el Water de los
// Standard Assets / Optically-Realistic-Water (MIT)). Solo en Medio (256 px) y Alto (512 px), y solo cerca de la sala.
public class PoolPlanarReflection : MonoBehaviour
{
    public float waterY; public Vector3 center;
    private Camera rc; private RenderTexture rt; private int size;
    private static readonly int TexId = Shader.PropertyToID("_PoolPlanarTex"), OnId = Shader.PropertyToID("_PoolPlanarOn"), CkId = Shader.PropertyToID("_PoolCausticK");

    private void OnDisable() { Shader.SetGlobalFloat(OnId, 0f); PoolLook.Clear(); if (rt != null) { rt.Release(); Destroy(rt); rt = null; } if (rc != null) Destroy(rc.gameObject); }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        // v7.1.3: cáusticas del fondo enteras solo con la cámara bajo el agua
        Vector4 ck = PoolLook.CausticK; ck.z = cam != null && cam.transform.position.y < waterY ? 1f : 0f;
        Shader.SetGlobalVector(CkId, ck);
        int q = GameSettings.Quality;
        int want = q >= 2 ? 512 : q == 1 ? 256 : 0;
        if (cam == null || want == 0 || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null
            || (cam.transform.position - center).sqrMagnitude > 26f * 26f || cam.transform.position.y < waterY)
        { Shader.SetGlobalFloat(OnId, 0f); return; }
        if (rt == null || size != want)
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
            size = want; rt = new RenderTexture(want * 16 / 9, want, 16, RenderTextureFormat.DefaultHDR); rt.name = "PoolPlanar";
            rt.useMipMap = true; rt.autoGenerateMips = true;   // v7.1.3: mips para el reflejo borroso de la playa
        }
        if (rc == null)
        {
            GameObject g = new GameObject("PoolPlanarCam"); g.hideFlags = HideFlags.HideAndDontSave;
            rc = g.AddComponent<Camera>(); rc.enabled = false;
        }
        rc.CopyFrom(cam);
        rc.targetTexture = rt;
        rc.cullingMask = ~((1 << WaterVolume.WaterLayer) | (1 << 2) | (1 << 5) | (1 << 8) | (1 << 9));
        rc.depthTextureMode = DepthTextureMode.None;
        rc.useOcclusionCulling = false; rc.allowMSAA = false;
        Vector4 plane = new Vector4(0f, 1f, 0f, -waterY);
        Matrix4x4 refl = new Matrix4x4();
        refl.SetRow(0, new Vector4(1, 0, 0, 0)); refl.SetRow(1, new Vector4(0, -1, 0, 2f * waterY));
        refl.SetRow(2, new Vector4(0, 0, 1, 0)); refl.SetRow(3, new Vector4(0, 0, 0, 1));
        rc.worldToCameraMatrix = cam.worldToCameraMatrix * refl;
        Vector3 cp = rc.worldToCameraMatrix.MultiplyPoint(new Vector3(0f, waterY + 0.02f, 0f));
        Vector3 cn = rc.worldToCameraMatrix.MultiplyVector(Vector3.up).normalized;
        rc.projectionMatrix = cam.CalculateObliqueMatrix(new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn)));
        bool inv = GL.invertCulling; GL.invertCulling = !inv;
        try { rc.Render(); } finally { GL.invertCulling = inv; }
        Shader.SetGlobalTexture(TexId, rt); Shader.SetGlobalFloat(OnId, 1f);
    }
}

// v6.8: sondas de luz de la sala horneada. Unity no puede hornear sondas en un nivel procedural, así que se proyectan a SH
// las muestras direccionales que renderizó Cycles (6 puntos) y se aplican con LightProbeUsage.CustomProvided a los renderers
// del jugador, cuerpos remotos y monstruos que están dentro de la sala (fundido con el ambiente fuera de ella).
public class PoolRoomProbes : MonoBehaviour
{
    [System.Serializable] private class PJ { public PP[] probes; }
    [System.Serializable] private class PP { public float[] p; public float[] s; }
    public Vector3[] positions; public SphericalHarmonicsL2[] sh; public Bounds bounds;
    private readonly List<Renderer> targets = new List<Renderer>();
    private readonly Dictionary<Renderer, LightProbeUsage> orig = new Dictionary<Renderer, LightProbeUsage>();
    private MaterialPropertyBlock mpb; private float nextScan, nextApply;
    private readonly SphericalHarmonicsL2[] one = new SphericalHarmonicsL2[1];
    public int AppliedCount { get; private set; }

    public void Init(Transform levelRoot, Vector3 ctr, Bounds b) { Init(levelRoot, ctr, b, "PoolRoom/PoolRoomProbes", PoolRoomSetPiece.BakeExposure); }

    // v7.1.0: también para las salas del parque de bolas (otro recurso y exposición)
    public void Init(Transform levelRoot, Vector3 ctr, Bounds b, string resource, float exposure)
    {
        bounds = b;
        TextAsset ta = Resources.Load<TextAsset>(resource);
        if (ta == null) { positions = new Vector3[0]; sh = new SphericalHarmonicsL2[0]; return; }
        PJ pj = JsonUtility.FromJson<PJ>(ta.text);
        positions = new Vector3[pj.probes.Length]; sh = new SphericalHarmonicsL2[pj.probes.Length];
        for (int i = 0; i < pj.probes.Length; i++)
        {
            PP p = pj.probes[i];
            positions[i] = levelRoot.TransformPoint(ctr + new Vector3(p.p[0], p.p[1], p.p[2]));
            SphericalHarmonicsL2 s = new SphericalHarmonicsL2();
            for (int k = 0; k + 5 < p.s.Length; k += 6)
            {
                Vector3 d = levelRoot.TransformDirection(new Vector3(p.s[k], p.s[k + 1], p.s[k + 2]));
                Color c = new Color(p.s[k + 3], p.s[k + 4], p.s[k + 5]) * exposure;
                float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                if (m <= 0f) continue;
                s.AddDirectionalLight(-d, c / m, m);
            }
            sh[i] = s;
        }
    }

    public SphericalHarmonicsL2 Sample(Vector3 p)
    {
        SphericalHarmonicsL2 acc = new SphericalHarmonicsL2(); float wsum = 0f;
        for (int i = 0; i < positions.Length; i++)
        {
            float w = 1f / (0.25f + (positions[i] - p).sqrMagnitude);
            w *= w; acc += sh[i] * w; wsum += w;
        }
        return wsum > 0f ? acc * (1f / wsum) : acc;
    }

    // 0 fuera de la sala, 1 a más de 1 m dentro (fundido en las puertas)
    public float Inside(Vector3 p)
    {
        Vector3 d = p - bounds.center, e = bounds.extents;
        float m = Mathf.Min(e.x - Mathf.Abs(d.x), e.z - Mathf.Abs(d.z));
        return Mathf.Clamp01(m + 0.3f);
    }

    private void Scan()
    {
        targets.Clear();
        foreach (MonsterAI m in FindObjectsOfType<MonsterAI>()) targets.AddRange(m.GetComponentsInChildren<Renderer>(true));
        foreach (PlayerBody pb in FindObjectsOfType<PlayerBody>()) targets.AddRange(pb.GetComponentsInChildren<Renderer>(true));
        foreach (RemotePlayer rp in FindObjectsOfType<RemotePlayer>()) targets.AddRange(rp.GetComponentsInChildren<Renderer>(true));
    }

    private void Update()
    {
        if (sh == null || sh.Length == 0) return;
        if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1f; Scan(); }
        if (Time.unscaledTime < nextApply) return;
        nextApply = Time.unscaledTime + 0.1f;
        if (mpb == null) mpb = new MaterialPropertyBlock();
        int applied = 0;
        SphericalHarmonicsL2 amb = RenderSettings.ambientProbe;
        foreach (Renderer r in targets)
        {
            if (r == null || r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            Vector3 c = r.bounds.center;
            float w = Inside(c);
            if (w <= 0f && !orig.ContainsKey(r)) continue;
            if (!orig.ContainsKey(r)) orig[r] = r.lightProbeUsage;
            if (w <= 0f) { r.lightProbeUsage = orig[r]; orig.Remove(r); continue; }
            one[0] = amb * (1f - w) + Sample(c) * w;
            r.lightProbeUsage = LightProbeUsage.CustomProvided;
            r.GetPropertyBlock(mpb); mpb.CopySHCoefficientArraysFrom(one); r.SetPropertyBlock(mpb);
            applied++;
        }
        AppliedCount = applied;
    }

    private void OnDestroy()
    {
        foreach (var kv in orig) if (kv.Key != null) kv.Key.lightProbeUsage = kv.Value;
        orig.Clear();
    }
}

// v6.8.1: salida de la piscina de la sala fija. El vaso tiene 1,4 m de profundidad y la playa queda a 0,28 m sobre el pasillo.
public static class PoolExit
{
    public const float RescueSeconds = 4f;
    public static bool Active { get { return owner != null; } }
    public static Rect PoolXZ { get; private set; }
    public static float DeckY { get; private set; }
    public static bool HasLadder { get; private set; }
    public static Vector3 LadderPos { get; private set; }     // al pie de la escalera, dentro del agua
    public static Vector3 LadderOut { get; private set; }     // dirección hacia la playa
    public static Vector3 LadderTop { get; private set; }     // punto de la playa donde se acaba
    private static GameObject owner;

    public static void Set(Rect xz, float deckY, bool hasLadder, Bounds ladder, GameObject o)
    {
        PoolXZ = xz; DeckY = deckY; owner = o; HasLadder = hasLadder;
        Shader.SetGlobalVector("_PoolWaterRect", new Vector4(xz.xMin, xz.yMin, xz.xMax, xz.yMax));
        Shader.SetGlobalFloat("_PoolWaterY", deckY - 0.06f);
        Shader.SetGlobalFloat("_PoolCausticsUp", 1f);
        if (hasLadder)
        {
            Vector3 c = ladder.center;
            Vector3 n; Vector3 edge = NearestEdge(c, out n);
            LadderOut = n;
            LadderPos = new Vector3(edge.x, deckY - 1.4f, edge.z) - n * 0.45f;
            LadderTop = new Vector3(edge.x, deckY + 0.03f, edge.z) + n * 0.7f;
        }
    }

    // punto del borde más cercano y normal hacia fuera (XZ)
    public static Vector3 NearestEdge(Vector3 p, out Vector3 outDir)
    {
        Rect r = PoolXZ;
        float x = Mathf.Clamp(p.x, r.xMin, r.xMax), z = Mathf.Clamp(p.z, r.yMin, r.yMax);
        float dl = x - r.xMin, dr = r.xMax - x, db = z - r.yMin, dt = r.yMax - z;
        float m = Mathf.Min(Mathf.Min(dl, dr), Mathf.Min(db, dt));
        if (m == dl) { outDir = Vector3.left; return new Vector3(r.xMin, p.y, z); }
        if (m == dr) { outDir = Vector3.right; return new Vector3(r.xMax, p.y, z); }
        if (m == db) { outDir = Vector3.back; return new Vector3(x, p.y, r.yMin); }
        outDir = Vector3.forward; return new Vector3(x, p.y, r.yMax);
    }

    // ¿pies dentro del vaso (por debajo de la playa)?
    public static bool InPool(Vector3 feet)
    {
        if (owner == null) return false;
        Rect r = PoolXZ;
        return feet.y < DeckY - 0.35f && feet.x > r.xMin - 0.05f && feet.x < r.xMax + 0.05f && feet.z > r.yMin - 0.05f && feet.z < r.yMax + 0.05f;
    }

    public static bool NearLadder(Vector3 feet)
    {
        if (!HasLadder || !InPool(feet)) return false;
        Vector3 d = feet - LadderPos; d.y = 0f;
        return d.magnitude < 1.0f;
    }

    // punto seguro en la playa frente al borde más cercano (rescate)
    public static Vector3 RescuePoint(Vector3 feet)
    {
        Vector3 n; Vector3 e = NearestEdge(feet, out n);
        return new Vector3(e.x, DeckY + 0.03f, e.z) + n * 0.7f;
    }
}
