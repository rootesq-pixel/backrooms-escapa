using System.Collections.Generic;
using UnityEngine;

// Nivel 7460 (pasillo de puertas): plano lógico. v7.10.2 (t500u): un laberinto de pasillos. Hay varios pasillos largos
// paralelos (Mains, a lo largo de z, separados Spacing celdas = 28,8 m) y, detrás de cada puerta que se abre, un pasillo
// transversal largo (10 módulos, 24 m) que llega a la puerta de enfrente del pasillo vecino: cruces y bucles. Casi todas las
// puertas siguen cerradas (sólidas), alguna tapiada, y una sola, la salida (con la llave buena), en el sitio más lejano del
// ascensor según el camino más corto. Todo sale de la semilla: igual en todos los equipos (cambia la huella → MP-14).
// Rejilla lógica (celdas de 2,4 m): el pasillo i ocupa la columna Col + i·Spacing; cada transversal ocupa su fila entre dos.
public class CorridorPlan
{
    public enum Door { Locked, Room, Bricked, Exit, Link }
    public const int Left = 0, Right = 1;
    public const int Spacing = 12, CrossModules = 10, MinRow = 2;
    public const int RoomCount = 0;   // v7.10.2: ya no hay habitaciones: cada puerta que se abre da a un pasillo largo

    public class Run
    {
        public int id; public bool cross; public int main = -1;   // pasillos largos: índice 0..Mains-1
        public int a = -1, b = -1, row = -1;                       // transversales: pasillos que une y fila
        public int rows; public Door[,] doors;                     // [lado, módulo]
        public bool deadEnd;                                       // transversal sin salida: al fondo (pasillo b) un muro con una puerta cerrada
    }
    public readonly List<Run> runs = new List<Run>();
    public int Mains;
    public int rows;                           // largo de los pasillos largos (módulos)
    public Door[,] doors;                      // pasillo 0 (el del ascensor): runs[0].doors
    public int exitRun, exitRow, exitSide;
    public int Col;                            // columna lógica del pasillo 0
    public int PathCells { get; private set; } // camino más corto ascensor → salida (celdas de 2,4 m)
    public int Attempts { get; private set; }
    public int[,] Dist;
    public bool[,] WV, WH;                     // muros de la rejilla lógica (los del plano elegido)
    // ¿se pasa de la celda a a la vecina b? (respeta los fondos de los transversales sin salida)
    public bool Step(Vector2Int a, Vector2Int b)
    {
        int W = WV.GetLength(0) - 1, H = rows;
        if (b.x < 0 || b.y < 0 || b.x >= W || b.y >= H) return false;
        if (b.x == a.x + 1) return !WV[b.x, a.y]; if (b.x == a.x - 1) return !WV[a.x, a.y];
        if (b.y == a.y + 1) return !WH[a.x, b.y]; if (b.y == a.y - 1) return !WH[a.x, a.y];
        return false;
    }                        // distancia (celdas) desde el ascensor, -1 = macizo
    public readonly List<Vector2Int> rooms = new List<Vector2Int>();   // (vacío; compatibilidad)
    public readonly List<Vector2Int> bricked = new List<Vector2Int>();
    public int ColOf(int main) { return Col + main * Spacing; }
    public Vector2Int ExitCell { get { return new Vector2Int(ColOf(runs[exitRun].main), exitRow); } }
    public Run ExitRunDef { get { return runs[exitRun]; } }
    public static int GridWidthFor(int mains) { return 5 + (mains - 1) * Spacing; }
    public const int OldAvgPathCells = 93;     // v7.10.1: salida al 45–88 % de un pasillo de 140 módulos

    public static CorridorPlan Make(int seed, int w, int h, bool[,] wallsV, bool[,] wallsH)
    {
        CorridorPlan best = null;
        int target = 3 * OldAvgPathCells;   // al menos 3 veces más camino que antes
        for (int at = 0; at < 24; at++)
        {
            CorridorPlan p = Try(seed, at, w, h);
            if (best == null || p.PathCells > best.PathCells) best = p;
            if (p.PathCells >= target) break;
        }
        best.ApplyWalls(wallsV, wallsH, w, h);
        return best;
    }

