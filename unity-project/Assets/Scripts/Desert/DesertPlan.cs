using System.Collections.Generic;
using UnityEngine;

// v7.7.0 Desierto Cálido: reparto determinista (solo semilla: idéntico en multijugador) de aldeas abandonadas, casas en
// ruinas, pozos, la casa de la salida (puerta de piedra), arenas movedizas, afloramientos de roca, cuevas, plantas secas y
// los sitios de los objetos tirados en la arena. Coordenadas XZ locales del nivel en [0, L)².
public class DesertPlan
{
    public struct House { public Vector2 pos; public float yaw; public int tpl; public int ruin; public int village; public bool exit; public int seed; }
    public struct Village { public Vector2 c; public float r; public float yaw; public bool hasWell; public Vector2 well; public bool exit; }
    public struct Quick { public Vector2 c; public float r; public int target; }
    public struct Outcrop { public Vector2 c; public float yaw; public float scale; public int kind; public bool cave; }
    public struct Plant { public Vector2 p; public float yaw; public float scale; public int kind; }

    public float L;
    public Vector2 start;
    public readonly List<Village> villages = new List<Village>();
    public readonly List<House> houses = new List<House>();
    public readonly List<Quick> quick = new List<Quick>();
    public readonly List<Outcrop> outcrops = new List<Outcrop>();
    public readonly List<Plant> plants = new List<Plant>();
    public readonly List<Vector2> sandItems = new List<Vector2>();   // objetos medio enterrados en la arena (mirar al suelo)
    public readonly List<Vector2> zones = new List<Vector2>();       // destinos de las arenas movedizas / reaparición tras el Vacío
    public int exitVillage = -1, exitHouse = -1;

    // plantillas de casa: ancho (X) x fondo (Z) interior + muros; la puerta en +Z
    public static readonly Vector2[] TemplateSize = { new Vector2(5.6f, 4.6f), new Vector2(7.4f, 5.2f), new Vector2(4.4f, 4.4f), new Vector2(8.6f, 6.0f) };
    public const float VillageMinDist = 125f, StartClear = 85f, ExitMinDist = 150f;

    public float Dist(Vector2 a, Vector2 b) { float dx = DesertHeight.Wrap(a.x - b.x), dz = DesertHeight.Wrap(a.y - b.y); return Mathf.Sqrt(dx * dx + dz * dz); }
    private Vector2 Mod(Vector2 p) { return new Vector2(Mathf.Repeat(p.x, L), Mathf.Repeat(p.y, L)); }

