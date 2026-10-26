// v7.9.0 «Nivel 0»: ruidos aleatorios (sin crujidos ni siseo), apagones rojos de 10–30 s iguales para todos (semilla + reloj
// del anfitrión), el nivel sigue oscuro, y latido por amenaza en los niveles con monstruos (no en el Nivel 114).
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class V790Checks
{
    static IEnumerator WaitFor(System.Func<bool> cond, float max)
    {
        float end = Time.time + max;
        while (Time.time < end && !cond()) yield return null;
    }

    // ---- v7.9.0 (t428u–t432u): huellas con relieve, lluvia por capas, vida, empuje del viento
    static float SeamJump(float[] d)
    {   // salto en el empalme del bucle comparado con el paso típico de la señal
        double s = 0; for (int i = 1; i < d.Length; i++) s += Mathf.Abs(d[i] - d[i - 1]);
        float mean = (float)(s / (d.Length - 1)); return Mathf.Abs(d[0] - d[d.Length - 1]) / Mathf.Max(1e-6f, mean);
    }
    static void EditMore(SmokeTest.Report r, string T)
    {
        // huella: talón más hondo que la punta, reborde de arena por encima del suelo, fuera plano
        float heel = DesertTracks.FootHeight(0f, 0.16f), toe = DesertTracks.FootHeight(0.05f, 0.80f), flat = DesertTracks.FootHeight(0.98f, 0.5f);
        float rim = 0f; for (int k = 0; k < 64; k++) { float u = -1f + k / 31.5f; for (float v = 0.02f; v < 1f; v += 0.02f) rim = Mathf.Max(rim, DesertTracks.FootHeight(u, v)); }
        r.Check(heel < toe - 0.05f && toe < 0.4f && rim > 0.56f && Mathf.Abs(flat - 0.5f) < 0.03f, T + "[Huellas] suela con relieve: talón " + heel.ToString("F2") + " más hondo que la punta " + toe.ToString("F2") + ", reborde " + rim.ToString("F2") + " (0,5 = arena)");
        r.Check(DesertTracks.StrideFor(2) > DesertTracks.StrideFor(1) && DesertTracks.StrideFor(0) < DesertTracks.StrideFor(1) && DesertTracks.FootLife <= 12f, T + "[Huellas] zancada " + DesertTracks.StrideFor(0) + " / " + DesertTracks.StrideFor(1) + " / " + DesertTracks.StrideFor(2) + " m (agachado / andando / corriendo); se borran en " + DesertTracks.FootLife + " s");
        Shader sd = Resources.Load<Shader>("Desert/Shaders/SandDecal");
        r.Check(sd != null && sd.isSupported, T + "[Huellas] sombreado de huellas con relieve y erosión (SandDecal)");
        // lluvia: capas sin siseo y bucles sin costura
        DesertRain.Build();
        string info = ""; bool ok = true;
        float[][] layers = { DesertRain.BedData, DesertRain.NearData, DesertRain.RoofData, DesertRain.MetalData };
        string[] names = { "lecho", "gotas", "tejado", "chapa" };
        for (int i = 0; i < layers.Length; i++)
        {
            float hi = HighRatio(layers[i], DesertSynth.SR, 5000f), seam = SeamJump(layers[i]);
            info += " " + names[i] + " " + (hi * 100f).ToString("F2") + " %/" + seam.ToString("F1");
            if (hi > 0.02f || seam > 6f || layers[i].Length < DesertSynth.SR * 6) ok = false;
        }
        bool lens = layers[0].Length != layers[1].Length && layers[1].Length != layers[2].Length && layers[2].Length != layers[3].Length;
        r.Check(ok && lens, T + "[Lluvia] 4 capas sin siseo (energía >5 kHz) y sin costura de bucle (salto/paso medio):" + info);
        bool thOk = DesertRain.ThunderData.Length == 3; string ti = "";
        foreach (float[] th in DesertRain.ThunderData) { float hi = HighRatio(th, DesertSynth.SR, 400f); ti += " " + (hi * 100f).ToString("F2") + " %"; if (hi > 0.01f) thOk = false; }
        r.Check(thOk, T + "[Lluvia] truenos lejanos: solo retumbo grave (energía >400 Hz:" + ti + ")");
        // vida
        r.Check(100f / Health.PainRainRate > 180f && 100f / Health.PainRainRate < 600f && Health.AlmondRainRate < Health.PainRainRate && Health.Regen > 0f && Health.ReviveHealth >= 25f, T + "[Vida] la lluvia de dolor quita la vida en " + (100f / Health.PainRainRate / 60f).ToString("F1") + " min a la intemperie; la de almendras en " + (100f / Health.AlmondRainRate / 60f).ToString("F0") + " min; se recupera " + Health.Regen + "/s a salvo");
        r.Check(OceanNet.KillText(OceanNet.KillHealth) == Health.DeathText, T + "[Vida] a 0: «" + Health.DeathText + "» (derribado en multijugador)");
        r.Check(DesertFx.MaxPush > 0.8f && DesertFx.MaxPush < 2.5f, T + "[Tormenta] el viento empuja como mucho " + DesertFx.MaxPush + " m/s (andar 3,5, correr 6,5: nunca atrapa)");
    }

    // Nivel 114: huellas, lluvia, vida y empuje del viento (jugando)
    public static IEnumerator Desert790(SmokeTest.Report r, string tag, GameManager gm)
    {
        string T = tag + "[v7.9.0] ";
        DesertWorld w = DesertWorld.Active; DesertCars dc = DesertCars.Active; DesertSky sky = DesertSky.Active; DesertFx fx = DesertFx.Active;
        FirstPersonController p = gm.player; Health hp = Health.Instance; DesertRain rain = DesertRain.Active;
        r.Check(w != null && dc != null && sky != null && fx != null && hp != null && rain != null && gm.healthFill != null, T + "desierto con huellas, lluvia por capas y VIDA en el HUD");
        if (w == null || dc == null || sky == null || fx == null || hp == null || rain == null) yield break;
        DesertTracks tr = dc.tracks;
        // sitio despejado en llano
        Vector3 spot = w.levelRoot.InverseTransformPoint(p.transform.position);
        for (int k = 0; k < 60; k++)
        {
            float ang = k * 2.4f, dist = 20f + k * 6f;
            Vector3 q = spot + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
            float x = Mathf.Repeat(q.x, w.L), z = Mathf.Repeat(q.z, w.L);
            if (w.plan.QuickAt(new Vector2(x, z), 12f) >= 0 || DesertHeight.Normal(x, z).y < 0.97f || w.UnderRoof(w.levelRoot.TransformPoint(q)) || fx.InCave(w.levelRoot.TransformPoint(q))) continue;
            bool clear = true;
            foreach (Collider c in Physics.OverlapSphere(w.levelRoot.TransformPoint(new Vector3(q.x, DesertHeight.H(x, z) + 1f, q.z)), 14f, ~0, QueryTriggerInteraction.Ignore))
                if (!(c.name.StartsWith("Dune_") || c is CharacterController || (dc.Drivable != null && c.transform.IsChildOf(dc.Drivable.transform)))) { clear = false; break; }
            if (clear) { spot = new Vector3(q.x, DesertHeight.H(x, z), q.z); break; }
        }
        System.Func<Vector3> local = () => w.levelRoot.InverseTransformPoint(p.transform.position);
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 12f; Health.DisableForTests = false;
        p.Teleport(w.levelRoot.TransformPoint(spot + Vector3.up * 0.3f), 0f); yield return 1.0f;
        // ---- huellas: andando, corriendo y agachado; polvo; erosión
        int f0 = tr.FootAdded, pf0 = tr.PuffEmitted;
        p.debugInput = true; p.debugMove = new Vector2(0f, 1f); p.debugSprint = false; p.debugCrouch = false;
        yield return 2.5f;
        float sWalk = tr.LastFootScale, dWalk = tr.LastFootDepth; int nWalk = tr.FootAdded - f0;
        p.SetStaminaForTests(p.maxStamina); Vector3 r0 = local();
        p.debugSprint = true; int f1 = tr.FootAdded; yield return 2.0f;
        float sRun = tr.LastFootScale, dRun = tr.LastFootDepth; int nRun = tr.FootAdded - f1;
        float runM = new Vector2(DesertHeight.Wrap(local().x - r0.x), DesertHeight.Wrap(local().z - r0.z)).magnitude;
        p.debugSprint = false; p.debugCrouch = true; int f2 = tr.FootAdded; yield return 2.5f;
        float sCr = tr.LastFootScale, dCr = tr.LastFootDepth; int nCr = tr.FootAdded - f2;
        p.debugCrouch = false; p.debugMove = Vector2.zero; yield return 0.5f;
        r.Info(T + "[Huellas] corriendo 2 s: " + runM.ToString("F1") + " m");
        r.Check(nWalk >= 3 && nRun >= 3 && nCr >= 1 && sRun > sWalk && dRun > dWalk && sCr < sWalk && dCr < dWalk, T + "[Huellas] corriendo más grandes y hondas (" + sRun.ToString("F2") + "×, " + dRun.ToString("F2") + "), agachado más pequeñas (" + sCr.ToString("F2") + "×, " + dCr.ToString("F2") + "); " + nWalk + "/" + nRun + "/" + nCr + " pisadas");
        int alive = tr.PuffSystem != null ? tr.PuffSystem.particleCount : -1;
        r.Check(tr.PuffEmitted > pf0 && alive >= 0 && alive <= DesertTracks.PuffMax, T + "[Huellas] polvo al pisar (" + (tr.PuffEmitted - pf0) + " granos, " + alive + " vivos, tope " + DesertTracks.PuffMax + ")");
        float e0 = tr.MeanErosionFoot();
        tr.AgeAll(DesertTracks.FootLife * 0.5f); yield return 0.3f;
        float e1 = tr.MeanErosionFoot(), a1 = tr.MeanAlphaFoot();
        r.Check(e1 > e0 + 0.3f && a1 > 0.05f, T + "[Huellas] el viento ablanda los bordes antes de borrarlas (erosión " + e0.ToString("F2") + " → " + e1.ToString("F2") + ", aún visibles " + a1.ToString("F2") + ")");
        tr.AgeAll(DesertTracks.FootLife * 2f);
        // ---- lluvia: capas, vida, truenos
        hp.value = 100f;
        DesertSky.ForceWeather = (int)DesertSky.Weather.PainRain; yield return 1.0f;
        float h0 = hp.value; yield return 4.0f;
        float lost = h0 - hp.value;
        r.Check(sky.Rain > 0.9f && lost > Health.PainRainRate * 4f * 0.6f && lost < Health.PainRainRate * 4f * 1.6f && hp.LastCause == "lluvia", T + "[Vida] la lluvia de dolor quita vida poco a poco a la intemperie (" + lost.ToString("F2") + " en 4 s)");
        yield return 2.5f;
        r.Check(rain.Where == "fuera" && rain.BedVolume > 0.2f && rain.NearVolume > 0.12f && rain.RoofVolume < 0.01f && rain.Cutoff > 3000f && rain.bed.isPlaying && rain.near.isPlaying, T + "[Lluvia] fuera: lecho " + rain.BedVolume.ToString("F2") + " + gotas " + rain.NearVolume.ToString("F2") + ", filtro " + rain.Cutoff.ToString("F0") + " Hz");
        int th0 = rain.Thunders; DesertRain.ForceThunderIn = 0f; yield return 0.3f;
        float thd = rain.thunder != null ? rain.thunder.transform.localPosition.magnitude : 0f;
        r.Check(rain.Thunders == th0 + 1 && rain.thunder.isPlaying && thd > 300f && rain.thunder.volume < AudioMix.A(0.3f), T + "[Lluvia] trueno lejano (" + thd.ToString("F0") + " m) y flojo (" + rain.thunder.volume.ToString("F2") + ")");
        float wv0 = DesertWind.Active != null ? DesertWind.Active.BaseVolume : 0f;
        r.Check(wv0 > 0f, T + "[Lluvia] mezclada con el viento (" + wv0.ToString("F3") + ")");
        // a cubierto en una casa: tejado, sin gotas cerca, apagado; sin daño
        Vector3 houseIn = Vector3.zero; bool hasHouse = false;
        foreach (DesertWorld.HouseBox hb in w.HouseBoxes) if (hb.roofed) { houseIn = new Vector3(hb.c.x, 0f, hb.c.y); hasHouse = true; break; }
        if (hasHouse)
        {
            float hx = Mathf.Repeat(houseIn.x, w.L), hz = Mathf.Repeat(houseIn.z, w.L);
            p.Teleport(w.levelRoot.TransformPoint(new Vector3(houseIn.x, DesertHeight.H(hx, hz) + 0.6f, houseIn.z)), 0f);
            yield return 3.5f;
            float hIn = hp.value; yield return 2.0f;
            r.Check(rain.Where == "casa" && rain.RoofVolume > 0.15f && rain.NearVolume < 0.03f && rain.Cutoff < 900f && hp.DrainRate == 0f && Mathf.Abs(hp.value - hIn) < 0.05f + Health.Regen * 2f, T + "[Lluvia] en casa: golpeteo en el tejado " + rain.RoofVolume.ToString("F2") + ", fuera apagado (" + rain.Cutoff.ToString("F0") + " Hz), sin perder vida");
            // a salvo se recupera
            hp.value = 50f; hp.Damage(0.01f, "prueba"); yield return Health.RegenDelay + 2.0f;
            r.Check(hp.value > 50.5f && hp.RegenRate > 0f, T + "[Vida] a salvo se recupera despacio (" + hp.value.ToString("F1") + ")");
        }
        else r.Info(T + "sin casa con tejado en esta semilla");
        // en el coche: gotas en la chapa
        DesertCar car = dc.Drivable;
        if (car != null)
        {
            p.Teleport(car.transform.TransformPoint(car.DoorLocal(0)) + Vector3.up * 0.1f, 0f); yield return 0.5f;
            DesertCars.DebugInteract = true; yield return null; yield return null; yield return 2.5f;
            r.Check(p.IsMounted && rain.Where == "coche" && rain.metal != null && rain.metal.isPlaying && rain.MetalVolume > 0.3f && rain.metal.spatialBlend > 0.9f && rain.Cutoff < 1000f && hp.DrainRate == 0f, T + "[Lluvia] en el coche: gotas en la chapa del techo (" + rain.MetalVolume.ToString("F2") + ", 3D), fuera apagado, sin perder vida");
            DesertCars.DebugInteract = true; yield return null; yield return null; yield return 0.5f;
        }
        // ---- tormenta de arena: empuja (no frena), sin daño; contra el viento se avanza
        DesertSky.ForceWeather = (int)DesertSky.Weather.Sandstorm; hp.value = 80f;
        p.Teleport(w.levelRoot.TransformPoint(spot + Vector3.up * 0.3f), 0f); yield return 2.5f;
        Vector3 a0 = local(); yield return 2.0f; Vector3 a1v = local() - a0; a1v.y = 0f;
        Vector3 pd = w.levelRoot.InverseTransformDirection(fx.PushDir); pd.y = 0f; pd.Normalize();
        float along = Vector3.Dot(a1v, pd), drift = a1v.magnitude;
        r.Check(sky.Storm > 0.9f && fx.Push > 0.3f && along > 0.5f && drift < DesertFx.MaxPush * 2f + 0.3f && hp.value >= 80f - 0.01f && p.walkSpeed > 3f, T + "[Tormenta] el viento empuja al jugador quieto " + along.ToString("F1") + " m en 2 s (máx. " + (DesertFx.MaxPush * 2f).ToString("F1") + "), sin quitar vida ni frenar (andar " + p.walkSpeed.ToString("F1") + " m/s)");
        float yawAgainst = Mathf.Atan2(-fx.PushDir.x, -fx.PushDir.z) * Mathf.Rad2Deg;
        p.Teleport(p.transform.position, yawAgainst); yield return 0.2f;
        Vector3 b0 = local(); p.debugInput = true; p.debugMove = new Vector2(0f, 1f); p.debugSprint = true; yield return 2.0f;
        Vector3 b1 = local() - b0; b1.y = 0f; p.debugMove = Vector2.zero; p.debugSprint = false;
        float against = -Vector3.Dot(b1, pd);
        r.Check(against > 4f, T + "[Tormenta] corriendo contra el viento cuesta pero se avanza (" + against.ToString("F1") + " m en 2 s)");
        if (hasHouse)
        {
            float hx = Mathf.Repeat(houseIn.x, w.L), hz = Mathf.Repeat(houseIn.z, w.L);
            p.Teleport(w.levelRoot.TransformPoint(new Vector3(houseIn.x, DesertHeight.H(hx, hz) + 0.6f, houseIn.z)), 0f); yield return 1.5f;
            r.Check(fx.Push == 0f && p.ExternalPush.magnitude < 0.05f, T + "[Tormenta] dentro de casa no empuja");
        }
        // ---- caída grande: quita vida (pequeña no)
        hp.value = 100f; p.debugInput = false;
        p.Teleport(w.levelRoot.TransformPoint(spot + Vector3.up * 3.0f), 0f); yield return 1.5f;
        float small = 100f - hp.value;
        p.Teleport(w.levelRoot.TransformPoint(spot + Vector3.up * 16f), 0f); yield return 2.5f;
        float big = 100f - small - hp.value;
        r.Check(small < 0.01f && big > 5f && big < 60f && hp.value > 0f, T + "[Vida] una caída de 3 m no hace daño; una de 16 m quita " + big.ToString("F0") + " (no mata)");
        hp.value = 100f;
        DesertSky.ForceWeather = 0; DesertSky.ForceHour = 12f; yield return 1.0f;
    }

    // v7.9.0: claxon, cámaras, manos en el volante y objetos guardados (dentro del todoterreno, parado, al volante)
    public static IEnumerator CarExtras(SmokeTest.Report r, string T, DesertCar car, FirstPersonController p, GameManager gm)
    {
        // objetos guardados
        Flashlight fl = p.GetComponentInChildren<Flashlight>();
        PlayerBody pb = Object.FindObjectOfType<PlayerBody>();
        bool bodyOn = pb != null && pb.Loaded && pb.Visible;
        r.Check(fl != null && !fl.IsOn && (!bodyOn || !pb.PropShown), T + "al subir la linterna se apaga y se guarda (encendida " + (fl != null && fl.IsOn) + ", en la mano " + (bodyOn && pb.PropShown) + ")");
        // manos en el volante, siguiendo el giro
        DesertCars.DbgSteer = 0f; yield return 0.6f;
        Vector3 gl0, gr0; bool hasG = car.Grips(out gl0, out gr0);
        r.Check(hasG && Vector3.Distance(gl0, gr0) > 0.18f && Vector3.Distance(gl0, gr0) < 0.5f, T + "agarres del volante a las 10 y a las 2 (" + Vector3.Distance(gl0, gr0).ToString("F2") + " m entre manos)");
        if (bodyOn)
        {
            r.Check(pb.OnWheel && pb.WheelErrL < 0.06f && pb.WheelErrR < 0.06f, T + "primera persona: las dos manos en el volante (error " + pb.WheelErrL.ToString("F3") + " / " + pb.WheelErrR.ToString("F3") + " m, se inclina " + pb.WheelLean.ToString("F2") + " m)");
            DesertCars.DbgSteer = 1f; yield return 0.8f;
            Vector3 gl1, gr1; car.Grips(out gl1, out gr1);
            float mv = Vector3.Distance(gr0, gr1);
            r.Check(mv > 0.04f && pb.OnWheel && pb.WheelErrL < 0.06f && pb.WheelErrR < 0.06f, T + "al girar el volante las manos lo siguen (agarre movido " + mv.ToString("F2") + " m, error " + pb.WheelErrL.ToString("F3") + " / " + pb.WheelErrR.ToString("F3") + " m)");
            DesertCars.DbgSteer = 0f; yield return 0.5f;
        }
        else r.Info(T + "cuerpo en primera persona no cargado (sin gráficos): manos comprobadas en MP / capturas");
        // la tecla de la linterna no la saca dentro del coche
        int tc = fl != null ? fl.ToggleCount : 0;
        r.Check(fl != null && !fl.IsOn && fl.ToggleCount == tc, T + "dentro del coche no se usan objetos (linterna guardada)");
        // claxon
        int he = car.HornEvents;
        DesertCars.DbgHorn = true; yield return 0.5f;
        bool honk = car.HornOn && car.HornLevel > 0.5f;
        DesertCars.DbgHorn = false; yield return 0.6f;
        AudioSource hs = null; foreach (AudioSource a in car.GetComponentsInChildren<AudioSource>()) if (a.clip == DesertCar.HornClip) hs = a;
        float[] hd = DesertCar.HornData(); float hi = HighRatio(hd, 22050, 4000f);
        r.Check(honk && car.HornEvents == he + 1 && !car.HornOn && car.HornLevel < 0.05f && hs != null && hs.spatialBlend > 0.9f && hi < 0.02f, T + "H: claxon 3D (suena " + honk + ", sucesos " + (car.HornEvents - he) + ", 3D " + (hs != null ? hs.spatialBlend.ToString("F1") : "-") + ", energía >4 kHz " + (hi * 100f).ToString("F2") + " %)");
        // cámaras: asiento → por detrás → capó → asiento
        Transform cam = gm.playerCamera;
        Vector3 seatCam = p.transform.InverseTransformPoint(cam.position);   // la vista del asiento va con el jugador (derecho aunque el coche esté en cuesta)
        DesertCars.DbgCamCycle = true; yield return 0.3f;
        { IEnumerator wf = WaitFor(() => car.transform.InverseTransformPoint(cam.position).z < -3.5f, 4f); while (wf.MoveNext()) yield return wf.Current; }
        yield return 0.3f;
        Vector3 chase = car.transform.InverseTransformPoint(cam.position);
        r.Check(DesertCars.CamView == 1 && DesertCarCam.AppliedView == 1 && chase.z < -3.5f && chase.y > 1.5f && DesertCarCam.LastClearance >= DesertCarCam.MinAboveSand - 0.01f, T + "C: cámara por detrás (" + chase.z.ToString("F1") + " m detrás, " + chase.y.ToString("F1") + " m alto, " + DesertCarCam.LastClearance.ToString("F1") + " m sobre la arena)");
        DesertCars.DbgCamCycle = true; yield return 0.3f;
        { Vector3 lastH = Vector3.one * 99f; IEnumerator wf = WaitFor(() => { Vector3 h = car.transform.InverseTransformPoint(cam.position); bool st = (h - lastH).magnitude < 0.01f && h.z > 0.9f; lastH = h; return st; }, 4f); while (wf.MoveNext()) yield return wf.Current; }   // la cámara se desliza: esperar a que llegue (con fotogramas lentos tarda más)
        Vector3 hood = car.transform.InverseTransformPoint(cam.position);
        r.Check(DesertCars.CamView == 2 && DesertCarCam.AppliedView == 2 && hood.z > 0.9f && hood.y > 1.4f, T + "C otra vez: cámara sobre el capó (" + hood.z.ToString("F1") + " m)");
        DesertCars.DbgCamCycle = true; yield return 0.3f;
        { IEnumerator wf = WaitFor(() => Vector3.Distance(p.transform.InverseTransformPoint(cam.position), seatCam) < 0.15f, 4f); while (wf.MoveNext()) yield return wf.Current; }
        Vector3 back = p.transform.InverseTransformPoint(cam.position);
        r.Check(DesertCars.CamView == 0 && DesertCarCam.AppliedView == 0 && Vector3.Distance(back, seatCam) < 0.15f, T + "C otra vez: de vuelta al asiento (" + Vector3.Distance(back, seatCam).ToString("F2") + " m; asiento " + seatCam.ToString("F2") + ", ahora " + back.ToString("F2") + ", capó " + hood.ToString("F2") + ", coche a " + car.Speed.ToString("F2") + " m/s)");
    }

    static float Lum(Color c) { return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; }

    // fracción de energía por encima de f0 (paso-alto biquad de 2.º orden)
    public static float HighRatio(float[] d, int sr, float f0)
    {
        float w0 = 2f * Mathf.PI * f0 / sr, cs = Mathf.Cos(w0), al = Mathf.Sin(w0) / (2f * 0.7071f);
        float b0 = (1f + cs) / 2f, b1 = -(1f + cs), b2 = (1f + cs) / 2f, a0 = 1f + al, a1 = -2f * cs, a2 = 1f - al;
        double eh = 0, et = 0; float x1 = 0, x2 = 0, y1 = 0, y2 = 0;
        for (int i = 0; i < d.Length; i++)
        {
            float x = d[i], y = (b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2) / a0;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            eh += y * y; et += x * x;
        }
        return et > 0 ? (float)(eh / et) : 0f;
    }

    public static void Edit(SmokeTest.Report r)
    {
        string T = "[v7.9.0] ";
        EditMore(r, T);
        r.Check(GameVersion.Current == "7.9.1" && NetSession.Protocol == "BackroomsEscape-MP-12", T + "versión " + GameVersion.Current + ", protocolo " + NetSession.Protocol);
        // ruidos
        string bad = ""; float worst = 0f;
        for (int k = 0; k < Level0Events.NoiseNames.Length; k++)
        {
            AudioClip c = Level0Events.CreateNoise(k);
            float[] d = new float[c.samples]; c.GetData(d, 0);
            float hr = HighRatio(d, c.frequency, 3000f), pk = 0f, tail = 0f;
            for (int i = 0; i < d.Length; i++) pk = Mathf.Max(pk, Mathf.Abs(d[i]));
            for (int i = d.Length - 200; i < d.Length; i++) tail = Mathf.Max(tail, Mathf.Abs(d[i]));
            worst = Mathf.Max(worst, hr);
            if (c.name != Level0Events.NoiseNames[k] || c.length < 1f || c.length > 5f || pk < 0.5f || pk > 0.95f || hr > 0.01f || tail > 0.02f) bad += " " + c.name + "(" + (hr * 100f).ToString("F2") + " %)";
            Object.DestroyImmediate(c);
        }
        r.Check(bad == "", T + "[Ruidos] 4 ruidos (portazo lejano, pasos, golpes en la pared, golpe metálico): graves, sin siseo (máx. " + (worst * 100f).ToString("F2") + " % por encima de 3 kHz), sin chasquido final" + bad);
        foreach (string n in Level0Events.NoiseNames) r.Check(!n.ToLower().Contains("cruj") && !n.ToLower().Contains("creak") && !n.ToLower().Contains("hiss"), T + "[Ruidos] " + n + ": no es un crujido ni un siseo");
        // calendario del rojo
        bool okDur = true, okGap = true, det = true, differ = false; string first = "";
        foreach (int seed in new[] { 1, 7, 12345, 144139097 })
        {
            var a = new List<Level0Events.RedEvent>(); var b = new List<Level0Events.RedEvent>();
            Level0Events.BuildSchedule(seed, a); Level0Events.BuildSchedule(seed, b);
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].dur < Level0Events.RedMin - 1e-3f || a[i].dur > Level0Events.RedMax + 1e-3f) okDur = false;
                if (i > 0 && a[i].start - a[i - 1].End < Level0Events.GapMin - 1e-3f) okGap = false;
                if (a[i].start != b[i].start || a[i].dur != b[i].dur) det = false;
            }
            if (a[0].start < Level0Events.FirstMin || a[0].start > Level0Events.FirstMax) okGap = false;
            first += " " + a[0].start.ToString("F0") + "+" + a[0].dur.ToString("F0") + "s";
            var o = new List<Level0Events.RedEvent>(); Level0Events.BuildSchedule(seed + 1, o);
            if (o[0].start != a[0].start) differ = true;
        }
        r.Check(okDur, T + "[Rojo] cada apagón rojo dura 10–30 s");
        r.Check(okGap, T + "[Rojo] el primero llega a los 50–100 s y entre uno y otro pasan 75–160 s (" + first.Trim() + ")");
        r.Check(det && differ, T + "[Rojo] el calendario sale de la semilla: igual en todos los equipos, distinto en cada partida");
        {   // v7.9.1: alarma roja: toda la sala roja, pulso lento y zumbido grave sin siseo ni costura
            float[] al = Level0Events.AlarmData(); float hiA = HighRatio(al, 22050, 2000f), seamA = SeamJump(al);
            float pMin = 9f, pMax = -9f; for (float c = 0f; c < 4f; c += 0.05f) { float pv = Level0Events.PulseAt(c); pMin = Mathf.Min(pMin, pv); pMax = Mathf.Max(pMax, pv); }
            Color ra = Level0Events.RedAmbient, rf = Level0Events.RedFog;
            r.Check(ra.r > 0.25f && ra.r > 8f * ra.g && ra.r > 8f * ra.b && rf.r > 8f * rf.g && Level0Events.RedIntensity >= 1f, T + "[Rojo] alarma: ambiente y niebla rojos intensos (ambiente " + ra + "), luces ×" + Level0Events.RedIntensity);
            r.Check(pMin >= Level0Events.PulseMin - 0.01f && pMin < 0.75f && pMax > 0.98f && Mathf.Abs(Level0Events.PulseAt(1.3f) - Level0Events.PulseAt(1.3f + Level0Events.PulsePeriod)) < 1e-4f, T + "[Rojo] la luz late despacio (" + pMin.ToString("F2") + "–" + pMax.ToString("F2") + " cada " + Level0Events.PulsePeriod.ToString("F0") + " s, en el reloj del nivel)");
            r.Check(hiA < 0.01f && seamA < 2f && al.Length == Mathf.RoundToInt(Level0Events.AlarmLoop * 22050), T + "[Rojo] zumbido de alarma grave, sin siseo (" + (hiA * 100f).ToString("F2") + " % por encima de 2 kHz) y sin costura (" + seamA.ToString("F2") + ")");
        }
        // latido
        AudioClip hb = Adrenaline.CreateHeartbeat();
        r.Check(hb.length > 0.3f && hb.length < 0.5f, T + "[Latido] un latido suelto (" + hb.length.ToString("F2") + " s) que se dispara a su ritmo");
        Object.DestroyImmediate(hb);
        // noche del desierto
        {
            string badN = ""; float wN = 0f;
            for (int k = 0; k < DesertNight.Names.Length; k++)
            {
                float[] d = DesertNight.Synth(k); float hr = HighRatio(d, DesertSynth.SR, 3000f), pk = 0f; foreach (float v in d) pk = Mathf.Max(pk, Mathf.Abs(v));
                wN = Mathf.Max(wN, hr); if (hr > 0.01f || pk < 0.5f || Mathf.Abs(d[d.Length - 1]) > 0.01f) badN += " " + DesertNight.Names[k];
                string lo = DesertNight.Names[k].ToLower(); if (lo.Contains("cruj") || lo.Contains("hiss") || lo.Contains("sopl")) badN += " (nombre)" + lo;
            }
            r.Check(badN == "", T + "[Noche 114] 4 sonidos lejanos (zumbido, llamada, metal, murmullos): graves, sin siseo ni crujidos (máx. " + (wN * 100f).ToString("F2") + " % por encima de 3 kHz)" + badN);
            int on = 0; bool same = true; for (int s = 0; s < 2000; s++) { var a = DesertNight.EventAt(777, s); var b = DesertNight.EventAt(777, s); if (a.on) on++; if (a.on != b.on || a.kind != b.kind || a.azimuth != b.azimuth) same = false; }
            float mean = 2000f * DesertNight.Slot / Mathf.Max(1, on);
            r.Check(same && mean > 18f && mean < 45f, T + "[Noche 114] de vez en cuando (uno cada " + mean.ToString("F0") + " s de media de noche), calendario por semilla y reloj: igual para todos");
        }
        r.Check(Adrenaline.HeartMinBpm < 75f && Adrenaline.HeartMaxBpm >= 140f, T + "[Latido] ritmo " + Adrenaline.HeartMinBpm + "–" + Adrenaline.HeartMaxBpm + " ppm");
    }

    // recorrido en el nivel 0 (monstruos ya parados por la prueba)
    public static IEnumerator Level0Play(SmokeTest.Report r, string tag, GameManager gm)
    {
        Level0Events le = Level0Events.Active;
        r.Check(le != null, tag + "[Nivel 0] sucesos del nivel (ruidos y apagones rojos) activos");
        {   // v7.9.0: VIDA en el HUD, encima de la batería; sin lluvia no baja; caída grande sí
            Health hp = Health.Instance;
            RectTransform hb = gm.healthFill != null ? (RectTransform)gm.healthFill.parent : null, bb = gm.batteryFill != null ? (RectTransform)gm.batteryFill.parent : null;
            r.Check(hp != null && hb != null && bb != null && hb.anchoredPosition.y > bb.anchoredPosition.y + 10f && hb.GetComponentInChildren<UnityEngine.UI.Text>().text == "VIDA", tag + "[Vida] barra roja VIDA encima de la de batería en el HUD");
            float v0 = hp != null ? hp.value : 0f; yield return 1.0f;
            r.Check(hp != null && hp.DrainRate == 0f && hp.value >= v0 - 0.001f, tag + "[Vida] en el nivel 0 no baja sola (" + (hp != null ? hp.value.ToString("F0") : "-") + ")");
        }
        if (le == null) yield break;
        FlickeringLight[] fls = Object.FindObjectsOfType<FlickeringLight>();
        FlickeringLight lit = null; foreach (FlickeringLight f in fls) if (f.targetLight != null && f.targetLight.type == LightType.Point) { lit = f; break; }
        BackroomsGenerator gen = gm.generator;
        r.Check(lit != null && gen != null && gen.PanelEmissiveMat != null, tag + "[Rojo] luces (" + fls.Length + ") y paneles del techo localizados");
        { bool calm = true; for (float t = 0f; t < Level0Events.FirstMin; t += 0.5f) if (le.RedAt(t) > 0f) calm = false;
          r.Check(calm, tag + "[Rojo] al empezar el nivel la luz es la normal (primer apagón a los " + le.Schedule[0].start.ToString("F0") + " s)"); }
        le.DebugSetClock(1f); yield return 0.3f;
        Color amb0 = RenderSettings.ambientLight, col0 = lit != null ? lit.targetLight.color : Color.white;
        Color pan0 = gen != null && gen.PanelEmissiveMat != null ? gen.PanelEmissiveMat.GetColor("_EmissionColor") : Color.white;
        // latido en calma: no suena
        Adrenaline adr = Adrenaline.Local;
        Adrenaline.ForceThreatForTests = 0f; yield return 2.5f;
        int bc = adr != null ? adr.Beats : 0; yield return 1.5f;
        r.Check(adr != null && adr.HeartVolume == 0f && adr.Beats == bc, tag + "[Latido] en calma no suena");
        Adrenaline.ForceThreatForTests = 0.25f; yield return 3.0f;
        float bpmLo = adr.Bpm, volLo = adr.HeartVolume; int b1 = adr.Beats; yield return 2.0f; int nLo = adr.Beats - b1;
        Adrenaline.ForceThreatForTests = 1f; yield return 2.5f;
        float bpmHi = adr.Bpm, volHi = adr.HeartVolume; int b2 = adr.Beats; yield return 2.0f; int nHi = adr.Beats - b2;
        Adrenaline.ForceThreatForTests = -1f;
        r.Check(volLo > 0.005f && bpmLo < 100f && bpmHi > 135f && volHi > volLo * 2f && nHi > nLo,
            tag + "[Latido] más rápido y más fuerte con más amenaza: " + bpmLo.ToString("F0") + " → " + bpmHi.ToString("F0") + " ppm, vol " + volLo.ToString("F2") + " → " + volHi.ToString("F2") + ", latidos en 2 s " + nLo + " → " + nHi);
        // amenaza real: el monstruo cerca persiguiendo > lejos
        MonsterAI main = null; foreach (MonsterAI m in gm.monsters) if (m != null && m.mimic == null) { main = m; break; }
        if (main != null && adr != null)
        {
            Transform me = adr.transform; Vector3 dir = me.forward; dir.y = 0f; dir.Normalize();
            float kd = main.killDistance; main.killDistance = 0.05f;
            main.ChaseForTests();
            main.WarpTo(me.position + dir * 26f, 0f); float tFar = Adrenaline.ThreatFor(me);
            main.WarpTo(me.position + dir * 5f, 0f); float tNear = Adrenaline.ThreatFor(me);
            main.Stop(); main.WarpTo(me.position + new Vector3(500f, 0f, 500f), 0f); main.killDistance = kd;
            float tStop = Adrenaline.ThreatFor(me);
            r.Check(tNear > tFar && tNear > 0.9f && tFar > 0.5f && tStop == 0f, tag + "[Latido] amenaza por distancia y persecución: a 26 m " + tFar.ToString("F2") + ", a 5 m " + tNear.ToString("F2") + ", sin monstruo " + tStop.ToString("F2"));
        }
        yield return 4.5f;
        // ruidos
        Level0Events.DisableNoiseForTests = true;
        string heard = ""; bool okSrc = true;
        for (int k = 0; k < Level0Events.NoiseNames.Length; k++)
        {
            int n0 = le.NoiseEvents;
            le.PlayNoise(k);
            float d = Vector3.Distance(le.NoiseSource.transform.position, adr.transform.position);
            if (le.NoiseEvents != n0 + 1 || le.LastNoise != Level0Events.NoiseNames[k] || d < 13f || d > 33f || le.NoiseSource.spatialBlend < 0.99f || le.NoiseFilter.cutoffFrequency > 2000f) okSrc = false;
            heard += " " + le.LastNoise + " a " + d.ToString("F0") + " m";
            yield return 0.6f;
        }
        Level0Events.DisableNoiseForTests = false;
        r.Check(okSrc, tag + "[Ruidos] suenan en 3D, lejos (13–33 m) y apagados (paso-bajo ≤ 2 kHz):" + heard);
        // apagón rojo: se adelanta el reloj del nivel
        Level0Events.RedEvent ev = le.Schedule[0];
        le.DebugSetClock(ev.start - 1.5f); yield return 0.3f;
        bool before = !le.RedOn && le.Red01 == 0f;
        int rs0 = le.RedStarts;
        le.DebugSetClock(ev.start + 2f); yield return 0.3f;
        Color amb1 = RenderSettings.ambientLight, col1 = lit != null ? lit.targetLight.color : Color.black;
        Color pan1 = gen.PanelEmissiveMat.GetColor("_EmissionColor");
        r.Check(before && le.RedOn && le.Red01 > 0.99f && le.RedStarts == rs0 + 1, tag + "[Rojo] empieza a su hora (" + ev.start.ToString("F0") + " s) y dura " + ev.dur.ToString("F0") + " s");
        r.Check(col1.r > 4f * col1.g && col1.r > 4f * col1.b && pan1.r > 4f * pan1.g, tag + "[Rojo] fluorescentes y paneles en rojo (luz " + col1 + ", panel " + pan1 + ")");
        int allRed = 0, nLights = 0; foreach (FlickeringLight f in fls) if (f != null && f.targetLight != null) { nLights++; if (f.targetLight.color.r > 6f * f.targetLight.color.g) allRed++; }
        r.Check(amb1.r > amb0.r && amb1.r > 6f * amb1.g && RenderSettings.fogColor.r > 6f * RenderSettings.fogColor.g && allRed == nLights && nLights > 0, tag + "[Rojo] toda la sala roja: ambiente " + amb0 + " → " + amb1 + ", niebla " + RenderSettings.fogColor + ", " + allRed + "/" + nLights + " luces en rojo");
        {   // v7.9.1: pulso de alarma y zumbido mientras dura
            float pmn = 9f, pmx = -9f, amn = 9f, amx = -9f;
            for (int k = 0; k < 12; k++) { le.DebugSetClock(ev.start + 2f + k * 0.18f); yield return null; yield return null; pmn = Mathf.Min(pmn, le.Pulse01); pmx = Mathf.Max(pmx, le.Pulse01); amn = Mathf.Min(amn, RenderSettings.ambientLight.r); amx = Mathf.Max(amx, RenderSettings.ambientLight.r); }
            AudioSource asrc = le.AlarmSource;
            r.Check(pmx - pmn > 0.2f && amx > amn * 1.08f && asrc != null && asrc.isPlaying && asrc.volume > 0f && asrc.spatialBlend < 0.01f, tag + "[Rojo] la luz roja late (pulso " + pmn.ToString("F2") + "–" + pmx.ToString("F2") + ", ambiente " + amn.ToString("F3") + "–" + amx.ToString("F3") + ") y suena el zumbido de alarma (" + (asrc != null && asrc.isPlaying) + ", volumen " + (asrc != null ? asrc.volume.ToString("F3") : "-") + ")");
            le.DebugSetClock(ev.start + 2f); yield return 0.2f;
        }
        if (V704Checks.Shots) { Camera cam = adr.GetComponentInChildren<Camera>(); if (cam != null) V704Checks.CaptureHud(cam, "l0_rojo.png"); }
        le.DebugSetClock(ev.End - 0.3f); yield return 0.1f;
        le.DebugSetClock(ev.End + 1.5f); yield return 0.3f;
        Color amb2 = RenderSettings.ambientLight, col2 = lit != null ? lit.targetLight.color : Color.black;
        Color pan2 = gen.PanelEmissiveMat.GetColor("_EmissionColor");
        bool back = !le.RedOn && le.Red01 == 0f && Mathf.Abs(amb2.r - amb0.r) + Mathf.Abs(amb2.g - amb0.g) < 0.002f && Mathf.Abs(col2.g - col0.g) < 0.01f && Mathf.Abs(pan2.g - pan0.g) < 0.01f;
        r.Check(back && (le.AlarmSource == null || !le.AlarmSource.isPlaying), tag + "[Rojo] al acabar vuelve la luz de siempre y calla la alarma");
        le.DebugSetClock(1f); yield return 0.2f;
    }

    // Nivel 114: sin latido (no hay monstruos)
    public static IEnumerator DesertHeart(SmokeTest.Report r, string tag)
    {
        Adrenaline adr = Adrenaline.Local;
        yield return 1.0f;
        r.Check(adr != null && adr.HeartVolume == 0f && Adrenaline.ThreatFor(adr.transform) == 0f && Level0Events.Active == null, tag + "[Latido] en el Nivel 114 no hay latido ni sucesos del nivel 0");
        // v7.9.0: sonidos de la noche
        DesertNight dn = DesertNight.Active; DesertSky sky = DesertSky.Active;
        r.Check(dn != null && sky != null, tag + "[Noche 114] sonidos de la noche activos");
        if (dn == null || sky == null || adr == null) yield break;
        string heard = ""; bool okS = true;
        for (int k = 0; k < DesertNight.Names.Length; k++)
        {
            DesertNight.Ev e = DesertNight.EventAt(dn.Seed, k); e.kind = k; int p0 = dn.Played;
            dn.Fire(e, -1);
            float d = Vector3.Distance(dn.Source.transform.position, adr.transform.position);
            if (dn.Played != p0 + 1 || dn.Last != DesertNight.Names[k] || d < 40f || d > 125f || dn.Source.spatialBlend < 0.99f || dn.Filter.cutoffFrequency > 1501f) okS = false;
            heard += " " + dn.Last.Substring(3) + " a " + d.ToString("F0") + " m";
            yield return 0.3f;
        }
        r.Check(okS, tag + "[Noche 114] suenan en 3D, lejos (40–125 m) y apagados:" + heard);
        // de día no suena nada; de noche, el siguiente suceso del calendario suena a su hora
        float fh = DesertSky.ForceHour;
        DesertSky.ForceHour = 0.5f; yield return 2.0f;
        int slot = Mathf.FloorToInt(sky.Clock / DesertNight.Slot) + 1, found = -1;
        for (int s = slot; s < slot + 6; s++) if (DesertNight.EventAt(dn.Seed, s).on) { found = s; break; }
        if (found >= 0)
        {
            float at = found * DesertNight.Slot + DesertNight.EventAt(dn.Seed, found).offset; int p1 = dn.Played; float t0 = Time.time;
            while (sky.Clock < at + 0.5f && Time.time - t0 < 70f) yield return null;
            r.Check(sky.Night >= DesertNight.NightMin && dn.Played == p1 + 1 && dn.LastSlot == found, tag + "[Noche 114] de noche el suceso del calendario suena a su hora (ranura " + found + ", " + dn.Last + ", noche " + sky.Night.ToString("F2") + ")");
        }
        else r.Info(tag + "[Noche 114] sin suceso en las próximas ranuras (comprobado solo el calendario)");
        DesertSky.ForceHour = fh;
    }
}
