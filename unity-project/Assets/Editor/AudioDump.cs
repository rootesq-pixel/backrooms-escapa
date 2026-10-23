using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

// Vuelca a WAV (16 bit) cada clip procedural con su ganancia efectiva de mezcla, para analizarlos con numpy.
// Uso: Unity -batchmode -nographics -quit -executeMethod AudioDump.Run   -> /workspace/audio-dump/*.wav + manifest.tsv
public static class AudioDump
{
    private static StringBuilder man;

    private static void Add(string name, AudioClip c, bool loop, float gain, string where)
    {
        float[] d = new float[c.samples * c.channels];
        c.GetData(d, 0);
        int sr = c.frequency;
        using (FileStream fs = new FileStream("/workspace/audio-dump/" + name + ".wav", FileMode.Create))
        using (BinaryWriter w = new BinaryWriter(fs))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + d.Length * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(d.Length * 2);
            for (int i = 0; i < d.Length; i++) w.Write((short)(Mathf.Clamp(d[i], -1f, 1f) * 32767f));
        }
        man.AppendLine(name + "\t" + (loop ? 1 : 0) + "\t" + gain.ToString("R") + "\t" + where);
        Object.DestroyImmediate(c);
    }

    public static void Run()
    {
        Directory.CreateDirectory("/workspace/audio-dump");
        foreach (string f in Directory.GetFiles("/workspace/audio-dump")) File.Delete(f);
        man = new StringBuilder();
        for (int i = 0; i < LevelConfig.Count; i++)
        {
            LevelConfig c = LevelConfig.Get(i);
            Add("drone_L" + i, ProceduralAudio.CreateDrone(c.droneHz, c.droneNoise, 1000 + i * 17), true, AudioMix.Drone(c.droneVolume), "ambiente nivel " + i + " (2D, siempre)");
        }
        Add("buzz", ProceduralAudio.CreateBuzzClip(), true, AudioMix.Buzz(0.25f), "luces (3D, max 9 m) x N");
        Add("tapehum_idle", ProceduralAudio.CreateTapeHum(), true, AudioMix.Hum(0.7f), "VHS reposo, tonal (2D)");
        Add("tapehiss_glitch", ProceduralAudio.CreateTapeHiss(), true, AudioMix.Hiss(0.7f, 1f), "VHS con monstruo cerca (2D)");
        Add("elev_rumble", ElevatorAudio.CreateRumble(), true, ElevatorAudio.RumbleGain, "ascensor (solo ~7 s)");
        Add("elev_machinery", ElevatorAudio.CreateMachinery(), true, ElevatorAudio.MachineGain, "ascensor (solo ~7 s)");
        Add("elev_air", ElevatorAudio.CreateAir(), true, ElevatorAudio.AirGain, "ascensor (solo ~7 s)");
        Add("breath_in", ProceduralAudio.CreateBreathPhase(true), false, AudioMix.BreathMax, "respiración");
        foreach (StepKind k in new[] { StepKind.Carpet, StepKind.Concrete, StepKind.Water, StepKind.Metal }) Add("step_" + k, ProceduralAudio.CreateFootstep(k), false, 1f, "pasos");
        Add("splash", ProceduralAudio.CreateSplash(), false, 1f, "chapoteo");
        Add("drip", ProceduralAudio.CreateDrip(), false, AudioMix.Event(0.65f), "suceso");
        Add("clank", ProceduralAudio.CreateClank(), false, AudioMix.Event(0.65f), "suceso");
        foreach (GrowlKind g in new[] { GrowlKind.LowRumble, GrowlKind.Rasp, GrowlKind.Metal, GrowlKind.Hiss, GrowlKind.Moan })
            Add("growl_" + g, ProceduralAudio.CreateGrowl(g, 7), false, 1f, "monstruo");
        File.WriteAllText("/workspace/audio-dump/manifest.tsv", man.ToString());
        Debug.Log("[AudioDump] listo");
    }
}
