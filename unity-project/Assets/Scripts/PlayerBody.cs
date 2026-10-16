using System.Collections.Generic;
using UnityEngine;

// Cuerpo del jugador en primera persona: traje hazmat amarillo (Quaternius "Character Hazmat", CC0).
//  - Se ve al mirar hacia abajo (pecho, piernas, botas) y el brazo derecho con la linterna delante de la cámara.
//  - La cabeza no se ve (solo proyecta sombra) para que no tape la cámara.
//  - Animación: Idle / Walk / Run / Duck, con la cadencia de las zancadas ligada a la distancia recorrida (pasos sincronizados).
//  - Brazo derecho por IK de dos huesos hacia un punto fijo respecto a la cámara => sigue la inclinación y el balanceo.
//  - Linterna visible en la mano, alineada con el haz real (que sale de la cámara).
//  - En agua el traje se ve mojado (brillo, más oscuro) y los pasos levantan ondas en cada pie.
//  - Capa propia "PlayerBody" (8), sin colliders => los rayos de visión del monstruo no lo detectan; la linterna lo ignora (cullingMask).
public class PlayerBody : MonoBehaviour
{
    public const int Layer = 8;
    public const string LayerName = "PlayerBody";
    public const string PrefabResource = "Player/PlayerHazmat";
    public static int LayerMaskBit { get { return 1 << Layer; } }

    public FirstPersonController fpc;
    public Transform cam;
    public Flashlight flashlight;

    public bool Loaded { get; private set; }
    public bool Visible { get; private set; }
    public Animator animator;
    public string CurrentState { get; private set; }
    public float Wet01 { get; private set; }
    public bool IkActive { get; private set; }
    public Transform Head { get { return head; } }
    public Transform RightHand { get { return hand; } }
    public Vector3 HandTarget { get; private set; }
    public GameObject FlashlightProp { get { return prop; } }
    public int RendererCount { get; private set; }
    public int ShadowOnlyCount { get; private set; }
    public float ModelScale { get { return model != null ? model.localScale.y : 0f; } }

    public Vector3 handLocal = new Vector3(0.30f, -0.29f, 0.50f);   // posición de la mano respecto a la cámara
    public float backOffset = 0.17f;                                // el cuerpo queda algo por detrás de la cámara (anti-clipping)

    private Transform model;
    private Transform head, neck, torso, upper, lower, hand, footL, footR, mid2, mid3;
    private float upperLen, lowerLen;
    private Vector3 lowerBaseScale = Vector3.one;
    private readonly List<Renderer> renderers = new List<Renderer>();
    private readonly List<Material> mats = new List<Material>();
    private readonly List<Color> matBaseColor = new List<Color>();
    private readonly List<float> matBaseGloss = new List<float>();
    private readonly List<Object> owned = new List<Object>();
    private GameObject prop;
    private Material lensMat;
    private readonly int hIdle = Animator.StringToHash("Idle"), hWalk = Animator.StringToHash("Walk"), hRun = Animator.StringToHash("Run"), hDuck = Animator.StringToHash("Duck");
    private float walkLen = 0.8f, runLen = 0.6f;
    private bool showWanted = true;
    private bool footFlip;
    private float pitchSmooth, leanSmooth;
    private Vector3 handSmooth;
    private bool handInit;

