using UnityEngine;

// Audio generado por código (sin ficheros): zumbidos, drones, gotas, golpes metálicos y pasos.
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

    // Drone ambiental (bucle de 8 s): SOLO tonal (graves con ciclos enteros + parciales desafinados que "laten" despacio).
    // v6.5: antes llevaba ruido grave modulado lentamente, que sonaba a viento/soplo constante. «noiseAmount» es ahora la «aspereza»
    // (cuánto laten los parciales desafinados); no hay ruido en el drone.
    public static AudioClip CreateDrone(float baseHz, float noiseAmount, int seed)
    {
        const float seconds = 8f;
        int n = (int)(SR * seconds);
        float[] d = new float[n];
        float[] mult = { 1f, 2f, 3f, 5f };
        float[] amp = { 1f, 0.5f, 0.28f, 0.1f };
        float rough = Mathf.Clamp01(noiseAmount / 0.35f);
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float v = 0f;
            for (int h = 0; h < mult.Length; h++)
            {
                float f = Mathf.Round(baseHz * mult[h] * seconds) / seconds;      // ciclos enteros
                v += amp[h] * Mathf.Sin(2f * Mathf.PI * f * t + h);
                // parcial desafinado ±(1..2)/8 Hz: batido lento (sin ruido)
                float fd = f + (h % 2 == 0 ? 1f : 2f) / seconds;
                v += rough * 0.45f * amp[h] * Mathf.Sin(2f * Mathf.PI * fd * t + h * 1.7f + seed * 0.013f);
            }
            float lfo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * t / seconds);   // 2 ciclos por bucle
            d[i] = v * 0.25f * lfo;
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
            case StepKind.Grass: alpha = 0.25f; decay = 24f; gain = 0.8f; break;   // suelo de madera de las casas (fuera suenan pasos de hierba reales)
            case StepKind.Sand: alpha = 0.10f; decay = 11f; gain = 0.75f; break;   // v7.7.0: arena: compresión blanda y granulada, sin chasquido ni siseo
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
            float env = Mathf.Min(1f, t * (kind == StepKind.Sand ? 90f : 400f)) * Mathf.Exp(-t * decay);
            float v = s * env * gain;
            if (kind == StepKind.Sand)
            {   // granos: impulsos dispersos filtrados (crujido apagado) sobre un golpe sordo grave
                float g = Noise.Hash01(i / 23, 7, 311) < 0.16f ? (Noise.Hash01(i, 3, 77) * 2f - 1f) : 0f;
                lp2 += 0.25f * (g - lp2);
                v = (lp * 1.6f + lp2 * 0.9f) * env * gain + Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 26f) * 0.35f * Mathf.Min(1f, t * 200f);
            }
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
                        v = Sat(Mathf.Sin(2f * Mathf.PI * phase) * 2.2f) * 0.8f + Mathf.Sin(2f * Mathf.PI * phase2) * 0.25f + nzLow[i] * 1.1f;   // v7.3.0b3: antes ×3 (retumbo de viento)
                        break;
                    }
                case GrowlKind.Rasp:
                    {
                        float f = 95f + 40f * (1f - u) + 12f * Mathf.Sin(2f * Mathf.PI * 6f * t);
                        phase += f / SR;
                        float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                        // v7.3.0b3: carraspeo con la aspereza en la propia voz (antes ruido ancho ×0,6 = 57 % de la energía, sonaba a soplido)
                        float am = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 28f * t);
                        v = saw * 0.5f * am + (nz[i] - nzLow[i]) * 0.25f * am;
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
                        // v7.3.0b3: siseo de garganta con voz (antes 98 % ruido de banda ancha: se oía como una racha de viento a lo lejos)
                        float f = 150f + 50f * Mathf.Sin(Mathf.PI * u) + 6f * Mathf.Sin(2f * Mathf.PI * 7f * t);
                        phase += f / SR;
                        float saw = (phase - Mathf.Floor(phase)) * 2f - 1f;
                        float trem = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 14f * t);
                        v = (nz[i] - nzLow[i]) * 0.45f * trem + saw * 0.3f * trem + Mathf.Sin(2f * Mathf.PI * phase) * 0.3f;
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
        if (kind == GrowlKind.Rasp || kind == GrowlKind.Hiss)
        {   // paso bajo suave (~2,5 kHz): sin aire por encima
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * 2500f / SR), y = 0f;
            for (int i = 0; i < n; i++) { y += a * (d[i] - y); d[i] = y; }
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

    // Respiración suave: una inhalación o una exhalación por clip (NO es un bucle: FirstPersonController las programa).
    // Ruido limitado en banda (≈150 Hz – 1,2 kHz, sin agudos «siseantes») con envolvente de coseno elevado: ataque lento y cola larga.
    public static AudioClip CreateBreathPhase(bool inhale)
    {
        float len = inhale ? 0.85f : 1.25f;
        int n = (int)(SR * len);
        float[] d = new float[n];
        float a1 = 0f, a2 = 0f, hp = 0f;
        float alpha = inhale ? 0.20f : 0.15f;           // ~700 / ~520 Hz por polo (dos polos en cascada)
        float riseFrac = inhale ? 0.62f : 0.22f;
        for (int i = 0; i < n; i++)
        {
            float u = i / (float)n;
            float w = Noise.Hash01(i, inhale ? 21 : 22, 77) * 2f - 1f;
            a1 += alpha * (w - a1);
            a2 += alpha * (a1 - a2);
            hp += 0.043f * (a2 - hp);                    // quita graves < ~150 Hz
            float band = a2 - hp;
            float env = u < riseFrac ? 0.5f - 0.5f * Mathf.Cos(Mathf.PI * u / riseFrac)
                                     : 0.5f + 0.5f * Mathf.Cos(Mathf.PI * (u - riseFrac) / (1f - riseFrac));
            d[i] = band * env * env;                     // env² => aún más suave en los extremos
        }
        Normalize(d, 0.5f);
        return Make(inhale ? "BreathIn" : "BreathOut", d);
    }

    // Zumbido de la cinta/cabezal VHS (bucle de 2 s): 50 Hz y armónicos con ciclos enteros, sin ruido. Es lo que suena en reposo.
    public static AudioClip CreateTapeHum()
    {
        int n = SR * 2;
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SR;
            float flutter = 1f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
            d[i] = (Mathf.Sin(2f * Mathf.PI * 50f * t) + 0.5f * Mathf.Sin(2f * Mathf.PI * 100f * t + 1f) + 0.25f * Mathf.Sin(2f * Mathf.PI * 150f * t + 2f)
                    + 0.12f * Mathf.Sin(2f * Mathf.PI * 250f * t)) * flutter;
        }
        Normalize(d, 0.5f);
        return Make("TapeHum", d);
    }

    // Siseo de cinta VHS (bucle de 2 s): ruido de banda media. v6.5: SOLO suena (muy bajo) en las interferencias por glitch y se puede apagar en Opciones
    public static AudioClip CreateTapeHiss()
    {
        int n = SR * 2;
        float[] d = new float[n];
        float l1 = 0f, l2 = 0f, slow = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = Noise.Hash01(i, 9, 31) * 2f - 1f;
            l1 += 0.22f * (w - l1);
            l2 += 0.22f * (l1 - l2);                    // paso-bajo de 2 polos (~800 Hz)
            slow += 0.03f * (l2 - slow);                // paso-alto (~100 Hz)
            d[i] = (l2 - slow) + Mathf.Sin(2f * Mathf.PI * 50f * i / SR) * 0.03f;
        }
        int fade = SR / 50;
        for (int i = 0; i < fade; i++) { float k = i / (float)fade; d[i] *= k; d[n - 1 - i] *= k; }
        Normalize(d, 0.5f);
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
    // v7.0.4: cerradura atascada (llave que entra pero no gira): golpecitos metálicos y el picaporte que tiembla
    public static AudioClip CreateLockRattle()
    {
        int n = (int)(SR * 0.75f); float[] d = new float[n];
        System.Random r = new System.Random(4242);
        float[] hits = { 0.02f, 0.11f, 0.17f, 0.26f, 0.33f, 0.45f, 0.52f };
        for (int h = 0; h < hits.Length; h++)
        {
            int i0 = (int)(hits[h] * SR); float amp = h % 2 == 0 ? 0.9f : 0.55f; float f1 = 2100f + (float)r.NextDouble() * 900f, f2 = 3700f + (float)r.NextDouble() * 1400f;
            for (int i = 0; i < SR * 0.06f && i0 + i < n; i++)
            {
                float t = i / (float)SR, e = Mathf.Exp(-t * 90f);
                d[i0 + i] += amp * e * (0.6f * Mathf.Sin(2f * Mathf.PI * f1 * t) + 0.4f * Mathf.Sin(2f * Mathf.PI * f2 * t) + 0.5f * ((float)r.NextDouble() * 2f - 1f));
            }
            // golpe sordo de la puerta contra el marco
            for (int i = 0; i < SR * 0.08f && i0 + i < n; i++) { float t = i / (float)SR; d[i0 + i] += 0.5f * amp * Mathf.Exp(-t * 35f) * Mathf.Sin(2f * Mathf.PI * 95f * t); }
        }
        Normalize(d, 0.8f);
        return Make("LockRattle", d);
    }

    // v7.0.4: la llave buena: clic-clac del pestillo y un crujido corto de la puerta
    public static AudioClip CreateUnlock()
    {
        int n = (int)(SR * 1.1f); float[] d = new float[n];
        System.Random r = new System.Random(777);
        foreach (float at in new[] { 0.05f, 0.21f })
        {
            int i0 = (int)(at * SR);
            for (int i = 0; i < SR * 0.05f && i0 + i < n; i++) { float t = i / (float)SR, e = Mathf.Exp(-t * 120f); d[i0 + i] += e * (Mathf.Sin(2f * Mathf.PI * 2600f * t) * 0.7f + ((float)r.NextDouble() * 2f - 1f) * 0.6f); }
            for (int i = 0; i < SR * 0.07f && i0 + i < n; i++) { float t = i / (float)SR; d[i0 + i] += 0.6f * Mathf.Exp(-t * 50f) * Mathf.Sin(2f * Mathf.PI * 160f * t); }
        }
        int c0 = (int)(0.4f * SR); float ph = 0f;
        for (int i = 0; c0 + i < n; i++)
        {
            float t = i / (float)SR, u = t / 0.65f; if (u > 1f) break;
            float f = Mathf.Lerp(180f, 120f, u) * (1f + 0.15f * Mathf.Sin(t * 70f));
            ph += 2f * Mathf.PI * f / SR;
            float saw = (ph / (2f * Mathf.PI)) % 1f * 2f - 1f;
            d[c0 + i] += 0.35f * Mathf.Sin(Mathf.PI * u) * saw * (0.6f + 0.4f * (float)r.NextDouble());
        }
        Normalize(d, 0.75f);
        return Make("Unlock", d);
    }

    // v7.1.0: caja de música gastada tocando la «Canción de cuna» de Brahms (dominio público): púas metálicas con parciales
    // inarmónicos, algo desafinada y frenándose al final (cuerda acabándose)
    public static AudioClip CreateMusicBox()
    {
        // (semitonos desde C6, pulsos)
        int[] nt = { 4, 4, 7, 4, 4, 7, 4, 7, 12, 11, 9, 9, 7, 2, 4, 5, 2, 2, 4, 5, 2, 5, 11, 9, 7, 11, 12 };
        float[] du = { 0.5f, 0.5f, 2f, 0.5f, 0.5f, 2f, 0.5f, 0.5f, 1f, 1.5f, 0.5f, 1f, 1f, 0.5f, 0.5f, 1f, 1f, 0.5f, 0.5f, 2f, 0.5f, 0.5f, 0.5f, 0.5f, 1f, 1f, 3f };
        int[] bass = { 0, 0, 7, 7, 7, 7, 0, 0 };
        float total = 0f; foreach (float d0 in du) total += d0;
        float beat0 = 60f / 66f;
        int n = (int)(SR * (total * beat0 * 1.25f + 3f));
        float[] d = new float[n];
        System.Random r = new System.Random(19);
        float t = 0.3f, beat = 0f;
        System.Action<float, float, float> tine = (start, f, amp) =>
        {
            int s0 = (int)(start * SR), len = (int)(SR * 2.6f);
            float[] pf = { 1f, 2.76f, 5.40f, 8.93f }, pa = { 1f, 0.32f, 0.12f, 0.05f }, pd = { 1.7f, 4f, 9f, 16f };
            for (int i = 0; i < len && s0 + i < n; i++)
            {
                float tt = i / (float)SR, v = 0f;
                for (int k = 0; k < 4; k++) if (f * pf[k] < SR * 0.45f) v += pa[k] * Mathf.Exp(-tt * pd[k]) * Mathf.Sin(2f * Mathf.PI * f * pf[k] * tt);
                if (i < 60) v += (Noise.Hash01(i, 3, s0) * 2f - 1f) * 0.3f * (1f - i / 60f);   // golpe de la púa
                d[s0 + i] += v * amp;
            }
        };
        for (int i = 0; i < nt.Length; i++)
        {
            float slow = 1f + 0.35f * Mathf.Pow(i / (float)(nt.Length - 1), 2.5f);   // se va parando
            float cents = ((float)r.NextDouble() - 0.5f) * 22f;
            float f = 1046.5f * Mathf.Pow(2f, (nt[i] + cents / 100f) / 12f);
            tine(t, f, 0.55f + (float)r.NextDouble() * 0.15f);
            int bar = Mathf.FloorToInt(beat / 3f);
            if (Mathf.Abs(beat - bar * 3f) < 0.01f && bar < bass.Length) tine(t, 261.6f * Mathf.Pow(2f, bass[bar] / 12f), 0.35f);
            t += du[i] * beat0 * slow; beat += du[i];
        }
        Normalize(d, 0.7f);
        return Make("MusicBox", d);
    }

    // v7.1.0: risa infantil lejana (síntesis por formantes: pulsos glotales agudos + resonancias de /a/ infantil, "ja-ja-ja")
    public static AudioClip CreateChildLaugh(int seed)
    {
        System.Random r = new System.Random(seed);
        int n = (int)(SR * 2.2f);
        float[] d = new float[n];
        int voices = 1 + r.Next(2);
        for (int vce = 0; vce < voices; vce++)
        {
            float f0b = 360f + (float)r.NextDouble() * 120f, start = vce * (0.15f + (float)r.NextDouble() * 0.3f);
            int syl = 4 + r.Next(4);
            float[] src = new float[n];
            float ph = 0f;
            for (int sy = 0; sy < syl; sy++)
            {
                float t0 = start + sy * (0.15f + (float)r.NextDouble() * 0.04f), dur = 0.10f + (float)r.NextDouble() * 0.04f;
                int a0 = (int)(t0 * SR), len = (int)(dur * SR);
                float f0 = f0b * (1f + 0.12f * Mathf.Exp(-sy * 0.6f)) * (1f - 0.03f * sy);
                for (int i = 0; i < len && a0 + i < n; i++)
                {
                    float tt = i / (float)SR, env = Mathf.Min(1f, tt / 0.012f) * Mathf.Pow(1f - i / (float)len, 1.4f);
                    float ff = f0 * (1f + 0.02f * Mathf.Sin(2f * Mathf.PI * 7f * tt) - 0.1f * i / len);
                    ph += ff / SR; if (ph > 1f) ph -= 1f;
                    float glot = (ph < 0.4f ? Mathf.Sin(Mathf.PI * ph / 0.4f) : 0f);           // pulso glotal
                    float asp = (Noise.Hash01(a0 + i, 9, seed + vce) * 2f - 1f) * (tt < 0.035f ? 0.9f : 0.12f);   // la "j" aspirada
                    src[a0 + i] += (glot * (tt < 0.03f ? 0.2f : 1f) + asp) * env;
                }
            }
            // formantes (resonadores de 2º orden) de una /a/ infantil
            float[] F = { 1050f, 1750f, 3300f }, Bw = { 120f, 160f, 260f }, G = { 1f, 0.6f, 0.25f };
            for (int k = 0; k < 3; k++)
            {
                float rr = Mathf.Exp(-Mathf.PI * Bw[k] / SR), th = 2f * Mathf.PI * F[k] / SR;
                float a1 = 2f * rr * Mathf.Cos(th), a2 = -rr * rr, y1 = 0f, y2 = 0f;
                for (int i = 0; i < n; i++) { float y = (1f - rr) * src[i] + a1 * y1 + a2 * y2; y2 = y1; y1 = y; d[i] += y * G[k]; }
            }
        }
        Normalize(d, 0.6f);
        return Make("ChildLaugh" + seed, d);
    }

    // v7.1.0: rozar bolas de plástico huecas (bucle de 2 s): cientos de "toc" agudos amortiguados + roce de plástico
    public static AudioClip CreateBallRustle(int seed)
    {
        int n = SR * 2;
        float[] d = new float[n];
        System.Random r = new System.Random(seed);
        int clicks = 900;
        for (int c = 0; c < clicks; c++)
        {
            int start = r.Next(n);
            float f = 1500f + (float)r.NextDouble() * 3400f, f2 = f * (1.47f + (float)r.NextDouble() * 0.2f);
            float decay = 260f + (float)r.NextDouble() * 500f, amp = Mathf.Pow((float)r.NextDouble(), 2.2f);
            int len = (int)(SR * 6f / decay);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SR, e = Mathf.Exp(-t * decay);
                d[(start + i) % n] += amp * e * (Mathf.Sin(2f * Mathf.PI * f * t) + 0.45f * Mathf.Sin(2f * Mathf.PI * f2 * t));
            }
        }
        // roce: ruido paso-alto con envolvente lenta (bucle sin costura)
        float lp = 0f, prev = 0f;
        for (int i = 0; i < n; i++)
        {
            float w = Noise.Hash01(i, 7, seed) * 2f - 1f;
            lp += 0.5f * (w - lp);
            float hp = lp - prev; prev = lp;
            float env = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 3f * i / (float)n * 2f);
            d[i] += hp * 0.5f * env;
        }
        Normalize(d, 0.55f);
        return Make("BallRustle", d);
    }

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
