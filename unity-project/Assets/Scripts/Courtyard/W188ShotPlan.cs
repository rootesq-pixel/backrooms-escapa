using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// v7.12.0: recorrido de capturas del Nivel 188 (mismos encuadres en el editor -V7120Shots- y en el ejecutable -W188BuildShots-).
// Las esperas se devuelven como float (segundos); quien lo conduce decide cómo esperar. Shot y Log los pone el conductor.
public static class W188ShotPlan
{
    public static System.Action<string, Vector3, Vector3, float> ShotFn;
    public static System.Action<string> LogFn;
    static void Shot(string name, Vector3 eye, Vector3 at, float fov = 0f) { if (ShotFn != null) ShotFn(name, eye, at, fov); }
    static void Log(string s) { if (LogFn != null) LogFn(s); }
    // el jugador se coloca donde está la cámara (luz del patio/interior y plantas visibles según dónde está)
    public static IEnumerator Go(FirstPersonController p, Vector3 feet, float yaw, float wait = 0.8f)
    {
        p.Teleport(feet, yaw); yield return wait;
    }
    public static Vector3 Lw(W188World w, int s, float u, float y, float d) { return w.levelRoot.TransformPoint(W188Plan.ToLevel(s, u, y, d)); }

    // nivel 10 ya cargado y el jugador fuera del ascensor
    public static IEnumerator Views()
    {
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
            // de cerca (a 1,3 m del cristal, a su altura) y de lejos (desde el otro lado del patio, con tele)
            Shot("09b_la_ventana_cerca", Lw(w, fg.s, W188Plan.UC(fg.i) + 0.3f, fg.f * W188Plan.FH + 1.6f, -1.3f), fg.root.position + Vector3.up * 1.45f, 42f);
            yield return 0.6f;
            Shot("09d_la_ventana_cerca_llamando", Lw(w, fg.s, W188Plan.UC(fg.i) - 0.25f, fg.f * W188Plan.FH + 1.45f, -0.9f), fg.root.position + Vector3.up * 1.4f, 50f);
            Shot("09c_la_ventana_lejos", Lw(w, fg.s, W188Plan.UC(fg.i) - 1.5f, Mathf.Max(eyeH, fg.f * W188Plan.FH + 0.6f), -12f), fg.root.position + Vector3.up * 1.3f, 22f);   // como desde una ventana de enfrente, una planta más abajo
            // desde dentro de su cuarto (el jugador sigue en el patio: la figura mira al cristal y se recorta contra la ventana)
            Shot("10b_la_ventana_cuarto_media", w.CellWorld(fg.f, fg.s, fg.i, -0.85f, 3.45f) + Vector3.up * (eyeH - 0.1f), fg.root.position + Vector3.up * 1.25f, 62f);
            Shot("10c_la_ventana_cuarto_cerca", w.CellWorld(fg.f, fg.s, fg.i, 0.8f, 2.0f) + Vector3.up * (eyeH - 0.05f), fg.root.position + Vector3.up * 1.45f, 58f);
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
                // v7.12.1: el cuarto (muebles): el mismo encuadre que la captura del usuario (junto a la silla, mirando la cama
                // hacia la medianera), desde la puerta, la cama de cerca y la ventana con el radiador y el escritorio
                // (CellWorld: du desde el centro de la ventana, d hacia dentro; la cama está en du -1,54..0,44 y d 1,0..2,46)
                Shot("12b_cuarto_como_la_captura", w.CellWorld(ct.f, ct.s, ct.i, 1.16f, 1.95f) + Vector3.up * 1.52f, w.CellWorld(ct.f, ct.s, ct.i, -0.64f, 1.5f) + Vector3.up * 0.25f, 70f);
                Shot("12c_cuarto_desde_la_puerta", w.CellWorld(ct.f, ct.s, ct.i, 1.25f, 3.75f) + Vector3.up * 1.5f, w.CellWorld(ct.f, ct.s, ct.i, -0.9f, 1.6f) + Vector3.up * 0.55f, 72f);
                Shot("12d_cuarto_cama_cerca", w.CellWorld(ct.f, ct.s, ct.i, 0.35f, 1.05f) + Vector3.up * 1.0f, w.CellWorld(ct.f, ct.s, ct.i, -1.3f, 2.0f) + Vector3.up * 0.55f, 62f);
                Shot("12e_cuarto_ventana", w.CellWorld(ct.f, ct.s, ct.i, -0.35f, 3.1f) + Vector3.up * 1.45f, w.CellWorld(ct.f, ct.s, ct.i, 0.15f, 0.3f) + Vector3.up * 1.15f, 70f);
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
    }
}
