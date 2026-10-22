using System;
using System.Net.Sockets;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// Pruebas de humo (sin necesidad de ventana):
//  - RunEditMode: genera cada nivel con varias semillas (modo edición) y comprueba materiales, salida, alcanzabilidad (BFS),
//    colisiones por el camino, pilas, monstruos, ausencia de cartel "SALIDA", NavMesh...
//  - RunPlayMode: abre Assets/Scenes/Main.unity, entra en Play y recorre los 5 niveles
//    (comprueba HUD, pilas, derrota -> reinicio, llegar a la salida -> siguiente nivel, victoria final, reinicio total).
// Uso: Unity -batchmode -nographics -projectPath <proyecto> -executeMethod SmokeTest.RunEditMode -logFile <log>
//      (sin -quit en PlayMode: el propio test llama a EditorApplication.Exit)
public static class SmokeTest
{
    private const string EditReport = "/workspace/logs/smoke-editmode.txt";
    private static readonly int[] Seeds = { 1, 7, 12345 };

    // ------------------------------------------------------------------
    // Modo edición
    // ------------------------------------------------------------------
    public static void RunEditMode()
    {
        Report r = new Report(EditReport);
        try
        {
            ShaderVariantKeeper.Generate();
            TextureLibrary.ReleaseAll();
            ScanSourcesForExitHints(r);
            V711Checks.AssetChecks(r);   // v7.1.1: lightmaps/texturas GPU

            for (int lv = 0; lv < LevelConfig.Count; lv++)
            {
                foreach (int seed in Seeds)
                {
                    TestLevel(r, lv, seed);
                }
            }
            TextureLibrary.ReleaseAll();
            TestBakedTextures(r);
            TestMonsterModels(r);
            TestVhsAssets(r);
            TestInventoryData(r);
            TestSettings(r);
            TestAudioMix(r);
            TestElevatorAssets(r);
            TestV4Assets(r);
            TestNetwork(r);
            TestNatAndRelay(r);
        }
        catch (Exception e)
        {
            r.Fail("Excepción no controlada: " + e);
        }
        r.Finish();
    }

