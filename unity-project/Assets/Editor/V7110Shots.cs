using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.11.0: capturas reales dentro del juego del personaje CC5 (xvfb con gráficos): primera persona mirando abajo (manos,
// pies, sombra), agachado, andando y corriendo; el cuerpo propio en tercera persona (frente/lado/espalda); compañeros remotos
// (RemotePlayer, lo que ven los demás jugadores) andando/agachados/de pie; y el conductor del todoterreno del Nivel 114
// (fuera y en primera persona con las manos en el volante). Salida en $V7110_OUT (por defecto /workspace/v7110/capturas).
//   V7110_OUT=<dir> xvfb-run -a -s "-screen 0 1920x1080x24" Unity -batchmode -projectPath <p> -executeMethod V7110Shots.Export
[InitializeOnLoad]
public static class V7110Shots
{
    private static IEnumerator routine; private static double waitUntil;
    static string Out { get { string o = System.Environment.GetEnvironmentVariable("V7110_OUT"); return string.IsNullOrEmpty(o) ? "/workspace/v7110/capturas" : o; } }
    static string Only { get { return System.Environment.GetEnvironmentVariable("V7110_ONLY") ?? ""; } }
    static V7110Shots() { if (SessionState.GetString("smoke_phase", "") != "v7110shots") return; EditorApplication.update += Tick; }
    public static void Export()
    {
        Directory.CreateDirectory(Out);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v7110shots");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try { if (!routine.MoveNext()) Finish(); else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current; }
        catch (System.Exception e) { Debug.LogError("[V7110Shots] " + e); File.AppendAllText(Out + "/report.txt", "EXC " + e + "\n"); Finish(); }
    }
    private static void Finish() { Time.timeScale = 1f; SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; EditorApplication.Exit(0); }
    static void Log(string s) { File.AppendAllText(Out + "/report.txt", s + "\n"); Debug.Log("[V7110Shots] " + s); }

    private static void Shot(string name, System.Action<Camera> place = null, int w = 1920, int h = 1080)
    {
        RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        Vector3 p0 = game.transform.position; Quaternion r0 = game.transform.rotation;
        if (place != null) place(game);
        if (CorridorPlanar.Instance != null) CorridorPlanar.Instance.Refresh(game);
        RenderTexture o = game.targetTexture; game.targetTexture = rt; game.Render(); game.targetTexture = o;
        game.transform.position = p0; game.transform.rotation = r0;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev; File.WriteAllBytes(Out + "/" + name + ".png", tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
        Log("captura " + name);
    }
    static System.Action<Camera> LookAt(Vector3 eye, Vector3 at) { return cm => { cm.transform.position = eye; cm.transform.rotation = Quaternion.LookRotation(at - eye); }; }
    // cámara a «dist» del centro, acercándola si hay una pared en medio (mínimo 1,2 m)
    static Vector3 Eye(Vector3 center, Vector3 dir, float dist, float h)
    {
        Vector3 d = (dir * dist + Vector3.up * h); float len = d.magnitude; d /= len;
        if (Physics.SphereCast(center, 0.15f, d, out RaycastHit hit, len, ~PlayerBody.LayerMaskBit, QueryTriggerInteraction.Ignore)) len = Mathf.Max(1.2f, hit.distance - 0.1f);
        return center + d * len;
    }
    static void Around(string pre, Vector3 center, Vector3 fwd, float dist, float h, bool back = true)
    {
        Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
        Shot(pre + "_frente", LookAt(Eye(center, fwd, dist, h), center));
        Shot(pre + "_lado", LookAt(Eye(center, right, dist, h), center));
        Shot(pre + "_34", LookAt(Eye(center, (fwd + right).normalized, dist, h), center));
        if (back) Shot(pre + "_espalda", LookAt(Eye(center, -fwd, dist, h), center));
    }
    static void HeadVisible(PlayerBody pb, bool on)
    {
        pb.ShowHeadForShots = on; pb.Tick();
        foreach (SkinnedMeshRenderer r in pb.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name.ToLowerInvariant().Contains("head")) r.shadowCastingMode = on ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
    }
    static string Rig(Transform model, Transform basis)
    {
        string o = "";
        foreach (string b in new[] { "Hips", "Head", "UpperArm.R", "Middle1.R", "Middle1.L", "Foot.L", "Foot.R" })
        {
            Transform t = PlayerRig.Find(model, b); if (t == null) continue;
            o += " " + b + " " + basis.InverseTransformPoint(t.position).ToString("F2") + ";";
        }
        float top = -9f, bot = 9f; int tris = 0;
        foreach (SkinnedMeshRenderer r in model.GetComponentsInChildren<SkinnedMeshRenderer>()) { top = Mathf.Max(top, basis.InverseTransformPoint(r.bounds.max).y); bot = Mathf.Min(bot, basis.InverseTransformPoint(r.bounds.min).y); tris += r.sharedMesh.triangles.Length / 3; }
        return o + " altura mallas " + bot.ToString("F2") + "–" + top.ToString("F2") + ", " + tris + " triángulos";
    }

    private static IEnumerator WaitLevel(GameBootstrap gb, int lv)
    {
        int ld = gb.LoadCount;
        UIManager.Instance.StartGame(lv);
        float wt = 0f; while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
        wt = 0f; while (wt < 40f && ElevatorCabin.Current != null && !ElevatorCabin.Current.Released) { yield return 0.25f; wt += 0.25f; }
        yield return 1.5f;
        GameManager gm = GameManager.Instance;
        if (Health.Instance != null) Health.DisableForTests = true;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        VhsEffect vx = Camera.main.GetComponent<VhsEffect>(); if (vx != null) vx.intensityOverride = 0f;
    }

    private static IEnumerator Run()
    {
        File.WriteAllText(Out + "/report.txt", "");
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance;
        while (gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) { yield return 0.2f; gb = GameBootstrap.Instance; }
        yield return 1.0f;
        GameSettings.Quality = 2; yield return 0.3f;
        Log("versión " + GameVersion.Current + ", gráficos " + SystemInfo.graphicsDeviceType + ", " + System.DateTime.Now.ToString("u"));
        gb.useRandomSeed = false; gb.seed = 12345;
        if (Only == "" || Only.Contains("n0")) { IEnumerator a = Level0(gb); while (a.MoveNext()) yield return a.Current; }
        if (Only == "" || Only.Contains("n1")) { IEnumerator a1 = Level1(gb); while (a1.MoveNext()) yield return a1.Current; }
        if (Only == "" || Only.Contains("n7")) { IEnumerator a7 = Ocean(gb); while (a7.MoveNext()) yield return a7.Current; }
        if (Only == "" || Only.Contains("car")) { IEnumerator b = Desert(gb); while (b.MoveNext()) yield return b.Current; }
        Log("FIN");
    }

    // ---------------------------------------------------------------- Nivel 0: primera persona, tercera persona, compañeros
    private static IEnumerator Level0(GameBootstrap gb)
    {
        IEnumerator wl = WaitLevel(gb, 0); while (wl.MoveNext()) yield return wl.Current;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player;
        PlayerBody pb = p.GetComponentInChildren<PlayerBody>();
        if (pb == null || !pb.Loaded) { Log("SIN CUERPO"); yield break; }
        Transform pm = pb.transform.Find("Model");
        Log("cuerpo: CC " + pb.IsCC + ", escala " + pb.ModelScale.ToString("F3") + ", renderers " + pb.RendererCount + ", solo sombra " + pb.ShadowOnlyCount + ", estado " + pb.CurrentState);
        IEnumerator wo0 = WalkOut(p, 2.2f); while (wo0.MoveNext()) yield return wo0.Current;
        if (gm.flashlight != null) { gm.flashlight.SetBattery(999f); gm.flashlight.SetOn(true); }
        yield return 0.8f;

        // primera persona
        p.SetPitch(62f); yield return 1.0f;
        Log("1.ª persona abajo: cámara " + p.transform.InverseTransformPoint(Camera.main.transform.position).ToString("F2") + Rig(pm, p.transform) + " | cabeza solo sombra " + !pb.HeadShown + ", IK " + pb.IkActive);
        Shot("primera_persona_mirando_abajo");
        p.SetPitch(35f); yield return 0.6f; Shot("primera_persona_abajo_35");
        p.SetPitch(0f); yield return 0.6f; Shot("primera_persona_frente_linterna");
        p.SetPitch(-30f); yield return 0.6f; Shot("primera_persona_arriba_sin_capucha");
        p.debugInput = true; p.debugMove = new Vector2(0f, 1f); p.SetPitch(62f); yield return 0.9f;
        Log("andando: estado " + pb.CurrentState + " " + p.FlatSpeed.ToString("F1") + " m/s"); Shot("primera_persona_andando_abajo");
        p.debugSprint = true; yield return 0.9f;
        Log("corriendo: estado " + pb.CurrentState + " " + p.FlatSpeed.ToString("F1") + " m/s"); Shot("primera_persona_corriendo_abajo");
        p.debugSprint = false; p.debugMove = Vector2.zero; p.debugCrouch = true; yield return 1.2f;
        Log("agachado: estado " + pb.CurrentState + ", ojos " + p.EyeHeightNow.ToString("F2")); Shot("agachado_primera_persona_abajo");
        p.debugCrouch = false; p.debugInput = false; p.SetPitch(10f); yield return 1.2f;
    }

    // sale del ascensor andando hacia el pasillo más largo
    private static IEnumerator WalkOut(FirstPersonController p, float secs)
    {
        float bestD = 0f; float bestYaw = 0f;
        for (int k = 0; k < 16; k++)
        {
            float yaw = k * 22.5f; Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float d = Physics.SphereCast(p.transform.position + Vector3.up * 1.0f, 0.3f, dir, out RaycastHit hh, 40f, ~PlayerBody.LayerMaskBit, QueryTriggerInteraction.Ignore) ? hh.distance : 40f;
            if (d > bestD) { bestD = d; bestYaw = yaw; }
        }
        p.Teleport(p.transform.position, bestYaw); yield return 0.3f;
        p.debugInput = true; p.debugMove = new Vector2(0f, 1f); yield return secs;
        p.debugMove = Vector2.zero; p.debugInput = false; yield return 0.6f;
        Log("salida: " + bestD.ToString("F1") + " m libres hacia " + bestYaw.ToString("F0") + "°, ahora en " + p.transform.position.ToString("F1"));
    }

    // el sitio más abierto alrededor (para las vistas desde fuera): gira al jugador hacia la dirección con más espacio
    private static float OpenYaw(Vector3 at, out float dist)
    {
        float bestD = 0f, bestYaw = 0f;
        for (int k = 0; k < 24; k++)
        {
            float yaw = k * 15f; Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            float d = Physics.SphereCast(at + Vector3.up * 1.0f, 0.4f, dir, out RaycastHit hh, 40f, ~PlayerBody.LayerMaskBit, QueryTriggerInteraction.Ignore) ? hh.distance : 40f;
            if (d > bestD) { bestD = d; bestYaw = yaw; }
        }
        dist = bestD; return bestYaw;
    }

    // ---------------------------------------------------------------- Nivel 1 (aparcamiento, espacio abierto): tercera persona y compañeros
    private static IEnumerator Level1(GameBootstrap gb)
    {
        if (GameManager.Instance == null || gb.InMenu) { IEnumerator wl = WaitLevel(gb, 0); while (wl.MoveNext()) yield return wl.Current; }
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player;
        PlayerBody pb = p.GetComponentInChildren<PlayerBody>();
        Transform pm = pb.transform.Find("Model");
        // sala: el punto más despejado (mínimo de 8 direcciones) en 24 m alrededor, con suelo y techo
        Vector3 p0 = p.transform.position, best = p0; float bestS = -1f;
        int mask = ~(PlayerBody.LayerMaskBit | (1 << 2));
        var lamps = new System.Collections.Generic.List<Vector3>();
        foreach (Light l in Object.FindObjectsOfType<Light>()) if (l.enabled && l.type != LightType.Directional && l.intensity > 0.05f && l.GetComponentInParent<FirstPersonController>() == null) lamps.Add(l.transform.position);
        for (float x = -24f; x <= 24f; x += 1.5f)
            for (float z = -24f; z <= 24f; z += 1.5f)
            {
                Vector3 q = p0 + new Vector3(x, 0f, z);
                if (!Physics.Raycast(q + Vector3.up * 1.2f, Vector3.down, out RaycastHit fl, 2.5f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (!Physics.Raycast(q + Vector3.up * 1.2f, Vector3.up, 6f, mask, QueryTriggerInteraction.Ignore)) continue;
                q = fl.point; if (Mathf.Abs(q.y - p0.y) > 0.4f) continue;
                if (Physics.CheckCapsule(q + Vector3.up * 0.45f, q + Vector3.up * 1.6f, 0.4f, mask, QueryTriggerInteraction.Ignore)) continue;
                float sc = 99f;
                for (int k = 0; k < 8; k++) { Vector3 d = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward; float dd = Physics.Raycast(q + Vector3.up * 1.0f, d, out RaycastHit hh, 8f, mask, QueryTriggerInteraction.Ignore) ? hh.distance : 8f; sc = Mathf.Min(sc, dd); }
                if (sc < 2.5f) continue;
                float ld = 99f; foreach (Vector3 lp in lamps) { Vector3 dl = lp - q; dl.y = 0f; ld = Mathf.Min(ld, dl.magnitude); }
                sc = Mathf.Min(sc, 5f) + (ld < 1.2f ? 6f : ld < 2.2f ? 3f : 0f);   // v7.11.0: bajo una lámpara del techo, para ver el traje
                if (sc > bestS) { bestS = sc; best = q; }
            }
        float od; float oy = OpenYaw(best, out od);
        p.Teleport(best + Vector3.up * 0.05f, oy); yield return 1.0f;
        Log("sala: " + best.ToString("F1") + ", despejado mín. " + bestS.ToString("F1") + " m, mirando " + oy.ToString("F0") + "° (" + od.ToString("F1") + " m libres)");
        Log("lámparas: " + lamps.Count);
        if (gm.flashlight != null) { gm.flashlight.SetBattery(999f); gm.flashlight.SetOn(false); }
        p.SetPitch(10f); yield return 1.0f;

        // tercera persona (el mismo cuerpo con la cabeza visible)
        HeadVisible(pb, true); yield return 0.2f;
        Vector3 fwd = p.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 c = pb.transform.position + Vector3.up * 0.95f;
        Log("de pie: " + Rig(pm, pb.transform));
        Around("tercera_persona", c, fwd, 2.7f, 0.25f);
        p.debugInput = true; p.debugCrouch = true; yield return 1.2f;
        c = pb.transform.position + Vector3.up * 0.6f; fwd = p.transform.forward; fwd.y = 0f; fwd.Normalize();
        Log("agachado: " + Rig(pm, pb.transform));
        Around("agachado", c, fwd, 2.3f, 0.3f, false);
        p.debugCrouch = false; p.debugMove = new Vector2(0f, 1f); p.debugSprint = true; yield return 0.8f;
        c = pb.transform.position + Vector3.up * 0.95f; fwd = p.transform.forward; fwd.y = 0f; fwd.Normalize();
        { Vector3 rr = Vector3.Cross(Vector3.up, fwd); Vector3 e1 = Eye(c, rr, 2.8f, 0.2f), e2 = Eye(c, -rr, 2.8f, 0.2f);
          Shot("corriendo_lado", LookAt((e1 - c).sqrMagnitude >= (e2 - c).sqrMagnitude ? e1 : e2, c)); }
        p.debugSprint = false; p.debugMove = Vector2.zero; p.debugInput = false; yield return 0.8f;
        HeadVisible(pb, false);

        // compañeros remotos (lo que ven los demás jugadores)
        if (gm.flashlight != null) gm.flashlight.SetOn(true);
        p.Teleport(best + Vector3.up * 0.05f, oy); yield return 0.6f;
        Vector3 basePos = p.transform.position; float yaw0 = p.transform.eulerAngles.y;
        Vector3 f0 = Quaternion.Euler(0f, yaw0, 0f) * Vector3.forward, r0 = Quaternion.Euler(0f, yaw0, 0f) * Vector3.right;
        RemotePlayer a = RemotePlayer.Create(gm.transform, 91, "Ana", StepKind.Carpet), b = RemotePlayer.Create(gm.transform, 92, "Leo", StepKind.Carpet), d2 = RemotePlayer.Create(gm.transform, 93, "Iker", StepKind.Carpet);
        Vector3 pa = basePos + f0 * 3.2f - r0 * 0.9f, pbp = basePos + f0 * 4.6f + r0 * 1.1f, pd = basePos + f0 * 6.5f;
        float tt = 0f;
        while (tt < 2.5f)
        {   // Ana de frente quieta con la linterna, Leo andando hacia la izquierda, Iker agachado
            a.Push(pa, yaw0 + 180f, 5f, 0f, RemotePlayer.FlagFlash);
            b.Push(pbp - r0 * tt * 1.4f, yaw0 - 90f, 0f, 1.4f, RemotePlayer.FlagFlash);
            d2.Push(pd, yaw0 + 150f, 10f, 0f, RemotePlayer.FlagCrouch);
            yield return 0.05f; tt += 0.05f;
        }
        if (System.Environment.GetEnvironmentVariable("V7110_DBG") == "1")
        {
            string o = "DBG local vs remoto:";
            foreach (Transform t in pm.GetComponentsInChildren<Transform>())
            {
                if (!t.name.StartsWith("CC_Base")) continue;
                Transform u = PlayerRig.Find(a.transform, t.name); if (u == null) continue;
                float ang = Quaternion.Angle(t.localRotation, u.localRotation); if (ang > 8f) o += " " + t.name.Replace("CC_Base_", "") + " " + ang.ToString("F0");
            }
            Log(o);
        }
        System.Func<Transform, string> matsOf = t2 => { string o2 = ""; foreach (SkinnedMeshRenderer r in t2.GetComponentsInChildren<SkinnedMeshRenderer>()) foreach (Material m in r.sharedMaterials) if (m != null) o2 += " " + m.name.Replace(" (Instance)", "") + "[" + m.shader.name + " col " + (m.HasProperty("_Color") ? ((Color)m.color).ToString("F2") : "-") + " tex " + (m.mainTexture != null ? m.mainTexture.name : "-") + "]"; return o2; };
        Log("materiales local:" + matsOf(pm));
        Log("materiales remoto:" + matsOf(a.transform));
        Log("remoto Ana: estado " + a.CurrentState + ", " + Rig(a.transform, a.transform) + " | Leo " + b.CurrentState + " | Iker " + d2.CurrentState);
        p.SetPitch(4f); yield return 0.3f;
        Shot("multijugador_companeros");
        Vector3 ca = a.transform.position + Vector3.up * 0.95f; Vector3 fa = a.transform.forward;
        Around("remoto", ca, fa, 2.6f, 0.25f);
        tt = 0f;
        while (tt < 1.2f) { b.Push(pbp - r0 * (2.5f + tt) * 1.4f, yaw0 - 90f, 0f, 1.4f, RemotePlayer.FlagFlash); a.Push(pa, yaw0 + 180f, 5f, 0f, RemotePlayer.FlagFlash); d2.Push(pd, yaw0 + 150f, 10f, 0f, RemotePlayer.FlagCrouch); yield return 0.05f; tt += 0.05f; }
        Vector3 cb = b.transform.position + Vector3.up * 0.95f;
        Shot("remoto_andando_lado", LookAt(cb + b.transform.right * 2.8f + Vector3.up * 0.2f, cb));
        Vector3 cd = d2.transform.position + Vector3.up * 0.6f;
        Shot("remoto_agachado", LookAt(cd + d2.transform.forward * 2.2f + d2.transform.right * 0.8f + Vector3.up * 0.4f, cd));
        Object.Destroy(a.gameObject); Object.Destroy(b.gameObject); Object.Destroy(d2.gameObject);
        yield return 0.3f;
    }

    // ---------------------------------------------------------------- Nivel 7 (Talasofobia): casco de buzo
    // el HUD del casco (viñeta) es un lienzo superpuesto: para que salga en la captura se pasa un momento a «cámara»
    static void ShotHud(string name)
    {
        GameObject vg = GameObject.Find("HelmetVignette"); Canvas cv = vg != null ? vg.GetComponentInParent<Canvas>() : null;
        if (cv == null) { Shot(name); return; }
        RenderMode rm = cv.renderMode; Camera cm = Camera.main;
        cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cm; cv.planeDistance = cm.nearClipPlane + 0.02f;
        Canvas.ForceUpdateCanvases();
        Shot(name);
        cv.renderMode = rm; cv.worldCamera = null;
    }
    static string HelmetInfo(GameObject h, Transform avatar)
    {
        if (h == null) return "sin casco";
        Bounds hb = new Bounds(h.transform.position, Vector3.zero); bool any = false;
        foreach (Renderer r in h.GetComponentsInChildren<Renderer>()) { if (!any) { hb = r.bounds; any = true; } else hb.Encapsulate(r.bounds); }
        string o = "casco bounds y " + (hb.min.y - avatar.position.y).ToString("F2") + "–" + (hb.max.y - avatar.position.y).ToString("F2") + " ancho " + hb.size.x.ToString("F2") + "×" + hb.size.z.ToString("F2");
        Transform hd = PlayerRig.Find(avatar, "Head"); if (hd != null) o += ", hueso cabeza y " + (hd.position.y - avatar.position.y).ToString("F2");
        foreach (SkinnedMeshRenderer r in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name.EndsWith("_Head")) o += ", capucha " + (r.enabled ? "VISIBLE" : "oculta");
            else { Bounds b = r.bounds; o += ", " + r.name + " y máx " + (b.max.y - avatar.position.y).ToString("F2"); }
        return o;
    }
    private static IEnumerator Ocean(GameBootstrap gb)
    {
        int lv = 7; for (int i = 0; i < LevelConfig.Count; i++) if (LevelConfig.Get(i).layout == LayoutKind.Ocean) lv = i;
        IEnumerator wl = WaitLevel(gb, lv); while (wl.MoveNext()) yield return wl.Current;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player;
        PlayerBody pb = p.GetComponentInChildren<PlayerBody>(); Transform pm = pb.transform.Find("Model");
        OceanWorld w = OceanWorld.Active; OceanPlayer op = OceanPlayer.Local;
        if (w == null || op == null) { Log("SIN MAR"); yield break; }
        if (w.fish != null) w.fish.enabled = false; if (w.angler != null) w.angler.enabled = false;
        gm.lostRestartSeconds = 999f;
        Log("nivel 7: " + LevelConfig.Get(lv).name);
        // en el muelle, junto al soporte de cascos, con el casco puesto
        Vector3 at = new Vector3(46f, OceanWorld.PierY + 0.02f, 251.0f);
        p.Teleport(at, 90f); yield return 1.0f;
        op.SetHelmet(true); yield return 0.5f;
        p.Teleport(at, 90f); p.SetPitch(5f); yield return 1.2f;
        Log("casco local " + op.HasHelmet + ", cabeza solo sombra " + (pb.ShadowOnlyCount > 0) + ", " + Rig(pm, pb.transform));
        ShotHud("nivel7_primera_persona_casco_frente");
        p.SetPitch(60f); yield return 1.0f;
        ShotHud("nivel7_primera_persona_casco_abajo");
        Shot("nivel7_primera_persona_casco_abajo_sin_hud");
        p.SetPitch(-35f); yield return 0.8f;
        ShotHud("nivel7_primera_persona_casco_arriba");
        p.SetPitch(5f); yield return 0.6f;

        // tercera persona: el cuerpo propio con el casco que ven los demás (mismo AttachHelmetTo del juego)
        HeadVisible(pb, true); yield return 0.2f;
        GameObject lh = w.AttachHelmetTo(pm); yield return 0.3f;
        Log("local 3.ª: " + HelmetInfo(lh, pb.transform));
        Vector3 fwd = p.transform.forward; fwd.y = 0f; fwd.Normalize();
        Vector3 c = pb.transform.position + Vector3.up * 1.0f;
        Around("nivel7_tercera_persona", c, fwd, 2.6f, 0.3f);
        Vector3 hc = pb.transform.position + Vector3.up * 1.55f;
        Shot("nivel7_tercera_persona_casco_cerca", LookAt(hc + fwd * 1.0f + Vector3.Cross(Vector3.up, fwd) * 0.5f + Vector3.up * 0.1f, hc));
        Object.Destroy(lh); yield return 0.2f;
        HeadVisible(pb, false);

        // compañero remoto con casco
        RemotePlayer mate = RemotePlayer.Create(gm.transform, 97, "Ana", StepKind.Metal);
        Vector3 mp = at + Vector3.right * 2.6f; float my = 270f;
        for (int k = 0; k < 8; k++) { mate.Push(mp, my, 0f, 0f, RemotePlayer.FlagFlash); yield return 0.05f; }
        GameObject mh = w.AttachHelmetTo(mate.transform); yield return 0.4f;
        for (int k = 0; k < 8; k++) { mate.Push(mp, my, 0f, 0f, RemotePlayer.FlagFlash); yield return 0.05f; }
        Log("remoto: " + HelmetInfo(mh, mate.transform));
        Shot("nivel7_remoto_desde_jugador");
        Vector3 mf = mate.transform.forward; Vector3 mc = mate.transform.position + Vector3.up * 1.0f;
        Around("nivel7_remoto", mc, mf, 2.6f, 0.3f);
        Vector3 mhc = mate.transform.position + Vector3.up * 1.6f;
        Shot("nivel7_remoto_casco_cerca", LookAt(mhc + mf * 1.0f + mate.transform.right * 0.5f + Vector3.up * 0.1f, mhc));
        Shot("nivel7_remoto_casco_lado_cerca", LookAt(mhc + mate.transform.right * 1.1f, mhc));
        Object.Destroy(mate.gameObject); yield return 0.3f;

        // bajo el agua, en el fondo, con la lámpara del casco
        Vector3 me = new Vector3(60f, -47f, 252f);
        p.Teleport(me, 0f); yield return 1.5f; p.Teleport(me, 0f);
        p.SetPitch(55f); yield return 1.2f;
        ShotHud("nivel7_fondo_primera_persona_abajo");
        p.SetPitch(0f); yield return 0.6f;
        ShotHud("nivel7_fondo_primera_persona_frente");
        yield return 0.3f;
    }

    // ---------------------------------------------------------------- Nivel 114: conductor
    private static IEnumerator Desert(GameBootstrap gb)
    {
        int lv = 0; for (int i = 0; i < LevelConfig.Count; i++) if (LevelConfig.Get(i).layout == LayoutKind.Desert) lv = i;
        IEnumerator wl = WaitLevel(gb, lv); while (wl.MoveNext()) yield return wl.Current;
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 12.5f;
        GameManager gm = GameManager.Instance; FirstPersonController p = gm.player; DesertCars dc = DesertCars.Active;
        DesertCar car = dc != null ? dc.Drivable : null;
        if (car == null) { Log("SIN COCHE"); yield break; }
        Transform ct = car.transform; float side = car.driverSide;
        PlayerBody pb = p.GetComponentInChildren<PlayerBody>(); Transform pm = pb.transform.Find("Model");
        p.Teleport(ct.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.6f;
        DesertCars.DebugInteract = true; yield return 0.1f; yield return 2.0f;
        DesertCars.DebugDrive = true; DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true; DesertCars.DbgSteer = 0f;
        yield return 1.0f;
        Log("montado " + p.IsMounted + ", volante " + pb.OnWheel + ", error manos " + pb.WheelErrL.ToString("F3") + " / " + pb.WheelErrR.ToString("F3") + " m, inclinación " + pb.WheelLean.ToString("F2"));
        p.SetPitch(25f); yield return 0.6f; Shot("conductor_primera_persona_volante");
        p.SetPitch(55f); yield return 0.6f; Shot("conductor_primera_persona_piernas");
        if (System.Environment.GetEnvironmentVariable("V7110_DBG") == "2")
        {
            PlayerBody dpb = p.GetComponentInChildren<PlayerBody>();
            Color[] cols = { Color.red, Color.green, Color.blue, Color.magenta, Color.cyan };
            var saved = new List<KeyValuePair<Renderer, Material[]>>();
            int ri = 0;
            foreach (Renderer r in dpb.GetComponentsInChildren<Renderer>())
            {
                saved.Add(new KeyValuePair<Renderer, Material[]>(r, r.sharedMaterials));
                Material[] ms = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < ms.Length; i++) { ms[i] = new Material(Shader.Find("Unlit/Color")); ms[i].color = cols[(ri + i) % cols.Length] * (0.5f + 0.5f * (ri % 2)); }
                Log("DBG renderer " + ri + " " + r.name + " capa " + r.gameObject.layer + " sombra " + r.shadowCastingMode + " mats " + string.Join(",", System.Array.ConvertAll(r.sharedMaterials, m => m != null ? m.name : "-")) + (r is SkinnedMeshRenderer ? " submallas " + ((SkinnedMeshRenderer)r).sharedMesh.subMeshCount : ""));
                r.sharedMaterials = ms; ri += ms.Length;
            }
            Transform dh = PlayerRig.Find(dpb.transform, "Head"), dn = PlayerRig.Find(dpb.transform, "Neck"), dch = PlayerRig.Find(dpb.transform, "Chest");
            Transform cmt = Camera.main.transform;
            Log("DBG cámara local " + dpb.transform.InverseTransformPoint(cmt.position).ToString("F3") + " cabeza " + dpb.transform.InverseTransformPoint(dh.position).ToString("F3") + " cuello " + dpb.transform.InverseTransformPoint(dn.position).ToString("F3") + " pecho " + dpb.transform.InverseTransformPoint(dch.position).ToString("F3") + " near " + Camera.main.nearClipPlane);
            yield return 0.2f; Shot("dbg_conductor_colores");
            foreach (var kv in saved) kv.Key.sharedMaterials = kv.Value;
        }
        p.SetPitch(0f);
        DesertCars.CamView = 1; yield return 1.5f;
        Log("vista exterior " + PlayerBody.ExternalView + ", cabeza " + pb.HeadShown + ", escala asiento " + pb.SeatScaled + ", volante " + pb.OnWheel + ", error " + pb.WheelErrL.ToString("F3") + " / " + pb.WheelErrR.ToString("F3"));
        Log("conductor (coche): " + Rig(pm, ct) + " | asiento " + car.SeatLocal(0).ToString("F2") + ", techo " + car.ModelHeight.ToString("F2"));
        Transform hp = pb.Hips; Log("cadera sobre el asiento " + (ct.InverseTransformPoint(hp.position).y - car.SeatLocal(0).y).ToString("F2") + " (objetivo " + SeatedLegs.HipYFor(hp).ToString("F2") + ")");
        foreach (RaycastHit hh in Physics.RaycastAll(ct.TransformPoint(car.SeatLocal(0) + new Vector3(0f, 1.2f, 0.5f)), -ct.up, 3f)) Log("   rayo suelo: " + hh.collider.name + " y " + ct.InverseTransformPoint(hh.point).y.ToString("F2"));
        Vector3 seat = ct.TransformPoint(car.SeatLocal(0)) + ct.up * 1.45f;
        Vector3 rgt = ct.right * side;
        Shot("conductor_fuera_34", LookAt(seat + (ct.forward * 0.7f + rgt * 0.7f).normalized * 2.6f + ct.up * 0.15f, seat));
        Shot("conductor_fuera_lado", LookAt(seat + rgt * 2.6f + ct.up * 0.15f, seat));
        Shot("conductor_fuera_frente", LookAt(seat + ct.forward * 2.6f + ct.up * 0.15f, seat));
        { Vector3 eb = seat - ct.forward * 3.2f + ct.up * 0.35f + rgt * 0.2f; Shot("conductor_fuera_detras", LookAt(eb, seat)); }
        List<Renderer> carR = new List<Renderer>(); foreach (Renderer r in car.GetComponentsInChildren<Renderer>()) if (r.enabled && !r.transform.IsChildOf(pb.transform)) carR.Add(r);
        foreach (Renderer r in carR) r.enabled = false;
        Vector3 s2 = seat - ct.up * 0.5f;
        Shot("conductor_sin_coche_lado", LookAt(s2 + rgt * 2.8f + ct.up * 0.2f, s2));
        Shot("conductor_sin_coche_34", LookAt(s2 + (ct.forward * 0.7f + rgt * 0.7f).normalized * 2.8f + ct.up * 0.2f, s2));
        foreach (Renderer r in carR) r.enabled = true;
        Shot("conductor_camara_exterior");
        DesertCars.CamView = 0; DesertCars.DebugDrive = false;
        DesertSky.ForceWeather = -1; DesertSky.ForceHour = -1f; Health.DisableForTests = false;
    }
}
