using UnityEngine;

// v7.7.0 Desierto Cálido: objetos tirados en la arena (agua de almendras, raciones reales, agua de anacardo, agua de neón)
public static class DesertItems
{
    public static int Placed { get; private set; }
    public static void Spawn(Transform worldRoot, BackroomsGenerator gen, bool net) { Placed = 0; }
}
