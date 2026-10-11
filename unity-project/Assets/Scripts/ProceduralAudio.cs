using UnityEngine;

// Audio generado por código (sin ficheros): zumbidos, drones, gotas, crujidos, golpes metálicos y pasos.
public static class ProceduralAudio
{
    private const int SR = 22050;

    private static AudioClip Make(string name, float[] data)
    {
        AudioClip clip = AudioClip.Create(name, data.Length, 1, SR, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static void Normalize(float[] d, float peak)
    {
        float max = 0.0001f;
        for (int i = 0; i < d.Length; i++) { float a = Mathf.Abs(d[i]); if (a > max) max = a; }
        float k = peak / max;
        for (int i = 0; i < d.Length; i++) d[i] *= k;
    }

    // Ruido blanco filtrado con un polo (alpha pequeño = más grave). circular = deja el bucle sin costura.
    private static float[] LowNoise(int n, float alpha, int seed, bool circular)
    {
        float[] d = new float[n];
        float y = 0f;
        int passes = circular ? 2 : 1;
        for (int p = 0; p < passes; p++)
        {
            for (int i = 0; i < n; i++)
            {
                float w = Noise.Hash01(i, p == 0 ? 0 : 0, seed) * 2f - 1f;   // mismo ruido en ambas pasadas
                y += alpha * (w - y);
                if (p == passes - 1) d[i] = y;
            }
        }
        return d;
    }

    // Zumbido de fluorescente: armónicos enteros en 1 segundo => bucle sin cortes
    public static AudioClip CreateBuzzClip()
    {
        float[] data = new float[SR];
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SR;
            float v = 0.50f * Mathf.Sin(2f * Mathf.PI * 100f * t)
                    + 0.25f * Mathf.Sin(2f * Mathf.PI * 200f * t)
                    + 0.12f * Mathf.Sin(2f * Mathf.PI * 300f * t)
                    + 0.06f * Mathf.Sin(2f * Mathf.PI * 700f * t);
            data[i] = v * 0.6f;
        }
        return Make("FluorescentBuzz", data);
    }

    // Drone ambiental (bucle de 8 s): graves con ciclos enteros + ruido grave modulado lentamente
    public static AudioClip CreateDrone(float baseHz, float noiseAmount, int seed)
    {
        const float seconds = 8f;
        int n = (int)(SR * seconds);
        float[] d = new float[n];
        float[] noise = LowNoise(n, 0.04f, seed, true);
        float[] noise2 = LowNoise(n, 0.4f, seed + 7, true);
        float[] mult = { 1f, 2f, 3f, 5f };
        float[] amp = { 1f, 0.5f, 0.28f, 0.1f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float v = 0f;
            for (int h = 0; h < mult.Length; h++)
            {
                float f = Mathf.Round(baseHz * mult[h] * seconds) / seconds;      // ciclos enteros
                v += amp[h] * Mathf.Sin(2f * Mathf.PI * f * t + h);
            }
            float lfo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * t / seconds * 1f);   // 2 ciclos por bucle
            d[i] = v * 0.25f * lfo + noise[i] * 6f * noiseAmount * (0.6f + 0.4f * lfo) + noise2[i] * 0.4f * noiseAmount * 0.3f;
        }
        Normalize(d, 0.8f);
        return Make("Drone", d);
    }

    public static AudioClip CreateDrip()
    {
        int n = (int)(SR * 0.7f);
        float[] d = new float[n];
        for (int echo = 0; echo < 2; echo++)
        {
            int offset = echo == 0 ? 0 : (int)(SR * 0.23f);
            float gain = echo == 0 ? 1f : 0.3f;
            float phase = 0f;
            for (int i = 0; i + offset < n; i++)
            {
                float t = i / (float)SR;
                float f = 700f + 1600f * (1f - Mathf.Exp(-t * 45f));
                phase += 2f * Mathf.PI * f / SR;
                float env = Mathf.Exp(-t * 32f) * Mathf.Min(1f, t * 600f);
                d[i + offset] += Mathf.Sin(phase) * env * gain;
            }
        }
        Normalize(d, 0.7f);
        return Make("Drip", d);
    }

