using System.Collections.Generic;
using UnityEngine;

// v7.3.0: criaturas del nivel (diseño propio).
//  · «Los Pálidos»: peces alargados, blancuzcos, de 2,3 m. Patrullan bajo la superficie entre las casas. Si alguien nada cerca,
//    uno lo rodea unos 4 s (se ve la aleta y suena un aviso grave) y luego embiste: el mordisco mata. En la barca, en una escalera
//    o fuera del agua no te pueden alcanzar.
//  · «El Farolero»: enorme pez abisal de la fosa con un señuelo luminoso. Detecta a 14 m con la lámpara encendida (7 m apagada)
//    y persigue más rápido de lo que se nada; no entra en la gruta ni alcanza a los que van dentro de la jaula.
public static class OceanFishMesh
{
    // cuerpo de revolución a lo largo de +z: perfil (z, radio) y achatamiento lateral
    public static Mesh Body(string name, Vector2[] prof, float flat, int sides)
    {
        // v7.3.0: torno con vértices compartidos (normales suaves) y tapas en morro y cola; antes eran quads sueltos (facetado)
        List<Vector2> pr = new List<Vector2>(prof);
        if (pr[0].y > 0.001f) pr.Insert(0, new Vector2(pr[0].x - Mathf.Min(0.3f, pr[0].y), 0f));
        Vector2 last = pr[pr.Count - 1];
        if (last.y > 0.001f) pr.Add(new Vector2(last.x + Mathf.Min(0.35f, last.y * 0.7f), 0f));
        int ring = sides + 1;
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> t = new List<int>();
        for (int j = 0; j < pr.Count; j++)
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2f / sides;
                v.Add(new Vector3(Mathf.Cos(a) * pr[j].y * flat, Mathf.Sin(a) * pr[j].y, pr[j].x));
                uv.Add(new Vector2(i / (float)sides, j / (float)(pr.Count - 1)));
            }
        for (int j = 0; j < pr.Count - 1; j++)
        {
            Vector2 sn = new Vector2(-(pr[j + 1].y - pr[j].y), pr[j + 1].x - pr[j].x);   // normal exterior del perfil (z, r)
            for (int i = 0; i < sides; i++)
            {
                int i0 = j * ring + i, i1 = i0 + 1, i2 = i0 + ring + 1, i3 = i0 + ring;
                AddTri(t, v, i0, i1, i2, sn); AddTri(t, v, i0, i2, i3, sn);
            }
        }
        Mesh m = new Mesh { name = name };
        m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        return m;
    }

    private static void AddTri(List<int> t, List<Vector3> v, int a, int b, int c, Vector2 sn)
    {
        Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
        if (n.sqrMagnitude < 1e-10f) return;   // triángulo degenerado en las tapas
        Vector3 ctr = (v[a] + v[b] + v[c]) / 3f;
        Vector3 rad = new Vector3(ctr.x, ctr.y, 0f); rad = rad.sqrMagnitude > 1e-8f ? rad.normalized : Vector3.zero;
        Vector3 outv = rad * sn.y + Vector3.forward * sn.x;
        if (Vector3.Dot(n, outv) >= 0f) { t.Add(a); t.Add(b); t.Add(c); } else { t.Add(a); t.Add(c); t.Add(b); }
    }

    public static Mesh Fin(string name, Vector3[] pts)
    {
        MeshBuilder mb = new MeshBuilder(1);
        Vector3 n = Vector3.Cross(pts[1] - pts[0], pts[2] - pts[0]).normalized;
        mb.AddQuad(0, pts[0], pts[1], pts[2], pts.Length > 3 ? pts[3] : pts[2], n, Vector3.forward, Vector3.up, 1f, 1f, 0f, 0f);
        mb.AddQuad(0, pts[0], pts[1], pts[2], pts.Length > 3 ? pts[3] : pts[2], -n, Vector3.forward, Vector3.up, 1f, 1f, 0f, 0f);
        return mb.ToMesh(name);
    }

    public static GameObject Part(Transform p, string n, Mesh m, Material mat, Vector3 pos)
    {
        GameObject g = new GameObject(n); g.transform.SetParent(p, false); g.transform.localPosition = pos;
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }
}

public class OceanFishSchool : MonoBehaviour
{
    public enum FishState { Patrol = 0, Circle = 1, Charge = 2, Retreat = 3 }
    public const int Count = 5;
    public const float DetectRange = 24f, CircleRadius = 7f, CircleTime = 4f, BiteRange = 1.4f, PatrolSpeed = 2.2f, CircleSpeed = 3.2f, ChargeSpeed = 7.5f;
    public static readonly Rect Region = new Rect(-40f, 40f, 110f, 196f);   // x -40..70, z 40..236

