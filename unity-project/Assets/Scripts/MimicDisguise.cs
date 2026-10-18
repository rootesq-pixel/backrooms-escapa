using UnityEngine;

// v7.0.1: monstruo imitador. A veces (según la semilla, igual para todos en MP) un monstruo aparece disfrazado de jugador:
// mismo modelo y traje, linterna encendida y, en multijugador, el nombre de un compañero (elegido por la semilla).
// Pistas: se mueve a tirones, no hace ruido al pisar, se queda mirando, su linterna no ilumina y la etiqueta falla.
// Si te acercas o lo miras fijamente demasiado tiempo, se transforma y ataca (persecución + susto normales).
// Autoridad: un jugador solo / el anfitrión decide; los clientes reciben el estado "disfrazado" en el byte de estado del monstruo.
// v7.0.2: ya no es raro: hay EXACTAMENTE UN imitador por nivel (niveles 1+), un monstruo más además del habitual.
// Un «director» (anfitrión / un jugador) garantiza el encuentro: tras 60-120 s en el campo (40-90 s en el resto) lo coloca
// a la vista del jugador a media distancia (15-35 m en el campo, 8-22 m en pasillos), quieto o andando despacio, linterna encendida.
// Si no llega a verlo (no está en pantalla ~0,8 s) en 40 s, vuelve a intentarlo más tarde. Las pistas y el ataque no cambian.
public class MimicDisguise : MonoBehaviour
{
    public const float Chance = 1.0f;            // v7.0.2: siempre (niveles 1+)
    public const float RevealDistance = 3.2f, StareSeconds = 2.2f, StareDot = 0.965f, StareRange = 22f, MaxDisguise = 50f, HoldDistance = 7f;
    public const int NetBit = 0x40;

    public MonsterAI ai;
    public RemotePlayer puppet;
    public bool Disguised { get; private set; }
    public float Stare { get; private set; }
    public float DisguiseTime { get; private set; }
    public int CopiedId { get; private set; }
    public string CopiedName { get; private set; }
    public int Reveals { get; private set; }
    public static MimicDisguise Active;

    // ¿este nivel tiene imitador? (determinista: misma respuesta en anfitrión y clientes)
    public static bool LevelHasMimic(int seed, int level)
    {
        if (ForceForTests >= 0) return ForceForTests == 1;
        return level >= 1;
    }
    public static int ForceForTests = -1;        // pruebas: 1 siempre, 0 nunca, -1 según la semilla

