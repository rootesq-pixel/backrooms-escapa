using UnityEngine;

// v7.3.0: soporte de madera con cuatro cascos de buzo de latón (uno por jugador) al final del muelle.
// Mantener clic izquierdo mirándolo para ponerte uno. Cada casco lleva una lámpara (F la enciende/apaga).
public class OceanHelmetRack : MonoBehaviour
{
    public const int Count = 4;
    public OceanWorld world;
    public readonly int[] owner = { -1, -1, -1, -1 };
    private readonly GameObject[] helmets = new GameObject[Count];
    public int TakenCount { get { int n = 0; foreach (int o in owner) if (o >= 0) n++; return n; } }

    public static OceanHelmetRack Create(OceanWorld w, System.Action<Object> track)
    {
        GameObject g = new GameObject("HelmetRack"); g.transform.SetParent(w.root, false);
        g.transform.position = OceanWorld.HelmetRack;
        OceanHelmetRack r = g.AddComponent<OceanHelmetRack>();
        r.world = w;
        MeshBuilder mb = new MeshBuilder(1);
        OceanGeo.Box(mb, 0, new Vector3(-1.6f, 0f, -0.25f), new Vector3(-1.5f, 1.5f, 0.05f), 0.6f);
        OceanGeo.Box(mb, 0, new Vector3(1.5f, 0f, -0.25f), new Vector3(1.6f, 1.5f, 0.05f), 0.6f);
        OceanGeo.Box(mb, 0, new Vector3(-1.6f, 0.95f, -0.3f), new Vector3(1.6f, 1.02f, 0.1f), 0.6f);
        OceanGeo.Box(mb, 0, new Vector3(-1.6f, 1.45f, -0.05f), new Vector3(1.6f, 1.52f, 0.05f), 0.6f);
        OceanGeo.Emit(mb, "Rack", g.transform, new[] { w.WoodMat }, true, true, track);
        Material brass = ProceduralAssets.CreateMaterial(new Color(0.62f, 0.46f, 0.24f), Resources.Load<Texture2D>("Textures/MetalWall_a"), Vector2.one, 0.62f); track(brass);
        Material glass = ProceduralAssets.CreateMaterial(new Color(0.12f, 0.16f, 0.18f), null, Vector2.one, 0.95f); track(glass);
        for (int i = 0; i < Count; i++)
        {
            GameObject h = BuildHelmet(g.transform, brass, glass, track);
            h.transform.localPosition = new Vector3(-1.15f + i * 0.77f, 1.02f, -0.1f);
            h.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            r.helmets[i] = h;
        }
        return r;
    }

    // casco de buzo clásico: esfera de latón, ojo de buey con rejilla, collar y la lámpara encima
    public static GameObject BuildHelmet(Transform p, Material brass, Material glass, System.Action<Object> track)
    {
        GameObject h = new GameObject("DivingHelmet"); h.transform.SetParent(p, false);
        MeshBuilder mb = new MeshBuilder(2);
        Vector2[] prof = new Vector2[11];
        for (int k = 0; k <= 10; k++) { float a = Mathf.Lerp(-0.35f, Mathf.PI * 0.5f, k / 10f); prof[k] = new Vector2(Mathf.Cos(a) * 0.2f + 0.001f, 0.2f + Mathf.Sin(a) * 0.2f); }
        OceanGeo.Lathe(mb, 0, Vector3.zero, prof, 14, 0.3f, 0.3f, false);
        OceanGeo.Lathe(mb, 0, Vector3.zero, new[] { new Vector2(0.26f, 0f), new Vector2(0.26f, 0.06f), new Vector2(0.19f, 0.13f) }, 14, 0.3f, 0.3f, false);
        // ojo de buey frontal (+z) y laterales
        foreach (float ang in new[] { 0f, 90f, -90f })
        {
            Quaternion q = Quaternion.Euler(0f, ang, 0f);
            Vector3 c = q * new Vector3(0f, 0.22f, 0.19f);
            Vector3 n = q * Vector3.forward;
            float r0 = ang == 0f ? 0.085f : 0.055f;
            OceanGeo.Tube(mb, 0, c - n * 0.01f, c + n * 0.035f, r0 + 0.018f, 10, 0.3f);
            Vector3 rt = q * Vector3.right, up = Vector3.up;
            mb.AddQuad(1, c + n * 0.02f + (-rt - up) * r0, c + n * 0.02f + (rt - up) * r0, c + n * 0.02f + (rt + up) * r0, c + n * 0.02f + (-rt + up) * r0, n, rt, up, 0.2f, 0.2f, 0f, 0f);
            if (ang == 0f) for (int k = -1; k <= 1; k++) OceanGeo.Tube(mb, 0, c + n * 0.04f + rt * (k * 0.04f) - up * r0, c + n * 0.04f + rt * (k * 0.04f) + up * r0, 0.006f, 4, 0.2f);
        }
        OceanGeo.Tube(mb, 0, new Vector3(0f, 0.4f, 0.02f), new Vector3(0f, 0.44f, 0.08f), 0.035f, 8, 0.2f);   // lámpara
        OceanGeo.Emit(mb, "Mesh", h.transform, new[] { brass, glass }, false, true, track);
        return h;
    }

    public void SetOwner(int slot, int id)
    {
        if (slot < 0 || slot >= Count) return;
        owner[slot] = id;
        if (helmets[slot] != null) helmets[slot].SetActive(id < 0);
    }

    public int OwnerSlot(int id) { for (int i = 0; i < Count; i++) if (owner[i] == id) return i; return -1; }
    public int FreeSlot() { for (int i = 0; i < Count; i++) if (owner[i] < 0) return i; return -1; }

    public bool LookingAt(Transform cam, out int slot)
    {
        slot = FreeSlot();
        if (slot < 0 || cam == null) return false;
        Vector3 c = transform.position + Vector3.up * 1.1f;
        Vector3 d = c - cam.position;
        if (d.magnitude > 2.7f) return false;
        return Vector3.Dot(cam.forward, d.normalized) > 0.72f;
    }
}
