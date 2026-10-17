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
    public FieldPlan FieldPlan { get; private set; }   // v7.0: casas/postes/puertas del Campo infinito (null en otros niveles)
    public bool IsField { get { return config != null && config.layout == LayoutKind.Field; } }
    public Vector3 PlayerStart { get; private set; }
    public float PlayerStartYaw { get; private set; }
    public Vector3 MonsterSpawn { get; private set; }
    public List<Vector3> MonsterSpawns { get; private set; }
    public List<Vector3> BatterySpots { get; private set; }
    public List<Vector3> ExtraSpots { get; private set; }     // sitios para notas, agua de almendras, barras luminosas y cintas
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
    public const float PoolDoorW = 2f, PoolDoorTop = 2.5f;
    public bool PoolFixed { get; private set; }
    public bool IsPoolDoor(int i, int j, bool vertical)
    {
        if (!PoolFixed) return false;
        RectInt b = PoolBlock;
        return vertical ? (j == b.y + 1 && (i == b.x || i == b.xMax)) : ((i == b.x + 1 && j == b.y) || (i == b.x + 2 && j == b.yMax));
    }
    public bool InPoolBlock(int x, int z) { return HasPoolRoom && PoolBlock.Contains(new Vector2Int(x, z)); }
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

        FieldHeight.Reset(); FieldPlan = null;
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

        // v7.0 Campo infinito: sin muros (ni siquiera el borde: el campo se repite), salida desde el centro
        if (config.layout == LayoutKind.Field)
        {
            for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) wallsV[i, j] = false;
            for (int i = 0; i < w; i++) for (int j = 0; j <= h; j++) wallsH[i, j] = false;
            startCell = new Vector2Int(w / 2, h / 2);
            FieldHeight.Init(UsedSeed, w * cellSize, new Vector2((startCell.x + 0.5f) * cellSize, (startCell.y + 0.5f) * cellSize));
        }

        ChoosePoolBlock();
    }

    private void ChoosePoolBlock()
    {
        HasPoolRoom = false; PoolFixed = false;
        Shader.SetGlobalVector("_PoolRoomRect", new Vector4(1e6f, 1e6f, -1e6f, -1e6f));
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
            wallsH[bx + 1, bz] = false; wallsH[bx + 2, bz + PoolBlockH] = false;
            wallsV[bx, bz + 1] = false; wallsV[bx + PoolBlockW, bz + 1] = false;
            EnsureConnected();
        }
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
                        if (!InBounds(n) || !seen[n.x, n.y] || InPoolBlock(c.x, c.y) != InPoolBlock(n.x, n.y)) continue;
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
                Vector2Int c = new Vector2Int(i, j);
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
        Material floorMat = Track(ProceduralAssets.CreateSurfaceMaterial(floorSet, cfg.floorTint, 1f, 1f));
        Material ceilMat = Track(ProceduralAssets.CreateSurfaceMaterial(ceilSet, cfg.ceilTint, 1f, 1f));
        Material pipeMat = cfg.pipes ? wallMat : null;

        // --- Chunks (cada uno es un renderer, para que las luces por píxel se repartan bien) ---
        int chunkCells = Mathf.Max(1, Mathf.RoundToInt(8f / cs));
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
        for (int cx = 0; cx < cxN; cx++)
        {
            for (int cz = 0; cz < czN; cz++)
            {
                float x0 = cx * chunkCells * cs, x1 = Mathf.Min(w, (cx + 1) * chunkCells) * cs;
                float z0 = cz * chunkCells * cs, z1 = Mathf.Min(h, (cz + 1) * chunkCells) * cs;
                foreach (Rect fr in FloorRectsMinusPool(x0, z0, x1, z1))
                    flatB[cx, cz].AddQuad(SlotFloor, new Vector3(fr.xMin, 0f, fr.yMin), new Vector3(fr.xMax, 0f, fr.yMin), new Vector3(fr.xMax, 0f, fr.yMax), new Vector3(fr.xMin, 0f, fr.yMax),
                        Vector3.up, Vector3.right, Vector3.forward, cfg.floorTileU, cfg.floorTileV, 0f, 0f);
                flatB[cx, cz].AddQuad(SlotCeiling, new Vector3(x0, wh, z0), new Vector3(x1, wh, z0), new Vector3(x1, wh, z1), new Vector3(x0, wh, z1),
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

        // v6.8: jambas y dintel de las 4 puertas fijas de la sala de piscina (cara exterior = muro del nivel)
        if (PoolFixed)
        {
            RectInt pb = PoolBlock;
            float side = (cs - PoolDoorW) * 0.5f + t * 0.5f - 0.002f, lh = wh - PoolDoorTop;
            Vector2Int[] hs = { new Vector2Int(pb.x + 1, pb.y), new Vector2Int(pb.x + 2, pb.yMax) };
            foreach (Vector2Int d in hs)
            {
                MeshBuilder mb = wallsB[Mathf.Min(d.x, w - 1) / chunkCells, Mathf.Min(d.y, h - 1) / chunkCells];
                float c0 = (d.x + 0.5f) * cs, z = d.y * cs;
                mb.AddWallBox(SlotWall, new Vector3(c0 - PoolDoorW * 0.5f - side * 0.5f, wh * 0.5f, z), new Vector3(side, wh, t), cfg.wallTileU, cfg.wallTileV, 0.37f);
                mb.AddWallBox(SlotWall, new Vector3(c0 + PoolDoorW * 0.5f + side * 0.5f, wh * 0.5f, z), new Vector3(side, wh, t), cfg.wallTileU, cfg.wallTileV, 0.61f);
                mb.AddWallBox(SlotWall, new Vector3(c0, PoolDoorTop + lh * 0.5f, z), new Vector3(PoolDoorW + 0.01f, lh, t), cfg.wallTileU, cfg.wallTileV, 0.13f);
            }
            Vector2Int[] vs = { new Vector2Int(pb.x, pb.y + 1), new Vector2Int(pb.xMax, pb.y + 1) };
            foreach (Vector2Int d in vs)
            {
                MeshBuilder mb = wallsB[Mathf.Min(d.x, w - 1) / chunkCells, Mathf.Min(d.y, h - 1) / chunkCells];
                float c0 = (d.y + 0.5f) * cs, x = d.x * cs;
                mb.AddWallBox(SlotWall, new Vector3(x, wh * 0.5f, c0 - PoolDoorW * 0.5f - side * 0.5f), new Vector3(t, wh, side), cfg.wallTileU, cfg.wallTileV, 0.29f);
                mb.AddWallBox(SlotWall, new Vector3(x, wh * 0.5f, c0 + PoolDoorW * 0.5f + side * 0.5f), new Vector3(t, wh, side), cfg.wallTileU, cfg.wallTileV, 0.83f);
                mb.AddWallBox(SlotWall, new Vector3(x, PoolDoorTop + lh * 0.5f, c0), new Vector3(t, lh, PoolDoorW + 0.01f), cfg.wallTileU, cfg.wallTileV, 0.47f);
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
                    wallsB[cx, cz].AddWallBox(SlotWall, new Vector3(i * cs, wh * 0.5f, j * cs),
                        new Vector3(cfg.pillarSize, wh, cfg.pillarSize), cfg.wallTileU, cfg.wallTileV, (float)rng.NextDouble());
                    pillars++;
                }
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
        if (!HasPoolRoom) AddBox("FloorCollider", new Vector3(totalW * 0.5f, -0.1f, totalH * 0.5f), new Vector3(totalW + 2f * t, 0.2f, totalH + 2f * t));
        else
        {
            int k = 0;
            foreach (Rect fr in FloorRectsMinusPool(-t, -t, totalW + t, totalH + t))
                AddBox("FloorCollider" + (k++ == 0 ? "" : k.ToString()), new Vector3(fr.center.x, -0.1f, fr.center.y), new Vector3(fr.width, 0.2f, fr.height));
            PoolRoomSetPiece.Build(this, cfg, levelRoot, poolWallB.IsEmpty ? null : Track(poolWallB.ToMesh("PoolRoomWalls")));
        }
        AddBox("CeilingCollider", new Vector3(totalW * 0.5f, wh + 0.1f, totalH * 0.5f), new Vector3(totalW + 2f * t, 0.2f, totalH + 2f * t));

        // Luces
        BuildFixtures(rng);

        // Posiciones clave
        Vector3 startLocal = CellCenterLocal(startCell.x, startCell.y);
        PlayerStart = levelRoot.TransformPoint(startLocal + new Vector3(0f, 0.1f, 0f));
        PlayerStartYaw = ComputeStartYaw();

        MonsterSpawns = new List<Vector3>();
        List<Vector2Int> mc = PickMonsterCells(rng, Mathf.Max(1, cfg.monsterCount) + Mathf.Max(0, extraMonsters));
        for (int i = 0; i < mc.Count; i++) MonsterSpawns.Add(levelRoot.TransformPoint(CellCenterLocal(mc[i].x, mc[i].y)));
        MonsterSpawn = MonsterSpawns[0];

        PickBatterySpots(rng, mc);
        PickExtraSpots(new System.Random(UsedSeed * 31 + 7), 9);
        BuildExitDoor(rng);

        // Agua realista (shader propio + volumen con interacción)
        if (cfg.water)
        {
            Water = WaterVolume.Create(this, cfg, decorRoot, rippleSet);
        }
    }

    // Rectángulo (x0,z0)-(x1,z1) menos el bloque de la piscina (hasta 4 trozos)
    private List<Rect> FloorRectsMinusPool(float x0, float z0, float x1, float z1)
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

        AudioClip buzzClip = null;
        if (cfg.buzzChance > 0f)
        {
            buzzClip = Track(ProceduralAudio.CreateBuzzClip());
        }

        int fixtures = 0, live = 0;
        int spacing = Mathf.Max(1, cfg.lightSpacing);
        float wh = wallHeight;

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++)
            {
                Vector3 c = CellCenterLocal(x, z);

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
                    CreateSconce(lightsRoot, wallPos, dir, isDead, emissive, dead, housing, rng, buzzClip);
                    fixtures++;
                    if (!isDead) live++;
                    continue;
                }

                if (x % spacing != 0 || z % spacing != 0) continue;
                if (InPoolBlock(x, z)) continue;   // la sala de piscina tiene su propia luz (tragaluces/ventanas)
                bool deadFix = rng.NextDouble() < cfg.deadChance;
                CreateCeilingFixture(lightsRoot, c, deadFix, emissive, dead, housing, rng, buzzClip);
                fixtures++;
                if (!deadFix) live++;
            }
        }
        FixtureCount = fixtures;
        LiveFixtureCount = live;
    }

    private void CreateCeilingFixture(Transform parent, Vector3 c, bool isDead, Material emissive, Material dead,
        Material housing, System.Random rng, AudioClip buzzClip)
    {
        LevelConfig cfg = config;
        float wh = wallHeight;
        GameObject fx = new GameObject("Fixture");
        fx.transform.SetParent(parent, false);
        fx.transform.localPosition = new Vector3(c.x, 0f, c.z);

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

        // Placa trasera + pantalla emisiva
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Plate", fx.transform, new Vector3(0f, 0f, 0.015f), new Vector3(0.16f, 0.32f, 0.03f), housing, false);
        Renderer lens = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Shade", fx.transform, new Vector3(0f, 0.04f, 0.1f),
            new Vector3(0.11f, 0.2f, 0.11f), isDead ? dead : emissive, false).GetComponent<Renderer>();
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Arm", fx.transform, new Vector3(0f, -0.1f, 0.06f), new Vector3(0.03f, 0.03f, 0.1f), housing, false);

        if (isDead) return;
        CreateFixtureLight(fx.transform, new Vector3(0f, 0.05f, 0.45f), lens, rng, buzzClip);
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
        f.emissionColor = cfg.emissionColor;
        f.baseIntensity = cfg.lightIntensity;
        f.flickerEnabled = rng.NextDouble() < cfg.flickerChance;
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
            BatterySpots.Add(levelRoot.TransformPoint(CellCenterLocal(c.x, c.y) + new Vector3(ox, 0.1f, oz)));
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
            ExtraSpots.Add(levelRoot.TransformPoint(CellCenterLocal(c.x, c.y) + new Vector3(ox, 0.1f, oz)));
        }
    }

    // ------------------------------------------------------------------
    // Puerta de salida: discreta (marco distinto, LED verde tenue visible solo de cerca)
    // ------------------------------------------------------------------

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
        PickExtraSpots(new System.Random(UsedSeed * 31 + 7), 9);
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
        int nb = BatterySpots.Count, ne = ExtraSpots.Count;
        if (order.Count < nb + ne) Debug.LogWarning("[Campo] pocos huecos en casas: " + order.Count + " < " + (nb + ne));
        int k = 0;
        List<Vector3> bs = new List<Vector3>(), es = new List<Vector3>();
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
        return new Vector3(px, FieldHeight.Active ? FieldHeight.H(px, pz) : 0f, pz);
    }

    private Vector2Int WorldToCell(Vector3 world)
    {
        Vector3 local = levelRoot.InverseTransformPoint(world);
        int x = Mathf.Clamp(Mathf.FloorToInt(local.x / cellSize), 0, w - 1);
        int z = Mathf.Clamp(Mathf.FloorToInt(local.z / cellSize), 0, h - 1);
        return new Vector2Int(x, z);
    }
}