    // ------------------------------------------------------------------
    public static PlayerBody Create(FirstPersonController fpc, Camera cam, Flashlight fl)
    {
        if (fpc == null || cam == null) return null;
        GameObject go = new GameObject("PlayerBody");
        go.transform.SetParent(fpc.transform, false);
        go.layer = Layer;
        PlayerBody pb = go.AddComponent<PlayerBody>();
        pb.fpc = fpc; pb.cam = cam.transform; pb.flashlight = fl;
        pb.Build();
        return pb;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform r = Find(root.GetChild(i), name);
            if (r != null) return r;
        }
        return null;
    }

    private void SetLayerRec(Transform t)
    {
        t.gameObject.layer = Layer;
        for (int i = 0; i < t.childCount; i++) SetLayerRec(t.GetChild(i));
    }

    private void Build()
    {
        GameObject prefab = Resources.Load<GameObject>(PrefabResource);
        if (prefab == null)
        {
            Debug.LogWarning("[PlayerBody] No se encuentra Resources/" + PrefabResource);
            return;
        }
        GameObject inst = Instantiate(prefab);
        inst.name = "Model";
        model = inst.transform;
        model.SetParent(transform, false);
        SetLayerRec(model);
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
        head = Find(model, "Head"); neck = Find(model, "Neck"); torso = Find(model, "Torso");
        upper = Find(model, "UpperArm.R"); lower = Find(model, "LowerArm.R"); hand = Find(model, "Middle1.R");
        if (hand == null) hand = Find(model, "Index1.R");
        footL = Find(model, "Foot.L"); footR = Find(model, "Foot.R");
        mid2 = Find(model, "Middle2.R"); mid3 = Find(model, "Middle3.R");

        // escala: la cabeza queda a la altura de los ojos del jugador
        if (head != null)
        {
            float h = transform.InverseTransformPoint(head.position).y;
            if (h > 0.2f)
            {
                float s = (fpc.standEyeHeight * 1.0f) / h;
                model.localScale = Vector3.one * s;
                if (animator != null) animator.Update(0f);
            }
        }
        if (upper != null && lower != null && hand != null)
        {
            upperLen = Vector3.Distance(upper.position, lower.position);
            lowerLen = Vector3.Distance(lower.position, hand.position);
            lowerBaseScale = lower.localScale;
        }

        // renderers: sombras, la cabeza solo proyecta sombra
        SkinnedMeshRenderer[] smr = inst.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        foreach (SkinnedMeshRenderer r in smr)
        {
            r.updateWhenOffscreen = true;
            r.receiveShadows = true;
            bool isHead = r.name.ToLowerInvariant().Contains("head");
            r.shadowCastingMode = isHead ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            if (isHead) ShadowOnlyCount++;
            renderers.Add(r);
            Material[] ms = r.materials;      // instancias propias (mojado)
            for (int i = 0; i < ms.Length; i++)
            {
                mats.Add(ms[i]); owned.Add(ms[i]);
                matBaseColor.Add(ms[i].HasProperty("_Color") ? ms[i].color : Color.white);
                matBaseGloss.Add(ms[i].HasProperty("_Glossiness") ? ms[i].GetFloat("_Glossiness") : 0.3f);
            }
        }
        RendererCount = renderers.Count;

        BuildFlashlightProp();
        // la linterna no ilumina ni proyecta sombra desde/sobre el cuerpo
        ExcludeFromFlashlight();
        if (fpc != null) fpc.Footstep += OnFootstep;
        Loaded = true;
        Visible = true;
        GameSettings.Changed += OnSettings;
        OnSettings();
        PlaceBody();
    }

    private void ExcludeFromFlashlight()
    {
        if (flashlight != null && flashlight.spot != null) flashlight.spot.cullingMask = ~LayerMaskBit;
    }

    private Material NewMat(Color c, float gloss, Color emission)
    {
        Shader sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Legacy Shaders/Diffuse");
        Material m = new Material(sh);
        m.color = c;
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", gloss);
        if (emission.maxColorComponent > 0.01f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
        }
        owned.Add(m);
        return m;
    }

    private GameObject Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Material m)
    {
        GameObject g = GameObject.CreatePrimitive(t);
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        g.layer = Layer;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos; g.transform.localScale = scale;
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        renderers.Add(r);
        return g;
    }

    // ------------------------------------------------------------------
    // Linterna de mano generada por código: cuerpo torneado (cola, tubo, collar, cabezal), agarre de goma con estrías, banda naranja,
    // reflector plateado, lente emisiva, pulsador y brillo de la lente. Eje Z = dirección del haz; la lente está en z = LensZ.
    // ------------------------------------------------------------------
    public const float LensZ = 0.1125f;
    public Transform LensTransform { get { return lensT; } }
    public Renderer GlowRenderer { get { return glowRenderer; } }
    public float ClickKick { get; private set; }
    public float LensGlow { get; private set; }
    public float ClipPull { get; private set; }
    public bool LightAtLens { get; private set; }
    public Light FillLight { get { return fill; } }
    public Transform SwitchTransform { get { return switchT; } }
    public int PropRendererCount { get { return prop != null ? prop.GetComponentsInChildren<Renderer>(true).Length : 0; } }

    private Transform lensT, switchT;
    private Renderer glowRenderer;
    private Material glowMat;
    private Light fill;
    private AudioSource clickSrc;
    private AudioClip clickClip;
    private float clickT;
    private float lensK;
    private float clipSmooth;
    public float dtOverride;     // pruebas / vistas previas fuera de Play: paso de tiempo fijo
    private float Dt { get { return dtOverride > 0f ? dtOverride : Time.deltaTime; } }
    public float forearmScale = 0.7f;               // el antebrazo/mano del modelo es enorme: se encoge para que quepa en pantalla
    public float propScale = 1.4f;                  // las manos del modelo son muy grandes (estilo cartoon): la linterna se escala acorde
    public float gripZ = -0.035f;                   // punto de la linterna (eje Z local, sin escalar) que queda en el centro del puño
    public Vector3 gripShift = new Vector3(0f, 0f, 0.14f);
    public Vector3 propAim = new Vector3(-6f, -30f, 0f);   // ligera inclinación (arriba/izquierda) para que se vea junto al puño        // ajuste fino del centro del puño (en ejes del haz, metros)
    public float propAxisBlend = 0.75f;             // 0 = eje de agarre de la mano, 1 = exactamente la dirección del haz
    public float GripAngle { get; private set; }    // ángulo entre el eje de agarre y el haz (depuración)

    private static void Strip(List<Vector3> v, List<Vector3> n, List<int> t, List<Vector2> uv, float z0, float r0, float z1, float r1, int seg)
    {
        float dz = z1 - z0, dr = r1 - r0;
        float len = Mathf.Sqrt(dz * dz + dr * dr);
        if (len < 1e-6f) return;
        float nr = dz / len, nz = -dr / len;
        int b = v.Count;
        for (int k = 0; k <= seg; k++)
        {
            float a = (float)k / seg * Mathf.PI * 2f;
            float c = Mathf.Cos(a), sn = Mathf.Sin(a);
            Vector3 nrm = new Vector3(nr * c, nr * sn, nz);
            v.Add(new Vector3(r0 * c, r0 * sn, z0)); n.Add(nrm); uv.Add(new Vector2((float)k / seg, 0f));
            v.Add(new Vector3(r1 * c, r1 * sn, z1)); n.Add(nrm); uv.Add(new Vector2((float)k / seg, 1f));
        }
        for (int k = 0; k < seg; k++)
        {
            int a = b + k * 2;
            // sentido horario visto desde fuera (Unity)
            t.Add(a); t.Add(a + 2); t.Add(a + 1);
            t.Add(a + 2); t.Add(a + 3); t.Add(a + 1);
        }
    }

    // Revoluciona un perfil (pares z, r) alrededor del eje Z
    private Mesh Lathe(float[] prof, int seg, string name)
    {
        List<Vector3> v = new List<Vector3>(); List<Vector3> n = new List<Vector3>(); List<int> t = new List<int>(); List<Vector2> uv = new List<Vector2>();
        for (int i = 0; i + 3 < prof.Length; i += 2) Strip(v, n, t, uv, prof[i], prof[i + 1], prof[i + 2], prof[i + 3], seg);
        Mesh m = new Mesh();
        m.name = name;
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        owned.Add(m);
        return m;
    }

    private GameObject MeshPart(string name, Mesh mesh, Material mat, Transform parent)
    {
        GameObject g = new GameObject(name);
        g.layer = Layer;
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        r.receiveShadows = true;
        renderers.Add(r);
        return g;
    }

    private Material MetalMat(Color c, float metal, float gloss)
    {
        Material m = NewMat(c, gloss, Color.black);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return m;
    }

    private void BuildFlashlightProp()
    {
        prop = new GameObject("FlashlightProp");
        prop.layer = Layer;
        prop.transform.SetParent(hand != null ? hand : transform, false);

        Material metal = MetalMat(new Color(0.34f, 0.35f, 0.38f), 0.6f, 0.55f);
        Material rubber = MetalMat(new Color(0.045f, 0.045f, 0.05f), 0f, 0.2f);
        Material orange = MetalMat(new Color(0.92f, 0.52f, 0.06f), 0f, 0.4f);
        Material silver = MetalMat(new Color(0.8f, 0.8f, 0.82f), 0.9f, 0.85f);
        lensMat = NewMat(new Color(0.88f, 0.92f, 0.95f), 0.95f, new Color(1.9f, 1.7f, 1.25f));

        // cuerpo metálico: tapa trasera, tubo, collar, cabezal acampanado y borde
        Mesh body = Lathe(new float[] {
            -0.118f, 0.000f,  -0.118f, 0.020f,  -0.112f, 0.0255f,  -0.100f, 0.0268f,  -0.100f, 0.0235f,
             0.032f, 0.0235f,  0.032f, 0.0258f,   0.046f, 0.0258f,   0.052f, 0.0300f,   0.076f, 0.0395f,
             0.100f, 0.0440f,  0.100f, 0.0475f,   0.1125f, 0.0475f,  0.1125f, 0.0415f }, 20, "FlashBody");
        MeshPart("Body", body, metal, prop.transform);

        // agarre de goma con estrías
        List<float> gp = new List<float>();
        int ridges = 9; float z0 = -0.088f, z1 = 0.022f;
        gp.Add(z0); gp.Add(0.0235f);
        for (int i = 0; i < ridges; i++)
        {
            float a = z0 + (z1 - z0) * i / ridges, b = z0 + (z1 - z0) * (i + 1) / ridges;
            float m1 = a + (b - a) * 0.25f, m2 = a + (b - a) * 0.7f;
            gp.Add(a); gp.Add(0.0262f); gp.Add(m1); gp.Add(0.0272f); gp.Add(m2); gp.Add(0.0272f); gp.Add(b); gp.Add(0.0262f);
        }
        gp.Add(z1); gp.Add(0.0235f);
        MeshPart("Grip", Lathe(gp.ToArray(), 18, "FlashGrip"), rubber, prop.transform);

        // banda naranja de seguridad
        MeshPart("Band", Lathe(new float[] { -0.099f, 0.0272f, -0.092f, 0.0272f }, 20, "FlashBand"), orange, prop.transform);

        // reflector (cónico, cóncavo) y lente
        MeshPart("Reflector", Lathe(new float[] { 0.1125f, 0.0415f, 0.1125f, 0.0415f, 0.078f, 0.0135f, 0.078f, 0.0f }, 20, "FlashReflector"), silver, prop.transform);
        GameObject lens = MeshPart("Lens", Lathe(new float[] { 0.1105f, 0.0405f, 0.1105f, 0.0f }, 20, "FlashLens"), lensMat, prop.transform);
        lens.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lensT = new GameObject("LensPoint").transform;
        lensT.SetParent(prop.transform, false);
        lensT.localPosition = new Vector3(0f, 0f, LensZ);
        lensT.gameObject.layer = Layer;

        // pulsador (arriba, y = +)
        GameObject sw = Prim(PrimitiveType.Cube, prop.transform, new Vector3(0f, 0.0268f, -0.045f), new Vector3(0.013f, 0.007f, 0.024f), rubber);
        switchT = sw.transform;
        GameObject sw2 = Prim(PrimitiveType.Cube, prop.transform, new Vector3(0f, 0.0245f, -0.045f), new Vector3(0.016f, 0.004f, 0.030f), orange);

        // brillo de la lente (cuadro que mira a la cámara, textura radial)
        Texture2D gt = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        gt.wrapMode = TextureWrapMode.Clamp;
        Color[] gp2 = new Color[64 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = Mathf.Sqrt((x - 31.5f) * (x - 31.5f) + (y - 31.5f) * (y - 31.5f)) / 32f;
                float a = Mathf.Clamp01(1f - d); a = a * a * a;
                gp2[y * 64 + x] = new Color(1f, 0.96f, 0.8f, a);
            }
        gt.SetPixels(gp2); gt.Apply(false, true);
        owned.Add(gt);
        Shader gs = Shader.Find("Hidden/BackroomsParticle");
        if (gs == null) gs = Shader.Find("Sprites/Default");
        glowMat = new Material(gs) { mainTexture = gt };
        owned.Add(glowMat);
        Mesh q = new Mesh(); q.name = "GlowQuad";
        q.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        q.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        q.colors = new[] { Color.white, Color.white, Color.white, Color.white };
        q.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        q.RecalculateBounds();
        owned.Add(q);
        GameObject glow = new GameObject("LensGlow");
        glow.layer = Layer;
        glow.transform.SetParent(prop.transform, false);
        glow.transform.localPosition = new Vector3(0f, 0f, LensZ + 0.004f);
        glow.transform.localScale = Vector3.one * 0.07f;
        glow.AddComponent<MeshFilter>().sharedMesh = q;
        MeshRenderer gr = glow.AddComponent<MeshRenderer>();
        gr.sharedMaterial = glowMat;
        gr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        gr.receiveShadows = false;
        glowRenderer = gr;

        // sonido del clic
        clickClip = ProceduralAudio.CreateUiClick();
        owned.Add(clickClip);
        clickSrc = prop.AddComponent<AudioSource>();
        clickSrc.clip = clickClip; clickSrc.playOnAwake = false; clickSrc.spatialBlend = 0f; clickSrc.volume = 0.45f;

        // luz de relleno solo para el cuerpo/linterna (para que la mano se vea aunque esté oscuro)
        GameObject fl = new GameObject("ViewmodelFill");
        fl.layer = Layer;
        fl.transform.SetParent(cam, false);
        fl.transform.localPosition = new Vector3(0.1f, 0.05f, 0.1f);
        fill = fl.AddComponent<Light>();
        fill.type = LightType.Point; fill.range = 1.8f; fill.intensity = 0.55f; fill.color = new Color(1f, 0.93f, 0.75f);
        fill.shadows = LightShadows.None;
        fill.cullingMask = LayerMaskBit;

        if (flashlight != null) flashlight.Toggled += OnFlashToggled;
        RendererCount = renderers.Count;
    }

    private void OnFlashToggled(bool on)
    {
        clickT = 1f;
        if (clickSrc != null && clickClip != null && Visible)
        {
            clickSrc.pitch = on ? 1.35f : 0.9f;
            clickSrc.PlayOneShot(clickClip, AudioMix.S(0.5f));
        }
    }

    private void OnSettings()
    {
        showWanted = GameSettings.ShowBody;
    }

    private void OnFootstep(float intensity)
    {
        if (!Loaded || !Visible) return;
        footFlip = !footFlip;
        WaterVolume wv = WaterVolume.Active;
        if (wv != null && fpc != null && fpc.WaterDepth > 0.06f)
        {
            Transform f = footFlip ? footL : footR;
            Vector3 p = f != null ? f.position : fpc.transform.position;
            wv.AddRipple(p, 0.35f + 0.4f * Mathf.Clamp01(intensity));
        }
    }

    // ------------------------------------------------------------------
    private void PlaceBody()
    {
        if (fpc == null) return;
        float lean = fpc.Lean;
        float pitch01 = Mathf.Clamp01(fpc.Pitch / 70f);                 // mirando hacia abajo (pitch > 0)
        transform.localPosition = new Vector3(lean * fpc.leanDistance * 0.55f, 0f, -(backOffset + 0.10f * pitch01));
        transform.localRotation = Quaternion.Euler(0f, 0f, -lean * fpc.leanDegrees * 0.35f);
    }

    private void LateUpdate() { Tick(); }

    public void Tick()
    {
        if (!Loaded) return;
        bool want = showWanted;
        if (want != Visible)
        {
            Visible = want;
            if (model != null) model.gameObject.SetActive(Visible);
            if (prop != null) prop.SetActive(Visible);
            if (fill != null) fill.enabled = Visible;
            if (!Visible) ResetLightOrigin();
        }
        if (!Visible || fpc == null || cam == null) return;
        ExcludeFromFlashlight();

        PlaceBody();
        DriveAnimator();
        if (animator != null && animator.isActiveAndEnabled) { /* el Animator ya evaluó este fotograma antes de LateUpdate */ }
        BendTorso();
        UpdateWet();
        SolveArm();
        UpdateProp();
    }

    private void ResetLightOrigin()
    {
        LightAtLens = false;
        if (flashlight != null && flashlight.spot != null) flashlight.spot.transform.localPosition = Vector3.zero;
    }

    private void DriveAnimator()
    {
        if (animator == null) return;
        float v = fpc.FlatSpeed;
        string want;
        if (fpc.IsCrouching) want = "Duck";
        else if (v < 0.2f) want = "Idle";
        else if (fpc.IsSprinting && v > 4.2f) want = "Run";
        else want = "Walk";
        if (want != CurrentState)
        {
            CurrentState = want;
            int h = want == "Duck" ? hDuck : want == "Idle" ? hIdle : want == "Run" ? hRun : hWalk;
            animator.CrossFadeInFixedTime(h, 0.18f);
        }
        // cadencia: ciclo = 2 pasos => frecuencia = velocidad / (2 * distancia de paso)
        float speed = 1f;
        if (CurrentState == "Walk") speed = Mathf.Clamp(v / (2f * fpc.stepDistanceWalk) * walkLen, 0.3f, 2.5f);
        else if (CurrentState == "Run") speed = Mathf.Clamp(v / (2f * fpc.stepDistanceSprint) * runLen, 0.4f, 2.5f);
        else if (CurrentState == "Duck") speed = 0.6f + Mathf.Clamp01(v / fpc.crouchSpeed) * 0.5f;
        animator.speed = speed * (fpc.IsSwimming ? 0.5f : 1f);
    }

    // Al mirar hacia abajo el torso se inclina un poco hacia delante
    private void BendTorso()
    {
        if (torso == null) return;
        pitchSmooth = Mathf.Lerp(pitchSmooth, Mathf.Max(0f, fpc.Pitch), 1f - Mathf.Exp(-10f * Dt));
        float bend = pitchSmooth * 0.28f;
        if (bend > 0.1f) torso.rotation = Quaternion.AngleAxis(bend, transform.right) * torso.rotation;
    }

    private void UpdateWet()
    {
        float dt = Dt;
        bool inWater = WaterVolume.Active != null && fpc.WaterDepth > 0.1f;
        if (inWater) Wet01 = Mathf.MoveTowards(Wet01, 1f, dt * 1.5f);
        else Wet01 = Mathf.MoveTowards(Wet01, 0f, dt * 0.04f);       // se seca despacio
        for (int i = 0; i < mats.Count; i++)
        {
            Material m = mats[i];
            if (m == null) continue;
            Color c = matBaseColor[i];
            m.color = new Color(c.r * Mathf.Lerp(1f, 0.68f, Wet01), c.g * Mathf.Lerp(1f, 0.68f, Wet01), c.b * Mathf.Lerp(1f, 0.7f, Wet01), c.a);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", Mathf.Lerp(matBaseGloss[i], 0.88f, Wet01));
        }
    }

    // IK analítico de dos huesos: hombro -> codo -> mano hacia un punto delante de la cámara
    private void SolveArm()
    {
        IkActive = false;
        if (upper == null || lower == null || hand == null || upperLen < 0.01f || lowerLen < 0.01f) return;
        // antebrazo + mano más pequeños (se reaplica cada fotograma por si la animación escribe la escala)
        lower.localScale = lowerBaseScale * forearmScale;
        lowerLen = Vector3.Distance(lower.position, hand.position);
        float hz = ClipTargetZ();
        Vector3 hl = handLocal;
        hl.z = hz;
        hl.y -= ClipPull * 0.05f;
        hl.x -= ClipPull * 0.06f;
        Vector3 target = cam.TransformPoint(hl);
        // balanceo: la mano sigue con retraso la orientación de la linterna
        if (flashlight != null && flashlight.spot != null)
        {
            Vector3 lag = flashlight.spot.transform.forward - cam.forward;
            target += cam.TransformDirection(new Vector3(lag.x, lag.y, 0f)) * 0.0f + lag * 0.12f;
        }
        if (!handInit) { handSmooth = target; handInit = true; }
        handSmooth = Vector3.Lerp(handSmooth, target, 1f - Mathf.Exp(-26f * Dt));
        target = handSmooth;
        HandTarget = target;

        Vector3 sh = upper.position;
        Vector3 toT = target - sh;
        float dist = Mathf.Clamp(toT.magnitude, 0.05f, (upperLen + lowerLen) * 0.995f);
        Vector3 dir = toT.normalized;
        // polo: codo hacia fuera y hacia abajo
        Vector3 pole = transform.right * 0.7f + Vector3.down * 0.9f - dir * Vector3.Dot(transform.right * 0.7f + Vector3.down * 0.9f, dir);
        pole = pole.sqrMagnitude > 0.0001f ? pole.normalized : Vector3.down;
        float cosA = Mathf.Clamp((upperLen * upperLen + dist * dist - lowerLen * lowerLen) / (2f * upperLen * dist), -1f, 1f);
        float ang = Mathf.Acos(cosA);
        Vector3 elbowPos = sh + (dir * Mathf.Cos(ang) + pole * Mathf.Sin(ang)) * upperLen;

        // rotar hombro para que el codo quede en elbowPos
        Quaternion q1 = Quaternion.FromToRotation(lower.position - sh, elbowPos - sh);
        upper.rotation = q1 * upper.rotation;
        // rotar codo para que la mano llegue al objetivo
        Vector3 wrist = hand.position;
        Quaternion q2 = Quaternion.FromToRotation(wrist - lower.position, target - lower.position);
        lower.rotation = q2 * lower.rotation;
        IkActive = true;
    }

    private void UpdateProp()
    {
        if (prop == null || hand == null) return;
        float dt = Dt;
        Vector3 fwd = cam.forward;
        if (flashlight != null && flashlight.spot != null) fwd = flashlight.spot.transform.forward;
        Vector3 up = cam.up;
        // la linterna cuelga del hueso de la mano: se compensa su escala (el rig viene del FBX con escala x100) y se aplica propScale
        Vector3 ls = hand.lossyScale;
        if (ls.x > 1e-4f && ls.y > 1e-4f && ls.z > 1e-4f) prop.transform.localScale = new Vector3(propScale / ls.x, propScale / ls.y, propScale / ls.z);

        // clic del interruptor: la linterna cabecea un instante y el pulsador se hunde
        clickT = Mathf.MoveTowards(clickT, 0f, dt / 0.16f);
        float kick = Mathf.Sin(clickT * Mathf.PI);
        ClickKick = kick;

        // Agarre: se gira el antebrazo sobre su eje para que los dedos (eje de flexión) abracen la linterna, que apunta hacia el haz
        Vector3 axis = fwd;
        Vector3 center = hand.position;
        if (lower != null && mid2 != null && mid3 != null)
        {
            Vector3 f = (hand.position - lower.position).normalized;
            Vector3 a = Vector3.Cross(mid2.position - hand.position, mid3.position - mid2.position);
            if (a.sqrMagnitude > 1e-8f && f.sqrMagnitude > 0.5f)
            {
                Vector3 aa = Vector3.ProjectOnPlane(a, f);
                Vector3 want = Vector3.ProjectOnPlane(fwd, f);
                if (aa.sqrMagnitude > 1e-8f && want.sqrMagnitude > 1e-6f)
                {
                    aa.Normalize(); want.Normalize();
                    if (Vector3.Dot(aa, want) < 0f) aa = -aa;
                    float ang = Vector3.SignedAngle(aa, want, f);
                    lower.rotation = Quaternion.AngleAxis(ang, f) * lower.rotation;
                    // tras girar, recalcular el eje real de agarre
                    a = Vector3.Cross(mid2.position - hand.position, mid3.position - mid2.position);
                    aa = a.normalized; if (Vector3.Dot(aa, fwd) < 0f) aa = -aa;
                    GripAngle = Vector3.Angle(aa, fwd);
                    axis = Vector3.Slerp(aa, fwd, propAxisBlend).normalized;
                }
            }
            center = (hand.position + mid2.position + mid3.position) / 3f;
        }

        Quaternion rot = Quaternion.LookRotation(axis, up);
        rot = rot * Quaternion.Euler(propAim) * Quaternion.Euler(-10f * kick, 0f, 0f);
        Vector3 pos = center + rot * (gripShift - new Vector3(0f, 0f, gripZ * propScale)) - fwd * 0.02f * kick;
        prop.transform.SetPositionAndRotation(pos, rot);
        if (switchT != null) switchT.localPosition = new Vector3(0f, 0.0268f - 0.004f * kick, -0.045f);

        // luz y brillo de la lente
        bool on = flashlight != null && flashlight.spot != null && flashlight.spot.enabled;
        float inten = on && flashlight.maxIntensity > 0f ? Mathf.Clamp01(flashlight.spot.intensity / Mathf.Max(0.05f, flashlight.NearDim) / flashlight.maxIntensity) : 0f;
        lensK = Mathf.MoveTowards(lensK, inten, dt * 14f);
        LensGlow = lensK;
        if (lensMat != null) lensMat.SetColor("_EmissionColor", new Color(1.9f, 1.7f, 1.25f) * Mathf.Lerp(0.04f, 1f, lensK));
        if (glowRenderer != null)
        {
            glowRenderer.enabled = lensK > 0.02f;
            Transform g = glowRenderer.transform;
            Vector3 toCam = cam.position - g.position;
            if (toCam.sqrMagnitude > 1e-6f) g.rotation = Quaternion.LookRotation(-toCam.normalized, up);
            float sc = 0.075f + 0.012f * Mathf.PerlinNoise(Time.time * 9f, 0.3f);
            g.localScale = Vector3.one * sc * (0.5f + 0.5f * lensK);
            glowMat.color = new Color(1f, 1f, 1f, 0.7f * lensK);
        }
        if (fill != null) fill.intensity = Mathf.Lerp(0.3f, 0.7f, lensK);

        // el haz sale de la lente (si la cámara no está cerca de una pared el desplazamiento es de unos 25 cm a la derecha y abajo)
        if (flashlight != null && flashlight.spot != null && lensT != null)
        {
            flashlight.spot.transform.position = lensT.position;
            LightAtLens = true;
        }
    }

    // Anti-clipping: si hay una pared delante, la mano (y la linterna) retroceden hacia la cámara
    private float ClipTargetZ()
    {
        float z = handLocal.z;
        RaycastHit hit;
        float reach = handLocal.z + 0.7f;
        int mask = ~(LayerMaskBit | (1 << 2));
        float pull = 0f;
        if (Physics.SphereCast(cam.position, 0.07f, cam.forward, out hit, reach, mask, QueryTriggerInteraction.Ignore))
        {
            float maxZ = hit.distance - 0.60f;     // la punta de la linterna sobresale ~0.55 m de la mano
            float nz = Mathf.Clamp(maxZ, 0.10f, handLocal.z);
            pull = Mathf.Clamp01((handLocal.z - nz) / Mathf.Max(0.01f, handLocal.z - 0.10f));
            z = nz;
        }
        clipSmooth = Mathf.Lerp(clipSmooth, pull, 1f - Mathf.Exp(-14f * Dt));
        ClipPull = clipSmooth;
        return z;
    }

    // ------------------------------------------------------------------
    private void OnDestroy()
    {
        GameSettings.Changed -= OnSettings;
        if (flashlight != null) flashlight.Toggled -= OnFlashToggled;
        if (flashlight != null && flashlight.spot != null) flashlight.spot.transform.localPosition = Vector3.zero;
        if (fpc != null) fpc.Footstep -= OnFootstep;
        for (int i = 0; i < owned.Count; i++) ProceduralAssets.SafeDestroy(owned[i]);
        owned.Clear();
    }
}
