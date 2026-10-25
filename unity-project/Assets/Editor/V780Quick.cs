using UnityEditor;
// v7.8.0: solo las pruebas de edición de V780Checks (rápido):  Unity -batchmode -executeMethod V780Quick.Run
public static class V780Quick
{
    static void Wav(string path, float[] d, int sr)
    {
        using (var w = new System.IO.BinaryWriter(System.IO.File.Create(path)))
        {
            int n = d.Length;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            for (int i = 0; i < n; i++) w.Write((short)UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(d[i] * 32767f), -32768, 32767));
        }
    }
    static void DumpWavs()
    {
        string dir = "/workspace/v780/audio"; System.IO.Directory.CreateDirectory(dir);
        Wav(dir + "/wind.wav", DesertSynth.Wind(9101), DesertSynth.SR);
        Wav(dir + "/wind_storm.wav", DesertSynth.WindStorm(9202), DesertSynth.SR);
        Wav(dir + "/engine_lo.wav", DesertSynth.Engine(7301, 1000f, false), DesertSynth.SR);
        Wav(dir + "/engine_hi.wav", DesertSynth.Engine(7302, 2800f, true), DesertSynth.SR);
        Wav(dir + "/tyres.wav", DesertSynth.Tyres(7303), DesertSynth.SR);
        var m = typeof(DesertFx).GetMethod("SandTrickle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (m != null) { UnityEngine.AudioClip c = (UnityEngine.AudioClip)m.Invoke(null, new object[] { 12345 }); float[] d = new float[c.samples]; c.GetData(d, 0); Wav(dir + "/sand_trickle_v771.wav", d, c.frequency); }
    }
    public static void Run()
    {
        SmokeTest.Report r = new SmokeTest.Report("/workspace/logs/v780quick.txt");
        try { V780Checks.Edit(r); } catch (System.Exception e) { r.Fail("excepción " + e); }
        if (System.Environment.GetEnvironmentVariable("V780_WAV") == "1") DumpWavs();
        r.Finish();
        EditorApplication.Exit(0);
    }
}
