using System.Collections.Generic;
using UnityEngine;

// v7.5.0: plano completo del hotel abandonado (nivel 3). Ya no es un laberinto: es un edificio.
//  - Planta baja: el ascensor de llegada da al vestíbulo con la recepción (y ascensores muertos), comedor y salón de baile,
//    y la galería (pasillo ancho) que los une; escalera de servicio al fondo.
//  - Encima, 5 «plantas» (franjas de 5 filas): habitaciones de 1 x 2 celdas a los dos lados de un pasillo larguísimo con
//    un rellano de ascensores muertos en medio. Algunas habitaciones se abren (puerta de verdad, con cama, mesillas, lámpara,
//    armario, baño...); el resto están cerradas con llave (celdas fuera del juego).
//  - Pasillos de servicio (lencería): las columnas de los extremos, filas entre algunas plantas y pasos de servicio que cruzan
//    las habitaciones; con cortes (callejones sin salida), derrumbes que cierran un pasillo, escaleras de incendios bloqueadas.
// Todo determinista por semilla (igual en MP). Se garantiza que todo lo transitable se alcanza desde el ascensor.
public class HotelPlan
{
    public enum K : byte { Void, Corridor, Linen, Hall, Room, Lift, Stair, Start, Shaft, Gallery }
    public struct Corridor { public bool horizontal; public int line, a, b; public int number; }
    public struct Hall { public RectInt r; public string kind; public Vector3 spot; }
    public class Room { public Vector2Int entry, bed; public int doorDir; public bool open; public int number; public int bathSide = -1; }
    public struct Edge { public Vector2Int c; public int d; public int kind; public int number; }   // celda + lado
    public struct Item { public int k; public Vector3 p; public float yaw; public Vector3 s; public int v; public float tilt; public bool solid; }
    public struct View { public string name; public Vector3 pos, look; }

    // tipos de doorway / puerta
    public const int DoorRoom = 0, DoorStaff = 1, DoorBath = 2, DoorFire = 3, DoorOffice = 4;
    // objetos
    public const int IBed = 1, INight = 2, ILamp = 3, IWardrobe = 4, IDesk = 5, IChair = 6, IArmchair = 7, ISofa = 8, ICoffee = 9,
        IPainting = 10, IFallenFrame = 11, IPlant = 12, ICart = 13, IReception = 14, IKeyBoard = 15, IRoundTable = 16, ISquareTable = 17,
        IChandelier = 18, IColumn = 19, IStairs = 20, ILinenShelf = 21, ILaundry = 22, IDebris = 23, ISuitcase = 24, IPiano = 25, IBench = 26,
        IAshtray = 27, ISmallChand = 28, IBellCart = 29, IRubble = 30;

    public readonly List<Corridor> corridors = new List<Corridor>();
    public readonly List<Hall> halls = new List<Hall>();
    public readonly List<Room> rooms = new List<Room>();
    public readonly List<Edge> doorways = new List<Edge>();    // huecos de puerta abiertos en la rejilla (jambas + hoja que se abre)
    public readonly List<Edge> fakeDoors = new List<Edge>();   // puertas cerradas pegadas a un muro
    public readonly List<Edge> liftDoors = new List<Edge>();
    public readonly List<Edge> blocked = new List<Edge>();     // derrumbes: muro + escombros
    public readonly List<Item> items = new List<Item>();
    public readonly List<View> views = new List<View>();
    public K[,] kind;
    public bool[,] corridorCell; public int[,] hallOf;
    public int w, h; public float cs, t, wh;
    public Vector2Int start;
    public bool mirrored;
    bool[,] V, H;