    public static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16; return x; }

    // a quién copia: en MP un jugador elegido por la semilla entre los ids ordenados; solo, el propio jugador (sin etiqueta)
    public static void PickCopy(int seed, int level, out int id, out string name)
    {
        id = -1; name = "";
        NetSession ns = NetSession.Instance;
        if (!NetSession.Active || ns == null || ns.Players.Count == 0) return;
        System.Collections.Generic.List<NetPlayer> ps = new System.Collections.Generic.List<NetPlayer>(ns.Players);
        ps.Sort((a, b) => a.id.CompareTo(b.id));
        NetPlayer p = ps[(int)(Hash((uint)seed ^ 0x9e3779b9u ^ (uint)level) % (uint)ps.Count)];
        id = p.id; name = p.name;
    }

    public static MimicDisguise Attach(MonsterAI ai, int seed, int level, StepKind step)
    {
        MimicDisguise d = ai.gameObject.AddComponent<MimicDisguise>();
        d.ai = ai;
        PickCopy(seed, level, out int id, out string nm);
        d.CopiedId = id; d.CopiedName = nm;
        d.puppet = RemotePlayer.CreateMimic(ai.transform.parent, id < 0 ? 7 : id, nm, step);
        d.puppet.ShowTag = id >= 0;
        d.jitterSeed = (int)(Hash((uint)seed + 31u) & 0xffff);
        d.SetDisguised(true);
        ai.mimic = d;
        Active = d;
        d.rng = new System.Random(seed * 131 + level * 7 + 5);
        d.field = LevelConfig.Get(level).layout == LayoutKind.Field;
        d.stageT = FirstStageOverride >= 0f ? FirstStageOverride : (d.field ? Rand(d.rng, 60f, 120f) : Rand(d.rng, 40f, 90f));
        d.FirstStageDelay = d.stageT;
        return d;
    }

    // ---------------- director de encuentros (anfitrión / un jugador) ----------------
    public enum Phase { Lurk, Staged, Encountered }
    public Phase CurrentPhase { get; private set; }
    public int StageCount { get; private set; }
    public float LastStageDistance { get; private set; }
    public bool StagedWalking { get; private set; }
    public float SeenTime { get; private set; }
    public float StageTimer { get { return stageT; } }
    public float FirstStageDelay { get; private set; }
    public static float FirstStageOverride = -1f;   // pruebas: primer encuentro antes
    public const float SeenToEncounter = 0.8f, StagedTimeout = 40f;
    private System.Random rng; private bool field; private float stageT, stagedT, encounterT;
    private static float Rand(System.Random r, float a, float b) { return a + (float)r.NextDouble() * (b - a); }

    public void ForceStageSoon(float t) { stageT = Mathf.Min(stageT, t); }

    // ojo y dirección de quien mira (cámara si es el jugador local)
    public static void ViewOf(Transform viewer, out Vector3 eye, out Vector3 fwd, out Camera cam)
    {
        FirstPersonController f = viewer.GetComponent<FirstPersonController>();
        cam = f != null ? f.GetComponentInChildren<Camera>() : null;
        if (cam != null) { eye = cam.transform.position; fwd = cam.transform.forward; }
        else { eye = viewer.position + Vector3.up * 1.6f; fwd = viewer.forward; }
    }

    // ¿la copia se ve en pantalla (dentro del encuadre y sin nada delante)?
    public bool OnScreen(Transform viewer)
    {
        if (viewer == null || puppet == null || !puppet.gameObject.activeInHierarchy) return false;
        ViewOf(viewer, out Vector3 eye, out Vector3 fwd, out Camera cam);
        Vector3 chest = FieldWrap.Image(transform.position, eye) + Vector3.up * 1.3f;   // v7.0.4: imagen más cercana
        Vector3 to = chest - eye; float d = to.magnitude;
        if (d > 60f || d < 0.1f) return false;
        if (cam != null)
        {
            Vector3 vp = cam.WorldToViewportPoint(chest);
            if (vp.z <= 0f || vp.x < 0.03f || vp.x > 0.97f || vp.y < 0.03f || vp.y > 0.97f) return false;
        }
        else if (Vector3.Dot(fwd, to / d) < 0.75f) return false;
        return !Physics.Linecast(eye, eye + to * 0.95f, ~0, QueryTriggerInteraction.Ignore);
    }

    // busca un sitio a la vista del jugador, a media distancia, sobre el NavMesh y con línea de visión
    public int StageTries, FailFree, FailNav, FailDy, FailLos, FailVp;   // diagnóstico (pruebas)
    public string StageDiag { get { return "intentos " + StageTries + " hueco " + FailFree + " nav " + FailNav + " altura " + FailDy + " visión " + FailLos + " encuadre " + FailVp + " stageT " + stageT.ToString("F1"); } }
    public bool TryStage(Transform viewer)
    {
        if (viewer == null || ai == null) return false;
        StageTries++;
        ViewOf(viewer, out Vector3 eye, out Vector3 fwd, out Camera cam);
        Vector3 flat = fwd; flat.y = 0f; if (flat.sqrMagnitude < 0.01f) flat = viewer.forward; flat.Normalize();
        float dMin = field ? 15f : 7f, dMax = field ? 35f : 22f, spread = field ? 22f : 40f;
        // abanico de direcciones dentro del encuadre (orden aleatorio): se mide el hueco libre con un rayo
        // y se elige una distancia que quepa (en pasillos estrechos los intentos al azar casi nunca acertaban)
        const int Fan = 17;
        int start = rng.Next(Fan);
        for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < Fan; i++)
        {
            int k = (start + i * 7) % Fan;
            float yaw = -spread + 2f * spread * k / (Fan - 1) + Rand(rng, -2f, 2f);
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * flat;
            Vector3 o = viewer.position + Vector3.up * 1.2f;
            float free = Physics.Raycast(o, dir, out RaycastHit wh, dMax + 2f, ~0, QueryTriggerInteraction.Ignore) ? wh.distance - 0.8f : dMax;
            if (free < dMin - 1f) { FailFree++; continue; }
            float hi = Mathf.Min(dMax, free), lo = Mathf.Min(dMin, hi);
            for (int tr = 0; tr < 3; tr++)
            {
                float d = pass == 0 ? Rand(rng, lo, hi) : Mathf.Lerp(lo, hi, 0.25f * tr);
                Vector3 p = viewer.position + dir * d;
                Vector3 q;
                if (UnityEngine.AI.NavMesh.SamplePosition(p, out UnityEngine.AI.NavMeshHit h, 2.5f, UnityEngine.AI.NavMesh.AllAreas)) q = h.position;
                else if (!ai.UsesNav && Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out RaycastHit gh, 8f, ~0, QueryTriggerInteraction.Ignore)) q = gh.point;
                else { FailNav++; continue; }
                if (Mathf.Abs(q.y - viewer.position.y) > 3.5f) { FailDy++; continue; }
                Vector3 fq = q - viewer.position; fq.y = 0f;
                if (fq.magnitude < dMin - 1.5f || fq.magnitude > dMax + 2f) continue;
                Vector3 chest = q + Vector3.up * 1.3f;
                if (Physics.Linecast(eye, chest, ~0, QueryTriggerInteraction.Ignore)) { FailLos++; continue; }
                if (cam != null) { Vector3 vp = cam.WorldToViewportPoint(chest); if (vp.z <= 0f || vp.x < 0.08f || vp.x > 0.92f) { FailVp++; continue; } }
                float face = Quaternion.LookRotation(-fq.normalized).eulerAngles.y;
                ai.WarpTo(q, face);
                LastStageDistance = fq.magnitude; StageCount++;
                StagedWalking = rng.NextDouble() < 0.5;
                if (puppet != null) puppet.Snap(q, face);
                return true;
            }
        }
        return false;
    }

    private int jitterSeed;
    private float nextPush, glitchT;

    public void SetDisguised(bool on)
    {
        if (Disguised == on) return;
        bool wasDisguised = Disguised;
        Disguised = on;
        if (ai != null && ai.visual != null)
            foreach (Renderer r in ai.visual.GetComponentsInChildren<Renderer>(true))
                if (puppet == null || !r.transform.IsChildOf(puppet.transform)) r.enabled = !on;
        if (puppet != null) { puppet.gameObject.SetActive(on); if (on) puppet.RestartAnimator(); }
        if (!on && wasDisguised)
        {
            Reveals++;
            if (ai != null && ai.visual != null) ai.visual.OnChaseStart();   // chillido de transformación
        }
        if (on) { Stare = 0f; DisguiseTime = 0f; }
    }

    // anfitrión / solo: ¿lo están mirando fijamente?
    public bool Watched(Transform viewer)
    {
        if (viewer == null) return false;
        Vector3 eye, fwd;
        FirstPersonController f = viewer.GetComponent<FirstPersonController>();
        Camera cam = f != null ? f.GetComponentInChildren<Camera>() : null;
        if (cam != null) { eye = cam.transform.position; fwd = cam.transform.forward; }
        else { eye = viewer.position + Vector3.up * 1.6f; fwd = viewer.forward; }
        Vector3 to = FieldWrap.Image(transform.position, eye) + Vector3.up * 1.5f - eye;
        float d = to.magnitude;
        if (d > StareRange || d < 0.01f) return false;
        if (Vector3.Dot(fwd, to / d) <= StareDot) return false;
        return d < 6f || !Physics.Linecast(eye, eye + to * 0.9f, ~0, QueryTriggerInteraction.Ignore);
    }

    // anfitrión / solo: devuelve true si se ha descubierto este fotograma
    public bool Tick(Transform target, float dt)
    {
        if (!Disguised) return false;
        DisguiseTime += dt;
        Vector3 to = FieldWrap.Delta(transform.position, target.position); to.y = 0f;
        bool watched = Watched(target);
        Stare = watched ? Stare + dt : Mathf.Max(0f, Stare - dt * 0.5f);
        switch (CurrentPhase)
        {
            case Phase.Lurk:
                stageT -= dt;
                if (stageT <= 0f)
                {
                    if (TryStage(target)) { CurrentPhase = Phase.Staged; stagedT = 0f; SeenTime = 0f; }
                    else stageT = 1.0f;   // no hay sitio ahora: se reintenta enseguida
                }
                break;
            case Phase.Staged:
                stagedT += dt;
                if (OnScreen(target)) SeenTime += dt;
                if (SeenTime >= SeenToEncounter || to.magnitude < 12f) { CurrentPhase = Phase.Encountered; encounterT = 0f; }
                else if (stagedT > StagedTimeout) { CurrentPhase = Phase.Lurk; stageT = field ? Rand(rng, 45f, 75f) : Rand(rng, 35f, 60f); }
                break;
            case Phase.Encountered:
                encounterT += dt;
                break;
        }
        bool timeout = CurrentPhase == Phase.Encountered && encounterT > MaxDisguise;
        if (to.magnitude < RevealDistance || Stare >= StareSeconds || timeout) { SetDisguised(false); return true; }
        return false;
    }

    // ---------------- silbido de respuesta (pista inquietante) ----------------
    private float whistleBackT = -1f;
    public int WhistleBacks { get; private set; }
    public void OnPlayerWhistle(Vector3 pos)
    {
        if (!Disguised || rng == null || whistleBackT >= 0f) return;
        if (rng.NextDouble() < WhistleBackChance) whistleBackT = WhistleBackDelayOverride >= 0f ? WhistleBackDelayOverride : Rand(rng, 6f, 12f);
    }
    public static float WhistleBackChance = 0.4f, WhistleBackDelayOverride = -1f;   // override: pruebas
    public string WhistleDiag { get { return "t " + whistleBackT.ToString("F2") + " proxy " + (ai != null && ai.netProxy) + " activo " + isActiveAndEnabled + " estado " + (GameManager.Instance != null ? GameManager.Instance.State.ToString() : "-") + " escala " + Time.timeScale + " lu " + lateUpdates; } }
    private int lateUpdates;
    public void CancelWhistleBack() { whistleBackT = -1f; }
    public bool WhistleBackPending { get { return whistleBackT >= 0f; } }
    public void WhistleBackNow() { whistleBackT = -1f; WhistleBacks++; Whistle.Emit(transform.position + Vector3.up * 1.5f, -1, rng != null ? rng.Next(3) : 0, true); }
    private void UpdateWhistleBack(float dt)
    {
        if (whistleBackT < 0f) return;
        whistleBackT -= dt;
        if (whistleBackT < 0f) WhistleBackNow();
    }

    private void LateUpdate()
    {
        lateUpdates++;
        // respuesta al silbido: en el anfitrión / solo, aunque la IA esté parada o fuera del NavMesh
        if (Disguised && ai != null && !ai.netProxy && GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Playing) UpdateWhistleBack(Time.deltaTime);
        if (!Disguised || puppet == null) return;
        // movimiento a tirones: la copia solo se actualiza a saltos irregulares
        float t = Time.time;
        if (t >= nextPush)
        {
            float speed = ai != null ? ai.VisSpeed : 0f;
            puppet.Push(transform.position, transform.eulerAngles.y + Mathf.Sin(t * 7.3f + jitterSeed) * 6f, -4f, Mathf.Min(speed, 2.2f), RemotePlayer.FlagFlash);
            nextPush = t + 0.09f + Mathf.Abs(Mathf.Sin(t * 3.1f + jitterSeed)) * 0.22f;
        }
        glitchT += Time.deltaTime;
    }

    private void OnDestroy() { if (Active == this) Active = null; if (puppet != null) Destroy(puppet.gameObject); }
}
