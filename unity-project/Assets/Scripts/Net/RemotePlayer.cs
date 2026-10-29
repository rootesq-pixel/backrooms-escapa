using UnityEngine;
using UnityEngine.UI;

// Avatar de otro jugador (multijugador): el traje hazmat amarillo con animaciones Idle/Walk/Run/Duck, linterna con luz que se enciende/apaga,
// etiqueta con el nombre, posición/rotación interpolada (retardo de ~0,1 s entre muestras de red), pasos y chapoteos espaciales 3D.
// Sin colliders: no estorba al jugador ni a la visión del monstruo (que lo trata como objetivo por posición).
public class RemotePlayer : MonoBehaviour
{
    public const int FlagCrouch = 1, FlagSprint = 2, FlagFlash = 4, FlagExit = 8, FlagSwim = 16, FlagDowned = 32, FlagRevive = 64;   // v7.2.0: FlagRevive = mantiene E sobre un derribado

    public static readonly System.Collections.Generic.List<RemotePlayer> All = new System.Collections.Generic.List<RemotePlayer>();   // v7.0
    public int playerId;
    public string playerName = "Jugador";
    public bool Downed { get; private set; }
    public float Revive01 { get; private set; }
    public bool FlashOn { get; private set; }
    public bool AtExit { get; private set; }
    public float FlatSpeed { get; private set; }
    public bool Crouching { get; private set; }
    public bool Reviving { get; private set; }            // v7.2.0: agachado sobre un derribado, alargando la mano
    public float ReviveK { get; private set; }
    public bool Sprinting { get; private set; }
    public string CurrentState { get; private set; }
    public Animator animator;
    public Light lampLight;
    public bool ModelLoaded { get; private set; }
    public int SampleCount { get { return count; } }
    public Vector3 LatestPos { get { return count > 0 ? buf[(head + count - 1) % N].pos : transform.position; } }
    public int StepsPlayed { get; private set; }
    public const float InterpDelay = 0.1f;

    // Ruido que oye el monstruo (como el FirstPersonController local)
    public float NoiseRadius
    {
        get
        {
            if (Downed || FlatSpeed < 0.15f) return 0f;
            float r = Sprinting ? 14f : 6f;
            if (Crouching) r *= 0.4f;
            return r;
        }
    }

    private struct Sample { public float t; public Vector3 pos; public float yaw, pitch, speed; public int flags; }
    private const int N = 12;
    private readonly Sample[] buf = new Sample[N];
    private int head, count;

    private Transform model, lamp;
    private Text tagText;
    private Transform tagRoot;
    private float walkLen = 0.8f, runLen = 0.6f;
    private readonly int hIdle = Animator.StringToHash("Idle"), hWalk = Animator.StringToHash("Walk"), hRun = Animator.StringToHash("Run"), hDuck = Animator.StringToHash("Duck");
    private float stepAcc, lampPitch, downK;
    private Vector3 lastPos;
    private bool posInit;
    private AudioSource stepSrc;
    private AudioClip stepClip, splashClip;
    private Material lensMat;
    private readonly System.Collections.Generic.List<Object> owned = new System.Collections.Generic.List<Object>();
    private StepKind stepKind = StepKind.Carpet;

    private static readonly Color[] TagColors = {
        new Color(1f, 0.92f, 0.45f), new Color(0.55f, 0.9f, 1f), new Color(0.6f, 1f, 0.6f), new Color(1f, 0.65f, 0.55f) };

    public static RemotePlayer Create(Transform parent, int id, string name, StepKind step)
    {
        GameObject go = new GameObject("Remote_" + name);
        if (parent != null) go.transform.SetParent(parent, false);
        RemotePlayer rp = go.AddComponent<RemotePlayer>();
        rp.playerId = id; rp.playerName = name; rp.stepKind = step;
        rp.Build();
        return rp;
    }

