// v7.12.0: capturas del Nivel 188 EN EL EJECUTABLE (GPU real), mismos encuadres que V7120Shots (W188ShotPlan). Solo con
// «-w188shots <carpeta>» [-w188res 3840x2160] [-w188seed 188] [-perf]. PNG propios (el juego no lleva ImageConversion), informe
// report.txt, restaura la preferencia de calidad y cierra. Pensado para -batchmode (sin ventana) con -screen-width/-height.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

public class W188BuildShots : MonoBehaviour
{
    string dir; int resW = 1920, resH = 1080, seed = 188; bool perf;
    int savedQuality = -2;
    readonly StringBuilder rep = new StringBuilder();

    public static void MaybeStart(GameObject host)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        string d = null;
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-w188shots") d = a[i + 1];
        if (d == null) return;
        W188BuildShots c = host.AddComponent<W188BuildShots>(); c.dir = d;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == "-perf") c.perf = true;
            if (i < a.Length - 1 && a[i] == "-w188res") { string[] r = a[i + 1].Split('x'); int w, h; if (r.Length == 2 && int.TryParse(r[0], out w) && int.TryParse(r[1], out h)) { c.resW = Mathf.Clamp(w, 320, 7680); c.resH = Mathf.Clamp(h, 240, 4320); } }
            if (i < a.Length - 1 && a[i] == "-w188seed") int.TryParse(a[i + 1], out c.seed);
        }
    }

    void Log(string s) { rep.AppendLine(s); Debug.Log("[W188BuildShots] " + s); try { File.WriteAllText(Path.Combine(dir, "report.txt"), rep.ToString()); } catch { } }
    void RestoreQuality()
    {
        if (savedQuality == -2) return;
        if (savedQuality < 0) PlayerPrefs.DeleteKey("opt_quality"); else PlayerPrefs.SetInt("opt_quality", savedQuality);
        PlayerPrefs.Save(); savedQuality = -2;
    }
    void OnApplicationQuit() { RestoreQuality(); }

    IEnumerator Watchdog() { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < 900f) yield return null; Log("FAIL tiempo agotado (900 s)"); Finish(3); }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        StartCoroutine(Watchdog());
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance; float t0 = Time.realtimeSinceStartup;
        while ((gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) && Time.realtimeSinceStartup - t0 < 240f) { yield return null; gb = GameBootstrap.Instance; }
        yield return Wait(1f);
        savedQuality = PlayerPrefs.HasKey("opt_quality") ? PlayerPrefs.GetInt("opt_quality") : -1;
        GameSettings.Quality = 2; yield return Wait(0.3f);
        Log("versión " + GameVersion.Current + ", " + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName + ", CPU " + SystemInfo.processorType + ", pantalla " + Screen.width + "x" + Screen.height + ", batch " + Application.isBatchMode + ", capturas " + resW + "x" + resH + " MSAA 8, " + System.DateTime.Now.ToString("u"));
        gb.useRandomSeed = false; gb.seed = seed - 10 * 101;
        int ld = gb.LoadCount; float tl = Time.realtimeSinceStartup;
        UIManager.Instance.StartGame(10);
        while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu && W188World.Active != null) && Time.realtimeSinceStartup - tl < 240f) yield return null;
        Log("nivel 10 cargado en " + (Time.realtimeSinceStartup - tl).ToString("F1") + " s");
        float wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return Wait(0.25f); wt += 0.25f; }
        yield return Wait(1.5f);
        W188ShotPlan.ShotFn = Shot; W188ShotPlan.LogFn = Log;
        IEnumerator v = W188ShotPlan.Views();
        while (true)
        {
            bool more;
            try { more = v.MoveNext(); } catch (System.Exception e) { Log("EXC " + e); break; }
            if (!more) break;
            if (v.Current is float) yield return Wait((float)v.Current); else yield return null;
        }
        if (perf) yield return PerfAll();
        Log("FIN");
        Finish(0);
    }
    static IEnumerator Wait(float s) { float t0 = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t0 < s) yield return null; }

    void Shot(string name, Vector3 eye, Vector3 at, float fov)
    {
        Camera c = Camera.main; if (c == null) { Log("sin cámara: " + name); return; }
        RenderTexture rt = RenderTexture.GetTemporary(resW, resH, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 8);
        Vector3 p0 = c.transform.position; Quaternion r0 = c.transform.rotation; float f0 = c.fieldOfView;
        c.transform.position = eye; c.transform.rotation = Quaternion.LookRotation(at - eye); if (fov > 0f) c.fieldOfView = fov;
        RenderTexture o = c.targetTexture; c.targetTexture = rt; c.Render(); c.targetTexture = o;
        c.transform.position = p0; c.transform.rotation = r0; c.fieldOfView = f0;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D t = new Texture2D(resW, resH, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, resW, resH), 0, 0); t.Apply();
        RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), Png(t.GetRawTextureData(), resW, resH)); Destroy(t);
        Log("captura " + name + " (ojo " + eye.ToString("F1") + ")");
    }

    // PNG RGB 8 bits mínimo: filas de abajo arriba (Unity) -> de arriba abajo, filtro 0, zlib (Deflate + Adler-32)
    static byte[] Png(byte[] rgb, int w, int h)
    {
        byte[] raw = new byte[(w * 3 + 1) * h];
        for (int y = 0; y < h; y++) { int src = (h - 1 - y) * w * 3, dst = y * (w * 3 + 1); raw[dst] = 0; System.Buffer.BlockCopy(rgb, src, raw, dst + 1, w * 3); }
        byte[] z;
        using (MemoryStream ms = new MemoryStream())
        {
            ms.WriteByte(0x78); ms.WriteByte(0x9C);
            using (DeflateStream ds = new DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, true)) ds.Write(raw, 0, raw.Length);
            uint a = 1, b = 0; for (int i = 0; i < raw.Length; i++) { a = (a + raw[i]) % 65521; b = (b + a) % 65521; }
            uint ad = (b << 16) | a; ms.WriteByte((byte)(ad >> 24)); ms.WriteByte((byte)(ad >> 16)); ms.WriteByte((byte)(ad >> 8)); ms.WriteByte((byte)ad);
            z = ms.ToArray();
        }
        using (MemoryStream o = new MemoryStream())
        {
            o.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            byte[] ih = new byte[13]; BE(ih, 0, (uint)w); BE(ih, 4, (uint)h); ih[8] = 8; ih[9] = 2; ih[10] = 0; ih[11] = 0; ih[12] = 0;
            Chunk(o, "IHDR", ih); Chunk(o, "IDAT", z); Chunk(o, "IEND", new byte[0]);
            return o.ToArray();
        }
    }
    static void BE(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
    static uint[] crcT;
    static uint Crc(byte[] t, byte[] d)
    {
        if (crcT == null) { crcT = new uint[256]; for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1; crcT[n] = c; } }
        uint r = 0xFFFFFFFFu; foreach (byte x in t) r = crcT[(r ^ x) & 0xFF] ^ (r >> 8); foreach (byte x in d) r = crcT[(r ^ x) & 0xFF] ^ (r >> 8); return r ^ 0xFFFFFFFFu;
    }
    static void Chunk(Stream o, string type, byte[] d)
    {
        byte[] l = new byte[4]; BE(l, 0, (uint)d.Length); o.Write(l, 0, 4);
        byte[] t = Encoding.ASCII.GetBytes(type); o.Write(t, 0, 4); o.Write(d, 0, d.Length);
        byte[] c = new byte[4]; BE(c, 0, Crc(t, d)); o.Write(c, 0, 4);
    }

    // FPS: cada fotograma = bucle del juego + render de la cámara a 1920x1080 (MSAA de la calidad) + espera a la GPU (1 píxel)
    IEnumerator PerfAll()
    {
        W188World w = W188World.Active; GameManager gm = GameManager.Instance; if (w == null || gm == null) yield break;
        FirstPersonController p = gm.player; Camera mc = Camera.main; Transform R = w.levelRoot;
        Vector3 cc = R.TransformPoint(new Vector3(W188Plan.R + W188Plan.NX * W188Plan.M * 0.5f, 0f, W188Plan.R + W188Plan.NZ * W188Plan.M * 0.5f));
        Vector3 c1 = cc + R.TransformDirection(new Vector3(-3f, 0f, -12f)) + Vector3.up * 0.1f;
        yield return Perf(p, mc, "patio", c1 + Vector3.up * 1.62f, c1 + Vector3.up * 7f + R.TransformDirection(new Vector3(4f, 0f, 14f)), 70f, c1);
        Vector3 cp = W188ShotPlan.Lw(w, W188Plan.E, 6.0f, 2 * W188Plan.FH + 0.1f, 5.15f);
        Vector3 along = R.TransformDirection(W188World.LevelDir(W188Plan.E, 1f, 0f));
        yield return Perf(p, mc, "pasillo_planta2", cp + Vector3.up * 1.62f, cp + Vector3.up * 1.52f + along * 10f, 70f, cp);
        if (w.Figures.Count > 0)
        {
            W188World.Figure fg = w.Figures[0];
            yield return Perf(p, mc, "cuarto_de_la_ventana", w.CellWorld(fg.f, fg.s, fg.i, -0.85f, 3.45f) + Vector3.up * 1.52f, fg.root.position + Vector3.up * 1.25f, 62f, c1);
        }
    }
    IEnumerator Perf(FirstPersonController pw, Camera mc, string n, Vector3 p, Vector3 t, float fov, Vector3 stand)
    {
        pw.Teleport(stand, Mathf.Atan2(t.x - p.x, t.z - p.z) * Mathf.Rad2Deg);
        yield return Wait(0.8f);
        int vs0 = QualitySettings.vSyncCount, tf0 = Application.targetFrameRate; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
        RenderTexture rt = new RenderTexture(1920, 1080, 24); rt.antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing); rt.Create();
        Texture2D one = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        bool en = mc.enabled; mc.enabled = false;
        var ft = new List<double>(); var sw = new System.Diagnostics.Stopwatch();
        for (int k = 0; k < 960; k++)
        {
            mc.transform.position = p; mc.transform.rotation = Quaternion.LookRotation(t - p); mc.fieldOfView = fov;
            mc.targetTexture = rt; mc.Render(); mc.targetTexture = null;
            RenderTexture.active = rt; one.ReadPixels(new Rect(0, 0, 1, 1), 0, 0, false); RenderTexture.active = null;
            if (k >= 60) ft.Add(sw.Elapsed.TotalMilliseconds);
            yield return null;
            sw.Restart();
        }
        mc.enabled = en; QualitySettings.vSyncCount = vs0; Application.targetFrameRate = tf0;
        Destroy(one); rt.Release(); Destroy(rt);
        double sum = 0; foreach (double x in ft) sum += x; double mean = sum / ft.Count;
        ft.Sort(); double p99 = ft[(int)(ft.Count * 0.99)];
        int worst = Mathf.Max(1, ft.Count / 100); double ws = 0; for (int i = ft.Count - worst; i < ft.Count; i++) ws += ft[i];
        Log("perf " + n + " calidad " + GameSettings.Quality + " 1920x1080 AA " + rt.antiAliasing + ": media " + mean.ToString("F2") + " ms = " + (1000.0 / mean).ToString("F0") + " FPS, 1% bajo " + (1000.0 / (ws / worst)).ToString("F0")
            + " FPS (p99 " + p99.ToString("F2") + " ms), mediana " + ft[ft.Count / 2].ToString("F2") + " ms, " + ft.Count + " fotogramas, " + SystemInfo.graphicsDeviceName);
    }

    void Finish(int code)
    {
        RestoreQuality();
        try { File.WriteAllText(Path.Combine(dir, "report.txt"), rep.ToString()); } catch { }
        Application.Quit(code);
    }
}