    static readonly Vector2Int[] D = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };
    public static Vector3 Dir3(int d) { return new Vector3(D[d].x, 0f, D[d].y); }
    public bool InHall(int x, int z) { return x >= 0 && z >= 0 && x < w && z < h && hallOf[x, z] >= 0; }
    public K At(int x, int z) { return x >= 0 && z >= 0 && x < w && z < h ? kind[x, z] : K.Void; }
    public bool Walkable(int x, int z) { K k = At(x, z); return k != K.Void && k != K.Shaft; }
    public bool ExitAllowed(int x, int z) { K k = At(x, z); return k == K.Corridor || k == K.Linen || k == K.Gallery; }
    public bool IsOpen(int x, int z, int d)
    {
        switch (d) { case 0: return !H[x, z + 1]; case 1: return !V[x + 1, z]; case 2: return !H[x, z]; default: return !V[x, z]; }
    }
    void Set(int x, int z, int d, bool wall)
    {
        switch (d) { case 0: H[x, z + 1] = wall; break; case 1: V[x + 1, z] = wall; break; case 2: H[x, z] = wall; break; default: V[x, z] = wall; break; }
    }
    void Open(int x, int z, int d) { Set(x, z, d, false); }
    bool In(int x, int z) { return x >= 0 && z >= 0 && x < w && z < h; }
    public Vector3 Center(int x, int z) { return new Vector3((x + 0.5f) * cs, 0f, (z + 0.5f) * cs); }

    public static HotelPlan Make(int seed, int w, int h, float cs, float t, float wh, bool[,] wallsV, bool[,] wallsH)
    {
        HotelPlan p = new HotelPlan { w = w, h = h, cs = cs, t = t, wh = wh, V = wallsV, H = wallsH };
        p.kind = new K[w, h]; p.corridorCell = new bool[w, h]; p.hallOf = new int[w, h];
        for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) wallsV[i, j] = true;
        for (int i = 0; i < w; i++) for (int j = 0; j <= h; j++) wallsH[i, j] = true;
        System.Random r = new System.Random(seed);
        p.Topology(r);
        p.mirrored = r.Next(2) == 1;
        if (p.mirrored) p.Mirror();
        p.Connect();
        for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) p.hallOf[i, j] = -1;
        for (int k = 0; k < p.halls.Count; k++) { RectInt b = p.halls[k].r; for (int i = b.x; i < b.xMax; i++) for (int j = b.y; j < b.yMax; j++) p.hallOf[i, j] = k; }
        p.Furnish(new System.Random(seed * 31 + 5));
        return p;
    }

    // ---------------------------------------------------------------- topología (sin espejo)
    void Rect(int x0, int z0, int rw, int rh, K k)
    {
        for (int i = x0; i < x0 + rw; i++) for (int j = z0; j < z0 + rh; j++)
        {
            kind[i, j] = k;
            if (i > x0) Open(i, j, 3);
            if (j > z0) Open(i, j, 2);
        }
    }
    void Run(bool horiz, int line, int a, int b, K k)
    {
        for (int s = a; s <= b; s++)
        {
            int x = horiz ? s : line, z = horiz ? line : s;
            kind[x, z] = k;
            if (s > a) Open(x, z, horiz ? 3 : 2);
        }
    }
    void Topology(System.Random r)
    {
        int lobW = Mathf.Min(7, w / 3), dinW = Mathf.Max(3, Mathf.Min(5, w - lobW - 10)), ballX = 1 + lobW + dinW, ballW = w - 1 - ballX;
        int G = Mathf.Min(6, h / 5);   // filas de la planta baja; la galería en la fila G
        start = new Vector2Int(0, 0); kind[0, 0] = K.Start; Open(0, 0, 1);
        Rect(1, 0, lobW, G, K.Hall); halls.Add(new Hall { r = new RectInt(1, 0, lobW, G), kind = "vestibulo" });
        Rect(1 + lobW, 0, dinW, G, K.Hall); halls.Add(new Hall { r = new RectInt(1 + lobW, 0, dinW, G), kind = "comedor" });
        Rect(ballX, 0, ballW, G, K.Hall); halls.Add(new Hall { r = new RectInt(ballX, 0, ballW, G), kind = "salon" });
        // columna de servicio este de la planta baja: escalera al fondo (celda 0) y pasillo hasta la galería
        Run(false, w - 1, 1, G - 1, K.Linen); kind[w - 1, 0] = K.Stair; Open(w - 1, 0, 0); Open(w - 1, G - 1, 0);
        // galería
        Run(true, G, 0, w - 1, K.Gallery); corridors.Add(new Corridor { horizontal = true, line = G, a = 0, b = w - 1, number = 0 });
        // vestíbulo <-> galería (entrada ancha), <-> comedor (arco doble); comedor <-> salón; salón <-> galería (puertas dobles)
        int ga = 2 + r.Next(2); for (int i = ga; i < ga + 2; i++) Open(i, G - 1, 0);
        Open(lobW, 1, 1); Open(lobW, 2, 1);
        Open(1 + lobW + dinW / 2, G - 1, 0);
        Open(ballX - 1, 2 + r.Next(2), 1);
        int gb = ballX + ballW / 2 - 1; Open(gb, G - 1, 0); Open(gb + 1, G - 1, 0);
        doorways.Add(new Edge { c = new Vector2Int(w - 2, 1), d = 1, kind = DoorStaff }); Open(w - 2, 1, 1);   // salón -> servicio
        // oficina tras la recepción (columna 0, cerrada) con ascensores muertos en el muro del vestíbulo
        for (int j = 1; j < G; j++) kind[0, j] = K.Void;
        for (int j = 1; j <= Mathf.Min(3, G - 1); j++) liftDoors.Add(new Edge { c = new Vector2Int(1, j), d = 3 });
        fakeDoors.Add(new Edge { c = new Vector2Int(1, G - 1), d = 3, kind = DoorOffice });

        // franjas de habitaciones: 5 filas cada una; filas de lencería en los huecos que sobren
        int z = G + 1, rest = h - z, nb = Mathf.Max(1, rest / 5), extra = rest - nb * 5;
        bool[] linenAfter = new bool[nb + 1];
        List<int> gaps = new List<int>(); for (int k = 1; k < nb; k++) gaps.Add(k);
        for (int e = 0; e < extra && gaps.Count > 0; e++) { int gi = r.Next(gaps.Count); linenAfter[gaps[gi]] = true; gaps.RemoveAt(gi); }
        int lx = w / 2 - 1 + r.Next(2) - (r.Next(3) == 0 ? 1 : 0);
        int lastCorr = G; int floor = 1;
        for (int b = 0; b < nb; b++)
        {
            if (linenAfter[b] && b > 0)
            {
                Run(true, z, 0, w - 1, K.Linen); corridors.Add(new Corridor { horizontal = true, line = z, a = 0, b = w - 1, number = -1 });
                int cut = 3 + r.Next(w - 6); if (r.Next(2) == 0) Set(cut, z, 1, true);   // corte: callejón
                z++;
            }
            if (z + 5 > h) break;
            int zA0 = z, zA1 = z + 1, zc = z + 2, zB0 = z + 3, zB1 = z + 4;
            Run(true, zc, 1, w - 2, K.Corridor); corridors.Add(new Corridor { horizontal = true, line = zc, a = 1, b = w - 2, number = floor });
            for (int x = 1; x < w - 1; x++) corridorCell[x, zc] = true;
            // rellano de ascensores
            for (int i = lx; i <= lx + 1; i++) { kind[i, zA0] = K.Shaft; kind[i, zB1] = K.Shaft; }
            Rect(lx, zA1, 2, 3, K.Lift);
            for (int i = lx; i <= lx + 1; i++) { liftDoors.Add(new Edge { c = new Vector2Int(i, zA1), d = 2 }); liftDoors.Add(new Edge { c = new Vector2Int(i, zB0), d = 0 }); }
            // habitaciones
            int seqA = 1, seqB = 2;
            for (int x = 1; x < w - 1; x++)
            {
                if (x == lx || x == lx + 1) { seqA += 2; seqB += 2; continue; }
                rooms.Add(new Room { entry = new Vector2Int(x, zA1), bed = new Vector2Int(x, zA0), doorDir = 0, number = floor * 100 + seqA });
                rooms.Add(new Room { entry = new Vector2Int(x, zB0), bed = new Vector2Int(x, zB1), doorDir = 2, number = floor * 100 + seqB });   // doorDir: de la entrada al pasillo
                seqA += 2; seqB += 2;
            }
            // derrumbe que corta el pasillo (40 %)
            if (r.NextDouble() < 0.4)
            {
                int bx = 2 + r.Next(w - 5); if (bx == lx - 1 || bx == lx || bx == lx + 1) bx = Mathf.Max(2, lx - 3);
                blocked.Add(new Edge { c = new Vector2Int(bx, zc), d = 1 });
            }
            // paso de servicio hacia la galería / lencería / planta anterior (1-2 por hueco), cruzando la fila de habitaciones
            int np = 1 + (r.NextDouble() < 0.4 ? 1 : 0);
            for (int q = 0; q < np; q++)
            {
                int px = 0;
                for (int tries = 0; tries < 20; tries++) { px = 2 + r.Next(w - 4); if (Mathf.Abs(px - lx) > 1 && px != lx + 2 && px != lx - 1) break; }
                PendingPass.Add(new Vector3Int(px, lastCorr, zc));
            }
            lastCorr = zc; floor++;
            z += 5;
        }
        // columnas de servicio en los extremos (desde la galería hasta arriba), con escaleras de incendios al final
        Run(false, 0, G, h - 1, K.Linen); kind[0, G] = K.Gallery;
        Run(false, w - 1, G, h - 1, K.Linen); kind[w - 1, G] = K.Gallery;
        corridors.Add(new Corridor { horizontal = false, line = 0, a = G, b = h - 1, number = -1 });
        corridors.Add(new Corridor { horizontal = false, line = w - 1, a = G, b = h - 1, number = -1 });
        Open(0, G, 1); Open(w - 1, G, 3);
        kind[0, h - 1] = K.Stair; kind[w - 1, h - 1] = K.Stair;
        // cortes en las columnas (callejones; Connect reabre si hace falta)
        for (int s = 0; s < 2; s++)
        {
            int cx = s == 0 ? 0 : w - 1, cz = G + 3 + r.Next(Mathf.Max(1, h - G - 6));
            Set(cx, cz, 0, true);
        }
        // pasillos de cada planta -> columnas de servicio (puertas de personal)
        foreach (Corridor c in corridors)
        {
            if (c.number <= 0 || !c.horizontal) continue;
            Open(0, c.line, 1); doorways.Add(new Edge { c = new Vector2Int(1, c.line), d = 3, kind = DoorStaff });
            Open(w - 1, c.line, 3); doorways.Add(new Edge { c = new Vector2Int(w - 2, c.line), d = 1, kind = DoorStaff });
        }
        // filas de lencería -> columnas (ya abiertas por Run de 0 a w-1)
        // pasos de servicio
        foreach (Vector3Int pp in PendingPass)
        {
            int px = pp.x, za = pp.y, zb = pp.z;
            for (int j = za + 1; j < zb; j++)
            {
                if (kind[px, j] == K.Lift || kind[px, j] == K.Shaft) { break; }
                kind[px, j] = K.Linen;
                if (j > za + 1) Open(px, j, 2);
            }
            doorways.Add(new Edge { c = new Vector2Int(px, za + 1), d = 2, kind = DoorStaff }); Open(px, za + 1, 2);
            doorways.Add(new Edge { c = new Vector2Int(px, zb - 1), d = 0, kind = DoorStaff }); Open(px, zb - 1, 0);
        }
        PendingPass.Clear();
        // habitaciones: las que no quedaron pisadas por un paso; 30 % abiertas (al menos 4 por planta)
        List<Room> keep = new List<Room>();
        foreach (Room rm in rooms)
            if (kind[rm.entry.x, rm.entry.y] == K.Void && kind[rm.bed.x, rm.bed.y] == K.Void) keep.Add(rm);
        rooms.Clear(); rooms.AddRange(keep);
        Dictionary<int, int> openPerFloor = new Dictionary<int, int>();
        foreach (Room rm in rooms)
        {
            rm.open = r.NextDouble() < 0.30;
            int f = rm.number / 100; if (!openPerFloor.ContainsKey(f)) openPerFloor[f] = 0;
            if (rm.open) openPerFloor[f]++;
        }
        foreach (Room rm in rooms)
        {
            int f = rm.number / 100;
            if (!rm.open && openPerFloor[f] < 4 && r.NextDouble() < 0.35) { rm.open = true; openPerFloor[f]++; }
        }
        foreach (Room rm in rooms)
        {
            Vector2Int e = rm.entry, b = rm.bed;
            if (rm.open)
            {
                kind[e.x, e.y] = K.Room; kind[b.x, b.y] = K.Room;
                Open(e.x, e.y, b.y > e.y ? 0 : 2);   // entrada <-> dormitorio
                int dd = rm.doorDir;
                Open(e.x, e.y, dd);
                doorways.Add(new Edge { c = e, d = dd, kind = DoorRoom, number = rm.number });
            }
            else
            {
                int dd = rm.doorDir;
                fakeDoors.Add(new Edge { c = e, d = dd, kind = DoorRoom, number = rm.number });
            }
        }
        // los derrumbes cierran el pasillo
        foreach (Edge bl in blocked) Set(bl.c.x, bl.c.y, bl.d, true);
        // muros entre celdas fuera del juego: no hacen falta (menos geometría)
        for (int i = 1; i < w; i++) for (int j = 0; j < h; j++) if (!Walkable(i - 1, j) && !Walkable(i, j)) V[i, j] = false;
        for (int i = 0; i < w; i++) for (int j = 1; j < h; j++) if (!Walkable(i, j - 1) && !Walkable(i, j)) H[i, j] = false;
        // muro del borde exterior en las celdas fuera del juego: tampoco
        for (int j = 0; j < h; j++) { if (!Walkable(0, j)) V[0, j] = false; if (!Walkable(w - 1, j)) V[w, j] = false; }
        for (int i = 0; i < w; i++) { if (!Walkable(i, 0)) H[i, 0] = false; if (!Walkable(i, h - 1)) H[i, h] = false; }
        // escaleras: callejones (solo se entra por un lado)
    }
    static readonly List<Vector3Int> PendingPass = new List<Vector3Int>();

    void Mirror()
    {
        bool[,] nv = new bool[w + 1, h], nh = new bool[w, h + 1];
        for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) nv[i, j] = V[w - i, j];
        for (int i = 0; i < w; i++) for (int j = 0; j <= h; j++) nh[i, j] = H[w - 1 - i, j];
        for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) V[i, j] = nv[i, j];
        for (int i = 0; i < w; i++) for (int j = 0; j <= h; j++) H[i, j] = nh[i, j];
        K[,] nk = new K[w, h]; bool[,] nc = new bool[w, h];
        for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) { nk[i, j] = kind[w - 1 - i, j]; nc[i, j] = corridorCell[w - 1 - i, j]; }
        kind = nk; corridorCell = nc;
        Vector2Int M(Vector2Int c) { return new Vector2Int(w - 1 - c.x, c.y); }
        int MD(int d) { return d == 1 ? 3 : d == 3 ? 1 : d; }
        start = M(start);
        for (int k = 0; k < halls.Count; k++) { Hall hl = halls[k]; hl.r = new RectInt(w - hl.r.xMax, hl.r.y, hl.r.width, hl.r.height); halls[k] = hl; }
        for (int k = 0; k < corridors.Count; k++)
        {
            Corridor c = corridors[k];
            if (c.horizontal) { int a = w - 1 - c.b, b = w - 1 - c.a; c.a = a; c.b = b; } else c.line = w - 1 - c.line;
            corridors[k] = c;
        }
        foreach (Room rm in rooms) { rm.entry = M(rm.entry); rm.bed = M(rm.bed); rm.doorDir = MD(rm.doorDir); }
        foreach (List<Edge> l in new[] { doorways, fakeDoors, liftDoors, blocked })
            for (int k = 0; k < l.Count; k++) { Edge e = l[k]; e.c = M(e.c); e.d = MD(e.d); l[k] = e; }
    }

    // Todo lo transitable, alcanzable desde el ascensor (determinista): se reabren cortes de pasillos de servicio/galería
    void Connect()
    {
        for (int guard = 0; guard < w * h; guard++)
        {
            bool[,] seen = new bool[w, h]; Queue<Vector2Int> q = new Queue<Vector2Int>(); q.Enqueue(start); seen[start.x, start.y] = true;
            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!IsOpen(c.x, c.y, d)) continue;
                    Vector2Int n = c + D[d];
                    if (!In(n.x, n.y) || seen[n.x, n.y] || !Walkable(n.x, n.y)) continue;
                    seen[n.x, n.y] = true; q.Enqueue(n);
                }
            }
            bool did = false;
            for (int i = 0; i < w && !did; i++)
                for (int j = 0; j < h && !did; j++)
                {
                    if (seen[i, j] || !Walkable(i, j)) continue;
                    K ki = kind[i, j];
                    if (ki != K.Linen && ki != K.Corridor && ki != K.Gallery && ki != K.Stair) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int n = new Vector2Int(i, j) + D[d];
                        if (!In(n.x, n.y) || !seen[n.x, n.y]) continue;
                        K kn = kind[n.x, n.y];
                        if (kn != K.Linen && kn != K.Corridor && kn != K.Gallery) continue;
                        if (kn != ki && !(ki == K.Linen && kn == K.Gallery) && !(kn == K.Linen && ki == K.Gallery)) continue;
                        Open(i, j, d); did = true;
                        for (int b = blocked.Count - 1; b >= 0; b--)
                        {
                            Edge e = blocked[b];
                            if ((e.c.x == i && e.c.y == j && e.d == d) || (e.c + D[e.d] == new Vector2Int(i, j) && (e.d + 2) % 4 == d)) blocked.RemoveAt(b);
                        }
                        break;
                    }
                }
            if (!did) return;
        }
    }

    // ---------------------------------------------------------------- mobiliario (coordenadas locales del nivel, metros)
    void Add(int k, Vector3 p, float yaw, Vector3 s, int v = 0, bool solid = true, float tilt = 0f)
    {
        items.Add(new Item { k = k, p = p, yaw = yaw, s = s, v = v, solid = solid, tilt = tilt });
    }
    static float Yaw(int d) { return d == 0 ? 0f : d == 1 ? 90f : d == 2 ? 180f : 270f; }   // +z local mirando hacia D[d]
    // punto sobre la cara interior del muro del lado d de la celda, desplazado «along» (eje u = (-n.z, n.x) con n hacia dentro)
    public Vector3 WallPoint(int x, int z, int d, float along, float inset)
    {
        Vector3 dir = Dir3(d), n = -dir, u = new Vector3(-n.z, 0f, n.x);
        return Center(x, z) + dir * (cs * 0.5f - t * 0.5f - inset) + u * along;
    }
    bool HasFake(int x, int z, int d) { foreach (Edge e in fakeDoors) if (e.c.x == x && e.c.y == z && e.d == d) return true; foreach (Edge e in liftDoors) if (e.c.x == x && e.c.y == z && e.d == d) return true; return false; }
    bool HasDoorway(int x, int z, int d)
    {
        Vector2Int c = new Vector2Int(x, z);
        foreach (Edge e in doorways) if ((e.c == c && e.d == d) || (e.c + D[e.d] == c && (e.d + 2) % 4 == d)) return true;
        return false;
    }

    void Furnish(System.Random r)
    {
        float H2 = cs * 0.5f;
        // habitaciones abiertas
        foreach (Room rm in rooms)
        {
            if (!rm.open) continue;
            int din = (rm.doorDir + 2) % 4;            // de la puerta hacia dentro
            Vector3 fw = Dir3(din), side = new Vector3(fw.z, 0f, -fw.x);
            Vector3 cb = Center(rm.bed.x, rm.bed.y), ce = Center(rm.entry.x, rm.entry.y);
            float back = H2 - t * 0.5f;
            Vector3 bedC = cb + fw * (back - 1.02f);
            Add(IBed, bedC, Yaw((din + 2) % 4), new Vector3(1.6f, 0.6f, 2.04f), r.Next(4));
            Add(INight, cb + fw * (back - 0.24f) + side * 1.12f, Yaw((din + 2) % 4), new Vector3(0.46f, 0.56f, 0.42f));
            Add(INight, cb + fw * (back - 0.24f) - side * 1.12f, Yaw((din + 2) % 4), new Vector3(0.46f, 0.56f, 0.42f));
            int lamps = r.NextDouble() < 0.5 ? 2 : 1;
            for (int l = 0; l < lamps; l++)
                Add(ILamp, cb + fw * (back - 0.24f) + side * (l == 0 ? 1.12f : -1.12f) + Vector3.up * 0.56f, 0f, new Vector3(0.3f, 0.55f, 0.3f), r.NextDouble() < 0.45 ? 1 : 0, false);
            Add(IPainting, cb + fw * (back - 0.012f) + Vector3.up * 1.62f, Yaw((din + 2) % 4), new Vector3(0.95f, 0.62f, 0.04f), r.Next(8), false);
            // entrada: armario a un lado, baño (puerta cerrada) al otro si el vecino no es otra habitación abierta
            int sA = (din + 1) % 4, sB = (din + 3) % 4;
            if (r.Next(2) == 0) { int tmp = sA; sA = sB; sB = tmp; }
            Vector3 dA = Dir3(sA), dB = Dir3(sB);
            Add(IWardrobe, ce + dA * (H2 - t * 0.5f - 0.31f) + fw * 0.25f, Yaw((sA + 2) % 4), new Vector3(1.15f, 2.05f, 0.6f), r.Next(3));
            Vector2Int nb = rm.entry + D[sB];
            K kn = At(nb.x, nb.y);
            if (kn == K.Void || kn == K.Shaft) { rm.bathSide = sB; fakeDoors.Add(new Edge { c = rm.entry, d = sB, kind = DoorBath }); }
            else Add(IDesk, ce + dB * (H2 - t * 0.5f - 0.3f) + fw * 0.3f, Yaw((sB + 2) % 4), new Vector3(1.1f, 0.76f, 0.55f), 0);
            // al pie de la cama: sillón o maleta; a veces un carrito o un cuadro caído
            double q = r.NextDouble();
            if (q < 0.45) Add(IArmchair, cb - fw * 0.95f + side * (r.Next(2) == 0 ? 0.95f : -0.95f), Yaw(din) + (float)(r.NextDouble() - 0.5) * 50f, new Vector3(0.78f, 0.9f, 0.78f), 0);
            else if (q < 0.7) Add(ISuitcase, cb - fw * 0.9f + side * 0.9f, (float)r.NextDouble() * 360f, new Vector3(0.7f, 0.25f, 0.45f), r.Next(2), true, 0f);
            if (r.NextDouble() < 0.3) Add(ICart, ce - fw * 0.6f + side * 0.2f, Yaw(sA) + (float)(r.NextDouble() - 0.5) * 30f, new Vector3(0.9f, 0.85f, 0.55f), 0);
            if (r.NextDouble() < 0.35) Add(IFallenFrame, ce + dB * (H2 - t * 0.5f - 0.15f) - fw * 0.5f, Yaw((sB + 2) % 4), new Vector3(0.6f, 0.45f, 0.04f), r.Next(8), false, 72f);
            views.Add(new View { name = "habitacion", pos = ce - fw * 1.0f + Vector3.up * 1.6f, look = cb + fw * 0.6f + Vector3.up * 0.7f });
        }
        // vestíbulo: recepción, casillero de llaves, sofás, plantas, lámparas de araña, carrito de maletas
        Hall lob = halls[0]; RectInt L = lob.r;
        float lx0 = L.x * cs, lx1 = L.xMax * cs, lz0 = L.y * cs, lz1 = L.yMax * cs;
        bool m = mirrored;
        float deskX = m ? lx0 + 2.6f : lx1 - 2.6f; int faceIn = m ? 1 : 3;   // la recepción mira al centro
        Vector3 rc = new Vector3(deskX, 0f, lz0 + (L.height * cs) * 0.68f);
        Add(IReception, rc, Yaw(faceIn), new Vector3(4.6f, 1.1f, 0.75f));
        Add(IKeyBoard, new Vector3(m ? lx0 + t * 0.5f + 0.03f : lx1 - t * 0.5f - 0.03f, 1.55f, rc.z), Yaw(faceIn), new Vector3(2.2f, 1.1f, 0.12f), 0, false);
        Add(IChair, rc + Dir3((faceIn + 2) % 4) * 1.0f + new Vector3(0, 0, 0.8f), (float)r.NextDouble() * 360f, new Vector3(0.48f, 0.95f, 0.48f), 0, true, 82f);
        Add(IBellCart, rc + Dir3(faceIn) * 2.2f + new Vector3(0f, 0f, -2.6f), (float)r.NextDouble() * 60f, new Vector3(1.0f, 1.9f, 0.6f));
        Vector3 sofaC = new Vector3(m ? lx1 - 6.5f : lx0 + 6.5f, 0f, lz0 + 6.5f);
        Add(ICoffee, sofaC, 0f, new Vector3(1.2f, 0.42f, 0.7f));
        Add(ISofa, sofaC + new Vector3(0f, 0f, 1.35f), 180f, new Vector3(2.1f, 0.85f, 0.85f));
        Add(ISofa, sofaC + new Vector3(0f, 0f, -1.35f), 0f, new Vector3(2.1f, 0.85f, 0.85f));
        Add(IArmchair, sofaC + new Vector3(1.7f, 0f, 0f), 270f + (float)(r.NextDouble() - 0.5) * 30f, new Vector3(0.8f, 0.9f, 0.8f));
        foreach (Vector3 pc in new[] { new Vector3(lx0 + 0.6f, 0, lz1 - 0.6f), new Vector3(lx1 - 0.6f, 0, lz0 + 0.6f), new Vector3(lx1 - 0.6f, 0, lz1 - 0.6f) })
            Add(IPlant, pc, (float)r.NextDouble() * 360f, new Vector3(0.6f, 1.4f, 0.6f), r.Next(3));
        Add(IChandelier, new Vector3((lx0 + lx1) * 0.5f, 0f, lz0 + (lz1 - lz0) * 0.3f), 0f, new Vector3(1.3f, 0.75f, 1.3f), r.NextDouble() < 0.6 ? 1 : 0, false);
        Add(IChandelier, new Vector3((lx0 + lx1) * 0.5f, 0f, lz0 + (lz1 - lz0) * 0.72f), 0f, new Vector3(1.3f, 0.75f, 1.3f), 0, false);
        lob.spot = sofaC + new Vector3(m ? -2.6f : 2.6f, 0f, 0f); halls[0] = lob;
        views.Add(new View { name = "recepcion", pos = new Vector3(m ? lx1 - 2.0f : lx0 + 2.0f, 1.6f, lz0 + 3.0f), look = rc + Vector3.up * 0.9f });
        Vector3 liftV = Center(liftDoors[1].c.x, liftDoors[1].c.y);
        views.Add(new View { name = "ascensores_vestibulo", pos = liftV + Dir3((liftDoors[1].d + 2) % 4) * 1.2f + new Vector3(0, 1.6f, -0.8f), look = liftV + Dir3(liftDoors[1].d) * 1.6f + Vector3.up * 1.3f });
        // comedor: mesas cuadradas con mantel y 4 sillas (algunas caídas), 2 arañas
        Hall din_ = halls[1]; RectInt Dn = din_.r;
        float dx0 = Dn.x * cs, dz0 = Dn.y * cs, dW = Dn.width * cs, dH = Dn.height * cs;
        int ncx = Mathf.Max(1, Mathf.FloorToInt((dW - 1.0f) / 3.6f)), ncz = Mathf.Max(1, Mathf.FloorToInt((dH - 1.0f) / 3.6f));
        for (int i = 0; i < ncx; i++) for (int j = 0; j < ncz; j++)
        {
            if (r.NextDouble() < 0.12) continue;
            Vector3 tc = new Vector3(dx0 + dW * (i + 0.5f) / ncx, 0f, dz0 + dH * (j + 0.5f) / ncz);
            Add(ISquareTable, tc, (float)(r.NextDouble() - 0.5) * 8f, new Vector3(0.95f, 0.76f, 0.95f), r.Next(2));
            for (int c = 0; c < 4; c++)
            {
                if (r.NextDouble() < 0.15) continue;
                Vector3 cd = Dir3(c); bool fallen = r.NextDouble() < 0.2;
                Add(IChair, tc + cd * (fallen ? 1.05f : 0.72f), Yaw((c + 2) % 4) + (float)(r.NextDouble() - 0.5) * 24f, new Vector3(0.48f, 0.95f, 0.48f), 0, true, fallen ? 82f : 0f);
            }
        }
        Add(IChandelier, new Vector3(dx0 + dW * 0.5f, 0, dz0 + dH * 0.3f), 0f, new Vector3(1.1f, 0.7f, 1.1f), r.NextDouble() < 0.5 ? 1 : 0, false);
        Add(IChandelier, new Vector3(dx0 + dW * 0.5f, 0, dz0 + dH * 0.72f), 0f, new Vector3(1.1f, 0.7f, 1.1f), 0, false);
        din_.spot = new Vector3(dx0 + dW * 0.5f, 0f, dz0 + dH * (ncz > 1 ? 1f / ncz : 0.5f)); halls[1] = din_;
        views.Add(new View { name = "comedor", pos = new Vector3(dx0 + 1.2f, 1.6f, dz0 + dH - 1.2f), look = new Vector3(dx0 + dW * 0.6f, 0.6f, dz0 + dH * 0.35f) });
        // salón de baile: mesas redondas en los lados, pista en el centro, columnas, piano, 3 arañas grandes
        Hall bal = halls[2]; RectInt B = bal.r;
        float bx0 = B.x * cs, bz0 = B.y * cs, bW = B.width * cs, bH = B.height * cs;
        int cols = Mathf.Max(2, Mathf.FloorToInt(bW / 4.4f)), rowsN = Mathf.Max(2, Mathf.FloorToInt(bH / 4.4f));
        for (int i = 0; i < cols; i++) for (int j = 0; j < rowsN; j++)
        {
            float fx = (i + 0.5f) / cols, fz = (j + 0.5f) / rowsN;
            if (fx > 0.36f && fx < 0.64f) continue;   // pista
            if (r.NextDouble() < 0.1) continue;
            Vector3 tc = new Vector3(bx0 + bW * fx, 0f, bz0 + bH * fz);
            Add(IRoundTable, tc, (float)r.NextDouble() * 360f, new Vector3(1.55f, 0.76f, 1.55f), r.Next(2));
            int nch = 6; float a0 = (float)r.NextDouble() * 60f;
            for (int c = 0; c < nch; c++)
            {
                if (r.NextDouble() < 0.2) continue;
                float a = (a0 + c * 360f / nch) * Mathf.Deg2Rad; Vector3 cd = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)); bool fallen = r.NextDouble() < 0.15;
                Add(IChair, tc + cd * (fallen ? 1.25f : 1.0f), a * Mathf.Rad2Deg + 180f + (float)(r.NextDouble() - 0.5) * 20f, new Vector3(0.48f, 0.95f, 0.48f), 1, true, fallen ? 82f : 0f);
            }
        }
        for (int i = 0; i < 2; i++) for (int j = 0; j < 2; j++)
            Add(IColumn, new Vector3(bx0 + bW * (0.36f + 0.28f * i), 0f, bz0 + bH * (0.33f + 0.34f * j)), 0f, new Vector3(0.62f, wh, 0.62f), 0);
        Add(IPiano, new Vector3(bx0 + bW * 0.5f + (m ? -1.5f : 1.5f), 0f, bz0 + bH - 2.0f), m ? 160f : 200f, new Vector3(1.5f, 1.0f, 2.1f));
        for (int k = 0; k < 3; k++)
            Add(IChandelier, new Vector3(bx0 + bW * 0.5f, 0f, bz0 + bH * (0.2f + 0.3f * k)), 0f, new Vector3(1.7f, 0.85f, 1.7f), k == 1 || r.NextDouble() < 0.3 ? 1 : 0, false);
        bal.spot = new Vector3(bx0 + bW * 0.5f, 0f, bz0 + bH * 0.5f); halls[2] = bal;
        views.Add(new View { name = "salon", pos = new Vector3(m ? bx0 + bW - 1.3f : bx0 + 1.3f, 1.65f, bz0 + 1.3f), look = new Vector3(bx0 + bW * 0.5f, 0.9f, bz0 + bH * 0.6f) });
        // rellanos de ascensores: cenicero, banco, planta, araña pequeña
        for (int j = 0; j < h; j++) for (int i = 0; i < w - 1; i++)
        {
            if (kind[i, j] != K.Lift || kind[i + 1, j] != K.Lift || At(i, j - 1) != K.Shaft) continue;
            if (i > 0 && kind[i - 1, j] == K.Lift) continue;
            // (i, j) = esquina inferior del rellano 2 x 3 (j, j+1, j+2)
            Vector3 c0 = new Vector3((i + 1) * cs, 0f, (j + 1.5f) * cs);
            Add(IAshtray, c0 + new Vector3(0f, 0f, -0.2f), 0f, new Vector3(0.3f, 0.75f, 0.3f));
            Add(ISmallChand, c0 + new Vector3(0f, 0f, 0f), 0f, new Vector3(0.8f, 0.5f, 0.8f), r.NextDouble() < 0.5 ? 1 : 0, false);
            if (r.NextDouble() < 0.6) Add(IPlant, new Vector3(i * cs + 0.55f, 0f, (j + 3) * cs - 0.55f), (float)r.NextDouble() * 360f, new Vector3(0.5f, 1.2f, 0.5f), r.Next(3));
            if (r.NextDouble() < 0.6) Add(IBench, new Vector3((i + 2) * cs - 0.45f, 0f, (j + 1.5f) * cs), 90f, new Vector3(1.4f, 0.5f, 0.45f));
            if (views.FindIndex(v => v.name == "ascensores") < 0 && j > 6)
                views.Add(new View { name = "ascensores", pos = new Vector3((i + 1) * cs - 1.2f, 1.6f, (j + 2.6f) * cs), look = new Vector3((i + 1) * cs + 0.4f, 1.2f, j * cs) });
        }
        // escaleras (callejón): tramo de 4 peldaños hasta un rellano con una puerta de incendios bloqueada
        for (int i = 0; i < w; i++) for (int j = 0; j < h; j++)
        {
            if (kind[i, j] != K.Stair) continue;
            int up = -1;
            for (int d = 0; d < 4; d++) if (IsOpen(i, j, d)) { up = (d + 2) % 4; break; }
            if (up < 0) continue;
            Vector3 c = Center(i, j);
            Add(IStairs, c, Yaw(up), new Vector3(1.9f, 0.72f, cs - t), 0);
            fakeDoors.Add(new Edge { c = new Vector2Int(i, j), d = up, kind = DoorFire });
            if (views.FindIndex(v => v.name == "escalera") < 0)
                views.Add(new View { name = "escalera", pos = c - Dir3(up) * 1.4f + Vector3.up * 1.6f - Dir3(up) * 0.0f, look = c + Dir3(up) * 1.6f + Vector3.up * 1.4f });
        }
        // pasillos: carritos, sillas, plantas secas, cuadros (en muros sin puerta), cuadros caídos; derrumbes
        for (int i = 0; i < w; i++) for (int j = 0; j < h; j++)
        {
            K k = kind[i, j];
            if (k != K.Corridor && k != K.Gallery && k != K.Linen) continue;
            for (int d = 0; d < 4; d++)
            {
                if (IsOpen(i, j, d) || HasFake(i, j, d) || HasDoorway(i, j, d)) continue;
                if (k == K.Linen)
                {
                    if (r.NextDouble() < 0.22) Add(ILinenShelf, WallPoint(i, j, d, 0f, 0.24f), Yaw((d + 2) % 4), new Vector3(1.2f, 1.85f, 0.45f), r.Next(3));
                    else if (r.NextDouble() < 0.08) Add(ILaundry, WallPoint(i, j, d, 0.3f, 0.4f), Yaw((d + 2) % 4) + (float)(r.NextDouble() - 0.5) * 20f, new Vector3(0.9f, 0.95f, 0.6f), 0);
                    continue;
                }
                double q = r.NextDouble();
                if (q < 0.30) Add(IPainting, WallPoint(i, j, d, 0f, 0.025f) + Vector3.up * 1.55f, Yaw((d + 2) % 4), new Vector3(0.8f, 0.58f, 0.04f), r.Next(8), false);
                else if (q < 0.36) Add(IFallenFrame, WallPoint(i, j, d, 0.2f, 0.16f), Yaw((d + 2) % 4), new Vector3(0.8f, 0.58f, 0.04f), r.Next(8), false, 70f);
                else if (q < 0.42) Add(IPlant, WallPoint(i, j, d, 1.05f, 0.35f), (float)r.NextDouble() * 360f, new Vector3(0.5f, 1.2f, 0.5f), r.Next(3));
                else if (q < 0.47) Add(IChair, WallPoint(i, j, d, 0.3f, 0.35f), Yaw((d + 2) % 4) + (float)(r.NextDouble() - 0.5) * 40f, new Vector3(0.48f, 0.95f, 0.48f), 0, true, r.NextDouble() < 0.3 ? 82f : 0f);
            }
            if (k == K.Corridor && r.NextDouble() < 0.07)
            {
                int dd = r.Next(2) == 0 ? 0 : 2;
                Add(ICart, WallPoint(i, j, dd, -0.2f, 0.5f), Yaw(1) + (float)(r.NextDouble() - 0.5) * 30f, new Vector3(0.9f, 0.85f, 0.55f), 0);
            }
            if (r.NextDouble() < 0.05) Add(IDebris, Center(i, j) + new Vector3((float)(r.NextDouble() - 0.5) * 1.6f, 0f, (float)(r.NextDouble() - 0.5) * 1.6f), (float)r.NextDouble() * 360f, new Vector3(1.0f, 0.05f, 1.0f), r.Next(4), false);
        }
        foreach (Edge e in blocked)
        {
            Vector3 c = (Center(e.c.x, e.c.y) + Center(e.c.x + D[e.d].x, e.c.y + D[e.d].y)) * 0.5f;
            Add(IRubble, c, Yaw(e.d), new Vector3(cs - t, 1.6f, 2.2f), r.Next(4));
        }
        // vistas de pasillo: los tramos más largos de pasillo de planta, de punta a punta
        int nCorr = 0;
        foreach (Corridor c in corridors)
        {
            if (c.number <= 0 || !c.horizontal || nCorr >= 3) continue;
            int a = c.a, b = c.b;
            foreach (Edge e in blocked) if (e.c.y == c.line) { if (e.c.x - a > b - e.c.x) b = e.c.x; else a = e.c.x + 1; }
            if (b - a < 5) continue;
            bool fromA = (nCorr % 2) == 0;
            Vector3 pa = Center(fromA ? a : b, c.line), pb = Center(fromA ? b : a, c.line);
            Vector3 dir = (pb - pa).normalized;
            views.Add(new View { name = "pasillo", pos = pa - dir * 0.6f + Vector3.up * 1.6f, look = pa + dir * 12f + Vector3.up * 1.25f });
            nCorr++;
        }
        foreach (Corridor c in corridors)
            if (c.number < 0 && c.horizontal) { views.Add(new View { name = "servicio", pos = Center(c.a + 1, c.line) + Vector3.up * 1.6f, look = Center(c.a + 6, c.line) + Vector3.up * 1.3f }); break; }
        views.Add(new View { name = "galeria", pos = Center(halls[0].r.x + 1, halls[0].r.yMax) + Vector3.up * 1.6f, look = Center(halls[2].r.xMax - 1, halls[0].r.yMax) + Vector3.up * 1.2f });
    }

    // ---------------------------------------------------------------- sitios libres (objetos, monstruos, pruebas)
    // Devuelve un punto (local) dentro de la celda lejos de muros y muebles (margen 0,6 m: cabe el NavMesh), lo más cerca posible del pedido.
    public Vector3 FreePoint(int x, int z, Vector3 want)
    {
        Vector3 c = Center(x, z);
        if (Free(x, z, want)) return want;
        for (int ring = 1; ring <= 6; ring++)
            for (int a = 0; a < 8; a++)
            {
                float ang = a * Mathf.PI / 4f + ring * 0.4f;
                Vector3 q = c + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (ring * 0.22f);
                q.y = want.y;
                if (Free(x, z, q)) return q;
            }
        return new Vector3(c.x, want.y, c.z);
    }
    bool Free(int x, int z, Vector3 q)
    {
        const float M = 0.6f;
        Vector3 c = Center(x, z); float lim = cs * 0.5f - t * 0.5f - M;
        float dx = q.x - c.x, dz = q.z - c.z;
        if (Mathf.Abs(dx) > cs * 0.5f || Mathf.Abs(dz) > cs * 0.5f) return false;
        if (!IsOpen(x, z, 1) && dx > lim) return false;
        if (!IsOpen(x, z, 3) && -dx > lim) return false;
        if (!IsOpen(x, z, 0) && dz > lim) return false;
        if (!IsOpen(x, z, 2) && -dz > lim) return false;
        foreach (Item it in items)
        {
            if (!it.solid) continue;
            Vector3 o = q - it.p; o.y = 0f;
            if (o.sqrMagnitude > 16f) continue;
            float ya = -it.yaw * Mathf.Deg2Rad;
            float lx = o.x * Mathf.Cos(ya) - o.z * Mathf.Sin(ya), lz = o.x * Mathf.Sin(ya) + o.z * Mathf.Cos(ya);
            float ex = it.s.x * 0.5f + M, ez = it.s.z * 0.5f + M;
            if (it.k == IRoundTable) { if (o.magnitude < it.s.x * 0.5f + 0.4f + M) return false; continue; }
            if (it.k == IChair && it.tilt > 1f) { ex = 0.5f + M; ez = 0.6f + M; }
            if (Mathf.Abs(lx) < ex && Mathf.Abs(lz) < ez) return false;
        }
        return true;
    }
}
