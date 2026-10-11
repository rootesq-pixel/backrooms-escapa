using System.Threading.Tasks;
using UnityEngine;

// Ruido procedural en CPU (hilos seguros, sin usar la API de Unity salvo Mathf):
// - Hash entero (ruido de valor / ruido por píxel).
// - Perlin periódico (tileable) y fBm periódico sobre una rejilla cuadrada.
// Todo es determinista para una semilla dada.
public static class Noise
{
    private static readonly float[] CosTable = new float[256];
    private static readonly float[] SinTable = new float[256];

    static Noise()
    {
        for (int i = 0; i < 256; i++)
        {
            float a = i * (Mathf.PI * 2f / 256f);
            CosTable[i] = Mathf.Cos(a);
            SinTable[i] = Mathf.Sin(a);
        }
    }

    // Hash de 32 bits a partir de dos enteros y una semilla
    public static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 1274126177u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            h *= 2246822519u;
            h ^= h >> 13;
            return h;
        }
    }

    // Número 0..1 por celda (ruido de valor sin interpolar)
    public static float Hash01(int x, int y, int seed)
    {
        return (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;
    }

    public static float Fade(float t)
    {
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }

    public static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    private static int Mod(int a, int m)
    {
        int r = a % m;
        return r < 0 ? r + m : r;
    }

    private static float Grad(int ix, int iy, int seed, float dx, float dy)
    {
        int idx = (int)(Hash(ix, iy, seed) & 255u);
        return CosTable[idx] * dx + SinTable[idx] * dy;
    }

    // Perlin 2D periódico: el dominio es [0,px) x [0,py)
    public static float Perlin(float x, float y, int px, int py, int seed)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = x - x0;
        float fy = y - y0;
        int xa = Mod(x0, px), xb = Mod(x0 + 1, px);
        int ya = Mod(y0, py), yb = Mod(y0 + 1, py);
        float n00 = Grad(xa, ya, seed, fx, fy);
        float n10 = Grad(xb, ya, seed, fx - 1f, fy);
        float n01 = Grad(xa, yb, seed, fx, fy - 1f);
        float n11 = Grad(xb, yb, seed, fx - 1f, fy - 1f);
        float u = Fade(fx), v = Fade(fy);
        return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v);
    }

    // fBm periódico: campo res x res con valores normalizados a 0..1 (tileable).
    // cx, cy = celdas de la rejilla del primer octavo; cada octavo duplica la frecuencia.
    public static float[] Fbm(int res, int cx, int cy, int octaves, float persistence, int seed)
    {
        float[] f = new float[res * res];
        Parallel.For(0, res, j =>
        {
            for (int i = 0; i < res; i++)
            {
                float amp = 1f, sum = 0f, norm = 0f;
                int px = cx, py = cy;
                float u = i / (float)res, v = j / (float)res;
                for (int o = 0; o < octaves; o++)
                {
                    sum += amp * Perlin(u * px, v * py, px, py, seed + o * 31);
                    norm += amp;
                    amp *= persistence;
                    px <<= 1;
                    py <<= 1;
                }
                f[j * res + i] = sum / norm;
            }
        });

        // Normalizar a 0..1 con el rango real (así los umbrales son predecibles)
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < f.Length; i++)
        {
            if (f[i] < min) min = f[i];
            if (f[i] > max) max = f[i];
        }
        float inv = max > min ? 1f / (max - min) : 1f;
        for (int i = 0; i < f.Length; i++) f[i] = (f[i] - min) * inv;
        return f;
    }

    // Muestreo bilineal con repetición (u, v en unidades de textura 0..1)
    // (el parámetro res se ignora: la resolución del campo se deduce de su tamaño, así da igual la del lienzo)
    public static float Sample(float[] f, int unusedCanvasRes, float u, float v)
    {
        int res = f.Length == 65536 ? 256 : (f.Length == 262144 ? 512 : Mathf.RoundToInt(Mathf.Sqrt(f.Length)));
        u -= Mathf.Floor(u);
        v -= Mathf.Floor(v);
        float x = u * res, y = v * res;
        int x0 = (int)x, y0 = (int)y;
        if (x0 >= res) x0 = res - 1;
        if (y0 >= res) y0 = res - 1;
        float fx = x - x0, fy = y - y0;
        int x1 = x0 + 1 == res ? 0 : x0 + 1;
        int y1 = y0 + 1 == res ? 0 : y0 + 1;
        float a = f[y0 * res + x0] + (f[y0 * res + x1] - f[y0 * res + x0]) * fx;
        float b = f[y1 * res + x0] + (f[y1 * res + x1] - f[y1 * res + x0]) * fx;
        return a + (b - a) * fy;
    }

    // Normal map (RGBA32, alfa = 255) a partir de un mapa de alturas (con repetición).
    // Codificación RGB estándar: Standard.shader la lee bien con alfa = 1.
    public static Color32[] NormalFromHeight(float[] h, int res, float strength)
    {
        Color32[] n = new Color32[res * res];
        Parallel.For(0, res, y =>
        {
            int ym = (y - 1 + res) % res, yp = (y + 1) % res;
            for (int x = 0; x < res; x++)
            {
                int xm = (x - 1 + res) % res, xp = (x + 1) % res;
                float dx = (h[y * res + xp] - h[y * res + xm]) * strength;
                float dy = (h[yp * res + x] - h[ym * res + x]) * strength;
                float nx = -dx, ny = -dy, nz = 1f;
                float inv = 1f / Mathf.Sqrt(nx * nx + ny * ny + nz * nz);
                n[y * res + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt((nx * inv * 0.5f + 0.5f) * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt((ny * inv * 0.5f + 0.5f) * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt((nz * inv * 0.5f + 0.5f) * 255f), 0, 255),
                    255);
            }
        });
        return n;
    }
}
