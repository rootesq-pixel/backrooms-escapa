using UnityEngine;

// v7.3.0: barca pequeña de madera con motor fueraborda viejo (4 plazas). Dentro de la barca los peces no te alcanzan.
//  · Plaza 0 (popa) = timón: W/S acelera/frena (máx. 1,5 m/s, más lenta que nadando), A/D gira.
//  · Sin nadie al timón, con alguien a bordo, se deja llevar por la corriente (0,6 m/s) hacia la siguiente casa y para
//    junto a su escalera unos segundos; la última parada es la escalera del muelle del faro. Vacía, se queda quieta.
//  · Autoridad: anfitrión (pose en el snapshot); los clientes piden subir/bajar y mandan el timón.
[DefaultExecutionOrder(-40)]
public class OceanBoat : MonoBehaviour
{
    public const int Seats = 4;
    public const float MaxSpeed = 1.5f, DriftSpeed = 0.6f, TurnRate = 34f, BoardRange = 2.6f, StopWait = 6f;
    public static readonly Vector3[] SeatLocal = { new Vector3(0f, -0.42f, -1.35f), new Vector3(-0.38f, -0.42f, 0.05f), new Vector3(0.38f, -0.42f, 0.05f), new Vector3(0f, -0.42f, 1.15f) };

    public OceanWorld world;
    public Transform hull;
    public readonly int[] seat = { -1, -1, -1, -1 };
    public float speed, yaw;
    public Vector2 pos;
    public int nextStop = 1;
    public float waitT;
    public float throttle, steer;           // del conductor (en la autoridad)
    public float DriveTime { get; private set; }
    public float DriftDistance { get; private set; }
    public int StopsReached { get; private set; }
    private float lastDriveInputT = -10f;
    private float sendT;
    private AudioSource motor;
    private Transform prop;

    public int LocalSeat { get { int id = OceanNet.LocalId; for (int i = 0; i < Seats; i++) if (seat[i] == id) return i; return -1; } }
    public bool Occupied { get { for (int i = 0; i < Seats; i++) if (seat[i] >= 0) return true; return false; } }
    public int SeatOf(int id) { for (int i = 0; i < Seats; i++) if (seat[i] == id) return i; return -1; }

    public static OceanBoat Create(OceanWorld w, System.Action<Object> track)
    {
        GameObject g = new GameObject("Boat"); g.transform.SetParent(w.root, false);
        OceanBoat b = g.AddComponent<OceanBoat>();
        b.world = w;
        b.pos = new Vector2(OceanWorld.BoatHome.x, OceanWorld.BoatHome.z); b.yaw = 0f;
        b.BuildMesh(track);
        b.transform.position = new Vector3(b.pos.x, 0f, b.pos.y);
        return b;
    }

