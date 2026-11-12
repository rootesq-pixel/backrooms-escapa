using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// v7.12.0 Nivel 188 (El Patio de las Ventanas): pruebas de edición (datos, recursos, plano por semilla, geometría) y de juego
// (cielo y luz ambiente, puertas, cortinas que te hacen desaparecer, la Ventana, los Periféricos, llave y salida por la ventana con sol).
public static class V7120Checks
{
    public const int Lv = 10;

    public static void Edit(SmokeTest.Report r)
    {
        string T = "[188] ";
        LevelConfig c = LevelConfig.Get(Lv);
        r.Check(c.layout == LayoutKind.Courtyard && c.DisplayNumber == "188" && c.name == "El Patio de las Ventanas" && !c.HasMonsterAI && c.monsterModel == LevelConfig.WindowEntitiesId && c.monsterCount == 0,
            T + "nivel 10 = Nivel 188 «" + c.name + "», sin monstruo de NavMesh (la Ventana y los Periféricos son propios)");
        r.Check(!string.IsNullOrEmpty(c.subtitle) && c.subtitle.Length > 40, T + "tarjeta del nivel con texto: «" + c.subtitle + "»");
        r.Check(Resources.Load<Texture2D>("UI/LevelThumbs/level_10") != null, T + "miniatura del selector de niveles (level_10)");
        foreach (string n in new[] { "W188Facade", "W188Paving", "W188Carpet", "W188RoomCarpet", "W188Wall", "W188Curtain", "W188Concrete", "W188Door" })
        {
            Texture2D a = Resources.Load<Texture2D>("W188/" + n + "_a"), nm = Resources.Load<Texture2D>("W188/" + n + "_n"), m = Resources.Load<Texture2D>("W188/" + n + "_m");
            r.Check(a != null && nm != null && m != null && a.width >= 1024, T + "texturas PBR " + n + " (albedo " + (a != null ? a.width : 0) + " px, normal, máscara)");
        }
        r.Check(Resources.Load<Texture2D>("W188/W188ExitView") != null && Resources.Load<Texture2D>("Field/sky_overcast") != null && Shader.Find("Backrooms/FieldSky") != null, T + "vista del prado al sol (Poly Haven) y cielo nublado (HDRI CC0)");
        int thumbs = 0; for (int i = 0; i < 10; i++) if (Resources.Load<Texture2D>("UI/LevelThumbs/level_" + i) != null) thumbs++;
        r.Check(thumbs == 10, T + "las ventanas abiertas enseñan los otros 10 niveles (" + thumbs + " vistas)");
        r.Check(LoreNotes.Valid(LoreNotes.IdFor(Lv, 2)) && LoreNotes.Get(LoreNotes.IdFor(Lv, 1)).text.Contains("señas") && LoreNotes.Get(LoreNotes.IdFor(Lv, 2)).text.Contains("cortina"), T + "3 notas de ambiente del nivel (la Ventana, las cortinas, la salida)");
        string credits = File.Exists("CREDITS.md") ? File.ReadAllText("CREDITS.md") : "";
        bool cr = credits.Contains("meadow_2") && credits.Contains("Poly Haven");
        foreach (string id in new[] { "PaintedPlaster017", "PavingStones136", "Carpet012", "Carpet016", "Wallpaper001A", "Fabric062", "Concrete034", "PaintedWood005" }) if (!credits.Contains(id)) cr = false;
        r.Check(cr, T + "CREDITS.md cita las 8 texturas ambientCG (CC0) y el HDRI meadow_2 de Poly Haven (CC0)");

        // plano por semilla: determinista, una salida en plantas 2-4, 4 ventanas de la Ventana, 9 abiertas, 36 habitaciones, 5 llaves
        bool det = true, differ = false, counts = true, keys = true, sep = true; string ex = "";
        for (int seed = 1; seed <= 200; seed++)
        {
            int sd = seed * 7919 + 3;
            W188Plan a = W188Plan.Make(sd), b = W188Plan.Make(sd);
            for (int f = 0; f < W188Plan.Floors; f++) for (int s = 0; s < 4; s++) for (int i = 0; i < W188Plan.Len(s); i++)
                if (a.kind[f, s, i] != b.kind[f, s, i] || a.variant[f, s, i] != b.variant[f, s, i]) det = false;
            if (seed <= 4) { W188Plan o = W188Plan.Make(sd + 1); if (o.exitF != a.exitF || o.exitS != a.exitS || o.exitI != a.exitI) differ = true; ex += " " + a.exitF + "/" + a.exitS + "/" + a.exitI; }
            if (a.Count(W188Plan.Kind.Exit) != 1 || a.exitF < 2 || a.Count(W188Plan.Kind.Entity) != W188Plan.EntityCount || a.Count(W188Plan.Kind.Open) != W188Plan.OpenCount
                || a.Count(W188Plan.Kind.Room) != W188Plan.RoomCount || a.Count(W188Plan.Kind.Passage) != 1 || a.Count(W188Plan.Kind.Lift) != 1) counts = false;
            if (a.KeyRooms.Count != ExitLock.KeyCount) keys = false;
            foreach (Vector3Int k in a.KeyRooms) if (a.K(k) != W188Plan.Kind.Room) keys = false;
            foreach (Vector3Int e in a.Entities) { if (e.x < 1 || (e.x == a.exitF && e.y == a.exitS && Mathf.Abs(e.z - a.exitI) <= 1)) sep = false; }
            var views = new HashSet<int>(); foreach (Vector3Int o in a.Opens) views.Add(a.variant[o.x, o.y, o.z]); if (views.Count != W188Plan.OpenCount) counts = false;
        }
        r.Check(det && differ, T + "plano sale de la semilla (igual en todos los equipos, distinto en cada partida); salidas planta/lado/módulo:" + ex);
        r.Check(counts, T + "200 semillas: 1 salida (planta ≥ 2), " + W188Plan.EntityCount + " ventanas de la Ventana, " + W188Plan.OpenCount + " abiertas a niveles distintos, " + W188Plan.RoomCount + " habitaciones que se abren, vestíbulo y ascensor");
        r.Check(keys, T + "las 5 llaves, cada una en una habitación que se abre");
        r.Check(sep, T + "la Ventana nunca en la planta baja ni junto a la salida");

        // sonido del susto: grave, corto, sin siseo
        {
            AudioClip sting = W188Events.CreateSting();
            float[] d = new float[sting.samples]; sting.GetData(d, 0);
            float hr = V790Checks.HighRatio(d, sting.frequency, 3000f), pk = 0f; foreach (float x in d) pk = Mathf.Max(pk, Mathf.Abs(x));
            r.Check(sting.length <= 1.5f && hr < 0.01f && pk > 0.3f && pk < 0.98f, T + "[Sonido] golpe de la Ventana: grave y corto, sin siseo (" + (hr * 100f).ToString("F2") + " % > 3 kHz, pico " + pk.ToString("F2") + ")");
            Object.DestroyImmediate(sting);
        }
    }

