using UnityEngine;

// v7.3.0: sonda de reflejos pequeña que acompaña al casco de un compañero (cubemap de superficie o de fondo según la altura)
public class HelmetReflection : MonoBehaviour
{
    public Cubemap air, water;
    private ReflectionProbe probe;
    private void Start()
    {
        probe = new GameObject("HelmetProbe").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(transform, false);
        probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Custom;
        probe.boxProjection = false; probe.importance = 3; probe.intensity = 1f;
        probe.size = Vector3.one * 2.5f;
        probe.customBakedTexture = air;
    }
    private void LateUpdate()
    {
        if (probe == null) return;
        Cubemap want = transform.position.y < -0.6f ? water : air;
        if (probe.customBakedTexture != want) probe.customBakedTexture = want;
        probe.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, transform.lossyScale.x), 1f / Mathf.Max(0.0001f, transform.lossyScale.y), 1f / Mathf.Max(0.0001f, transform.lossyScale.z));
    }
}
