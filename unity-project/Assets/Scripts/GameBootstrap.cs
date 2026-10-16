using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Arranque todo-en-uno: añade este script a un objeto vacío de una escena vacía y pulsa Play.
// Construye en tiempo de ejecución, para el nivel actual: atmósfera (niebla/ambiente/viñeta), nivel (generador),
// NavMesh, jugador con cámara y linterna, monstruos, pilas, audio ambiente, HUD y GameManager.
// Los niveles (LevelConfig) se encadenan: llegar a la salida carga el siguiente; R reinicia el nivel actual.
public class GameBootstrap : MonoBehaviour
{
    public static GameBootstrap Instance { get; private set; }

    [Header("Partida")]
    [Tooltip("Nivel con el que empieza la partida (0 = Oficinas amarillas). También: argumento de línea de comandos -level N.")]
    public int startLevel = 0;
    public bool useRandomSeed = true;
    public int seed = 12345;
    public bool spawnMonster = true;
    [Tooltip("Intenta hornear la NavMesh en runtime (requiere com.unity.ai.navigation). Si no, usa el modo alternativo.")]
    public bool buildNavMesh = true;
    [Tooltip("Teclas de depuración (N = saltar de nivel). Desactivadas por defecto.")]
    public bool debugKeys = false;
    [Tooltip("Muestra el menú principal al arrancar. Con el argumento -level N se salta el menú y empieza directamente en ese nivel.")]
    public bool showMenuOnStart = true;

    // Estado público
    public bool IsLoaded { get; private set; }
    public bool InMenu { get; private set; }
    public UIManager UI { get; private set; }
    public VhsOverlay Overlay { get; private set; }
    public BackroomsGenerator MenuGenerator { get; private set; }
    public int CurrentLevel { get; private set; }
    public GameManager Manager { get; private set; }
    public BackroomsGenerator Generator { get; private set; }
    public GameObject WorldRoot { get { return worldRoot; } }
    public float RunElapsed;
    public int Deaths;
    public int LoadCount { get; private set; }

    private GameObject worldRoot;
    private bool loading;
    private NetStart netStart;
    public bool CanLoadNow { get { return !loading; } }
    private readonly List<Object> worldAssets = new List<Object>();

    // Pantalla de carga persistente
    private GameObject loadingCanvas;
    private Text loadingText;

