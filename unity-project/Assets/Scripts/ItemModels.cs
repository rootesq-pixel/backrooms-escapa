using System.Collections.Generic;
using UnityEngine;

// v7.0.2: modelos 3D realistas de los objetos recogibles (Tools/blender/item_models.py: modelado en Blender, horneado PBR
// albedo + metal/suavidad + normal (+ emisión), dominio público). Origen en la base: se apoyan en mesas, estantes y suelos.
// Brillo sutil (ItemHighlight) para poder encontrarlos sin que parezcan de arcade.
public static class ItemModels
{
    // escala de juego (1 = tamaño real; un poco mayor en lo más pequeño para que se encuentre)
    public static float Scale(string id)
    {
        switch (id)
        {
            case "Battery": return 1.6f;
            case "Glowstick": return 1.35f;
            case "AlmondWater": return 1.2f;
            case "VhsTape": return 1.2f;
            case "Note": return 1.25f;
            default: return 1f;
        }
    }

    public static string IdFor(ItemType t, bool note)
    {
        if (note) return "Note";
        switch (t)
        {
            case ItemType.Battery: return "Battery";
            case ItemType.AlmondWater: return "AlmondWater";
            case ItemType.Glowstick: return "Glowstick";
            case ItemType.VhsTape: return "VhsTape";
            case ItemType.Flashlight: return "Flashlight";
            default: return "Note";
        }
    }

    private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
    private static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    public static int Built { get; private set; }

    public static bool Available(string id) { return GetMesh(id) != null && GetMat(id) != null; }

    private static Mesh GetMesh(string id)
    {
        if (meshes.TryGetValue(id, out Mesh m) && m != null) return m;
        GameObject go = Resources.Load<GameObject>("Items/" + id);
        MeshFilter mf = go != null ? go.GetComponentInChildren<MeshFilter>() : null;
        m = mf != null ? mf.sharedMesh : null;
        meshes[id] = m;
        return m;
    }

    private static Material GetMat(string id)
    {
        if (materials.TryGetValue(id, out Material m) && m != null) return m;
        m = Resources.Load<Material>("Items/" + id + "_mat");
        materials[id] = m;
        return m;
    }

    // crea el modelo bajo 'parent' (origen en la base). Devuelve null si falta el recurso (se usará el de primitivas).
    public static GameObject Build(string id, Transform parent)
    {
        Mesh mesh = GetMesh(id); Material mat = GetMat(id);
        if (mesh == null || mat == null) return null;
        GameObject go = new GameObject("Model_" + id);
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * Scale(id);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // diminutos: sin sombras (presupuesto)
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        ItemHighlight hl = parent.gameObject.GetComponent<ItemHighlight>();
        if (hl == null) hl = parent.gameObject.AddComponent<ItemHighlight>();
        hl.Add(mr, id == "Glowstick" ? 0.9f : id == "Flashlight" ? 0.6f : 0f);
        Built++;
        return go;
    }
}

// Brillo sutil: un pulso lento de emisión (con la propia textura) cuando el jugador está cerca y lo tiene a la vista.
public class ItemHighlight : MonoBehaviour
{
    public const float Range = 9f, Strength = 0.22f;
    private readonly List<Renderer> rs = new List<Renderer>();
    private readonly List<float> baseEmit = new List<float>();
    private MaterialPropertyBlock mpb;
    private float phase, cur;
    private static readonly int EmId = Shader.PropertyToID("_EmissionColor");
    public float Current { get { return cur; } }

    public void Add(Renderer r, float baseEmission) { rs.Add(r); baseEmit.Add(baseEmission); if (mpb == null) mpb = new MaterialPropertyBlock(); phase = (GetInstanceID() & 255) * 0.1f; Apply(0f); }

    private void Update()
    {
        Camera cam = Camera.main;
        float target = 0f;
        if (cam != null)
        {
            Vector3 to = transform.position - cam.transform.position;
            float d = to.magnitude;
            if (d < Range && Vector3.Dot(cam.transform.forward, to) > 0f)
                target = (1f - d / Range) * (0.55f + 0.45f * Mathf.Sin(Time.time * 2.2f + phase));
        }
        float n = Mathf.MoveTowards(cur, target, Time.deltaTime * 1.5f);
        if (Mathf.Abs(n - cur) > 0.002f || (n == 0f && cur != 0f)) { cur = n; Apply(cur); }
    }

    private void Apply(float k)
    {
        for (int i = 0; i < rs.Count; i++)
        {
            if (rs[i] == null) continue;
            float e = baseEmit[i] + k * Strength;
            rs[i].GetPropertyBlock(mpb);
            mpb.SetColor(EmId, new Color(1f, 0.95f, 0.85f) * e);
            rs[i].SetPropertyBlock(mpb);
        }
    }
}
