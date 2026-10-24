using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.5.0: detalles del hotel (nivel 3), solo visuales y sin colisión (las puertas sobresalen 4 cm de la pared): puertas de madera
// de 4 paneles con marco, manilla de latón y número de habitación de latón grabado en las paredes de los pasillos principales
// (impares a un lado, pares al otro; cada pasillo es una «planta»). Todo en mallas por bloques de 8 x 8 celdas (4 materiales).
public static class HotelDecor
{
    public static int Doors { get; private set; }
    public static readonly List<Vector3> DoorCenters = new List<Vector3>();   // local al generador (capturas y pruebas)
    public static readonly List<Vector3> DoorNormals = new List<Vector3>();
    const int SDoor = 0, SWood = 1, SBrass = 2, SNum = 3;
    const float DW = 0.92f, DH = 2.08f, FW = 0.08f;

    static Material Surface(string n, Color tint, System.Func<Object, Object> track)
    {
        Texture2D a = Resources.Load<Texture2D>("Textures/Hotel/" + n + "_a"), nm = Resources.Load<Texture2D>("Textures/Hotel/" + n + "_n"), m = Resources.Load<Texture2D>("Textures/Hotel/" + n + "_m");
        SurfaceSet s = a != null && nm != null && m != null ? new SurfaceSet { albedo = a, normal = nm, mask = m, baked = true } : null;
        Material mat = ProceduralAssets.CreateSurfaceMaterial(s, s != null ? tint : new Color(0.25f, 0.13f, 0.08f), 1f, 1f);
        track(mat); return mat;
    }