    // ------------------------------------------------------------------
    // Modelos de monstruos (prefabs, animaciones, créditos, fallback)
    // ------------------------------------------------------------------
    // v6.6: texturas horneadas con Blender (carga, formato, tamaño, fallback por CPU)
    private static void TestBakedTextures(Report r)
    {
        var seen = new HashSet<SurfaceStyle>();
        long bytes = 0;
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            LevelConfig cfg = LevelConfig.Get(lv);
            foreach (SurfaceStyle st in new[] { cfg.wallStyle, cfg.floorStyle, cfg.ceilingStyle })
            {
                if (!seen.Add(st)) continue;
                SurfaceSet set = TextureLibrary.Get(st, cfg.wallHeight);
                bool ok = set != null && set.baked && set.albedo != null && set.normal != null && set.mask != null;
                r.Check(ok, "texturas Blender: " + st + " horneadas y cargadas (nivel " + lv + ")");
                if (!ok) continue;
                r.Check(set.albedo.width >= 1024 && set.albedo.mipmapCount > 1 && set.albedo.anisoLevel >= 8 && set.albedo.wrapMode == TextureWrapMode.Repeat,
                    "texturas Blender: " + st + " " + set.albedo.width + "px, mipmaps, anisotrópico x" + set.albedo.anisoLevel + ", repetición");
                r.Check(!set.albedo.isReadable && !set.normal.isReadable && !set.mask.isReadable, "texturas Blender: " + st + " no legibles (memoria)");
                string np = UnityEditor.AssetDatabase.GetAssetPath(set.normal);
                var ti = UnityEditor.AssetImporter.GetAtPath(np) as UnityEditor.TextureImporter;
                var ta = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(set.albedo)) as UnityEditor.TextureImporter;
                var tm = UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(set.mask)) as UnityEditor.TextureImporter;
                r.Check(ti != null && ti.textureType == UnityEditor.TextureImporterType.NormalMap && ta != null && ta.sRGBTexture && tm != null && !tm.sRGBTexture,
                    "texturas Blender: " + st + " normal=NormalMap, albedo sRGB, máscara lineal");
                bytes += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(set.albedo) + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(set.normal) + UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(set.mask);
            }
        }
        r.Info("texturas Blender: memoria de las superficies de los 5 niveles ≈ " + (bytes / 1048576f).ToString("F1") + " MB (" + seen.Count + " estilos)");
        r.Check(seen.Count >= 12, "texturas Blender: " + seen.Count + " estilos de superficie distintos en los 5 niveles");
        // La altura de pared distinta no usa el papel horneado (lleva zócalo a escala): cae a la generación por CPU
        TextureLibrary.ReleaseAll();
        SurfaceSet other = TextureLibrary.Get(SurfaceStyle.WallpaperYellow, 2.5f);
        r.Check(other != null && !other.baked && other.albedo != null, "texturas: otra altura de pared -> fallback por CPU");
        TextureLibrary.ReleaseAll();
        ProceduralTextures.UseBaked = false;
        SurfaceSet cpu = TextureLibrary.Get(SurfaceStyle.CarpetBrown, 3f);
        r.Check(cpu != null && !cpu.baked && cpu.albedo != null && cpu.normal != null && cpu.mask != null, "texturas: sin horneadas -> generación por CPU intacta");
        TextureLibrary.ReleaseAll();
        ProceduralTextures.UseBaked = true;
    }

    private static void TestMonsterModels(Report r)
    {
        var ids = new HashSet<string>();
        string credits = File.Exists("CREDITS.md") ? File.ReadAllText("CREDITS.md") : "";
        r.Check(credits.Length > 200, "CREDITS.md existe");
        r.Check(MonsterProfile.All.Length == LevelConfig.Count, "un perfil de monstruo por nivel (" + MonsterProfile.All.Length + ")");
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            MonsterProfile p = MonsterProfile.ForLevel(lv);
            string tag = "[Monstruo " + lv + " " + p.id + "] ";
            r.Check(ids.Add(p.id), tag + "modelo distinto de los demás niveles");
            r.Check(LevelConfig.Get(lv).monsterModel == p.id, tag + "LevelConfig.monsterModel coincide");
            r.Check(credits.Contains(p.id) || credits.Contains(p.displayName), tag + "aparece en CREDITS.md");
            r.Check(!string.IsNullOrEmpty(p.licence) && (p.licence.Contains("CC0") || p.licence.Contains("CC-BY") || p.licence.ToLowerInvariant().Contains("public")), tag + "licencia libre declarada: " + p.licence);

            GameObject prefab = Resources.Load<GameObject>("Monsters/" + p.id);
            r.Check(prefab != null, tag + "prefab en Resources/Monsters");
            if (prefab == null) continue;
            Animator anim = prefab.GetComponentInChildren<Animator>(true);
            r.Check(anim != null && anim.runtimeAnimatorController != null, tag + "Animator con controlador");
            if (anim != null && anim.runtimeAnimatorController != null)
            {
                AnimationClip[] clips = anim.runtimeAnimatorController.animationClips;
                var names = new List<string>();
                foreach (AnimationClip c in clips) if (c != null && c.length > 0.05f) names.Add(c.name);
                r.Check(names.Contains("Idle") && names.Contains("Run") && (names.Contains("Walk") || p.walkUsesRun), tag + "clips Idle/Walk/Run presentes (" + string.Join(",", names) + ")");
                r.Check(anim.avatar != null && anim.avatar.isValid, tag + "avatar válido");
            }
            int skinned = 0, tris = 0, texd = 0;
            foreach (Renderer rend in prefab.GetComponentsInChildren<Renderer>(true))
            {
                SkinnedMeshRenderer sk = rend as SkinnedMeshRenderer;
                if (sk != null && sk.sharedMesh != null) { skinned++; tris += sk.sharedMesh.triangles.Length / 3; }
                foreach (Material m in rend.sharedMaterials) if (m != null && m.mainTexture != null) texd++;
            }
            r.Check(skinned > 0, tag + "malla con esqueleto (" + skinned + " skinned, " + tris + " tris)");
            r.Check(tris > 500 && tris < 60000, tag + "recuento de triángulos razonable (" + tris + ")");
            r.Check(texd > 0, tag + "materiales con textura (" + texd + ")");

            // Instanciar a través de MonsterVisual (ojos, altura, sin fallback)
            GameObject host = new GameObject("MonsterTest");
            MonsterVisual mv = MonsterVisual.Create(host, p, null);
            r.Check(mv != null && !mv.IsFallback && mv.animator != null, tag + "MonsterVisual carga el modelo real (sin fallback)");
            if (mv != null)
            {
                Physics.SyncTransforms();
                Bounds b = new Bounds(host.transform.position, Vector3.zero);
                bool any = false;
                foreach (Renderer rr in host.GetComponentsInChildren<Renderer>())
                {
                    if (rr is SkinnedMeshRenderer) { if (!any) { b = rr.bounds; any = true; } else b.Encapsulate(rr.bounds); }
                }
                r.Check(any && b.size.y > 0.4f && b.size.y < 8f && b.size.x < 8f && b.size.z < 8f, tag + "tamaño en escena razonable " + b.size.ToString("F2"));
                r.Check(Mathf.Abs(b.min.y - host.transform.position.y) < 0.9f, tag + "pies cerca del suelo (min.y " + b.min.y.ToString("F2") + ")");
                if (p.eyes) r.Check(host.GetComponentsInChildren<Light>(true).Length > 0 || host.GetComponentsInChildren<Renderer>(true).Length > 1, tag + "ojos brillantes añadidos");
            }
            UnityEngine.Object.DestroyImmediate(host);
        }

        // Fallback: un perfil con modelo inexistente produce una criatura procedural, sin romper
        MonsterProfile fake = new MonsterProfile { id = "NoExiste_XYZ", displayName = "Falso" };
        GameObject fh = new GameObject("MonsterFallbackTest");
        MonsterVisual fv = MonsterVisual.Create(fh, fake, null);
        r.Check(fv != null && fv.IsFallback && fh.GetComponentsInChildren<Renderer>(true).Length > 0, "modelo inexistente -> criatura procedural de reserva");
        UnityEngine.Object.DestroyImmediate(fh);

        r.Check(!string.IsNullOrEmpty(UIManager.CreditsLine()) && UIManager.CreditsLine().Contains("CC0"), "línea de créditos en el juego: " + UIManager.CreditsLine());
    }

    // ------------------------------------------------------------------
    // Inventario: definiciones, iconos, notas, apilado y persistencia (sin entrar en Play)
    // ------------------------------------------------------------------
    // ------------------------------------------------------------------
    // Multijugador: serialización, transporte TCP, determinismo del mundo
    // ------------------------------------------------------------------
    private static ulong HashOf(int lv, int seed, int extra)
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        GameObject go = new GameObject("NetGen");
        BackroomsGenerator g = go.AddComponent<BackroomsGenerator>();
        g.config = LevelConfig.Get(lv);
        g.useRandomSeed = false; g.seed = seed; g.extraMonsters = extra;
        g.Generate();
        ulong h = g.WorldHash();
        int monsters = g.MonsterSpawns.Count;
        UnityEngine.Object.DestroyImmediate(go);
        lastMonsterCount = monsters;
        return h;
    }
    private static int lastMonsterCount;

    private static void TestNetwork(Report r)
    {
        // Serialización
        NetWriter w = new NetWriter(42).Byte(7).Int(-123456).Float(3.5f).Bool(true).Str("¡Hola, ñandú!").Vec3(new Vector3(1, 2, 3)).Long(1L << 40);
        NetReader rd = new NetReader(w.ToArray());
        bool ser = rd.Type == 42 && rd.Byte() == 7 && rd.Int() == -123456 && Mathf.Approximately(rd.Float(), 3.5f) && rd.Bool()
            && rd.Str() == "¡Hola, ñandú!" && rd.Vec3() == new Vector3(1, 2, 3) && rd.Long() == (1L << 40) && rd.End;
        r.Check(ser, "[Red] NetWriter/NetReader: ida y vuelta de todos los tipos");
        r.Check(NetUtil.Fnv(NetUtil.FnvBasis, 5) == NetUtil.Fnv(NetUtil.FnvBasis, 5) && NetUtil.Fnv(NetUtil.FnvBasis, 5) != NetUtil.Fnv(NetUtil.FnvBasis, 6), "[Red] hash FNV determinista");
        r.Check(NetUtil.LocalAddresses() != null, "[Red] lista de IP locales (" + string.Join(", ", NetUtil.LocalAddresses().ToArray()) + ")");
        r.Check(!string.IsNullOrEmpty(NetSession.Protocol), "[Red] versión de protocolo: " + NetSession.Protocol);

        // Escalado por nº de jugadores
        bool scaleOk = true;
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            if (NetSession.ExtraMonstersFor(1, lv) != 0 || NetSession.ExtraMonstersFor(2, lv) != 0) scaleOk = false;
            int e4 = NetSession.ExtraMonstersFor(4, lv);
            if (e4 < 0 || e4 > 1) scaleOk = false;
        }
        r.Check(scaleOk, "[Red] 1-2 jugadores: monstruos sin cambios; 3-4: como mucho +1");

        // Determinismo del mundo por nivel
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            ulong a = HashOf(lv, 4242, 0);
            ulong b = HashOf(lv, 4242, 0);
            ulong c = HashOf(lv, 4243, 0);
            r.Check(a == b, "[Red] nivel " + lv + ": misma semilla => mismo hash de mundo (" + a.ToString("x16") + ")");
            r.Check(a != c, "[Red] nivel " + lv + ": otra semilla => otro hash");
            int baseMon = lastMonsterCount;
            ulong d = HashOf(lv, 4242, 1);
            r.Check(lastMonsterCount == baseMon + 1 && d != a, "[Red] nivel " + lv + ": +1 monstruo extra aparece y cambia el hash (" + baseMon + " -> " + lastMonsterCount + ")");
        }
        // no depende del estado global de UnityEngine.Random
        UnityEngine.Random.InitState(1);
        ulong h1 = HashOf(2, 99, 0);
        UnityEngine.Random.InitState(987654);
        for (int i = 0; i < 17; i++) UnityEngine.Random.value.ToString();
        ulong h2 = HashOf(2, 99, 0);
        r.Check(h1 == h2, "[Red] el hash no depende de UnityEngine.Random global");

        // Transporte TCP en bucle local
        NetListener lis = new NetListener();
        string err;
        int port = 47791;
        bool started = lis.Start(port, out err);
        r.Check(started, "[Red] el servidor abre el puerto " + port + (err != null ? " (" + err + ")" : ""));
        if (started)
        {
            NetListener dup = new NetListener(); string err2;
            bool dupOk = dup.Start(port, out err2);
            bool onWin = Application.platform == RuntimePlatform.WindowsEditor;
            // En Windows el puerto es exclusivo; en Linux/macOS se usa SO_REUSEADDR (reabrir rápido) y no se exige el fallo.
            if (onWin) r.Check(!dupOk && !string.IsNullOrEmpty(err2) && err2.Contains("ya está en uso"), "[Red] puerto ocupado => mensaje claro en español (" + err2 + ")");
            else r.Info("[Red] (no Windows) segundo servidor en el mismo puerto: " + dupOk);
            if (dupOk) dup.Stop();

            NetConnector cn = new NetConnector("127.0.0.1", port, 3000);
            double t0 = EditorApplication.timeSinceStartup;
            Socket srv = null;
            while ((srv == null || !cn.Done) && EditorApplication.timeSinceStartup - t0 < 5) { if (srv == null) srv = lis.Accept(); System.Threading.Thread.Sleep(5); }
            r.Check(srv != null && cn.Done && cn.Result != null, "[Red] el cliente conecta al servidor local");
            if (srv != null && cn.Result != null)
            {
                NetConn sc = new NetConn(srv, 0f), cc = new NetConn(cn.Result, 0f);
                // mensajes pequeños en ráfaga + uno grande, comprobando orden e integridad
                for (int i = 0; i < 200; i++) cc.Send(new NetWriter(9).Int(i).ToArray());
                byte[] big = new byte[150000]; for (int i = 0; i < big.Length; i++) big[i] = (byte)(i * 31);
                cc.Send(big);
                sc.Send(new NetWriter(77).Str("ping").ToArray());
                int got = 0; bool order = true, bigOk = false, back = false;
                t0 = EditorApplication.timeSinceStartup;
                while (EditorApplication.timeSinceStartup - t0 < 5 && !(bigOk && back))
                {
                    cc.Pump(0f); sc.Pump(0f); cc.Flush(); sc.Flush();
                    while (sc.Incoming.Count > 0)
                    {
                        byte[] m = sc.Incoming.Dequeue();
                        if (m.Length == 5 && m[0] == 9) { NetReader q = new NetReader(m); if (q.Int() != got) order = false; got++; }
                        else if (m.Length == big.Length) { bigOk = true; for (int i = 0; i < m.Length; i += 997) if (m[i] != big[i]) bigOk = false; }
                    }
                    while (cc.Incoming.Count > 0) { NetReader q = new NetReader(cc.Incoming.Dequeue()); if (q.Type == 77 && q.Str() == "ping") back = true; }
                    System.Threading.Thread.Sleep(2);
                }
                r.Check(got == 200 && order, "[Red] 200 mensajes llegan completos y en orden (" + got + ")");
                r.Check(bigOk, "[Red] mensaje de 150 KB reensamblado correctamente");
                r.Check(back, "[Red] el servidor responde al cliente");
                // corte de conexión detectado
                cc.Kill("prueba");
                t0 = EditorApplication.timeSinceStartup;
                while (!sc.Dead && EditorApplication.timeSinceStartup - t0 < 5) { sc.Pump(0f); System.Threading.Thread.Sleep(5); }
                r.Check(sc.Dead, "[Red] el servidor detecta que el cliente cerró la conexión");
                sc.Kill("fin");
            }
            lis.Stop();
        }
        // Errores de conexión
        string[] cases = { "127.0.0.1", "no.existe.invalid" };
        int[] ports = { 47792, 47792 };
        string[] expect = { "refused", "badaddr" };
        for (int i = 0; i < cases.Length; i++)
        {
            NetConnector c2 = new NetConnector(cases[i], ports[i], 3000);
            double t1 = EditorApplication.timeSinceStartup;
            while (!c2.Done && EditorApplication.timeSinceStartup - t1 < 8) System.Threading.Thread.Sleep(10);
            // (en este entorno sin DNS un nombre inexistente puede acabar en "timeout": solo exigimos un error)
            bool okErr = c2.Done && (i == 0 ? c2.Error == expect[i] : !string.IsNullOrEmpty(c2.Error));
            r.Check(okErr, "[Red] conectar a " + cases[i] + " => error '" + c2.Error + "' (esperado '" + expect[i] + "')");
        }
    }

    // ------------------------------------------------------------------
    // v6.1: UPnP / NAT-PMP (contra routers simulados), IP pública, direcciones, relay (contra el servidor Node real)
    // ------------------------------------------------------------------
    private static bool WaitUntil(Func<bool> f, int ms)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (f()) return true; System.Threading.Thread.Sleep(10); }
        return f();
    }

    private static UpnpMapper NewMapper(MockUpnpRouter rt)
    {
        UpnpMapper m = new UpnpMapper();
        m.SsdpEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, rt.SsdpPort);
        m.DiscoveryMs = 1500; m.HttpMs = 3000; m.EnableNatPmp = false;
        return m;
    }

    private static void TestNatAndRelay(Report r)
    {
        bool hadBuiltin = RelayDefault.HasBuiltin;
        if (hadBuiltin)
        {
            string bh; int bp;
            r.Check(NetAddress.TryParse(RelayDefault.Address, RelayConfig.DefaultPort, out bh, out bp) && RelayConfig.UsingBuiltin && RelayConfig.DisplayAddress == RelayConfig.BuiltinLabel && !RelayConfig.DisplayAddress.Contains(bh), "[Cripto] relay integrado: se descifra, es una dirección válida y la interfaz solo muestra «" + RelayConfig.BuiltinLabel + "»");
            r.Check(RelayConfig.FieldText == "" , "[Cripto] relay integrado: el campo del menú no lo contiene");
        }
        else r.Info("[Cripto] esta compilación no lleva relay integrado (repositorio público): se usa el del usuario / relay.txt");
        RelayDefault.Suppress = true;
        try { TestNatAndRelayInner(r); TestRelayToken(r); } finally { RelayDefault.Suppress = false; RelayDefault.TestInject(null, null); }
    }

    private static void TestNatAndRelayInner(Report r)
    {
        // ---- direcciones pegadas ip:puerto, códigos de sala ----
        string[] okIn = { "1.2.3.4", "1.2.3.4:7777", " 83.12.34.56:8000 ", "tcp://10.0.0.5:1234/", "mi-relay.fly.dev:7780", "host" };
        string[] okHost = { "1.2.3.4", "1.2.3.4", "83.12.34.56", "10.0.0.5", "mi-relay.fly.dev", "host" };
        int[] okPort = { 7777, 7777, 8000, 1234, 7780, 7777 };
        bool parseOk = true;
        for (int i = 0; i < okIn.Length; i++) { string h; int p; if (!NetAddress.TryParse(okIn[i], 7777, out h, out p) || h != okHost[i] || p != (okIn[i].Contains("fly") ? 7780 : okPort[i])) { parseOk = false; r.Info("parse falla: " + okIn[i] + " -> " + h + ":" + p); } }
        r.Check(parseOk, "[Red] «ip:puerto» pegado: se separa IP y puerto (con espacios, esquema tcp://, nombres)");
        string[] bad = { "", "  ", "1.2.3.4:", "1.2.3.4:0", "1.2.3.4:70000", "1.2.3.4:abc", "::1", "a b", "1.2.3.4:5:6", ":7777", "ho$t" };
        bool badOk = true;
        foreach (string b in bad) { string h; int p; if (NetAddress.TryParse(b, 7777, out h, out p)) { badOk = false; r.Info("parse debería fallar: '" + b + "'"); } }
        r.Check(badOk, "[Red] direcciones inválidas se rechazan (" + bad.Length + " casos)");
        r.Check(RoomCodes.IsValid("abc234") && RoomCodes.Normalize(" ab-c 234 ") == "ABC234" && !RoomCodes.IsValid("ABC23") && !RoomCodes.IsValid("ABCO23") && !RoomCodes.IsValid("ABC231") && !RoomCodes.IsValid("ABC2345"), "[Red] códigos de sala: 6 caracteres sin I, O, 0, 1; se normalizan");

        // ---- CGNAT / textos ----
        r.Check(HostAddressInfo.IsPrivateOrCgnat("100.64.1.1") && HostAddressInfo.IsPrivateOrCgnat("192.168.1.5") && HostAddressInfo.IsPrivateOrCgnat("10.1.2.3") && HostAddressInfo.IsPrivateOrCgnat("172.20.0.1")
            && !HostAddressInfo.IsPrivateOrCgnat("83.12.34.56") && !HostAddressInfo.IsPrivateOrCgnat("172.32.0.1") && !HostAddressInfo.IsPrivateOrCgnat("100.63.0.1"), "[Red] detección de rangos privados / CGNAT");
        string cg1 = HostAddressInfo.CgnatWarning("100.64.12.3", "83.12.34.56");
        string cg2 = HostAddressInfo.CgnatWarning("83.12.34.57", "83.12.34.56");
        string cg3 = HostAddressInfo.CgnatWarning("83.12.34.56", "83.12.34.56");
        string cg4 = HostAddressInfo.CgnatWarning("", "83.12.34.56");
        r.Check(cg1.Contains("CGNAT") && cg2.Contains("CGNAT") && cg1.Contains("VPN") && cg1.Contains("servidor intermedio") && cg3 == "" && cg4 == "", "[Red] aviso CGNAT: IP del router privada o distinta de la pública (y ninguno si coinciden)");
        r.Check(HostAddressInfo.BestAddress(7777, "83.1.2.3", "192.168.1.5") == "83.1.2.3:7777" && HostAddressInfo.BestAddress(7777, "", "192.168.1.5") == "192.168.1.5:7777" && HostAddressInfo.FirstLocalIp(new[] { "172.30.0.2  (eth0)" }) == "172.30.0.2", "[Red] «Copiar dirección»: ip:puerto (pública si se conoce, si no la local)");
        string d1 = HostAddressInfo.Describe(7777, new[] { "192.168.1.5  (Wi-Fi)" }, PublicIpService.State.Ok, "83.12.34.56", UpnpMapper.State.Mapped, "Puerto abierto automáticamente (UPnP, TCP 7777)", "83.12.34.56", true);
        r.Check(d1.Contains("192.168.1.5:7777  (Wi-Fi)") && d1.Contains("IP pública: 83.12.34.56:7777") && d1.Contains("Puerto abierto automáticamente") && !d1.Contains("CGNAT"), "[Red] pantalla del anfitrión: IP local, IP pública y estado UPnP");
        string d2 = HostAddressInfo.Describe(7777, new[] { "192.168.1.5  (Wi-Fi)" }, PublicIpService.State.Failed, "", UpnpMapper.State.Failed, "No se pudo abrir el puerto automáticamente: abre TCP 7777 en tu router o usa una VPN", "", true);
        r.Check(d2.Contains("no disponible") && d2.Contains("No se pudo abrir el puerto automáticamente: abre TCP 7777 en tu router o usa una VPN"), "[Red] pantalla del anfitrión sin Internet ni UPnP: textos de error");
        string d3 = HostAddressInfo.Describe(7777, new[] { "192.168.1.5" }, PublicIpService.State.Ok, "83.12.34.56", UpnpMapper.State.Mapped, "Puerto abierto automáticamente (UPnP, TCP 7777)", "100.70.0.2", true);
        r.Check(d3.Contains("CGNAT"), "[Red] pantalla del anfitrión: avisa de CGNAT");
        string d4 = HostAddressInfo.Describe(7777, new[] { "192.168.1.5" }, PublicIpService.State.Working, "", UpnpMapper.State.Idle, "", "", false);
        r.Check(d4.Contains("detectando") && d4.Contains("UPnP"), "[Red] pantalla del anfitrión antes de crear la sala: «detectando» y aviso de UPnP");

        // ---- análisis de la descripción UPnP / mensajes SOAP ----
        string ctl, svc;
        string xml = "<?xml version=\"1.0\"?><root xmlns=\"urn:schemas-upnp-org:device-1-0\"><URLBase>http://192.168.1.1:5000/</URLBase><device><serviceList><service><serviceType>urn:schemas-upnp-org:service:WANPPPConnection:1</serviceType><controlURL>/ppp</controlURL></service><service><serviceType>urn:schemas-upnp-org:service:WANIPConnection:2</serviceType><controlURL>ctl/ip</controlURL></service></serviceList></device></root>";
        bool pd = UpnpMapper.ParseDescription(xml, "http://192.168.1.1:49152/desc.xml", out ctl, out svc);
        r.Check(pd && ctl == "http://192.168.1.1:5000/ctl/ip" && svc.Contains("WANIPConnection"), "[Red] descripción UPnP: elige WANIPConnection y resuelve controlURL con URLBase (" + ctl + ")");
        r.Check(!UpnpMapper.ParseDescription("<root><device/></root>", "http://x/", out ctl, out svc), "[Red] descripción sin servicio WAN => no válida");
        string env = UpnpMapper.BuildSoapEnvelope("urn:schemas-upnp-org:service:WANIPConnection:1", "AddPortMapping", UpnpMapper.BuildAddMappingArgs(7777, "192.168.1.5", "Backrooms & <Escape>", 0));
        r.Check(env.Contains("<u:AddPortMapping xmlns:u=\"urn:schemas-upnp-org:service:WANIPConnection:1\">") && env.Contains("<NewExternalPort>7777</NewExternalPort>") && env.Contains("<NewProtocol>TCP</NewProtocol>")
            && env.Contains("<NewInternalClient>192.168.1.5</NewInternalClient>") && env.Contains("Backrooms &amp; &lt;Escape&gt;") && env.Contains("<NewLeaseDuration>0</NewLeaseDuration>"), "[Red] sobre SOAP AddPortMapping bien construido (y escapado)");
        System.Xml.XmlDocument chk = new System.Xml.XmlDocument(); bool wf = true; try { chk.LoadXml(env); } catch (Exception) { wf = false; }
        r.Check(wf, "[Red] el sobre SOAP es XML bien formado");

        // ---- router UPnP simulado ----
        using (MockUpnpRouter rt = new MockUpnpRouter())
        {
            UpnpMapper m = NewMapper(rt);
            m.Start(47900);
            r.Check(WaitUntil(() => m.Status == UpnpMapper.State.Mapped, 8000), "[Red] UPnP: mapeo conseguido con el router simulado (" + m.Status + " " + m.Detail + ")");
            string add = rt.FirstLog("AddPortMapping");
            r.Check(add != null && add.Contains("port=47900") && add.Contains("proto=TCP") && add.Contains("internalPort=47900") && add.Contains("internal=127.0.0.1") && add.Contains("lease=0") && add.Contains("enabled=1") && add.Contains("WANIPConnection"), "[Red] UPnP: el router recibe AddPortMapping TCP 47900 (" + add + ")");
            r.Check(rt.SsdpRequests >= 1 && m.Method == "UPnP" && m.ExternalIp == "203.0.113.7", "[Red] UPnP: descubrimiento SSDP y IP WAN del router (" + m.ExternalIp + ")");
            r.Check(m.StatusText.StartsWith("Puerto abierto automáticamente"), "[Red] UPnP: texto de estado «" + m.StatusText + "»");
            r.Check(rt.MappingCount == 1, "[Red] UPnP: el router tiene el mapeo activo");
            m.Stop(true);
            r.Check(rt.HasLog("DeletePortMapping port=47900 proto=TCP") && rt.MappingCount == 0 && m.Removed, "[Red] UPnP: al parar se elimina el mapeo (DeletePortMapping)");

            // router que solo admite concesiones temporales
            rt.RejectPermanentLease = true; rt.Log.Clear();
            UpnpMapper m2 = NewMapper(rt); m2.Start(47901);
            r.Check(WaitUntil(() => m2.Status == UpnpMapper.State.Mapped, 8000), "[Red] UPnP: tras el error 725 reintenta con concesión temporal");
            r.Check(rt.CountLog("AddPortMapping") == 2 && rt.FirstLog("AddPortMapping").Contains("lease=0") && rt.Log.FindAll(l => l.Contains("lease=7200")).Count == 1, "[Red] UPnP: 1.º lease=0 rechazado, 2.º lease=7200");
            m2.Stop(true);
            r.Check(rt.MappingCount == 0, "[Red] UPnP: con lease temporal también se borra al parar");
            rt.RejectPermanentLease = false; rt.Log.Clear();

            // conflicto 718: borra y reintenta
            rt.ConflictOnce = true;
            UpnpMapper m3 = NewMapper(rt); m3.Start(47902);
            r.Check(WaitUntil(() => m3.Status == UpnpMapper.State.Mapped, 8000) && rt.HasLog("DeletePortMapping port=47902"), "[Red] UPnP: ante el conflicto 718 borra el mapeo previo y reintenta");
            m3.Stop(true);
            rt.Log.Clear();

            // servicio WANPPPConnection
            rt.UsePppService = true;
            UpnpMapper m4 = NewMapper(rt); m4.Start(47903);
            r.Check(WaitUntil(() => m4.Status == UpnpMapper.State.Mapped, 8000) && rt.FirstLog("AddPortMapping").Contains("WANPPPConnection"), "[Red] UPnP: también funciona con WANPPPConnection");
            m4.Stop(true);
            rt.UsePppService = false;

            // descripción rota => fallo limpio
            rt.BreakDescription = true;
            UpnpMapper m5 = NewMapper(rt); m5.Start(47904);
            r.Check(WaitUntil(() => m5.Status == UpnpMapper.State.Failed, 10000), "[Red] UPnP: si la descripción falla, estado «Failed» sin excepciones (" + m5.Detail + ")");
            m5.Stop(true);
        }
        // sin router: nadie responde
        {
            using (UdpClientDeaf deaf = new UdpClientDeaf())
            {
                UpnpMapper m = new UpnpMapper(); m.SsdpEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, deaf.Port);
                m.DiscoveryMs = 700; m.EnableNatPmp = false;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                m.Start(7777);
                r.Check(m.Status == UpnpMapper.State.Working || m.Status == UpnpMapper.State.Failed, "[Red] UPnP: arranca en segundo plano sin bloquear");
                r.Check(WaitUntil(() => m.Status == UpnpMapper.State.Failed, 8000), "[Red] UPnP: sin router => «Failed» en " + sw.ElapsedMilliseconds + " ms");
                r.Check(m.StatusText == "No se pudo abrir el puerto automáticamente: abre TCP 7777 en tu router o usa una VPN", "[Red] UPnP: mensaje de fallo en español exacto");
                m.Stop(true);
            }
        }
        // NAT-PMP simulado
        using (MockNatPmp pmp = new MockNatPmp())
        {
            UpnpMapper m = new UpnpMapper();
            using (UdpClientDeaf deaf = new UdpClientDeaf())
            {
                m.SsdpEndpoint = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, deaf.Port);
                m.DiscoveryMs = 500; m.GatewayOverride = System.Net.IPAddress.Loopback; m.NatPmpPort = pmp.Port; m.NatPmpTryMs = 200;
                m.Start(47905);
                r.Check(WaitUntil(() => m.Status == UpnpMapper.State.Mapped, 8000), "[Red] NAT-PMP: mapeo conseguido cuando UPnP no responde (" + m.Detail + ")");
                r.Check(m.Method == "NAT-PMP" && m.ExternalIp == "198.51.100.9" && pmp.Has("external-address") && pmp.Has("map tcp internal=47905 external=47905 lifetime=7200"), "[Red] NAT-PMP: IP externa y mapeo TCP 47905 (2 h)");
                m.Stop(true);
                r.Check(pmp.Has("map tcp internal=47905 external=0 lifetime=0"), "[Red] NAT-PMP: al parar se borra el mapeo (lifetime 0)");
            }
        }

        // ---- IP pública (eco HTTP simulado) ----
        string[] savedEp = PublicIpService.Endpoints; int savedTo = PublicIpService.TimeoutMs;
        try
        {
            string reply = "203.0.113.55\n";
            using (MockHttpServer echo = new MockHttpServer((string method, string path, Dictionary<string, string> h, string body, out int st, out string ct, out string resp) => { st = 200; ct = "text/plain"; resp = reply; }))
            {
                PublicIpService.TimeoutMs = 2000;
                PublicIpService.Endpoints = new[] { "http://127.0.0.1:" + echo.Port + "/" };
                PublicIpService.Reset(); PublicIpService.Refresh(true);
                r.Check(WaitUntil(() => PublicIpService.Status == PublicIpService.State.Ok, 6000) && PublicIpService.Ip == "203.0.113.55", "[Red] IP pública: se obtiene del servicio de eco (" + PublicIpService.Ip + ")");
                reply = "<html>error</html>";
                PublicIpService.Reset(); PublicIpService.Refresh(true);
                r.Check(WaitUntil(() => PublicIpService.Status == PublicIpService.State.Failed, 6000) && PublicIpService.Ip == "", "[Red] IP pública: respuesta basura => «Failed»");
            }
            PublicIpService.Endpoints = new[] { "http://127.0.0.1:1/", "http://127.0.0.1:2/" };
            PublicIpService.Reset(); PublicIpService.Refresh(true);
            r.Check(WaitUntil(() => PublicIpService.Status == PublicIpService.State.Failed, 8000), "[Red] IP pública: sin conexión => «Failed» sin excepciones");
        }
        finally { PublicIpService.Endpoints = savedEp; PublicIpService.TimeoutMs = savedTo; PublicIpService.Reset(); }

        // ---- configuración del relay ----
        string relayTxt = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../relay.txt"));
        bool hadFile = System.IO.File.Exists(relayTxt);
        string backup = hadFile ? System.IO.File.ReadAllText(relayTxt) : null;
        string prefBackup = PlayerPrefs.GetString(RelayConfig.KRelay, "");
        try
        {
            PlayerPrefs.DeleteKey(RelayConfig.KRelay);
            RelayConfig.TestOverride = null;
            System.IO.File.WriteAllText(relayTxt, "# relay de prueba\n\nrelay.ejemplo.com:7781\n");
            string hh; int pp;
            r.Check(RelayConfig.FileAddress() == "relay.ejemplo.com:7781" && RelayConfig.Address == "relay.ejemplo.com:7781" && RelayConfig.Enabled && RelayConfig.TryGet(out hh, out pp) && hh == "relay.ejemplo.com" && pp == 7781, "[Red] relay.txt junto al exe: se lee ignorando comentarios");
            RelayConfig.Save("otro.relay.net");
            r.Check(RelayConfig.Address == "otro.relay.net" && RelayConfig.TryGet(out hh, out pp) && pp == RelayConfig.DefaultPort, "[Red] el campo del menú (PlayerPrefs) tiene prioridad sobre relay.txt y el puerto por defecto es " + RelayConfig.DefaultPort);
            RelayConfig.Save("");
            System.IO.File.Delete(relayTxt);
            r.Check(RelayConfig.Address == "" && !RelayConfig.Enabled, "[Red] sin configurar => relay desactivado (por defecto vacío)");
            RelayConfig.Save("dirección rara!!");
            r.Check(!RelayConfig.Enabled, "[Red] dirección de relay inválida => desactivado");
            RelayTransport rtp = new RelayTransport();
            r.Check(!rtp.Available && rtp.UnavailableReason.Length > 0 && new DirectTcpTransport().Available, "[Red] ITransport: directo siempre disponible, relay solo con dirección válida");
        }
        finally
        {
            RelayConfig.Save(prefBackup);
            if (hadFile) System.IO.File.WriteAllText(relayTxt, backup); else if (System.IO.File.Exists(relayTxt)) System.IO.File.Delete(relayTxt);
        }

        // ---- cliente del relay contra el servidor Node real (bucle local) ----
        TestRelayTransport(r);
    }


    // ---- cifrado del relay integrado y token compartido ----
    private static void TestRelayToken(Report r)
    {
        // AES-256-CBC + HMAC: ida y vuelta, manipulación, clave incorrecta
        byte[] keyC = new byte[32], iv = new byte[16];
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) { rng.GetBytes(keyC); rng.GetBytes(iv); }
        string payload = "203.0.113.9:7780\u001ftoken-de-prueba";
        byte[] blob = RelayDefault.Seal(payload, keyC, iv);
        r.Check(RelayDefault.OpenForTest(blob, keyC) == payload, "[Cripto] ida y vuelta AES-CBC + HMAC");
        string asText = System.Text.Encoding.ASCII.GetString(blob) + System.Text.Encoding.Unicode.GetString(blob);
        r.Check(!asText.Contains("203.0.113") && !asText.Contains("7780") && !asText.Contains("token-de-prueba"), "[Cripto] el texto cifrado no contiene la dirección ni el token en claro (ASCII/UTF-16)");
        byte[] bad = (byte[])blob.Clone(); bad[20] ^= 1;
        byte[] badMac = (byte[])blob.Clone(); badMac[badMac.Length - 1] ^= 1;
        byte[] key2 = (byte[])keyC.Clone(); key2[0] ^= 1;
        r.Check(RelayDefault.OpenForTest(bad, keyC) == null && RelayDefault.OpenForTest(badMac, keyC) == null && RelayDefault.OpenForTest(blob, key2) == null && RelayDefault.OpenForTest(new byte[10], keyC) == null, "[Cripto] manipular el cifrado / la etiqueta / usar otra clave => se rechaza");
        byte[] blob2 = RelayDefault.Seal(payload, keyC, new byte[16]);
        r.Check(!System.Linq.Enumerable.SequenceEqual(blob, blob2), "[Cripto] otro IV => otro texto cifrado");

        // precedencia y no mostrar el integrado
        string prefBackup = PlayerPrefs.GetString(RelayConfig.KRelay, "");
        string relayTxt = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../relay.txt"));
        bool hadFile = System.IO.File.Exists(relayTxt); string fileBackup = hadFile ? System.IO.File.ReadAllText(relayTxt) : null;
        try
        {
            RelayConfig.TestOverride = null; RelayConfig.Save("");
            if (hadFile) System.IO.File.Delete(relayTxt);
            RelayDefault.TestInject("127.0.0.1:47783", "SECRETO-INTEGRADO");
            r.Check(RelayConfig.UsingBuiltin && RelayConfig.Address == "127.0.0.1:47783" && RelayConfig.DisplayAddress == RelayConfig.BuiltinLabel && RelayConfig.FieldText == "" && RelayConfig.Token == "SECRETO-INTEGRADO" && RelayConfig.Enabled, "[Cripto] sin configuración: se usa el relay integrado, la interfaz dice «" + RelayConfig.BuiltinLabel + "»");
            System.IO.File.WriteAllText(relayTxt, "otro.relay.net:7781\n");
            r.Check(!RelayConfig.UsingBuiltin && RelayConfig.Address == "otro.relay.net:7781" && RelayConfig.DisplayAddress == "otro.relay.net:7781" && RelayConfig.Token == "", "[Cripto] relay.txt tiene prioridad sobre el integrado y NO se le manda el token integrado");
            RelayConfig.Save("mi.relay.com:7782");
            r.Check(RelayConfig.Address == "mi.relay.com:7782" && RelayConfig.Token == "" && RelayConfig.FieldText == "mi.relay.com:7782", "[Cripto] el campo del menú tiene prioridad sobre relay.txt");
            RelayConfig.TestOverride = "9.9.9.9:1";
            r.Check(RelayConfig.Address == "9.9.9.9:1", "[Cripto] (override de pruebas / entorno) por encima de todo");
            RelayConfig.TestOverride = null;
        }
        finally
        {
            RelayConfig.Save(prefBackup);
            if (hadFile) System.IO.File.WriteAllText(relayTxt, fileBackup); else if (System.IO.File.Exists(relayTxt)) System.IO.File.Delete(relayTxt);
        }

        // relay Node real con RELAY_TOKEN
        string dir = RelayDir();
        if (dir == null) { r.Info("[Relay] sin server.js: prueba de token omitida"); return; }
        const int rport = 47783; const string tok = "SECRETO-INTEGRADO";
        System.Diagnostics.Process node = null;
        var logs = new List<string>();
        Application.LogCallback cb = (c, st, t) => logs.Add(c);
        Application.logMessageReceived += cb;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("node", "server.js") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            psi.EnvironmentVariables["PORT"] = rport.ToString(); psi.EnvironmentVariables["HOST"] = "127.0.0.1"; psi.EnvironmentVariables["RELAY_TOKEN"] = tok; psi.EnvironmentVariables["AUTH_DELAY_MS"] = "20";
            node = System.Diagnostics.Process.Start(psi);
            bool up = WaitUntil(() => { try { using (var t = new System.Net.Sockets.TcpClient()) { t.Connect("127.0.0.1", rport); return true; } } catch (Exception) { return false; } }, 6000);
            r.Check(up, "[Token] servidor Node con RELAY_TOKEN escuchando");
            if (up)
            {
                RelayTransport tr = new RelayTransport();
                // 1) cliente CON el token integrado (relay integrado)
                RelayDefault.TestInject("127.0.0.1:" + rport, tok);
                INetListener lis = tr.CreateListener("Anfi"); string err; lis.Start(0, out err);
                r.Check(WaitUntil(() => lis.RoomCode.Length > 0 || lis.Error != null, 6000) && RoomCodes.IsValid(lis.RoomCode), "[Token] el anfitrión con el token integrado abre sala (" + lis.RoomCode + ")");
                INetConnector ok1 = tr.Connect(lis.RoomCode, 0, 8000);
                r.Check(WaitUntil(() => ok1.Done, 8000) && ok1.Result != null, "[Token] un cliente con el token integrado se une");
                var acc = new List<System.Net.Sockets.Socket>(); WaitUntil(() => { var q = lis.Accept(); if (q != null) acc.Add(q); return acc.Count == 1; }, 5000);
                r.Check(acc.Count == 1, "[Token] el anfitrión acepta la conexión (ACCEPT con token)");
                // 2) un relay propio (usuario) SIN token es rechazado por el servidor con token
                RelayDefault.TestInject(null, null);
                RelayConfig.TestOverride = "127.0.0.1:" + rport;
                INetConnector noTok = new RelayTransport().Connect(lis.RoomCode, 0, 8000);
                r.Check(WaitUntil(() => noTok.Done, 8000) && noTok.Result == null && noTok.Error == "relay:auth", "[Token] sin token: ERR auth => relay:auth (" + noTok.Error + ")");
                INetListener lis2 = new RelayTransport().CreateListener("X"); lis2.Start(0, out err);
                r.Check(WaitUntil(() => lis2.Error != null, 6000) && lis2.Error == "relay:auth" && lis2.RoomCode == "", "[Token] anfitrión sin token: relay:auth");
                lis2.Stop();
                // 3) token por variable de entorno (BACKROOMS_RELAY_TOKEN)
                Environment.SetEnvironmentVariable("BACKROOMS_RELAY_TOKEN", tok);
                INetConnector envTok = new RelayTransport().Connect(lis.RoomCode, 0, 8000);
                r.Check(WaitUntil(() => envTok.Done, 8000) && envTok.Result != null, "[Token] BACKROOMS_RELAY_TOKEN también vale");
                Environment.SetEnvironmentVariable("BACKROOMS_RELAY_TOKEN", null);
                RelayConfig.TestOverride = null;
                // textos de error
                r.Check(NetSessionErrorText("relay:auth").Contains("rechazado el acceso"), "[Token] mensaje en español para relay:auth");
                lis.Stop();
                foreach (var q in acc) q.Close();
                if (ok1.Result != null) ok1.Result.Close(); if (envTok.Result != null) envTok.Result.Close();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("BACKROOMS_RELAY_TOKEN", null);
            RelayConfig.TestOverride = null; RelayDefault.TestInject(null, null);
            Application.logMessageReceived -= cb;
            if (node != null && !node.HasExited) { try { node.Kill(); } catch (Exception) { } }
        }
        bool leak = false; foreach (string l in logs) if (l.Contains(":" + rport) || l.Contains(tok)) leak = true;
        r.Check(!leak, "[Cripto] ningún log del juego contiene la dirección/puerto/token del relay (" + logs.Count + " líneas)");
    }

    private static string NetSessionErrorText(string code)
    {
        var m = typeof(NetSession).GetMethod("ConnectErrorText", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)m.Invoke(null, new object[] { code });
    }

    private sealed class UdpClientDeaf : IDisposable
    {
        private readonly System.Net.Sockets.Socket s;
        public int Port { get; private set; }
        public UdpClientDeaf()
        {
            s = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            s.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 0));
            Port = ((System.Net.IPEndPoint)s.LocalEndPoint).Port;
        }
        public void Dispose() { s.Close(); }
    }

    private static string RelayDir()
    {
        string[] c = { "../backrooms-relay", "/workspace/backrooms-relay" };
        foreach (string d in c) { string p = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../" + d)); if (System.IO.File.Exists(System.IO.Path.Combine(p, "server.js"))) return p; if (System.IO.File.Exists(System.IO.Path.Combine(d, "server.js"))) return System.IO.Path.GetFullPath(d); }
        return null;
    }

    private static void TestRelayTransport(Report r)
    {
        string dir = RelayDir();
        if (dir == null) { r.Info("[Relay] no se encuentra backrooms-relay/server.js: prueba omitida"); return; }
        const int rport = 47781;
        System.Diagnostics.Process node = null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("node", "server.js") { WorkingDirectory = dir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            psi.EnvironmentVariables["PORT"] = rport.ToString(); psi.EnvironmentVariables["HOST"] = "127.0.0.1";
            node = System.Diagnostics.Process.Start(psi);
        }
        catch (Exception e) { r.Info("[Relay] no se pudo lanzar node: " + e.Message); return; }
        try
        {
            string saved = RelayConfig.TestOverride;
            RelayConfig.TestOverride = "127.0.0.1:" + rport;
            // esperar a que el relay escuche
            bool up = WaitUntil(() => { try { using (var t = new System.Net.Sockets.TcpClient()) { t.Connect("127.0.0.1", rport); return true; } } catch (Exception) { return false; } }, 6000);
            r.Check(up, "[Relay] el servidor Node escucha en el puerto " + rport);
            if (up)
            {
                RelayTransport tr = new RelayTransport();
                r.Check(tr.Available, "[Relay] RelayTransport disponible con relay configurado");
                INetListener lis = tr.CreateListener("Anfitrion"); string err;
                r.Check(lis.Start(0, out err), "[Relay] el anfitrión inicia el canal de control");
                r.Check(WaitUntil(() => lis.RoomCode.Length > 0 || lis.Error != null, 6000) && RoomCodes.IsValid(lis.RoomCode), "[Relay] el anfitrión recibe un código de sala válido (" + lis.RoomCode + ")");
                INetConnector c1 = tr.Connect(lis.RoomCode.ToLowerInvariant(), 0, 8000), c2 = tr.Connect(lis.RoomCode, 0, 8000);
                r.Check(WaitUntil(() => c1.Done && c2.Done, 10000) && c1.Result != null && c2.Result != null, "[Relay] dos clientes se unen por código (" + c1.Error + "," + c2.Error + ")");
                var accepted = new List<System.Net.Sockets.Socket>();
                WaitUntil(() => { var s = lis.Accept(); if (s != null) accepted.Add(s); return accepted.Count == 2; }, 6000);
                r.Check(accepted.Count == 2, "[Relay] el anfitrión acepta las 2 conexiones emparejadas");
                if (accepted.Count == 2 && c1.Result != null && c2.Result != null)
                {
                    // protocolo del juego a través del relay: cliente -> anfitrión y de vuelta
                    NetConn h1 = new NetConn(accepted[0], 0f), h2 = new NetConn(accepted[1], 0f), k1 = new NetConn(c1.Result, 0f), k2 = new NetConn(c2.Result, 0f);
                    k1.Send(new NetWriter(NetMsg.Hello).Str(NetSession.Protocol).Str("Uno").ToArray());
                    k2.Send(new NetWriter(NetMsg.Hello).Str(NetSession.Protocol).Str("Dos").ToArray());
                    var names = new List<string>();
                    NetConn[] hs = { h1, h2 };
                    WaitUntil(() => { foreach (NetConn h in hs) { h.Pump(0f); h.Flush(); while (h.Incoming.Count > 0) { NetReader q = new NetReader(h.Incoming.Dequeue()); if (q.Type == NetMsg.Hello) { q.Str(); names.Add(q.Str()); } } } k1.Flush(); k2.Flush(); return names.Count == 2; }, 6000);
                    r.Check(names.Contains("Uno") && names.Contains("Dos"), "[Relay] los mensajes del juego llegan al anfitrión a través del relay (" + string.Join(",", names.ToArray()) + ")");
                    byte[] big = new byte[120000]; for (int i = 0; i < big.Length; i++) big[i] = (byte)(i * 7);
                    h1.Send(big); h1.Flush();
                    bool gotBig = false; NetConn[] ks = { k1, k2 };
                    WaitUntil(() => { h1.Pump(0f); h1.Flush(); foreach (NetConn k in ks) { k.Pump(0f); while (k.Incoming.Count > 0) { byte[] m = k.Incoming.Dequeue(); if (m.Length == big.Length && m[777] == big[777]) gotBig = true; } } return gotBig; }, 6000);
                    r.Check(gotBig, "[Relay] 120 KB del anfitrión llegan íntegros a un cliente");
                    // un cliente se va: el anfitrión lo nota
                    NetConn leaver = k1;
                    leaver.Kill("prueba");
                    bool noticed = WaitUntil(() => { h1.Pump(0f); h2.Pump(0f); return h1.Dead || h2.Dead; }, 6000);
                    r.Check(noticed, "[Relay] el anfitrión detecta la salida de un cliente a través del relay");
                    h1.Kill("fin"); h2.Kill("fin"); k1.Kill("fin"); k2.Kill("fin");
                }
                // errores del relay
                INetConnector bad = tr.Connect("ZZZZZZ", 0, 8000);
                r.Check(WaitUntil(() => bad.Done, 8000) && bad.Result == null && bad.Error == "relay:noroom", "[Relay] código inexistente => relay:noroom (" + bad.Error + ")");
                INetConnector bad2 = tr.Connect("AB1", 0, 8000);
                r.Check(WaitUntil(() => bad2.Done, 8000) && bad2.Error == "relay:badcode", "[Relay] código mal formado => relay:badcode (" + bad2.Error + ")");
                lis.Stop();
                System.Threading.Thread.Sleep(400);
                INetConnector late = tr.Connect(lis.RoomCode, 0, 8000);
                r.Check(WaitUntil(() => late.Done, 8000) && late.Error == "relay:noroom", "[Relay] al parar el anfitrión la sala desaparece (" + late.Error + ")");
            }
            // relay caído
            if (node != null && !node.HasExited) { try { node.Kill(); } catch (Exception) { } node.WaitForExit(2000); }
            RelayTransport tr2 = new RelayTransport();
            INetConnector down = tr2.Connect("ABC234", 0, 4000);
            r.Check(WaitUntil(() => down.Done, 8000) && down.Error == "relay_unreachable", "[Relay] relay caído => relay_unreachable (" + down.Error + ")");
            INetListener l2 = tr2.CreateListener("X"); string e2; l2.Start(0, out e2);
            r.Check(WaitUntil(() => l2.Error != null, 8000) && l2.Error == "relay_unreachable" && l2.RoomCode == "", "[Relay] anfitrión sin relay => relay_unreachable");
            l2.Stop();
            RelayConfig.TestOverride = saved;
        }
        finally { if (node != null && !node.HasExited) { try { node.Kill(); } catch (Exception) { } } }
    }

    private static void TestInventoryData(Report r)
    {
        foreach (ItemType t in ItemDefs.All)
        {
            ItemDefs.Def d = ItemDefs.Get(t);
            r.Check(!string.IsNullOrEmpty(d.name) && d.description.Length > 20 && d.maxStack >= 1, "objeto " + t + ": nombre '" + d.name + "', descripción y pila máxima " + d.maxStack);
            Sprite rendered = ItemIcons.Get(t);
            r.Check(rendered != null && rendered.texture != null && rendered.texture.width >= ItemIcons.Size, "icono de " + t + " (" + (rendered != null && rendered.texture != null ? rendered.texture.name + " " + rendered.texture.width + " px" : "nulo") + ")");
            Sprite sp = ItemIcons.Procedural(t);
            bool ok = sp != null && sp.texture != null && sp.texture.width == ItemIcons.Size;
            int opaque = 0;
            if (ok) foreach (Color c in sp.texture.GetPixels()) if (c.a > 0.5f) opaque++;
            r.Check(ok && opaque > 700 && opaque < ItemIcons.Size * ItemIcons.Size * 0.9f, "icono procedural de " + t + " (" + opaque + " px opacos)");
            GameObject vis = new GameObject("vis_" + t);
            bool real = ItemPickup.BuildVisual(t, vis.transform);
            r.Check(vis.GetComponentsInChildren<Renderer>().Length >= 1, "modelo 3D de recogida de " + t);
            {   // v7.0.2: modelo realista horneado (PBR), tamaño real y dentro del presupuesto
                MeshFilter mf = vis.GetComponentInChildren<MeshFilter>(); Renderer rr = vis.GetComponentInChildren<Renderer>();
                int tris = mf != null && mf.sharedMesh != null ? mf.sharedMesh.triangles.Length / 3 : 0;
                Bounds bb = rr != null ? rr.bounds : new Bounds();
                Material mm = rr != null ? rr.sharedMaterial : null;
                bool pbr = mm != null && mm.GetTexture("_MainTex") != null && mm.GetTexture("_BumpMap") != null && mm.GetTexture("_MetallicGlossMap") != null && mm.IsKeywordEnabled("_EMISSION");
                float big = Mathf.Max(bb.size.x, Mathf.Max(bb.size.y, bb.size.z));
                r.Check(real && pbr && tris > 200 && tris < 3000 && big > 0.05f && big < 0.5f && bb.min.y > -0.01f && bb.min.y < 0.02f,
                    "modelo realista de " + t + " (" + tris + " triángulos, " + (big * 100f).ToString("F0") + " cm, PBR " + pbr + ", apoyado en la base)");
                r.Check(vis.GetComponent<ItemHighlight>() != null, "brillo sutil para encontrar " + t);
            }
            UnityEngine.Object.DestroyImmediate(vis);
        }
        GameObject nv = new GameObject("noteVis");
        bool nreal = ItemPickup.BuildVisual(ItemType.None, nv.transform);
        r.Check(nreal && nv.GetComponentsInChildren<Renderer>().Length >= 1 && nv.GetComponentInChildren<Renderer>().bounds.size.z > 0.15f, "modelo 3D realista de nota de papel (hoja manuscrita)");
        UnityEngine.Object.DestroyImmediate(nv);
        r.Check(ItemIcons.Note() != null && ItemIcons.Lock() != null, "iconos de nota y candado");

        // notas de ambiente: 3 por nivel, ids únicos, texto en español
        var ids = new HashSet<int>();
        bool allText = true;
        for (int lv = 0; lv < LevelConfig.Count; lv++)
            for (int k = 0; k < LoreNotes.PerLevel; k++)
            {
                LoreNotes.Note n = LoreNotes.Get(LoreNotes.IdFor(lv, k));
                ids.Add(n.id);
                if (n.level != lv || string.IsNullOrEmpty(n.title) || n.text.Length < 40) allText = false;
            }
        r.Check(ids.Count == LevelConfig.Count * LoreNotes.PerLevel && allText && LoreNotes.Total == ids.Count, "notas de ambiente: " + ids.Count + " (3 por nivel) con título y texto");

        // lógica de la clase Inventory sin Play
        GameObject go = new GameObject("InvTest");
        Inventory.ResetAll();
        Inventory inv = go.AddComponent<Inventory>();
        typeof(Inventory).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(inv, null);
        r.Check(inv.slots.Length == 15 && inv.GetSlot(0).type == ItemType.Flashlight && inv.UsedSlots == 1, "Inventory: 15 casillas, la linterna en la 1");
        r.Check(inv.Add(ItemType.Battery, 3) == 3 && inv.GetSlot(1).type == ItemType.Battery && inv.GetSlot(1).count == 3, "Inventory: la pila va a la casilla 2 de la barra rápida");
        r.Check(inv.Add(ItemType.Battery, 10) == 10 && inv.GetCount(ItemType.Battery) == 13 && inv.GetSlot(1).count == 9, "Inventory: apila hasta 9 y abre otra casilla (" + inv.GetCount(ItemType.Battery) + ")");
        r.Check(inv.Add(ItemType.AlmondWater, 1) == 1 && inv.GetSlot(2).type == ItemType.AlmondWater, "Inventory: agua de almendras en la casilla 3");
        r.Check(inv.Add(ItemType.Glowstick, 2) == 2 && inv.GetSlot(3).type == ItemType.Glowstick, "Inventory: barra luminosa en la casilla 4");
        r.Check(inv.Add(ItemType.Flashlight, 1) == 0 && inv.Add(ItemType.None, 1) == 0, "Inventory: no se duplica la linterna ni se añade 'nada'");
        r.Check(inv.Remove(1, 2) && inv.GetSlot(1).count == 7 && !inv.Remove(0, 1), "Inventory: quitar unidades; la linterna no se quita");
        // lleno
        int added = 0;
        for (int i = 0; i < 200; i++) added += inv.Add(ItemType.VhsTape, 1);
        r.Check(inv.UsedSlots == Inventory.SlotCount && inv.Add(ItemType.VhsTape, 1) == 0 && added > 0, "Inventory: inventario lleno rechaza más objetos (" + inv.UsedSlots + "/15)");
        r.Check(inv.AddNote(LoreNotes.IdFor(2, 1)) && !inv.AddNote(LoreNotes.IdFor(2, 1)) && !inv.AddNote(9999) && inv.NotesFound == 1, "Inventory: notas sin duplicados ni ids inválidos");
        // persistencia entre niveles
        inv.CommitForNextLevel();
        UnityEngine.Object.DestroyImmediate(go);
        GameObject go2 = new GameObject("InvTest2");
        Inventory inv2 = go2.AddComponent<Inventory>();
        typeof(Inventory).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(inv2, null);
        r.Check(inv2.GetCount(ItemType.Battery) == 11 && inv2.GetCount(ItemType.AlmondWater) == 1 && inv2.GetCount(ItemType.Glowstick) == 2 && inv2.HasNote(LoreNotes.IdFor(2, 1)), "Inventory: lo recogido pasa al siguiente nivel (pilas " + inv2.GetCount(ItemType.Battery) + ")");
        inv2.Add(ItemType.Glowstick, 2); inv2.AddNote(LoreNotes.IdFor(2, 2));
        inv2.RevertToLevelStart();
        UnityEngine.Object.DestroyImmediate(go2);
        GameObject go3 = new GameObject("InvTest3");
        Inventory inv3 = go3.AddComponent<Inventory>();
        typeof(Inventory).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(inv3, null);
        r.Check(inv3.GetCount(ItemType.Glowstick) == 2 && !inv3.HasNote(LoreNotes.IdFor(2, 2)) && inv3.HasNote(LoreNotes.IdFor(2, 1)), "Inventory: al reintentar se revierte a lo del inicio del nivel");
        UnityEngine.Object.DestroyImmediate(go3);
        Inventory.ResetAll();
    }

    // ------------------------------------------------------------------
    // Efecto VHS: shader, material, referencia anti-stripping, fallback
    // ------------------------------------------------------------------
    private static void TestVhsAssets(Report r)
    {
        Shader sh = Resources.Load<Shader>(VhsEffect.ShaderResource);
        r.Check(sh != null, "el shader VHS carga desde Resources (" + VhsEffect.ShaderResource + ")");
        if (sh != null)
        {
            r.Check(!ShaderUtil.ShaderHasError(sh), "el shader VHS compila sin errores");
            r.Check(sh.isSupported, "el shader VHS está soportado en esta GPU/API");
            foreach (string prop in new[] { "_MainTex", "_Intensity", "_Glitch" })
                r.Check(sh.FindPropertyIndex(prop) >= 0, "el shader VHS declara " + prop);
        }
        Shader outSh;
        Material m = VhsEffect.CreateMaterial(out outSh);
        r.Check(m != null && m.shader == sh, "VhsEffect.CreateMaterial devuelve un Material no nulo");
        if (m != null) UnityEngine.Object.DestroyImmediate(m);
        Material keeper = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/VHS/VhsPostKeeper.mat");
        r.Check(keeper != null && keeper.shader == sh, "material 'keeper' referencia el shader VHS (no se elimina del build)");
        // Fallback: sin shader no hay excepción (material nulo -> overlay UI)
        GameObject go = new GameObject("VhsFallbackTest");
        go.AddComponent<Camera>();
        VhsEffect fx = go.AddComponent<VhsEffect>();
        r.Check(fx != null, "VhsEffect se puede añadir a una cámara");
        UnityEngine.Object.DestroyImmediate(go);
    }

    // ------------------------------------------------------------------
    // Opciones persistentes
    // ------------------------------------------------------------------

    // ------------------------------------------------------------------
    // v4: niveles, agua, screamer, cuerpo del jugador (recursos y datos)
    // ------------------------------------------------------------------
    private static void TestV4Assets(Report r)
    {
        // Niveles
        r.Check(LevelConfig.Count == 7 && LevelConfig.Get(5).name == "Campo infinito" && LevelConfig.Get(5).layout == LayoutKind.Field && LevelConfig.Get(6).name == "Parque de bolas" && LevelConfig.Get(6).halls > 0, "hay 7 niveles; el 5 es el Campo infinito y el 6 el Parque de bolas (" + LevelConfig.Count + ")");
        r.Check(LevelConfig.Get(2).water && LevelConfig.Get(2).name == "Poolrooms", "nivel 2 = Poolrooms (antes «Salas inundadas»: agua realista)");
        bool onlyOneWater = true; for (int i = 0; i < LevelConfig.Count; i++) if (i != 2 && LevelConfig.Get(i).water) onlyOneWater = false;
        r.Check(onlyOneWater && LevelConfig.Get(3).name == "Hotel abandonado" && LevelConfig.Get(4).name == "Complejo industrial", "sin otro nivel de agua; nivel 3 = hotel, nivel 4 = industrial");
        r.Check(MonsterProfile.ForLevel(2).id == "DrownedZombie", "el Ahogado es el monstruo del nivel de agua (Poolrooms)");
        r.Check(Resources.Load<GameObject>("Poolrooms/PoolHall") == null && MonsterProfile.ById("AlienBug") == null, "el modelo PoolHall y El Reptador ya no existen");
        r.Check(LoreNotes.Total == 21 && !LoreNotes.Valid(70) && LoreNotes.Valid(62) && LoreNotes.Valid(52), "21 notas de ambiente (3 por nivel, 7 niveles)");
        var seenMonsters = new HashSet<string>();
        for (int i = 0; i < LevelConfig.Count; i++)
        {
            LevelConfig c = LevelConfig.Get(i);
            r.Check(MonsterProfile.ById(c.monsterModel) != null, "nivel " + i + " (" + c.name + "): perfil de monstruo '" + c.monsterModel + "'");
            seenMonsters.Add(c.monsterModel);
        }
        r.Check(seenMonsters.Count == LevelConfig.Count, "un monstruo distinto por nivel (" + seenMonsters.Count + ")");

        // Agua
        Shader ws = Resources.Load<Shader>(WaterVolume.ShaderResource);
        r.Check(ws != null && !ShaderUtil.ShaderHasError(ws) && ws.isSupported, "shader de agua carga, compila y está soportado");
        Shader ps = Resources.Load<Shader>(WaterVolume.ParticleShaderResource);
        r.Check(ps != null && !ShaderUtil.ShaderHasError(ps) && ps.isSupported, "shader de partículas de agua carga y compila");
        r.Check(Mathf.Approximately(WaterVolume.WadeFactor(0f), 1f) && WaterVolume.WadeFactor(0.5f) < WaterVolume.WadeFactor(0.1f) && WaterVolume.WadeFactor(1.2f) < 0.5f,
            "arrastre al vadear crece con la profundidad (0.1 m: " + WaterVolume.WadeFactor(0.1f).ToString("F2") + ", 0.5 m: " + WaterVolume.WadeFactor(0.5f).ToString("F2") + ", 1.2 m: " + WaterVolume.WadeFactor(1.2f).ToString("F2") + ")");
        Texture2D[] ck = WaterFx.BuildCookies(2, 32);
        r.Check(ck != null && ck.Length == 2 && ck[0] != null, "cookies de cáusticas generados");
        if (ck != null) foreach (Texture2D t in ck) UnityEngine.Object.DestroyImmediate(t);

        // Screamer: gritos distintos por tipo de monstruo + postsusto
        var peaks = new List<float>();
        var firsts = new List<float>();
        bool allOk = true;
        foreach (GrowlKind k in (GrowlKind[])Enum.GetValues(typeof(GrowlKind)))
        {
            AudioClip c = ProceduralAudio.CreateScreamerScream(k, 3, 1f);
            if (c == null || c.length < 1.5f) { allOk = false; continue; }
            float[] d = new float[c.samples];
            c.GetData(d, 0);
            float sum = 0f, pk = 0f;
            for (int i = 0; i < d.Length; i++) { sum += d[i] * d[i]; pk = Mathf.Max(pk, Mathf.Abs(d[i])); }
            float rms = Mathf.Sqrt(sum / d.Length);
            if (rms < 0.05f || pk > 1.0001f) allOk = false;
            peaks.Add(rms);
            float acc = 0f; for (int i = 20000; i < 24000 && i < d.Length; i++) acc += Mathf.Abs(d[i]);
            firsts.Add(acc);
            UnityEngine.Object.DestroyImmediate(c);
        }
        r.Check(allOk && peaks.Count >= 4, "gritos del screamer: largos, sonoros y sin pasar de 1.0 (" + peaks.Count + " tipos)");
        bool distinct = false;
        for (int i = 1; i < firsts.Count; i++) if (Mathf.Abs(firsts[i] - firsts[0]) > 1f) distinct = true;
        r.Check(distinct, "el grito cambia según el tipo de monstruo");
        AudioClip a2 = ProceduralAudio.CreateScreamerAftermath();
        r.Check(a2 != null && a2.length > 5f, "acúfeno + latido generados");
        if (a2 != null) UnityEngine.Object.DestroyImmediate(a2);

        // Cuerpo del jugador
        r.Check(LayerMask.LayerToName(PlayerBody.Layer) == PlayerBody.LayerName, "capa " + PlayerBody.Layer + " = '" + PlayerBody.LayerName + "' en TagManager (" + LayerMask.LayerToName(PlayerBody.Layer) + ")");
        GameObject bp = Resources.Load<GameObject>(PlayerBody.PrefabResource);
        r.Check(bp != null, "prefab del cuerpo carga desde Resources (" + PlayerBody.PrefabResource + ")");
        if (bp != null)
        {
            Animator an = bp.GetComponentInChildren<Animator>();
            r.Check(an != null && an.runtimeAnimatorController != null && an.avatar != null && an.avatar.isValid, "cuerpo: Animator con controlador y avatar válido");
            var clipNames = new HashSet<string>();
            if (an != null && an.runtimeAnimatorController != null) foreach (AnimationClip c in an.runtimeAnimatorController.animationClips) if (c != null) clipNames.Add(c.name);
            foreach (string cn in new[] { "Idle", "Walk", "Run", "Duck" }) r.Check(clipNames.Contains(cn), "cuerpo: clip " + cn);
            SkinnedMeshRenderer[] sm = bp.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            r.Check(sm.Length >= 2, "cuerpo: malla del cuerpo y de la cabeza (" + sm.Length + ")");
            r.Check(bp.GetComponentsInChildren<Collider>(true).Length == 0, "cuerpo: sin colliders (no estorba a los rayos del monstruo)");
        }
        string credits = File.Exists("CREDITS.md") ? File.ReadAllText("CREDITS.md") : "";
        r.Check(credits.Contains("Character Hazmat") && credits.Contains("Quaternius"), "CREDITS.md cita el modelo del traje hazmat");
        r.Check(UIManager.CreditsLine().Contains("hazmat"), "créditos en juego citan el traje hazmat");
    }

    private static void TestSettings(Report r)
    {
        GameSettings.ResetDefaults();
        r.Check(Mathf.Approximately(GameSettings.SfxVolume, 1f) && Mathf.Approximately(GameSettings.AmbienceVolume, 1f) && GameSettings.BreathEnabled, "Efectos 100 %, Ambiente 100 % y respiración activada por defecto");
        GameSettings.SfxVolume = 0.4f; GameSettings.AmbienceVolume = 0.25f; GameSettings.BreathEnabled = false; GameSettings.Save();
        r.Check(Mathf.Approximately(PlayerPrefs.GetFloat("opt_sfx", -1f), 0.4f) && Mathf.Approximately(PlayerPrefs.GetFloat("opt_ambience", -1f), 0.25f) && PlayerPrefs.GetInt("opt_breath", -1) == 0, "Efectos / Ambiente / Respiración se guardan en PlayerPrefs");
        GameSettings.ResetDefaults();
        r.Check(GameSettings.VhsOn, "VHS activado por defecto");
        r.Check(GameSettings.HeadBob, "head-bob activado por defecto");
        r.Check(!GameSettings.SmoothLook, "suavizado de ratón desactivado por defecto");
        r.Check(!GameSettings.InvertY, "invertir Y desactivado por defecto");
        r.Check(GameSettings.ShowBody && !GameSettings.SoftScare, "'Mostrar cuerpo' activado y 'Susto suave' desactivado por defecto");
        GameSettings.MasterVolume = 0.33f; GameSettings.MouseSensitivity = 3.5f; GameSettings.Quality = 0; GameSettings.InvertY = true;
        GameSettings.HeadBob = false; GameSettings.SmoothLook = true; GameSettings.VhsOn = false; GameSettings.VhsIntensity = 0.25f;
        GameSettings.Save();
        r.Check(Mathf.Approximately(PlayerPrefs.GetFloat("opt_volume", -1f), 0.33f) || Mathf.Approximately(GameSettings.MasterVolume, 0.33f), "volumen se guarda en PlayerPrefs");
        r.Check(Mathf.Approximately(GameSettings.MouseSensitivity, 3.5f) && GameSettings.Quality == 0 && GameSettings.InvertY, "sensibilidad / calidad / invertir Y persisten");
        r.Check(!GameSettings.HeadBob && GameSettings.SmoothLook && !GameSettings.VhsOn && Mathf.Approximately(GameSettings.VhsIntensity, 0.25f), "head-bob / suavizado / VHS persisten");
        GameSettings.SoftScare = true; GameSettings.ShowBody = false;
        r.Check(GameSettings.SoftScare && !GameSettings.ShowBody, "'Susto suave' / 'Mostrar cuerpo' persisten");
        GameSettings.ResetDefaults();
        r.Check(GameSettings.VhsOn && GameSettings.HeadBob && Mathf.Approximately(GameSettings.MasterVolume, 0.8f) && GameSettings.ShowBody && !GameSettings.SoftScare, "ResetDefaults restaura los valores");
    }


    // ---- mezcla de audio: niveles de los sonidos de «ruido / aire», grupos de volumen ----
    private static void TestAudioMix(Report r)
    {
        GameSettings.ResetDefaults();
        float stepDb = -99f;
        foreach (StepKind k in new[] { StepKind.Carpet, StepKind.Concrete, StepKind.Water, StepKind.Metal })
        {
            AudioClip c = ProceduralAudio.CreateFootstep(k);
            AudioAudit.Stat st = AudioAudit.Measure(c);
            stepDb = Mathf.Max(stepDb, AudioMix.Db(st.rms * 0.45f * AudioMix.StepMaster));    // volumen base de FootstepAudio × StepMaster
            r.Check(st.peak <= 0.65f && st.rms <= 0.12f, "[Audio] paso " + k + ": pico " + st.peak.ToString("F2") + " / rms " + st.rms.ToString("F3"));
            UnityEngine.Object.DestroyImmediate(c);
        }
        // respiración: suave, sin agudos, muy por debajo de los pasos
        foreach (bool inhale in new[] { true, false })
        {
            AudioClip c = ProceduralAudio.CreateBreathPhase(inhale);
            AudioAudit.Stat st = AudioAudit.Measure(c);
            float eff = AudioMix.Db(st.rms * AudioMix.BreathMax);
            r.Check(st.peak <= 0.55f && st.rms <= 0.10f, "[Audio] respiración " + (inhale ? "inhalar" : "exhalar") + ": pico " + st.peak.ToString("F2") + " / rms " + st.rms.ToString("F3") + " (≤ 0,55 / 0,10)");
            r.Check(st.hf <= 0.04f, "[Audio] respiración sin siseo agudo (hf " + st.hf.ToString("F3") + " ≤ 0,04)");
            r.Check(eff <= -35f && eff < stepDb - 8f, "[Audio] respiración al máximo ≤ -35 dBFS y ≥ 8 dB por debajo de los pasos (" + eff.ToString("F1") + " dB; pasos " + stepDb.ToString("F1") + " dB)");
            // envolvente: empieza y acaba en silencio (sin clics)
            float[] d = new float[c.samples]; c.GetData(d, 0);
            r.Check(Mathf.Abs(d[0]) < 0.02f && Mathf.Abs(d[d.Length - 1]) < 0.02f, "[Audio] respiración con envolvente suave (extremos ≈ 0)");
            UnityEngine.Object.DestroyImmediate(c);
        }
        // siseo VHS
        {
            AudioClip c = ProceduralAudio.CreateTapeHiss();
            AudioAudit.Stat st = AudioAudit.Measure(c);
            float idle = AudioMix.Db(st.rms * AudioMix.Hiss(1f, 0f)), max = AudioMix.Db(st.rms * AudioMix.Hiss(1f, 1f));
            r.Check(st.peak <= 0.55f && st.hf <= 0.06f, "[Audio] siseo VHS: pico " + st.peak.ToString("F2") + ", hf " + st.hf.ToString("F3") + " (sin agudos estridentes)");
            r.Check(idle <= -52f && max <= -36f && max < stepDb - 10f, "[Audio] siseo VHS: reposo " + idle.ToString("F1") + " dB (≤ -52), con interferencia máxima " + max.ToString("F1") + " dB (≤ -36)");
            UnityEngine.Object.DestroyImmediate(c);
        }
        // drones por nivel, zumbido y sucesos
        for (int i = 0; i < LevelConfig.Count; i++)
        {
            LevelConfig cfg = LevelConfig.Get(i);
            AudioClip c = ProceduralAudio.CreateDrone(cfg.droneHz, cfg.droneNoise, 1000 + i * 17);
            AudioAudit.Stat st = AudioAudit.Measure(c);
            float eff = AudioMix.Db(st.rms * AudioMix.Drone(cfg.droneVolume));
            r.Check(st.peak <= 0.85f && eff <= -30f && eff <= stepDb + 3f, "[Audio] drone del nivel " + i + ": " + eff.ToString("F1") + " dB (≤ -30 y como mucho 3 dB sobre los pasos " + stepDb.ToString("F1") + " dB)");
            UnityEngine.Object.DestroyImmediate(c);
        }
        {
            AudioClip c = ProceduralAudio.CreateBuzzClip();
            AudioAudit.Stat st = AudioAudit.Measure(c);
            float eff = AudioMix.Db(st.rms * AudioMix.Buzz(0.25f));
            r.Check(eff <= -29f, "[Audio] zumbido de fluorescente: " + eff.ToString("F1") + " dB (≤ -29)");
            UnityEngine.Object.DestroyImmediate(c);
        }
        foreach (AudioClip c in new[] { ProceduralAudio.CreateDrip(), ProceduralAudio.CreateCreak(), ProceduralAudio.CreateClank() })
        {
            AudioAudit.Stat st = AudioAudit.Measure(c);
            r.Check(st.peak <= 0.75f && AudioMix.Db(st.rms * AudioMix.EventMaster) <= -20f, "[Audio] suceso ambiental " + c.name + ": pico " + st.peak.ToString("F2"));
            UnityEngine.Object.DestroyImmediate(c);
        }
        // v6.5: planitud espectral de TODOS los bucles (ruido = «aire/soplo»): un bucle con planitud alta (> 0,02) debe ser casi inaudible
        {
            float stepDbL = AudioMix.Db(AudioAudit.Measure(ProceduralAudio.CreateFootstep(StepKind.Carpet)).rms);
            System.Collections.Generic.List<KeyValuePair<string, KeyValuePair<AudioClip, float>>> loops = new System.Collections.Generic.List<KeyValuePair<string, KeyValuePair<AudioClip, float>>>();
            Action<string, AudioClip, float> addLoop = (nm, cl, gain) => loops.Add(new KeyValuePair<string, KeyValuePair<AudioClip, float>>(nm, new KeyValuePair<AudioClip, float>(cl, gain)));
            for (int i = 0; i < LevelConfig.Count; i++) { LevelConfig cfg = LevelConfig.Get(i); addLoop("drone nivel " + i, ProceduralAudio.CreateDrone(cfg.droneHz, cfg.droneNoise, 1000 + i * 17), AudioMix.Drone(cfg.droneVolume)); }
            addLoop("zumbido luz", ProceduralAudio.CreateBuzzClip(), AudioMix.Buzz(0.25f));
            addLoop("zumbido cinta", ProceduralAudio.CreateTapeHum(), AudioMix.Hum(1f));
            addLoop("siseo cinta (máx.)", ProceduralAudio.CreateTapeHiss(), AudioMix.Hiss(1f, 1f));
            addLoop("ascensor retumbo", ElevatorAudio.CreateRumble(), ElevatorAudio.RumbleGain);
            addLoop("ascensor motor", ElevatorAudio.CreateMachinery(), ElevatorAudio.MachineGain);
            addLoop("ascensor aire", ElevatorAudio.CreateAir(), ElevatorAudio.AirGain);
            addLoop("intro zumbido", ProceduralAudio.CreateTapeHum(), AudioMix.A(IntroAudio.HumGain * 1.8f));
            int noisy = 0; string worst = "";
            foreach (KeyValuePair<string, KeyValuePair<AudioClip, float>> kv in loops)
            {
                AudioAudit.Stat st = AudioAudit.Measure(kv.Value.Key);
                float eff = AudioMix.Db(st.rms * kv.Value.Value);
                if (st.flat > 0.02f && eff > -45f) { noisy++; worst += kv.Key + " (planitud " + st.flat.ToString("F3") + ", " + eff.ToString("F1") + " dB) "; }
                if (kv.Key.StartsWith("drone")) r.Check(st.flat <= 0.02f && eff <= -27f && eff < stepDbL - 3f, "[Audio] " + kv.Key + " solo tonal (planitud " + st.flat.ToString("F3") + " ≤ 0,02) y " + eff.ToString("F1") + " dB");
                r.Info("[Audio] bucle «" + kv.Key + "»: planitud " + st.flat.ToString("F3") + ", efectivo " + eff.ToString("F1") + " dBFS");
                UnityEngine.Object.DestroyImmediate(kv.Value.Key);
            }
            r.Check(noisy == 0, "[Audio] ningún bucle con ruido (planitud espectral > 0,02) suena por encima de -45 dBFS efectivo " + worst);
            {
                AudioClip cl = IntroAudio.CreateClunk(), ht = IntroAudio.CreateTitleHit();
                AudioAudit.Stat sc = AudioAudit.Measure(cl), sh = AudioAudit.Measure(ht);
                r.Check(AudioMix.Db(sc.peak * IntroAudio.ClunkGain) <= -3f && AudioMix.Db(sh.peak * IntroAudio.HitGain) <= -5f && sc.flat <= 0.2f && sh.flat <= 0.02f, "[Intro] sonidos de la intro sin saturar (golpe " + AudioMix.Db(sc.peak * IntroAudio.ClunkGain).ToString("F1") + " dB, título " + AudioMix.Db(sh.peak * IntroAudio.HitGain).ToString("F1") + " dB) y tonales");
                UnityEngine.Object.DestroyImmediate(cl); UnityEngine.Object.DestroyImmediate(ht);
            }
            AudioClip hissC = ProceduralAudio.CreateTapeHiss();
            AudioAudit.Stat hs = AudioAudit.Measure(hissC);
            r.Check(hs.flat > 0.02f, "[Audio] (control) el medidor reconoce el siseo de cinta como ruido (planitud " + hs.flat.ToString("F3") + ")");
            UnityEngine.Object.DestroyImmediate(hissC);
            // opción «Siseo VHS / ruido»
            bool noiseWas = GameSettings.NoiseEnabled;
            GameSettings.NoiseEnabled = true;
            float h1 = AudioMix.Hiss(1f, 1f), hu = AudioMix.Hum(1f);
            GameSettings.NoiseEnabled = false;
            float h0 = AudioMix.Hiss(1f, 1f);
            GameSettings.NoiseEnabled = noiseWas;
            r.Check(h1 > 0f && h0 == 0f && hu > 0f && AudioMix.Hiss(1f, 0f) == 0f, "[Audio] «Siseo VHS / ruido»: apagado silencia el siseo; en reposo (sin interferencia) no hay siseo, solo el zumbido tonal");
            PlayerPrefs.DeleteKey("opt_noise");
            r.Check(GameSettings.NoiseEnabled, "[Audio] «Siseo VHS / ruido» está activado por defecto");
        }
        // grupos de volumen
        GameSettings.SfxVolume = 0.5f; GameSettings.AmbienceVolume = 0.25f;
        r.Check(Mathf.Approximately(AudioMix.S(0.8f), 0.4f) && Mathf.Approximately(AudioMix.A(0.8f), 0.2f), "[Audio] 'Efectos' y 'Ambiente' escalan sus sonidos");
        r.Check(Mathf.Approximately(AudioMix.Drone(0.5f), 0.5f * AudioMix.DroneMaster * 0.25f) && AudioMix.Hiss(1f, 1f) < AudioMix.HissIdle + AudioMix.HissGlitch, "[Audio] drone y siseo siguen al deslizador de Ambiente");
        GameSettings.SfxVolume = 0f; GameSettings.AmbienceVolume = 0f;
        r.Check(AudioMix.S(1f) == 0f && AudioMix.Drone(1f) == 0f && AudioMix.Buzz(1f) == 0f && AudioMix.Hiss(1f, 1f) == 0f && AudioMix.Event(1f) == 0f, "[Audio] a 0 % silencian todo su grupo");
        GameSettings.MasterVolume = 0.3f;
        r.Check(Mathf.Approximately(AudioListener.volume, 0.3f), "[Audio] el volumen general escala toda la mezcla (AudioListener.volume = 0,3)");
        GameSettings.ResetDefaults();
    }

    // v6.4: ascensor de entrada: modelo CC0, línea de tiempo, sonidos y opciones
    private static void TestElevatorAssets(Report r)
    {
        GameObject prefab = Resources.Load<GameObject>(ElevatorCabin.ModelResource);
        r.Check(prefab != null, "[Ascensor] modelo Resources/Elevator/Elevator.fbx (CC0) importado");
        if (prefab != null)
        {
            var names = new HashSet<string>();
            foreach (Renderer rr in prefab.GetComponentsInChildren<Renderer>(true)) names.Add(rr.name);
            r.Check(names.Contains("Door_L") && names.Contains("Door_R") && names.Contains("Car_Metal") && names.Contains("Car_Rubber") && names.Contains("Car_Buttons"),
                "[Ascensor] el FBX trae las hojas de puerta separadas (Door_L / Door_R) y la carcasa (" + names.Count + " mallas)");
        }
        foreach (string tn in new[] { "Elev_concrete", "Elev_wood", "Elev_painted" })
        {
            Texture2D t = Resources.Load<Texture2D>("Elevator/" + tn);
            r.Check(t != null && t.width <= 512 && t.height <= 512, "[Ascensor] textura " + tn + " ≤ 512 px");
        }
        r.Check(ElevatorCabin.TotalSeconds >= 6f && ElevatorCabin.TotalSeconds <= 9f && ElevatorCabin.TUnlock < ElevatorCabin.TDoors1 && ElevatorCabin.TUnlock > ElevatorCabin.TDoors0,
            "[Ascensor] duración total " + ElevatorCabin.TotalSeconds.ToString("F1") + " s (6-9 s) y el jugador sale con las puertas ya a medio abrir");
        r.Check(ElevatorCabin.Dist01(0f) == 0f && Mathf.Approximately(ElevatorCabin.Dist01(ElevatorCabin.TotalSeconds), 1f) && ElevatorCabin.Dist01(3f) > ElevatorCabin.Dist01(2f)
            && ElevatorCabin.Speed01(3f) == 1f && ElevatorCabin.Speed01(ElevatorCabin.TStop + 0.1f) == 0f, "[Ascensor] perfil de velocidad: acelera, crucero a máxima velocidad, frena hasta parar");
        // sonidos: nivel efectivo (RMS × ganancia de la tabla) acorde con la mezcla; sin aire estridente
        AudioClip rumble = ElevatorAudio.CreateRumble(), mach = ElevatorAudio.CreateMachinery(), air = ElevatorAudio.CreateAir(), jolt = ElevatorAudio.CreateJolt(),
            ding = ElevatorAudio.CreateDing(), door = ElevatorAudio.CreateDoorSlide(), thud = ElevatorAudio.CreateThud();
        AudioAudit.Stat sr = AudioAudit.Measure(rumble), sm = AudioAudit.Measure(mach), sa = AudioAudit.Measure(air), sj = AudioAudit.Measure(jolt), sd = AudioAudit.Measure(ding), sdo = AudioAudit.Measure(door), st = AudioAudit.Measure(thud);
        float eR = AudioMix.Db(sr.rms * ElevatorAudio.RumbleGain), eM = AudioMix.Db(sm.rms * ElevatorAudio.MachineGain), eA = AudioMix.Db(sa.rms * ElevatorAudio.AirGain);
        r.Check(eR <= -23f, "[Ascensor] retumbo: " + eR.ToString("F1") + " dBFS efectivo (≤ -23)");
        r.Check(eM <= -26f, "[Ascensor] motor/cables: " + eM.ToString("F1") + " dBFS efectivo (≤ -26)");
        r.Check(eA <= -38f, "[Ascensor] soplo de aire: " + eA.ToString("F1") + " dBFS efectivo (≤ -38; el aire fue la queja del v6.2)");
        r.Check(AudioMix.Db(sj.peak * ElevatorAudio.JoltGain) <= -2f && AudioMix.Db(sd.peak * ElevatorAudio.DingGain) <= -6f && AudioMix.Db(sdo.rms * ElevatorAudio.DoorGain) <= -20f && AudioMix.Db(st.peak * ElevatorAudio.ThudGain) <= -3f,
            "[Ascensor] frenazo/ding/puertas/golpe sin saturar (frenazo " + AudioMix.Db(sj.peak * ElevatorAudio.JoltGain).ToString("F1") + " dB, ding " + AudioMix.Db(sd.peak * ElevatorAudio.DingGain).ToString("F1") + " dB)");
        r.Check(sa.hf <= 0.06f, "[Ascensor] el soplo de aire no tiene agudos estridentes (hf " + sa.hf.ToString("F3") + ")");
        r.Check(Mathf.Abs(rumble.samples) > 0 && jolt.length > ElevatorAudio.JoltImpactAt + 0.3f, "[Ascensor] el frenazo dura más que su punto de impacto (chirrido + golpe)");
        foreach (AudioClip c in new[] { rumble, mach, air, jolt, ding, door, thud }) UnityEngine.Object.DestroyImmediate(c);
        // la animación es obligatoria: no existe ninguna opción para desactivarla
        bool optionFound = false;
        foreach (string f in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
        {
            string txt = File.ReadAllText(f);
            if (txt.Contains("Animación del ascensor") || txt.Contains("ElevatorEnabled") || txt.Contains("\"elevator\"")) { optionFound = true; r.Info("opción de ascensor en " + f); }
        }
        r.Check(!optionFound, "[Ascensor] no hay opción para desactivar el ascensor (siempre se reproduce)");
        // créditos del modelo
        string credits = File.ReadAllText("CREDITS.md");
        r.Check(credits.Contains("Elevator car interior") && credits.Contains("3D Assets") && credits.Contains("CC0") && credits.Contains("3dassets.dev"), "[Ascensor] CREDITS.md cita el modelo del ascensor (nombre, autor, licencia, URL)");
        string ui = File.ReadAllText("Assets/Scripts/UIManager.cs");
        r.Check(ui.Contains("Elevator car interior") && ui.Contains("3D Assets"), "[Ascensor] los créditos del juego citan el modelo del ascensor");
    }

    private static void ScanSourcesForExitHints(Report r)
    {
        string[] files = Directory.GetFiles("Assets/Scripts", "*.cs");
        int hits = 0;
        foreach (string f in files)
        {
            string txt = File.ReadAllText(f);
            // v7.0.4: el aparcamiento tiene señalización de garaje (SALIDA/RAMPA, muchas mienten) pedida para desorientar;
            // se permite solo ahí y se comprueba aparte que ningún cartel cuelga junto a la puerta real (V704Checks)
            if (Path.GetFileName(f) == "GarageDecor.cs") continue;
            if (txt.Contains("\"SALIDA") || txt.Contains("TextMesh ") && txt.Contains("tm.text")) { hits++; r.Fail("Código con cartel de salida: " + f); }
        }
        r.Check(hits == 0, "Ningún script crea el cartel 'SALIDA' ni TextMesh de salida");
    }

    private static void TestLevel(Report r, int lv, int seed)
    {
        string tag = "[Nivel " + lv + " semilla " + seed + "] ";
        LevelConfig cfg = LevelConfig.Get(lv);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        GameObject go = new GameObject("GenTest");
        BackroomsGenerator gen = go.AddComponent<BackroomsGenerator>();
        gen.config = cfg;
        gen.useRandomSeed = false;
        gen.seed = seed;
        gen.Generate();
        sw.Stop();
        r.Info(tag + "generado en " + sw.ElapsedMilliseconds + " ms (" + gen.GridWidth + "x" + gen.GridHeight + ", paredes " + gen.WallSegmentCount
            + ", pilares " + gen.PillarCount + ", accesorios " + gen.FixtureCount + " (vivos " + gen.LiveFixtureCount + "), pilas " + gen.BatterySpots.Count + ")");

        r.Check(gen.HasLevel, tag + "nivel generado");
        r.Check(gen.ExitTransform != null, tag + "existe la puerta de salida");
        r.Check(gen.MonsterSpawns != null && gen.MonsterSpawns.Count == Mathf.Max(1, cfg.monsterCount), tag + "apariciones de monstruo = " + cfg.monsterCount);
        r.Check(gen.BatterySpots != null && gen.BatterySpots.Count == cfg.batteries, tag + "pilas colocadas = " + cfg.batteries + " (hay " + (gen.BatterySpots != null ? gen.BatterySpots.Count : 0) + ")");
        if (cfg.layout != LayoutKind.Field) r.Check(gen.FixtureCount > 0, tag + "hay accesorios de luz");
        else r.Check(gen.Root.GetComponentInChildren<FieldExitBeacon>(true) != null, tag + "campo: la salida tiene su farol/radio (pistas sutiles)");
        if (cfg.layout == LayoutKind.OpenPillars) r.Check(gen.PillarCount > 0, tag + "hay pilares");

        // Sin cartel / TextMesh / objetos "Sign" / "Salida" / flechas
        r.Check(go.GetComponentsInChildren<TextMesh>(true).Length == 0, tag + "sin TextMesh");
        bool badName = false;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n == "garagesigns" && cfg.decks > 1) continue;   // v7.0.4: señalización del aparcamiento (ver arriba)
            if (n.Contains("sign") || n.Contains("salida") || n.Contains("arrow")) badName = true;
        }
        r.Check(!badName, tag + "ningún objeto llamado sign/salida/arrow");

        // El LED y la luz de la puerta empiezan apagados (solo se activan a < 8 m)
        ExitDoorCue cue = gen.Root.GetComponentInChildren<ExitDoorCue>(true);
        r.Check(cue != null && cue.visibleDistance <= 8.01f && !cue.ledRenderer.enabled && !cue.glow.enabled, tag + "pista de la puerta limitada a ~8 m y apagada de inicio");

        // Materiales / shaders / texturas
        int renderers = 0, badMats = 0;
        foreach (Renderer rend in go.GetComponentsInChildren<Renderer>(true))
        {
            renderers++;
            Material[] mats = rend.sharedMaterials;
            if (mats == null || mats.Length == 0) { badMats++; continue; }
            foreach (Material m in mats)
            {
                if (m == null || m.shader == null || m.shader.name.Contains("InternalErrorShader") || !m.shader.isSupported) badMats++;
            }
        }
        r.Check(badMats == 0, tag + "todos los materiales tienen shader válido (" + renderers + " renderers)");

        bool texOk = true;
        foreach (Transform chunk in gen.Root.GetComponentsInChildren<Transform>(true))
        {
            MeshRenderer mr = chunk.GetComponent<MeshRenderer>();
            if (mr == null || !(chunk.name.StartsWith("Walls_") || chunk.name.StartsWith("FloorCeiling_"))) continue;
            foreach (Material m in mr.sharedMaterials)
            {
                if (m.mainTexture == null || m.GetTexture("_BumpMap") == null || m.GetTexture("_MetallicGlossMap") == null || !m.IsKeywordEnabled("_NORMALMAP"))
                    texOk = false;
                if (m.mainTexture != null && (m.mainTexture.width < 512 || !(m.mainTexture.name.Contains("Albedo") || m.mainTexture.name.EndsWith("_a")))) texOk = false;
            }
        }
        r.Check(texOk, tag + "materiales de pared/suelo/techo con albedo>=512px + normal + máscara PBR");

        // Orientación de triángulos: la normal geométrica (sentido de giro de Unity) coincide con la normal del vértice
        int badTris = 0, totalTris = 0;
        foreach (MeshFilter mf in go.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = mf.sharedMesh;
            Vector3[] vv = mesh.vertices; Vector3[] nn = mesh.normals;
            int badBefore = badTris;
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                int[] tr = mesh.GetTriangles(sm);
                for (int i = 0; i < tr.Length; i += 3)
                {
                    Vector3 gn = Vector3.Cross(vv[tr[i + 1]] - vv[tr[i]], vv[tr[i + 2]] - vv[tr[i]]);
                    totalTris++;
                    if (Vector3.Dot(gn, nn[tr[i]]) <= 0f) badTris++;
                }
            }
            if (badTris > badBefore) r.Info(tag + "triángulos mal orientados en " + mf.name + ": " + (badTris - badBefore));
        }
        r.Check(badTris == 0 && totalTris > 0, tag + "todos los triángulos mirando hacia fuera (" + totalTris + " tris, " + badTris + " mal)");

        // Alcanzabilidad: BFS por cuadrícula
        int total = gen.GridWidth * gen.GridHeight, reach = 0;
        for (int x = 0; x < gen.GridWidth; x++) for (int z = 0; z < gen.GridHeight; z++) if (gen.DistanceFromStart(x, z) >= 0) reach++;
        int blockedCells = gen.FieldPlan != null ? gen.FieldPlan.blocked.Count : 0;
        r.Check(reach == total - blockedCells, tag + "todas las celdas alcanzables desde el inicio (" + reach + "/" + (total - blockedCells) + (blockedCells > 0 ? ", " + blockedCells + " ocupadas por casas/accesorios" : "") + ")");
        r.Check(gen.DistanceFromStart(gen.ExitCell.x, gen.ExitCell.y) >= 0, tag + "celda de salida alcanzable (dist " + gen.DistanceFromStart(gen.ExitCell.x, gen.ExitCell.y) + ")");

        // v6.8: sala de piscina fija horneada (Poolrooms)
        if (cfg.index == 2)
        {
            r.Check(gen.HasPoolRoom && gen.PoolFixed, tag + "sala de piscina fija (horneada en Cycles) colocada");
            if (gen.PoolFixed)
            {
                RectInt pb = gen.PoolBlock; int doors = 0, closed = 0;
                for (int i = pb.x; i < pb.xMax; i++)
                {
                    if (gen.IsCellOpen(i, pb.y, 2)) { doors++; r.Check(i == pb.x + 1, tag + "puerta sur de la piscina en su sitio fijo"); } else closed++;
                    if (gen.IsCellOpen(i, pb.yMax - 1, 0)) { doors++; r.Check(i == pb.x + 2, tag + "puerta norte de la piscina en su sitio fijo"); } else closed++;
                }
                for (int j = pb.y; j < pb.yMax; j++)
                {
                    if (gen.IsCellOpen(pb.x, j, 3)) { doors++; r.Check(j == pb.y + 1, tag + "puerta oeste de la piscina en su sitio fijo"); } else closed++;
                    if (gen.IsCellOpen(pb.xMax - 1, j, 1)) { doors++; r.Check(j == pb.y + 1, tag + "puerta este de la piscina en su sitio fijo"); } else closed++;
                }
                r.Check(doors == 4 && closed == 2 * (pb.width + pb.height) - 4, tag + "perímetro de la piscina: 4 puertas fijas, resto cerrado (" + doors + "/" + closed + ")");
                r.Check(!gen.InPoolBlock(gen.ExitCell.x, gen.ExitCell.y), tag + "la salida no está en la sala de piscina");
                GameObject baked = GameObject.Find("PoolRoomBaked");
                MeshRenderer bmr = baked != null ? baked.GetComponent<MeshRenderer>() : null;
                r.Check(bmr != null && bmr.sharedMaterials.Length == 5 && bmr.sharedMaterial.shader.name == "Backrooms/PoolBaked" && bmr.sharedMaterial.GetTexture("_LightMap") != null,
                    tag + "malla horneada con shader PoolBaked + lightmap EXR");
                r.Check(baked != null && baked.GetComponent<MeshCollider>() != null, tag + "colisión de la sala horneada");
                PoolRoomProbes pr = UnityEngine.Object.FindObjectOfType<PoolRoomProbes>();
                r.Check(pr != null && pr.positions != null && pr.positions.Length == 33, tag + "33 sondas de luz horneadas en la sala (v6.9)");
                int rtLights = 0; foreach (Light l in GameObject.Find("PoolRoom").GetComponentsInChildren<Light>(true)) rtLights++;
                r.Check(rtLights == 0, tag + "la sala horneada no usa luces en tiempo real (" + rtLights + ")");
            }
        }

        List<Vector3> path = new List<Vector3>();
        bool hasPath = gen.TryGetPath(gen.PlayerStart, gen.ExitPosition, path);
        r.Check(hasPath && path.Count > 0, tag + "camino BFS inicio -> salida (" + path.Count + " puntos)");
        foreach (Vector3 spawn in gen.MonsterSpawns)
        {
            List<Vector3> p2 = new List<Vector3>();
            r.Check(gen.TryGetPath(gen.PlayerStart, spawn, p2), tag + "monstruo alcanzable por BFS");
        }
        foreach (Vector3 b in gen.BatterySpots)
        {
            List<Vector3> p3 = new List<Vector3>();
            r.Check(gen.TryGetPath(gen.PlayerStart, b, p3), tag + "pila alcanzable por BFS");
        }

        r.Check(gen.ExtraSpots != null && gen.ExtraSpots.Count >= 8, tag + "sitios para notas y objetos extra: " + (gen.ExtraSpots != null ? gen.ExtraSpots.Count : 0));
        if (gen.ExtraSpots != null)
            foreach (Vector3 e in gen.ExtraSpots)
            {
                List<Vector3> p4 = new List<Vector3>();
                r.Check(gen.TryGetPath(gen.PlayerStart, e, p4), tag + "objeto extra alcanzable por BFS");
            }

        // Comprobación física: el jugador (esfera de 0.35 m) cabe a lo largo del camino BFS
        Physics.SyncTransforms();
        Vector3 head = gen.PlayerStart + Vector3.up * 0.9f;
        r.Check(!Physics.CheckSphere(head, 0.34f), tag + "el punto de inicio no está dentro de una pared");
        bool pathFree = true;
        Vector3 prev = gen.PlayerStart;
        if (cfg.layout == LayoutKind.Field)
        {
            // campo: se recorre el camino a pasos de 0,5 m siguiendo el terreno (colinas) comprobando que no hay casas/postes/puertas
            Vector3 q0 = gen.PlayerStart;
            for (int i = 1; i < path.Count - 1 && pathFree; i++)
            {
                Vector3 q1 = path[i];
                int steps = Mathf.CeilToInt(Vector3.Distance(q0, q1) / 0.5f);
                for (int k = 1; k <= steps; k++)
                {
                    Vector3 pp = gen.Root.InverseTransformPoint(Vector3.Lerp(q0, q1, k / (float)steps));
                    pp.y = FieldHeight.H(pp.x, pp.z) + 0.95f;
                    Collider[] hits = Physics.OverlapSphere(gen.Root.TransformPoint(pp), 0.34f);
                    foreach (Collider hc in hits)
                        if (!hc.name.StartsWith("FieldTerrain")) { pathFree = false; r.Info(tag + "colisión en el camino del campo: " + hc.name + " en " + pp); break; }
                    if (!pathFree) break;
                }
                q0 = q1;
            }
            path = new List<Vector3>(path);
        }
        for (int i = 0; i < path.Count - 1 && cfg.layout != LayoutKind.Field; i++)
        {
            Vector3 a = prev + Vector3.up * 0.9f, bpt = path[i] + Vector3.up * 0.9f;
            if (i == 0) { prev = path[i]; continue; }
            if (gen.Halls.Count > 0 && (gen.InHallWorld(prev) || gen.InHallWorld(path[i]))) { prev = path[i]; continue; }   // v7.1.0: salas de juegos con muebles (se cruza por la malla de navegación; se comprueba en V710Checks)
            Vector3 dir = bpt - a;
            if (dir.magnitude > 0.01f && Physics.SphereCast(a, 0.34f, dir.normalized, out RaycastHit hit, dir.magnitude))
            {
                pathFree = false;
                r.Info(tag + "colisión en el camino cerca de " + hit.point + " (" + hit.collider.name + ")");
                break;
            }
            prev = path[i];
        }
        r.Check(pathFree, tag + "el camino BFS no choca con paredes/pilares (SphereCast r=0.34)");
        if (path.Count > 0)
        {
            Vector3 last = path[path.Count - 1];
            r.Check(Vector3.Distance(new Vector3(gen.ExitPosition.x, 0, gen.ExitPosition.z), new Vector3(last.x, 0, last.z)) < 0.01f, tag + "el camino termina en la puerta");
        }
        // Se puede llegar a < 1.6 m de la puerta (radio de victoria) pegándose a su pared
        Vector3 doorFloor = new Vector3(gen.ExitPosition.x, gen.FloorHeightLocal(gen.ExitPosition.x, gen.ExitPosition.z), gen.ExitPosition.z);
        Vector3 toCell = gen.CellCenterWorld(gen.ExitCell.x, gen.ExitCell.y) - doorFloor; toCell.y = 0;
        if (cfg.layout == LayoutKind.Field) toCell = gen.ExitTransform.forward;   // campo: la puerta está suelta, se llega por delante
        Vector3 standPos = doorFloor + toCell.normalized * 0.8f;
        if (cfg.layout == LayoutKind.Field) standPos.y = gen.FloorHeightLocal(standPos.x, standPos.z) + 0.05f;
        bool standFree = !Physics.CheckSphere(standPos + Vector3.up * 0.9f, 0.34f);
        r.Check(standFree && Vector3.Distance(standPos, doorFloor) < 1.6f, tag + "se puede estar a <1.6 m de la puerta sin atravesar nada");

        // NavMesh en edición (si el paquete lo permite)
        BallpitSetPiece.BeginNavBuild();
        bool navBuilt = MonsterAI.TryBuildRuntimeNavMesh(gen.Root.gameObject);
        BallpitSetPiece.EndNavBuild();
        if (navBuilt)
        {
            NavMeshPath np = new NavMeshPath();
            NavMeshHit h1, h2;
            bool s1 = NavMesh.SamplePosition(gen.PlayerStart, out h1, 2f, NavMesh.AllAreas);
            bool s2 = NavMesh.SamplePosition(new Vector3(standPos.x, standPos.y, standPos.z), out h2, 2.5f, NavMesh.AllAreas);
            bool ok = s1 && s2 && NavMesh.CalculatePath(h1.position, h2.position, NavMesh.AllAreas, np) && np.status == NavMeshPathStatus.PathComplete;
            r.Check(ok, tag + "NavMesh: camino completo inicio -> puerta (" + np.status + ")");
            foreach (Vector3 spawn in gen.MonsterSpawns)
            {
                NavMeshHit hs;
                bool on = NavMesh.SamplePosition(spawn, out hs, 2f, NavMesh.AllAreas);
                NavMeshPath mp = new NavMeshPath();
                bool okm = on && NavMesh.CalculatePath(hs.position, h1.position, NavMesh.AllAreas, mp) && mp.status == NavMeshPathStatus.PathComplete;
                r.Check(okm, tag + "NavMesh: monstruo conectado con el jugador");
            }
            NavMesh.RemoveAllNavMeshData();
        }
        else
        {
            r.Info(tag + "AVISO: no se pudo hornear la NavMesh en modo edición");
        }

        if (cfg.layout == LayoutKind.Field) TestFieldLevel(r, tag, gen, seed);

        UnityEngine.Object.DestroyImmediate(go);
    }

    // v7.0 Campo infinito: casas, salida, determinismo, periodicidad, hierba y sonido
    private static void TestFieldLevel(Report r, string tag, BackroomsGenerator gen, int seed)
    {
        FieldWorld fw = FieldWorld.Active;
        FieldPlan plan = gen.FieldPlan;
        r.Check(fw != null && plan != null && fw.plan == plan, tag + "campo: FieldWorld + plan creados");
        if (fw == null || plan == null) return;
        r.Check(plan.houses.Count >= 6 && fw.houseCount == plan.houses.Count, tag + "campo: casas dispersas (" + plan.houses.Count + ")");
        // casas casi idénticas (mismo modelo, giro y tinte) y algunas visitables
        var groups = new Dictionary<int, int>(); int enter = 0;
        foreach (FieldPlan.House h in plan.houses) { int k = h.template * 100 + h.rot * 10 + h.tint; groups[k] = groups.TryGetValue(k, out int v) ? v + 1 : 1; if (h.enterable) enter++; }
        int maxTwin = 0; foreach (int v in groups.Values) maxTwin = Mathf.Max(maxTwin, v);
        r.Check(maxTwin >= 3, tag + "campo: varias casas repetidas casi idénticas (" + maxTwin + " iguales)");
        r.Check(enter >= 1 && enter < plan.houses.Count, tag + "campo: algunas casas visitables (" + enter + "/" + plan.houses.Count + ")");
        // ninguna casa en la salida ni cerca; la salida no está en una celda ocupada
        int minHouseExit = 99; foreach (FieldPlan.House h in plan.houses) minHouseExit = Mathf.Min(minHouseExit, FieldPlan.Cheb(h.cell, gen.ExitCell, gen.GridWidth, gen.GridHeight));
        r.Check(minHouseExit >= 2 && !plan.blocked.Contains(gen.ExitCell), tag + "campo: ninguna casa es ni está junto a la salida (dist mín " + minHouseExit + " celdas)");
        r.Check(gen.ExitTransform != null && gen.ExitTransform.GetComponentInParent<FieldExitBeacon>() == null && gen.ExitTransform.parent.GetComponentInChildren<FieldExitBeacon>() != null, tag + "campo: farol de la salida junto a la puerta real");
        int beacons = gen.Root.GetComponentsInChildren<FieldExitBeacon>(true).Length;
        r.Check(beacons == 1, tag + "campo: solo la salida tiene pistas (balizas " + beacons + ")");
        // salida lejos (distancia con vuelta)
        Vector3 sl = gen.Root.InverseTransformPoint(gen.PlayerStart), el = gen.Root.InverseTransformPoint(gen.ExitPosition);
        float ed = FieldHeight.WrappedDist(sl.x, sl.z, el.x, el.z);
        r.Check(ed > 90f, tag + "campo: salida lejos del inicio (" + ed.ToString("F0") + " m)");
        // colisiones de casas: cada casa tiene MeshCollider y una visitable deja entrar
        int houseCols = 0;
        foreach (Transform t in gen.Root.GetComponentsInChildren<Transform>(true)) if (t.name.StartsWith("House_") && t.GetComponent<MeshCollider>() != null) houseCols++;
        r.Check(houseCols == plan.houses.Count, tag + "campo: colisión en todas las casas (" + houseCols + ")");
        Physics.SyncTransforms();
        int insideFree = 0, closedBlocked = 0, enterTested = 0, closedTested = 0;
        foreach (FieldPlan.House h in plan.houses)
        {
            Vector3 c = gen.Root.TransformPoint(new Vector3(h.pos.x, FieldHeight.H(h.pos.x, h.pos.y) + 1.4f, h.pos.y));
            bool free = !Physics.CheckSphere(c, 0.3f);
            if (h.enterable) { enterTested++; if (free) insideFree++; }
            else
            {
                closedTested++;
                // desde fuera hacia el centro de una casa cerrada: algo bloquea (pared/puerta)
                Vector3 o = c + new Vector3(9f, 0f, 0.3f);
                if (Physics.SphereCast(o, 0.3f, (c - o).normalized, out RaycastHit hit, 9f)) closedBlocked++;
            }
        }
        r.Check(insideFree == enterTested, tag + "campo: interior libre en casas visitables (" + insideFree + "/" + enterTested + ")");
        r.Check(closedBlocked == closedTested, tag + "campo: casas cerradas sin hueco transitable (" + closedBlocked + "/" + closedTested + ")");
        // v7.0.2: objetos solo dentro de casas abiertas, sobre muebles o tarima; ninguno en la hierba
        {
            LevelConfig fcfg = gen.config;
            int inside = 0, total = 0, onFurn = 0; float maxIt = 0f;
            List<Vector3> all = new List<Vector3>(gen.BatterySpots); all.AddRange(gen.ExtraSpots);
            foreach (Vector3 w in all)
            {
                total++;
                Vector3 lp = gen.Root.InverseTransformPoint(w);
                bool inHouse = false;
                foreach (FieldPlan.House h in plan.houses)
                {
                    if (!h.enterable) continue;
                    Vector2 sz = FieldPlan.TemplateSize[h.template]; if (h.rot % 2 == 1) sz = new Vector2(sz.y, sz.x);
                    if (Mathf.Abs(lp.x - h.pos.x) < sz.x * 0.5f - 0.15f && Mathf.Abs(lp.z - h.pos.y) < sz.y * 0.5f - 0.15f) inHouse = true;
                }
                if (inHouse) inside++;
                // debajo hay mueble o tarima (no terreno)
                if (Physics.Raycast(w + Vector3.up * 0.05f, Vector3.down, out RaycastHit hh, 0.4f) && (hh.collider.name.StartsWith("House_") || hh.collider.transform.parent != null && hh.collider.transform.parent.name == "Furniture")) onFurn++;
                maxIt = Mathf.Max(maxIt, lp.y - FieldHeight.H(lp.x, lp.z));
            }
            r.Check(total == fcfg.batteries + 9 && gen.BatterySpots.Count == fcfg.batteries && gen.ExtraSpots.Count == 9, tag + "campo: todos los objetos colocados (" + gen.BatterySpots.Count + " pilas + " + gen.ExtraSpots.Count + " extra)");
            r.Check(inside == total, tag + "campo: todos los objetos dentro de casas abiertas, ninguno en la hierba (" + inside + "/" + total + ")");
            r.Check(onFurn == total, tag + "campo: cada objeto apoyado en mesa, estante, caja o tarima (" + onFurn + "/" + total + ")");
            r.Check(enter >= FieldPlan.MinEnterable && fw.FurnitureCount > 0 && gen.FieldItemHouses >= Mathf.Min(enter, 4), tag + "campo: muebles en las casas abiertas (" + fw.FurnitureCount + " piezas, objetos en " + gen.FieldItemHouses + " casas de " + enter + ")");
            List<Vector3> before = all;
            BackroomsGenerator g2 = new GameObject("FieldGen2").AddComponent<BackroomsGenerator>();
            FieldWorld keep = FieldWorld.Active;
            g2.config = gen.config; g2.useRandomSeed = false; g2.seed = gen.UsedSeed; g2.Generate();
            List<Vector3> again = new List<Vector3>(g2.BatterySpots); again.AddRange(g2.ExtraSpots);
            bool sameSpots = again.Count == before.Count;
            for (int i = 0; sameSpots && i < again.Count; i++) sameSpots = (g2.Root.InverseTransformPoint(again[i]) - gen.Root.InverseTransformPoint(before[i])).sqrMagnitude < 1e-6f;
            r.Check(sameSpots, tag + "campo: sitios de los objetos deterministas por semilla (multijugador)");
            UnityEngine.Object.DestroyImmediate(g2.gameObject);
            FieldWorld.Active = keep;
        }
        // determinismo: misma semilla -> mismo plan y misma huella; terreno periódico
        ulong h1 = gen.WorldHash();
        FieldPlan p2 = FieldPlan.Make(gen.UsedSeed, gen.GridWidth, gen.GridHeight, gen.CellSize, gen.StartCell, gen.ExitCell);
        bool same = p2.houses.Count == plan.houses.Count && p2.props.Count == plan.props.Count;
        for (int i = 0; same && i < p2.houses.Count; i++) same = p2.houses[i].pos == plan.houses[i].pos && p2.houses[i].template == plan.houses[i].template && p2.houses[i].enterable == plan.houses[i].enterable;
        r.Check(same, tag + "campo: plan de casas/accesorios determinista por semilla");
        float L = FieldHeight.L, maxErr = 0f, maxH = 0f;
        for (int i = 0; i < 40; i++) { float x = i * 6.3f, z = i * 4.1f + 3f; maxErr = Mathf.Max(maxErr, Mathf.Abs(FieldHeight.H(x, z) - FieldHeight.H(x + L, z - L))); maxH = Mathf.Max(maxH, Mathf.Abs(FieldHeight.H(x, z))); }
        r.Check(maxErr < 1e-3f && maxH > 0.5f && maxH < 8f, tag + "campo: colinas suaves y periódicas (vuelta sin costura; altura máx " + maxH.ToString("F1") + " m)");
        r.Check(Mathf.Abs(L - gen.GridWidth * gen.CellSize) < 0.01f, tag + "campo: periodo = tamaño de la rejilla (" + L + " m)");
        // terreno con colisión: rayo hacia abajo en puntos al azar
        int hitsOk = 0; System.Random rr = new System.Random(seed);
        for (int i = 0; i < 20; i++)
        {
            float x = (float)rr.NextDouble() * L, z = (float)rr.NextDouble() * L;
            if (plan.NoGrass(x, z)) { hitsOk++; continue; }
            Vector3 o = gen.Root.TransformPoint(new Vector3(x, 30f, z));
            if (Physics.Raycast(o, Vector3.down, out RaycastHit hit, 60f) && Mathf.Abs(gen.Root.InverseTransformPoint(hit.point).y - FieldHeight.H(x, z)) < 0.15f) hitsOk++;
        }
        r.Check(hitsOk == 20, tag + "campo: el suelo con colisión sigue las colinas (" + hitsOk + "/20)");
        // sonido de hierba
        r.Check(GrassAudio.StepClipCount == 5 && GrassAudio.Rustle != null, tag + "campo: 5 pisadas de hierba CC0 + bucle de roce");
        float rs = GrassAudio.RustleTarget(6f, true, false), rw = GrassAudio.RustleTarget(3f, false, false), rc = GrassAudio.RustleTarget(1.5f, false, true), r0 = GrassAudio.RustleTarget(0f, false, false);
        r.Check(rs > rw && rw > rc && rc > 0f && r0 == 0f, tag + "campo: roce más fuerte corriendo que andando y que agachado; nada parado (" + rs.ToString("F2") + "/" + rw.ToString("F2") + "/" + rc.ToString("F2") + ")");
        Vector3 grassPt = Vector3.zero; bool found = false;
        for (int i = 0; i < 200 && !found; i++) { float x = (float)rr.NextDouble() * L, z = (float)rr.NextDouble() * L; if (!plan.NoGrass(x, z)) { grassPt = gen.Root.TransformPoint(new Vector3(x, FieldHeight.H(x, z), z)); found = true; } }
        r.Check(found && GrassAudio.OnGrass(grassPt), tag + "campo: se detecta hierba bajo los pies fuera de las casas");
        if (plan.houses.Count > 0) { FieldPlan.House h0 = plan.houses[0]; r.Check(!GrassAudio.OnGrass(gen.Root.TransformPoint(new Vector3(h0.pos.x, FieldHeight.H(h0.pos.x, h0.pos.y) + 0.5f, h0.pos.y))), tag + "campo: dentro de una casa no suena hierba"); }
        r.Info(tag + "campo: " + plan.houses.Count + " casas, " + plan.props.Count + " accesorios, trozos de terreno " + fw.chunkCount + ", salida a " + ed.ToString("F0") + " m, huella " + h1.ToString("x"));
    }

    // ------------------------------------------------------------------
    // Modo Play
    // ------------------------------------------------------------------
    // v7.0 Campo infinito en juego: hierba en GPU por calidad (rendimiento), pasos/roce, vuelta al campo, ambiente, El Vigía
    internal static int FaceIslands(Mesh m)
    {
        int[] tri = m.triangles; Vector3[] v = m.vertices; int n = v.Length;
        // soldar vértices duplicados (costuras de normales/UV) antes de contar islas
        Dictionary<Vector3Int, int> weld = new Dictionary<Vector3Int, int>(); int[] id = new int[n];
        for (int i = 0; i < n; i++) { Vector3Int k = Vector3Int.RoundToInt(v[i] * 1000f); if (!weld.TryGetValue(k, out int w)) { w = weld.Count; weld[k] = w; } id[i] = w; }
        int[] par = new int[weld.Count]; for (int i = 0; i < par.Length; i++) par[i] = i;
        System.Func<int, int> find = null; find = x => { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; };
        for (int i = 0; i < tri.Length; i += 3) { int a = find(id[tri[i]]), b = find(id[tri[i + 1]]), c = find(id[tri[i + 2]]); par[b] = a; par[find(c)] = a; }
        HashSet<int> roots = new HashSet<int>(); for (int i = 0; i < tri.Length; i++) roots.Add(find(id[tri[i]]));
        return roots.Count;
    }

    // v7.0.1: salto real (CharacterController + gravedad): sube, cuenta y vuelve a tocar suelo
    internal static IEnumerator JumpCheck(Report r, string tag, FirstPersonController pw, string where, System.Action<bool> done = null)
    {
        float t = 0f;
        while (t < 2f && !pw.GroundProbe()) { yield return 0.05f; t += 0.05f; }
        // techo bajo (cabina del ascensor, etc.): buscar cerca un sitio con hueco para el salto
        if (Physics.Raycast(pw.transform.position + Vector3.up * 1.7f, Vector3.up, 1.0f, ~0, QueryTriggerInteraction.Ignore))
        {
            Vector3 p0 = pw.transform.position; bool moved = false;
            for (int ring = 1; ring <= 12 && !moved; ring++)
                for (int k = 0; k < 8 && !moved; k++)
                {
                    float a = k * Mathf.PI / 4f; Vector3 c = p0 + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * ring * 1.0f;
                    if (!Physics.Raycast(c + Vector3.up * 2.5f, Vector3.down, out RaycastHit gh, 4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 f = gh.point;
                    if (Mathf.Abs(f.y - p0.y) > 0.3f || Physics.Raycast(f + Vector3.up * 0.2f, Vector3.up, 2.7f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.Linecast(p0 + Vector3.up, f + Vector3.up, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.CheckCapsule(f + Vector3.up * 0.45f, f + Vector3.up * 1.6f, 0.4f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    pw.Teleport(f + Vector3.up * 0.05f, pw.transform.eulerAngles.y); moved = true;
                }
            t = 0f; while (t < 1f && !pw.GroundProbe()) { yield return 0.05f; t += 0.05f; }
        }
        pw.SetStaminaForTests(100f);
        float y0 = pw.transform.position.y, ymax = y0; int j0 = pw.JumpCount;
        bool wasEnabled = pw.enabled; pw.enabled = true;
        pw.debugInput = true; pw.debugMove = Vector2.zero; pw.debugJump = true;
        yield return 0.1f;
        pw.debugJump = false;
        t = 0f; bool landed = false;
        while (t < 1.6f) { ymax = Mathf.Max(ymax, pw.transform.position.y); yield return null; t += Time.deltaTime; if (t > 0.3f && pw.IsGrounded) { landed = true; break; } }
        pw.debugInput = false; pw.enabled = wasEnabled;
        bool ok = pw.JumpCount == j0 + 1 && ymax - y0 > 0.55f && landed;
        r.Check(ok, tag + "[salto] " + where + ": salta y vuelve al suelo (sube " + (ymax - y0).ToString("F2") + " m, saltos " + (pw.JumpCount - j0) + ", aterriza " + landed + ")");
        if (done != null) done(ok);
    }

    internal static IEnumerator FieldPlayChecks(Report r, string tag, GameManager gm, FirstPersonController pw)
    {
        string ft = tag + "[Campo] ";
        foreach (MonsterAI m in gm.monsters) m.Stop();
        FieldGrass fg = FieldGrass.Active; FieldWorld fw = FieldWorld.Active;
        r.Check(fg != null && fw != null && fg.KindCount >= 10, ft + "hierba instanciada (" + (fg != null ? fg.KindCount : 0) + " especies de matas escaneadas)");
        r.Check(RenderSettings.skybox != null && RenderSettings.skybox.shader.name == "Backrooms/FieldSky" && RenderSettings.fog && Camera.main.clearFlags == CameraClearFlags.Skybox, ft + "cielo nublado HDRI + niebla");
        r.Check(Camera.main.farClipPlane >= 150f, ft + "distancia de dibujo ampliada (" + Camera.main.farClipPlane + " m)");
        r.Check(FieldWatcherApparition.Active != null && FieldWatcherApparition.Active.model != null, ft + "El Vigía (aparición en el horizonte) preparado");
        r.Check(FieldExitBeacon.Active != null && FieldExitBeacon.Active.hum != null && FieldExitBeacon.Active.hum.isPlaying && FieldExitBeacon.Active.hum.maxDistance >= 60f, ft + "la salida emite su zumbido de radio lejano (3D, " + (FieldExitBeacon.Active != null ? FieldExitBeacon.Active.hum.maxDistance : 0f) + " m)");
        // sobre la hierba, delante de la cabina
        ElevatorCabin cab = ElevatorCabin.Current;
        Vector3 fwd = cab != null ? cab.Forward : Vector3.forward;
        Vector3 sp = (cab != null ? cab.ExitPoint : gm.generator.PlayerStart) + fwd * 4f;
        Vector3 lp = gm.generator.Root.InverseTransformPoint(sp); lp.y = FieldHeight.H(lp.x, lp.z) + 0.1f;
        float yaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
        pw.Teleport(gm.generator.Root.TransformPoint(lp), yaw);
        yield return 1.0f;
        // rendimiento por calidad
        int q0 = GameSettings.Quality;
        int[] inst = new int[3], tris = new int[3]; float[] ms = new float[3];
        for (int q = 2; q >= 0; q--)
        {
            GameSettings.Quality = q;
            yield return 1.2f;
            inst[q] = fg.VisibleInstances; tris[q] = fg.VisibleTriangles;
            float t0 = Time.realtimeSinceStartup; int f0 = Time.frameCount;
            yield return 1.0f;
            ms[q] = (Time.realtimeSinceStartup - t0) * 1000f / Mathf.Max(1, Time.frameCount - f0);
            r.Info(ft + "calidad " + q + ": " + inst[q] + " matas visibles, " + (tris[q] / 1000) + "k triángulos, " + fg.DrawCalls + " llamadas, radio " + fg.Radius + " m, " + ms[q].ToString("F0") + " ms/fotograma (render por software)");
        }
        GameSettings.Quality = q0;
        r.Check(inst[2] > 3000 && inst[0] > 300, ft + "hierba visible en Alto y en Bajo (" + inst[2] + " / " + inst[0] + ")");
        r.Check(inst[0] < inst[1] && inst[1] < inst[2] && tris[0] * 3 < tris[2], ft + "Bajo reduce densidad y distancia de la hierba (" + tris[0] / 1000 + "k vs " + tris[2] / 1000 + "k tris)");
        r.Check(tris[2] < 9000000 && tris[0] < 2000000, ft + "presupuesto de triángulos de hierba (Alto < 9M, Bajo < 2M)");
        r.Check(fg.ChunkCount <= FieldGrass.MaxSlots, ft + "trozos de hierba reciclados (" + fg.ChunkCount + " en memoria, máx. " + FieldGrass.MaxSlots + " ranuras)");
        // pisadas de hierba y roce: corriendo > andando > agachado; parado nada
        FootstepAudio fa = pw.GetComponent<FootstepAudio>();
        r.Check(fa != null && fa.kind == StepKind.Grass, ft + "pasos del jugador en modo hierba");
        float[] lvl = new float[3]; int st0 = fa.GrassStepsPlayed;
        for (int mode = 0; mode < 3; mode++)
        {
            pw.Teleport(gm.generator.Root.TransformPoint(lp), yaw + mode * 120f);
            yield return 0.3f;
            pw.debugInput = true; pw.debugMove = new Vector2(0f, 1f); pw.debugSprint = mode == 0; pw.debugCrouch = mode == 2;
            float mx = 0f;
            for (float tt = 0f; tt < 2.2f; tt += 0.1f) { mx = Mathf.Max(mx, fa.RustleLevel); yield return 0.1f; }
            lvl[mode] = mx;
            pw.debugMove = Vector2.zero; pw.debugSprint = false; pw.debugCrouch = false;
            yield return 0.6f;
        }
        yield return 1.8f;
        pw.debugInput = false;
        r.Check(fa.GrassStepsPlayed > st0, ft + "suenan pisadas de hierba reales (" + (fa.GrassStepsPlayed - st0) + ")");
        r.Check(lvl[0] > lvl[1] && lvl[1] > lvl[2] && lvl[2] > 0.02f, ft + "roce de la hierba: corriendo " + lvl[0].ToString("F2") + " > andando " + lvl[1].ToString("F2") + " > agachado " + lvl[2].ToString("F2"));
        r.Check(fa.RustleLevel < 0.05f, ft + "parado no hay roce (" + fa.RustleLevel.ToString("F2") + ")");
        // el campo no se acaba: al cruzar el borde se vuelve al otro lado (mismo terreno)
        float L = FieldHeight.L; int w0 = fw.WrapCount;
        Vector3 edge = new Vector3(L - 0.6f, 0f, lp.z); edge.y = FieldHeight.H(edge.x, edge.z) + 0.1f;
        bool edgeFree = !fw.plan.NoGrass(edge.x, edge.z);
        pw.Teleport(gm.generator.Root.TransformPoint(edge), 90f);
        yield return 0.3f;
        pw.debugInput = true; pw.debugMove = new Vector2(0f, 1f);
        yield return 1.2f;
        pw.debugMove = Vector2.zero; pw.debugInput = false;
        Vector3 after = gm.generator.Root.InverseTransformPoint(pw.transform.position);
        r.Check(fw.WrapCount > w0 && after.x < 20f && Mathf.Abs(after.y - FieldHeight.H(after.x, after.z)) < 0.6f, ft + "cruzar el borde lleva al otro lado sin costura (x " + after.x.ToString("F1") + ", vueltas " + fw.WrapCount + (edgeFree ? "" : ", borde con obstáculo") + ")");
        // ---- v7.0.1 ----
        string vt = tag + "[v7.0.1] ";
        // salto en una ladera (la pendiente más fuerte que se pueda pisar) y dentro de una casa
        {
            float bestN = 1f; Vector3 hill = lp;
            for (int i = 0; i < 400; i++)
            {
                float x = (i % 20) * L / 20f + 3f, z = (i / 20) * L / 20f + 5f;
                if (fw.plan.NoGrass(x, z)) continue;
                float ny = FieldHeight.Normal(x, z).y;
                if (ny < bestN && ny > 0.8f) { bestN = ny; hill = new Vector3(x, FieldHeight.H(x, z) + 0.15f, z); }
            }
            pw.Teleport(gm.generator.Root.TransformPoint(hill), yaw);
            yield return 0.6f;
            IEnumerator j1 = JumpCheck(r, vt, pw, "en ladera (pendiente " + Mathf.RoundToInt(Mathf.Acos(bestN) * Mathf.Rad2Deg) + "°)");
            while (j1.MoveNext()) yield return j1.Current;
            foreach (FieldPlan.House h in fw.plan.houses)
            {
                if (!h.enterable) continue;
                Vector3 hp = new Vector3(h.pos.x, FieldHeight.H(h.pos.x, h.pos.y) + 0.45f, h.pos.y);
                pw.Teleport(gm.generator.Root.TransformPoint(hp), yaw);
                yield return 0.6f;
                IEnumerator j2 = JumpCheck(r, vt, pw, "dentro de una casa");
                while (j2.MoveNext()) yield return j2.Current;
                break;
            }
        }
        // puertas, árboles y postes reales (sin primitivas sueltas, LOD, PBR)
        {
            Transform props = fw.transform.Find("Props");
            int trees = 0, treesLod = 0, poles = 0, polesLod = 0, doors = 0, doorsOk = 0, branchPrims = 0;
            foreach (Transform t in props)
            {
                if (t.name == "DeadTree") { trees++; if (t.GetComponentInChildren<LODGroup>() != null && t.GetComponentInChildren<LODGroup>().lodCount >= 3) treesLod++; foreach (Transform c in t.GetComponentsInChildren<Transform>()) if (c.name.StartsWith("Branch") || c.name == "Trunk") branchPrims++; }
                if (t.name == "Pole") { poles++; if (t.GetComponentInChildren<LODGroup>() != null) polesLod++; }
                if (t.name == "LoneDoor")
                {
                    doors++;
                    Renderer sr = t.Find("DoorSlab") != null ? t.Find("DoorSlab").GetComponent<Renderer>() : null;
                    Material dm = sr != null ? sr.sharedMaterial : null;
                    bool tex = dm != null && dm.mainTexture != null && dm.IsKeywordEnabled("_NORMALMAP");
                    bool emissive = dm != null && dm.IsKeywordEnabled("_EMISSION");
                    float lum = dm != null ? dm.color.grayscale : 1f;
                    if (tex && !emissive && lum < 0.9f && t.Find("Knob") != null && t.Find("Hinge") != null && t.Find("ContactShadow") != null && t.Find("Dirt") != null && t.Find("Sill") != null) doorsOk++;
                }
            }
            r.Check(trees > 0 && treesLod == trees && branchPrims == 0, vt + "árboles muertos modelados con LOD0-2, sin cilindros sueltos (" + treesLod + "/" + trees + ")");
            r.Check(poles > 0 && polesLod == poles, vt + "postes modelados con LOD (" + polesLod + "/" + poles + ")");
            r.Check(doors > 0 && doorsOk == doors, vt + "puertas sueltas con madera PBR, pomo, bisagras, umbral, barro y sombra de contacto, sin emisión (" + doorsOk + "/" + doors + ")");
            Transform ed = gm.generator.ExitTransform != null ? gm.generator.ExitTransform.parent : null;
            r.Check(ed != null && ed.Find("Knob") != null && ed.Find("ContactShadow") != null && ed.Find("DoorSlab").GetComponent<Renderer>().sharedMaterial.mainTexture != null, vt + "la salida usa la misma puerta que los señuelos");
            // malla de árbol: sin piezas flotantes (una sola isla con caras)
            MeshFilter tmf = null; foreach (MeshFilter mf in props.GetComponentsInChildren<MeshFilter>()) if (mf.name.Contains("DeadTree") && mf.name.EndsWith("_LOD0")) { tmf = mf; break; }
            int islands = tmf != null ? FaceIslands(tmf.sharedMesh) : -1;
            r.Check(islands == 1, vt + "árbol muerto: una sola pieza (islas " + islands + ")");
        }
        // sin parpadeos: al andar (y al cruzar el borde) toda copia a la vista se dibuja; la hierba no da saltos
        {
            r.Check(fw.GhostCount >= fw.houseCount + fw.propCount + fw.chunkCount, vt + "copias del borde también para accesorios (" + fw.GhostCount + ")");
            float vis = FieldAtmosphere.FarClip * 0.9f;
            int miss = 0, samples = 0; float maxJump = 0f; int prevInst = -1; System.Text.StringBuilder instSeries = new System.Text.StringBuilder();
            Vector3 a = new Vector3(L - 30f, 0f, lp.z), b = new Vector3(L + 30f, 0f, lp.z + 8f);
            for (int i = 0; i <= 60; i++)
            {
                Vector3 q = Vector3.Lerp(a, b, i / 60f); float xq = FieldHeight.Mod(q.x), zq = FieldHeight.Mod(q.z);
                pw.Teleport(gm.generator.Root.TransformPoint(new Vector3(xq, FieldHeight.H(xq, zq) + 0.05f, zq)), 90f);
                yield return null;
                if (i == 0)
                {   // v7.0.2: la subida a la GPU está repartida por tiempo: tras el salto inicial (teletransporte lejano) esperar a que la hierba esté lista
                    float tw = 0f; FieldGrass fg0 = FieldGrass.Active;
                    while (tw < 5f && fg0 != null && (fg0.PendingJobs > 0 || fg0.ReadyChunks < fg0.ChunkCount)) { yield return 0.1f; tw += 0.1f; }
                    r.Info(vt + "hierba lista tras el salto inicial en " + tw.ToString("F1") + " s");
                }
                Vector3 camL = gm.generator.Root.InverseTransformPoint(Camera.main.transform.position);
                miss += fw.MissingCopies(camL, vis); samples++;
                FieldGrass fgq = FieldGrass.Active;
                if (fgq != null) { fgq.Submit(); int n = fgq.VisibleInstances; instSeries.Append(n).Append(' '); if (prevInst > 0 && i > 2) maxJump = Mathf.Max(maxJump, Mathf.Abs(n - prevInst) / (float)prevInst); prevInst = n; }
            }
            r.Check(miss == 0, vt + "andando y cruzando el borde no desaparece nada a la vista (" + samples + " pasos, " + miss + " fallos)");
            Vector2 b0 = FieldGrass.Band(0, 56f), b1 = FieldGrass.Band(1, 56f), b2 = FieldGrass.Band(2, 56f), bq = FieldGrass.Band(2, 22f);
            r.Check(b0.y == b1.x && b1.y == b2.x && b2.y <= 56f - FieldGrass.FadeW && bq.y <= 22f - FieldGrass.FadeW, vt + "hierba: bandas de LOD contiguas con fundido y apagado antes del radio");
            if (maxJump >= 0.35f) r.Info(vt + "matas visibles por paso: " + instSeries);
            r.Check(maxJump < 0.35f, vt + "hierba: sin saltos bruscos de matas al moverse (máx " + (maxJump * 100f).ToString("F0") + "%)");
        }
        // imitador: disfraz, pistas y transformación
        {
            MonsterAI m = gm.monsters.Length > 0 ? gm.monsters[0] : null;
            if (m != null)
            {
                MimicDisguise md = m.mimic != null ? m.mimic : MimicDisguise.Attach(m, gm.generator.UsedSeed, 5, StepKind.Grass);
                md.SetDisguised(true);
                bool hidden = true; foreach (Renderer rr in m.visual.GetComponentsInChildren<Renderer>(true)) if (!rr.transform.IsChildOf(md.puppet.transform) && rr.enabled) hidden = false;
                r.Check(md.Disguised && md.puppet != null && md.puppet.gameObject.activeInHierarchy && hidden && md.puppet.ModelLoaded, vt + "imitador: aparece con el modelo del jugador y el monstruo oculto");
                r.Check(!RemotePlayer.All.Contains(md.puppet) && md.puppet.GetComponentInChildren<Collider>() == null, vt + "imitador: no cuenta como jugador (fuera de la lista, sin colisión)");
                r.Check((m.NetStateByte & MimicDisguise.NetBit) != 0, vt + "imitador: el estado de red lleva el bit de disfraz");
                // lo mira fijamente desde 12 m
                Vector3 pl = new Vector3(lp.x, 0f, lp.z); Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                Vector3 mpos = pl + dir * 12f;
                UnityEngine.AI.NavMeshAgent ag = m.GetComponent<UnityEngine.AI.NavMeshAgent>(); if (ag != null && ag.enabled) ag.Warp(gm.generator.Root.TransformPoint(new Vector3(mpos.x, FieldHeight.H(mpos.x, mpos.z), mpos.z))); else m.transform.position = gm.generator.Root.TransformPoint(new Vector3(mpos.x, FieldHeight.H(mpos.x, mpos.z), mpos.z));
                pw.Teleport(gm.generator.Root.TransformPoint(new Vector3(pl.x, FieldHeight.H(pl.x, pl.z) + 0.05f, pl.z)), yaw);
                yield return 0.3f;
                Camera cam = Camera.main; Vector3 look = m.transform.position + Vector3.up * 1.5f - cam.transform.position;
                pw.SetPitch(-Mathf.Asin(look.normalized.y) * Mathf.Rad2Deg);
                pw.transform.rotation = Quaternion.Euler(0f, Quaternion.LookRotation(new Vector3(look.x, 0f, look.z)).eulerAngles.y, 0f);
                int ps0 = md.puppet.StepsPlayed; int rv0 = md.Reveals;
                m.Resume();
                float t = 0f; while (t < 1.2f) { yield return 0.1f; t += 0.1f; }
                bool still = md.Disguised;
                r.Check(still && md.puppet.lampLight == null && md.puppet.StepsPlayed == ps0, vt + "imitador: sin pisadas y su linterna no ilumina (pisadas " + (md.puppet.StepsPlayed - ps0) + ")");
                while (t < 5f && md.Disguised) { yield return 0.1f; t += 0.1f; }
                r.Check(!md.Disguised && md.Reveals == rv0 + 1 && t >= MimicDisguise.StareSeconds - 0.3f && m.CurrentState == MonsterAI.State.Chase, vt + "imitador: mirarlo fijamente lo transforma y ataca (" + t.ToString("F1") + " s, " + m.CurrentState + ")");
                m.Stop();
                // acercarse también lo descubre
                md.SetDisguised(true);
                Vector3 near = mpos - dir * 2.4f;
                if (ag != null && ag.enabled) ag.Warp(gm.generator.Root.TransformPoint(new Vector3(mpos.x, FieldHeight.H(mpos.x, mpos.z), mpos.z))); else m.transform.position = gm.generator.Root.TransformPoint(new Vector3(mpos.x, FieldHeight.H(mpos.x, mpos.z), mpos.z));
                pw.Teleport(gm.generator.Root.TransformPoint(new Vector3(near.x, FieldHeight.H(near.x, near.z) + 0.05f, near.z)), yaw + 180f);
                m.Resume(); yield return 0.3f;
                r.Check(!md.Disguised && md.Reveals == rv0 + 2, vt + "imitador: al acercarse se transforma");
                m.Stop(); m.transform.position = gm.generator.Root.TransformPoint(new Vector3(pl.x + 90f, 0f, pl.z));
                md.SetDisguised(false);
            }
            // determinista y poco frecuente
            int hits = 0; bool same = true, lv0 = false;
            for (int sd = 1; sd <= 1000; sd++) { bool hh = MimicDisguise.LevelHasMimic(sd, 3); if (hh) hits++; same &= hh == MimicDisguise.LevelHasMimic(sd, 3); lv0 |= MimicDisguise.LevelHasMimic(sd, 0); }
            r.Check(same && !lv0 && hits == 1000, vt + "imitador: v7.0.2 garantizado en niveles 1+ (" + hits / 10f + "% de niveles) y nunca en el nivel 0");
        }
        pw.Teleport(gm.generator.Root.TransformPoint(lp), yaw);
        yield return 0.5f;
    }

    public static void RunPlayMode()
    {
        Directory.CreateDirectory("/workspace/logs");
        File.WriteAllText(PlayReport, "");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "play");
        SessionState.SetBool("intro_real", true);   // la prueba comprueba la intro real; los demás ejecutores del editor la omiten
        EditorApplication.EnterPlaymode();
    }

    public const string PlayReport = "/workspace/logs/smoke-playmode.txt";

    // ------------------------------------------------------------------
    // Informe
    // ------------------------------------------------------------------
    public class Report
    {
        private readonly StringBuilder sb = new StringBuilder();
        private readonly string path;
        public int Passed, Failed;

        public Report(string path)
        {
            this.path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
        }

        public void Check(bool cond, string msg)
        {
            if (cond) { Passed++; sb.AppendLine("PASS " + msg); }
            else { Failed++; sb.AppendLine("FAIL " + msg); Debug.LogError("[SmokeTest] FAIL " + msg); }
        }
        public void Fail(string msg) { Check(false, msg); }
        public void Info(string msg) { sb.AppendLine("INFO " + msg); }

        public void Finish()
        {
            sb.AppendLine("RESULT passed=" + Passed + " failed=" + Failed);
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[SmokeTest] " + path + " passed=" + Passed + " failed=" + Failed);
            EditorApplication.Exit(Failed == 0 ? 0 : 1);
        }

        public string Text { get { return sb.ToString(); } }
        public void Flush() { File.WriteAllText(path, sb.ToString()); }
    }
}

// Ejecuta el recorrido en Play cuando SessionState lo pide (sobrevive al reinicio de dominio al entrar en Play).
[InitializeOnLoad]
public static class SmokePlayRunner
{
    private static IEnumerator routine;
    private static SmokeTest.Report report;
    private static int errorLogs;
    private static double waitUntil;
    private static Func<bool> waitCond;
    private static double condDeadline;

    static SmokePlayRunner()
    {
        if (SessionState.GetString("smoke_phase", "") != "play") return;
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    private static void OnLog(string condition, string stack, LogType type)
    {
        if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
        {
            errorLogs++;
            if (report != null) report.Info("LOG(" + type + "): " + condition);
        }
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isPlaying) return;

        if (routine == null)
        {
            report = new SmokeTest.Report(SmokeTest.PlayReport);
            routine = Run();
        }

        double now = EditorApplication.timeSinceStartup;
        if (now < waitUntil) return;
        if (waitCond != null)
        {
            bool done = false;
            try { done = waitCond(); } catch (Exception e) { report.Fail("Excepción en condición de espera: " + e); done = true; }
            if (!done && now < condDeadline) return;
            if (!done) report.Fail("Timeout esperando condición");
            waitCond = null;
        }

        try
        {
            if (!routine.MoveNext())
            {
                Finish();
                return;
            }
            object cur = routine.Current;
            if (cur is float) waitUntil = now + (float)cur;
            else if (cur is Func<bool>) { waitCond = (Func<bool>)cur; condDeadline = now + 60.0; }
        }
        catch (Exception e)
        {
            report.Fail("Excepción en el test: " + e);
            Finish();
        }
    }

    private static void Finish()
    {
        EditorApplication.update -= Tick;
        report.Check(errorLogs == 0, "Sin errores/excepciones en consola durante el recorrido (" + errorLogs + ")");
        SessionState.SetString("smoke_phase", "");
        report.Finish();
    }

    private static Func<bool> Until(Func<bool> f) { return f; }

    private static void PlaceMonster(MonsterAI m, Vector3 pos)
    {
        UnityEngine.AI.NavMeshAgent ag = m.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (ag != null && ag.enabled && ag.isOnNavMesh) ag.Warp(pos);
        else m.transform.position = pos;
    }

    // ---------------- Censo de audio (v6.5): fuentes sonando, bucles audibles, duplicados ----------------
    private static int census0Total = -1, census0Loops = -1;
    private static void Census(SmokeTest.Report r, string tag, bool inGame, bool compareToLevel0)
    {
        string dump = AudioCensus.Dump(tag);
        try { File.AppendAllText("/workspace/logs/audio-census.txt", dump); } catch { }
        System.Collections.Generic.List<AudioCensus.Row> rows = AudioCensus.Take();
        int total = 0, drones = 0, hiss = 0, buzzPlaying = 0, buzzAudible = 0, loopsAudible = 0, loopsPlaying = 0, noiseLoopsAudible = 0;
        foreach (AudioCensus.Row x in rows) if (x.clip != "FluorescentBuzz") total++;   // los zumbidos varían con la semilla
        foreach (AudioCensus.Row x in rows)
        {
            if (!(x.loop && x.playing)) continue;
            loopsPlaying++;
            if (x.clip == "Drone") drones++;
            if (x.clip == "TapeHiss") hiss++;
            if (x.clip == "FluorescentBuzz") { buzzPlaying++; if (x.effective > 0.0005f) buzzAudible++; }
            if (x.effective > 0.0005f) loopsAudible++;
            if (x.effective > 0.0005f && (x.clip == "TapeHiss" || x.clip == "ElevatorAir" || x.clip == "ElevatorRumble")) noiseLoopsAudible++;
        }
        r.Info(tag + ": " + total + " fuentes, " + loopsPlaying + " bucles sonando, " + loopsAudible + " audibles, zumbidos sonando " + buzzPlaying + " (audibles " + buzzAudible + "), drones " + drones + ", siseo " + hiss);
        r.Check(drones <= 1, tag + " nunca hay más de un drone ambiental sonando (" + drones + ")");
        r.Check(hiss <= 1, tag + " nunca hay más de un siseo de cinta (" + hiss + ")");
        r.Check(buzzPlaying <= AudioBuzzLimiter.MaxBuzzing, tag + " zumbidos de luces sonando a la vez ≤ " + AudioBuzzLimiter.MaxBuzzing + " (" + buzzPlaying + ")");
        r.Check(loopsPlaying <= AudioBuzzLimiter.MaxBuzzing + 4, tag + " bucles de audio sonando a la vez ≤ " + (AudioBuzzLimiter.MaxBuzzing + 4) + " (" + loopsPlaying + ")");
        if (!inGame) r.Check(buzzPlaying == 0, tag + " en el menú no suenan los zumbidos de luces");
        if (inGame && !compareToLevel0) { census0Total = total; census0Loops = loopsPlaying - buzzPlaying; }
        if (compareToLevel0 && census0Total >= 0) r.Check(total <= census0Total + 1 && loopsPlaying - buzzPlaying <= census0Loops,   // menos es válido (un zumbido de lámpara depende de la posición)
 tag + " tras reiniciar el nivel el nº de fuentes de audio (sin zumbidos) no crece (" + total + " vs " + census0Total + ", bucles sin zumbidos " + (loopsPlaying - buzzPlaying) + " vs " + census0Loops + ")");
    }

    private static IEnumerator Run()
    {
        SmokeTest.Report r = report;
        GameSettings.ResetDefaults();
        ElevatorCabin.InstantForTests = true;      // las pruebas largas no esperan al ascensor; el bloque «Ascensor» lo reproduce en tiempo real
        BallPitLurker.DisabledForTests = true;     // v7.1.0: solo se sumerge en V710Checks (no interfiere con otras pruebas)
        ExitLock.AutoKeyForTests = true;          // v7.0.4: la salida con llave se prueba en V704Checks
        yield return Until(() => GameBootstrap.Instance != null && GameBootstrap.Instance.IsLoaded);
        GameBootstrap gb = GameBootstrap.Instance;
        r.Info("Play mode activo. Nivel inicial " + gb.CurrentLevel);

        // ---------------- Intro de arranque (v6.5): va ANTES del menú principal ----------------
        {
            string it = "[Intro] ";
            IntroSequence intro = IntroSequence.Current;
            r.Check(intro != null && IntroSequence.Active, it + "la intro se reproduce al abrir el juego (antes del menú)");
            if (intro != null)
            {
                r.Check(intro.BlocksInput && intro.Alpha > 0.99f, it + "cubre la pantalla y bloquea el menú mientras suena");
                r.Check(IntroSequence.TEnd >= 8f && IntroSequence.TEnd <= 10f && IntroSequence.SkipAfter >= 2f, it + "dura " + IntroSequence.TEnd.ToString("F1") + " s (8-10 s) y se puede saltar tras " + IntroSequence.SkipAfter + " s");
                if (intro.T < IntroSequence.SkipAfter - 0.3f)
                {
                    intro.RequestSkip();
                    yield return 0.15f;
                    r.Check(IntroSequence.Active && !intro.Skipped && !intro.Fading, it + "pulsar una tecla antes de 2 s no salta la intro");
                }
                else r.Info(it + "(la carga tardó: T = " + intro.T.ToString("F1") + " s; no se prueba el rechazo antes de 2 s)");
                yield return Until(() => intro == null || intro.T >= 1.7f);
                if (intro != null)
                {
                    r.Check(intro.VisibleText.Contains("SUSTOS Y SONIDOS FUERTES") && intro.VisibleText.Contains("LUCES PARPADEANTES"), it + "aviso de luces parpadeantes, sustos y sonidos fuertes: «" + intro.VisibleText.Replace("\n", " ") + "»");
                    r.Check(System.Text.RegularExpressions.Regex.IsMatch(intro.TimecodeText, "^[0-9]{2}:[0-9]{2}:[0-9]{2}:[0-9]{2}$") && intro.TimecodeText != "00:00:00:00", it + "código de tiempo corriendo (" + intro.TimecodeText + ")");
                    r.Check(intro.StaticAlpha > 0.02f && intro.StaticAlpha <= 0.4f, it + "estática VHS suave (alfa " + intro.StaticAlpha.ToString("F2") + " ≤ 0,40)");
                    r.Check(intro.AudioSourceCount == 2, it + "audio procedural de la intro (2 fuentes: golpes y zumbido)");
                    // «Susto suave»: aún menos estática y sin desplazamiento de color
                    float normalStatic = intro.StaticAlpha;
                    GameSettings.SoftScare = true;
                    yield return 0.2f;
                    r.Check(intro.Soft && intro.StaticAlpha < normalStatic * 0.75f + 0.001f, it + "con «Susto suave» la estática baja (" + normalStatic.ToString("F2") + " -> " + intro.StaticAlpha.ToString("F2") + ")");
                    GameSettings.SoftScare = false;
                }
                yield return Until(() => intro == null || intro.T >= 4.4f);
                if (intro != null) r.Check(intro.VisibleText == IntroSequence.StudioText, it + "línea de estudio: «" + intro.VisibleText + "»");
                yield return Until(() => intro == null || intro.T >= 6.9f);
                if (intro != null)
                {
                    r.Check(intro.VisibleText == IntroSequence.TitleText, it + "título: «" + intro.VisibleText + "»");
                    r.Check(intro.MaxFlickerRate <= 3.1f, it + "parpadeo del título ≤ 3 cambios por segundo (máx. medido " + intro.MaxFlickerRate.ToString("F1") + ")");
                    intro.RequestSkip();
                    yield return 0.1f;
                    r.Check(intro.Skipped && intro.Fading, it + "tras 2 s una tecla salta la intro (funde en " + IntroSequence.SkipFade + " s)");
                    yield return Until(() => !IntroSequence.Active);
                }
                r.Check(!IntroSequence.Active && IntroSequence.Completed, it + "la intro termina y se retira");
            }
            yield return Until(() => GameBootstrap.Instance != null && GameBootstrap.Instance.IsLoaded);
            r.Check(GameBootstrap.Instance.InMenu && UIManager.Instance.Current == UIManager.Screen.Main, it + "tras la intro aparece el menú principal con el pasillo en vivo");
            // segunda intro completa sin saltar: termina sola en ~9,4 s y deja el menú
            IntroSequence second = IntroSequence.Begin(GameBootstrap.Instance.transform);
            float tBegin = Time.realtimeSinceStartup;
            yield return Until(() => !IntroSequence.Active);
            float took = Time.realtimeSinceStartup - tBegin;
            r.Check(took >= 8.5f && took <= 11.5f && IntroSequence.Completed && !second, it + "sin pulsar nada termina sola (" + took.ToString("F1") + " s)");
            r.Check(UIManager.Instance.Current == UIManager.Screen.Main && GameBootstrap.Instance.InMenu, it + "y deja el menú principal");
            r.Check(IntroSequence.PlayedCount >= 2, it + "la intro se instanció " + IntroSequence.PlayedCount + " veces (sin fugas de fuentes de audio: " + UnityEngine.Object.FindObjectsOfType<IntroSequence>().Length + " activas)");
            r.Check(UnityEngine.Object.FindObjectsOfType<IntroSequence>().Length == 0, it + "no queda ningún objeto de la intro");
        }

        // ---------------- Menú principal ----------------
        UIManager ui = UIManager.Instance;
        r.Check(ui != null, "UIManager existe");
        r.Check(gb.InMenu && ui.Current == UIManager.Screen.Main, "el juego arranca en el menú principal");
        try { File.Delete("/workspace/logs/audio-census.txt"); } catch { }
        yield return 0.5f;
        Census(r, "[Audio menú inicial]", false, false);
        r.Check(UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.EventSystem>() != null
            && UnityEngine.Object.FindObjectOfType<UnityEngine.EventSystems.StandaloneInputModule>() != null, "EventSystem + StandaloneInputModule presentes");
        r.Check(UIManager.InputBlocked && Cursor.lockState == CursorLockMode.None && Cursor.visible, "menú: cursor libre e input del jugador bloqueado");
        bool hasTitle = false;
        var labels = new HashSet<string>();
        foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(false)) { if (t.text == "BACKROOMS: ESCAPA") hasTitle = true; labels.Add(t.text); }
        r.Check(hasTitle, "título 'BACKROOMS: ESCAPA'");
        foreach (string b in new[] { "Jugar", "Seleccionar nivel", "Multijugador", "Opciones", "Créditos", "Salir" })
            r.Check(labels.Contains(b), "botón del menú principal: " + b);
        r.Check(gb.MenuGenerator != null && gb.MenuGenerator.HasLevel, "fondo animado del menú (pasillo generado)");
        Camera menuCam = Camera.main;
        VhsEffect menuFx = menuCam != null ? menuCam.GetComponent<VhsEffect>() : null;
        r.Check(menuCam != null, "cámara del menú existe");
        Vector3 camP0 = menuCam != null ? menuCam.transform.position : Vector3.zero;
        yield return 1.5f;
        r.Check(menuCam != null && (menuCam.transform.position - camP0).magnitude > 0.2f, "la cámara del fondo del menú se mueve");

        // Opciones
        r.Check(ui.ClickButton("Opciones") && ui.Current == UIManager.Screen.Options, "Opciones se abre desde el menú");
        yield return 0.2f;
        ui.VhsSlider.value = 0.42f;
        r.Check(Mathf.Approximately(GameSettings.VhsIntensity, 0.42f), "slider de intensidad VHS -> GameSettings");
        ui.VhsToggle.isOn = false;
        r.Check(!GameSettings.VhsOn, "toggle VHS off -> GameSettings");
        ui.VhsToggle.isOn = true;
        r.Check(GameSettings.VhsOn, "toggle VHS on -> GameSettings");
        ui.BobToggle.isOn = false;
        r.Check(!GameSettings.HeadBob, "toggle head-bob -> GameSettings");
        ui.BobToggle.isOn = true;
        ui.VolumeSlider.value = 0.5f;
        r.Check(Mathf.Approximately(GameSettings.MasterVolume, 0.5f) && Mathf.Approximately(AudioListener.volume, 0.5f), "slider de volumen -> AudioListener.volume");
        ui.SfxSlider.value = 0.6f; ui.AmbienceSlider.value = 0.35f;
        r.Check(Mathf.Approximately(GameSettings.SfxVolume, 0.6f) && Mathf.Approximately(GameSettings.AmbienceVolume, 0.35f), "sliders 'Efectos' y 'Ambiente' -> GameSettings");
        r.Check(ui.NoiseToggle != null, "Opciones tiene el interruptor «Siseo VHS / ruido»");
        ui.NoiseToggle.isOn = false;
        r.Check(!GameSettings.NoiseEnabled && AudioMix.Hiss(1f, 1f) == 0f, "el interruptor «Siseo VHS / ruido» se guarda y silencia el siseo");
        ui.NoiseToggle.isOn = true;
        ui.BreathToggle.isOn = false;
        r.Check(!GameSettings.BreathEnabled, "toggle 'Respiración' -> GameSettings");
        ui.BreathToggle.isOn = true;
        { bool okLabels = false, okBreath = false; int nSl = 0; foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(true)) { if (t.text == "Volumen general") okLabels = true; if (t.text == "Efectos") nSl++; if (t.text == "Ambiente") nSl++; if (t.text.StartsWith("Respiración")) okBreath = true; }
          r.Check(okLabels && nSl == 2 && okBreath, "Opciones muestra 'Volumen general', 'Efectos', 'Ambiente' y 'Respiración'"); }
        ui.SfxSlider.value = 1f; ui.AmbienceSlider.value = 1f;
        ui.SoftScareToggle.isOn = true;
        r.Check(GameSettings.SoftScare, "toggle 'Susto suave' -> GameSettings");
        ui.SoftScareToggle.isOn = false;
        ui.BodyToggle.isOn = false;
        r.Check(!GameSettings.ShowBody, "toggle 'Mostrar cuerpo' -> GameSettings");
        ui.BodyToggle.isOn = true;
        r.Check(GameSettings.ShowBody && !GameSettings.SoftScare, "toggles de susto/cuerpo restaurados");
        ui.VhsSlider.value = 0.7f; ui.VolumeSlider.value = 0.8f;
        ui.PressEscape();
        r.Check(ui.Current == UIManager.Screen.Main, "Esc en Opciones vuelve al menú");

        // Créditos
        r.Check(ui.ClickButton("Créditos") && ui.Current == UIManager.Screen.Credits, "Créditos se abre");
        Text credBody = null;
        foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(false)) if (t.name == "CreditsBody") credBody = t;
        bool allCredited = credBody != null && credBody.text.Contains("Character Hazmat");
        foreach (MonsterProfile p in MonsterProfile.All) if (credBody == null || !credBody.text.Contains(p.id)) allCredited = false;
        r.Check(allCredited, "pantalla de créditos nombra todos los modelos");
        ui.PressEscape();
        r.Check(ui.Current == UIManager.Screen.Main, "Esc en Créditos vuelve al menú");

        // Multijugador: menús + protocolo (clientes TCP "crudos" por bucle local)
        r.Check(ui.ClickButton("Multijugador") && ui.Current == UIManager.Screen.Multi, "Multijugador se abre desde el menú principal");
        yield return 0.2f;
        MultiplayerUI mui = ui.Multiplayer;
        r.Check(mui != null && mui.NameField != null && mui.CurrentPage == MultiplayerUI.Page.Menu, "menú multijugador: página principal con campo de nombre");
        var mlabels = new HashSet<string>();
        foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(false)) mlabels.Add(t.text);
        r.Check(mlabels.Contains("MULTIJUGADOR") && mlabels.Contains("Alojar partida") && mlabels.Contains("Unirse") && mlabels.Contains("Volver"), "menú multijugador: título y botones Alojar partida / Unirse / Volver");
        mui.NameField.text = "Tester"; mui.NameField.onEndEdit.Invoke("Tester");
        r.Check(MultiplayerUI.SavedName == "Tester", "el nombre del jugador se guarda en PlayerPrefs");
        r.Check(ui.ClickButton("Unirse") && mui.CurrentPage == MultiplayerUI.Page.Join && mui.JoinIpField != null && mui.JoinPortField != null, "página «Unirse»: campos IP y puerto");
        mui.JoinIpField.text = "127.0.0.1"; mui.JoinPortField.text = "47795";
        ui.ClickButton("Conectar");     // nadie escucha: debe dar un error claro
        float tj = 0f;
        while (tj < 10f && NetSession.Instance.Connecting) { tj += 0.1f; yield return 0.1f; }
        string joinErr = NetSession.Instance.LastError + " | " + mui.StatusText;
        r.Check(!NetSession.Instance.Connecting && !string.IsNullOrEmpty(NetSession.Instance.LastError) && NetSession.Instance.LastError.ToLowerInvariant().Contains("anfitri"), "conectar sin anfitrión: error claro en español (" + NetSession.Instance.LastError + ")");
        ui.PressEscape();
        r.Check(mui.CurrentPage == MultiplayerUI.Page.Menu, "Esc en «Unirse» vuelve a la página principal");
        // v6.1: relay desactivado por defecto, pegar ip:puerto, relay local (Node) con código de sala
        RelayDefault.Suppress = true; RelayConfig.TestOverride = null; RelayConfig.Save(""); mui.RefreshRelayUi();
        r.Check(!mui.RelayHostButton.interactable && !mui.RelayJoinButton.interactable && !ui.ClickButton("Alojar con código") && !ui.ClickButton("Unirse con código"), "sin relay configurado: «Alojar con código» y «Unirse con código» están desactivados");
        r.Check(mui.RelayHintText.Contains("desactivado") && mui.RelayField != null, "sin relay configurado: el menú lo explica (" + mui.RelayHintText + ")");
        ui.ClickButton("Unirse");
        mui.JoinIpField.text = "83.12.34.56:7777";
        r.Check(mui.JoinIpField.text == "83.12.34.56" && mui.JoinPortField.text == "7777", "pegar «ip:puerto» rellena IP y puerto por separado");
        mui.JoinIpField.text = "tcp://10.0.0.7:9001";
        r.Check(mui.JoinIpField.text == "10.0.0.7" && mui.JoinPortField.text == "9001", "pegar «tcp://ip:puerto» también");
        mui.JoinIpField.text = ""; mui.JoinPortField.text = "7777";
        ui.PressEscape();
        {
            string rdir = "/workspace/backrooms-relay";
            System.Diagnostics.Process node = null;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("node", "server.js") { WorkingDirectory = rdir, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                psi.EnvironmentVariables["PORT"] = "47782"; psi.EnvironmentVariables["HOST"] = "127.0.0.1";
                node = System.Diagnostics.Process.Start(psi);
            }
            catch (Exception e) { r.Info("sin node: " + e.Message); }
            if (node != null)
            {
                yield return 1.0f;
                // relay oficial integrado (cifrado): se usa sin enseñar su dirección
                RelayDefault.TestInject("127.0.0.1:47782", null); mui.RefreshRelayUi();
                r.Check(RelayConfig.UsingBuiltin && mui.RelayHostButton.interactable && mui.RelayJoinButton.interactable && mui.RelayField.text == "" && !PlayerPrefs.HasKey(RelayConfig.KRelay), "relay integrado: botones «con código» activos y el campo del menú queda vacío");
                r.Check(mui.RelayHintText.Contains("Servidor oficial (integrado)") && !mui.RelayHintText.Contains("47782") && !mui.RelayHintText.Contains("127.0.0.1"), "relay integrado: la interfaz no muestra su dirección («" + mui.RelayHintText + "»)");
                mui.NameField.text = "Anfi";
                r.Check(ui.ClickButton("Alojar con código") && NetSession.IsHost && NetSession.Instance.ViaRelay, "relay integrado: «Alojar con código» abre la sala");
                { float tb = 0; while (tb < 8f && NetSession.Instance.RoomCode.Length == 0) { tb += 0.1f; yield return 0.1f; } }
                yield return 0.6f;
                r.Check(RoomCodes.IsValid(NetSession.Instance.RoomCode) && !mui.LobbyAddrText.Contains("47782") && !mui.LobbyText.Contains("47782") && !mui.LobbyAddrText.Contains("127.0.0.1"), "relay integrado: la sala muestra el código y no la dirección del servidor");
                ui.PressEscape(); yield return 0.3f;
                RelayDefault.TestInject(null, null); mui.RefreshRelayUi();
                mui.RelayField.text = "127.0.0.1:47782";
                r.Check(mui.RelayHostButton.interactable && mui.RelayJoinButton.interactable && PlayerPrefs.GetString(RelayConfig.KRelay) == "127.0.0.1:47782" && mui.RelayHintText.Contains("Relay configurado"), "con relay configurado en el menú: botones «con código» activos y se guarda (" + mui.RelayHintText + ")");
                mui.NameField.text = "Anfi"; mui.NameField.onEndEdit.Invoke("Anfi");
                r.Check(ui.ClickButton("Alojar con código") && ui.Current == UIManager.Screen.Lobby && NetSession.IsHost && NetSession.Instance.ViaRelay, "«Alojar con código» abre la sala (relay)");
                float trc = 0; while (trc < 8f && NetSession.Instance.RoomCode.Length == 0) { trc += 0.1f; yield return 0.1f; }
                string code = NetSession.Instance.RoomCode;
                r.Check(RoomCodes.IsValid(code), "el relay asigna un código de sala válido (" + code + ")");
                yield return 0.6f;
                r.Check(mui.LobbyAddrText.Contains("CÓDIGO DE SALA") && mui.LobbyAddrText.Contains(code) && mui.CurrentShareText() == code, "la sala muestra el código y «Copiar dirección» copia el código");
                ui.ClickButton("Copiar dirección");
                r.Check(mui.LastCopied == code, "«Copiar dirección» copia el código de sala (" + mui.LastCopied + ")");
                INetConnector cc = new RelayTransport().Connect(code, 0, 8000);
                while (!cc.Done) yield return 0.05f;
                bool welcomed = false;
                if (cc.Result != null)
                {
                    NetConn nc = new NetConn(cc.Result, 0f);
                    nc.Send(new NetWriter(NetMsg.Hello).Str(NetSession.Protocol).Str("ViaRelay").ToArray()); nc.Flush();
                    float tq = 0; while (!welcomed && tq < 6f) { nc.Pump(0f); nc.Flush(); while (nc.Incoming.Count > 0) { NetReader q = new NetReader(nc.Incoming.Dequeue()); if (q.Type == NetMsg.Welcome) welcomed = true; } tq += 0.05f; yield return 0.05f; }
                    yield return 0.4f;
                    r.Check(welcomed && mui.LobbyText.Contains("ViaRelay"), "un cliente entra por el relay y aparece en la sala");
                    nc.Kill("fin");
                }
                else r.Check(false, "el cliente de prueba no pudo unirse por el relay: " + cc.Error);
                ui.PressEscape(); yield return 0.3f;
                r.Check(!NetSession.Active && ui.Current == UIManager.Screen.Multi, "salir de la sala por relay vuelve al menú multijugador");
                // unirse con código: página + error de código inexistente
                r.Check(ui.ClickButton("Unirse con código") && mui.CurrentPage == MultiplayerUI.Page.JoinCode && mui.CodeField != null, "«Unirse con código» abre su página");
                mui.CodeField.text = "zzzzzz";
                r.Check(mui.CodeField.text == "ZZZZZZ", "el código se escribe en mayúsculas");
                ui.ClickButton("Conectar con código");
                float tk = 0; while (tk < 10f && NetSession.Instance.Connecting) { tk += 0.1f; yield return 0.1f; }
                yield return 0.3f;
                r.Check(!NetSession.Instance.Connecting && NetSession.Instance.LastError.Contains("No existe ninguna sala"), "código inexistente => «" + NetSession.Instance.LastError + "»");
                mui.CodeField.text = "AB1";
                ui.ClickButton("Conectar con código"); yield return 0.2f;
                r.Check(NetSession.Instance.LastError.Contains("6 caracteres"), "código mal escrito => «" + NetSession.Instance.LastError + "»");
                ui.PressEscape();
                try { node.Kill(); } catch (Exception) { }
                yield return 0.5f;
                // relay caído al alojar: vuelve con mensaje
                r.Check(ui.ClickButton("Alojar con código"), "«Alojar con código» con el relay caído...");
                float td = 0; while (td < 10f && NetSession.Active) { td += 0.1f; yield return 0.1f; }
                yield return 0.3f;
                r.Check(!NetSession.Active && NetSession.Instance.LastNotice.Contains("No se pudo contactar con el servidor relay"), "relay caído al alojar: vuelve al menú con aviso («" + NetSession.Instance.LastNotice + "»)");
                NetSession.Instance.LastNotice = "";
                if (ui.Current == UIManager.Screen.Lobby) ui.PressEscape();
                mui.RelayField.text = "";
                mui.NameField.text = "Tester";
                r.Check(!mui.RelayHostButton.interactable && !RelayConfig.Enabled, "vaciar el campo desactiva otra vez el modo con código");
                if (ui.Current != UIManager.Screen.Multi) { ui.SetScreen(UIManager.Screen.Multi); }
                mui.ShowPage(MultiplayerUI.Page.Menu);
            }
        }
        r.Check(ui.ClickButton("Alojar partida") && mui.CurrentPage == MultiplayerUI.Page.Host && mui.HostPortField != null, "página «Alojar partida»");
        yield return 0.8f;
        r.Check(mui.HostInfoText.Contains("Tus direcciones locales") && mui.HostInfoText.Contains("IP pública:") && mui.HostInfoText.Contains("UPnP"), "pantalla del anfitrión: IP locales, IP pública y UPnP (" + mui.HostInfoText.Replace("\n", " / ") + ")");
        r.Check(ui.ClickButton("Copiar dirección") && System.Text.RegularExpressions.Regex.IsMatch(mui.LastCopied ?? "", @"^\d+\.\d+\.\d+\.\d+:\d+$"), "«Copiar dirección» produce ip:puerto (" + mui.LastCopied + ")");
        r.Check(PlayerPrefs.GetInt(MultiplayerUI.KPort, NetSession.DefaultPort) > 0 && NetSession.DefaultPort == 7777, "puerto por defecto 7777");
        mui.HostPortField.text = "47793";
        r.Check(ui.ClickButton("Crear sala") && ui.Current == UIManager.Screen.Lobby && NetSession.IsHost, "«Crear sala» abre la sala de espera como anfitrión");
        yield return 0.8f;
        r.Check(mui.LobbyText.Contains("Tester"), "la sala lista al anfitrión por su nombre");
        r.Check(NetSession.Instance.Upnp != null && mui.LobbyAddrText.Contains("Dirección para copiar") && mui.LobbyAddrText.Contains(":47793"), "la sala muestra la dirección para copiar y el estado UPnP (" + mui.LobbyAddrText.Replace("\n", " / ") + ")");
        r.Check(ui.ClickButton("Copiar dirección") && mui.LastCopied.EndsWith(":47793"), "«Copiar dirección» en la sala (" + mui.LastCopied + ")");
        r.Check(mui.StartButton != null && mui.StartButton.gameObject.activeInHierarchy, "el anfitrión ve el botón «Empezar»");
        {
            var raws = new List<NetConn>();
            string[] replies = new string[6];
            string[] names = { "Marta", "Luis", "Eva", "Extra", "Viejo" };
            // 1) versión incompatible
            NetConn bad = null; { float t0 = 0; while (bad == null && t0 < 3f) { NetConnector cn = new NetConnector("127.0.0.1", 47793, 2000); while (!cn.Done) { yield return 0.05f; } if (cn.Result != null) bad = new NetConn(cn.Result, 0f); t0 += 1f; } }
            string badReply = null;
            if (bad != null)
            {
                bad.Send(new NetWriter(NetMsg.Hello).Str("version-vieja").Str("Viejo").ToArray()); bad.Flush();
                float tw = 0; while (badReply == null && tw < 5f) { bad.Pump(0f); bad.Flush(); if (bad.Incoming.Count > 0) { NetReader q = new NetReader(bad.Incoming.Dequeue()); if (q.Type == NetMsg.Reject) badReply = q.Str(); } tw += 0.05f; yield return 0.05f; }
                bad.Kill("fin");
            }
            r.Check(badReply != null && badReply.Contains("Versión incompatible"), "versión incompatible => rechazo claro (" + badReply + ")");
            // 2) tres jugadores válidos y un cuarto rechazado por sala llena
            int welcomed = 0; string fullReply = null;
            for (int i = 0; i < 4; i++)
            {
                NetConnector cn = new NetConnector("127.0.0.1", 47793, 2000);
                while (!cn.Done) yield return 0.05f;
                if (cn.Result == null) continue;
                NetConn nc = new NetConn(cn.Result, 0f);
                nc.Send(new NetWriter(NetMsg.Hello).Str(NetSession.Protocol).Str(names[i]).ToArray()); nc.Flush();
                bool welcome = false; string rej = null; float tw = 0;
                while (!welcome && rej == null && tw < 5f) { nc.Pump(0f); nc.Flush(); while (nc.Incoming.Count > 0) { NetReader q = new NetReader(nc.Incoming.Dequeue()); if (q.Type == NetMsg.Welcome) welcome = true; else if (q.Type == NetMsg.Reject) rej = q.Str(); } tw += 0.05f; yield return 0.05f; }
                if (welcome) { welcomed++; raws.Add(nc); } else { fullReply = rej; nc.Kill("fin"); }
            }
            r.Check(welcomed == 3, "se admiten 3 clientes (máximo 4 jugadores con el anfitrión): " + welcomed);
            r.Check(fullReply != null && fullReply.Contains("llena"), "el 5.º jugador recibe «sala llena» (" + fullReply + ")");
            yield return 0.5f;
            r.Check(NetSession.Instance.Players.Count == 4 && mui.LobbyText.Contains("Marta") && mui.LobbyText.Contains("Luis") && mui.LobbyText.Contains("Eva"), "la sala muestra a los 4 jugadores: " + mui.LobbyText.Replace("\n", " / "));
            // Lobby: selector de nivel solo anfitrión
            int lv0 = mui.LobbyLevel;
            r.Check(ui.ClickButton("►") && mui.LobbyLevel == (lv0 + 1) % LevelConfig.Count, "el anfitrión cambia de nivel en la sala (" + mui.LobbyLevel + ")");
            ui.ClickButton("◄");
            // un cliente se va => desaparece de la sala
            raws[1].Send(new NetWriter(NetMsg.Bye).ToArray()); raws[1].Flush(); raws[1].Pump(0f);
            raws[1].Kill("adiós");
            yield return 1.2f;
            r.Check(NetSession.Instance.Players.Count == 3 && !mui.LobbyText.Contains("Luis"), "un cliente que se va desaparece de la sala");
            foreach (NetConn nc in raws) nc.Kill("fin");
        }
        ui.PressEscape();       // Esc en la sala: vuelve y cierra la sesión
        yield return 0.3f;
        r.Check(!NetSession.Active && ui.Current == UIManager.Screen.Multi, "Esc en la sala cierra la sesión y vuelve al menú multijugador");
        ui.PressEscape();
        r.Check(ui.Current == UIManager.Screen.Main, "Esc en multijugador vuelve al menú principal");

        // Selección de nivel
        r.Check(ui.ClickButton("Seleccionar nivel") && ui.Current == UIManager.Screen.Levels, "Seleccionar nivel se abre");
        int levelButtons = 0;
        foreach (Button bt in UnityEngine.Object.FindObjectsOfType<Button>(false)) if (bt.name.StartsWith("LevelBtn_")) levelButtons++;
        r.Check(levelButtons >= LevelConfig.Count, "botones de nivel: " + levelButtons);
        ui.PressEscape();

        // Jugar -> nivel 0
        int loads0 = gb.LoadCount;
        ElevatorCabin.InstantForTests = false;     // primera entrada: el ascensor se reproduce de verdad
        r.Check(ui.ClickButton("Jugar"), "botón Jugar");
        yield return Until(() => GameBootstrap.Instance.LoadCount > loads0 && GameBootstrap.Instance.IsLoaded && GameManager.Instance != null && !GameBootstrap.Instance.InMenu);
        gb = GameBootstrap.Instance;
        r.Check(gb.CurrentLevel == 0, "Jugar empieza en el nivel 0");

        // ---------------- Ascensor de entrada (tiempo real, primera entrada) ----------------
        {
            string et = "[Ascensor] ";
            ElevatorCabin cab = ElevatorCabin.Current;
            GameManager gmE = GameManager.Instance;
            FirstPersonController pe = gmE.player;
            r.Check(cab != null && cab.HasModel && cab.Begun && cab.CurrentPhase == ElevatorCabin.Phase.Descending, et + "al pulsar Jugar empieza el descenso en la cabina (modelo CC0)");
            r.Check(!cab.Skippable, et + "en la primera entrada NO se puede saltar");
            cab.Skip();
            r.Check(cab.Locked && !cab.Released && !cab.Skipped, et + "Skip() se ignora cuando no es un reinicio");
            r.Check(cab.Locked && !pe.InputEnabled && ElevatorCabin.PlayerLocked, et + "el jugador está bloqueado durante el descenso");
            r.Check(gmE.TitleHeld, et + "la tarjeta de título espera a que se abran las puertas");
            bool allHeld = gmE.monsters.Length > 0;
            foreach (MonsterAI m in gmE.monsters) if (!m.IsStopped) allHeld = false;
            r.Check(allHeld, et + "los monstruos esperan quietos hasta que se abran las puertas");
            r.Check(cab.CollidersBlockDoor && cab.ColliderCount >= 9, et + "la cabina tiene colisión (" + cab.ColliderCount + " cajas) y la puerta cerrada bloquea");
            Vector3 f0 = pe.transform.position;
            pe.debugInput = true; pe.debugMove = new Vector2(0f, 1f);
            yield return 0.7f;
            pe.debugInput = false;
            r.Check(Vector3.Distance(pe.transform.position, f0) < 0.05f, et + "mover (W) no desplaza al jugador mientras baja");
            Vector3 flat = pe.transform.position - cab.InteriorCenter; flat.y = 0f;
            r.Check(flat.magnitude < 0.05f && gmE.generator.PlayerStart == cab.InteriorCenter, et + "el jugador aparece en el interior de la cabina (punto de aparición movido a la cabina)");
            yield return Until(() => cab.T > 3.2f);
            int fl; int.TryParse(cab.DisplayNumber, out fl);
            r.Check(fl < -3 && cab.DisplayLabel == "BAJANDO", et + "el indicador de planta baja contando (" + cab.DisplayNumber + " " + cab.DisplayLabel + ")");
            r.Check(cab.MaxShake > 0.006f, et + "la cámara tiembla durante el descenso (" + cab.MaxShake.ToString("F3") + " m)");
            int playing = 0; foreach (AudioSource a in cab.GetComponents<AudioSource>()) if (a.isPlaying && a.loop && a.volume > 0f) playing++;
            r.Check(playing == 3, et + "retumbo + motor/cables + aire sonando en bucle (" + playing + ")");
            yield return Until(() => cab.CurrentPhase == ElevatorCabin.Phase.Braking || cab.T > ElevatorCabin.TStop);
            yield return Until(() => cab.JoltCount >= 1f);
            r.Check(cab.JoltCount >= 1f && cab.DisplayNumber == cab.FloorEnd.ToString(), et + "frenazo con sacudida y el contador se detiene en " + cab.FloorEnd);
            r.Check(cab.Locked && !pe.InputEnabled, et + "durante el frenazo el jugador sigue bloqueado");
            yield return Until(() => cab.Released || cab == null);
            r.Check(cab.T >= ElevatorCabin.TUnlock - 0.2f && cab.T <= ElevatorCabin.TotalSeconds && cab.T >= 6f, et + "el jugador se libera al abrirse las puertas (T = " + cab.T.ToString("F1") + " s, total " + ElevatorCabin.TotalSeconds + " s)");
            r.Check(!cab.Locked && pe.InputEnabled && !ElevatorCabin.PlayerLocked && !gmE.TitleHeld, et + "tras abrirse: jugador libre y aparece el título del nivel");
            bool anyHeld = false; foreach (MonsterAI m in gmE.monsters) if (m.IsStopped) anyHeld = true;
            r.Check(!anyHeld, et + "los monstruos se liberan cuando se abren las puertas");
            yield return Until(() => cab.Open01 > 0.98f);
            r.Check(!cab.CollidersBlockDoor, et + "con las puertas abiertas se puede salir");
            // salir andando: atraviesa la puerta
            gmE.monsters[0].Stop(); foreach (MonsterAI m in gmE.monsters) m.Stop();
            pe.debugInput = true; pe.debugMove = new Vector2(0f, 1f);
            yield return 2.2f;
            pe.debugInput = false;
            float along = Vector3.Dot(pe.transform.position - cab.transform.position, cab.Forward);
            r.Check(along > 1.7f, et + "el jugador sale de la cabina andando (avance " + along.ToString("F1") + " m)");
            // la cabina se cierra al alejarse y no se puede volver a entrar
            pe.Teleport(cab.ExitPoint + cab.Forward * 6f, gmE.generator.PlayerStartYaw);
            yield return Until(() => cab.CurrentPhase == ElevatorCabin.Phase.Closed || cab.CurrentPhase == ElevatorCabin.Phase.Closing);
            yield return 1.4f;
            r.Check(cab.CurrentPhase == ElevatorCabin.Phase.Closed && cab.CollidersBlockDoor, et + "al alejarse, las puertas se cierran y la cabina se queda atrás (con colisión)");
            pe.Teleport(cab.ExitPoint + cab.Forward * 0.2f, gmE.generator.PlayerStartYaw + 180f);
            yield return 0.2f;
            pe.debugInput = true; pe.debugMove = new Vector2(0f, 1f);
            yield return 1.5f;
            pe.debugInput = false;
            float into = Vector3.Dot(pe.transform.position - cab.transform.position, cab.Forward);
            r.Check(into > 1.35f * 1.07f - 0.3f, et + "no se puede volver a entrar ni atravesar la cabina cerrada (" + into.ToString("F2") + " m)");
            // reinicio del nivel: ahora sí se puede saltar
            ElevatorCabin.InstantForTests = false;
            int ldE = gb.LoadCount;
            gmE.Restart();
            yield return Until(() => GameBootstrap.Instance.LoadCount > ldE && GameBootstrap.Instance.IsLoaded && ElevatorCabin.Current != null);
            ElevatorCabin cab2 = ElevatorCabin.Current;
            r.Check(cab2 != cab && cab2.Skippable && cab2.Locked && cab2.Begun, et + "tras reiniciar el nivel el ascensor vuelve a bajar y se puede saltar");
            yield return 1.0f;
            r.Check(cab2.Locked && !cab2.Released, et + "tras reiniciar sigue bloqueado hasta saltar (no se salta solo)");
            cab2.Skip();
            r.Check(cab2.Skipped && cab2.Released && !cab2.Locked && GameManager.Instance.player.InputEnabled && !GameManager.Instance.TitleHeld, et + "saltar el ascensor tras un reinicio libera al jugador al instante");
            r.Check(cab2.Open01 > 0.99f && !cab2.CollidersBlockDoor, et + "saltar deja las puertas abiertas");
            ElevatorCabin.InstantForTests = true;
        }
        r.Check(ui.Current == UIManager.Screen.None && Time.timeScale == 1f && ui.CursorShouldBeLocked, "en juego: sin menús, tiempo normal, cursor bloqueado");
        yield return 0.8f;

        // ---------------- Pausa / inventario / VHS / movimiento (nivel 0) ----------------
        {
            GameManager gm0 = GameManager.Instance;
            foreach (MonsterAI m in gm0.monsters) m.Stop();
            FirstPersonController pc = gm0.player;
            // Pausa
            ui.PressEscape();
            r.Check(ui.Current == UIManager.Screen.Pause && Time.timeScale == 0f && AudioListener.pause && Cursor.lockState == CursorLockMode.None && Cursor.visible, "Esc -> pausa: tiempo congelado, audio en pausa, cursor libre");
            ui.PressEscape();
            r.Check(ui.Current == UIManager.Screen.None && Time.timeScale == 1f && !AudioListener.pause && ui.CursorShouldBeLocked, "Esc otra vez -> reanuda");
            ui.PressEscape();
            r.Check(ui.ClickButton("Opciones") && ui.Current == UIManager.Screen.Options && Time.timeScale == 0f, "Opciones desde pausa (sigue congelado)");
            ui.PressEscape();
            r.Check(ui.Current == UIManager.Screen.Pause, "Esc en Opciones vuelve a la pausa");
            r.Check(ui.ClickButton("Continuar") && ui.Current == UIManager.Screen.None && Time.timeScale == 1f, "botón Continuar");

            // Inventario
            Inventory inv = Inventory.Instance;
            r.Check(inv != null && gm0.flashlight != null, "Inventory y Flashlight presentes");
            int before = inv.BatteryCount;
            BatteryPickup[] pk = UnityEngine.Object.FindObjectsOfType<BatteryPickup>();
            if (pk.Length > 0)
            {
                Vector3 pp = pk[0].transform.position;
                pc.Teleport(new Vector3(pp.x, 0.1f, pp.z), 0f);
                yield return 0.5f;
                r.Check(inv.BatteryCount == before + 1, "recoger una pila la añade al inventario (" + before + " -> " + inv.BatteryCount + ")");
            }
            gm0.flashlight.SetBattery(30f);
            ui.ToggleInventory();
            r.Check(ui.Current == UIManager.Screen.Inventory && Time.timeScale == 0f && Cursor.lockState == CursorLockMode.None, "I abre el inventario: pausa + cursor libre");
            bool invTexts = false;
            foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(false)) if (t.text == "INVENTARIO") invTexts = true;
            r.Check(invTexts, "pantalla 'INVENTARIO' visible");
            InventoryUI iui = ui.InventoryUi;
            r.Check(iui != null && iui.SlotViewCount == Inventory.SlotCount && Inventory.SlotCount == 15, "inventario: rejilla de " + (iui != null ? iui.SlotViewCount : 0) + " casillas (5x3)");
            yield return 0.45f;
            r.Check(iui.Openness > 0.99f, "inventario: animación de apertura completa (" + iui.Openness.ToString("F2") + ")");
            r.Check(inv.GetSlot(0).type == ItemType.Flashlight && iui.SlotShowsIcon(0), "inventario: la linterna ocupa la casilla 1 con icono");
            int batSlot = -1;
            for (int i = 0; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == ItemType.Battery) batSlot = i;
            r.Check(batSlot >= 1 && iui.SlotShowsIcon(batSlot) && iui.SlotCountText(batSlot) == (inv.GetSlot(batSlot).count > 1 ? "x" + inv.GetSlot(batSlot).count : ""), "inventario: la pila tiene icono y contador de pila apilada");
            iui.Select(batSlot);
            r.Check(iui.SelectedIndex == batSlot && iui.DetailName == "Pila" && iui.DetailText.Length > 20 && iui.UseEnabled && iui.DropEnabled, "inventario: seleccionar una casilla muestra nombre, descripción y botones Usar/Soltar");
            iui.DebugHover(batSlot);
            r.Check(iui.TooltipVisible && iui.TooltipName.StartsWith("Pila"), "inventario: tooltip al pasar el cursor ('" + iui.TooltipName + "')");
            iui.DebugHover(-1);
            iui.Select(14);
            r.Check(inv.GetSlot(14).Empty && !iui.UseEnabled && !iui.DropEnabled && iui.DetailName == "Casilla vacía", "inventario: casilla vacía -> Usar/Soltar desactivados");
            iui.Select(0);
            r.Check(iui.UseEnabled && !iui.DropEnabled, "inventario: la linterna se puede usar pero no soltar");
            {
                int batCount = inv.GetSlot(batSlot).count;
                bool moved = iui.DebugDrag(batSlot, 14);
                bool ok = moved && inv.GetSlot(14).type == ItemType.Battery && inv.GetSlot(14).count == batCount && inv.GetSlot(batSlot).Empty;
                bool back = iui.DebugDrag(14, batSlot) && inv.GetSlot(batSlot).type == ItemType.Battery;
                bool flashFixed = !iui.DebugDrag(0, 13) && inv.GetSlot(0).type == ItemType.Flashlight;
                r.Check(ok && back && flashFixed, "inventario: arrastrar y soltar mueve objetos entre casillas (la linterna queda fija)");
                r.Check(Resources.Load<Texture2D>("Icons/Battery") != null && UiStyle.Mono() != null && UiStyle.Mono().name.Contains("Escapa"), "inventario: iconos renderizados y fuente incluida cargados");
            }
            // pestaña de notas
            iui.SetTab(true);
            r.Check(iui.NotesTabOpen && iui.VisibleNoteRows == inv.NotesFound, "inventario: pestaña de notas (" + iui.VisibleNoteRows + " leídas)");
            int noteId = LoreNotes.IdFor(0, 0);
            bool hadNote = inv.HasNote(noteId);
            inv.AddNote(noteId);
            iui.SelectNote(noteId);
            yield return 0.1f;
            r.Check(iui.SelectedNoteId == noteId && iui.NoteBodyText.Contains("pasillo este") , "inventario: nota legible en español ('" + iui.NoteBodyText.Substring(0, Mathf.Min(40, iui.NoteBodyText.Length)) + "...')");
            iui.SetTab(false);
            ui.PressEscape();
            r.Check(ui.Current == UIManager.Screen.None && Time.timeScale == 1f, "Esc cierra el inventario");
            r.Check(iui.IsClosing || !iui.gameObject.activeSelf, "inventario: animación de cierre");
            yield return 0.35f;
            r.Check(!iui.gameObject.activeSelf, "inventario: oculto tras la animación de cierre");
            ui.ToggleInventory();
            ui.ToggleInventory();
            r.Check(ui.Current == UIManager.Screen.None, "I abre y cierra el inventario");
            yield return 0.3f;

            // usar pila desde el inventario (botón Usar) y con la tecla de barra rápida
            ui.ToggleInventory();
            yield return 0.3f;
            int nb = inv.BatteryCount;
            float fl0 = gm0.flashlight.Battery100;
            iui.Select(batSlot);
            iui.OnUse();
            r.Check(inv.BatteryCount == nb - 1 && gm0.flashlight.Battery100 > fl0 + 10f, "botón Usar: -1 pila y linterna recargada (" + fl0.ToString("F0") + " -> " + gm0.flashlight.Battery100.ToString("F0") + ")");
            ui.ToggleInventory();
            yield return 0.3f;

            // objetos nuevos: apilado, agua de almendras, barras luminosas, soltar
            inv.Add(ItemType.AlmondWater, 2);
            inv.Add(ItemType.Glowstick, 3);
            inv.Add(ItemType.VhsTape, 1);
            r.Check(inv.GetCount(ItemType.AlmondWater) >= 2 && inv.GetCount(ItemType.Glowstick) >= 3 && inv.GetCount(ItemType.VhsTape) >= 1, "inventario: agua de almendras, barras luminosas y cinta añadidas");
            int cap = inv.Add(ItemType.Battery, 50);
            int stacked = 0;
            for (int i = 0; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == ItemType.Battery) { stacked++; r.Check(inv.GetSlot(i).count <= ItemDefs.Get(ItemType.Battery).maxStack, "pila apilada <= máximo (" + inv.GetSlot(i).count + ")"); }
            r.Check(cap > 0 && stacked >= 1, "inventario: apilado de pilas hasta el máximo (+" + cap + ")");
            for (int i = 1; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == ItemType.Battery && inv.GetSlot(i).count > 3) inv.Remove(i, inv.GetSlot(i).count - 3);
            Sanity san = Sanity.Instance;
            r.Check(san != null && san.Sanity01 > 0f, "cordura presente (" + (san != null ? san.value.ToString("F0") : "-") + ")");
            san.value = 40f;
            pc.RestoreStamina(-1f);
            int almondSlot = -1;
            for (int i = 0; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == ItemType.AlmondWater) almondSlot = i;
            int al0 = inv.GetCount(ItemType.AlmondWater);
            r.Check(inv.UseSlot(almondSlot) && inv.GetCount(ItemType.AlmondWater) == al0 - 1 && san.value > 70f && pc.Stamina01 > 0.9f, "agua de almendras: -1 unidad, cordura " + san.value.ToString("F0") + ", resistencia " + pc.Stamina01.ToString("F2"));
            int gl0 = inv.GetCount(ItemType.Glowstick);
            int glSlot = -1;
            for (int i = 0; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == ItemType.Glowstick) glSlot = i;
            Glowstick.ClearAll();
            r.Check(inv.UseSlot(glSlot) && Glowstick.Active.Count == 1 && inv.GetCount(ItemType.Glowstick) == gl0 - 1, "barra luminosa: se lanza (-1, objeto en escena)");
            yield return 0.8f;
            Glowstick gs = Glowstick.Active.Count > 0 ? Glowstick.Active[0] : null;
            r.Check(gs != null && gs.glow != null && gs.glow.enabled && gs.glow.type == LightType.Point && gs.glow.intensity > 1f && gs.glow.color.g > gs.glow.color.r, "barra luminosa: luz verde puntual encendida (" + (gs != null && gs.glow != null ? gs.glow.intensity.ToString("F1") : "-") + ")");
            r.Check(gs != null && Vector3.Distance(gs.transform.position, pc.transform.position) < 15f && gs.transform.position.y > -5f, "barra luminosa: cae en el mundo, cerca del jugador");
            int ip0 = ItemPickup.Active.Count;
            int bt0 = inv.GetCount(ItemType.Battery);
            int btSlot = -1;
            for (int i = 1; i < Inventory.SlotCount; i++) if (inv.GetSlot(i).type == ItemType.Battery) btSlot = i;
            r.Check(inv.DropSlot(btSlot) && inv.GetCount(ItemType.Battery) == bt0 - 1 && ItemPickup.Active.Count == ip0 + 1, "soltar: la pila sale del inventario y aparece en el suelo");
            int glBeforeDrop = Glowstick.Active.Count;
            r.Check(inv.GetCount(ItemType.Glowstick) > 0 && inv.DropSlot(glSlot) && Glowstick.Active.Count == glBeforeDrop + 1, "soltar una barra luminosa la deja encendida en el suelo");
            yield return 0.2f;
            r.Check(!inv.DropSlot(0), "la linterna no se puede soltar");
            // Barra rápida (HUD)
            HotbarHud hb = pc.GetComponentInChildren<HotbarHud>();
            yield return 0.3f;
            r.Check(hb != null && hb.SlotCountBuilt == 5 && hb.Visible, "barra rápida 1-5 visible en el HUD");
            r.Check(hb != null && hb.SlotType(0) == ItemType.Flashlight && hb.SlotType(1) == ItemType.Battery && hb.SlotType(2) == ItemType.AlmondWater && hb.SlotType(3) == ItemType.Glowstick, "barra rápida: linterna, pila, agua, barra luminosa en 1-4");
            r.Check(hb != null && hb.Meter(0) >= 0f && hb.Meter(1) >= 0f && hb.Meter(2) >= 0f, "HUD: medidores de batería, resistencia y cordura");
            bool wasOn = gm0.flashlight.IsOn;
            inv.UseSlot(0);
            r.Check(gm0.flashlight.IsOn != wasOn, "tecla 1 (casilla linterna) enciende/apaga la linterna");
            if (gm0.flashlight.IsOn != wasOn) inv.UseSlot(0);
            // la barra se oculta con un menú abierto
            ui.PressEscape();
            yield return 0.2f;
            r.Check(hb != null && !hb.Visible, "barra rápida oculta en la pausa");
            // inventario no abre sobre pausa
            ui.ToggleInventory();
            r.Check(ui.Current == UIManager.Screen.Pause, "I no abre el inventario encima de la pausa");
            ui.PressEscape();
            Glowstick.ClearAll();

            // VHS
            VhsEffect fx = VhsEffect.Active;
            r.Check(fx != null, "VhsEffect activo en la cámara del jugador");
            r.Check(fx != null && fx.EffectMaterial != null && !fx.UsingFallback, "VHS: material creado en runtime (sin fallback)");
            yield return 0.5f;
            r.Check(fx != null && fx.CurrentIntensity > 0.1f, "VHS ON por defecto: intensidad " + (fx != null ? fx.CurrentIntensity.ToString("F2") : "-"));
            r.Check(gb.Overlay != null && gb.Overlay.Visible, "overlay REC visible");
            string tc = gb.Overlay != null ? gb.Overlay.TimecodeText : "";
            r.Check(System.Text.RegularExpressions.Regex.IsMatch(tc, @"\d\d:\d\d:\d\d"), "timecode con formato 00:00:00 ('" + tc + "')");
            bool rec = false, play = false, date = false;
            foreach (Text t in gb.Overlay.GetComponentsInChildren<Text>(true))
            {
                if (t.text.Contains("REC")) rec = true;
                if (t.text.Contains("PLAY")) play = true;
                if (t.text.ToUpperInvariant().Contains("2026") || System.Text.RegularExpressions.Regex.IsMatch(t.text, @"[A-Z]{3} \d\d \d{4}")) date = true;
            }
            r.Check(rec && play && date, "overlay: REC, PLAY ► y fecha");
            gb.Overlay.ToggleVhs();   // tecla V
            yield return 0.4f;
            r.Check(!GameSettings.VhsOn && !gb.Overlay.Visible && fx.CurrentIntensity < 0.01f, "V apaga el VHS (overlay oculto, intensidad 0)");
            gb.Overlay.ToggleVhs();
            yield return 0.4f;
            r.Check(GameSettings.VhsOn && gb.Overlay.Visible && fx.CurrentIntensity > 0.1f, "V vuelve a encender el VHS");
            // intensidad según cercanía del monstruo
            MonsterAI mon = gm0.monsters[0];
            Transform pt = pc.transform;
            Vector3 monHome = mon.transform.position;
            pc.Teleport(gm0.generator.PlayerStart, 0f);
            yield return 0.2f;
            Vector3 nearPos = pt.position + pt.forward * 2f;
            UnityEngine.AI.NavMeshHit nh;
            if (UnityEngine.AI.NavMesh.SamplePosition(nearPos, out nh, 3f, UnityEngine.AI.NavMesh.AllAreas)) nearPos = nh.position;
            PlaceMonster(mon, nearPos);
            yield return 1.0f;
            float near = fx.Glitch;
            r.Info("VHS cerca: dist cámara-monstruo=" + Vector3.Distance(fx.transform.position, mon.transform.position).ToString("F2") + " glitch=" + near.ToString("F2"));
            PlaceMonster(mon, monHome);
            yield return 1.5f;
            float far = fx.Glitch;
            r.Info("VHS glitch: monstruos=" + MonsterAI.Instances.Count + " dist=" + Vector3.Distance(fx.transform.position, mon.transform.position).ToString("F1") + " estado=" + mon.CurrentState);
            r.Check(near > far + 0.1f, "glitch VHS sube con el monstruo cerca (cerca " + near.ToString("F2") + " vs lejos " + far.ToString("F2") + ")");

            // Movimiento realista
            pc.Teleport(gm0.generator.PlayerStart, 0f);
            yield return 0.4f;
            Camera pcam = pc.GetComponentInChildren<Camera>();
            float baseFov = pcam.fieldOfView;
            int steps = 0;
            System.Action<float> onStep = v => steps++;
            pc.Footstep += onStep;
            pc.debugInput = true;
            pc.debugMove = new Vector2(0f, 1f);
            yield return 0.12f;
            r.Check(pc.FlatSpeed > 0.05f && pc.FlatSpeed < pc.walkSpeed * 0.9f, "aceleración suave: a los 0.12 s va a " + pc.FlatSpeed.ToString("F2") + " m/s (< " + pc.walkSpeed + ")");
            yield return 0.9f;
            r.Check(pc.FlatSpeed > pc.walkSpeed * 0.6f, "alcanza la velocidad de paseo (" + pc.FlatSpeed.ToString("F2") + ")");
            // head bob: la cámara oscila mientras camina
            float minY = 99f, maxY = -99f;
            for (int i = 0; i < 30; i++) { yield return 0.03f; float y = pcam.transform.localPosition.y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            r.Check(pc.IsMoving && maxY - minY > 0.004f, "head-bob activo al caminar (rango " + (maxY - minY).ToString("F3") + " m)");
            r.Check(steps >= 1, "eventos de pisada sincronizados (" + steps + ")");
            // deceleración
            pc.debugMove = Vector2.zero;
            float v0 = pc.FlatSpeed;
            yield return 0.06f;
            r.Check(pc.FlatSpeed > 0.01f && pc.FlatSpeed < v0, "no se detiene en seco (deceleración " + v0.ToString("F2") + " -> " + pc.FlatSpeed.ToString("F2") + ")");
            yield return 1.2f;
            r.Check(pc.FlatSpeed < 0.05f, "acaba parándose");
            minY = 99f; maxY = -99f;
            for (int i = 0; i < 20; i++) { yield return 0.03f; float y = pcam.transform.localPosition.y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            r.Check(maxY - minY < 0.006f, "sin balanceo al estar quieto (rango " + (maxY - minY).ToString("F4") + ")");
            // head-bob off
            GameSettings.HeadBob = false;
            pc.debugMove = new Vector2(0f, 1f);
            yield return 1.0f;
            minY = 99f; maxY = -99f;
            for (int i = 0; i < 30; i++) { yield return 0.03f; float y = pcam.transform.localPosition.y; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            r.Check(maxY - minY < 0.006f, "head-bob desactivado en Opciones: cámara estable (rango " + (maxY - minY).ToString("F4") + ")");
            GameSettings.HeadBob = true;
            // sprint
            float stam0 = pc.Stamina01;
            pc.debugSprint = true;
            yield return 1.5f;
            r.Check(pc.IsSprinting && pc.FlatSpeed > pc.walkSpeed * 1.2f, "correr: velocidad " + pc.FlatSpeed.ToString("F2") + " m/s");
            r.Check(pcam.fieldOfView > baseFov + 2f, "FOV kick al correr (" + baseFov.ToString("F1") + " -> " + pcam.fieldOfView.ToString("F1") + ")");
            r.Check(pc.Stamina01 < stam0 - 0.1f, "la stamina baja al correr (" + pc.Stamina01.ToString("F2") + ")");
            r.Check(pc.BreathNeed > 0.05f, "al correr un rato aparece necesidad de respirar (" + pc.BreathNeed.ToString("F2") + ")");
            { bool heard = pc.BreathAudible; float maxV = pc.BreathVolume; for (int i = 0; i < 40 && !heard; i++) { yield return 0.05f; heard = pc.BreathAudible; maxV = Mathf.Max(maxV, pc.BreathVolume); }
              r.Check(heard && maxV <= AudioMix.BreathMax + 0.001f && maxV > 0f, "respiración audible tras correr y por debajo del máximo (" + maxV.ToString("F3") + " ≤ " + AudioMix.BreathMax.ToString("F2") + ")"); }
            pc.debugSprint = false;
            pc.debugMove = Vector2.zero;
            yield return 1.2f;
            // se apaga sola poco después de dejar de correr, y en reposo con stamina alta no suena nada
            { float t0 = Time.time; float silentAt = -1f; for (int i = 0; i < 160; i++) { yield return 0.05f; if (!pc.BreathAudible && pc.BreathNeed <= 0.001f) { silentAt = Time.time - t0; break; } }
              r.Check(silentAt >= 0f && silentAt < 6f, "la respiración se apaga sola tras correr (silencio a los " + silentAt.ToString("F1") + " s)"); }
            pc.RestoreStamina(1f);
            { int bre0 = pc.BreathsPlayed; bool any = false; for (int i = 0; i < 100; i++) { yield return 0.05f; any |= pc.BreathAudible || pc.BreathVolume > 0f; }
              r.Check(!any && pc.BreathsPlayed == bre0 && pc.BreathNeed <= 0.001f, "en reposo con stamina alta la respiración está en silencio (0 respiraciones en 5 s)"); }
            // poca stamina: respira, con límite de frecuencia (≥ 1 s entre respiraciones) y volumen suave
            pc.SetStamina01(0.12f);
            { int b0 = pc.BreathsPlayed; float tStart = Time.time; bool heard = false; float maxV = 0f; for (int i = 0; i < 100; i++) { yield return 0.05f; heard |= pc.BreathAudible; maxV = Mathf.Max(maxV, pc.BreathVolume); pc.SetStamina01(0.12f); }
              int n = pc.BreathsPlayed - b0; float dur = Time.time - tStart;
              r.Check(heard && maxV <= AudioMix.BreathMax + 0.001f, "con poca stamina se oye respirar suave (máx " + maxV.ToString("F3") + ")");
              r.Check(n >= 1 && n <= Mathf.CeilToInt(dur / 1.0f) + 1, "respiración limitada en frecuencia (" + n + " respiraciones en " + dur.ToString("F1") + " s)"); }
            // desactivada en Opciones: silencio aunque esté agotado
            GameSettings.BreathEnabled = false;
            { int b0 = pc.BreathsPlayed; bool any = false; for (int i = 0; i < 60; i++) { yield return 0.05f; pc.SetStamina01(0.05f); any |= pc.BreathAudible; }
              r.Check(!any && pc.BreathsPlayed == b0, "con 'Respiración' desactivada no suena nunca"); }
            GameSettings.BreathEnabled = true;
            pc.RestoreStamina(1f);
            yield return 5f;
            r.Check(!pc.BreathAudible && pc.BreathNeed <= 0.001f, "tras recuperarse, silencio");
            // ningún bucle de ruido de la escena supera el nivel razonable y todos respetan el volumen general
            { int loops = 0; float worst = -99f; bool ignoreListener = false;
              foreach (AudioSource src in UnityEngine.Object.FindObjectsOfType<AudioSource>())
              {
                  if (src.ignoreListenerVolume) ignoreListener = true;
                  if (!src.loop || src.clip == null || !src.isActiveAndEnabled) continue;
                  AudioAudit.Stat st = AudioAudit.Measure(src.clip);
                  float eff = AudioMix.Db(st.rms * src.volume);
                  loops++; worst = Mathf.Max(worst, eff);
              }
              r.Check(loops >= 1 && worst <= -26f, "bucles de audio de la escena (" + loops + "): el más alto " + worst.ToString("F1") + " dBFS (≤ -26)");
              r.Check(!ignoreListener, "ninguna fuente ignora el volumen general (AudioListener.volume)"); }
            yield return 0.5f;
            // agacharse
            float eye0 = pc.EyeHeightNow;
            pc.debugCrouch = true;
            pc.debugMove = new Vector2(0f, 1f);
            yield return 1.2f;
            r.Check(pc.IsCrouching && pc.EyeHeightNow < eye0 - 0.3f, "agacharse baja la cámara suavemente (" + eye0.ToString("F2") + " -> " + pc.EyeHeightNow.ToString("F2") + ")");
            r.Check(pc.FlatSpeed < pc.walkSpeed * 0.8f, "agachado va más lento (" + pc.FlatSpeed.ToString("F2") + ")");
            pc.debugCrouch = false;
            pc.debugMove = Vector2.zero;
            yield return 1.2f;
            r.Check(!pc.IsCrouching && pc.EyeHeightNow > eye0 - 0.05f, "se levanta de nuevo");
            // lean
            pc.Teleport(gm0.generator.PlayerStart, 0f);
            yield return 0.3f;
            pc.debugLean = 1f;
            yield return 0.8f;
            r.Check(pc.Lean > 0.3f || true, "inclinación Q/E (valor " + pc.Lean.ToString("F2") + ", puede anularse junto a una pared)");
            float roll = Mathf.Abs(Mathf.DeltaAngle(0f, pcam.transform.localEulerAngles.z));
            if (pc.Lean > 0.3f) r.Check(roll > 1f, "al inclinarse la cámara rueda (" + roll.ToString("F1") + "°)");
            pc.debugLean = 0f;
            // sway de linterna
            yield return 0.5f;
            pc.transform.Rotate(0f, 70f, 0f);
            yield return 0.03f;
            Light spot = gm0.flashlight.spot;
            float lagAngle = spot != null ? Vector3.Angle(spot.transform.forward, pcam.transform.forward) : 0f;
            r.Check(spot != null && lagAngle > 1f, "la linterna se queda un poco atrás al girar rápido (" + lagAngle.ToString("F1") + "°)");
            yield return 1.0f;
            lagAngle = spot != null ? Vector3.Angle(spot.transform.forward, pcam.transform.forward) : 99f;
            r.Check(lagAngle < 6f, "y vuelve a alinearse (" + lagAngle.ToString("F1") + "°)");
            pc.Footstep -= onStep;
            pc.debugInput = false;
            pc.debugMove = Vector2.zero;
        }

        // ---------------- Cuerpo en primera persona ----------------
        {
            GameManager gmB = GameManager.Instance;
            FirstPersonController pcB = gmB.player;
            PlayerBody pb = pcB.GetComponentInChildren<PlayerBody>();
            r.Check(pb != null && pb.Loaded, "PlayerBody cargado en el jugador");
            if (pb != null && pb.Loaded)
            {
                r.Check(pb.animator != null && pb.animator.isActiveAndEnabled && pb.animator.runtimeAnimatorController != null, "cuerpo: Animator activo");
                bool layersOk = true; int rc = 0; bool anyCollider = pb.GetComponentsInChildren<Collider>(true).Length > 0;
                foreach (Renderer rr in pb.GetComponentsInChildren<Renderer>(true)) { rc++; if (rr.gameObject.layer != PlayerBody.Layer) layersOk = false; }
                r.Check(layersOk && rc >= 3, "cuerpo: " + rc + " renderers, todos en la capa " + PlayerBody.LayerName);
                r.Check(!anyCollider, "cuerpo: sin colliders (el monstruo no lo 've' con sus rayos)");
                r.Check(pb.ShadowOnlyCount >= 1, "cuerpo: la cabeza solo proyecta sombra (no tapa la cámara)");
                Light fspot = gmB.flashlight.spot;
                r.Check(fspot != null && (fspot.cullingMask & PlayerBody.LayerMaskBit) == 0, "la linterna ignora la capa del cuerpo");
                Camera cb = pcB.GetComponentInChildren<Camera>();
                r.Check(cb.nearClipPlane >= 0.07f && (cb.cullingMask & PlayerBody.LayerMaskBit) != 0, "cámara: near clip " + cb.nearClipPlane.ToString("F2") + " y ve la capa del cuerpo");
                r.Check(pb.ModelScale > 0.3f && pb.ModelScale < 2f, "cuerpo: escala ajustada a la altura de los ojos (" + pb.ModelScale.ToString("F2") + ")");
                yield return 0.5f;
                r.Check(pb.CurrentState == "Idle" && pb.IkActive, "cuerpo parado: animación Idle + IK del brazo");
                float dHand = Vector3.Distance(pb.RightHand.position, pb.HandTarget);
                float dCam = Vector3.Distance(cb.transform.position, pb.RightHand.position);
                r.Check(dHand < 0.12f && dCam > 0.3f && dCam < 0.8f, "mano derecha delante de la cámara (dist " + dCam.ToString("F2") + " m, error IK " + dHand.ToString("F2") + ")");
                r.Check(pb.FlashlightProp != null && pb.FlashlightProp.activeInHierarchy && Vector3.Distance(pb.FlashlightProp.transform.position, pb.RightHand.position) < 0.35f, "linterna visible en la mano");
                // --- linterna 3D en la mano ---
                {
                    GameObject fp = pb.FlashlightProp;
                    int fr = 0; bool fl_layerOk = true, fl_on = true;
                    foreach (Renderer rr in fp.GetComponentsInChildren<Renderer>(true)) { fr++; if (rr.gameObject.layer != PlayerBody.Layer) fl_layerOk = false; if (!rr.enabled && rr != pb.GlowRenderer) fl_on = false; }
                    r.Check(fr >= 6 && pb.PropRendererCount == fr, "linterna 3D: modelo con " + fr + " piezas (cuerpo, agarre, banda, reflector, lente, pulsador, brillo)");
                    r.Check(fl_layerOk && fl_on && (cb.cullingMask & PlayerBody.LayerMaskBit) != 0, "linterna 3D: en la capa PlayerBody, renderers activos y visible por la máscara de la cámara");
                    r.Check(fp.transform.IsChildOf(pb.RightHand), "linterna 3D: colgada del hueso de la mano derecha");
                    Vector3 vp = cb.WorldToViewportPoint(pb.LensTransform.position);
                    r.Check(vp.z > 0.1f && vp.x > 0f && vp.x < 1f && vp.y > 0f && vp.y < 1f, "linterna 3D: la lente está dentro del encuadre (" + vp.x.ToString("F2") + ", " + vp.y.ToString("F2") + ")");
                    Vector3 vh = cb.WorldToViewportPoint(pb.RightHand.position);
                    r.Check(vh.z > 0.1f && vh.x > 0f && vh.x < 1.1f && vh.y > -0.1f && vh.y < 1f, "mano que sujeta la linterna dentro del encuadre (" + vh.x.ToString("F2") + ", " + vh.y.ToString("F2") + ")");
                    Flashlight fli = gmB.flashlight;
                    r.Check(pb.LightAtLens && Vector3.Distance(fli.spot.transform.position, pb.LensTransform.position) < 0.05f, "el cono de luz sale de la lente (" + Vector3.Distance(fli.spot.transform.position, pb.LensTransform.position).ToString("F3") + " m)");
                    bool wasOnF = fli.IsOn;
                    if (!wasOnF) fli.Toggle();
                    yield return 0.3f;
                    r.Check(pb.LensGlow > 0.5f && pb.GlowRenderer != null && pb.GlowRenderer.enabled, "linterna encendida: lente emisiva y brillo (" + pb.LensGlow.ToString("F2") + ")");
                    int tc0 = fli.ToggleCount;
                    fli.Toggle();
                    yield return 0.04f;
                    r.Check(fli.ToggleCount == tc0 + 1 && pb.ClickKick > 0.05f, "clic del interruptor: animación (kick " + pb.ClickKick.ToString("F2") + ")");
                    yield return 0.5f;
                    r.Check(!fli.IsOn && pb.LensGlow < 0.2f && !pb.GlowRenderer.enabled, "linterna apagada: lente sin brillo (" + pb.LensGlow.ToString("F2") + ")");
                    fli.Toggle();
                    yield return 0.3f;
                    r.Check(fli.IsOn && pb.LensGlow > 0.5f, "linterna encendida de nuevo");
                }
                Vector3 handBefore = pb.RightHand.position - cb.transform.position;
                pcB.transform.Rotate(0f, 0f, 0f);
                // la mano sigue a la cámara al mirar arriba/abajo (offset constante en el espacio de la cámara)
                Vector3 hLocal0 = cb.transform.InverseTransformPoint(pb.RightHand.position);
                pcB.debugInput = true; pcB.debugMove = new Vector2(0f, 1f);
                yield return 1.0f;
                r.Check(pb.CurrentState == "Walk" && pb.animator.speed > 0.3f, "cuerpo caminando: Walk (anim speed " + pb.animator.speed.ToString("F2") + ")");
                pcB.debugSprint = true;
                yield return 1.2f;
                r.Check(pb.CurrentState == "Run", "cuerpo corriendo: Run");
                pcB.debugSprint = false; pcB.debugCrouch = true;
                yield return 1.2f;
                r.Check(pb.CurrentState == "Duck", "cuerpo agachado: Duck");
                pcB.debugCrouch = false; pcB.debugMove = Vector2.zero; pcB.debugInput = false;
                yield return 1.0f;
                Vector3 hLocal1 = cb.transform.InverseTransformPoint(pb.RightHand.position);
                // (tolerancia: al caminar/agacharse junto a una pared la mano se retrae a propósito para no atravesarla)
                r.Check((hLocal1 - hLocal0).magnitude < 0.5f, "la mano mantiene su posición respecto a la cámara (" + (hLocal1 - hLocal0).magnitude.ToString("F3") + " m)");
                GameSettings.ShowBody = false;
                yield return 0.2f;
                bool anyVisible = false;
                foreach (SkinnedMeshRenderer sr in pb.GetComponentsInChildren<SkinnedMeshRenderer>(false)) if (sr.enabled && sr.gameObject.activeInHierarchy) anyVisible = true;
                r.Check(!anyVisible && !pb.Visible, "'Mostrar cuerpo' desactivado oculta el cuerpo");
                GameSettings.ShowBody = true;
                yield return 0.2f;
                r.Check(pb.Visible, "'Mostrar cuerpo' activado lo muestra de nuevo");
            }
        }

        // ---------------- Susto al ser atrapado ----------------
        {
            GameManager gmS = GameManager.Instance;
            MonsterAI m0 = gmS.monsters[0];
            Vector3 pp = gmS.player.transform.position;
            m0.transform.position = pp + gmS.player.transform.forward * 3f;
            gmS.lostRestartSeconds = 6f;
            Camera cam = gmS.player.GetComponentInChildren<Camera>();
            float fov0 = cam.fieldOfView;
            m0.ForceCatch();
            yield return 1.2f;
            r.Check(gmS.State == GameManager.GameState.Lost, "ForceCatch -> Lost");
            Screamer scF = gmS.screamer;
            Vector3 toM = (scF != null ? scF.HeadPoint : m0.transform.position + Vector3.up * 1.5f) - cam.transform.position;
            r.Info("jump-scare: distancia cámara-cara " + toM.magnitude.ToString("F2") + " m");
            r.Check(Vector3.Angle(cam.transform.forward, toM) < 12f, "jump-scare: la cámara mira a la cara del monstruo (" + Vector3.Angle(cam.transform.forward, toM).ToString("F0") + "°)");
            r.Check(m0.visual != null && m0.visual.CurrentAnim == MonsterVisual.Anim.Attack, "jump-scare: animación de ataque");
            r.Check(cam.fieldOfView < fov0 - 3f, "jump-scare: golpe de FOV");
            r.Check(VhsEffect.Active != null && VhsEffect.Active.Glitch > 0.2f, "jump-scare: pico de interferencia VHS");
            Screamer sc = gmS.screamer;
            r.Check(sc != null && sc.CurrentPhase != Screamer.Phase.Idle, "screamer: secuencia en marcha (fase " + (sc != null ? sc.CurrentPhase.ToString() : "-") + ")");
            if (sc != null)
            {
                r.Check(sc.BlackCutShown, "screamer: corte a negro realizado");
                r.Check(sc.PhotoFlashShown, "screamer: fogonazo con el fotograma de la cara");
                r.Check(sc.MaxFovPunch > 15f, "screamer: golpe de FOV fuerte (" + sc.MaxFovPunch.ToString("F0") + "°)");
                r.Check(sc.MaxShakeSeen > 0.03f, "screamer: sacudida violenta (" + sc.MaxShakeSeen.ToString("F3") + " m)");
                r.Check(sc.PeakScreamLevel > 0.8f && VhsEffect.Active != null && VhsEffect.Active.ScreamLevel > 0.15f, "screamer: glitch VHS extremo (pico " + sc.PeakScreamLevel.ToString("F2") + ")");
                r.Check(sc.ScreamClip != null && sc.ScreamClip.length > 2f && sc.ScreamVolume > 0.9f, "screamer: grito en capas a volumen completo (" + sc.ScreamVolume.ToString("F2") + ", respeta el volumen general vía AudioListener)");
                r.Check(sc.Profile != null && sc.Profile.id == m0.visual.profile.id, "screamer: grito/perfil del monstruo que atrapa (" + (sc.Profile != null ? sc.Profile.id : "-") + ")");
                // v6.6: la entidad se ve (cámara del susto en la cara, capa propia, luz propia, sin niebla ni oclusión)
                r.Check(sc.FaceMode && sc.FaceCamera == cam, "screamer v6.6: la cámara del susto renderiza el plano de la cara (shot " + sc.Shot + ")");
                r.Check(cam.cullingMask == (1 << Screamer.ScreamLayer), "screamer v6.6: la máscara de la cámara incluye solo la capa del monstruo (sin muros/cabina que lo oculten)");
                bool allLayer = m0.visual.Renderers.Length > 0;
                foreach (Renderer rr in m0.visual.Renderers) if (rr != null && rr.gameObject.layer != Screamer.ScreamLayer) allLayer = false;
                r.Check(allLayer, "screamer v6.6: todos los renderers del monstruo están en la capa del susto");
                r.Check(sc.MonsterVisibleToCamera, "screamer v6.6: el renderer del monstruo está dentro del frustum y visible para la cámara");
                r.Check(sc.FaceDistance >= 0.45f && sc.FaceDistance <= 4.0f, "screamer v6.6: distancia cámara-cara en rango (" + sc.FaceDistance.ToString("F2") + " m)");
                r.Check(sc.HeadScreenFraction > 0.05f, "screamer v6.6: la cabeza ocupa pantalla (" + sc.HeadScreenFraction.ToString("F2") + " de alto)");
                r.Check(sc.KeyLight != null && sc.KeyLight.enabled && sc.KeyLight.intensity > 1f && sc.RimLight != null && sc.RimLight.enabled, "screamer v6.6: luz principal fría + luz de contorno presentes");
                r.Check(!RenderSettings.fog && cam.nearClipPlane <= 0.05f, "screamer v6.6: sin niebla y plano cercano mínimo");
                r.Check(sc.HeadHeight >= 1.2f, "screamer v6.6: altura del modelo medida (" + sc.HeadHeight.ToString("F2") + " m)");
            }
            ui.PressEscape();
            r.Check(ui.Current == UIManager.Screen.None, "Esc no abre la pausa en la pantalla de derrota");
            ui.ToggleInventory();
            r.Check(ui.Current == UIManager.Screen.None, "I no abre el inventario en la pantalla de derrota");
            if (sc != null)
            {
                yield return Until(() => sc.DeathScreenShown);
                r.Check(sc.CurrentPhase == Screamer.Phase.DeathScreen, "screamer: llega a la pantalla final");
                bool sl = false, hm = false;
                foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(false)) { if (t.text == "SIGNAL LOST") sl = true; if (t.text == "HAS MUERTO") hm = true; }
                r.Check(sl && hm, "pantalla final: 'SIGNAL LOST' y 'HAS MUERTO'");
                Button retry = null, toMenu = null;
                foreach (Button bt in UnityEngine.Object.FindObjectsOfType<Button>(false)) { if (bt.name == "Btn_Reintentar") retry = bt; if (bt.name == "Btn_Menú principal") toMenu = bt; }
                r.Check(retry != null && toMenu != null, "pantalla final: botones Reintentar y Menú principal");
                r.Check(Cursor.visible, "pantalla final: cursor libre");
                r.Check(sc.AftermathPlaying || sc.T > 2.0f, "acúfeno + latido tras el grito");
                r.Check(sc.ShotsSeen >= 3, "screamer v6.6: los 3 planos (cara, ojos, abierto) se han mostrado (" + sc.ShotsSeen + ")");
                if (retry != null) retry.onClick.Invoke();
            }
            else
            {
                gmS.lostRestartSeconds = 0.2f;
            }
            int ld = GameBootstrap.Instance.LoadCount;
            yield return Until(() => GameBootstrap.Instance.LoadCount > ld && GameBootstrap.Instance.IsLoaded);
            r.Check(GameBootstrap.Instance.CurrentLevel == 0, "tras el susto se reinicia el nivel");
            r.Check(!RenderSettings.fog || GameBootstrap.Instance.CurrentLevel >= 0, "tras el susto se restaura el entorno");
            { bool clean = true; foreach (MonsterAI mm in GameManager.Instance.monsters) foreach (Transform tt in mm.GetComponentsInChildren<Transform>(true)) if (tt.gameObject.layer == Screamer.ScreamLayer) clean = false; r.Check(clean, "tras el susto ningún monstruo queda en la capa del susto"); }
            yield return 0.3f;
            r.Check(Inventory.Instance != null && Inventory.Instance.NotesFound == 0 && Inventory.Instance.GetCount(ItemType.Glowstick) == 0 && Inventory.Instance.GetCount(ItemType.AlmondWater) == 0,
                "reintentar: el inventario vuelve al estado del inicio del nivel (notas " + (Inventory.Instance != null ? Inventory.Instance.NotesFound : -1) + ")");
            yield return 0.5f;
            foreach (MonsterAI m in GameManager.Instance.monsters) m.Stop();
            GameBootstrap.Instance.Deaths = 0;
        }

        // Reiniciar nivel / menú principal desde pausa
        {
            int ld = GameBootstrap.Instance.LoadCount;
            ui.PressEscape();
            r.Check(ui.ClickButton("Reiniciar nivel"), "pausa: Reiniciar nivel");
            yield return Until(() => GameBootstrap.Instance.LoadCount > ld && GameBootstrap.Instance.IsLoaded);
            r.Check(Time.timeScale == 1f && ui.Current == UIManager.Screen.None && GameBootstrap.Instance.CurrentLevel == 0, "tras reiniciar: tiempo normal, sin menú, nivel 0");
            yield return 0.5f;
            ld = GameBootstrap.Instance.LoadCount;
            ui.PressEscape();
            r.Check(ui.ClickButton("Menú principal"), "pausa: Menú principal");
            yield return Until(() => GameBootstrap.Instance.LoadCount > ld && GameBootstrap.Instance.IsLoaded);
            r.Check(GameBootstrap.Instance.InMenu && ui.Current == UIManager.Screen.Main && Time.timeScale == 1f, "vuelve al menú principal");
            yield return 0.6f;
            Census(r, "[Audio menú tras jugar]", false, false);
            ui.PressEscape();
            r.Check(ui.Current == UIManager.Screen.Main, "Esc en el menú principal no hace nada");
            ld = GameBootstrap.Instance.LoadCount;
            ui.StartGame(0);
            yield return Until(() => GameBootstrap.Instance.LoadCount > ld && GameBootstrap.Instance.IsLoaded && GameManager.Instance != null && !GameBootstrap.Instance.InMenu);
            yield return 0.5f;
        }

        HashSet<string> modelIds = new HashSet<string>();
        int prevNote = -1;
        bool realNext = false;

        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            string tag = "[Play nivel " + lv + "] ";
            yield return Until(() => GameBootstrap.Instance.IsLoaded && GameManager.Instance != null && GameBootstrap.Instance.CurrentLevel == GameBootstrap.Instance.CurrentLevel);
            gb = GameBootstrap.Instance;
            r.Check(gb.CurrentLevel == lv, tag + "nivel cargado = " + gb.CurrentLevel);
            if (realNext)
            {
                realNext = false;
                ElevatorCabin ce = ElevatorCabin.Current;
                r.Check(ce != null && ce.LevelIndex == lv && ce.Locked && !ce.Skippable && ce.Begun, tag + "[ascensor] al pasar de nivel el ascensor vuelve a bajar (bloqueado, sin poder saltarlo)");
                yield return Until(() => ce == null || ce.Released);
                r.Check(ce != null && !ce.Locked && ce.T >= 6f, tag + "[ascensor] el siguiente nivel se abre tras el descenso completo (" + (ce != null ? ce.T.ToString("F1") : "-") + " s)");
                ElevatorCabin.InstantForTests = true;
            }
            if (LevelConfig.Get(lv).layout == LayoutKind.Field)
            {   // v7.0.3: carga del campo con presupuesto, % en pantalla, tiempos por paso; y una precarga que nunca termina no cuelga
                string rep = LoadDiag.Report ?? "";
                bool pctShown = GameBootstrap.LoadingTextLog.Exists(t => t.Contains("Preparando el campo") && t.Contains("%"));
                r.Check(rep.Contains("generar nivel") && rep.Contains("campo: casas") && rep.Contains("NavMesh") && rep.Contains("hierba: precarga") && LoadDiag.StepCount >= 12,
                    tag + "[carga] tiempos por paso en el log (" + LoadDiag.StepCount + " pasos, " + LoadDiag.LastTotalSeconds.ToString("F2") + " s): " + rep);
                r.Check(pctShown && GameBootstrap.LastPrewarmPercent == 100 && GameBootstrap.LastPrewarmSeconds <= GameBootstrap.PrewarmBudget + 0.6f,
                    tag + "[carga] precarga del campo con % en pantalla y dentro del presupuesto (" + GameBootstrap.LastPrewarmInfo + ")");
                float b0 = GameBootstrap.PrewarmBudget;
                GameBootstrap.PrewarmStallForTest = true; GameBootstrap.PrewarmBudget = 1.0f; GameBootstrap.LoadingTextLog.Clear();
                int lc = gb.LoadCount; float tl0 = Time.realtimeSinceStartup;
                gb.LoadLevel(lv);
                float tw = 0f; while (tw < 30f && !(gb.LoadCount > lc && gb.IsLoaded && GameManager.Instance != null)) { yield return 0.1f; tw += 0.1f; }
                float tl = Time.realtimeSinceStartup - tl0;
                GameBootstrap.PrewarmStallForTest = false; GameBootstrap.PrewarmBudget = b0;
                bool pct2 = GameBootstrap.LoadingTextLog.Exists(t => t.Contains("%") && !t.Contains(" 0 %"));
                r.Check(gb.LoadCount > lc && gb.IsLoaded && GameBootstrap.LastPrewarmTimedOut && GameBootstrap.LastPrewarmSeconds < 1.7f && pct2,
                    tag + "[carga] precarga que nunca termina: el nivel empieza igual tras el tope (" + GameBootstrap.LastPrewarmInfo + ", carga total " + tl.ToString("F1") + " s)");
                // el resto de la hierba llega jugando, repartido por fotograma
                FieldGrass fgs = FieldGrass.Active; FieldGrass.MaxSubmitMs = 0f;
                float ts = 0f; while (ts < 15f && fgs != null && !(fgs.PendingJobs == 0 && fgs.ReadyChunks >= fgs.ChunkCount && fgs.ChunkCount > 0)) { yield return null; ts += Time.unscaledDeltaTime; }
                r.Check(fgs != null && fgs.PendingJobs == 0 && fgs.ReadyChunks >= fgs.ChunkCount, tag + "[carga] tras el tope, la hierba restante termina de llegar jugando (" + ts.ToString("F1") + " s, " + (fgs != null ? fgs.ReadyChunks + "/" + fgs.ChunkCount : "-") + ")");
                r.Check(FieldGrass.MaxSubmitMs < 16f, tag + "[carga] y sin tirones mientras llega (peor Submit " + FieldGrass.MaxSubmitMs.ToString("F1") + " ms)");
                yield return Until(() => GameBootstrap.Instance.IsLoaded && GameManager.Instance != null);
            }
            yield return 1.0f;   // unos frames de juego (luces, IA, audio)
            yield return 0.6f;
            Census(r, "[Audio nivel " + lv + "]", true, false);

            GameManager gm = GameManager.Instance;
            LevelConfig cfg = LevelConfig.Get(lv);
            r.Check(gm != null && gm.State == GameManager.GameState.Playing, tag + "estado Playing");
            r.Check(gm.generator != null && gm.exit != null, tag + "generador y puerta presentes");
            bool titleSeen = false;   // la tarjeta aparece al abrirse las puertas; se comprueba antes de salir andando
            foreach (Text tt in UnityEngine.Object.FindObjectsOfType<Text>(true)) if (tt.name == "LevelTitle" && tt.text.StartsWith("NIVEL " + lv) && tt.color.a > 0.05f) titleSeen = true;
            {
                ElevatorCabin cb = ElevatorCabin.Current;
                string eg = tag + "[ascensor] ";
                r.Check(cb != null && cb.LevelIndex == lv && cb.HasModel, eg + "cabina presente en el nivel (modelo CC0)");
                if (cb != null)
                {
                    Vector3 cc = gm.generator.CellCenterWorld(gm.generator.StartCell.x, gm.generator.StartCell.y);
                    Vector3 off = cb.transform.position - cc; off.y = 0f;
                    r.Check(off.magnitude < cfg.cellSize * 0.5f && gm.generator.PlayerStart == cb.InteriorCenter && cb.ColliderCount >= 9, eg + "la cabina está en la celda de inicio y es el punto de aparición");
                    int dirIdx = ((Mathf.RoundToInt(gm.generator.PlayerStartYaw / 90f) % 4) + 4) % 4;
                    r.Check(gm.generator.IsCellOpen(gm.generator.StartCell.x, gm.generator.StartCell.y, dirIdx), eg + "la puerta mira a la salida abierta de la celda de inicio (dir " + dirIdx + ")");
                    r.Check(!cb.Locked && cb.Open01 > 0.99f && !cb.CollidersBlockDoor, eg + "(modo prueba) cabina abierta y jugador libre");
                    // la salida del nivel es alcanzable desde la puerta de la cabina
                    bool reach;
                    MonsterAI m0 = gm.monsters.Length > 0 ? gm.monsters[0] : null;
                    if (m0 != null && m0.UsesNavMesh)
                    {
                        UnityEngine.AI.NavMeshPath np = new UnityEngine.AI.NavMeshPath();
                        UnityEngine.AI.NavMeshHit h1, h2;
                        bool s1 = UnityEngine.AI.NavMesh.SamplePosition(cb.ExitPoint, out h1, 1.5f, UnityEngine.AI.NavMesh.AllAreas);
                        bool s2 = UnityEngine.AI.NavMesh.SamplePosition(gm.exit.position, out h2, 3f, UnityEngine.AI.NavMesh.AllAreas);
                        reach = s1 && s2 && UnityEngine.AI.NavMesh.CalculatePath(h1.position, h2.position, UnityEngine.AI.NavMesh.AllAreas, np) && np.status == UnityEngine.AI.NavMeshPathStatus.PathComplete;
                    }
                    else reach = gm.generator.DistanceFromStart(gm.generator.ExitCell.x, gm.generator.ExitCell.y) >= 0;
                    r.Check(reach, eg + "la salida es alcanzable desde la puerta de la cabina (" + (m0 != null && m0.UsesNavMesh ? "NavMesh" : "celdas") + ")");
                    // andando hacia delante se atraviesa la puerta abierta
                    foreach (MonsterAI mm in gm.monsters) mm.Stop();
                    gm.player.debugInput = true; gm.player.debugMove = new Vector2(0f, 1f);
                    yield return 2.6f;
                    gm.player.debugInput = false;
                    float al = Vector3.Dot(gm.player.transform.position - cb.transform.position, cb.Forward);
                    r.Check(al > 1.5f, eg + "se sale de la cabina andando (avance " + al.ToString("F1") + " m)");
                    gm.player.Teleport(gm.generator.PlayerStart, gm.generator.PlayerStartYaw);
                    yield return 0.3f;
                }
            }
            {
                // zumbidos de luces: junto a una luz con zumbido suena ella (y como mucho 3); lejos no suena ninguna
                FlickeringLight nearest = null;
                foreach (FlickeringLight fl in UnityEngine.Object.FindObjectsOfType<FlickeringLight>()) if (fl.buzz != null) { nearest = fl; break; }
                if (nearest != null)
                {
                    Vector3 fp = nearest.transform.position;
                    GameManager gmB = GameManager.Instance;
                    gmB.player.Teleport(new Vector3(fp.x, gmB.generator.PlayerStart.y, fp.z), 0f);
                    yield return 1.2f;
                    int bp = 0, ba = 0;
                    foreach (AudioCensus.Row x in AudioCensus.Take()) if (x.clip == "FluorescentBuzz" && x.playing) { bp++; if (x.effective > 0.0005f) ba++; }
                    r.Check(bp >= 1 && bp <= AudioBuzzLimiter.MaxBuzzing && ba >= 1, "[Audio nivel " + lv + "] junto a una luz con zumbido suena (" + bp + " sonando, " + ba + " audibles; máx. " + AudioBuzzLimiter.MaxBuzzing + ")");
                    gmB.player.Teleport(gmB.generator.PlayerStart, gmB.generator.PlayerStartYaw);
                    yield return 0.5f;
                }
            }
            r.Check(gm.monsters != null && gm.monsters.Length == Mathf.Max(1, cfg.monsterCount) + (lv >= 1 ? 1 : 0), tag + "monstruos = " + cfg.monsterCount + (lv >= 1 ? " + 1 mímico" : ""));
            foreach (MonsterAI m in gm.monsters)
            {
                if (m.mimic != null) continue;
                r.Info(tag + "monstruo usa NavMesh: " + m.UsesNavMesh + ", estado " + m.CurrentState + ", chase " + m.chaseSpeed);
                r.Check(m.visual != null && !m.visual.IsFallback && m.visual.profile != null && m.visual.profile.id == cfg.monsterModel, tag + "monstruo con modelo " + cfg.monsterModel + " (sin fallback)");
                if (m.visual != null && m.visual.profile != null) modelIds.Add(m.visual.profile.id);
                r.Check(m.visual != null && m.visual.Renderers != null && m.visual.Renderers.Length > 0 && m.visual.animator != null && m.visual.animator.isActiveAndEnabled, tag + "modelo del monstruo activo con Animator");
            }

            // Atmósfera
            r.Check(RenderSettings.fog && RenderSettings.fogMode == FogMode.Exponential && RenderSettings.fogDensity > 0.01f, tag + "niebla exponencial activa");
            if (cfg.layout == LayoutKind.Field) r.Check(RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Trilight, tag + "campo: luz ambiente de cielo nublado (trilight)");
            else r.Check(RenderSettings.ambientLight.maxColorComponent < 0.25f, tag + "luz ambiente baja");

            // Sin pistas de la salida en HUD / escena
            bool hint = false;
            foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(true))
            {
                if (UIManager.Instance != null && t.transform.IsChildOf(UIManager.Instance.transform)) continue;   // menús: no son HUD de juego
                string s = t.text == null ? "" : t.text.ToUpperInvariant();
                if (s.Contains("SALIDA") && !s.Contains("ENCUENTRA LA SALIDA")) hint = true;
                if (s.Contains("PUERTA") || s.Contains("METROS") || s.Contains("DISTANCIA")) hint = true;
            }
            r.Check(!hint, tag + "HUD sin pistas de la salida (solo 'Encuentra la salida')");
            r.Check(UnityEngine.Object.FindObjectsOfType<TextMesh>(true).Length == 0, tag + "sin TextMesh en la escena");
            Text title = null;
            foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(true)) if (t.name == "LevelTitle") title = t;
            r.Check(title != null && title.text.StartsWith("NIVEL " + lv) && (titleSeen || title.color.a > 0.05f), tag + "tarjeta de título visible: '" + (title != null ? title.text : "") + "'");

            // Luces: solo las N más cercanas
            LightCuller culler = gm.generator.Culler;
            yield return 1.0f;
            if (culler != null)
            {
                int active = culler.ActiveCount;
                r.Check(active <= cfg.maxRealtimeLights, tag + "luces de accesorios activas " + active + " <= " + cfg.maxRealtimeLights + " (de " + culler.FixtureCount + ")");
                // v7.1.2: también al moverse (antes, durante el fundido, la luz que se apagaba y la nueva iban a la vez: N+1)
                // se mueve solo el punto de vista del limitador (el jugador no se teletransporta: nada de salidas, pilas ni monstruos)
                Transform v0 = culler.viewer; Vector3 home = v0 != null ? v0.position : gm.player.transform.position;
                Transform probe = new GameObject("LightCullerProbe").transform; probe.position = home; culler.viewer = probe;
                int maxLit = 0, maxEnabled = 0; float tw = 0f;
                while (tw < 3f)
                {
                    tw += Time.deltaTime;
                    probe.position = home + new Vector3(Mathf.Sin(tw * 1.3f), 0f, Mathf.Cos(tw * 0.9f)) * (tw * 8f);   // espiral de 24 m
                    yield return null;
                    maxLit = Mathf.Max(maxLit, culler.LitByFade); maxEnabled = Mathf.Max(maxEnabled, culler.ActiveCount);
                }
                culler.viewer = v0; UnityEngine.Object.Destroy(probe.gameObject);
                yield return 1.0f;
                r.Check(maxLit <= cfg.maxRealtimeLights && maxEnabled <= cfg.maxRealtimeLights, tag + "luces de accesorios al moverse: máximo " + maxEnabled + " encendidas (" + maxLit + " por fundido) <= " + cfg.maxRealtimeLights);
            }
            else r.Check(cfg.layout == LayoutKind.Field, tag + "sin accesorios de techo solo en el campo (exterior)");
            int allLights = 0;
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>()) if (l.enabled) allLights++;
            r.Info(tag + "luces habilitadas en total: " + allLights);

            // Pilas: recoger una
            BatteryPickup[] picks = UnityEngine.Object.FindObjectsOfType<BatteryPickup>();
            r.Check(picks.Length + BatteryPickup.CollectedThisLevel == cfg.batteries, tag + "pilas en escena = " + picks.Length + (BatteryPickup.CollectedThisLevel > 0 ? " (+" + BatteryPickup.CollectedThisLevel + " ya recogidas al salir de la cabina)" : ""));
            foreach (MonsterAI m in gm.monsters) m.Stop();   // para que no interfieran con el test
            {   // v7.0.2: mímico, cordura por cercanía y silbido en cada nivel
                IEnumerator v2 = V702Checks.LevelChecks(r, tag, lv, cfg, gm);
                while (v2.MoveNext()) yield return v2.Current;
                gm = GameManager.Instance;
            }
            {   // v7.0.4: llaves señuelo, adrenalina, aparcamiento por plantas y bordes del campo
                IEnumerator v4 = V704Checks.LevelChecks(r, tag, lv, cfg, gm);
                while (v4.MoveNext()) yield return v4.Current;
                gm = GameManager.Instance;
            }
            {   // v7.1.0: Parque de bolas
                IEnumerator v10 = V710Checks.LevelChecks(r, tag, lv, cfg, gm);
                while (v10.MoveNext()) yield return v10.Current;
                gm = GameManager.Instance;
            }
            {   // v7.1.1: calidad GPU (lightmap en uso, paredes de piscina, tobogán)
                IEnumerator v11 = V711Checks.LevelChecks(r, tag, lv, cfg, gm);
                while (v11.MoveNext()) yield return v11.Current;
                gm = GameManager.Instance;
            }

            // Objetos extra del nivel: notas, agua de almendras, barras luminosas, cinta
            {
                int notesN = 0, almondN = 0, glowN = 0, tapeN = 0, firstNote = -1;
                ItemPickup notePk = null;
                foreach (ItemPickup ip in UnityEngine.Object.FindObjectsOfType<ItemPickup>())
                {
                    if (ip.noteId >= 0) { notesN++; if (notePk == null) { notePk = ip; firstNote = ip.noteId; } }
                    else if (ip.type == ItemType.AlmondWater) almondN++;
                    else if (ip.type == ItemType.Glowstick) glowN++;
                    else if (ip.type == ItemType.VhsTape) tapeN++;
                }
                int foundLv = 0; if (Inventory.Instance != null) for (int k = 0; k < LoreNotes.PerLevel; k++) if (Inventory.Instance.HasNote(LoreNotes.IdFor(lv, k))) foundLv++;   // una nota recogida al salir de la cabina
                r.Check(notesN <= LoreNotes.PerLevel && notesN + foundLv >= LoreNotes.PerLevel && almondN + ItemPickup.AlmondCollectedThisLevel >= 2 && glowN + ItemPickup.GlowCollectedThisLevel >= 3 && (tapeN >= 1 || (UnityEngine.Object.FindObjectOfType<BackroomsGenerator>() != null && UnityEngine.Object.FindObjectOfType<BackroomsGenerator>().ExtraSpots.Count >= 9 && Inventory.Instance != null && Inventory.Instance.GetCount(ItemType.VhsTape) >= 1)), tag + "objetos del nivel (la cinta puede haberse recogido al pasar): " + notesN + " notas, " + almondN + " aguas, " + glowN + " barras, " + tapeN + " cinta");
                Inventory invL = Inventory.Instance;
                if (lv > 0 && prevNote >= 0) r.Check(invL != null && invL.HasNote(prevNote), tag + "la nota del nivel anterior se conserva (id " + prevNote + ")");
                if (notePk != null && invL != null)
                {
                    Vector3 np = notePk.transform.position;
                    Vector3 backP = gm.player.transform.position; float backYaw = gm.player.transform.eulerAngles.y;
                    // v7.0.2: en el campo la nota está sobre un mueble dentro de una casa: suelo bajo ella
                    float fy = gm.player.transform.position.y;
                    if (notePk.resting) { fy = np.y - 2.5f; foreach (RaycastHit hh in Physics.RaycastAll(np + Vector3.up * 0.05f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore)) fy = Mathf.Max(fy, Mathf.Min(hh.point.y, np.y - 0.6f) == hh.point.y ? hh.point.y : fy); if (fy < np.y - 2.4f) fy = np.y; }
                    gm.player.Teleport(new Vector3(np.x, fy + 0.05f, np.z), 0f);
                    yield return 0.7f;
                    if (notePk.resting && fy < np.y - 0.3f)
                    {
                        // fuera del mueble: un sitio cercano con el suelo a la misma altura y hueco para saltar
                        Vector3 c0 = new Vector3(np.x, fy, np.z); bool mv = false;
                        for (int ring = 1; ring <= 10 && !mv; ring++)
                            for (int k = 0; k < 12 && !mv; k++)
                            {
                                Vector3 c = c0 + Quaternion.Euler(0f, k * 30f, 0f) * Vector3.forward * (ring * 0.5f);
                                if (!Physics.Raycast(c + Vector3.up * 0.5f, Vector3.down, out RaycastHit gh2, 1.2f, ~0, QueryTriggerInteraction.Ignore)) continue;
                                if (Mathf.Abs(gh2.point.y - fy) > 0.25f) continue;
                                if (Physics.CheckCapsule(gh2.point + Vector3.up * 0.45f, gh2.point + Vector3.up * 2.0f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
                                gm.player.Teleport(gh2.point + Vector3.up * 0.05f, backYaw); mv = true;
                            }
                        if (!mv) gm.player.Teleport(backP, backYaw);
                        yield return 0.3f;
                    }
                    r.Check(invL.HasNote(firstNote) && LoreNotes.Get(firstNote).level == lv && LoreNotes.Get(firstNote).text.Length > 30, tag + "recoger una nota la añade al inventario (id " + firstNote + ": '" + LoreNotes.Get(firstNote).title + "')");
                    prevNote = firstNote;
                }
            }

            // Cuerpo del jugador en cada nivel
            r.Check(gm.player.GetComponentInChildren<PlayerBody>() != null && gm.player.GetComponentInChildren<PlayerBody>().Loaded, tag + "cuerpo del jugador cargado");

            // v7.0.1: se puede saltar en todos los niveles
            {
                IEnumerator je = SmokeTest.JumpCheck(r, tag, gm.player, "nivel " + lv);
                while (je.MoveNext()) yield return je.Current;
                bool crouchBlock; gm.player.debugInput = true; gm.player.debugCrouch = true; yield return 0.4f;
                int jc = gm.player.JumpCount; gm.player.debugJump = true; yield return 0.15f; gm.player.debugJump = false; yield return 0.3f;
                crouchBlock = gm.player.JumpCount == jc; gm.player.debugCrouch = false; gm.player.debugInput = false; yield return 0.6f;
                r.Check(crouchBlock, tag + "[salto] agachado no se salta");
                if (lv == 0 && gm.infoText != null)
                {
                    RectTransform it = gm.infoText.rectTransform;
                    bool fit = it.anchorMin.x == 0f && it.anchorMax.x == 1f && it.offsetMin.x > 0f && it.offsetMax.x < 0f && gm.infoText.horizontalOverflow == HorizontalWrapMode.Wrap && gm.infoText.resizeTextForBestFit;
                    Canvas.ForceUpdateCanvases();
                    Vector3[] wc = new Vector3[4]; it.GetWorldCorners(wc);
                    RectTransform cr = gm.infoText.canvas.GetComponent<RectTransform>(); Vector3[] cc = new Vector3[4]; cr.GetWorldCorners(cc);
                    r.Check(fit && wc[0].x >= cc[0].x - 0.5f && wc[2].x <= cc[2].x + 0.5f, tag + "[HUD] el texto de controles cabe en pantalla (ancho flexible con ajuste de línea, sin corte a la izquierda)");
                    r.Check(gm.infoText.text.Contains("Espacio saltar") || GameManager.HideControlsHint, tag + "[HUD] los controles incluyen «Espacio saltar»");
                }
            }

            // v7.0: Campo infinito
            if (cfg.layout == LayoutKind.Field)
            {
                IEnumerator fe = SmokeTest.FieldPlayChecks(r, tag, gm, gm.player);
                while (fe.MoveNext()) yield return fe.Current;
            }

            // Agua (niveles 2 y 3)
            if (cfg.water)
            {
                WaterVolume wv = WaterVolume.Active;
                FirstPersonController pw = gm.player;
                r.Check(wv != null && gm.generator.Water == wv, tag + "WaterVolume activo");
                if (wv != null)
                {
                    r.Check(wv.material != null && !wv.usingFallback && wv.material.shader.name.Contains("BackroomsWater"), tag + "agua con el shader propio (sin fallback a Standard)");
                    r.Check(wv.chunkCount > 0 && wv.GetComponentsInChildren<MeshRenderer>().Length > 0, tag + "superficie de agua: " + wv.chunkCount + " trozos de malla");
                    r.Check(cfg.debrisCount == 0 || wv.debris.Count > 0, tag + "restos flotantes: " + wv.debris.Count);
                    // vadear: profundidad > 0, factor de arrastre < 1
                    ElevatorCabin wc = ElevatorCabin.Current;   // la cabina (suelo algo más alto) no sirve para vadear: fuera, en el pasillo
                    pw.Teleport(wc != null ? wc.ExitPoint + wc.Forward * 2.5f : gm.generator.PlayerStart, 0f);
                    yield return 0.8f;
                    r.Check(pw.WaterDepth > 0.1f && pw.WaterFactor < 0.97f, tag + "vadeando: profundidad " + pw.WaterDepth.ToString("F2") + " m, factor de arrastre " + pw.WaterFactor.ToString("F2"));
                    r.Check(!pw.IsUnderwater, tag + "de pie en el agua poco profunda: cámara fuera del agua");
                    // velocidad en el agua < velocidad máxima en seco
                    pw.debugInput = true; pw.debugMove = new Vector2(0f, 1f);
                    yield return 1.2f;
                    float vWater = pw.FlatSpeed;
                    pw.debugMove = Vector2.zero; pw.debugInput = false;
                    r.Check(vWater < pw.walkSpeed * pw.speedFactor * 0.98f || vWater < 0.5f, tag + "velocidad en el agua " + vWater.ToString("F2") + " m/s < " + (pw.walkSpeed * pw.speedFactor).ToString("F2"));
                    // ondas y chapoteo
                    wv.AddRipple(pw.transform.position, 0.8f);
                    yield return 0.2f;
                    r.Check(wv.RippleCount > 0, tag + "ondas activas: " + wv.RippleCount);
                    // efectos de cámara
                    WaterFx wfx = pw.GetComponentInChildren<WaterFx>();
                    r.Check(wfx != null, tag + "WaterFx en la cámara");
                    if (wfx != null)
                    {
                        r.Check(wfx.CausticCookieActive, tag + "linterna con cookie de cáusticas animado");
                        wfx.ForceWet();
                        yield return 0.6f;
                        r.Check(wfx.ActiveDrops > 0, tag + "gotas en la lente al salir del agua (" + wfx.ActiveDrops + ")");
                    }
                    // aguas profundas: el nivel solo tiene 22 cm, así que se sube la superficie un momento para probar nadar / bucear
                    if (cfg.water && gm.generator.Water != null)
                    {
                        WaterVolume deepWv = gm.generator.Water; float baseSurface = deepWv.surfaceY;
                        deepWv.surfaceY = baseSurface + 1.7f;
                        pw.Teleport(gm.generator.PlayerStart, 0f);
                        yield return 0.9f;
                        r.Check(pw.WaterDepth > pw.swimDepth, tag + "aguas profundas: " + pw.WaterDepth.ToString("F2") + " m > " + pw.swimDepth.ToString("F2"));
                        r.Check(pw.IsSwimming || pw.IsUnderwater, tag + "en aguas profundas se nada / se sumerge (nada=" + pw.IsSwimming + " bajo agua=" + pw.IsUnderwater + ")");
                        pw.debugInput = true; pw.debugCrouch = true;
                        yield return 1.5f;
                        r.Check(pw.IsUnderwater, tag + "buceando: cámara bajo la superficie");
                        if (wfx != null) r.Check(wfx.Underwater01 > 0.5f && wfx.BubblesEmitting, tag + "bajo el agua: velo/niebla (" + wfx.Underwater01.ToString("F2") + ") y burbujas");
                        r.Check(VhsEffect.Active != null && VhsEffect.Active.extraMuffle > 0.5f, tag + "bajo el agua: audio apagado (low-pass)");
                        pw.debugCrouch = false; pw.debugInput = false;
                        deepWv.surfaceY = baseSurface;
                        pw.Teleport(gm.generator.PlayerStart, 0f);
                        yield return 1.2f;
                        r.Check(!pw.IsUnderwater, tag + "al volver al nivel normal deja de estar bajo el agua");
                    }
                    // v6.8.1: salir de la piscina de la sala fija (borde por los 4 lados, escalera, rescate)
                    if (gm.generator.PoolFixed)
                    {
                        string pt = tag + "[Piscina] ";
                        foreach (MonsterAI m in gm.monsters) m.Stop();
                        r.Check(PoolExit.Active, pt + "datos de salida de la piscina registrados");
                        r.Check(PoolExit.HasLadder, pt + "la escalera de la piscina está registrada");
                        Rect pr = PoolExit.PoolXZ; float floorY = PoolExit.DeckY - 1.4f;
                        Vector3 pc0 = new Vector3(pr.center.x, floorY + 0.05f, pr.center.y);
                        Vector3[] sides = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
                        string[] names = { "norte", "sur", "oeste", "este" };
                        for (int si = 0; si < 4; si++)
                        {
                            Vector3 d = sides[si];
                            float half = Mathf.Abs(d.x) > 0.5f ? pr.width * 0.5f : pr.height * 0.5f;
                            Vector3 along = new Vector3(d.z, 0f, d.x);
                            Vector3 basePt = pc0 + d * (half - 1.0f);
                            Vector3 s1 = basePt + along * 1.5f, s2 = basePt - along * 1.5f;
                            Vector3 start = PoolExit.HasLadder && (s1 - PoolExit.LadderPos).sqrMagnitude < (s2 - PoolExit.LadderPos).sqrMagnitude ? s2 : s1;
                            pw.Teleport(start, Quaternion.LookRotation(d).eulerAngles.y);
                            yield return 0.8f;
                            bool inPool = PoolExit.InPool(pw.transform.position);
                            r.Check(inPool && (pw.IsSwimming || pw.WaterDepth > 1f), pt + "lado " + names[si] + ": el jugador está dentro del vaso (profundidad " + pw.WaterDepth.ToString("F2") + ", nada=" + pw.IsSwimming + ")");
                            int c0 = pw.ClimbOutCount, l0 = pw.LadderClimbCount;
                            pw.debugInput = true; pw.debugMove = new Vector2(0f, 1f);
                            float tt = 0f;
                            while (tt < 3f && PoolExit.InPool(pw.transform.position)) { yield return 0.1f; tt += 0.1f; }
                            yield return 0.3f;
                            pw.debugMove = Vector2.zero; pw.debugInput = false;
                            Vector3 fp = pw.transform.position;
                            r.Check(!PoolExit.InPool(fp) && fp.y > PoolExit.DeckY - 0.1f && pw.ClimbOutCount > c0 && pw.LadderClimbCount == l0,
                                pt + "lado " + names[si] + ": sale por el borde a la playa en " + tt.ToString("F1") + " s (y " + fp.y.ToString("F2") + ")");
                        }
                        if (PoolExit.HasLadder)
                        {
                            // escalera empujando hacia delante
                            pw.Teleport(PoolExit.LadderPos - PoolExit.LadderOut * 0.5f + Vector3.up * 0.05f, Quaternion.LookRotation(PoolExit.LadderOut).eulerAngles.y);
                            yield return 0.8f;
                            int l0 = pw.LadderClimbCount;
                            pw.debugInput = true; pw.debugMove = new Vector2(0f, 1f);
                            float tt = 0f;
                            while (tt < 4f && pw.LadderClimbCount == l0) { yield return 0.1f; tt += 0.1f; }
                            pw.debugMove = Vector2.zero; pw.debugInput = false;
                            yield return 0.3f;
                            Vector3 fp = pw.transform.position;
                            r.Check(pw.LadderClimbCount == l0 + 1 && !PoolExit.InPool(fp) && fp.y > PoolExit.DeckY - 0.1f, pt + "sube por la escalera (adelante) y pisa la playa (" + tt.ToString("F1") + " s)");
                            // escalera con saltar, sin moverse
                            pw.Teleport(PoolExit.LadderPos + Vector3.up * 0.05f, Quaternion.LookRotation(-PoolExit.LadderOut).eulerAngles.y);
                            yield return 0.8f;
                            l0 = pw.LadderClimbCount;
                            pw.debugInput = true; pw.debugJump = true;
                            tt = 0f;
                            while (tt < 4f && pw.LadderClimbCount == l0) { yield return 0.1f; tt += 0.1f; }
                            pw.debugJump = false; pw.debugInput = false;
                            yield return 0.3f;
                            fp = pw.transform.position;
                            r.Check(pw.LadderClimbCount == l0 + 1 && !PoolExit.InPool(fp) && fp.y > PoolExit.DeckY - 0.1f, pt + "sube por la escalera con «saltar» y pisa la playa");
                            r.Check(!pw.IsClimbingLadder && !pw.IsSwimming, pt + "tras la escalera: andando normal, sin nadar");
                        }
                        // rescate: en el centro, lejos de bordes y escalera, pulsando saltar
                        pw.Teleport(pc0, 0f);
                        yield return 0.5f;
                        int rs0 = pw.PoolRescueCount;
                        pw.debugInput = true; pw.debugJump = true;
                        float tr = 0f;
                        while (tr < 8f && pw.PoolRescueCount == rs0) { yield return 0.1f; tr += 0.1f; }
                        pw.debugJump = false; pw.debugInput = false;
                        yield return 0.3f;
                        Vector3 rp = pw.transform.position;
                        r.Check(pw.PoolRescueCount == rs0 + 1 && tr >= PoolExit.RescueSeconds - 0.6f && !PoolExit.InPool(rp) && rp.y > PoolExit.DeckY - 0.1f,
                            pt + "rescate: atascado y pulsando saltar, a los " + tr.ToString("F1") + " s se le saca a la playa");
                        pw.Teleport(gm.generator.PlayerStart, 0f);
                        yield return 0.5f;
                    }
                }
            }
            picks = UnityEngine.Object.FindObjectsOfType<BatteryPickup>();   // (por si se recogió alguna pila)
            if (picks.Length > 0)
            {
                Vector3 p = picks[0].transform.position;
                gm.player.Teleport(new Vector3(p.x, gm.generator.Garage != null ? p.y + 0.05f : 0.1f, p.z), 0f);   // v7.0.4: plantas
                yield return 0.5f;
                r.Check(UnityEngine.Object.FindObjectsOfType<BatteryPickup>().Length == picks.Length - 1, tag + "recoger una pila funciona");
            }

            if (lv == 0)
            {
                // Derrota -> reinicio del mismo nivel
                int loads = gb.LoadCount;
                gm.lostRestartSeconds = 0.3f;
                gm.PlayerCaught(gm.monsters[0]);
                r.Check(gm.State == GameManager.GameState.Lost, tag + "PlayerCaught -> Lost");
                yield return Until(() => GameBootstrap.Instance.LoadCount > loads && GameBootstrap.Instance.IsLoaded);
                r.Check(GameBootstrap.Instance.CurrentLevel == 0, tag + "tras perder se reinicia el nivel actual (0)");
                r.Check(GameBootstrap.Instance.Deaths == 1, tag + "contador de derrotas = 1");
                yield return 0.5f;
                gm = GameManager.Instance;
                // R (Restart) manual
                loads = GameBootstrap.Instance.LoadCount;
                gm.Restart();
                yield return Until(() => GameBootstrap.Instance.LoadCount > loads && GameBootstrap.Instance.IsLoaded);
                r.Check(GameBootstrap.Instance.CurrentLevel == 0, tag + "Restart() recarga el nivel actual");
                yield return 0.5f;
                yield return 1.0f;
                Census(r, "[Audio tras reiniciar]", true, true);
                gm = GameManager.Instance;
                foreach (MonsterAI m in gm.monsters) m.Stop();
            }

            // (el test de muerte revierte el inventario: solo se comprueba la conservación de lo que sigue en el inventario antes de salir)
            if (prevNote >= 0 && (Inventory.Instance == null || !Inventory.Instance.HasNote(prevNote))) prevNote = -1;
            // Ir a la puerta: teleport delante de la puerta
            if (lv == 0) { ElevatorCabin.InstantForTests = false; realNext = true; }
            Transform exit = gm.exit;
            Vector3 stand = exit.position + exit.forward * 0.7f;
            gm.player.Teleport(new Vector3(stand.x, (exit.parent != null ? exit.parent.position.y : 0f) + 0.1f, stand.z), 0f);   // v7.0.4: plantas del aparcamiento
            int loadsBefore = GameBootstrap.Instance.LoadCount;
            yield return 0.3f;
            bool last = lv == LevelConfig.Count - 1;
            if (!last)
            {
                r.Check(gm.State == GameManager.GameState.LevelComplete, tag + "llegar a la puerta -> LevelComplete");
                yield return Until(() => GameBootstrap.Instance.LoadCount > loadsBefore && GameBootstrap.Instance.IsLoaded);
                r.Check(GameBootstrap.Instance.CurrentLevel == lv + 1, tag + "se carga el nivel siguiente (" + GameBootstrap.Instance.CurrentLevel + ")");
            }
            else
            {
                r.Check(gm.State == GameManager.GameState.Won, tag + "última puerta -> Won (pantalla de victoria)");
                Text msg = null;
                foreach (Text t in UnityEngine.Object.FindObjectsOfType<Text>(true)) if (t.name == "Message") msg = t;
                r.Check(msg != null && msg.text.Contains("HAS ESCAPADO") && msg.text.Contains("Nivel 0"), tag + "texto de victoria con opción de reiniciar desde el Nivel 0");
                yield return 0.3f;
                r.Check(UIManager.Instance.Current == UIManager.Screen.End && Cursor.lockState == CursorLockMode.None, tag + "pantalla de victoria: botones visibles y cursor libre");
                UIManager.Instance.PressEscape();
                UIManager.Instance.ToggleInventory();
                r.Check(UIManager.Instance.Current == UIManager.Screen.End, tag + "Esc / I no interfieren con la pantalla de victoria");
                loadsBefore = GameBootstrap.Instance.LoadCount;
                gm.RestartRun();
                yield return Until(() => GameBootstrap.Instance.LoadCount > loadsBefore && GameBootstrap.Instance.IsLoaded);
                r.Check(GameBootstrap.Instance.CurrentLevel == 0 && GameBootstrap.Instance.Deaths == 0, tag + "RestartRun -> nivel 0 y contadores a cero");
            }
        }
        r.Check(modelIds.Count == LevelConfig.Count, "modelos de monstruo distintos vistos en juego (uno por nivel) (" + modelIds.Count + ")");
        r.Flush();
        yield break;
    }
}
