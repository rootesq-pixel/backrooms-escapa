using System.Collections.Generic;
using UnityEngine;

// v7.12.0 Nivel 188 — El Patio de las Ventanas. Plano lógico (todo sale de la semilla: igual en todos los equipos).
// Un patio de 7 × 13 módulos de 3,2 m rodeado por un anillo de 5 plantas: en cada lado, una fila de habitaciones con su
// ventanita al patio y, detrás, un pasillo. Cada lado tiene su propio marco: u a lo largo de la fachada (0..n·M) y
// d hacia fuera del patio (0 = fachada, 6,4 = muro exterior). La esquina de inicio de cada lado (u en [-6,4, 0]) es suya:
// en las esquinas suroeste (lado S) y noreste (lado N) hay una escalera; en las otras dos, un cuarto cerrado.
public class W188Plan
{
    public const int NX = 7, NZ = 13, Floors = 5;
    public const float M = 3.2f, R = 6.4f, FH = 3.0f;
    // profundidades (d) dentro del anillo
    public const float FacadeT = 0.25f, RoomEnd = 4.0f, CorrIn = 4.15f, CorrOut = 6.15f;
    public const float WinW = 1.1f, WinH = 1.15f, Sill = 0.95f;          // ventana cuadrada (como la foto)
    public const float DoorW = 0.95f, DoorH = 2.15f, DoorOff = 0.62f;     // puerta de la habitación (desplazada hacia u+)
    public const int S = 0, E = 1, N = 2, W = 3;
    public enum Kind : byte { Closed, Room, Open, Exit, Entity, Passage, Lift }

    public static int Len(int side) { return side == S || side == N ? NX : NZ; }
    public static float Width { get { return 2f * R + NX * M; } }   // 35,2 m (x)
    public static float Depth { get { return 2f * R + NZ * M; } }   // 54,4 m (z)
    public static readonly int[] StairSides = { S, N };             // escaleras en las esquinas de inicio de S (SO) y N (NE)
    public static bool HasStair(int side) { return side == S || side == N; }

    public readonly Kind[,,] kind = new Kind[Floors, 4, NZ];
    public readonly int[,,] variant = new int[Floors, 4, NZ];     // cerradas: bit0 encendida, bits1-2 tono; abiertas: nivel que enseñan
    public int seed;
    public int exitF, exitS, exitI;
    public readonly List<Vector3Int> Rooms = new List<Vector3Int>(), Entities = new List<Vector3Int>(), Opens = new List<Vector3Int>();
    public readonly List<Vector3Int> KeyRooms = new List<Vector3Int>();
    public static readonly Vector3Int PassageCell = new Vector3Int(0, S, 0);   // planta baja, lado S, módulo 0 (junto a la escalera SO)
    public static readonly Vector3Int LiftCell = new Vector3Int(0, N, 3);      // planta baja, lado N, módulo central (ascensor)
    public const int RoomCount = 36, EntityCount = 4, OpenCount = 9;

    public Kind K(Vector3Int c) { return kind[c.x, c.y, c.z]; }
    public Kind K(int f, int s, int i) { return kind[f, s, i]; }
    public int Count(Kind k) { int n = 0; for (int f = 0; f < Floors; f++) for (int s = 0; s < 4; s++) for (int i = 0; i < Len(s); i++) if (kind[f, s, i] == k) n++; return n; }

    // marco de cada lado (local del nivel): origen en la esquina del patio donde empieza el lado y giro
    public static Vector3 SideOrigin(int s)
    {
        float x0 = R, x1 = R + NX * M, z0 = R, z1 = R + NZ * M;
        switch (s) { case S: return new Vector3(x0, 0f, z0); case E: return new Vector3(x1, 0f, z0); case N: return new Vector3(x1, 0f, z1); default: return new Vector3(x0, 0f, z1); }
    }
    public static float SideYaw(int s) { switch (s) { case S: return 0f; case E: return -90f; case N: return 180f; default: return 90f; } }
    public static Matrix4x4 SideMatrix(int s) { return Matrix4x4.TRS(SideOrigin(s), Quaternion.Euler(0f, SideYaw(s), 0f), Vector3.one); }
    // (u, y, d) del lado s -> local del nivel. En el marco del lado: x = u, z = -d (el patio queda en z > 0)
    public static Vector3 ToLevel(int s, float u, float y, float d) { return SideMatrix(s).MultiplyPoint3x4(new Vector3(u, y, -d)); }
    public static Vector3 FromLevel(int s, Vector3 p) { Vector3 l = SideMatrix(s).inverse.MultiplyPoint3x4(p); return new Vector3(l.x, l.y, -l.z); }   // (u, y, d)
    public static float UC(int i) { return (i + 0.5f) * M; }
    public static float DoorU(int i) { return UC(i) + DoorOff; }

