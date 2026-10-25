using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.8.0: huellas de pies y rodadas del todoterreno en la arena. Dos mallas (una por tipo) con un número fijo de quads
// reciclados (el más viejo se reutiliza); cada marca se desvanece en unos minutos (más deprisa con tormenta de arena) y
// sigue la forma de la duna (rayo al suelo). Cada jugador las crea en local para todos (su propio cuerpo, los avatares
// remotos y los coches que ve), así que en multijugador se ven las de todos sin tráfico de red extra.
public class DesertTracks : MonoBehaviour
{
    public static DesertTracks Active;
    public const int FootMax = 800, TyreMax = 1600;
    public static float Life = 210f;           // s hasta desaparecer del todo (3,5 min)
    public const float Stride = 0.72f;

    private class Pool
    {
        public Mesh mesh; public Vector3[] v; public Vector3[] n; public Vector2[] uv; public Color32[] col; public float[] born; public byte[] a0;
        public int max, next, used; public bool geoDirty, colDirty;
    }
    private Pool foot, tyre;
    private readonly List<Object> owned = new List<Object>();
    private Transform lroot; private DesertWorld dw;
    public int FootAdded { get; private set; }
    public int TyreAdded { get; private set; }
    public int FootAlive { get { return Alive(foot); } }
    public int TyreAlive { get { return Alive(tyre); } }
    private float fadeT;
    private float ageBoost;   // segundos de «edad» extra acumulados por la tormenta

    public static DesertTracks Create(DesertWorld dw)
    {
        GameObject go = new GameObject("DesertTracks"); go.transform.SetParent(dw.levelRoot, false);
        DesertTracks t = go.AddComponent<DesertTracks>(); t.dw = dw; t.lroot = dw.levelRoot;
        Shader sh = Shader.Find("Backrooms/SandDecal");
        if (sh == null) sh = Resources.Load<Shader>("Desert/Shaders/SandDecal");
        Texture2D ft = FootTex(), tt = TreadTex(); t.owned.Add(ft); t.owned.Add(tt);
        t.foot = t.MakePool("Footprints", FootMax, sh, ft, new Color(0.64f, 0.50f, 0.36f));   // arena pisada: más oscura (hundida, en sombra)
        t.tyre = t.MakePool("TyreTracks", TyreMax, sh, tt, new Color(0.64f, 0.50f, 0.36f));
        Active = t; return t;
    }
    private void OnDestroy() { if (Active == this) Active = null; foreach (Object o in owned) ProceduralAssets.SafeDestroy(o); }

