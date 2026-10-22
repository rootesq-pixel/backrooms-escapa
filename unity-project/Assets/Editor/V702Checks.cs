using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;

// v7.0.2: pruebas por nivel de mímico (encuentro garantizado y visible), cordura por cercanía y silbido (G).
public static class V702Checks
{
    public static string ShotDir = "/workspace/v702shots";

    private static Camera Cam(GameManager gm) { return gm.player.GetComponentInChildren<Camera>(); }

    private static bool Sample(Vector3 p, float r, out Vector3 q)
    {
        if (NavMesh.SamplePosition(p, out NavMeshHit h, r, NavMesh.AllAreas)) { q = h.position; return true; }
        q = p; return false;
    }

    // dirección con 'dist' metros libres desde el ojo del jugador
    private static bool OpenDir(GameManager gm, float dist, out Vector3 dir, out Vector3 spot)
    {
        Vector3 eye = gm.player.transform.position + Vector3.up * 1.5f;
        for (int a = 0; a < 16; a++)
        {
            Vector3 d = Quaternion.Euler(0f, a * 22.5f, 0f) * Vector3.forward;
            if (Physics.Raycast(eye, d, dist + 0.8f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (!Sample(gm.player.transform.position + d * dist, 1.2f, out Vector3 q)) continue;
            Vector3 fq = q - gm.player.transform.position; fq.y = 0f;
            if (Mathf.Abs(fq.magnitude - dist) > 1.3f || Mathf.Abs(q.y - gm.player.transform.position.y) > 1f) continue;
            if (Physics.Linecast(eye, q + Vector3.up * 1.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
            dir = d; spot = q; return true;
        }
        dir = Vector3.forward; spot = gm.player.transform.position; return false;
    }

    private static void Place(MonsterAI m, Vector3 p, float yaw) { m.WarpTo(p, yaw); }

    // lleva al jugador a un punto del NavMesh cerca de 'home' con 'dist' metros libres (la cabina del ascensor es estrecha)
    private static float PathLen(Vector3 a, Vector3 b)
    {
        UnityEngine.AI.NavMeshPath path = new UnityEngine.AI.NavMeshPath();
        if (!UnityEngine.AI.NavMesh.SamplePosition(a, out UnityEngine.AI.NavMeshHit ha, 2f, UnityEngine.AI.NavMesh.AllAreas) || !UnityEngine.AI.NavMesh.SamplePosition(b, out UnityEngine.AI.NavMeshHit hb, 3f, UnityEngine.AI.NavMesh.AllAreas)) return -1f;
        if (!UnityEngine.AI.NavMesh.CalculatePath(ha.position, hb.position, UnityEngine.AI.NavMesh.AllAreas, path) || path.corners.Length < 2) return -1f;
        float L = 0f; for (int i = 1; i < path.corners.Length; i++) L += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return L;
    }

    private static IEnumerator MoveToOpen(GameManager gm, Vector3 home, float yaw, float dist, Action<bool> done)
    {
        bool ok = OpenDir(gm, dist, out Vector3 d0, out Vector3 s0);
        for (int a = 0; a < 80 && !ok; a++)
        {
            Vector3 c = home + Quaternion.Euler(0f, a * 47f, 0f) * Vector3.forward * (5f + (a % 8) * 4f);
            if (!Sample(c, 2.5f, out Vector3 q) || Mathf.Abs(q.y - home.y) > 2.5f) continue;
            gm.player.Teleport(q + Vector3.up * 0.05f, yaw);
            yield return null;
            ok = OpenDir(gm, dist, out d0, out s0);
            if (ok) gm.player.Teleport(gm.player.transform.position, Quaternion.LookRotation(d0).eulerAngles.y);
        }
        done(ok);
    }

    public static void Capture(Camera cam, string file)
    {
        if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;   // -nographics: Render revienta
        try
        {
            RenderTexture rt = new RenderTexture(1280, 720, 24);
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            Texture2D t = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); t.Apply();
            Directory.CreateDirectory(ShotDir);
            File.WriteAllBytes(Path.Combine(ShotDir, file), t.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = prev;
            UnityEngine.Object.DestroyImmediate(t); UnityEngine.Object.DestroyImmediate(rt);
        }
        catch (Exception e) { Debug.LogWarning("[V702] captura: " + e.Message); }
    }

    public static IEnumerator LevelChecks(SmokeTest.Report r, string tag, int lv, LevelConfig cfg, GameManager gm)
    {
        Vector3 home = gm.player.transform.position; float homeYaw = gm.player.transform.eulerAngles.y;
        // que los paseos de la prueba no recojan objetos (los cuentan pruebas posteriores)
        List<Behaviour> pks = new List<Behaviour>();
        foreach (ItemPickup ip in UnityEngine.Object.FindObjectsOfType<ItemPickup>()) if (ip.enabled) { ip.enabled = false; pks.Add(ip); }
        foreach (BatteryPickup bp in UnityEngine.Object.FindObjectsOfType<BatteryPickup>()) if (bp.enabled) { bp.enabled = false; pks.Add(bp); }
        MonsterAI main = null, mim = null; int mainN = 0, mimN = 0;
        foreach (MonsterAI m in gm.monsters) { if (m == null) continue; if (m.mimic != null) { mimN++; mim = m; } else { mainN++; if (main == null) main = m; } }
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        r.Check(mainN == Mathf.Max(1, cfg.monsterCount), tag + "[v7.0.2] monstruo(s) principal(es) del nivel intactos: " + mainN);
        r.Check(lv >= 1 ? mimN == 1 : mimN == 0, tag + "[Mímico] exactamente " + (lv >= 1 ? 1 : 0) + " mímico además del monstruo (hay " + mimN + ")");
        if (main != null && main.visual != null && main.visual.profile != null)
            r.Check(main.visual.profile.id == cfg.monsterModel, tag + "[v7.0.2] el monstruo principal conserva su modelo (" + main.visual.profile.id + ")");

        // ---------------- cordura por cercanía ----------------
        Sanity san = Sanity.Instance;
        Flashlight fl = gm.player.GetComponentInChildren<Flashlight>();
        // la salida (cabina del ascensor) suele ser estrecha: buscar un sitio del NavMesh con 4 m libres
        bool open4 = OpenDir(gm, 4f, out Vector3 sd, out Vector3 sp);
        for (int a = 0; a < 80 && !open4 && san != null && main != null; a++)
        {
            Vector3 c = home + Quaternion.Euler(0f, a * 47f, 0f) * Vector3.forward * (5f + (a % 8) * 4f);
            if (!Sample(c, 2.5f, out Vector3 q) || Mathf.Abs(q.y - home.y) > 2.5f) continue;
            gm.player.Teleport(q + Vector3.up * 0.05f, homeYaw);
            yield return null;
            open4 = OpenDir(gm, 4f, out sd, out sp);
        }
        // v7.1.2: en niveles con 2 monstruos principales, el otro (quieto donde estuviera) podía quedar a pocos metros del
        // punto de la prueba y seguir drenando (5/s constantes: fallaban «a ~10 m», «lejos» y «se recupera» al azar).
        // Mientras dura la prueba de cordura solo cuenta el monstruo que se coloca a mano (y el mímico, que se aparta luego).
        List<MonsterAI> parked = new List<MonsterAI>();
        foreach (MonsterAI m in gm.monsters) if (m != null && m != main && m != mim && m.enabled) { m.enabled = false; parked.Add(m); }
        if (san != null && main != null && open4)
        {
            if (fl != null) { fl.SetBattery(100f); fl.SetOn(true); }
            float yawTo = Quaternion.LookRotation(sd).eulerAngles.y;
            Place(main, sp, yawTo + 180f);
            gm.player.Teleport(gm.player.transform.position, yawTo);
            yield return 0.8f;
            float v0 = san.value; float dLook = san.MonsterDrain; bool look = san.LookingAtMonster;
            yield return 1.0f;
            r.Check(look && dLook >= Sanity.FearRate * Sanity.LookMult * 0.8f, tag + "[Cordura] a ~4 m mirándolo: drenaje fuerte " + dLook.ToString("F2") + "/s (mirando=" + look + ")");
            r.Check(san.value < v0 - 0.5f || v0 < 1f, tag + "[Cordura] la barra COR baja cerca del monstruo (" + v0.ToString("F1") + " → " + san.value.ToString("F1") + ")");
            gm.player.Teleport(gm.player.transform.position, yawTo + 180f);
            yield return 0.6f;
            float dAway = san.MonsterDrain;
            r.Check(!san.LookingAtMonster && dAway > 0.5f && dAway < dLook * 0.75f, tag + "[Cordura] a ~4 m de espaldas: drena, pero menos que mirándolo (" + dAway.ToString("F2") + " < " + dLook.ToString("F2") + ")");
            if (OpenDir(gm, 10f, out Vector3 sd2, out Vector3 sp2))
            {
                Place(main, sp2, 0f); gm.player.Teleport(gm.player.transform.position, Quaternion.LookRotation(sd2).eulerAngles.y + 180f);
                yield return 0.6f;
                float dMid = san.MonsterDrain;
                r.Check(dMid > 0.05f && dMid < dAway, tag + "[Cordura] a ~10 m drena menos (" + dMid.ToString("F2") + "/s) que a 4 m");
            }
            Vector3 far = main.transform.position + new Vector3(400f, 0f, 400f);
            Place(main, far, 0f);
            yield return 0.5f;
            r.Check(san.MonsterDrain < 0.01f, tag + "[Cordura] lejos (>15 m): sin drenaje por monstruos (" + san.MonsterDrain.ToString("F2") + ")");
            if (mim != null) Place(mim, far + new Vector3(60f, 0f, 0f), 0f);   // el imitador disfrazado también inquieta un poco: lejos
            yield return Sanity.RecoverDelay + 0.6f;
            bool under = gm.player.IsUnderwater;
            r.Check(under || (fl != null && fl.IsOn && san.DrainRate > 0f && san.DrainRate <= Sanity.RecoverRate + 0.01f), tag + "[Cordura] lejos y con luz se recupera despacio (" + san.DrainRate.ToString("F2") + "/s)");
            r.Check(VhsEffect.Active == null || VhsEffect.Active.sanityGlitch >= 0f, tag + "[Cordura] enganchada a la interferencia VHS");
            if (lv == 1)
            {
                float keep = san.value; san.value = 10f; yield return 0.4f;
                r.Check(VhsEffect.Active != null && VhsEffect.Active.sanityGlitch > 0.3f, tag + "[Cordura] cordura muy baja → interferencia VHS fuerte (" + (VhsEffect.Active != null ? VhsEffect.Active.sanityGlitch.ToString("F2") : "-") + ")");
                san.value = keep;
            }
            r.Check(Sanity.FearFactor(16f) == 0f && Sanity.FearFactor(5f) == 1f && Sanity.FearFactor(10f) > 0.2f && Sanity.FearFactor(10f) < 0.8f, tag + "[Cordura] curva 15 m → 5 m");
        }
        else r.Check(san != null && main != null, tag + "[Cordura] (no hay hueco abierto de 4 m para la prueba: " + (san != null) + ")");
        foreach (MonsterAI m in parked) if (m != null) m.enabled = true;

        // ---------------- silbido ----------------
        Whistle wh = gm.player.GetComponent<Whistle>();
        r.Check(wh != null, tag + "[Silbido] componente en el jugador");
        if (wh != null)
        {
            gm.player.Teleport(home, homeYaw); yield return 0.3f;
            int e0 = Whistle.EmitCount;
            wh.debugPress = true; yield return 0.2f;
            int e1 = Whistle.EmitCount;
            wh.debugPress = true; yield return 0.2f;
            r.Check(e1 == e0 + 1 && Whistle.EmitCount == e1 && wh.CooldownLeft > 2f, tag + "[Silbido] G silba una vez y respeta el enfriamiento de " + Whistle.Cooldown + " s (" + (e1 - e0) + " / " + (Whistle.EmitCount - e1) + ")");
            r.Check(GameObject.Find("Whistle") != null || Whistle.LastPos != Vector3.zero, tag + "[Silbido] suena como fuente 3D");
        }
        if (main != null)
        {
            // un punto del NavMesh lejos del jugador; el silbido a ~12 m del monstruo
            bool found = false; Vector3 A = Vector3.zero, W = Vector3.zero;
            for (int a = 0; a < 60 && !found; a++)
            {
                float ang = a * 37f, dist = 18f + (a % 5) * 6f;
                Vector3 c = home + Quaternion.Euler(0f, ang, 0f) * Vector3.forward * dist;
                if (!Sample(c, 3f, out A)) continue;
                for (int b = 0; b < 12; b++)
                {
                    Vector3 c2 = A + Quaternion.Euler(0f, b * 30f, 0f) * Vector3.forward * 12f;
                    if (!Sample(c2, 2f, out W)) continue;
                    NavMeshPath np = new NavMeshPath();
                    if (!NavMesh.CalculatePath(A, W, NavMesh.AllAreas, np) || np.status != NavMeshPathStatus.PathComplete) continue;
                    if (Vector3.Distance(A, W) < 8f) continue;
                    found = true; break;
                }
            }
            if (!found) { A = main.transform.position; W = A + Vector3.forward * 10f; Sample(W, 3f, out W); }
            // el jugador lejos (para que no lo vea ni lo oiga)
            gm.player.Teleport(home, homeYaw);
            Place(main, A, 0f); main.Resume();
            yield return 0.4f;
            int h0 = main.WhistlesHeard;
            Whistle.Emit(W + Vector3.up * 1.55f, 0, 1, false);
            bool heardNow = main.WhistlesHeard == h0 + 1;
            bool notInstant = main.CurrentState != MonsterAI.State.Search || true;
            yield return 1.3f;
            bool searching = main.CurrentState == MonsterAI.State.Search || main.CurrentState == MonsterAI.State.Chase;
            float d0 = Vector3.Distance(A, W), ip = Vector3.Distance(main.InvestigatePoint, W);
            float d1 = Vector3.Distance(main.transform.position, W);
            float p0 = PathLen(main.transform.position, W), p1 = p0;   // a través de pared el camino rodea: se mide lo que le falta por recorrer
            for (float tq = 0f; tq < 4f; tq += 0.1f) { yield return 0.1f; d1 = Mathf.Min(d1, Vector3.Distance(main.transform.position, W)); float pq = PathLen(main.transform.position, W); if (pq >= 0f) p1 = Mathf.Min(p1, pq); }   // lo más cerca que llega (luego sigue buscando alrededor)
            float range = MonsterAI.WhistleRange(W, A, out bool wall);
            r.Check(heardNow && searching && ip < 3.5f, tag + "[Silbido] el monstruo a " + d0.ToString("F0") + " m lo oye y va a investigar el sitio (estado " + main.CurrentState + ", punto a " + ip.ToString("F1") + " m" + (wall ? ", a través de pared" : "") + ")");
            r.Check(d1 < d0 - 1.5f || d1 < 2.5f || (p0 > 0f && p1 < p0 - 1.5f), tag + "[Silbido] el monstruo se acerca al silbido (" + d0.ToString("F1") + " → " + d1.ToString("F1") + " m; por el camino " + p0.ToString("F1") + " → " + p1.ToString("F1") + " m)");
            // lejos: no lo oye
            main.Stop(); yield return 0.1f; main.Resume();
            int h1 = main.WhistlesHeard;
            Whistle.Emit(main.transform.position + new Vector3(90f, 1.5f, 0f), 0, 2, false);
            r.Check(main.WhistlesHeard == h1, tag + "[Silbido] a 90 m no lo oye (radio " + MonsterAI.WhistleRadius + " m, " + (MonsterAI.WhistleRadius * MonsterAI.WhistleWallFactor) + " con paredes)");
            if (cfg.water) r.Check(range > MonsterAI.WhistleRadius * (wall ? MonsterAI.WhistleWallFactor : 1f) * 1.2f, tag + "[Silbido] acústica de agua/azulejos (" + cfg.name + "): el silbido llega más lejos (" + range.ToString("F0") + " m)");
            main.Stop();
            r.Info(tag + "[Silbido] radio efectivo de la prueba " + range.ToString("F0") + " m" + (wall ? " (con paredes)" : ""));
        }

        // ---------------- mímico: encuentro visible andando ----------------
        if (mim != null && mim.mimic != null)
        {
            MimicDisguise md = mim.mimic;
            r.Check(md.Disguised && md.puppet != null, tag + "[Mímico] disfrazado con el aspecto del jugador (copia " + (md.CopiedId < 0 ? "propia" : "de " + md.CopiedName) + ")");
            r.Check(md.StageTimer > 0f && (cfg.layout != LayoutKind.Field || (md.FirstStageDelay >= 60f && md.FirstStageDelay <= 120f)), tag + "[Mímico] primer encuentro programado a los " + md.FirstStageDelay.ToString("F0") + " s (quedan " + md.StageTimer.ToString("F0") + " s)" + (cfg.layout == LayoutKind.Field ? " (campo: 60-120 s)" : ""));
            // responde al silbido (a veces): con probabilidad forzada a 1 y espera corta
            {
                gm.player.Teleport(home, homeYaw);
                Place(mim, home + new Vector3(300f, 0f, 300f), 0f);   // lejos: que no lo descubra la mirada durante la espera
                yield return 0.2f;
                mim.Resume();
                float ch = MimicDisguise.WhistleBackChance; MimicDisguise.WhistleBackChance = 1f; MimicDisguise.WhistleBackDelayOverride = 0.6f;
                md.CancelWhistleBack();   // una respuesta pendiente de los silbidos anteriores (6-12 s) bloquearía esta
                int wb0 = md.WhistleBacks, em0 = Whistle.EmitCount;
                Whistle.Emit(home + Vector3.up * 1.55f, 0, 0, false);
                bool pend = md.WhistleBackPending;
                float tw = 0f; while (tw < 3f && md.WhistleBacks == wb0) { yield return 0.1f; tw += 0.1f; }
                r.Check(pend && md.WhistleBacks == wb0 + 1 && Whistle.LastFromMimic && Whistle.EmitCount == em0 + 2 && (Whistle.LastPos - mim.transform.position).magnitude < 2.5f,
                    tag + "[Silbido] el mímico responde al silbido desde su posición (" + (md.WhistleBacks - wb0) + " respuesta, a los " + tw.ToString("F1") + " s; pendiente " + pend + ", disfrazado " + md.Disguised + ", " + md.WhistleDiag + ")");
                MimicDisguise.WhistleBackChance = 0f; int wb1 = md.WhistleBacks;
                Whistle.Emit(home + Vector3.up * 1.55f, 0, 1, false); yield return 1.0f;
                r.Check(md.WhistleBacks == wb1, tag + "[Silbido] el mímico no siempre responde (probabilidad " + (ch * 100f).ToString("F0") + " %)");
                MimicDisguise.WhistleBackChance = ch; MimicDisguise.WhistleBackDelayOverride = -1f;
                foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
            }
            gm.player.Teleport(home, homeYaw); yield return 0.3f;
            Place(mim, home + new Vector3(300f, 0f, 300f), 0f);
            bool openOk = false;
            IEnumerator mo = MoveToOpen(gm, home, homeYaw, cfg.layout == LayoutKind.Field ? 20f : 9f, b => openOk = b);
            while (mo.MoveNext()) yield return mo.Current;
            r.Info(tag + "[Mímico] el paseo empieza en un sitio abierto: " + openOk);
            mim.Resume();
            md.ForceStageSoon(1.5f);
            Camera cam = Cam(gm);
            const float N = 20f;
            float t = 0f; bool seen = false; int turns = 0;
            gm.player.debugInput = true; gm.player.debugMove = new Vector2(0f, 0.6f);
            Vector3 lastP = gm.player.transform.position; float stuck = 0f;
            while (t < N)
            {
                yield return 0.1f; t += 0.1f;
                if (md.OnScreen(gm.player.transform)) { seen = true; break; }
                // andando por el nivel: si se atasca contra una pared, gira
                if ((gm.player.transform.position - lastP).sqrMagnitude < 0.0004f) stuck += 0.1f; else stuck = 0f;
                lastP = gm.player.transform.position;
                if (stuck > 0.6f) { gm.player.Teleport(gm.player.transform.position, gm.player.transform.eulerAngles.y + 90f); stuck = 0f; turns++; }
            }
            gm.player.debugMove = Vector2.zero; gm.player.debugInput = false;
            float dist = Vector3.Distance(mim.transform.position, gm.player.transform.position);
            r.Check(seen, tag + "[Mímico] andando, el mímico aparece en pantalla en " + t.ToString("F1") + " s (límite " + N + " s; a " + dist.ToString("F0") + " m; fase " + md.CurrentPhase + ", escenificaciones " + md.StageCount + "; " + md.StageDiag + ")");
            if (md.puppet != null)
            {
                yield return 0.2f;
                r.Check(md.puppet.AnimatorRunning && md.puppet.ArmIkActive && md.puppet.ArmHandError < 0.06f, tag + "[Mímico] tras escenificarse se anima (sin pose de T): animador " + md.puppet.AnimatorRunning + ", brazo con la linterna " + md.puppet.ArmIkActive + " (error " + md.puppet.ArmHandError.ToString("F3") + " m)");
            }
            if (seen)
            {
                float lo = cfg.layout == LayoutKind.Field ? 15f : 7f, hi = cfg.layout == LayoutKind.Field ? 35f : 22f;
                if (md.StageCount > 0 || !FieldWrap.On)
                    r.Check(md.LastStageDistance >= lo - 1.6f && md.LastStageDistance <= hi + 2.1f, tag + "[Mímico] a media distancia: " + md.LastStageDistance.ToString("F1") + " m (" + lo + "-" + hi + ")");
                else
                {
                    // v7.0.4: en el campo envuelto puede verse de lejos al otro lado del borde antes de escenificarse
                    float wd = FieldWrap.Distance(mim.transform.position, gm.player.transform.position);
                    r.Check(wd >= lo - 1.6f && wd <= 60.5f, tag + "[Mímico] visto sin escenificar al otro lado del borde, a " + wd.ToString("F1") + " m envueltos (" + (lo - 1.6f) + "-60)");
                }
                RemotePlayer pp = md.puppet;
                r.Check(pp != null && pp.gameObject.activeInHierarchy, tag + "[Mímico] la copia (jugador) es visible, con linterna");
                yield return 0.5f;
                if (lv == 1 || cfg.layout == LayoutKind.Field || lv == 3) Capture(cam, "mimic_encounter_lv" + lv + ".png");
            }
            // sigue disfrazado; acercarse lo revela y ataca
            Vector3 toward = mim.transform.position - gm.player.transform.position; toward.y = 0f;
            int rv = md.Reveals;
            gm.player.Teleport(mim.transform.position - toward.normalized * 2.9f, Quaternion.LookRotation(toward.normalized).eulerAngles.y);
            { float tr = 0f; while (tr < 0.5f && md.Disguised) { yield return null; tr += Time.deltaTime; } }
            mim.Stop();   // revelado: que no llegue a atrapar al jugador (la prueba sigue)
            r.Check(md.Reveals > rv && !md.Disguised, tag + "[Mímico] al acercarse se transforma (revelaciones " + md.Reveals + ")");
            mim.Stop();
            if (!md.Disguised && san != null) { yield return 0.3f; r.Check(san.MonsterDrain > 1f, tag + "[Cordura] el mímico revelado cerca también drena la cordura (" + san.MonsterDrain.ToString("F2") + "/s)"); }
            Place(mim, home + new Vector3(300f, 0f, 300f), 0f);
            if (gm.State != GameManager.GameState.Playing) r.Info(tag + "estado tras el mímico: " + gm.State);
        }
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        gm.player.Teleport(home, homeYaw);
        yield return 0.4f;
        foreach (Behaviour b in pks) if (b != null) b.enabled = true;
    }
}
