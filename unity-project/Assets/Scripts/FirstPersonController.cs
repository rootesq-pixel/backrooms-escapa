using UnityEngine;

// Controlador en primera persona basado en CharacterController.
// WASD + ratón, correr (Shift, stamina limitada), agacharse (Ctrl mantener / C alternar), inclinarse (Q/E),
// aceleración suave, balanceo de cabeza sincronizado con los pasos, inclinación al desplazarse de lado, patada de FOV
// al correr, respiración según la stamina y caída con "dip". Usa el Input Manager clásico.
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    public static FirstPersonController LocalInstance;   // v7.0: jugador local (solo hay uno por proceso)

    [Header("Referencias")]
    [Tooltip("Transform de la cámara (se inclina arriba/abajo). Si está vacío se busca una Camera hija.")]
    public Transform cameraPivot;

    [Header("Movimiento")]
    public float walkSpeed = 3.5f;
    public float sprintSpeed = 6.0f;
    public float crouchSpeed = 1.7f;
    public float gravity = -20f;
    [Tooltip("Multiplicador de velocidad (p. ej. 0.88 al caminar por el agua).")]
    public float speedFactor = 1f;
    [Tooltip("Aceleración (m/s²) al empezar a moverse y deceleración al soltar las teclas.")]
    public float acceleration = 16f;
    public float deceleration = 13f;
    public float airControl = 2.5f;

    [Header("Ratón")]
    public float mouseSensitivity = 2f;
    public float maxPitch = 85f;
    public bool lockCursorOnStart = true;
    [Tooltip("Si está activo, usa los valores de GameSettings (sensibilidad, invertir Y, suavizado).")]
    public bool useGameSettings = true;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaDrainPerSecond = 25f;
    public float staminaRegenPerSecond = 15f;
    [Tooltip("Segundos sin correr antes de empezar a recuperar stamina.")]
    public float staminaRegenDelay = 1f;
    [Tooltip("Tras agotarse, hay que recuperar este mínimo para volver a correr.")]
    public float minStaminaToSprint = 20f;

    [Header("Ruido (lo usa la IA del monstruo para 'oír')")]
    public float walkNoiseRadius = 6f;
    public float sprintNoiseRadius = 14f;
    public float crouchNoiseFactor = 0.4f;

    [Header("Cámara")]
    public bool headBob = true;
    public float bobAmplitude = 0.035f;
    public float bobSwayAmplitude = 0.018f;
    public float bobRollDegrees = 0.45f;
    public float strafeTiltDegrees = 1.6f;
    public float sprintFovKick = 7f;
    public float standEyeHeight = 1.6f;
    public float crouchEyeHeight = 0.95f;
    public float standHeight = 1.8f;
    public float crouchHeight = 1.1f;
    public float leanDistance = 0.3f;
    public float leanDegrees = 11f;

    [Header("Pasos (distancia entre pisadas)")]
    public float stepDistanceWalk = 1.7f;
    public float stepDistanceSprint = 2.2f;
    public float stepDistanceCrouch = 1.2f;

    // Se pone a false cuando termina la partida
    public bool InputEnabled = true;

    // Entrada simulada (solo pruebas automáticas)
    [HideInInspector] public bool debugInput;
    [HideInInspector] public Vector2 debugMove;
    [HideInInspector] public bool debugSprint, debugCrouch;
    [HideInInspector] public float debugLean;
    // Respiración: volumen de la última respiración si está sonando (0 si no suena nada), necesidad 0..1 y si hay alguna sonando
    public float BreathVolume { get { return BreathAudible ? breathVol : 0f; } }
    public float BreathNeed { get { return breathNeed; } }
    public bool BreathAudible { get { return breathVol > 0.0001f && Time.time < breathEndTime; } }
    public int BreathsPlayed { get; private set; }
    public void SetStamina01(float f) { stamina = Mathf.Clamp01(f) * maxStamina; if (stamina < minStaminaToSprint) exhausted = stamina <= 0f; }
    public float EyeHeightNow { get { return eyeHeightCurrent; } }

    // Estado público (para HUD, IA y audio)
    public void RestoreStamina(float fraction) { stamina = Mathf.Clamp(stamina + maxStamina * fraction, 0f, maxStamina); if (stamina >= minStaminaToSprint) exhausted = false; }
    public void SetStaminaForTests(float v) { stamina = Mathf.Clamp(v, 0f, maxStamina); exhausted = false; }
    public float Stamina01 { get { return maxStamina > 0f ? stamina / maxStamina : 0f; } }
    public bool IsSprinting { get; private set; }
    public bool IsMoving { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsGrounded { get { return controller != null && controller.isGrounded; } }
    public float FlatSpeed { get; private set; }
    public float CurrentNoiseRadius { get; private set; }
    public float Lean { get { return leanValue; } }
    public float Pitch { get { return pitch; } }
    public void SetPitch(float p) { pitch = Mathf.Clamp(p, -maxPitch, maxPitch); }   // tráiler / cámara guionizada

    // --- Agua (WaterVolume) ---
    [Header("Agua")]
    public float swimDepth = 1.15f;          // a partir de esta profundidad se nada
    public float swimSpeed = 2.0f;
    public float WaterDepth { get; private set; }
    public float WaterFactor { get; private set; }
    public bool IsSwimming { get; private set; }
    public bool IsUnderwater { get; private set; }
    public bool IsDiving { get; private set; }
    private float swimStrokeT;

    // Se dispara en cada pisada (parámetro: intensidad 0.4..1.4) y al aterrizar (intensidad según la caída)
    public event System.Action<float> Footstep;
    public event System.Action<float> Landed;

    private CharacterController controller;
    private float climbCooldown;
    public int ClimbOutCount { get; private set; }

    // v6.8.1: salir de la piscina. Borde (en cualquier punto), escalera y rescate si se queda atascado.
    [HideInInspector] public bool debugJump;
    [Header("Salto (v7.0.1)")]
    public float jumpHeight = 0.95f;
    public float jumpStaminaCost = 8f;
    public float coyoteTime = 0.12f, jumpBuffer = 0.15f;
    public int JumpCount { get; private set; }
    public bool IsJumping { get; private set; }
    public event System.Action Jumped;
    private float sinceGrounded, jumpBufT = -1f, jumpCooldown;
    private bool prevJumpHeld;
    // suelo robusto: isGrounded del CharacterController más una sonda corta (pendientes, bordes de colinas)
    public bool GroundProbe()
    {
        if (controller == null) return false;
        if (controller.isGrounded) return true;
        if (verticalVelocity > 0.5f) return false;
        float r = controller.radius * 0.9f;
        Vector3 o = transform.position + controller.center - Vector3.up * (controller.height * 0.5f - controller.radius);
        return Physics.SphereCast(o + Vector3.up * 0.05f, r, Vector3.down, out RaycastHit h, 0.2f + controller.skinWidth, ~0, QueryTriggerInteraction.Ignore) && h.normal.y > 0.55f;
    }
    public int LadderClimbCount { get; private set; }
    public int PoolRescueCount { get; private set; }
    public bool IsClimbingLadder { get { return ladderT >= 0f; } }
    public float PoolStuckTime { get { return poolStuckT; } }
    private float ladderT = -1f, poolStuckT;
    private Vector3 ladderFrom, ladderMid, ladderTo;

    private bool ClearAt(Vector3 feet)
    {
        int mask = ~((1 << 2) | (1 << WaterVolume.WaterLayer) | (1 << 8));
        Collider[] hits = Physics.OverlapCapsule(feet + Vector3.up * (controller.radius + 0.08f), feet + Vector3.up * (controller.height - controller.radius),
            controller.radius * 0.9f, mask, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits) if (c != controller && !c.transform.IsChildOf(transform)) return false;
        return true;
    }

    private void PlaceAt(Vector3 feet)
    {
        controller.enabled = false;
        transform.position = feet;
        controller.enabled = true;
        verticalVelocity = 0f; moveVel = Vector3.zero; IsSwimming = false; poolStuckT = 0f;
    }

    private bool TryClimbOut(Vector3 dir)
    {
        climbCooldown -= Time.deltaTime;
        if (climbCooldown > 0f) return false;
        dir.y = 0f; if (dir.sqrMagnitude < 0.01f) return false;
        dir.Normalize();
        Vector3 p = transform.position;
        int mask = ~((1 << 2) | (1 << WaterVolume.WaterLayer) | (1 << 8));
        // pared del vaso delante (a la altura de la cintura o del pecho)
        float reach = controller.radius + 0.5f;
        if (!Physics.Raycast(p + Vector3.up * 0.9f, dir, reach, mask, QueryTriggerInteraction.Ignore)
            && !Physics.Raycast(p + Vector3.up * 0.5f, dir, reach, mask, QueryTriggerInteraction.Ignore)) return false;
        // suelo arriba al otro lado, a menos de 1,9 m sobre los pies y con hueco libre encima
        Vector3 top = p + dir * (controller.radius + 0.75f) + Vector3.up * 2.4f;
        RaycastHit hit;
        if (!Physics.Raycast(top, Vector3.down, out hit, 2.8f, mask, QueryTriggerInteraction.Ignore)) return false;
        float rise = hit.point.y - p.y;
        if (rise < 0.3f || rise > 1.9f || hit.normal.y < 0.8f) return false;
        if (!ClearAt(hit.point)) return false;
        PlaceAt(hit.point + Vector3.up * 0.02f);
        climbCooldown = 0.6f; ClimbOutCount++;
        return true;
    }

    private void StartLadder()
    {
        ladderFrom = transform.position;
        Vector3 foot = PoolExit.LadderPos;
        ladderMid = new Vector3(foot.x, PoolExit.DeckY + 0.25f, foot.z);
        ladderTo = PoolExit.LadderTop;
        ladderT = 0f; moveVel = Vector3.zero; verticalVelocity = 0f;
        controller.enabled = false;
    }

    // subir por la escalera: 0..0.7 sube pegado a la escalera, 0.7..1 pasa a la playa
    private void StepLadder(float dt)
    {
        ladderT = Mathf.Min(1f, ladderT + dt / 1.3f);
        Vector3 pos;
        if (ladderT < 0.7f)
        {
            float k = ladderT / 0.7f;
            Vector3 start = new Vector3(Mathf.Lerp(ladderFrom.x, ladderMid.x, Mathf.Clamp01(k * 3f)), ladderFrom.y, Mathf.Lerp(ladderFrom.z, ladderMid.z, Mathf.Clamp01(k * 3f)));
            pos = new Vector3(start.x, Mathf.Lerp(ladderFrom.y, ladderMid.y, Mathf.SmoothStep(0f, 1f, k)), start.z);
        }
        else
        {
            float k = (ladderT - 0.7f) / 0.3f;
            pos = Vector3.Lerp(ladderMid, ladderTo, Mathf.SmoothStep(0f, 1f, k));
        }
        transform.position = pos;
        if (ladderT >= 1f)
        {
            ladderT = -1f;
            PlaceAt(ladderTo);
            LadderClimbCount++;
        }
    }

    private void UpdatePoolExit(Vector3 input, bool jump, float dt)
    {
        Vector3 feet = transform.position;
        if (!PoolExit.InPool(feet)) { poolStuckT = 0f; return; }
        poolStuckT += dt;
        if (IsDiving) return;
        Vector3 wish = transform.TransformDirection(input); wish.y = 0f;
        bool pushing = wish.sqrMagnitude > 0.01f;
        if (pushing) wish.Normalize();
        // escalera: cerca de ella, empujando hacia la playa o pulsando saltar
        if (PoolExit.NearLadder(feet) && (jump || (pushing && Vector3.Dot(wish, PoolExit.LadderOut) > 0.3f))) { StartLadder(); return; }
        Vector3 outDir; Vector3 edge = PoolExit.NearestEdge(feet, out outDir);
        if (!pushing && !jump) return;
        // borde: en cualquier punto, empujando contra él (o saltar junto al borde)
        Vector3 d = pushing ? wish : outDir;
        if (TryClimbOut(d)) return;
        // esquinas: probar la normal del borde más cercano si se empuja más o menos hacia él
        if (pushing && Vector3.Dot(wish, outDir) > 0.35f && TryClimbOut(outDir)) return;
        // rescate: lleva un rato en el vaso y sigue pulsando saltar / empujando hacia el borde
        Vector3 toEdge = edge - feet; toEdge.y = 0f;
        bool atWall = toEdge.magnitude < controller.radius + 0.6f;
        if (poolStuckT > PoolExit.RescueSeconds && (jump || (atWall && pushing && Vector3.Dot(wish, outDir) > 0.3f)))
        {
            PlaceAt(PoolExit.RescuePoint(feet));
            PoolRescueCount++;
        }
    }
    private Camera cam;
    private float baseFov = 70f;
    private float stamina;
    private bool exhausted;
    private float regenTimer;
    private float pitch;
    private float verticalVelocity;
    private Vector3 moveVel;
    private Vector3 pivotRestPosition;
    private bool restCaptured;

    // Cabeza
    private float stepAccum;
    private int stepIndex;
    private float bobBlend;
    private float sprintBlend;
    private float tiltRoll;
    private float leanValue, leanTarget;
    private float eyeHeightCurrent;
    private float landDip;
    private float landDipVel;
    private float airTime;
    private float lastFallSpeed;
    private bool wasGrounded = true;
    private bool crouchToggled;
    private Vector2 smoothedLook;

    // Respiración
    private AudioSource breath;
    private AudioClip breathIn, breathOut;
    private float breathVol, breathNeed, breathEndTime, nextBreathAt, exhaleAt = -1f, exhaleGain;
    private float sprintTime, windTimer;

    private void Awake()
    {
        LocalInstance = this;
        controller = GetComponent<CharacterController>();
        stamina = maxStamina;
        eyeHeightCurrent = standEyeHeight;
    }

    private void Start()
    {
        // La cámara puede asignarse después de AddComponent (GameBootstrap), por eso se resuelve aquí
        if (cameraPivot == null)
        {
            Camera c = GetComponentInChildren<Camera>();
            if (c != null) cameraPivot = c.transform;
        }
        if (cameraPivot != null)
        {
            cam = cameraPivot.GetComponent<Camera>();
            if (cam != null) baseFov = cam.fieldOfView;
            pivotRestPosition = new Vector3(0f, standEyeHeight, 0f);
            restCaptured = true;
            pitch = NormalizeAngle(cameraPivot.localEulerAngles.x);
        }
        if (lockCursorOnStart && !UIManager.InputBlocked)
        {
            SetCursorLocked(true);
        }

        // Respiración: dos clips cortos (inhalar / exhalar) que se lanzan «a mano»; no hay ningún bucle de ruido sonando.
        breathIn = ProceduralAudio.CreateBreathPhase(true);
        breathOut = ProceduralAudio.CreateBreathPhase(false);
        breath = gameObject.AddComponent<AudioSource>();
        breath.loop = false;
        breath.volume = 1f;
        breath.spatialBlend = 0f;
        breath.playOnAwake = false;
    }

    private void OnDestroy()
    {
        if (LocalInstance == this) LocalInstance = null;
        ProceduralAssets.SafeDestroy(breathIn);
        ProceduralAssets.SafeDestroy(breathOut);
    }

    private void Update()
    {
        HandleCursor();

        bool blocked = UIManager.InputBlocked;
        if (InputEnabled && !blocked)
        {
            HandleLook();
            HandleMovement(Time.deltaTime);
        }
        else if (!InputEnabled)
        {
            IsSprinting = false;
            IsMoving = false;
            CurrentNoiseRadius = 0f;
            moveVel = Vector3.zero;
            FlatSpeed = 0f;
        }
        if (!blocked && InputEnabled) UpdateCamera(Time.deltaTime);
        UpdateBreathing(blocked ? 0f : Time.deltaTime);
    }

    // Si hay menús (UIManager) el cursor lo gestiona él; sin UIManager: Esc libera y un clic captura
    private void HandleCursor()
    {
        if (UIManager.Instance != null) return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            SetCursorLocked(false);
        }
        else if (Cursor.lockState != CursorLockMode.Locked && Input.GetMouseButtonDown(0))
        {
            SetCursorLocked(true);
        }
    }

    public void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    // Mirada: el ratón gira el cuerpo en horizontal y la cámara en vertical
    private void HandleLook()
    {
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        float sens = useGameSettings ? GameSettings.MouseSensitivity : mouseSensitivity;
        float inv = (useGameSettings && GameSettings.InvertY) ? -1f : 1f;
        Vector2 raw = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        if (useGameSettings && GameSettings.SmoothLook)
        {
            smoothedLook = Vector2.Lerp(smoothedLook, raw, 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime));
            raw = smoothedLook;
        }
        else smoothedLook = raw;

        transform.Rotate(0f, raw.x * sens, 0f);
        pitch = Mathf.Clamp(pitch - raw.y * sens * inv, -maxPitch, maxPitch);
    }

    private void HandleMovement(float dt)
    {
        // --- Entrada ---
        Vector3 input = debugInput ? new Vector3(debugMove.x, 0f, debugMove.y) : new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        input = Vector3.ClampMagnitude(input, 1f);
        bool hasInput = input.sqrMagnitude > 0.01f;
        if (ladderT >= 0f) { StepLadder(dt); return; }
        bool wantsJump = debugJump || Input.GetKey(KeyCode.Space);
        bool jumpPressed = wantsJump && !prevJumpHeld; prevJumpHeld = wantsJump;
        if (jumpPressed) jumpBufT = jumpBuffer; else if (jumpBufT >= 0f) jumpBufT -= dt;

        // --- Agua: profundidad, vadeo y natación ---
        WaterVolume wv = WaterVolume.Active;
        float rawDepth = wv != null ? wv.DepthAt(transform.position) : 0f;
        WaterDepth = Mathf.Lerp(WaterDepth, rawDepth, 1f - Mathf.Exp(-14f * dt));
        IsSwimming = wv != null && (IsSwimming ? rawDepth > swimDepth - 0.2f : rawDepth > swimDepth);
        WaterFactor = wv != null ? WaterVolume.WadeFactor(WaterDepth) : 1f;

        // --- Agacharse (Ctrl mantener, C alternar). Nadando, Ctrl = bucear ---
        if (Input.GetKeyDown(KeyCode.C)) crouchToggled = !crouchToggled;
        bool wantsCrouch = debugCrouch || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) || crouchToggled;
        IsDiving = IsSwimming && wantsCrouch;
        if (IsSwimming) wantsCrouch = false;
        if (wantsCrouch) IsCrouching = true;
        else if (IsCrouching && CanStandUp()) IsCrouching = false;
        UpdateCapsule(dt);

        // --- Correr y stamina ---
        bool wantsSprint = debugSprint || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        IsSprinting = wantsSprint && hasInput && !exhausted && stamina > 0f && !IsCrouching && input.z > -0.1f && !IsSwimming;
        if (IsSprinting && IsCrouching) IsSprinting = false;

        if (IsSprinting)
        {
            stamina -= staminaDrainPerSecond * dt;
            regenTimer = staminaRegenDelay;
            if (stamina <= 0f)
            {
                stamina = 0f;
                exhausted = true; // hay que descansar un poco
            }
        }
        else
        {
            if (regenTimer > 0f)
            {
                regenTimer -= dt;
            }
            else
            {
                float regen = staminaRegenPerSecond * (hasInput ? 0.6f : 1.2f);
                stamina = Mathf.Min(maxStamina, stamina + regen * dt);
            }
            if (exhausted && stamina >= minStaminaToSprint)
            {
                exhausted = false;
            }
        }

        // --- Velocidad con aceleración / deceleración suaves ---
        float baseSpeed = IsCrouching ? crouchSpeed : (IsSprinting ? sprintSpeed : walkSpeed);
        float speed = baseSpeed * speedFactor * WaterFactor;
        if (IsSwimming) speed = swimSpeed * speedFactor;
        Vector3 desired = transform.TransformDirection(input) * speed;
        bool grounded = controller.isGrounded;
        float rate = hasInput ? acceleration : deceleration;
        if (!grounded) rate = airControl;
        moveVel = Vector3.MoveTowards(moveVel, desired, rate * dt);
        FlatSpeed = new Vector2(moveVel.x, moveVel.z).magnitude;
        IsMoving = FlatSpeed > 0.35f;

        float noise = IsMoving ? (IsSprinting ? sprintNoiseRadius : walkNoiseRadius) : 0f;
        if (IsCrouching) noise *= crouchNoiseFactor;
        CurrentNoiseRadius = noise * Mathf.Clamp01(FlatSpeed / Mathf.Max(0.1f, walkSpeed * speedFactor));

        // --- Gravedad y caída ---
        bool probe = GroundProbe();
        if (probe) sinceGrounded = 0f; else sinceGrounded += dt;
        if (jumpCooldown > 0f) jumpCooldown -= dt;
        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f; // pequeño empuje para mantenerse pegado al suelo
            IsJumping = false;
        }
        // --- Salto: con margen (coyote) y búfer de pulsación; no agachado, nadando ni agotado ---
        if (jumpBufT >= 0f && sinceGrounded <= coyoteTime && jumpCooldown <= 0f && !IsSwimming && !IsCrouching && !(PoolExit.Active && PoolExit.InPool(transform.position)) && stamina >= jumpStaminaCost * 0.5f)
        {
            verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
            stamina = Mathf.Max(0f, stamina - jumpStaminaCost); regenTimer = staminaRegenDelay;
            jumpBufT = -1f; sinceGrounded = 99f; jumpCooldown = 0.25f; IsJumping = true; JumpCount++;
            grounded = false;
            if (Jumped != null) Jumped();
        }
        if (IsSwimming)
        {
            // flotación: los ojos quedan justo sobre la superficie (o bajo ella al bucear)
            float surf = wv.SurfaceHeightAt(transform.position);
            float targetFeet = surf - standEyeHeight + 0.14f - (IsDiving ? 1.5f : 0f);
            float desiredVy = Mathf.Clamp((targetFeet - transform.position.y) * 3.2f, -2.6f, 2.6f);
            verticalVelocity = Mathf.Lerp(verticalVelocity, desiredVy, 1f - Mathf.Exp(-6f * dt));
            airTime = 0f; lastFallSpeed = 0f;
        }
        else
        {
            verticalVelocity += gravity * dt;
        }
        if (!grounded && !IsSwimming)
        {
            airTime += dt;
            lastFallSpeed = Mathf.Min(lastFallSpeed, verticalVelocity);
        }

        Vector3 move = moveVel;
        move.y = verticalVelocity;
        controller.Move(move * dt);

        // salir del agua: en la sala fija (v6.8.1) borde/escalera/rescate; en el resto, nadando hacia un borde (v6.7.1)
        if (PoolExit.Active) UpdatePoolExit(input, wantsJump, dt);
        else if (IsSwimming && !IsDiving && moveVel.sqrMagnitude > 0.5f) TryClimbOut(moveVel.normalized);

        // Aterrizaje
        bool nowGrounded = controller.isGrounded;
        if (nowGrounded && !wasGrounded && airTime > 0.18f)
        {
            float impact = Mathf.Clamp01((-lastFallSpeed - 2f) / 10f);
            landDipVel = -impact * 1.2f;
            landDip = -impact * 0.12f;
            if (Landed != null) Landed(0.5f + impact);
        }
        if (nowGrounded) { airTime = 0f; lastFallSpeed = 0f; }
        wasGrounded = nowGrounded;

        // --- Pisadas sincronizadas con el balanceo ---
        if (nowGrounded && IsMoving)
        {
            float stepDist = IsCrouching ? stepDistanceCrouch : (IsSprinting ? stepDistanceSprint : stepDistanceWalk);
            stepAccum += FlatSpeed * dt / stepDist;
            int idx = Mathf.FloorToInt(stepAccum);
            if (idx > stepIndex)
            {
                stepIndex = idx;
                if (Footstep != null) Footstep(IsCrouching ? 0.45f : (IsSprinting ? 1.3f : 0.9f));
            }
        }

        // --- Braceadas al nadar: ondas y chapoteo ---
        if (IsSwimming && IsMoving && wv != null)
        {
            swimStrokeT += dt;
            if (swimStrokeT > 0.85f)
            {
                swimStrokeT = 0f;
                wv.Splash(transform.position + transform.forward * 0.35f, 0.35f, true);
            }
        }

        // --- Inclinarse (Q/E) ---
        float lt = 0f;
        if (Input.GetKey(KeyCode.Q)) lt -= 1f;
        if (Input.GetKey(KeyCode.E)) lt += 1f;
        lt += debugLean;
        leanTarget = lt;
        if (lt != 0f && cameraPivot != null)
        {
            Vector3 origin = transform.position + Vector3.up * eyeHeightCurrent;
            if (Physics.Raycast(origin, transform.right * lt, leanDistance + 0.25f, ~PlayerBody.LayerMaskBit, QueryTriggerInteraction.Ignore)) leanTarget = 0f;
        }
        leanValue = Mathf.Lerp(leanValue, leanTarget, 1f - Mathf.Exp(-9f * dt));

        // Para el tilt
        float strafe = Vector3.Dot(moveVel, transform.right) / Mathf.Max(0.1f, sprintSpeed);
        tiltRoll = Mathf.Lerp(tiltRoll, -strafe * strafeTiltDegrees * 1.6f, 1f - Mathf.Exp(-8f * dt));
    }

    // Capsula del CharacterController (agachado/de pie) con transición suave
    private void UpdateCapsule(float dt)
    {
        float targetH = IsCrouching ? crouchHeight : standHeight;
        float h = Mathf.MoveTowards(controller.height, targetH, 5f * dt);
        if (!Mathf.Approximately(h, controller.height))
        {
            controller.height = h;
            controller.center = new Vector3(0f, h * 0.5f, 0f);
        }
    }

    private bool CanStandUp()
    {
        float r = controller.radius * 0.95f;
        Vector3 p0 = transform.position + Vector3.up * (r + 0.05f);
        Vector3 p1 = transform.position + Vector3.up * (standHeight - r);
        Collider[] hits = Physics.OverlapCapsule(p0, p1, r, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i] != controller && hits[i].transform != transform && !hits[i].transform.IsChildOf(transform)) return false;
        }
        return true;
    }

    // Cámara: altura de ojos, balanceo, tilt, inclinación lateral, FOV y caída
    private void UpdateCamera(float dt)
    {
        if (cameraPivot == null) return;
        if (!restCaptured) { pivotRestPosition = new Vector3(0f, standEyeHeight, 0f); restCaptured = true; }

        float targetEye = IsCrouching ? crouchEyeHeight : standEyeHeight;
        eyeHeightCurrent = Mathf.Lerp(eyeHeightCurrent, targetEye, 1f - Mathf.Exp(-10f * dt));

        bool bob = headBob && GameSettings.HeadBob;
        float moving = (controller.isGrounded && IsMoving) ? 1f : 0f;
        bobBlend = Mathf.Lerp(bobBlend, moving, 1f - Mathf.Exp(-8f * dt));

        float bobY = 0f, bobX = 0f, bobRoll = 0f;
        if (bob)
        {
            float speedK = Mathf.Clamp(FlatSpeed / Mathf.Max(0.1f, walkSpeed * speedFactor), 0f, 1.7f);
            float amp = bobBlend * speedK * (IsSprinting ? 1.25f : 1f);
            float t = stepAccum;   // 1 ciclo = 1 paso
            bobY = -Mathf.Abs(Mathf.Cos(Mathf.PI * t)) * bobAmplitude * amp + bobAmplitude * 0.5f * amp;
            bobX = Mathf.Sin(Mathf.PI * t) * bobSwayAmplitude * amp;
            bobRoll = Mathf.Sin(Mathf.PI * t) * bobRollDegrees * amp;
            // Respiración al estar parado
            if (bobBlend < 0.2f) bobY += Mathf.Sin(Time.time * 1.6f) * 0.0025f * (1f - bobBlend * 5f);
        }

        if (IsSwimming) bobY += Mathf.Sin(Time.time * 1.7f) * 0.03f;
        landDip = Mathf.SmoothDamp(landDip, 0f, ref landDipVel, 0.18f, 100f, dt);

        Vector3 pos = new Vector3(bobX + leanValue * leanDistance, eyeHeightCurrent + bobY + landDip, 0f);
        cameraPivot.localPosition = pos;

        float roll = (bob ? tiltRoll : 0f) + bobRoll - leanValue * leanDegrees;
        cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, roll);

        IsUnderwater = WaterVolume.Active != null && WaterVolume.Active.IsUnderwater(cameraPivot.position);

        if (cam != null)
        {
            sprintBlend = Mathf.Lerp(sprintBlend, IsSprinting ? 1f : 0f, 1f - Mathf.Exp(-6f * dt));
            cam.fieldOfView = baseFov + sprintFovKick * sprintBlend;
        }
    }

    // Empuja objetos con física (objetos flotantes)
    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        Rigidbody body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic || hit.moveDirection.y < -0.3f) return;
        Vector3 push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        body.AddForceAtPosition(push * Mathf.Clamp(FlatSpeed, 0.8f, 6f) * 1.6f, hit.point, ForceMode.Impulse);
    }

    // Respiración suave y limitada:
    //  · en reposo con stamina > 50 % y sin haber corrido: silencio total (no suena ni se reproduce nada);
    //  · tras correr ≥ 1 s: unas pocas respiraciones que se apagan solas (cola proporcional al esfuerzo, máx. 3,5 s);
    //  · con stamina < 50 % o agotado: respiración más rápida y algo más fuerte (siempre muy por debajo de pasos / ambiente);
    //  · inhalar y exhalar son clips distintos con envolvente suave; mínimo 1 s entre respiraciones; se puede desactivar en Opciones.
    private void UpdateBreathing(float dt)
    {
        if (breath == null) return;
        bool allowed = GameSettings.BreathEnabled && InputEnabled && dt > 0f && !IsUnderwater;
        float now = Time.time;

        if (IsSprinting) sprintTime += dt;
        else
        {
            if (sprintTime > 0f)
            {
                if (sprintTime > 1f) windTimer = Mathf.Clamp(sprintTime * 0.8f, 0.8f, 3.5f);
                sprintTime = 0f;
            }
            windTimer = Mathf.Max(0f, windTimer - dt);
        }

        float deficit = Mathf.Clamp01((0.5f - Stamina01) / 0.5f);
        float need = deficit * 0.9f;
        if (IsSprinting && sprintTime > 1f) need = Mathf.Max(need, 0.25f + 0.15f * Mathf.Clamp01((sprintTime - 1f) / 3f));
        if (windTimer > 0f) need = Mathf.Max(need, 0.3f * Mathf.Clamp01(windTimer / 1.5f));
        if (exhausted) need = 1f;
        if (!allowed) need = 0f;
        breathNeed = need;

        if (!allowed)
        {
            if (breath.isPlaying) breath.Stop();
            breathEndTime = 0f; exhaleAt = -1f;
            return;
        }

        // exhalación pendiente (aunque la necesidad haya bajado: se desvanece sola)
        if (exhaleAt >= 0f && now >= exhaleAt)
        {
            exhaleAt = -1f;
            breath.pitch = Mathf.Lerp(0.92f, 1.08f, breathNeed);
            breath.PlayOneShot(breathOut, AudioMix.S(exhaleGain));
            breathEndTime = now + breathOut.length;
        }

        if (need > 0.04f && now >= nextBreathAt && exhaleAt < 0f)
        {
            float period = Mathf.Lerp(3.4f, 1.3f, need);
            breathVol = AudioMix.BreathGain(need);
            exhaleGain = breathVol * 0.9f;
            breath.pitch = Mathf.Lerp(0.95f, 1.1f, need);
            breath.PlayOneShot(breathIn, AudioMix.S(breathVol));
            BreathsPlayed++;
            exhaleAt = now + breathIn.length * 0.8f + 0.12f;
            breathEndTime = Mathf.Max(breathEndTime, now + breathIn.length);
            nextBreathAt = now + Mathf.Max(1.0f, period);   // límite de frecuencia
        }
    }

    // Mueve al jugador a una posición (desactiva el CharacterController un instante para que no lo pise)
    public void Teleport(Vector3 position, float yaw)
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        ladderT = -1f; poolStuckT = 0f;
        controller.enabled = false;
        transform.position = position;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        controller.enabled = true;
        verticalVelocity = 0f;
        moveVel = Vector3.zero;
    }

    // Convierte 0..360 en -180..180
    private static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;
        return angle;
    }
}