    public static void Build(BackroomsGenerator gen, HotelPlan plan, LevelConfig cfg, Transform parent, Dictionary<int, float> sconceAlong, System.Func<Object, Object> track)
    {
        Doors = 0; DoorCenters.Clear(); DoorNormals.Clear();
        System.Random r = new System.Random(gen.UsedSeed * 733 + 19);
        float cs = gen.CellSize, t = gen.wallThickness;
        int B = 8, bx = (plan.w + B - 1) / B, bz = (plan.h + B - 1) / B;
        MeshBuilder[,] mb = new MeshBuilder[bx, bz];
        for (int i = 0; i < bx; i++) for (int j = 0; j < bz; j++) mb[i, j] = new MeshBuilder(4);
        Vector2Int st = gen.StartCell, ex = gen.ExitCell;
        foreach (HotelPlan.Corridor c in plan.corridors)
        {
            int k = 0;
            for (int s = c.a; s <= c.b; s++)
            {
                int x = c.horizontal ? s : c.line, z = c.horizontal ? c.line : s;
                k++;
                if (plan.InHall(x, z) || (Mathf.Abs(x - st.x) <= 1 && Mathf.Abs(z - st.y) <= 1) || (x == ex.x && z == ex.y)) continue;
                int[] sides = c.horizontal ? new[] { 0, 2 } : new[] { 1, 3 };
                foreach (int d in sides)
                {
                    if (gen.IsCellOpen(x, z, d)) continue;
                    if (r.NextDouble() > 0.62) continue;
                    Vector3 n = -new Vector3(d == 1 ? 1 : d == 3 ? -1 : 0, 0f, d == 0 ? 1 : d == 2 ? -1 : 0);   // hacia el pasillo
                    Vector3 u = new Vector3(-n.z, 0f, n.x);
                    float along = ((float)r.NextDouble() - 0.5f) * 1.0f;
                    if (sconceAlong.TryGetValue((x * 1000 + z) * 4 + d, out float sa)) along = sa >= 0f ? -0.85f : 0.85f;   // lejos del aplique
                    Vector3 cc = new Vector3((x + 0.5f) * cs, 0f, (z + 0.5f) * cs) - n * (cs * 0.5f - t * 0.5f) + u * along;
                    int roomNo = (c.number % 9 + 1) * 100 + Mathf.Min(99, k * 2 + (d >= 2 ? 1 : 0));
                    AddDoor(mb[x / B, z / B], cc, n, u, roomNo, r);
                    DoorCenters.Add(cc + Vector3.up * 1.2f); DoorNormals.Add(n); Doors++;
                }
            }
        }
        Material door = Surface("HotelDoor", Color.white, track), wood = Surface("HotelWood", Color.white, track), num = Surface("HotelNumbers", Color.white, track);
        Material brass = (Material)track(ProceduralAssets.CreateMaterial(new Color(0.62f, 0.46f, 0.24f), null, Vector2.one, 0.62f));
        if (brass.HasProperty("_Metallic")) brass.SetFloat("_Metallic", 0.92f);
        Material[] mats = { door, wood, brass, num };
        for (int i = 0; i < bx; i++) for (int j = 0; j < bz; j++)
        {
            if (mb[i, j].IsEmpty) continue;
            GameObject go = new GameObject("HotelDoors_" + i + "_" + j);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = (Mesh)track(mb[i, j].ToMesh("HotelDoors_" + i + "_" + j));
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }

    // cc: centro de la base de la puerta sobre la cara de la pared; n: normal hacia el pasillo; u: a lo largo de la pared
    static void AddDoor(MeshBuilder m, Vector3 cc, Vector3 n, Vector3 u, int number, System.Random r)
    {
        Vector3 up = Vector3.up; float dep = 0.035f;
        Vector3 o = cc + n * dep;
        // hoja (cara frontal con la textura entera, cantos oscuros)
        Vector3 a0 = o - u * DW * 0.5f, a1 = o + u * DW * 0.5f;
        m.AddQuadUV(SDoor, a0, a1, a1 + up * DH, a0 + up * DH, n, u, up, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
        m.AddQuad(SWood, a1, a1 - n * dep, a1 - n * dep + up * DH, a1 + up * DH, u, -n, up, 1f, 1f, 0f, 0f);
        m.AddQuad(SWood, a0 - n * dep, a0, a0 + up * DH, a0 - n * dep + up * DH, -u, n, up, 1f, 1f, 0f, 0f);
        m.AddQuad(SWood, a0 + up * DH, a1 + up * DH, a1 + up * DH - n * dep, a0 + up * DH - n * dep, up, u, -n, 1f, 1f, 0f, 0f);
        // marco (jambas y dintel), 8 cm de ancho, 5 cm de vuelo
        float fd = 0.05f;
        Box(m, SWood, cc - u * (DW * 0.5f + FW * 0.5f) + up * (DH + FW) * 0.5f, u, n, FW, DH + FW, fd);
        Box(m, SWood, cc + u * (DW * 0.5f + FW * 0.5f) + up * (DH + FW) * 0.5f, u, n, FW, DH + FW, fd);
        Box(m, SWood, cc + up * (DH + FW * 0.5f), u, n, DW + 2f * FW, FW, fd);
        // manilla de latón (placa + palanca) a la derecha mirando la puerta
        Vector3 hp = o + u * (DW * 0.5f - 0.09f) + up * 1.0f;
        Box(m, SBrass, hp + up * 0.02f + n * 0.004f, u, n, 0.045f, 0.20f, 0.008f);
        Box(m, SBrass, hp + n * 0.045f, u, n, 0.022f, 0.022f, 0.075f);
        Box(m, SBrass, hp + n * 0.075f - u * 0.05f, u, n, 0.12f, 0.02f, 0.02f);
        // número de latón grabado (3 cifras) a 1,55 m; mirilla
        string s = number.ToString("000");
        float cw = 0.05f, chh = 0.10f; Vector3 pc = o + up * 1.55f + n * 0.004f;
        for (int i = 0; i < 3; i++)
        {
            int dgt = s[i] - '0';
            Vector3 c0 = pc + u * ((i - 1) * cw) - u * cw * 0.5f - up * chh * 0.5f;
            float u0 = dgt / 10f + 0.004f, u1 = (dgt + 1) / 10f - 0.004f;
            m.AddQuadUV(SNum, c0, c0 + u * cw, c0 + u * cw + up * chh, c0 + up * chh, n, u, up, new Vector2(u0, 0.03f), new Vector2(u1, 0.03f), new Vector2(u1, 0.97f), new Vector2(u0, 0.97f));
        }
        Box(m, SBrass, o + up * 1.42f + n * 0.006f, u, n, 0.022f, 0.022f, 0.012f);
    }

    static void Box(MeshBuilder m, int sub, Vector3 c, Vector3 u, Vector3 n, float wu, float hy, float dn)
    {
        Vector3 up = Vector3.up; Vector3 hu = u * wu * 0.5f, hv = up * hy * 0.5f, f = n * dn;
        Vector3 b = c - n * 0f;   // la cara trasera queda sobre la pared
        Vector3 p00 = b - hu - hv, p10 = b + hu - hv, p11 = b + hu + hv, p01 = b - hu + hv;
        m.AddQuad(sub, p00 + f, p10 + f, p11 + f, p01 + f, n, u, up, 0.5f, 0.5f, 0f, 0f);
        m.AddQuad(sub, p10, p10 + f, p11 + f, p11, u, n, up, 0.5f, 0.5f, 0f, 0f);
        m.AddQuad(sub, p00 + f, p00, p01, p01 + f, -u, n, up, 0.5f, 0.5f, 0f, 0f);
        m.AddQuad(sub, p01 + f, p11 + f, p11, p01, up, u, n, 0.5f, 0.5f, 0f, 0f);
        m.AddQuad(sub, p00, p10, p10 + f, p00 + f, -up, u, n, 0.5f, 0.5f, 0f, 0f);
    }
}