    private void BuildMesh(System.Action<Object> track)
    {
        hull = new GameObject("Hull").transform; hull.SetParent(transform, false);
        // v7.3.0: barca modelada en Blender (tingladillo con remaches, cuadernas, bancadas, regala, toletes, remos, farol y fueraborda)
        GameObject src = OceanFloat.OldProps ? null : Resources.Load<GameObject>("Ocean/Props/Boat");
        GameObject model = null;
        if (src != null)
        {
            model = Object.Instantiate(src, hull, false); model.name = "Model";
            Material wm = OceanGeo.PbrMat("Ocean/Props/BoatWood", new Color(1f, 0.9f, 0.78f), 1f, track);   // v7.3.0b: tinte cálido que compensa el ambiente gris azulado del nivel 7 (el fondo seco se veía azulado)
            if (wm.HasProperty("_GlossMapScale")) wm.SetFloat("_GlossMapScale", 0.18f);   // v7.3.0b: madera seca (antes 0,8: el fondo reflejaba el cielo en manchas azuladas)
            Texture2D em = Resources.Load<Texture2D>("Ocean/Props/BoatWood_e");
            if (em != null) { wm.SetTexture("_EmissionMap", em); wm.SetColor("_EmissionColor", new Color(1f, 0.8f, 0.55f) * 1.6f); wm.EnableKeyword("_EMISSION"); wm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)]; for (int i = 0; i < ms.Length; i++) ms[i] = wm;
                r.sharedMaterials = ms; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;   // sin reflejo del cielo dentro del casco
            }
        }
        // casco: costillas en «U» unidas (proa estrecha), tablas por dentro y por fuera
        MeshBuilder mb;
        if (model == null)
        {
        mb = new MeshBuilder(3);   // 0 madera pintada, 1 madera, 2 metal
        const int N = 10; const float L = 4.0f;
        Vector3[] prevL = null, prevR = null, prevB = null;
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N, z = -L * 0.5f + t * L;
            float w = Mathf.Lerp(0.78f, 0.06f, Mathf.Pow(Mathf.Clamp01((t - 0.55f) / 0.45f), 1.6f)) * (t < 0.08f ? Mathf.Lerp(0.88f, 1f, t / 0.08f) : 1f);
            float keel = Mathf.Lerp(-0.42f, -0.12f, Mathf.Pow(Mathf.Clamp01((t - 0.6f) / 0.4f), 1.4f));
            float gun = 0.32f + Mathf.Pow(t, 4f) * 0.18f;
            Vector3[] l = { new Vector3(-w, gun, z), new Vector3(-w * 0.86f, -0.05f, z), new Vector3(-w * 0.45f, keel + 0.05f, z) };
            Vector3[] r = { new Vector3(w, gun, z), new Vector3(w * 0.86f, -0.05f, z), new Vector3(w * 0.45f, keel + 0.05f, z) };
            Vector3[] bt = { new Vector3(-w * 0.45f, keel + 0.05f, z), new Vector3(w * 0.45f, keel + 0.05f, z) };
            if (prevL != null)
            {
                for (int k = 0; k < 2; k++)
                {
                    Quad(mb, 0, prevL[k], l[k], l[k + 1], prevL[k + 1], true);
                    Quad(mb, 0, prevR[k], r[k], r[k + 1], prevR[k + 1], false);
                }
                Quad(mb, 0, prevB[0], bt[0], bt[1], prevB[1], false);
            }
            prevL = l; prevR = r; prevB = bt;
        }
        // espejo de popa
        Quad(mb, 0, new Vector3(-0.69f, 0.32f, -2f), new Vector3(0.69f, 0.32f, -2f), new Vector3(0.35f, -0.37f, -2f), new Vector3(-0.35f, -0.37f, -2f), false);
        // fondo interior (suelo de tablas) y bancos
        OceanGeo.Box(mb, 1, new Vector3(-0.5f, -0.34f, -1.85f), new Vector3(0.5f, -0.3f, 1.3f), 0.6f);
        foreach (float z in new[] { -1.35f, 0.05f, 1.15f }) OceanGeo.Box(mb, 1, new Vector3(-0.72f, 0.05f, z - 0.15f), new Vector3(0.72f, 0.1f, z + 0.15f), 0.6f);
        // borda
        OceanGeo.Box(mb, 1, new Vector3(-0.82f, 0.3f, -1.95f), new Vector3(-0.7f, 0.37f, 1.0f), 0.6f);
        OceanGeo.Box(mb, 1, new Vector3(0.7f, 0.3f, -1.95f), new Vector3(0.82f, 0.37f, 1.0f), 0.6f);
        // motor fueraborda
        OceanGeo.Box(mb, 2, new Vector3(-0.17f, 0.25f, -2.38f), new Vector3(0.17f, 0.72f, -2.02f), 0.5f);
        OceanGeo.Box(mb, 2, new Vector3(-0.05f, -0.55f, -2.27f), new Vector3(0.05f, 0.25f, -2.15f), 0.5f);
        OceanGeo.Box(mb, 2, new Vector3(-0.025f, 0.45f, -2.02f), new Vector3(0.025f, 0.5f, -1.5f), 0.5f);   // caña del timón
        Material paint = ProceduralAssets.CreateMaterial(new Color(0.42f, 0.5f, 0.48f), Resources.Load<Texture2D>("Field/house_floor_a"), Vector2.one, 0.3f); track(paint);
        GameObject hm = OceanGeo.Emit(mb, "Mesh", hull, new[] { paint, world.WoodMat, world.MetalMat }, false, true, track);
        }
        if (!OceanFloat.OldProps) BuildWaterMask(track);
        // collider: el suelo y la borda (sólo para apoyarse al subir/bajar; los pasajeros van montados)
        BoxCollider floor = hull.gameObject.AddComponent<BoxCollider>(); floor.center = new Vector3(0f, -0.1f, -0.2f); floor.size = new Vector3(1.4f, 0.5f, 3.6f);
        // farolillo de la barca (se ve en la niebla)
        GameObject lp = new GameObject("BoatLamp"); lp.transform.SetParent(hull, false); lp.transform.localPosition = new Vector3(0f, 1.0f, 1.55f);
        Light li = lp.AddComponent<Light>(); li.type = LightType.Point; li.color = new Color(1f, 0.78f, 0.5f); li.intensity = 0.9f; li.range = 6f; li.shadows = LightShadows.None;
        if (model == null)
        {
            OceanGeo.Box(mb = new MeshBuilder(1), 0, new Vector3(-0.012f, 0.1f, 1.53f), new Vector3(0.012f, 1.0f, 1.57f), 0.5f);
            OceanGeo.Emit(mb, "LampPole", hull, new[] { world.MetalMat }, false, false, track);
        }
        Material glow = OceanGeo.Unlit("Ocean/Glow", new Color(1f, 0.75f, 0.45f), track);
        glow.SetFloat("_Intensity", 1.4f); glow.SetFloat("_Fog", 0.02f);
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy0(q.GetComponent<Collider>());
        q.name = "LampGlow"; q.transform.SetParent(lp.transform, false); q.transform.localScale = Vector3.one * (model != null ? 0.42f : 0.7f);
        q.GetComponent<Renderer>().sharedMaterial = glow; q.AddComponent<OceanBillboard>();
        prop = new GameObject("Prop").transform; prop.SetParent(hull, false); prop.localPosition = new Vector3(0f, -0.5f, -2.32f);
        if (model != null)
        {
            Transform pb = null; foreach (Transform c in model.GetComponentsInChildren<Transform>(true)) if (c.name == "Propeller") pb = c;
            if (pb != null) { pb.SetParent(prop, true); pb.localPosition = Vector3.zero; }
        }
        // motor: zumbido tonal (sin ruido de banda ancha), solo cuando alguien conduce
        motor = gameObject.AddComponent<AudioSource>();
        motor.clip = OceanAudio.MotorClip(); motor.loop = true; motor.spatialBlend = 1f; motor.minDistance = 2f; motor.maxDistance = 30f; motor.volume = 0f; motor.dopplerLevel = 0f;
        motor.rolloffMode = AudioRolloffMode.Linear;
        if (Application.isPlaying) motor.Play();
    }

    // v7.3.0: tapa invisible a la altura de la borda (forma del casco: anchura y arrufo de la barca) que marca el stencil;
    // el mar (BackroomsWater) y las cáusticas no se dibujan detrás de ella, así el interior del casco queda seco.
    private void BuildWaterMask(System.Action<Object> track)
    {
        Shader sh = Resources.Load<Shader>("Ocean/BoatWaterMask");
        if (sh == null) return;
        Material m = new Material(sh) { name = "BoatWaterMask" }; track(m);
        const int N = 24; const float L = 4.0f;
        Vector3[] v = new Vector3[(N + 1) * 2]; int[] tri = new int[N * 6];
        for (int i = 0; i <= N; i++)
        {
            float t = i / (float)N, z = -L * 0.5f + 0.04f + t * (L - 0.1f);
            float tt = (z + L * 0.5f) / L;
            float w = Mathf.Lerp(0.78f, 0.06f, Mathf.Pow(Mathf.Clamp01((tt - 0.55f) / 0.45f), 1.6f)) * (tt < 0.08f ? Mathf.Lerp(0.88f, 1f, tt / 0.08f) : 1f);
            float gun = 0.32f + Mathf.Pow(tt, 4f) * 0.18f - 0.03f;
            w = Mathf.Max(0.02f, w - 0.035f);
            v[i * 2] = new Vector3(-w, gun, z); v[i * 2 + 1] = new Vector3(w, gun, z);
            if (i < N) { int k = i * 6, a = i * 2; tri[k] = a; tri[k + 1] = a + 2; tri[k + 2] = a + 1; tri[k + 3] = a + 1; tri[k + 4] = a + 2; tri[k + 5] = a + 3; }
        }
        Mesh mesh = new Mesh { name = "BoatWaterMask" }; mesh.vertices = v; mesh.triangles = tri; mesh.RecalculateBounds(); track(mesh);
        GameObject g = new GameObject("WaterMask"); g.transform.SetParent(hull, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off; r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private static void Destroy0(Object o) { if (o != null) Object.DestroyImmediate(o); }

    private static void Quad(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flip)
    {
        Vector3 n = Vector3.Cross(b - a, d - a).normalized; if (flip) n = -n;
        mb.AddQuad(sub, a, b, c, d, n, Vector3.forward, Vector3.up, 0.8f, 0.8f, 0f, 0f);
        mb.AddQuad(sub, a, b, c, d, -n, Vector3.forward, Vector3.up, 0.8f, 0.8f, 0f, 0f);
    }

    public bool CanBoardFrom(Vector3 p)
    {
        Vector3 c = transform.position;
        float dx = p.x - c.x, dz = p.z - c.z;
        if (dx * dx + dz * dz > BoardRange * BoardRange) return false;
        if (p.y < -2.2f || p.y > 2.6f) return false;
        for (int i = 0; i < Seats; i++) if (seat[i] < 0) return true;
        return false;
    }

    // ---- autoridad
    public bool HostBoard(int id, Vector3 from)
    {
        if (SeatOf(id) >= 0) return false;
        Vector3 c = transform.position;
        if (new Vector2(from.x - c.x, from.z - c.z).magnitude > BoardRange + 1.5f) return false;
        for (int i = 0; i < Seats; i++) if (seat[i] < 0) { seat[i] = id; waitT = 0f; return true; }
        return false;
    }
    public bool HostLeave(int id)
    {
        int s = SeatOf(id); if (s < 0) return false;
        seat[s] = -1; if (s == 0) { throttle = 0f; steer = 0f; }
        return true;
    }
    public void HostDrive(int id, float th, float st)
    {
        if (seat[0] != id) return;
        throttle = Mathf.Clamp(th, -1f, 1f); steer = Mathf.Clamp(st, -1f, 1f);
        lastDriveInputT = Time.time;
    }

    // conductor local: manda el timón (a la autoridad directamente o por red, ~15 veces/s)
    public void LocalDrive(float th, float st)
    {
        if (OceanNet.Authority) { HostDrive(OceanNet.LocalId, th, st); return; }
        sendT -= Time.deltaTime;
        if (sendT <= 0f || Mathf.Abs(th - throttle) > 0.5f) { sendT = 0.066f; throttle = th; OceanNet.Request(OceanNet.OpDrive, 0, th, st); }
    }

    // pose recibida del anfitrión (clientes)
    public void ApplyNet(Vector2 p, float y, float s, int[] seats)
    {
        pos = p; yaw = y; speed = s;
        for (int i = 0; i < Seats; i++) seat[i] = seats[i];
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (OceanNet.Authority) Simulate(dt);
        // bamboleo con las olas
        WaterVolume w = WaterVolume.Active;
        Vector3 c = new Vector3(pos.x, 0f, pos.y);
        float s = w != null ? w.SurfaceHeightAt(c) : 0f;
        float t = Time.time;
        Vector3 target = new Vector3(pos.x, s + 0.02f + Mathf.Sin(t * 1.3f) * 0.03f, pos.y);
        transform.position = OceanNet.Authority ? target : Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-10f * dt));
        Quaternion rot = Quaternion.Euler(Mathf.Sin(t * 0.9f) * 2.2f - speed * 1.5f, yaw, Mathf.Sin(t * 0.7f + 1f) * 3f);
        transform.rotation = OceanNet.Authority ? rot : Quaternion.Slerp(transform.rotation, rot, 1f - Mathf.Exp(-10f * dt));
        if (prop != null) prop.Rotate(0f, 0f, speed * 900f * dt, Space.Self);
        if (motor != null) motor.volume = Mathf.Lerp(motor.volume, seat[0] >= 0 && Mathf.Abs(throttle) > 0.05f ? 0.22f * AudioMix.Sfx : (seat[0] >= 0 ? 0.08f * AudioMix.Sfx : 0f), 1f - Mathf.Exp(-3f * dt));
        if (motor != null) motor.pitch = 0.8f + Mathf.Abs(speed) / MaxSpeed * 0.5f;
        // estelas
        if (Mathf.Abs(speed) > 0.3f && w != null && Time.frameCount % 20 == 0) w.Splash(transform.position - transform.forward * 1.9f, 0.25f, false);
    }

    private void Simulate(float dt)
    {
        bool driver = seat[0] >= 0 && Time.time - lastDriveInputT < 1.0f && (Mathf.Abs(throttle) > 0.05f || Mathf.Abs(steer) > 0.05f);
        float targetSpeed; float turn = 0f;
        if (driver)
        {
            targetSpeed = throttle >= 0f ? throttle * MaxSpeed : throttle * MaxSpeed * 0.4f;
            turn = steer * TurnRate * Mathf.Clamp01(0.35f + Mathf.Abs(speed) / MaxSpeed);
            DriveTime += dt;
            waitT = 0f;
            ChooseNextStop();
        }
        else if (Occupied && nextStop < world.BoatStops.Count)
        {
            // deriva hacia la siguiente parada
            Vector3 st = world.BoatStops[nextStop];
            Vector2 to = new Vector2(st.x, st.z) - pos;
            float d = to.magnitude;
            if (d < 2.2f)
            {
                targetSpeed = 0f;
                waitT += dt;
                if (waitT >= StopWait && nextStop < world.BoatStops.Count - 1) { nextStop++; waitT = 0f; StopsReached++; }
                else if (waitT >= StopWait && nextStop == world.BoatStops.Count - 1 && StopsReached < 99) { StopsReached = 99; }
            }
            else
            {
                targetSpeed = Mathf.Min(DriftSpeed, d * 0.4f);
                float want = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg;
                turn = Mathf.Clamp(Mathf.DeltaAngle(yaw, want) * 0.8f, -20f, 20f);
            }
        }
        else targetSpeed = 0f;
        speed = Mathf.MoveTowards(speed, targetSpeed, (driver ? 0.6f : 0.35f) * dt);
        yaw += turn * dt;
        Vector2 fwd = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
        Vector2 np = pos + fwd * speed * dt;
        // obstáculos: casas, isla, muelle
        np = PushOut(np);
        if (!driver) DriftDistance += (np - pos).magnitude;
        pos = np;
    }

    public void ChooseNextStop()
    {
        // tras conducir, la deriva sigue hacia la primera parada que quede por delante (más al norte)
        for (int i = 1; i < world.BoatStops.Count; i++)
            if (world.BoatStops[i].z > pos.y + 1.5f) { nextStop = i; return; }
        nextStop = world.BoatStops.Count - 1;
    }

    public Vector2 PushOut(Vector2 p)
    {
        const float hullR = 1.7f;
        foreach (OceanWorld.Obstacle o in world.Obstacles)
        {
            if (o.r <= 0f) continue;
            Vector2 d = p - o.c; float m = d.magnitude, min = o.r + hullR;
            if (m < min && m > 0.001f) { p = o.c + d / m * min; speed *= 0.6f; }
        }
        // muelle (rectángulos): fuera hacia el lado más cercano
        if (world.InPierRect(p, hullR))
        {
            float z0 = OceanWorld.PierZ0 - hullR - (p.x > OceanWorld.PierX1 - 8.5f ? 3f : 0f), z1 = OceanWorld.PierZ1 + hullR + (p.x > OceanWorld.PierX1 - 8.5f ? 3f : 0f);
            p.y = Mathf.Abs(p.y - z0) < Mathf.Abs(p.y - z1) ? z0 : z1;
            speed *= 0.6f;
        }
        return p;
    }
}

// cara de un quad siempre hacia la cámara (brillos)
public class OceanBillboard : MonoBehaviour
{
    private void LateUpdate()
    {
        Camera c = Camera.main;
        if (c != null) transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position);
    }
}