    static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    // geometría generada (en edición, tras gen.Generate())
    public static void Geometry(SmokeTest.Report r, string tag, BackroomsGenerator gen)
    {
        string T = tag + "[188] ";
        W188World w = gen.Root.GetComponentInChildren<W188World>(true);
        r.Check(w != null && gen.Courtyard != null && w.plan == gen.Courtyard, T + "patio construido por W188World");
        if (w == null) return;
        W188Plan P = w.plan;
        r.Check(w.FloorGroups.Count == W188Plan.Floors && w.ExtGroup != null && w.ColliderCount > 800, T + "5 plantas de interiores + fachadas; " + w.ColliderCount + " cajas de colisión");
        r.Check(w.Curtains.Count == P.Count(W188Plan.Kind.Room) && w.Figures.Count == W188Plan.EntityCount && w.Doors.Count == P.Count(W188Plan.Kind.Room) && w.Locked.Count == P.Count(W188Plan.Kind.Closed) + P.Count(W188Plan.Kind.Open),
            T + w.Curtains.Count + " cortinas, " + w.Doors.Count + " puertas que se abren, " + w.Locked.Count + " cerradas, " + w.Figures.Count + " siluetas de la Ventana");
        r.Check(gen.KeySpots.Count == ExitLock.KeyCount && gen.FixtureCount > 100, T + "5 llaves; " + gen.FixtureCount + " luces (se encienden las 8 más cercanas de tu planta)");
        Physics.SyncTransforms();
        // objetos: ninguno dentro de un muro; cada llave en una habitación que se abre
        int badSpot = 0; string bs = "";
        var all = new List<Vector3>(gen.KeySpots); all.AddRange(gen.BatterySpots); all.AddRange(gen.ExtraSpots);
        foreach (Vector3 s in all) if (Physics.CheckSphere(s + Vector3.up * 0.5f, 0.25f) || !Physics.Raycast(s + Vector3.up * 0.3f, Vector3.down, 0.6f)) { badSpot++; if (bs == "") bs = " " + w.levelRoot.InverseTransformPoint(s).ToString("F2"); }
        r.Check(badSpot == 0, T + "los " + all.Count + " objetos sobre el suelo y fuera de los muros" + bs);
        int keyIn = 0; foreach (Vector3 k in gen.KeySpots) { Vector3 lp = w.levelRoot.InverseTransformPoint(k); foreach (Vector3Int c in P.KeyRooms) { Vector3 q = W188Plan.FromLevel(c.y, lp); if (W188Plan.FloorOf(lp.y) == c.x && q.x > c.z * W188Plan.M && q.x < (c.z + 1) * W188Plan.M && q.z > 0.25f && q.z < 4f) { keyIn++; break; } } }
        r.Check(keyIn == ExitLock.KeyCount, T + "cada llave dentro de su habitación (" + keyIn + "/5)");
        // pasillos: en cada planta se da la vuelta entera al patio por el centro sin chocar
        int blocked = 0; string fh = "";
        Vector3[] ring = { new Vector3(1.25f, 0, 1.25f), new Vector3(33.95f, 0, 1.25f), new Vector3(33.95f, 0, 53.15f), new Vector3(1.25f, 0, 53.15f) };
        for (int f = 0; f < W188Plan.Floors; f++)
            for (int k = 0; k < 4; k++)
            {
                Vector3 a = w.levelRoot.TransformPoint(ring[k] + Vector3.up * (f * W188Plan.FH + 0.95f)), b = w.levelRoot.TransformPoint(ring[(k + 1) % 4] + Vector3.up * (f * W188Plan.FH + 0.95f));
                foreach (RaycastHit h in Physics.SphereCastAll(a, 0.34f, (b - a).normalized, (b - a).magnitude)) { blocked++; if (fh == "") fh = " planta " + f + " tramo " + k + ": " + h.collider.name + " " + w.levelRoot.InverseTransformPoint(h.point).ToString("F2"); break; }
            }
        r.Check(blocked == 0, T + "en las 5 plantas se rodea el patio entero por el pasillo sin chocar (" + blocked + " tramos bloqueados)" + fh);
        // cada puerta cerrada bloquea; la ventana de la fachada no deja pasar (cristal/colisión)
        int lockedOk = 0;
        foreach (W188World.LockedDoor d in w.Locked) { Vector3 o = d.pos + d.outward * 0.8f + Vector3.up * 1.0f; if (Physics.Raycast(o, -d.outward, 1.2f)) lockedOk++; }
        r.Check(lockedOk == w.Locked.Count, T + "las puertas cerradas tienen colisión (" + lockedOk + "/" + w.Locked.Count + ")");
        // escaleras: rayos hacia abajo a lo largo de cada tramo dan en la rampa a la altura de los peldaños
        int stairBad = 0; string sb = "";
        foreach (int s in W188Plan.StairSides)
            for (int f = 0; f < W188Plan.Floors - 1; f++)
            {
                float y0 = f * W188Plan.FH;
                for (int k = 0; k <= 8; k++)
                {
                    float t = (k + 0.5f) / 9f;
                    float dA = Mathf.Lerp(W188Plan.RoomEnd, W188World.StLand, t), yA = y0 + W188World.StRise * t;
                    float dB = Mathf.Lerp(W188World.StLand, W188Plan.RoomEnd, t), yB = y0 + W188World.StRise * (1f + t);
                    foreach (Vector3 q in new[] { W188Plan.ToLevel(s, -3.35f, yA, dA), W188Plan.ToLevel(s, -0.7f, yB, dB) })
                    {
                        Vector3 o = w.levelRoot.TransformPoint(q + Vector3.up * 1.2f); RaycastHit h;
                        bool hit = Physics.Raycast(o, Vector3.down, out h, 2.0f);
                        if (!hit || Mathf.Abs(h.point.y - w.levelRoot.TransformPoint(q).y) > 0.2f) { stairBad++; if (sb == "") sb = " lado " + s + " planta " + f + " " + q.ToString("F2") + (hit ? " da a " + h.point.y.ToString("F2") : " sin suelo"); }
                    }
                }
            }
        r.Check(stairBad == 0, T + "las dos escaleras (SO y NE) suben las 5 plantas con rampa bajo los peldaños" + sb);
        // salida
        Vector3 inside = w.ExitRoomWorld(1.2f, 0.6f), corr = w.ExitDoorWorld(W188Plan.CorrIn + 0.6f);
        Vector3 below = inside - Vector3.up * W188Plan.FH;
        r.Check(w.AtExit(inside) && !w.AtExit(corr) && !w.AtExit(below) && !w.AtExit(gen.PlayerStart), T + "la salida es asomarse a la ventana con sol desde dentro de su cuarto (planta " + P.exitF + ", lado " + P.exitS + ", módulo " + P.exitI + ")");
        r.Check(!Physics.CheckSphere(inside + Vector3.up * 0.9f, 0.34f) && !Physics.CheckSphere(corr + Vector3.up * 0.9f, 0.34f), T + "hay sitio dentro del cuarto de la salida y delante de su puerta");
        ExitDoorCue cue = gen.Root.GetComponentInChildren<ExitDoorCue>(true);
        r.Check(cue != null && cue.visibleDistance <= 8.01f && !cue.ledRenderer.enabled && !cue.glow.enabled && Vector3.Distance(cue.transform.position, w.ExitDoorWorld(W188Plan.CorrIn)) < 0.3f, T + "pista de la salida: luz de día bajo su puerta, apagada y solo a < 8 m");
        r.Check(w.ExitTransform != null && w.ExitLeaf != null, T + "puerta de salida con hoja (se abre con la llave buena)");
        // presupuesto
        int tris = 0, rends = 0; foreach (MeshFilter mf in gen.Root.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) { tris += (int)(mf.sharedMesh.GetIndexCount(0) / 3); for (int sm = 1; sm < mf.sharedMesh.subMeshCount; sm++) tris += (int)(mf.sharedMesh.GetIndexCount(sm) / 3); rends++; }
        int lights = gen.Root.GetComponentsInChildren<Light>(true).Length;
        r.Info(T + "triángulos " + tris + ", mallas " + rends + ", luces " + lights);
        {   // entrada andando a la escalera SO de la planta baja: cápsula del jugador (r 0,35, alto 1,8) por el hueco del tramo A
            Physics.SyncTransforms();
            int stBlk = 0; string stWho = "";
            for (float dd = 5.0f; dd > 1.9f; dd -= 0.1f)
            {
                float yy = Mathf.Clamp((4.0f - dd) / (4.0f - W188World.StLand), 0f, 1f) * W188World.StRise + 0.02f;
                Vector3 c = gen.Root.transform.TransformPoint(W188Plan.ToLevel(W188Plan.S, -3.35f, yy, dd));
                foreach (Collider cc in Physics.OverlapCapsule(c + Vector3.up * 0.37f, c + Vector3.up * 1.45f, 0.34f, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (cc.name == "StairRamp") continue;
                    stBlk++; if (stWho.Length < 400) stWho += " d" + dd.ToString("F1") + ":" + cc.name + " " + cc.bounds.center.ToString("F2") + "/" + cc.bounds.size.ToString("F2");
                }
            }
            r.Check(stBlk == 0, T + "la escalera SO de la planta baja deja pasar al jugador por su hueco" + (stBlk == 0 ? "" : " (choca:" + stWho + ")"));
        }
        r.Check(tris < 1500000 && rends < 600, T + "presupuesto: " + tris + " triángulos en " + rends + " mallas");
    }

    // un bot que anda (CharacterController de verdad) por una lista de puntos; res: [0] llegó, [1] metros, [2] atascos
    public static IEnumerator Walk(FirstPersonController p, List<Vector3> wp, float[] res, float timeout, bool sprint)
    {
        res[0] = 0f; res[1] = 0f; res[2] = 0f;
        p.debugInput = true; p.debugSprint = sprint;
        int k = 0; float t = 0f, stuckT = 0f; Vector3 last = p.transform.position; float walked = 0f;
        while (k < wp.Count && t < timeout && (GameManager.Instance == null || GameManager.Instance.State == GameManager.GameState.Playing))
        {
            p.SetStaminaForTests(p.maxStamina);
            Vector3 d = wp[k] - p.transform.position; float dy = d.y; d.y = 0f;
            if (d.magnitude < 0.35f && Mathf.Abs(dy) < 0.9f) { k++; continue; }
            p.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 0f);
            p.debugMove = new Vector2(0f, 1f);
            yield return 0f; t += Time.unscaledDeltaTime;
            Vector3 now = p.transform.position; float step = Vector3.Distance(Flat(now), Flat(last)); walked += step; last = now;
            if (step < 0.002f * Mathf.Max(1f, Time.timeScale)) stuckT += Time.deltaTime; else stuckT = 0f;
            if (stuckT > 4f) { res[2] += 1f; break; }
        }
        p.debugInput = false; p.debugSprint = false; p.debugMove = Vector2.zero;
        res[0] = k >= wp.Count ? 1f : 0f; res[1] = walked;
        if (res.Length > 3) res[3] = k;
    }