    public static AudioClip CreateCreak()
    {
        float len = 1.5f;
        int n = (int)(SR * len);
        float[] d = new float[n];
        float[] nz = LowNoise(n, 0.2f, 91, false);
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float f = 75f + 55f * Mathf.Sin(2f * Mathf.PI * 1.1f * t) + 40f * (t / len) + nz[i] * 120f;
            phase += f / SR;
            float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
            float env = Mathf.Sin(Mathf.PI * t / len);
            env *= env;
            d[i] = (saw * 0.5f + Mathf.Sin(2f * Mathf.PI * phase) * 0.5f + nz[i] * 3f) * env;
        }
        Normalize(d, 0.6f);
        return Make("Creak", d);
    }

    public static AudioClip CreateClank()
    {
        int n = (int)(SR * 1.6f);
        float[] d = new float[n];
        float[] freq = { 187f, 311f, 459f, 677f, 1170f, 1630f };
        float[] amp = { 1f, 0.7f, 0.5f, 0.35f, 0.2f, 0.12f };
        float[] dec = { 3.5f, 4.5f, 6f, 8f, 11f, 14f };
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float v = 0f;
            for (int k = 0; k < freq.Length; k++) v += amp[k] * Mathf.Sin(2f * Mathf.PI * freq[k] * t) * Mathf.Exp(-t * dec[k]);
            if (t < 0.01f) v += (Noise.Hash01(i, 3, 5) * 2f - 1f) * (1f - t / 0.01f) * 1.5f;
            d[i] = v;
        }
        Normalize(d, 0.7f);
        return Make("Clank", d);
    }

    public static AudioClip CreateFootstep(StepKind kind)
    {
        int n = (int)(SR * 0.32f);
        float[] d = new float[n];
        float alpha, decay, gain = 1f;
        switch (kind)
        {
            case StepKind.Carpet: alpha = 0.07f; decay = 20f; gain = 0.9f; break;
            case StepKind.Concrete: alpha = 0.35f; decay = 28f; break;
            case StepKind.Water: alpha = 0.18f; decay = 10f; break;
            default: alpha = 0.4f; decay = 32f; break;
        }
        float lp = 0f, lp2 = 0f;
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float w = Noise.Hash01(i, 11, (int)kind + 100) * 2f - 1f;
            lp += alpha * (w - lp);
            lp2 += 0.02f * (w - lp2);
            float s = kind == StepKind.Water ? (lp - lp2 * 3f) : lp * 2f;
            float env = Mathf.Min(1f, t * 400f) * Mathf.Exp(-t * decay);
            float v = s * env * gain;
            if (kind == StepKind.Water)
            {
                float f = 350f + 900f * Mathf.Min(1f, t * 12f);
                phase += 2f * Mathf.PI * f / SR;
                v += Mathf.Sin(phase) * Mathf.Exp(-t * 30f) * 0.25f * Mathf.Min(1f, t * 80f);
            }
            if (kind == StepKind.Metal)
            {
                v += (Mathf.Sin(2f * Mathf.PI * 480f * t) * 0.15f + Mathf.Sin(2f * Mathf.PI * 1130f * t) * 0.08f) * Mathf.Exp(-t * 16f);
            }
            if (kind == StepKind.Concrete && t < 0.004f) v += 0.5f * (1f - t / 0.004f);
            d[i] = v;
        }
        Normalize(d, 0.6f);
        return Make("Step_" + kind, d);
    }

    public static AudioClip CreatePickup()
    {
        int n = (int)(SR * 0.4f);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float a = Mathf.Sin(2f * Mathf.PI * 880f * t) * Mathf.Exp(-t * 14f);
            float b = t > 0.08f ? Mathf.Sin(2f * Mathf.PI * 1320f * (t - 0.08f)) * Mathf.Exp(-(t - 0.08f) * 10f) : 0f;
            d[i] = (a + b) * 0.5f;
        }
        return Make("Pickup", d);
    }

    // ------------------------------------------------------------------
    // Monstruos, sustos, respiración, interferencia de cinta
    // ------------------------------------------------------------------

    private static float Sat(float x) { return x / (1f + Mathf.Abs(x)); }

    // Gruñido de monstruo (sin ficheros): distinto timbre según el tipo
    public static AudioClip CreateGrowl(GrowlKind kind, int seed)
    {
        float len = kind == GrowlKind.Hiss ? 1.2f : (kind == GrowlKind.Metal ? 1.0f : 1.6f);
        int n = (int)(SR * len);
        float[] d = new float[n];
        float[] nz = LowNoise(n, 0.5f, seed, false);
        float[] nzLow = LowNoise(n, 0.05f, seed + 3, false);
        float phase = 0f, phase2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR, u = t / len;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(u)); env *= env;
            float v = 0f;
            switch (kind)
            {
                case GrowlKind.LowRumble:
                    {
                        float f = 38f + 18f * Mathf.Sin(2f * Mathf.PI * 0.9f * t) + nzLow[i] * 60f;
                        phase += f / SR; phase2 += f * 1.5f / SR;
                        v = Sat(Mathf.Sin(2f * Mathf.PI * phase) * 2.2f) * 0.8f + Mathf.Sin(2f * Mathf.PI * phase2) * 0.25f + nzLow[i] * 3f;
                        break;
                    }
                case GrowlKind.Rasp:
                    {
                        float f = 95f + 40f * (1f - u) + 12f * Mathf.Sin(2f * Mathf.PI * 6f * t);
                        phase += f / SR;
                        float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                        v = saw * 0.45f * (0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 28f * t)) + nz[i] * 0.6f;
                        break;
                    }
                case GrowlKind.Metal:
                    {
                        float f = 140f + 260f * u;
                        phase += f / SR;
                        float sq = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * phase));
                        v = sq * 0.25f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 42f * t)) + Mathf.Sin(2f * Mathf.PI * 1210f * t) * 0.12f + nz[i] * 0.3f;
                        break;
                    }
                case GrowlKind.Hiss:
                    {
                        v = nz[i] * 1.2f * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 14f * t)) + Mathf.Sin(2f * Mathf.PI * (3200f + 400f * Mathf.Sin(t * 30f)) * t) * 0.05f;
                        break;
                    }
                default: // Moan
                    {
                        float f = 120f + 90f * Mathf.Sin(Mathf.PI * u) + 8f * Mathf.Sin(2f * Mathf.PI * 5.5f * t);
                        phase += f / SR;
                        v = Mathf.Sin(2f * Mathf.PI * phase) * 0.5f + Mathf.Sin(2f * Mathf.PI * phase * 2.01f) * 0.3f + Mathf.Sin(2f * Mathf.PI * phase * 3.02f) * 0.18f + nz[i] * 0.18f;
                        break;
                    }
            }
            d[i] = v * env;
        }
        Normalize(d, 0.85f);
        return Make("Growl_" + kind, d);
    }

    // Grito del susto al ser atrapado: ruido + chirrido ascendente muy saturado
    public static AudioClip CreateScream(GrowlKind kind, int seed)
    {
        float len = 1.4f;
        int n = (int)(SR * len);
        float[] d = new float[n];
        float[] nz = LowNoise(n, 0.7f, seed, false);
        float phase = 0f, phase2 = 0f;
        float basef = kind == GrowlKind.LowRumble ? 90f : (kind == GrowlKind.Metal ? 320f : (kind == GrowlKind.Hiss ? 700f : 420f));
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR, u = t / len;
            float env = Mathf.Min(1f, t * 60f) * Mathf.Exp(-u * 2.2f);
            float f = basef * (1f + 1.2f * Mathf.Min(1f, u * 4f)) * (1f + 0.04f * Mathf.Sin(2f * Mathf.PI * 11f * t));
            phase += f / SR; phase2 += f * 1.51f / SR;
            float v = Sat(Mathf.Sin(2f * Mathf.PI * phase) * 3f) + 0.6f * Sat(Mathf.Sin(2f * Mathf.PI * phase2) * 3f) + nz[i] * 0.7f;
            d[i] = v * env;
        }
        Normalize(d, 0.95f);
        return Make("Scream_" + kind, d);
    }

    // Paso de monstruo según su tipo
    public static AudioClip CreateMonsterStep(GrowlKind kind, int seed)
    {
        int n = (int)(SR * 0.4f);
        float[] d = new float[n];
        float lp = 0f;
        float alpha = kind == GrowlKind.LowRumble ? 0.04f : (kind == GrowlKind.Metal ? 0.5f : (kind == GrowlKind.Hiss ? 0.6f : 0.12f));
        float decay = kind == GrowlKind.LowRumble ? 9f : (kind == GrowlKind.Metal ? 14f : (kind == GrowlKind.Hiss ? 40f : 18f));
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float w = Noise.Hash01(i, 5, seed) * 2f - 1f;
            lp += alpha * (w - lp);
            float v = lp * 3f * Mathf.Exp(-t * decay);
            if (kind == GrowlKind.LowRumble) v += Mathf.Sin(2f * Mathf.PI * 48f * t) * Mathf.Exp(-t * 10f) * 1.2f;
            if (kind == GrowlKind.Metal) v += (Mathf.Sin(2f * Mathf.PI * 310f * t) * 0.4f + Mathf.Sin(2f * Mathf.PI * 770f * t) * 0.25f) * Mathf.Exp(-t * 11f);
            d[i] = v * Mathf.Min(1f, t * 500f);
        }
        Normalize(d, 0.8f);
        return Make("MStep_" + kind, d);
    }

    // Respiración (bucle ~2.4 s): ruido filtrado con envolvente inhalar/exhalar
    public static AudioClip CreateBreath()
    {
        float len = 2.4f;
        int n = (int)(SR * len);
        float[] d = new float[n];
        float lp = 0f, lp2 = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR, u = t / len;
            float w = Noise.Hash01(i, 21, 77) * 2f - 1f;
            lp += 0.22f * (w - lp);
            lp2 += 0.05f * (lp - lp2);
            float inhale = Mathf.Exp(-Mathf.Pow((u - 0.22f) / 0.10f, 2f));
            float exhale = Mathf.Exp(-Mathf.Pow((u - 0.62f) / 0.16f, 2f)) * 1.2f;
            d[i] = (lp - lp2) * (inhale * 0.8f + exhale);
        }
        // sin costura: desvanecer los extremos
        int fade = SR / 40;
        for (int i = 0; i < fade; i++) { float k = i / (float)fade; d[i] *= k; d[n - 1 - i] *= k; }
        Normalize(d, 0.7f);
        return Make("Breath", d);
    }

    // Siseo de cinta VHS (bucle de 2 s)
    public static AudioClip CreateTapeHiss()
    {
        int n = SR * 2;
        float[] d = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = Noise.Hash01(i, 9, 31) * 2f - 1f;
            lp += 0.65f * (w - lp);
            d[i] = (w - lp) * 0.9f + Mathf.Sin(2f * Mathf.PI * 50f * i / SR) * 0.05f;
        }
        int fade = SR / 50;
        for (int i = 0; i < fade; i++) { float k = i / (float)fade; d[i] *= k; d[n - 1 - i] *= k; }
        Normalize(d, 0.6f);
        return Make("TapeHiss", d);
    }

    public static AudioClip CreateUiClick()
    {
        int n = (int)(SR * 0.09f);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            d[i] = (Mathf.Sin(2f * Mathf.PI * 520f * t) * 0.6f + (Noise.Hash01(i, 2, 8) * 2f - 1f) * 0.2f) * Mathf.Exp(-t * 55f);
        }
        return Make("UiClick", d);
    }

    // Chapoteo extra (nivel inundado)
    public static AudioClip CreateSplash()
    {
        int n = (int)(SR * 0.45f);
        float[] d = new float[n];
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float w = Noise.Hash01(i, 4, 61) * 2f - 1f;
            lp += 0.3f * (w - lp);
            float bubble = Mathf.Sin(2f * Mathf.PI * (500f + 1400f * t) * t) * Mathf.Exp(-t * 22f) * 0.35f;
            d[i] = (lp * Mathf.Exp(-t * 9f) + bubble) * Mathf.Min(1f, t * 300f);
        }
        Normalize(d, 0.6f);
        return Make("Splash", d);
    }

    // ------------------------------------------------------------------
    // Grito de muerte (screamer): ráfaga de ruido distorsionada + chillidos con barrido + golpe grave,
    // todo saturado y con bitcrush. Cada tipo de monstruo cambia tono y timbre; pitch escala el conjunto.
    // ------------------------------------------------------------------
    public static AudioClip CreateScreamerScream(GrowlKind kind, int seed, float pitch)
    {
        float len = 2.4f;
        int n = (int)(SR * len);
        float[] d = new float[n];
        float baseF, noiseAmt, boomAmt, wobble, ring;
        switch (kind)
        {
            case GrowlKind.LowRumble: baseF = 170f; noiseAmt = 0.9f; boomAmt = 1.3f; wobble = 3f; ring = 0f; break;     // El Alto / Coloso: grave y sucio
            case GrowlKind.Metal: baseF = 540f; noiseAmt = 0.6f; boomAmt = 0.9f; wobble = 0f; ring = 1f; break;
            case GrowlKind.Hiss: baseF = 980f; noiseAmt = 1.1f; boomAmt = 0.6f; wobble = 18f; ring = 0f; break;        // Reptador: chillido agudo
            case GrowlKind.Moan: baseF = 310f; noiseAmt = 0.7f; boomAmt = 1.0f; wobble = 6.5f; ring = 0f; break;       // Ahogado: lamento
            default: baseF = 410f; noiseAmt = 0.8f; boomAmt = 1.0f; wobble = 9f; ring = 0f; break;                      // Rasp
        }
        baseF *= Mathf.Clamp(pitch, 0.5f, 1.6f);
        float lpN = 0f, lpN2 = 0f;
        float ph1 = 0f, ph2 = 0f, ph3 = 0f, phB = 0f;
        float held = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR, u = t / len;
            float w = Noise.Hash01(i, 9, seed) * 2f - 1f;
            lpN += 0.55f * (w - lpN);                 // ruido pasa-bajo suave
            lpN2 += 0.06f * (w - lpN2);
            float hp = lpN - lpN2;                    // ruido de banda media-alta

            // 1) ráfaga de ruido distorsionada
            float burst = hp * 5f * Mathf.Min(1f, t * 400f) * Mathf.Exp(-t * 3.2f) * noiseAmt;

            // 2) chillidos con barrido + vibrato (dos osciladores desafinados + FM)
            float sweep = 1f + 1.1f * Mathf.Min(1f, u * 3.2f) - 0.25f * Mathf.Max(0f, u - 0.5f);
            float vib = 1f + 0.05f * Mathf.Sin(2f * Mathf.PI * (7f + wobble) * t) + 0.02f * Mathf.Sin(2f * Mathf.PI * 33f * t);
            float f1 = baseF * sweep * vib;
            ph1 += f1 / SR; ph2 += f1 * 1.503f / SR; ph3 += f1 * 2.01f / SR;
            float saw = (ph1 - Mathf.Floor(ph1)) * 2f - 1f;
            float sq = Mathf.Sin(2f * Mathf.PI * ph2 + 2.5f * Mathf.Sin(2f * Mathf.PI * ph3));
            float screech = (saw * 0.8f + sq * 0.7f);
            if (ring > 0f) screech *= Mathf.Sin(2f * Mathf.PI * 97f * t);
            float envS = Mathf.Min(1f, t * 90f) * Mathf.Exp(-Mathf.Max(0f, t - 0.25f) * 1.25f);
            screech *= envS * 1.1f;

            // 3) golpe grave (boom) + thump
            float fb = 62f * Mathf.Exp(-t * 2.2f) + 24f;
            phB += fb / SR;
            float boom = Mathf.Sin(2f * Mathf.PI * phB) * Mathf.Exp(-t * 2.6f) * 1.5f * boomAmt;
            boom += lpN2 * 6f * Mathf.Exp(-t * 14f) * boomAmt;

            float v = burst + screech + boom;
            // saturación fuerte (clipping) + bitcrush (muestreo reducido y 6 bits)
            v = Mathf.Clamp(v * 2.4f, -1f, 1f);
            if (i % 3 == 0) held = Mathf.Round(v * 31f) / 31f;
            d[i] = Mathf.Lerp(v, held, 0.55f);
        }
        // reverb de pocos ecos para dar sensación de espacio cerrado
        for (int i = n - 1; i >= 0; i--)
        {
            int a = i - (int)(SR * 0.047f), b = i - (int)(SR * 0.113f);
            if (a >= 0) d[i] += d[a] * 0.28f;
            if (b >= 0) d[i] += d[b] * 0.16f;
        }
        Normalize(d, 0.97f);
        return Make("Screamer_" + kind, d);
    }

    // Acúfeno (pitido) + latido que se desvanecen tras el susto
    public static AudioClip CreateScreamerAftermath()
    {
        float len = 9f;
        int n = (int)(SR * len);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float fade = Mathf.Clamp01(1f - t / 7.5f);
            float tin = (Mathf.Sin(2f * Mathf.PI * 4300f * t) * 0.7f + Mathf.Sin(2f * Mathf.PI * 5820f * t) * 0.3f) * 0.22f * fade * fade
                * (1f + 0.12f * Mathf.Sin(2f * Mathf.PI * 0.8f * t));
            // latido "lub-dub" a ~88 bpm, algo más lento con el tiempo
            float bpm = 96f - 14f * Mathf.Clamp01(t / 8f);
            float per = 60f / bpm;
            float ph = Mathf.Repeat(t, per);
            float lub = Mathf.Exp(-ph * 22f) * Mathf.Sin(2f * Mathf.PI * 52f * ph);
            float dub = ph > 0.17f ? Mathf.Exp(-(ph - 0.17f) * 24f) * Mathf.Sin(2f * Mathf.PI * 44f * (ph - 0.17f)) * 0.75f : 0f;
            float beat = (lub + dub) * 0.9f * Mathf.Clamp01(1.1f - t / 9f);
            d[i] = (tin + beat) * Mathf.Min(1f, t * 4f);
        }
        Normalize(d, 0.8f);
        return Make("ScreamerAftermath", d);
    }
}