    public class Fish
    {
        public Vector3 pos, vel, wp; public FishState state; public int target = -1; public float t, angle;
        public Transform tr, tail; public float phase;
    }
    public readonly List<Fish> fish = new List<Fish>();
    public OceanWorld world;
    public int Bites { get; private set; }
    public int Warnings { get; private set; }
    private System.Random rng;
    private Mesh bodyM, tailM, finM;

    public static OceanFishSchool Create(OceanWorld w, int seed, System.Action<Object> track)
    {
        GameObject g = new GameObject("PaleFish"); g.transform.SetParent(w.root, false);
        OceanFishSchool s = g.AddComponent<OceanFishSchool>();
        s.world = w; s.rng = new System.Random(seed * 31 + 17);
        Material skin = ProceduralAssets.CreateMaterial(new Color(0.72f, 0.74f, 0.72f), Resources.Load<Texture2D>("Field/Props/FieldRock2_a"), new Vector2(0.6f, 2f), 0.55f); track(skin);
        Material eye = ProceduralAssets.CreateMaterial(new Color(0.02f, 0.02f, 0.02f), null, Vector2.one, 0.95f); track(eye);
        s.bodyM = OceanFishMesh.Body("PaleBody", new[] { new Vector2(-1.15f, 0.02f), new Vector2(-0.9f, 0.1f), new Vector2(-0.4f, 0.24f), new Vector2(0.2f, 0.3f), new Vector2(0.7f, 0.26f), new Vector2(1.0f, 0.16f), new Vector2(1.15f, 0.04f) }, 0.55f, 10); track(s.bodyM);
        s.tailM = OceanFishMesh.Fin("PaleTail", new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0.42f, -0.55f), new Vector3(0f, 0f, -0.3f), new Vector3(0f, -0.38f, -0.55f) }); track(s.tailM);
        s.finM = OceanFishMesh.Fin("PaleFin", new[] { new Vector3(0f, 0f, 0.3f), new Vector3(0f, 0.5f, -0.15f), new Vector3(0f, 0.05f, -0.35f) }); track(s.finM);
        for (int i = 0; i < Count; i++)
        {
            Fish f = new Fish();
            f.pos = s.RandomPoint(); f.wp = s.RandomPoint(); f.phase = i * 1.7f;
            GameObject fg = new GameObject("Pale" + i); fg.transform.SetParent(g.transform, false);
            OceanFishMesh.Part(fg.transform, "Body", s.bodyM, skin, Vector3.zero);
            f.tail = OceanFishMesh.Part(fg.transform, "Tail", s.tailM, skin, new Vector3(0f, 0f, -1.1f)).transform;
            OceanFishMesh.Part(fg.transform, "Dorsal", s.finM, skin, new Vector3(0f, 0.22f, 0.1f));
            GameObject e1 = ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "Eye", fg.transform, new Vector3(0.13f, 0.06f, 0.85f), Vector3.one * 0.07f, eye, false);
            GameObject e2 = ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "Eye", fg.transform, new Vector3(-0.13f, 0.06f, 0.85f), Vector3.one * 0.07f, eye, false);
            f.tr = fg.transform; f.tr.position = f.pos;
            s.fish.Add(f);
        }
        return s;
    }

    public Vector3 RandomPoint()
    {
        for (int k = 0; k < 30; k++)
        {
            Vector3 p = new Vector3(Region.xMin + (float)rng.NextDouble() * Region.width, -3.5f - (float)rng.NextDouble() * 4f, Region.yMin + (float)rng.NextDouble() * Region.height);
            if (Clear(new Vector2(p.x, p.z), 4f)) return p;
        }
        return new Vector3(30f, -5f, 130f);
    }

    public bool Clear(Vector2 p, float pad)
    {
        foreach (OceanWorld.Obstacle o in world.Obstacles) if (o.r > 0f && (p - o.c).magnitude < o.r + pad) return false;
        if (world.InPierRect(p, pad)) return false;
        return world.SeabedY(p.x, p.y) < -9f;
    }

    // ¿se puede atacar a este jugador? (nadando en mar abierto, no montado ni subiendo, no derribado)
    public bool Huntable(OceanNet.Target t)
    {
        if (t.downed || t.mounted || t.climbing || !t.swimming) return false;
        if (t.pos.y > 0.8f || t.pos.y < -16f) return false;
        return world.SeabedY(t.pos.x, t.pos.z) < -6f;   // junto a la isla (poco fondo) no
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (OceanNet.Authority) Simulate(dt);
        for (int i = 0; i < fish.Count; i++)
        {
            Fish f = fish[i];
            Vector3 v = f.vel; if (v.sqrMagnitude < 0.01f) v = f.tr.forward;
            f.tr.position = OceanNet.Authority ? f.pos : Vector3.Lerp(f.tr.position, f.pos, 1f - Mathf.Exp(-8f * dt));
            f.tr.rotation = Quaternion.Slerp(f.tr.rotation, Quaternion.LookRotation(v.normalized), 1f - Mathf.Exp(-5f * dt));
            float sp = v.magnitude;
            if (f.tail != null) f.tail.localRotation = Quaternion.Euler(0f, Mathf.Sin(Time.time * (4f + sp * 1.6f) + f.phase) * (18f + sp * 3f), 0f);
        }
    }

    private void Simulate(float dt)
    {
        List<OceanNet.Target> ts = OceanNet.Targets();
        foreach (Fish f in fish)
        {
            f.t += dt;
            OceanNet.Target tg = default(OceanNet.Target); bool has = false;
            if (f.target >= 0) foreach (OceanNet.Target t in ts) if (t.id == f.target) { tg = t; has = true; }
            if (f.state != FishState.Patrol && f.state != FishState.Retreat && (!has || !Huntable(tg))) { f.state = FishState.Retreat; f.t = 0f; f.target = -1; f.wp = RandomPoint(); }
            Vector3 desired;
            switch (f.state)
            {
                case FishState.Patrol:
                    if ((f.wp - f.pos).magnitude < 3f) f.wp = RandomPoint();
                    desired = (f.wp - f.pos).normalized * PatrolSpeed;
                    foreach (OceanNet.Target t in ts)
                    {
                        if (!Huntable(t) || Stalked(t.id)) continue;
                        Vector2 d = new Vector2(t.pos.x - f.pos.x, t.pos.z - f.pos.z);
                        if (d.magnitude < DetectRange) { f.state = FishState.Circle; f.target = t.id; f.t = 0f; f.angle = Mathf.Atan2(-d.y, -d.x); Warnings++; if (t.local && OceanAudio.Instance != null) OceanAudio.Instance.FishWarning(); break; }
                    }
                    break;
                case FishState.Circle:
                {
                    f.angle += CircleSpeed / CircleRadius * dt;
                    Vector3 c = tg.pos + new Vector3(Mathf.Cos(f.angle), 0f, Mathf.Sin(f.angle)) * Mathf.Lerp(CircleRadius + 6f, CircleRadius, Mathf.Clamp01(f.t / 1.5f));
                    c.y = -0.55f;   // la aleta asoma
                    desired = Vector3.ClampMagnitude((c - f.pos) * 2f, CircleSpeed * 1.6f);
                    if (f.t > CircleTime) { f.state = FishState.Charge; f.t = 0f; }
                    break;
                }
                case FishState.Charge:
                {
                    Vector3 aim = tg.pos + Vector3.up * 0.9f;
                    desired = (aim - f.pos).normalized * ChargeSpeed;
                    if ((aim - f.pos).magnitude < BiteRange)
                    {
                        Bites++;
                        OceanNet.HostKill(tg.id, OceanNet.KillFish);
                        f.state = FishState.Retreat; f.t = 0f; f.target = -1; f.wp = RandomPoint();
                    }
                    else if (f.t > 4f) { f.state = FishState.Circle; f.t = 0f; }
                    break;
                }
                default:
                    desired = (f.wp - f.pos).normalized * PatrolSpeed * 1.3f;
                    desired.y -= 1f;
                    if (f.t > 8f) { f.state = FishState.Patrol; f.t = 0f; }
                    break;
            }
            // evita estructuras (salvo embistiendo)
            if (f.state != FishState.Charge)
            {
                Vector2 p2 = new Vector2(f.pos.x, f.pos.z);
                foreach (OceanWorld.Obstacle o in world.Obstacles)
                {
                    if (o.r <= 0f) continue;
                    Vector2 d = p2 - o.c; float m = d.magnitude;
                    if (m < o.r + 2.5f && m > 0.01f) { Vector2 push = d / m * (o.r + 2.5f - m) * 3f; desired += new Vector3(push.x, 0f, push.y); }
                }
            }
            f.vel = Vector3.Lerp(f.vel, desired, 1f - Mathf.Exp(-(f.state == FishState.Charge ? 4f : 1.6f) * dt));
            f.pos += f.vel * dt;
            f.pos.y = Mathf.Clamp(f.pos.y, Mathf.Max(world.SeabedY(f.pos.x, f.pos.z) + 1.5f, -14f), -0.45f);
        }
    }

    public bool Stalked(int id) { foreach (Fish f in fish) if (f.target == id && (f.state == FishState.Circle || f.state == FishState.Charge)) return true; return false; }
    public FishState StateOf(int i) { return fish[i].state; }

    public void ApplyNet(int i, Vector3 p, Vector3 v, int st, int target)
    {
        if (i < 0 || i >= fish.Count) return;
        Fish f = fish[i];
        if ((FishState)st == FishState.Circle && f.state != FishState.Circle && target == OceanNet.LocalId && OceanAudio.Instance != null) OceanAudio.Instance.FishWarning();
        f.pos = p; f.vel = v; f.state = (FishState)st; f.target = target;
    }
}

