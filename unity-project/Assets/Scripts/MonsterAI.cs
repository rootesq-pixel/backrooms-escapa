using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// IA del monstruo: patrulla, persigue, busca y mata. Basada en NavMeshAgent.
//
// ---- NAVMESH (importante) ----
// 1) Si el paquete "AI Navigation" (com.unity.ai.navigation) está en Packages/manifest.json,
//    GameBootstrap hornea la NavMesh en tiempo de ejecución (TryBuildRuntimeNavMesh, por reflexión,
//    así este script compila aunque el paquete no esté instalado).
// 2) Si tienes una escena propia: añade un componente NavMeshSurface al nivel y pulsa "Bake"
//    (o usa Window > AI > Navigation en versiones antiguas).
// 3) Si no hay NavMesh disponible, el monstruo NO falla: usa un modo alternativo que sigue el camino
//    por la cuadrícula del BackroomsGenerator (BFS) moviendo el transform directamente.
//
// El componente NavMeshAgent se añade solo si hay NavMesh bajo el monstruo (evita errores en consola).
public class MonsterAI : MonoBehaviour
{
    public enum State { Patrol, Chase, Search, Kill }

    [Header("Referencias")]
    public Transform target;
    public BackroomsGenerator generator;

    [Header("Velocidades")]
    public float patrolSpeed = 1.8f;
    public float chaseSpeed = 4.2f;

    [Header("Vista")]
    public float sightRange = 18f;
    [Range(30f, 360f)] public float fieldOfView = 120f;
    public float eyeHeight = 1.7f;
    [Tooltip("Multiplicador del alcance de visión cuando el jugador tiene la linterna apagada.")]
    [Range(0.1f, 1f)] public float darkSightFactor = 0.6f;
    public LayerMask sightMask = ~0;

    [Header("Oído")]
    [Tooltip("Multiplicador global del radio de ruido que oye (niveles difíciles > 1).")]
    public float hearingScale = 1f;
    [Tooltip("Multiplicador del radio de ruido si hay una pared entre ambos.")]
    [Range(0f, 1f)] public float hearingThroughWallsFactor = 0.5f;

    [Header("Comportamiento")]
    public float killDistance = 1.4f;
    public float loseSightTime = 4f;
    public float searchDuration = 5f;
    public float patrolWaitTime = 2f;
    public float repathInterval = 0.25f;
    public float arriveDistance = 0.6f;

    [Header("Aspecto")]
    public MonsterVisual visual;

    // Lista de monstruos vivos (la usa el efecto VHS para el "glitch" por cercanía)
    public static readonly List<MonsterAI> Instances = new List<MonsterAI>();

    [Header("Multijugador")]
    [Tooltip("Cliente: este monstruo solo se muestra; su posición y estado llegan del anfitrión.")]
    public bool netProxy;
    public MimicDisguise mimic;   // v7.0.1
    public bool Disguised { get { return mimic != null && mimic.Disguised; } }
    public int NetStateByte { get { return (int)CurrentState | (Disguised ? MimicDisguise.NetBit : 0); } }
    public float VisSpeed { get { return visSpeed; } }

    public State CurrentState { get; private set; }
    public bool UsesNavMesh { get { return useNav; } }

    // Agua: profundidad bajo el monstruo y factor de velocidad (más lento al chapotear)
    public float WaterDepth { get; private set; }
    public float WaterSlow { get; private set; }
    private float waterTimer;

    private NavMeshAgent agent;
    private bool useNav;
    private bool stopped;
    private float currentSpeed;

    private RemotePlayer remoteTarget;
    private float retargetTimer;
    private float netKillTimer;
    private FirstPersonController playerController;
    private Flashlight playerFlashlight;

    private Vector3 lastKnownPosition;
    // v7.0.4 Campo infinito: el objetivo en su imagen más cercana al monstruo (persigue a través del borde)
    private Vector3 TargetPos { get { return target == null ? transform.position : FieldWrap.Image(target.position, transform.position); } }
    private Vector2Int edgeCrossing; private Vector3 edgeDest;
    public int EdgeCrossings { get; private set; }
    private float lostSightTimer;
    private float searchTimer;
    private float waitTimer;
    private float repathTimer;
    private float stuckTimer;
    private Vector3 lastPos;
    private float visSpeed;
    private float killTimer;