    public static DesertPlan Make(int seed, float L, Vector2 start)
    {
        DesertPlan p = new DesertPlan { L = L, start = start };
        System.Random r = new System.Random(seed * 389 + 61);
        // --- aldeas: 5 separadas, lejos del ascensor
        int guard = 0;
        while (p.villages.Count < 5 && guard++ < 4000)
        {
            Vector2 c = new Vector2((float)r.NextDouble() * L, (float)r.NextDouble() * L);
            if (p.Dist(c, start) < StartClear + 20f) continue;
            bool ok = true; foreach (Village v in p.villages) if (p.Dist(c, v.c) < VillageMinDist) { ok = false; break; }
            if (!ok) continue;
            p.villages.Add(new Village { c = c, r = 16f + (float)r.NextDouble() * 8f, yaw = (float)r.NextDouble() * 90f, hasWell = r.NextDouble() < 0.8 });
        }
        // la aldea de la salida: una de las lejanas (al menos 150 m del ascensor por el camino más corto con vuelta)
        List<int> far = new List<int>();
        for (int i = 0; i < p.villages.Count; i++) if (p.Dist(p.villages[i].c, start) >= ExitMinDist) far.Add(i);
        if (far.Count == 0) { float best = -1f; for (int i = 0; i < p.villages.Count; i++) { float d = p.Dist(p.villages[i].c, start); if (d > best) { best = d; p.exitVillage = i; } } }
        else p.exitVillage = far[r.Next(far.Count)];
        { Village ev = p.villages[p.exitVillage]; ev.exit = true; ev.hasWell = true; p.villages[p.exitVillage] = ev; }

        // --- casas de cada aldea: alrededor de una plaza (con el pozo en el centro), orientadas a la plaza o a la calle
        for (int vi = 0; vi < p.villages.Count; vi++)
        {
            Village v = p.villages[vi];
            int want = 4 + r.Next(4);
            float a0 = (float)r.NextDouble() * 360f;
            int placed = 0; guard = 0;
            while (placed < want && guard++ < 400)
            {
                float ang = a0 + placed * (360f / want) + ((float)r.NextDouble() - 0.5f) * 30f;
                float rad = v.r * (0.55f + (float)r.NextDouble() * 0.45f) + 3f;
                Vector2 dir = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad));
                Vector2 pos = p.Mod(v.c + dir * rad);
                int tpl = r.Next(TemplateSize.Length);
                // la fachada (+Z local) mira a la plaza, con un giro pequeño (casas que no están perfectamente alineadas)
                float yaw = Mathf.Atan2(-dir.x, -dir.y) * Mathf.Rad2Deg + ((float)r.NextDouble() - 0.5f) * 16f;
                Vector2 sz = TemplateSize[tpl];
                bool ok = true;
                foreach (House o in p.houses)
                {
                    Vector2 so = TemplateSize[o.tpl];
                    if (p.Dist(o.pos, pos) < (sz.magnitude + so.magnitude) * 0.5f + 2.6f) { ok = false; break; }
                }
                if (!ok) continue;
                int ruin = r.NextDouble() < 0.35 ? 0 : r.NextDouble() < 0.6 ? 1 : 2;
                p.houses.Add(new House { pos = pos, yaw = yaw, tpl = tpl, ruin = ruin, village = vi, seed = r.Next() });
                placed++;
            }
            if (v.hasWell) { v.well = p.Mod(v.c + new Vector2(((float)r.NextDouble() - 0.5f) * 3f, ((float)r.NextDouble() - 0.5f) * 3f)); p.villages[vi] = v; }
        }
        // la puerta de piedra: una casa de la aldea de la salida que conserve los muros (ruina 0-1)
        for (int i = 0; i < p.houses.Count; i++)
            if (p.houses[i].village == p.exitVillage && (p.exitHouse < 0 || p.houses[i].ruin < p.houses[p.exitHouse].ruin)) p.exitHouse = i;
        if (p.exitHouse >= 0) { House eh = p.houses[p.exitHouse]; eh.exit = true; eh.ruin = 0; p.houses[p.exitHouse] = eh; }

        // explanadas: el ascensor y cada aldea, a la altura media de las dunas de alrededor
        DesertHeight.AddPad(start, 9f, 34f, DesertHeight.MeanAround(start, 14f));
        foreach (Village v in p.villages) DesertHeight.AddPad(v.c, v.r + 7f, v.r + 34f, DesertHeight.MeanAround(v.c, v.r));

        // destinos de las arenas movedizas: afueras de cada aldea y algunos puntos sueltos entre dunas
        foreach (Village v in p.villages) p.zones.Add(p.Mod(v.c + new Vector2(v.r + 12f, 0f)));
        for (int i = 0; i < 4; i++) p.zones.Add(new Vector2((float)r.NextDouble() * L, (float)r.NextDouble() * L));

        // --- arenas movedizas: 9 manchas en zonas llanas entre dunas, lejos del ascensor, de las aldeas y de los pozos
        guard = 0;
        while (p.quick.Count < 9 && guard++ < 3000)
        {
            Vector2 c = new Vector2((float)r.NextDouble() * L, (float)r.NextDouble() * L);
            if (p.Dist(c, start) < 45f) continue;
            bool ok = true;
            foreach (Village v in p.villages) if (p.Dist(c, v.c) < v.r + 26f) { ok = false; break; }
            foreach (Quick q in p.quick) if (p.Dist(c, q.c) < 40f) { ok = false; break; }
            if (!ok) continue;
            Vector3 n = DesertHeight.Normal(c.x, c.y);
            if (n.y < 0.985f) continue;   // solo en llano (entre dunas): las arenas movedizas no están en las pendientes
            p.quick.Add(new Quick { c = c, r = 2.6f + (float)r.NextDouble() * 2.4f, target = r.Next(p.zones.Count) });
        }

        // --- afloramientos de roca (2 con cueva), plantas secas y objetos en la arena
        guard = 0; int caves = 0;
        while (p.outcrops.Count < 16 && guard++ < 3000)
        {
            Vector2 c = new Vector2((float)r.NextDouble() * L, (float)r.NextDouble() * L);
            if (!p.FreeOf(c, 14f, 30f)) continue;
            bool ok = true; foreach (Outcrop o in p.outcrops) if (p.Dist(c, o.c) < 45f) { ok = false; break; }
            if (!ok) continue;
            bool cave = caves < 2 && r.NextDouble() < 0.4; if (cave) caves++;
            p.outcrops.Add(new Outcrop { c = c, yaw = (float)r.NextDouble() * 360f, scale = cave ? 1.6f : 0.7f + (float)r.NextDouble() * 1.1f, kind = r.Next(4), cave = cave });
        }
        guard = 0;
        while (p.plants.Count < 170 && guard++ < 6000)
        {
            Vector2 c = new Vector2((float)r.NextDouble() * L, (float)r.NextDouble() * L);
            if (!p.FreeOf(c, 2.5f, 6f)) continue;
            if (DesertHeight.Normal(c.x, c.y).y < 0.93f) continue;   // en las caras de avalancha no crece nada
            int n = r.NextDouble() < 0.3 ? 2 + r.Next(3) : 1;        // a veces matas en grupo
            for (int k = 0; k < n && p.plants.Count < 170; k++)
            {
                Vector2 q = p.Mod(c + (k == 0 ? Vector2.zero : new Vector2((float)r.NextDouble() - 0.5f, (float)r.NextDouble() - 0.5f) * 5f));
                p.plants.Add(new Plant { p = q, yaw = (float)r.NextDouble() * 360f, scale = 0.6f + (float)r.NextDouble() * 0.8f, kind = r.Next(4) });
            }
        }
        guard = 0;
        while (p.sandItems.Count < 22 && guard++ < 4000)
        {
            // la mitad cerca de las aldeas (afueras), el resto por el desierto
            Vector2 c;
            if (p.sandItems.Count % 2 == 0) { Village v = p.villages[r.Next(p.villages.Count)]; float a = (float)r.NextDouble() * 6.283f; c = p.Mod(v.c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (v.r + 6f + (float)r.NextDouble() * 22f)); }
            else c = new Vector2((float)r.NextDouble() * L, (float)r.NextDouble() * L);
            if (p.Dist(c, start) < 12f || !p.FreeOf(c, 3f, 3f, false)) continue;
            bool ok = true; foreach (Vector2 o in p.sandItems) if (p.Dist(o, c) < 18f) { ok = false; break; }
            if (ok) p.sandItems.Add(c);
        }
        return p;
    }

    // ¿punto lejos de casas, pozos, arenas movedizas y (opcional) aldeas enteras?
    public bool FreeOf(Vector2 c, float houseMargin, float quickMargin, bool avoidVillages = true)
    {
        if (Dist(c, start) < 16f) return false;
        foreach (House h in houses) if (Dist(c, h.pos) < TemplateSize[h.tpl].magnitude * 0.5f + houseMargin) return false;
        foreach (Village v in villages) { if (avoidVillages && Dist(c, v.c) < v.r + 4f) return false; if (v.hasWell && Dist(c, v.well) < 3f) return false; }
        foreach (Quick q in quick) if (Dist(c, q.c) < q.r + quickMargin) return false;
        return true;
    }

    public int QuickAt(Vector2 c, float extra = 0f)
    {
        for (int i = 0; i < quick.Count; i++) if (Dist(c, quick[i].c) < quick[i].r + extra) return i;
        return -1;
    }
}