public class OceanAngler : MonoBehaviour
{
    public enum AState { Patrol = 0, Hunt = 1, Return = 2 }
    public const float LampRange = 14f, DarkRange = 7f, KillRange = 3.4f, PatrolSpeed = 1.4f, HuntSpeed = 3.3f, LoseRange = 28f;
    /// <summary>v7.3.0: El Farolero es mucho más grande (cuerpo y boca); la mordida y las holguras se escalan con él.</summary>
    public const float Size = 1.8f, MouthAhead = 2.6f * Size, BodyClear = 3.6f;
    public static readonly Bounds Region = new Bounds(new Vector3(70f, -38f, 251f), new Vector3(62f, 22f, 82f));

    public OceanWorld world;
    public AState State { get; private set; }
    public Vector3 pos, vel, wp;
    public int target = -1;
    public int Kills { get; private set; }
    public float t;
    private System.Random rng;
    private Transform jaw, lure;
    private Light lureLight;
    // v7.3.0: modelo nuevo (Resources/Ocean/Angler/Farolero.fbx) con mandíbula, ilicio y aletas animados
    private Transform model, stalk, tailT, pecL, pecR;
    private Quaternion jawRest, stalkRest, tailRest, pecLRest, pecRRest, modelRest;
    private float jawSign = 1f, jawAngle = 20f;
    private Material escaMat, glowMat;
    private Color escaEm;
    /// <summary>Ángulo fijo de la mandíbula para capturas y pruebas (&lt; 0 = animación normal).</summary>
    public float PoseJaw = -1f;
    /// <summary>Capturas: el Farolero se queda quieto (sin IA) pero sigue animado.</summary>
    public bool FreezeMotion;
    public bool NewModel { get; private set; }
    /// <summary>"-oldangler" en la línea de comandos: aspecto anterior (solo para capturas de antes/después).</summary>
    public static bool LegacyLook { get { return System.Environment.CommandLine.Contains("-oldangler"); } }