    // Destino y camino alternativo (sin NavMesh)
    private Vector3 destination;
    private bool hasDestination;
    private readonly List<Vector3> fallbackPath = new List<Vector3>();
    private int fallbackIndex;

    // ------------------------------------------------------------------
    // Utilidad estática: hornear NavMesh en runtime si el paquete está instalado
    // ------------------------------------------------------------------
    // Devuelve true si se pudo hornear. Usa reflexión para no depender del paquete en compilación.
    public static bool TryBuildRuntimeNavMesh(GameObject root)
    {
        try
        {
            System.Type surfaceType = System.Type.GetType("Unity.AI.Navigation.NavMeshSurface, Unity.AI.Navigation");
            if (surfaceType == null)
            {
                Debug.LogWarning("[MonsterAI] Paquete com.unity.ai.navigation no encontrado: se usará el modo alternativo por cuadrícula.");
                return false;
            }

            Component surface = root.AddComponent(surfaceType);

            // Recoger solo la geometría hija de este objeto (CollectObjects.Children)
            System.Reflection.PropertyInfo collect = surfaceType.GetProperty("collectObjects");
            if (collect != null && collect.PropertyType.IsEnum)
            {
                collect.SetValue(surface, System.Enum.Parse(collect.PropertyType, "Children"), null);
            }

            System.Reflection.MethodInfo build = surfaceType.GetMethod("BuildNavMesh", System.Type.EmptyTypes);
            if (build == null)
            {
                return false;
            }
            build.Invoke(surface, null);
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[MonsterAI] No se pudo hornear la NavMesh en runtime: " + e.Message);
            return false;
        }
    }

    // ------------------------------------------------------------------
    // Ciclo de vida
    // ------------------------------------------------------------------

    private void OnEnable() { if (!Instances.Contains(this)) Instances.Add(this); }
    private void OnDisable() { Instances.Remove(this); }

    // Para asignar referencias desde código (GameBootstrap)
    public void Setup(Transform playerTarget, BackroomsGenerator gen)
    {
        target = playerTarget;
        generator = gen;
    }

    private void Start()
    {
        SetTargetRefs();
        if (netProxy)
        {
            CurrentState = State.Patrol;
            lastPos = transform.position;
            if (visual == null) visual = GetComponent<MonsterVisual>();
            return;
        }

        InitNavigation();

        CurrentState = State.Patrol;
        SetSpeed(patrolSpeed);
        PickNewPatrolPoint();
        lastPos = transform.position;
        if (visual == null) visual = GetComponent<MonsterVisual>();
    }