    // v7.0.1: copia del imitador (no cuenta como jugador: fuera de All, sin pisadas, linterna sin luz, etiqueta con fallos)
    public bool IsMimic { get; private set; }
    public bool ShowTag = true;
    public string AnimDiag { get { if (animator == null) return "sin animator"; AnimatorStateInfo si = animator.GetCurrentAnimatorStateInfo(0); return "enabled " + animator.isActiveAndEnabled + " init " + animator.isInitialized + " avatar " + (animator.avatar != null && animator.avatar.isValid) + " human " + animator.isHuman + " ctrl " + (animator.runtimeAnimatorController != null) + " estado " + CurrentState + " idle " + (si.shortNameHash == hIdle) + " walk " + (si.shortNameHash == hWalk) + " t " + si.normalizedTime.ToString("F2") + " speed " + animator.speed.ToString("F2") + " clips " + animator.GetCurrentAnimatorClipInfoCount(0); } }
    public void RestartAnimator()
    {
        if (animator == null) return;
        animator.Rebind(); animator.Play(hIdle, 0, 0f); animator.Update(0f); CurrentState = "Idle";
    }
    public static RemotePlayer CreateMimic(Transform parent, int id, string name, StepKind step)
    {
        RemotePlayer rp = Create(parent, id, name, step);
        rp.IsMimic = true; All.Remove(rp); rp.gameObject.name = "MimicPuppet";
        foreach (Collider c in rp.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        if (rp.lampLight != null) { rp.lampLight.enabled = false; Destroy(rp.lampLight); rp.lampLight = null; }
        return rp;
    }

    private void Build()
    {
        GameObject prefab = Resources.Load<GameObject>(PlayerBody.PrefabResource);
        if (prefab != null)
        {
            GameObject inst = Instantiate(prefab);
            inst.name = "Model";
            model = inst.transform;
            model.SetParent(transform, false);
            foreach (Collider c in inst.GetComponentsInChildren<Collider>(true)) Destroy(c);
            animator = inst.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                animator.Play(hIdle, 0, 0f);
                animator.Update(0f);
                CurrentState = "Idle";
                foreach (AnimationClip c in animator.runtimeAnimatorController.animationClips)
                {
                    if (c.name.StartsWith("Walk")) walkLen = c.length;
                    else if (c.name.StartsWith("Run")) runLen = c.length;
                }
            }
            Transform headT = Find(model, "Head");
            if (headT != null)
            {
                float h = transform.InverseTransformPoint(headT.position).y;
                if (h > 0.2f) { model.localScale = Vector3.one * (1.6f / h); if (animator != null) animator.Update(0f); }
            }
            foreach (SkinnedMeshRenderer r in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                r.updateWhenOffscreen = true;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
            }
            armU = Find(model, "UpperArm.R"); armL = Find(model, "LowerArm.R"); armH = Find(model, "Middle1.R");
            armUL = Find(model, "UpperArm.L"); armLL = Find(model, "LowerArm.L"); armHL = Find(model, "Middle1.L");   // v7.9.0: volante
            ModelLoaded = true;
        }
        else
        {
            // plan B: cápsula amarilla
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(transform, false);
            cap.transform.localPosition = Vector3.up * 0.9f; cap.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            Material m = ProceduralAssets.CreateMaterial(new Color(0.95f, 0.8f, 0.1f), null, Vector2.one, 0.3f);
            cap.GetComponent<Renderer>().sharedMaterial = m; owned.Add(m);
            model = cap.transform;
        }

        BuildLamp();
        BuildTag();
        GameObject sfx = new GameObject("Sfx"); sfx.transform.SetParent(transform, false);   // v7.0.4: lo desplaza WrapView
        stepSrc = sfx.AddComponent<AudioSource>();
        stepSrc.spatialBlend = 1f; stepSrc.minDistance = 1.5f; stepSrc.maxDistance = 20f; stepSrc.rolloffMode = AudioRolloffMode.Linear;
        stepSrc.playOnAwake = false; stepSrc.dopplerLevel = 0f;
        stepClip = ProceduralAudio.CreateFootstep(stepKind);
        splashClip = ProceduralAudio.CreateSplash();
        owned.Add(stepClip); owned.Add(splashClip);
        if (stepKind == StepKind.Grass) rustleSrc = GrassAudio.MakeRustleSource(sfx, true);
        WrapView.Attach(gameObject);   // v7.0.4: imagen más cercana en el Campo infinito
    }

