using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.12.0: capturas reales del Nivel 188 (El Patio de las Ventanas) dentro del juego (xvfb con gráficos):
// patio desde el ascensor, fachadas, vista alta, ventana abierta (otro nivel), ventana con sol (salida), la Ventana
// (desde el patio y desde el pasillo por la puerta entornada), pasillo, habitación con la cortina descorrida, escalera
// y un Periférico de reojo. Salida en $V7120_OUT (por defecto /workspace/v7120/capturas).
//   V7120_OUT=<dir> xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath <p> -executeMethod V7120Shots.Export -force-glcore
[InitializeOnLoad]
public static class V7120Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V7120_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v7120/capturas" : o; } }
    static int Seed { get { int s; return int.TryParse(System.Environment.GetEnvironmentVariable("V7120_SEED"), out s) ? s : 188; } }
    static V7120Shots() { if (SessionState.GetString("smoke_phase", "") != "v7120shots") return; EditorApplication.update += Tick; }
    // en el PC del usuario (editor de Unity con su GPU): menú Backrooms > Capturas Nivel 188 (4K). Salida: <proyecto>/Capturas188
    [MenuItem("Backrooms/Capturas Nivel 188 (4K)")]
    public static void ExportMenu4K()
    {
        System.Environment.SetEnvironmentVariable("V7120_RES", "3840x2160");
        System.Environment.SetEnvironmentVariable("V7120_OUT", System.IO.Path.GetFullPath("Capturas188"));
        Export();
    }
    static int ResW { get { string r = System.Environment.GetEnvironmentVariable("V7120_RES"); int w; return r != null && r.Contains("x") && int.TryParse(r.Split('x')[0], out w) ? w : 1920; } }
    static int ResH { get { string r = System.Environment.GetEnvironmentVariable("V7120_RES"); int h; return r != null && r.Contains("x") && int.TryParse(r.Split('x')[1], out h) ? h : 1080; } }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v7120shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V7120Shots] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { Time.timeScale = 1f; SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; if (Application.isBatchMode) EditorApplication.Exit(0); else { EditorApplication.isPlaying = false; EditorUtility.RevealInFinder(Out); } }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V7120Shots] " + s); }

    private static void Shot(string name, Vector3 eye, Vector3 at, float fov = 0f)
    {
        int w = ResW, h = ResH;
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, Application.isBatchMode ? 1 : 4);
        Camera game = Camera.main;
        Vector3 p0 = game.transform.position; Quaternion r0 = game.transform.rotation; float f0 = game.fieldOfView;
        game.transform.position = eye; game.transform.rotation = Quaternion.LookRotation(at - eye);
        if (fov > 0f) game.fieldOfView = fov;
        RenderTexture o = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = o;
        game.transform.position = p0; game.transform.rotation = r0; game.fieldOfView = f0;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
        Log("captura " + name + " (ojo " + eye.ToString("F1") + ")");
    }

    // el jugador se coloca donde está la cámara (luz del patio/interior y plantas visibles según dónde está)
    static IEnumerator Go(FirstPersonController p, Vector3 feet, float yaw, float wait = 0.8f)
    {
        p.Teleport(feet, yaw); yield return wait;
    }
    static Vector3 Lw(W188World w, int s, float u, float y, float d) { return w.levelRoot.TransformPoint(W188Plan.ToLevel(s, u, y, d)); }

    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", "");
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        GameSettings.Quality = 2; yield return 0.3f;
        Log("versión " + GameVersion.Current + ", gráficos " + SystemInfo.graphicsDeviceType + ", " + System.DateTime.Now.ToString("u"));
        gb.useRandomSeed = false; gb.seed = Seed - 10 * 101;
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(10);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu && W188World.Active != null) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player;
        if (Health.Instance != null) Health.DisableForTests = true;
        W188Events.NoGrabForTests = true;
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
        Flashlight fl = gm.player.GetComponentInChildren<Flashlight>(); if (fl != null) fl.SetOn(false);   // linterna apagada: se ve la luz del nivel
        W188World w = W188World.Active; W188Events ev = W188Events.Active; W188Plan P = w.plan;
        Log("semilla " + gm.generator.UsedSeed + ", salida planta " + P.exitF + " lado " + P.exitS + " módulo " + P.exitI + ", " + w.Figures.Count + " Ventanas");
        Transform R = w.levelRoot;
        Vector3 cc = R.TransformPoint(new Vector3(W188Plan.R + W188Plan.NX * W188Plan.M * 0.5f, 0f, W188Plan.R + W188Plan.NZ * W188Plan.M * 0.5f));
        float eyeH = 1.62f;

        // 1) al salir del ascensor
        Shot("01_llegada_ascensor", p.transform.position + Vector3.up * eyeH, p.transform.position + Vector3.up * (eyeH + 0.6f) + p.transform.forward * 10f, 70f);
        // 2) en el patio mirando las fachadas
        Vector3 c1 = cc + R.TransformDirection(new Vector3(-3f, 0f, -12f)) + Vector3.up * 0.1f;
        IEnumerator g = Go(p, c1, 0f); while (g.MoveNext()) yield return g.Current;
        Shot("02_patio_fachadas", c1 + Vector3.up * eyeH, c1 + Vector3.up * 7f + R.TransformDirection(new Vector3(4f, 0f, 14f)), 70f);
        Shot("03_patio_cielo", c1 + Vector3.up * eyeH, c1 + Vector3.up * 16f + R.TransformDirection(new Vector3(9f, 0f, 9f)), 78f);
        // 4) vista alta desde una esquina del patio
        Vector3 hi = R.TransformPoint(new Vector3(W188Plan.R + 0.8f, 9.5f, W188Plan.R + 0.8f));
        Shot("04_patio_vista_alta", hi, cc + Vector3.up * 3f, 70f);
        // 5) una ventana abierta (muestra otro nivel)
        if (P.Opens.Count > 0)
        {
            Vector3Int o = P.Opens[0]; int f = o.x, s = o.y, i = o.z;
            Vector3 wc = Lw(w, s, W188Plan.UC(i), f * W188Plan.FH + 1.5f, 0f);
            Vector3 eye = Lw(w, s, W188Plan.UC(i) + 0.6f, Mathf.Max(eyeH, f * W188Plan.FH + 1.3f), -3.2f);
            Shot("05_ventana_abierta_nivel" + P.variant[f, s, i], eye, wc, 55f);
        }
        // 6) la ventana con sol (la salida) desde el patio y desde dentro del cuarto
        {
            int f = P.exitF, s = P.exitS, i = P.exitI;
            Vector3 wc = Lw(w, s, W188Plan.UC(i), f * W188Plan.FH + 1.5f, 0f);
            Shot("06_ventana_salida_desde_patio", Lw(w, s, W188Plan.UC(i) - 1.5f, f * W188Plan.FH + 1.0f, -5.5f), wc, 50f);
            Vector3 inRoom = w.ExitRoomWorld(3.2f, -0.4f);
            if (ExitLock.Active != null) { Inventory.Instance.Add(ExitLock.Active.CorrectKey, 1); ExitLock.Active.ResetCooldownForTests(); }
            g = Go(p, w.ExitDoorWorld(W188Plan.CorrIn + 0.6f), W188World.YawToCourt(s), 2.5f); while (g.MoveNext()) yield return g.Current;   // la llave buena abre la puerta
            Shot("07_salida_por_la_puerta", w.ExitDoorWorld(W188Plan.CorrIn + 0.6f) + Vector3.up * (eyeH - 0.1f), wc, 70f);
            Shot("08_salida_dentro", inRoom + Vector3.up * (eyeH - 0.1f), wc, 70f);
            Shot("08b_salida_ventana_cerca", w.ExitRoomWorld(1.4f, 0.2f) + Vector3.up * (eyeH - 0.1f), wc + Vector3.up * 0.1f, 60f);
        }
        // 7) la Ventana: desde el patio y desde el pasillo, por la puerta entornada
        if (w.Figures.Count > 0)
        {
            W188World.Figure fg = w.Figures[0];
            Vector3 wc = Lw(w, fg.s, W188Plan.UC(fg.i), fg.f * W188Plan.FH + 1.4f, 0.4f);
            g = Go(p, c1, 0f); while (g.MoveNext()) yield return g.Current;
            yield return 1.0f;
            Shot("09_la_ventana_desde_patio", Lw(w, fg.s, W188Plan.UC(fg.i) + 1.0f, Mathf.Max(eyeH, fg.f * W188Plan.FH + 1.2f), -3.0f), wc, 45f);
            Vector3 corr = w.DoorWorld(fg.f, fg.s, fg.i, W188Plan.CorrIn + 0.75f);
            g = Go(p, corr, W188World.YawToCourt(fg.s), 3.5f); while (g.MoveNext()) yield return g.Current;   // la figura se gira hacia el jugador
            Vector3 eyeD = w.CellWorld(fg.f, fg.s, fg.i, 1.05f, 3.55f) + Vector3.up * (eyeH - 0.1f);   // (solo la cámara entra en el cuarto)
            Shot("10_la_ventana_dentro_de_su_cuarto", eyeD, fg.root.position + Vector3.up * 1.2f, 62f);
        }
        // 8) pasillo de una planta alta y habitación con la cortina descorrida
        {
            Vector3 cp = Lw(w, W188Plan.E, 6.0f, 2 * W188Plan.FH + 0.1f, 5.15f);
            g = Go(p, cp, 0f); while (g.MoveNext()) yield return g.Current;
            Vector3 along = R.TransformDirection(W188World.LevelDir(W188Plan.E, 1f, 0f));
            Shot("11_pasillo_planta2", cp + Vector3.up * eyeH, cp + Vector3.up * (eyeH - 0.1f) + along * 10f, 70f);
            if (w.Curtains.Count > 0)
            {
                W188World.Curtain ct = w.Curtains[0];
                Vector3 inside = w.CellWorld(ct.f, ct.s, ct.i, 0.9f, 2.9f);
                g = Go(p, inside, W188World.YawToCourt(ct.s)); while (g.MoveNext()) yield return g.Current;
                Shot("12_habitacion_cortina", inside + Vector3.up * eyeH, ct.center, 70f);
                ct.busy = true; yield return 1.2f;
                Shot("13_cortina_descorrida", inside + Vector3.up * eyeH, ct.center, 70f);
                ct.busy = false;
            }
        }
        // 9) escalera de la esquina SO
        {
            Vector3 st = Lw(w, W188Plan.S, -3.35f, 0.1f, 5.0f);
            g = Go(p, st, 0f); while (g.MoveNext()) yield return g.Current;
            Shot("14_escalera", st + Vector3.up * eyeH, Lw(w, W188Plan.S, -3.35f, 1.6f, 1.2f), 72f);
        }
        // 9b) multijugador: compañeros remotos (RemotePlayer, el avatar que ven los demás jugadores) en el patio y en el pasillo
        {
            g = Go(p, c1, 0f); while (g.MoveNext()) yield return g.Current;
            Vector3 fw = R.TransformDirection(Vector3.forward), rt = R.TransformDirection(Vector3.right);
            RemotePlayer a = RemotePlayer.Create(gm.transform, 91, "Ana", StepKind.Concrete), b = RemotePlayer.Create(gm.transform, 92, "Leo", StepKind.Concrete);
            Vector3 pa = c1 + fw * 3.4f - rt * 0.9f, pb = c1 + fw * 6.0f + rt * 1.6f;
            float yb = Mathf.Atan2(fw.x, fw.z) * Mathf.Rad2Deg;
            float tt = 0f;
            while (tt < 2.5f) { a.Push(pa, yb + 180f, 0f, 0f, 0); b.Push(pb - rt * tt * 1.3f, yb - 90f, -25f, 1.3f, 0); yield return 0.05f; tt += 0.05f; }
            Shot("16_multijugador_patio", c1 + Vector3.up * eyeH - fw * 0.5f, c1 + fw * 6f + Vector3.up * 2.4f, 70f);
            // en el pasillo de la salida, los dos delante de la puerta de la ventana con sol
            Vector3 dw = w.ExitDoorWorld(W188Plan.CorrIn + 0.9f);
            Vector3 along = R.TransformDirection(W188World.LevelDir(P.exitS, 1f, 0f)), outw = R.TransformDirection(W188World.LevelDir(P.exitS, 0f, 1f));
            Vector3 me = dw + along * 3.2f;
            g = Go(p, me, Mathf.Atan2(-along.x, -along.z) * Mathf.Rad2Deg); while (g.MoveNext()) yield return g.Current;
            tt = 0f;
            float ya = Mathf.Atan2(-outw.x, -outw.z) * Mathf.Rad2Deg, yl = Mathf.Atan2(along.x, along.z) * Mathf.Rad2Deg;
            while (tt < 2.0f) { a.Push(dw + along * 0.3f, ya, 0f, 0f, RemotePlayer.FlagFlash); b.Push(dw + along * 1.3f, yl, 0f, 0f, RemotePlayer.FlagFlash); yield return 0.05f; tt += 0.05f; }
            Shot("17_multijugador_pasillo_salida", me + Vector3.up * eyeH, dw + Vector3.up * 1.2f - outw * 0.3f, 70f);
            Object.Destroy(a.gameObject); Object.Destroy(b.gameObject);
        }
        // 10) un Periférico, de reojo, en el patio
        {
            g = Go(p, c1, 0f, 0.5f); while (g.MoveNext()) yield return g.Current;
            W188Events.DisableForTests = false;
            ev.ForcePeripheral();
            float t = 0f; while (!ev.PeripheralVisible && t < 6f) { ev.ForcePeripheral(); yield return 0.1f; t += 0.1f; }
            if (ev.PeripheralVisible)
            {
                Vector3 eye = p.transform.position + Vector3.up * eyeH, pp = ev.PeripheralPos + Vector3.up * 1.2f;
                Vector3 to = pp - eye; to.y = 0f; Vector3 fwd = Quaternion.Euler(0f, Mathf.Sign(Vector3.SignedAngle(p.transform.forward, to, Vector3.up)) * -38f, 0f) * to.normalized;
                Shot("15_periferico_de_reojo", eye, eye + fwd * 10f, 70f);
            }
            else Log("periférico: no apareció");
            W188Events.DisableForTests = true;
        }
        Log("FIN");
    }
}
