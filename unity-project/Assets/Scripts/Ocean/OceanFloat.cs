using UnityEngine;

// v7.3.0: muebles y tablas que flotan (cabeceo suave con las olas; sin física para que sea igual en todos los equipos)
public class OceanFloat : MonoBehaviour
{
    public float draft = 0.12f, phase, rollAmp = 2.5f;
    private Vector3 basePos; private Quaternion baseRot;

    private void Start() { basePos = transform.position; baseRot = transform.rotation; phase = (basePos.x * 0.37f + basePos.z * 0.21f) % 6.28f; }

    private void Update()
    {
        WaterVolume w = WaterVolume.Active;
        float s = w != null ? w.SurfaceHeightAt(basePos) : 0f;
        float t = Time.time;
        transform.position = new Vector3(basePos.x + Mathf.Sin(t * 0.21f + phase) * 0.08f, s - draft + Mathf.Sin(t * 1.1f + phase) * 0.025f, basePos.z + Mathf.Cos(t * 0.17f + phase) * 0.08f);
        transform.rotation = baseRot * Quaternion.Euler(Mathf.Sin(t * 0.9f + phase) * rollAmp, Mathf.Sin(t * 0.13f + phase) * 4f, Mathf.Cos(t * 0.7f + phase) * rollAmp);
    }

    /// <summary>"-oldprops" en la línea de comandos: sofá y barca antiguos (solo para capturas de antes/después).</summary>
    public static bool OldProps { get { return System.Environment.CommandLine.Contains("-oldprops"); } }

    private static GameObject Root(Transform p, string n, Vector3 pos, float yaw, float draft)
    {
        GameObject g = new GameObject(n); g.transform.SetParent(p, false);
        g.transform.position = pos; g.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        g.AddComponent<OceanFloat>().draft = draft;
        return g;
    }

    public static GameObject Sofa(OceanWorld w, Transform p, Vector3 pos, float yaw, System.Action<Object> track)
    {
        GameObject g = Root(p, "FloatingSofa", pos, yaw, 0.28f);
        // v7.3.0: sofá modelado en Blender (cojines, brazos, respaldo hundido, tela empapada); "-oldprops" = caja antigua (capturas de antes)
        GameObject src = OldProps ? null : Resources.Load<GameObject>("Ocean/Props/Sofa");
        if (src != null)
        {
            GameObject inst = Object.Instantiate(src, g.transform, false); inst.name = "Mesh";
            Material m = OceanGeo.PbrMat("Ocean/Props/SofaFabric", Color.white, 1f, track);
            if (m.HasProperty("_GlossMapScale")) m.SetFloat("_GlossMapScale", 0.85f);
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                Material[] ms = new Material[Mathf.Max(1, r.sharedMaterials.Length)]; for (int i = 0; i < ms.Length; i++) ms[i] = m;
                r.sharedMaterials = ms; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
            BoxCollider bn = g.AddComponent<BoxCollider>(); bn.center = new Vector3(0f, 0.45f, 0f); bn.size = new Vector3(2.2f, 0.9f, 0.86f);   // el mismo de antes
            return g;
        }
        MeshBuilder mb = new MeshBuilder(2);
        OceanGeo.Box(mb, 0, new Vector3(-1.0f, 0f, -0.42f), new Vector3(1.0f, 0.42f, 0.45f), 0.6f);       // asiento
        OceanGeo.Box(mb, 0, new Vector3(-1.0f, 0.42f, 0.22f), new Vector3(1.0f, 0.95f, 0.45f), 0.6f);     // respaldo
        OceanGeo.Box(mb, 0, new Vector3(-1.12f, 0f, -0.42f), new Vector3(-0.95f, 0.66f, 0.45f), 0.6f);    // brazos
        OceanGeo.Box(mb, 0, new Vector3(0.95f, 0f, -0.42f), new Vector3(1.12f, 0.66f, 0.45f), 0.6f);
        OceanGeo.Box(mb, 1, new Vector3(-0.95f, 0.42f, -0.4f), new Vector3(-0.02f, 0.55f, 0.2f), 0.6f);   // cojines
        OceanGeo.Box(mb, 1, new Vector3(0.02f, 0.42f, -0.4f), new Vector3(0.95f, 0.55f, 0.2f), 0.6f);
        OceanGeo.Emit(mb, "Mesh", g.transform, new[] { w.ClothMat, w.ClothMat }, false, true, track);
        BoxCollider bc = g.AddComponent<BoxCollider>(); bc.center = new Vector3(0f, 0.45f, 0f); bc.size = new Vector3(2.2f, 0.9f, 0.86f);
        return g;
    }

    public static GameObject Chair(OceanWorld w, Transform p, Vector3 pos, float yaw, System.Action<Object> track)
    {
        GameObject g = Root(p, "FloatingChair", pos, yaw, 0.08f);
        g.GetComponent<OceanFloat>().rollAmp = 6f;
        // v7.3.0: la silla modelada (la misma de las casas), volcada y flotando
        if (!OldProps && w.FurnPiece("Chair", g.transform, new Vector3(0f, -0.45f, 0f), 0f, Vector3.one, Vector3.zero) != null)
        {
            g.transform.rotation *= Quaternion.Euler(0f, 0f, 78f);
            return g;
        }
        MeshBuilder mb = new MeshBuilder(1);
        OceanGeo.Box(mb, 0, new Vector3(-0.22f, 0f, -0.22f), new Vector3(0.22f, 0.04f, 0.22f), 0.5f);
        OceanGeo.Box(mb, 0, new Vector3(-0.22f, 0.04f, 0.18f), new Vector3(0.22f, 0.5f, 0.22f), 0.5f);
        foreach (Vector2 l in new[] { new Vector2(-0.19f, -0.19f), new Vector2(0.19f, -0.19f), new Vector2(-0.19f, 0.19f), new Vector2(0.19f, 0.19f) })
            OceanGeo.Box(mb, 0, new Vector3(l.x - 0.02f, -0.42f, l.y - 0.02f), new Vector3(l.x + 0.02f, 0f, l.y + 0.02f), 0.5f);
        g.transform.rotation *= Quaternion.Euler(0f, 0f, 78f);   // volcada
        OceanGeo.Emit(mb, "Mesh", g.transform, new[] { w.WoodMat }, false, true, track);
        return g;
    }

    public static GameObject Debris(OceanWorld w, Transform p, Vector3 pos, float yaw, Vector3 size, Material m, System.Action<Object> track)
    {
        GameObject g = Root(p, "FloatingDebris", pos, yaw, size.y * 0.4f);
        MeshBuilder mb = new MeshBuilder(1);
        OceanGeo.Box(mb, 0, -size * 0.5f, size * 0.5f, 0.6f);
        OceanGeo.Emit(mb, "Mesh", g.transform, new[] { m }, false, false, track);
        return g;
    }
}
