// v7.3.0: Talasofobia (nivel 7). Recorrido completo en juego: casa inundada y escalera, bengala, nado y aire,
// barca (conducir, deriva, a salvo de los peces), escalera del muelle, faro, casco, jaula, El Farolero y la gruta (victoria).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class V730Checks
{
    static float Clock() { return Time.realtimeSinceStartup; }

    // espera hasta cond o tope (segundos de juego)
    static IEnumerator WaitFor(System.Func<bool> cond, float max)
    {
        float end = Time.time + max;   // tiempo de juego (el editor puede dar varias vueltas por fotograma)
        while (Time.time < end && !cond()) yield return null;
    }

    static IEnumerator Reload(SmokeTest.Report r, string T, string why)
    {
        GameBootstrap gb = GameBootstrap.Instance;
        int loads = gb.LoadCount;
        float rt0 = Time.realtimeSinceStartup;
        while (gb.LoadCount == loads && Time.realtimeSinceStartup - rt0 < 30f) yield return null;
        { IEnumerator wf1 = WaitFor(() => gb.LoadCount > loads && gb.IsLoaded && GameManager.Instance != null && OceanWorld.Active != null && OceanPlayer.Local != null, 90f); while (wf1.MoveNext()) yield return wf1.Current; }
        r.Check(gb.LoadCount > loads && gb.CurrentLevel == 7 && OceanWorld.Active != null && GameManager.Instance.State == GameManager.GameState.Playing, T + "tras " + why + " se reinicia Talasofobia");
        yield return 0.6f;
        GameManager.Instance.lostRestartSeconds = 0.3f;
    }

    static void Freeze(OceanWorld w, bool fish, bool angler)
    {
        if (w.fish != null) w.fish.enabled = fish;
        if (w.angler != null) w.angler.enabled = angler;
    }

    static void Face(FirstPersonController p, Vector3 target)
    {
        Vector3 d = target - p.cameraPivot.position;
        float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        p.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        p.SetPitch(-Mathf.Atan2(d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg);
    }

    public static IEnumerator OceanPlay(SmokeTest.Report r, string tag, GameManager gm)
    {
        string T = tag + "[Talasofobia] ";
        OceanWorld w = OceanWorld.Active;
        r.Check(w != null && w.water != null && w.water.ocean, T + "mar abierto creado (agua en modo océano)");
        if (w == null) yield break;
        gm.lostRestartSeconds = 0.3f;
        FirstPersonController p = gm.player;
        OceanPlayer op = OceanPlayer.Local;
        r.Check(op != null && op.fpc == p, T + "jugador con aire/casco (OceanPlayer)");
        r.Check(gm.monsters.Length == 0 && MonsterAI.Instances.Count == 0, T + "sin monstruo de NavMesh (sus criaturas son propias)");
        r.Check(w.boat != null && w.cage != null && w.rack != null && w.lighthouse != null && w.fish != null && w.fish.fish.Count == OceanFishSchool.Count && w.angler != null, T + "barca, jaula, cascos, faro, " + (w.fish != null ? w.fish.fish.Count : 0) + " peces y El Farolero");
        r.Check(w.Stilts.Count == OceanWorld.StiltHouses.Length && w.Ladders.Count >= OceanWorld.StiltHouses.Length + 1, T + w.Stilts.Count + " casas sobre pilotes y " + w.Ladders.Count + " escaleras desde el agua");
        r.Check(GameManager.ExitTest != null && gm.exit != null && ExitLock.Active == null, T + "salida propia (la poza de la gruta), sin cerradura");
        r.Check(!w.lighthouse.On, T + "el faro está apagado al empezar (lejos)");
        float far = gm.playerCamera.GetComponent<Camera>().farClipPlane;
        r.Check(far >= 300f, T + "plano lejano de la cámara " + far + " m (el faro se intuye entre la niebla)");
        ItemPickup flarePk = null;
        foreach (ItemPickup ip in Object.FindObjectsOfType<ItemPickup>()) if (ip.type == ItemType.FlareGun) flarePk = ip;
        r.Check(flarePk != null && (flarePk.transform.position - w.FlareSpot).magnitude < 0.6f && w.FlareSpot.y > 3.5f, T + "pistola de bengalas en el piso de arriba de la casa " + w.FlareHouse + " (y=" + w.FlareSpot.y.ToString("F1") + ")");
        Freeze(w, false, false);

        // ---- 1) casa inundada: el sofá flota y la escalera sube al piso de arriba
        {
            OceanFloat sofa = null; foreach (OceanFloat f in Object.FindObjectsOfType<OceanFloat>()) if (f.name.Contains("Sofa")) sofa = f;
            float y0 = sofa != null ? sofa.transform.position.y : 0f;
            yield return 1.0f;
            float y1 = sofa != null ? sofa.transform.position.y : 0f;
            r.Check(sofa != null && Mathf.Abs(y0) < 0.5f && Mathf.Abs(y0 - y1) > 0.001f, T + "sofá flotando en el salón (cabecea en la superficie)");
            p.Teleport(new Vector3(11.25f, -0.8f, 28.1f), 180f);
            yield return 0.4f;
            p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
            float t0 = Time.time + 7f, ymax = -9f; while (Time.time < t0) { yield return null; ymax = Mathf.Max(ymax, p.transform.position.y); }
            p.debugInput = false; p.debugMove = Vector2.zero;
            r.Check(ymax > 2.6f, T + "la escalera de la casa inundada sube al piso de arriba (altura " + ymax.ToString("F2") + ")");
        }

        // ---- 2) pistola de bengalas: recoger, disparar; se ve alta sobre la niebla
        {
            Inventory inv = Inventory.Instance;
            p.Teleport(w.FlareSpot + Vector3.up * 0.05f, 0f);
            { IEnumerator wf2 = WaitFor(() => inv.GetCount(ItemType.FlareGun) > 0, 3f); while (wf2.MoveNext()) yield return wf2.Current; }
            int n = inv.GetCount(ItemType.FlareGun);
            r.Check(n == 3, T + "se recoge la pistola de bengalas (" + n + " bengalas)");
            int slot = -1; for (int i = 0; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == ItemType.FlareGun) slot = i;
            int f0 = OceanFlare.All.Count;
            p.SetPitch(-60f);
            bool used = slot >= 0 && inv.UseSlot(slot);
            yield return 2.5f;
            OceanFlare fl = OceanFlare.All.Count > f0 ? OceanFlare.All[OceanFlare.All.Count - 1] : null;
            r.Check(used && fl != null && inv.GetCount(ItemType.FlareGun) == n - 1, T + "disparar gasta una bengala y la lanza");
            r.Check(fl != null && fl.transform.position.y > w.FlareSpot.y + 15f, T + "la bengala sube muy alto (y=" + (fl != null ? fl.transform.position.y.ToString("F0") : "-") + ")");
            Light ll = fl != null ? fl.GetComponentInChildren<Light>() : null;
            r.Check(ll != null && ll.enabled && ll.range > 30f, T + "la bengala ilumina (luz roja, alcance " + (ll != null ? ll.range.ToString("F0") : "-") + " m)");
            p.SetPitch(0f);
        }

        // ---- 3) nado en superficie: resistencia, aire al bucear, ahogarse
        Vector3 open = new Vector3(-8f, -0.6f, 46f);
        {
            p.Teleport(open, 0f);
            yield return 1.0f;
            float wy = p.transform.position.y;
            r.Check(p.IsSwimming && !p.IsUnderwater && wy > -2.2f && wy < 0.3f, T + "en mar abierto se nada en la superficie (pies a " + wy.ToString("F2") + ")");
            p.SetStaminaForTests(p.maxStamina);
            float s0 = p.Stamina01;
            p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
            Vector3 a = p.transform.position;
            yield return 3.0f;
            float moved = Vector3.Distance(a, p.transform.position);
            p.debugMove = Vector2.zero;
            r.Check(p.Stamina01 < s0 - 0.05f && moved > 3f, T + "nadar cansa (resistencia " + s0.ToString("F2") + " -> " + p.Stamina01.ToString("F2") + ", " + moved.ToString("F1") + " m)");
            p.SetStaminaForTests(0f); p.SetStamina01(0f);
            a = p.transform.position; p.debugMove = new Vector2(0f, 1f);
            yield return 2.0f;
            float slow = Vector3.Distance(a, p.transform.position) / 2f;
            r.Check(p.SwimExhausted && slow < 1.4f, T + "agotado se nada más despacio (" + slow.ToString("F2") + " m/s)");
            p.debugMove = Vector2.zero; p.SetStaminaForTests(p.maxStamina);
            // aire: bajo el agua sin casco se gasta y en la superficie vuelve
            p.debugCrouch = true;
            yield return 3.0f;
            float air = op.Air01;
            r.Check(p.IsUnderwater && air < 0.9f && air > 0.6f, T + "buceando sin casco se gasta el aire (" + air.ToString("F2") + ")");
            p.debugCrouch = false;
            { IEnumerator wf3 = WaitFor(() => op.Air01 > air + 0.15f, 6f); while (wf3.MoveNext()) yield return wf3.Current; }
            r.Check(!p.IsUnderwater && op.Air01 > air + 0.1f, T + "en la superficie se recupera el aire (" + op.Air01.ToString("F2") + ", bajo el agua " + p.IsUnderwater + ", pies " + p.transform.position.y.ToString("F2") + ")");
            p.debugInput = false;
        }

        // ---- 4) barca: subir, conducir (más lenta que nadando), los peces no atacan, deriva, bajar
        {
            OceanBoat b = w.boat;
            Vector3 bp = b.transform.position;
            p.Teleport(bp + new Vector3(-2.1f, -0.6f, 0f), 90f);
            yield return 0.6f;
            r.Check(b.CanBoardFrom(p.transform.position), T + "junto a la barca se puede subir");
            op.debugInteract = true; yield return null; yield return null; op.debugInteract = false;
            yield return 0.5f;
            r.Check(b.LocalSeat == 0 && p.IsMounted, T + "E: sube a la barca (asiento del conductor)");
            Vector3 b0 = b.transform.position;
            p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
            float vmax = 0f, tt = Time.time + 6f; while (Time.time < tt) { yield return null; vmax = Mathf.Max(vmax, b.speed); }
            float dist = Vector3.Distance(b0, b.transform.position);
            r.Check(dist > 3f && vmax <= OceanBoat.MaxSpeed + 0.05f && vmax < p.swimSpeed, T + "conduce la barca (" + dist.ToString("F1") + " m; máx " + vmax.ToString("F2") + " m/s, nadando " + p.swimSpeed.ToString("F1") + ")");
            r.Check(Vector3.Distance(p.transform.position, b.transform.position) < 2.5f, T + "el jugador va en la barca");
            // los peces no atacan a quien va en la barca
            Freeze(w, true, false);
            OceanFishSchool.Fish fz = w.fish.fish[0];
            fz.pos = p.transform.position + p.transform.forward * 4f + Vector3.down * 1.5f; fz.state = OceanFishSchool.FishState.Charge; fz.target = OceanNet.LocalId; fz.t = 0f;
            yield return 2.0f;
            r.Check(gm.State == GameManager.GameState.Playing && fz.state != OceanFishSchool.FishState.Charge, T + "en la barca los peces no muerden (estado del pez " + fz.state + ")");
            Freeze(w, false, false);
            // deriva: sin mandos, la barca ocupada va sola hacia la siguiente parada
            p.debugMove = Vector2.zero;
            yield return 1.5f;
            Vector3 d0 = b.transform.position; yield return 4.0f;
            r.Check(Vector3.Distance(d0, b.transform.position) > 1f || b.waitT > 0f, T + "sin conducir, la barca va a la deriva hacia la siguiente casa (" + Vector3.Distance(d0, b.transform.position).ToString("F1") + " m)");
            p.debugInput = false;
            op.debugInteract = true; yield return null; yield return null; op.debugInteract = false;
            yield return 0.6f;
            r.Check(b.LocalSeat < 0 && !p.IsMounted, T + "E: baja de la barca");
        }

        // ---- 5) mordisco de Los Pálidos (nadando entre las casas)
        {
            p.Teleport(open, 0f);
            yield return 0.8f;
            Freeze(w, true, false);
            OceanFishSchool.Fish fz = w.fish.fish[1];
            fz.pos = p.transform.position + new Vector3(5f, -1.5f, 0f); fz.state = OceanFishSchool.FishState.Charge; fz.target = OceanNet.LocalId; fz.t = 0f;
            { IEnumerator wf4 = WaitFor(() => gm.State != GameManager.GameState.Playing, 5f); while (wf4.MoveNext()) yield return wf4.Current; }
            r.Check(gm.State == GameManager.GameState.Lost && gm.LastDeathReason == OceanNet.KillText(OceanNet.KillFish), T + "un pez que embiste muerde y mata (\"" + gm.LastDeathReason + "\"; " + gm.State + ", pez " + fz.state + " a " + Vector3.Distance(fz.pos, p.transform.position).ToString("F1") + " m, nadando " + p.IsSwimming + ", mordiscos " + w.fish.Bites + ")");
            IEnumerator rl = Reload(r, T, "el mordisco"); while (rl.MoveNext()) yield return rl.Current;
            gm = GameManager.Instance; w = OceanWorld.Active; p = gm.player; op = OceanPlayer.Local; Freeze(w, false, false);
        }

        // ---- 6) ahogarse (bucear sin casco hasta quedarse sin aire)
        {
            p.Teleport(open, 0f);
            yield return 0.8f;
            p.debugInput = true; p.debugCrouch = true;
            { IEnumerator wf5 = WaitFor(() => gm.State != GameManager.GameState.Playing, OceanPlayer.BreathSeconds + OceanPlayer.DrownGrace + 6f); while (wf5.MoveNext()) yield return wf5.Current; }
            p.debugCrouch = false; p.debugInput = false;
            r.Check(gm.State == GameManager.GameState.Lost && gm.LastDeathReason == OceanNet.KillText(OceanNet.KillDrown), T + "sin aire te ahogas (\"" + gm.LastDeathReason + "\"; " + gm.State + ", aire " + op.Air01.ToString("F2") + ", bajo el agua " + p.IsUnderwater + ", pies " + p.transform.position.y.ToString("F2") + ")");
            IEnumerator rl = Reload(r, T, "ahogarse"); while (rl.MoveNext()) yield return rl.Current;
            gm = GameManager.Instance; w = OceanWorld.Active; p = gm.player; op = OceanPlayer.Local; Freeze(w, false, false);
        }

        // ---- 7) escalera del muelle desde el agua; el faro se enciende al acercarse
        {
            p.Teleport(OceanWorld.PierLadderFoot + new Vector3(0f, 0.4f, -1.0f), 0f);
            yield return 0.5f;
            r.Check(w.lighthouse.On, T + "el faro se enciende al acercarse");
            p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
            float t0 = Time.time + 6f, ymax = -9f; while (Time.time < t0) { yield return null; ymax = Mathf.Max(ymax, p.transform.position.y); }
            p.debugInput = false; p.debugMove = Vector2.zero;
            r.Check(ymax > OceanWorld.PierY - 0.2f && !p.IsSwimming, T + "la escalera del muelle saca del agua (altura " + ymax.ToString("F2") + ")");
        }

        // ---- 8) la jaula no baja sin casco; casco (mantener clic); jaula baja; El Farolero; gruta; victoria
        OceanCage cage = w.cage;
        {
            p.Teleport(OceanWorld.CageTop + Vector3.up * 0.08f, 270f);
            yield return 3.0f;
            r.Check(cage.State == OceanCage.CageState.TopOpen && cage.LocalInside && op.Prompt != null && op.Prompt.Contains("casco"), T + "la jaula no baja si el de dentro no lleva casco (\"" + op.Prompt + "\")");
            // casco
            p.Teleport(OceanWorld.HelmetRack + new Vector3(0f, 0.05f, -1.7f), 0f);
            yield return 0.3f;
            p.SetPitch(18f);
            yield return 0.2f;
            op.debugHold = true;
            yield return 0.5f;
            bool partial = !op.HasHelmet && op.HelmetHold01 > 0.2f;
            { IEnumerator wf6 = WaitFor(() => op.HasHelmet, 3f); while (wf6.MoveNext()) yield return wf6.Current; }
            op.debugHold = false;
            r.Check(partial && op.HasHelmet && w.rack.TakenCount == 1, T + "manteniendo clic izquierdo se pone el casco de buzo (parcial " + partial + ", aviso \"" + op.Prompt + "\", cámara " + op.cam.transform.position.ToString("F2") + ")");
            r.Check(op.flashlight != null && op.flashlight.IsOn && op.flashlight.range >= 15f && p.freeSwim, T + "el casco lleva lámpara encendida y permite nadar bajo el agua");
            p.SetPitch(0f);
        }
        {
            p.Teleport(OceanWorld.CageTop + Vector3.up * 0.08f, 270f);
            { IEnumerator wf7 = WaitFor(() => cage.State == OceanCage.CageState.Descending, 8f); while (wf7.MoveNext()) yield return wf7.Current; }
            r.Check(cage.State == OceanCage.CageState.Descending && p.IsMounted, T + "con casco la jaula se cierra y baja (con el jugador dentro)");
            float tD = Clock();
            { IEnumerator wf8 = WaitFor(() => cage.State == OceanCage.CageState.BottomOpen, 40f); while (wf8.MoveNext()) yield return wf8.Current; }
            yield return 1.6f;
            Vector3 pp = p.transform.position;
            r.Check(cage.State == OceanCage.CageState.BottomOpen && !p.IsMounted && pp.y < -40f, T + "la jaula llega al fondo y se abre (" + pp.y.ToString("F1") + " m, " + (Clock() - tD).ToString("F0") + " s)");
            r.Check(p.IsUnderwater && op.Air01 > 0.99f && gm.State == GameManager.GameState.Playing, T + "con el casco no se gasta el aire en el fondo");
        }
        // El Farolero: con la lámpara apagada no te ve a 10 m; encendida sí
        {
            OceanAngler an = w.angler;
            Vector3 me = p.transform.position;
            Freeze(w, false, true);
            Vector3 ap = me + new Vector3(10f, 1f, 4f);
            op.flashlight.SetOn(false);
            an.ForceForTests(ap, OceanAngler.AState.Patrol);
            yield return 1.0f;
            r.Check(an.State != OceanAngler.AState.Hunt, T + "El Farolero no ve a 10 m con la lámpara apagada (" + an.State + ")");
            op.flashlight.SetOn(true);
            an.ForceForTests(ap, OceanAngler.AState.Patrol);
            { IEnumerator wf9 = WaitFor(() => an.State == OceanAngler.AState.Hunt, 2f); while (wf9.MoveNext()) yield return wf9.Current; }
            r.Check(an.State == OceanAngler.AState.Hunt, T + "con la lámpara encendida El Farolero te detecta a 10 m y caza");
            Freeze(w, false, false);
            an.ForceForTests(OceanAngler.Region.center + new Vector3(25f, 0f, 30f), OceanAngler.AState.Patrol);
        }
        // nado guiado: puerta de la jaula (-x), ~15-20° a la izquierda hasta la boca de la gruta, túnel y subida a la poza
        {
            Vector3 start = p.transform.position;
            Vector3 mouth = OceanWorld.TunnelPath[0];
            Vector3 fwd = Vector3.left, toMouth = mouth - start; toMouth.y = 0f;
            float ang = Vector3.SignedAngle(fwd, toMouth, Vector3.up);
            r.Check(ang < -10f && ang > -25f, T + "la boca de la gruta queda " + (-ang).ToString("F0") + "° a la izquierda de la puerta de la jaula");
            List<Vector3> route = new List<Vector3>();
            route.Add(new Vector3(start.x - 2.5f, start.y + 0.6f, start.z));
            route.Add(mouth + new Vector3(3.5f, 0f, 0f));
            route.AddRange(OceanWorld.TunnelPath);
            route.Add(OceanWorld.PocketCenter + Vector3.down * 0.5f);
            p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
            int k = 0; float tt = 0f, stuck = 0f, t0 = Time.time, lastT = Time.time; Vector3 last = p.transform.position;
            while (k < route.Count && tt < 90f && gm.State == GameManager.GameState.Playing)
            {
                Vector3 eye = p.cameraPivot.position;
                Face(p, route[k]);
                p.debugJump = route[k].y > eye.y + 2f;
                if (Vector3.Distance(eye, route[k]) < 1.6f) k++;
                yield return null; tt = Time.time - t0;
                if (Time.time - lastT > 0.5f) { stuck = (p.transform.position - last).magnitude < 0.1f ? stuck + (Time.time - lastT) : 0f; last = p.transform.position; lastT = Time.time; }
                if (stuck > 4f) break;
            }
            string blk = "";
            if (stuck > 4f && k < route.Count)
            {
                Vector3 e = p.cameraPivot.position, dd = route[k] - e;
                foreach (RaycastHit h in Physics.SphereCastAll(e - Vector3.up * 0.8f, 0.4f, dd.normalized, 2f, ~0, QueryTriggerInteraction.Ignore)) blk += h.collider.name + "@" + h.point.ToString("F1") + " ";
                foreach (Collider c in Physics.OverlapSphere(p.transform.position + Vector3.up * 0.4f, 0.6f, ~0, QueryTriggerInteraction.Ignore)) blk += "[" + c.name + "]";
                r.Info(T + "atasco: objetivo " + route[k].ToString("F1") + ", choques: " + blk);
            }
            p.debugInput = false; p.debugJump = false; p.debugMove = Vector2.zero;
            r.Check(k >= route.Count - 1 || gm.State == GameManager.GameState.Won, T + "se nada por el túnel hasta la poza de la gruta (punto " + k + "/" + route.Count + ", " + tt.ToString("F0") + " s" + (stuck > 4f ? ", atascado en " + p.transform.position.ToString("F1") : "") + ")");
            p.debugInput = true; p.debugJump = true; p.debugMove = Vector2.zero;   // Espacio: subir a respirar
            { IEnumerator wfx = WaitFor(() => gm.State != GameManager.GameState.Playing, 5f); while (wfx.MoveNext()) yield return wfx.Current; }
            p.debugJump = false; p.debugInput = false;
            r.Check(gm.State == GameManager.GameState.Won, T + "salir a la superficie en la poza de la gruta completa el nivel (" + gm.State + "; pies " + p.transform.position.ToString("F2") + ", AtExit " + OceanWorld.AtExit(p.transform.position) + ", ExitTest " + (GameManager.ExitTest != null) + ", exit " + (gm.exit != null) + ", nadando " + p.IsSwimming + ")");
        }
    }
}
