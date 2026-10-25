using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.8.0 Nivel 114: un todoterreno 4x4 que se conduce (y otro medio enterrado, de chatarra, al que se puede subir).
//  · E junto a una puerta: subir (al asiento libre más cercano: conductor, copiloto o detrás); E dentro: bajar junto a la puerta.
//  · conductor: W/S acelerar / frenar y marcha atrás, A/D girar, Espacio freno de mano, L luces; tope 75 km/h.
//  · física: cuerpo rígido con 4 ruedas de suspensión por rayo (muelle + amortiguador + barra estabilizadora), agarre de arena,
//    centro de masas bajo y ayuda para no volcar en las dunas; da la vuelta al desierto (borde toroidal) él solo.
//  · techo, capó y parachoques tienen colisión: se puede trepar por delante (parachoques → capó → techo).
//  · multijugador: el anfitrión es la autoridad (como la barca): plazas, conducción, luces; los clientes mandan el volante y
//    ven la pose del snapshot (bloque «coches» detrás del reloj del desierto).
//  · arenas movedizas: el coche se hunde, los ocupantes salen (y la arena los lleva como a pie) y el coche vuelve a su aldea.
//  · sonido: motor diésel procedimental (armónicos del encendido según las rpm) y arena bajo las ruedas, sin siseo; polvo y
//    rodadas en la arena (DesertTracks).
public class DesertCar : MonoBehaviour
{
    public const int Seats = 4;
    public int index;
    public bool wreck;
    public readonly int[] seat = { -1, -1, -1, -1 };
    public Vector3 homeLocal; public float homeYaw;
    // estado (autoridad → todos)
    public float throttle, steerIn, steerAngle, brake; public bool handbrake, lights, engineOn, hidden, sinking;
    public float sinkDepth, hiddenT, rpm;
    public Vector3 netVel;
    // ---- piezas
    public Rigidbody rb;
    public Transform visual, steering;
    private Quaternion steer0; private Vector3 steerAxis = Vector3.forward;
    private readonly Transform[] hub = new Transform[4], wheelMesh = new Transform[4];
    private readonly Quaternion[] wheelBase = new Quaternion[4];
    private readonly float[] spin = new float[4], springLen = new float[4], prevLen = new float[4];
    public readonly bool[] contact = new bool[4]; public readonly bool[] onSand = new bool[4];
    public readonly Vector3[] contactPt = new Vector3[4];
    private readonly Vector3[] lastTrack = new Vector3[4]; private readonly bool[] trackOn = new bool[4];
    private Light[] heads = new Light[0];
    private Material lampM, tailM;
    private AudioSource engLo, engHi, tyreSrc;
    private ParticleSystem dustPs;
    public float SlipAmount { get; private set; }
    public float Speed { get { return rb != null && !rb.isKinematic ? rb.velocity.magnitude : netVel.magnitude; } }
    public float ForwardSpeed { get { Vector3 v = rb != null && !rb.isKinematic ? rb.velocity : netVel; return Vector3.Dot(v, transform.forward); } }
    public int DriverId { get { return seat[0]; } }
    public int SeatOf(int id) { for (int i = 0; i < Seats; i++) if (seat[i] == id) return i; return -1; }
    public int Occupants { get { int n = 0; for (int i = 0; i < Seats; i++) if (seat[i] >= 0) n++; return n; } }

    // medidas del modelo (m): ruedas en (±0,95, 0,40, ±1,40), radio 0,40
    public const float R = 0.40f, TrackHalf = 0.95f, AxleZ = 1.40f, AnchorY = 0.80f;
    public const float SusRest = 0.52f, SusMin = 0.12f, SusMax = 0.62f, K = 36000f, C = 4200f, AntiRoll = 14000f;
    public const float Mass = 1900f, EngineF = 7600f, PowerW = 98000f, BrakeF = 17000f, Vmax = 20.8f, VmaxRev = 7f;   // 75 km/h, 25 km/h atrás
    public static float SinkRespawnSeconds = 25f;
    public static readonly Vector3[] Anchor = { new Vector3(-TrackHalf, AnchorY, AxleZ), new Vector3(TrackHalf, AnchorY, AxleZ), new Vector3(-TrackHalf, AnchorY, -AxleZ), new Vector3(TrackHalf, AnchorY, -AxleZ) };
    public float driverSide = -1f;   // lado del volante (x local), se mide en el modelo
    public Vector3 SeatLocal(int i)
    {
        float s = driverSide;
        switch (i) { case 0: return new Vector3(0.42f * s, -0.04f, 0.10f); case 1: return new Vector3(-0.42f * s, -0.04f, 0.10f); case 2: return new Vector3(0.40f * s, 0.0f, -0.98f); default: return new Vector3(-0.40f * s, 0.0f, -0.98f); }
    }
    public Vector3 DoorLocal(int i) { Vector3 s = SeatLocal(i); return new Vector3(Mathf.Sign(s.x) * 1.45f, 0f, s.z + 0.25f); }

    private DesertWorld dw;
    private Transform lroot;

