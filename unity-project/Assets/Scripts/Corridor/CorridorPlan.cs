using System.Collections.Generic;
using UnityEngine;

// Nivel 7460 (pasillo de puertas): plano lógico. Rejilla de 3 columnas: la 1 es el pasillo (abierto de punta a punta),
// la 0 y la 2 son lo que hay detrás de cada puerta. Casi todas las puertas están cerradas con llave; unas pocas se abren a
// una habitación vacía idéntica (ahí están las llaves, notas y pilas), otras a un hueco tapiado con ladrillo, y una sola,
// lejos del ascensor, es la salida (cerrada con la llave buena, ExitLock). Todo sale de la semilla: igual en todos los equipos.
public class CorridorPlan
{
    public enum Door { Locked, Room, Bricked, Exit }
    public const int Left = 0, Right = 1;
    public int rows;
    public Door[,] doors;                      // [lado, fila]
    public int exitRow, exitSide;
    public readonly List<Vector2Int> rooms = new List<Vector2Int>();   // (lado, fila)
    public readonly List<Vector2Int> bricked = new List<Vector2Int>();
    public Vector2Int ExitCell { get { return new Vector2Int(1, exitRow); } }
    public const int RoomCount = 14, MinRow = 2;

    public static CorridorPlan Make(int seed, int w, int h, bool[,] wallsV, bool[,] wallsH)
    {
        CorridorPlan p = new CorridorPlan();
        p.rows = h; p.doors = new Door[2, h];
        System.Random r = new System.Random(seed * 7460 + 31);
        // salida: entre el 45 % y el 88 % del pasillo (130–300 m del ascensor), a un lado al azar
        p.exitRow = Mathf.Clamp(Mathf.RoundToInt(h * (0.45f + 0.43f * (float)r.NextDouble())), MinRow + 2, h - 2);
        p.exitSide = r.Next(2);
        p.doors[p.exitSide, p.exitRow] = Door.Exit;
        // habitaciones repartidas por todo el pasillo (una por tramo, para que haya que recorrerlo)
        int seg = Mathf.Max(1, (h - MinRow - 1) / RoomCount);
        for (int k = 0; k < RoomCount; k++)
        {
            for (int tries = 0; tries < 12; tries++)
            {
                int row = Mathf.Clamp(MinRow + k * seg + r.Next(seg), MinRow, h - 2), side = r.Next(2);
                if (p.doors[side, row] != Door.Locked) continue;
                p.doors[side, row] = Door.Room; p.rooms.Add(new Vector2Int(side, row));
                break;
            }
        }
        // puertas tapiadas (~7 %): se abren a un hueco de un metro que acaba en ladrillo
        for (int j = MinRow; j < h - 1; j++)
            for (int s = 0; s < 2; s++)
                if (p.doors[s, j] == Door.Locked && r.NextDouble() < 0.07) { p.doors[s, j] = Door.Bricked; p.bricked.Add(new Vector2Int(s, j)); }
        // muros lógicos: el pasillo abierto de punta a punta y cada habitación abierta solo hacia él
        for (int j = 1; j < h; j++) wallsH[1, j] = false;
        foreach (Vector2Int rm in p.rooms) wallsV[rm.x == Left ? 1 : 2, rm.y] = false;
        return p;
    }

    public bool IsRoomCell(int x, int z) { return (x == 0 || x == 2) && z >= 0 && z < rows && doors[x == 0 ? Left : Right, z] == Door.Room; }
    public int Count(Door d) { int n = 0; for (int s = 0; s < 2; s++) for (int j = 0; j < rows; j++) if (doors[s, j] == d) n++; return n; }
}