    private void Awake()
    {
        Instance = this;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            int parsed;
            if (args[i] == "-level" && int.TryParse(args[i + 1], out parsed)) { startLevel = parsed; showMenuOnStart = false; }
        }
        for (int i = 0; i < args.Length; i++) if (args[i] == "-nomenu") showMenuOnStart = false;
        startLevel = Mathf.Clamp(startLevel, 0, LevelConfig.Count - 1);
        GameSettings.ApplyAll();
        BuildLoadingScreen();
        NetSession.Create(this);
        BuildPersistentUi();
    }

    // Menús (UIManager) y overlay de camcorder: persisten durante toda la sesión
    private void BuildPersistentUi()
    {
        GameObject ui = new GameObject("UIManager");
        ui.transform.SetParent(transform, false);
        UI = ui.AddComponent<UIManager>();
        UI.Init(this);
        GameObject ov = new GameObject("VhsOverlay");
        ov.transform.SetParent(transform, false);
        Overlay = ov.AddComponent<VhsOverlay>();
    }

    private void Start()
    {
        if (showMenuOnStart)
        {
            if (IntroSequence.ShouldPlay()) IntroSequence.Begin(transform);   // intro de arranque; el menú se carga detrás
            GoToMainMenu();
        }
        else LoadLevel(startLevel);
    }

    // Empieza una partida nueva en el nivel indicado (desde el menú principal / selección de nivel)
    public void StartGame(int level)
    {
        RunElapsed = 0f;
        Deaths = 0;
        Inventory.ResetAll();
        LoadLevel(level);
    }

    // Carga el fondo del menú principal (pasillo en vivo) y muestra el menú
    public void GoToMainMenu()
    {
        if (loading) return;
        StartCoroutine(LoadMenuRoutine());
    }

    private IEnumerator LoadMenuRoutine()
    {
        loading = true;
        IsLoaded = false;
        ShowLoading("BACKROOMS: ESCAPA\n\nCargando...");
        yield return null;
        yield return null;
        TeardownWorld();
        yield return null;
        InMenu = true;
        BuildMenuWorld();
        HideLoading();
        LoadCount++;
        IsLoaded = true;
        loading = false;
        if (UI != null) UI.ShowMainMenu();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Carga un nivel (destruye el mundo actual). Lo llama GameManager.
    public void LoadLevel(int index)
    {
        if (loading) return;
        StartCoroutine(LoadRoutine(Mathf.Clamp(index, 0, LevelConfig.Count - 1)));
    }

    // Multijugador: carga el nivel indicado por el anfitrión (semilla, monstruos extra, huecos de aparición)
    public void LoadLevelNet(NetStart ns)
    {
        if (loading || ns == null) return;
        if (ns.restart && Inventory.Instance != null) Inventory.Instance.RevertToLevelStart();
        if (ns.newRun)
        {
            RunElapsed = 0f; Deaths = 0;
            Inventory.ResetAll();
        }
        if (UI != null) UI.HideAll();
        netStart = ns;
        StartCoroutine(LoadRoutine(Mathf.Clamp(ns.level, 0, LevelConfig.Count - 1)));
    }

    // Tras perder la conexión / salir de la sala: vuelve al menú principal (esperando a que termine una carga en curso)
    public void ReturnToMenuAfterNet()
    {
        StartCoroutine(MenuAfterNetRoutine());
    }

    private IEnumerator MenuAfterNetRoutine()
    {
        while (loading) yield return null;
        netStart = null;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (UI != null) UI.HideAll();
        yield return LoadMenuRoutine();
    }

    // Compatibilidad: reconstruye el nivel actual
    public void Rebuild()
    {
        LoadLevel(CurrentLevel);
    }

    // Vuelve a empezar la partida desde el nivel 0
    public void RestartRun()
    {
        RunElapsed = 0f;
        Deaths = 0;
        Inventory.ResetAll();
        LoadLevel(0);
    }

    // Sólo tras REINICIAR un nivel (un solo uso) se puede saltar la animación del ascensor; en el resto de entradas siempre se reproduce
    public bool NextIntroSkippable;

    private IEnumerator LoadRoutine(int index)
    {
        loading = true;
        IsLoaded = false;
        LevelConfig cfg = LevelConfig.Get(index);
        ShowLoading("NIVEL " + index + "\n" + cfg.name + "\n\nGenerando...");
        yield return null; // deja que se dibuje la pantalla de carga
        yield return null;

        TeardownWorld();
        yield return null; // esperar a que Unity destruya los objetos viejos

        InMenu = false;
        MenuGenerator = null;
        CurrentLevel = index;
        NetStart ns = netStart;
        netStart = null;
        Build(index, ns);
        LoadCount++;
        HideLoading();
        IsLoaded = true;
        loading = false;
    }

    private void TeardownWorld()
    {
        if (worldRoot != null) Destroy(worldRoot);
        worldRoot = null;
        for (int i = 0; i < worldAssets.Count; i++)
        {
            if (worldAssets[i] != null) Destroy(worldAssets[i]);
        }
        worldAssets.Clear();
        BatteryPickup.CollectedThisLevel = 0;
        if (NetSession.Instance != null) NetSession.Instance.OnWorldTeardown();
    }

    // ------------------------------------------------------------------
    // Construcción
    // ------------------------------------------------------------------

    private void Build(int levelIndex, NetStart ns)
    {
        bool netClient = ns != null && NetSession.IsClient;
        LevelConfig cfg = LevelConfig.Get(levelIndex);

        // Si la escena trae su propia cámara principal, se desactiva para no duplicar AudioListener
        Camera existing = Camera.main;
        if (existing != null && existing.GetComponent<AudioListener>() != null && existing.transform.root.name != "BackroomsWorld")
        {
            existing.gameObject.SetActive(false);
        }

        QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
        QualitySettings.pixelLightCount = 8;

        worldRoot = new GameObject("BackroomsWorld");
        ApplyAtmosphere(cfg);

        // 1) Nivel
        GameObject genGo = new GameObject("BackroomsGenerator");
        genGo.transform.SetParent(worldRoot.transform, false);
        BackroomsGenerator generator = genGo.AddComponent<BackroomsGenerator>();
        generator.config = cfg;
        generator.useRandomSeed = ns == null && useRandomSeed;
        generator.seed = ns != null ? ns.seed : seed + levelIndex * 101;
        generator.extraMonsters = ns != null ? ns.extraMonsters : 0;
        generator.Generate();
        Generator = generator;

        // 2) NavMesh (antes de crear jugador y monstruos para que no entren en el horneado)
        bool navBuilt = false;
        if (spawnMonster && buildNavMesh && !netClient)
        {
            navBuilt = MonsterAI.TryBuildRuntimeNavMesh(generator.Root.gameObject);
        }
        Debug.Log("[GameBootstrap] Nivel " + levelIndex + " (" + cfg.name + ") semilla: " + generator.UsedSeed + " | NavMesh en runtime: " + navBuilt);

        // 2b) Ascensor de entrada: cabina en la celda de inicio (mueve el punto de aparición al interior; no toca el laberinto)
        ElevatorCabin cabin = ElevatorCabin.Create(generator, cfg, worldRoot.transform);
        cabin.Skippable = ns == null && NextIntroSkippable;
        NextIntroSkippable = false;

        // 3) Jugador
        GameObject player = BuildPlayer(generator, cfg, ns != null ? ns.SlotOf(NetSession.Instance.LocalId) : 0, out FirstPersonController fpc, out Flashlight flashlight, out Camera cam);
        if (generator.Culler != null) generator.Culler.viewer = cam.transform;

        // 4) Monstruos
        List<MonsterAI> monsters = new List<MonsterAI>();
        if (spawnMonster)
        {
            int mcount = Mathf.Max(1, cfg.monsterCount) + (ns != null ? ns.extraMonsters : 0);
            for (int i = 0; i < generator.MonsterSpawns.Count && i < mcount; i++)
            {
                monsters.Add(BuildMonster(generator, cfg, player.transform, generator.MonsterSpawns[i], i, netClient));
            }
        }

        // 5) Pilas
        BuildBatteries(generator, cfg, player.transform, flashlight, ns != null);
        BuildExtraPickups(generator, cfg, ns != null);

        // 6) UI + GameManager
        GameObject gmGo = new GameObject("GameManager");
        gmGo.transform.SetParent(worldRoot.transform, false);
        GameManager gm = gmGo.AddComponent<GameManager>();
        gm.player = fpc;
        gm.flashlight = flashlight;
        gm.exit = generator.ExitTransform;
        gm.monsters = monsters.ToArray();
        gm.monster = monsters.Count > 0 ? monsters[0] : null;
        gm.playerCamera = cam.transform;
        gm.generator = generator;
        gm.bootstrap = this;
        gm.levelIndex = levelIndex;
        gm.config = cfg;
        BuildHud(gmGo.transform, gm);
        Manager = gm;

        // 7) Ascensor: el jugador queda bloqueado y los monstruos quietos hasta que se abren las puertas
        cabin.Bind(fpc, cam.transform, gm);
        if (ns == null)
        {
            MonsterAI[] held = monsters.ToArray();
            foreach (MonsterAI m in held) if (m != null) m.Stop();
            cabin.DoorsOpened = () => { foreach (MonsterAI m in held) if (m != null) m.Resume(); };
            cabin.Begin();
        }
        else
        {
            cabin.DoorsOpened = () => { if (NetSession.Instance != null) NetSession.Instance.OnElevatorOpened(); };
        }
        if (ns != null && NetSession.Instance != null) NetSession.Instance.OnWorldBuilt(gm, generator, player, fpc, monsters.ToArray(), cfg);
    }

    // Fondo del menú principal: nivel 0 con semilla fija, cámara recorriendo los pasillos (con efecto VHS y ambiente)
    private void BuildMenuWorld()
    {
        LevelConfig cfg = LevelConfig.Get(0);
        Camera existing = Camera.main;
        if (existing != null && existing.GetComponent<AudioListener>() != null && existing.transform.root.name != "BackroomsWorld")
            existing.gameObject.SetActive(false);

        worldRoot = new GameObject("BackroomsWorld");
        ApplyAtmosphere(cfg);

        GameObject genGo = new GameObject("BackroomsGenerator");
        genGo.transform.SetParent(worldRoot.transform, false);
        BackroomsGenerator generator = genGo.AddComponent<BackroomsGenerator>();
        generator.config = cfg;
        generator.useRandomSeed = false;
        generator.seed = 4242;
        generator.Generate();
        Generator = generator;
        MenuGenerator = generator;

        GameObject camGo = new GameObject("MenuCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(worldRoot.transform, false);
        Camera cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 68f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 80f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = cfg.fog;
        cam.allowHDR = true;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<VhsEffect>();
        MenuCameraRig rig = camGo.AddComponent<MenuCameraRig>();
        rig.Setup(generator);
        if (generator.Culler != null) generator.Culler.viewer = camGo.transform;

        AmbienceAudio amb = worldRoot.AddComponent<AmbienceAudio>();
        amb.Setup(cfg, camGo.transform);
        Manager = null;
    }

    private void ApplyAtmosphere(LevelConfig cfg)
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = cfg.ambient;
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Skybox;
        RenderSettings.reflectionIntensity = cfg.reflectionIntensity;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = cfg.fog;
        RenderSettings.fogDensity = cfg.fogDensity;
        Shader.SetGlobalFloat("_GradeAmt", cfg.grade);
        PostStack.LevelActive = cfg.index == 2;   // v6.9: post-procesado tipo POOLS en las Poolrooms
        Shader.SetGlobalFloat("_GradeWarm", 1f);
        Shader.SetGlobalFloat("_GradeContrast", 1.4f);
        Shader.SetGlobalFloat("_GradeExposure", 0.85f);
    }

    // ------------------------------------------------------------------
    // Jugador
    // ------------------------------------------------------------------

    private GameObject BuildPlayer(BackroomsGenerator generator, LevelConfig cfg, int slot, out FirstPersonController fpc,
        out Flashlight flashlight, out Camera cam)
    {
        GameObject player = new GameObject("Player");
        player.transform.SetParent(worldRoot.transform, false);
        // multijugador: cada jugador aparece en un hueco distinto alrededor del punto de inicio (hueco 0 = el punto exacto)
        Vector3 spawnOff = slot <= 0 ? Vector3.zero : Quaternion.Euler(0f, generator.PlayerStartYaw, 0f) * new Vector3(slot == 3 ? 0f : (slot == 2 ? -0.62f : 0.62f), 0f, slot == 3 ? -0.7f : 0f);
        player.transform.position = generator.PlayerStart + spawnOff;
        player.transform.rotation = Quaternion.Euler(0f, generator.PlayerStartYaw, 0f);

        CharacterController cc = player.AddComponent<CharacterController>();
        cc.height = 1.8f;
        cc.radius = 0.35f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.stepOffset = 0.3f;
        cc.slopeLimit = 45f;

        // Cámara a la altura de los ojos
        GameObject camGo = new GameObject("PlayerCamera");
        camGo.tag = "MainCamera";
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 70f;
        cam.nearClipPlane = 0.08f;
        cam.farClipPlane = 120f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = cfg.fog;
        cam.allowHDR = true;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<VhsEffect>();

        // Linterna (Spot Light en la propia cámara)
        flashlight = camGo.AddComponent<Flashlight>();
        flashlight.drainPerSecond = cfg.flashlightDrain;
        flashlight.rechargePerSecond = cfg.flashlightRecharge;
        flashlight.maxIntensity = cfg.flashlightIntensity;

        fpc = player.AddComponent<FirstPersonController>();
        fpc.cameraPivot = camGo.transform;
        if (cfg.water && WaterVolume.Active != null)
        {
            WaterFx wfx = camGo.AddComponent<WaterFx>();
            wfx.fpc = fpc; wfx.flashlight = flashlight; wfx.cam = cam;
        }
        fpc.speedFactor = cfg.playerSpeedFactor;

        // Cuerpo en primera persona (traje hazmat); opción "Mostrar cuerpo"
        PlayerBody.Create(fpc, cam, flashlight);

        // Cordura + inventario (objetos y notas recogidos; se conservan entre niveles)
        Sanity sanity = player.AddComponent<Sanity>();
        sanity.flashlight = flashlight;
        sanity.fpc = fpc;
        sanity.vhs = cam.GetComponent<VhsEffect>();
        Inventory inv = player.AddComponent<Inventory>();
        inv.flashlight = flashlight;
        inv.fpc = fpc;
        inv.sanity = sanity;
        inv.cam = cam;
        inv.batteryAmount = cfg.batteryAmount;
        HotbarHud.Create(inv);

        // Audio: pasos y ambiente
        FootstepAudio steps = player.AddComponent<FootstepAudio>();
        steps.Setup(cfg.step);
        AmbienceAudio amb = worldRoot.AddComponent<AmbienceAudio>();
        amb.Setup(cfg, player.transform);
        return player;
    }

    // ------------------------------------------------------------------
    // Pilas recogibles
    // ------------------------------------------------------------------

    private void BuildBatteries(BackroomsGenerator generator, LevelConfig cfg, Transform player, Flashlight flashlight, bool net)
    {
        if (generator.BatterySpots == null) return;
        Material body = ProceduralAssets.CreateMaterial(new Color(0.08f, 0.08f, 0.09f), null, Vector2.one, 0.5f);
        Material cap = ProceduralAssets.CreateMaterial(new Color(0.75f, 0.55f, 0.2f), null, Vector2.one, 0.7f);
        Material band = ProceduralAssets.CreateMaterial(new Color(0.1f, 0.3f, 0.1f), null, Vector2.one, 0.4f);
        ProceduralAssets.SetEmission(band, new Color(0.15f, 0.8f, 0.25f) * 0.9f);
        worldAssets.Add(body); worldAssets.Add(cap); worldAssets.Add(band);
        AudioClip clip = ProceduralAudio.CreatePickup();
        worldAssets.Add(clip);

        Transform root = new GameObject("Batteries").transform;
        root.SetParent(worldRoot.transform, false);
        for (int i = 0; i < generator.BatterySpots.Count; i++)
        {
            GameObject b = new GameObject("Battery");
            b.transform.SetParent(root, false);
            b.transform.position = generator.BatterySpots[i] + Vector3.up * 0.12f;
            b.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Body", b.transform, Vector3.zero, new Vector3(0.11f, 0.09f, 0.11f), body, false);
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Band", b.transform, new Vector3(0f, 0.02f, 0f), new Vector3(0.115f, 0.025f, 0.115f), band, false);
            ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Cap", b.transform, new Vector3(0f, 0.1f, 0f), new Vector3(0.05f, 0.015f, 0.05f), cap, false);
            BatteryPickup pk = b.AddComponent<BatteryPickup>();
            pk.target = player;
            pk.flashlight = flashlight;
            pk.amount = cfg.batteryAmount;
            pk.pickupClip = clip;
            if (net) { pk.netId = i; NetSession.RegisterPickup(i, pk); }
        }
    }

    // Objetos extra por nivel: 3 notas, 2 aguas de almendras, 3 barras luminosas y 1 cinta de vídeo
    private static readonly ItemType[] ExtraOrder =
    {
        ItemType.None, ItemType.None, ItemType.None,
        ItemType.AlmondWater, ItemType.AlmondWater,
        ItemType.Glowstick, ItemType.Glowstick, ItemType.Glowstick,
        ItemType.VhsTape,
    };

    private void BuildExtraPickups(BackroomsGenerator generator, LevelConfig cfg, bool net)
    {
        if (generator.ExtraSpots == null) return;
        Transform root = new GameObject("ExtraPickups").transform;
        root.SetParent(worldRoot.transform, false);
        int noteK = 0;
        for (int i = 0; i < generator.ExtraSpots.Count && i < ExtraOrder.Length; i++)
        {
            Vector3 p = generator.ExtraSpots[i];
            p.y -= 0.1f;                                   // los puntos vienen a ~10 cm del suelo
            if (cfg.water) p.y += Mathf.Max(0f, cfg.waterLevel - 0.05f);   // sobre el agua
            ItemType t = ExtraOrder[i];
            if (t == ItemType.None)
            {
                // las notas que ya tengo no se vuelven a colocar
                int id = LoreNotes.IdFor(cfg.index, noteK++);
                ItemPickup np = ItemPickup.Spawn(ItemType.None, p, id, root, 0f);
                if (net) { np.netId = 1000 + i; NetSession.RegisterPickup(np.netId, np); }
            }
            else
            {
                ItemPickup ip = ItemPickup.Spawn(t, p, -1, root, 0f);
                if (net) { ip.netId = 1000 + i; NetSession.RegisterPickup(ip.netId, ip); }
            }
        }
    }

    // ------------------------------------------------------------------
    // Monstruo: modelo 3D del nivel (Resources/Monsters/<id>) + IA; si el modelo falla, figura procedural
    // ------------------------------------------------------------------

    private MonsterAI BuildMonster(BackroomsGenerator generator, LevelConfig cfg, Transform playerTransform, Vector3 spawn, int index, bool proxy)
    {
        GameObject m = new GameObject("Monster" + (index > 0 ? "_" + index : string.Empty));
        m.transform.SetParent(worldRoot.transform, false);
        m.transform.position = spawn;

        MonsterProfile profile = MonsterProfile.ById(cfg.monsterModel) ?? MonsterProfile.ForLevel(cfg.index);

        MonsterAI ai = m.AddComponent<MonsterAI>();
        ai.netProxy = proxy;
        ai.patrolSpeed = cfg.patrolSpeed;
        ai.chaseSpeed = cfg.chaseSpeed;
        ai.sightRange = cfg.sightRange;
        ai.fieldOfView = cfg.fieldOfView;
        ai.darkSightFactor = cfg.darkSightFactor;
        ai.hearingScale = cfg.hearingScale;
        ai.loseSightTime = cfg.loseSightTime;
        ai.killDistance = Mathf.Max(cfg.killDistance, profile.killDistance);
        ai.eyeHeight = profile.ai_eyeHeight;
        ai.visual = MonsterVisual.Create(m, profile, playerTransform);
        ai.Setup(playerTransform, generator);
        return ai;
    }

    // ------------------------------------------------------------------
    // Pantalla de carga persistente
    // ------------------------------------------------------------------

    private void BuildLoadingScreen()
    {
        loadingCanvas = new GameObject("LoadingScreen");
        loadingCanvas.transform.SetParent(transform, false);
        Canvas canvas = loadingCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        Image bg = CreateImage(loadingCanvas.transform, "Bg", Color.black);
        Stretch(bg.rectTransform);
        loadingText = CreateText(loadingCanvas.transform, "Text", string.Empty, 44, TextAnchor.MiddleCenter);
        Stretch(loadingText.rectTransform);
        loadingCanvas.SetActive(false);
    }

    private void ShowLoading(string text)
    {
        if (loadingCanvas == null) return;
        loadingText.text = text;
        loadingCanvas.SetActive(true);
    }

    private void HideLoading()
    {
        if (loadingCanvas != null) loadingCanvas.SetActive(false);
    }

    // ------------------------------------------------------------------
    // HUD con UnityEngine.UI (legacy)
    // ------------------------------------------------------------------

    private void BuildHud(Transform parent, GameManager gm)
    {
        GameObject canvasGo = new GameObject("HUD");
        canvasGo.transform.SetParent(parent, false);
        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Viñeta: textura radial generada por código (bordes oscuros)
        RawImage vignette = CreateVignette(canvasGo.transform);
        Stretch(vignette.rectTransform);

        // Capa de color a pantalla completa (se tiñe al ganar/perder)
        Image overlay = CreateImage(canvasGo.transform, "Overlay", new Color(0f, 0f, 0f, 0f));
        Stretch(overlay.rectTransform);

        // Texto central
        Text message = CreateText(canvasGo.transform, "Message", string.Empty, 52, TextAnchor.MiddleCenter);
        message.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        message.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        message.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        message.rectTransform.sizeDelta = new Vector2(1500f, 400f);
        message.rectTransform.anchoredPosition = Vector2.zero;

        // Tarjeta de título del nivel
        Text title = CreateText(canvasGo.transform, "LevelTitle", string.Empty, 70, TextAnchor.MiddleCenter);
        title.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        title.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        title.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(1700f, 160f);
        title.rectTransform.anchoredPosition = new Vector2(0f, 80f);
        Text subtitle = CreateText(canvasGo.transform, "LevelSubtitle", string.Empty, 30, TextAnchor.UpperCenter);
        subtitle.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        subtitle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        subtitle.rectTransform.pivot = new Vector2(0.5f, 1f);
        subtitle.rectTransform.sizeDelta = new Vector2(1500f, 120f);
        subtitle.rectTransform.anchoredPosition = new Vector2(0f, 0f);

        // Aviso breve ("+ Pila")
        Text toast = CreateText(canvasGo.transform, "Toast", string.Empty, 32, TextAnchor.MiddleCenter);
        toast.rectTransform.anchorMin = new Vector2(0.5f, 0f);
        toast.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        toast.rectTransform.pivot = new Vector2(0.5f, 0f);
        toast.rectTransform.sizeDelta = new Vector2(800f, 60f);
        toast.rectTransform.anchoredPosition = new Vector2(0f, 150f);

        // Texto de información arriba a la izquierda
        Text info = CreateText(canvasGo.transform, "Info", string.Empty, 22, TextAnchor.UpperLeft);
        info.rectTransform.anchorMin = new Vector2(0f, 1f);
        info.rectTransform.anchorMax = new Vector2(0f, 1f);
        info.rectTransform.pivot = new Vector2(0f, 1f);
        info.rectTransform.sizeDelta = new Vector2(1800f, 110f);
        info.rectTransform.anchoredPosition = new Vector2(24f, -20f);

        // Barras de resistencia y batería abajo a la izquierda
        gm.staminaFill = CreateBar(canvasGo.transform, "Stamina", new Vector2(24f, 24f), new Color(0.9f, 0.85f, 0.3f), "RESISTENCIA");
        gm.batteryFill = CreateBar(canvasGo.transform, "Battery", new Vector2(24f, 64f), new Color(0.4f, 0.8f, 1f), "BATERÍA");

        // Fundido a negro (transiciones entre niveles) — por encima de todo
        Image fade = CreateImage(canvasGo.transform, "Fade", new Color(0f, 0f, 0f, 1f));
        Stretch(fade.rectTransform);

        gm.overlay = overlay;
        gm.messageText = message;
        gm.infoText = info;
        gm.titleText = title;
        gm.subtitleText = subtitle;
        gm.toastText = toast;
        gm.fadeImage = fade;
    }

    private RawImage CreateVignette(Transform parent)
    {
        const int size = 256;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.name = "VignetteTex";
        Color32[] px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) / 1.4142f;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, r)) * 0.78f;
                px[y * size + x] = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(a * 255f));
            }
        }
        tex.SetPixels32(px);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply(false, true);
        worldAssets.Add(tex);

        GameObject go = new GameObject("Vignette", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RawImage img = go.AddComponent<RawImage>();
        img.texture = tex;
        img.raycastTarget = false;
        return img;
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Image img = go.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    private static Text CreateText(Transform parent, string name, string content, int size, TextAnchor alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Text t = go.AddComponent<Text>();
        t.font = ProceduralAssets.GetDefaultFont();
        t.fontSize = size;
        t.alignment = alignment;
        t.color = new Color(0.95f, 0.92f, 0.75f);
        t.text = content;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    // Barra con fondo oscuro; devuelve el RectTransform del relleno (GameManager cambia su anchorMax.x)
    private static RectTransform CreateBar(Transform parent, string name, Vector2 position, Color fillColor, string label)
    {
        Image back = CreateImage(parent, name + "Back", new Color(0f, 0f, 0f, 0.55f));
        RectTransform br = back.rectTransform;
        br.anchorMin = Vector2.zero;
        br.anchorMax = Vector2.zero;
        br.pivot = Vector2.zero;
        br.sizeDelta = new Vector2(260f, 16f);
        br.anchoredPosition = position;

        Image fill = CreateImage(back.transform, name + "Fill", fillColor);
        RectTransform fr = fill.rectTransform;
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = Vector2.one;
        fr.offsetMin = Vector2.zero;
        fr.offsetMax = Vector2.zero;

        Text text = CreateText(back.transform, name + "Label", label, 16, TextAnchor.MiddleLeft);
        RectTransform tr = text.rectTransform;
        tr.anchorMin = new Vector2(1f, 0.5f);
        tr.anchorMax = new Vector2(1f, 0.5f);
        tr.pivot = new Vector2(0f, 0.5f);
        tr.sizeDelta = new Vector2(200f, 24f);
        tr.anchoredPosition = new Vector2(10f, 0f);

        return fr;
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
