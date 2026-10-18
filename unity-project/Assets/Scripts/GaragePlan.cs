using System.Collections.Generic;
using UnityEngine;

// v7.0.4: aparcamiento «casi infinito». Plantas a medio nivel (split-level) en bandas a lo largo de Z, cada una más baja que
// la anterior, unidas por rampas de 2 celdas en extremos alternos (el camino serpentea por todo el ancho), tabiques largos con
// huecos (rodeos y callejones sin salida), rampas falsas que acaban en un muro y señales que no siempre dicen la verdad.
// La rejilla sigue siendo 2D (cada celda tiene una única altura de suelo), así que el resto del generador (BFS, NavMesh,
// huella MP) funciona igual. Todo sale de la semilla: idéntico en todos los equipos.
public class GaragePlan
{
    public int w, h, decks, rows;
    public float cs, drop, wallH;
    public int[] boundaries;                 // fila Z donde empieza la planta d+1 (d = 0..decks-2)
    public int[] rampX;                      // columna de la rampa de cada frontera
    public readonly HashSet<Vector2Int> ramp = new HashSet<Vector2Int>();
    public readonly Dictionary<Vector2Int, int> rampB = new Dictionary<Vector2Int, int>();   // celda de rampa → frontera
    public readonly List<Vector2Int> decoys = new List<Vector2Int>();                         // fondo de las rampas falsas
    public readonly List<Vector2Int> decoyMouth = new List<Vector2Int>();
    public static GaragePlan Active;

    public int DeckOfRow(int z) { int d = 0; for (int b = 0; b < boundaries.Length; b++) if (z >= boundaries[b]) d = b + 1; return d; }
    public float DeckY(int d) { return -d * drop; }
    public bool IsRamp(int x, int z) { return ramp.Contains(new Vector2Int(x, z)); }

    // altura del suelo (coordenadas locales del nivel)
    public float Y(float x, float z)
    {
        int ix = Mathf.Clamp(Mathf.FloorToInt(x / cs), 0, w - 1), iz = Mathf.Clamp(Mathf.FloorToInt(z / cs), 0, h - 1);
        if (rampB.TryGetValue(new Vector2Int(ix, iz), out int b))
        {
            int zb = boundaries[b];
            float u = Mathf.Clamp01((z - (zb - 1) * cs) / (2f * cs));
            return Mathf.Lerp(DeckY(b), DeckY(b + 1), u);
        }
        return DeckY(DeckOfRow(iz));
    }
    public float CellMin(int x, int z) { return Mathf.Min(Y((x + 0.02f) * cs, (z + 0.02f) * cs), Y((x + 0.98f) * cs, (z + 0.98f) * cs)); }
    public float CellMax(int x, int z) { return Mathf.Max(Y((x + 0.02f) * cs, (z + 0.02f) * cs), Y((x + 0.98f) * cs, (z + 0.98f) * cs)); }

