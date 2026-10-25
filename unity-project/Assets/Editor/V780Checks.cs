// v7.8.0 «Todoterreno del desierto»: modelo del 4x4, sonido (viento, motor, neumáticos: sin siseo y sin costura),
// huellas / rodadas y recorrido en juego (subir, conducir, luces, trepar, bajar, arenas movedizas).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class V780Checks
{
    static IEnumerator WaitFor(System.Func<bool> cond, float max)
    {
        float end = Time.time + max;
        while (Time.time < end && !cond()) yield return null;
    }

    public static void Edit(SmokeTest.Report r)
    {
        string T = "[v7.8.0] ";
        r.Check(GameVersion.Current == "7.8.0" && NetSession.Protocol == "BackroomsEscape-MP-11", T + "versión " + GameVersion.Current + ", protocolo " + NetSession.Protocol);
        // modelo
        GameObject car = Resources.Load<GameObject>("Desert/Car/DCar");
        r.Check(car != null, T + "modelo del todoterreno en Resources/Desert/Car");
        if (car != null)
        {
            string miss = ""; int tris = 0;
            foreach (string n in new[] { "DCar_Body", "DCar_Glass", "DCar_Interior", "DCar_Lamps", "DCar_Tails", "DCar_Wheel", "DCar_Steering" })
            {
                Transform t = null; foreach (Transform c in car.GetComponentsInChildren<Transform>(true)) if (c.name.StartsWith(n)) t = c;
                if (t == null) { miss += " " + n; continue; }
                MeshFilter mf = t.GetComponent<MeshFilter>(); if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3 * (n == "DCar_Wheel" ? 4 : 1);
            }
            r.Check(miss == "" && tris > 15000 && tris < 80000, T + "piezas del 4x4 (carrocería, cristales, interior, faros, pilotos, rueda, volante), " + tris + " triángulos con 4 ruedas" + (miss != "" ? " faltan:" + miss : ""));
            Texture2D pt = Resources.Load<Texture2D>("Desert/Car/DCar_paint");
            r.Check(pt != null && pt.width >= 512, T + "pintura con polvo y óxido horneada (" + (pt != null ? pt.width + " px" : "falta") + ")");
            Texture2D wt = Resources.Load<Texture2D>("Desert/Car/DCar_wheel"), it = Resources.Load<Texture2D>("Desert/Car/DCar_interior");
            r.Check(pt != null && pt.width >= 2048 && wt != null && wt.width >= 1024 && it != null && it.width >= 1024, T + "desgaste horneado en 3 atlas: carrocería " + (pt != null ? pt.width : 0) + " px, rueda " + (wt != null ? wt.width : 0) + " px, interior " + (it != null ? it.width : 0) + " px");
        }
        r.Check(Shader.Find("Backrooms/SandDecal") != null && Shader.Find("Backrooms/SandDecal").isSupported, T + "shader de huellas (Backrooms/SandDecal) soportado");
        // sonido: viento (capa base y tormenta), motor, neumáticos
        float[] w = DesertSynth.Wind(9101), ws = DesertSynth.WindStorm(9202);
        float[] e1 = DesertSynth.Engine(7301, 1000f, false), e2 = DesertSynth.Engine(7302, 2800f, true), ty = DesertSynth.Tyres(7303);
        string sp = "";
        bool hissOk = true, seamOk = true, flatOk = true;
        foreach (var kv in new[] { ("viento", w, 1500f), ("tormenta", ws, 1700f), ("motor bajo", e1, 2200f), ("motor alto", e2, 2600f), ("neumáticos", ty, 1200f) })
        {
            float hi = DesertSynth.EnergyAbove(kv.Item2, 3000f), mid = DesertSynth.EnergyAbove(kv.Item2, kv.Item3), seam = DesertSynth.SeamRatio(kv.Item2);
            int sr = DesertSynth.SR; float a = DesertSynth.Rms(kv.Item2, 0, sr), b = DesertSynth.Rms(kv.Item2, kv.Item2.Length - sr, kv.Item2.Length);
            float ratio = a / Mathf.Max(1e-6f, b);
            sp += " " + kv.Item1 + ": >3 kHz " + (hi * 100f).ToString("F3") + " %, >" + kv.Item3 + " Hz " + (mid * 100f).ToString("F2") + " %, costura " + seam.ToString("F2") + ", inicio/fin " + ratio.ToString("F2") + ";";
            if (hi > 0.01f || mid > 0.03f) hissOk = false;
            if (seam > 1f) seamOk = false;
            if (ratio < 0.4f || ratio > 2.5f) flatOk = false;
        }
        r.Info(T + "espectros:" + sp);
        r.Check(hissOk, T + "viento, motor y neumáticos sin siseo: menos del 1 % de la energía por encima de 3 kHz");
        r.Check(seamOk && flatOk, T + "bucles sin costura (el salto del empalme no supera el percentil 99,9 de los saltos normales) y sin hueco de volumen en el empalme");
        // mezcla de lo que se oye conduciendo a fondo (motor alto a 3700 rpm, neumáticos, viento con racha) y en tormenta
        {
            float[][] cl = { e1, e2, ty, w, ws };
            float[] mixA = DesertSynth.Mix(cl, new[] { 0.10f, 0.6f, 0.5f, 0.2f, 0f }, new[] { 2.4f, 1.32f, 1.2f, 1.1f, 1f }, new[] { 0f, 0f, 0f, 1400f, 1550f }, 48000, 6f);
            float[] mixB = DesertSynth.Mix(cl, new[] { 0f, 0f, 0f, 0.3f, 0.7f }, new[] { 1f, 1f, 1f, 1.15f, 1.1f }, new[] { 0f, 0f, 0f, 1750f, 1900f }, 48000, 6f);
            float ha = DesertSynth.EnergyAbove(mixA, 3000f, 48000), hb = DesertSynth.EnergyAbove(mixB, 3000f, 48000), hb6 = DesertSynth.EnergyAbove(mixB, 6000f, 48000);
            r.Check(ha < 0.02f && hb < 0.01f && hb6 < 0.002f, T + "mezcla conduciendo a fondo: " + (ha * 100f).ToString("F2") + " % por encima de 3 kHz; tormenta de arena: " + (hb * 100f).ToString("F3") + " % (>6 kHz " + (hb6 * 100f).ToString("F4") + " %): sin siseo");
        }
        // el viento sube y baja en rachas suaves, sin saltos
        float gmin = 1f, gmax = 0f, gstep = 0f;
        for (int i = 0; i < 6000; i++) { float g = DesertWind.GustAt(i * 0.05f, 7), g2 = DesertWind.GustAt((i + 1) * 0.05f, 7); gmin = Mathf.Min(gmin, g); gmax = Mathf.Max(gmax, g); gstep = Mathf.Max(gstep, Mathf.Abs(g2 - g)); }
        r.Check(gmax - gmin > 0.6f && gstep < 0.06f, T + "rachas de viento: de " + gmin.ToString("F2") + " a " + gmax.ToString("F2") + ", sin saltos (máx " + gstep.ToString("F3") + " por 50 ms)");
        // huellas: se desvanecen en minutos
        float f30 = DesertTracks.Fade01(30f), f120 = DesertTracks.Fade01(120f), fEnd = DesertTracks.Fade01(DesertTracks.Life + 1f);
        r.Check(f30 < 0.15f && f120 > 0.3f && f120 < 0.8f && fEnd >= 0.999f && DesertTracks.Life >= 120f && DesertTracks.Life <= 400f, T + "huellas y rodadas se desvanecen en " + (DesertTracks.Life / 60f).ToString("F1") + " min (30 s: " + f30.ToString("F2") + ", 2 min: " + f120.ToString("F2") + ")");
        r.Check(DesertCar.Vmax * 3.6f >= 60f && DesertCar.Vmax * 3.6f <= 80f, T + "velocidad máxima " + (DesertCar.Vmax * 3.6f).ToString("F0") + " km/h (60-80)");
    }

    // ---------------- en juego (desierto) ----------------
    public static IEnumerator CarPlay(SmokeTest.Report r, string tag, GameManager gm)
    {
        string T = tag + "[Todoterreno] ";
        DesertWorld w = DesertWorld.Active; DesertCars dc = DesertCars.Active;
        r.Check(dc != null && dc.Drivable != null && dc.wreck != null && dc.tracks != null && DesertWind.Active != null, T + "todoterreno, chatarra, huellas y viento creados");
        if (dc == null || dc.Drivable == null || w == null) yield break;
        DesertCar car = dc.Drivable; FirstPersonController p = gm.player;
        DesertFx.HazardsOn = false;
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 12f;
        yield return 0.5f;
        Vector3 home = w.levelRoot.InverseTransformPoint(car.transform.position);
        r.Check(Mathf.Abs(car.ModelLength - 5.1f) < 0.4f && car.ModelHeight > 1.9f && car.ModelHeight < 2.4f, T + "medidas reales: " + car.ModelLength.ToString("F2") + " m de largo, " + car.ModelHeight.ToString("F2") + " m de alto");
        float vill = 999f; foreach (DesertPlan.Village v in w.plan.villages) vill = Mathf.Min(vill, w.plan.Dist(v.c, new Vector2(home.x, home.z)) - v.r);
        r.Check(vill > 2f && vill < 40f && car.Grounded, T + "aparcado junto a una aldea (" + vill.ToString("F0") + " m del borde), sobre la arena con las ruedas en el suelo");
        Vector3 wl = w.levelRoot.InverseTransformPoint(dc.wreck.transform.position);
        float wh = wl.y - DesertHeight.H(Mathf.Repeat(wl.x, w.L), Mathf.Repeat(wl.z, w.L));
        r.Check(wh < -0.5f && dc.DriftCount >= 2 && dc.wreck.GetComponentsInChildren<BoxCollider>().Length >= 4, T + "chatarra medio enterrada (" + wh.ToString("F2") + " m) con dunas contra el coche, se puede pisar");
        {   // realismo: todas las piezas de carrocería, rueda e interior con su atlas de desgaste; cristal muy liso; sonda de reflejos propia
            int plain = 0, total = 0; float glassSmooth = 0f;
            foreach (MeshRenderer mr in car.GetComponentsInChildren<MeshRenderer>(true))
                foreach (Material m in mr.sharedMaterials)
                {
                    if (m == null) continue;
                    if (mr.name.StartsWith("DCar_Glass")) { glassSmooth = m.HasProperty("_Glossiness") ? m.GetFloat("_Glossiness") : 0f; continue; }
                    if (!(mr.name.StartsWith("DCar_Body") || mr.name.StartsWith("DCar_Wheel") || mr.name.StartsWith("DCar_Interior"))) continue;
                    total++; if (m.mainTexture == null) plain++;
                }
            r.Check(total >= 20 && plain == 0, T + "materiales con desgaste horneado (" + total + " ranuras, " + plain + " sin textura)");
            r.Check(glassSmooth > 0.9f && car.probe != null && car.probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime && car.probe.resolution <= 128, T + "cristales con reflejos (lisura " + glassSmooth.ToString("F2") + ", sonda en tiempo real " + (car.probe != null ? car.probe.resolution + " px" : "—") + ")");
            r.Check(DesertCars.WheelSand > 0.1f && dc.DriftCount >= 2, T + "arena acumulada contra las ruedas (" + DesertCars.WheelSand.ToString("F2") + " m)");
        }
        // se puede trepar: el techo y el capó tienen colisión arriba
        {
            Vector3 roof = car.transform.TransformPoint(new Vector3(0f, 3.5f, -0.6f)), hood = car.transform.TransformPoint(new Vector3(0f, 3.5f, 1.7f)), bump = car.transform.TransformPoint(new Vector3(0f, 3.5f, 2.47f));
            bool a = Physics.Raycast(roof, Vector3.down, out RaycastHit hr, 3f) && hr.collider.transform.IsChildOf(car.transform);
            bool b = Physics.Raycast(hood, Vector3.down, out RaycastHit hh, 3f) && hh.collider.transform.IsChildOf(car.transform);
            bool c = Physics.Raycast(bump, Vector3.down, out RaycastHit hb, 3.5f) && hb.collider.transform.IsChildOf(car.transform);
            float gy = car.transform.position.y;
            float sRoof = a ? hr.point.y - gy : 0f, sHood = b ? hh.point.y - gy : 0f, sBump = c ? hb.point.y - gy : 0f;
            r.Check(a && b && c && sBump < p.jumpHeight + 0.05f && sHood - sBump < p.jumpHeight && sRoof - sHood < p.jumpHeight, T + "se trepa por delante: parachoques " + sBump.ToString("F2") + " m → capó " + sHood.ToString("F2") + " → techo " + sRoof.ToString("F2") + " (salto " + p.jumpHeight + " m)");
            // de verdad: el jugador puesto encima del techo se queda de pie en él
            p.Teleport(car.transform.TransformPoint(new Vector3(0f, 2.3f, -0.6f)), 0f);
            yield return 1.0f;
            float onRoof = p.transform.position.y - gy;
            r.Check(onRoof > 1.9f && onRoof < 2.4f, T + "de pie sobre el techo (" + onRoof.ToString("F2") + " m)");
        }
        // subir al volante
        p.Teleport(car.transform.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f);
        yield return 0.5f;
        r.Check(dc.Prompt != null && dc.Prompt.Contains("conducir"), T + "junto a la puerta: «" + dc.Prompt + "»");
        DesertCars.DebugInteract = true; yield return null; yield return null;
        r.Check(car.SeatOf(DesertCars.LocalId) == 0 && p.IsMounted && p.mount == car.transform, T + "E: al volante");
        float eyeLocal = car.transform.InverseTransformPoint(gm.playerCamera.position).y;
        r.Check(eyeLocal > 1.3f && eyeLocal < 1.85f, T + "vista del conductor a " + eyeLocal.ToString("F2") + " m (dentro de la cabina, bajo el techo)");
        // conducir: acelera hasta el tope y no lo pasa
        int f0 = dc.tracks.TyreAdded, d0 = car.DustEmitted;
        Vector3 c0 = car.transform.position; float yaw0 = car.transform.eulerAngles.y;
        DesertCars.DebugDrive = true; DesertCars.DbgThrottle = 1f; DesertCars.DbgSteer = 0f; DesertCars.DbgHandbrake = false;
        float vmaxSeen = 0f; float t8 = -1f; float tStart = Time.time;
        for (float t = 0f; t < 14f; t += Time.deltaTime) { vmaxSeen = Mathf.Max(vmaxSeen, car.ForwardSpeed); if (t8 < 0f && car.ForwardSpeed > 13.9f) t8 = Time.time - tStart; yield return null; }
        float moved = Vector3.Distance(c0, car.transform.position);
        r.Check(vmaxSeen > 15f && vmaxSeen < DesertCar.Vmax + 1.2f && moved > 60f, T + "acelera por las dunas: " + (vmaxSeen * 3.6f).ToString("F0") + " km/h máx (tope 75), 0-50 en " + (t8 >= 0 ? t8.ToString("F1") + " s" : "—") + ", " + moved.ToString("F0") + " m");
        float roll = Vector3.Angle(car.transform.up, Vector3.up);
        r.Check(roll < 35f && car.Grounded, T + "estable en las dunas (inclinación " + roll.ToString("F0") + "°), ruedas en el suelo");
        r.Check(car.EngineVolume > 0.3f && dc.tracks.TyreAdded > f0 + 10 && car.DustEmitted > d0 + 10, T + "motor sonando (" + car.EngineVolume.ToString("F2") + "), rodadas (" + (dc.tracks.TyreAdded - f0) + ") y polvo de las ruedas (" + (car.DustEmitted - d0) + ")");
        r.Check(car.DustAlive >= 25, T + "nube de polvo visible detrás del coche (" + car.DustAlive + " partículas vivas)");
        // girar
        DesertCars.DbgThrottle = 0.6f; DesertCars.DbgSteer = 1f;
        float y1 = car.transform.eulerAngles.y;
        yield return 3.0f;
        float turned = Mathf.Abs(Mathf.DeltaAngle(y1, car.transform.eulerAngles.y));
        float viewYawDiff = Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.y, car.transform.eulerAngles.y));
        r.Check(turned > 30f && viewYawDiff < 10f, T + "A/D gira (" + turned.ToString("F0") + "° en 3 s) y la vista gira con el coche (" + viewYawDiff.ToString("F1") + "°)");
        // freno de mano
        DesertCars.DbgThrottle = 0f; DesertCars.DbgSteer = 0f; DesertCars.DbgHandbrake = true;
        float sp0 = car.Speed;
        { IEnumerator wf = WaitFor(() => car.Speed < 0.4f, 10f); while (wf.MoveNext()) yield return wf.Current; }
        r.Check(car.Speed < 0.4f, T + "freno de mano: de " + (sp0 * 3.6f).ToString("F0") + " km/h a parado");
        // luces
        DesertSky.ForceHour = 23f; DesertCars.DbgLights = true;
        yield return 0.4f;
        Light[] ls = car.GetComponentsInChildren<Light>();
        int on = 0; foreach (Light l in ls) if (l.enabled && l.type == LightType.Spot) on++;
        r.Check(car.lights && on == 2, T + "L: faros encendidos (" + on + " focos)");
        DesertCars.DbgLights = false; yield return 0.3f;
        on = 0; foreach (Light l in ls) if (l.enabled) on++;
        r.Check(!car.lights && on == 0, T + "L otra vez: faros apagados");
        DesertSky.ForceHour = 12f;
        // viento apagado dentro del coche
        yield return 1.0f;
        DesertWind wind = DesertWind.Active;
        r.Check(wind != null && wind.Muffle > 0.4f && wind.Cutoff < 900f, T + "dentro del todoterreno el viento suena apagado (filtro " + (wind != null ? wind.Cutoff.ToString("F0") : "-") + " Hz)");
        // bajar: junto a la puerta, fuera de la carrocería
        DesertCars.DebugDrive = false;
        DesertCars.DebugInteract = true; yield return null; yield return null; yield return 0.5f;
        Vector3 off = car.transform.InverseTransformPoint(p.transform.position);
        r.Check(!p.IsMounted && car.Occupants == 0 && (Mathf.Abs(off.x) > 1.0f || Mathf.Abs(off.z) > 2.6f) && Mathf.Abs(off.z) < 4f, T + "E: bajar junto a la puerta (" + off.x.ToString("F1") + ", " + off.z.ToString("F1") + ")");
        r.Check(Mathf.Abs(Mathf.DeltaAngle(p.transform.eulerAngles.x, 0f)) < 1f, T + "el jugador queda derecho al bajar");
        // huellas al andar
        int fa = dc.tracks.FootAdded;
        p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
        p.transform.rotation = Quaternion.Euler(0f, car.transform.eulerAngles.y + 90f * Mathf.Sign(off.x), 0f);
        yield return 3.0f;
        p.debugMove = Vector2.zero; p.debugInput = false;
        r.Check(dc.tracks.FootAdded > fa + 3, T + "huellas al andar por la arena (" + (dc.tracks.FootAdded - fa) + ")");
        float a0 = dc.tracks.MeanAlphaFoot();
        dc.tracks.AgeAll(DesertTracks.Life * 0.6f); float a1 = dc.tracks.MeanAlphaFoot();
        dc.tracks.AgeAll(DesertTracks.Life); float a2 = dc.tracks.MeanAlphaFoot();
        r.Check(a0 > 0.5f && a1 < a0 * 0.75f && a2 < 0.01f && dc.tracks.TyreAlive == 0, T + "las huellas se desvanecen con el tiempo (" + a0.ToString("F2") + " → " + a1.ToString("F2") + " → " + a2.ToString("F2") + ")");
        // arenas movedizas: con alguien dentro, el coche se hunde, los ocupantes salen y vuelve a su aldea
        if (w.plan.quick.Count > 0)
        {
            p.Teleport(car.transform.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.4f;
            DesertCars.DebugInteract = true; yield return null; yield return null;
            Vector2 q = w.plan.quick[0].c;
            Vector3 at = w.levelRoot.TransformPoint(new Vector3(q.x, DesertHeight.H(q.x, q.y) + 0.3f, q.y));
            car.rb.position = at; car.transform.position = at; car.rb.velocity = Vector3.zero;
            float old = DesertCar.SinkRespawnSeconds; DesertCar.SinkRespawnSeconds = 2f;
            int rs = DesertCars.Respawns;
            { IEnumerator wf = WaitFor(() => car.sinking, 3f); while (wf.MoveNext()) yield return wf.Current; }
            bool sank = car.sinking;
            { IEnumerator wf = WaitFor(() => car.Occupants == 0, 6f); while (wf.MoveNext()) yield return wf.Current; }
            yield return 0.2f;
            bool ejected = car.Occupants == 0 && !p.IsMounted;
            { IEnumerator wf = WaitFor(() => DesertCars.Respawns > rs, 15f); while (wf.MoveNext()) yield return wf.Current; }
            DesertCar.SinkRespawnSeconds = old;
            Vector3 back = w.levelRoot.InverseTransformPoint(car.transform.position);
            float dh = new Vector2(DesertHeight.Wrap(back.x - home.x), DesertHeight.Wrap(back.z - home.z)).magnitude;
            r.Check(sank && ejected && DesertCars.Respawns > rs && dh < 1f && !car.hidden, T + "arenas movedizas: el coche se hunde (" + sank + "), el conductor sale (" + ejected + ", ocupantes " + car.Occupants + ", montado " + p.IsMounted + ") y el coche vuelve a su sitio (" + (DesertCars.Respawns > rs) + ", " + dh.ToString("F1") + " m, oculto " + car.hidden + ")");
            DesertFx.HazardsOn = false;
            { IEnumerator wf = WaitFor(() => DesertFx.Active == null || !DesertFx.Active.Busy, 10f); while (wf.MoveNext()) yield return wf.Current; }
        }
        // borde del desierto: el coche da la vuelta él solo con el conductor dentro
        {
            float x = w.L - 3f, z = 40f;
            p.Teleport(car.transform.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.4f;
            DesertCars.DebugInteract = true; yield return null; yield return null;
            Vector3 at = w.levelRoot.TransformPoint(new Vector3(x, DesertHeight.H(x, z) + 0.6f, z));
            car.rb.position = at; car.transform.position = at; car.rb.rotation = w.levelRoot.rotation * Quaternion.Euler(0f, 90f, 0f); car.rb.velocity = Vector3.zero; car.rb.angularVelocity = Vector3.zero;
            int wr = car.Wraps;
            DesertCars.DebugDrive = true; DesertCars.DbgThrottle = 1f; DesertCars.DbgHandbrake = false; DesertCars.DbgSteer = 0f;
            { IEnumerator wf = WaitFor(() => car.Wraps > wr, 8f); while (wf.MoveNext()) yield return wf.Current; }
            yield return 0.3f;
            Vector3 lc = w.levelRoot.InverseTransformPoint(car.transform.position);
            float seatD = Vector3.Distance(p.transform.position, car.transform.TransformPoint(car.SeatLocal(0)));
            r.Check(car.Wraps > wr && lc.x < 30f && p.IsMounted && seatD < 0.3f, T + "desierto sin fin en coche: cruza el borde y sigue (x " + lc.x.ToString("F0") + "), conductor en su asiento (" + seatD.ToString("F2") + " m)");
            DesertCars.DbgThrottle = 0f; DesertCars.DbgHandbrake = true;
            { IEnumerator wf = WaitFor(() => car.Speed < 0.5f, 8f); while (wf.MoveNext()) yield return wf.Current; }
            DesertCars.DebugDrive = false;
            DesertCars.DebugInteract = true; yield return null; yield return null; yield return 0.3f;
            r.Check(!p.IsMounted, T + "bajar tras la vuelta del desierto");
            DesertCars.HostRespawn(car, true);
        }
        DesertSky.ForceWeather = -1; DesertSky.ForceHour = -1f;
    }
}
