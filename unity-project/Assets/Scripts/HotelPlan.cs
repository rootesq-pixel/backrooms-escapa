using System.Collections.Generic;
using UnityEngine;

// v7.5.0: plano del hotel abandonado (nivel 3). Sobre el laberinto (que sigue siendo conexo: aquí solo se QUITAN muros, nunca se
// añaden, así que la salida se alcanza siempre) se abren:
//  - pasillos principales rectos y larguísimos (filas cada 6 celdas y 3 espinas verticales), con algún corte para que haya giros;
//  - salas grandes: vestíbulo junto al ascensor, salón de baile y comedor (con columnas: LevelConfig.roomPillars).
// El resto del laberinto son los pasillos de servicio, con callejones sin salida y bucles. Determinista por semilla (igual en MP).
public class HotelPlan
{
    public struct Corridor { public bool horizontal; public int line, a, b; public int number; }   // celdas [a, b] en la fila/columna «line»
    public struct Hall { public RectInt r; public string kind; }
    public readonly List<Corridor> corridors = new List<Corridor>();
    public readonly List<Hall> halls = new List<Hall>();
    public bool[,] corridorCell;   // celda de un pasillo principal
    public int[,] hallOf;          // -1 o índice de sala
    public int w, h;

    public bool InHall(int x, int z) { return x >= 0 && z >= 0 && x < w && z < h && hallOf[x, z] >= 0; }

    public static HotelPlan Make(System.Random r, int w, int h, Vector2Int start, bool[,] wallsV, bool[,] wallsH)
    {
        HotelPlan p = new HotelPlan { w = w, h = h, corridorCell = new bool[w, h], hallOf = new int[w, h] };
        for (int i = 0; i < w; i++) for (int j = 0; j < h; j++) p.hallOf[i, j] = -1;
        int num = 1;
        // filas
        for (int z = 2; z < h - 1; z += 6)
        {
            int a = 0;
            while (a < w - 2)
            {
                int len = 9 + r.Next(14);              // tramos de 9-22 celdas (29-70 m)
                int b = Mathf.Min(w - 1, a + len);
                if (w - 1 - b < 4) b = w - 1;
                p.Carve(true, z, a, b, wallsV, wallsH); p.corridors.Add(new Corridor { horizontal = true, line = z, a = a, b = b, number = num++ });
                a = b + 1 + r.Next(2);                 // corte (callejón) o continuación tras un muro
            }
        }
        // espinas verticales
        int[] xs = { 3 + r.Next(2), w / 2 + r.Next(2) - 1, w - 4 - r.Next(2) };
        foreach (int x in xs)
        {
            int a = 1 + r.Next(3);
            while (a < h - 3)
            {
                int len = 12 + r.Next(16);
                int b = Mathf.Min(h - 2, a + len);
                p.Carve(false, x, a, b, wallsV, wallsH); p.corridors.Add(new Corridor { horizontal = false, line = x, a = a, b = b, number = num++ });
                a = b + 2 + r.Next(3);
            }
        }
        // salas: vestíbulo cerca del ascensor, salón de baile y comedor lejos, sin solaparse
        p.AddHall(r, "vestibulo", 6, 5, new RectInt(1, 3, Mathf.Min(8, w - 7), 5), start, wallsV, wallsH);
        p.AddHall(r, "salon", 8, 6, new RectInt(1, h / 2, w - 10, h / 2 - 7), start, wallsV, wallsH);
        p.AddHall(r, "comedor", 6, 5, new RectInt(1, h / 4, w - 8, h / 4), start, wallsV, wallsH);
        return p;
    }

    private void Carve(bool horiz, int line, int a, int b, bool[,] wallsV, bool[,] wallsH)
    {
        for (int k = a; k <= b; k++)
        {
            if (horiz) { corridorCell[k, line] = true; if (k > a) wallsV[k, line] = false; }
            else { corridorCell[line, k] = true; if (k > a) wallsH[line, k] = false; }
        }
    }

    private void AddHall(System.Random r, string kind, int hw, int hh, RectInt area, Vector2Int start, bool[,] wallsV, bool[,] wallsH)
    {
        for (int tries = 0; tries < 40; tries++)
        {
            int x = area.x + r.Next(Mathf.Max(1, area.width - hw + 1)), z = area.y + r.Next(Mathf.Max(1, area.height - hh + 1));
            RectInt rr = new RectInt(x, z, hw, hh);
            if (rr.xMax > w || rr.yMax > h || rr.x < 1) continue;
            bool ok = !(start.x >= x - 1 && start.x <= rr.xMax && start.y >= z - 1 && start.y <= rr.yMax);
            foreach (Hall o in halls) if (rr.xMax + 1 > o.r.x && o.r.xMax + 1 > rr.x && rr.yMax + 1 > o.r.y && o.r.yMax + 1 > rr.y) ok = false;
            if (!ok) continue;
            for (int i = x + 1; i < rr.xMax; i++) for (int j = z; j < rr.yMax; j++) wallsV[i, j] = false;
            for (int i = x; i < rr.xMax; i++) for (int j = z + 1; j < rr.yMax; j++) wallsH[i, j] = false;
            for (int i = x; i < rr.xMax; i++) for (int j = z; j < rr.yMax; j++) hallOf[i, j] = halls.Count;
            halls.Add(new Hall { r = rr, kind = kind });
            return;
        }
    }
}
