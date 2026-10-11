using System.Collections.Generic;
using UnityEngine;

// Barra luminosa: se lanza o se deja en el suelo y alumbra una zona pequeña con luz verde (~2 min, se desvanece al final).
public class Glowstick : MonoBehaviour
{
    public const int MaxActive = 6;
    public static readonly List<Glowstick> Active = new List<Glowstick>();

    public float life = 130f;
    public float fadeTime = 25f;
    public Light glow;
    public float Age { get; private set; }

    private Material mat;
    private float flickerSeed;
    private static readonly Color Green = new Color(0.35f, 1f, 0.3f);

    public static Glowstick Spawn(Vector3 position, Vector3 velocity, Collider ignore)
    {
        while (Active.Count >= MaxActive)
        {
            Glowstick old = Active[0];
            Active.RemoveAt(0);
            if (old != null) Destroy(old.gameObject);
        }

        GameObject go = new GameObject("Glowstick");
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(0f, 0f, 90f);

        Material m = ProceduralAssets.CreateMaterial(new Color(0.25f, 0.8f, 0.2f), null, Vector2.one, 0.6f);
        ProceduralAssets.SetEmission(m, Green * 2.2f);
        GameObject body = ProceduralAssets.CreatePrimitive(PrimitiveType.Capsule, "Body", go.transform, Vector3.zero, new Vector3(0.022f, 0.065f, 0.022f), m, false);
        body.transform.localRotation = Quaternion.identity;

        CapsuleCollider cc = go.AddComponent<CapsuleCollider>();
        cc.direction = 1; cc.radius = 0.011f; cc.height = 0.13f;
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.mass = 0.08f; rb.drag = 0.15f; rb.angularDrag = 0.4f;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.velocity = velocity;
        rb.angularVelocity = new Vector3(Random.Range(-6f, 6f), Random.Range(-6f, 6f), Random.Range(-6f, 6f));
        if (ignore != null) Physics.IgnoreCollision(cc, ignore);

        GameObject lg = new GameObject("GlowLight");
        lg.transform.SetParent(go.transform, false);
        lg.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        Light l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 7.5f; l.intensity = 1.5f; l.color = Green;
        l.shadows = LightShadows.None;

        Glowstick g = go.AddComponent<Glowstick>();
        g.glow = l;
        g.mat = m;
        g.flickerSeed = Random.value * 10f;
        Active.Add(g);
        return g;
    }

    private void Update()
    {
        Age += Time.deltaTime;
        float fade = Age > life - fadeTime ? Mathf.Clamp01((life - Age) / fadeTime) : 1f;
        float fl = 0.94f + 0.06f * Mathf.PerlinNoise(Time.time * 6f, flickerSeed);
        if (glow != null) glow.intensity = 1.5f * fade * fl;
        if (mat != null) ProceduralAssets.SetEmission(mat, Green * (2.2f * fade * fl));
        if (Age >= life) Destroy(gameObject);
    }

    private void OnDestroy()
    {
        Active.Remove(this);
        ProceduralAssets.SafeDestroy(mat);
    }

    public static void ClearAll()
    {
        for (int i = Active.Count - 1; i >= 0; i--) if (Active[i] != null) Destroy(Active[i].gameObject);
        Active.Clear();
    }
}
