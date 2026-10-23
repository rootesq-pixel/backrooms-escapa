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
        if (srec) lock (sbuf) { for (int i = 0; i + ch - 1 < data.Length; i += ch) { float v = 0f; for (int q = 0; q < ch; q++) v += data[i + q]; sbuf.Add(v / ch); } }
        if (SilenceOutput) System.Array.Clear(data, 0, data.Length);
    }

    // v7.3.0b3: grabación larga en flujo (mono): el hilo de audio acumula y el hilo principal vacía a disco cada poco
    readonly List<float> sbuf = new List<float>();
    volatile bool srec;
    public void StreamBegin() { lock (sbuf) sbuf.Clear(); srec = true; }
    public float[] StreamDrain() { lock (sbuf) { float[] r = sbuf.ToArray(); sbuf.Clear(); return r; } }
    public void StreamEnd() { srec = false; }

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
    float longStill, longMove; bool vhsOff;
    public static void MaybeStart(GameObject host)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        AudioBuildCheck made = null;
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == "-audiocheck")
            {
                AudioListener.pause = true;
                AudioBuildCheck c = host.AddComponent<AudioBuildCheck>(); c.dir = a[i + 1]; made = c;
                GameBootstrap gb = host.GetComponent<GameBootstrap>(); if (gb != null) { gb.useRandomSeed = false; gb.seed = 7110; }
            }
        if (made == null) return;
        // «-audiolong <s quieto> <s andando>»: grabación larga por nivel (WAV 16 bits mono + línea temporal cada 10 s)
        // «-vhsoff»: prueba A/B sin el efecto VHS (imagen y audio de la cinta) SIN tocar las preferencias guardadas del jugador
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == "-audiolong" && i + 2 < a.Length) { float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out made.longStill); float.TryParse(a[i + 2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out made.longMove); }
            if (a[i] == "-vhsoff") made.vhsOff = true;
        }
    }

    void Update()
    {
        if (!vhsOff) return;
        foreach (VhsEffect v in Object.FindObjectsOfType<VhsEffect>(true)) if (v.EffectEnabled) v.EffectEnabled = false;
        foreach (VhsOverlay o in Object.FindObjectsOfType<VhsOverlay>(true)) if (o.enabled) o.enabled = false;
    }

    static void WriteI16Header(BinaryWriter w, int sr, int samples)
    {
        int bytes = samples * 2;
        w.Seek(0, SeekOrigin.Begin);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes); w.Write(Encoding.ASCII.GetBytes("WAVE"));
        w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        w.Seek(0, SeekOrigin.End);
    }

    string TimelineRow(float t)
    {
        List<AudioCensus.Row> rows = AudioCensus.Take();
        int total = rows.Count, playing = 0, loops = 0;
        List<AudioCensus.Row> pl = new List<AudioCensus.Row>();
        foreach (AudioCensus.Row r in rows) if (r.playing) { playing++; if (r.loop) loops++; pl.Add(r); }
        pl.Sort((x, y) => y.effective.CompareTo(x.effective));
        StringBuilder sb = new StringBuilder();
        sb.Append(t.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)).Append("s fuentes=").Append(total).Append(" sonando=").Append(playing).Append(" bucles=").Append(loops);
        VhsEffect v = Object.FindObjectOfType<VhsEffect>();
        if (v != null) sb.Append(" vhs=").Append(v.EffectEnabled ? "on" : "off").Append(" int=").Append(v.CurrentIntensity.ToString("F2")).Append(" glitch=").Append(v.Glitch.ToString("F2"));
        foreach (AudioLowPassFilter f in Object.FindObjectsOfType<AudioLowPassFilter>()) sb.Append(" lp=").Append(f.cutoffFrequency.ToString("F0"));
        for (int i = 0; i < pl.Count && i < 8; i++) sb.Append(" | ").Append(pl[i].clip).Append("@").Append(pl[i].owner.Length > 28 ? pl[i].owner.Substring(pl[i].owner.Length - 28) : pl[i].owner).Append("=").Append(pl[i].effective.ToString("0.0000"));
        return sb.ToString();
    }

    System.Collections.IEnumerator LongRecord(string tag, float secs, int sr, FirstPersonController pw, bool move)
    {
        string path = Path.Combine(dir, tag + "_largo.wav"), tl = Path.Combine(dir, tag + "_linea.txt");
        File.WriteAllText(tl, "== " + tag + " " + secs + " s\n");
        int written = 0; float t = 0f, nextRow = 0f, nextTurn = 0f; int dirIx = 0;
        Vector2[] dirs = { new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0f, -1f), new Vector2(-1f, 0f) };
        using (BinaryWriter w = new BinaryWriter(File.Create(path)))
        {
            WriteI16Header(w, sr, 0);
            cap.StreamBegin();
            while (t < secs)
            {
                if (move && t >= nextTurn) { pw.debugInput = true; pw.debugMove = dirs[dirIx++ % 4]; nextTurn += 6f; }
                if (t >= nextRow) { File.AppendAllText(tl, TimelineRow(t) + "\n"); nextRow += 10f; }
                yield return new WaitForSecondsRealtime(0.5f);
                t += 0.5f;
                float[] d = cap.StreamDrain();
                foreach (float f in d) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(f * 32767f), -32768, 32767));
                written += d.Length;
            }
            cap.StreamEnd();
            float[] rest = cap.StreamDrain();
            foreach (float f in rest) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(f * 32767f), -32768, 32767));
            written += rest.Length;
            WriteI16Header(w, sr, written);
        }
        pw.debugInput = false; pw.debugMove = Vector2.zero;
        File.AppendAllText(tl, TimelineRow(secs) + "\nmuestras " + written + " (" + (written / (float)sr).ToString("F1") + " s)\n");
    }

    AudioCapture cap;
    void LateUpdate()
    {   // siempre con el filtro silenciador en el oyente activo (el oyente cambia al cargar niveles)
        AudioListener al = null;
        foreach (AudioListener x in Object.FindObjectsOfType<AudioListener>()) if (x.enabled && x.gameObject.activeInHierarchy) { al = x; if (x.gameObject.name == "CapListener") break; }
        if (al == null) { AudioListener.pause = true; return; }
        // (v7.3.0b2) si el oyente comparte objeto con un AudioSource (PlayerCamera lleva el TapeHum), Unity engancha el filtro a esa
        // fuente y no a la mezcla. En vez de mover la fuente, se mueve el OYENTE a un hijo vacío en la misma posición ("CapListener")
        if (al.GetComponent<AudioSource>() != null && al.gameObject.name != "CapListener")
        {
            Transform ch = al.transform.Find("CapListener");
            if (ch == null) { GameObject go = new GameObject("CapListener"); go.transform.SetParent(al.transform, false); ch = go.transform; }
            AudioListener nl = ch.GetComponent<AudioListener>(); if (nl == null) nl = ch.gameObject.AddComponent<AudioListener>();
            al.enabled = false; nl.enabled = true; al = nl;
        }
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
            if (longStill > 0f)
            {
                while (cap == null) yield return null;
                FirstPersonController pl = FirstPersonController.LocalInstance;
                string pre = "L" + lv + (vhsOff ? "_sinvhs" : "_convhs");
                yield return LongRecord(pre + "_quieto", longStill, sr, pl, false);
                if (longMove > 0f) yield return LongRecord(pre + "_andando", longMove, sr, pl, true);
                File.AppendAllText(rep, pre + " " + LevelConfig.Get(lv).name + " callbacks=" + cap.Callbacks + "\n" + AudioCapture.FilterReaders());
                continue;
            }
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