    private Pool MakePool(string name, int max, Shader sh, Texture2D tex, Color c)
    {
        Pool p = new Pool { max = max };
        p.v = new Vector3[max * 4]; p.n = new Vector3[max * 4]; p.uv = new Vector2[max * 4]; p.col = new Color32[max * 4]; p.born = new float[max]; p.a0 = new byte[max];
        int[] tri = new int[max * 6];
        for (int i = 0; i < max; i++) { int b = i * 4; tri[i * 6] = b; tri[i * 6 + 1] = b + 1; tri[i * 6 + 2] = b + 2; tri[i * 6 + 3] = b; tri[i * 6 + 4] = b + 2; tri[i * 6 + 5] = b + 3; p.born[i] = -1e6f; }
        for (int i = 0; i < p.n.Length; i++) p.n[i] = Vector3.up;
        p.mesh = new Mesh { name = name }; p.mesh.MarkDynamic(); owned.Add(p.mesh);
        p.mesh.vertices = p.v; p.mesh.normals = p.n; p.mesh.uv = p.uv; p.mesh.colors32 = p.col; p.mesh.triangles = tri;
        p.mesh.bounds = new Bounds(new Vector3(dw.L * 0.5f, 0f, dw.L * 0.5f), new Vector3(dw.L + 20f, 400f, dw.L + 20f));
        GameObject g = new GameObject(name); g.transform.SetParent(transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = p.mesh;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        Material m = sh != null ? new Material(sh) : ProceduralAssets.CreateMaterial(c, tex, Vector2.one, 0f); owned.Add(m);
        m.mainTexture = tex; m.color = c; mr.sharedMaterial = m;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return p;
    }
    private int Alive(Pool p) { if (p == null) return 0; int n = 0; float now = Time.time; for (int i = 0; i < p.max; i++) if (now - p.born[i] + ageBoost < Life && p.born[i] > -1e5f) n++; return n; }

    // fundido: 0 recién hecha → 1 desaparecida (empieza a notarse a los 40 s)
    public static float Fade01(float age) { return Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(age / Life), 1.4f)); }

    private void Quad(Pool p, Vector3 c, Vector3 fwd, Vector3 n, float halfW, float halfL, float v0, float v1, byte a)
    {
        int i = p.next; p.next = (p.next + 1) % p.max; p.used = Mathf.Min(p.max, p.used + 1);
        Vector3 r = Vector3.Cross(n, fwd).normalized; fwd = Vector3.Cross(r, n).normalized;
        c += n * 0.015f;
        int b = i * 4;
        p.v[b] = c - r * halfW - fwd * halfL; p.v[b + 1] = c - r * halfW + fwd * halfL; p.v[b + 2] = c + r * halfW + fwd * halfL; p.v[b + 3] = c + r * halfW - fwd * halfL;
        p.uv[b] = new Vector2(0f, v0); p.uv[b + 1] = new Vector2(0f, v1); p.uv[b + 2] = new Vector2(1f, v1); p.uv[b + 3] = new Vector2(1f, v0);
        for (int k = 0; k < 4; k++) { p.n[b + k] = n; p.col[b + k] = new Color32(255, 255, 255, a); }
        p.born[i] = Time.time - ageBoost; p.a0[i] = a;
        p.geoDirty = true;
    }

    // suelo de arena bajo p (local del nivel): sí solo si es duna (no suelos de casa, rocas ni el coche)
    private static readonly RaycastHit[] hits = new RaycastHit[8];
    public bool SandAt(Vector3 local, out Vector3 point, out Vector3 normal)
    {
        point = local; normal = Vector3.up;
        float x = Mathf.Repeat(local.x, dw.L), z = Mathf.Repeat(local.z, dw.L);
        if (dw.plan.QuickAt(new Vector2(x, z), 0.3f) >= 0) return false;   // la arena movediza no guarda huellas
        float h = DesertHeight.H(x, z);
        Vector3 top = lroot.TransformPoint(new Vector3(local.x, h + 1.2f, local.z));
        int cnt = Physics.RaycastNonAlloc(top, -lroot.up, hits, 2.6f, ~(1 << 2), QueryTriggerInteraction.Ignore);
        float best = 99f; bool sand = false;
        for (int i = 0; i < cnt; i++)
        {
            Collider c = hits[i].collider; if (c == null || c is CharacterController || c.attachedRigidbody != null) continue;
            if (hits[i].distance < best) { best = hits[i].distance; sand = c.name.StartsWith("Dune_"); point = lroot.InverseTransformPoint(hits[i].point); normal = lroot.InverseTransformDirection(hits[i].normal); }
        }
        if (best > 50f) { point = new Vector3(local.x, h, local.z); normal = DesertHeight.Normal(x, z); sand = true; }   // borde del tramo
        return sand;
    }

    public void AddFoot(Vector3 local, Vector3 dirLocal, bool left)
    {
        dirLocal.y = 0f; if (dirLocal.sqrMagnitude < 1e-4f) return;
        dirLocal.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, dirLocal) * (left ? -0.11f : 0.11f);
        if (!SandAt(local + side, out Vector3 pt, out Vector3 n)) return;
        Quad(foot, pt, dirLocal, n, 0.065f, 0.15f, 0f, 1f, 215);
        // el pie izquierdo usa la textura espejada (u invertida)
        if (left) { int b = ((foot.next - 1 + foot.max) % foot.max) * 4; for (int k = 0; k < 4; k++) foot.uv[b + k].x = 1f - foot.uv[b + k].x; }
        FootAdded++;
    }
    private float tyreV;
    public void AddTyre(Vector3 fromLocal, Vector3 toLocal, float width, float strength)
    {
        Vector3 mid = (fromLocal + toLocal) * 0.5f, d = toLocal - fromLocal; float len = d.magnitude; if (len < 0.05f) return;
        if (!SandAt(mid, out Vector3 pt, out Vector3 n)) return;
        float v0 = tyreV, v1 = tyreV + len / 0.42f; tyreV = Mathf.Repeat(v1, 64f);
        Quad(tyre, pt, d / len, n, width * 0.5f, len * 0.5f + 0.03f, v0, v1, (byte)Mathf.Clamp(Mathf.RoundToInt(200f * strength), 40, 255));
        TyreAdded++;
    }

    // ---- caminantes: el jugador local y los avatares remotos (cada uno pisa al andar sobre la arena)
    private class Walker { public Vector3 last; public float acc; public bool left; public bool has; }
    private readonly Dictionary<int, Walker> walkers = new Dictionary<int, Walker>();
    private FirstPersonController fpc;
    private void Step(int key, Vector3 world, bool grounded)
    {
        if (!walkers.TryGetValue(key, out Walker w)) { w = new Walker(); walkers[key] = w; }
        Vector3 lp = lroot.InverseTransformPoint(world);
        if (!grounded || !w.has) { w.last = lp; w.has = grounded; w.acc = 0f; return; }
        Vector3 d = lp - w.last; d.y = 0f; float m = d.magnitude;
        if (m > 4f) { w.last = lp; return; }   // teletransporte / vuelta del desierto
        if (m < 0.05f) return;
        w.acc += m;
        if (w.acc >= Stride) { w.acc -= Stride; w.left = !w.left; AddFoot(lp, d, w.left); }
        w.last = lp;
    }

    private void Update()
    {
        if (lroot == null || dw == null) return;
        if (fpc == null) fpc = FindObjectOfType<FirstPersonController>();
        DesertSky sky = DesertSky.Active;
        float dt = Time.deltaTime;
        if (sky != null && sky.Storm > 0.05f) ageBoost += dt * 5f * sky.Storm;   // la tormenta borra las huellas
        if (fpc != null)
        {
            bool busy = DesertFx.Active != null && DesertFx.Active.Busy;
            bool g = fpc.IsGrounded && !fpc.IsMounted && !busy && !fpc.IsSwimming;
            Step(-1, fpc.transform.position, g);
        }
        NetSession ns = NetSession.Instance;
        if (ns != null && NetSession.Active)
            foreach (NetPlayer p in ns.Players)
            {
                if (p.isLocal || p.avatar == null) continue;
                Vector3 wp = p.avatar.transform.position, lp = lroot.InverseTransformPoint(wp);
                float h = DesertHeight.H(Mathf.Repeat(lp.x, dw.L), Mathf.Repeat(lp.z, dw.L));
                bool g = Mathf.Abs(lp.y - h) < 0.3f && !DesertCars.IsSeated(p.id);
                Step(p.id, wp, g);
            }
        // geometría nueva
        if (foot.geoDirty) Upload(foot);
        if (tyre.geoDirty) Upload(tyre);
        // fundido (cada medio segundo)
        fadeT -= dt;
        if (fadeT <= 0f) { fadeT = 0.5f; Fade(foot); Fade(tyre); }
    }
    private void Upload(Pool p) { p.mesh.vertices = p.v; p.mesh.normals = p.n; p.mesh.uv = p.uv; p.mesh.colors32 = p.col; p.geoDirty = false; p.colDirty = false; }
    private void Fade(Pool p)
    {
        float now = Time.time; bool any = false;
        for (int i = 0; i < p.max; i++)
        {
            if (p.born[i] < -1e5f) continue;
            float age = now - p.born[i] - 0f + ageBoost;
            byte a = (byte)Mathf.RoundToInt(p.a0[i] * (1f - Fade01(age)));
            int b = i * 4; if (p.col[b].a == a) continue;
            for (int k = 0; k < 4; k++) p.col[b + k].a = a;
            any = true;
            if (a == 0) p.born[i] = -1e6f;
        }
        if (any) p.mesh.colors32 = p.col;
    }
    // pruebas: envejecer todas las marcas
    public void AgeAll(float seconds) { ageBoost += seconds; Fade(foot); Fade(tyre); }
    public float MeanAlphaFoot() { return MeanA(foot); }
    public float MeanAlphaTyre() { return MeanA(tyre); }
    private float MeanA(Pool p) { int n = 0; float s = 0f; for (int i = 0; i < p.max; i++) if (p.born[i] > -1e5f) { n++; s += p.col[i * 4].a; } return n > 0 ? s / n / 255f : 0f; }

    // ---- texturas procedimentales
    // bota: suela con tacos (zonas hundidas más oscuras, borde algo más claro: arena apartada)
    private static Texture2D FootTex()
    {
        const int W = 32, H = 64;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Footprint" };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H;   // v: 0 talón → 1 punta
                float wv = v < 0.32f ? 0.62f : 0.62f + (v - 0.32f) * 0.55f + (v > 0.8f ? -(v - 0.8f) * 2.4f : 0f);
                float cx = 0.08f * Mathf.Sin(v * 3.1f);
                float d = Mathf.Abs(u - cx) / Mathf.Max(0.05f, wv);
                float endD = v < 0.06f ? (0.06f - v) / 0.06f : v > 0.95f ? (v - 0.95f) / 0.05f : 0f;
                float arch = v > 0.32f && v < 0.42f ? 0.35f : 0f;
                float inside = Mathf.Clamp01((1f - Mathf.Max(d, endD)) * 6f);
                float rim = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Max(d, endD) - 1.05f) * 7f) * 0.6f;
                float lug = (Mathf.Repeat(v * 11f, 1f) < 0.45f) ? 0.82f : 1f;
                float shade = Mathf.Lerp(1.08f, 0.66f * lug + arch * 0.2f, inside);
                float a = Mathf.Clamp01(inside * 0.9f + rim * 0.5f);
                t.SetPixel(x, y, new Color(shade, shade * 0.98f, shade * 0.95f, a));
            }
        t.Apply(true); return t;
    }
    // neumático de taco: bandas en V oscuras, bordes suaves, se repite a lo largo
    private static Texture2D TreadTex()
    {
        const int W = 32, H = 32;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, name = "TyreTread" };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H;
                float edge = Mathf.Clamp01((1f - Mathf.Abs(u)) * 5f);
                float vv = Mathf.Repeat(v + Mathf.Abs(u) * 0.35f, 1f);
                float lug = vv < 0.5f ? 0.62f : 0.82f;
                float rim = Mathf.Clamp01(1f - Mathf.Abs(Mathf.Abs(u) - 0.97f) * 12f);
                float shade = Mathf.Lerp(1.08f, lug, edge);
                float a = Mathf.Clamp01(edge * 0.85f + rim * 0.4f);
                t.SetPixel(x, y, new Color(shade, shade * 0.98f, shade * 0.95f, a));
            }
        t.Apply(true); return t;
    }
}
