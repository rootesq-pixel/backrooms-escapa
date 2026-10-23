using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.3.0 Nivel 7 «Talasofobia»: mar abierto con niebla.
// Recorrido: salón inundado (sofá flotando, escaleras) → porche y barca → casas sobre pilotes (una con la pistola de bengalas
// en el piso de arriba) → isla del faro (se enciende al acercarte) → muelle con los cascos de buzo y la jaula →
// bajada a la fosa (el Farolero patrulla) → gruta a ~18° a la izquierda de la puerta de la jaula → chimenea hacia arriba →
// bolsa de aire con una poza = salida (victoria final).
// Todo es determinista (posiciones fijas + semilla) para que sea idéntico en multijugador. Agua: superficie en y = 0.
public class OceanWorld : MonoBehaviour
{
    public static OceanWorld Active;

    // ---------------------------------------------------------------- disposición (metros, coordenadas del nivel)
    public const float DeckY = 1.0f, PierY = 1.4f;
    public static readonly Rect StartHouse = new Rect(10.5f, 20.2f, 9f, 8.3f);     // salón inundado (interior)
    public const float StartFloorY = -0.85f, StartUpperY = 2.9f;
    public static readonly Vector3 BoatHome = new Vector3(15f, 0f, 34.2f);
    public static readonly Vector2[] StiltHouses = { new Vector2(14f, 58f), new Vector2(-14f, 86f), new Vector2(36f, 112f), new Vector2(4f, 150f), new Vector2(34f, 188f) };
    public static readonly Vector3 IslandCenter = new Vector3(0f, 4.5f, 250f);
    public const float PierX0 = 17.5f, PierX1 = 56f, PierZ0 = 248.8f, PierZ1 = 251.2f;
    public static readonly Vector3 PierLadderFoot = new Vector3(32f, -1.2f, 248.1f);
    public static readonly Vector3 PierLadderTop = new Vector3(32f, PierY + 0.02f, 249.7f);
    public static readonly Vector3 CageTop = new Vector3(57.6f, PierY, 250f);      // centro del suelo de la jaula arriba
    public static readonly Vector3 HelmetRack = new Vector3(52f, PierY, 253.1f);
    public static readonly Vector3[] TunnelPath =
    {
        new Vector3(44.5f, -47.6f, 245.6f), new Vector3(37.5f, -47.5f, 244.8f), new Vector3(29f, -47.3f, 245.1f),
        new Vector3(23.6f, -46.2f, 245.05f), new Vector3(21.0f, -42f, 245f), new Vector3(20.2f, -34f, 245f), new Vector3(20f, -24.5f, 245f),
    };
    public static readonly Vector3 PocketCenter = new Vector3(20f, -21f, 245f);   // superficie de la poza de la gruta
    public const float PocketR = 3.3f, ChamberR = 6f;
    public const float LighthouseRange = 78f;

    public Vector3 PlayerStart { get; private set; }
    public readonly List<Vector3> MonsterSpawns = new List<Vector3>();
    public readonly List<Vector3> BatterySpots = new List<Vector3>();
    public readonly List<Vector3> ExtraSpots = new List<Vector3>();
    public Transform ExitTransform { get; private set; }
    public Vector3 CageBottom { get; private set; }
    public int FlareHouse { get; private set; }
    public Vector3 FlareSpot { get; private set; }

    // obstáculos para la barca y los peces (círculos en planta)
    public struct Obstacle { public Vector2 c; public float r; }
    public readonly List<Obstacle> Obstacles = new List<Obstacle>();
    public readonly List<OceanLadder> Ladders = new List<OceanLadder>();
    public readonly List<Vector3> BoatStops = new List<Vector3>();   // puntos donde la barca a la deriva se para (escaleras)
    public int seabedTris, holeTris;

    public WaterVolume water;
    public OceanBoat boat;
    public OceanCage cage;
    public OceanLighthouse lighthouse;
    public OceanHelmetRack rack;
    public OceanFishSchool fish;
    public OceanAngler angler;
    public Transform root;
    public BackroomsGenerator gen;
    public LevelConfig cfg;
    private readonly List<Object> owned = new List<Object>();
    private System.Action<Object> track;

    private Material wallM, floorM, roofM, trimM, woodM, pileM, rockM, sandM, metalM, whiteM, redM, darkM, glassM, clothM, caveM;

    public static OceanWorld Build(BackroomsGenerator gen, LevelConfig cfg, Transform levelRoot, int seed, System.Action<Object> track)
    {
        GameObject go = new GameObject("Ocean");
        go.transform.SetParent(levelRoot, false);
        OceanWorld w = go.AddComponent<OceanWorld>();
        w.gen = gen; w.cfg = cfg; w.root = go.transform;
        w.track = o => { if (o != null) { w.owned.Add(o); track(o); } };
        Active = w;
        System.Random rng = new System.Random(seed * 7919 + 77);
        LoadDiag.Step("mar: materiales");
        w.BuildMaterials();
        LoadDiag.Step("mar: fondo e isla");
        w.BuildSeabed();
        LoadDiag.Step("mar: casa inundada");
        w.BuildStartHouse();
        LoadDiag.Step("mar: casas sobre pilotes");
        w.FlareHouse = 1 + rng.Next(2);   // casa 1 o 2 (las dos primeras lejos de la salida)
        for (int i = 0; i < StiltHouses.Length; i++) w.BuildStiltHouse(i, rng);
        LoadDiag.Step("mar: faro y muelle");
        w.BuildLighthouseAndPier();
        LoadDiag.Step("mar: gruta");
        w.BuildCave();
        w.BuildLight();
        LoadDiag.Step("mar: puntos");
        w.BuildSpots(rng);
        return w;
    }

    public void BindWater(WaterVolume v)
    {
        water = v;
        if (v == null) return;
        v.surfaceOverride = PocketSurface;
        v.oceanFloorFallback = -45f;
        v.AddPocketSurface(PocketCenter, new Vector2(PocketR * 2.2f, PocketR * 2.2f));
        // actores (necesitan el agua)
        boat = OceanBoat.Create(this, track);
        cage = OceanCage.Create(this, track);
        rack = OceanHelmetRack.Create(this, track);
        fish = OceanFishSchool.Create(this, gen.UsedSeed, track);
        angler = OceanAngler.Create(this, gen.UsedSeed, track);
        OceanAudio.Create(this);
    }

    // superficie de la bolsa de aire de la gruta (NaN fuera de ella)
    public static float PocketSurface(Vector3 p)
    {
        float dx = p.x - PocketCenter.x, dz = p.z - PocketCenter.z;
        if (dx * dx + dz * dz > ChamberR * ChamberR) return float.NaN;
        if (p.y < PocketCenter.y - 5.5f || p.y > PocketCenter.y + 7f) return float.NaN;
        return PocketCenter.y;
    }

    // ¿ha salido a la superficie en la poza de la gruta? (salida del nivel)
    public static bool AtExit(Vector3 feet)
    {
        float dx = feet.x - PocketCenter.x, dz = feet.z - PocketCenter.z;
        return dx * dx + dz * dz < (PocketR + 0.6f) * (PocketR + 0.6f) && feet.y > PocketCenter.y - 2.2f && feet.y < PocketCenter.y + 4f;
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        // los avatares remotos sobreviven al cambio de nivel: quitarles el casco
        if (NetSession.Instance != null)
            foreach (NetPlayer p in NetSession.Instance.Players)
                if (p.avatar != null)
                    foreach (Transform c in p.avatar.GetComponentsInChildren<Transform>(true))
                        if (c != null && c.name == "RemoteHelmet") Destroy(c.gameObject);
        if (GameManager.ExitTest == (System.Func<Vector3, bool>)AtExit) GameManager.ExitTest = null;
    }