    public static W188Plan Make(int seed)
    {
        W188Plan p = new W188Plan { seed = seed };
        System.Random r = new System.Random(seed * 7919 + 188);
        p.kind[PassageCell.x, PassageCell.y, PassageCell.z] = Kind.Passage;
        p.kind[LiftCell.x, LiftCell.y, LiftCell.z] = Kind.Lift;
        List<Vector3Int> free = new List<Vector3Int>();
        for (int f = 0; f < Floors; f++) for (int s = 0; s < 4; s++) for (int i = 0; i < Len(s); i++) if (p.kind[f, s, i] == Kind.Closed) free.Add(new Vector3Int(f, s, i));
        System.Func<System.Predicate<Vector3Int>, Vector3Int> pick = cond =>
        {
            for (int t = 0; t < 2000; t++) { Vector3Int c = free[r.Next(free.Count)]; if (cond(c)) { free.Remove(c); return c; } }
            Vector3Int any = free[0]; free.RemoveAt(0); return any;
        };
        // salida: plantas 2-4, lejos de las esquinas con escalera (hay que buscarla)
        Vector3Int ex = pick(c => c.x >= 2 && c.z >= 1 && c.z < Len(c.y) - 1);
        p.kind[ex.x, ex.y, ex.z] = Kind.Exit; p.exitF = ex.x; p.exitS = ex.y; p.exitI = ex.z;
        // la Ventana (entidad): plantas 1-4, nunca junto a la salida ni junto a otra
        for (int k = 0; k < EntityCount; k++)
        {
            Vector3Int c = pick(q => q.x >= 1 && !Near(q, ex, 1) && !p.Entities.Exists(o => Near(q, o, 1)));
            p.kind[c.x, c.y, c.z] = Kind.Entity; p.Entities.Add(c);
        }
        // ventanas abiertas: cada una enseña un nivel distinto (0-9), repartidas
        List<int> views = new List<int>(); for (int v = 0; v < 10; v++) views.Add(v);
        for (int k = 0; k < OpenCount; k++)
        {
            Vector3Int c = pick(q => !Near(q, ex, 0) && !p.Opens.Exists(o => Near(q, o, 1)));
            int vi = views[r.Next(views.Count)]; views.Remove(vi);
            p.kind[c.x, c.y, c.z] = Kind.Open; p.variant[c.x, c.y, c.z] = vi; p.Opens.Add(c);
        }
        // habitaciones que se abren: al menos 5 por planta
        for (int f = 0; f < Floors; f++) for (int k = 0; k < 5; k++) { int ff = f; Vector3Int c = pick(q => q.x == ff); p.kind[c.x, c.y, c.z] = Kind.Room; p.Rooms.Add(c); }
        while (p.Rooms.Count < RoomCount) { Vector3Int c = pick(q => true); p.kind[c.x, c.y, c.z] = Kind.Room; p.Rooms.Add(c); }
        // cerradas: luz detrás de la cortina (~40 %) y tono de la tela
        foreach (Vector3Int c in free) p.variant[c.x, c.y, c.z] = (r.NextDouble() < 0.4 ? 1 : 0) | (r.Next(3) << 1);
        // llaves: 5 habitaciones que se abren, en plantas distintas (como mucho una en la baja)
        List<Vector3Int> rs = new List<Vector3Int>(p.Rooms);
        int[] perFloor = new int[Floors];
        for (int t = 0; t < 4000 && p.KeyRooms.Count < ExitLock.KeyCount; t++)
        {
            Vector3Int c = rs[r.Next(rs.Count)];
            if (p.KeyRooms.Contains(c) || perFloor[c.x] >= (c.x == 0 ? 1 : 2)) continue;
            p.KeyRooms.Add(c); perFloor[c.x]++;
        }
        return p;
    }
    static bool Near(Vector3Int a, Vector3Int b, int k) { return a.x == b.x && a.y == b.y && Mathf.Abs(a.z - b.z) <= k; }

    // celda lógica (rejilla de 3,2 m de la planta) de un punto local del nivel
    public static Vector2Int CellOf(Vector3 local) { return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(local.x / M), 0, 10), Mathf.Clamp(Mathf.FloorToInt(local.z / M), 0, 16)); }
    public static int FloorOf(float y) { return Mathf.Clamp(Mathf.FloorToInt((y + 0.6f) / FH), 0, Floors - 1); }
    public static bool InCourtyard(Vector3 local, float margin) { return local.x > R - margin && local.x < R + NX * M + margin && local.z > R - margin && local.z < R + NZ * M + margin; }
}
