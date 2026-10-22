using UnityEngine;
using UnityEngine.Rendering;

// v7.0 Campo infinito: pistas sutiles de la salida (solo la puerta real las tiene; los señuelos y las casas no):
//  - un farol viejo en un poste junto a la puerta, con una bombilla que parpadea débilmente (visible entre la bruma de lejos);
//  - un zumbido eléctrico de red (50 Hz), 3D, audible desde ~80 m.
//    v7.2.0: sin la melodía de la «radio» (nada de música junto a la salida) ni su estática (ruido de banda ancha = siseo).
// Es determinista por semilla salvo el parpadeo (cosmético, local).
public class FieldExitBeacon : MonoBehaviour
{
    public static FieldExitBeacon Active;
    public Light bulbLight;
    public Renderer bulb;
    public AudioSource hum;
    public float baseIntensity = 0.9f;
    private Material bulbMat;
    private float t, nextFlick, flickT;
    public const float HearDistance = 80f;

    public static FieldExitBeacon Build(BackroomsGenerator gen, Transform door, LevelConfig cfg, int seed, System.Action<Object> track)
    {
        GameObject root = new GameObject("ExitBeacon");
        root.transform.SetParent(door, false);
        System.Random r = new System.Random(seed * 7 + 3);
        float side = r.NextDouble() < 0.5 ? -1f : 1f;
        root.transform.localPosition = new Vector3(side * 1.9f, 0f, -0.6f);
        Material pole = ProceduralAssets.CreateMaterial(new Color(0.24f, 0.2f, 0.16f), null, Vector2.one, 0.15f); track(pole);
        Material metal = ProceduralAssets.CreateMaterial(new Color(0.16f, 0.16f, 0.15f), null, Vector2.one, 0.45f); track(metal);
        Material bm = ProceduralAssets.CreateMaterial(new Color(1f, 0.92f, 0.7f), null, Vector2.one, 0.6f); track(bm);
        ProceduralAssets.SetEmission(bm, new Color(2.2f, 1.8f, 1.1f));
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "BeaconPole", root.transform, new Vector3(0f, 1.6f, 0f), new Vector3(0.12f, 1.6f, 0.12f), pole, true);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "BeaconArm", root.transform, new Vector3(-side * 0.3f, 3.1f, 0f), new Vector3(0.6f, 0.05f, 0.05f), metal, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "BeaconShade", root.transform, new Vector3(-side * 0.55f, 3.02f, 0f), new Vector3(0.26f, 0.05f, 0.26f), metal, false);
        GameObject b = ProceduralAssets.CreatePrimitive(PrimitiveType.Sphere, "BeaconBulb", root.transform, new Vector3(-side * 0.55f, 2.93f, 0f), Vector3.one * 0.11f, bm, false);
        b.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        FieldExitBeacon fb = root.AddComponent<FieldExitBeacon>();
        fb.bulb = b.GetComponent<Renderer>(); fb.bulbMat = bm;
        GameObject lg = new GameObject("BeaconLight");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(-side * 0.55f, 2.8f, 0f);
        fb.bulbLight = lg.AddComponent<Light>();
        fb.bulbLight.type = LightType.Point; fb.bulbLight.range = 7f; fb.bulbLight.intensity = fb.baseIntensity;
        fb.bulbLight.color = new Color(1f, 0.85f, 0.6f); fb.bulbLight.shadows = LightShadows.None;
        fb.bulbLight.renderMode = LightRenderMode.ForcePixel;

        AudioClip clip = CreateHum(seed); track(clip);
        fb.hum = root.AddComponent<AudioSource>();
        fb.hum.clip = clip; fb.hum.loop = true; fb.hum.playOnAwake = false;
        fb.hum.spatialBlend = 1f; fb.hum.rolloffMode = AudioRolloffMode.Linear; fb.hum.minDistance = 4f; fb.hum.maxDistance = HearDistance;
        fb.hum.dopplerLevel = 0f; fb.hum.spread = 40f;
        return fb;
    }

    private void OnEnable() { Active = this; }
    private void OnDisable() { if (Active == this) Active = null; }

    private void Start()
    {
        if (hum != null) { hum.volume = AudioMix.A(0.32f); hum.time = Random.value * hum.clip.length; hum.Play(); }
        nextFlick = 2f + Random.value * 4f;
    }

    private void Update()
    {
        float dt = Time.deltaTime; t += dt;
        if (hum != null) hum.volume = AudioMix.A(0.32f);
        nextFlick -= dt;
        if (nextFlick <= 0f) { flickT = 0.15f + Random.value * 0.5f; nextFlick = 3f + Random.value * 7f; }
        float k = 0.85f + 0.15f * Mathf.PerlinNoise(t * 3f, 0.3f);
        if (flickT > 0f) { flickT -= dt; k *= Random.value < 0.5f ? 0.1f : 0.7f; }
        if (bulbLight != null) bulbLight.intensity = baseIntensity * k;
        if (bulbMat != null) bulbMat.SetColor("_EmissionColor", new Color(2.2f, 1.8f, 1.1f) * k);
    }

    // zumbido de red (50 Hz y armónicos). v7.2.0: quitadas la estática de radio (ruido) y la melodía de 8 notas
    // (La3-Si3-Do4-Sol3-La3-Fa3-Sol3-Mi3, una por segundo) que se oía como música cerca de la puerta.
    public const bool HasMelody = false;
    public static AudioClip CreateHum(int seed)
    {
        const int SR = 22050; int n = SR * 8;
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float tt = i / (float)SR;
            float hm = Mathf.Sin(2f * Mathf.PI * 50f * tt) * 0.35f + Mathf.Sin(2f * Mathf.PI * 100f * tt) * 0.25f + Mathf.Sin(2f * Mathf.PI * 150f * tt) * 0.12f;
            d[i] = hm * 0.55f;   // ciclos enteros en 8 s: el bucle no hace clic (sin bajada de volumen periódica)
        }
        float mx = 1e-4f; for (int i = 0; i < n; i++) mx = Mathf.Max(mx, Mathf.Abs(d[i]));
        for (int i = 0; i < n; i++) d[i] *= 0.7f / mx;
        AudioClip c = AudioClip.Create("FieldExitHum", n, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }
}
