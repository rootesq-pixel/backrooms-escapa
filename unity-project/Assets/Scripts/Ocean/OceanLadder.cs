using UnityEngine;

// v7.3.0: escalera de mano desde el agua hasta un porche o el muelle (se sube nadando hacia ella o pulsando Espacio a su lado)
public class OceanLadder : MonoBehaviour
{
    public Vector3 foot, top, outDir;
    public int ClimbCount { get; private set; }

    public static OceanLadder Create(OceanWorld w, Transform parent, Vector3 topAnchorWorld, Vector3 foot, Vector3 top, Vector3 outDir, System.Action<Object> track)
    {
        GameObject g = new GameObject("WaterLadder"); g.transform.SetParent(parent, true);
        g.transform.position = topAnchorWorld;
        OceanLadder l = g.AddComponent<OceanLadder>();
        l.foot = foot; l.top = top; l.outDir = new Vector3(outDir.x, 0f, outDir.z).normalized;
        // dos largueros y peldaños (sin collider: no estorban al nadar)
        MeshBuilder mb = new MeshBuilder(1);
        Vector3 side = Vector3.Cross(Vector3.up, l.outDir).normalized * 0.28f;
        Vector3 b = topAnchorWorld - l.outDir * 0.05f;
        float y0 = -2.6f, y1 = topAnchorWorld.y + 0.9f;
        foreach (float s in new[] { -1f, 1f })
            OceanGeo.Tube(mb, 0, new Vector3(b.x, y0, b.z) + side * s, new Vector3(b.x, y1, b.z) + side * s, 0.035f, 6, 1f);
        for (float y = y0 + 0.2f; y < topAnchorWorld.y; y += 0.3f)
            OceanGeo.Tube(mb, 0, new Vector3(b.x, y, b.z) - side, new Vector3(b.x, y, b.z) + side, 0.022f, 5, 1f);
        GameObject vis = OceanGeo.Emit(mb, "Rails", g.transform, new[] { w.MetalMat }, false, false, track);
        vis.transform.position = Vector3.zero; vis.transform.rotation = Quaternion.identity;
        vis.transform.SetParent(w.root, true);
        vis.transform.SetParent(g.transform, true);
        return l;
    }

    public bool Near(Vector3 p, float r)
    {
        Vector2 d = new Vector2(p.x - foot.x, p.z - foot.z);
        return d.sqrMagnitude < r * r && p.y < top.y - 0.6f && p.y > foot.y - 3f;
    }

    public bool TryClimb(FirstPersonController fpc)
    {
        if (fpc == null || !fpc.IsSwimming || fpc.IsClimbingLadder || fpc.IsMounted) return false;
        Vector3 p = fpc.transform.position;
        if (!Near(p, 1.25f)) return false;
        if (p.y < OceanWorldSurface(p) - 2.6f) return false;   // buceando por debajo no
        bool push = Vector3.Dot(fpc.WishDir, outDir) > 0.3f;
        if (!(push || fpc.JumpHeld)) return false;
        if (fpc.StartExternalLadder(foot, top.y + 0.25f, top)) { ClimbCount++; return true; }
        return false;
    }

    private static float OceanWorldSurface(Vector3 p) { return WaterVolume.Active != null ? WaterVolume.Active.RestSurfaceAt(p) : 0f; }
}
