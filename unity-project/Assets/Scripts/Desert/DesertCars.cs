using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.8.0: los todoterrenos del Nivel 114: dónde están (semilla aparte: no cambia el plano del desierto), materiales y sonidos
// compartidos, interacción del jugador local (E subir/bajar, mandos del conductor), red (peticiones por OceanReq y eventos
// por OceanEvent: el desierto no tiene mar; bloque «coches» en el snapshot) y el aviso en pantalla.
public class DesertCars : MonoBehaviour
{
    public static DesertCars Active;
    public readonly List<DesertCar> cars = new List<DesertCar>();
    public DesertCar wreck;
    public DesertTracks tracks;
    private DesertWorld dw;
    private FirstPersonController fpc;

    public const int OpEnter = 21, OpLeave = 22, OpDrive = 23, OpLights = 24;
    public const int EvSeat = 21, EvSink = 22, EvRespawn = 23, EvFlip = 24;
    public const float EnterRange = 2.3f;
    public static bool Authority { get { return OceanNet.Authority; } }
    public static int LocalId { get { return OceanNet.LocalId; } }
    // pruebas
    public static bool DebugInteract, DebugDrive, DbgHandbrake, DbgLights, ForceSink;
    public static float DbgThrottle, DbgSteer;
    public int ReqsHandled { get; private set; }
    public int EventsApplied { get; private set; }

    public static AudioClip EngineLo, EngineHi, TyreClip;
    public static Texture2D PuffTex;
    public string Prompt { get; private set; }

    // ------------------------------------------------------------------ construcción
    public static DesertCars Build(DesertWorld dw, System.Action<Object> track)
    {
        GameObject go = new GameObject("DesertCars"); go.transform.SetParent(dw.transform, false);
        DesertCars m = go.AddComponent<DesertCars>(); m.dw = dw; Active = m;
        if (EngineLo == null) EngineLo = DesertSynth.Clip("CarEngineLo", DesertSynth.Engine(7301, 1000f, false));
        if (EngineHi == null) EngineHi = DesertSynth.Clip("CarEngineHi", DesertSynth.Engine(7302, 2800f, true));
        if (TyreClip == null) TyreClip = DesertSynth.Clip("CarTyresSand", DesertSynth.Tyres(7303));
        if (PuffTex == null) PuffTex = Puff(64, 11);
        Dictionary<string, Material> mats = Materials(track);
        Spots(dw, out Vector3 a, out float ya, out Vector3 b, out float yb);
        DesertCar car = DesertCar.Create(dw, dw.transform, 0, a, ya, false, mats, track);
        m.cars.Add(car);
        DesertCar wr = DesertCar.Create(dw, dw.transform, 1, b, yb, true, mats, track);
        // la chatarra: medio enterrada e inclinada (se puede subir al techo y al capó)
        wr.transform.localPosition += Vector3.down * 0.6f;
        wr.transform.localRotation *= Quaternion.Euler(5f, 0f, -10f);
        m.wreck = wr;
        // dunas pequeñas contra las ruedas del que se conduce y la chatarra hundida en una duna
        // el que se conduce: la arena amontonada solo contra el costado del copiloto (barlovento), así al salir no deja un cráter
        m.Drift(a, ya, 0.62f, 0f, track, -car.driverSide);
        m.Drift(b, yb, 0.60f, 0.22f, track, 0f);
        m.tracks = DesertTracks.Create(dw);
        return m;
    }
    private void OnDestroy() { if (Active == this) Active = null; }

