using UnityEngine;

// Sonidos del ascensor generados por código (sin ficheros): retumbo grave del descenso, motor y cables, zumbido de presión (tonal),
// frenazo con sacudida, «ding», deslizar de puertas y golpe de cierre.
// Los bucles usan ciclos enteros (sin costura) y todos pasan por AudioMix (Efectos × volumen general).
public static class ElevatorAudio
{
    private const int SR = 22050;

    // Ganancias de la tabla de mezcla (volumen de AudioSource, antes de escalar por Efectos)
    public const float RumbleGain = 0.27f, MachineGain = 0.21f, AirGain = 0.05f, JoltGain = 0.70f, DingGain = 0.45f, DoorGain = 0.42f, ThudGain = 0.6f;

    private static float R(int i, int seed) { return Noise.Hash01(i, 0, seed) * 2f - 1f; }

    private static AudioClip Make(string name, float[] d)
    {
        AudioClip c = AudioClip.Create(name, d.Length, 1, SR, false);
        c.SetData(d, 0);
        return c;
    }

    private static void Normalize(float[] d, float peak)
    {
        float m = 1e-4f;
        for (int i = 0; i < d.Length; i++) m = Mathf.Max(m, Mathf.Abs(d[i]));
        float k = peak / m;
        for (int i = 0; i < d.Length; i++) d[i] *= k;
    }

    // ruido con filtro de un polo, dos pasadas => bucle sin costura
    private static float[] LowNoise(int n, float alpha, int seed)
    {
        float[] d = new float[n];
        float y = 0f;
        for (int p = 0; p < 2; p++)
            for (int i = 0; i < n; i++)
            {
                y += alpha * (R(i, seed) - y);
                if (p == 1) d[i] = y;
            }
        return d;
    }

