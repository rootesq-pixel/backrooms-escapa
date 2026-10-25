using System.Collections.Generic;
using UnityEngine;

// v7.7.0 Nivel 114 (El Desierto Cálido): altura de las dunas. PERIÓDICA con periodo L (el desierto se repite sin costuras,
// como el Campo infinito) y solo depende de la semilla: todos los equipos del multijugador calculan la misma altura.
//  · dunas transversales con perfil asimétrico (barlovento largo y suave, cresta afilada, cara de avalancha de ~30°),
//    con la fase deformada (crestas sinuosas que se bifurcan) y la amplitud modulada (hay pasillos llanos entre dunas);
//  · un segundo tren de dunas más pequeñas cruzado y una ondulación suave;
//  · llanos (explanadas) alrededor del ascensor y de cada aldea, fundidos con suavidad.
// Todas las ondas tienen números de onda enteros (k = 2π·n/L): la función es exactamente periódica.
public static class DesertHeight
{
    public static float L = 512f;
    public struct Pad { public Vector2 c; public float r0, r1, h; }
    public static readonly List<Pad> Pads = new List<Pad>();
    private struct Wave { public float kx, kz, amp, ph; }
    private static Wave[] warpA, warpB, ampW, swell;
    private static float k1x, k1z, k2x, k2z, ph1, ph2;
    public const float DuneAmp = 7.5f, SmallAmp = 1.6f;

    public static void Init(int seed, float period)
    {
        L = period; Pads.Clear();
        System.Random r = new System.Random(seed * 6151 + 29);
        float k = 2f * Mathf.PI / L;
        // dunas principales: ~73 m entre crestas (7 crestas en 512 m), crestas casi perpendiculares al viento (+X)
        int n1 = 7, m1 = r.Next(-1, 2);
        k1x = k * n1; k1z = k * m1; ph1 = (float)(r.NextDouble() * 6.283);
        int n2 = 3 + r.Next(2), m2 = 17 + r.Next(3);   // dunas pequeñas cruzadas (~28 m)
        k2x = k * n2; k2z = k * m2 * (r.NextDouble() < 0.5 ? -1f : 1f); ph2 = (float)(r.NextDouble() * 6.283);
        warpA = Waves(r, k, new[] { new Vector2Int(0, 1), new Vector2Int(1, 2), new Vector2Int(2, -1), new Vector2Int(0, 3), new Vector2Int(3, 4) }, new[] { 1.5f, 0.9f, 0.6f, 0.45f, 0.25f });
        warpB = Waves(r, k, new[] { new Vector2Int(1, 0), new Vector2Int(2, 3), new Vector2Int(-3, 2), new Vector2Int(4, 1) }, new[] { 1.1f, 0.6f, 0.4f, 0.3f });
        ampW = Waves(r, k, new[] { new Vector2Int(1, 1), new Vector2Int(2, -1), new Vector2Int(0, 2), new Vector2Int(3, 1), new Vector2Int(1, -3) }, new[] { 0.42f, 0.32f, 0.26f, 0.18f, 0.12f });
        swell = Waves(r, k, new[] { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, -1), new Vector2Int(2, 1) }, new[] { 1.6f, 1.3f, 0.9f, 0.6f });
    }

    private static Wave[] Waves(System.Random r, float k, Vector2Int[] n, float[] a)
    {
        Wave[] w = new Wave[n.Length];
        for (int i = 0; i < n.Length; i++) w[i] = new Wave { kx = k * n[i].x, kz = k * n[i].y, amp = a[i], ph = (float)(r.NextDouble() * 6.283) };
        return w;
    }
    private static float Sum(Wave[] w, float x, float z) { float s = 0f; for (int i = 0; i < w.Length; i++) s += w[i].amp * Mathf.Sin(w[i].kx * x + w[i].kz * z + w[i].ph); return s; }

    // perfil de duna en t ∈ [0,1): sube despacio (barlovento) hasta la cresta en t = a y cae deprisa (cara de avalancha)
    public static float Profile(float t)
    {
        const float a = 0.80f;
        t -= Mathf.Floor(t);
        if (t < a) { float u = t / a; return u * u * (1.6f - 0.6f * u); }            // barlovento convexo hacia la cresta
        float v = (t - a) / (1f - a); return 1f - v * v * (3f - 2f * v) * 1.0f;       // sotavento (pendiente máxima ~32°)
    }

    // altura «natural» de las dunas, sin explanadas
    public static float Raw(float x, float z)
    {
        float w1 = Sum(warpA, x, z), w2 = Sum(warpB, x, z);
        float amp = Mathf.Clamp01(0.62f + Sum(ampW, x, z));                       // pasillos llanos entre campos de dunas
        float t1 = (k1x * x + k1z * z + ph1 + w1) / (2f * Mathf.PI);
        float t2 = (k2x * x + k2z * z + ph2 + w2 * 0.7f) / (2f * Mathf.PI);
        float big = Profile(t1) * DuneAmp * amp * amp;
        float small = Profile(t2) * SmallAmp * (0.4f + 0.6f * (1f - amp));
        return big + small + Sum(swell, x, z);
    }

    public static float Wrap(float d) { return d - L * Mathf.Round(d / L); }

    // explanada: radio r0 llano a la altura h, fundido con suavidad hasta r1
    public static void AddPad(Vector2 c, float r0, float r1, float h) { Pads.Add(new Pad { c = c, r0 = r0, r1 = r1, h = h }); }

    // altura media de la duna alrededor de c (para nivelar una explanada sin que quede en un hoyo ni en una cresta)
    public static float MeanAround(Vector2 c, float rad)
    {
        float s = 0f; int n = 0;
        for (int i = -3; i <= 3; i++) for (int j = -3; j <= 3; j++) { s += Raw(c.x + i * rad / 3f, c.y + j * rad / 3f); n++; }
        return s / n;
    }

    public static float H(float x, float z)
    {
        float h = Raw(x, z);
        for (int i = 0; i < Pads.Count; i++)
        {
            Pad p = Pads[i];
            float dx = Wrap(x - p.c.x), dz = Wrap(z - p.c.y);
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d >= p.r1) continue;
            float f = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(p.r0, p.r1, d));
            h = Mathf.Lerp(h, p.h, f);
        }
        return h;
    }

    public static Vector3 Normal(float x, float z)
    {
        const float e = 0.4f;
        float hx = H(x + e, z) - H(x - e, z), hz = H(x, z + e) - H(x, z - e);
        return new Vector3(-hx, 2f * e, -hz).normalized;
    }
}
