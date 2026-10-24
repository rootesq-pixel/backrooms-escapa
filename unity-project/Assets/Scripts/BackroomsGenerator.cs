using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Generador procedural de niveles (Backrooms) a partir de un LevelConfig.
// - Layout: laberinto (backtracker + bucles + salas abiertas) o rejilla abierta con pilares.
// - Geometría: mallas por "chunks" con UV en coordenadas de mundo (texturas con escala real),
//   suelo/techo/paredes con materiales Standard (albedo + normal + máscara PBR).
// - Luces: accesorios emisivos; solo las N más cercanas al jugador son luces reales (LightCuller).
// - Puerta de salida sutil (sin cartel ni pistas lejanas), pilas, puntos de aparición del monstruo.
// NOTA: asume escala (1,1,1) en este objeto.
public class BackroomsGenerator : MonoBehaviour
{
    public Material WallMaterial { get; private set; }   // v7.4.0: material de pared del nivel (lo reutiliza el rellano del ascensor)
    [Header("Semilla")]
    public bool useRandomSeed = true;
    [Tooltip("Multijugador: monstruos extra (por nº de jugadores). Se envía igual a todos los equipos para que el mundo sea idéntico.")]
    public int extraMonsters = 0;
    public int seed = 12345;

    [Header("Nivel")]
    [Tooltip("Parámetros del nivel. Si es null se usa el Nivel 0.")]
    public LevelConfig config;

    // Parámetros de layout (se copian del LevelConfig al generar)
    [HideInInspector] public int gridWidth = 14;
    [HideInInspector] public int gridHeight = 14;
    [HideInInspector] public float cellSize = 4f;
    [HideInInspector] public float wallHeight = 3f;
    [HideInInspector] public float loopChance = 0.12f;
    [HideInInspector] public int roomCount = 8;
    [HideInInspector] public Vector2Int roomSizeRange = new Vector2Int(2, 4);
    public float wallThickness = 0.2f;

    [Header("Ejecución")]
    [Tooltip("Genera el nivel en Start (déjalo en false si lo llama GameBootstrap).")]
    public bool generateOnStart = false;

    // --- Resultados públicos ---
    public bool IsGenerated { get; private set; }
    public bool HasLevel { get { return IsGenerated && levelRoot != null; } }
    public int UsedSeed { get; private set; }
    public FieldPlan FieldPlan { get; private set; }
    public HotelPlan Hotel { get; private set; }       // v7.5.0: plano del hotel largo (nivel 3; null en otros niveles)
    private readonly Dictionary<int, float> sconceAlong = new Dictionary<int, float>();   // v7.5.0: apliques por cara de celda (las puertas del hotel los esquivan)
    public GaragePlan Garage { get; private set; }     // v7.0.4: plantas del aparcamiento (null en otros niveles)   // v7.0: casas/postes/puertas del Campo infinito (null en otros niveles)
    public bool IsField { get { return config != null && config.layout == LayoutKind.Field; } }
    public Vector3 PlayerStart { get; private set; }
    public float PlayerStartYaw { get; private set; }
    public Vector3 MonsterSpawn { get; private set; }
    public List<Vector3> MonsterSpawns { get; private set; }
    public List<Vector3> BatterySpots { get; private set; }
    public List<Vector3> ExtraSpots { get; private set; }
    public List<Vector3> KeySpots { get; private set; }        // v7.0.4: llaves señuelo (ExitLock.KeyCount; una abre la salida)     // sitios para notas, agua de almendras, barras luminosas y cintas
    public Vector3 ExitPosition { get; private set; }
    public Transform ExitTransform { get; private set; }
    public Transform Root { get { return levelRoot; } }
    // El ascensor (ElevatorCabin) mueve el punto de aparición del jugador al interior de la cabina (no cambia el laberinto)
    public void OverridePlayerStart(Vector3 p) { PlayerStart = p; }
    public LightCuller Culler { get; private set; }
    public int GridWidth { get { return w; } }
    public int GridHeight { get { return h; } }
    public float CellSize { get { return cellSize; } }
    public Vector2Int StartCell { get { return startCell; } }
    public Vector2Int ExitCell { get { return exitCell; } }
    public int FixtureCount { get; private set; }
    public int LiveFixtureCount { get; private set; }
    public int PillarCount { get; private set; }
    public int WallSegmentCount { get; private set; }

    public WaterVolume Water { get; private set; }

    // --- Datos internos ---
    private Transform levelRoot;
    private Transform decorRoot;
    private int w;
    private int h;
    // wallsV[i, j]: muro vertical en la línea x=i, fila j (entre celda i-1 e i)
    // v6.7.1: sala de piscina de Blender (Poolrooms) en un bloque de celdas abierto
    public bool HasPoolRoom { get; private set; }
    public RectInt PoolBlock { get; private set; }          // celdas
    public Rect PoolRectLocal { get; private set; }         // vaso de la piscina (XZ local)
    public const float PoolDeckY = 0.28f, PoolDepth = 1.4f;
    public const int PoolBlockW = 4, PoolBlockH = 3;
    // v6.8: sala fija horneada: perímetro cerrado con 4 puertas fijas (S: col 1, N: col 2, O/E: fila 1), hueco 2 m x 2,5 m
    // v7.2.0: 2 puertas (W: fila 1, S: col 0); las paredes N y E (ventanas, como en Blender) quedan cerradas
    public const float PoolDoorW = 2f, PoolDoorTop = 2.5f;
    public const int PoolDoorColS = 0, PoolDoorRowW = 1;
    public bool PoolFixed { get; private set; }
    public bool IsPoolDoor(int i, int j, bool vertical)
    {
        if (!PoolFixed) return false;
        RectInt b = PoolBlock;
        return vertical ? (j == b.y + PoolDoorRowW && i == b.x) : (i == b.x + PoolDoorColS && j == b.y);   // v7.2.0: solo W y S
    }
    public bool InPoolBlock(int x, int z) { return HasPoolRoom && PoolBlock.Contains(new Vector2Int(x, z)) || InHall(x, z); }   // v7.1.0: también las salas del parque
    // v7.1.0 Parque de bolas: salas fijas horneadas de 5x4 celdas (BallpitSetPiece), perímetro cerrado con 4 puertas
    public const int HallW = 5, HallH = 4;
    public readonly List<RectInt> Halls = new List<RectInt>();
    public bool InHallWorld(Vector3 world) { Vector2Int c = WorldToCell(world); return InHall(c.x, c.y); }   // v7.1.0
    public bool InHall(int x, int z) { for (int k = 0; k < Halls.Count; k++) if (Halls[k].Contains(new Vector2Int(x, z))) return true; return false; }
    public int HallIndex(int x, int z) { for (int k = 0; k < Halls.Count; k++) if (Halls[k].Contains(new Vector2Int(x, z))) return k; return -1; }
    private int BlockId(int x, int z) { if (HasPoolRoom && PoolBlock.Contains(new Vector2Int(x, z))) return 0; int k = HallIndex(x, z); return k < 0 ? -1 : 1 + k; }
    // puertas de la sala: S en la columna 3, N en la 0, O y E en la fila 1 (igual que Tools/ballpit/build_hall.py)
    public static bool HallDoorH(RectInt b, int i, int j) { return (j == b.y && i == b.x + 3) || (j == b.yMax && i == b.x); }
    // v7.4.0: sin la puerta este. En la malla horneada de la sala, justo detrás de ese hueco está la planta alta del parque (1,7 m):
    // no cabe un monstruo ni el jugador de pie, y el NavMesh no pasaba. Cuando esa era la única entrada a una zona (p. ej. semilla
    // 64257619: la salida), la salida quedaba inalcanzable a pie. Ahora el muro del laberinto la cierra y EnsureConnected abre otro paso.
    public static readonly bool HallEastDoor = false;
    public static bool HallDoorV(RectInt b, int i, int j) { return j == b.y + 1 && (i == b.x || (HallEastDoor && i == b.xMax)); }
    public Vector3 HallCenterLocal(int k) { RectInt b = Halls[k]; return new Vector3((b.x + b.width * 0.5f) * cellSize, 0f, (b.y + b.height * 0.5f) * cellSize); }
    public bool IsPoolCell(int x, int z)
    {
        if (!InPoolBlock(x, z)) return false;
        Vector3 c = CellCenterLocal(x, z);
        return PoolRectLocal.Contains(new Vector2(c.x, c.z));
    }
    private bool[,] wallsV;
    // wallsH[i, j]: muro horizontal en la línea z=j, columna i (entre celda j-1 y j)
    private bool[,] wallsH;
    private int[,] distFromStart;
    private Vector2Int startCell;
    private Vector2Int exitCell;
    private int maxDistCache;
    private readonly List<Object> createdAssets = new List<Object>();