    public static OceanAngler Create(OceanWorld w, int seed, System.Action<Object> track)
    {
        GameObject g = new GameObject("Farolero"); g.transform.SetParent(w.root, false);
        OceanAngler a = g.AddComponent<OceanAngler>();
        a.world = w; a.rng = new System.Random(seed * 53 + 5);
        a.pos = w.MonsterSpawns.Count > 0 ? w.MonsterSpawns[0] : new Vector3(80f, -38f, 270f);
        a.wp = a.RandomPoint();
        a.Build(track);
        g.transform.position = a.pos;
        return a;
    }

    private void Build(System.Action<Object> track)
    {
        GameObject src = LegacyLook ? null : Resources.Load<GameObject>("Ocean/Angler/Farolero");
        if (src == null) { BuildLegacy(track); return; }
        GameObject inst = Object.Instantiate(src, transform, false); inst.name = "Model"; model = inst.transform;
        foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Object.Destroy(c);
        Transform jw = FindDeep(model, "Jaw");
        // el frente (boca) debe quedar en +z: si el FBX llega girado, se le da la vuelta
        if (jw != null && jw.GetComponent<Renderer>() != null && transform.InverseTransformPoint(jw.GetComponent<Renderer>().bounds.center).z < 0f) model.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.localRotation;
        model.localScale = Vector3.one * Size;
        model.localPosition = new Vector3(0f, 0f, -0.6f * Size);   // la mordida (MouthAhead delante del centro) cae dentro de la boca
        modelRest = model.localRotation;

        Material skin = OceanGeo.PbrMat("Ocean/Angler/FaroleroSkin", new Color(0.42f, 0.4f, 0.43f), 0.9f, track);   // casi negra
        if (skin.HasProperty("_GlossMapScale")) skin.SetFloat("_GlossMapScale", 0.86f);   // mojada, con brillo de baba, sin parecer metal
        Texture2D em = Resources.Load<Texture2D>("Ocean/Angler/FaroleroSkin_e");
        if (em != null) { skin.SetTexture("_EmissionMap", em); skin.SetColor("_EmissionColor", new Color(0.5f, 1f, 0.85f) * 0.9f); skin.EnableKeyword("_EMISSION"); skin.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
        Material tooth = ProceduralAssets.CreateMaterial(new Color(0.8f, 0.84f, 0.76f, 0.62f), null, Vector2.one, 0.92f); track(tooth);   // agujas translúcidas
        if (tooth.HasProperty("_Mode"))
        {
            tooth.SetFloat("_Mode", 3f); tooth.SetOverrideTag("RenderType", "Transparent");
            tooth.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); tooth.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); tooth.SetInt("_ZWrite", 0);
            tooth.DisableKeyword("_ALPHATEST_ON"); tooth.DisableKeyword("_ALPHABLEND_ON"); tooth.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            tooth.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        Material eye = ProceduralAssets.CreateMaterial(new Color(0.6f, 0.64f, 0.62f), null, Vector2.one, 0.82f); track(eye);   // ojos lechosos, muertos
        ProceduralAssets.SetEmission(eye, new Color(0.035f, 0.045f, 0.045f));
        escaMat = ProceduralAssets.CreateMaterial(new Color(0.35f, 0.6f, 0.52f), null, Vector2.one, 0.7f); track(escaMat);
        escaEm = new Color(0.45f, 1f, 0.82f) * 1.6f; ProceduralAssets.SetEmission(escaMat, escaEm);
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
        {
            string n = r.name;
            Material m = n.StartsWith("Teeth") ? tooth : n.StartsWith("Eye") ? eye : n.StartsWith("Esca") ? escaMat : skin;
            Material[] ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
            for (int i = 0; i < ms.Length; i++) ms[i] = m;
            r.sharedMaterials = ms;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        jaw = jw; if (jaw != null) { jaw.localScale *= 1.15f; jawRest = jaw.localRotation; Renderer jr = jaw.GetComponent<Renderer>(); if (jr != null) jawSign = jaw.InverseTransformPoint(jr.bounds.center).z >= 0f ? 1f : -1f; }
        stalk = FindDeep(model, "Stalk"); if (stalk != null) stalkRest = stalk.localRotation;
        tailT = FindDeep(model, "Tail"); if (tailT != null) tailRest = tailT.localRotation;
        pecL = FindDeep(model, "PecL"); if (pecL != null) pecLRest = pecL.localRotation;
        pecR = FindDeep(model, "PecR"); if (pecR != null) pecRRest = pecR.localRotation;
        foreach (string fn in new[] { "Tail", "Dorsal", "Anal", "PecL", "PecR" })
        {
            Transform f = FindDeep(model, fn);
            if (f != null) OceanFinWave.Attach(f, fn == "Tail" ? 0.16f : 0.1f, fn == "Tail" ? 2.2f : 3.1f, track);
        }
        Transform esca = FindDeep(model, "Esca");
        lure = new GameObject("Lure").transform; lure.SetParent(esca != null ? esca : transform, false);
        if (esca == null) lure.localPosition = new Vector3(0f, 2.5f, 3.9f);
        glowMat = OceanGeo.Unlit("Ocean/Glow", new Color(0.55f, 1f, 0.85f), track);
        glowMat.SetFloat("_Intensity", 3.0f); glowMat.SetFloat("_Fog", 0.045f);
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(lure, false); q.transform.localScale = Vector3.one * (1.5f / Size); q.GetComponent<Renderer>().sharedMaterial = glowMat; q.AddComponent<OceanBillboard>();
        lureLight = lure.gameObject.AddComponent<Light>(); lureLight.type = LightType.Point; lureLight.color = new Color(0.5f, 1f, 0.85f); lureLight.range = 15f; lureLight.intensity = 2.3f; lureLight.renderMode = LightRenderMode.ForcePixel; lureLight.shadows = LightShadows.None;
        ReflectionProbe rp = new GameObject("AnglerProbe").AddComponent<ReflectionProbe>();
        rp.transform.SetParent(transform, false); rp.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom; rp.customBakedTexture = OceanHelmetRack.DeepCube(track);
        rp.boxProjection = false; rp.importance = 3; rp.intensity = 1f; rp.size = new Vector3(14f, 10f, 18f) * Size;
        NewModel = true;
    }

    static Transform FindDeep(Transform t, string n)
    {
        if (t.name == n) return t;
        foreach (Transform c in t) { Transform r = FindDeep(c, n); if (r != null) return r; }
        return null;
    }

    // aspecto anterior (respaldo si falta el modelo, y capturas de "antes")
    private void BuildLegacy(System.Action<Object> track)
    {
        Material skin = ProceduralAssets.CreateMaterial(new Color(0.24f, 0.22f, 0.23f), Resources.Load<Texture2D>("Field/Props/FieldRock1_a"), new Vector2(2.5f, 2.5f), 0.38f); track(skin);   // piel casi negra y mate: la lámpara no la quema
        Material tooth = ProceduralAssets.CreateMaterial(new Color(0.78f, 0.76f, 0.68f), null, Vector2.one, 0.7f); track(tooth);
        Material eye = ProceduralAssets.CreateMaterial(new Color(0.55f, 0.6f, 0.55f), null, Vector2.one, 0.95f); track(eye);
        ProceduralAssets.SetEmission(eye, new Color(0.08f, 0.12f, 0.1f));
        Vector2[] bodyProf = new[] { new Vector2(-4.2f, 0.05f), new Vector2(-3.4f, 0.45f), new Vector2(-2f, 1.1f), new Vector2(-0.4f, 1.75f), new Vector2(0.9f, 1.95f), new Vector2(2.0f, 1.7f), new Vector2(2.6f, 1.2f), new Vector2(2.85f, 0.5f) };
        Mesh body = OceanFishMesh.Body("AnglerBody", bodyProf, 0.85f, 24); track(body);
        Mesh jawM = OceanFishMesh.Body("AnglerJaw", new[] { new Vector2(-1.2f, 0.4f), new Vector2(0.4f, 1.2f), new Vector2(1.9f, 1.35f), new Vector2(2.4f, 0.9f), new Vector2(2.55f, 0.2f) }, 1f, 20); track(jawM);
        Mesh tail = OceanFishMesh.Fin("AnglerTail", new[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 1.3f, -1.6f), new Vector3(0f, 0f, -1.0f), new Vector3(0f, -1.2f, -1.6f) }); track(tail);
        OceanFishMesh.Part(transform, "Body", body, skin, Vector3.zero);
        jaw = OceanFishMesh.Part(transform, "Jaw", jawM, skin, new Vector3(0f, -0.9f, 0.3f)).transform;
        jaw.localScale = new Vector3(1f, 0.55f, 1f);
        OceanFishMesh.Part(transform, "Tail", tail, skin, new Vector3(0f, 0f, -4.1f));
        // dientes (agujas) en la mandíbula y arriba
        MeshBuilder tb = new MeshBuilder(1);
        for (int i = 0; i < 14; i++)
        {
            float a = Mathf.Lerp(-1.1f, 1.1f, i / 13f);
            Vector3 b0 = new Vector3(Mathf.Sin(a) * 1.25f, -0.55f, 2.2f + Mathf.Cos(a) * 0.35f);
            OceanGeo.Tube(tb, 0, b0, b0 + new Vector3(0f, 0.55f + (i % 3) * 0.12f, 0.05f), 0.035f, 4, 1f);
            Vector3 b1 = new Vector3(Mathf.Sin(a) * 1.2f, -0.2f, 2.35f + Mathf.Cos(a) * 0.3f);
            OceanGeo.Tube(tb, 0, b1, b1 + new Vector3(0f, -0.45f - (i % 2) * 0.15f, 0.05f), 0.03f, 4, 1f);
        }
        Mesh tm = tb.ToMesh("Teeth"); track(tm);
        OceanFishMesh.Part(transform, "Teeth", tm, tooth, Vector3.zero);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "Eye", transform, new Vector3(1.05f, 0.9f, 1.6f), Vector3.one * 0.16f, eye, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "Eye", transform, new Vector3(-1.05f, 0.9f, 1.6f), Vector3.one * 0.16f, eye, false);
        // fotóforos: dos hileras de puntos tenues en los flancos (se lee la silueta en la oscuridad)
        Material spot = ProceduralAssets.CreateMaterial(new Color(0.2f, 0.35f, 0.32f), null, Vector2.one, 0.5f); track(spot);
        ProceduralAssets.SetEmission(spot, new Color(0.25f, 0.75f, 0.65f) * 1.6f);
        for (int i = 0; i < 9; i++)
        {
            float z = Mathf.Lerp(1.8f, -3.0f, i / 8f);
            float rr = 0.5f;
            for (int k = 0; k < bodyProf.Length - 1; k++) if (z >= bodyProf[k].x && z <= bodyProf[k + 1].x) rr = Mathf.Lerp(bodyProf[k].y, bodyProf[k + 1].y, Mathf.InverseLerp(bodyProf[k].x, bodyProf[k + 1].x, z));
            rr *= 0.85f;   // ancho (achatado) del cuerpo
            for (int sd = -1; sd <= 1; sd += 2)
                ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "Photophore", transform, new Vector3(sd * (rr - 0.01f), -0.22f - (i % 2) * 0.12f, z), Vector3.one * 0.12f, spot, false);
        }
        // varilla y señuelo luminoso
        MeshBuilder sb = new MeshBuilder(1);
        OceanGeo.Tube(sb, 0, new Vector3(0f, 1.7f, 1.2f), new Vector3(0f, 3.0f, 2.4f), 0.05f, 5, 1f);
        OceanGeo.Tube(sb, 0, new Vector3(0f, 3.0f, 2.4f), new Vector3(0f, 2.6f, 3.8f), 0.04f, 5, 1f);
        Mesh sm = sb.ToMesh("Stalk"); track(sm);
        OceanFishMesh.Part(transform, "Stalk", sm, skin, Vector3.zero);
        lure = new GameObject("Lure").transform; lure.SetParent(transform, false); lure.localPosition = new Vector3(0f, 2.5f, 3.9f);
        Material glow = OceanGeo.Unlit("Ocean/Glow", new Color(0.55f, 1f, 0.85f), track);
        glow.SetFloat("_Intensity", 3.0f); glow.SetFloat("_Fog", 0.045f);
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(lure, false); q.transform.localScale = Vector3.one * 1.3f; q.GetComponent<Renderer>().sharedMaterial = glow; q.AddComponent<OceanBillboard>();
        lureLight = lure.gameObject.AddComponent<Light>(); lureLight.type = LightType.Point; lureLight.color = new Color(0.5f, 1f, 0.85f); lureLight.range = 9f; lureLight.intensity = 2.1f; lureLight.renderMode = LightRenderMode.ForcePixel; lureLight.shadows = LightShadows.None;
    }

    public Vector3 RandomPoint()
    {
        for (int k = 0; k < 30; k++)
        {
            Vector3 p = Region.min + new Vector3((float)rng.NextDouble() * Region.size.x, (float)rng.NextDouble() * Region.size.y, (float)rng.NextDouble() * Region.size.z);
            float sb = world.SeabedY(p.x, p.z);
            if (p.y < sb + 4.2f) p.y = sb + 4.2f;
            if (p.y > -26f) continue;
            if (NearTunnel(p, 8f)) continue;
            if (NearSculpture(p, BodyClear + 2f)) continue;
            if (Vector2.Distance(new Vector2(p.x, p.z), new Vector2(OceanWorld.CageTop.x, OceanWorld.CageTop.z)) < 9f) continue;
            return p;
        }
        return new Vector3(85f, -40f, 265f);
    }

    public bool NearSculpture(Vector3 p, float pad)
    {
        foreach (Vector4 o in world.DeepObstacles) if ((new Vector3(o.x, o.y, o.z) - p).magnitude < o.w + pad) return true;
        return false;
    }

    public bool NearTunnel(Vector3 p, float r)
    {
        foreach (Vector3 s in world.TunnelSamples) if ((s - p).sqrMagnitude < r * r) return true;
        return false;
    }

    // dentro de la gruta (pasada la boca) el Farolero no entra
    public bool InCave(Vector3 p)
    {
        for (int i = 3; i < world.TunnelSamples.Count; i++) if ((world.TunnelSamples[i] - p).sqrMagnitude < 9f) return true;
        return OceanWorld.PocketSurface(p) == OceanWorld.PocketCenter.y;
    }

    public bool Huntable(OceanNet.Target t, float range)
    {
        if (t.downed || t.mounted || InCave(t.pos)) return false;
        if (t.pos.y > -8f) return false;
        return (t.pos + Vector3.up - pos).magnitude < range;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (OceanNet.Authority && !FreezeMotion) Simulate(dt);
        Vector3 v = vel.sqrMagnitude > 0.01f ? vel : transform.forward;
        transform.position = OceanNet.Authority ? pos : Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-6f * dt));
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(v.normalized), 1f - Mathf.Exp(-2f * dt));
        if (!NewModel)
        {
            if (jaw != null) jaw.localRotation = Quaternion.Euler((State == AState.Hunt ? 18f : 6f) + Mathf.Sin(Time.time * 0.8f) * 4f, 0f, 0f);
            if (lureLight != null) lureLight.intensity = 1.4f + Mathf.Sin(Time.time * 2.1f) * 0.25f;
            return;
        }
        Animate(dt);
    }

    // mandíbula que se abre y chasquea al cazar, ilicio que se mece, esca que late, aletas y cola que ondulan
    private void Animate(float dt)
    {
        float tt = Time.time;
        float want = State == AState.Hunt ? 44f + Mathf.Max(0f, Mathf.Sin(tt * 2.6f)) * 14f - Mathf.Max(0f, Mathf.Sin(tt * 2.6f + 2.2f)) * 20f
                   : State == AState.Return ? 14f + Mathf.Sin(tt * 0.9f) * 4f
                   : 27f + Mathf.Sin(tt * 0.55f) * 7f + Mathf.Max(0f, Mathf.Sin(tt * 0.21f)) * 9f;
        if (PoseJaw >= 0f) want = PoseJaw;
        jawAngle = PoseJaw >= 0f ? want : Mathf.Lerp(jawAngle, want, 1f - Mathf.Exp(-(State == AState.Hunt ? 9f : 3f) * dt));
        if (jaw != null) jaw.localRotation = jawRest * Quaternion.Euler(jawSign * jawAngle, 0f, 0f);
        if (stalk != null) stalk.localRotation = stalkRest * Quaternion.Euler(Mathf.Sin(tt * 1.3f) * 5f, Mathf.Sin(tt * 0.9f) * 7f, Mathf.Sin(tt * 1.1f + 1f) * 4f);
        float sp = Mathf.Clamp01(vel.magnitude / Mathf.Max(0.1f, HuntSpeed));
        float sw = tt * (1.6f + sp * 2.4f);
        if (tailT != null) tailT.localRotation = tailRest * Quaternion.AngleAxis(Mathf.Sin(sw) * (10f + sp * 12f), Vector3.up);
        if (pecL != null) pecL.localRotation = pecLRest * Quaternion.AngleAxis(Mathf.Sin(tt * 2.1f) * 12f, Vector3.forward);
        if (pecR != null) pecR.localRotation = pecRRest * Quaternion.AngleAxis(-Mathf.Sin(tt * 2.1f + 0.4f) * 12f, Vector3.forward);
        if (model != null) model.localRotation = Quaternion.Euler(Mathf.Sin(tt * 0.6f) * 1.2f, Mathf.Sin(sw + 1.5f) * (1.5f + sp * 2f), Mathf.Sin(tt * 0.45f) * 1.5f) * modelRest;
        float pulse = 0.5f + 0.5f * Mathf.Sin(tt * 2.4f) * Mathf.Abs(Mathf.Sin(tt * 0.7f + 0.3f));
        if (lureLight != null) lureLight.intensity = 1.3f + pulse * 1.0f;
        if (escaMat != null) escaMat.SetColor("_EmissionColor", escaEm * (0.55f + pulse * 0.9f));
        if (glowMat != null) glowMat.SetFloat("_Intensity", 2.2f + pulse * 1.8f);
    }

    private void Simulate(float dt)
    {
        t += dt;
        List<OceanNet.Target> ts = OceanNet.Targets();
        OceanNet.Target tg = default(OceanNet.Target); bool has = false;
        if (target >= 0) foreach (OceanNet.Target x in ts) if (x.id == target) { tg = x; has = true; }
        Vector3 desired;
        switch (State)
        {
            case AState.Patrol:
                if ((wp - pos).magnitude < 4f) wp = RandomPoint();
                desired = (wp - pos).normalized * PatrolSpeed;
                foreach (OceanNet.Target x in ts)
                    if (Huntable(x, x.lamp ? LampRange : DarkRange)) { State = AState.Hunt; target = x.id; t = 0f; break; }
                break;
            case AState.Hunt:
                if (!has || !Huntable(tg, LoseRange)) { State = AState.Return; target = -1; t = 0f; wp = RandomPoint(); desired = vel; break; }
                Vector3 aim = tg.pos + Vector3.up * 0.9f;
                // el cuerpo es largo: el punto de mordisco es la boca (delante)
                Vector3 mouth = pos + (vel.sqrMagnitude > 0.01f ? vel.normalized : transform.forward) * (NewModel ? MouthAhead : 2.6f);
                desired = (aim - pos).normalized * HuntSpeed;
                if ((aim - mouth).magnitude < KillRange)
                {
                    Kills++;
                    OceanNet.HostKill(tg.id, OceanNet.KillAngler);
                    State = AState.Return; target = -1; t = 0f; wp = RandomPoint();
                }
                break;
            default:
                desired = (wp - pos).normalized * PatrolSpeed;
                if (t > 6f) { State = AState.Patrol; t = 0f; }
                break;
        }
        // no entra en la gruta: si el siguiente paso cae en ella, se para y da la vuelta
        Vector3 np = pos + vel * dt;
        if (InCave(np) || NearTunnel(np, 5f) && State != AState.Hunt) { desired = (pos - world.TunnelSamples[1]).normalized * PatrolSpeed; if (State == AState.Hunt && InCave(tg.pos)) { State = AState.Return; target = -1; } }
        vel = Vector3.Lerp(vel, desired, 1f - Mathf.Exp(-1.4f * dt));
        pos += vel * dt;
        float sb = world.SeabedY(pos.x, pos.z) + (NewModel ? BodyClear : 2.6f);
        if (pos.y < sb) pos.y = sb;
        if (pos.y > -12f) pos.y = -12f;
        // v7.3.0: rodea las esculturas del fondo (esferas con margen para el cuerpo)
        foreach (Vector4 o in world.DeepObstacles)
        {
            Vector3 c = new Vector3(o.x, o.y, o.z), d = pos - c; float r = o.w + BodyClear * 0.8f;
            if (d.sqrMagnitude < r * r) { if (d.sqrMagnitude < 1e-4f) d = Vector3.up; pos = c + d.normalized * r; vel = Vector3.ProjectOnPlane(vel, d.normalized); }
        }
        if (InCave(pos)) pos -= vel * dt * 2f;
    }

    public void ApplyNet(Vector3 p, Vector3 v, int st, int tgt) { pos = p; vel = v; State = (AState)st; target = tgt; }
    public void ForceForTests(Vector3 p, AState s) { pos = p; vel = Vector3.zero; State = s; transform.position = p; }
}
