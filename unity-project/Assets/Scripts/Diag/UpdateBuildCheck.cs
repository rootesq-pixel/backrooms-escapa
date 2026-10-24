// v7.5.0: «-updatetest <carpeta>» en el ejecutable (junto con «-updatefeed http://127.0.0.1:…/latest.json», un servidor falso):
// prueba de extremo a extremo del auto-actualizador sobre una COPIA temporal del juego. Primera instancia: espera al aviso, lo
// captura, pulsa «Actualizar», captura la barra de progreso y deja que el ayudante sustituya los archivos y reabra el juego.
// La instancia reabierta (mismos argumentos) encuentra _update_result.txt, lo anota, captura el menú y se cierra.
// Con un zip dañado: captura el error y se cierra sin tocar nada. Nunca se ejecuta sin «-updatetest».
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public class UpdateBuildCheck : MonoBehaviour
{
    string dir; readonly StringBuilder rep = new StringBuilder(); string tag;
    public static void MaybeStart(GameObject host)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == "-updatetest") { UpdateBuildCheck c = host.AddComponent<UpdateBuildCheck>(); c.dir = a[i + 1]; }
    }
    void Info(string m) { rep.AppendLine(m); Debug.Log("[UpdateBuildCheck] " + m); }
    void Flush() { File.WriteAllText(Path.Combine(dir, "updatetest_" + tag + ".txt"), rep.ToString()); }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        bool relaunched = AutoUpdater.StartupResult != null;
        tag = relaunched ? "reabierto_" + System.DateTime.Now.ToString("HHmmss") : "inicio_" + System.DateTime.Now.ToString("HHmmss");
        Info("juego " + GameVersion.Label + " en " + AutoUpdater.GameDir + "  pid " + System.Diagnostics.Process.GetCurrentProcess().Id);
        Info("feed " + AutoUpdater.FeedOverride + "  resultado anterior: " + (AutoUpdater.StartupResult ?? "(ninguno)"));
        float t0 = Time.realtimeSinceStartup;
        while ((GameBootstrap.Instance == null || UIManager.Instance == null || !GameBootstrap.Instance.InMenu || UIManager.Instance.Current != UIManager.Screen.Main) && Time.realtimeSinceStartup - t0 < 120f) yield return null;
        yield return new WaitForSecondsRealtime(1.5f);
        UIManager ui = UIManager.Instance; AutoUpdater up = ui.Updater;
        if (relaunched)
        {
            Info("REABIERTO tras la actualización: " + AutoUpdater.StartupResult + "  estado " + up.Current + "  diálogo " + up.DialogOpen);
            yield return new WaitForSecondsRealtime(1f);
            Shot(ui, AutoUpdater.StartupResult.StartsWith("ok|") ? "menu_actualizado" : "dialogo_vuelta_atras");
            Info("END"); Flush(); Application.Quit(0); yield break;
        }
        t0 = Time.realtimeSinceStartup;
        while (up.Current != AutoUpdater.State.Available && up.Current != AutoUpdater.State.UpToDate && Time.realtimeSinceStartup - t0 < 30f) yield return null;
        Info("consulta: " + up.Current + (up.Latest != null ? "  " + up.Latest.tag + " «" + up.Latest.title + "» " + up.Latest.zipName + " " + up.Latest.zipSize + " B sha " + up.Latest.sha256 : ""));
        if (up.Current != AutoUpdater.State.Available) { Info("FAIL sin versión nueva"); Info("END"); Flush(); Application.Quit(1); yield break; }
        yield return new WaitForSecondsRealtime(0.5f);
        Info("diálogo abierto: " + up.DialogOpen);
        Shot(ui, "dialogo_aviso");
        bool progShot = false;
        AutoUpdater.OnBeforeQuit = () => BeforeQuit(ui);
        up.ClickUpdateForTests();
        t0 = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - t0 < 300f)
        {
            if (!progShot && up.Current == AutoUpdater.State.Downloading && up.Progress > 0.3f && up.Progress < 0.8f) { progShot = true; Shot(ui, "dialogo_progreso"); Info("progreso capturado al " + Mathf.FloorToInt(up.Progress * 100f) + " %"); }
            if (up.Current == AutoUpdater.State.Error) break;
            yield return null;
        }
        if (up.Current == AutoUpdater.State.Error)
        {
            Info("ERROR mostrado: " + up.LastError.Replace("\n", " / "));
            yield return new WaitForSecondsRealtime(0.5f);
            Shot(ui, "dialogo_error");
            Info("carpeta _update tras el error: " + Directory.Exists(Path.Combine(AutoUpdater.GameDir, UpdateCore.WorkDirName)));
            Info("END"); Flush(); Application.Quit(0); yield break;
        }
        Info("FAIL tiempo agotado en estado " + up.Current); Info("END"); Flush(); Application.Quit(1);
    }
    IEnumerator BeforeQuit(UIManager ui)
    {
        Shot(ui, "dialogo_instalando");
        Info("ayudante lanzado; el juego se cierra. Nuevo en _update/nuevo: " + Directory.Exists(Path.Combine(AutoUpdater.GameDir, UpdateCore.WorkDirName, "nuevo")));
        Info("END"); Flush();
        yield break;
    }
    void Shot(UIManager ui, string name)
    {
        Camera cam = Camera.main; if (cam == null) { Info("sin cámara para " + name); return; }
        VhsEffect v = cam.GetComponent<VhsEffect>(); if (v != null) v.intensityOverride = 0f;
        Texture2D t = ui.CaptureMenu(cam, 1920, 1080);
        int w = t.width, h = t.height; Color32[] px = t.GetPixels32();
        byte[] head = Encoding.ASCII.GetBytes("P6\n" + w + " " + h + "\n255\n");
        byte[] b = new byte[head.Length + w * h * 3]; System.Buffer.BlockCopy(head, 0, b, 0, head.Length);
        int o = head.Length;
        for (int y = h - 1; y >= 0; y--) for (int x = 0; x < w; x++) { Color32 q = px[y * w + x]; b[o++] = q.r; b[o++] = q.g; b[o++] = q.b; }
        File.WriteAllBytes(Path.Combine(dir, tag + "_" + name + ".ppm"), b);
        Destroy(t);
        Info("captura " + name);
    }
}
