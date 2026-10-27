using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Nivel 7460 (El pasillo de las puertas): pruebas de edición (datos, recursos, plano por semilla, geometría) y de juego
// (ruidos a la espalda, latido, manilla de las cerradas, habitaciones, llave buena y salida, VIDA en el HUD).
public static class V7460Checks
{
    const int Lv = 9;

    public static void Edit(SmokeTest.Report r)
    {
        string T = "[7460] ";
        LevelConfig c = LevelConfig.Get(Lv);
        r.Check(c.layout == LayoutKind.Corridor && c.DisplayNumber == "7460" && c.name == "El pasillo de las puertas" && !c.HasMonsterAI && c.monsterModel == LevelConfig.NoMonsterId && c.monsterCount == 0, T + "nivel 9 = Nivel 7460 «" + c.name + "», sin entidades");
        r.Check(!string.IsNullOrEmpty(c.subtitle) && c.subtitle.Length > 40, T + "tarjeta del nivel con texto: «" + c.subtitle + "»");
        r.Check(Resources.Load<Texture2D>("UI/LevelThumbs/level_9") != null, T + "miniatura del selector de niveles (level_9)");
        r.Check(CorridorWorld.HasAssets && Resources.Load<TextAsset>("C7460/C7460Leaf") != null && Resources.Load<TextAsset>("C7460/C7460Room") != null, T + "módulo, hoja y habitación horneados en Blender (.bytes) y shader del pasillo");
        Texture2D lm = Resources.Load<Texture2D>("C7460/C7460_LM"), lr = Resources.Load<Texture2D>("C7460/C7460Room_LM");
        r.Check(lm != null && lr != null && lm.width >= 512 && lr.width >= 256, T + "mapas de luz horneados (Cycles + OIDN): " + (lm != null ? lm.width : 0) + " / " + (lr != null ? lr.width : 0) + " px");
        Shader sh = Shader.Find("Backrooms/CorridorBaked");
        r.Check(sh != null && sh.isSupported, T + "shader Backrooms/CorridorBaked compila");
        foreach (string n in new[] { "C7460Floor", "C7460Wall", "C7460Trim", "C7460Ceil", "C7460Brick" })
            r.Check(Resources.Load<Texture2D>("Textures/C7460/" + n + "_a") != null && Resources.Load<Texture2D>("Textures/C7460/" + n + "_n") != null && Resources.Load<Texture2D>("Textures/C7460/" + n + "_m") != null, T + "texturas PBR " + n + " (albedo, normal, máscara)");
        r.Check(LoreNotes.Valid(LoreNotes.IdFor(Lv, 2)) && LoreNotes.Get(LoreNotes.IdFor(Lv, 0)).text.Contains("puertas"), T + "3 notas de ambiente del nivel");

        // plano por semilla: determinista, una salida, habitaciones y tapiadas
        bool det = true, okExit = true, differ = false; string ex = "";
        foreach (int seed in new[] { 1, 7, 12345, 144139097, 99 })
        {
            CorridorPlan a = CorridorPlan.Make(seed, 3, 100, new bool[4, 100], new bool[3, 101]);
            CorridorPlan b = CorridorPlan.Make(seed, 3, 100, new bool[4, 100], new bool[3, 101]);
            int exits = 0;
            for (int s = 0; s < 2; s++) for (int j = 0; j < 100; j++) { if (a.doors[s, j] != b.doors[s, j]) det = false; if (a.doors[s, j] == CorridorPlan.Door.Exit) exits++; }
            if (exits != 1 || a.exitRow < 40 || a.exitRow > 92 || a.Count(CorridorPlan.Door.Room) < 10) okExit = false;
            CorridorPlan o = CorridorPlan.Make(seed + 1, 3, 100, new bool[4, 100], new bool[3, 101]);
            if (o.exitRow != a.exitRow || o.exitSide != a.exitSide) differ = true;
            ex += " " + a.exitRow + (a.exitSide == 0 ? "I" : "D") + "/" + a.Count(CorridorPlan.Door.Room) + "h/" + a.Count(CorridorPlan.Door.Bricked) + "t";
        }
        r.Check(det && differ, T + "plano sale de la semilla (igual en todos los equipos, distinto en cada partida)");
        r.Check(okExit, T + "una sola puerta de salida, lejos de la entrada, y ≥10 habitaciones (" + ex.Trim() + ")");

        // ruidos a la espalda: graves, sin siseo ni crujidos
        string bad = ""; float worst = 0f;
        for (int k = 0; k < CorridorEvents.NoiseNames.Length; k++)
        {
            AudioClip cl = CorridorEvents.CreateNoise(k);
            float[] d = new float[cl.samples]; cl.GetData(d, 0);
            float hr = V790Checks.HighRatio(d, cl.frequency, 3000f), pk = 0f, tail = 0f;
            for (int i = 0; i < d.Length; i++) pk = Mathf.Max(pk, Mathf.Abs(d[i]));
            for (int i = d.Length - 200; i < d.Length; i++) tail = Mathf.Max(tail, Mathf.Abs(d[i]));
            // «sin siseo continuo»: la energía no está repartida por todo el clip (hay silencios entre golpes/sílabas)
            int quiet = 0, win = cl.frequency / 50; for (int w = 0; w + win < d.Length; w += win) { float e = 0f; for (int i = w; i < w + win; i++) e = Mathf.Max(e, Mathf.Abs(d[i])); if (e < 0.03f) quiet++; }
            float quietFrac = quiet / (float)(d.Length / win);
            worst = Mathf.Max(worst, hr);
            if (cl.length < 1f || cl.length > 5f || pk < 0.5f || pk > 0.95f || hr > 0.01f || tail > 0.02f || quietFrac < 0.25f) bad += " " + cl.name + "(" + (hr * 100f).ToString("F2") + " %, silencio " + (quietFrac * 100f).ToString("F0") + " %)";
            string lo = cl.name.ToLower(); if (lo.Contains("cruj") || lo.Contains("creak") || lo.Contains("hiss") || lo.Contains("sise")) bad += " (nombre)" + lo;
            Object.DestroyImmediate(cl);
        }
        r.Check(bad == "", T + "[Ruidos] pasos, puerta, golpes y susurro: graves, sin siseo (máx. " + (worst * 100f).ToString("F2") + " % por encima de 3 kHz), sin crujidos ni chasquido final" + bad);
        r.Check(CorridorEvents.GapMin >= 15f && CorridorEvents.GapMax <= 60f && CorridorEvents.DistMin >= 5f, T + "[Ruidos] cada " + CorridorEvents.GapMin + "–" + CorridorEvents.GapMax + " s, a " + CorridorEvents.DistMin + "–" + CorridorEvents.DistMax + " m a tu espalda");
    }

