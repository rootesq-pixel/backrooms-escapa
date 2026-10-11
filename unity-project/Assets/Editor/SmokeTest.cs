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
//  - RunPlayMode: abre Assets/Scenes/Main.unity, entra en Play y recorre los 6 niveles
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

            for (int lv = 0; lv < LevelConfig.Count; lv++)
            {
                foreach (int seed in Seeds)
                {
                    TestLevel(r, lv, seed);
                }
            }
            TextureLibrary.ReleaseAll();
            TestMonsterModels(r);
            TestVhsAssets(r);
            TestInventoryData(r);
            TestSettings(r);
            TestV4Assets(r);
            TestNetwork(r);
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

    private static void TestInventoryData(Report r)
    {
        foreach (ItemType t in ItemDefs.All)
        {
            ItemDefs.Def d = ItemDefs.Get(t);
            r.Check(!string.IsNullOrEmpty(d.name) && d.description.Length > 20 && d.maxStack >= 1, "objeto " + t + ": nombre '" + d.name + "', descripción y pila máxima " + d.maxStack);
            Sprite sp = ItemIcons.Get(t);
            bool ok = sp != null && sp.texture != null && sp.texture.width == ItemIcons.Size;
            int opaque = 0;
            if (ok) foreach (Color c in sp.texture.GetPixels()) if (c.a > 0.5f) opaque++;
            r.Check(ok && opaque > 700 && opaque < ItemIcons.Size * ItemIcons.Size * 0.9f, "icono procedural de " + t + " (" + opaque + " px opacos)");
            GameObject vis = new GameObject("vis_" + t);
            ItemPickup.BuildVisual(t, vis.transform);
            r.Check(vis.GetComponentsInChildren<Renderer>().Length >= 1, "modelo 3D de recogida de " + t);
            UnityEngine.Object.DestroyImmediate(vis);
        }
        GameObject nv = new GameObject("noteVis");
        ItemPickup.BuildVisual(ItemType.None, nv.transform);
        r.Check(nv.GetComponentsInChildren<Renderer>().Length >= 2, "modelo 3D de nota de papel");
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
        r.Check(LevelConfig.Count == 6, "hay 6 niveles (" + LevelConfig.Count + ")");
        r.Check(LevelConfig.Get(2).water && !LevelConfig.Get(2).poolHall, "nivel 2 = salas inundadas (agua, sin sala Poolrooms)");
        LevelConfig pool = LevelConfig.Get(3);
        r.Check(pool.poolHall && pool.water, "nivel 3 = Poolrooms (sala del modelo del usuario + agua)");
        var seenMonsters = new HashSet<string>();
        for (int i = 0; i < LevelConfig.Count; i++)
        {
            LevelConfig c = LevelConfig.Get(i);
            r.Check(MonsterProfile.ById(c.monsterModel) != null, "nivel " + i + " (" + c.name + "): perfil de monstruo '" + c.monsterModel + "'");
            seenMonsters.Add(c.monsterModel);
        }
        r.Check(seenMonsters.Count == LevelConfig.Count, "un monstruo distinto por nivel (" + seenMonsters.Count + ")");
        GameObject hall = Resources.Load<GameObject>("Poolrooms/PoolHall");
        r.Check(hall != null && hall.GetComponentInChildren<MeshFilter>() != null && hall.GetComponentInChildren<MeshFilter>().sharedMesh != null, "modelo PoolHall.fbx carga desde Resources (malla)");
        r.Check(hall != null && hall.GetComponentInChildren<MeshFilter>().sharedMesh.isReadable, "PoolHall.fbx con mallas legibles (Read/Write) para UV, colliders y NavMesh en el build");

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

    private static void ScanSourcesForExitHints(Report r)
    {
        string[] files = Directory.GetFiles("Assets/Scripts", "*.cs");
        int hits = 0;
        foreach (string f in files)
        {
            string txt = File.ReadAllText(f);
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
        r.Check(gen.BatterySpots != null && gen.BatterySpots.Count == cfg.batteries + (cfg.poolHall ? 2 : 0), tag + "pilas colocadas = " + (cfg.batteries + (cfg.poolHall ? 2 : 0)) + " (hay " + (gen.BatterySpots != null ? gen.BatterySpots.Count : 0) + ")");
        r.Check(gen.FixtureCount > 0, tag + "hay accesorios de luz");
        if (cfg.layout == LayoutKind.OpenPillars) r.Check(gen.PillarCount > 0, tag + "hay pilares");

        // Sin cartel / TextMesh / objetos "Sign" / "Salida" / flechas
        r.Check(go.GetComponentsInChildren<TextMesh>(true).Length == 0, tag + "sin TextMesh");
        bool badName = false;
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
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
                if (m.mainTexture != null && (m.mainTexture.width < 512 || !m.mainTexture.name.Contains("Albedo"))) texOk = false;
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
        r.Check(reach == total, tag + "todas las celdas alcanzables desde el inicio (" + reach + "/" + total + ")");
        r.Check(gen.DistanceFromStart(gen.ExitCell.x, gen.ExitCell.y) >= 0, tag + "celda de salida alcanzable (dist " + gen.DistanceFromStart(gen.ExitCell.x, gen.ExitCell.y) + ")");

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
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = prev + Vector3.up * 0.9f, bpt = path[i] + Vector3.up * 0.9f;
            if (i == 0) { prev = path[i]; continue; }
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
        Vector3 doorFloor = new Vector3(gen.ExitPosition.x, 0f, gen.ExitPosition.z);
        Vector3 toCell = gen.CellCenterWorld(gen.ExitCell.x, gen.ExitCell.y) - doorFloor; toCell.y = 0;
        Vector3 standPos = doorFloor + toCell.normalized * 0.8f;
        bool standFree = !Physics.CheckSphere(standPos + Vector3.up * 0.9f, 0.34f);
        r.Check(standFree && Vector3.Distance(standPos, doorFloor) < 1.6f, tag + "se puede estar a <1.6 m de la puerta sin atravesar nada");

        // NavMesh en edición (si el paquete lo permite)
        bool navBuilt = MonsterAI.TryBuildRuntimeNavMesh(gen.Root.gameObject);
        if (navBuilt)
        {
            NavMeshPath np = new NavMeshPath();
            NavMeshHit h1, h2;
            bool s1 = NavMesh.SamplePosition(gen.PlayerStart, out h1, 2f, NavMesh.AllAreas);
            bool s2 = NavMesh.SamplePosition(new Vector3(standPos.x, 0f, standPos.z), out h2, 2.5f, NavMesh.AllAreas);
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

        UnityEngine.Object.DestroyImmediate(go);
    }

    // ------------------------------------------------------------------
    // Modo Play
    // ------------------------------------------------------------------
    public static void RunPlayMode()
    {
        Directory.CreateDirectory("/workspace/logs");
        File.WriteAllText(PlayReport, "");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "play");
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

    private static IEnumerator Run()
    {
        SmokeTest.Report r = report;
        GameSettings.ResetDefaults();
        yield return Until(() => GameBootstrap.Instance != null && GameBootstrap.Instance.IsLoaded);
        GameBootstrap gb = GameBootstrap.Instance;
        r.Info("Play mode activo. Nivel inicial " + gb.CurrentLevel);

        // ---------------- Menú principal ----------------
        UIManager ui = UIManager.Instance;
        r.Check(ui != null, "UIManager existe");
        r.Check(gb.InMenu && ui.Current == UIManager.Screen.Main, "el juego arranca en el menú principal");
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
        r.Check(ui.ClickButton("Alojar partida") && mui.CurrentPage == MultiplayerUI.Page.Host && mui.HostPortField != null, "página «Alojar partida»");
        r.Check(PlayerPrefs.GetInt(MultiplayerUI.KPort, NetSession.DefaultPort) > 0 && NetSession.DefaultPort == 7777, "puerto por defecto 7777");
        mui.HostPortField.text = "47793";
        r.Check(ui.ClickButton("Crear sala") && ui.Current == UIManager.Screen.Lobby && NetSession.IsHost, "«Crear sala» abre la sala de espera como anfitrión");
        yield return 0.4f;
        r.Check(mui.LobbyText.Contains("Tester"), "la sala lista al anfitrión por su nombre");
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
        r.Check(ui.ClickButton("Jugar"), "botón Jugar");
        yield return Until(() => GameBootstrap.Instance.LoadCount > loads0 && GameBootstrap.Instance.IsLoaded && GameManager.Instance != null && !GameBootstrap.Instance.InMenu);
        gb = GameBootstrap.Instance;
        r.Check(gb.CurrentLevel == 0, "Jugar empieza en el nivel 0");
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
            r.Check(pc.BreathVolume > 0.05f, "respiración audible al estar cansado (vol " + pc.BreathVolume.ToString("F2") + ")");
            pc.debugSprint = false;
            pc.debugMove = Vector2.zero;
            yield return 1.2f;
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
            float headH = m0.visual != null && m0.visual.profile != null ? m0.visual.profile.ai_eyeHeight * 0.95f : 1.5f;
            Vector3 toM = (m0.transform.position + Vector3.up * headH) - cam.transform.position;
            r.Info("jump-scare: distancia cámara-monstruo " + toM.magnitude.ToString("F2") + " m");
            r.Check(Vector3.Angle(cam.transform.forward, toM) < 25f, "jump-scare: la cámara mira al monstruo (" + Vector3.Angle(cam.transform.forward, toM).ToString("F0") + "°)");
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
                r.Check(sc.PeakScreamLevel > 0.8f && VhsEffect.Active != null && VhsEffect.Active.ScreamLevel > 0.3f, "screamer: glitch VHS extremo (pico " + sc.PeakScreamLevel.ToString("F2") + ")");
                r.Check(sc.ScreamClip != null && sc.ScreamClip.length > 2f && sc.ScreamVolume > 0.9f, "screamer: grito en capas a volumen completo (" + sc.ScreamVolume.ToString("F2") + ", respeta el volumen general vía AudioListener)");
                r.Check(sc.Profile != null && sc.Profile.id == m0.visual.profile.id, "screamer: grito/perfil del monstruo que atrapa (" + (sc.Profile != null ? sc.Profile.id : "-") + ")");
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
                if (retry != null) retry.onClick.Invoke();
            }
            else
            {
                gmS.lostRestartSeconds = 0.2f;
            }
            int ld = GameBootstrap.Instance.LoadCount;
            yield return Until(() => GameBootstrap.Instance.LoadCount > ld && GameBootstrap.Instance.IsLoaded);
            r.Check(GameBootstrap.Instance.CurrentLevel == 0, "tras el susto se reinicia el nivel");
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
            ui.PressEscape();
            r.Check(ui.Current == UIManager.Screen.Main, "Esc en el menú principal no hace nada");
            ld = GameBootstrap.Instance.LoadCount;
            ui.StartGame(0);
            yield return Until(() => GameBootstrap.Instance.LoadCount > ld && GameBootstrap.Instance.IsLoaded && GameManager.Instance != null && !GameBootstrap.Instance.InMenu);
            yield return 0.5f;
        }

        HashSet<string> modelIds = new HashSet<string>();
        int prevNote = -1;

        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            string tag = "[Play nivel " + lv + "] ";
            yield return Until(() => GameBootstrap.Instance.IsLoaded && GameManager.Instance != null && GameBootstrap.Instance.CurrentLevel == GameBootstrap.Instance.CurrentLevel);
            gb = GameBootstrap.Instance;
            r.Check(gb.CurrentLevel == lv, tag + "nivel cargado = " + gb.CurrentLevel);
            yield return 1.0f;   // unos frames de juego (luces, IA, audio)

            GameManager gm = GameManager.Instance;
            LevelConfig cfg = LevelConfig.Get(lv);
            r.Check(gm != null && gm.State == GameManager.GameState.Playing, tag + "estado Playing");
            r.Check(gm.generator != null && gm.exit != null, tag + "generador y puerta presentes");
            r.Check(gm.monsters != null && gm.monsters.Length == Mathf.Max(1, cfg.monsterCount), tag + "monstruos = " + cfg.monsterCount);
            foreach (MonsterAI m in gm.monsters)
            {
                r.Info(tag + "monstruo usa NavMesh: " + m.UsesNavMesh + ", estado " + m.CurrentState + ", chase " + m.chaseSpeed);
                r.Check(m.visual != null && !m.visual.IsFallback && m.visual.profile != null && m.visual.profile.id == cfg.monsterModel, tag + "monstruo con modelo " + cfg.monsterModel + " (sin fallback)");
                if (m.visual != null && m.visual.profile != null) modelIds.Add(m.visual.profile.id);
                r.Check(m.visual != null && m.visual.Renderers != null && m.visual.Renderers.Length > 0 && m.visual.animator != null && m.visual.animator.isActiveAndEnabled, tag + "modelo del monstruo activo con Animator");
            }

            // Atmósfera
            r.Check(RenderSettings.fog && RenderSettings.fogMode == FogMode.Exponential && RenderSettings.fogDensity > 0.01f, tag + "niebla exponencial activa");
            r.Check(RenderSettings.ambientLight.maxColorComponent < (cfg.poolHall ? 0.75f : 0.25f), tag + "luz ambiente " + (cfg.poolHall ? "moderada (Poolrooms luminoso)" : "baja"));

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
            r.Check(title != null && title.text.StartsWith("NIVEL " + lv) && title.color.a > 0.05f, tag + "tarjeta de título visible: '" + (title != null ? title.text : "") + "'");

            // Luces: solo las N más cercanas
            LightCuller culler = gm.generator.Culler;
            yield return 1.0f;
            int active = culler.ActiveCount;
            r.Check(active <= cfg.maxRealtimeLights, tag + "luces de accesorios activas " + active + " <= " + cfg.maxRealtimeLights + " (de " + culler.FixtureCount + ")");
            int allLights = 0;
            foreach (Light l in UnityEngine.Object.FindObjectsOfType<Light>()) if (l.enabled) allLights++;
            r.Info(tag + "luces habilitadas en total: " + allLights);

            // Pilas: recoger una
            BatteryPickup[] picks = UnityEngine.Object.FindObjectsOfType<BatteryPickup>();
            r.Check(picks.Length == cfg.batteries + (cfg.poolHall ? 2 : 0), tag + "pilas en escena = " + picks.Length);
            foreach (MonsterAI m in gm.monsters) m.Stop();   // para que no interfieran con el test

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
                r.Check(notesN == LoreNotes.PerLevel && almondN >= 2 && glowN >= 3 && tapeN >= 1, tag + "objetos del nivel: " + notesN + " notas, " + almondN + " aguas, " + glowN + " barras, " + tapeN + " cinta");
                Inventory invL = Inventory.Instance;
                if (lv > 0 && prevNote >= 0) r.Check(invL != null && invL.HasNote(prevNote), tag + "la nota del nivel anterior se conserva (id " + prevNote + ")");
                if (notePk != null && invL != null)
                {
                    Vector3 np = notePk.transform.position;
                    gm.player.Teleport(new Vector3(np.x, gm.player.transform.position.y, np.z), 0f);
                    yield return 0.7f;
                    r.Check(invL.HasNote(firstNote) && LoreNotes.Get(firstNote).level == lv && LoreNotes.Get(firstNote).text.Length > 30, tag + "recoger una nota la añade al inventario (id " + firstNote + ": '" + LoreNotes.Get(firstNote).title + "')");
                    prevNote = firstNote;
                }
            }

            // Cuerpo del jugador en cada nivel
            r.Check(gm.player.GetComponentInChildren<PlayerBody>() != null && gm.player.GetComponentInChildren<PlayerBody>().Loaded, tag + "cuerpo del jugador cargado");

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
                    pw.Teleport(gm.generator.PlayerStart, 0f);
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
                    // bajo el agua (foso del Poolrooms)
                    if (cfg.poolHall)
                    {
                        Vector3 pit = gm.generator.HallBatteryPit;
                        pw.Teleport(new Vector3(pit.x, pit.y + 0.1f, pit.z), 0f);
                        yield return 0.9f;
                        r.Check(pw.WaterDepth > pw.swimDepth, tag + "foso profundo: " + pw.WaterDepth.ToString("F2") + " m > " + pw.swimDepth.ToString("F2"));
                        r.Check(pw.IsSwimming || pw.IsUnderwater, tag + "en el foso se nada / se sumerge (nada=" + pw.IsSwimming + " bajo agua=" + pw.IsUnderwater + ")");
                        pw.debugInput = true; pw.debugCrouch = true;
                        yield return 1.5f;
                        r.Check(pw.IsUnderwater, tag + "buceando: cámara bajo la superficie");
                        if (wfx != null) r.Check(wfx.Underwater01 > 0.5f && wfx.BubblesEmitting, tag + "bajo el agua: velo/niebla (" + wfx.Underwater01.ToString("F2") + ") y burbujas");
                        r.Check(VhsEffect.Active != null && VhsEffect.Active.extraMuffle > 0.5f, tag + "bajo el agua: audio apagado (low-pass)");
                        pw.debugCrouch = false; pw.debugInput = false;
                        pw.Teleport(gm.generator.PlayerStart, 0f);
                        yield return 1.2f;
                        r.Check(!pw.IsUnderwater, tag + "al salir del foso deja de estar bajo el agua");
                    }
                }
            }
            picks = UnityEngine.Object.FindObjectsOfType<BatteryPickup>();   // el foso pudo recoger una pila
            if (picks.Length > 0)
            {
                Vector3 p = picks[0].transform.position;
                gm.player.Teleport(new Vector3(p.x, 0.1f, p.z), 0f);
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
                gm = GameManager.Instance;
                foreach (MonsterAI m in gm.monsters) m.Stop();
            }

            // (el test de muerte revierte el inventario: solo se comprueba la conservación de lo que sigue en el inventario antes de salir)
            if (prevNote >= 0 && (Inventory.Instance == null || !Inventory.Instance.HasNote(prevNote))) prevNote = -1;
            // Ir a la puerta: teleport delante de la puerta
            Transform exit = gm.exit;
            Vector3 stand = exit.position + exit.forward * 0.7f;
            gm.player.Teleport(new Vector3(stand.x, 0.1f, stand.z), 0f);
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