    // Anima el modelo según el estado y la velocidad real
    private void UpdateVisual(float dt)
    {
        waterTimer -= dt;
        if (waterTimer <= 0f)
        {
            waterTimer = 0.15f;
            WaterVolume wv = WaterVolume.Active;
            WaterDepth = wv != null ? wv.DepthAt(transform.position) : 0f;
            WaterSlow = Mathf.Lerp(1f, 0.62f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((WaterDepth - 0.05f) / 0.9f)));
            if (useNav && agent != null && agent.enabled) agent.speed = currentSpeed * WaterSlow;
        }
        if (visual == null) return;
        float speed;
        if (useNav && agent != null && agent.enabled && agent.isOnNavMesh) speed = agent.velocity.magnitude;
        else speed = dt > 0f ? (transform.position - lastPos).magnitude / dt : 0f;
        lastPos = transform.position;
        visSpeed = Mathf.Lerp(visSpeed, speed, 1f - Mathf.Exp(-12f * Mathf.Max(dt, 0.0001f)));
        visual.SetLocomotion(CurrentState, visSpeed);
    }

    // Añade el NavMeshAgent solo si hay NavMesh cerca; si no, modo alternativo
    private void InitNavigation()
    {
        useNav = false;

        NavMeshHit hit;
        if (NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas))
        {
            agent = GetComponent<NavMeshAgent>();
            if (agent == null)
            {
                agent = gameObject.AddComponent<NavMeshAgent>();
            }
            agent.radius = 0.4f;
            agent.height = 2.2f;
            agent.acceleration = 14f;
            agent.angularSpeed = 240f;
            agent.stoppingDistance = 0.2f;
            agent.autoBraking = true;
            agent.Warp(hit.position);
            useNav = agent.isOnNavMesh;
        }

        if (!useNav)
        {
            Debug.LogWarning("[MonsterAI] Sin NavMesh: el monstruo usa el modo alternativo (camino por cuadrícula).");
        }
    }

    private void SetTargetRefs()
    {
        playerController = null; playerFlashlight = null; remoteTarget = null;
        if (target == null) return;
        playerController = target.GetComponent<FirstPersonController>();
        playerFlashlight = target.GetComponentInChildren<Flashlight>();
        remoteTarget = target.GetComponent<RemotePlayer>();
    }

    // Anfitrión (multijugador): persigue al jugador vivo más cercano
    private void NetRetarget(float dt)
    {
        retargetTimer -= dt;
        if (retargetTimer > 0f) return;
        retargetTimer = 0.3f;
        Transform t = NetSession.Instance.PickTarget(transform.position, target);
        if (t != null && t != target)
        {
            target = t;
            SetTargetRefs();
            if (CurrentState == State.Chase) { lastKnownPosition = TargetPos; repathTimer = 0f; }
        }
    }

    // Anfitrión: tras alcanzar a un jugador (sin que sea el último) el monstruo se recupera y vuelve a buscar
    private void ResumeAfterKill()
    {
        netKillTimer = 0f;
        if (useNav && agent != null)
        {
            agent.enabled = true;
            NavMeshHit hit;
            if (NavMesh.SamplePosition(transform.position, out hit, 3f, NavMesh.AllAreas)) agent.Warp(hit.position);
            if (agent.isOnNavMesh) agent.isStopped = false;
        }
        EnterPatrol();
    }

    public void Resume() { stopped = false; }
    public void ChaseForTests() { stopped = false; EnterChase(); }   // v7.0.4: pruebas de adrenalina
    public bool IsStopped { get { return stopped; } }

    // ---- cliente: estado recibido del anfitrión, interpolado ----
    private struct NetSample { public float t; public Vector3 pos; public float yaw, speed; public int state; }
    private readonly NetSample[] nbuf = new NetSample[10];
    private int nhead, ncount;
    public int NetSamples { get { return ncount; } }
    public const float NetDelay = 0.1f;

    public void NetPush(Vector3 pos, float yaw, int state, float speed)
    {
        NetSample s = new NetSample { t = Time.unscaledTime, pos = pos, yaw = yaw, speed = speed, state = state };
        if (ncount < nbuf.Length) { nbuf[(nhead + ncount) % nbuf.Length] = s; ncount++; }
        else { nbuf[nhead] = s; nhead = (nhead + 1) % nbuf.Length; }
    }

    private void ProxyUpdate(float dt)
    {
        if (ncount == 0) return;
        float rt = Time.unscaledTime - NetDelay;
        NetSample a = nbuf[nhead], res = a;
        for (int i = 1; i < ncount; i++)
        {
            NetSample b = nbuf[(nhead + i) % nbuf.Length];
            if (b.t >= rt)
            {
                float k = Mathf.Clamp01((rt - a.t) / Mathf.Max(0.0001f, b.t - a.t));
                res = b; res.pos = Vector3.Lerp(a.pos, b.pos, k); res.yaw = Mathf.LerpAngle(a.yaw, b.yaw, k); res.speed = Mathf.Lerp(a.speed, b.speed, k);
                break;
            }
            a = b; res = b;
        }
        NetSample last = nbuf[(nhead + ncount - 1) % nbuf.Length];
        if ((last.pos - res.pos).sqrMagnitude > 36f && mimic != null) res = last;   // v7.0.2: el imitador se recoloca de golpe (sin deslizarse)
        transform.position = res.pos;
        transform.rotation = Quaternion.Euler(0f, res.yaw, 0f);
        if (mimic != null) mimic.SetDisguised((last.state & MimicDisguise.NetBit) != 0);
        State st = (State)Mathf.Clamp(last.state & 3, 0, 3);
        if (st == State.Chase && CurrentState != State.Chase && visual != null) visual.OnChaseStart();
        if (st == State.Kill && CurrentState != State.Kill && visual != null) { visual.OnKill(); }
        CurrentState = st;
        visSpeed = Mathf.Lerp(visSpeed, res.speed, 1f - Mathf.Exp(-12f * Mathf.Max(dt, 0.0001f)));
        lastPos = transform.position;
        if (visual != null) visual.SetLocomotion(CurrentState, CurrentState == State.Kill ? 0f : visSpeed);
    }

    private void Update()
    {
        if (netProxy) { ProxyUpdate(Time.deltaTime); return; }
        if (NetSession.IsHost && !stopped && CurrentState != State.Kill) NetRetarget(Time.deltaTime);
        if (target == null)
        {
            return;
        }
        if (stopped)
        {
            if (visual != null && CurrentState != State.Kill) visual.SetLocomotion(State.Patrol, 0f);
            return;
        }

        float dt = Time.deltaTime;
        if (CurrentState == State.Kill)
        {
            UpdateKill(dt);
            return;
        }
        if (edgeCrossing != Vector2Int.zero && FieldWrap.On && FieldWrap.EdgeDistance(transform.position, edgeCrossing) < 1.0f) CrossFieldEdge();
        Vector3 toTarget = TargetPos - transform.position;
        float distance = toTarget.magnitude;
        float flatDistance = new Vector2(toTarget.x, toTarget.z).magnitude;
        if (whistleDelay >= 0f && !(mimic != null && mimic.Disguised))
        {
            whistleDelay -= dt;
            if (whistleDelay < 0f && CurrentState != State.Chase && CurrentState != State.Kill) EnterSearch(whistlePos);
        }
        if (mimic != null && mimic.Disguised)
        {
            if (!mimic.Tick(target, dt)) { UpdateDisguised(dt, flatDistance, toTarget); return; }
            EnterChase();   // se ha descubierto: ataca
        }

        bool lineOfSight = HasLineOfSight();
        bool sees = CanSee(toTarget, distance, lineOfSight);
        bool hears = CanHear(distance, lineOfSight);
        // v7.4.0: el jugador dentro de una casa del Campo infinito está a salvo: el monstruo no lo «ve» ni lo «oye» (pierde el rastro,
        // busca un rato junto a la puerta y vuelve a patrullar; no se queda empujando en el hueco ni puede atraparlo)
        bool refuge = FieldWorld.Refuge(TargetPos);
        if (refuge) { sees = false; hears = false; }

        // Matar: muy cerca y con línea de visión directa
        if (CurrentState != State.Kill && !refuge && flatDistance < killDistance && Mathf.Abs(toTarget.y) < 2f && lineOfSight && CatchReachable() && (!NetSession.IsHost || NetSession.Instance.CanCatch(target)))
        {
            EnterKill();
            return;
        }

        switch (CurrentState)
        {
            case State.Patrol:
                UpdatePatrol(dt, sees, hears);
                break;
            case State.Chase:
                UpdateChase(dt, sees);
                break;
            case State.Search:
                UpdateSearch(dt, sees, hears);
                break;
        }

        if (!useNav)
        {
            UpdateFallbackMovement(dt);
        }
        UpdateVisual(dt);
    }

    // v7.0.1 imitador disfrazado: se acerca despacio hasta unos metros y se queda mirando
    private void UpdateDisguised(float dt, float flatDistance, Vector3 toTarget)
    {
        MimicDisguise.Phase ph = mimic.CurrentPhase;
        Vector3 l = toTarget; l.y = 0f;
        // acecho: quieto donde está (el director lo pondrá a la vista); a la vista: quieto o andando muy despacio hacia el jugador;
        // descubierto (ya lo ha visto): se acerca despacio hasta unos metros y se queda mirando
        bool walk = ph == MimicDisguise.Phase.Encountered ? flatDistance > MimicDisguise.HoldDistance
                  : ph == MimicDisguise.Phase.Staged && mimic.StagedWalking && flatDistance > MimicDisguise.HoldDistance + 4f;
        if (walk)
        {
            SetSpeed(ph == MimicDisguise.Phase.Staged ? 0.7f : patrolSpeed * 0.8f);
            repathTimer -= dt;
            if (repathTimer <= 0f || !hasDestination) { repathTimer = repathInterval; SetDestination(TargetPos); }
            if (!useNav) UpdateFallbackMovement(dt);
        }
        else
        {
            StopMoving();
            if (useNav && agent != null && agent.enabled && agent.isOnNavMesh) agent.ResetPath();
            if (ph != MimicDisguise.Phase.Lurk && l.sqrMagnitude > 0.01f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(l), 1f - Mathf.Exp(-3f * dt));
        }
        UpdateVisual(dt);
    }

    // Susto: el monstruo se abalanza hasta quedar frente a la cámara
    private void UpdateKill(float dt)
    {
        killTimer += dt;
        if (NetSession.IsHost && netKillTimer >= 0f)
        {
            netKillTimer += dt;
            if (netKillTimer > 2.2f && !GameManagerLost()) { ResumeAfterKill(); return; }
        }
        Vector3 toPlayer = TargetPos - transform.position;
        toPlayer.y = 0f;
        float d = toPlayer.magnitude;
        if (d > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(toPlayer);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 1f - Mathf.Exp(-18f * dt));
            float stopAt = 0.75f;
            if (d > stopAt)
            {
                Vector3 np = transform.position + toPlayer / d * Mathf.Min(d - stopAt, 7f * dt);
                transform.position = np;
            }
        }
        UpdateVisual(dt);
    }

    private static bool GameManagerLost() { return GameManager.Instance != null && GameManager.Instance.State != GameManager.GameState.Playing; }

    // ------------------------------------------------------------------
    // Estados
    // ------------------------------------------------------------------

    private void UpdatePatrol(float dt, bool sees, bool hears)
    {
        if (sees)
        {
            EnterChase();
            return;
        }
        if (hears)
        {
            EnterSearch(TargetPos);
            return;
        }

        if (HasArrived())
        {
            waitTimer += dt;
            if (waitTimer >= patrolWaitTime)
            {
                PickNewPatrolPoint();
            }
        }
        else
        {
            CheckStuck(dt);
        }
    }

    private void UpdateChase(float dt, bool sees)
    {
        if (sees)
        {
            lostSightTimer = 0f;
            lastKnownPosition = TargetPos;
        }
        else
        {
            lostSightTimer += dt;
            if (lostSightTimer >= loseSightTime)
            {
                EnterSearch(lastKnownPosition);
                return;
            }
        }

        // Recalcular el destino cada cierto tiempo (más barato que cada frame)
        repathTimer -= dt;
        if (repathTimer <= 0f)
        {
            repathTimer = repathInterval;
            SetDestination(sees ? TargetPos : lastKnownPosition);
        }
    }

    private void UpdateSearch(float dt, bool sees, bool hears)
    {
        if (sees)
        {
            EnterChase();
            return;
        }

        if (hears)
        {
            // Actualiza el punto de búsqueda hacia el ruido
            repathTimer -= dt;
            if (repathTimer <= 0f)
            {
                repathTimer = repathInterval;
                lastKnownPosition = TargetPos;
                SetDestination(lastKnownPosition);
                searchTimer = 0f;
            }
            return;
        }

        if (HasArrived())
        {
            // Mira alrededor un rato y vuelve a patrullar
            searchTimer += dt;
            transform.Rotate(0f, 90f * dt, 0f);
            if (searchTimer >= searchDuration)
            {
                EnterPatrol();
            }
        }
        else
        {
            CheckStuck(dt);
        }
    }

    private void EnterPatrol()
    {
        CurrentState = State.Patrol;
        SetSpeed(patrolSpeed);
        PickNewPatrolPoint();
    }

    private void EnterChase()
    {
        bool wasChasing = CurrentState == State.Chase;
        CurrentState = State.Chase;
        if (!wasChasing && visual != null) visual.OnChaseStart();
        SetSpeed(chaseSpeed);
        lostSightTimer = 0f;
        lastKnownPosition = TargetPos;
        repathTimer = 0f;
    }

    private void EnterSearch(Vector3 position)
    {
        CurrentState = State.Search;
        SetSpeed(patrolSpeed * 1.5f);
        lastKnownPosition = position;
        searchTimer = 0f;
        repathTimer = repathInterval;
        SetDestination(position);
    }

    private void EnterKill()
    {
        CurrentState = State.Kill;
        StopMoving();
        if (useNav && agent != null && agent.enabled) agent.enabled = false;
        if (visual != null)
        {
            visual.OnKill();
            visual.SetLocomotion(State.Kill, 0f);
        }

        // Mirar al jugador
        Vector3 look = TargetPos - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.001f)
        {
            transform.rotation = Quaternion.LookRotation(look);
        }

        if (NetSession.IsHost)
        {
            // multijugador: derriba a ese jugador; si es el último, el anfitrión lanza el screamer para todos
            netKillTimer = 0f;
            NetSession.Instance.HostMonsterCaught(this, target);
            return;
        }
        netKillTimer = -1f;
        if (GameManager.Instance != null)
        {
            GameManager.Instance.PlayerCaught(this);
        }
    }

    // Fuerza la captura (pruebas): reproduce la secuencia de susto completa
    public void ForceCatch()
    {
        stopped = false;
        EnterKill();
    }

    // v7.0.4: llega al borde del campo camino de su destino: pasa al lado opuesto (el campo es periódico: mismo sitio físico)
    private void CrossFieldEdge()
    {
        Vector2Int c = edgeCrossing; edgeCrossing = Vector2Int.zero;
        Vector3 np = FieldWrap.CrossEdge(transform.position, c, 0.4f);
        if (useNav && NavMesh.SamplePosition(np, out NavMeshHit h, 3f, NavMesh.AllAreas) && !FieldWorld.Refuge(h.position, 0.5f)) np = h.position;   // v7.4.0: nunca dentro de una casa
        Vector3 goal = edgeDest;
        if (useNav && agent != null && agent.enabled) agent.Warp(np); else transform.position = np;
        lastPos = transform.position; EdgeCrossings++;
        SetDestination(goal);
    }

    // v7.0.2: imitador: lo coloca el director de encuentros (anfitrión / un jugador)
    public void WarpTo(Vector3 p, float yaw)
    {
        hasDestination = false; fallbackPath.Clear(); fallbackIndex = 0;
        if (useNav && agent != null && agent.enabled) { agent.Warp(p); if (agent.isOnNavMesh) agent.ResetPath(); }
        else transform.position = p;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        lastPos = transform.position;
    }
    public bool UsesNav { get { return useNav; } }

    // v7.0.2: silbido (G). Si está en su radio (menos a través de paredes; más en las piscinas por el eco), tras un momento
    // va a investigar el sitio del silbido (no lo detecta: búsqueda normal, solo persigue si luego ve al jugador)
    public const float WhistleRadius = 40f, WhistleWallFactor = 0.55f, WhistleReactDelay = 0.7f;
    public int WhistlesHeard { get; private set; }
    public Vector3 InvestigatePoint { get { return lastKnownPosition; } }
    private float whistleDelay = -1f; private Vector3 whistlePos;
    public static float WhistleRange(Vector3 from, Vector3 to, out bool wall)
    {
        wall = Physics.Linecast(from + Vector3.up * 1.6f, to + Vector3.up * 1.4f, ~0, QueryTriggerInteraction.Ignore);
        float r = WhistleRadius * (wall ? WhistleWallFactor : 1f);
        if (WaterVolume.Active != null) r *= 1.3f;   // Poolrooms: azulejos y agua, el silbido resuena más lejos
        return r;
    }
    public bool HearWhistle(Vector3 pos)
    {
        if (netProxy || stopped || CurrentState == State.Kill || CurrentState == State.Chase || Disguised) return false;
        pos = FieldWrap.Image(pos, transform.position);   // v7.0.4: al otro lado del borde del campo también se oye
        float r = WhistleRange(pos, transform.position, out bool wall);
        if (Vector3.Distance(pos, transform.position) > r) return false;
        whistlePos = pos;
        if (NavMesh.SamplePosition(pos, out NavMeshHit h, 3f, NavMesh.AllAreas)) whistlePos = h.position;
        whistleDelay = WhistleReactDelay; WhistlesHeard++;
        return true;
    }

    // Detiene al monstruo (lo llama GameManager al terminar la partida)
    public void Stop()
    {
        stopped = true;
        StopMoving();
    }

    private void StopMoving()
    {
        hasDestination = false;
        fallbackPath.Clear();
        fallbackIndex = 0;
        if (useNav && agent != null && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    // ------------------------------------------------------------------
    // Sentidos
    // ------------------------------------------------------------------

    private Vector3 EyePosition()
    {
        return transform.position + Vector3.up * eyeHeight;
    }

    // ¿Hay una línea sin obstáculos entre el monstruo y el jugador?
    private bool HasLineOfSight()
    {
        Vector3 from = EyePosition();
        Vector3 to = TargetPos + Vector3.up * 1.2f;

        RaycastHit hit;
        if (Physics.Linecast(from, to, out hit, sightMask & ~PlayerBody.LayerMaskBit, QueryTriggerInteraction.Ignore))
        {
            return hit.transform == target || hit.transform.IsChildOf(target);
        }
        // v7.2.0: también al revés (paredes de una sola cara vistas por detrás)
        if (Physics.Linecast(to, from, out hit, sightMask & ~PlayerBody.LayerMaskBit, QueryTriggerInteraction.Ignore))
            return hit.transform == transform || hit.transform.IsChildOf(transform) || hit.transform == target || hit.transform.IsChildOf(target);
        return true;
    }

    // v7.2.0: atrapar exige además que no haya pared entre los dos a la altura del cuerpo (en ambos sentidos: las mallas de
    // colisión de una cara no se detectan por detrás) y un camino corto por la NavMesh (no a través de una pared de casa ni de
    // una ventana: antes bastaba la distancia + la línea de los ojos, que pasaba por los huecos)
    public static float LastCatchPath { get; private set; }
    // pruebas: ¿atraparía ahora mismo al objetivo? (misma condición que Update)
    public bool WouldCatchNow()
    {
        Vector3 d = TargetPos - transform.position;
        return new Vector2(d.x, d.z).magnitude < killDistance && Mathf.Abs(d.y) < 2f && HasLineOfSight() && CatchReachable();
    }
    public bool CatchReachable()
    {
        Vector3 a = transform.position, b = TargetPos;
        if (FieldWorld.Refuge(b)) return false;   // v7.4.0: dentro de una casa no te atrapa
        int mask = sightMask & ~PlayerBody.LayerMaskBit;
        foreach (float h in CatchHeights)
        {
            Vector3 pa = new Vector3(a.x, Mathf.Max(a.y, b.y) + h, a.z), pb = new Vector3(b.x, Mathf.Max(a.y, b.y) + h, b.z);
            if (BlockedBy(pa, pb, mask) || BlockedBy(pb, pa, mask)) return false;
        }
        LastCatchPath = -1f;
        if (useNav && agent != null && agent.enabled && agent.isOnNavMesh)
        {
            if (!NavMesh.SamplePosition(b, out NavMeshHit hb, 1.2f, agent.areaMask)) return true;   // jugador fuera de la NavMesh (p. ej. saltando): basta lo físico
            if (!NavMesh.Raycast(agent.nextPosition, hb.position, out NavMeshHit hr, agent.areaMask)) { LastCatchPath = Vector3.Distance(agent.nextPosition, hb.position); return true; }
            NavMeshPath path = catchPath ?? (catchPath = new NavMeshPath());
            if (!NavMesh.CalculatePath(agent.nextPosition, hb.position, agent.areaMask, path) || path.status != NavMeshPathStatus.PathComplete) return false;
            float len = 0f; Vector3[] c = path.corners; for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            LastCatchPath = len;
            return len <= killDistance + 1.2f;
        }
        return true;
    }
    static readonly float[] CatchHeights = { 0.5f, 1.0f, 1.5f };
    NavMeshPath catchPath;
    bool BlockedBy(Vector3 from, Vector3 to, int mask)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore)) return false;
        if (target != null && (hit.transform == target || hit.transform.IsChildOf(target))) return false;
        if (hit.transform == transform || hit.transform.IsChildOf(transform)) return false;
        return true;
    }

    // Vista: distancia + ángulo de visión + línea de visión
    private bool CanSee(Vector3 toTarget, float distance, bool lineOfSight)
    {
        if (!lineOfSight)
        {
            return false;
        }

        float range = sightRange;
        if (playerFlashlight != null && !playerFlashlight.IsOn)
        {
            range *= darkSightFactor; // en la oscuridad ve menos
        }
        else if (remoteTarget != null && !remoteTarget.FlashOn)
        {
            range *= darkSightFactor;
        }
        if (distance > range)
        {
            return false;
        }

        Vector3 flatDir = new Vector3(toTarget.x, 0f, toTarget.z);
        if (flatDir.sqrMagnitude < 0.0001f)
        {
            return true;
        }
        float angle = Vector3.Angle(transform.forward, flatDir);
        return angle <= fieldOfView * 0.5f;
    }

    // Oído: el jugador hace ruido según camine o corra (FirstPersonController.CurrentNoiseRadius)
    private bool CanHear(float distance, bool lineOfSight)
    {
        float noise;
        if (playerController != null) noise = playerController.CurrentNoiseRadius;
        else if (remoteTarget != null) noise = remoteTarget.NoiseRadius;
        else return false;
        float radius = noise * hearingScale;
        if (!lineOfSight)
        {
            radius *= hearingThroughWallsFactor;
        }
        return radius > 0f && distance <= radius;
    }

    // ------------------------------------------------------------------
    // Navegación (NavMesh o alternativa)
    // ------------------------------------------------------------------

    private void SetSpeed(float speed)
    {
        currentSpeed = speed;
        if (useNav && agent != null)
        {
            agent.speed = speed * (WaterSlow > 0f ? WaterSlow : 1f);
        }
    }

    private void SetDestination(Vector3 dest)
    {
        edgeCrossing = Vector2Int.zero;
        if (FieldWrap.On)
        {   // v7.0.4: destino en otra imagen del campo: ir al punto del borde por el que se cruza (y allí se pasa al otro lado)
            dest = FieldWrap.Image(dest, transform.position);
            edgeDest = dest;
            dest = FieldWrap.NavTarget(transform.position, dest, 0.4f, out edgeCrossing);
        }
        destination = dest;
        hasDestination = true;

        if (useNav)
        {
            if (!agent.isOnNavMesh) return;

            NavMeshHit hit;
            Vector3 point = NavMesh.SamplePosition(dest, out hit, 2f, NavMesh.AllAreas) ? hit.position : dest;
            agent.isStopped = false;
            agent.SetDestination(point);
        }
        else
        {
            fallbackIndex = 0;
            if (generator == null || !generator.HasLevel || !generator.TryGetPath(transform.position, dest, fallbackPath))
            {
                // Sin generador: línea recta (última opción)
                fallbackPath.Clear();
                fallbackPath.Add(dest);
            }
        }
    }

    private bool HasArrived()
    {
        if (!hasDestination)
        {
            return true;
        }
        if (useNav)
        {
            if (agent.pathPending) return false;
            if (agent.pathStatus == NavMeshPathStatus.PathInvalid) return true;
            return agent.remainingDistance <= arriveDistance;
        }
        return fallbackIndex >= fallbackPath.Count;
    }

    // Si el agente lleva tiempo sin avanzar, elige otro punto
    private void CheckStuck(float dt)
    {
        if (!useNav || agent == null || !agent.isOnNavMesh)
        {
            return;
        }
        if (agent.velocity.sqrMagnitude < 0.01f && !agent.pathPending)
        {
            stuckTimer += dt;
            if (stuckTimer > 2f)
            {
                stuckTimer = 0f;
                if (CurrentState == State.Patrol) PickNewPatrolPoint();
                else if (CurrentState == State.Search) EnterPatrol();
            }
        }
        else
        {
            stuckTimer = 0f;
        }
    }

    private void PickNewPatrolPoint()
    {
        waitTimer = 0f;
        stuckTimer = 0f;

        Vector3 point;
        if (generator != null && generator.HasLevel)
        {
            point = generator.GetRandomCellCenter();
            for (int tr = 0; tr < 6 && FieldWorld.Refuge(point, 1.5f); tr++) point = generator.GetRandomCellCenter();   // v7.4.0: no patrulla hacia dentro de las casas
        }
        else
        {
            Vector2 circle = Random.insideUnitCircle * 15f;
            point = transform.position + new Vector3(circle.x, 0f, circle.y);
        }
        SetDestination(point);
    }

    // Movimiento sin NavMesh: seguir los puntos del camino por la cuadrícula
    private void UpdateFallbackMovement(float dt)
    {
        if (CurrentState == State.Kill || fallbackIndex >= fallbackPath.Count)
        {
            return;
        }

        Vector3 pos = transform.position;
        Vector3 waypoint = fallbackPath[fallbackIndex];
        waypoint.y = pos.y;

        Vector3 to = waypoint - pos;
        float dist = to.magnitude;
        float step = currentSpeed * (WaterSlow > 0f ? WaterSlow : 1f) * dt;

        if (dist <= step)
        {
            transform.position = waypoint;
            fallbackIndex++;
        }
        else
        {
            transform.position = pos + (to / dist) * step;
        }

        if (to.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(to);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 360f * dt);
        }
    }
}
