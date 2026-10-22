using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.2.0: medición del «aire / soplido» por nivel. Graba la salida real del AudioListener (OnAudioFilterRead) con el jugador
// quieto tras el ascensor y luego silencia cada grupo de fuentes (por clip) para atribuir el ruido. Escribe WAV + inventario.
//   HISS_DIR=/ruta [HISS_LEVELS=0,1,2] [HISS_SECS=6] Unity -batchmode -executeMethod AudioHissScan.Run
[InitializeOnLoad]
public static class AudioHissScan
{
    private static IEnumerator routine; private static double waitUntil;
    static string Dir { get { return System.Environment.GetEnvironmentVariable("HISS_DIR") ?? "/workspace/logs/hiss"; } }
    static AudioHissScan() { if (SessionState.GetString("smoke_phase", "") != "hissscan") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "hissscan");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[AudioHissScan] " + e); Info("ERROR " + e); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; Debug.Log("[AudioHissScan] FIN"); EditorApplication.Exit(0); }
    static readonly System.Text.StringBuilder info = new System.Text.StringBuilder();
    static void Info(string s) { info.AppendLine(s); Directory.CreateDirectory(Dir); File.WriteAllText(Dir + "/info.txt", info.ToString()); Debug.Log("[AudioHissScan] " + s); }

    public class Tap : MonoBehaviour
    {
        public readonly List<float> buf = new List<float>(); public bool rec; public int calls;
        private void OnAudioFilterRead(float[] data, int ch)
        {
            calls++;
            if (!rec) return;
            lock (buf) { for (int i = 0; i < data.Length; i += ch) { float s = 0f; for (int c = 0; c < ch; c++) s += data[i + c]; buf.Add(s / ch); } }
        }
    }

    static void WriteWav(string path, List<float> d, int sr)
    {
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            int n = d.Count;
            w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
            w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            for (int i = 0; i < n; i++) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(d[i] * 32767f), -32768, 32767));
        }
    }

    static string Key(AudioSource a)
    {
        if (a.clip != null) return a.clip.name;
        Transform t = a.transform; string p = t.name; if (t.parent != null) p = t.parent.name + "/" + p;
        return "oneshot:" + p;
    }

    static IEnumerator Record(Tap tap, float secs, string file)
    {
        lock (tap.buf) tap.buf.Clear();
        tap.rec = true; yield return secs; tap.rec = false;
        List<float> copy; lock (tap.buf) copy = new List<float>(tap.buf);
        float mx = 0f; foreach (float v in copy) mx = Mathf.Max(mx, Mathf.Abs(v));
        Info("      grabado " + Path.GetFileName(file) + " muestras " + copy.Count + " pico " + mx.ToString("0.000000"));
        WriteWav(file, copy, AudioSettings.outputSampleRate);
    }

    static readonly HashSet<string> dumped = new HashSet<string>();
    static void DumpClip(AudioClip c)
    {
        if (c == null || dumped.Contains(c.name)) return; dumped.Add(c.name);
        float[] d = new float[c.samples * c.channels];
        if (!c.GetData(d, 0)) { Info("   (clip " + c.name + " no legible)"); return; }
        List<float> mono = new List<float>(c.samples);
        for (int i = 0; i < c.samples; i++) { float v = 0f; for (int k = 0; k < c.channels; k++) v += d[i * c.channels + k]; mono.Add(v / c.channels); }
        string safe = c.name.Replace('/', '_').Replace(' ', '_');
        WriteWav(Dir + "/clips/" + safe + ".wav", mono, c.frequency);
    }
    // efectivo máximo por fuente (clave = clip) en cada fase
    static void Sample(Dictionary<string, float> acc, Dictionary<string, string> where)
    {
        foreach (AudioCensus.Row r in AudioCensus.Take())
        {
            if (!r.playing) continue;
            string k = r.clip == "(sin clip)" ? "oneshot:" + r.owner : r.clip;
            float e = r.effective;
            if (!acc.ContainsKey(k) || acc[k] < e) { acc[k] = e; where[k] = r.owner + (r.loop ? " [bucle]" : "") + " d=" + r.dist.ToString("0.0"); }
        }
        foreach (AudioSource a in Object.FindObjectsOfType<AudioSource>()) if (a != null && a.isPlaying && a.clip != null) DumpClip(a.clip);
    }
    static void Report(int lv, string phase, Dictionary<string, float> acc, Dictionary<string, string> where)
    {
        List<KeyValuePair<string, float>> l = new List<KeyValuePair<string, float>>(acc); l.Sort((a, b) => b.Value.CompareTo(a.Value));
        foreach (KeyValuePair<string, float> kv in l) Info("SRC\t" + lv + "\t" + phase + "\t" + kv.Key + "\t" + kv.Value.ToString("0.00000") + "\t" + where[kv.Key]);
    }

    private static IEnumerator Main()
    {
        string lvEnv = System.Environment.GetEnvironmentVariable("HISS_LEVELS") ?? "0,1,2,3,4,5,6";
        Directory.CreateDirectory(Dir + "/clips");
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        gb.useRandomSeed = false; gb.seed = 12345;
        foreach (string ls in lvEnv.Split(','))
        {
            int lv = int.Parse(ls.Trim());
            int ld = gb.LoadCount;
            if (gb.InMenu) UIManager.Instance.StartGame(lv); else gb.LoadLevel(lv);
            float wt = 0f;
            while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
            GameManager gm = GameManager.Instance;
            Info("== nivel " + lv + " (" + LevelConfig.Get(lv).name + ") agua=" + (WaterVolume.Active != null));
            Dictionary<string, float> acc = new Dictionary<string, float>(); Dictionary<string, string> where = new Dictionary<string, string>();
            wt = 0f;
            while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { Sample(acc, where); yield return 0.25f; wt += 0.25f; }
            Report(lv, "ascensor", acc, where); acc.Clear(); where.Clear();
            yield return 2.0f;
            NetSession.NoCatchForTests = true;
            if (gm.monsters != null) foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
            for (int i = 0; i < 16; i++) { Sample(acc, where); yield return 0.25f; }
            Report(lv, "quieto", acc, where); acc.Clear(); where.Clear();
            // paseo: puntos al azar del NavMesh
            UnityEngine.AI.NavMeshTriangulation tri = UnityEngine.AI.NavMesh.CalculateTriangulation();
            System.Random rng = new System.Random(lv * 7 + 3);
            FirstPersonController pw = gm.player;
            for (int j = 0; j < 10 && tri.vertices.Length > 0; j++)
            {
                Vector3 v = tri.vertices[rng.Next(tri.vertices.Length)];
                pw.Teleport(v + Vector3.up * 0.1f, (float)rng.NextDouble() * 360f);
                for (int i = 0; i < 6; i++) { Sample(acc, where); yield return 0.25f; }
            }
            Report(lv, "paseo", acc, where);
        }
        Info("FIN");
    }
}
