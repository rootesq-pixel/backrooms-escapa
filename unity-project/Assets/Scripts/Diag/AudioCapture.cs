// v7.3.0: diagnóstico de audio. Graba la mezcla REAL que llega al AudioListener (OnAudioFilterRead) y, además, una mezcla
// «offline» calculada a partir de las fuentes que suenan (datos del clip × volumen efectivo) para cuando no hay salida de audio.
// Escribe WAV de 32 bits flotantes. No se usa en el juego normal: solo lo añaden las pruebas o «-audiocheck».
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

public class AudioCapture : MonoBehaviour
{
    readonly List<float> buf = new List<float>();
    volatile bool rec;
    public int Channels = 2, Callbacks;
    public bool SilenceOutput;   // copia y deja la salida en silencio (para grabar sin que suene en los altavoces)

    public static AudioCapture Attach()
    {
        AudioListener al = Object.FindObjectOfType<AudioListener>();
        if (al == null) return null;
        AudioCapture c = al.GetComponent<AudioCapture>();
        if (c == null) c = al.gameObject.AddComponent<AudioCapture>();
        return c;
    }

    void OnAudioFilterRead(float[] data, int ch)
    {
        Callbacks++; Channels = ch;
        if (rec) lock (buf) buf.AddRange(data);
        if (SilenceOutput) System.Array.Clear(data, 0, data.Length);
    }

    public void Begin() { lock (buf) buf.Clear(); rec = true; }
    public float[] End() { rec = false; lock (buf) return buf.ToArray(); }

    public static void WriteWav(string path, float[] d, int ch, int sr)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            int bytes = d.Length * 4;
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)3); w.Write((short)ch); w.Write(sr); w.Write(sr * ch * 4); w.Write((short)(ch * 4)); w.Write((short)32);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
            foreach (float f in d) w.Write(f);
        }
    }

    // mezcla offline (mono) de las fuentes que suenan: clip en bucle desde su posición actual, con tono, volumen y atenuación
    public static float[] OfflineMix(float seconds, int sr, StringBuilder list)
    {
        int n = Mathf.RoundToInt(seconds * sr); float[] mix = new float[n];
        AudioListener al = Object.FindObjectOfType<AudioListener>();
        Vector3 lp = al != null ? al.transform.position : Vector3.zero;
        foreach (AudioSource a in Object.FindObjectsOfType<AudioSource>(true))
        {
            if (a.clip == null || !a.isPlaying || !a.enabled || !a.gameObject.activeInHierarchy || a.mute) continue;
            float dist = Vector3.Distance(lp, a.transform.position), ro;
            if (a.rolloffMode == AudioRolloffMode.Linear) ro = Mathf.Clamp01(1f - (dist - a.minDistance) / Mathf.Max(0.01f, a.maxDistance - a.minDistance));
            else ro = Mathf.Clamp01(a.minDistance / Mathf.Max(a.minDistance, dist));
            float g = a.volume * AudioListener.volume * Mathf.Lerp(1f, ro, a.spatialBlend);
            AudioClip c = a.clip;
            if (list != null) list.AppendLine(string.Format("  src {0,-60} clip={1,-18} loop={2} gain={3:0.00000}", AudioCensus.PathOf(a.transform), c.name, a.loop ? "S" : "n", g));
            if (g <= 0f) continue;
            float[] d = new float[c.samples * c.channels];
            if (!c.GetData(d, 0)) continue;
            double step = c.frequency * (double)a.pitch / sr; double pos = a.timeSamples;
            for (int i = 0; i < n; i++)
            {
                int k = (int)pos; if (k >= c.samples) { if (!a.loop) break; pos -= c.samples; k = (int)pos; }
                float v = 0f; for (int q = 0; q < c.channels; q++) v += d[k * c.channels + q]; v /= c.channels;
                mix[i] += v * g; pos += step;
            }
        }
        return mix;
    }

    // componentes que generan o filtran audio por código (OnAudioFilterRead) en la escena
    public static string FilterReaders()
    {
        StringBuilder sb = new StringBuilder();
        foreach (MonoBehaviour m in Object.FindObjectsOfType<MonoBehaviour>(true))
        {
            if (m == null || m is AudioCapture) continue;
            MethodInfo mi = m.GetType().GetMethod("OnAudioFilterRead", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null) sb.AppendLine("  OnAudioFilterRead: " + m.GetType().Name + " en " + AudioCensus.PathOf(m.transform) + (m.enabled ? "" : " (desactivado)"));
        }
        foreach (Behaviour b in Object.FindObjectsOfType<AudioLowPassFilter>(true)) sb.AppendLine("  filtro: AudioLowPassFilter en " + AudioCensus.PathOf(b.transform) + " corte " + ((AudioLowPassFilter)b).cutoffFrequency.ToString("F0") + " Hz");
        foreach (Behaviour b in Object.FindObjectsOfType<AudioReverbFilter>(true)) sb.AppendLine("  filtro: AudioReverbFilter en " + AudioCensus.PathOf(b.transform));
        foreach (AudioReverbZone z in Object.FindObjectsOfType<AudioReverbZone>(true)) sb.AppendLine("  zona: AudioReverbZone en " + AudioCensus.PathOf(z.transform) + " " + z.reverbPreset);
        return sb.ToString();
    }
}

