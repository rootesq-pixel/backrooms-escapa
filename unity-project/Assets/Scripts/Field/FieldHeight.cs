using UnityEngine;

// v7.0 Campo infinito: altura del terreno (colinas suaves). Es PERIÓDICA (periodo = tamaño del campo L) para que el
// campo se repita sin costuras al dar la vuelta (el jugador reaparece por el lado opuesto). Solo depende de la semilla:
// todos los equipos del multijugador calculan exactamente la misma altura.
public static class FieldHeight
{
    public static bool Active;
    public static float L = 264f;
    private static Vector2 start;
    private static float raw0;
    private static readonly int[] KX = { 1, 0, 1, 1, 2, 1, 3, 2, 4, 5, 6, 7 };
    private static readonly int[] KZ = { 0, 1, 1, -1, 1, -2, 1, 3, -1, 3, -4, 2 };
    private static readonly float[] AMP = { 1.7f, 1.5f, 1.05f, 0.9f, 0.55f, 0.5f, 0.32f, 0.26f, 0.18f, 0.11f, 0.07f, 0.06f };
    private static readonly float[] ph = new float[12];
    private static readonly float[] sg = new float[12];

    public static void Init(int seed, float period, Vector2 startXZ)
    {
        L = period; start = startXZ; Active = true;
        System.Random r = new System.Random(seed * 7919 + 3);
        for (int i = 0; i < ph.Length; i++) { ph[i] = (float)(r.NextDouble() * Mathf.PI * 2.0); sg[i] = r.NextDouble() < 0.5 ? -1f : 1f; }
        raw0 = 0f; raw0 = Raw(start.x, start.y);
    }

    public static void Reset() { Active = false; Custom = null; }

    // v7.7.0: otro mundo periódico (el Desierto Cálido) reutiliza la vuelta, las copias del borde y la altura del suelo
    // de todo el juego (FloorHeightLocal, FieldWrap, WrapView) con su propia función de altura.
    public static System.Func<float, float, float> Custom;
    public static void InitCustom(float period, System.Func<float, float, float> h) { L = period; Custom = h; Active = true; }

    private static float Raw(float x, float z)
    {
        float s = 0f, k = 2f * Mathf.PI / L;
        for (int i = 0; i < KX.Length; i++) s += sg[i] * AMP[i] * Mathf.Sin(k * (KX[i] * x + KZ[i] * z) + ph[i]);
        return s;
    }

    public static float Wrap(float d) { return d - L * Mathf.Round(d / L); }
    public static float Mod(float v) { v %= L; return v < 0f ? v + L : v; }

    // distancia (con vuelta) entre dos puntos XZ locales
    public static float WrappedDist(float x0, float z0, float x1, float z1)
    {
        float dx = Wrap(x1 - x0), dz = Wrap(z1 - z0);
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // altura local (y) del suelo en (x, z) locales del nivel
    public static float H(float x, float z)
    {
        if (!Active) return 0f;
        if (Custom != null) return Custom(x, z);
        float d = WrappedDist(start.x, start.y, x, z);
        float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(7f, 28f, d));   // llano alrededor del ascensor
        return (Raw(x, z) - raw0) * f;
    }

    public static Vector3 Normal(float x, float z)
    {
        const float e = 0.5f;
        float hx = H(x + e, z) - H(x - e, z), hz = H(x, z + e) - H(x, z - e);
        return new Vector3(-hx, 2f * e, -hz).normalized;
    }
}