    // ---------------------------------------------------------------- fondo
    public float SeabedY(float x, float z)
    {
        float baseY = -40f + (Mathf.PerlinNoise(x * 0.021f + 3.1f, z * 0.021f + 7.7f) - 0.5f) * 4f;
        float tdx = x - CageTop.x, tdz = z - CageTop.z;
        float td = Mathf.Sqrt(tdx * tdx + tdz * tdz);
        baseY -= 11f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((td - 12f) / 34f)));   // fosa bajo la jaula (~ -51)
        float dx = x - IslandCenter.x, dz = z - IslandCenter.z;
        float d = Mathf.Sqrt(dx * dx + dz * dz);
        if (d > 42f) return baseY;
        float rough = (Mathf.PerlinNoise(x * 0.17f + 11f, z * 0.17f + 5f) - 0.5f);
        float iy;
        if (d < 11f) iy = 4.5f + rough * 0.5f;
        else if (d < 20f) iy = Mathf.Lerp(4.5f, -0.5f, Mathf.SmoothStep(0f, 1f, (d - 11f) / 9f)) + rough * 0.9f;
        else iy = -0.5f - (d - 20f) * 2.5f + rough * 2.4f;
        return Mathf.Max(baseY, iy);
    }

    private void BuildSeabed()
    {
        // parche fino (isla, fosa y gruta) y fondo grueso alrededor (con el hueco del parche)
        const float px0 = -56f, px1 = 112f, pz0 = 176f, pz1 = 320f, ps = 1.5f;
        Heightfield("SeabedFine", px0, px1, pz0, pz1, ps, true, false, 0f, 0f, 0f, 0f);
        Heightfield("SeabedFar", -160f, 224f, -80f, 360f, 8f, false, true, px0, px1, pz0, pz1);
        Obstacles.Add(new Obstacle { c = new Vector2(IslandCenter.x, IslandCenter.z), r = 21f });
    }

    private void Heightfield(string name, float x0, float x1, float z0, float z1, float step, bool fine, bool skipRect, float sx0, float sx1, float sz0, float sz1)
    {
        int nx = Mathf.RoundToInt((x1 - x0) / step), nz = Mathf.RoundToInt((z1 - z0) / step);
        Vector3[] v = new Vector3[(nx + 1) * (nz + 1)];
        Vector2[] uv = new Vector2[v.Length];
        for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++)
        {
            float x = x0 + i * step, z = z0 + j * step;
            v[j * (nx + 1) + i] = new Vector3(x, SeabedY(x, z), z);
            uv[j * (nx + 1) + i] = new Vector2(x / 6f, z / 6f);
        }
        List<int> rockT = new List<int>(), sandT = new List<int>();
        for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
        {
            int a = j * (nx + 1) + i, b = a + 1, c = a + nx + 1, d = c + 1;
            Vector3 ctr = (v[a] + v[d]) * 0.5f;
            if (skipRect && ctr.x > sx0 && ctr.x < sx1 && ctr.z > sz0 && ctr.z < sz1) continue;
            if (fine && CaveHole(ctr)) { holeTris += 2; continue; }
            float dIs = new Vector2(ctr.x - IslandCenter.x, ctr.z - IslandCenter.z).magnitude;
            List<int> t = fine && dIs < 44f ? rockT : sandT;
            t.Add(a); t.Add(c); t.Add(d); t.Add(a); t.Add(d); t.Add(b);
        }
        Mesh m = new Mesh(); m.name = name; track(m);
        m.indexFormat = IndexFormat.UInt32;
        m.vertices = v; m.uv = uv;
        m.subMeshCount = 2; m.SetTriangles(sandT, 0); m.SetTriangles(rockT, 1);
        m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
        seabedTris += (sandT.Count + rockT.Count) / 3;
        GameObject g = new GameObject(name);
        g.transform.SetParent(root, false);
        g.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[] { sandM, rockM };
        mr.shadowCastingMode = fine ? ShadowCastingMode.On : ShadowCastingMode.Off;
        g.AddComponent<MeshCollider>().sharedMesh = m;
    }

    // hueco del fondo en la boca de la gruta (el tubo de la gruta sale de la pared del acantilado)
    private bool CaveHole(Vector3 c)
    {
        if (c.y > -40f) return false;
        Vector2 p = new Vector2(c.x, c.z);
        for (int k = 0; k < 2; k++)
        {
            Vector2 a = new Vector2(TunnelPath[k].x, TunnelPath[k].z), b = new Vector2(TunnelPath[k + 1].x, TunnelPath[k + 1].z);
            Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            float axisY = Mathf.Lerp(TunnelPath[k].y, TunnelPath[k + 1].y, t);
            if ((a + ab * t - p).magnitude < 2.9f && c.y < axisY + 2.75f) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- materiales (texturas CC0 ya incluidas en el juego)
    private void BuildMaterials()
    {
        wallM = OceanGeo.Mat("Field/house_wall_a", "Field/house_wall_n", new Color(0.72f, 0.76f, 0.78f), 0.12f, 0.9f, track);
        floorM = OceanGeo.Mat("Field/house_floor_a", "Field/house_floor_n", new Color(0.62f, 0.58f, 0.52f), 0.35f, 0.8f, track);
        roofM = OceanGeo.Mat("Field/house_roof_a", "Field/house_roof_n", new Color(0.52f, 0.54f, 0.56f), 0.15f, 1f, track);
        trimM = OceanGeo.Mat("Field/House/htrim_a", "Field/House/htrim_n", new Color(0.62f, 0.62f, 0.6f), 0.2f, 0.8f, track);
        woodM = OceanGeo.Mat("Field/house_floor_a", "Field/house_floor_n", new Color(0.46f, 0.42f, 0.37f), 0.25f, 1f, track);
        pileM = OceanGeo.Mat("Field/Props/prop_pole_diff", "Field/Props/prop_pole_nor", new Color(0.5f, 0.48f, 0.44f), 0.2f, 1f, track);
        rockM = OceanGeo.Mat("Field/Props/FieldRock0_a", "Field/Props/FieldRock0_n", new Color(0.46f, 0.48f, 0.48f), 0.18f, 1f, track);
        sandM = OceanGeo.Mat("Field/ground_a", "Field/ground_n", new Color(0.34f, 0.36f, 0.34f), 0.05f, 0.7f, track);
        caveM = OceanGeo.Mat("Field/Props/FieldRock1_a", "Field/Props/FieldRock1_n", new Color(0.36f, 0.38f, 0.38f), 0.32f, 1f, track);
        metalM = OceanGeo.Mat("Textures/MetalWall_a", "Textures/MetalWall_n", new Color(0.46f, 0.43f, 0.40f), 0.45f, 1f, track);
        whiteM = OceanGeo.Mat("Textures/ConcreteWall_a", "Textures/ConcreteWall_n", new Color(0.86f, 0.86f, 0.84f), 0.12f, 0.8f, track);
        redM = OceanGeo.Mat("Textures/ConcreteWall_a", "Textures/ConcreteWall_n", new Color(0.62f, 0.16f, 0.13f), 0.12f, 0.8f, track);
        darkM = ProceduralAssets.CreateMaterial(new Color(0.08f, 0.09f, 0.1f), null, Vector2.one, 0.55f); track(darkM);
        clothM = ProceduralAssets.CreateMaterial(new Color(0.33f, 0.38f, 0.34f), Resources.Load<Texture2D>("Textures/CarpetBrown_a"), Vector2.one, 0.05f); track(clothM);
        glassM = ProceduralAssets.CreateMaterial(new Color(0.55f, 0.62f, 0.65f), null, Vector2.one, 0.9f); track(glassM);
    }
    public Material MetalMat { get { return metalM; } }
    public Material WoodMat { get { return woodM; } }
    public Material DarkMat { get { return darkM; } }
    public Material ClothMat { get { return clothM; } }
    public Material PileMat { get { return pileM; } }
    public Material RockMat { get { return rockM; } }

    // ---------------------------------------------------------------- casa inicial (salón inundado + piso de arriba)
    private void BuildStartHouse()
    {
        GameObject hg = new GameObject("FloodedHouse"); hg.transform.SetParent(root, false);
        float x0 = StartHouse.xMin, x1 = StartHouse.xMax, z0 = StartHouse.yMin, z1 = StartHouse.yMax, t = 0.2f;
        float f0 = StartFloorY, f1 = StartUpperY, top = StartUpperY + 2.55f;
        MeshBuilder mb = new MeshBuilder(4);   // 0 pared, 1 suelo, 2 tejado, 3 marcos
        // forjados
        OceanGeo.Box(mb, 1, new Vector3(x0 - t, f0 - 0.2f, z0 - t), new Vector3(x1 + t, f0, z1 + t), 1.4f);
        // forjado del piso de arriba con el hueco de la escalera (x0..x0+1.4, z 23.2..27.9)
        float sx1 = x0 + 1.45f, sz0 = 23.0f, sz1 = 28.0f;
        OceanGeo.Box(mb, 1, new Vector3(sx1, f1 - 0.15f, z0), new Vector3(x1, f1, z1), 1.4f);
        OceanGeo.Box(mb, 1, new Vector3(x0, f1 - 0.15f, z0), new Vector3(sx1, f1, sz0), 1.4f);
        OceanGeo.Box(mb, 1, new Vector3(x0, f1 - 0.15f, sz1), new Vector3(sx1, f1, z1), 1.4f);
        OceanGeo.Box(mb, 1, new Vector3(x0 - t, top, z0 - t), new Vector3(x1 + t, top + 0.12f, z1 + t), 1.4f);
        // muro trasero: hueco de la cabina del ascensor (abajo)
        OceanGeo.Hole cab = new OceanGeo.Hole(13.35f, 16.65f, f0, 2.62f);
        OceanGeo.Hole bw = new OceanGeo.Hole(16.8f, 18.2f, f1 + 0.9f, f1 + 2.0f);
        OceanGeo.Wall(mb, 0, true, z0 - t, t, x0 - t, x1 + t, f0 - 0.2f, f1, 2.5f, cab);
        OceanGeo.Wall(mb, 0, true, z0 - t, t, x0 - t, x1 + t, f1, top, 2.5f, bw);
        // muro delantero: puerta al porche y ventanas
        OceanGeo.Hole door = new OceanGeo.Hole(14.35f, 15.65f, f0, f0 + 2.15f);
        OceanGeo.Hole fw1 = new OceanGeo.Hole(11.6f, 13.2f, 0.25f, 1.7f), fw2 = new OceanGeo.Hole(16.8f, 18.4f, 0.25f, 1.7f);
        OceanGeo.Hole uw1 = new OceanGeo.Hole(12f, 13.4f, f1 + 0.9f, f1 + 2.0f), uw2 = new OceanGeo.Hole(16.6f, 18.0f, f1 + 0.9f, f1 + 2.0f);
        OceanGeo.Wall(mb, 0, true, z1, t, x0 - t, x1 + t, f0 - 0.2f, f1, 2.5f, door, fw1, fw2);
        OceanGeo.Wall(mb, 0, true, z1, t, x0 - t, x1 + t, f1, top, 2.5f, uw1, uw2);
        // laterales
        OceanGeo.Hole ew1 = new OceanGeo.Hole(22.2f, 24.0f, 0.25f, 1.7f), ew2 = new OceanGeo.Hole(25.4f, 27.0f, 0.25f, 1.7f);
        OceanGeo.Hole eu = new OceanGeo.Hole(23.5f, 25.2f, f1 + 0.9f, f1 + 2.0f);
        OceanGeo.Wall(mb, 0, false, x0 - t, t, z0, z1, f0 - 0.2f, f1, 2.5f);
        OceanGeo.Wall(mb, 0, false, x0 - t, t, z0, z1, f1, top, 2.5f, eu);
        OceanGeo.Wall(mb, 0, false, x1, t, z0, z1, f0 - 0.2f, f1, 2.5f, ew1, ew2);
        OceanGeo.Wall(mb, 0, false, x1, t, z0, z1, f1, top, 2.5f, eu);
        foreach (OceanGeo.Hole h in new[] { fw1, fw2, uw1, uw2, door }) OceanGeo.Frame(mb, 3, true, z1, t, h, 0.07f);
        foreach (OceanGeo.Hole h in new[] { ew1, ew2, eu }) OceanGeo.Frame(mb, 3, false, x1, t, h, 0.07f);
        OceanGeo.Frame(mb, 3, false, x0 - t, t, eu, 0.07f);
        OceanGeo.Frame(mb, 3, true, z0 - t, t, bw, 0.07f);
        // escalera: junto al muro oeste, sube hacia -z (de z 27.9 a 23.2) hasta el piso de arriba
        OceanGeo.Stairs(mb, 3, new Vector3(x0 + 1.4f, f0, 27.95f), Vector3.back, 1.3f, f1 - f0, 18, 0.262f, 0.6f);
        // barandilla de la escalera arriba
        OceanGeo.Box(mb, 3, new Vector3(sx1, f1, sz0), new Vector3(sx1 + 0.06f, f1 + 0.95f, sz1), 0.5f);
        // escalón de la cabina (el ascensor queda 0,87 m por encima del salón)
        OceanGeo.Box(mb, 1, new Vector3(13.7f, f0, z0), new Vector3(16.3f, -0.43f, z0 + 0.75f), 0.8f);
        // tejado
        OceanGeo.Gable(mb, 2, 0, x0 - t, x1 + t, z0 - t, z1 + t, top + 0.12f, top + 2.2f, 0.45f, 2.2f);
        GameObject house = OceanGeo.Emit(mb, "Shell", hg.transform, new[] { wallM, floorM, roofM, trimM }, true, true, track);
        // porche y pilotes
        MeshBuilder pb = new MeshBuilder(2);
        OceanGeo.Box(pb, 0, new Vector3(12.6f, -0.52f, z1 + t), new Vector3(17.4f, -0.3f, 31.7f), 1.2f);
        for (int i = 0; i < 3; i++) for (int j = 0; j < 2; j++)
        {
            Vector3 p = new Vector3(12.8f + i * 2.2f, 0f, 29.0f + j * 2.5f);
            OceanGeo.Tube(pb, 1, new Vector3(p.x, SeabedY(p.x, p.z) - 0.5f, p.z), new Vector3(p.x, -0.5f, p.z), 0.16f, 7, 3f);
        }
        foreach (float px in new[] { 12.75f, 17.25f }) OceanGeo.Box(pb, 1, new Vector3(px - 0.07f, -0.3f, 31.45f), new Vector3(px + 0.07f, 2.4f, 31.6f), 0.6f);
        OceanGeo.Box(pb, 0, new Vector3(12.5f, 2.4f, z1 + t), new Vector3(17.5f, 2.52f, 31.9f), 1.2f);
        // pilotes bajo la casa
        for (int i = 0; i < 4; i++) for (int j = 0; j < 3; j++)
        {
            Vector3 p = new Vector3(x0 + 0.3f + i * (x1 - x0 - 0.6f) / 3f, 0f, z0 + 0.3f + j * (z1 - z0 - 0.6f) / 2f);
            OceanGeo.Tube(pb, 1, new Vector3(p.x, SeabedY(p.x, p.z) - 0.5f, p.z), new Vector3(p.x, f0 - 0.2f, p.z), 0.2f, 7, 3f);
        }
        OceanGeo.Emit(pb, "Porch", hg.transform, new[] { woodM, pileM }, true, true, track);
        Obstacles.Add(new Obstacle { c = new Vector2(15f, 25f), r = 8.2f });
        BoatStops.Add(BoatHome);
        // muebles: sofá flotando, mesa, sillas, estantería, lámpara caída, cama arriba
        Transform fu = new GameObject("Furniture").transform; fu.SetParent(hg.transform, false);
        OceanFloat.Sofa(this, fu, new Vector3(16.9f, 0f, 24.6f), 18f, track);
        OceanFloat.Chair(this, fu, new Vector3(13.4f, 0f, 26.3f), 140f, track);
        OceanFloat.Debris(this, fu, new Vector3(18.4f, 0f, 21.5f), 30f, new Vector3(0.6f, 0.05f, 0.45f), floorM, track);
        OceanFloat.Debris(this, fu, new Vector3(14.6f, 0f, 22.3f), 75f, new Vector3(0.35f, 0.04f, 0.5f), trimM, track);
        MeshBuilder fb = new MeshBuilder(3);
        OceanGeo.Box(fb, 0, new Vector3(13.0f, f0, 23.4f), new Vector3(14.6f, f0 + 0.72f, 24.4f), 0.8f);           // mesa (sumergida casi entera)
        OceanGeo.Box(fb, 0, new Vector3(18.95f, f0, 26.0f), new Vector3(19.45f, f0 + 2.0f, 27.9f), 0.8f);           // estantería
        for (int k = 0; k < 4; k++) OceanGeo.Box(fb, 1, new Vector3(18.9f, f0 + 0.3f + k * 0.45f, 26.0f), new Vector3(19.45f, f0 + 0.33f + k * 0.45f, 27.9f), 0.8f);
        OceanGeo.Box(fb, 2, new Vector3(16.6f, f1, 20.4f), new Vector3(18.6f, f1 + 0.5f, 22.4f), 1f);              // cama
        OceanGeo.Box(fb, 0, new Vector3(16.6f, f1, 20.2f), new Vector3(18.6f, f1 + 0.95f, 20.4f), 1f);
        OceanGeo.Box(fb, 0, new Vector3(12.6f, f1, 27.6f), new Vector3(14.2f, f1 + 1.9f, 28.4f), 1f);              // armario
        OceanGeo.Emit(fb, "Static", fu, new[] { woodM, trimM, clothM }, true, true, track);
        // luz fría de la ventana en el salón (sin sombras)
        AddPoint(hg.transform, new Vector3(15f, 1.5f, 26.8f), new Color(0.75f, 0.85f, 0.95f), 0.55f, 9f);
        AddPoint(hg.transform, new Vector3(15f, f1 + 1.8f, 24f), new Color(0.75f, 0.85f, 0.95f), 0.45f, 8f);
    }

    private Light AddPoint(Transform p, Vector3 pos, Color c, float intensity, float range)
    {
        GameObject g = new GameObject("Light"); g.transform.SetParent(p, false); g.transform.position = pos;
        Light l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
        l.renderMode = LightRenderMode.ForceVertex;
        return l;
    }

    // ---------------------------------------------------------------- casas sobre pilotes (dos plantas, porche y escalera al agua)
    public struct StiltInfo { public Vector3 center; public float yaw; public Vector3 upperFloor; }
    public readonly List<StiltInfo> Stilts = new List<StiltInfo>();

    private void BuildStiltHouse(int idx, System.Random rng)
    {
        Vector2 c2 = StiltHouses[idx];
        float yaw = 180f + (idx % 2 == 0 ? 0f : 0f);
        GameObject hg = new GameObject("StiltHouse" + idx); hg.transform.SetParent(root, false);
        hg.transform.position = new Vector3(c2.x, 0f, c2.y);
        hg.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Transform T = hg.transform;
        const float hx = 3.3f, hz = 2.8f, t = 0.2f, f0 = DeckY, f1 = DeckY + 2.6f;
        float top = f1 + 2.4f;
        MeshBuilder mb = new MeshBuilder(4);
        OceanGeo.Box(mb, 1, new Vector3(-hx - t, f0 - 0.22f, -hz - t), new Vector3(hx + t, f0, hz + t), 1.4f);
        // forjado alto con hueco de escalera (x -0.5..3.3, z -2.8..-1.6)
        OceanGeo.Box(mb, 1, new Vector3(-hx, f1 - 0.15f, -1.55f), new Vector3(hx, f1, hz), 1.4f);
        OceanGeo.Box(mb, 1, new Vector3(-hx, f1 - 0.15f, -hz), new Vector3(-0.6f, f1, -1.55f), 1.4f);
        OceanGeo.Box(mb, 1, new Vector3(-hx - t, top, -hz - t), new Vector3(hx + t, top + 0.12f, hz + t), 1.4f);
        OceanGeo.Hole door = new OceanGeo.Hole(-0.6f, 0.6f, f0, f0 + 2.1f);
        OceanGeo.Hole w1 = new OceanGeo.Hole(-2.6f, -1.4f, f0 + 0.9f, f0 + 1.9f), w2 = new OceanGeo.Hole(1.4f, 2.6f, f0 + 0.9f, f0 + 1.9f);
        OceanGeo.Hole u1 = new OceanGeo.Hole(-2.4f, -1.0f, f1 + 0.85f, f1 + 1.9f), u2 = new OceanGeo.Hole(1.0f, 2.4f, f1 + 0.85f, f1 + 1.9f);
        OceanGeo.Hole s1 = new OceanGeo.Hole(-1.0f, 0.4f, f0 + 0.9f, f0 + 1.9f), s2 = new OceanGeo.Hole(-0.8f, 0.8f, f1 + 0.85f, f1 + 1.9f);
        OceanGeo.Wall(mb, 0, true, hz, t, -hx - t, hx + t, f0 - 0.22f, f1, 2.5f, door, w1, w2);     // frente (+z local: puerta)
        OceanGeo.Wall(mb, 0, true, hz, t, -hx - t, hx + t, f1, top, 2.5f, u1, u2);
        OceanGeo.Wall(mb, 0, true, -hz - t, t, -hx - t, hx + t, f0 - 0.22f, f1, 2.5f);
        OceanGeo.Wall(mb, 0, true, -hz - t, t, -hx - t, hx + t, f1, top, 2.5f, u1);
        OceanGeo.Wall(mb, 0, false, -hx - t, t, -hz, hz, f0 - 0.22f, f1, 2.5f, s1);
        OceanGeo.Wall(mb, 0, false, -hx - t, t, -hz, hz, f1, top, 2.5f, s2);
        OceanGeo.Wall(mb, 0, false, hx, t, -hz, hz, f0 - 0.22f, f1, 2.5f, s1);
        OceanGeo.Wall(mb, 0, false, hx, t, -hz, hz, f1, top, 2.5f, s2);
        foreach (OceanGeo.Hole h in new[] { door, w1, w2, u1, u2 }) OceanGeo.Frame(mb, 3, true, hz, t, h, 0.07f);
        foreach (OceanGeo.Hole h in new[] { s1, s2 }) { OceanGeo.Frame(mb, 3, false, hx, t, h, 0.07f); OceanGeo.Frame(mb, 3, false, -hx - t, t, h, 0.07f); }
        // escalera interior: junto al muro del fondo, sube hacia -x desde x 3.2 hasta el piso alto
        OceanGeo.Stairs(mb, 3, new Vector3(3.25f, f0, -hz + 0.02f), Vector3.left, 1.2f, f1 - f0, 12, 0.3f, 0.6f);
        OceanGeo.Box(mb, 3, new Vector3(-0.6f, f1, -1.6f), new Vector3(3.3f, f1 + 0.95f, -1.54f), 0.5f);
        OceanGeo.Gable(mb, 2, 0, -hx - t, hx + t, -hz - t, hz + t, top + 0.12f, top + 1.9f, 0.45f, 2.2f);
        Material wm = wallM;
        OceanGeo.Emit(mb, "Shell", T, new[] { wm, floorM, roofM, trimM }, true, true, track);
        // porche delantero, barandilla, pilotes y escalera al agua
        MeshBuilder pb = new MeshBuilder(2);
        OceanGeo.Box(pb, 0, new Vector3(-4.4f, f0 - 0.22f, hz + t), new Vector3(4.4f, f0, hz + 2.6f), 1.2f);
        float zr = hz + 2.55f;
        OceanGeo.Box(pb, 0, new Vector3(-4.4f, f0 + 0.95f, zr - 0.05f), new Vector3(-0.7f, f0 + 1.02f, zr), 1f);
        OceanGeo.Box(pb, 0, new Vector3(0.7f, f0 + 0.95f, zr - 0.05f), new Vector3(4.4f, f0 + 1.02f, zr), 1f);
        for (float x = -4.3f; x <= 4.31f; x += 1.2f) if (Mathf.Abs(x) > 0.65f) OceanGeo.Box(pb, 0, new Vector3(x - 0.04f, f0, zr - 0.06f), new Vector3(x + 0.04f, f0 + 0.98f, zr), 1f);
        Vector3 cw = T.position;
        foreach (Vector2 pp in new[] { new Vector2(-hx, -hz), new Vector2(hx, -hz), new Vector2(-hx, hz), new Vector2(hx, hz), new Vector2(-4.2f, hz + 2.45f), new Vector2(4.2f, hz + 2.45f), new Vector2(0f, -hz), new Vector2(-1.2f, hz + 2.45f), new Vector2(1.2f, hz + 2.45f) })
        {
            Vector3 w = T.TransformPoint(new Vector3(pp.x, 0f, pp.y));
            float sy = SeabedY(w.x, w.z) - 0.5f;
            OceanGeo.Tube(pb, 1, new Vector3(pp.x, sy, pp.y), new Vector3(pp.x, f0 - 0.22f, pp.y), 0.19f, 7, 3f);
        }
        // tirantes en cruz bajo el porche (le dan aspecto de palafito)
        OceanGeo.Tube(pb, 1, new Vector3(-hx, -3f, -hz), new Vector3(hx, f0 - 0.4f, -hz), 0.07f, 5, 2f);
        OceanGeo.Tube(pb, 1, new Vector3(hx, -3f, hz), new Vector3(-hx, f0 - 0.4f, hz), 0.07f, 5, 2f);
        OceanGeo.Emit(pb, "Porch", T, new[] { woodM, pileM }, true, true, track);
        Vector3 lf = T.TransformPoint(new Vector3(0f, -1.2f, zr + 0.55f)), lt = T.TransformPoint(new Vector3(0f, f0 + 0.02f, zr - 0.8f));
        OceanLadder lad = OceanLadder.Create(this, T, new Vector3(0f, f0, zr + 0.06f), lf, lt, T.TransformDirection(Vector3.back), track);
        Ladders.Add(lad);
        BoatStops.Add(T.TransformPoint(new Vector3(0f, 0f, zr + 3.2f)));
        Obstacles.Add(new Obstacle { c = new Vector2(c2.x, c2.y), r = 5.2f });
        Obstacles.Add(new Obstacle { c = new Vector2(lf.x, lf.z) + (Vector2)(new Vector2(c2.x - lf.x, c2.y - lf.z).normalized * 0.4f), r = 1.6f });
        // muebles del piso de arriba: mesa (aquí puede estar la pistola de bengalas), cama, silla
        MeshBuilder fb = new MeshBuilder(3);
        OceanGeo.Box(fb, 0, new Vector3(-2.9f, f1, 1.0f), new Vector3(-1.5f, f1 + 0.76f, 2.3f), 0.8f);
        OceanGeo.Box(fb, 2, new Vector3(0.9f, f1, 0.6f), new Vector3(3.0f, f1 + 0.48f, 2.5f), 1f);
        OceanGeo.Box(fb, 1, new Vector3(-2.8f, f0, -2.6f), new Vector3(-1.2f, f0 + 0.9f, -2.0f), 0.8f);
        OceanGeo.Emit(fb, "Furniture", T, new[] { woodM, trimM, clothM }, true, true, track);
        StiltInfo si = new StiltInfo { center = T.position, yaw = yaw, upperFloor = T.TransformPoint(new Vector3(-2.2f, f1 + 0.76f + 0.1f, 1.65f)) };
        Stilts.Add(si);
        if (idx == FlareHouse) FlareSpot = si.upperFloor;
        AddPoint(T, T.TransformPoint(new Vector3(0f, f1 + 1.7f, 0f)), new Color(0.75f, 0.85f, 0.95f), 0.4f, 7f);
    }

    // ---------------------------------------------------------------- faro, muelle, cascos y jaula
    private void BuildLighthouseAndPier()
    {
        GameObject lg = new GameObject("Lighthouse"); lg.transform.SetParent(root, false);
        Vector3 b = IslandCenter + Vector3.down * 0.6f;
        MeshBuilder mb = new MeshBuilder(4);   // 0 blanco, 1 rojo, 2 metal oscuro, 3 cristal
        // torre troncocónica con bandas
        float[] bands = { 0f, 8f, 13f, 21f, 26f, 34f };
        for (int i = 0; i < bands.Length - 1; i++)
        {
            float y0 = bands[i], y1 = bands[i + 1];
            OceanGeo.Lathe(mb, i % 2, b, new[] { new Vector2(R(y0), y0), new Vector2(R(y1), y1) }, 20, 2.2f, 2.2f, false);
        }
        OceanGeo.Lathe(mb, 0, b, new[] { new Vector2(5.2f, -0.4f), new Vector2(5.2f, 0.6f), new Vector2(R(0.6f), 0.6f) }, 20, 2f, 2f, false);   // zócalo
        // galería y linterna
        OceanGeo.Lathe(mb, 2, b, new[] { new Vector2(R(34f), 34f), new Vector2(3.9f, 34.1f), new Vector2(3.9f, 34.45f), new Vector2(0.1f, 34.45f) }, 20, 2f, 2f, false);
        OceanGeo.Lathe(mb, 3, b, new[] { new Vector2(2.2f, 34.45f), new Vector2(2.2f, 38.2f) }, 16, 2f, 2f, false);
        OceanGeo.Lathe(mb, 2, b, new[] { new Vector2(2.5f, 38.2f), new Vector2(2.2f, 39.2f), new Vector2(0.9f, 40.6f), new Vector2(0.2f, 41.6f), new Vector2(0.01f, 41.7f) }, 16, 2f, 2f, false);
        for (int i = 0; i < 16; i++)
        {
            float a = i * Mathf.PI * 2f / 16f;
            Vector3 d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            OceanGeo.Tube(mb, 2, b + d * 3.8f + Vector3.up * 34.45f, b + d * 3.8f + Vector3.up * 35.5f, 0.035f, 4, 1f);
            if (i % 2 == 0) OceanGeo.Tube(mb, 2, b + d * 2.22f + Vector3.up * 34.45f, b + d * 2.22f + Vector3.up * 38.2f, 0.05f, 4, 1f);
        }
        for (int i = 0; i < 16; i++)
        {
            float a0 = i * Mathf.PI * 2f / 16f, a1 = (i + 1) * Mathf.PI * 2f / 16f;
            OceanGeo.Tube(mb, 2, b + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 3.8f + Vector3.up * 35.5f, b + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 3.8f + Vector3.up * 35.5f, 0.04f, 4, 1f);
        }
        // puerta (hacia el muelle, +x)
        OceanGeo.Box(mb, 2, b + new Vector3(R(0.6f) - 0.15f, 0.6f, -0.65f), b + new Vector3(R(0.6f) + 0.08f, 2.9f, 0.65f), 1f);
        Material glass = ProceduralAssets.CreateMaterial(new Color(0.25f, 0.3f, 0.32f), null, Vector2.one, 0.95f); track(glass);
        GameObject tower = OceanGeo.Emit(mb, "Tower", lg.transform, new[] { whiteM, redM, darkM, glass }, true, true, track);
        // silueta lejana (se intuye entre la niebla desde la casa)
        Material sil = OceanGeo.Unlit("Ocean/Silhouette", new Color(0.18f, 0.2f, 0.22f), track);
        if (sil.HasProperty("_FogCol")) sil.SetColor("_FogCol", cfg.fog);
        GameObject sg = new GameObject("Silhouette"); sg.transform.SetParent(lg.transform, false);
        sg.AddComponent<MeshFilter>().sharedMesh = tower.GetComponent<MeshFilter>().sharedMesh;
        MeshRenderer smr = sg.AddComponent<MeshRenderer>(); smr.sharedMaterial = sil; smr.shadowCastingMode = ShadowCastingMode.Off;
        lighthouse = OceanLighthouse.Create(this, lg.transform, b + Vector3.up * 36.3f, track);

        // muelle
        GameObject pg = new GameObject("Pier"); pg.transform.SetParent(root, false);
        MeshBuilder pb = new MeshBuilder(3);   // 0 tablas, 1 pilotes, 2 metal
        OceanGeo.Box(pb, 0, new Vector3(PierX0, PierY - 0.2f, PierZ0), new Vector3(PierX1 - 8.5f, PierY, PierZ1), 1.2f);
        OceanGeo.Box(pb, 0, new Vector3(PierX1 - 8.5f, PierY - 0.2f, PierZ0 - 3f), new Vector3(PierX1, PierY, PierZ1 + 3f), 1.2f);   // plataforma del final
        for (float x = PierX0 + 1f; x < PierX1; x += 3.5f)
        {
            bool wide = x > PierX1 - 8.5f;
            foreach (float z in wide ? new[] { PierZ0 - 2.9f, PierZ0, PierZ1, PierZ1 + 2.9f } : new[] { PierZ0 + 0.1f, PierZ1 - 0.1f })
                OceanGeo.Tube(pb, 1, new Vector3(x, SeabedY(x, z) - 0.5f, z), new Vector3(x, PierY - 0.2f, z), 0.17f, 7, 3f);
        }
        // barandillas (dejando el hueco de la escalera)
        for (int side = 0; side < 2; side++)
        {
            float z = side == 0 ? PierZ0 + 0.04f : PierZ1 - 0.04f;
            float xe = PierX1 - 8.5f;
            for (float x = PierX0 + 0.5f; x <= xe; x += 2f)
            {
                if (side == 0 && Mathf.Abs(x - PierLadderTop.x) < 1f) continue;
                OceanGeo.Box(pb, 0, new Vector3(x - 0.04f, PierY, z - 0.04f), new Vector3(x + 0.04f, PierY + 1.0f, z + 0.04f), 1f);
            }
            if (side == 0)
            {
                OceanGeo.Box(pb, 0, new Vector3(PierX0 + 0.5f, PierY + 0.96f, z - 0.04f), new Vector3(PierLadderTop.x - 0.7f, PierY + 1.04f, z + 0.04f), 1f);
                OceanGeo.Box(pb, 0, new Vector3(PierLadderTop.x + 0.7f, PierY + 0.96f, z - 0.04f), new Vector3(xe, PierY + 1.04f, z + 0.04f), 1f);
            }
            else OceanGeo.Box(pb, 0, new Vector3(PierX0 + 0.5f, PierY + 0.96f, z - 0.04f), new Vector3(xe, PierY + 1.04f, z + 0.04f), 1f);
        }
        // grúa de la jaula: dos postes y una viga sobre la jaula
        Vector3 cg = CageTop;
        foreach (float z in new[] { cg.z - 1.9f, cg.z + 1.9f })
            OceanGeo.Box(pb, 2, new Vector3(PierX1 - 0.5f, PierY, z - 0.15f), new Vector3(PierX1 - 0.2f, PierY + 5.6f, z + 0.15f), 1f);
        OceanGeo.Box(pb, 2, new Vector3(PierX1 - 0.5f, PierY + 5.6f, cg.z - 2.05f), new Vector3(PierX1 - 0.2f, PierY + 5.95f, cg.z + 2.05f), 1f);
        OceanGeo.Box(pb, 2, new Vector3(PierX1 - 0.5f, PierY + 5.6f, cg.z - 0.2f), new Vector3(cg.x + 0.3f, PierY + 5.95f, cg.z + 0.2f), 1f);
        OceanGeo.Box(pb, 2, new Vector3(PierX1 - 1.5f, PierY, cg.z + 2.1f), new Vector3(PierX1 - 0.4f, PierY + 1.1f, cg.z + 2.9f), 1f);   // torno
        OceanGeo.Emit(pb, "Deck", pg.transform, new[] { woodM, pileM, metalM }, true, true, track);
        Ladders.Add(OceanLadder.Create(this, pg.transform, new Vector3(PierLadderTop.x, PierY, PierZ0 - 0.06f), PierLadderFoot, PierLadderTop, Vector3.forward, track));
        BoatStops.Add(new Vector3(PierLadderFoot.x, 0f, PierZ0 - 3.6f));
        Obstacles.Add(new Obstacle { c = new Vector2((PierX0 + PierX1) * 0.5f, 250f), r = 0f });   // el muelle se trata aparte (rectángulo)
        CageBottom = new Vector3(cg.x, SeabedY(cg.x, cg.z) + 0.35f, cg.z);
        AddPoint(pg.transform, new Vector3(PierX1 - 3f, PierY + 3f, 250f), new Color(0.85f, 0.85f, 0.8f), 0.35f, 9f);
    }

    private static float R(float y) { return Mathf.Lerp(4.3f, 2.7f, y / 34f); }

    public bool InPierRect(Vector2 p, float pad)
    {
        if (p.x > PierX0 - pad && p.x < PierX1 - 8.5f + pad && p.y > PierZ0 - pad && p.y < PierZ1 + pad) return true;
        return p.x > PierX1 - 8.5f - pad && p.x < CageTop.x + 1.6f + pad && p.y > PierZ0 - 3f - pad && p.y < PierZ1 + 3f + pad;
    }

    // ---------------------------------------------------------------- gruta: túnel desde la fosa, chimenea y bolsa de aire con poza
    public readonly List<Vector3> TunnelSamples = new List<Vector3>();
    private void BuildCave()
    {
        GameObject cg = new GameObject("Cave"); cg.transform.SetParent(root, false);
        // camino suavizado (Catmull-Rom) por los puntos del túnel
        TunnelSamples.Clear();
        for (int k = 0; k < TunnelPath.Length - 1; k++)
        {
            Vector3 p0 = TunnelPath[Mathf.Max(0, k - 1)], p1 = TunnelPath[k], p2 = TunnelPath[k + 1], p3 = TunnelPath[Mathf.Min(TunnelPath.Length - 1, k + 2)];
            int n = Mathf.Max(2, Mathf.CeilToInt((p2 - p1).magnitude / 1.2f));
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                TunnelSamples.Add(0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
            }
        }
        TunnelSamples.Add(TunnelPath[TunnelPath.Length - 1]);
        MeshBuilder mb = new MeshBuilder(1);
        const int sides = 12;
        Vector3 prevN = Vector3.up;
        Vector3[] prevRing = null; float vAcc = 0f;
        for (int i = 0; i < TunnelSamples.Count; i++)
        {
            Vector3 p = TunnelSamples[i];
            Vector3 fwd = (i < TunnelSamples.Count - 1 ? TunnelSamples[i + 1] - p : p - TunnelSamples[i - 1]).normalized;
            Vector3 n0 = Vector3.ProjectOnPlane(prevN, fwd); if (n0.sqrMagnitude < 0.01f) n0 = Vector3.ProjectOnPlane(Vector3.right, fwd); n0.Normalize(); prevN = n0;
            Vector3 b0 = Vector3.Cross(fwd, n0);
            float tR = i / (float)(TunnelSamples.Count - 1);
            float rad = tR < 0.85f ? 2.5f : Mathf.Lerp(2.5f, 2.1f, (tR - 0.85f) / 0.15f);   // más ancho en el codo y el pozo; encaja con el agujero de la gruta (2.1) al final
            Vector3[] ring = new Vector3[sides];
            for (int s = 0; s < sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                float wob = 1f + 0.12f * (Mathf.PerlinNoise(i * 0.37f, s * 0.71f) - 0.5f) * 2f;
                if (i == TunnelSamples.Count - 1) wob = 1f;
                ring[s] = p + (n0 * Mathf.Cos(a) + b0 * Mathf.Sin(a)) * rad * wob;
            }
            if (prevRing != null)
            {
                float vStep = (p - TunnelSamples[i - 1]).magnitude / 2.5f;
                for (int s = 0; s < sides; s++)
                {
                    int s1 = (s + 1) % sides;
                    Vector3 a = prevRing[s], bq = prevRing[s1], c = ring[s1], d = ring[s];
                    Vector3 ctr = (a + bq + c + d) * 0.25f, axis = (TunnelSamples[i - 1] + p) * 0.5f;
                    Vector3 nIn = (axis - ctr).normalized;
                    QuadUV(mb, 0, a, bq, c, d, nIn, new Vector2(s / (float)sides * 3f, vAcc), new Vector2((s + 1) / (float)sides * 3f, vAcc + vStep));
                    // cara exterior (boca del túnel vista desde fuera)
                    if (i <= 6) QuadUV(mb, 0, a, bq, c, d, -nIn, new Vector2(s / (float)sides * 3f, vAcc), new Vector2((s + 1) / (float)sides * 3f, vAcc + vStep));
                }
                vAcc += vStep;
            }
            prevRing = ring;
        }
        // sala de la bolsa de aire (torno: suelo con el agujero del túnel, poza, repisa y bóveda)
        Vector3 c0 = new Vector3(PocketCenter.x, 0f, PocketCenter.z);
        Vector2[] prof = { new Vector2(2.1f, -24.5f), new Vector2(3.0f, -24.6f), new Vector2(3.3f, -22.6f), new Vector2(3.45f, -20.75f), new Vector2(5.4f, -20.7f), new Vector2(ChamberR, -19.2f), new Vector2(5.6f, -17.4f), new Vector2(3.6f, -16.2f), new Vector2(0.01f, -15.8f) };
        OceanGeo.Lathe(mb, 0, c0, prof, 18, 2.5f, 2.5f, true);
        GameObject cave = OceanGeo.Emit(mb, "Tunnel", cg.transform, new[] { caveM }, true, false, track);
        // rocas alrededor de la boca (tapan la junta con el fondo)
        Vector3 mouth = TunnelPath[0];
        for (int i = 0; i < 7; i++)
        {
            float a = i * 51f * Mathf.Deg2Rad;
            Vector3 dir = Quaternion.LookRotation((TunnelPath[0] - TunnelPath[1]).normalized) * new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            PlaceRock(cg.transform, mouth + dir * 3.1f + (TunnelPath[1] - TunnelPath[0]).normalized * 0.8f, 1.4f + (i % 3) * 0.5f, i * 77f, i % 3);
        }
        // brillo débil de la poza (algas luminosas): se ve al subir por la chimenea
        AddPoint(cg.transform, PocketCenter + Vector3.up * 1.5f, new Color(0.45f, 0.85f, 0.8f), 1.6f, 11f).renderMode = LightRenderMode.ForcePixel;
        AddPoint(cg.transform, PocketCenter + Vector3.down * 4.5f, new Color(0.35f, 0.8f, 0.75f), 1.1f, 9f).renderMode = LightRenderMode.ForcePixel;   // resplandor que baja por el pozo: guía desde el túnel
        GameObject ex = new GameObject("Exit"); ex.transform.SetParent(cg.transform, false); ex.transform.position = PocketCenter + Vector3.up * 0.1f;
        ExitTransform = ex.transform;
        GameManager.ExitTest = AtExit;
        // rocas de la isla (arriba y en la orilla)
        System.Random r = new System.Random(911);
        for (int i = 0; i < 26; i++)
        {
            float a = (float)r.NextDouble() * Mathf.PI * 2f, d = 9f + (float)r.NextDouble() * 14f;
            Vector3 p = IslandCenter + new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
            if (p.x > PierX0 - 4f && Mathf.Abs(p.z - 250f) < 3f) continue;
            p.y = SeabedY(p.x, p.z) - 0.3f;
            PlaceRock(root, p, 0.9f + (float)r.NextDouble() * 1.8f, (float)r.NextDouble() * 360f, i % 3);
        }
    }

    private static void QuadUV(MeshBuilder mb, int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 uv0, Vector2 uv1)
    {
        int i0 = mb.verts.Count;
        mb.verts.Add(a); mb.verts.Add(b); mb.verts.Add(c); mb.verts.Add(d);
        Vector3 tg = (b - a).normalized;
        for (int k = 0; k < 4; k++) { mb.normals.Add(n); mb.tangents.Add(new Vector4(tg.x, tg.y, tg.z, 1f)); }
        mb.uvs.Add(new Vector2(uv0.x, uv0.y)); mb.uvs.Add(new Vector2(uv1.x, uv0.y)); mb.uvs.Add(new Vector2(uv1.x, uv1.y)); mb.uvs.Add(new Vector2(uv0.x, uv1.y));
        Vector3 tn = Vector3.Cross(b - a, c - a);
        List<int> t = mb.tris[sub];
        if (Vector3.Dot(tn, n) >= 0f) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
        else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
    }

    private static Mesh[] rockMeshes;
    private static Material[] rockMats;
    public void PlaceRock(Transform parent, Vector3 pos, float scale, float yaw, int kind)
    {
        if (rockMeshes == null || rockMeshes[0] == null)
        {
            rockMeshes = new Mesh[3]; rockMats = new Material[3];
            for (int i = 0; i < 3; i++)
            {
                GameObject fbx = Resources.Load<GameObject>("Field/Props/FieldRock" + i);
                MeshFilter mf = fbx != null ? fbx.GetComponentInChildren<MeshFilter>() : null;
                rockMeshes[i] = mf != null ? mf.sharedMesh : null;
            }
        }
        if (rockMats[kind] == null)
        {
            rockMats[kind] = OceanGeo.Mat("Field/Props/FieldRock" + kind + "_a", "Field/Props/FieldRock" + kind + "_n", new Color(0.62f, 0.64f, 0.64f), 0.2f, 1f, o => { });
        }
        Mesh m = rockMeshes[kind];
        GameObject g;
        if (m == null) g = ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "Rock", parent, Vector3.zero, Vector3.one, rockM, true);
        else
        {
            g = new GameObject("Rock"); g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            g.AddComponent<MeshRenderer>().sharedMaterial = rockMats[kind];
            g.AddComponent<MeshCollider>().sharedMesh = m;
        }
        g.transform.position = pos;
        g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        g.transform.localScale = Vector3.one * scale;
    }

    // ---------------------------------------------------------------- luz del día (gris, de niebla)
    private void BuildLight()
    {
        GameObject g = new GameObject("OceanSun"); g.transform.SetParent(root, false);
        g.transform.rotation = Quaternion.Euler(52f, 35f, 0f);
        Light l = g.AddComponent<Light>();
        l.type = LightType.Directional; l.color = new Color(0.84f, 0.88f, 0.92f); l.intensity = 0.55f;
        l.shadows = LightShadows.Soft; l.shadowStrength = 0.55f;
        sun = l;
    }
    public Light sun;

    // ---------------------------------------------------------------- objetos: pilas, notas y demás en las casas (a ~10 cm del suelo)
    private void BuildSpots(System.Random rng)
    {
        PlayerStart = new Vector3(15f, 0.1f, 18f);
        // pilas: piso de arriba de la casa inicial y de las casas sobre pilotes
        BatterySpots.Add(new Vector3(17.6f, StartUpperY + 0.6f, 21.4f));
        for (int i = 0; i < Stilts.Count && BatterySpots.Count < 4; i += 2) BatterySpots.Add(Stilts[i].center + Quaternion.Euler(0f, Stilts[i].yaw, 0f) * new Vector3(2f, DeckY + 2.6f + 0.58f, 1.5f));
        // 9 extras: 3 notas, 2 aguas, 3 barras, 1 cinta
        List<Vector3> e = new List<Vector3>
        {
            new Vector3(13.5f, StartUpperY + 0.1f, 25.5f),                                           // nota 1: piso de arriba de la casa
            Stilts[0].center + Quaternion.Euler(0f, Stilts[0].yaw, 0f) * new Vector3(-2.0f, DeckY + 0.1f, 1.4f),   // nota 2
            new Vector3(PierX1 - 3f, PierY + 0.1f, PierZ0 - 1.8f),                                  // nota 3: plataforma del muelle
            Stilts[1].center + Quaternion.Euler(0f, Stilts[1].yaw, 0f) * new Vector3(1.8f, DeckY + 0.1f, -0.5f),
            Stilts[3].center + Quaternion.Euler(0f, Stilts[3].yaw, 0f) * new Vector3(-1.8f, DeckY + 0.1f, 0.5f),
            new Vector3(17.9f, StartUpperY + 0.1f, 26.6f),
            Stilts[2].center + Quaternion.Euler(0f, Stilts[2].yaw, 0f) * new Vector3(-2.2f, DeckY + 2.6f + 0.1f, -0.5f),
            Stilts[4].center + Quaternion.Euler(0f, Stilts[4].yaw, 0f) * new Vector3(1.6f, DeckY + 0.1f, 1.0f),
            Stilts[4].center + Quaternion.Euler(0f, Stilts[4].yaw, 0f) * new Vector3(-1.6f, DeckY + 2.6f + 0.1f, -0.4f),
        };
        ExtraSpots.AddRange(e);
        MonsterSpawns.Add(new Vector3(70f, -38f, 270f));
    }


    // ---------------------------------------------------------------- autoridad: peticiones de los jugadores
    public void HostRequest(int id, int op, int a, float x, float y, Vector3 p, Vector3 d)
    {
        switch (op)
        {
            case OceanNet.OpBoard:
            {
                if (boat == null || (cage != null && cage.IsRiderHost(id))) return;
                Vector3 from = PlayerPos(id);
                if (boat.HostBoard(id, from)) OceanNet.Event(OceanNet.EvSeat, id, boat.SeatOf(id), Vector3.zero, Vector3.zero);
                break;
            }
            case OceanNet.OpLeave:
                if (boat != null && boat.HostLeave(id)) OceanNet.Event(OceanNet.EvSeat, id, -1, Vector3.zero, Vector3.zero);
                break;
            case OceanNet.OpDrive:
                if (boat != null) boat.HostDrive(id, x, y);
                break;
            case OceanNet.OpHelmet:
                if (rack == null || rack.OwnerSlot(id) >= 0) return;
                if (a < 0 || a >= OceanHelmetRack.Count || rack.owner[a] >= 0) a = rack.FreeSlot();
                if (a < 0) return;
                OceanNet.Event(OceanNet.EvHelmet, a, id, Vector3.zero, Vector3.zero);
                break;
            case OceanNet.OpFlare:
                OceanNet.Event(OceanNet.EvFlare, id, 0, p, d);
                break;
            case OceanNet.OpDrown:
                OceanNet.HostKill(id, OceanNet.KillDrown);
                break;
        }
    }

    private Vector3 PlayerPos(int id)
    {
        foreach (OceanNet.Target t in OceanNet.Targets()) if (t.id == id) return t.pos;
        return new Vector3(9999f, 0f, 9999f);
    }

    public bool IsMountedHost(int id) { return (boat != null && boat.SeatOf(id) >= 0) || (cage != null && cage.IsRiderHost(id)); }
    public bool HasHelmetHost(int id) { return rack != null && rack.OwnerSlot(id) >= 0; }
    public int HelmetEvents { get; private set; }

    // ---------------------------------------------------------------- eventos (en todos los equipos)
    public void ApplyEvent(int ev, int a, int b, Vector3 p, Vector3 d)
    {
        GameManager gm = GameManager.Instance;
        int me = OceanNet.LocalId;
        switch (ev)
        {
            case OceanNet.EvSeat:
                if (boat == null) return;
                if (!OceanNet.Authority)
                {
                    for (int i = 0; i < OceanBoat.Seats; i++) if (boat.seat[i] == a) boat.seat[i] = -1;
                    if (b >= 0 && b < OceanBoat.Seats) boat.seat[b] = a;
                }
                if (a == me) LocalSeatChanged(b);
                break;
            case OceanNet.EvHelmet:
                if (rack == null) return;
                rack.SetOwner(a, b);
                HelmetEvents++;
                if (b == me && OceanPlayer.Local != null)
                {
                    OceanPlayer.Local.SetHelmet(true);
                    if (gm != null) gm.ShowToast("Casco de buzo puesto: aire ilimitado · F: lámpara · Espacio/Ctrl: subir/bajar");
                }
                else if (b != me) AttachRemoteHelmet(b);
                break;
            case OceanNet.EvFlare:
                OceanFlare.Spawn(a, p, d);
                if (a != me && NetSession.Active && gm != null) gm.ShowToast(PlayerName(a) + " ha disparado una bengala");
                break;
            case OceanNet.EvLighthouse:
                if (lighthouse != null) lighthouse.SetOn(a != 0);
                break;
        }
    }

    private void LocalSeatChanged(int seatIdx)
    {
        OceanPlayer lp = OceanPlayer.Local;
        if (lp == null || lp.fpc == null || boat == null) return;
        if (seatIdx >= 0)
        {
            lp.fpc.Mount(boat.transform, OceanBoat.SeatLocal[seatIdx]);
            if (GameManager.Instance != null) GameManager.Instance.ShowToast(seatIdx == 0 ? "Al timón: W/S avanzar · A/D girar · E bajar" : "En la barca: los peces no te alcanzan · E bajar");
        }
        else if (lp.fpc.mount == boat.transform) lp.fpc.Dismount(DismountSpot(boat.transform.position));
    }

    // al bajar de la barca: a la tabla más cercana (porche, muelle) si hay una a mano; si no, al agua junto a la barca
    public Vector3 DismountSpot(Vector3 c)
    {
        int mask = ~((1 << 2) | (1 << WaterVolume.WaterLayer) | (1 << 8));
        Vector3 best = Vector3.zero; float bd = 99f;
        for (int ring = 0; ring < 3; ring++)
        {
            float r = 1.9f + ring * 1.2f;
            for (int k = 0; k < 16; k++)
            {
                float a = k * Mathf.PI / 8f;
                Vector3 q = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (!Physics.Raycast(new Vector3(q.x, 3.2f, q.z), Vector3.down, out RaycastHit h, 4.5f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (h.normal.y < 0.8f || h.point.y < -0.7f || h.point.y > 2.2f) continue;
                if (boat != null && h.collider.transform.IsChildOf(boat.transform)) continue;
                if (Physics.CheckCapsule(h.point + Vector3.up * 0.45f, h.point + Vector3.up * 1.5f, 0.32f, mask, QueryTriggerInteraction.Ignore)) continue;
                float dd = (h.point - c).magnitude;
                if (dd < bd) { bd = dd; best = h.point + Vector3.up * 0.03f; }
            }
            if (bd < 99f) return best;
        }
        Vector3 side = boat != null ? boat.transform.right : Vector3.right;
        return c + side * 1.8f + Vector3.down * 1.3f;
    }

    private static string PlayerName(int id)
    {
        if (NetSession.Instance != null) foreach (NetPlayer p in NetSession.Instance.Players) if (p.id == id) return p.name;
        return "Alguien";
    }

    // casco en la cabeza de los compañeros (sólo aspecto)
    private Material helmetBrass, helmetGlass;
    private static bool HasRemoteHelmet(Transform t) { foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) if (c.name == "RemoteHelmet") return true; return false; }

    private static Transform FindDeep(Transform t, string n)
    {
        // primero el hueso exacto («Head»); si no, «mixamorig:Head»; nunca la malla «Character_Hazmat_Head» (pivote en los pies)
        Transform[] all = t.GetComponentsInChildren<Transform>(true);
        foreach (Transform c in all) if (c.name == n) return c;
        foreach (Transform c in all) if (c.name.EndsWith(":" + n) && c.GetComponent<Renderer>() == null) return c;
        return null;
    }

    private void AttachRemoteHelmet(int id)
    {
        if (NetSession.Instance == null) return;
        foreach (NetPlayer p in NetSession.Instance.Players)
        {
            if (p.id != id || p.avatar == null || HasRemoteHelmet(p.avatar.transform)) continue;
            AttachHelmetTo(p.avatar.transform);
        }
    }

    // casco sobre un avatar (compañero remoto o maniquí de las capturas): sigue al hueso de la cabeza si el modelo lo tiene
    public GameObject AttachHelmetTo(Transform avatar)
    {
        if (helmetBrass == null)
        {
            helmetBrass = OceanHelmetRack.BrassMaterial(track);
            helmetGlass = OceanHelmetRack.GlassMaterial(track);
        }
        GameObject h = OceanHelmetRack.BuildHelmet(avatar, helmetBrass, helmetGlass, track);
        h.name = "RemoteHelmet";
        h.transform.localPosition = new Vector3(0f, 1.38f, 0.02f);
        h.transform.localScale = Vector3.one * 1.05f;
        Transform head = FindDeep(avatar, "Head");
        if (head != null)
        {
            Vector3 up = avatar.up;
            h.transform.SetParent(head, true);
            Vector3 basePos = head.position - up * 0.13f;
            float sc = 1.05f;
            // la cabeza del traje (malla «…_Head») es más grande que un casco real: el casco la sustituye (se oculta mientras lo lleva)
            Renderer hr = null;
            foreach (Renderer r in avatar.GetComponentsInChildren<Renderer>(true))
                if (r is SkinnedMeshRenderer && r.enabled && r.name.EndsWith("_Head")) { hr = r; break; }
            if (hr != null)
            {
                sc = 1.4f;   // cúpula de ~0,56 m (la cabeza del traje mide ~0,5 m) y peto sobre los hombros
                basePos = head.position - up * 0.18f;
                hr.enabled = false;
                h.AddComponent<HelmetHeadRestore>().hidden = hr;
            }
            h.transform.position = basePos;
            h.transform.rotation = avatar.rotation;
            HelmetReflection hrf = h.AddComponent<HelmetReflection>();
            hrf.air = OceanHelmetRack.AirCube(track); hrf.water = OceanHelmetRack.WaterCube(track);
            float ls = Mathf.Max(0.0001f, head.lossyScale.x);
            h.transform.localScale = Vector3.one * (sc / ls);
        }
        return h;
    }


    // ---------------------------------------------------------------- red: bloque «mar» del snapshot (anfitrión → clientes)
    public void WriteNet(NetWriter w)
    {
        w.Byte(73);   // marca
        w.Float(boat.pos.x).Float(boat.pos.y).Float(boat.yaw).Float(boat.speed).Byte(boat.nextStop);
        for (int i = 0; i < OceanBoat.Seats; i++) w.Int(boat.seat[i]);
        w.Byte((int)cage.State).Float(cage.Y).Float(cage.stateT);
        w.Bool(lighthouse != null && lighthouse.On);
        for (int i = 0; i < OceanHelmetRack.Count; i++) w.Int(rack.owner[i]);
        w.Byte(fish.fish.Count);
        foreach (OceanFishSchool.Fish f in fish.fish) w.Vec3(f.pos).Vec3(f.vel).Byte((int)f.state).Int(f.target);
        w.Vec3(angler.pos).Vec3(angler.vel).Byte((int)angler.State).Int(angler.target);
    }

    private readonly int[] netSeats = new int[OceanBoat.Seats];
    public void ReadNet(NetReader r)
    {
        if (r.Byte() != 73) return;
        Vector2 bp = new Vector2(r.Float(), r.Float()); float by = r.Float(), bs = r.Float(); int ns = r.Byte();
        for (int i = 0; i < OceanBoat.Seats; i++) netSeats[i] = r.Int();
        int cs = r.Byte(); float cy = r.Float(), ct = r.Float();
        bool lh = r.Bool();
        int[] own = new int[OceanHelmetRack.Count];
        for (int i = 0; i < own.Length; i++) own[i] = r.Int();
        int nf = r.Byte();
        for (int i = 0; i < nf; i++) { Vector3 fp = r.Vec3(), fv = r.Vec3(); int st = r.Byte(), tg = r.Int(); fish.ApplyNet(i, fp, fv, st, tg); }
        Vector3 ap = r.Vec3(), av = r.Vec3(); int ast = r.Byte(), atg = r.Int();
        if (OceanNet.Authority) return;
        boat.ApplyNet(bp, by, bs, netSeats); boat.nextStop = ns;
        cage.ApplyNet(cs, cy, ct);
        if (lighthouse != null && lh && !lighthouse.On) lighthouse.SetOn(true);
        angler.ApplyNet(ap, av, ast, atg);
        int me = OceanNet.LocalId;
        for (int i = 0; i < own.Length; i++)
        {
            if (rack.owner[i] != own[i]) { rack.SetOwner(i, own[i]); if (own[i] >= 0 && own[i] != me) AttachRemoteHelmet(own[i]); }
            if (own[i] == me && OceanPlayer.Local != null && !OceanPlayer.Local.HasHelmet) OceanPlayer.Local.SetHelmet(true);
        }
        // reconciliar la plaza en la barca
        OceanPlayer lp = OceanPlayer.Local;
        if (lp != null && lp.fpc != null)
        {
            int seatIdx = boat.LocalSeat;
            bool onBoat = lp.fpc.mount == boat.transform;
            if (seatIdx >= 0 && !onBoat && !lp.fpc.IsMounted) LocalSeatChanged(seatIdx);
            else if (seatIdx < 0 && onBoat) LocalSeatChanged(-1);
        }
    }

    public int StructureCount { get { return Stilts.Count + 3; } }
}
