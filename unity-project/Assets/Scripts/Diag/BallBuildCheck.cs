// v7.1.2: prueba de las bolas EN EL EJECUTABLE (no en el editor). Solo se activa con «-ballcheck <carpeta>» (y -level 6 -nointro).
// Hace capturas desde una cámara fija en el borde de una piscina y desde los ojos del jugador mientras vadea, mide
// (1) que las bolas instanciadas se dibujan de verdad (comparando con las bolas ocultas), (2) que se mecen solas y
// (3) que se apartan alrededor del jugador, escribe ballcheck.txt con RESULT y cierra el juego.
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

public class BallBuildCheck : MonoBehaviour
{
    string dir; readonly StringBuilder rep = new StringBuilder(); int pass, fail;

    public static void MaybeStart(GameObject host)
    {
        string[] a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++)
            if (a[i] == "-ballcheck") { BallBuildCheck c = host.AddComponent<BallBuildCheck>(); c.dir = a[i + 1]; }
    }

    void Check(bool ok, string m) { if (ok) pass++; else fail++; rep.AppendLine((ok ? "PASS " : "FAIL ") + m); Debug.Log("[BallBuildCheck] " + (ok ? "PASS " : "FAIL ") + m); }
    void Info(string m) { rep.AppendLine("INFO " + m); Debug.Log("[BallBuildCheck] INFO " + m); }

    IEnumerator Start()
    {
        Directory.CreateDirectory(dir);
        float t0 = Time.realtimeSinceStartup;
        while ((GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded || BallPitField.All.Count == 0 || FirstPersonController.LocalInstance == null) && Time.realtimeSinceStartup - t0 < 300f) yield return null;
        if (BallPitField.All.Count == 0) { Check(false, "nivel 6 cargado con piscinas de bolas"); Finish(); yield break; }
        BallPitLurker.DisabledForTests = true;
        foreach (MonsterAI m in MonsterAI.Instances) if (m != null) m.Stop();
        yield return new WaitForSeconds(1.0f);
        BallPitField f0 = BallPitField.All[0]; BallpitSetPiece.PitJ pit = f0.pits[0]; Transform tr = f0.transform;
        Rect inn = BallpitSetPiece.PitInner(pit, f0.rim);
        FirstPersonController pw = FirstPersonController.LocalInstance;
        Material bm = BallPitField.SharedMaterial;
        Info("GPU " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType + ", instancing " + SystemInfo.supportsInstancing + ", calidad " + GameSettings.Quality);
        Check(bm != null && bm.shader != null && bm.shader.name == "Backrooms/BallInstanced" && bm.enableInstancing, "material de bolas con el shader BallInstanced e instancing (" + (bm != null && bm.shader != null ? bm.shader.name : "-") + ")");

        // cámara fija: de pie en el borde, mirando la piscina hacia abajo (como la captura del usuario)
        Vector3 edge = tr.TransformPoint(new Vector3(inn.xMin - 0.25f, pit.ball + 1.55f, inn.center.y));
        Vector3 look = tr.TransformPoint(new Vector3(inn.xMin + 1.6f, pit.ball, inn.center.y));
        Vector3 wade = tr.TransformPoint(new Vector3(inn.xMin + 1.6f, pit.floor + 0.05f, inn.center.y));
        Vector3 away = tr.TransformPoint(new Vector3(inn.xMin - 1.2f, 0.05f, inn.center.y - 3.0f));
        GameObject cg = new GameObject("BallCheckCam"); Camera cam = cg.AddComponent<Camera>();
        Camera mc = Camera.main; if (mc != null) { cam.CopyFrom(mc); }
        cam.enabled = false; cam.fieldOfView = 70f; cam.transform.position = edge; cam.transform.rotation = Quaternion.LookRotation(look - edge);
        pw.Teleport(away, 90f);
        yield return new WaitForSeconds(1.0f);
        Texture2D a0 = Grab(cam, "fixed_idle_a");
        yield return new WaitForSeconds(0.7f);
        Texture2D a1 = Grab(cam, "fixed_idle_b");
        BallPitField[] fs = BallPitField.All.ToArray();   // OnDisable quita el campo de All
        foreach (BallPitField f in fs) f.enabled = false;
        yield return null; yield return null;
        Texture2D hid = Grab(cam, "fixed_balls_hidden");
        foreach (BallPitField f in fs) f.enabled = true;
        yield return null;
        float drawn = Diff(a0, hid, null), idle = Diff(a0, a1, null);
        Check(drawn > 0.06f, "las bolas 3D se dibujan en el ejecutable (diferencia con las bolas ocultas " + drawn.ToString("F3") + " > 0.06)");
        Check(idle > 0.012f, "las bolas se mecen solas (diferencia entre dos fotogramas a 0,7 s sin nadie en la piscina " + idle.ToString("F4") + " > 0.012)");

        // vadeando: el jugador en la piscina; misma cámara fija, comparado con la piscina sin el jugador en el mismo instante aproximado
        pw.Teleport(wade, 90f);
        yield return new WaitForSeconds(1.2f);
        Vector3 pp = pw.transform.position;
        Texture2D w = Grab(cam, "fixed_wading");
        Vector3 sp = cam.WorldToViewportPoint(new Vector3(pp.x, BallPitField.SurfaceY(pp), pp.z));
        Vector3 sp2 = cam.WorldToViewportPoint(new Vector3(pp.x, BallPitField.SurfaceY(pp) + 0.55f, pp.z));
        float rad = Mathf.Abs(sp2.y - sp.y) * 1.2f + 0.03f;
        pw.Teleport(away, 90f);
        yield return new WaitForSeconds(1.5f);
        Texture2D w0 = Grab(cam, "fixed_after_leaving");
        Rect near = new Rect(sp.x - rad, sp.y - rad * 0.8f, rad * 2f, rad * 1.6f);
        Rect farR = new Rect(sp.x - rad, Mathf.Max(0.02f, sp.y - rad * 0.8f - 0.32f), rad * 2f, rad * 1.6f);
        float dn = Diff(w, w0, near), df = Diff(w, w0, farR);
        Info("jugador en " + pp + ", pantalla " + sp + ", radio " + rad.ToString("F3") + ", empujadores " + BallPitField.PusherCount);
        Check(dn > 0.05f && dn > df * 2.5f, "las bolas se apartan alrededor del jugador (diferencia cerca " + dn.ToString("F3") + " vs lejos " + df.ToString("F3") + ")");

        // desde los ojos del jugador: secuencia vadeando por la piscina (con ondas)
        Vector3 wdir = tr.TransformDirection(Vector3.right);
        for (int k = 0; k < 8; k++)
        {
            Vector3 p = wade + wdir * (k * 0.22f);
            pw.Teleport(p, Mathf.Atan2(wdir.x, wdir.z) * Mathf.Rad2Deg);
            yield return new WaitForSeconds(0.12f);
            if (mc != null)
            {
                Quaternion r0 = mc.transform.rotation;
                mc.transform.rotation = Quaternion.LookRotation(wdir + Vector3.down * 0.8f);
                Destroy(Grab(mc, "player_wading_" + k));
                mc.transform.rotation = r0;
            }
        }
        Check(BallPitField.RipplesEmitted > 0, "al vadear salen ondas (" + BallPitField.RipplesEmitted + ")");
        Finish();
    }

    Texture2D Grab(Camera c, string name)
    {
        const int W = 960, H = 540;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24); RenderTexture prev = c.targetTexture;
        c.targetTexture = rt; c.Render(); RenderTexture.active = rt;
        Texture2D t = new Texture2D(W, H, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
        RenderTexture.active = null; c.targetTexture = prev; RenderTexture.ReleaseTemporary(rt);
        WritePpm(t, Path.Combine(dir, name + ".ppm"));   // PPM sin compresión (el módulo de conversión de imágenes no está en el juego); se pasa a PNG fuera
        return t;
    }

    static void WritePpm(Texture2D t, string path)
    {
        int W = t.width, H = t.height; Color32[] px = t.GetPixels32();
        byte[] head = Encoding.ASCII.GetBytes("P6\n" + W + " " + H + "\n255\n");
        byte[] b = new byte[head.Length + W * H * 3]; System.Buffer.BlockCopy(head, 0, b, 0, head.Length);
        int o = head.Length;
        for (int y = H - 1; y >= 0; y--) for (int x = 0; x < W; x++) { Color32 c = px[y * W + x]; b[o++] = c.r; b[o++] = c.g; b[o++] = c.b; }
        File.WriteAllBytes(path, b);
    }

    // diferencia media (0..1) entre dos capturas, opcionalmente en un rectángulo de la vista (0..1)
    static float Diff(Texture2D a, Texture2D b, Rect? r)
    {
        Color32[] pa = a.GetPixels32(), pb = b.GetPixels32(); int W = a.width, H = a.height;
        Rect q = r ?? new Rect(0f, 0f, 1f, 1f);
        int x0 = Mathf.Clamp((int)(q.xMin * W), 0, W - 1), x1 = Mathf.Clamp((int)(q.xMax * W), 1, W), y0 = Mathf.Clamp((int)(q.yMin * H), 0, H - 1), y1 = Mathf.Clamp((int)(q.yMax * H), 1, H);
        double s = 0; int n = 0;
        for (int y = y0; y < y1; y++) for (int x = x0; x < x1; x++)
        {
            Color32 c1 = pa[y * W + x], c2 = pb[y * W + x];
            s += (Mathf.Abs(c1.r - c2.r) + Mathf.Abs(c1.g - c2.g) + Mathf.Abs(c1.b - c2.b)) / (3.0 * 255.0); n++;
        }
        return n > 0 ? (float)(s / n) : 0f;
    }

    void Finish()
    {
        rep.AppendLine("RESULT passed=" + pass + " failed=" + fail);
        File.WriteAllText(Path.Combine(dir, "ballcheck.txt"), rep.ToString());
        Debug.Log("[BallBuildCheck] RESULT passed=" + pass + " failed=" + fail);
        Application.Quit(fail == 0 ? 0 : 3);
    }
}
