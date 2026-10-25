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
    // v7.9.0 (usuario): el viento borra las pisadas en unos segundos; las rodadas, más hondas, aguantan algo más
    public static float FootLife = 10f, TyreLife = 45f;   // s hasta desaparecer del todo
    public static float Life { get { return TyreLife; } }
    public const float Stride = 0.72f;

    private class Pool
    {
        public Mesh mesh; public Vector3[] v; public Vector3[] n; public Vector4[] tg; public Vector2[] uv; public Color32[] col; public float[] born; public byte[] a0;
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
        Texture2D ft = FootTex(), tt = TreadTex(), fh = FootHeightTex(), th = TreadHeightTex(); t.owned.Add(ft); t.owned.Add(tt); t.owned.Add(fh); t.owned.Add(th);
        t.foot = t.MakePool("Footprints", FootMax, sh, ft, fh, new Color(0.70f, 0.47f, 0.24f));   // arena pisada (mismo tono que la duna, algo más oscura): algo más oscura (compactada)
        t.tyre = t.MakePool("TyreTracks", TyreMax, sh, tt, th, new Color(0.70f, 0.48f, 0.25f));
        t.BuildPuff();
        Active = t; return t;
    }
    private void OnDestroy() { if (Active == this) Active = null; foreach (Object o in owned) ProceduralAssets.SafeDestroy(o); }

    private Pool MakePool(string name, int max, Shader sh, Texture2D tex, Texture2D height, Color c)
    {
        Pool p = new Pool { max = max };
        p.v = new Vector3[max * 4]; p.n = new Vector3[max * 4]; p.tg = new Vector4[max * 4]; p.uv = new Vector2[max * 4]; p.col = new Color32[max * 4]; p.born = new float[max]; p.a0 = new byte[max];
        int[] tri = new int[max * 6];
        for (int i = 0; i < max; i++) { int b = i * 4; tri[i * 6] = b; tri[i * 6 + 1] = b + 1; tri[i * 6 + 2] = b + 2; tri[i * 6 + 3] = b; tri[i * 6 + 4] = b + 2; tri[i * 6 + 5] = b + 3; p.born[i] = -1e6f; }
        for (int i = 0; i < p.n.Length; i++) p.n[i] = Vector3.up;
        p.mesh = new Mesh { name = name }; p.mesh.MarkDynamic(); owned.Add(p.mesh);
        p.mesh.vertices = p.v; p.mesh.normals = p.n; p.mesh.tangents = p.tg; p.mesh.uv = p.uv; p.mesh.colors32 = p.col; p.mesh.triangles = tri;
        p.mesh.bounds = new Bounds(new Vector3(dw.L * 0.5f, 0f, dw.L * 0.5f), new Vector3(dw.L + 20f, 400f, dw.L + 20f));
        GameObject g = new GameObject(name); g.transform.SetParent(transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = p.mesh;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        Material m = sh != null ? new Material(sh) : ProceduralAssets.CreateMaterial(c, tex, Vector2.one, 0f); owned.Add(m);
        m.mainTexture = tex; m.color = c; if (m.HasProperty("_HeightTex")) m.SetTexture("_HeightTex", height); mr.sharedMaterial = m;
        mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return p;
    }
    private int Alive(Pool p) { if (p == null) return 0; int n = 0; float now = Time.time, life = p == foot ? FootLife : TyreLife; for (int i = 0; i < p.max; i++) if (now - p.born[i] + ageBoost < life && p.born[i] > -1e5f) n++; return n; }

    // fundido: 0 recién hecha → 1 desaparecida: nítida el primer tercio y luego se la lleva el viento
    public static float Fade01(float age, float life) { float x = Mathf.Clamp01(age / life); return x <= 0.3f ? 0f : Mathf.SmoothStep(0f, 1f, (x - 0.3f) / 0.7f); }
    public static float Fade01(float age) { return Fade01(age, TyreLife); }
    public static float Erosion01(float age, float life) { return Mathf.Clamp01(age / (life * 0.75f)); }
    public float MeanErosionFoot() { int n = 0; float s = 0f; for (int i = 0; i < foot.max; i++) if (foot.born[i] > -1e5f) { n++; s += foot.col[i * 4].g; } return n > 0 ? s / n / 255f : 0f; }

    private void Quad(Pool p, Vector3 c, Vector3 fwd, Vector3 n, float halfW, float halfL, float v0, float v1, byte a, byte depth = 200, bool mirror = false)
    {
        int i = p.next; p.next = (p.next + 1) % p.max; p.used = Mathf.Min(p.max, p.used + 1);
        Vector3 r = Vector3.Cross(n, fwd).normalized; fwd = Vector3.Cross(r, n).normalized;
        c += n * 0.015f;
        int b = i * 4;
        p.v[b] = c - r * halfW - fwd * halfL; p.v[b + 1] = c - r * halfW + fwd * halfL; p.v[b + 2] = c + r * halfW + fwd * halfL; p.v[b + 3] = c + r * halfW - fwd * halfL;
        p.uv[b] = new Vector2(0f, v0); p.uv[b + 1] = new Vector2(0f, v1); p.uv[b + 2] = new Vector2(1f, v1); p.uv[b + 3] = new Vector2(1f, v0);
        if (mirror) for (int k = 0; k < 4; k++) p.uv[b + k].x = 1f - p.uv[b + k].x;
        Vector4 tg = new Vector4(r.x, r.y, r.z, 1f) * (mirror ? -1f : 1f); tg.w = 1f;   // tangente = sentido de u (espejado en el pie izquierdo)
        for (int k = 0; k < 4; k++) { p.n[b + k] = n; p.tg[b + k] = tg; p.col[b + k] = new Color32(depth, 0, 255, a); }
        p.born[i] = Time.time + ageBoost; p.a0[i] = a;   // v7.9.0: antes «- ageBoost»: tras una tormenta (o AgeAll) las marcas nuevas nacían ya viejas y desaparecían al instante
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

    // v7.9.0: pisada de bota (izquierda/derecha), orientada con la marcha y algo abierta hacia fuera; al correr más grande y
    // honda (y la punta arrastra arena), agachado más pequeña y somera; levanta un poco de polvo
    public float LastFootScale { get; private set; }
    public float LastFootDepth { get; private set; }
    public int PuffEmitted { get; private set; }
    public void AddFoot(Vector3 local, Vector3 dirLocal, bool left, int gait = 1)
    {
        dirLocal.y = 0f; if (dirLocal.sqrMagnitude < 1e-4f) return;
        dirLocal.Normalize();
        float sc = gait == 2 ? 1.10f : gait == 0 ? 0.90f : 1f, depth = gait == 2 ? 1f : gait == 0 ? 0.55f : 0.78f;
        Vector3 side = Vector3.Cross(Vector3.up, dirLocal) * (left ? -0.11f : 0.11f) * (gait == 0 ? 1.1f : 1f);
        if (!SandAt(local + side, out Vector3 pt, out Vector3 n)) return;
        Vector3 dir = Quaternion.AngleAxis(left ? -6f : 6f, Vector3.up) * dirLocal;   // las punteras un poco hacia fuera
        Quad(foot, pt + dir * (gait == 2 ? 0.03f : 0f), dir, n, 0.085f * sc, 0.175f * sc * (gait == 2 ? 1.06f : 1f), 0f, 1f, (byte)(gait == 0 ? 190 : 230), (byte)Mathf.RoundToInt(255f * depth), left);
        LastFootScale = sc; LastFootDepth = depth;
        FootAdded++;
        Puff(lroot.TransformPoint(pt + dir * 0.06f), gait);
    }

    // ---- polvo al pisar (un solo sistema de partículas compartido, con tope)
    private ParticleSystem puff;
    public const int PuffMax = 240;
    public ParticleSystem PuffSystem { get { return puff; } }
    private void BuildPuff()
    {
        Texture2D tex = DesertCars.PuffTex;
        Shader sh = Resources.Load<Shader>(WaterVolume.ParticleShaderResource); if (sh == null) sh = Shader.Find("Sprites/Default");
        if (sh == null) return;
        Material m = new Material(sh) { mainTexture = tex }; owned.Add(m);
        GameObject g = new GameObject("StepPuff"); g.transform.SetParent(transform, false);
        ParticleSystem ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.playOnAwake = false; main.maxParticles = PuffMax; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.55f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.10f, 0.26f); main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = new Color(0.83f, 0.69f, 0.51f, 0.45f); main.gravityModifier = 0.12f;
        var em = ps.emission; em.rateOverTime = 0f;
        var shp = ps.shape; shp.enabled = true; shp.shapeType = ParticleSystemShapeType.Hemisphere; shp.radius = 0.07f;
        var col = ps.colorOverLifetime; col.enabled = true;
        Gradient gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0f, 1f) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.7f));
        ParticleSystemRenderer pr = g.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = m; pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false;
        ps.Play(); puff = ps;
    }
    private void Puff(Vector3 world, int gait)
    {
        if (puff == null) return;
        Camera cam = Camera.main;
        if (cam != null && (cam.transform.position - world).sqrMagnitude > 60f * 60f) return;   // lejos no se ve: no gastar partículas
        ParticleSystem.EmitParams ep = new ParticleSystem.EmitParams { position = world + Vector3.up * 0.03f, applyShapeToPosition = true };
        int n = gait == 2 ? 7 : gait == 0 ? 2 : 4;
        puff.Emit(ep, n); PuffEmitted += n;
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
    public static float StrideFor(int gait) { return gait == 2 ? 1.0f : gait == 0 ? 0.52f : Stride; }
    private readonly Dictionary<int, Walker> walkers = new Dictionary<int, Walker>();
    private FirstPersonController fpc;
    private void Step(int key, Vector3 world, bool grounded, int gait = 1)
    {
        if (!walkers.TryGetValue(key, out Walker w)) { w = new Walker(); walkers[key] = w; }
        Vector3 lp = lroot.InverseTransformPoint(world);
        if (!grounded || !w.has) { w.last = lp; w.has = grounded; w.acc = 0f; return; }
        Vector3 d = lp - w.last; d.y = 0f; float m = d.magnitude;
        if (m > 4f) { w.last = lp; return; }   // teletransporte / vuelta del desierto
        if (m < 0.05f) return;
        w.acc += m;
        float st = StrideFor(gait);
        if (w.acc >= st) { w.acc = Mathf.Min(w.acc - st, st); w.left = !w.left; AddFoot(lp, d, w.left, gait); }
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
            Step(-1, fpc.transform.position, g, fpc.IsCrouching ? 0 : fpc.IsSprinting && fpc.FlatSpeed > 4.2f ? 2 : 1);
        }
        NetSession ns = NetSession.Instance;
        if (ns != null && NetSession.Active)
            foreach (NetPlayer p in ns.Players)
            {
                if (p.isLocal || p.avatar == null) continue;
                Vector3 wp = p.avatar.transform.position, lp = lroot.InverseTransformPoint(wp);
                float h = DesertHeight.H(Mathf.Repeat(lp.x, dw.L), Mathf.Repeat(lp.z, dw.L));
                bool g = Mathf.Abs(lp.y - h) < 0.3f && !DesertCars.IsSeated(p.id);
                Step(p.id, wp, g, p.avatar.Crouching ? 0 : p.avatar.Sprinting && p.avatar.FlatSpeed > 4.2f ? 2 : 1);
            }
        // geometría nueva
        if (foot.geoDirty) Upload(foot);
        if (tyre.geoDirty) Upload(tyre);
        // fundido (cada medio segundo)
        fadeT -= dt;
        if (fadeT <= 0f) { fadeT = 0.2f; Fade(foot); Fade(tyre); }
    }
    private void Upload(Pool p) { p.mesh.vertices = p.v; p.mesh.normals = p.n; p.mesh.tangents = p.tg; p.mesh.uv = p.uv; p.mesh.colors32 = p.col; p.geoDirty = false; p.colDirty = false; }
    private void Fade(Pool p)
    {
        float now = Time.time, life = p == foot ? FootLife : TyreLife; bool any = false;
        for (int i = 0; i < p.max; i++)
        {
            if (p.born[i] < -1e5f) continue;
            float age = now - p.born[i] - 0f + ageBoost;
            byte a = (byte)Mathf.RoundToInt(p.a0[i] * (1f - Fade01(age, life)));
            byte er = (byte)Mathf.RoundToInt(255f * Erosion01(age, life));   // el viento ablanda los bordes antes de borrarla
            int b = i * 4; if (p.col[b].a == a && p.col[b].g == er) continue;
            for (int k = 0; k < 4; k++) { p.col[b + k].a = a; p.col[b + k].g = er; }
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
    // v7.9.0: bota con forma de suela (talón, puente estrecho, planta ancha y puntera), tacos, talón más hondo que la punta y
    // reborde de arena apartada alrededor. Alturas en FootHeight (0,5 = arena plana); el sombreado lo da la luz con el relieve.
    public const int FootW = 48, FootH = 96;
    // forma: distancia normalizada al borde de la suela (<1 dentro) en coordenadas del quad (u -1..1, v 0 talón → 1 punta)
    static float SoleD(float u, float v)
    {
        float vr = (v - 0.08f) / 0.84f;                          // la suela ocupa el 84 % central (el resto es reborde)
        float ve = vr < 0f ? -vr * 9f : vr > 1f ? (vr - 1f) * 9f : 0f;   // fuera por los extremos: distancia redondeada (sin bandas rectas)
        float vs = Mathf.Clamp01(vr);
        float half = vs < 0.28f ? Mathf.Lerp(0.52f, 0.60f, vs / 0.28f)                  // talón
                   : vs < 0.48f ? Mathf.Lerp(0.60f, 0.47f, (vs - 0.28f) / 0.2f) * 1f      // puente
                   : vs < 0.82f ? Mathf.Lerp(0.47f, 0.74f, Mathf.Sin((vs - 0.48f) / 0.34f * 1.5708f))   // planta
                   : 0.74f * Mathf.Sqrt(Mathf.Max(0.02f, 1f - Mathf.Pow((vs - 0.82f) / 0.18f, 2f)));      // puntera redonda
        float cx = 0.06f * Mathf.Sin(vs * 3.1f) + (vs > 0.5f ? 0.05f * (vs - 0.5f) : 0f);          // curva del pie (hacia el dedo gordo)
        float ends = vs < 0.07f ? Mathf.Sqrt(Mathf.Max(0.02f, 1f - Mathf.Pow(1f - vs / 0.07f, 2f))) : 1f;           // talón redondeado
        float lr = Mathf.Abs(u - cx) / Mathf.Max(0.02f, half * ends);
        if (ve <= 0f) return lr;
        return Mathf.Sqrt(0.5f * lr * lr + (1f + ve) * (1f + ve));
    }
    public static float FootHeight(float u, float v)
    {
        float d = SoleD(u, v), vs = Mathf.Clamp01((v - 0.08f) / 0.84f);
        float inside = Mathf.Clamp01((1f - d) * 5f);
        float depth = Mathf.Lerp(0.42f, 0.27f, vs) * (vs > 0.3f && vs < 0.46f ? 0.82f : 1f);          // talón más hondo que la punta
        float lug = Mathf.Repeat(vs * 13f + Mathf.Abs(u) * 0.8f, 1f) < 0.42f ? 0.06f : 0f;          // tacos (crestas en el hueco)
        float rim = Mathf.Exp(-Mathf.Pow((d - 1.18f) / 0.22f, 2f)) * 0.16f * (0.8f + 0.4f * vs);   // arena empujada fuera, más en la puntera
        return Mathf.Clamp01(0.5f - inside * (depth - lug * inside) + rim);
    }
    private static Texture2D FootHeightTex() { return HeightTex("FootprintHeight", FootW, FootH, FootHeight, TextureWrapMode.Clamp); }
    private static Texture2D HeightTex(string name, int W, int H, System.Func<float, float, float> f, TextureWrapMode wrap)
    {
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, true, true) { wrapMode = wrap, name = name };
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++) { float h = f((x + 0.5f) / W * 2f - 1f, (y + 0.5f) / H); t.SetPixel(x, y, new Color(h, h, h, 1f)); }
        t.Apply(true); return t;
    }
    private static Texture2D FootTex()
    {
        Texture2D t = new Texture2D(FootW, FootH, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "Footprint" };
        for (int y = 0; y < FootH; y++)
            for (int x = 0; x < FootW; x++)
            {
                float u = (x + 0.5f) / FootW * 2f - 1f, v = (y + 0.5f) / FootH;
                float d = SoleD(u, v), h = FootHeight(u, v);
                float inside = Mathf.Clamp01((1f - d) * 5f);
                float shade = Mathf.Lerp(1.0f, 0.74f, inside) + Mathf.Max(0f, h - 0.5f) * 0.5f;   // fondo compactado algo más oscuro, reborde suelto más claro
                float a = Mathf.Clamp01(1.6f - Mathf.Max(0f, d - 1f) * 3.2f) * Mathf.Clamp01((1.75f - d) * 2f);   // mezcla suave hasta fuera del reborde
                t.SetPixel(x, y, new Color(shade, shade * 0.98f, shade * 0.95f, a));
            }
        t.Apply(true); return t;
    }
    public static float TreadHeight(float u, float v)
    {
        float au = Mathf.Abs(u);
        float edge = Mathf.Clamp01((0.92f - au) * 6f);
        float vv = Mathf.Repeat(v + au * 0.35f, 1f);
        float lug = vv < 0.5f ? 0.08f : 0f;
        float rim = Mathf.Exp(-Mathf.Pow((au - 1.0f) / 0.12f, 2f)) * 0.14f;
        return Mathf.Clamp01(0.5f - edge * (0.32f - lug) + rim);
    }
    private static Texture2D TreadHeightTex() { return HeightTex("TyreTreadHeight", 32, 32, TreadHeight, TextureWrapMode.Repeat); }
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
                float shade = Mathf.Lerp(1.08f + rim * 0.5f, lug * 0.85f, edge);   // surco en sombra con los bordes de arena empujada más claros
                float a = Mathf.Clamp01(edge * 0.9f + rim * 0.6f);
                t.SetPixel(x, y, new Color(shade, shade * 0.98f, shade * 0.95f, a));
            }
        t.Apply(true); return t;
    }
}