    // muros lógicos: wallsV[w+1,h], wallsH[w,h+1] ya tienen el laberinto abierto del aparcamiento
    public static GaragePlan Make(System.Random r, int w, int h, float cs, float wallH, int decks, float drop, bool[,] wallsV, bool[,] wallsH)
    {
        GaragePlan g = new GaragePlan { w = w, h = h, cs = cs, wallH = wallH, decks = Mathf.Max(2, decks), drop = drop };
        g.rows = h / g.decks;
        g.boundaries = new int[g.decks - 1]; g.rampX = new int[g.decks - 1];
        for (int b = 0; b < g.decks - 1; b++) g.boundaries[b] = (b + 1) * g.rows;

        for (int b = 0; b < g.boundaries.Length; b++)
        {
            int zb = g.boundaries[b];
            for (int i = 0; i < w; i++) wallsH[i, zb] = true;           // losa entre plantas
            // rampa real: en el extremo contrario al de la anterior (el inicio está en x = 0)
            bool far = b % 2 == 0;
            int rx = far ? w - 2 - r.Next(3) : 1 + r.Next(3);
            g.rampX[b] = rx;
            Vector2Int up = new Vector2Int(rx, zb - 1), dn = new Vector2Int(rx, zb);
            g.ramp.Add(up); g.ramp.Add(dn); g.rampB[up] = b; g.rampB[dn] = b;
            wallsV[rx, zb - 1] = wallsV[rx + 1, zb - 1] = wallsV[rx, zb] = wallsV[rx + 1, zb] = true;
            wallsH[rx, zb] = false; wallsH[rx, zb - 1] = false; wallsH[rx, zb + 1] = false;
            // rampa falsa: el mismo pasillo con señal, pero acaba en el muro (callejón sin salida)
            for (int tries = 0; tries < 20; tries++)
            {
                int dx = far ? 2 + r.Next(Mathf.Max(1, w / 2 - 2)) : w - 3 - r.Next(Mathf.Max(1, w / 2 - 2));
                if (Mathf.Abs(dx - rx) < 4 || dx < 1 || dx > w - 2) continue;
                Vector2Int d = new Vector2Int(dx, zb - 1);
                wallsV[dx, zb - 1] = wallsV[dx + 1, zb - 1] = true; wallsH[dx, zb - 1] = false; wallsH[dx, zb] = true;
                g.decoys.Add(d); g.decoyMouth.Add(new Vector2Int(dx, zb - 2));
                break;
            }
        }
        // tabiques largos con 1-2 huecos (rodeos) y muros cortos sueltos (callejones)
        for (int d = 0; d < g.decks; d++)
        {
            int z0 = d * g.rows, z1 = d == g.decks - 1 ? h : (d + 1) * g.rows;
            int parts = (z1 - z0) >= 8 ? 2 : 1;
            for (int p = 0; p < parts; p++)
            {
                int row = z0 + 2 + (z1 - z0 - 4) * (p + 1) / (parts + 1) + r.Next(3) - 1;
                if (row <= z0 + 1 || row >= z1 - 1) continue;
                int gaps = 1 + r.Next(2);
                for (int i = 0; i < w; i++) if (!g.ramp.Contains(new Vector2Int(i, row)) && !g.ramp.Contains(new Vector2Int(i, row - 1))) wallsH[i, row] = true;
                for (int k = 0; k < gaps; k++) { int gx = r.Next(w); wallsH[gx, row] = false; if (gx + 1 < w) wallsH[gx + 1, row] = false; }
            }
            for (int s = 0; s < 4 + w / 8; s++)
            {
                int cx = 1 + r.Next(w - 1), cz = z0 + 1 + r.Next(Mathf.Max(1, z1 - z0 - 2)), len = 2 + r.Next(4);
                for (int k = 0; k < len && cz + k < z1 - 1; k++)
                {
                    Vector2Int a = new Vector2Int(cx - 1, cz + k), b2 = new Vector2Int(cx, cz + k);
                    if (g.ramp.Contains(a) || g.ramp.Contains(b2)) break;
                    wallsV[cx, cz + k] = true;
                }
            }
        }
        // perímetro siempre cerrado
        for (int i = 0; i <= w; i++) for (int j = 0; j < h; j++) if (i == 0 || i == w) wallsV[i, j] = true;
        for (int i = 0; i < w; i++) { wallsH[i, 0] = true; wallsH[i, h] = true; }
        g.Connect(wallsV, wallsH);
        Active = g;
        return g;
    }

    public bool CanOpen(Vector2Int a, Vector2Int b) { return !Fixed(a, b); }
    private bool Fixed(Vector2Int a, Vector2Int b)
    {
        // no abrir: losas entre plantas, paredes de las rampas (reales o falsas)
        if (DeckOfRow(a.y) != DeckOfRow(b.y)) return true;
        if (ramp.Contains(a) || ramp.Contains(b)) return true;
        if (decoys.Contains(a) || decoys.Contains(b)) return a.x != b.x;   // lados del pasillo falso
        return false;
    }

    private bool Open(bool[,] V, bool[,] H, Vector2Int c, int d)
    {
        switch (d)
        {
            case 0: return c.y + 1 < h && !H[c.x, c.y + 1];
            case 1: return c.x + 1 < w && !V[c.x + 1, c.y];
            case 2: return c.y - 1 >= 0 && !H[c.x, c.y];
            default: return c.x - 1 >= 0 && !V[c.x, c.y];
        }
    }
    private static readonly Vector2Int[] D4 = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0) };

    // todas las celdas alcanzables desde (0,0) abriendo solo muros «normales» (determinista)
    private void Connect(bool[,] V, bool[,] H)
    {
        for (int guard = 0; guard < w * h; guard++)
        {
            bool[,] seen = new bool[w, h];
            Queue<Vector2Int> q = new Queue<Vector2Int>(); q.Enqueue(Vector2Int.zero); seen[0, 0] = true; int cnt = 1;
            while (q.Count > 0)
            {
                Vector2Int c = q.Dequeue();
                for (int d = 0; d < 4; d++)
                {
                    if (!Open(V, H, c, d)) continue;
                    Vector2Int n = c + D4[d];
                    if (seen[n.x, n.y]) continue;
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
                        Vector2Int n = c + D4[d];
                        if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h || !seen[n.x, n.y] || Fixed(c, n)) continue;
                        if (d == 0) H[c.x, c.y + 1] = false; else if (d == 1) V[c.x + 1, c.y] = false; else if (d == 2) H[c.x, c.y] = false; else V[c.x, c.y] = false;
                        opened = true; break;
                    }
                }
            if (!opened) return;
        }
    }
}
