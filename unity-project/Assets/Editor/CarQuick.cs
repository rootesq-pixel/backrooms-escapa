using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.8.0: prueba rápida del todoterreno: arranca directamente en el Nivel 114 y ejecuta V780Checks.CarPlay; además graba la
// salida real del AudioListener al conducir (WAV) y mide su espectro (siseo).  Unity -batchmode -executeMethod CarQuick.Run
[InitializeOnLoad]
public static class CarQuick
{
    private static IEnumerator routine; private static double waitUntil; private static SmokeTest.Report rep;
    const string Out = "/workspace/logs/carquick.txt";
    static CarQuick() { if (SessionState.GetString("smoke_phase", "") != "carquick") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        File.WriteAllText(Out, "");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "carquick");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try
        {
            if (!routine.MoveNext()) Finish();
            else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current;
            else if (routine.Current is IEnumerator) { }
        }
        catch (System.Exception e) { if (rep != null) { rep.Fail("excepción " + e); rep.Finish(); } else File.AppendAllText(Out, "FAIL excepción " + e + "\n"); Finish(); }
    }
    private static void Finish() { SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; File.AppendAllText(Out, "FIN\n"); EditorApplication.Exit(0); }

    // aplana corrutinas anidadas (float = esperar segundos)
    private static IEnumerator Flat(IEnumerator e)
    {
        Stack<IEnumerator> st = new Stack<IEnumerator>(); st.Push(e);
        while (st.Count > 0)
        {
            IEnumerator top = st.Peek();
            if (!top.MoveNext()) { st.Pop(); continue; }
            if (top.Current is IEnumerator) { st.Push((IEnumerator)top.Current); continue; }
            yield return top.Current;
        }
    }

    public class Tap : MonoBehaviour
    {
        public readonly List<float> buf = new List<float>(); public bool rec;
        private void OnAudioFilterRead(float[] data, int ch) { if (!rec) return; lock (buf) for (int i = 0; i < data.Length; i += ch) { float s = 0f; for (int c = 0; c < ch; c++) s += data[i + c]; buf.Add(s / ch); } }
    }

    private static IEnumerator Main()
    {
        SmokeTest.Report r = new SmokeTest.Report(Out); rep = r;
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        string se = System.Environment.GetEnvironmentVariable("CAR_SEED");
        gb.useRandomSeed = false; gb.seed = string.IsNullOrEmpty(se) ? 12345 : int.Parse(se);
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(8);
        float wt = 0f;
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.0f;
        GameManager gm = GameManager.Instance;
        r.Info("nivel " + gb.CurrentLevel + " semilla " + gb.seed + " LoadDiag: " + LoadDiag.Report);
        foreach (MeshFilter mf in Object.FindObjectsOfType<MeshFilter>())
        {
            if (mf.name != "SandDrift" || mf.sharedMesh == null) continue;
            Mesh mesh = mf.sharedMesh; Vector3[] vv = mesh.vertices; Vector3[] nn = mesh.normals; int[] tr = mesh.triangles;
            for (int i = 0; i < tr.Length; i += 3)
            {
                Vector3 gn = Vector3.Cross(vv[tr[i + 1]] - vv[tr[i]], vv[tr[i + 2]] - vv[tr[i]]);
                if (gn.sqrMagnitude < 1e-14f) continue;
                if (Vector3.Dot(gn, nn[tr[i]]) <= 0f && Vector3.Dot(gn, nn[tr[i + 1]]) <= 0f && Vector3.Dot(gn, nn[tr[i + 2]]) <= 0f)
                    r.Info("DRIFT tri " + (i / 3) + " idx " + tr[i] + "," + tr[i + 1] + "," + tr[i + 2] + " gn " + gn.ToString("G4") + " |gn|² " + gn.sqrMagnitude.ToString("G3") + " n0 " + nn[tr[i]].ToString("F2") + " v0 " + vv[tr[i]].ToString("F3") + " v1 " + vv[tr[i + 1]].ToString("F3") + " v2 " + vv[tr[i + 2]].ToString("F3"));
            }
        }
        // grabación de lo que se oye mientras se conduce (la prueba conduce en los primeros ~20 s)
        AudioListener al = Object.FindObjectOfType<AudioListener>();
        Tap tap = al != null ? al.gameObject.AddComponent<Tap>() : null;
        if (tap != null) tap.rec = true;
        r.Info("audio: listener vol " + AudioListener.volume + " pausa " + AudioListener.pause + " ambiente " + AudioMix.Ambience + " salida " + AudioSettings.outputSampleRate + " Hz, altavoces " + AudioSettings.speakerMode + ", dsp " + AudioSettings.GetConfiguration().dspBufferSize);
        IEnumerator cp = Flat(V780Checks.CarPlay(r, "[rápida] ", gm));
        float t0 = Time.realtimeSinceStartup; bool probed = false;
        while (cp.MoveNext())
        {
            if (tap != null && tap.rec && Time.realtimeSinceStartup - t0 > 40f) tap.rec = false;
            if (Time.realtimeSinceStartup - t0 > 12f && !probed && DesertCars.Active != null)
            {
                probed = true; float[] od = new float[1024]; float mx = 0f;
                foreach (AudioSource s in DesertCars.Active.Drivable.GetComponentsInChildren<AudioSource>()) { s.GetOutputData(od, 0); foreach (float v in od) mx = Mathf.Max(mx, Mathf.Abs(v)); r.Info("fuente " + s.name + " suena " + s.isPlaying + " vol " + s.volume.ToString("F3") + " tono " + s.pitch.ToString("F2")); }
                r.Info("salida de las fuentes del coche: pico " + mx.ToString("F4"));
            }
            yield return cp.Current;
        }
        {   // v7.9.0: huellas, lluvia, vida y viento en la misma prueba rápida
            if (tap != null) tap.rec = false;
            IEnumerator d79 = Flat(V790Checks.Desert790(r, "[rápida] ", GameManager.Instance));
            while (d79.MoveNext()) yield return d79.Current;
        }
        if (tap != null)
        {
            tap.rec = false; List<float> d; lock (tap.buf) d = new List<float>(tap.buf);
            int sr = AudioSettings.outputSampleRate;
            r.Info("audio grabado: " + d.Count + " muestras a " + sr + " Hz");
            if (d.Count > sr)
            {
                float[] a = d.ToArray();
                // espectro de la salida real (a la frecuencia de salida)
                int n = 1; while (n * 2 <= a.Length && n < (1 << 20)) n <<= 1;
                float[] re = new float[n], im = new float[n]; System.Array.Copy(a, re, n);
                DesertSynth.FFT(re, im, false);
                double tot = 0, hi = 0, hi6 = 0; float bin = (float)sr / n;
                for (int k = 1; k < n / 2; k++) { double e = re[k] * re[k] + im[k] * im[k]; tot += e; if (k * bin >= 3000f) hi += e; if (k * bin >= 6000f) hi6 += e; }
                r.Info("salida real: energía >3 kHz " + (hi / tot * 100).ToString("F2") + " %, >6 kHz " + (hi6 / tot * 100).ToString("F3") + " %");
                if (tot > 1e-12) r.Check(hi / tot < 0.05, "[rápida] lo que se oye al conducir no tiene siseo (>3 kHz " + (hi / tot * 100).ToString("F2") + " %)");
                else r.Info("(sin salida de audio en este equipo: se mide la mezcla fuera de línea en V780Checks.Edit)");
                using (BinaryWriter w = new BinaryWriter(File.Create("/workspace/logs/carquick.wav")))
                {
                    int m = a.Length;
                    w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + m * 2); w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                    w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
                    w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(m * 2);
                    for (int i = 0; i < m; i++) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(a[i] * 32767f), -32768, 32767));
                }
            }
        }
        r.Info("RESUMEN " + r.Passed + " ok / " + r.Failed + " fallos");
        r.Finish();
    }
}