    // ------------------------------------------------------------------ construcción
    public static DesertCar Create(DesertWorld dw, Transform parent, int index, Vector3 local, float yaw, bool wreck, Dictionary<string, Material> mats, System.Action<Object> track)
    {
        GameObject src = Resources.Load<GameObject>("Desert/Car/DCar");
        GameObject go = new GameObject(wreck ? "DesertWreck" : "DesertCar" + index);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local; go.transform.localRotation = Quaternion.identity;   // se mide el modelo sin girar; el giro va al final
        DesertCar c = go.AddComponent<DesertCar>();
        c.index = index; c.wreck = wreck; c.dw = dw; c.lroot = dw.levelRoot; c.homeLocal = local; c.homeYaw = yaw;
        if (src == null) { Debug.LogWarning("[DesertCar] falta Resources/Desert/Car/DCar"); return c; }
        GameObject vis = Object.Instantiate(src, go.transform, false); vis.name = "Visual";
        c.visual = vis.transform;
        c.FixOrientation();
        c.SetupMaterials(mats, track);
        c.BuildWheels();
        c.BuildColliders();
        if (!wreck)
        {
            c.BuildLights();
            c.BuildAudio(track);
            c.BuildDust(track);
            c.BuildProbe();
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = Mass; rb.drag = 0.02f; rb.angularDrag = 1.2f; rb.centerOfMass = new Vector3(0f, 0.50f, 0.05f);
            rb.interpolation = RigidbodyInterpolation.Interpolate; rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.maxDepenetrationVelocity = 3f;
            c.rb = rb;
            if (!DesertCars.Authority) rb.isKinematic = true;
            for (int i = 0; i < 4; i++) { c.springLen[i] = SusRest - 0.12f; c.prevLen[i] = c.springLen[i]; }
            c.handbrake = true;
        }
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        if (c.rb != null) { c.rb.position = go.transform.position; c.rb.rotation = go.transform.rotation; }
        return c;
    }

