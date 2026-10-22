using System.Collections.Generic;
using UnityEngine;

// v7.3.0: bengala de la pistola de señales. Sube ~40 m, se abre un paracaídas y baja despacio ardiendo en rojo ~25 s.
// Se ve a través de la niebla (brillo con niebla propia) e ilumina el agua alrededor. Sincronizada: la ven todos.
public class OceanFlare : MonoBehaviour
{
    public const float BurnTime = 25f;
    public static readonly List<OceanFlare> All = new List<OceanFlare>();
    public static int Fired { get; private set; }
    public Vector3 vel;
    public float age;
    public int owner;
    private Light li;
    private Material glow;
    public float MaxHeight { get; private set; }

    public static bool TryFire(Transform from, out string msg)
    {
        if (OceanWorld.Active == null) { msg = "Aquí no tiene sentido disparar una bengala"; return false; }
        Vector3 dir = from.forward; if (dir.y < 0.35f) { dir.y = 0.35f; } dir = (dir + Vector3.up * 1.4f).normalized;
        Vector3 o = from.position + from.forward * 0.5f;
        OceanNet.Request(OceanNet.OpFlare, 0, 0f, 0f, o, dir);
        msg = "¡Bengala disparada! Tus compañeros la verán sobre la niebla";
        return true;
    }

    public static OceanFlare Spawn(int owner, Vector3 origin, Vector3 dir)
    {
        OceanWorld w = OceanWorld.Active;
        GameObject g = new GameObject("Flare"); g.transform.SetParent(w != null ? w.root : null, false);
        g.transform.position = origin;
        OceanFlare f = g.AddComponent<OceanFlare>();
        f.owner = owner; f.vel = dir.normalized * 30f; f.MaxHeight = origin.y;
        f.glow = OceanGeo.Unlit("Ocean/Glow", new Color(1f, 0.2f, 0.12f), o => { });
        f.glow.SetFloat("_Intensity", 3f); f.glow.SetFloat("_Fog", 0.0025f);
        GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
        q.transform.SetParent(g.transform, false); q.transform.localScale = Vector3.one * 5f; q.GetComponent<Renderer>().sharedMaterial = f.glow; q.AddComponent<OceanBillboard>();
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(c.GetComponent<Collider>());
        c.transform.SetParent(g.transform, false); c.transform.localScale = Vector3.one * 0.9f; c.GetComponent<Renderer>().sharedMaterial = f.glow; c.AddComponent<OceanBillboard>();
        f.li = g.AddComponent<Light>(); f.li.type = LightType.Point; f.li.color = new Color(1f, 0.25f, 0.15f); f.li.range = 45f; f.li.intensity = 3f; f.li.shadows = LightShadows.None;
        All.Add(f); Fired++;
        AudioClip pop = OceanAudio.ClankClip();
        AudioSource.PlayClipAtPoint(pop, origin, 0.5f * AudioMix.Sfx);
        Object.Destroy(pop, 2f);
        return f;
    }

    private void OnDestroy() { All.Remove(this); if (glow != null) Destroy(glow); }

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (vel.y > -1.2f) vel += Vector3.down * 9f * dt;   // sube y frena
        else vel = Vector3.Lerp(vel, new Vector3(0.4f, -1.2f, 0.2f), 1f - Mathf.Exp(-2f * dt));   // paracaídas
        transform.position += vel * dt;
        MaxHeight = Mathf.Max(MaxHeight, transform.position.y);
        float flick = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 12f, owner);
        float fade = Mathf.Clamp01((BurnTime - age) / 3f);
        li.intensity = 3f * flick * fade;
        glow.SetFloat("_Intensity", 3f * flick * fade);
        if (age > BurnTime || transform.position.y < -0.2f) Destroy(gameObject);
    }
}