    // geometría generada (en edición, tras gen.Generate())
    public static void Geometry(SmokeTest.Report r, string tag, BackroomsGenerator gen)
    {
        string T = tag + "[7460] ";
        CorridorWorld w = gen.Root.GetComponentInChildren<CorridorWorld>(true);
        r.Check(w != null && gen.Corridor != null, T + "pasillo construido por CorridorWorld");
        if (w == null) return;
        r.Check(w.ChunkCount == 10 && w.RoomCount == gen.Corridor.Count(CorridorPlan.Door.Room), T + w.ChunkCount + " tramos de 10 módulos y " + w.RoomCount + " habitaciones");
        r.Check(w.Length > 300f && gen.FixtureCount == 100, T + "pasillo de " + w.Length.ToString("F0") + " m con " + gen.FixtureCount + " plafones");
        r.Check(gen.KeySpots.Count == ExitLock.KeyCount, T + "5 sitios de llave");
        int inRooms = 0; foreach (Vector3 k in gen.KeySpots) { Vector3 l = w.levelRoot.InverseTransformPoint(k); if (Mathf.Abs(l.x - w.CenterX) > CorridorWorld.HW + 0.2f) inRooms++; }
        r.Check(inRooms == gen.KeySpots.Count, T + "las llaves están dentro de las habitaciones (" + inRooms + "/" + gen.KeySpots.Count + ")");
        // recorrido a pie por el centro del pasillo sin chocar
        Physics.SyncTransforms();
        Vector3 a = w.levelRoot.TransformPoint(new Vector3(w.CenterX, 0.95f, 1.0f)), b = w.levelRoot.TransformPoint(new Vector3(w.CenterX, 0.95f, w.Length - 1.0f));
        bool free = true; foreach (RaycastHit h in Physics.SphereCastAll(a, 0.34f, (b - a).normalized, (b - a).magnitude)) { free = false; r.Info(T + "choque en el pasillo: " + h.collider.name + " " + h.point); break; }
        r.Check(free, T + "se recorre el pasillo entero sin chocar (" + (b - a).magnitude.ToString("F0") + " m)");
        // paredes cerradas: un rayo lateral a mitad de cada módulo da en pared o puerta
        int holes = 0;
        for (int j = 0; j < gen.Corridor.rows; j++) for (int s = -1; s <= 1; s += 2)
        {
            Vector3 o = w.levelRoot.TransformPoint(new Vector3(w.CenterX, 1.2f, (j + 0.5f) * CorridorWorld.CS + 1.0f));
            if (!Physics.Raycast(o, w.levelRoot.TransformDirection(new Vector3(s, 0, 0)), CorridorWorld.HW + 0.6f)) holes++;
        }
        r.Check(holes == 0, T + "paredes sin huecos (" + holes + " rayos que se escapan)");
        // en cada habitación cabe una persona y está cerrada
        int badRooms = 0;
        for (int s = 0; s < 2; s++) for (int j = 0; j < gen.Corridor.rows; j++) if (gen.Corridor.doors[s, j] == CorridorPlan.Door.Room)
        {
            Vector3 c = w.RoomCenterWorld(s, j) + Vector3.up * 0.95f;
            if (Physics.CheckSphere(c, 0.34f)) badRooms++;
            foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back }) if (!Physics.Raycast(c, w.levelRoot.TransformDirection(dir), CorridorWorld.RW)) badRooms++;
        }
        r.Check(badRooms == 0, T + "habitaciones libres por dentro y cerradas por los lados (" + badRooms + " fallos)");
        // salida: de pie delante, se llega a < 1,6 m
        Vector3 st = w.DoorWorld(gen.Corridor.exitSide, gen.Corridor.exitRow, 0.75f);
        r.Check(!Physics.CheckSphere(st + Vector3.up * 0.95f, 0.34f) && Vector3.Distance(Flat(st), Flat(gen.ExitPosition)) < 1.6f, T + "delante de la salida hay sitio y queda a <1,6 m");
        ExitDoorCue cue = gen.Root.GetComponentInChildren<ExitDoorCue>(true);
        r.Check(cue != null && cue.visibleDistance <= 8.01f && !cue.ledRenderer.enabled && !cue.glow.enabled, T + "pista de la salida apagada y solo a < 8 m");
        // presupuesto
        int tris = 0, rends = 0; foreach (MeshFilter mf in gen.Root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) { tris += mf.sharedMesh.triangles.Length / 3; rends++; }
        r.Info(T + "triángulos " + tris + ", mallas " + rends);
        r.Check(tris < 3000000 && rends < 900, T + "presupuesto: " + tris + " triángulos en " + rends + " mallas");
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // recorrido en juego. Termina en la puerta de salida abierta (el llamador sigue en OceanExit)
    public static IEnumerator Play(SmokeTest.Report r, string tag, GameManager gm)
    {
        string T = tag + "[7460] ";
        CorridorWorld w = CorridorWorld.Active; CorridorEvents ev = CorridorEvents.Active;
        r.Check(w != null && ev != null, T + "pasillo y ruidos activos");
        if (w == null || ev == null) yield break;
        FirstPersonController p = gm.player; Camera cam = gm.playerCamera.GetComponent<Camera>();
        r.Check(gm.monsters.Length == 0 && MonsterAI.Instances.Count == 0, T + "sin entidades");
        r.Check(cam.farClipPlane >= 400f, T + "se ve el fondo del pasillo (plano lejano " + cam.farClipPlane + " m)");
        r.Check(ExitLock.Active != null && gm.exit != null, T + "salida con cerradura (sistema de llaves)");
        r.Check(gm.healthFill != null && gm.healthFill.gameObject.activeInHierarchy && Health.Instance != null, T + "VIDA en el HUD");
        if (Health.Instance != null && gm.healthFill != null)
        {
            Health.Instance.Damage(30f, "prueba"); yield return 0.3f;
            r.Check(Mathf.Abs(gm.healthFill.anchorMax.x - Health.Instance.Health01) < 0.02f && Health.Instance.Health01 < 0.75f, T + "la barra de VIDA baja con el daño (" + gm.healthFill.anchorMax.x.ToString("F2") + ")");
            Health.Instance.Heal(100f); yield return 0.2f;
            r.Check(gm.healthFill.anchorMax.x > 0.98f, T + "y se llena al curarse");
        }

        // ruidos: siempre a tu espalda, aceleran el latido; los pasos se callan si te giras
        Vector3 mid = w.levelRoot.TransformPoint(new Vector3(w.CenterX, 0.1f, 40f * CorridorWorld.CS));
        p.Teleport(mid, 0f); yield return 0.4f;
        CorridorEvents.DisableForTests = true;
        bool allBehind = true; string dots = "";
        for (int k = 0; k < 4; k++)
        {
            Adrenaline.ScareThreat = 0f;
            ev.Play(k); yield return 0.1f;
            dots += " " + ev.LastNoise + " " + ev.LastBehindDot.ToString("F2");
            if (ev.LastBehindDot > -0.8f || !ev.Source.isPlaying || Adrenaline.ScareThreat < 0.3f) allBehind = false;
            yield return 0.4f;
        }
        r.Check(allBehind && ev.NoiseEvents >= 4, T + "[Ruidos] los 4 suenan detrás de ti (3D) y aceleran el latido:" + dots);
        p.Teleport(mid, 90f); yield return 0.3f;
        ev.Play(1); yield return 0.1f;
        r.Check(ev.LastBehindDot < -0.8f, T + "[Ruidos] mirando a la pared, también a la espalda (" + ev.LastBehindDot.ToString("F2") + ")");
        p.Teleport(mid, 0f); yield return 0.3f;
        int stopped = ev.StoppedByLook;
        ev.Play(0); yield return 0.3f;
        p.Teleport(mid, 180f); yield return 0.3f;
        r.Check(ev.StoppedByLook == stopped + 1 && !ev.Source.isPlaying, T + "[Ruidos] los pasos se callan cuando te giras a mirar");
        {
            Adrenaline ad = p.GetComponent<Adrenaline>(); Adrenaline.ScareThreat = 0f; yield return 4f; float bpm0 = ad != null ? ad.Bpm : 0f;
            ev.Play(3); yield return 2.5f;
            float bpm1 = ad != null ? ad.Bpm : 0f;
            r.Check(bpm1 > bpm0 + 8f, T + "[Latido] un susurro a la espalda acelera el corazón (" + bpm0.ToString("F0") + " → " + bpm1.ToString("F0") + " ppm)");
        }
        CorridorEvents.DisableForTests = false;

        // manilla de una puerta cerrada
        int lr = -1, ls = 0;
        for (int j = 6; j < w.plan.rows && lr < 0; j++) for (int s = 0; s < 2; s++) if (w.plan.doors[s, j] == CorridorPlan.Door.Locked) { lr = j; ls = s; break; }
        if (lr >= 0)
        {
            int r0 = w.Rattles; Vector3 dp = w.DoorWorld(ls, lr, 0.55f);
            p.Teleport(dp, ls == CorridorPlan.Left ? 270f : 90f); yield return 0.8f;
            r.Check(w.Rattles > r0, T + "una puerta cerrada: la manilla baja pero no abre (" + (w.Rattles - r0) + " intentos)");
        }
        // habitación: la puerta se abre al acercarte y se puede entrar
        int rr = -1, rs = 0;
        for (int j = 0; j < w.plan.rows && rr < 0; j++) for (int s = 0; s < 2; s++) if (w.plan.doors[s, j] == CorridorPlan.Door.Room) { rr = j; rs = s; break; }
        if (rr >= 0)
        {
            HotelDoorLeaf leaf = null; float best = 9f; Vector3 dp = w.DoorWorld(rs, rr, 0.6f);
            foreach (HotelDoorLeaf l in w.OpenableDoors) { float dd = Vector3.Distance(Flat(l.center), Flat(dp)); if (dd < best) { best = dd; leaf = l; } }
            p.Teleport(dp, rs == CorridorPlan.Left ? 270f : 90f); yield return 1.2f;
            r.Check(leaf != null && leaf.IsOpen, T + "la puerta de una habitación se abre al acercarte");
            Vector3 rc = w.RoomCenterWorld(rs, rr);
            p.Teleport(rc + Vector3.up * 0.1f, 0f); yield return 0.5f;
            r.Check(Vector3.Distance(Flat(p.transform.position), Flat(rc)) < 0.5f && p.transform.position.y > -0.3f, T + "dentro de la habitación (suelo firme)");
        }

        // salida: sin llave no abre, con la llave buena sí
        ExitLock.AutoKeyForTests = false;
        foreach (ItemType k in ItemDefs.Keys) { int guard = 20; while (Inventory.Instance.HasKey(k) && guard-- > 0) Inventory.Instance.RemoveOne(k); }
        ExitLock.Active.ResetCooldownForTests();
        Vector3 st = w.DoorWorld(w.plan.exitSide, w.plan.exitRow, 0.75f);
        p.Teleport(st + Vector3.up * 0.1f, w.plan.exitSide == CorridorPlan.Left ? 270f : 90f); yield return 0.8f;
        r.Check(gm.State == GameManager.GameState.Playing && !ExitLock.Active.Unlocked && ExitLock.Active.LockedNotices > 0, T + "la salida está cerrada con llave");
        p.Teleport(mid, 0f); yield return 0.3f;
        Inventory.Instance.Add(ExitLock.Active.CorrectKey, 1);
        ExitLock.Active.ResetCooldownForTests();
        p.Teleport(st + Vector3.up * 0.1f, w.plan.exitSide == CorridorPlan.Left ? 270f : 90f);
        float t0 = Time.realtimeSinceStartup;
        while (gm.State == GameManager.GameState.Playing && Time.realtimeSinceStartup - t0 < 5f) yield return 0.1f;
        r.Check(ExitLock.Active == null || ExitLock.Active.Unlocked, T + "la llave buena abre la salida");
        r.Check(w.ExitLeaf == null || Mathf.Abs(w.ExitAngle) > 1f || gm.State != GameManager.GameState.Playing, T + "la hoja de la salida se abre");
    }
}