    // puntos (mundo) de la ruta ascensor → vestíbulo → pasillo → escalera SO hasta la planta de la salida → anillo → cuarto → ventana
    static readonly Vector2[] RingC = { new Vector2(1.25f, 1.25f), new Vector2(33.95f, 1.25f), new Vector2(33.95f, 53.15f), new Vector2(1.25f, 53.15f) };
    static float RingT(Vector2 p)
    {
        float L0 = 32.7f, L1 = 51.9f;
        if (Mathf.Abs(p.y - 1.25f) < 0.6f) return p.x - 1.25f;
        if (Mathf.Abs(p.x - 33.95f) < 0.6f) return L0 + (p.y - 1.25f);
        if (Mathf.Abs(p.y - 53.15f) < 0.6f) return L0 + L1 + (33.95f - p.x);
        return 2f * L0 + L1 + (53.15f - p.y);
    }
    public static void RingPath(W188World w, List<Vector3> wp, Vector2 from, Vector2 to, float y)
    {
        float per = 2f * (32.7f + 51.9f), ta = RingT(from), tb = RingT(to);
        float fwd = Mathf.Repeat(tb - ta, per); bool dirF = fwd <= per * 0.5f;
        float[] ct = { 0f, 32.7f, 32.7f + 51.9f, 2f * 32.7f + 51.9f };
        var mids = new List<KeyValuePair<float, Vector2>>();
        for (int c = 0; c < 4; c++)
        {
            float dc = dirF ? Mathf.Repeat(ct[c] - ta, per) : Mathf.Repeat(ta - ct[c], per);
            float dt = dirF ? fwd : per - fwd;
            if (dc > 0.01f && dc < dt) mids.Add(new KeyValuePair<float, Vector2>(dc, RingC[c]));
        }
        mids.Sort((a, b) => a.Key.CompareTo(b.Key));
        foreach (var m in mids) wp.Add(w.levelRoot.TransformPoint(new Vector3(m.Value.x, y, m.Value.y)));
        wp.Add(w.levelRoot.TransformPoint(new Vector3(to.x, y, to.y)));
    }
    static Vector3 L(W188World w, int s, float u, float y, float d) { return w.levelRoot.TransformPoint(W188Plan.ToLevel(s, u, y, d)); }
    public static List<Vector3> RouteToCorridor0(W188World w)
    {
        var wp = new List<Vector3>();
        int S = W188Plan.S;
        wp.Add(L(w, S, 1.6f, 0f, -1.2f));                    // delante de la puerta acristalada
        wp.Add(L(w, S, 1.6f, 0f, 1.8f));                     // vestíbulo
        wp.Add(L(w, S, W188Plan.DoorU(0), 0f, 3.2f));
        wp.Add(L(w, S, W188Plan.DoorU(0), 0f, 5.15f));        // pasillo de la planta baja
        return wp;
    }
    public static List<Vector3> RouteUp(W188World w, int fromF, int toF)
    {
        var wp = new List<Vector3>(); int S = W188Plan.S;
        for (int f = fromF; f < toF; f++)
        {
            float y0 = f * W188Plan.FH;
            wp.Add(L(w, S, -3.35f, y0, 5.0f));
            wp.Add(L(w, S, -3.35f, y0, 3.9f));
            wp.Add(L(w, S, -3.35f, y0 + 1.5f, 1.1f));
            wp.Add(L(w, S, -0.7f, y0 + 1.5f, 0.9f));
            wp.Add(L(w, S, -0.7f, y0 + 1.5f, 1.9f));
            wp.Add(L(w, S, -0.7f, y0 + 3f, 4.0f));
            wp.Add(L(w, S, -0.7f, y0 + 3f, 5.0f));
        }
        return wp;
    }
    public static List<Vector3> RouteToExit(W188World w, Vector3 fromWorld)
    {
        var wp = new List<Vector3>(); W188Plan P = w.plan; float y = P.exitF * W188Plan.FH;
        Vector3 lf = w.levelRoot.InverseTransformPoint(fromWorld);
        Vector2 a = SnapRing(new Vector2(lf.x, lf.z));
        wp.Add(w.levelRoot.TransformPoint(new Vector3(a.x, y, a.y)));
        Vector3 dc = W188Plan.ToLevel(P.exitS, W188Plan.DoorU(P.exitI), y, 5.15f);
        RingPath(w, wp, a, new Vector2(dc.x, dc.z), y);
        wp.Add(L(w, P.exitS, W188Plan.DoorU(P.exitI), y, 3.5f));
        wp.Add(L(w, P.exitS, W188Plan.UC(P.exitI) + 0.8f, y, 1.2f));
        return wp;
    }
    static Vector2 SnapRing(Vector2 p)
    {
        float best = 1e9f; Vector2 bp = p;
        for (int k = 0; k < 4; k++)
        {
            Vector2 a = RingC[k], b = RingC[(k + 1) % 4]; Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            Vector2 q = a + ab * t; float d = (q - p).sqrMagnitude; if (d < best) { best = d; bp = q; }
        }
        return bp;
    }

