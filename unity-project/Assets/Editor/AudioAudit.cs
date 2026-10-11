using System;
using System.Text;
using UnityEditor;
using UnityEngine;

// Auditoría de nivel de los clips generados por código (pico, RMS, "ruidosidad" = tasa de cruces por cero / energía en altas frecuencias).
// Uso: Unity -batchmode -nographics -quit -executeMethod AudioAudit.Run
public static class AudioAudit
{
    public struct Stat { public string name; public float peak, rms, hf, flat; public float seconds; }

    public static Stat Measure(AudioClip c)
    {
        float[] d = new float[c.samples * c.channels];
        c.GetData(d, 0);
        double s2 = 0, hf2 = 0; float peak = 0;
        for (int i = 0; i < d.Length; i++)
        {
            float a = Mathf.Abs(d[i]); if (a > peak) peak = a;
            s2 += d[i] * d[i];
            if (i > 0) { float df = d[i] - d[i - 1]; hf2 += df * df; }
        }
        Stat s = new Stat();
        s.name = c.name; s.peak = peak; s.seconds = c.length;
        s.rms = (float)Math.Sqrt(s2 / Math.Max(1, d.Length));
        s.hf = (float)Math.Sqrt(hf2 / Math.Max(1, d.Length));   // diferencia de primer orden ~ energía de agudos
        s.flat = Flatness(d, c.frequency);
        return s;
    }


    // Planitud espectral (geométrica/aritmética) de 100 Hz a 8 kHz, mediana de varios fotogramas de 1024 muestras con ventana de Hann.
    // ~0 = tonal (zumbido, senos); ~1 = ruido blanco. Un bucle con planitud alta suena a «aire/soplo/siseo».
    public static float Flatness(float[] d, int sr)
    {
        const int N = 1024;
        if (d.Length < N) return 0f;
        int frames = Math.Min(48, d.Length / N);
        int step = Math.Max(N, (d.Length - N) / Math.Max(1, frames));
        System.Collections.Generic.List<float> vals = new System.Collections.Generic.List<float>();
        float[] re = new float[N], im = new float[N];
        int lo = Math.Max(1, (int)(100f * N / sr)), hi = Math.Min(N / 2 - 1, (int)(8000f * N / sr));
        for (int f = 0; f < frames; f++)
        {
            int o = Math.Min(f * step, d.Length - N);
            double e = 0;
            for (int i = 0; i < N; i++) { re[i] = d[o + i] * (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * i / (N - 1))); im[i] = 0f; e += re[i] * re[i]; }
            if (e < 1e-9) continue;
            Fft(re, im);
            double logSum = 0, sum = 0; int cnt = 0;
            for (int k = lo; k <= hi; k++) { double p = re[k] * re[k] + im[k] * im[k] + 1e-12; logSum += Math.Log(p); sum += p; cnt++; }
            vals.Add((float)(Math.Exp(logSum / cnt) / (sum / cnt)));
        }
        if (vals.Count == 0) return 0f;
        vals.Sort();
        return vals[vals.Count / 2];
    }

    private static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { float t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            float ang = -2f * Mathf.PI / len, wr = Mathf.Cos(ang), wi = Mathf.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                float cr = 1f, ci = 0f;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    float xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                    float ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }

    public static float Db(float x) { return 20f * Mathf.Log10(Mathf.Max(x, 1e-6f)); }

    public static void Run()
    {
        StringBuilder sb = new StringBuilder();
        Action<string, AudioClip, float> row = (label, clip, vol) =>
        {
            Stat s = Measure(clip);
            sb.AppendLine(string.Format("{0,-28} peak {1:0.00}  rms {2:0.000} ({3,6:0.0} dB)  hf {4:0.000} flat {7:0.000}  vol {5:0.00} -> efectivo rms {6,6:0.0} dB", label, s.peak, s.rms, Db(s.rms), s.hf, vol, Db(s.rms * vol), s.flat));
            UnityEngine.Object.DestroyImmediate(clip);
        };
        row("breath IN (max)", ProceduralAudio.CreateBreathPhase(true), AudioMix.BreathMax);
        row("breath OUT (max)", ProceduralAudio.CreateBreathPhase(false), AudioMix.BreathMax * 0.9f);
        row("tapeHum (vhs .7)", ProceduralAudio.CreateTapeHum(), AudioMix.Hum(0.7f));
        row("tapeHiss (max)", ProceduralAudio.CreateTapeHiss(), AudioMix.Hiss(1f, 1f));
        row("buzz (cfg .25)", ProceduralAudio.CreateBuzzClip(), AudioMix.Buzz(0.25f));
        for (int i = 0; i < LevelConfig.Count; i++)
        {
            LevelConfig c = LevelConfig.Get(i);
            row("drone L" + i + " " + c.droneHz + "Hz n" + c.droneNoise, ProceduralAudio.CreateDrone(c.droneHz, c.droneNoise, 1000 + i * 17), AudioMix.Drone(c.droneVolume));
        }
        row("footstep carpet", ProceduralAudio.CreateFootstep(StepKind.Carpet), 1f);
        row("footstep concrete", ProceduralAudio.CreateFootstep(StepKind.Concrete), 1f);
        row("footstep water", ProceduralAudio.CreateFootstep(StepKind.Water), 1f);
        row("splash", ProceduralAudio.CreateSplash(), 1f);
        row("aftermath", ProceduralAudio.CreateScreamerAftermath(), 0.5f);
        Debug.Log("AUDIOAUDIT\n" + sb);
        System.IO.File.WriteAllText("/workspace/logs/audio-audit.txt", sb.ToString());
    }
}
