using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// v7.1.0 Parque de bolas: salas de juegos (malla cocida + bolas instanciadas), vadeo, llave enterrada en una piscina, objetos
// en el atrezo, el monstruo que se esconde bajo las bolas (sumergirse / bulto / emerger con estallido), audio del parque,
// fluorescentes que parpadean y la reducción de bolas en Bajo. Por nivel desde SmokeTest (solo en niveles con salas).
public static class V710Checks
{
    private static float PathLen(Vector3 a, Vector3 b, float rb = 3f)
    {
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 2.5f, NavMesh.AllAreas) || !NavMesh.SamplePosition(b, out NavMeshHit hb, rb, NavMesh.AllAreas)) return -1f;
        if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
        float L = 0f; for (int i = 1; i < path.corners.Length; i++) L += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        return L;
    }

    // malla de navegación «del jugador» (radio 0.36 m, escalón 0.3 m, colisiones reales): para comprobar que el jugador llega a
    // sitios estrechos (tras el mostrador, sala de fiestas) por donde los monstruos (radio 0.5 m) no caben
    public static string LastNavInfo = "";
    private static NavMeshDataInstance playerNav; private static int playerAgent = -1;
    public static void BuildPlayerNav(Transform root)
    {
        ClearPlayerNav();
        NavMeshBuildSettings st = NavMesh.CreateSettings();
        st.agentRadius = 0.36f; st.agentHeight = 1.8f; st.agentClimb = 0.3f; st.agentSlope = 45f;
        st.overrideVoxelSize = true; st.voxelSize = 0.12f;
        Bounds b = new Bounds(root.position, Vector3.zero);
        foreach (Renderer rr in root.GetComponentsInChildren<Renderer>()) b.Encapsulate(rr.bounds);
        b.Expand(2f);
        List<NavMeshBuildSource> src = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(b, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), src);
        src.RemoveAll(x => x.component != null && x.component is Collider c && c.isTrigger);
        NavMeshData d = NavMeshBuilder.BuildNavMeshData(st, src, b, Vector3.zero, Quaternion.identity);
        playerNav = NavMesh.AddNavMeshData(d); playerAgent = st.agentTypeID;
        LastNavInfo = "fuentes " + src.Count + ", límites " + b.size + ", datos " + (d != null) + ", válido " + playerNav.valid + ", agente " + playerAgent;
    }
    public static int PlayerAgentId { get { return playerAgent; } }
    public static void ClearPlayerNav() { if (playerNav.valid) playerNav.Remove(); playerNav = default(NavMeshDataInstance); }
    public static float PlayerPathLen(Vector3 a, Vector3 b, float rb = 1.2f)
    {
        if (!playerNav.valid) return -1f;
        NavMeshQueryFilter q = new NavMeshQueryFilter { agentTypeID = playerAgent, areaMask = NavMesh.AllAreas };
        if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 2.5f, q)) return -1f;
        float r1 = TryPath(ha.position, b, b, rb, q);
        return r1 >= 0f ? r1 : TryPath(ha.position, b + Vector3.down * 1.0f, b, rb, q);   // en un mueble: desde el suelo de al lado
    }
    private static float TryPath(Vector3 from, Vector3 probe, Vector3 b, float rb, NavMeshQueryFilter q)
    {
        if (!NavMesh.SamplePosition(probe, out NavMeshHit hb, rb, q)) return -1f;
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, hb.position, q, path) || path.status != NavMeshPathStatus.PathComplete) return -1f;
        float L = 0f; for (int i = 1; i < path.corners.Length; i++) L += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        // el punto alcanzado debe quedar a distancia de recogida (radio 1.2 m, desnivel < 2.2 m)
        Vector3 e = path.corners[path.corners.Length - 1];
        Vector3 dd = b - e; if (dd.magnitude > 1.4f || Mathf.Abs(dd.y) > 2.2f) return -1f;
        return L;
    }

    public static IEnumerator LevelChecks(SmokeTest.Report r, string tag, int lv, LevelConfig cfg, GameManager gm)
    {
        BackroomsGenerator gen = gm.generator;
        if (gen == null || gen.Halls.Count == 0) yield break;
        string T = tag + "[Parque] ";
        Vector3 home = gm.player.transform.position; float homeYaw = gm.player.transform.eulerAngles.y;
        foreach (MonsterAI m in gm.monsters) if (m != null) m.Stop();
        List<ItemPickup> off = new List<ItemPickup>();
        foreach (ItemPickup ip in Object.FindObjectsOfType<ItemPickup>()) if (ip.enabled) { ip.enabled = false; off.Add(ip); }

        // ---------------- salas ----------------
        r.Check(BallpitSetPiece.HasBakedAssets, T + "malla cocida (Cycles), lightmap EXR y sondas presentes");
        r.Check(BallPitField.All.Count == gen.Halls.Count && gen.Halls.Count >= 3, T + gen.Halls.Count + " salas de bolas, una zona de bolas por sala (" + BallPitField.All.Count + ")");
        int total = 0; bool colOk = true;
        foreach (BallPitField f in BallPitField.All) { total += f.BallCount; if (f.GetComponentInParent<MeshCollider>() == null && f.transform.root.GetComponentInChildren<MeshCollider>() == null) colOk = false; }
        r.Check(total > 60000, T + "miles de bolas instanciadas en GPU: " + total);
        r.Check(colOk, T + "colisión de la malla cocida");
        r.Check(HallFlicker.Count >= gen.Halls.Count, T + "fluorescentes que parpadean/zumban: " + HallFlicker.Count);
        r.Info(T + "accesorios con luz cocida del soporte: " + BallpitSetPiece.FlattenedProps + ", tangentes reparadas: " + BallpitSetPiece.FixedTangents);
        r.Check(BallpitSetPiece.FlattenedProps >= 20, T + "accesorios pequeños (platos, tarta, vasos...) toman la luz del soporte (" + BallpitSetPiece.FlattenedProps + ")");

        {   // las salas (con muebles, piscinas y tubos) se cruzan andando: malla de navegación hasta cada sala y hasta la salida
            int reach = 0;
            foreach (GameObject root in BallpitSetPiece.Roots)
                if (root != null && PathLen(home, root.transform.TransformPoint(new Vector3(-3.0f, 0.05f, -3.2f)), 2f) > 0f) reach++;
            float le = PathLen(home, gen.ExitPosition, 3f);
            r.Check(reach == BallpitSetPiece.Roots.Count && le > 0f, T + "se llega andando a las " + reach + " salas y a la salida (" + le.ToString("F0") + " m)");
        }
        BallPitField f0 = BallPitField.All[0];
        BallpitSetPiece.PitJ main = f0.pits[0];
        Rect inner = BallpitSetPiece.PitInner(main, f0.rim);
        Vector3 pitC = f0.transform.TransformPoint(new Vector3(inner.center.x, main.floor + 0.05f, inner.center.y));
        float floorY = f0.transform.TransformPoint(new Vector3(0f, main.floor, 0f)).y, surfY = f0.transform.TransformPoint(new Vector3(0f, main.ball, 0f)).y;

        // ---------------- vadeo ----------------
        gm.player.Teleport(pitC + Vector3.up * 0.3f, 0f);
        yield return 1.0f;
        FirstPersonController fpc = gm.player;
        float py = fpc.transform.position.y;
        r.Check(Mathf.Abs(py - floorY) < 0.35f, T + "dentro de la piscina se pisa el fondo (y " + py.ToString("F2") + " / fondo " + floorY.ToString("F2") + ")");
        r.Check(fpc.BallDepth > 0.4f && fpc.BallWadeFactor < 0.8f, T + "hundido entre las bolas: profundidad " + fpc.BallDepth.ToString("F2") + " m, velocidad x" + fpc.BallWadeFactor.ToString("F2"));
        Vector4[] pu = Shader.GetGlobalVectorArray("_BallPushers");
        bool pushOk = pu != null && pu.Length >= 1 && pu[0].w > 0.3f && new Vector2(pu[0].x - fpc.transform.position.x, pu[0].z - fpc.transform.position.z).magnitude < 0.3f;
        r.Check(pushOk, T + "las bolas se apartan alrededor de las piernas (empujador del jugador activo)");
        int visAlto = f0.LastVisible;
        int qPrev = GameSettings.Quality;
        GameSettings.Quality = 0; yield return 0.4f;
        int visBajo = f0.LastVisible;
        GameSettings.Quality = qPrev; yield return 0.2f;
        r.Check(visBajo > 1000 && visBajo < visAlto * 0.9f, T + "Bajo dibuja menos bolas (v7.1.1: 2 capas, sin la 3.ª ni bolas rodando): " + visBajo + " vs " + visAlto + " (Alto)");

        // ---------------- llaves y objetos en las salas ----------------
        r.Check(gen.HallPitKeys.Count == 1, T + "una llave enterrada en una piscina de bolas");
        ItemPickup pitKey = null; float pitKeyL = -1f; Vector3 pitKeyPos = Vector3.zero; bool hasPitKey = false;
        if (gen.HallPitKeys.Count == 1)
            foreach (ItemPickup ip in Object.FindObjectsOfType<ItemPickup>())
                if (ItemDefs.IsKey(ip.type) && new Vector2(ip.transform.position.x - gen.HallPitKeys[0].x, ip.transform.position.z - gen.HallPitKeys[0].z).magnitude < 0.3f) pitKey = ip;
        if (pitKey != null)
        {
            float sy = BallPitField.SurfaceY(pitKey.transform.position), ky = pitKey.transform.position.y;
            r.Check(BallPitField.DepthAt(pitKey.transform.position + Vector3.down * 0.05f) > 0f && Mathf.Abs(ky - sy) < 0.12f, T + "la llave asoma entre las bolas (y " + ky.ToString("F2") + ", superficie " + sy.ToString("F2") + ")");
            ItemType kt = pitKey.type; int had = Inventory.Instance != null ? Inventory.Instance.GetCount(kt) : 0;
            Vector3 kp = pitKey.transform.position;
            pitKeyL = PathLen(home, new Vector3(kp.x, kp.y - 0.7f, kp.z), 1.5f); pitKeyPos = kp; hasPitKey = true;   // si el monstruo no cabe, se mira con la malla del jugador (abajo)
            gm.player.Teleport(new Vector3(kp.x + 0.5f, floorY + 0.3f, kp.z), 0f);
            yield return 0.5f;
            pitKey.enabled = true; off.Remove(pitKey);
            yield return 0.8f;
            bool got = Inventory.Instance != null && Inventory.Instance.GetCount(kt) > had;
            r.Check(got, T + "vadeando hasta ella se recoge (" + ItemDefs.Get(kt).name + ")");
            if (got && Inventory.Instance != null) { Inventory.Instance.RemoveOne(kt); Inventory.Instance.wrongKeys.Clear(); }
        }
        else r.Check(false, T + "la llave de la piscina existe");
        BuildPlayerNav(gen.Root);
        if (hasPitKey)   // (la llave ya se recogió: el objeto está destruido)
        {
            string how = "monstruo";
            if (pitKeyL <= 0f)
            {
                Vector3 hIn = home;
                foreach (GameObject root in BallpitSetPiece.Roots) if (root != null && Vector3.Distance(root.transform.position, pitKeyPos) < 14f) hIn = root.transform.TransformPoint(new Vector3(-3.0f, 0.05f, -3.2f));
                pitKeyL = PlayerPathLen(hIn, pitKeyPos + Vector3.down * 0.7f, 1.5f); how = "jugador";
            }
            r.Check(pitKeyL > 0f, T + "se llega andando a la llave de la piscina (" + pitKeyL.ToString("F0") + " m, malla del " + how + ")");
        }
        int propItems = 0, propReach = 0;
        foreach (ItemPickup ip in Object.FindObjectsOfType<ItemPickup>())
        {
            bool inHall = false; Vector3 hallIn = home;
            foreach (GameObject root in BallpitSetPiece.Roots) if (root != null && Vector3.Distance(root.transform.position, ip.transform.position) < 14f) { inHall = true; hallIn = root.transform.TransformPoint(new Vector3(-3.0f, 0.05f, -3.2f)); }
            if (!inHall || (pitKey != null && ip == pitKey)) continue;
            propItems++;
            Vector3 ipp = ip.transform.position;
            bool ok = PathLen(home, ipp, 1.6f) > 0f || PlayerPathLen(hallIn, ipp) > 0f;   // hasta la sala ya se comprueba arriba
            if (ok) propReach++;
            else r.Info(T + "sin camino hasta " + ip.type + " en " + ip.transform.position + " (local " + ip.transform.position + ")");
        }
        {   // v7.1.0: cada piscina se puede dejar andando por su abertura con escalones (sin saltar)
            Vector3 hIn0 = home; foreach (GameObject root in BallpitSetPiece.Roots) if (root != null && Vector3.Distance(root.transform.position, f0.transform.position) < 14f) hIn0 = root.transform.TransformPoint(new Vector3(-3.0f, 0.05f, -3.2f));
            int pitsOk = 0; string pl = "";
            foreach (BallpitSetPiece.PitJ pj in f0.pits)
            {
                Rect ir = BallpitSetPiece.PitInner(pj, f0.rim);
                float pL = PlayerPathLen(hIn0, f0.transform.TransformPoint(new Vector3(ir.center.x, pj.floor + 0.05f, ir.center.y)), 0.5f);
                if (pL > 0f) pitsOk++; pl += " " + pL.ToString("F0");
            }
            r.Check(pitsOk == f0.pits.Length, T + "se entra y se sale andando de cada piscina por los escalones (" + pitsOk + "/" + f0.pits.Length + ", m:" + pl + ", paredes recortadas " + BallpitSetPiece.OpenedPitMouths + ")");
        }
        ClearPlayerNav();
        r.Check(propItems >= 2 && propReach == propItems, T + "objetos en el atrezo de las salas, todos alcanzables: " + propReach + "/" + propItems);

        {   // a pie, con la física real: del centro de la piscina principal hacia la abertura y fuera
            float mid = (main.oa + main.ob) * 0.5f;
            Vector3 st = f0.transform.TransformPoint(new Vector3(mid, main.floor + 0.3f, inner.yMin + 1.8f));
            Vector3 fw = f0.transform.TransformDirection(main.open == "s" ? Vector3.back : main.open == "n" ? Vector3.forward : main.open == "e" ? Vector3.right : Vector3.left);
            if (main.open == "e" || main.open == "w") st = f0.transform.TransformPoint(new Vector3(main.open == "e" ? inner.xMax - 1.8f : inner.xMin + 1.8f, main.floor + 0.3f, mid));
            gm.player.Teleport(st, Mathf.Atan2(fw.x, fw.z) * Mathf.Rad2Deg);
            yield return 0.4f;
            gm.player.debugInput = true; gm.player.debugMove = new Vector2(0f, 1f);
            float tw = 0f; Vector3 lpw = Vector3.zero;
            while (tw < 7f) { yield return 0.2f; tw += 0.2f; lpw = f0.transform.InverseTransformPoint(gm.player.transform.position); if (lpw.y > -0.05f && Vector3.Dot(gm.player.transform.position - st, fw) > 2.4f) break; }
            gm.player.debugMove = Vector2.zero; gm.player.debugInput = false;
            bool outOk = lpw.y > -0.05f && Vector3.Dot(gm.player.transform.position - st, fw) > 2.4f;
            r.Check(outOk, T + "se sale de la piscina andando por los escalones (" + tw.ToString("F1") + " s, local " + lpw.ToString("F2") + ")");
            gm.player.Teleport(home, homeYaw); yield return 0.3f;
        }

        // ---------------- el monstruo de las piscinas ----------------
        BallPitLurker lk = BallPitLurker.Active;
        MonsterAI ai = lk != null ? lk.GetComponent<MonsterAI>() : null;
        r.Check(lk != null && ai != null && ai.mimic == null, T + "el monstruo principal se esconde en las piscinas (BallPitLurker)");
        if (ai != null && ai.visual != null && ai.visual.profile != null) r.Check(ai.visual.profile.id == "PitLurker", T + "modelo propio: " + ai.visual.profile.id);
        if (lk != null && ai != null)
        {
            gm.player.Teleport(home, homeYaw); yield return 0.3f;
            BallPitLurker.DisabledForTests = false;
            float kd0 = ai.killDistance; ai.killDistance = 0f;   // que no atrape al jugador de la prueba
            int em0 = lk.Emerges, b0 = BallPitMonsterFx.Bursts;
            lk.Submerge(f0, 0);
            yield return 0.6f;
            BallPitMonsterFx fx = ai.GetComponent<BallPitMonsterFx>();
            NavMeshAgent ag = ai.GetComponent<NavMeshAgent>();
            r.Check(lk.IsHidden && ai.transform.position.y < floorY - 1.5f && (ag == null || !ag.enabled), T + "sumergido bajo las bolas (y " + ai.transform.position.y.ToString("F2") + ", fondo " + floorY.ToString("F2") + ")");
            bool lightsOff = true; foreach (Light l in ai.GetComponentsInChildren<Light>()) if (l.enabled) lightsOff = false;
            r.Check(fx != null && fx.Under && lightsOff, T + "bajo las bolas: ojos apagados, roce al arrastrarse");
            Vector4[] pu2 = Shader.GetGlobalVectorArray("_BallPushers"); bool bulge = false;
            foreach (Vector4 p in pu2) if (p.w < 0f && new Vector2(p.x - ai.transform.position.x, p.z - ai.transform.position.z).magnitude < 0.4f) bulge = true;
            r.Check(bulge, T + "bulto en la superficie de bolas sobre él");
            // un jugador entra en la piscina a su lado: emerge con estallido de bolas y persigue
            Vector3 mp = ai.transform.position;
            // dentro de la misma piscina (la sala puede estar girada: fuera de ella, a la altura del fondo, se caería bajo el suelo)
            Vector3 ml = f0.transform.InverseTransformPoint(mp);
            Vector2 toC = inner.center - new Vector2(ml.x, ml.z);
            Vector2 dir = toC.sqrMagnitude > 0.01f ? toC.normalized : Vector2.right;
            Vector2 wl = new Vector2(ml.x, ml.z) + dir * 1.9f;
            wl.x = Mathf.Clamp(wl.x, inner.xMin + 0.45f, inner.xMax - 0.45f); wl.y = Mathf.Clamp(wl.y, inner.yMin + 0.45f, inner.yMax - 0.45f);
            if ((wl - new Vector2(ml.x, ml.z)).magnitude < 0.9f) { wl = new Vector2(ml.x, ml.z) - dir * 1.2f; wl.x = Mathf.Clamp(wl.x, inner.xMin + 0.3f, inner.xMax - 0.3f); wl.y = Mathf.Clamp(wl.y, inner.yMin + 0.3f, inner.yMax - 0.3f); }   // piscina estrecha: al otro lado
            Vector3 wp = f0.transform.TransformPoint(new Vector3(wl.x, main.floor + 0.3f, wl.y));
            gm.player.Teleport(wp, Quaternion.LookRotation(new Vector3(mp.x - wp.x, 0f, mp.z - wp.z)).eulerAngles.y);
            float t = 0f; while (t < 4f && lk.Emerges == em0) { yield return 0.1f; t += 0.1f; }
            r.Check(lk.Emerges > em0, T + "emerge cuando alguien vadea cerca (" + t.ToString("F1") + " s)");
            t = 0f; while (t < 2f && lk.Current != BallPitLurker.Phase.Roaming) { yield return 0.1f; t += 0.1f; }
            r.Check(BallPitMonsterFx.Bursts > b0, T + "estallido de bolas y ruido al salir (" + (BallPitMonsterFx.Bursts - b0) + ", bolas al aire " + BallPitField.FlyingCount + ")");
            ag = ai.GetComponent<NavMeshAgent>();
            bool pitNav = NavMesh.SamplePosition(pitC, out NavMeshHit pnh, 0.4f, NavMesh.AllAreas);
            r.Info(T + "tras emerger: agente " + (ag != null ? ag.enabled + "/" + ag.isOnNavMesh : "-") + " y " + ai.transform.position.y.ToString("F2") + " (fondo " + floorY.ToString("F2") + "), malla en el fondo de la piscina: " + pitNav);
            r.Check(lk.Current == BallPitLurker.Phase.Roaming && ag != null && ag.enabled && ag.isOnNavMesh && ai.transform.position.y < surfY + 0.2f, T + "de vuelta en la malla de navegación, de pie en la piscina");
            r.Check(pitNav, T + "malla de navegación en el fondo de las piscinas (el monstruo te sigue dentro)");
            r.Check(ai.CurrentState == MonsterAI.State.Chase, T + "persigue al salir (" + ai.CurrentState + ")");
            ai.Stop(); BallPitLurker.DisabledForTests = true; ai.killDistance = kd0;
            gm.player.Teleport(home, homeYaw);
            yield return 0.4f;
        }

        // ---------------- audio ----------------
        PlayparkAudio pa = PlayparkAudio.Active;
        r.Check(pa != null && pa.BoxClip != null && pa.BoxClip.length > 8f && pa.LaughClips != null && pa.LaughClips.Length >= 3, T + "caja de música (" + (pa != null && pa.BoxClip != null ? pa.BoxClip.length.ToString("F0") : "-") + " s) y risas lejanas");
        if (pa != null)
        {
            int b0 = pa.BoxPlays, l0 = pa.Laughs;
            pa.ForceForTests(); yield return 0.3f;
            r.Check(pa.BoxPlays > b0 && pa.Laughs > l0 && pa.BoxSource.GetComponent<AudioReverbFilter>() != null, T + "suenan con eco (reverberación de nave) y filtrado por paredes");
        }
        r.Check(BallPitField.RustleClip != null && BallPitField.RustleClip.length > 0.5f, T + "roce de bolas de plástico al vadear");

        foreach (ItemPickup ip in off) if (ip != null) ip.enabled = true;
        gm.player.Teleport(home, homeYaw);
        yield return 0.3f;
    }
}