    // recorrido en juego (lo llama SmokeTest en el nivel 10); termina con victoria (último nivel)
    public static IEnumerator Play(SmokeTest.Report r, string tag, GameManager gm)
    {
        string T = tag + "[188] ";
        W188World w = W188World.Active; W188Events ev = W188Events.Active;
        r.Check(w != null && ev != null, T + "patio y sucesos activos");
        if (w == null || ev == null) yield break;
        FirstPersonController p = gm.player; Camera cam = gm.playerCamera.GetComponent<Camera>();
        r.Check(gm.monsters.Length == 0 && MonsterAI.Instances.Count == 0, T + "sin monstruos de NavMesh");
        r.Check(cam.clearFlags == CameraClearFlags.Skybox && RenderSettings.skybox != null && RenderSettings.skybox.shader.name == "Backrooms/FieldSky", T + "cielo nublado de verdad sobre el patio (HDRI)");
        r.Check(ExitLock.Active != null && gm.exit != null && GameManager.ExitTest != null, T + "salida con cerradura y prueba de salida propia (varias plantas)");
        W188Events.DisableForTests = true;
        W188Plan P = w.plan;

        // luz: a cielo abierto en el patio, cálida y baja dentro; grupos y luces de la planta
        Vector3 court = w.levelRoot.TransformPoint(new Vector3(W188Plan.Width * 0.5f + 3f, 0.1f, W188Plan.Depth * 0.5f));
        p.Teleport(court, 0f); yield return 1.2f;
        float kc = w.CourtK; Color skyC = RenderSettings.ambientSkyColor; int grpC = 0; foreach (GameObject g in w.FloorGroups) if (g.activeSelf) grpC++;
        Vector3 corr2 = w.DoorWorld(2, W188Plan.E, 5, 5.15f);
        p.Teleport(corr2, 0f); yield return 1.2f;
        float ki = w.CourtK; Color skyI = RenderSettings.ambientSkyColor; int grpI = 0; foreach (GameObject g in w.FloorGroups) if (g.activeSelf) grpI++;
        r.Check(kc > 0.95f && ki < 0.05f && skyC.r > 0.4f && skyI.r < 0.2f, T + "[Luz] en el patio luz de cielo (ambiente " + skyC.r.ToString("F2") + "), dentro luz baja de lámparas (" + skyI.r.ToString("F2") + ")");
        r.Check(grpC == 1 && grpI == 3 && w.PlayerFloor == 2 && w.FloorGroups[2].activeSelf && !w.FloorGroups[0].activeSelf, T + "[Rendimiento] en el patio se dibuja solo la planta baja por dentro; en la 2.ª, las plantas 1-3 (" + grpC + " / " + grpI + ")");
        r.Check(w.ActiveLamps >= 3 && w.ActiveLamps <= Mathf.Max(4, LevelConfig.Get(Lv).maxRealtimeLights), T + "[Rendimiento] luces encendidas: las " + w.ActiveLamps + " más cercanas de la planta");

        // puerta cerrada: la manilla no cede
        {
            W188World.LockedDoor d = null; foreach (W188World.LockedDoor q in w.Locked) if (q.f == 1) { d = q; break; }
            if (d != null)
            {
                int r0 = w.Rattles; Vector3 o = d.pos + d.outward * 0.5f; o.y += 0.1f;
                p.Teleport(o, Mathf.Atan2(-d.outward.x, -d.outward.z) * Mathf.Rad2Deg); yield return 0.6f;
                r.Check(w.Rattles > r0, T + "[Puertas] una cerrada: la manilla baja y no abre");
            }
        }
        // habitación: la puerta se abre al acercarse
        Vector3Int room = P.Rooms[0];
        {
            Vector3 st = w.DoorWorld(room.x, room.y, room.z, W188Plan.CorrIn + 0.7f);
            p.Teleport(st, W188World.YawToCourt(room.y)); yield return 1.2f;
            W188Door dn = null; float bd = 9f; foreach (W188Door dd in w.Doors) { float q = Vector3.Distance(Flat(dd.center), Flat(st)); if (q < bd && Mathf.Abs(dd.floorY - st.y) < 1f) { bd = q; dn = dd; } }
            r.Check(dn != null && dn.IsOpen, T + "[Puertas] la de una habitación se abre al acercarte (" + (dn != null ? dn.Angle.ToString("F0") : "-") + "°)");
            // dentro, andando hasta la ventana
            var wp = new List<Vector3> { w.DoorWorld(room.x, room.y, room.z, 3.3f), w.CellWorld(room.x, room.y, room.z, 0.8f, 1.3f) };
            float[] res = new float[4];
            IEnumerator wi = Walk(p, wp, res, 15f, false); while (wi.MoveNext()) yield return wi.Current;
            r.Check(res[0] > 0f, T + "[Puertas] se entra andando en la habitación hasta la ventana (" + res[1].ToString("F1") + " m)");
        }
        // cortina: mantener clic la descorre y desapareces del cuarto
        {
            W188World.Curtain c = null; foreach (W188World.Curtain q in w.Curtains) if (q.f == room.x && q.s == room.y && q.i == room.z) c = q;
            Vector3 eye = w.CellWorld(room.x, room.y, room.z, 0.8f, 1.3f);
            Vector3 to = c != null ? c.center - (eye + Vector3.up * 1.6f) : Vector3.forward;
            p.Teleport(eye, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg); yield return 0.4f;
            string prompt = ev.Prompt ?? "";
            int v0 = ev.CurtainVanishes; ev.debugHold = true;
            float tw = 0f; while (tw < 2.5f && ev.CurtainVanishes == v0) { yield return 0.1f; tw += 0.1f; }
            ev.debugHold = false; yield return 0.3f;
            Vector3 lp = w.levelRoot.InverseTransformPoint(p.transform.position);
            int nf = W188Plan.FloorOf(lp.y);
            r.Check(prompt.Contains("cortina") && c != null, T + "[Cortinas] mirando la cortina: «" + prompt + "»");
            r.Check(ev.CurtainVanishes == v0 + 1 && nf != room.x && Vector3.Distance(p.transform.position, ev.LastVanishTo) < 0.6f && !W188Plan.InCourtyard(lp, 0f), T + "[Cortinas] al descorrerla desapareces: apareces en un pasillo de otra planta (" + room.x + " → " + nf + ")");
            r.Check(c != null && c.busy && c.left.localScale.x < 0.5f, T + "[Cortinas] la cortina queda descorrida un momento");
        }
        // la Ventana: golpes al acercarte a su puerta, la silueta se gira; dentro te atrapa
        {
            W188World.Figure f = w.Figures[0];
            W188Events.DisableForTests = false;
            int k0 = ev.Knocks;
            Vector3 st = w.DoorWorld(f.f, f.s, f.i, W188Plan.CorrIn + 1.2f);
            p.Teleport(st, W188World.YawToCourt(f.s)); yield return 4.0f;
            Vector3 fwd = f.root.forward; Vector3 toDoor = f.doorWorld - f.root.position; fwd.y = 0f; toDoor.y = 0f;
            float ang = Vector3.Angle(fwd, toDoor);
            r.Check(ev.Knocks > k0, T + "[Ventana] junto a su puerta entornada se oyen golpes en el cristal (" + (ev.Knocks - k0) + ")");
            r.Check(ang < 45f, T + "[Ventana] la silueta deja de mirar al patio y se gira hacia ti (" + ang.ToString("F0") + "°)");
            W188Events.DisableForTests = true;
        }
        // Periféricos: en el patio, en el rabillo del ojo; si la miras, desaparece
        {
            p.Teleport(court, 0f); yield return 0.5f;
            W188Events.DisableForTests = false; ev.ForcePeripheral();
            float tw = 0f; while (tw < 3f && !ev.PeripheralVisible) { yield return 0.1f; tw += 0.1f; }
            bool vis = ev.PeripheralVisible; int n0 = ev.PeripheralsVanishedByLook;
            Transform pr = ev.transform.Find("Peripheral");
            float side = 0f;
            if (pr != null) { Vector3 tp = pr.position - cam.transform.position; tp.y = 0f; Vector3 cf = cam.transform.forward; cf.y = 0f; side = Vector3.Angle(cf, tp); p.Teleport(p.transform.position, Mathf.Atan2(tp.x, tp.z) * Mathf.Rad2Deg); }
            yield return 0.3f;
            r.Check(vis && side > 50f && side < 85f, T + "[Periféricos] una sombra quieta aparece en el rabillo del ojo (" + side.ToString("F0") + "° de donde miras)");
            r.Check(!ev.PeripheralVisible && ev.PeripheralsVanishedByLook == n0 + 1, T + "[Periféricos] al mirarla, ya no está");
            W188Events.DisableForTests = true;
        }
        // muerte por la Ventana (y se reinicia el nivel)
        {
            W188World.Figure f = w.Figures[0];
            int loads = GameBootstrap.Instance.LoadCount; gm.lostRestartSeconds = 0.4f;
            p.Teleport(w.CellWorld(f.f, f.s, f.i, -0.4f, 2.0f), W188World.YawToCourt(f.s));
            float tw = 0f; bool grabbing = false; while (tw < 3f && gm.State == GameManager.GameState.Playing) { yield return 0.1f; tw += 0.1f; if (ev.Grabbing) grabbing = true; }
            r.Check(grabbing && gm.State == GameManager.GameState.Lost && gm.LastDeathReason == W188Events.GrabText, T + "[Ventana] si entras en su cuarto te atrapa («" + gm.LastDeathReason + "»)");
            float t0 = Time.realtimeSinceStartup;
            while (!(GameBootstrap.Instance.LoadCount > loads && GameBootstrap.Instance.IsLoaded && W188World.Active != null && W188World.Active != w) && Time.realtimeSinceStartup - t0 < 60f) yield return 0.2f;
            yield return 1.0f;
            gm = GameManager.Instance; w = W188World.Active; ev = W188Events.Active; p = gm.player;
            r.Check(gm != null && GameBootstrap.Instance.CurrentLevel == Lv && w != null && gm.State == GameManager.GameState.Playing, T + "[Ventana] tras la muerte se reinicia el Nivel 188");
            ElevatorCabin cb = ElevatorCabin.Current; float tc = 0f; while (cb != null && ElevatorCabin.PlayerLocked && tc < 20f) { yield return 0.25f; tc += 0.25f; }
            if (w == null) yield break;
            P = w.plan;
        }
        W188Events.DisableForTests = true;
        // salida: sin llave no abre; con la buena se abre; asomarse a la ventana con sol gana
        ExitLock.AutoKeyForTests = false;
        foreach (ItemType k in ItemDefs.Keys) { int guard = 20; while (Inventory.Instance.HasKey(k) && guard-- > 0) Inventory.Instance.RemoveOne(k); }
        ExitLock.Active.ResetCooldownForTests();
        Vector3 ds = w.ExitDoorWorld(W188Plan.CorrIn + 0.5f);
        p.Teleport(ds, W188World.YawToCourt(P.exitS)); yield return 0.8f;
        r.Check(gm.State == GameManager.GameState.Playing && !ExitLock.Active.Unlocked && ExitLock.Active.LockedNotices > 0, T + "la puerta de la ventana con sol está cerrada con llave");
        // contra la puerta cerrada no se pasa (andando 2 s hacia dentro)
        {
            var wp = new List<Vector3> { w.ExitRoomWorld(2.5f, 0.6f) }; float[] res = new float[4];
            IEnumerator wi = Walk(p, wp, res, 2.5f, true); while (wi.MoveNext()) yield return wi.Current;
            Vector3 q = W188Plan.FromLevel(P.exitS, w.levelRoot.InverseTransformPoint(p.transform.position));
            r.Check(q.z > W188Plan.RoomEnd && gm.State == GameManager.GameState.Playing, T + "sin la llave no se entra (d " + q.z.ToString("F2") + ")");
        }
        Inventory.Instance.Add(ExitLock.Active.CorrectKey, 1);
        ExitLock.Active.ResetCooldownForTests();
        p.Teleport(ds, W188World.YawToCourt(P.exitS)); yield return 2.0f;
        r.Check(ExitLock.Active.Unlocked && Mathf.Abs(w.ExitAngle) > 50f, T + "la llave buena abre la puerta (" + w.ExitAngle.ToString("F0") + "°)");
        {
            var wp = new List<Vector3> { w.ExitDoorWorld(3.4f), w.ExitRoomWorld(1.2f, 0.8f) }; float[] res = new float[4];
            IEnumerator wi = Walk(p, wp, res, 12f, false); while (wi.MoveNext()) yield return wi.Current;
            float t0 = Time.realtimeSinceStartup; while (gm.State == GameManager.GameState.Playing && Time.realtimeSinceStartup - t0 < 3f) yield return 0.1f;
            r.Check(gm.State != GameManager.GameState.Playing, T + "dentro, andando hasta la ventana con sol se sale (" + gm.State + ")");
        }
        W188Events.DisableForTests = false;
        ExitLock.AutoKeyForTests = true;
    }
}
