using System.Collections.Generic;
using UnityEngine;

// v7.0 Campo infinito: reparto determinista (solo semilla) de casas abandonadas, postes, puertas sueltas y árboles secos.
// Las celdas ocupadas se marcan como bloqueadas para que no caigan encima objetos, monstruos ni la salida.
public class FieldPlan
{
    public struct House { public Vector2Int cell; public int template; public int rot; public Vector2 pos; public bool enterable; public int tint; }
    public struct Prop { public int kind; public Vector2 pos; public float yaw; }   // 0 poste, 1 puerta suelta (señuelo), 2 árbol seco
    public const int PropPole = 0, PropDoor = 1, PropTree = 2;

    public readonly List<House> houses = new List<House>();
    public readonly List<Prop> props = new List<Prop>();
    public readonly HashSet<Vector2Int> blocked = new HashSet<Vector2Int>();
    public readonly List<Rect> footprints = new List<Rect>();        // XZ local (sin hierba dentro)
    public readonly List<Vector3> clearings = new List<Vector3>();   // x, z, radio (sin hierba)

    public const int MinEnterable = 5;
    public static readonly Vector2[] TemplateSize = { new Vector2(7.2f, 5.6f), new Vector2(6.0f, 6.0f), new Vector2(8.4f, 5.0f) };

    public static FieldPlan Make(int seed, int w, int h, float cs, Vector2Int start, Vector2Int exit)
    {
        FieldPlan p = new FieldPlan();
        System.Random r = new System.Random(seed * 97 + 5);
        System.Func<Vector2Int, Vector2> center = c => new Vector2((c.x + 0.5f) * cs, (c.y + 0.5f) * cs);

        // casas: ~12, separadas; muchas del mismo modelo, tinte y giro (casi idénticas) para desorientar
        int want = Mathf.Max(6, (w * h) / 40);
        int sameTpl = r.Next(3), sameRot = r.Next(4), sameTint = r.Next(3);
        int guard = 0;
        while (p.houses.Count < want && guard++ < 2000)
        {
            Vector2Int c = new Vector2Int(r.Next(w), r.Next(h));
            if (Cheb(c, start, w, h) < 3 || Cheb(c, exit, w, h) < 2) continue;
            bool ok = true;
            foreach (House o in p.houses) if (Cheb(c, o.cell, w, h) < 3) { ok = false; break; }
            if (!ok) continue;
            bool twin = r.NextDouble() < 0.6;
            House hs = new House
            {
                cell = c,
                template = twin ? sameTpl : r.Next(3),
                rot = twin ? sameRot : r.Next(4),
                tint = twin ? sameTint : r.Next(3),
                enterable = r.NextDouble() < 0.5,
                pos = center(c) + new Vector2((float)(r.NextDouble() - 0.5) * 1.5f, (float)(r.NextDouble() - 0.5) * 1.5f),
            };
            p.houses.Add(hs);
            p.blocked.Add(c);
            Vector2 sz = TemplateSize[hs.template];
            if (hs.rot % 2 == 1) sz = new Vector2(sz.y, sz.x);
            p.footprints.Add(new Rect(hs.pos - sz * 0.5f - Vector2.one * 0.35f, sz + Vector2.one * 0.7f));
        }

        // v7.0.2: los objetos van dentro de las casas abiertas: al menos 5 abiertas (las primeras cerradas se abren)
        int open = 0; foreach (House hh in p.houses) if (hh.enterable) open++;
        for (int i = 0; i < p.houses.Count && open < MinEnterable; i++)
            if (!p.houses[i].enterable) { House hh = p.houses[i]; hh.enterable = true; p.houses[i] = hh; open++; }

        // fila de postes de la luz que cruza todo el campo (no bloquea celdas: van a 3 m del centro)
        bool alongX = r.NextDouble() < 0.5;
        int line = r.Next(alongX ? h : w);
        for (int k = 0; k < (alongX ? w : h); k += 2)
        {
            Vector2Int c = alongX ? new Vector2Int(k, line) : new Vector2Int(line, k);
            if (p.blocked.Contains(c) || c == start || c == exit) continue;
            Vector2 pos = center(c) + (alongX ? new Vector2(0f, 3.2f) : new Vector2(3.2f, 0f));
            p.props.Add(new Prop { kind = PropPole, pos = pos, yaw = alongX ? 90f : 0f });
            p.clearings.Add(new Vector3(pos.x, pos.y, 0.5f));
        }
        // puertas sueltas (señuelos idénticos a la salida pero sin su luz) y árboles secos
        AddSingles(p, r, w, h, start, exit, center, PropDoor, 4, 1.4f);
        AddSingles(p, r, w, h, start, exit, center, PropTree, 3, 1.2f);
        return p;
    }

    private static void AddSingles(FieldPlan p, System.Random r, int w, int h, Vector2Int start, Vector2Int exit,
        System.Func<Vector2Int, Vector2> center, int kind, int count, float clear)
    {
        int n = 0, guard = 0;
        while (n < count && guard++ < 500)
        {
            Vector2Int c = new Vector2Int(r.Next(w), r.Next(h));
            if (p.blocked.Contains(c) || Cheb(c, start, w, h) < 3 || Cheb(c, exit, w, h) < 3) continue;
            Vector2 pos = center(c);
            p.props.Add(new Prop { kind = kind, pos = pos, yaw = (float)(r.NextDouble() * 360.0) });
            p.blocked.Add(c);
            p.clearings.Add(new Vector3(pos.x, pos.y, clear));
            n++;
        }
    }

    // distancia de Chebyshev en celdas con vuelta (el campo se repite)
    public static int Cheb(Vector2Int a, Vector2Int b, int w, int h)
    {
        int dx = Mathf.Abs(a.x - b.x), dz = Mathf.Abs(a.y - b.y);
        dx = Mathf.Min(dx, w - dx); dz = Mathf.Min(dz, h - dz);
        return Mathf.Max(dx, dz);
    }

    // ¿punto local (x,z) sin hierba? (casas, árboles, puertas, postes)
    public bool NoGrass(float x, float z)
    {
        for (int i = 0; i < footprints.Count; i++)
        {
            Rect f = footprints[i];
            float dx = FieldHeight.Wrap(x - f.center.x), dz = FieldHeight.Wrap(z - f.center.y);
            if (Mathf.Abs(dx) < f.width * 0.5f && Mathf.Abs(dz) < f.height * 0.5f) return true;
        }
        for (int i = 0; i < clearings.Count; i++)
        {
            Vector3 c = clearings[i];
            float dx = FieldHeight.Wrap(x - c.x), dz = FieldHeight.Wrap(z - c.y);
            if (dx * dx + dz * dz < c.z * c.z) return true;
        }
        return false;
    }
}