    // el FBX puede llegar girado: delante = donde están los faros, arriba = +Y; escala real 5,1 m de largo
    private void FixOrientation()
    {
        Transform lamps = Find(visual, "DCar_Lamps"), body = Find(visual, "DCar_Body");
        if (lamps == null || body == null) return;
        for (int pass = 0; pass < 2; pass++)
        {
            Vector3 lc = transform.InverseTransformPoint(lamps.GetComponent<Renderer>().bounds.center);
            if (Mathf.Abs(lc.x) > Mathf.Abs(lc.z)) visual.localRotation = Quaternion.Euler(0f, lc.x > 0f ? -90f : 90f, 0f) * visual.localRotation;
            else if (lc.z < 0f) visual.localRotation = Quaternion.Euler(0f, 180f, 0f) * visual.localRotation;
        }
        Bounds b = LocalBounds(body.GetComponent<Renderer>());
        float len = b.size.z;
        if (len > 0.1f && (len < 4.0f || len > 6.5f)) visual.localScale *= 5.1f / len;
        b = LocalBounds(body.GetComponent<Renderer>());
        // el suelo de las ruedas en y = 0 (el modelo está hecho así; si no, se corrige)
        Transform sw = Find(visual, "DCar_Steering");
        if (sw != null) { Vector3 sc = transform.InverseTransformPoint(sw.GetComponent<Renderer>().bounds.center); driverSide = sc.x < 0f ? -1f : 1f; }
        ModelLength = LocalBounds(body.GetComponent<Renderer>()).size.z; ModelHeight = LocalBounds(body.GetComponent<Renderer>()).max.y;
    }
    public float ModelLength, ModelHeight;
    private Bounds LocalBounds(Renderer r)
    {
        Bounds wb = r.bounds; Bounds b = new Bounds(transform.InverseTransformPoint(wb.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            Vector3 p = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1));
            b.Encapsulate(transform.InverseTransformPoint(p));
        }
        return b;
    }
    private static Transform Find(Transform t, string prefix)
    {
        foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) if (c.name.StartsWith(prefix)) return c;
        return null;
    }

    private void SetupMaterials(Dictionary<string, Material> mats, System.Action<Object> track)
    {
        if (!wreck)
        {
            lampM = new Material(mats["Lamp"]); track(lampM); tailM = new Material(mats["Tail"]); track(tailM);
        }
        foreach (MeshRenderer r in visual.GetComponentsInChildren<MeshRenderer>(true))
        {
            Material[] cur = r.sharedMaterials;
            for (int i = 0; i < cur.Length; i++)
            {
                string nm = cur[i] != null ? cur[i].name : "Black";
                string key = "Black";
                foreach (string k in new[] { "Glass", "Lamp", "Tail" }) if (nm.StartsWith(k)) { key = k; break; }
                string slot = null;
                if (key == "Black") foreach (string k in DesertCars.Slots) if (nm.StartsWith(k)) { slot = k; break; }
                string atlas = r.name.StartsWith("DCar_Body") ? (wreck ? "Wreck" : "Body") : r.name.StartsWith("DCar_Wheel") ? "Wheel" : r.name.StartsWith("DCar_Interior") ? "Interior" : null;
                if (key == "Black" && atlas != null && slot != null && mats.ContainsKey(atlas + "/" + slot)) { cur[i] = mats[atlas + "/" + slot]; continue; }
                if (key == "Black" && slot == "Chrome") key = "Chrome";
                cur[i] = key == "Lamp" && lampM != null ? lampM : key == "Tail" && tailM != null ? tailM : mats[key];
            }
            r.sharedMaterials = cur;
            bool glass = r.name.StartsWith("DCar_Glass");
            r.shadowCastingMode = glass ? ShadowCastingMode.Off : ShadowCastingMode.On; r.receiveShadows = true;
            if (wreck && glass) r.enabled = false;   // chatarra: cristales rotos
        }
        steering = Find(visual, "DCar_Steering");
        if (steering != null)
        {
            steer0 = steering.localRotation;
            Mesh m = steering.GetComponent<MeshFilter>().sharedMesh; Vector3 s = m.bounds.size;
            steerAxis = s.x < s.y && s.x < s.z ? Vector3.right : s.y < s.z ? Vector3.up : Vector3.forward;
        }
    }

    private void BuildWheels()
    {
        Transform wsrc = Find(visual, "DCar_Wheel");
        if (wsrc == null) return;
        MeshFilter mf = wsrc.GetComponent<MeshFilter>(); MeshRenderer mr = wsrc.GetComponent<MeshRenderer>();
        Mesh m = mf.sharedMesh;
        // eje = dimensión más fina de la malla; lado exterior = donde está el buje (submalla negra)
        Vector3 sz = m.bounds.size;
        Vector3 axle = sz.x <= sz.y && sz.x <= sz.z ? Vector3.right : sz.y <= sz.z ? Vector3.up : Vector3.forward;
        float outer = 1f;
        Material[] ms = mr.sharedMaterials;
        for (int si = 0; si < m.subMeshCount && si < ms.Length; si++)
        {
            if (ms[si] == null || !(ms[si].name.StartsWith("Black") || ms[si].name.EndsWith("_Black"))) continue;
            if (!m.isReadable) break;
            int[] tri = m.GetTriangles(si); Vector3[] v = m.vertices; float acc = 0f;
            foreach (int ti in tri) acc += Vector3.Dot(v[ti], axle);
            outer = acc >= 0f ? 1f : -1f;
        }
        Quaternion toCar = Quaternion.FromToRotation(axle * outer, Vector3.right);
        Vector3 vs = visual.localScale;
        for (int i = 0; i < 4; i++)
        {
            Transform h = new GameObject("Hub" + i).transform; h.SetParent(transform, false);
            h.localPosition = new Vector3(Anchor[i].x, R, Anchor[i].z);
            Transform w = new GameObject("Wheel").transform; w.SetParent(h, false); w.localScale = vs;
            w.gameObject.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer wr = w.gameObject.AddComponent<MeshRenderer>(); wr.sharedMaterials = mr.sharedMaterials; wr.shadowCastingMode = ShadowCastingMode.On;
            wheelBase[i] = Anchor[i].x > 0f ? toCar : Quaternion.Euler(0f, 180f, 0f) * toCar;
            w.localRotation = wheelBase[i];
            hub[i] = h; wheelMesh[i] = w;
        }
        wsrc.gameObject.SetActive(false);
    }

    private void Box(string n, Vector3 c, Vector3 s)
    {
        GameObject g = new GameObject(n); g.transform.SetParent(transform, false); g.transform.localPosition = c;
        BoxCollider b = g.AddComponent<BoxCollider>(); b.size = s;
    }
    private void BuildColliders()
    {
        Box("ColBody", new Vector3(0f, 0.86f, 0f), new Vector3(1.78f, 0.86f, 4.6f));        // bañera + capó (arriba a 1,29 m)
        Box("ColCabin", new Vector3(0f, 1.71f, -0.55f), new Vector3(1.74f, 0.84f, 3.30f));  // cabina + baca (techo a 2,13 m)
        Box("ColBumperF", new Vector3(0f, 0.62f, 2.42f), new Vector3(1.85f, 0.16f, 0.20f)); // parachoques (0,70 m): primer escalón
        Box("ColBumperR", new Vector3(0f, 0.60f, -2.32f), new Vector3(1.75f, 0.14f, 0.16f));
        Box("ColSpare", new Vector3(0f, 1.15f, -2.45f), new Vector3(0.82f, 0.82f, 0.28f));
    }

    private void BuildLights()
    {
        Transform lamps = Find(visual, "DCar_Lamps");
        heads = new Light[2];
        for (int i = 0; i < 2; i++)
        {
            GameObject g = new GameObject("Headlight" + i); g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(i == 0 ? -0.68f : 0.68f, 0.98f, 2.42f);
            g.transform.localRotation = Quaternion.Euler(7f, i == 0 ? -3f : 3f, 0f);
            Light l = g.AddComponent<Light>(); l.type = LightType.Spot; l.range = 48f; l.spotAngle = 62f; l.innerSpotAngle = 30f;
            l.intensity = 2.4f; l.color = new Color(1f, 0.93f, 0.80f); l.shadows = i == 0 ? LightShadows.Soft : LightShadows.None; l.shadowStrength = 0.8f;
            l.renderMode = LightRenderMode.ForcePixel; l.enabled = false;
            heads[i] = l;
        }
    }

    private void BuildAudio(System.Action<Object> track)
    {
        engLo = Src("EngineLo", DesertCars.EngineLo, 0.6f); engHi = Src("EngineHi", DesertCars.EngineHi, 0.6f); tyreSrc = Src("Tyres", DesertCars.TyreClip, 0.2f);
    }
    private AudioSource Src(string n, AudioClip c, float y)
    {
        GameObject g = new GameObject(n); g.transform.SetParent(transform, false); g.transform.localPosition = new Vector3(0f, y, n == "Tyres" ? 0f : 1.6f);
        AudioSource s = g.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.playOnAwake = false; s.spatialBlend = 1f; s.volume = 0f;
        s.minDistance = 4f; s.maxDistance = 90f; s.rolloffMode = AudioRolloffMode.Logarithmic; s.dopplerLevel = 0.3f; s.spread = 60f;
        return s;
    }
    private void BuildDust(System.Action<Object> track)
    {
        Texture2D puff = DesertCars.PuffTex;
        Shader sh = Resources.Load<Shader>(WaterVolume.ParticleShaderResource); if (sh == null) sh = Shader.Find("Sprites/Default");
        Material m = new Material(sh) { mainTexture = puff }; track(m);
        GameObject g = new GameObject("WheelDust"); g.transform.SetParent(transform, false);
        ParticleSystem ps = g.AddComponent<ParticleSystem>(); ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.loop = true; main.playOnAwake = false; main.maxParticles = 700; main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f); main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(1.3f, 2.8f); main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = new Color(0.80f, 0.64f, 0.46f, 0.6f); main.gravityModifier = -0.01f;
        var em = ps.emission; em.rateOverTime = 0f;
        var shp = ps.shape; shp.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true;
        Gradient gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = gr;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.y = new ParticleSystem.MinMaxCurve(0.15f, 0.6f);
        ParticleSystemRenderer pr = g.GetComponent<ParticleSystemRenderer>(); pr.sharedMaterial = m; pr.renderMode = ParticleSystemRenderMode.Billboard;
        pr.shadowCastingMode = ShadowCastingMode.Off; pr.receiveShadows = false; pr.sortingFudge = -2f;
        ps.Play(); dustPs = ps;
    }

    // ------------------------------------------------------------------ suelo bajo una rueda (rayo; si no hay colisión, la altura de las dunas)
    private static readonly RaycastHit[] hits = new RaycastHit[12];
    private bool Cast(Vector3 a, Vector3 down, float len, out float dist, out Vector3 n, out Vector3 pt, out bool sand)
    {
        dist = len; n = Vector3.up; pt = a + down * len; sand = false; bool any = false;
        int cnt = Physics.RaycastNonAlloc(a, down, hits, len, ~(1 << 2), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < cnt; i++)
        {
            Collider c = hits[i].collider;
            if (c == null || (rb != null && c.attachedRigidbody == rb) || c is CharacterController || c.transform.IsChildOf(transform)) continue;
            if (hits[i].distance < dist) { dist = hits[i].distance; n = hits[i].normal; pt = hits[i].point; sand = c.name.StartsWith("Dune_"); any = true; }
        }
        // las dunas analíticas (por si no hay malla de colisión: el borde del tramo)
        if (lroot != null)
        {
            Vector3 la = lroot.InverseTransformPoint(a);
            float x = Mathf.Repeat(la.x, dw.L), z = Mathf.Repeat(la.z, dw.L);
            float gy = DesertHeight.H(x, z) - (sinking ? sinkDepth : 0f);
            Vector3 ld = lroot.InverseTransformDirection(down);
            if (ld.y < -0.2f)
            {
                float t = (la.y - gy) / -ld.y;
                if (t >= -0.3f && t < dist) { dist = Mathf.Max(0f, t); n = lroot.TransformDirection(DesertHeight.Normal(x, z)); pt = a + down * dist; sand = true; any = true; }
            }
        }
        return any;
    }

    // ------------------------------------------------------------------ física (solo la autoridad)
    private bool wrapPending;
    private float flipT;
    private void FixedUpdate()
    {
        if (wreck || rb == null || !DesertCars.Authority) return;
        if (wrapPending) { rb.interpolation = RigidbodyInterpolation.Interpolate; wrapPending = false; }
        if (hidden || sinking) return;
        float dt = Time.fixedDeltaTime;
        if (rb.isKinematic) rb.isKinematic = false;
        bool driver = seat[0] >= 0;
        float th = driver ? throttle : 0f, stIn = driver ? steerIn : 0f;
        bool hb = !driver || handbrake;
        Vector3 v = rb.velocity; float vf = Vector3.Dot(v, transform.forward), speed = v.magnitude;
        // volante: menos giro a más velocidad
        float maxSteer = Mathf.Lerp(33f, 11f, Mathf.Clamp01(Mathf.Abs(vf) / 20f));
        steerAngle = Mathf.MoveTowards(steerAngle, stIn * maxSteer, dt * 95f);
        // motor / freno
        float drive = 0f; brake = 0f;
        if (th > 0.05f) { if (vf < -0.6f) brake = th; else if (vf < Vmax) drive = th * Mathf.Min(EngineF, PowerW / Mathf.Max(1f, vf)); }
        else if (th < -0.05f) { if (vf > 0.6f) brake = -th; else if (vf > -VmaxRev) drive = th * EngineF * 0.55f; }
        if (vf > Vmax + 0.3f) drive = Mathf.Min(drive, 0f);
        float slip = 0f;
        float[] comp = new float[4];
        for (int i = 0; i < 4; i++)
        {
            Vector3 a = transform.TransformPoint(Anchor[i]);
            Vector3 down = -transform.up;
            bool hit = Cast(a, down, SusMax + R, out float dist, out Vector3 n, out Vector3 pt, out bool sand);
            contact[i] = hit && dist <= SusMax + R; onSand[i] = sand; contactPt[i] = pt;
            if (!contact[i]) { prevLen[i] = springLen[i] = SusMax; continue; }
            float len = Mathf.Clamp(dist - R, SusMin, SusMax);
            float cv = (prevLen[i] - len) / dt; prevLen[i] = len; springLen[i] = len;
            float F = K * (SusRest - len) + C * cv;
            if (dist - R < SusMin) F += (SusMin - (dist - R)) * K * 6f;   // tope
            F = Mathf.Max(0f, F);
            comp[i] = SusRest - len;
            rb.AddForceAtPosition(transform.up * F, a);
            // neumático: lateral (agarre) y longitudinal (motor 4x4, freno, rodadura en arena)
            float sa = i < 2 ? steerAngle : 0f;
            Vector3 fwd = Quaternion.AngleAxis(sa, transform.up) * transform.forward;
            fwd = Vector3.ProjectOnPlane(fwd, n).normalized; Vector3 right = Vector3.Cross(n, fwd);
            Vector3 pv = rb.GetPointVelocity(pt);
            float wf = Vector3.Dot(pv, fwd), wr = Vector3.Dot(pv, right);
            float mu = sand ? 0.88f : 1.0f;
            bool rearHb = hb && i >= 2;
            float lat = -wr * 7200f;
            float latMax = mu * F * (rearHb && driver ? 0.42f : 1f);
            float lon = drive * 0.25f;
            float bk = (brake * BrakeF * 0.25f) + (rearHb ? BrakeF * 0.5f : 0f);
            if (bk > 0f) lon -= Mathf.Clamp(wf * 1400f, -bk, bk);
            lon -= Mathf.Clamp(wf, -1f, 1f) * (sand ? 0.05f : 0.02f) * F;
            float tot = Mathf.Sqrt(lat * lat + lon * lon), lim = Mathf.Max(latMax, mu * F);
            if (tot > lim && tot > 1e-3f) { float k = lim / tot; lat *= k; lon *= k; slip = Mathf.Max(slip, 1f - k); }
            if (Mathf.Abs(lat) > latMax) { slip = Mathf.Max(slip, 0.5f); lat = Mathf.Sign(lat) * latMax; }
            rb.AddForceAtPosition(fwd * lon + right * lat, pt);
        }
        // barras estabilizadoras
        for (int axle = 0; axle < 2; axle++)
        {
            int l = axle * 2, r = l + 1;
            float d = (comp[l] - comp[r]) * AntiRoll;
            if (contact[l]) rb.AddForceAtPosition(transform.up * d, transform.TransformPoint(Anchor[l]));
            if (contact[r]) rb.AddForceAtPosition(-transform.up * d, transform.TransformPoint(Anchor[r]));
        }
        // aire y estabilidad: la carrocería tiende a ponerse derecha si se inclina mucho (no vuelca en las dunas)
        rb.AddForce(-v * speed * 0.9f);
        Vector3 up = transform.up; float tilt = Vector3.Angle(up, Vector3.up);
        if (tilt > 28f) rb.AddTorque(Vector3.Cross(up, Vector3.up) * (tilt - 28f) * 900f);
        SlipAmount = Mathf.Lerp(SlipAmount, slip + (hb && driver && speed > 3f ? 0.4f : 0f), dt * 6f);
        // volcado: se endereza solo
        flipT = up.y < 0.35f && speed < 2f ? flipT + dt : 0f;
        if (flipT > 3f) { flipT = 0f; Vector3 p = rb.position + Vector3.up * 1.2f; rb.position = p; rb.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f); rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; DesertCars.Toast(this, "El todoterreno vuelve a ponerse sobre las ruedas"); }
        // borde toroidal del desierto
        Vector3 lp = lroot.InverseTransformPoint(rb.position);
        Vector3 sh = Vector3.zero;
        if (lp.x < 0f) sh.x = dw.L; else if (lp.x >= dw.L) sh.x = -dw.L;
        if (lp.z < 0f) sh.z = dw.L; else if (lp.z >= dw.L) sh.z = -dw.L;
        if (sh != Vector3.zero)
        {
            rb.interpolation = RigidbodyInterpolation.None; wrapPending = true;
            Vector3 ws = lroot.TransformVector(sh); rb.position += ws; transform.position += ws; Wraps++;
        }
        // caída fuera del mundo (el Vacío / un fallo de colisión): vuelve a la aldea y los ocupantes salen
        float gy = DesertHeight.H(Mathf.Repeat(lp.x, dw.L), Mathf.Repeat(lp.z, dw.L));
        if (lp.y < gy - 3f) DesertCars.HostRespawn(this, true);
        // arenas movedizas: si el coche para encima (o pasa despacio), se hunde
        int qi = dw.plan.QuickAt(new Vector2(Mathf.Repeat(lp.x, dw.L), Mathf.Repeat(lp.z, dw.L)), 0.6f);
        if (qi >= 0 && (speed < 6f || DesertCars.ForceSink)) DesertCars.HostStartSink(this);
        netVel = rb.velocity;
    }
    public int Wraps { get; private set; }

    // ------------------------------------------------------------------ por fotograma (todos): hundimiento, ruedas, luces, sonido, polvo, rodadas
    private Vector3 netPos; private Quaternion netRot = Quaternion.identity; private bool netHas;
    public void ApplyNet(Vector3 local, Quaternion rot, Vector3 vel, float st, float th, int flags, float sink)
    {
        if (DesertCars.Authority) return;
        netPos = local; netRot = rot; netVel = vel; steerAngle = st; throttle = th; netHas = true;
        lights = (flags & 1) != 0; handbrake = (flags & 2) != 0; brake = (flags & 4) != 0 ? 1f : 0f; engineOn = (flags & 8) != 0;
        hidden = (flags & 16) != 0; sinking = (flags & 32) != 0; sinkDepth = sink;
    }
    public int NetFlags { get { return (lights ? 1 : 0) | (handbrake ? 2 : 0) | (brake > 0.1f ? 4 : 0) | (engineOn ? 8 : 0) | (hidden ? 16 : 0) | (sinking ? 32 : 0); } }

    private float engineT;
    // v7.8.0 (realismo): sonda de reflejos propia (cielo, dunas, sol) para los cristales y la pintura; en el desierto no hay otra.
    // Barata: 64 px, una cara por fotograma, se repinta cada 2,5 s y solo si la cámara está a menos de 70 m.
    public ReflectionProbe probe;
    private float probeT = -10f;
    public int ProbeRenders { get; private set; }
    private void BuildProbe()
    {
        GameObject g = new GameObject("CarReflections"); g.transform.SetParent(transform, false); g.transform.localPosition = new Vector3(0f, 3.0f, 0f);
        probe = g.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime; probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces; probe.resolution = 64; probe.hdr = true;
        probe.size = new Vector3(14f, 10f, 14f); probe.boxProjection = false; probe.importance = 2; probe.shadowDistance = 0f;
        probe.farClipPlane = 400f; probe.nearClipPlane = 0.5f; probe.clearFlags = ReflectionProbeClearFlags.Skybox;
    }
    private void UpdateProbe()
    {
        if (probe == null || Time.time - probeT < 2.5f) return;
        Camera cam = Camera.main; if (cam == null) return;
        if ((cam.transform.position - transform.position).sqrMagnitude > 70f * 70f) return;
        probeT = Time.time; probe.RenderProbe(); ProbeRenders++;
    }

    private void Update()
    {
        if (wreck) return;
        float dt = Time.deltaTime;
        UpdateProbe();
        // clientes: siguen la pose del anfitrión (suavizada; saltos grandes = vuelta del desierto → directo)
        if (!DesertCars.Authority && netHas && lroot != null)
        {
            if (rb != null && !rb.isKinematic) rb.isKinematic = true;
            Vector3 cur = lroot.InverseTransformPoint(transform.position);
            Vector3 target = netPos + lroot.InverseTransformVector(netVel) * 0.05f;
            if ((cur - target).magnitude > 6f) cur = target; else cur = Vector3.Lerp(cur, target, 1f - Mathf.Exp(-12f * dt));
            transform.position = lroot.TransformPoint(cur);
            transform.rotation = Quaternion.Slerp(transform.rotation, lroot.rotation * netRot, 1f - Mathf.Exp(-12f * dt));
        }
        if (DesertCars.Authority)
        {
            engineOn = seat[0] >= 0 && !hidden && !sinking;
            if (sinking) UpdateSink(dt);
            if (hidden) { hiddenT += dt; if (hiddenT > SinkRespawnSeconds) DesertCars.HostRespawn(this, false); }
        }
        SetVisible(!hidden);
        if (hidden) { StopAudio(); return; }
        // ruedas: suspensión visible (rayo) y giro
        float vf = ForwardSpeed;
        for (int i = 0; i < 4; i++)
        {
            if (hub[i] == null) continue;
            if (!DesertCars.Authority)
            {
                Vector3 a = transform.TransformPoint(Anchor[i]);
                bool hit = Cast(a, -transform.up, SusMax + R, out float dist, out Vector3 n, out Vector3 pt, out bool sand);
                contact[i] = hit; onSand[i] = sand; contactPt[i] = pt;
                springLen[i] = hit ? Mathf.Clamp(dist - R, SusMin, SusMax) : SusMax;
            }
            hub[i].localPosition = new Vector3(Anchor[i].x, Anchor[i].y - springLen[i], Anchor[i].z);
            hub[i].localRotation = Quaternion.Euler(0f, i < 2 ? steerAngle : 0f, 0f);
            spin[i] = Mathf.Repeat(spin[i] + vf / R * Mathf.Rad2Deg * dt * (handbrake && i >= 2 && seat[0] >= 0 ? 0f : 1f), 360f);
            wheelMesh[i].localRotation = Quaternion.AngleAxis(spin[i], Vector3.right) * wheelBase[i];
        }
        if (steering != null) steering.localRotation = steer0 * Quaternion.AngleAxis(-steerAngle * 7.5f, steerAxis);
        // luces: faros (y su cristal), pilotos (más fuertes al frenar)
        bool night = DesertSky.Active != null && DesertSky.Active.Night > 0.3f;
        foreach (Light l in heads) if (l != null) { l.enabled = lights; l.intensity = night ? 2.6f : 1.6f; }
        if (lampM != null) ProceduralAssets.SetEmission(lampM, lights ? new Color(1f, 0.92f, 0.75f) * 2.2f : Color.black);
        if (tailM != null) ProceduralAssets.SetEmission(tailM, new Color(0.9f, 0.05f, 0.02f) * ((brake > 0.1f || (handbrake && seat[0] >= 0) ? 1.8f : 0f) + (lights ? 0.7f : 0f)));
        UpdateAudio(dt, vf);
        UpdateDust(dt);
        UpdateTracks();
    }

    private void UpdateSink(float dt)
    {
        if (rb != null && !rb.isKinematic) { rb.velocity = Vector3.zero; rb.isKinematic = true; }
        sinkDepth += dt * 0.42f;
        transform.position += lroot.TransformVector(Vector3.down * dt * 0.42f);
        transform.rotation *= Quaternion.Euler(dt * 3f, 0f, dt * 2f);
        if (sinkDepth > 1.0f && Occupants > 0) DesertCars.HostEjectAll(this);
        if (sinkDepth > 2.6f) { hidden = true; hiddenT = 0f; sinking = false; }
    }

    private bool visibleNow = true;
    private void SetVisible(bool on)
    {
        if (on == visibleNow) return; visibleNow = on;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) if (!(r is ParticleSystemRenderer)) r.enabled = on && !(wreck && r.name.StartsWith("DCar_Glass"));
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = on;
        foreach (Light l in heads) if (l != null && !on) l.enabled = false;
    }

    // ------------------------------------------------------------------ sonido
    public float EngineVolume { get; private set; }
    private void StopAudio() { foreach (AudioSource s in new[] { engLo, engHi, tyreSrc }) if (s != null && s.isPlaying) { s.volume = 0f; s.Stop(); } }
    private void UpdateAudio(float dt, float vf)
    {
        if (engLo == null) return;
        // marchas: 5 relaciones (cambio automático); rpm entre 800 (ralentí) y ~3800
        float v = Mathf.Abs(vf);
        float[] top = { 4.2f, 8.0f, 12.5f, 17.0f, 22f };
        int g = 0; while (g < 4 && v > top[g] * 0.92f) g++;
        float lo = g == 0 ? 0f : top[g - 1] * 0.78f;
        float target = engineOn ? Mathf.Lerp(850f, 3700f, Mathf.Clamp01((v - lo) / (top[g] - lo))) + Mathf.Abs(throttle) * 450f : 0f;
        if (engineOn && v < 0.5f) target = 850f + Mathf.Abs(throttle) * 1700f;
        rpm = Mathf.MoveTowards(rpm, target, dt * (target > rpm ? 3800f : 2600f));
        engineT = engineOn ? Mathf.Min(1f, engineT + dt * 1.2f) : Mathf.Max(0f, engineT - dt * 0.8f);
        float load = Mathf.Clamp01(Mathf.Abs(throttle));
        float hiMix = Mathf.Clamp01((rpm - 1500f) / 1600f);
        float baseV = engineT * (0.55f + 0.45f * load);
        EngineVolume = baseV;
        SetSrc(engLo, AudioMix.A(baseV * 0.75f * (1f - hiMix * 0.8f)), Mathf.Clamp(rpm / 1000f, 0.5f, 2.4f));
        SetSrc(engHi, AudioMix.A(baseV * 0.65f * hiMix), Mathf.Clamp(rpm / 2800f, 0.5f, 1.6f));
        float tyreV = Mathf.Clamp01(v / 14f) * (Grounded ? 1f : 0.1f) * (0.5f + 0.5f * Mathf.Clamp01(SlipAmount * 2f + 0.3f));
        SetSrc(tyreSrc, AudioMix.A(tyreV * 0.5f), 0.75f + Mathf.Clamp01(v / 20f) * 0.45f + SlipAmount * 0.15f);
    }
    private static void SetSrc(AudioSource s, float vol, float pitch)
    {
        s.volume = vol; s.pitch = pitch;
        if (vol > 0.002f && !s.isPlaying) s.Play(); else if (vol <= 0.002f && s.isPlaying) s.Stop();
    }
    public bool Grounded { get { int n = 0; for (int i = 0; i < 4; i++) if (contact[i]) n++; return n >= 2; } }

    // ------------------------------------------------------------------ polvo y rodadas
    private float dustAcc;
    public int DustEmitted { get; private set; }
    public int DustAlive { get { return dustPs != null ? dustPs.particleCount : 0; } }
    private void UpdateDust(float dt)
    {
        if (dustPs == null) return;
        float v = Speed;
        float q = GameSettings.Quality == 0 ? 0.4f : GameSettings.Quality == 1 ? 0.7f : 1f;
        float rate = (v > 1.5f ? Mathf.Clamp01((v - 1.5f) / 12f) * 60f + SlipAmount * 40f : 0f) * q;
        dustAcc += rate * dt;
        bool night = DesertSky.Active != null && DesertSky.Active.Night > 0.5f;
        float dl = DesertSky.Active != null ? Mathf.Lerp(0.08f, 1f, 1f - DesertSky.Active.Night) : 1f;
        while (dustAcc >= 1f)
        {
            dustAcc -= 1f;
            int i = 2 + (Random.value < 0.5f ? 0 : 1);
            if (Random.value < 0.35f) i -= 2;
            if (!contact[i] || !onSand[i]) continue;
            var ep = new ParticleSystem.EmitParams();
            ep.position = contactPt[i] + Vector3.up * 0.3f - transform.forward * 0.4f;
            ep.velocity = -transform.forward * Mathf.Min(v, 12f) * 0.18f + Random.insideUnitSphere * 0.6f + Vector3.up * 0.5f;
            ep.startColor = new Color(0.80f * dl, 0.64f * dl, 0.46f * dl, night ? 0.35f : 0.62f);
            dustPs.Emit(ep, 1); DustEmitted++;
        }
    }
    private void UpdateTracks()
    {
        DesertTracks tr = DesertTracks.Active; if (tr == null || lroot == null) return;
        for (int i = 0; i < 4; i++)
        {
            if (!contact[i] || !onSand[i] || sinking) { trackOn[i] = false; continue; }
            Vector3 p = lroot.InverseTransformPoint(contactPt[i]);
            if (!trackOn[i]) { lastTrack[i] = p; trackOn[i] = true; continue; }
            Vector3 d = p - lastTrack[i]; d.y = 0f;
            if (d.magnitude > 3f) { lastTrack[i] = p; continue; }
            if (d.magnitude < 0.55f) continue;
            // las traseras pisan casi lo mismo que las delanteras: solo se marcan cuando derrapan o giran
            if (i >= 2 && SlipAmount < 0.25f && Mathf.Abs(steerAngle) < 8f) { lastTrack[i] = p; continue; }
            tr.AddTyre(lastTrack[i], p, 0.27f, 0.75f + Mathf.Clamp01(SlipAmount) * 0.25f);
            lastTrack[i] = p;
        }
    }

    // ------------------------------------------------------------------ autoridad: plazas y mandos
    public bool HostEnter(int id, Vector3 from, out int s)
    {
        s = -1;
        if (wreck || hidden || sinking || SeatOf(id) >= 0) return false;
        float best = 99f;
        for (int i = 0; i < Seats; i++)
        {
            if (seat[i] >= 0) continue;
            float d = Vector3.Distance(Flat(from), Flat(transform.TransformPoint(DoorLocal(i))));
            if (d < best) { best = d; s = i; }
        }
        if (s < 0 || Vector3.Distance(Flat(from), Flat(transform.position)) > DesertCars.EnterRange + 2.6f) { s = -1; return false; }
        seat[s] = id;
        if (s == 0) { throttle = 0f; steerIn = 0f; handbrake = false; }
        return true;
    }
    private static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0f, v.z); }
    public bool HostLeave(int id)
    {
        int s = SeatOf(id); if (s < 0) return false;
        seat[s] = -1; if (s == 0) { throttle = 0f; steerIn = 0f; handbrake = true; }
        return true;
    }
    public void HostDrive(int id, float th, float st, bool hb)
    {
        if (seat[0] != id) return;
        throttle = Mathf.Clamp(th, -1f, 1f); steerIn = Mathf.Clamp(st, -1f, 1f); handbrake = hb;
    }

    // al bajar: junto a la puerta de esa plaza (o la otra, detrás, delante) en un sitio libre
    public Vector3 DismountSpot(int seatIdx)
    {
        Vector3 door = DoorLocal(Mathf.Clamp(seatIdx, 0, 3));
        Vector3[] tries = { door, new Vector3(-door.x, 0f, door.z), new Vector3(door.x, 0f, door.z - 1.2f), new Vector3(0f, 0f, -3.3f), new Vector3(0f, 0f, 3.4f), new Vector3(door.x * 1.6f, 0f, door.z) };
        foreach (Vector3 t in tries)
        {
            Vector3 w = transform.TransformPoint(t);
            Vector3 top = new Vector3(w.x, transform.position.y + 3f, w.z);
            float gy = GroundY(top);
            Vector3 feet = new Vector3(w.x, gy + 0.05f, w.z);
            if (!Physics.CheckCapsule(feet + Vector3.up * 0.45f, feet + Vector3.up * 1.5f, 0.32f, ~(1 << 2), QueryTriggerInteraction.Ignore)) return feet;
        }
        Vector3 f = transform.TransformPoint(door);
        return new Vector3(f.x, GroundY(f + Vector3.up * 3f) + 0.1f, f.z);
    }
    private float GroundY(Vector3 top)
    {
        float best = float.MinValue;
        int cnt = Physics.RaycastNonAlloc(top, Vector3.down, hits, 8f, ~(1 << 2), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < cnt; i++) { Collider c = hits[i].collider; if (c == null || c.transform.IsChildOf(transform) || c is CharacterController) continue; best = Mathf.Max(best, hits[i].point.y); }
        if (lroot != null) { Vector3 l = lroot.InverseTransformPoint(top); best = Mathf.Max(best, lroot.TransformPoint(new Vector3(l.x, DesertHeight.H(Mathf.Repeat(l.x, dw.L), Mathf.Repeat(l.z, dw.L)), l.z)).y); }
        return best;
    }

    public void Respawn()
    {
        hidden = false; sinking = false; sinkDepth = 0f; hiddenT = 0f; steerAngle = 0f; throttle = 0f; steerIn = 0f; handbrake = true; flipT = 0f;
        Vector3 p = homeLocal; p.y = DesertHeight.H(p.x, p.z) + 0.05f;
        transform.position = lroot.TransformPoint(p); transform.rotation = lroot.rotation * Quaternion.Euler(0f, homeYaw, 0f);
        if (rb != null) { rb.isKinematic = !DesertCars.Authority; rb.position = transform.position; rb.rotation = transform.rotation; if (!rb.isKinematic) { rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero; } }
        for (int i = 0; i < 4; i++) trackOn[i] = false;
    }
}