    static CorridorPlan Try(int seed, int attempt, int w, int h)
    {
        CorridorPlan p = new CorridorPlan();
        System.Random r = new System.Random(seed * 7460 + 31 + attempt * 104729);
        p.Attempts = attempt;
        p.Col = 2; p.rows = h;
        p.Mains = Mathf.Max(1, (w - 5) / Spacing + 1);
        for (int i = 0; i < p.Mains; i++) p.runs.Add(new Run { id = i, main = i, rows = h, doors = new Door[2, h] });
        p.doors = p.runs[0].doors;
        // transversales: en cada hueco entre pasillos, 5–7 filas en una banda estrecha (alternando norte y sur, en zigzag) y una
        // más en la misma banda (bucle); a veces se repite la fila del hueco anterior: un cruce de cuatro pasillos
        List<int>[] gap = new List<int>[Mathf.Max(0, p.Mains - 1)], dead = new List<int>[gap0(p.Mains)];
        for (int g = 0; g < gap.Length; g++)
        {
            gap[g] = new List<int>();
            bool north = g % 2 == 0;
            int lo = north ? Mathf.RoundToInt(h * 0.74f) : 5, hi = north ? h - 5 : Mathf.RoundToInt(h * 0.26f);   // bandas estrechas: hay que recorrer casi todo cada pasillo
            int want = 5 + r.Next(3);
            for (int t = 0; t < 200 && gap[g].Count < want; t++) TryAdd(gap[g], lo + r.Next(Mathf.Max(1, hi - lo)));
            for (int t = 0; t < 50; t++) if (TryAdd(gap[g], lo + r.Next(Mathf.Max(1, hi - lo)))) break;   // bucle (en la misma banda)
            dead[g] = new List<int>();
            if (g > 0 && r.NextDouble() < 0.8)
            {   // cruce de cuatro pasillos: misma fila que un enlace del hueco anterior; ese brazo acaba en una puerta cerrada
                // (si no, sería un atajo que se salta el zigzag)
                List<int> prev = gap[g - 1];
                for (int t = 0; t < 20; t++) { int row = prev[r.Next(prev.Count)]; if (TryAdd(gap[g], row)) { dead[g].Add(row); break; } }
            }
            {   // señuelo: un transversal en la banda contraria (parece un atajo) que acaba en una puerta cerrada
                int dlo = north ? 8 : Mathf.RoundToInt(h * 0.3f), dhi = north ? Mathf.RoundToInt(h * 0.7f) : h - 8;
                for (int t = 0; t < 50; t++) { int row = dlo + r.Next(Mathf.Max(1, dhi - dlo)); if (TryAdd(gap[g], row)) { dead[g].Add(row); break; } }
            }
            gap[g].Sort();
        }
        for (int g = 0; g < gap.Length; g++)
            foreach (int row in gap[g])
            {
                bool de = dead[g].Contains(row);
                p.runs[g].doors[Right, row] = Door.Link; if (!de) p.runs[g + 1].doors[Left, row] = Door.Link; else p.reserved.Add(Key(g + 1, Left, row));
                Run c = new Run { id = p.runs.Count, cross = true, a = g, b = g + 1, row = row, rows = CrossModules, doors = new Door[2, CrossModules], deadEnd = de };
                for (int k = 1; k < CrossModules - 1; k++) for (int s = 0; s < 2; s++) if (r.NextDouble() < 0.07) c.doors[s, k] = Door.Bricked;
                p.runs.Add(c);
            }
        // tapiadas en los pasillos largos (~7 %), lejos de los enlaces
        for (int i = 0; i < p.Mains; i++)
            for (int j = MinRow; j < h - 1; j++)
                for (int s = 0; s < 2; s++)
                    if (p.runs[i].doors[s, j] == Door.Locked && !p.NearLink(i, s, j, 1) && !p.reserved.Contains(Key(i, s, j)) && r.NextDouble() < 0.07)
                    { p.runs[i].doors[s, j] = Door.Bricked; if (i == 0) p.bricked.Add(new Vector2Int(s, j)); }
        // salida: de las puertas cerradas lejos de enlaces, una de las más lejanas del ascensor por el camino más corto
        int W = GridWidthFor(p.Mains);
        bool[,] wv = new bool[W + 1, h], wh = new bool[W, h + 1];
        p.FillWalls(wv, wh, W, h, true); p.WV = wv; p.WH = wh;
        p.Dist = Bfs(wv, wh, W, h, new Vector2Int(p.Col, 0));
        int maxD = 0;
        for (int i = 0; i < p.Mains; i++) for (int j = MinRow + 2; j < h - 2; j++) maxD = Mathf.Max(maxD, p.Dist[p.ColOf(i), j]);
        List<Vector3Int> cand = new List<Vector3Int>();
        for (int i = 0; i < p.Mains; i++)
            for (int j = MinRow + 2; j < h - 2; j++)
                for (int s = 0; s < 2; s++)
                    if (p.runs[i].doors[s, j] == Door.Locked && !p.NearLink(i, s, j, 2) && !p.reserved.Contains(Key(i, s, j)) && p.Dist[p.ColOf(i), j] >= maxD * 0.85f) cand.Add(new Vector3Int(i, j, s));
        Vector3Int ex = cand.Count > 0 ? cand[r.Next(cand.Count)] : new Vector3Int(0, h - 3, 0);
        p.exitRun = ex.x; p.exitRow = ex.y; p.exitSide = ex.z;
        p.runs[ex.x].doors[ex.z, ex.y] = Door.Exit;
        p.PathCells = Mathf.Max(0, p.Dist[p.ColOf(ex.x), ex.y]);
        return p;
    }

