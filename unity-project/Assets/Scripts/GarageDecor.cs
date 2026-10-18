using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// v7.0.4: detalles del aparcamiento grande (todo en 2 mallas, sin colisión): topes de hormigón en filas con huecos y
// desplazamientos, números de planta en algunos pilares, carteles colgados de «RAMPA» (también en las rampas falsas) y
// «SALIDA» con flechas que mienten la mitad de las veces. Cerca de la salida real las flechas dicen la verdad (pista sutil).
// Los textos se dibujan en una textura con una fuente de píxeles propia (no hay TextMesh en la escena).
public static class GarageDecor
{
    public static int Signs { get; private set; }
    public static int TruthfulExitSigns { get; private set; }
    public static readonly List<Vector3> SignCenters = new List<Vector3>();   // local al generador (pruebas: ningún cartel junto a la salida)
    public static int WheelStops { get; private set; }

    // filas del atlas (512 x 512, celdas de 256 x 64)
    const int SalidaR = 0, SalidaL = 1, RampaR = 2, RampaL = 3, Deck0 = 4;   // Deck0 + d = «P-(d+1)»

    public static void Build(BackroomsGenerator gen, GaragePlan g, LevelConfig cfg, Transform parent, Material concrete, System.Func<Object, Object> track)
    {
        System.Random r = new System.Random(gen.UsedSeed * 389 + 61);
        Signs = 0; TruthfulExitSigns = 0; WheelStops = 0; SignCenters.Clear();
        float cs = g.cs, wh = g.wallH;

        // ---- topes ----
        MeshBuilder stops = new MeshBuilder(1);
        for (int x = 0; x < g.w; x++)
            for (int z = 0; z < g.h; z++)
            {
                if (g.IsRamp(x, z) || g.decoys.Contains(new Vector2Int(x, z))) continue;
                if (r.NextDouble() > 0.45) continue;
                float y = g.Y((x + 0.5f) * cs, (z + 0.5f) * cs);
                bool south = r.NextDouble() < 0.5;
                float zz = south ? z * cs + 0.55f + (float)r.NextDouble() * 0.2f : (z + 1) * cs - 0.55f - (float)r.NextDouble() * 0.2f;
                float xx = (x + 0.5f) * cs + ((float)r.NextDouble() - 0.5f) * 1.6f;
                float len = 1.5f + (float)r.NextDouble() * 0.3f;
                Vector3 c = new Vector3(xx, y + 0.06f, zz);
                stops.AddWallBox(0, c, new Vector3(len, 0.12f, 0.18f), 1f, 1f, (float)r.NextDouble());
                stops.AddQuad(0, new Vector3(xx - len * 0.5f, y + 0.12f, zz - 0.09f), new Vector3(xx + len * 0.5f, y + 0.12f, zz - 0.09f),
                    new Vector3(xx + len * 0.5f, y + 0.12f, zz + 0.09f), new Vector3(xx - len * 0.5f, y + 0.12f, zz + 0.09f), Vector3.up, Vector3.right, Vector3.forward, 1f, 1f, 0f, 0f);
                WheelStops++;
            }
        if (!stops.IsEmpty) Make(parent, "GarageWheelStops", (Mesh)track(stops.ToMesh("GarageWheelStops")), concrete);

        // ---- carteles ----
        Texture2D atlas = (Texture2D)track(BuildAtlas(g.decks));
        Material sm = (Material)track(ProceduralAssets.CreateMaterial(Color.white, atlas, Vector2.one, 0.15f));
        sm.SetTexture("_EmissionMap", atlas); sm.EnableKeyword("_EMISSION"); sm.SetColor("_EmissionColor", new Color(0.55f, 0.55f, 0.55f));
        sm.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        List<Vector3> v = new List<Vector3>(); List<Vector2> uv = new List<Vector2>(); List<int> tri = new List<int>(); List<Vector3> nr = new List<Vector3>();

        // números de planta en pilares (una cara al azar)
        for (int i = 2; i < g.w; i += 2)
            for (int j = 2; j < g.h; j += 2)
            {
                bool onBoundary = false; foreach (int b in g.boundaries) if (j == b) onBoundary = true;
                if (onBoundary || r.NextDouble() > 0.3) continue;
                int d = g.DeckOfRow(j);
                float y = g.DeckY(d) + 1.75f;
                int face = r.Next(4);
                Vector3 f = face == 0 ? Vector3.forward : face == 1 ? Vector3.right : face == 2 ? Vector3.back : Vector3.left;
                Vector3 c = new Vector3(i * cs, y, j * cs) + f * (cfg.pillarSize * 0.5f * 1.09f + 0.012f);
                Quad(v, uv, tri, nr, c, f, 0.62f, 0.16f, Deck0 + Mathf.Min(d, 7 - Deck0));
                Signs++;
            }

        // rampas reales y falsas: cartel colgado sobre la boca, mirando hacia la planta
        for (int b = 0; b < g.boundaries.Length; b++)
        {
            Vector2Int mouth = new Vector2Int(g.rampX[b], g.boundaries[b] - 2);
            Hanging(v, uv, tri, nr, g, mouth, Vector3.back, true, Vector3.forward, b + 1);
            for (int k = 0; k < g.decoys.Count; k++) if (g.DeckOfRow(g.decoyMouth[k].y) == b) Hanging(v, uv, tri, nr, g, g.decoyMouth[k], Vector3.back, true, Vector3.forward, b + 1);
        }
        // «SALIDA» con flecha: la mitad miente; en la última planta, cerca de la salida, todas dicen la verdad
        Vector2Int ex = gen.ExitCell;
        for (int d = 0; d < g.decks; d++)
        {
            int z0 = d * g.rows, z1 = d == g.decks - 1 ? g.h : (d + 1) * g.rows;
            Vector2Int goal = d < g.decks - 1 ? new Vector2Int(g.rampX[d], g.boundaries[d] - 1) : ex;
            for (int k = 0; k < 6; k++)
            {
                Vector2Int c = new Vector2Int(r.Next(g.w), z0 + r.Next(z1 - z0));
                if (g.IsRamp(c.x, c.y)) continue;
                int md = Mathf.Abs(c.x - ex.x) + Mathf.Abs(c.y - ex.y);
                if (d == g.decks - 1 && md <= 2) continue;   // nunca un cartel junto a la puerta: la salida se descubre explorando
                bool near = d == g.decks - 1 && md <= 6;
                bool truth = near || r.NextDouble() < 0.5;
                Vector2Int delta = goal - c;
                Vector3 t = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? new Vector3(Mathf.Sign(delta.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(delta.y));
                if (!truth) t = -t;
                if (t.sqrMagnitude < 0.5f) t = Vector3.right;
                Vector3 f = Vector3.Cross(Vector3.up, t).normalized;   // el cartel mira perpendicular a la flecha
                Hanging(v, uv, tri, nr, g, c, f, false, t, 0);
                if (truth) TruthfulExitSigns++;
            }
        }
        if (v.Count > 0)
        {
            Mesh m = new Mesh { name = "GarageSigns" };
            m.SetVertices(v); m.SetNormals(nr); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds(); m.RecalculateTangents();
            track(m);
            Make(parent, "GarageSigns", m, sm);
        }
    }

    // cartel colgado del techo de la celda, a doble cara; 'arrowTo' (dirección del mundo) decide qué flecha ve cada lado
    private static void Hanging(List<Vector3> v, List<Vector2> uv, List<int> tri, List<Vector3> nr, GaragePlan g, Vector2Int cell, Vector3 f, bool ramp, Vector3 arrowTo, int deckBelow)
    {
        float cs = g.cs;
        float y = g.Y((cell.x + 0.5f) * cs, (cell.y + 0.5f) * cs) + g.wallH - 0.55f;
        Vector3 c = new Vector3((cell.x + 0.5f) * cs, y, (cell.y + 0.5f) * cs);
        for (int side = 0; side < 2; side++)
        {
            Vector3 ff = side == 0 ? f : -f;
            Vector3 right = Vector3.Cross(Vector3.up, -ff);
            bool rightward = Vector3.Dot(right, arrowTo) >= 0f;
            int row = ramp ? (rightward ? RampaR : RampaL) : (rightward ? SalidaR : SalidaL);
            if (ramp && side == 1) row = Deck0 + Mathf.Min(deckBelow, 7 - Deck0);   // por detrás: la planta a la que (dice que) baja
            Quad(v, uv, tri, nr, c + ff * 0.006f, ff, 1.3f, 0.33f, row);
        }
        Signs++; SignCenters.Add(c);
    }

    private static void Quad(List<Vector3> v, List<Vector2> uv, List<int> tri, List<Vector3> nr, Vector3 c, Vector3 f, float wHalf2, float hHalf2, int row)
    {
        float hw = wHalf2 * 0.5f, hh = hHalf2 * 0.5f;
        Vector3 right = Vector3.Cross(Vector3.up, -f).normalized;
        int b = v.Count;
        v.Add(c - right * hw - Vector3.up * hh); v.Add(c - right * hw + Vector3.up * hh); v.Add(c + right * hw + Vector3.up * hh); v.Add(c + right * hw - Vector3.up * hh);
        int col = row / 8, rr = row % 8;
        float u0 = col * 0.5f, u1 = u0 + 0.5f, v1 = 1f - rr / 8f, v0 = v1 - 1f / 8f;
        uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u0, v1)); uv.Add(new Vector2(u1, v1)); uv.Add(new Vector2(u1, v0));
        for (int i = 0; i < 4; i++) nr.Add(f);
        tri.Add(b); tri.Add(b + 1); tri.Add(b + 2); tri.Add(b); tri.Add(b + 2); tri.Add(b + 3);
    }

    private static void Make(Transform parent, string name, Mesh m, Material mat)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // ---------------- atlas de carteles (fuente de píxeles 5x7) ----------------
    static readonly Dictionary<char, string[]> Font = new Dictionary<char, string[]>
    {
        { 'S', new[] { ".###.", "#...#", "#....", ".###.", "....#", "#...#", ".###." } },
        { 'A', new[] { ".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#" } },
        { 'L', new[] { "#....", "#....", "#....", "#....", "#....", "#....", "#####" } },
        { 'I', new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####" } },
        { 'D', new[] { "####.", "#...#", "#...#", "#...#", "#...#", "#...#", "####." } },
        { 'R', new[] { "####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#" } },
        { 'M', new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" } },
        { 'P', new[] { "####.", "#...#", "#...#", "####.", "#....", "#....", "#...." } },
        { '-', new[] { ".....", ".....", ".....", "#####", ".....", ".....", "....." } },
        { '1', new[] { "..#..", ".##..", "..#..", "..#..", "..#..", "..#..", ".###." } },
        { '2', new[] { ".###.", "#...#", "....#", "...#.", "..#..", ".#...", "#####" } },
        { '3', new[] { ".###.", "#...#", "....#", "..##.", "....#", "#...#", ".###." } },
        { '4', new[] { "...#.", "..##.", ".#.#.", "#..#.", "#####", "...#.", "...#." } },
        { '5', new[] { "#####", "#....", "####.", "....#", "....#", "#...#", ".###." } },
        { '6', new[] { ".###.", "#....", "####.", "#...#", "#...#", "#...#", ".###." } },
        { '7', new[] { "#####", "....#", "...#.", "..#..", ".#...", ".#...", ".#..." } },
        { '>', new[] { "..#..", "...#.", "#####", "#####", "...#.", "..#..", "....." } },
        { '<', new[] { "..#..", ".#...", "#####", "#####", ".#...", "..#..", "....." } },
        { 'v', new[] { "..#..", "..#..", "..#..", "#.#.#", ".###.", "..#..", "....." } },
        { ' ', new[] { ".....", ".....", ".....", ".....", ".....", ".....", "....." } },
    };

    public static Texture2D BuildAtlas(int decks)
    {
        const int W = 512, H = 512, CW = 256, CH = 64;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "GarageSignAtlas", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
        Color[] px = new Color[W * H];
        for (int i = 0; i < px.Length; i++) px[i] = new Color(0.1f, 0.1f, 0.1f, 1f);
        string[] labels = new string[16];
        labels[SalidaR] = "SALIDA >"; labels[SalidaL] = "< SALIDA"; labels[RampaR] = "v RAMPA >"; labels[RampaL] = "< RAMPA v";
        for (int d = 0; d < 4; d++) labels[Deck0 + d] = "P-" + (d + 1);
        for (int row = 0; row < 16; row++)
        {
            if (labels[row] == null) continue;
            int col = row / 8, rr = row % 8;
            int ox = col * CW, oy = H - (rr + 1) * CH;
            Color bg = row <= SalidaL ? new Color(0.05f, 0.38f, 0.16f) : row <= RampaL ? new Color(0.08f, 0.16f, 0.42f) : new Color(0.85f, 0.78f, 0.25f);
            Color fg = row >= Deck0 ? new Color(0.06f, 0.06f, 0.06f) : new Color(0.95f, 0.97f, 0.95f);
            for (int y = 2; y < CH - 2; y++) for (int x = 2; x < CW - 2; x++)
            {
                float grime = Mathf.PerlinNoise((ox + x) * 0.05f, (oy + y) * 0.05f) * 0.18f;
                px[(oy + y) * W + ox + x] = Color.Lerp(bg, new Color(0.15f, 0.13f, 0.1f), grime);
            }
            string s = labels[row]; int sc = 5;
            int tw = s.Length * 6 * sc - sc, sx = ox + (CW - tw) / 2, sy = oy + (CH - 7 * sc) / 2;
            for (int k = 0; k < s.Length; k++)
            {
                if (!Font.TryGetValue(s[k], out string[] g)) continue;
                for (int gy = 0; gy < 7; gy++) for (int gx = 0; gx < 5; gx++)
                {
                    if (g[gy][gx] != '#') continue;
                    for (int yy = 0; yy < sc; yy++) for (int xx = 0; xx < sc; xx++)
                    {
                        int X = sx + (k * 6 + gx) * sc + xx, Y = sy + (6 - gy) * sc + yy;
                        if (X >= 0 && X < W && Y >= 0 && Y < H) px[Y * W + X] = fg;
                    }
                }
            }
        }
        t.SetPixels(px); t.Apply(true, false);
        return t;
    }
}