    public static void Spots(DesertWorld dw, out Vector3 car, out float carYaw, out Vector3 wreck, out float wreckYaw)
    {
        DesertPlan p = dw.plan;
        System.Random r = new System.Random((dw.gen != null ? dw.gen.UsedSeed : 1) * 577 + 13);
        List<int> order = new List<int>(); for (int i = 0; i < p.villages.Count; i++) order.Add(i);
        order.Sort((x, y) => p.Dist(p.villages[x].c, p.start).CompareTo(p.Dist(p.villages[y].c, p.start)));
        car = Find(dw, r, p.villages[order[0]], true, out carYaw);
        wreck = Find(dw, r, p.villages[order.Count > 1 ? order[1] : order[0]], false, out wreckYaw);
    }
    private static Vector3 Find(DesertWorld dw, System.Random r, DesertPlan.Village v, bool towardStart, out float yaw)
    {
        DesertPlan p = dw.plan; float L = dw.L;
        Vector2 dir = towardStart ? new Vector2(DesertHeight.Wrap(p.start.x - v.c.x), DesertHeight.Wrap(p.start.y - v.c.y)).normalized : Vector2.zero;
        if (dir.sqrMagnitude < 0.5f) { float a0 = (float)r.NextDouble() * 6.283f; dir = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)); }
        for (int k = 0; k < 200; k++)
        {
            float a = Mathf.Atan2(dir.y, dir.x) + (k == 0 ? 0f : ((float)r.NextDouble() - 0.5f) * Mathf.Min(6.28f, 0.6f + k * 0.04f));
            float rad = v.r + 8f + (float)r.NextDouble() * Mathf.Min(14f, 2f + k * 0.15f);
            Vector2 q = new Vector2(Mathf.Repeat(v.c.x + Mathf.Cos(a) * rad, L), Mathf.Repeat(v.c.y + Mathf.Sin(a) * rad, L));
            float y0 = Mathf.Atan2(-Mathf.Sin(a), Mathf.Cos(a)) * Mathf.Rad2Deg + ((float)r.NextDouble() - 0.5f) * 40f;   // aparcado de lado al borde de la aldea
            if (!p.FreeOf(q, 4f, 12f, true)) continue;
            bool flat = true;
            for (int s = -1; s <= 1 && flat; s++)
                for (int t = -1; t <= 1 && flat; t++)
                {
                    Vector2 o = Rot(new Vector2(s * 1.2f, t * 2.6f), y0);
                    float x = Mathf.Repeat(q.x + o.x, L), z = Mathf.Repeat(q.y + o.y, L);
                    if (DesertHeight.Normal(x, z).y < (k < 120 ? 0.975f : 0.94f)) flat = false;
                }
            if (!flat) continue;
            yaw = y0;
            return new Vector3(q.x, DesertHeight.H(q.x, q.y) + 0.04f, q.y);
        }
        Vector2 f = new Vector2(Mathf.Repeat(v.c.x + v.r + 10f, L), v.c.y);
        yaw = 0f; return new Vector3(f.x, DesertHeight.H(f.x, f.y) + 0.04f, f.y);
    }
    private static Vector2 Rot(Vector2 v, float yawDeg) { float a = -yawDeg * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a); return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c); }

    private static Material M(Color c, Texture2D t, float smooth, float metal, System.Action<Object> track)
    {
        Material m = ProceduralAssets.CreateMaterial(c, t, Vector2.one, smooth); track(m);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return m;
    }
    // v7.8.0 (realismo): carrocería, rueda e interior llevan el desgaste horneado en un atlas cada uno (polvo, óxido, desconchones);
    // cada ranura del modelo usa su atlas con su propio brillo/metal. Claves: "<atlas>/<ranura>" (atlas: Body, Wheel, Interior, Wreck).
    public static readonly string[] Slots = { "Paint", "Black", "Metal", "Chrome", "Rubber", "Rim", "Cloth", "Amber", "Plate", "Orange", "Canvas", "Jerry" };
    private static readonly float[] SlotSmooth = { 0.40f, 0.18f, 0.45f, 0.80f, 0.06f, 0.30f, 0.03f, 0.70f, 0.40f, 0.35f, 0.04f, 0.35f };
    private static readonly float[] SlotMetal = { 0.05f, 0f, 0.6f, 0.9f, 0f, 0.45f, 0f, 0f, 0f, 0f, 0f, 0f };
    private static Dictionary<string, Material> Materials(System.Action<Object> track)
    {
        Texture2D paint = Resources.Load<Texture2D>("Desert/Car/DCar_paint");
        Texture2D wheel = Resources.Load<Texture2D>("Desert/Car/DCar_wheel");
        Texture2D inter = Resources.Load<Texture2D>("Desert/Car/DCar_interior");
        Dictionary<string, Material> d = new Dictionary<string, Material>();
        foreach (var at in new[] { ("Body", paint, Color.white, 1f), ("Wreck", paint, new Color(0.92f, 0.70f, 0.55f), 0.3f), ("Wheel", wheel, Color.white, 1f), ("Interior", inter, Color.white, 1f) })
            for (int i = 0; i < Slots.Length; i++)
            {
                Material m = M(at.Item3, at.Item2, SlotSmooth[i] * at.Item4, SlotMetal[i] * at.Item4, track); m.name = "Car_" + at.Item1 + "_" + Slots[i];
                d[at.Item1 + "/" + Slots[i]] = m;
            }
        d["Black"] = M(new Color(0.03f, 0.03f, 0.03f), null, 0.25f, 0f, track);   // volante (sin atlas)
        d["Chrome"] = M(new Color(0.62f, 0.62f, 0.60f), null, 0.8f, 0.9f, track);
        d["Lamp"] = M(new Color(0.92f, 0.92f, 0.88f), null, 0.9f, 0f, track); ProceduralAssets.SetEmission(d["Lamp"], new Color(0.001f, 0.001f, 0.001f));
        d["Tail"] = M(new Color(0.55f, 0.05f, 0.03f), null, 0.8f, 0f, track); ProceduralAssets.SetEmission(d["Tail"], new Color(0.001f, 0f, 0f));
        Material g = ProceduralAssets.CreateWaterMaterial(null, new Color(0.30f, 0.35f, 0.37f, 0.30f)); track(g); g.name = "Car_Glass";
        if (g.HasProperty("_Glossiness")) g.SetFloat("_Glossiness", 0.97f);
        g.renderQueue = 3010; d["Glass"] = g;
        return d;
    }
    private static Texture2D Puff(int n, int seed)
    {
        Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, name = "CarDust" };
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f, d = Mathf.Sqrt(dx * dx + dy * dy);
                float nz = Mathf.PerlinNoise(seed + x * 0.08f, y * 0.08f) * 0.6f + Mathf.PerlinNoise(seed + 7 + x * 0.2f, y * 0.2f) * 0.4f;
                float fall = Mathf.Clamp01(1f - d); fall = fall * fall * (3f - 2f * fall);   // borde suave, centro denso
                t.SetPixel(x, y, new Color(1f, 1f, 1f, fall * Mathf.Clamp01(0.45f + nz * 0.9f)));
            }
        t.Apply(true); return t;
    }

    // duna pequeña alrededor del contorno del coche (sin colisión: solo se ve); h = altura del borde, fill = relleno del centro
    private void Drift(Vector3 c, float yaw, float h, float fill, System.Action<Object> track, float side)
    {
        const int RINGS = 22, SEG = 56; float L = dw.L;
        List<Vector3> v = new List<Vector3>(); List<int> tri = new List<int>();
        for (int i = 0; i <= RINGS; i++)
        {
            float rr = i / (float)RINGS * 2.1f;   // 1 = contorno del coche
            for (int k = 0; k < SEG; k++)
            {
                float a = k / (float)SEG * Mathf.PI * 2f;
                Vector2 o = Rot(new Vector2(Mathf.Cos(a) * 1.05f, Mathf.Sin(a) * 2.45f) * rr, yaw);
                float noise = Mathf.PerlinNoise(c.x * 0.1f + Mathf.Cos(a) * 1.3f, c.z * 0.1f + Mathf.Sin(a) * 1.3f);
                float ring = h * Mathf.Exp(-Mathf.Pow((rr - 1.05f) / 0.38f, 2f)) * (0.6f + 0.8f * noise);
                if (side != 0f) ring *= Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.15f, 0.75f, side * Mathf.Cos(a))) * (0.75f + 0.25f * Mathf.Abs(Mathf.Cos(a)));
                float centre = rr < 1f ? fill * (1f - rr * rr) : 0f;
                // arena acumulada contra cada rueda (las ruedas quedan algo metidas en la arena)
                float lx = Mathf.Cos(a) * 1.05f * rr, lz = Mathf.Sin(a) * 2.45f * rr, wheels = 0f;
                for (int wq = 0; wq < 4; wq++)
                {
                    float wx = (wq & 1) == 0 ? -0.95f : 0.95f, wz = (wq & 2) == 0 ? -1.40f : 1.40f;
                    float dd = ((lx - wx) * (lx - wx) * 1.6f + (lz - wz) * (lz - wz)) / (0.46f * 0.46f);
                    wheels = Mathf.Max(wheels, WheelSand * Mathf.Exp(-dd) * (0.8f + 0.4f * noise));
                }
                float bump = Mathf.Max(Mathf.Max(ring, centre), wheels) * Mathf.Clamp01((2.1f - rr) / 0.6f);
                float x = c.x + o.x, z = c.z + o.y;
                v.Add(new Vector3(x, DesertHeight.H(Mathf.Repeat(x, L), Mathf.Repeat(z, L)) + bump - 0.035f, z));
            }
        }
        for (int i = 0; i < RINGS; i++)
            for (int k = 0; k < SEG; k++)
            {
                int a = i * SEG + k, b = i * SEG + (k + 1) % SEG, cc = (i + 1) * SEG + k, d = (i + 1) * SEG + (k + 1) % SEG;
                if (i > 0) { tri.Add(a); tri.Add(cc); tri.Add(b); }   // el anillo 0 es el centro (un punto): ahí solo el triángulo del abanico
                tri.Add(b); tri.Add(cc); tri.Add(d);
            }
        Mesh m = new Mesh { name = "SandDrift" }; m.SetVertices(v); m.SetTriangles(tri, 0);
        Vector2[] uv = new Vector2[v.Count]; for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(v[i].x, v[i].z) * 0.25f; m.uv = uv;
        m.RecalculateNormals();
        // los triángulos deben mirar hacia arriba
        Vector3[] nn = m.normals; float up = 0f; foreach (Vector3 q in nn) up += q.y;
        if (up < 0f) { for (int i = 0; i < tri.Count; i += 3) { int t = tri[i + 1]; tri[i + 1] = tri[i + 2]; tri[i + 2] = t; } m.SetTriangles(tri, 0); m.RecalculateNormals(); }
        m.RecalculateTangents(); m.RecalculateBounds(); track(m);
        GameObject g = new GameObject("SandDrift"); g.transform.SetParent(dw.transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = dw.SandMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = true;
        DriftCount++;
    }
    public int DriftCount { get; private set; }
    public const float WheelSand = 0.24f;

    // ------------------------------------------------------------------ consultas
    public static bool IsSeated(int id) { if (Active == null) return false; foreach (DesertCar c in Active.cars) if (c.SeatOf(id) >= 0) return true; return false; }
    public static bool LocalInCar { get { return Active != null && Active.LocalCar(out int _) != null && Active.fpc != null && Active.fpc.IsMounted; } }
    public DesertCar LocalCar(out int seatIdx)
    {
        int me = LocalId; seatIdx = -1;
        foreach (DesertCar c in cars) { int s = c.SeatOf(me); if (s >= 0) { seatIdx = s; return c; } }
        return null;
    }
    public DesertCar Drivable { get { return cars.Count > 0 ? cars[0] : null; } }

    // ------------------------------------------------------------------ red
    public static void Request(int op, int a, float x, float y, Vector3 p)
    {
        if (Active == null) return;
        if (Authority) { Active.HostRequest(LocalId, op, a, x, y, p, Vector3.zero); return; }
        if (NetSession.Instance != null) NetSession.Instance.SendOceanReq(op, a, x, y, p, Vector3.zero);
    }
    public static void Event(int ev, int a, int b, Vector3 p)
    {
        if (NetSession.Active && NetSession.IsHost) NetSession.Instance.BroadcastOceanEvent(ev, a, b, p, Vector3.zero);
        if (Active != null) Active.ApplyEvent(ev, a, b, p, Vector3.zero);
    }
    public void HostRequest(int id, int op, int a, float x, float y, Vector3 p, Vector3 d)
    {
        if (a < 0 || a >= cars.Count) return;
        DesertCar c = cars[a]; ReqsHandled++;
        switch (op)
        {
            case OpEnter:
                if (IsSeated(id)) return;
                if (c.HostEnter(id, p, out int s)) Event(EvSeat, id, a * 8 + s, Vector3.zero);
                break;
            case OpLeave:
                if (c.HostLeave(id)) Event(EvSeat, id, -1 - a, Vector3.zero);
                break;
            case OpDrive:
                c.HostDrive(id, x, y, p.x > 0.5f);
                break;
            case OpLights:
                if (c.DriverId == id) c.lights = x > 0.5f;
                break;
        }
    }
    public void ApplyEvent(int ev, int a, int b, Vector3 p, Vector3 d)
    {
        EventsApplied++;
        switch (ev)
        {
            case EvSeat:
            {
                if (b >= 0)
                {
                    int ci = b / 8, s = b % 8; if (ci >= cars.Count || s >= DesertCar.Seats) return;
                    foreach (DesertCar c in cars) for (int i = 0; i < DesertCar.Seats; i++) if (c.seat[i] == a) c.seat[i] = -1;
                    cars[ci].seat[s] = a;
                    if (a != LocalId && NetSession.Active && GameManager.Instance != null) GameManager.Instance.ShowToast(PlayerName(a) + (s == 0 ? " conduce el todoterreno" : " se sube al todoterreno"));
                }
                else
                {
                    int ci = -1 - b; if (ci < 0 || ci >= cars.Count) return;
                    for (int i = 0; i < DesertCar.Seats; i++) if (cars[ci].seat[i] == a) cars[ci].seat[i] = -1;
                }
                break;
            }
            case EvSink: if (a < cars.Count) { if (cars[a].SeatOf(LocalId) >= 0 || Near(cars[a], 40f)) Toast0("¡El todoterreno se hunde en arenas movedizas!"); } break;
            case EvRespawn: if (a < cars.Count) { if (!Authority) cars[a].Respawn(); Toast0("El desierto ha devuelto el todoterreno a la aldea"); } break;
            case EvFlip: if (a < cars.Count && (cars[a].SeatOf(LocalId) >= 0 || Near(cars[a], 30f))) Toast0("El todoterreno vuelve a ponerse sobre las ruedas"); break;
        }
    }
    private bool Near(DesertCar c, float r) { return fpc != null && Vector3.Distance(fpc.transform.position, c.transform.position) < r; }
    private static void Toast0(string s) { if (GameManager.Instance != null) GameManager.Instance.ShowToast(s); }
    public static void Toast(DesertCar c, string s) { if (Active != null) Event(EvFlip, c.index, 0, Vector3.zero); }
    private static string PlayerName(int id)
    {
        NetSession ns = NetSession.Instance; if (ns != null) foreach (NetPlayer p in ns.Players) if (p.id == id) return p.name;
        return "Alguien";
    }

    public static void HostStartSink(DesertCar c)
    {
        if (c.sinking || c.hidden) return;
        c.sinking = true; c.sinkDepth = 0f; ForceSink = false;
        Event(EvSink, c.index, 0, Vector3.zero);
    }
    public static void HostEjectAll(DesertCar c)
    {
        for (int i = 0; i < DesertCar.Seats; i++) { int id = c.seat[i]; if (id >= 0 && c.HostLeave(id)) Event(EvSeat, id, -1 - c.index, Vector3.zero); }
    }
    public static void HostRespawn(DesertCar c, bool eject)
    {
        if (eject) HostEjectAll(c);
        c.Respawn(); Respawns++;
        Event(EvRespawn, c.index, 0, Vector3.zero);
    }
    public static int Respawns;

    // snapshot: bloque «coches» (detrás del reloj del desierto)
    public void WriteNet(NetWriter w)
    {
        Transform lr = dw.levelRoot;
        w.Byte(88).Byte(cars.Count);
        foreach (DesertCar c in cars)
        {
            Vector3 lp = lr.InverseTransformPoint(c.transform.position); Quaternion q = Quaternion.Inverse(lr.rotation) * c.transform.rotation;
            Vector3 v = c.rb != null && !c.rb.isKinematic ? c.rb.velocity : Vector3.zero;
            w.Vec3(lp).Float(q.x).Float(q.y).Float(q.z).Float(q.w).Vec3(lr.InverseTransformVector(v)).Float(c.steerAngle).Float(c.throttle).Byte(c.NetFlags).Float(c.sinkDepth);
            for (int i = 0; i < DesertCar.Seats; i++) w.Int(c.seat[i]);
        }
    }
    public int NetReads { get; private set; }
    public void ReadNet(NetReader r)
    {
        if (r.End || r.Byte() != 88) return;
        int n = r.Byte();
        for (int k = 0; k < n; k++)
        {
            Vector3 lp = r.Vec3(); Quaternion q = new Quaternion(r.Float(), r.Float(), r.Float(), r.Float()); Vector3 v = r.Vec3();
            float st = r.Float(), th = r.Float(); int fl = r.Byte(); float sk = r.Float();
            int[] seats = new int[DesertCar.Seats]; for (int i = 0; i < seats.Length; i++) seats[i] = r.Int();
            if (Authority || k >= cars.Count) continue;
            DesertCar c = cars[k];
            c.ApplyNet(lp, q, dw.levelRoot.TransformVector(v), st, th, fl, sk);
            for (int i = 0; i < DesertCar.Seats; i++) c.seat[i] = seats[i];
        }
        NetReads++;
    }

    // ------------------------------------------------------------------ jugador local
    private float sendT, cleanT; private float lastTh, lastSt; private bool lastHb;
    private int lastSeatIdx = -1; private DesertCar lastCar;
    private void Update()
    {
        if (fpc == null) fpc = GameManager.Instance != null && GameManager.Instance.player != null ? GameManager.Instance.player : FindObjectOfType<FirstPersonController>();
        if (fpc == null) return;
        Prompt = null;
        bool blocked = UIManager.InputBlocked;
        bool eDown = DebugInteract || (!blocked && Input.GetKeyDown(KeyCode.E)); DebugInteract = false;
        bool downed = NetSession.Instance != null && NetSession.Instance.LocalDowned;
        bool revive = NetSession.Active && NetSession.Instance.LocalReviveCandidate();
        SyncLocalSeat();
        DesertCar mine = LocalCar(out int seatIdx);
        if (mine != null)
        {
            if (seatIdx == 0)
            {
                float th = blocked ? 0f : Input.GetAxisRaw("Vertical"), st = blocked ? 0f : Input.GetAxisRaw("Horizontal");
                bool hb = !blocked && Input.GetKey(KeyCode.Space);
                if (DebugDrive) { th = DbgThrottle; st = DbgSteer; hb = DbgHandbrake; }
                if ((!blocked && Input.GetKeyDown(KeyCode.L)) || (DbgLights != mine.lights && DebugDrive)) Request(OpLights, mine.index, mine.lights ? 0f : 1f, 0f, Vector3.zero);
                LocalDrive(mine, th, st, hb);
                Prompt = "W/S acelerar · A/D girar · Espacio freno de mano · L luces · E bajar";
            }
            else Prompt = "E: bajar del todoterreno";
            if (eDown && !revive) Request(OpLeave, mine.index, 0f, 0f, Vector3.zero);
        }
        else if (!fpc.IsMounted && !downed && !(DesertFx.Active != null && DesertFx.Active.Busy))
        {
            DesertCar best = null; float bd = 99f; int bs = -1;
            foreach (DesertCar c in cars)
            {
                if (c.hidden || c.sinking) continue;
                for (int i = 0; i < DesertCar.Seats; i++)
                {
                    if (c.seat[i] >= 0) continue;
                    Vector3 dp = c.transform.TransformPoint(c.DoorLocal(i)) - fpc.transform.position; dp.y = 0f;
                    if (dp.magnitude < bd) { bd = dp.magnitude; best = c; bs = i; }
                }
            }
            if (best != null && bd < EnterRange)
            {
                Prompt = bs == 0 ? "E: conducir el todoterreno" : "E: subir al todoterreno";
                if (eDown && !revive) Request(OpEnter, best.index, 0f, 0f, fpc.transform.position);
            }
        }
        // anfitrión: plazas de jugadores que ya no están
        if (Authority && NetSession.Active)
        {
            cleanT -= Time.deltaTime;
            if (cleanT <= 0f)
            {
                cleanT = 1f;
                foreach (DesertCar c in cars)
                    for (int i = 0; i < DesertCar.Seats; i++)
                    {
                        int id = c.seat[i]; if (id < 0) continue;
                        bool there = false; foreach (NetPlayer p in NetSession.Instance.Players) if (p.id == id) there = true;
                        if (!there && c.HostLeave(id)) Event(EvSeat, id, -1 - c.index, Vector3.zero);
                    }
            }
        }
    }
    private void LocalDrive(DesertCar c, float th, float st, bool hb)
    {
        if (Authority) { c.HostDrive(LocalId, th, st, hb); return; }
        sendT -= Time.deltaTime;
        if (sendT <= 0f || Mathf.Abs(th - lastTh) > 0.4f || Mathf.Abs(st - lastSt) > 0.4f || hb != lastHb)
        {
            sendT = 0.066f; lastTh = th; lastSt = st; lastHb = hb;
            Request(OpDrive, c.index, th, st, new Vector3(hb ? 1f : 0f, 0f, 0f));
        }
    }
    // la plaza que dice la autoridad → montar / desmontar al jugador local
    private void SyncLocalSeat()
    {
        DesertCar c = LocalCar(out int s);
        if (c != null)
        {
            if (fpc.mount != c.transform && !fpc.IsMounted)
            {
                fpc.Mount(c.transform, c.SeatLocal(s)); fpc.mountYaw = true;
                fpc.transform.rotation = Quaternion.Euler(0f, c.transform.eulerAngles.y, 0f);
                if (GameManager.Instance != null) GameManager.Instance.ShowToast(s == 0 ? "Al volante: W/S · A/D · Espacio freno de mano · L luces · E bajar" : "En el todoterreno · E bajar");
                Mounts++;
            }
            else if (fpc.mount == c.transform && s != lastSeatIdx) fpc.mountLocal = c.SeatLocal(s);
            lastCar = c; lastSeatIdx = s;
        }
        else if (lastCar != null)
        {
            if (fpc.mount == lastCar.transform) { fpc.Dismount(lastCar.DismountSpot(Mathf.Max(0, lastSeatIdx))); fpc.mountYaw = false; Dismounts++; }
            lastCar = null; lastSeatIdx = -1;
        }
    }
    public int Mounts { get; private set; }
    public int Dismounts { get; private set; }

    // ------------------------------------------------------------------ aviso y velocímetro
    private GUIStyle style, big;
    private void OnGUI()
    {
        if (UnityEngine.Event.current.type != EventType.Repaint) return;
        if (UIManager.InputBlocked || (GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing)) return;
        DesertCar mine = LocalCar(out int s);
        if (Prompt == null && mine == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            big = new GUIStyle(style) { alignment = TextAnchor.MiddleRight };
        }
        style.fontSize = Mathf.Max(12, Screen.height / 40); big.fontSize = Mathf.Max(16, Screen.height / 22);
        if (Prompt != null) Shadowed(new Rect(0, Screen.height * 0.78f, Screen.width, Screen.height * 0.06f), Prompt, style);
        if (mine != null && s == 0)
            Shadowed(new Rect(Screen.width * 0.6f, Screen.height * 0.86f, Screen.width * 0.37f, Screen.height * 0.08f), Mathf.RoundToInt(Mathf.Abs(mine.ForwardSpeed) * 3.6f) + " km/h" + (mine.lights ? "  ◐" : ""), big);
    }
    private static void Shadowed(Rect r, string t, GUIStyle st)
    {
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.75f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, st);
        GUI.color = new Color(1f, 0.96f, 0.88f, 0.95f); GUI.Label(r, t, st);
        GUI.color = old;
    }
}