    // v7.0: pasos de hierba y roce también para los jugadores remotos (mismo volumen relativo que los locales)
    private AudioSource rustleSrc;
    private int lastGrass = -1;
    private int lastTake = -1;
    public float RustleLevel { get; private set; }
    public int GrassStepsPlayed { get; private set; }
    private void Rustle(float dt)
    {
        if (rustleSrc == null) return;
        bool grass = !Downed && GrassAudio.OnGrass(transform.position);
        float target = grass ? GrassAudio.RustleTarget(FlatSpeed, Sprinting, Crouching) : 0f;
        RustleLevel = Mathf.MoveTowards(RustleLevel, target, dt * (target > RustleLevel ? 3f : 1.6f));
        rustleSrc.volume = RustleLevel * GrassAudio.RustleMax * AudioMix.Sfx * AudioMix.StepMaster * 0.45f;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++) { Transform r = Find(root.GetChild(i), name); if (r != null) return r; }
        return null;
    }

    // Linterna en la mano derecha (aprox.): tubo oscuro con lente emisiva + foco de luz
    private void BuildLamp()
    {
        GameObject l = new GameObject("Lamp");
        l.transform.SetParent(transform, false);
        l.transform.localPosition = new Vector3(0.24f, 1.22f, 0.32f);
        lamp = l.transform;
        Material body = ProceduralAssets.CreateMaterial(new Color(0.07f, 0.07f, 0.08f), null, Vector2.one, 0.5f);
        lensMat = ProceduralAssets.CreateMaterial(new Color(0.9f, 0.9f, 0.7f), null, Vector2.one, 0.9f);
        owned.Add(body); owned.Add(lensMat);
        GameObject tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(tube.GetComponent<Collider>());
        tube.transform.SetParent(l.transform, false);
        tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        tube.transform.localScale = new Vector3(0.07f, 0.1f, 0.07f);
        tube.GetComponent<Renderer>().sharedMaterial = body;
        GameObject lens = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(lens.GetComponent<Collider>());
        lens.transform.SetParent(l.transform, false);
        lens.transform.localPosition = new Vector3(0f, 0f, 0.105f);
        lens.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        lens.transform.localScale = new Vector3(0.06f, 0.006f, 0.06f);
        lens.GetComponent<Renderer>().sharedMaterial = lensMat;
        GameObject lg = new GameObject("Spot");
        lg.transform.SetParent(l.transform, false);
        lg.transform.localPosition = new Vector3(0f, 0f, 0.12f);
        lampLight = lg.AddComponent<Light>();
        lampLight.type = LightType.Spot; lampLight.range = 18f; lampLight.spotAngle = 58f; lampLight.intensity = 2.0f;
        lampLight.color = new Color(1f, 0.96f, 0.82f); lampLight.shadows = LightShadows.None;
        lampLight.enabled = false;
        SetLens(false);
    }

    private void SetLens(bool on)
    {
        if (lensMat == null) return;
        if (on) { lensMat.EnableKeyword("_EMISSION"); lensMat.SetColor("_EmissionColor", new Color(1f, 0.95f, 0.7f) * 2.2f); }
        else { lensMat.SetColor("_EmissionColor", Color.black); }
    }

    private void BuildTag()
    {
        GameObject root = new GameObject("NameTag", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        root.transform.localPosition = new Vector3(0f, 2.4f, 0f);
        Canvas c = root.AddComponent<Canvas>();
        c.renderMode = RenderMode.WorldSpace;
        RectTransform rt = (RectTransform)root.transform;
        rt.sizeDelta = new Vector2(500f, 90f);
        root.transform.localScale = Vector3.one * 0.0045f;
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(root.transform, false);
        tagText = tg.AddComponent<Text>();
        tagText.font = UiStyle.Mono();
        tagText.fontSize = 44; tagText.fontStyle = FontStyle.Bold;
        tagText.alignment = TextAnchor.MiddleCenter;
        tagText.color = TagColors[Mathf.Abs(playerId) % TagColors.Length];
        tagText.raycastTarget = false;
        tagText.horizontalOverflow = HorizontalWrapMode.Overflow; tagText.verticalOverflow = VerticalWrapMode.Overflow;
        tagText.text = playerName;
        Shadow sh = tg.AddComponent<Shadow>(); sh.effectColor = new Color(0f, 0f, 0f, 0.95f); sh.effectDistance = new Vector2(3f, -3f);
        RectTransform tr = tagText.rectTransform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = tr.offsetMax = Vector2.zero;
        tagRoot = root.transform;
    }

    // ------------------------------------------------------------------
    // Red
    // ------------------------------------------------------------------
    public void Push(Vector3 pos, float yaw, float pitch, float speed, int flags)
    {
        Sample s = new Sample { t = Time.unscaledTime, pos = pos, yaw = yaw, pitch = pitch, speed = speed, flags = flags };
        if (count < N) { buf[(head + count) % N] = s; count++; }
        else { buf[head] = s; head = (head + 1) % N; }
        if (!posInit)
        {
            posInit = true;
            transform.position = pos; transform.rotation = Quaternion.Euler(0f, yaw, 0f); lastPos = pos;
        }
    }

    // v7.0.2: salto inmediato (escenificación del imitador): vacía la interpolación para que no cruce el mapa deslizándose
    public void Snap(Vector3 pos, float yaw)
    {
        count = 0; head = 0; posInit = false;
        Push(pos, yaw, -4f, 0f, FlagFlash);
        Push(pos, yaw, -4f, 0f, FlagFlash);
        transform.position = pos; transform.rotation = Quaternion.Euler(0f, yaw, 0f); lastPos = pos;
    }

    public void SetDowned(bool d) { Downed = d; if (!d) Revive01 = 0f; }
    public void SetRevive(float v) { Revive01 = Mathf.Clamp01(v); }
    public void SetName(string n) { playerName = n; if (tagText != null) tagText.text = n; name = "Remote_" + n; }

    private Sample Interp(float rt)
    {
        if (count == 0) return new Sample { pos = transform.position, yaw = transform.eulerAngles.y };
        if (count == 1) return buf[head];
        Sample a = buf[head];
        for (int i = 1; i < count; i++)
        {
            Sample b = buf[(head + i) % N];
            if (b.t >= rt)
            {
                float span = Mathf.Max(0.0001f, b.t - a.t);
                float k = Mathf.Clamp01((rt - a.t) / span);
                Sample r = b;
                r.pos = Vector3.Lerp(a.pos, b.pos, k);
                r.yaw = Mathf.LerpAngle(a.yaw, b.yaw, k);
                r.pitch = Mathf.Lerp(a.pitch, b.pitch, k);
                r.speed = Mathf.Lerp(a.speed, b.speed, k);
                return r;
            }
            a = b;
        }
        return a;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (count == 0) return;
        Sample s = Interp(Time.unscaledTime - InterpDelay);
        // teletransporte (cambio de nivel/reaparición): salto directo
        if ((s.pos - transform.position).sqrMagnitude > 25f) { transform.position = s.pos; lastPos = s.pos; }
        transform.position = s.pos;
        transform.rotation = Quaternion.Euler(0f, s.yaw, 0f);
        int f = buf[(head + count - 1) % N].flags;
        Reviving = (f & FlagRevive) != 0 && !Downed; Crouching = (f & FlagCrouch) != 0 || Reviving; Sprinting = (f & FlagSprint) != 0;
        FlashOn = (f & FlagFlash) != 0; AtExit = (f & FlagExit) != 0;
        FlatSpeed = Mathf.Lerp(FlatSpeed, s.speed, 1f - Mathf.Exp(-12f * dt));
        bool swim = (f & FlagSwim) != 0;

        DriveAnimator(swim);
        UpdatePose(dt);
        UpdateLamp(s.pitch, dt);
        Footsteps(dt);
        UpdateTag();
    }

    // v7.0.2: el brazo derecho no viene animado en los clips (en primera persona lo coloca un IK): sin esto se quedaba
    // en pose de T. IK de dos huesos: la mano sujeta la linterna (la del imitador sigue sus tirones).
    private Transform armU, armL, armH;
    public bool ArmIkActive { get; private set; }
    public bool AnimatorRunning { get { return animator != null && animator.isActiveAndEnabled && animator.isInitialized && animator.speed > 0.05f && animator.GetCurrentAnimatorClipInfoCount(0) > 0; } }
    public float ArmHandError { get; private set; }
    private Transform armUL, armLL, armHL;
    public bool OnWheel { get; private set; }
    public bool LampShown { get { return lamp != null && lamp.gameObject.activeSelf; } }
    public float WheelErr { get; private set; }
    private void LateUpdate()
    {
        // v7.9.0: en el todoterreno la linterna va guardada; el conductor lleva las dos manos en el volante
        bool seated = DesertCars.IsSeated(playerId);
        if (lamp != null && lamp.gameObject.activeSelf == seated) lamp.gameObject.SetActive(!seated);
        OnWheel = false;
        DesertCar dcar = seated ? DesertCars.DrivenBy(playerId) : null;
        DesertCar scar = seated ? DesertCars.CarOf(playerId) : null;
        if (scar != null) SeatPose(scar);   // conductor y pasajeros
        else SetSeatScale(false);
        if (dcar != null && model != null && model.gameObject.activeInHierarchy && !Downed && dcar.Grips(out Vector3 gl, out Vector3 gr))
        {
            float e1 = ArmIk.Solve(armU, armL, armH, gr, transform.right * 0.6f + Vector3.down);
            float e2 = ArmIk.Solve(armUL, armLL, armHL, gl, -transform.right * 0.6f + Vector3.down);
            WheelErr = Mathf.Max(e1, e2); OnWheel = true; ArmIkActive = true;
            return;
        }
        ArmIkActive = false;
        if (armU == null || armL == null || armH == null || lamp == null || model == null || !model.gameObject.activeInHierarchy || Downed) return;
        Vector3 S = armU.position;
        float a = Vector3.Distance(armU.position, armL.position), b = Vector3.Distance(armL.position, armH.position);
        if (a < 0.01f || b < 0.01f) return;
        Vector3 T = lamp.position - lamp.forward * 0.1f - transform.up * 0.03f;
        // v7.2.0: al reanimar alarga la mano hacia el derribado (abajo y delante)
        if (ReviveK > 0.001f) T = Vector3.Lerp(T, transform.position + transform.forward * 0.55f + transform.up * 0.28f + transform.right * 0.12f, ReviveK);
        Vector3 toT = T - S; float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(a - b) + 0.01f, (a + b) * 0.995f);
        Vector3 dir = toT.normalized;
        Vector3 pole = -transform.up + transform.right * 0.7f - transform.forward * 0.2f;
        Vector3 perp = pole - Vector3.Dot(pole, dir) * dir;
        if (perp.sqrMagnitude < 1e-4f) perp = -transform.up; perp.Normalize();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f), sinA = Mathf.Sqrt(1f - cosA * cosA);
        Vector3 E = S + dir * (a * cosA) + perp * (a * sinA);
        armU.rotation = Quaternion.FromToRotation(armL.position - armU.position, E - S) * armU.rotation;
        armL.rotation = Quaternion.FromToRotation(armH.position - armL.position, (S + dir * d) - armL.position) * armL.rotation;
        ArmHandError = Vector3.Distance(armH.position, S + dir * d);
        ArmIkActive = true;
    }

    private void DriveAnimator(bool swim)
    {
        if (animator == null) return;
        float v = FlatSpeed;
        string want;
        bool sit = DesertCars.IsSeated(playerId);   // v7.9.2: sentado en el todoterreno (agachado quieto, más bajo que el techo)
        if (Downed) want = "Duck";
        else if (sit) want = "Idle";
        else if (Crouching) want = "Duck";
        else if (v < 0.2f) want = "Idle";
        else if (Sprinting && v > 4.2f) want = "Run";
        else want = "Walk";
        if (want != CurrentState)
        {
            CurrentState = want;
            int h = want == "Duck" ? hDuck : want == "Idle" ? hIdle : want == "Run" ? hRun : hWalk;
            animator.CrossFadeInFixedTime(h, 0.18f);
        }
        float speed = 1f;
        if (CurrentState == "Walk") speed = Mathf.Clamp(v / (2f * 1.7f) * walkLen, 0.3f, 2.5f);
        else if (CurrentState == "Run") speed = Mathf.Clamp(v / (2f * 2.2f) * runLen, 0.4f, 2.5f);
        else if (CurrentState == "Duck") speed = Downed ? 0f : 0.6f + Mathf.Clamp01(v / 1.7f) * 0.5f;
        animator.speed = sit ? 0f : speed * (swim ? 0.5f : 1f);
        if (IsMimic) animator.speed *= 0.55f + Mathf.PerlinNoise(Time.time * 4f, 1.7f) * 1.2f;   // animación a tirones
    }

    // Derribado: el cuerpo se tumba en el suelo
    private void UpdatePose(float dt)
    {
        // v7.2.0: levantarse es más lento (se ve cómo se incorpora) y el que reanima se inclina hacia delante
        downK = Mathf.MoveTowards(downK, Downed ? 1f : 0f, dt * (Downed ? 2.5f : 1.1f));
        ReviveK = Mathf.MoveTowards(ReviveK, Reviving ? 1f : 0f, dt * 3f);
        if (model != null)
        {
            float rise = Downed ? 0f : Mathf.Sin(downK * Mathf.PI) * 12f;   // al incorporarse se apoya hacia delante
            model.localRotation = Quaternion.Euler(Mathf.Lerp(0f, -82f, downK) + rise + ReviveK * 24f, 0f, 0f);
            model.localPosition = new Vector3(0f, Mathf.Lerp(0f, 0.16f, downK), Mathf.Lerp(0f, -0.35f, downK));
        }
    }
    // v7.9.2: sentado mirando al frente del coche y con la cabeza bajo el techo, en cualquier plaza
    private Vector3 modelBaseScale; private bool seatScaled; private Transform seatHips;
    public bool SeatScaled { get { return seatScaled; } }
    private void SetSeatScale(bool on)
    {
        if (model == null || on == seatScaled) return;
        if (modelBaseScale == Vector3.zero) modelBaseScale = model.localScale;
        seatScaled = on; model.localScale = on ? modelBaseScale * SeatedLegs.SeatScale : modelBaseScale;
    }
    private void SeatPose(DesertCar car)
    {
        if (model == null || Downed) return;
        transform.rotation = Quaternion.LookRotation(car.transform.forward, car.transform.up);
        SetSeatScale(true);
        SeatedLegs.Apply(model, transform);
        if (seatHips == null) seatHips = Find(model, "Hips");
        SeatedLegs.SitOnSeat(seatHips, transform.position, car.transform.up, model);
    }

    private void UpdateLamp(float pitch, float dt)
    {
        if (lamp == null) return;
        lampPitch = Mathf.LerpAngle(lampPitch, pitch, 1f - Mathf.Exp(-14f * dt));
        bool on = FlashOn && !Downed;
        lamp.localRotation = Quaternion.Euler(lampPitch, 0f, 0f);
        float yAdj = Downed ? 0.35f : (Crouching ? 0.85f : 1.22f);
        lamp.localPosition = Vector3.Lerp(lamp.localPosition, new Vector3(0.24f, yAdj, 0.32f), 1f - Mathf.Exp(-10f * dt));
        if (IsMimic) { if (!lensOn && on) { SetLens(true); lensOn = true; } return; }   // la lente brilla pero no ilumina
        if (lampLight != null && lampLight.enabled != on) { lampLight.enabled = on; SetLens(on); }
    }

    private void Footsteps(float dt)
    {
        if (IsMimic) return;   // el imitador no hace ruido al pisar
        Rustle(dt);
        Vector3 d = transform.position - lastPos; d.y = 0f;
        lastPos = transform.position;
        if (Downed || FlatSpeed < 0.3f) return;
        stepAcc += d.magnitude;
        float stepLen = Sprinting ? 2.2f : (Crouching ? 1.2f : 1.7f);
        if (stepAcc < stepLen) return;
        stepAcc = 0f;
        StepsPlayed++;
        WaterVolume wv = WaterVolume.Active;
        float depth = wv != null ? wv.DepthAt(transform.position) : 0f;
        bool inWater = wv != null && depth > 0.03f;
        float vol = (Sprinting ? 0.6f : (Crouching ? 0.22f : 0.42f)) * AudioMix.Sfx * AudioMix.StepMaster;
        stepSrc.pitch = Random.Range(0.88f, 1.1f) * (Crouching ? 1.1f : 1f);
        AudioClip sc = stepClip;
        AudioClip real = StepBank.Next(stepKind, ref lastTake);   // v7.10.3: pasos reales también para los compañeros
        if (real != null) { sc = real; stepSrc.pitch = StepBank.Pitch() * (Crouching ? 1.06f : 1f); vol *= StepBank.Gain(); }
        if (stepKind == StepKind.Grass && GrassAudio.OnGrass(transform.position))
        {
            AudioClip g = GrassAudio.RandomStep(ref lastGrass);
            if (g != null) { sc = g; GrassStepsPlayed++; }
        }
        if (sc != null) stepSrc.PlayOneShot(sc, vol * (inWater ? 0.5f : 1f));
        if (inWater && splashClip != null)
        {
            stepSrc.pitch = Random.Range(0.9f, 1.25f);
            stepSrc.PlayOneShot(splashClip, Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(depth / 0.5f)) * vol * 1.4f);
            wv.Splash(transform.position + transform.forward * 0.25f, Mathf.Clamp01(0.25f + depth * 0.5f), false);
        }
    }

    private bool lensOn;
    private static readonly string Glyphs = "#%&@$?!/\\|_";
    public int TagGlitches { get; private set; }
    private void UpdateTag()
    {
        if (tagRoot == null) return;
        if (IsMimic && !ShowTag) { tagRoot.gameObject.SetActive(false); return; }
        Camera cam = Camera.main;
        if (cam != null)
        {
            Vector3 toCam = cam.transform.position - tagRoot.position;
            float dist = toCam.magnitude;
            tagRoot.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
            tagRoot.localScale = Vector3.one * (0.0045f * Mathf.Clamp(0.55f + dist * 0.12f, 0.8f, 2.2f));
            tagRoot.gameObject.SetActive(dist < 40f);
        }
        string t = playerName;
        if (IsMimic)
        {
            // etiqueta que falla: letras cambiadas, parpadeo y algún desplazamiento
            float g = Mathf.PerlinNoise(Time.time * 2.3f, playerId * 0.7f);
            if (g > 0.62f && t.Length > 0)
            {
                char[] cs = t.ToCharArray(); int k = (int)(Time.time * 13f) % cs.Length;
                cs[k] = Glyphs[(int)(Time.time * 31f) % Glyphs.Length]; t = new string(cs); TagGlitches++;
            }
            tagText.enabled = Mathf.PerlinNoise(Time.time * 6f, 3.1f) > 0.18f;
            tagRoot.localPosition = new Vector3(g > 0.75f ? 0.06f : 0f, 2.4f, 0f);
        }
        if (Reviving) t += "\n<color=#9dff9d>reanimando…</color>";
        if (Downed) t += "\n<color=#ff6a5a>DERRIBADO" + (Revive01 > 0.01f ? " · reanimando " + Mathf.RoundToInt(Revive01 * 100f) + "%" : "") + "</color>";
        if (tagText.text != t) { tagText.supportRichText = true; tagText.text = t; }
    }

    private void OnEnable() { if (!IsMimic && !All.Contains(this)) All.Add(this); }
    private void OnDisable() { All.Remove(this); }

    private void OnDestroy()
    {
        for (int i = 0; i < owned.Count; i++) if (owned[i] != null) Destroy(owned[i]);
    }
}