    // Retumbo grave del descenso (bucle de 2 s): SOLO senos graves de ciclos enteros con modulación de amplitud (v6.5: sin ruido,
    // el ruido grave sonaba a soplo de viento)
    public static AudioClip CreateRumble()
    {
        int n = SR * 2;
        float[] d = new float[n];
        float[] fr = { 26f, 39f, 52f, 65f, 78f, 91f, 104f };
        float[] am = { 0.55f, 0.35f, 0.3f, 0.22f, 0.16f, 0.1f, 0.07f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float v = 0f;
            for (int k = 0; k < fr.Length; k++) v += am[k] * Mathf.Sin(2f * Mathf.PI * fr[k] * t + k * 1.3f) * (0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * (3f + k) * t + k));
            d[i] = v;
        }
        Normalize(d, 0.8f);
        return Make("ElevatorRumble", d);
    }

    // Motor + cables (bucle de 1 s): zumbido del motor con armónicos y chasquidos metálicos periódicos (8 por segundo)
    public static AudioClip CreateMachinery()
    {
        int n = SR;
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float v = 0.5f * Mathf.Sin(2f * Mathf.PI * 112f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 224f * t + 0.5f) + 0.1f * Mathf.Sin(2f * Mathf.PI * 336f * t);
            // chasquidos (cable pasando por la polea)
            float ph = (t * 8f) % 1f;
            float tick = Mathf.Exp(-ph * 38f) * (0.7f * Mathf.Sin(2f * Mathf.PI * 700f * ph / 8f) + 0.5f * R(i, 5));
            d[i] = v * 0.45f + tick * 0.5f;
        }
        Normalize(d, 0.7f);
        return Make("ElevatorMachinery", d);
    }

    // «Aire» del hueco (bucle de 2 s): v6.5, TONAL (zumbido grave de presión con tres senos de ciclos enteros y respiración lenta de
    // amplitud). Antes era ruido filtrado y sonaba a soplo de viento.
    public static AudioClip CreateAir()
    {
        int n = SR * 2;
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float v = Mathf.Sin(2f * Mathf.PI * 84f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 126f * t + 1.1f) + 0.35f * Mathf.Sin(2f * Mathf.PI * 189f * t + 2.2f);
            d[i] = v * (0.7f + 0.3f * Mathf.Sin(2f * Mathf.PI * 1f * t));
        }
        Normalize(d, 0.7f);
        return Make("ElevatorAir", d);
    }

    // Frenazo + sacudida (1,9 s): chirrido metálico descendente que dura 1,1 s y, justo después, el golpe grave de la parada
    // (se reproduce 1,1 s antes de la sacudida visual)
    public const float JoltImpactAt = 1.1f;
    public static AudioClip CreateJolt()
    {
        int n = (int)(SR * 1.9f);
        float[] d = new float[n];
        float[] lo = LowNoise(n, 0.1f, 53);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float f = Mathf.Lerp(1500f, 380f, Mathf.Clamp01(t / JoltImpactAt));
            phase += 2f * Mathf.PI * f / SR;
            float env = t < JoltImpactAt ? Mathf.Clamp01(t * 6f) * (1f - 0.35f * t / JoltImpactAt) : Mathf.Exp(-(t - JoltImpactAt) * 9f);
            float screech = (Mathf.Sin(phase) * 0.5f + Mathf.Sin(phase * 1.51f) * 0.25f + R(i, 3) * 0.12f) * env * 0.4f;
            float t2 = t - JoltImpactAt;
            float boom = t2 >= 0f ? Mathf.Sin(2f * Mathf.PI * (50f - 16f * Mathf.Min(1f, t2 * 2f)) * t2) * Mathf.Exp(-t2 * 4.5f) : 0f;
            float thump = t2 >= 0f ? Mathf.Exp(-t2 * 14f) * lo[i] * 7f : 0f;
            d[i] = boom * 1.1f + screech + thump;
        }
        Normalize(d, 0.8f);
        return Make("ElevatorJolt", d);
    }

    // «Ding»: dos notas de campanilla (1,6 s)
    public static AudioClip CreateDing()
    {
        int n = (int)(SR * 1.6f);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float a = (Mathf.Sin(2f * Mathf.PI * 1318.5f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * 2637f * t)) * Mathf.Exp(-t * 3.6f);
            float t2 = t - 0.32f;
            float b = t2 > 0f ? (Mathf.Sin(2f * Mathf.PI * 987.8f * t2) + 0.3f * Mathf.Sin(2f * Mathf.PI * 1975.6f * t2)) * Mathf.Exp(-t2 * 2.8f) : 0f;
            d[i] = a * Mathf.Clamp01(t * 400f) * 0.7f + b * 0.8f;
        }
        Normalize(d, 0.6f);
        return Make("ElevatorDing", d);
    }

    // Puertas deslizando (1,4 s): barrido de ruido medio + traqueteo metálico + tope final
    public static AudioClip CreateDoorSlide()
    {
        int n = (int)(SR * 1.4f);
        float[] d = new float[n];
        float[] mid = LowNoise(n, 0.22f, 61), low = LowNoise(n, 0.04f, 61);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.15f));
            float swish = (mid[i] - low[i]) * 3f * env;
            float rattle = Mathf.Sin(2f * Mathf.PI * (190f + 30f * Mathf.Sin(t * 17f)) * t) * 0.12f * env * (0.5f + 0.5f * Mathf.Sin(t * 53f));
            float stop = t > 1.15f ? Mathf.Exp(-(t - 1.15f) * 30f) * (R(i, 9) * 0.35f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.4f) : 0f;
            d[i] = swish + rattle + stop;
        }
        Normalize(d, 0.7f);
        return Make("ElevatorDoor", d);
    }

    // Golpe seco de las puertas al cerrarse (0,7 s)
    public static AudioClip CreateThud()
    {
        int n = (int)(SR * 0.7f);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            d[i] = Mathf.Sin(2f * Mathf.PI * (70f - 25f * t) * t) * Mathf.Exp(-t * 9f) + R(i, 77) * Mathf.Exp(-t * 40f) * 0.4f + Mathf.Sin(2f * Mathf.PI * 330f * t) * Mathf.Exp(-t * 18f) * 0.2f;
        }
        Normalize(d, 0.7f);
        return Make("ElevatorThud", d);
    }
}