// v7.3.0: «-audiocheck <carpeta>» en el ejecutable: graba la mezcla real por nivel SIN que suene (el filtro del oyente copia y silencia;
// el audio está en pausa hasta que el filtro está puesto). Quieto 8 s y andando 8 s por nivel; WAV + sources.txt; luego sale.
public class AudioBuildCheck : MonoBehaviour
{
    string dir;
    public static void MaybeStart(GameObject host)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == "-audiocheck")
            {
                AudioListener.pause = true;
                AudioBuildCheck c = host.AddComponent<AudioBuildCheck>(); c.dir = a[i + 1];
                GameBootstrap gb = host.GetComponent<GameBootstrap>(); if (gb != null) { gb.useRandomSeed = false; gb.seed = 7110; }
            }
    }

    AudioCapture cap;
    void LateUpdate()
    {   // siempre con el filtro silenciador en el oyente activo (el oyente cambia al cargar niveles)
        AudioListener al = Object.FindObjectOfType<AudioListener>();
        if (al == null) { AudioListener.pause = true; return; }
        AudioCapture c = al.GetComponent<AudioCapture>();
        if (c == null) { c = al.gameObject.AddComponent<AudioCapture>(); }
        c.SilenceOutput = true; cap = c;
        if (AudioListener.pause) AudioListener.pause = false;
    }

    System.Collections.IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        string rep = Path.Combine(dir, "sources.txt"); File.WriteAllText(rep, "");
        float t0 = Time.realtimeSinceStartup;
        while ((GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) && Time.realtimeSinceStartup - t0 < 120f) yield return null;
        GameBootstrap gb = GameBootstrap.Instance;
        int sr = AudioSettings.outputSampleRate;
        File.AppendAllText(rep, "outputSampleRate " + sr + " speakerMode " + AudioSettings.speakerMode + " dsp " + AudioSettings.GetConfiguration().dspBufferSize + "\n");
        ElevatorCabin.InstantForTests = true; ExitLock.AutoKeyForTests = true;
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            int lc = gb.LoadCount; gb.LoadLevel(lv);
            float w = 0f;
            while (w < 120f && !(gb.LoadCount > lc && gb.IsLoaded && GameManager.Instance != null && FirstPersonController.LocalInstance != null && !ElevatorCabin.PlayerLocked)) { yield return null; w += Time.unscaledDeltaTime; }
            yield return new WaitForSecondsRealtime(2.5f);
            foreach (MonsterAI m in GameManager.Instance.monsters) if (m != null) m.Stop();
            yield return new WaitForSecondsRealtime(1f);
            string[] spots = lv == 7 ? new[] { "inicio", "mar", "bajo_agua" } : new[] { "inicio" };
            foreach (string s in spots)
            {
                FirstPersonController pw = FirstPersonController.LocalInstance;
                if (s == "mar") pw.Teleport(new Vector3(40f, 0.2f, 228f), 0f);
                if (s == "bajo_agua") pw.Teleport(new Vector3(50f, -18f, 232f), 0f);
                if (s != "inicio") yield return new WaitForSecondsRealtime(3f);
                foreach (string mode in new[] { "quieto", "andando" })
                {
                    pw.debugInput = mode == "andando"; pw.debugMove = mode == "andando" ? new Vector2(0f, 1f) : Vector2.zero;
                    yield return new WaitForSecondsRealtime(0.5f);
                    while (cap == null) yield return null;
                    cap.Begin();
                    yield return new WaitForSecondsRealtime(8f);
                    float[] real = cap.End();
                    string tag = "L" + lv + "_" + s + "_" + mode;
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("== " + tag + " (" + LevelConfig.Get(lv).name + ") callbacks=" + cap.Callbacks + " samples=" + real.Length + " ch=" + cap.Channels);
                    float[] off = AudioCapture.OfflineMix(8f, sr, sb);
                    sb.Append(AudioCapture.FilterReaders()); sb.Append(AudioCensus.Dump(tag));
                    File.AppendAllText(rep, sb.ToString());
                    AudioCapture.WriteWav(Path.Combine(dir, tag + "_real.wav"), real, cap.Channels, sr);
                    if (mode == "quieto") AudioCapture.WriteWav(Path.Combine(dir, tag + "_offline.wav"), off, 1, sr);
                }
                pw.debugInput = false;
            }
        }
        File.AppendAllText(rep, "END\n");
        Application.Quit(0);
    }
}
