using UnityEngine;

// v7.3.0: el faro. Apagado al principio (sólo se intuye su silueta); cuando alguien se acerca a menos de ~78 m se enciende:
// la lámpara brilla a través de la niebla, dos haces giran y suena una sirena grave (tonal) una vez.
public class OceanLighthouse : MonoBehaviour
{
    public OceanWorld world;
    public bool On { get; private set; }
    public float OnTime { get; private set; } = -1f;
    private Transform beams;
    private Material coreM, beamM;
    private Light lamp;
    private float k;
    private AudioSource horn;

    public static OceanLighthouse Create(OceanWorld w, Transform parent, Vector3 lampPos, System.Action<Object> track)
    {
        GameObject g = new GameObject("Lantern"); g.transform.SetParent(parent, false); g.transform.position = lampPos;
        OceanLighthouse l = g.AddComponent<OceanLighthouse>();
        l.world = w;
        l.coreM = OceanGeo.Unlit("Ocean/Glow", new Color(1f, 0.92f, 0.75f), track);
        l.coreM.SetFloat("_Fog", 0.0035f); l.coreM.SetFloat("_Intensity", 0f);
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
        q.name = "Core"; q.transform.SetParent(g.transform, false); q.transform.localScale = Vector3.one * 9f;
        q.GetComponent<Renderer>().sharedMaterial = l.coreM; q.AddComponent<OceanBillboard>();
        // haces: conos largos, aditivos, con su propia niebla
        l.beamM = OceanGeo.Unlit("Ocean/Glow", new Color(0.9f, 0.85f, 0.7f), track);
        l.beamM.SetFloat("_Fog", 0.006f); l.beamM.SetFloat("_Intensity", 0f); l.beamM.SetFloat("_Soft", 0f);
        l.beams = new GameObject("Beams").transform; l.beams.SetParent(g.transform, false);
        MeshBuilder mb = new MeshBuilder(1);
        foreach (float s in new[] { 1f, -1f }) Cone(mb, Vector3.zero, new Vector3(s * 120f, -6f, 0f), 0.5f, 9f);
        Mesh cm = mb.ToMesh("Beams"); track(cm);
        // alfa a lo largo del haz en el color de vértice (más fuerte cerca)
        Color[] cols = new Color[cm.vertexCount]; Vector3[] vs = cm.vertices;
        for (int i = 0; i < vs.Length; i++) { float d = vs[i].magnitude; cols[i] = new Color(1f, 1f, 1f, 0.16f * Mathf.Clamp01(1f - d / 125f)); }
        cm.colors = cols;
        l.beams.gameObject.AddComponent<MeshFilter>().sharedMesh = cm;
        MeshRenderer mr = l.beams.gameObject.AddComponent<MeshRenderer>(); mr.sharedMaterial = l.beamM; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        l.lamp = g.AddComponent<Light>(); l.lamp.type = LightType.Point; l.lamp.range = 55f; l.lamp.intensity = 0f; l.lamp.color = new Color(1f, 0.9f, 0.72f); l.lamp.shadows = LightShadows.None;
        l.horn = g.AddComponent<AudioSource>(); l.horn.spatialBlend = 0.6f; l.horn.minDistance = 30f; l.horn.maxDistance = 400f; l.horn.rolloffMode = AudioRolloffMode.Linear; l.horn.dopplerLevel = 0f;
        l.horn.clip = OceanAudio.HornClip();
        return l;
    }

    private static void Cone(MeshBuilder mb, Vector3 a, Vector3 b, float r0, float r1)
    {
        Vector3 ax = (b - a).normalized;
        Vector3 s = Vector3.Cross(ax, Vector3.up).normalized, u = Vector3.Cross(s, ax);
        const int n = 10;
        for (int i = 0; i < n; i++)
        {
            float a0 = i * Mathf.PI * 2f / n, a1 = (i + 1) * Mathf.PI * 2f / n;
            Vector3 d0 = s * Mathf.Cos(a0) + u * Mathf.Sin(a0), d1 = s * Mathf.Cos(a1) + u * Mathf.Sin(a1);
            mb.AddQuad(0, a + d0 * r0, b + d0 * r1, b + d1 * r1, a + d1 * r0, (d0 + d1).normalized, ax, (d1 - d0).normalized, 100f, 1f, 0f, 0f);
        }
    }

    public void SetOn(bool on)
    {
        if (on == On) return;
        On = on;
        if (on) { OnTime = Time.time; if (horn != null && horn.clip != null) horn.PlayOneShot(horn.clip, 0.8f * AudioMix.Sfx); }
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (OceanNet.Authority && !On)
        {
            foreach (OceanNet.Target t in OceanNet.Targets())
            {
                Vector2 d = new Vector2(t.pos.x - OceanWorld.IslandCenter.x, t.pos.z - OceanWorld.IslandCenter.z);
                if (d.magnitude < OceanWorld.LighthouseRange) { OceanNet.Event(OceanNet.EvLighthouse, 1, 0, Vector3.zero, Vector3.zero); break; }
            }
        }
        k = Mathf.MoveTowards(k, On ? 1f : 0f, dt * 0.5f);
        float flick = On && k < 1f ? (Mathf.PerlinNoise(Time.time * 9f, 0.3f) > 0.35f ? 1f : 0.2f) : 1f;
        coreM.SetFloat("_Intensity", k * 1.6f * flick);
        beamM.SetFloat("_Intensity", k * 1.2f * flick);
        lamp.intensity = k * 2.2f * flick;
        if (On) beams.Rotate(0f, 22f * dt, 0f, Space.World);
    }
}