    static int gap0(int mains) { return Mathf.Max(0, mains - 1); }
    static long Key(int main, int side, int row) { return ((long)main * 2 + side) * 100000 + row; }
    public readonly HashSet<long> reserved = new HashSet<long>();   // puertas cerradas al fondo de un transversal sin salida
    public int DeadEnds { get { int n = 0; foreach (Run ru in runs) if (ru.cross && ru.deadEnd) n++; return n; } }
    static bool TryAdd(List<int> l, int row) { foreach (int x in l) if (Mathf.Abs(x - row) < 4) return false; l.Add(row); return true; }

    public bool NearLink(int main, int side, int row, int d)
    {
        for (int j = Mathf.Max(0, row - d); j <= Mathf.Min(rows - 1, row + d); j++)
            for (int s = 0; s < 2; s++) if (runs[main].doors[s, j] == Door.Link && (s == side || j == row)) return true;
        return false;
    }

    void FillWalls(bool[,] wallsV, bool[,] wallsH, int w, int h, bool reset)
    {
        if (reset) { for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) wallsV[i, j] = true; for (int i = 0; i < w; i++) for (int j = 0; j <= h; j++) wallsH[i, j] = true; }
        for (int i = 0; i < Mains; i++) { int C = ColOf(i); if (C >= w) continue; for (int j = 1; j < h; j++) wallsH[C, j] = false; }
        foreach (Run c in runs)
        {
            if (!c.cross) continue;
            for (int x = ColOf(c.a) + 1; x <= ColOf(c.b) - (c.deadEnd ? 1 : 0) && x < w; x++) wallsV[x, c.row] = false;
        }
    }
    void ApplyWalls(bool[,] wallsV, bool[,] wallsH, int w, int h) { FillWalls(wallsV, wallsH, w, h, false); }

    public static int[,] Bfs(bool[,] wv, bool[,] wh, int w, int h, Vector2Int s)
    {
        int[,] d = new int[w, h];
        for (int x = 0; x < w; x++) for (int z = 0; z < h; z++) d[x, z] = -1;
        Queue<Vector2Int> q = new Queue<Vector2Int>(); d[s.x, s.y] = 0; q.Enqueue(s);
        while (q.Count > 0)
        {
            Vector2Int c = q.Dequeue(); int dc = d[c.x, c.y] + 1;
            if (c.x + 1 < w && !wv[c.x + 1, c.y] && d[c.x + 1, c.y] < 0) { d[c.x + 1, c.y] = dc; q.Enqueue(new Vector2Int(c.x + 1, c.y)); }
            if (c.x - 1 >= 0 && !wv[c.x, c.y] && d[c.x - 1, c.y] < 0) { d[c.x - 1, c.y] = dc; q.Enqueue(new Vector2Int(c.x - 1, c.y)); }
            if (c.y + 1 < h && !wh[c.x, c.y + 1] && d[c.x, c.y + 1] < 0) { d[c.x, c.y + 1] = dc; q.Enqueue(new Vector2Int(c.x, c.y + 1)); }
            if (c.y - 1 >= 0 && !wh[c.x, c.y] && d[c.x, c.y - 1] < 0) { d[c.x, c.y - 1] = dc; q.Enqueue(new Vector2Int(c.x, c.y - 1)); }
        }
        return d;
    }

    // celda abierta (pasillo largo o transversal)
    public bool IsOpenCell(int x, int z)
    {
        if (z < 0 || z >= rows) return false;
        for (int i = 0; i < Mains; i++) if (x == ColOf(i)) return true;
        foreach (Run c in runs) if (c.cross && c.row == z && x > ColOf(c.a) && x < ColOf(c.b)) return true;
        return false;
    }
    public bool IsRoomCell(int x, int z) { return x != Col && IsOpenCell(x, z); }   // compatibilidad: celdas abiertas fuera del pasillo 0
    public int Count(Door d) { int n = 0; foreach (Run ru in runs) for (int s = 0; s < 2; s++) for (int j = 0; j < ru.rows; j++) if (ru.doors[s, j] == d) n++; return n; }
    public int CrossCount { get { int n = 0; foreach (Run ru in runs) if (ru.cross) n++; return n; } }
}
