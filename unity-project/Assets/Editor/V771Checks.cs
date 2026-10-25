// v7.7.1 «Arreglo del desierto»: integridad de las mallas de rocas (sin jirones flotantes), objetos modelados, cordura sin bajar.
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public static class V771Checks
{
    public static readonly string[] Rocks = { "DCliff0", "DCliff1", "DRock0", "DRock1", "DRock2", "DRock3" };
    public static readonly string[] Props = { "DCliff0", "DCliff1", "DRock0", "DRock1", "DRock2", "DRock3", "DRocks", "DStones", "DPebbles", "DPlant0", "DPlant1", "DPlant2", "DPlant3",
        "FChair0", "FChair0_broken", "FChair1_broken", "FTable0", "FTable0_broken", "FStool0", "FStool1_broken", "FBed_broken", "FBucket", "FShelf_broken", "FJug", "FPot", "FFirePit", "FCrate_broken", "FCabinet_broken", "FBarrels", "FLadder_broken" };

    // Rayos desde 8 rumbos y 2 alturas (eje Y arriba, espacio de la malla), rejilla 9x9 sobre el contorno: ¿el primer triángulo que
    // toca cada rayo mira hacia la cámara? Una cáscara abierta vista desde atrás da caras traseras (que no se dibujan): jirones.
    public static float FrontFirst(Mesh m, out float worst)
    {
        Vector3[] v = m.vertices; int[] t = m.triangles; Bounds b = m.bounds; Vector3 c = b.center; float r = b.extents.magnitude;
        worst = 1f; int tot = 0, good = 0;
        for (int ai = 0; ai < 8; ai++)
            foreach (float ei in new[] { 0.25f, 0.5f })
            {
                float az = ai * Mathf.PI / 4f;
                Vector3 d = new Vector3(Mathf.Cos(az) * Mathf.Cos(ei), Mathf.Sin(ei), Mathf.Sin(az) * Mathf.Cos(ei)).normalized;   // hacia la cámara
                Vector3 u = Vector3.Cross(Vector3.up, d).normalized, w = Vector3.Cross(d, u);
                int h = 0, g = 0;
                for (int i = 0; i < 9; i++)
                    for (int j = 0; j < 9; j++)
                    {
                        Vector3 o = c + d * r * 2f + u * ((i - 4) / 4f * r * 0.9f) + w * ((j - 4) / 4f * r * 0.9f);
                        float best = float.MaxValue; bool front = false;
                        for (int k = 0; k < t.Length; k += 3)
                        {
                            Vector3 a = v[t[k]], e1 = v[t[k + 1]] - a, e2 = v[t[k + 2]] - a;
                            Vector3 p = Vector3.Cross(-d, e2); float det = Vector3.Dot(e1, p);
                            if (Mathf.Abs(det) < 1e-9f) continue;
                            float inv = 1f / det; Vector3 s = o - a; float uu = Vector3.Dot(s, p) * inv; if (uu < 0f || uu > 1f) continue;
                            Vector3 q = Vector3.Cross(s, e1); float vv = Vector3.Dot(-d, q) * inv; if (vv < 0f || uu + vv > 1f) continue;
                            float dist = Vector3.Dot(e2, q) * inv;
                            if (dist > 0f && dist < best) { best = dist; front = Vector3.Dot(Vector3.Cross(e1, e2), d) > 0f; }
                        }
                        if (best < float.MaxValue) { h++; if (front) g++; }
                    }
                if (h > 5) { worst = Mathf.Min(worst, g / (float)h); tot += h; good += g; }
            }
        return tot > 0 ? good / (float)tot : 1f;
    }

    // vértices lejos del cuerpo, aristas larguísimas y trozos sueltos fuera del cuerpo principal
    public static string Integrity(Mesh m, Bounds lod0, out bool ok, bool solid = true)
    {
        Vector3[] v = m.vertices; int[] t = m.triangles; float diag = lod0.size.magnitude;
        Bounds big = lod0; big.Expand(diag * 0.04f);
        int far = 0, nan = 0; foreach (Vector3 p in v) { if (float.IsNaN(p.x + p.y + p.z)) nan++; else if (!big.Contains(p)) far++; }
        // astillas: triángulos larguísimos y finísimos (los «jirones» típicos de un diezmado roto)
        int sliver = 0; float maxE = 0f;
        for (int k = 0; k < t.Length; k += 3)
        {
            Vector3 a = v[t[k]], b1 = v[t[k + 1]], c1 = v[t[k + 2]];
            float e = Mathf.Max((a - b1).magnitude, Mathf.Max((b1 - c1).magnitude, (c1 - a).magnitude)); maxE = Mathf.Max(maxE, e);
            float area = Vector3.Cross(b1 - a, c1 - a).magnitude * 0.5f;
            if (e > diag * 0.2f && e * e > 60f * Mathf.Max(area, 1e-9f)) sliver++;
        }
        // componentes conexas (soldando posiciones a 1 mm); «flotante» = pieza que no toca el suelo ni ninguna otra pieza
        var id = new Dictionary<Vector3Int, int>(); int[] map = new int[v.Length];
        for (int i = 0; i < v.Length; i++) { Vector3Int key = Vector3Int.RoundToInt(v[i] * 1000f); if (!id.TryGetValue(key, out int x)) { x = id.Count; id[key] = x; } map[i] = x; }
        int[] par = new int[id.Count]; for (int i = 0; i < par.Length; i++) par[i] = i;
        System.Func<int, int> find = null; find = a => { while (par[a] != a) { par[a] = par[par[a]]; a = par[a]; } return a; };
        for (int k = 0; k < t.Length; k += 3) { int a = find(map[t[k]]), b2 = find(map[t[k + 1]]); par[b2] = a; int c2 = find(map[t[k + 2]]); par[c2] = a; }
        var comp = new Dictionary<int, Bounds>();
        for (int k = 0; k < t.Length; k += 3)
        {
            int rt = find(map[t[k]]);
            if (!comp.TryGetValue(rt, out Bounds cb)) cb = new Bounds(v[t[k]], Vector3.zero);
            cb.Encapsulate(v[t[k]]); cb.Encapsulate(v[t[k + 1]]); cb.Encapsulate(v[t[k + 2]]); comp[rt] = cb;
        }
        var list = new List<Bounds>(comp.Values); int loose = 0; float ground = lod0.min.y + lod0.size.y * 0.25f;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].min.y <= ground) continue;
            Bounds bi = list[i]; bi.Expand(diag * 0.02f); bool touch = false;
            for (int j = 0; j < list.Count && !touch; j++) if (j != i && bi.Intersects(list[j])) touch = true;
            if (!touch) loose++;
        }
        ok = far == 0 && nan == 0 && (!solid || (sliver == 0 && loose == 0));   // tablas finas y hojas sueltas son normales en muebles y plantas
        return "tris " + t.Length / 3 + ", lejos " + far + ", astillas " + sliver + ", arista máx " + (maxE / diag).ToString("0.00") + "·diag, piezas " + comp.Count + " (flotando " + loose + ")";
    }

    public static List<Mesh> Lods(string name)
    {
        var l = new List<Mesh>(); GameObject g = Resources.Load<GameObject>("Desert/Props/" + name); if (g == null) return l;
        for (int i = 0; i < 3; i++)
            foreach (MeshFilter mf in g.GetComponentsInChildren<MeshFilter>(true)) if (mf.name.EndsWith("_LOD" + i) && mf.sharedMesh != null) l.Add(mf.sharedMesh);
        return l;
    }

    // ---------------- edición ----------------
    public static void Edit(SmokeTest.Report r)
    {
        string T = "[v7.7.1] ";
        r.Check(GameVersion.Current == "7.7.1", T + "versión " + GameVersion.Current);
        // 1) rocas grandes cerradas por detrás (todas las instancias salen de estas mallas, también las copias del borde)
        foreach (string n in Rocks)
        {
            List<Mesh> ls = Lods(n); bool ok = ls.Count == 3; var sb = new StringBuilder();
            foreach (Mesh m in ls) { float worst; float f = FrontFirst(m, out worst); sb.Append(" " + m.name.Substring(m.name.Length - 4) + " " + f.ToString("0.00") + "/" + worst.ToString("0.00")); if (f < 0.9f || worst < 0.5f) ok = false; }
            r.Check(ok, T + "roca " + n + " cerrada desde todos los lados (primer impacto de cara delantera, media/peor vista:" + sb + ")");
        }
        // 2) ningún modelo del desierto con triángulos sueltos, vértices lejanos o trozos separados, en ningún LOD
        int bad = 0; var why = new StringBuilder();
        foreach (string n in Props)
        {
            List<Mesh> ls = Lods(n); if (ls.Count == 0) { bad++; why.Append(n + " sin LOD; "); continue; }
            bool solid = n.StartsWith("DR") || n.StartsWith("DC") || n == "DStones" || n == "DPebbles";
            foreach (Mesh m in ls) { bool ok; string s = Integrity(m, ls[0].bounds, out ok, solid); if (!ok) { bad++; why.Append(m.name + ": " + s + "; "); } }
        }
        r.Check(bad == 0, T + "mallas del desierto íntegras (" + Props.Length + " modelos × 3 LOD: sin vértices lejos; rocas sin astillas ni trozos flotando) " + why);
        // 3) cordura
        r.Check(Sanity.NoDrainLevel == false, T + "fuera del desierto la cordura funciona como siempre");
        // 4) objetos modelados
        foreach (ItemType it in new[] { ItemType.CashewWater, ItemType.NeonWater, ItemType.RoyalRations })
        {
            GameObject g = new GameObject("t771_" + it); bool built = DesertItemModels.Build(it, g.transform);
            int parts = g.GetComponentsInChildren<MeshRenderer>(true).Length; Bounds bb = new Bounds(); bool first = true;
            foreach (MeshRenderer mr in g.GetComponentsInChildren<MeshRenderer>(true)) { if (first) { bb = mr.bounds; first = false; } else bb.Encapsulate(mr.bounds); }
            bool glass = false; foreach (MeshRenderer mr in g.GetComponentsInChildren<MeshRenderer>(true)) if (mr.sharedMaterial != null && mr.sharedMaterial.renderQueue >= 3000) glass = true;
            float hgt = bb.size.y;
            bool size = it == ItemType.RoyalRations ? hgt > 0.11f && hgt < 0.16f : hgt > 0.26f && hgt < 0.33f;
            r.Check(built && parts >= 3 && size && (glass || it == ItemType.RoyalRations), T + "modelo de " + it + ": " + parts + " piezas, alto " + hgt.ToString("0.000") + " m" + (glass ? ", vidrio translúcido" : ""));
            Object.DestroyImmediate(g);
        }
    }
}
