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

        Material wallM = TileMat("WallTile", Color.white, 0.85f);
        Material deckM = TileMat("DeckTile", Color.white, 0.9f);
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
        ProceduralAssets.SetEmission(sky, new Color(2.2f, 2.4f, 2.6f));
        float sx = (b.width * cs - t) / 14f, sz = (b.height * cs - t) / 10f;
        for (int k = -1; k <= 1; k += 2)
        {
            Vector3 p = ctr + new Vector3(k * 2.5f * sx, wh - 0.02f, 0f);
            Quad(root.transform, "Skylight", p, Quaternion.Euler(90f, 0f, 0f), new Vector3(2f * sx, 2f * sz, 1f), sky);
            Light l = new GameObject("SkylightLight").AddComponent<Light>();
            l.transform.SetParent(root.transform, false);
            l.transform.localPosition = p + Vector3.down * 0.4f;
            l.type = LightType.Point; l.range = 11f; l.intensity = 1.7f; l.color = new Color(0.86f, 0.95f, 1f);
            l.shadows = LightShadows.None; l.renderMode = LightRenderMode.ForcePixel;
        }
        float wz = b.y * cs + t * 0.5f + 0.012f;
        for (int k = -1; k <= 1; k++)
        {
            Vector3 p = new Vector3(ctr.x + k * 3.0f * sx, 1.0f + 0.9f + BackroomsGenerator.PoolDeckY, wz);
            Quad(root.transform, "Window", p, Quaternion.identity, new Vector3(1.2f * sx, 1.9f, 1f), sky);
        }
        Light wl = new GameObject("WindowLight").AddComponent<Light>();
        wl.transform.SetParent(root.transform, false);
        wl.transform.localPosition = new Vector3(ctr.x, 2.2f, wz + 0.6f);
        wl.transform.localRotation = Quaternion.Euler(28f, 0f, 0f);
        wl.type = LightType.Spot; wl.spotAngle = 120f; wl.range = 14f; wl.intensity = 1.6f; wl.color = new Color(0.92f, 0.97f, 1f);
        wl.shadows = LightShadows.None; wl.renderMode = LightRenderMode.ForcePixel;

        Vector3 w0 = levelRoot.TransformPoint(new Vector3(b.x * cs, 0f, b.y * cs)), w1 = levelRoot.TransformPoint(new Vector3(b.xMax * cs, 0f, b.yMax * cs));
        Shader.SetGlobalVector("_PoolRoomRect", new Vector4(Mathf.Min(w0.x, w1.x), Mathf.Min(w0.z, w1.z), Mathf.Max(w0.x, w1.x), Mathf.Max(w0.z, w1.z)));

        // sonda de reflexión de la sala (azulejos y agua brillantes); se refresca una vez al empezar
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
        {
            ReflectionProbe rp = new GameObject("PoolRoomProbe").AddComponent<ReflectionProbe>();
            rp.transform.SetParent(root.transform, false);
            rp.transform.localPosition = ctr + new Vector3(0f, 1.6f, 0f);
            rp.mode = ReflectionProbeMode.Realtime; rp.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            rp.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            rp.resolution = 128; rp.hdr = true; rp.boxProjection = true; rp.importance = 2;
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
