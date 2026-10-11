using System;
using System.Text;
using UnityEditor;
using UnityEngine;

// Auditoría de nivel de los clips generados por código (pico, RMS, "ruidosidad" = tasa de cruces por cero / energía en altas frecuencias).
// Uso: Unity -batchmode -nographics -quit -executeMethod AudioAudit.Run
public static class AudioAudit
{
    public struct Stat { public string name; public float peak, rms, hf; public float seconds; }

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
        return s;
    }

    public static float Db(float x) { return 20f * Mathf.Log10(Mathf.Max(x, 1e-6f)); }

    public static void Run()
    {
        StringBuilder sb = new StringBuilder();
        Action<string, AudioClip, float> row = (label, clip, vol) =>
        {
            Stat s = Measure(clip);
            sb.AppendLine(string.Format("{0,-28} peak {1:0.00}  rms {2:0.000} ({3,6:0.0} dB)  hf {4:0.000}  vol {5:0.00} -> efectivo rms {6,6:0.0} dB", label, s.peak, s.rms, Db(s.rms), s.hf, vol, Db(s.rms * vol)));
            UnityEngine.Object.DestroyImmediate(clip);
        };
        row("breath IN (max)", ProceduralAudio.CreateBreathPhase(true), AudioMix.BreathMax);
        row("breath OUT (max)", ProceduralAudio.CreateBreathPhase(false), AudioMix.BreathMax * 0.9f);
        row("tapeHiss (idle, vhs .7)", ProceduralAudio.CreateTapeHiss(), AudioMix.Hiss(0.7f, 0f));
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
