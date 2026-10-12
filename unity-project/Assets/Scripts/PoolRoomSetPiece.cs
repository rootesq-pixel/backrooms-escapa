using UnityEngine;
using UnityEngine.Rendering;

// v6.7.1: sala de piscina de Blender (PoolSetPiece.fbx: vaso, playa y escalera cromada) dentro de un bloque abierto de las Poolrooms.
// Paredes del bloque con azulejo blanco de Blender, tragaluces y ventanas emisivas con luz fría, sonda de reflexión propia.
// Colisión: MeshCollider en vaso y playa (el agua usa raycast al suelo, así que se nada en el vaso y se vadea fuera).
public static class PoolRoomSetPiece
{
    public static GameObject Root { get; private set; }

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

    public static void Build(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot, Mesh wallMesh)
    {
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
    private static readonly int TexId = Shader.PropertyToID("_PoolPlanarTex"), OnId = Shader.PropertyToID("_PoolPlanarOn");

    private void OnDisable() { Shader.SetGlobalFloat(OnId, 0f); if (rt != null) { rt.Release(); Destroy(rt); rt = null; } if (rc != null) Destroy(rc.gameObject); }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        int q = GameSettings.Quality;
        int want = q >= 2 ? 512 : q == 1 ? 256 : 0;
        if (cam == null || want == 0 || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null
            || (cam.transform.position - center).sqrMagnitude > 26f * 26f || cam.transform.position.y < waterY)
        { Shader.SetGlobalFloat(OnId, 0f); return; }
        if (rt == null || size != want)
        {
            if (rt != null) { rt.Release(); Destroy(rt); }
            size = want; rt = new RenderTexture(want * 16 / 9, want, 16, RenderTextureFormat.DefaultHDR); rt.name = "PoolPlanar";
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
