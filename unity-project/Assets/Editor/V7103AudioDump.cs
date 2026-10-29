using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// v7.10.3: vuelca los clips de alarma, latido y pasos (con sus ganancias de mezcla) a WAV para las muestras antes/después.
// Uso: V7103_OUT=/ruta Unity -batchmode -nographics -quit -executeMethod V7103AudioDump.Run
public static class V7103AudioDump
{
    private static StringBuilder man;
    private static string dir;

    private static void Wav(string name, float[] d, int sr, float gain, string note)
    {
        using (FileStream fs = new FileStream(Path.Combine(dir, name + ".wav"), FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + d.Length * 4); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)3); w.Write((short)1); w.Write(sr); w.Write(sr * 4); w.Write((short)4); w.Write((short)32);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(d.Length * 4);
            for (int i = 0; i < d.Length; i++) w.Write(d[i]);
        }
        man.AppendLine(name + "\t" + gain.ToString("R") + "\t" + note);
    }

    private static void Clip(string name, AudioClip c, float gain, string note)
    {
        if (c == null) return;
        float[] d = new float[c.samples * c.channels]; c.GetData(d, 0);
        if (c.channels > 1) { float[] m = new float[c.samples]; for (int i = 0; i < c.samples; i++) { float s = 0; for (int k = 0; k < c.channels; k++) s += d[i * c.channels + k]; m[i] = s / c.channels; } d = m; }
        Wav(name, d, c.frequency, gain, note);
    }

    public static void Run()
    {
        dir = System.Environment.GetEnvironmentVariable("V7103_OUT"); if (string.IsNullOrEmpty(dir)) dir = "/workspace/v7103/raw";
        Directory.CreateDirectory(dir); man = new StringBuilder();
        GameSettings.ResetDefaults();
        Wav("alarm", Level0Events.AlarmData(), 22050, Level0Events.AlarmGain, "loop " + Level0Events.AlarmLoop + " s");
        man.AppendLine("#heart_gain\t" + Adrenaline.HeartGain(0.3f).ToString("R") + "\t" + Adrenaline.HeartGain(1f).ToString("R"));
        System.Type hb = typeof(Adrenaline);
        System.Reflection.MethodInfo hv = hb.GetMethod("CreateHeartbeatVariant");
        if (hv != null) { for (int k = 0; k < 4; k++) Clip("heart_v" + k, (AudioClip)hv.Invoke(null, new object[] { k }), 1f, "variante " + k); }
        else Clip("heart_v0", Adrenaline.CreateHeartbeat(), 1f, "único");
        float stepGain = 0.45f * AudioMix.StepMaster;
        System.Type bank = System.Type.GetType("StepBank, Assembly-CSharp");
        foreach (StepKind k in new[] { StepKind.Carpet, StepKind.Concrete, StepKind.Water, StepKind.Metal, StepKind.Grass, StepKind.Sand })
        {
            if (bank != null)
            {
                int n = (int)bank.GetMethod("Count").Invoke(null, new object[] { k });
                for (int i = 0; i < n; i++) Clip("step_" + k + "_" + i, (AudioClip)bank.GetMethod("Get").Invoke(null, new object[] { k, i }), stepGain, "paso");
                if (n == 0) Clip("step_" + k + "_0", ProceduralAudio.CreateFootstep(k), stepGain, "paso (procedural)");
            }
            else Clip("step_" + k + "_0", ProceduralAudio.CreateFootstep(k), stepGain, "paso");
        }
        LevelConfig c0 = LevelConfig.Get(0), c9 = LevelConfig.Get(9);
        Clip("drone_L0", ProceduralAudio.CreateDrone(c0.droneHz, c0.droneNoise, 1000), AudioMix.Drone(c0.droneVolume), "drone nivel 0");
        Clip("drone_L9", ProceduralAudio.CreateDrone(c9.droneHz, c9.droneNoise, 1000 + 9 * 17), AudioMix.Drone(c9.droneVolume), "drone 7460");
        File.WriteAllText(Path.Combine(dir, "manifest.tsv"), man.ToString());
        Debug.Log("[V7103AudioDump] listo " + dir);
    }
}
