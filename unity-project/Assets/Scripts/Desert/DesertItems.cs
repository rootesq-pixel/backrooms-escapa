using System.Collections.Generic;
using UnityEngine;

// v7.7.0 Nivel 114: objetos tirados en la arena (Backrooms Wiki): agua de almendras (lo principal), Raciones Reales
// (caducadas), agua de anacardo y, muy rara, agua de neón («fíjate en el suelo»). Reparto determinista con la semilla
// (igual en multijugador), ids de red 5000+.
public static class DesertItems
{
    public const int NetBase = 5000;
    public static int Placed { get; private set; }
    public static int NeonPlaced { get; private set; }
    public static readonly List<ItemType> Types = new List<ItemType>();

    public static List<ItemType> Plan(int seed, int n)
    {
        System.Random r = new System.Random(seed * 613 + 29);
        List<ItemType> t = new List<ItemType>();
        int neon = 1 + (r.NextDouble() < 0.35 ? 1 : 0);
        for (int i = 0; i < n; i++)
        {
            ItemType k;
            if (i < neon) k = ItemType.NeonWater;
            else { double x = r.NextDouble(); k = x < 0.42 ? ItemType.AlmondWater : x < 0.70 ? ItemType.RoyalRations : x < 0.94 ? ItemType.CashewWater : ItemType.Battery; }
            t.Add(k);
        }
        for (int i = t.Count - 1; i > 0; i--) { int j = r.Next(i + 1); ItemType x = t[i]; t[i] = t[j]; t[j] = x; }
        return t;
    }

    public static void Spawn(Transform worldRoot, BackroomsGenerator gen, bool net)
    {
        Placed = 0; NeonPlaced = 0; Types.Clear();
        DesertWorld dw = DesertWorld.Active;
        if (dw == null || dw.SandSpots.Count == 0) return;
        Transform root = new GameObject("DesertPickups").transform; root.SetParent(worldRoot, false);
        List<ItemType> plan = Plan(gen.UsedSeed, dw.SandSpots.Count);
        System.Random r = new System.Random(gen.UsedSeed * 17 + 3);
        for (int i = 0; i < dw.SandSpots.Count; i++)
        {
            Vector3 p = dw.SandSpots[i] + Vector3.down * 0.07f;          // tumbado y medio enterrado en la arena
            ItemPickup ip = ItemPickup.Spawn(plan[i], p, -1, root, 0f);
            ip.transform.rotation = Quaternion.Euler(0f, (float)r.NextDouble() * 360f, 0f) * Quaternion.Euler(((float)r.NextDouble() - 0.5f) * 20f, 0f, 72f + (float)r.NextDouble() * 16f);
            ip.resting = true;   // quieto en la arena (sin girar ni flotar)
            if (net) { ip.netId = NetBase + i; NetSession.RegisterPickup(ip.netId, ip); }
            Types.Add(plan[i]); Placed++;
            if (plan[i] == ItemType.NeonWater) NeonPlaced++;
        }
    }
}