    // Direcciones: 0 = Norte (+Z), 1 = Este (+X), 2 = Sur (-Z), 3 = Oeste (-X)
    private static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0)
    };

    private void Start()
    {
        if (generateOnStart && !IsGenerated)
        {
            Generate();
        }
    }

    private void OnDestroy()
    {
        DestroyCreatedAssets();
    }

    // ------------------------------------------------------------------
    // API principal
    // ------------------------------------------------------------------

    // Genera (o regenera) todo el nivel
    public void Generate()
    {
        Clear();

        if (config == null) config = LevelConfig.Get(0);
        gridWidth = config.gridW;
        gridHeight = config.gridH;
        cellSize = config.cellSize;
        wallHeight = config.wallHeight;
        loopChance = config.loopChance;
        roomCount = config.roomCount;
        roomSizeRange = config.roomSize;

        FieldHeight.Reset(); FieldPlan = null; Garage = null; GaragePlan.Active = null; StartDoorRepairs = 0;
        UsedSeed = useRandomSeed ? System.Environment.TickCount : seed;
        System.Random rng = new System.Random(UsedSeed);

        w = Mathf.Max(3, gridWidth);
        h = Mathf.Max(3, gridHeight);

        BuildLayout(rng);
        ComputeKeyCells(rng);
        BuildGeometry(rng);

        IsGenerated = true;
    }

    // Borra el nivel generado y los materiales/mallas creados (las texturas viven en TextureLibrary)
    public void Clear()
    {
        if (levelRoot != null)
        {
            ProceduralAssets.SafeDestroy(levelRoot.gameObject);
            levelRoot = null;
        }
        if (decorRoot != null)
        {
            ProceduralAssets.SafeDestroy(decorRoot.gameObject);
            decorRoot = null;
        }
        DestroyCreatedAssets();
        ExitTransform = null;
        Culler = null;
        IsGenerated = false;
    }

    private void DestroyCreatedAssets()
    {
        for (int i = 0; i < createdAssets.Count; i++)
        {
            ProceduralAssets.SafeDestroy(createdAssets[i]);
        }
        createdAssets.Clear();
    }

    // Centro (en el suelo) de una celda, en coordenadas del mundo
    public Vector3 CellCenterWorld(int x, int z)
    {
        return levelRoot.TransformPoint(CellCenterLocal(x, z));
    }

    // Distancia (en el plano XZ, coordenadas locales del nivel) hasta la pared más cercana (superficie)
    public float WallDistance(float x, float z)
    {
        float cs = cellSize;
        int ci = Mathf.FloorToInt(x / cs), cj = Mathf.FloorToInt(z / cs);
        float best = 99f;
        for (int i = ci - 1; i <= ci + 2; i++)
        {
            if (i < 0 || i > w) continue;
            for (int j = cj - 1; j <= cj + 1; j++)
            {
                if (j < 0 || j >= h || !wallsV[i, j]) continue;
                float dz = Mathf.Max(0f, Mathf.Max(j * cs - z, z - (j + 1) * cs));
                float dx = Mathf.Abs(x - i * cs);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
        }
        for (int i = ci - 1; i <= ci + 1; i++)
        {
            if (i < 0 || i >= w) continue;
            for (int j = cj - 1; j <= cj + 2; j++)
            {
                if (j < 0 || j > h || !wallsH[i, j]) continue;
                float dx = Mathf.Max(0f, Mathf.Max(i * cs - x, x - (i + 1) * cs));
                float dz = Mathf.Abs(z - j * cs);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
        }
        return Mathf.Max(0f, best - wallThickness * 0.5f);
    }

    // Altura del suelo analítica (el suelo es plano); coordenadas locales
    public float FloorHeightLocal(float x, float z)
    {
        if (FieldHeight.Active) return FieldHeight.H(x, z);
        if (IsOcean && OceanWorld.Active != null) return OceanWorld.Active.SeabedY(x, z);
        if (Garage != null) return Garage.Y(x, z);
        if (Halls.Count > 0)
        {
            int hx = Mathf.FloorToInt(x / cellSize), hz = Mathf.FloorToInt(z / cellSize);
            int k = HallIndex(hx, hz);
            if (k >= 0) { Vector3 c = HallCenterLocal(k); return BallpitSetPiece.FloorY(x - c.x, z - c.z); }
        }
        if (HasPoolRoom)
        {
            float cs = cellSize;
            if (x >= PoolBlock.x * cs && x <= PoolBlock.xMax * cs && z >= PoolBlock.y * cs && z <= PoolBlock.yMax * cs)
                return PoolRectLocal.Contains(new Vector2(x, z)) ? PoolDeckY - PoolDepth : PoolDeckY;
        }
        return 0f;
    }

    // Huella del mundo generado (multijugador): posiciones clave + resumen de la geometría. Debe ser idéntica en todos los equipos
    // para la misma semilla/nivel (el generador solo usa System.Random con la semilla, nunca Random global ni el tiempo).
    public ulong WorldHash()
    {
        ulong h = NetUtil.FnvBasis;
        h = NetUtil.Fnv(h, UsedSeed);
        h = NetUtil.FnvVec(h, PlayerStart);
        h = NetUtil.FnvVec(h, ExitPosition);
        h = NetUtil.Fnv(h, ExitCell.x * 1000 + ExitCell.y);
        if (MonsterSpawns != null) foreach (Vector3 v in MonsterSpawns) h = NetUtil.FnvVec(h, v);
        if (BatterySpots != null) foreach (Vector3 v in BatterySpots) h = NetUtil.FnvVec(h, v);
        if (ExtraSpots != null) foreach (Vector3 v in ExtraSpots) h = NetUtil.FnvVec(h, v);
        if (KeySpots != null) foreach (Vector3 v in KeySpots) h = NetUtil.FnvVec(h, v);   // v7.0.4
        if (levelRoot != null)
        {
            int verts = 0, tris = 0, meshes = 0;
            Vector3 bsum = Vector3.zero;
            foreach (MeshFilter mf in levelRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                meshes++; verts += mf.sharedMesh.vertexCount; tris += (int)mf.sharedMesh.GetIndexCount(0);
                bsum += mf.sharedMesh.bounds.center + mf.sharedMesh.bounds.extents;
            }
            h = NetUtil.Fnv(h, meshes); h = NetUtil.Fnv(h, verts); h = NetUtil.Fnv(h, tris); h = NetUtil.FnvVec(h, bsum);
        }
        return h;
    }

    // Un punto aleatorio (centro de celda) para patrullas
    public Vector3 GetRandomCellCenter()
    {
        int x = Random.Range(0, w);
        int z = Random.Range(0, h);
        return CellCenterWorld(x, z);
    }

    // ¿Se puede pasar desde la celda (x,z) en la dirección dir (0 N, 1 E, 2 S, 3 O)? (para tests)
    public bool IsCellOpen(int x, int z, int dir)
    {
        return IsOpen(new Vector2Int(x, z), dir);
    }

    public int DistanceFromStart(int x, int z)
    {
        return distFromStart != null ? distFromStart[x, z] : -1;
    }

    // Camino por la cuadrícula (BFS) entre dos posiciones del mundo.
    // Sirve como alternativa a la NavMesh: el monstruo sigue los centros de celda.
    public bool TryGetPath(Vector3 fromWorld, Vector3 toWorld, List<Vector3> result)
    {
        result.Clear();
        if (!HasLevel)
        {
            return false;
        }

        Vector2Int a = WorldToCell(fromWorld);
        Vector2Int b = WorldToCell(toWorld);
        if (a == b)
        {
            result.Add(toWorld);
            return true;
        }

        int total = w * h;
        int[] parent = new int[total];
        for (int i = 0; i < total; i++) parent[i] = -2; // -2 = sin visitar

        int ai = a.y * w + a.x;
        int bi = b.y * w + b.x;
        Queue<int> queue = new Queue<int>();
        queue.Enqueue(ai);
        parent[ai] = -1;

        while (queue.Count > 0 && parent[bi] == -2)
        {
            int cur = queue.Dequeue();
            Vector2Int c = new Vector2Int(cur % w, cur / w);
            for (int d = 0; d < 4; d++)
            {
                if (!IsOpen(c, d)) continue;
                Vector2Int n = c + Dirs[d];
                int ni = n.y * w + n.x;
                if (parent[ni] != -2) continue;
                if (ni != bi && IsPoolCell(n.x, n.y)) continue;   // no cruzar el vaso de la piscina
                if (ni != bi && FieldBlocked(n.x, n.y)) continue; // v7.0: rodear las casas del campo
                parent[ni] = cur;
                queue.Enqueue(ni);
            }
        }

        if (parent[bi] == -2)
        {
            return false; // no hay camino (no debería pasar: el laberinto es conexo)
        }

        // Reconstruir de destino a origen y luego invertir
        List<Vector3> reversed = new List<Vector3>();
        reversed.Add(toWorld);
        int node = parent[bi];
        while (node != ai && node >= 0)
        {
            reversed.Add(CellCenterWorld(node % w, node / w));
            node = parent[node];
        }
        for (int i = reversed.Count - 1; i >= 0; i--)
        {
            result.Add(reversed[i]);
        }
        return true;
    }

    // ------------------------------------------------------------------
    // Layout lógico (muros)
    // ------------------------------------------------------------------

    private void BuildLayout(System.Random rng)
    {
        wallsV = new bool[w + 1, h];
        wallsH = new bool[w, h + 1];
        for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) wallsV[i, j] = true;
        for (int i = 0; i < w; i++) for (int j = 0; j <= h; j++) wallsH[i, j] = true;

        // v7.5.0: hotel: plano de edificio completo (no hay laberinto)
        if (config.hotel && config.layout == LayoutKind.Maze)
        {
            Hotel = HotelPlan.Make(UsedSeed * 577 + 3, w, h, cellSize, wallThickness, wallHeight, wallsV, wallsH);
            startCell = Hotel.start;
            ChoosePoolBlock(); ChooseHalls();
            return;
        }
        Hotel = null;
        // 1) Laberinto perfecto con backtracker iterativo
        bool[,] visited = new bool[w, h];
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        List<Vector2Int> options = new List<Vector2Int>(4);

        startCell = new Vector2Int(0, 0);
        visited[0, 0] = true;
        stack.Push(startCell);

        while (stack.Count > 0)
        {
            Vector2Int cur = stack.Peek();
            options.Clear();
            for (int d = 0; d < 4; d++)
            {
                Vector2Int n = cur + Dirs[d];
                if (InBounds(n) && !visited[n.x, n.y]) options.Add(n);
            }

            if (options.Count == 0)
            {
                stack.Pop();
                continue;
            }

            Vector2Int next = options[rng.Next(options.Count)];
            RemoveWallBetween(cur, next);
            visited[next.x, next.y] = true;
            stack.Push(next);
        }

        // 2) Bucles: abrir algunos muros interiores al azar
        for (int i = 1; i < w; i++)
            for (int j = 0; j < h; j++)
                if (wallsV[i, j] && rng.NextDouble() < loopChance) wallsV[i, j] = false;

        for (int i = 0; i < w; i++)
            for (int j = 1; j < h; j++)
                if (wallsH[i, j] && rng.NextDouble() < loopChance) wallsH[i, j] = false;

        // 3) Salas abiertas: se quitan los muros interiores de un rectángulo
        int minSize = Mathf.Max(2, roomSizeRange.x);
        int maxSize = Mathf.Max(minSize, roomSizeRange.y);
        for (int r = 0; r < roomCount; r++)
        {
            int rw = Mathf.Min(rng.Next(minSize, maxSize + 1), w);
            int rh = Mathf.Min(rng.Next(minSize, maxSize + 1), h);
            int rx = rng.Next(0, w - rw + 1);
            int rz = rng.Next(0, h - rh + 1);

            for (int i = rx + 1; i < rx + rw; i++)
                for (int j = rz; j < rz + rh; j++)
                    wallsV[i, j] = false;

            for (int i = rx; i < rx + rw; i++)
                for (int j = rz + 1; j < rz + rh; j++)
                    wallsH[i, j] = false;
        }


        // v7.0.4: aparcamiento por plantas (rampas, tabiques, callejones, rampas falsas)
        if (config.decks > 1 && config.layout != LayoutKind.Field)
        {
            Garage = GaragePlan.Make(new System.Random(UsedSeed * 313 + 41), w, h, cellSize, wallHeight, config.decks, config.deckDrop, wallsV, wallsH);
            StartDoorRepairs = EnsureReachFromStartDoor();
        }

        // v7.3.0 Talasofobia: sin muros; la rejilla (3x3 de 10 m) solo sitúa la cabina del ascensor en la casa inundada
        if (config.layout == LayoutKind.Ocean)
        {
            for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) wallsV[i, j] = false;
            for (int i = 0; i < w; i++) for (int j = 0; j <= h; j++) wallsH[i, j] = false;
            startCell = new Vector2Int(w / 2, h / 2);
        }

        // v7.0 Campo infinito: sin muros (ni siquiera el borde: el campo se repite), salida desde el centro
        if (config.layout == LayoutKind.Field)
        {
            for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) wallsV[i, j] = false;
            for (int i = 0; i < w; i++) for (int j = 0; j <= h; j++) wallsH[i, j] = false;
            startCell = new Vector2Int(w / 2, h / 2);
            FieldHeight.Init(UsedSeed, w * cellSize, new Vector2((startCell.x + 0.5f) * cellSize, (startCell.y + 0.5f) * cellSize));
        }

        ChoosePoolBlock();
        ChooseHalls();
    }

    private void ChoosePoolBlock()
    {
        HasPoolRoom = false; PoolFixed = false;
        Shader.SetGlobalVector("_PoolRoomRect", new Vector4(1e6f, 1e6f, -1e6f, -1e6f));
        PoolLook.Clear(); PoolPlanarReflection.ResetForNewLevel();   // v7.1.3
        if (config == null || !config.water || config.index != 2 || w < PoolBlockW + 2 || h < PoolBlockH + 2) return;
        System.Random pr = new System.Random(UsedSeed * 131 + 17);
        int bx = 1 + pr.Next(w - PoolBlockW - 1), bz = 1 + pr.Next(h - PoolBlockH - 1);
        PoolBlock = new RectInt(bx, bz, PoolBlockW, PoolBlockH);
        for (int i = bx + 1; i < bx + PoolBlockW; i++) for (int j = bz; j < bz + PoolBlockH; j++) wallsV[i, j] = false;
        for (int i = bx; i < bx + PoolBlockW; i++) for (int j = bz + 1; j < bz + PoolBlockH; j++) wallsH[i, j] = false;
        float cs = cellSize;
        Vector2 ctr = new Vector2((bx + PoolBlockW * 0.5f) * cs, (bz + PoolBlockH * 0.5f) * cs);
        // vaso 8x4 de Blender escalado como la sala (14x10 -> interior del bloque)
        float sx = (PoolBlockW * cs - wallThickness) / 14f, sz = (PoolBlockH * cs - wallThickness) / 10f;
        PoolRectLocal = new Rect(ctr.x - 4f * sx, ctr.y - 2f * sz, 8f * sx, 4f * sz);
        HasPoolRoom = true;
        PoolFixed = PoolRoomSetPiece.HasBakedAssets;
        if (PoolFixed)
        {
            for (int i = bx; i < bx + PoolBlockW; i++) { wallsH[i, bz] = true; wallsH[i, bz + PoolBlockH] = true; }
            for (int j = bz; j < bz + PoolBlockH; j++) { wallsV[bx, j] = true; wallsV[bx + PoolBlockW, j] = true; }
            // v7.2.0: como el render de Blender, sin puertas en las paredes que se ven desde la piscina (ventanas en E):
            // puertas en W (celda central) y S (celda 0), las mismas que tiene la malla horneada (build_fixed_v720.py)
            wallsH[bx + PoolDoorColS, bz] = false; wallsV[bx, bz + PoolDoorRowW] = false;
            EnsureConnected();
        }
    }

    private void ChooseHalls()
    {
        Halls.Clear();
        if (config == null || config.halls <= 0 || config.layout != LayoutKind.Maze || !BallpitSetPiece.HasBakedAssets) return;
        System.Random hr = new System.Random(UsedSeed * 173 + 29);
        for (int tries = 0; tries < 400 && Halls.Count < config.halls; tries++)
        {
            int bx = 1 + hr.Next(Mathf.Max(1, w - HallW - 1)), bz = 1 + hr.Next(Mathf.Max(1, h - HallH - 1));
            RectInt b = new RectInt(bx, bz, HallW, HallH);
            bool ok = !b.Contains(startCell);
            foreach (RectInt o in Halls) if (b.xMin - 1 < o.xMax && o.xMin - 1 < b.xMax && b.yMin - 1 < o.yMax && o.yMin - 1 < b.yMax) ok = false;
            if (!ok) continue;
            Halls.Add(b);
            for (int i = bx; i <= bx + HallW; i++) for (int j = bz; j < bz + HallH; j++) wallsV[i, j] = i == bx || i == bx + HallW ? !HallDoorV(b, i, j) : false;
            for (int i = bx; i < bx + HallW; i++) for (int j = bz; j <= bz + HallH; j++) wallsH[i, j] = j == bz || j == bz + HallH ? !HallDoorH(b, i, j) : false;
        }
        if (Halls.Count > 0) EnsureConnected();
    }

    // Abre muros (nunca los del perímetro de la sala de piscina) hasta que todas las celdas sean alcanzables (determinista)
    private void EnsureConnected()
    {
        for (int guard = 0; guard < w * h; guard++)
        {
            bool[,] seen = new bool[w, h];
            Queue<Vector2Int> q = new Queue<Vector2Int>();
            q.Enqueue(Vector2Int.zero); seen[0, 0] = true; int cnt = 1;
            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!IsOpen(c, d)) continue;
                    Vector2Int n = c + Dirs[d];
                    if (!InBounds(n) || seen[n.x, n.y]) continue;
                    seen[n.x, n.y] = true; cnt++; q.Enqueue(n);
                }
            }
            if (cnt == w * h) return;
            bool opened = false;
            for (int i = 0; i < w && !opened; i++)
                for (int j = 0; j < h && !opened; j++)
                {
                    if (seen[i, j]) continue;
                    Vector2Int c = new Vector2Int(i, j);
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int n = c + Dirs[d];
                        if (!InBounds(n) || !seen[n.x, n.y] || BlockId(c.x, c.y) != BlockId(n.x, n.y)) continue;
                        RemoveWallBetween(c, n); opened = true; break;
                    }
                }
            if (!opened) return;
        }
    }

    private bool InBounds(Vector2Int c)
    {
        return c.x >= 0 && c.y >= 0 && c.x < w && c.y < h;
    }

    private void RemoveWallBetween(Vector2Int a, Vector2Int b)
    {
        if (b.x == a.x + 1) wallsV[a.x + 1, a.y] = false;
        else if (b.x == a.x - 1) wallsV[a.x, a.y] = false;
        else if (b.y == a.y + 1) wallsH[a.x, a.y + 1] = false;
        else if (b.y == a.y - 1) wallsH[a.x, a.y] = false;
    }

    // ¿Se puede pasar desde la celda c en la dirección dir?
    private bool IsOpen(Vector2Int c, int dir)
    {
        if (!InBounds(c + Dirs[dir])) return false;   // v7.0: el campo no tiene muro exterior (los bordes no llevan fuera de la rejilla)
        switch (dir)
        {
            case 0: return !wallsH[c.x, c.y + 1];
            case 1: return !wallsV[c.x + 1, c.y];
            case 2: return !wallsH[c.x, c.y];
            default: return !wallsV[c.x, c.y];
        }
    }


    // ------------------------------------------------------------------
    // Celdas clave: inicio, salida (entre las más lejanas) y apariciones del monstruo
    // ------------------------------------------------------------------

    private void ComputeKeyCells(System.Random rng)
    {
        // BFS desde la celda de inicio
        distFromStart = new int[w, h];
        for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) distFromStart[i, j] = -1;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        distFromStart[startCell.x, startCell.y] = 0;
        queue.Enqueue(startCell);

        int maxDist = 0;
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int dc = distFromStart[c.x, c.y];
            if (dc > maxDist) maxDist = dc;
            for (int d = 0; d < 4; d++)
            {
                if (!IsOpen(c, d)) continue;
                Vector2Int n = c + Dirs[d];
                if (distFromStart[n.x, n.y] != -1) continue;
                distFromStart[n.x, n.y] = dc + 1;
                queue.Enqueue(n);
            }
        }
        maxDistCache = maxDist;

        // Salida: una celda entre el 20% más lejano que tenga al menos un muro donde apoyar la puerta
        List<Vector2Int> exits = new List<Vector2Int>();
        int minExit = Mathf.Max(1, Mathf.RoundToInt(maxDist * 0.8f));
        for (int i = 0; i < w; i++)
        {
            for (int j = 0; j < h; j++)
            {
                if (distFromStart[i, j] < minExit) continue;
                if (InPoolBlock(i, j)) continue;
                if (Hotel != null && !Hotel.ExitAllowed(i, j)) continue;   // v7.5.0: la salida, en un pasillo (no en habitaciones ni salas)
                Vector2Int c = new Vector2Int(i, j);
                // v7.0.4: aparcamiento: la salida, en la última planta (la más baja), nunca en una rampa ni en un pasillo falso
                if (Garage != null && (Garage.DeckOfRow(j) != Garage.decks - 1 || Garage.IsRamp(i, j) || Garage.decoys.Contains(c))) continue;
                bool hasWall = false;
                for (int d = 0; d < 4; d++) if (!IsOpen(c, d)) hasWall = true;
                if (hasWall || config.layout == LayoutKind.Field) exits.Add(c);
            }
        }
        exitCell = exits.Count > 0 ? exits[rng.Next(exits.Count)] : new Vector2Int(w - 1, h - 1);
        if (config.layout == LayoutKind.Field)
        {
            LoadDiag.Step("campo: plan");
            FieldPlan = FieldPlan.Make(UsedSeed, w, h, cellSize, startCell, exitCell);
            // distancias de nuevo rodeando las casas (las celdas con casa quedan a -1: no son sitio para objetos ni monstruos)
            for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) distFromStart[i, j] = -1;
            distFromStart[startCell.x, startCell.y] = 0; queue.Enqueue(startCell);
            while (queue.Count > 0)
            {
                Vector2Int c = queue.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!IsOpen(c, d)) continue;
                    Vector2Int n = c + Dirs[d];
                    if (distFromStart[n.x, n.y] != -1 || FieldBlocked(n.x, n.y)) continue;
                    distFromStart[n.x, n.y] = distFromStart[c.x, c.y] + 1;
                    queue.Enqueue(n);
                }
            }
            // celdas encerradas entre casas: se marcan ocupadas (no reciben objetos, monstruos ni salida)
            for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) if (distFromStart[i, j] < 0) FieldPlan.blocked.Add(new Vector2Int(i, j));
        }
    }

    // Elige celdas de aparición de monstruos: lejos del inicio y separadas entre sí
    private List<Vector2Int> PickMonsterCells(System.Random rng, int count)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        int minDist = Mathf.Max(3, Mathf.RoundToInt(maxDistCache * 0.4f));
        List<Vector2Int> candidates = new List<Vector2Int>();
        for (int i = 0; i < w; i++)
        {
            for (int j = 0; j < h; j++)
            {
                if (distFromStart[i, j] >= minDist && !(i == exitCell.x && j == exitCell.y) && !InPoolBlock(i, j) && !FieldBlocked(i, j))
                {
                    candidates.Add(new Vector2Int(i, j));
                }
            }
        }
        if (candidates.Count == 0) candidates.Add(new Vector2Int(w - 1, h - 1));

        for (int k = 0; k < count; k++)
        {
            Vector2Int best = candidates[rng.Next(candidates.Count)];
            for (int attempt = 0; attempt < 20; attempt++)
            {
                Vector2Int cand = candidates[rng.Next(candidates.Count)];
                bool farEnough = true;
                for (int q = 0; q < result.Count; q++)
                {
                    if (Mathf.Abs(cand.x - result[q].x) + Mathf.Abs(cand.y - result[q].y) < Mathf.Max(5, (w + h) / 4)) farEnough = false;
                }
                if (farEnough) { best = cand; break; }
            }
            result.Add(best);
        }
        return result;
    }

    // ------------------------------------------------------------------
    // Geometría
    // ------------------------------------------------------------------

    private const int SlotWall = 0;
    private const int SlotFloor = 0;     // en el builder plano: 0 = suelo, 1 = techo
    private const int SlotCeiling = 1;
    private const int SlotPipe = 0;      // en el builder de decoración: 0 = tuberías, 1 = agua
    private const int SlotWater = 1;

    private void BuildGeometry(System.Random rng)
    {
        LevelConfig cfg = config;
        GameObject rootGo = new GameObject("BackroomsLevel");
        rootGo.transform.SetParent(transform, false);
        levelRoot = rootGo.transform;
        GameObject decorGo = new GameObject("BackroomsDecor");
        decorGo.transform.SetParent(transform, false);
        decorRoot = decorGo.transform;

        if (cfg.layout == LayoutKind.Field) { BuildFieldGeometry(rng); return; }
        if (cfg.layout == LayoutKind.Ocean) { BuildOceanGeometry(rng); return; }

        float cs = cellSize;
        float totalW = w * cs;
        float totalH = h * cs;
        float t = wallThickness;
        float wh = wallHeight;

        // --- Texturas y materiales ---
        SurfaceSet wallSet = TextureLibrary.Get(cfg.wallStyle, wh);
        SurfaceSet floorSet = TextureLibrary.Get(cfg.floorStyle, wh);
        SurfaceSet ceilSet = TextureLibrary.Get(cfg.ceilingStyle, wh);
        HashSet<string> keys = new HashSet<string>();
        keys.Add(TextureLibrary.Key(cfg.wallStyle, wh));
        keys.Add(TextureLibrary.Key(cfg.floorStyle, wh));
        keys.Add(TextureLibrary.Key(cfg.ceilingStyle, wh));
        SurfaceSet rippleSet = null;
        if (cfg.water)
        {
            rippleSet = TextureLibrary.Get(SurfaceStyle.WaterNormal, wh);
            keys.Add(TextureLibrary.Key(SurfaceStyle.WaterNormal, wh));
        }
        TextureLibrary.ReleaseAllExcept(keys);

        Material wallMat = Track(ProceduralAssets.CreateSurfaceMaterial(wallSet, cfg.wallTint, 1f, 1f));
        WallMaterial = wallMat;   // v7.4.0: el rellano del ascensor usa el mismo material
        Material floorMat = Track(ProceduralAssets.CreateSurfaceMaterial(floorSet, cfg.floorTint, 1f, 1f));
        Material ceilMat = Track(ProceduralAssets.CreateSurfaceMaterial(ceilSet, cfg.ceilTint, 1f, 1f));
        Material pipeMat = cfg.pipes ? wallMat : null;

        // --- Chunks (cada uno es un renderer, para que las luces por píxel se repartan bien) ---
        int chunkCells = Garage != null ? 3 : Mathf.Max(1, Mathf.RoundToInt(8f / cs));   // v7.0.4: aparcamiento grande: menos renderers
        int cxN = (w + chunkCells - 1) / chunkCells;
        int czN = (h + chunkCells - 1) / chunkCells;
        MeshBuilder[,] wallsB = new MeshBuilder[cxN, czN];
        MeshBuilder[,] flatB = new MeshBuilder[cxN, czN];
        MeshBuilder[,] decorB = new MeshBuilder[cxN, czN];
        for (int cx = 0; cx < cxN; cx++)
        {
            for (int cz = 0; cz < czN; cz++)
            {
                wallsB[cx, cz] = new MeshBuilder(1);
                flatB[cx, cz] = new MeshBuilder(2);
                decorB[cx, cz] = new MeshBuilder(2);
            }
        }

        // Suelo y techo por chunk (UV en metros del mundo)
        if (Garage != null) BuildGarageFlats(flatB, chunkCells, cfg);   // v7.0.4: por celda, a la altura de su planta (rampas inclinadas)
        else
        for (int cx = 0; cx < cxN; cx++)
        {
            for (int cz = 0; cz < czN; cz++)
            {
                float x0 = cx * chunkCells * cs, x1 = Mathf.Min(w, (cx + 1) * chunkCells) * cs;
                float z0 = cz * chunkCells * cs, z1 = Mathf.Min(h, (cz + 1) * chunkCells) * cs;
                foreach (Rect fr in FloorRectsMinusPool(x0, z0, x1, z1))
                    flatB[cx, cz].AddQuad(SlotFloor, new Vector3(fr.xMin, 0f, fr.yMin), new Vector3(fr.xMax, 0f, fr.yMin), new Vector3(fr.xMax, 0f, fr.yMax), new Vector3(fr.xMin, 0f, fr.yMax),
                        Vector3.up, Vector3.right, Vector3.forward, cfg.floorTileU, cfg.floorTileV, 0f, 0f);
                foreach (Rect cr in RectsMinusHalls(Rect.MinMaxRect(x0, z0, x1, z1)))
                    flatB[cx, cz].AddQuad(SlotCeiling, new Vector3(cr.xMin, wh, cr.yMin), new Vector3(cr.xMax, wh, cr.yMin), new Vector3(cr.xMax, wh, cr.yMax), new Vector3(cr.xMin, wh, cr.yMax),
                        Vector3.down, Vector3.right, Vector3.forward, cfg.ceilTileU, cfg.ceilTileV, 0f, 0f);
            }
        }
        MeshBuilder poolWallB = new MeshBuilder(1);
        // Paredes verticales (a lo largo de Z) y horizontales (a lo largo de X)
        // Se acortan 2 mm en cada extremo para que los extremos no queden coplanares con otras paredes (z-fighting)
        float segLen = cs + t - 0.004f;
        int wallSegments = 0;
        for (int i = 0; i <= w; i++)
        {
            for (int j = 0; j < h; j++)
            {
                if (!wallsV[i, j]) continue;
                int cx = Mathf.Min(i, w - 1) / chunkCells, cz = j / chunkCells;
                float sh = wh;
                Vector3 center = new Vector3(i * cs, sh * 0.5f, (j + 0.5f) * cs);
                if (Garage != null) { GarageSpan(i - 1, j, i, j, out float yb, out float yt); sh = yt - yb; center.y = (yb + yt) * 0.5f; }
                bool poolSideV = HasPoolRoom && j >= PoolBlock.y && j < PoolBlock.yMax && (i == PoolBlock.x || i == PoolBlock.xMax);
                (poolSideV && !PoolFixed ? poolWallB : wallsB[cx, cz]).AddWallBox(SlotWall, center, new Vector3(t, sh, segLen), cfg.wallTileU, cfg.wallTileV, (float)rng.NextDouble());
                wallSegments++;
                if (cfg.pipes && rng.NextDouble() < 0.35)
                {
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float x = i * cs + side * (t * 0.5f + 0.16f);
                    float y = wh - 0.3f - (float)rng.NextDouble() * 0.5f;
                    decorB[cx, cz].AddTube(SlotPipe, new Vector3(x, y, j * cs + 0.1f), new Vector3(x, y, (j + 1) * cs - 0.1f), 0.08f + (float)rng.NextDouble() * 0.05f, 2.4f);
                }
            }
        }
        for (int i = 0; i < w; i++)
        {
            for (int j = 0; j <= h; j++)
            {
                if (!wallsH[i, j]) continue;
                int cx = i / chunkCells, cz = Mathf.Min(j, h - 1) / chunkCells;
                float sh = wh;
                Vector3 center = new Vector3((i + 0.5f) * cs, sh * 0.5f, j * cs);
                if (Garage != null) { GarageSpan(i, j - 1, i, j, out float yb, out float yt); sh = yt - yb; center.y = (yb + yt) * 0.5f; }
                bool poolSideH = HasPoolRoom && i >= PoolBlock.x && i < PoolBlock.xMax && (j == PoolBlock.y || j == PoolBlock.yMax);
                (poolSideH && !PoolFixed ? poolWallB : wallsB[cx, cz]).AddWallBox(SlotWall, center, new Vector3(segLen, sh, t), cfg.wallTileU, cfg.wallTileV, (float)rng.NextDouble());
                wallSegments++;
                if (cfg.pipes && rng.NextDouble() < 0.35)
                {
                    float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                    float z = j * cs + side * (t * 0.5f + 0.16f);
                    float y = wh - 0.3f - (float)rng.NextDouble() * 0.5f;
                    decorB[cx, cz].AddTube(SlotPipe, new Vector3(i * cs + 0.1f, y, z), new Vector3((i + 1) * cs - 0.1f, y, z), 0.08f + (float)rng.NextDouble() * 0.05f, 2.4f);
                }
            }
        }
        WallSegmentCount = wallSegments;

        // v6.8: jambas y dintel de las puertas fijas (v7.2.0: 2) de la sala de piscina (cara exterior = muro del nivel)
        if (PoolFixed)
        {
            RectInt pb = PoolBlock;
            float side = (cs - PoolDoorW) * 0.5f + t * 0.5f - 0.002f, lh = wh - PoolDoorTop;
            Vector2Int[] hs = { new Vector2Int(pb.x + PoolDoorColS, pb.y) };
            foreach (Vector2Int d in hs)
            {
                MeshBuilder mb = wallsB[Mathf.Min(d.x, w - 1) / chunkCells, Mathf.Min(d.y, h - 1) / chunkCells];
                float c0 = (d.x + 0.5f) * cs, z = d.y * cs;
                mb.AddWallBox(SlotWall, new Vector3(c0 - PoolDoorW * 0.5f - side * 0.5f, wh * 0.5f, z), new Vector3(side, wh, t), cfg.wallTileU, cfg.wallTileV, 0.37f);
                mb.AddWallBox(SlotWall, new Vector3(c0 + PoolDoorW * 0.5f + side * 0.5f, wh * 0.5f, z), new Vector3(side, wh, t), cfg.wallTileU, cfg.wallTileV, 0.61f);
                mb.AddWallBox(SlotWall, new Vector3(c0, PoolDoorTop + lh * 0.5f, z), new Vector3(PoolDoorW + 0.01f, lh, t), cfg.wallTileU, cfg.wallTileV, 0.13f);
            }
            Vector2Int[] vs = { new Vector2Int(pb.x, pb.y + PoolDoorRowW) };
            foreach (Vector2Int d in vs)
            {
                MeshBuilder mb = wallsB[Mathf.Min(d.x, w - 1) / chunkCells, Mathf.Min(d.y, h - 1) / chunkCells];
                float c0 = (d.y + 0.5f) * cs, x = d.x * cs;
                mb.AddWallBox(SlotWall, new Vector3(x, wh * 0.5f, c0 - PoolDoorW * 0.5f - side * 0.5f), new Vector3(t, wh, side), cfg.wallTileU, cfg.wallTileV, 0.29f);
                mb.AddWallBox(SlotWall, new Vector3(x, wh * 0.5f, c0 + PoolDoorW * 0.5f + side * 0.5f), new Vector3(t, wh, side), cfg.wallTileU, cfg.wallTileV, 0.83f);
                mb.AddWallBox(SlotWall, new Vector3(x, PoolDoorTop + lh * 0.5f, c0), new Vector3(t, lh, PoolDoorW + 0.01f), cfg.wallTileU, cfg.wallTileV, 0.47f);
            }
        }

        // v7.1.0: jambas y dintel de las puertas de las salas del parque de bolas (hueco 2 m x 2,5 m, como la piscina)
        foreach (RectInt hb in Halls)
        {
            float side = (cs - PoolDoorW) * 0.5f + t * 0.5f - 0.002f, lh = wh - PoolDoorTop;
            for (int i = hb.x; i < hb.xMax; i++)
                foreach (int j in new[] { hb.y, hb.yMax })
                {
                    if (!HallDoorH(hb, i, j)) continue;
                    MeshBuilder mb = wallsB[Mathf.Min(i, w - 1) / chunkCells, Mathf.Min(j, h - 1) / chunkCells];
                    float c0 = (i + 0.5f) * cs, z = j * cs;
                    mb.AddWallBox(SlotWall, new Vector3(c0 - PoolDoorW * 0.5f - side * 0.5f, wh * 0.5f, z), new Vector3(side, wh, t), cfg.wallTileU, cfg.wallTileV, 0.37f);
                    mb.AddWallBox(SlotWall, new Vector3(c0 + PoolDoorW * 0.5f + side * 0.5f, wh * 0.5f, z), new Vector3(side, wh, t), cfg.wallTileU, cfg.wallTileV, 0.61f);
                    mb.AddWallBox(SlotWall, new Vector3(c0, PoolDoorTop + lh * 0.5f, z), new Vector3(PoolDoorW + 0.01f, lh, t), cfg.wallTileU, cfg.wallTileV, 0.13f);
                }
            for (int j = hb.y; j < hb.yMax; j++)
                foreach (int i in new[] { hb.x, hb.xMax })
                {
                    if (!HallDoorV(hb, i, j)) continue;
                    MeshBuilder mb = wallsB[Mathf.Min(i, w - 1) / chunkCells, Mathf.Min(j, h - 1) / chunkCells];
                    float c0 = (j + 0.5f) * cs, x = i * cs;
                    mb.AddWallBox(SlotWall, new Vector3(x, wh * 0.5f, c0 - PoolDoorW * 0.5f - side * 0.5f), new Vector3(t, wh, side), cfg.wallTileU, cfg.wallTileV, 0.29f);
                    mb.AddWallBox(SlotWall, new Vector3(x, wh * 0.5f, c0 + PoolDoorW * 0.5f + side * 0.5f), new Vector3(t, wh, side), cfg.wallTileU, cfg.wallTileV, 0.83f);
                    mb.AddWallBox(SlotWall, new Vector3(x, PoolDoorTop + lh * 0.5f, c0), new Vector3(t, lh, PoolDoorW + 0.01f), cfg.wallTileU, cfg.wallTileV, 0.47f);
                }
        }

        // v7.5.0: hotel: jambas y dintel de las puertas que se abren (hueco HotelDecor.DoorW x DoorH centrado en el muro)
        if (Hotel != null)
        {
            float hw = HotelDecor.DoorW, hh = HotelDecor.DoorH, side = (cs - hw) * 0.5f + t * 0.5f - 0.002f, lh = wh - hh;
            foreach (HotelPlan.Edge e in Hotel.doorways)
            {
                int i = e.c.x, j = e.c.y; bool vert = e.d == 1 || e.d == 3;
                int li = e.d == 1 ? i + 1 : i, lj = e.d == 0 ? j + 1 : j;   // línea del muro
                MeshBuilder mb = wallsB[Mathf.Min(i, w - 1) / chunkCells, Mathf.Min(j, h - 1) / chunkCells];
                if (vert)
                {
                    float c0 = (j + 0.5f) * cs, x = li * cs;
                    mb.AddWallBox(SlotWall, new Vector3(x, wh * 0.5f, c0 - hw * 0.5f - side * 0.5f), new Vector3(t, wh, side), cfg.wallTileU, cfg.wallTileV, 0.29f);
                    mb.AddWallBox(SlotWall, new Vector3(x, wh * 0.5f, c0 + hw * 0.5f + side * 0.5f), new Vector3(t, wh, side), cfg.wallTileU, cfg.wallTileV, 0.83f);
                    mb.AddWallBox(SlotWall, new Vector3(x, hh + lh * 0.5f, c0), new Vector3(t, lh, hw + 0.01f), cfg.wallTileU, cfg.wallTileV, 0.47f);
                }
                else
                {
                    float c0 = (i + 0.5f) * cs, z = lj * cs;
                    mb.AddWallBox(SlotWall, new Vector3(c0 - hw * 0.5f - side * 0.5f, wh * 0.5f, z), new Vector3(side, wh, t), cfg.wallTileU, cfg.wallTileV, 0.37f);
                    mb.AddWallBox(SlotWall, new Vector3(c0 + hw * 0.5f + side * 0.5f, wh * 0.5f, z), new Vector3(side, wh, t), cfg.wallTileU, cfg.wallTileV, 0.61f);
                    mb.AddWallBox(SlotWall, new Vector3(c0, hh + lh * 0.5f, z), new Vector3(hw + 0.01f, lh, t), cfg.wallTileU, cfg.wallTileV, 0.13f);
                }
            }
        }

        // Pilares (aparcamiento): en las intersecciones de la rejilla cada N celdas
        int pillars = 0;
        if (cfg.layout == LayoutKind.OpenPillars && cfg.pillarEvery > 0)
        {
            for (int i = cfg.pillarEvery; i < w; i += cfg.pillarEvery)
            {
                for (int j = cfg.pillarEvery; j < h; j += cfg.pillarEvery)
                {
                    int cx = Mathf.Min(i, w - 1) / chunkCells, cz = Mathf.Min(j, h - 1) / chunkCells;
                    float py = wh * 0.5f, ph = wh;
                    if (Garage != null)
                    {
                        GarageSpan(i - 1, j - 1, i, j, out float b0, out float t0); GarageSpan(i - 1, j, i, j - 1, out float b1, out float t1);
                        float yb = Mathf.Min(b0, b1), yt = Mathf.Max(t0, t1); ph = yt - yb; py = (yb + yt) * 0.5f;
                    }
                    float psz = cfg.pillarSize * (Garage != null ? 0.92f + 0.16f * (float)new System.Random(i * 7919 + j * 104729 + UsedSeed).NextDouble() : 1f);   // v7.0.4: pequeñas variaciones
                    wallsB[cx, cz].AddWallBox(SlotWall, new Vector3(i * cs, py, j * cs),
                        new Vector3(psz, ph, psz), cfg.wallTileU, cfg.wallTileV, (float)rng.NextDouble());
                    pillars++;
                }
            }
        }
        // v7.3.0: nivel 0 «fotorrealista»: pilares cuadrados empapelados (con el mismo zócalo) en las esquinas libres de las salas abiertas,
        // como en la foto clásica. RNG propio (no altera el resto de la generación); lejos de la cabina y de la salida.
        if (cfg.roomPillars > 0f && cfg.layout == LayoutKind.Maze && Garage == null)
        {
            System.Random pr = new System.Random(UsedSeed * 977 + 13);
            for (int i = 1; i < w; i++)
                for (int j = 1; j < h; j++)
                {
                    if (wallsV[i, j - 1] || wallsV[i, j] || wallsH[i - 1, j] || wallsH[i, j]) continue;
                    bool nearSpecial = false;
                    foreach (Vector2Int c in new[] { startCell, exitCell })
                        if ((i == c.x || i == c.x + 1) && (j == c.y || j == c.y + 1)) nearSpecial = true;
                    if (nearSpecial || pr.NextDouble() > cfg.roomPillars) continue;
                    int cx = Mathf.Min(i, w - 1) / chunkCells, cz = Mathf.Min(j, h - 1) / chunkCells;
                    float psz = cfg.roomPillarSize;
                    wallsB[cx, cz].AddWallBox(SlotWall, new Vector3(i * cs, wh * 0.5f, j * cs), new Vector3(psz, wh, psz), cfg.wallTileU, cfg.wallTileV, (float)pr.NextDouble());
                    pillars++;
                }
        }
        PillarCount = pillars;

        // Instanciar los chunks
        Transform chunksRoot = new GameObject("Chunks").transform;
        chunksRoot.SetParent(levelRoot, false);
        for (int cx = 0; cx < cxN; cx++)
        {
            for (int cz = 0; cz < czN; cz++)
            {
                string tag = cx + "_" + cz;
                if (!wallsB[cx, cz].IsEmpty)
                {
                    Mesh wm = Track(wallsB[cx, cz].ToMesh("Walls_" + tag));
                    GameObject go = new GameObject("Walls_" + tag);
                    go.transform.SetParent(chunksRoot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = wm;
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = wallMat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    go.AddComponent<MeshCollider>().sharedMesh = wm;
                }
                if (!flatB[cx, cz].IsEmpty)
                {
                    Mesh fm = Track(flatB[cx, cz].ToMesh("Flat_" + tag));
                    GameObject go = new GameObject("FloorCeiling_" + tag);
                    go.transform.SetParent(chunksRoot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = fm;
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = new Material[] { floorMat, ceilMat };
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    if (Garage != null) go.AddComponent<MeshCollider>().sharedMesh = fm;   // v7.0.4: suelos a varias alturas y rampas
                }
                if (!decorB[cx, cz].IsEmpty)
                {
                    Mesh dm = Track(decorB[cx, cz].ToMesh("Decor_" + tag));
                    GameObject go = new GameObject("Decor_" + tag);
                    go.transform.SetParent(decorRoot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = dm;
                    MeshRenderer mr = go.AddComponent<MeshRenderer>();
                    // Los submeshes vacíos usan igualmente un material (no se dibujan)
                    mr.sharedMaterials = new Material[] { pipeMat != null ? pipeMat : wallMat, wallMat };
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }
            }
        }

        // Colliders planos de suelo y techo (cajas grandes: sin costuras)
        if (Garage != null) { /* v7.0.4: colisión con la propia malla de suelos/techos */ }
        else if (!HasPoolRoom && Halls.Count == 0) AddBox("FloorCollider", new Vector3(totalW * 0.5f, -0.1f, totalH * 0.5f), new Vector3(totalW + 2f * t, 0.2f, totalH + 2f * t));
        else
        {
            int k = 0;
            foreach (Rect fr in FloorRectsMinusPool(-t, -t, totalW + t, totalH + t))
                AddBox("FloorCollider" + (k++ == 0 ? "" : k.ToString()), new Vector3(fr.center.x, -0.1f, fr.center.y), new Vector3(fr.width, 0.2f, fr.height));
            if (HasPoolRoom) PoolRoomSetPiece.Build(this, cfg, levelRoot, poolWallB.IsEmpty ? null : Track(poolWallB.ToMesh("PoolRoomWalls")));
            if (Halls.Count > 0) BallpitSetPiece.Build(this, cfg, levelRoot);
        }
        if (Garage == null) AddBox("CeilingCollider", new Vector3(totalW * 0.5f, wh + 0.1f, totalH * 0.5f), new Vector3(totalW + 2f * t, 0.2f, totalH + 2f * t));

        // Luces
        BuildFixtures(rng);

        // Posiciones clave
        Vector3 startLocal = CellCenterLocal(startCell.x, startCell.y);
        PlayerStart = levelRoot.TransformPoint(startLocal + new Vector3(0f, 0.1f, 0f));
        PlayerStartYaw = ComputeStartYaw();

        MonsterSpawns = new List<Vector3>();
        List<Vector2Int> mc = PickMonsterCells(rng, Mathf.Max(1, cfg.monsterCount) + Mathf.Max(0, extraMonsters));
        for (int i = 0; i < mc.Count; i++) MonsterSpawns.Add(levelRoot.TransformPoint(HotelSpot(mc[i].x, mc[i].y, CellCenterLocal(mc[i].x, mc[i].y))));
        MonsterSpawn = MonsterSpawns[0];

        PickBatterySpots(rng, mc);
        PickExtraSpots(new System.Random(UsedSeed * 31 + 7), Mathf.Max(1, cfg.extraSpots));
        PickKeySpots(new System.Random(UsedSeed * 71 + 29), ExitLock.KeyCount);
        if (Halls.Count > 0) PlaceHallSpots(new System.Random(UsedSeed * 53 + 11));   // v7.1.0: llaves y objetos dentro de las salas (una llave entre las bolas)
        BuildExitDoor(rng);
        if (Garage != null) GarageDecor.Build(this, Garage, cfg, decorRoot, wallMat, o => Track(o));
        if (Hotel != null) HotelDecor.Build(this, Hotel, cfg, levelRoot, decorRoot, o => Track(o));   // v7.5.0: el hotel entero (puertas, muebles, atrezo)

        // Agua realista (shader propio + volumen con interacción)
        if (cfg.water)
        {
            Water = WaterVolume.Create(this, cfg, decorRoot, rippleSet);
        }
    }

    // v7.0.4: rango vertical que debe cubrir un muro entre dos celdas (la que esté fuera de la rejilla no cuenta)
    private void GarageSpan(int ax, int az, int bx, int bz, out float yb, out float yt)
    {
        yb = float.MaxValue; yt = float.MinValue;
        if (ax >= 0 && az >= 0 && ax < w && az < h) { yb = Mathf.Min(yb, Garage.CellMin(ax, az)); yt = Mathf.Max(yt, Garage.CellMax(ax, az) + wallHeight); }
        if (bx >= 0 && bz >= 0 && bx < w && bz < h) { yb = Mathf.Min(yb, Garage.CellMin(bx, bz)); yt = Mathf.Max(yt, Garage.CellMax(bx, bz) + wallHeight); }
        if (yb > yt) { yb = 0f; yt = wallHeight; }
        yb -= 0.05f;
    }

    private void BuildGarageFlats(MeshBuilder[,] flatB, int chunkCells, LevelConfig cfg)
    {
        float cs = cellSize, wh = wallHeight;
        for (int x = 0; x < w; x++)
            for (int z = 0; z < h; z++)
            {
                MeshBuilder mb = flatB[x / chunkCells, z / chunkCells];
                float x0 = x * cs, x1 = (x + 1) * cs, z0 = z * cs, z1 = (z + 1) * cs;
                float y0 = Garage.Y(x0 + 0.01f, z0 + 0.001f), y1 = Garage.Y(x0 + 0.01f, z1 - 0.001f);
                if (Garage.IsRamp(x, z))
                {
                    int b = Garage.rampB[new Vector2Int(x, z)]; int zb = Garage.boundaries[b];
                    y0 = Mathf.Lerp(Garage.DeckY(b), Garage.DeckY(b + 1), Mathf.Clamp01((z0 - (zb - 1) * cs) / (2f * cs)));
                    y1 = Mathf.Lerp(Garage.DeckY(b), Garage.DeckY(b + 1), Mathf.Clamp01((z1 - (zb - 1) * cs) / (2f * cs)));
                }
                Vector3 n = Vector3.Cross(new Vector3(0f, y1 - y0, cs), Vector3.right).normalized;
                mb.AddQuad(SlotFloor, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1),
                    n, Vector3.right, Vector3.forward, cfg.floorTileU, cfg.floorTileV, 0f, 0f);
                mb.AddQuad(SlotCeiling, new Vector3(x0, y0 + wh, z0), new Vector3(x1, y0 + wh, z0), new Vector3(x1, y1 + wh, z1), new Vector3(x0, y1 + wh, z1),
                    -n, Vector3.right, Vector3.forward, cfg.ceilTileU, cfg.ceilTileV, 0f, 0f);
            }
    }

    // Rectángulo (x0,z0)-(x1,z1) menos el bloque de la piscina (hasta 4 trozos)
    private List<Rect> FloorRectsMinusPool(float x0, float z0, float x1, float z1)
    {
        List<Rect> outR = new List<Rect>();
        foreach (Rect a in FloorRectsMinusPoolOnly(x0, z0, x1, z1)) outR.AddRange(RectsMinusHalls(a));
        return outR;
    }

    // v7.1.0: rectángulo menos las salas del parque de bolas (resta sucesiva)
    private List<Rect> RectsMinusHalls(Rect a)
    {
        List<Rect> cur = new List<Rect> { a };
        float cs = cellSize;
        foreach (RectInt hb in Halls)
        {
            Rect h = Rect.MinMaxRect(hb.x * cs, hb.y * cs, hb.xMax * cs, hb.yMax * cs);
            List<Rect> nx = new List<Rect>();
            foreach (Rect r0 in cur)
            {
                if (h.xMax <= r0.xMin || h.xMin >= r0.xMax || h.yMax <= r0.yMin || h.yMin >= r0.yMax) { nx.Add(r0); continue; }
                float ix0 = Mathf.Max(r0.xMin, h.xMin), ix1 = Mathf.Min(r0.xMax, h.xMax), iz0 = Mathf.Max(r0.yMin, h.yMin), iz1 = Mathf.Min(r0.yMax, h.yMax);
                if (r0.yMin < iz0) nx.Add(Rect.MinMaxRect(r0.xMin, r0.yMin, r0.xMax, iz0));
                if (iz1 < r0.yMax) nx.Add(Rect.MinMaxRect(r0.xMin, iz1, r0.xMax, r0.yMax));
                if (r0.xMin < ix0) nx.Add(Rect.MinMaxRect(r0.xMin, iz0, ix0, iz1));
                if (ix1 < r0.xMax) nx.Add(Rect.MinMaxRect(ix1, iz0, r0.xMax, iz1));
            }
            cur = nx;
        }
        return cur;
    }

    private List<Rect> FloorRectsMinusPoolOnly(float x0, float z0, float x1, float z1)
    {
        List<Rect> r = new List<Rect>();
        if (!HasPoolRoom) { r.Add(Rect.MinMaxRect(x0, z0, x1, z1)); return r; }
        float cs = cellSize;
        float bx0 = PoolBlock.x * cs, bx1 = PoolBlock.xMax * cs, bz0 = PoolBlock.y * cs, bz1 = PoolBlock.yMax * cs;
        if (bx1 <= x0 || bx0 >= x1 || bz1 <= z0 || bz0 >= z1) { r.Add(Rect.MinMaxRect(x0, z0, x1, z1)); return r; }
        float ix0 = Mathf.Max(x0, bx0), ix1 = Mathf.Min(x1, bx1), iz0 = Mathf.Max(z0, bz0), iz1 = Mathf.Min(z1, bz1);
        if (z0 < iz0) r.Add(Rect.MinMaxRect(x0, z0, x1, iz0));
        if (iz1 < z1) r.Add(Rect.MinMaxRect(x0, iz1, x1, z1));
        if (x0 < ix0) r.Add(Rect.MinMaxRect(x0, iz0, ix0, iz1));
        if (ix1 < x1) r.Add(Rect.MinMaxRect(ix1, iz0, x1, iz1));
        return r;
    }

    private BoxCollider AddBox(string name, Vector3 center, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(levelRoot, false);
        BoxCollider b = go.AddComponent<BoxCollider>();
        b.center = center;
        b.size = size;
        return b;
    }

    private T Track<T>(T obj) where T : Object
    {
        createdAssets.Add(obj);
        return obj;
    }

    // ------------------------------------------------------------------
    // Accesorios de luz (paneles, tubos, apliques, emergencia)
    // ------------------------------------------------------------------

    private void BuildFixtures(System.Random rng)
    {
        LevelConfig cfg = config;
        Transform lightsRoot = new GameObject("Fixtures").transform;
        lightsRoot.SetParent(levelRoot, false);

        LightCuller culler = lightsRoot.gameObject.AddComponent<LightCuller>();
        culler.maxActive = cfg.maxRealtimeLights;
        Culler = culler;

        Material emissive = Track(ProceduralAssets.CreateMaterial(new Color(0.95f, 0.95f, 0.85f), null, Vector2.one, 0.2f));
        ProceduralAssets.SetEmission(emissive, cfg.emissionColor);
        Material dead = Track(ProceduralAssets.CreateMaterial(new Color(0.45f, 0.45f, 0.42f), null, Vector2.one, 0.1f));
        Material housing = Track(ProceduralAssets.CreateMaterial(new Color(0.12f, 0.12f, 0.12f), null, Vector2.one, 0.4f));
        if (cfg.hotel) { housing.color = new Color(0.50f, 0.37f, 0.19f); housing.SetFloat("_Glossiness", 0.55f); housing.SetFloat("_Metallic", 0.9f); }   // v7.5.0: latón envejecido
        // v7.3.0c nivel 0: pantallas empotradas realistas (marco de chapa + difusor prismático horneado, 4 variantes por atlas)
        trofOn = trofOff = trofFrame = null; trofFrames.Clear();
        if (cfg.troffers && cfg.fixture == FixtureKind.Panel && Garage == null)
        {
            Texture2D on = Resources.Load<Texture2D>("Textures/L0/Troffer_on"), off = Resources.Load<Texture2D>("Textures/L0/Troffer_off");
            if (on != null && off != null)
            {
                trofOn = Track(ProceduralAssets.CreateMaterial(new Color(0.5f, 0.5f, 0.5f), on, Vector2.one, 0.3f));
                // el difusor horneado ya lleva el tono; aquí solo un toque verdoso (antes el color plano saturado parecía neón)
                Color e = cfg.emissionColor; float em = e.maxColorComponent;
                ProceduralAssets.SetEmission(trofOn, Color.Lerp(new Color(em * 0.95f, em, em * 0.85f), e, 0.35f) * 1.05f);
                if (trofOn.HasProperty("_EmissionMap")) trofOn.SetTexture("_EmissionMap", on);
                trofOff = Track(ProceduralAssets.CreateMaterial(Color.white, off, Vector2.one, 0.3f));
                trofFrame = Track(ProceduralAssets.CreateMaterial(new Color(0.86f, 0.85f, 0.80f), null, Vector2.one, 0.35f));
                emissive = trofOn; dead = trofOff;
            }
        }

        AudioClip buzzClip = null;
        if (cfg.buzzChance > 0f)
        {
            buzzClip = Track(ProceduralAudio.CreateBuzzClip());
        }

        sconceAlong.Clear();
        int fixtures = 0, live = 0; DarkClusterFixtures = 0;
        int spacing = Mathf.Max(1, cfg.lightSpacing);
        float wh = wallHeight;

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++)
            {
                Vector3 c = CellCenterLocal(x, z);

                if (Hotel != null)
                {   // v7.5.0: apliques del hotel: en los pasillos casi en cada celda (a un lado y a otro), entre las puertas (a ±1,1 m del centro del muro)
                    HotelPlan.K hk = Hotel.kind[x, z];
                    float chance = hk == HotelPlan.K.Corridor || hk == HotelPlan.K.Gallery ? cfg.sconceChance : hk == HotelPlan.K.Linen ? cfg.sconceChance * 0.4f : hk == HotelPlan.K.Lift ? cfg.sconceChance * 0.7f : hk == HotelPlan.K.Hall ? 0.75f : hk == HotelPlan.K.Stair ? 0.5f : 0f;
                    if (chance <= 0f || (x == exitCell.x && z == exitCell.y)) continue;
                    List<int> hs = new List<int>(4);
                    for (int d = 0; d < 4; d++) if (!IsOpen(new Vector2Int(x, z), d) && !(hk == HotelPlan.K.Hall && d == 2 && z == 0)) hs.Add(d);   // el muro sur de las salas lleva ventanales
                    if (hs.Count == 0 || rng.NextDouble() >= chance) continue;
                    int hside = hs[(x + z) % hs.Count];
                    Vector3 hdir = new Vector3(Dirs[hside].x, 0f, Dirs[hside].y), hperp = new Vector3(-hdir.z, 0f, hdir.x);
                    float halong = (((x + z) / Mathf.Max(1, hs.Count)) % 2 == 0 ? 1.1f : -1.1f);
                    Vector3 hpos = c + hdir * (cellSize * 0.5f - wallThickness * 0.5f) + hperp * halong + new Vector3(0f, wh * 0.66f, 0f);
                    bool hdead = rng.NextDouble() < cfg.deadChance;
                    CreateSconce(lightsRoot, hpos, hdir, hdead, emissive, dead, housing, rng, buzzClip);
                    fixtures++; if (!hdead) live++;
                    continue;
                }
                if (cfg.fixture == FixtureKind.Sconce)
                {
                    // Aplique en una pared de la celda (si tiene alguna)
                    List<int> sides = new List<int>(4);
                    for (int d = 0; d < 4; d++) if (!IsOpen(new Vector2Int(x, z), d)) sides.Add(d);
                    if (sides.Count == 0 || rng.NextDouble() >= cfg.sconceChance) continue;
                    int side = sides[rng.Next(sides.Count)];
                    Vector3 dir = new Vector3(Dirs[side].x, 0f, Dirs[side].y);
                    float along = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.4f;
                    Vector3 perp = new Vector3(-dir.z, 0f, dir.x);
                    Vector3 wallPos = c + dir * (cellSize * 0.5f - wallThickness * 0.5f) + perp * along + new Vector3(0f, wh * 0.66f, 0f);
                    bool isDead = rng.NextDouble() < cfg.deadChance;
                    sconceAlong[(x * 1000 + z) * 4 + side] = Vector3.Dot(perp * along, new Vector3(dir.z, 0f, -dir.x));   // a lo largo de u = (-n.z, n.x) con n = -dir
                    CreateSconce(lightsRoot, wallPos, dir, isDead, emissive, dead, housing, rng, buzzClip);
                    fixtures++;
                    if (!isDead) live++;
                    continue;
                }

                if (x % spacing != 0 || z % spacing != 0) continue;
                if (InPoolBlock(x, z)) continue;
                if (Garage != null && Garage.IsRamp(x, z)) continue;   // v7.0.4: techo inclinado   // la sala de piscina tiene su propia luz (tragaluces/ventanas)
                int cst = ClusterState(x, z);
                bool deadFix = rng.NextDouble() < cfg.deadChance || cst == 1;
                nextForceFlicker = cst == 2; if (cst == 1) DarkClusterFixtures++;
                curCell = new Vector2Int(x, z);
                CreateCeilingFixture(lightsRoot, c, deadFix, emissive, dead, housing, rng, buzzClip);
                nextForceFlicker = false;
                fixtures++;
                if (!deadFix) live++;
            }
        }
        // v7.3.0 nivel 0: paneles fluorescentes «de relleno» en las demás celdas (solo emisivos, sin Light), en mallas combinadas por bloque
        // de 8 × 8 celdas: el techo se ve lleno de paneles como en la foto sin coste de luces en tiempo real (vale también en Bajo)
        FillerPanelCount = 0;
        if (cfg.fillerPanels && cfg.fixture == FixtureKind.Panel && Garage == null)
        {
            Mesh cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var groups = new Dictionary<int, List<CombineInstance>>();
            for (int x = 0; x < w; x++)
                for (int z = 0; z < h; z++)
                {
                    if (x % spacing == 0 && z % spacing == 0) continue;
                    if (InPoolBlock(x, z)) continue;
                    Vector3 c = CellCenterLocal(x, z);
                    int key = (x / 8) * 1000 + z / 8 + (ClusterState(x, z) == 1 ? 500000 : 0);   // grupo apagado: material de panel muerto
                    if (!groups.TryGetValue(key, out List<CombineInstance> l)) groups[key] = l = new List<CombineInstance>();
                    bool along = ((x * 7 + z * 3) & 1) == 0;
                    if (trofOn != null)
                    {
                        int var = TrofferMesh.Variant(UsedSeed, x, z, out bool flip);
                        Matrix4x4 pose = TrofferMesh.Pose(new Vector3(c.x, c.y + wh, c.z), along, flip);
                        l.Add(new CombineInstance { mesh = TrofferMesh.Diffuser(var), transform = pose });
                        AddTrofFrame(x, z, pose);
                    }
                    else
                    l.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(new Vector3(c.x, c.y + wh - 0.03f, c.z), Quaternion.identity, along ? new Vector3(1.6f, 0.06f, 0.8f) : new Vector3(0.8f, 0.06f, 1.6f)) });
                    FillerPanelCount++;
                }
            foreach (var kv in groups)
            {
                GameObject go = new GameObject("PanelBatch_" + kv.Key); go.transform.SetParent(lightsRoot, false);
                Mesh m = new Mesh { name = "PanelBatch" }; m.CombineMeshes(kv.Value.ToArray(), true, true); m.RecalculateBounds(); Track(m);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = kv.Key >= 500000 ? dead : emissive;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            }
        }
        // marcos de chapa de todas las pantallas (con y sin luz), combinados por bloque de 8 × 8 celdas: 1 llamada por bloque
        TrofferFrameBatches = 0;
        foreach (var kv in trofFrames)
        {
            GameObject go = new GameObject("TrofferFrames_" + kv.Key); go.transform.SetParent(lightsRoot, false);
            Mesh m = new Mesh { name = "TrofferFrames" }; m.CombineMeshes(kv.Value.ToArray(), true, true); m.RecalculateBounds(); Track(m);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            MeshRenderer mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = trofFrame;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            TrofferFrameBatches++;
        }
        trofFrames.Clear();
        FixtureCount = fixtures;
        LiveFixtureCount = live;
    }
    private Material trofOn, trofOff, trofFrame;
    private Vector2Int curCell;
    private readonly Dictionary<int, List<CombineInstance>> trofFrames = new Dictionary<int, List<CombineInstance>>();
    public int TrofferFrameBatches { get; private set; }
    public bool UsesTroffers { get { return trofOn != null; } }
    private void AddTrofFrame(int x, int z, Matrix4x4 pose)
    {
        int key = (x / 8) * 1000 + z / 8;
        if (!trofFrames.TryGetValue(key, out List<CombineInstance> l)) trofFrames[key] = l = new List<CombineInstance>();
        l.Add(new CombineInstance { mesh = TrofferMesh.Frame(), transform = pose });
    }
    public int FillerPanelCount { get; private set; }
    public int DarkClusterFixtures { get; private set; }

    // v7.3.0b: grupos de clusterCells × clusterCells celdas apagados (1) o parpadeando (2), deterministas por semilla (iguales en
    // multijugador); nunca junto a la salida ni al punto de aparición, para que siempre se vea el camino al empezar y al llegar
    private bool nextForceFlicker;
    private int ClusterState(int x, int z)
    {
        LevelConfig cfg = config;
        if (cfg.darkClusters <= 0f && cfg.flickerClusters <= 0f) return 0;
        if (Mathf.Abs(x - startCell.x) <= 2 && Mathf.Abs(z - startCell.y) <= 2) return 0;
        if (Mathf.Abs(x - exitCell.x) <= 2 && Mathf.Abs(z - exitCell.y) <= 2) return 0;
        int cc = Mathf.Max(1, cfg.clusterCells);
        float hsh = ClusterHash(x / cc, z / cc);
        if (hsh < cfg.darkClusters) return 1;
        if (hsh < cfg.darkClusters + cfg.flickerClusters) return 2;
        return 0;
    }
    private float ClusterHash(int cx, int cz)
    {
        unchecked
        {
            uint n = (uint)(UsedSeed * 73856093) ^ (uint)(cx * 19349663) ^ (uint)(cz * 83492791) ^ 0x9E3779B9u;
            n ^= n >> 13; n *= 1274126177u; n ^= n >> 16;
            return (n & 0xffffu) / 65535f;
        }
    }

    private void CreateCeilingFixture(Transform parent, Vector3 c, bool isDead, Material emissive, Material dead,
        Material housing, System.Random rng, AudioClip buzzClip)
    {
        LevelConfig cfg = config;
        float wh = wallHeight;
        GameObject fx = new GameObject("Fixture");
        fx.transform.SetParent(parent, false);
        fx.transform.localPosition = new Vector3(c.x, c.y, c.z);   // v7.0.4: c.y = suelo de la planta (0 salvo en el aparcamiento)

        Renderer lens;
        float lightY = wh - 0.5f;
        Material lensMat = isDead ? dead : emissive;

        switch (cfg.fixture)
        {
            case FixtureKind.Tube:
            {
                bool alongX = rng.NextDouble() < 0.5;
                Vector3 scale = alongX ? new Vector3(1.3f, 0.07f, 0.22f) : new Vector3(0.22f, 0.07f, 1.3f);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Housing", fx.transform, new Vector3(0f, wh - 0.035f, 0f), scale, housing, false);
                Vector3 lscale = alongX ? new Vector3(1.2f, 0.04f, 0.1f) : new Vector3(0.1f, 0.04f, 1.2f);
                lens = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Tube", fx.transform, new Vector3(0f, wh - 0.085f, 0f), lscale, lensMat, false).GetComponent<Renderer>();
                lightY = wh - 0.6f;
                break;
            }
            case FixtureKind.Emergency:
            {
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Housing", fx.transform, new Vector3(0f, wh - 0.1f, 0f), new Vector3(0.4f, 0.2f, 0.26f), housing, false);
                lens = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Lens", fx.transform, new Vector3(0f, wh - 0.215f, 0f), new Vector3(0.28f, 0.03f, 0.16f), lensMat, false).GetComponent<Renderer>();
                lightY = wh - 0.55f;
                break;
            }
            default: // Panel
            {
                if (trofOn != null)
                {   // v7.3.0c: difusor horneado (variante por celda) + marco combinado aparte; el parpadeo sigue actuando sobre el difusor
                    int var = TrofferMesh.Variant(UsedSeed, curCell.x, curCell.y, out bool flip);
                    GameObject pg = new GameObject("Panel"); pg.transform.SetParent(fx.transform, false);
                    pg.transform.localPosition = new Vector3(0f, wh, 0f); pg.transform.localRotation = Quaternion.Euler(0f, flip ? 180f : 0f, 0f);
                    pg.AddComponent<MeshFilter>().sharedMesh = TrofferMesh.Diffuser(var);
                    MeshRenderer pr = pg.AddComponent<MeshRenderer>(); pr.sharedMaterial = lensMat;
                    pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; pr.receiveShadows = false;
                    lens = pr;
                    AddTrofFrame(curCell.x, curCell.y, parent.localToWorldMatrix.inverse * fx.transform.localToWorldMatrix * Matrix4x4.TRS(new Vector3(0f, wh, 0f), Quaternion.Euler(0f, flip ? 180f : 0f, 0f), Vector3.one));
                    lightY = wh - 0.5f;
                    break;
                }
                lens = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Panel", fx.transform, new Vector3(0f, wh - 0.03f, 0f), new Vector3(1.6f, 0.06f, 0.8f), lensMat, false).GetComponent<Renderer>();
                lightY = wh - 0.5f;
                break;
            }
        }

        if (isDead) return;
        CreateFixtureLight(fx.transform, new Vector3(0f, lightY, 0f), lens, rng, buzzClip);
    }

    private void CreateSconce(Transform parent, Vector3 wallPos, Vector3 dirToWall, bool isDead, Material emissive,
        Material dead, Material housing, System.Random rng, AudioClip buzzClip)
    {
        GameObject fx = new GameObject("Sconce");
        fx.transform.SetParent(parent, false);
        fx.transform.localPosition = wallPos;
        fx.transform.localRotation = Quaternion.LookRotation(-dirToWall); // +Z local = hacia la habitación

        if (config != null && config.hotel)
        {   // v7.5.0: aplique de hotel: placa y brazo de latón, pantalla de tela en tambor (emisiva cálida) sobre una vela
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Plate", fx.transform, new Vector3(0f, -0.02f, 0.008f), new Vector3(0.11f, 0.008f, 0.11f), housing, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Arm", fx.transform, new Vector3(0f, -0.06f, 0.075f), new Vector3(0.018f, 0.018f, 0.14f), housing, false);
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Cup", fx.transform, new Vector3(0f, -0.04f, 0.15f), new Vector3(0.05f, 0.02f, 0.05f), housing, false);
            Renderer shade = ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Shade", fx.transform, new Vector3(0f, 0.06f, 0.15f),
                new Vector3(0.17f, 0.075f, 0.17f), isDead ? dead : emissive, false).GetComponent<Renderer>();
            if (isDead) return;
            CreateFixtureLight(fx.transform, new Vector3(0f, 0.05f, 0.45f), shade, rng, buzzClip);
            return;
        }
        // Placa trasera + pantalla emisiva
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Plate", fx.transform, new Vector3(0f, 0f, 0.015f), new Vector3(0.16f, 0.32f, 0.03f), housing, false);
        Renderer lens = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Shade", fx.transform, new Vector3(0f, 0.04f, 0.1f),
            new Vector3(0.11f, 0.2f, 0.11f), isDead ? dead : emissive, false).GetComponent<Renderer>();
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Arm", fx.transform, new Vector3(0f, -0.1f, 0.06f), new Vector3(0.03f, 0.03f, 0.1f), housing, false);

        if (isDead) return;
        CreateFixtureLight(fx.transform, new Vector3(0f, 0.05f, 0.45f), lens, rng, buzzClip);
    }

    // v7.5.0: haz de luna de los ventanales del hotel (foco frío sin parpadeo, bajo el LightCuller como el resto)
    public void AddHotelSpot(Transform parent, Vector3 localPos, Vector3 localDir, Color color, float intensity, float range, float angle, Texture cookie = null)
    {
        System.Random lr = new System.Random(UsedSeed * 197 + Mathf.RoundToInt(localPos.x * 29f));
        CreateFixtureLight(parent, localPos, null, lr, null);
        Transform lt = parent.GetChild(parent.childCount - 1);
        lt.localRotation = Quaternion.LookRotation(localDir, Vector3.up);
        Light l = lt.GetComponent<Light>(); FlickeringLight f = lt.GetComponent<FlickeringLight>();
        if (l != null) { l.type = LightType.Spot; l.spotAngle = angle; l.innerSpotAngle = angle * 0.35f; l.color = color; l.intensity = intensity; l.range = range; if (cookie != null) l.cookie = cookie; }
        if (f != null) { f.baseIntensity = intensity; f.flickerEnabled = false; }
    }

    // v7.5.0: luces del atrezo del hotel (lámparas de mesilla, arañas): como las de los apliques, con su parpadeo y bajo el LightCuller
    public void AddHotelLight(Transform parent, Vector3 localPos, Renderer lens, float intensity, float range)
    {
        System.Random lr = new System.Random(UsedSeed * 389 + Mathf.RoundToInt(localPos.x * 31f) * 7 + Mathf.RoundToInt(localPos.z * 17f));
        CreateFixtureLight(parent, localPos, lens, lr, null);
        Transform lt = parent.GetChild(parent.childCount - 1);
        Light l = lt.GetComponent<Light>(); FlickeringLight f = lt.GetComponent<FlickeringLight>();
        if (l != null) { l.intensity = intensity; l.range = range; }
        if (f != null) { f.baseIntensity = intensity; f.flickerEnabled = lr.NextDouble() < 0.35; }
    }

    private void CreateFixtureLight(Transform parent, Vector3 localPos, Renderer lens, System.Random rng, AudioClip buzzClip)
    {
        LevelConfig cfg = config;
        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(parent, false);
        lightGo.transform.localPosition = localPos;

        Light l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = cfg.lightRange;
        l.intensity = cfg.lightIntensity;
        l.color = cfg.lightColor;
        l.shadows = LightShadows.None;

        FlickeringLight f = lightGo.AddComponent<FlickeringLight>();
        f.targetLight = l;
        f.emissiveRenderer = lens;
        f.emissionColor = (lens != null && lens.sharedMaterial != null && lens.sharedMaterial.HasProperty("_EmissionColor")) ? lens.sharedMaterial.GetColor("_EmissionColor") : cfg.emissionColor;
        f.baseIntensity = cfg.lightIntensity;
        f.flickerEnabled = rng.NextDouble() < cfg.flickerChance || nextForceFlicker;
        f.pulse = cfg.pulse;
        if (cfg.pulse) f.pulseSpeed = 1.2f + (float)rng.NextDouble() * 0.8f;

        if (f.flickerEnabled && buzzClip != null && rng.NextDouble() < cfg.buzzChance)
        {
            AudioSource src = lightGo.AddComponent<AudioSource>();
            src.clip = buzzClip;
            src.loop = true;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1f;
            src.maxDistance = 6f;
            src.volume = 0f;   // AudioBuzzLimiter lo enciende solo si es de las luces más cercanas
            src.time = (float)rng.NextDouble(); // desfasa los zumbidos
            f.buzz = src;
        }

        Culler.Register(f);
    }

    // ------------------------------------------------------------------
    // Pilas
    // ------------------------------------------------------------------

    private void PickBatterySpots(System.Random rng, List<Vector2Int> monsterCells)
    {
        BatterySpots = new List<Vector3>();
        List<Vector2Int> picked = new List<Vector2Int>();
        int want = config.batteries;
        int guard = 0;
        while (picked.Count < want && guard++ < 500)
        {
            Vector2Int c = new Vector2Int(rng.Next(w), rng.Next(h));
            if (c == startCell || c == exitCell) continue;
            if (distFromStart[c.x, c.y] < 2) continue;
            if (InPoolBlock(c.x, c.y) || FieldBlocked(c.x, c.y)) continue;
            bool ok = true;
            int minSep = guard < 250 ? 3 : 1;
            for (int i = 0; i < picked.Count; i++)
            {
                if (Mathf.Abs(picked[i].x - c.x) + Mathf.Abs(picked[i].y - c.y) < minSep) ok = false;
            }
            if (!ok) continue;
            picked.Add(c);
            float ox = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.4f;
            float oz = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.4f;
            BatterySpots.Add(levelRoot.TransformPoint(HotelSpot(c.x, c.y, CellCenterLocal(c.x, c.y) + new Vector3(ox, 0.1f, oz))));
        }
    }

    // Sitios para objetos extra (el orden de uso lo decide GameBootstrap): celdas libres, separadas entre sí y de las pilas
    private void PickExtraSpots(System.Random rng, int count)
    {
        ExtraSpots = new List<Vector3>();
        List<Vector2Int> picked = new List<Vector2Int>();
        int guard = 0;
        while (picked.Count < count && guard++ < 1500)
        {
            Vector2Int c = new Vector2Int(rng.Next(w), rng.Next(h));
            if (c == startCell || c == exitCell) continue;
            if (distFromStart[c.x, c.y] < 2) continue;
            if (InPoolBlock(c.x, c.y) || FieldBlocked(c.x, c.y)) continue;
            int minSep = guard < 600 ? 2 : 1;
            bool ok = true;
            for (int i = 0; i < picked.Count; i++)
                if (Mathf.Abs(picked[i].x - c.x) + Mathf.Abs(picked[i].y - c.y) < minSep) { ok = false; break; }
            if (!ok) continue;
            // no encima de una pila
            Vector3 world = levelRoot.TransformPoint(CellCenterLocal(c.x, c.y));
            for (int i = 0; i < BatterySpots.Count && ok; i++)
            {
                Vector3 d = BatterySpots[i] - world; d.y = 0f;
                if (d.magnitude < cellSize * 0.8f) ok = false;
            }
            if (!ok && guard < 1200) continue;
            picked.Add(c);
            float ox = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.5f;
            float oz = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.5f;
            ExtraSpots.Add(levelRoot.TransformPoint(HotelSpot(c.x, c.y, CellCenterLocal(c.x, c.y) + new Vector3(ox, 0.1f, oz))));
        }
    }

    // v7.1.0: dos llaves y dos objetos pasan a las salas del parque de bolas: una llave enterrada entre las bolas de una piscina
    // (asoma un poco), otra en el atrezo (tras el mostrador, bajo la mesa de la fiesta, en la plataforma alta...)
    public readonly List<Vector3> HallPitKeys = new List<Vector3>();
    private void PlaceHallSpots(System.Random rng)
    {
        HallPitKeys.Clear();
        List<Vector3> props = new List<Vector3>(BallpitSetPiece.PropSpots);
        for (int i = props.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); Vector3 t = props[i]; props[i] = props[j]; props[j] = t; }
        int hk = rng.Next(Halls.Count), hp = (hk + 1 + rng.Next(System.Math.Max(1, Halls.Count - 1))) % Halls.Count;
        Vector3 pit = BallpitSetPiece.PitKeySpot(rng);
        Vector3 Hall(int k, Vector3 l) { return levelRoot.TransformPoint(HallCenterLocal(k) + l + new Vector3(0f, 0.1f, 0f)); }
        if (KeySpots.Count >= 4)
        {
            KeySpots[1] = Hall(hk, pit); HallPitKeys.Add(KeySpots[1]);
            KeySpots[3] = Hall(hp, props[0]);
        }
        for (int i = 0; i < 2 && i < ExtraSpots.Count && 1 + i < props.Count; i++)
            ExtraSpots[ExtraSpots.Count - 1 - i] = Hall((hp + 1 + i) % Halls.Count, props[1 + i]);
    }

    // v7.0.4: sitios para las llaves: repartidas, lejos del inicio y no pegadas a la salida ni a otros objetos
    private void PickKeySpots(System.Random rng, int count)
    {
        KeySpots = new List<Vector3>();
        List<Vector2Int> picked = new List<Vector2Int>();
        int maxD = 1; for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) if (distFromStart[x, z] >= 0) maxD = Mathf.Max(maxD, distFromStart[x, z]);
        int minStart = Mathf.Clamp(maxD / 5, 2, 8);
        int guard = 0;
        while (picked.Count < count && guard++ < 3000)
        {
            Vector2Int c = new Vector2Int(rng.Next(w), rng.Next(h));
            if (c == startCell || c == exitCell) continue;
            if (InPoolBlock(c.x, c.y) || FieldBlocked(c.x, c.y)) continue;
            if (distFromStart[c.x, c.y] < 0) continue;   // inalcanzable
            bool strict = guard < 1500;
            if (distFromStart[c.x, c.y] < (strict ? minStart : 2)) continue;
            if (Mathf.Abs(c.x - exitCell.x) + Mathf.Abs(c.y - exitCell.y) < (strict ? 3 : 2)) continue;
            int minSep = guard < 800 ? Mathf.Max(3, Mathf.Min(w, h) / 4) : (guard < 2000 ? 2 : 1);
            bool ok = true;
            for (int i = 0; i < picked.Count; i++)
                if (Mathf.Abs(picked[i].x - c.x) + Mathf.Abs(picked[i].y - c.y) < minSep) { ok = false; break; }
            if (!ok) continue;
            Vector3 world = levelRoot.TransformPoint(CellCenterLocal(c.x, c.y));
            if (guard < 2500)
            {
                foreach (Vector3 b in BatterySpots) { Vector3 d = b - world; d.y = 0f; if (d.magnitude < cellSize * 0.7f) ok = false; }
                foreach (Vector3 b in ExtraSpots) { Vector3 d = b - world; d.y = 0f; if (d.magnitude < cellSize * 0.7f) ok = false; }
                if (!ok) continue;
            }
            picked.Add(c);
            float ox = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.45f;
            float oz = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.45f;
            KeySpots.Add(levelRoot.TransformPoint(HotelSpot(c.x, c.y, CellCenterLocal(c.x, c.y) + new Vector3(ox, 0.1f, oz))));
        }
    }

    // ------------------------------------------------------------------
    // Puerta de salida: discreta (marco distinto, LED verde tenue visible solo de cerca)
    // ------------------------------------------------------------------

    // v7.3.0 Talasofobia: todo el nivel lo construye OceanWorld (casa inundada, mar, casas sobre pilotes, faro, jaula y gruta)
    public bool IsOcean { get { return config != null && config.layout == LayoutKind.Ocean; } }
    private void BuildOceanGeometry(System.Random rng)
    {
        LevelConfig cfg = config;
        TextureLibrary.ReleaseAllExcept(new HashSet<string> { TextureLibrary.Key(SurfaceStyle.WaterNormal, wallHeight) });
        WallSegmentCount = 0; PillarCount = 0;
        SurfaceSet ripple = TextureLibrary.Get(SurfaceStyle.WaterNormal, wallHeight);
        OceanWorld ow = OceanWorld.Build(this, cfg, levelRoot, UsedSeed, o => Track(o));
        Water = WaterVolume.CreateOcean(this, cfg, decorRoot, ripple, 520f);
        ow.BindWater(Water);
        PlayerStart = ow.PlayerStart; PlayerStartYaw = 0f;
        MonsterSpawns = new List<Vector3>(ow.MonsterSpawns);
        MonsterSpawn = MonsterSpawns.Count > 0 ? MonsterSpawns[0] : PlayerStart;
        BatterySpots = new List<Vector3>(ow.BatterySpots);
        ExtraSpots = new List<Vector3>(ow.ExtraSpots);
        KeySpots = new List<Vector3>();
        ExitTransform = ow.ExitTransform;
        ExitPosition = ow.ExitTransform.position;
    }

    // v7.0 Campo infinito: terreno, casas y accesorios (FieldWorld); sin muros, techo ni luces de techo
    private void BuildFieldGeometry(System.Random rng)
    {
        LevelConfig cfg = config;
        TextureLibrary.ReleaseAllExcept(new HashSet<string>());
        WallSegmentCount = 0; PillarCount = 0;
        FieldWorld.Build(this, cfg, levelRoot, FieldPlan, o => Track(o));

        Vector3 startLocal = CellCenterLocal(startCell.x, startCell.y);
        PlayerStart = levelRoot.TransformPoint(startLocal + new Vector3(0f, 0.1f, 0f));
        PlayerStartYaw = 0f;
        MonsterSpawns = new List<Vector3>();
        List<Vector2Int> mc = PickMonsterCells(rng, Mathf.Max(1, cfg.monsterCount) + Mathf.Max(0, extraMonsters));
        for (int i = 0; i < mc.Count; i++) MonsterSpawns.Add(levelRoot.TransformPoint(CellCenterLocal(mc[i].x, mc[i].y)));
        MonsterSpawn = MonsterSpawns[0];
        PickBatterySpots(rng, mc);
        PickExtraSpots(new System.Random(UsedSeed * 31 + 7), Mathf.Max(1, cfg.extraSpots));
        KeySpots = new List<Vector3>();
        PickFieldHouseSpots();   // v7.0.2: los objetos, solo dentro de las casas abiertas
        BuildFieldExit(rng);
    }

    // Salida del campo: una puerta suelta idéntica a los señuelos, lejos y en una celda al azar (según la semilla).
    // Pistas sutiles que los señuelos no tienen: un farol que parpadea débilmente y un zumbido de radio lejano (FieldExitBeacon).
    private void BuildFieldExit(System.Random rng)
    {
        LevelConfig cfg = config;
        Vector3 c = CellCenterLocal(exitCell.x, exitCell.y);
        float ox = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.4f, oz = ((float)rng.NextDouble() - 0.5f) * cellSize * 0.4f;
        c.x += ox; c.z += oz; c.y = FieldHeight.H(c.x, c.z) - 0.03f;
        GameObject doorRoot = new GameObject("ExitDoor");
        doorRoot.transform.SetParent(levelRoot, false);
        doorRoot.transform.localPosition = c;
        doorRoot.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
        FieldWorld.BuildLoneDoor(doorRoot.transform, cfg, o => Track(o));
        if (FieldWorld.Active != null) FieldWorld.Active.GhostTree(doorRoot.transform);
        Transform slab = doorRoot.transform.Find("DoorSlab");

        Material ledMat = Track(ProceduralAssets.CreateMaterial(new Color(0.05f, 0.15f, 0.06f), null, Vector2.one, 0.3f));
        ProceduralAssets.SetEmission(ledMat, new Color(0.15f, 1.3f, 0.3f));
        GameObject led = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ExitLed", doorRoot.transform,
            new Vector3(0f, 2.15f + 0.19f, 0.065f), new Vector3(0.09f, 0.035f, 0.03f), ledMat, false);
        GameObject glow = new GameObject("ExitGlow");
        glow.transform.SetParent(doorRoot.transform, false);
        glow.transform.localPosition = new Vector3(0f, 2.3f, 0.5f);
        Light gl = glow.AddComponent<Light>();
        gl.type = LightType.Point; gl.color = new Color(0.25f, 1f, 0.4f); gl.range = 8f; gl.intensity = 0.45f; gl.shadows = LightShadows.None;
        ExitDoorCue cue = doorRoot.AddComponent<ExitDoorCue>();
        cue.ledRenderer = led.GetComponent<Renderer>(); cue.glow = gl; cue.visibleDistance = 8f; cue.glowIntensity = 0.45f;
        led.GetComponent<Renderer>().enabled = false; gl.enabled = false;

        FieldExitBeacon.Build(this, doorRoot.transform, cfg, UsedSeed, o => Track(o));

        ExitTransform = slab;
        ExitPosition = slab.position;
    }

    // v7.0.2: reparte pilas y objetos extra entre los huecos de los muebles de las casas abiertas (determinista con la semilla:
    // idéntico en multijugador). Primero un objeto por casa (repartidos), luego el resto; mesas y estantes antes que el suelo.
    public int FieldItemHouses { get; private set; }
    private void PickFieldHouseSpots()
    {
        FieldWorld fw = FieldWorld.Active;
        if (fw == null || fw.ItemSlots.Count == 0) return;
        System.Random r = new System.Random(UsedSeed * 53 + 11);
        List<FieldWorld.ItemSlot> pool = new List<FieldWorld.ItemSlot>(fw.ItemSlots);
        for (int i = pool.Count - 1; i > 0; i--) { int j = r.Next(i + 1); FieldWorld.ItemSlot tmp = pool[i]; pool[i] = pool[j]; pool[j] = tmp; }
        // orden: rondas por casa (una por casa en cada ronda), dentro de la ronda muebles antes que suelo
        pool.Sort((a, b) => (a.kind == FieldWorld.SlotFloor ? 1 : 0).CompareTo(b.kind == FieldWorld.SlotFloor ? 1 : 0));
        List<FieldWorld.ItemSlot> order = new List<FieldWorld.ItemSlot>();
        Dictionary<int, int> taken = new Dictionary<int, int>();
        HashSet<int> houses = new HashSet<int>();
        while (pool.Count > 0)
        {
            int minTaken = int.MaxValue;
            foreach (FieldWorld.ItemSlot s in pool) { taken.TryGetValue(s.house, out int tk); if (tk < minTaken) minTaken = tk; }
            for (int i = 0; i < pool.Count; i++)
            {
                taken.TryGetValue(pool[i].house, out int tk);
                if (tk != minTaken) continue;
                order.Add(pool[i]); taken[pool[i].house] = tk + 1; houses.Add(pool[i].house); pool.RemoveAt(i); break;
            }
        }
        int nb = BatterySpots.Count, ne = ExtraSpots.Count, nk = ExitLock.KeyCount;
        if (order.Count < nb + ne + nk) Debug.LogWarning("[Campo] pocos huecos en casas: " + order.Count + " < " + (nb + ne + nk));
        int k = 0;
        List<Vector3> bs = new List<Vector3>(), es = new List<Vector3>();
        // v7.0.4: las llaves primero (la primera ronda: cada una en una casa distinta)
        KeySpots = new List<Vector3>();
        for (int i = 0; i < nk && k < order.Count; i++) KeySpots.Add(order[k++].pos);
        // los extra (notas, agua, cinta…) primero para que siempre quepan; luego las pilas
        for (int i = 0; i < ne && k < order.Count; i++) es.Add(order[k++].pos);
        for (int i = 0; i < nb && k < order.Count; i++) bs.Add(order[k++].pos);
        ExtraSpots = es; BatterySpots = bs;
        FieldItemHouses = houses.Count;
    }

    private bool FieldBlocked(int x, int z) { return FieldPlan != null && FieldPlan.blocked.Contains(new Vector2Int(x, z)); }

    private void BuildExitDoor(System.Random rng)
    {
        LevelConfig cfg = config;
        Vector3 c = CellCenterLocal(exitCell.x, exitCell.y);

        // Lados de la celda que tienen muro (ahí se apoya la puerta)
        List<int> wallSides = new List<int>(4);
        for (int d = 0; d < 4; d++)
        {
            if (!IsOpen(exitCell, d)) wallSides.Add(d);
        }

        Vector3 dir = Vector3.zero; // dirección hacia el muro (en local)
        if (wallSides.Count > 0)
        {
            int side = wallSides[rng.Next(wallSides.Count)];
            dir = new Vector3(Dirs[side].x, 0f, Dirs[side].y);
        }

        float doorThickness = 0.07f;
        float offset = cellSize * 0.5f - wallThickness * 0.5f - doorThickness * 0.5f - 0.012f;
        Vector3 basePos = c + dir * offset;
        float doorH = Mathf.Min(2.15f, wallHeight - 0.5f);

        Quaternion rot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-dir) : Quaternion.identity;

        Material doorMat = Track(ProceduralAssets.CreateMaterial(cfg.doorColor, null, Vector2.one, 0.25f));
        Material panelMat = Track(ProceduralAssets.CreateMaterial(cfg.doorColor * 0.82f, null, Vector2.one, 0.2f));
        Material frameMat = Track(ProceduralAssets.CreateMaterial(cfg.doorFrameColor, null, Vector2.one, 0.3f));
        Material handleMat = Track(ProceduralAssets.CreateMaterial(new Color(0.6f, 0.55f, 0.4f), null, Vector2.one, 0.7f));
        handleMat.SetFloat("_Metallic", 0.8f);
        Material ledMat = Track(ProceduralAssets.CreateMaterial(new Color(0.05f, 0.15f, 0.06f), null, Vector2.one, 0.3f));
        ProceduralAssets.SetEmission(ledMat, new Color(0.15f, 1.3f, 0.3f));

        GameObject doorRoot = new GameObject("ExitDoor");
        doorRoot.transform.SetParent(levelRoot, false);
        doorRoot.transform.localPosition = basePos;
        doorRoot.transform.localRotation = rot;

        // Hoja y paneles (el eje local Z apunta hacia el interior de la habitación)
        GameObject slab = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "DoorSlab", doorRoot.transform,
            new Vector3(0f, doorH * 0.5f, 0f), new Vector3(1.0f, doorH, doorThickness), doorMat, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "PanelUpper", doorRoot.transform,
            new Vector3(0f, doorH * 0.72f, 0.003f + doorThickness * 0.5f), new Vector3(0.7f, doorH * 0.28f, 0.006f), panelMat, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "PanelLower", doorRoot.transform,
            new Vector3(0f, doorH * 0.30f, 0.003f + doorThickness * 0.5f), new Vector3(0.7f, doorH * 0.36f, 0.006f), panelMat, false);
        // Marco algo distinto del muro
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "FrameL", doorRoot.transform,
            new Vector3(-0.56f, doorH * 0.5f + 0.05f, 0.01f), new Vector3(0.12f, doorH + 0.1f, 0.1f), frameMat, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "FrameR", doorRoot.transform,
            new Vector3(0.56f, doorH * 0.5f + 0.05f, 0.01f), new Vector3(0.12f, doorH + 0.1f, 0.1f), frameMat, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "FrameTop", doorRoot.transform,
            new Vector3(0f, doorH + 0.05f, 0.01f), new Vector3(1.24f, 0.12f, 0.1f), frameMat, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Handle", doorRoot.transform,
            new Vector3(0.36f, 1.0f, 0.06f), new Vector3(0.14f, 0.04f, 0.08f), handleMat, false);

        // LED verde diminuto sobre el marco + luz verde tenue: solo existen a menos de ~8 m (ExitDoorCue)
        GameObject led = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ExitLed", doorRoot.transform,
            new Vector3(0f, doorH + 0.19f, 0.03f), new Vector3(0.09f, 0.035f, 0.03f), ledMat, false);
        GameObject glow = new GameObject("ExitGlow");
        glow.transform.SetParent(doorRoot.transform, false);
        glow.transform.localPosition = new Vector3(0f, doorH + 0.15f, 0.5f);
        Light gl = glow.AddComponent<Light>();
        gl.type = LightType.Point;
        gl.color = new Color(0.25f, 1f, 0.4f);
        gl.range = 8f;
        gl.intensity = 0.45f;
        gl.shadows = LightShadows.None;

        ExitDoorCue cue = doorRoot.AddComponent<ExitDoorCue>();
        cue.ledRenderer = led.GetComponent<Renderer>();
        cue.glow = gl;
        cue.visibleDistance = 8f;
        cue.glowIntensity = 0.45f;
        led.GetComponent<Renderer>().enabled = false;
        gl.enabled = false;

        ExitTransform = slab.transform;
        ExitPosition = slab.transform.position;
    }

    // Orienta al jugador hacia una dirección abierta
    // v7.1.0: la cabina del ascensor ocupa la celda de inicio y solo se sale por su puerta (la primera salida abierta,
    // como en ComputeStartYaw). Si la celda tenía más salidas, lo que quedaba tras ellas solo se alcanzaba por detrás de
    // la cabina: en el aparcamiento podía quedar el jugador encerrado en un callejón. Se abren muros normales hasta que
    // todo el nivel se alcanza desde la puerta sin cruzar la celda de inicio.
    public int StartDoorRepairs { get; private set; }
    private int EnsureReachFromStartDoor()
    {
        int dir = IsOpen(startCell, 0) ? 0 : IsOpen(startCell, 1) ? 1 : IsOpen(startCell, 2) ? 2 : 3;
        Vector2Int n0 = startCell + Dirs[dir];
        if (!InBounds(n0)) return 0;
        int opened = 0;
        for (int guard = 0; guard < w * h; guard++)
        {
            bool[,] seen = new bool[w, h];
            Queue<Vector2Int> q = new Queue<Vector2Int>(); q.Enqueue(n0); seen[n0.x, n0.y] = true; seen[startCell.x, startCell.y] = true; int cnt = 2;
            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!IsOpen(c, d)) continue;
                    Vector2Int n = c + Dirs[d];
                    if (seen[n.x, n.y]) continue;
                    seen[n.x, n.y] = true; cnt++; q.Enqueue(n);
                }
            }
            if (cnt >= w * h) return opened;
            bool did = false;
            for (int i = 0; i < w && !did; i++)
                for (int j = 0; j < h && !did; j++)
                {
                    if (seen[i, j]) continue;
                    Vector2Int c = new Vector2Int(i, j);
                    for (int d = 0; d < 4; d++)
                    {
                        Vector2Int n = c + Dirs[d];
                        if (!InBounds(n) || !seen[n.x, n.y] || n == startCell) continue;
                        if (Garage != null && !Garage.CanOpen(c, n)) continue;
                        if (Dirs[d].y == 1) wallsH[c.x, c.y + 1] = false; else if (Dirs[d].x == 1) wallsV[c.x + 1, c.y] = false; else if (Dirs[d].y == -1) wallsH[c.x, c.y] = false; else wallsV[c.x, c.y] = false;
                        did = true; opened++; break;
                    }
                }
            if (!did) return opened;
        }
        return opened;
    }

    // v7.5.0: en el hotel, los objetos y monstruos no aparecen dentro de los muebles (punto libre de la celda con margen para el NavMesh)
    private Vector3 HotelSpot(int x, int z, Vector3 local) { return Hotel != null ? Hotel.FreePoint(x, z, local) : local; }

    private float ComputeStartYaw()
    {
        if (IsOpen(startCell, 0)) return 0f;     // Norte
        if (IsOpen(startCell, 1)) return 90f;    // Este
        if (IsOpen(startCell, 2)) return 180f;   // Sur
        return 270f;                             // Oeste
    }

    // ------------------------------------------------------------------
    // Utilidades
    // ------------------------------------------------------------------

    private Vector3 CellCenterLocal(int x, int z)
    {
        float px = (x + 0.5f) * cellSize, pz = (z + 0.5f) * cellSize;
        return new Vector3(px, FieldHeight.Active ? FieldHeight.H(px, pz) : (Garage != null ? Garage.Y(px, pz) : 0f), pz);
    }

    private Vector2Int WorldToCell(Vector3 world)
    {
        Vector3 local = levelRoot.InverseTransformPoint(world);
        int x = Mathf.Clamp(Mathf.FloorToInt(local.x / cellSize), 0, w - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(local.z / cellSize), 0, h - 1);
        return new Vector2Int(x, z);
    }
}
