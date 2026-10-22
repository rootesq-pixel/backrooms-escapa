using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

// Ascensor de entrada: CADA nivel empieza dentro de una cabina de ascensor que baja a toda velocidad (temblor, retumbo, contador de
// plantas descendiendo, parpadeo del fluorescente, frenazo con sacudida, «ding») y luego abre las puertas al nivel.
//
//  · La cabina se coloca en la celda de inicio, con la puerta hacia la salida abierta de esa celda: no cambia el laberinto
//    (generación determinista idéntica en todos los clientes); sólo mueve el punto de aparición al interior de la cabina.
//  · Modelo: «Elevator car interior» + «Elevator door pair» (Backrooms and Liminal Spaces Kit, 3D Assets, CC0), convertidos a FBX.
//    El suelo, los fijos de luz, el indicador de planta, las luces de botones, el audio y las colisiones son procedurales.
//    Si el modelo no cargase, se usa una cabina procedural equivalente.
//  · Durante el descenso el jugador no puede moverse (FirstPersonController.InputEnabled = false). Se libera al abrirse las puertas.
//    Siempre se reproduce (no es opcional). Sólo se puede saltar tras REINICIAR un nivel después de morir (Espacio / E).
//  · Los monstruos esperan quietos hasta que se abren las puertas. En multijugador el anfitrión elige el instante (mensaje
//    «Descend»): todos animan el descenso en local a la vez y nadie sale hasta que se abren las puertas.
//  · Tras salir el jugador las puertas se cierran: la cabina se queda atrás, a oscuras, con colisión.
public class ElevatorCabin : MonoBehaviour
{
    public enum Phase { Waiting, Descending, Braking, Opening, Open, Closing, Closed }

    public static ElevatorCabin Current { get; private set; }
    public static bool InstantForTests;                       // sólo pruebas: la cabina aparece ya abierta y sin bloqueo
    public static bool PlayerLocked { get { return Current != null && Current.Locked; } }

    public const string ModelResource = "Elevator/Elevator";
    public const float ScaleX = 1.3f, ScaleZ = 1.3f;          // el modelo original (2×2 m) se ensancha para que quepan 4 jugadores
    // Línea de tiempo (s) desde el inicio del descenso
    public const float TStart = 0.8f, TCruise = 1.8f, TBrake = 5.2f, TStop = 6.3f, TDing = 6.5f, TDoors0 = 6.8f, TDoors1 = 8.0f, TUnlock = 7.4f;
    public const float TotalSeconds = TDoors1;

    private static readonly Color[] Tints =
    {
        new Color(1.00f, 0.94f, 0.72f),   // 0 oficinas: blanco cálido
        new Color(0.80f, 0.95f, 0.86f),   // 1 aparcamiento: verdoso
        new Color(0.60f, 0.90f, 1.00f),   // 2 Poolrooms: cian
        new Color(1.00f, 0.68f, 0.32f),   // 3 hotel: ámbar
        new Color(1.00f, 0.14f, 0.07f),   // 4 industrial: luz de emergencia roja
        new Color(0.92f, 0.96f, 1.00f),   // 5 campo infinito: luz de día fría
        new Color(1.00f, 0.90f, 0.78f)    // 6 parque de bolas: fluorescente cálido y desvaído
    };

    public Phase CurrentPhase { get; private set; }
    public bool Begun { get; private set; }
    public bool Locked { get; private set; }
    public bool Skippable;
    public bool Skipped { get; private set; }
    public bool HasModel { get; private set; }
    public bool Released { get; private set; }
    public float T { get; private set; }
    public float Open01 { get; private set; }
    public int LevelIndex { get; private set; }
    public Color Tint { get; private set; }
    public string DisplayNumber { get; private set; }
    public string DisplayLabel { get; private set; }
    public Vector3 InteriorCenter { get; private set; }
    public Vector3 ExitPoint { get; private set; }            // punto del suelo justo delante de la puerta (fuera de la cabina)
    public Vector3 Forward { get; private set; }
    public Action DoorsOpened;                                // se invoca una vez al abrirse las puertas (libera jugador y monstruos)
    public bool CollidersBlockDoor { get { return doorBlock != null && doorBlock.enabled; } }
    public float JoltCount;                                   // sacudidas aplicadas (pruebas)
    public float MaxShake;                                    // mayor desplazamiento de cámara aplicado (pruebas)
    public int FloorStart, FloorEnd;

    private BackroomsGenerator gen;
    private LevelConfig cfg;
    private FirstPersonController fpc;
    private Transform pivot;
    private GameManager gm;
    private Vector3 pivotBasePos;
    private Quaternion pivotBaseRot;
    private bool pivotCaptured;
    private bool frozen;                                       // vista previa: el tiempo no avanza
    private float closeT, farTimer, nextFarCheck, fadeOut = 1f, waitTimer;
    private bool jolted, dinged, doorSfx, brakeSfx, closeSfx;

    private Transform model, fixtures, doorL, doorR, pivL, pivR;
    private BoxCollider doorBlock;
    private Light cabinLight;
    private float lightBase = 1.5f;
    private Material panelMat, buttonMat;
    private Renderer panelRenderer;
    private readonly List<Renderer> buttonDots = new List<Renderer>();
    private Text numText, labelText;
    private Image arrowImg;
    private Canvas displayCanvas;
    private AudioSource rumbleSrc, machineSrc, airSrc, oneSrc;
    private AudioClip cRumble, cMachine, cAir, cJolt, cDing, cDoor, cThud;
    private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    private GameObject skipHint;
    private float noiseSeed;

    // ------------------------------------------------------------------ construcción
    public static ElevatorCabin Create(BackroomsGenerator generator, LevelConfig config, Transform parent)
    {
        GameObject go = new GameObject("ElevatorCabin");
        go.transform.SetParent(parent, false);
        ElevatorCabin c = go.AddComponent<ElevatorCabin>();
        c.Setup(generator, config);
        return c;
    }

    private Material Mat(Color c, Texture2D tex, float smooth, float metal)
    {
        Material m = ProceduralAssets.CreateMaterial(c, tex, Vector2.one, smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        owned.Add(m);
        return m;
    }

    private static Texture2D Tex(string n) { return Resources.Load<Texture2D>("Elevator/" + n); }

    private void Setup(BackroomsGenerator generator, LevelConfig config)
    {
        gen = generator; cfg = config;
        LevelIndex = Mathf.Clamp(config.index, 0, Tints.Length - 1);
        Tint = Tints[LevelIndex];
        noiseSeed = 17f + LevelIndex * 3.7f;
        FloorStart = 0; FloorEnd = -(24 + 12 * LevelIndex);

        // posición: la cara frontal queda alineada con el borde abierto de la celda de inicio
        float yaw = generator.PlayerStartYaw;
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
        Forward = rot * Vector3.forward;
        Vector3 cell = generator.CellCenterWorld(generator.StartCell.x, generator.StartCell.y);
        float frontExtent = 1.07f * ScaleZ;
        Vector3 pos = cell + Forward * (config.cellSize * 0.5f - frontExtent - 0.03f);
        pos.y = cell.y - 0.12f;                                // el suelo de goma queda 2 cm sobre el suelo del nivel
        transform.SetPositionAndRotation(pos, rot);

        BuildModel();
        if (config.layout == LayoutKind.Field) BuildFieldShell(frontExtent);
        BuildColliders(config.layout == LayoutKind.Field ? 0f : config.cellSize, frontExtent);
        BuildFixtures();
        BuildAudio();

        InteriorCenter = transform.TransformPoint(new Vector3(0f, 0.14f, 0.1f * ScaleZ));
        ExitPoint = transform.TransformPoint(new Vector3(0f, 0.14f, frontExtent + 0.9f));
        generator.OverridePlayerStart(InteriorCenter);

        DisplayNumber = "--"; DisplayLabel = "NIVEL " + LevelIndex;
        CurrentPhase = Phase.Waiting;
        ApplyDoors(0f);
        Current = this;
        enabled = true;
    }

    private void BuildModel()
    {
        GameObject prefab = Resources.Load<GameObject>(ModelResource);
        GameObject m = new GameObject("Model");
        m.transform.SetParent(transform, false);
        m.transform.localScale = new Vector3(ScaleX, 1f, ScaleZ);
        model = m.transform;
        Texture2D tConcrete = Tex("Elev_concrete"), tWood = Tex("Elev_wood"), tPaint = Tex("Elev_painted");
        Material metal = Mat(new Color(0.46f, 0.50f, 0.52f), null, 0.55f, 0.45f);
        Material dark = Mat(new Color(0.085f, 0.10f, 0.11f), null, 0.35f, 0f);
        Material rubber = Mat(new Color(0.05f, 0.055f, 0.05f), null, 0.2f, 0f);
        Material concrete = Mat(new Color(0.8f, 0.8f, 0.8f), tConcrete, 0.1f, 0f);
        Material plaster = Mat(new Color(0.78f, 0.77f, 0.72f), tConcrete, 0.1f, 0f);
        Material wood = Mat(new Color(0.8f, 0.8f, 0.8f), tWood, 0.3f, 0f);
        Material paint = Mat(new Color(0.8f, 0.8f, 0.8f), tPaint, 0.3f, 0f);
        buttonMat = Mat(new Color(0.82f, 0.85f, 0.80f), null, 0.5f, 0f);

        if (prefab != null)
        {
            GameObject inst = Instantiate(prefab, m.transform, false);
            inst.name = "Elevator";
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            HasModel = true;
            foreach (Renderer r in inst.GetComponentsInChildren<Renderer>(true))
            {
                string n = r.name;
                Material[] src = r.sharedMaterials;
                Material[] dst = new Material[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    string mn = src[i] != null ? src[i].name.ToLowerInvariant() : "";
                    if (n == "Car_Rubber") dst[i] = rubber;
                    else if (n == "Car_Slab") dst[i] = concrete;
                    else if (n == "Car_Metal") dst[i] = metal;
                    else if (n == "Car_Wood") dst[i] = wood;
                    else if (n == "Car_Ceiling") dst[i] = plaster;
                    else if (n == "Car_Panel") dst[i] = dark;
                    else if (n == "Car_Buttons") dst[i] = buttonMat;
                    else if (n == "Door_Lintel") dst[i] = paint;
                    else dst[i] = mn.Contains("dark") ? dark : metal;       // hojas de las puertas: metal + junta oscura
                }
                r.sharedMaterials = dst;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            doorL = inst.transform.Find("Door_L"); doorR = inst.transform.Find("Door_R");
        }
        if (doorL == null || doorR == null)
        {
            // cabina procedural equivalente (sin modelo)
            HasModel = false;
            foreach (Transform ch in new List<Transform>(m.GetComponentsInChildren<Transform>())) if (ch != m.transform) Destroy(ch.gameObject);
            Box("Car_Floor", new Vector3(0f, 0.07f, 0f), new Vector3(2f, 0.14f, 2.02f), rubber, m.transform);
            Box("Car_WallL", new Vector3(-0.9f, 1.17f, 0f), new Vector3(0.2f, 2.34f, 2.02f), metal, m.transform);
            Box("Car_WallR", new Vector3(0.9f, 1.17f, 0f), new Vector3(0.2f, 2.34f, 2.02f), metal, m.transform);
            Box("Car_WallB", new Vector3(0f, 1.17f, -0.9f), new Vector3(2f, 2.34f, 0.2f), metal, m.transform);
            Box("Car_Ceiling", new Vector3(0f, 2.31f, 0f), new Vector3(2f, 0.07f, 2f), plaster, m.transform);
            Box("Car_Rail", new Vector3(0f, 1.1f, -0.74f), new Vector3(1.5f, 0.04f, 0.04f), metal, m.transform);
            Box("Car_Panel", new Vector3(0.73f, 1.225f, -0.77f), new Vector3(0.12f, 0.65f, 0.03f), dark, m.transform);
            doorL = Box("Door_L", new Vector3(-0.48f, 1.05f, 1.04f), new Vector3(0.95f, 2.1f, 0.046f), metal, m.transform).transform;
            doorR = Box("Door_R", new Vector3(0.48f, 1.05f, 1.04f), new Vector3(0.95f, 2.1f, 0.046f), metal, m.transform).transform;
            Box("Door_Lintel", new Vector3(0f, 2.14f, 1.04f), new Vector3(2f, 0.08f, 0.12f), paint, m.transform);
        }
        // cada hoja cuelga de un pivote anclado en su borde exterior; al abrir, el pivote se encoge en X (la hoja se «retrae» a su lado)
        pivL = MakePivot(doorL); pivR = MakePivot(doorR);
    }

    private Transform MakePivot(Transform leaf)
    {
        Renderer r = leaf.GetComponent<Renderer>();
        Transform parent = leaf.parent;
        float outer = 0f;
        if (r != null)
        {
            Vector3 p1 = parent.InverseTransformPoint(r.bounds.min), p2 = parent.InverseTransformPoint(r.bounds.max);
            float mn = Mathf.Min(p1.x, p2.x), mx = Mathf.Max(p1.x, p2.x);
            outer = Mathf.Abs(mx) > Mathf.Abs(mn) ? mx : mn;
        }
        GameObject pv = new GameObject("DoorPivot_" + leaf.name);
        pv.transform.SetParent(parent, false);
        pv.transform.localPosition = new Vector3(outer, 0f, 0f);
        leaf.SetParent(pv.transform, true);
        return pv.transform;
    }

    private GameObject Box(string name, Vector3 p, Vector3 s, Material mat, Transform parent)
    {
        return ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, name, parent, p, s, mat, false);
    }

    private void AddCol(string name, Vector3 center, Vector3 size, bool enabled)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(transform, false);
        BoxCollider b = g.AddComponent<BoxCollider>();
        b.center = center; b.size = size; b.enabled = enabled;
        if (name == "DoorBlock") doorBlock = b;
    }

    private void BuildColliders(float cellSize, float frontExtent)
    {
        float sx = ScaleX, sz = ScaleZ;
        AddCol("Floor", new Vector3(0f, 0.07f, 0f), new Vector3(2.0f * sx, 0.14f, 2.02f * sz), true);
        AddCol("WallL", new Vector3(-0.9f * sx, 1.17f, 0f), new Vector3(0.2f * sx, 2.34f, 2.02f * sz), true);
        AddCol("WallR", new Vector3(0.9f * sx, 1.17f, 0f), new Vector3(0.2f * sx, 2.34f, 2.02f * sz), true);
        AddCol("WallB", new Vector3(0f, 1.17f, -0.9f * sz), new Vector3(2.0f * sx, 2.34f, 0.2f * sz), true);
        AddCol("Ceiling", new Vector3(0f, 2.31f, 0f), new Vector3(2.0f * sx, 0.1f, 2.02f * sz), true);
        AddCol("Header", new Vector3(0f, 2.26f, 1.04f * sz), new Vector3(2.0f * sx, 0.16f, 0.12f), true);
        AddCol("DoorBlock", new Vector3(0f, 1.09f, 1.04f * sz), new Vector3(1.9f * sx, 2.18f, 0.12f), true);
        // rellenos laterales: tapan el hueco entre la cabina y las paredes de la celda
        float half = cellSize * 0.5f + wallPad;
        float zBack = -1.01f * sz, zFront = frontExtent;
        float fx = Mathf.Max(0.05f, half - 1.0f * sx);
        float cx = 1.0f * sx + fx * 0.5f;
        if (cellSize > 0f)   // v7.0: en el campo no hay paredes de celda que rellenar (la cabina está sola en la hierba)
        {
            AddCol("FillL", new Vector3(-cx, 1.4f, (zBack + zFront) * 0.5f), new Vector3(fx, 2.8f, zFront - zBack), true);
            AddCol("FillR", new Vector3(cx, 1.4f, (zBack + zFront) * 0.5f), new Vector3(fx, 2.8f, zFront - zBack), true);
        }
        else
        {
            AddCol("ShellL", new Vector3(-1.12f * sx, 1.5f, (zBack + zFront) * 0.5f), new Vector3(0.24f, 3.0f, zFront - zBack + 0.3f), true);
            AddCol("ShellR", new Vector3(1.12f * sx, 1.5f, (zBack + zFront) * 0.5f), new Vector3(0.24f, 3.0f, zFront - zBack + 0.3f), true);
            AddCol("ShellB", new Vector3(0f, 1.5f, zBack - 0.12f), new Vector3(2.48f * sx, 3.0f, 0.24f), true);
        }
        // el laberinto no debe atravesar la cabina
        GameObject nb = new GameObject("NavBlock");
        nb.transform.SetParent(transform, false);
        NavMeshObstacle ob = nb.AddComponent<NavMeshObstacle>();
        ob.shape = NavMeshObstacleShape.Box;
        ob.center = new Vector3(0f, 1.2f, (zBack + zFront) * 0.5f);
        ob.size = new Vector3(2.2f * sx, 2.4f, zFront - zBack);
        ob.carving = true;
    }
    private const float wallPad = 0.1f;

    // v7.0 Campo infinito: caja exterior de hormigón viejo alrededor de la cabina (en los demás niveles la tapan las paredes)
    private void BuildFieldShell(float frontExtent)
    {
        float sx = ScaleX, sz = ScaleZ;
        Material m = Mat(new Color(0.86f, 0.85f, 0.82f), Resources.Load<Texture2D>("Field/House/hw2_a"), 0.08f, 0f);   // v7.2.0: yeso gastado CC0 como las casas
        m.mainTextureScale = new Vector2(1.7f, 1.7f);
        Texture2D n = Resources.Load<Texture2D>("Field/House/hw2_n");
        if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); }
        Material roof = Mat(new Color(0.3f, 0.3f, 0.3f), null, 0.2f, 0.3f);
        float zBack = -1.01f * sz, zFront = frontExtent + 0.02f, len = zFront - zBack + 0.3f, zc = (zBack + zFront) * 0.5f - 0.12f;
        GameObject shell = new GameObject("FieldShell");
        shell.transform.SetParent(transform, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ShellL", shell.transform, new Vector3(-1.12f * sx, 1.45f, zc), new Vector3(0.24f, 3.2f, len), m, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ShellR", shell.transform, new Vector3(1.12f * sx, 1.45f, zc), new Vector3(0.24f, 3.2f, len), m, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ShellB", shell.transform, new Vector3(0f, 1.45f, zBack - 0.15f), new Vector3(2.48f * sx, 3.2f, 0.24f), m, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ShellTop", shell.transform, new Vector3(0f, 3.1f, zc), new Vector3(2.5f * sx, 0.24f, len), roof, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ShellHeader", shell.transform, new Vector3(0f, 2.65f, zFront + 0.02f), new Vector3(2.48f * sx, 0.7f, 0.1f), m, false);
        ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ShellBase", shell.transform, new Vector3(0f, -0.08f, zc), new Vector3(2.6f * sx, 0.3f, len + 0.2f), m, false);
    }

    private void BuildFixtures()
    {
        GameObject fx = new GameObject("Fixtures");
        fx.transform.SetParent(transform, false);
        fixtures = fx.transform;
        float sx = ScaleX, sz = ScaleZ;
        Material metal = Mat(new Color(0.40f, 0.43f, 0.45f), null, 0.5f, 0.4f);
        Material blackM = Mat(new Color(0.02f, 0.02f, 0.02f), null, 0.3f, 0f);
        // dintel superior (tapa el hueco entre la puerta y el techo) y visera del indicador
        Box("HeaderFill", new Vector3(0f, 2.26f, 1.04f * sz), new Vector3(2.0f * sx, 0.16f, 0.12f), metal, fx.transform);
        Box("DisplaySoffit", new Vector3(0f, 2.14f, 1.04f * sz - 0.12f), new Vector3(1.22f, 0.30f, 0.2f), blackM, fx.transform);

        // panel luminoso del techo
        panelMat = Mat(new Color(0.9f, 0.9f, 0.9f), null, 0.2f, 0f);
        panelMat.EnableKeyword("_EMISSION");
        GameObject panel = Box("CeilingPanel", new Vector3(0f, 2.265f, 0.1f * sz), new Vector3(1.3f, 0.025f, 1.0f * sz), panelMat, fx.transform);
        panelRenderer = panel.GetComponent<Renderer>();

        // luz de la cabina
        GameObject lg = new GameObject("CabinLight");
        lg.transform.SetParent(fx.transform, false);
        lg.transform.localPosition = new Vector3(0f, 2.05f, 0.1f * sz);
        cabinLight = lg.AddComponent<Light>();
        lightBase = LevelIndex == 4 ? 1.9f : 1.45f;
        cabinLight.type = LightType.Point; cabinLight.range = 4.6f; cabinLight.intensity = lightBase; cabinLight.color = Tint; cabinLight.shadows = LightShadows.None;

        BuildDisplay(fx.transform, sx, sz);

        // luces de los botones (sobre el panel del fondo)
        Renderer btn = null;
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true)) if (r.name == "Car_Buttons") btn = r;
        Vector3 bc = btn != null ? btn.bounds.center : transform.TransformPoint(new Vector3(0.73f * sx, 1.2f, -0.74f * sz));
        float yMin = btn != null ? btn.bounds.min.y : 0.95f, yMax = btn != null ? btn.bounds.max.y : 1.45f;
        Vector3 inward = transform.TransformDirection(new Vector3(0f, 0f, 1f));
        for (int i = 0; i < 5; i++)
        {
            Material dm = Mat(new Color(0.1f, 0.1f, 0.1f), null, 0.4f, 0f);
            dm.EnableKeyword("_EMISSION");
            GameObject dot = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "ButtonLight" + i, fx.transform, Vector3.zero, new Vector3(0.035f, 0.035f, 0.012f), dm, false);
            Vector3 wp = new Vector3(bc.x, Mathf.Lerp(yMin + 0.03f, yMax - 0.03f, i / 4f), bc.z) + inward * 0.012f;
            dot.transform.position = wp; dot.transform.rotation = transform.rotation;
            buttonDots.Add(dot.GetComponent<Renderer>());
        }
    }

    private void BuildDisplay(Transform parent, float sx, float sz)
    {
        GameObject cg = new GameObject("FloorDisplay", typeof(RectTransform));
        cg.transform.SetParent(parent, false);
        cg.transform.localPosition = new Vector3(0f, 2.14f, 1.04f * sz - 0.225f);
        displayCanvas = cg.AddComponent<Canvas>();
        displayCanvas.renderMode = RenderMode.WorldSpace;
        RectTransform rt = (RectTransform)cg.transform;
        rt.sizeDelta = new Vector2(480f, 120f);
        rt.localScale = new Vector3(0.0025f, 0.0025f, 0.0025f);
        Image bg = UiStyle.Rect(cg.transform, "Bg", 0f, 0f, 480f, 120f, new Color(0.01f, 0.01f, 0.01f, 1f));
        UiStyle.Rect(cg.transform, "Frame", 0f, 0f, 470f, 110f, new Color(0.04f, 0.04f, 0.04f, 1f));
        // flecha ▼ (sprite de triángulo procedural)
        GameObject ag = new GameObject("Arrow", typeof(RectTransform));
        ag.transform.SetParent(cg.transform, false);
        arrowImg = ag.AddComponent<Image>();
        Texture2D tt = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
        {
            float half = y / 31f * 15.5f + 0.5f;                           // triángulo con la punta abajo (y=0 es la fila inferior)
            bool inside = Mathf.Abs(x - 15.5f) <= half && y >= 2 && y <= 29;
            tt.SetPixel(x, y, inside ? Color.white : new Color(1f, 1f, 1f, 0f));
        }
        tt.Apply(); tt.filterMode = FilterMode.Bilinear;
        owned.Add(tt);
        Sprite sp = Sprite.Create(tt, new Rect(0, 0, 32, 32), new Vector2(0.5f, 0.5f), 32f);
        owned.Add(sp);
        arrowImg.sprite = sp; arrowImg.raycastTarget = false;
        RectTransform ar = arrowImg.rectTransform;
        ar.anchorMin = ar.anchorMax = new Vector2(0.5f, 0.5f); ar.sizeDelta = new Vector2(62f, 62f); ar.anchoredPosition = new Vector2(-190f, 0f);
        numText = MakeText(cg.transform, "Number", new Vector2(-30f, 0f), new Vector2(240f, 110f), 92, TextAnchor.MiddleCenter);
        labelText = MakeText(cg.transform, "Label", new Vector2(150f, 0f), new Vector2(170f, 100f), 30, TextAnchor.MiddleCenter);
    }

    private Text MakeText(Transform parent, string name, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        Text t = g.AddComponent<Text>();
        t.font = UiStyle.Mono(); t.fontSize = fontSize; t.alignment = anchor; t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.color = Tint; t.text = "";
        RectTransform r = t.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = size; r.anchoredPosition = pos;
        return t;
    }

    private AudioSource MakeSrc(string n, AudioClip clip, bool loop)
    {
        AudioSource s = gameObject.AddComponent<AudioSource>();
        s.clip = clip; s.loop = loop; s.playOnAwake = false; s.spatialBlend = 0f; s.volume = 0f;
        return s;
    }

    private void BuildAudio()
    {
        cRumble = ElevatorAudio.CreateRumble(); cMachine = ElevatorAudio.CreateMachinery(); cAir = ElevatorAudio.CreateAir();
        cJolt = ElevatorAudio.CreateJolt(); cDing = ElevatorAudio.CreateDing(); cDoor = ElevatorAudio.CreateDoorSlide(); cThud = ElevatorAudio.CreateThud();
        owned.Add(cRumble); owned.Add(cMachine); owned.Add(cAir); owned.Add(cJolt); owned.Add(cDing); owned.Add(cDoor); owned.Add(cThud);
        rumbleSrc = MakeSrc("Rumble", cRumble, true);
        machineSrc = MakeSrc("Machinery", cMachine, true);
        airSrc = MakeSrc("Air", cAir, true);
        oneSrc = MakeSrc("OneShot", null, false);
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        for (int i = 0; i < owned.Count; i++) if (owned[i] != null) ProceduralAssets.SafeDestroy(owned[i]);
        owned.Clear();
        if (skipHint != null) Destroy(skipHint);
        // el jugador nunca debe quedarse bloqueado por una cabina destruida
        if (Locked && fpc != null) fpc.InputEnabled = true;
    }

    // ------------------------------------------------------------------ enlace con el jugador
    // Enlaza jugador, cámara y GameManager. El jugador queda bloqueado (y el título retenido) hasta abrirse las puertas.
    public void Bind(FirstPersonController player, Transform cameraPivot, GameManager manager)
    {
        fpc = player; pivot = cameraPivot; gm = manager;
        if (pivot != null) { pivotBasePos = pivot.localPosition; pivotBaseRot = pivot.localRotation; pivotCaptured = true; }
        if (!InstantForTests)
        {
            Locked = true;
            if (fpc != null) fpc.InputEnabled = false;
            if (gm != null) gm.HoldTitle(true);
        }
        if (Skippable && !InstantForTests) BuildSkipHint();
    }

    private void BuildSkipHint()
    {
        skipHint = new GameObject("ElevatorSkipHint", typeof(RectTransform));
        Canvas c = skipHint.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 40;
        Text t = MakeText(skipHint.transform, "Hint", new Vector2(0f, 46f), new Vector2(700f, 40f), 22, TextAnchor.MiddleCenter);
        RectTransform r = t.rectTransform; r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f); r.anchoredPosition = new Vector2(0f, 46f);
        t.color = new Color(0.95f, 0.91f, 0.7f, 0.85f);
        t.text = "[ESPACIO] Saltar el ascensor";
    }

    // Empieza el descenso. En un reinicio tras morir (skippable) se puede saltar con Espacio / E.
    public void Begin()
    {
        if (Begun) return;
        Begun = true;
        if (InstantForTests) { FinishInstant(); return; }
        T = 0f;
        CurrentPhase = Phase.Descending;
        if (gm != null) gm.HoldTitle(true);
        if (VhsEffect.Active != null) VhsEffect.Active.Spike(0.25f);
    }

    private void FinishInstant()
    {
        T = TDoors1; Open01 = 1f; ApplyDoors(1f);
        CurrentPhase = Phase.Open;
        DisplayNumber = FloorEnd.ToString(); DisplayLabel = "NIVEL " + LevelIndex;
        Release();
    }

    // Salta la animación (sólo si Skippable)
    public void Skip()
    {
        if (!Skippable || Released) return;
        Skipped = true;
        Begun = true;
        if (rumbleSrc != null) { rumbleSrc.Stop(); machineSrc.Stop(); airSrc.Stop(); }
        T = TDoors1; Open01 = 1f; ApplyDoors(1f);
        CurrentPhase = Phase.Open;
        DisplayNumber = FloorEnd.ToString();
        Release();
    }

    private void Release()
    {
        if (Released) return;
        Released = true;
        Locked = false;
        if (fpc != null) fpc.InputEnabled = true;
        if (gm != null) gm.ShowTitle();
        if (skipHint != null) skipHint.SetActive(false);
        if (DoorsOpened != null) DoorsOpened();
        if (doorBlock != null) doorBlock.enabled = false;
    }

    // ------------------------------------------------------------------ bucle
    public static float Speed01(float t)
    {
        if (t < TStart) return 0f;
        if (t < TCruise) return (t - TStart) / (TCruise - TStart);
        if (t < TBrake) return 1f;
        if (t < TStop) return 1f - (t - TBrake) / (TStop - TBrake);
        return 0f;
    }

    // distancia recorrida normalizada 0..1 (integral del perfil de velocidad)
    public static float Dist01(float t)
    {
        float a = 0.5f * (TCruise - TStart), b = (TBrake - TCruise), c = 0.5f * (TStop - TBrake), tot = a + b + c;
        float d;
        if (t <= TStart) d = 0f;
        else if (t < TCruise) { float u = t - TStart; d = 0.5f * u * u / (TCruise - TStart); }
        else if (t < TBrake) d = a + (t - TCruise);
        else if (t < TStop) { float u = t - TBrake; float len = TStop - TBrake; d = a + b + u - 0.5f * u * u / len; }
        else d = tot;
        return d / tot;
    }

    private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

    private void Update()
    {
        if (frozen) return;
        float dt = Time.deltaTime;
        if (Locked && !Released && Skippable && Begun && !UIManager.InputBlocked && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.E)))
        {
            Skip();
        }
        if (!Begun)
        {
            // multijugador: esperando la orden del anfitrión (si no llega en 40 s se baja igualmente)
            waitTimer += Time.unscaledDeltaTime;
            if (waitTimer > 40f && NetSession.Active) Begin();
        }
        else if (T < TDoors1 && CurrentPhase != Phase.Closing && CurrentPhase != Phase.Closed)
        {
            T += dt;
        }
        Evaluate(dt);
    }

    // Aplica el estado de la línea de tiempo T (visuales y sonidos)
    private void Evaluate(float dt)
    {
        if (CurrentPhase == Phase.Closed) { SetAudioIdle(); ApplyLightAndPanel(0f); return; }

        if (CurrentPhase == Phase.Descending || CurrentPhase == Phase.Braking || (CurrentPhase == Phase.Opening))
        {
            CurrentPhase = T < TBrake ? Phase.Descending : (T < TStop ? Phase.Braking : (T < TDoors1 ? Phase.Opening : Phase.Open));
            if (T >= TUnlock && !Released) Release();                 // las puertas ya están medio abiertas: el jugador sale
        }

        float s = Speed01(T);
        float open = Mathf.Clamp01((T - TDoors0) / (TDoors1 - TDoors0));
        if (CurrentPhase == Phase.Closing)
        {
            closeT += dt;
            open = 1f - Smooth(closeT / 1.1f);
            if (!closeSfx && closeT > 0.75f) { closeSfx = true; PlayOne(cThud, ElevatorAudio.ThudGain); }
            if (closeT >= 1.1f) { open = 0f; CurrentPhase = Phase.Closed; fadeOut = 1f; }
            Open01 = open; ApplyDoors(open);
        }
        else
        {
            Open01 = Smooth(open);
            ApplyDoors(Open01);
        }

        // contador de plantas
        if (Begun)
        {
            if (T < TStop)
            {
                int f = Mathf.RoundToInt(Mathf.Lerp(FloorStart, FloorEnd, Dist01(T)));
                DisplayNumber = f.ToString(); DisplayLabel = T < TStart ? "ESPERE" : "BAJANDO";
            }
            else { DisplayNumber = FloorEnd.ToString(); DisplayLabel = "NIVEL " + LevelIndex; }
        }
        else { DisplayNumber = "--"; DisplayLabel = "ESPERE"; }
        UpdateDisplay();

        // sonidos
        if (Begun && CurrentPhase != Phase.Closed && CurrentPhase != Phase.Closing)
        {
            if (!brakeSfx && T >= TStop - ElevatorAudio.JoltImpactAt) { brakeSfx = true; PlayOne(cJolt, ElevatorAudio.JoltGain); }
            if (!dinged && T >= TDing) { dinged = true; PlayOne(cDing, ElevatorAudio.DingGain); }
            if (!doorSfx && T >= TDoors0) { doorSfx = true; PlayOne(cDoor, ElevatorAudio.DoorGain); }
            if (!jolted && T >= TStop)
            {
                jolted = true; JoltCount++;
                if (VhsEffect.Active != null) VhsEffect.Active.Spike(0.7f);
            }
        }
        UpdateLoops(s);
        UpdateLight();
    }

    private void UpdateLoops(float s)
    {
        if (rumbleSrc == null) return;
        bool run = Begun && !Released && T < TStop + 0.9f && CurrentPhase != Phase.Closed;
        if (Skipped) run = false;
        float fade = T > TStop ? Mathf.Clamp01(1f - (T - TStop) / 0.9f) : 1f;
        float sv = Mathf.Max(s, T > TStop ? 0f : 0f);
        if (run)
        {
            if (!rumbleSrc.isPlaying) { rumbleSrc.Play(); machineSrc.Play(); airSrc.Play(); }
            rumbleSrc.volume = AudioMix.S(ElevatorAudio.RumbleGain * (0.12f + 0.88f * sv) * fade);
            rumbleSrc.pitch = 0.82f + 0.38f * sv;
            machineSrc.volume = AudioMix.S(ElevatorAudio.MachineGain * Mathf.Pow(sv, 0.8f) * fade);
            machineSrc.pitch = 0.85f + 0.5f * sv;
            airSrc.volume = AudioMix.S(ElevatorAudio.AirGain * sv * fade);
            airSrc.pitch = 0.9f + 0.3f * sv;
        }
        else SetAudioIdle();
    }

    private void SetAudioIdle()
    {
        if (rumbleSrc == null) return;
        if (rumbleSrc.isPlaying) { rumbleSrc.Stop(); machineSrc.Stop(); airSrc.Stop(); }
        rumbleSrc.volume = machineSrc.volume = airSrc.volume = 0f;
    }

    private void PlayOne(AudioClip c, float gain)
    {
        if (oneSrc == null || c == null) return;
        oneSrc.PlayOneShot(c, AudioMix.S(gain));
    }

    private float Perlin(float x, float y) { return Mathf.PerlinNoise(x + noiseSeed, y + noiseSeed * 0.5f); }

    // brillo relativo de la luz de la cabina según la fase
    private float LightFactor()
    {
        float t = T;
        float f;
        if (!Begun) f = 0.5f + 0.06f * Mathf.Sin(Time.time * 7f);
        else if (CurrentPhase == Phase.Closing || CurrentPhase == Phase.Closed) f = Mathf.Clamp01(1f - closeT / 2.2f) * 0.8f;
        else
        {
            float n = Perlin(t * 13f, 7.1f);
            f = n < 0.22f ? 0.12f : (n < 0.40f ? 0.55f : 1f);
            float cruise = Speed01(t);
            if (cruise < 0.05f && t > TStart) f = Mathf.Max(f, 0.85f);
            if (t >= TStop && t < TStop + 0.14f) f = 0f;                              // apagón en el frenazo
            else if (t >= TStop + 0.14f && t < TStop + 0.32f) f = 0.35f + 0.5f * Mathf.PerlinNoise(t * 40f, 3f);
            if (CurrentPhase == Phase.Open && Released && t >= TDoors1) f = Perlin(t * 6f, 2.3f) < 0.07f ? 0.2f : 0.9f;
        }
        if (LevelIndex == 4 && f > 0.02f) f *= 0.6f + 0.4f * (0.5f + 0.5f * Mathf.Sin(Time.time * 3.4f));      // emergencia: pulso rojo
        return f;
    }

    private void UpdateLight()
    {
        float f = LightFactor();
        ApplyLightAndPanel(f);
        // luces de botones: la de la planta actual parpadea
        for (int i = 0; i < buttonDots.Count; i++)
        {
            bool lit = Begun && ((i == 4 - Mathf.Clamp(Mathf.FloorToInt(Dist01(T) * 5f), 0, 4)) && CurrentPhase != Phase.Closed);
            float pulse = lit ? 0.6f + 0.4f * Mathf.Sin(Time.time * 9f) : 0.05f;
            Material m = buttonDots[i].sharedMaterial;
            if (m != null) { Color c = Color.Lerp(new Color(0.9f, 0.7f, 0.2f), Tint, 0.4f); ProceduralAssets.SetEmission(m, c * pulse * 1.6f * Mathf.Clamp01(f * 1.2f)); m.color = Color.Lerp(new Color(0.1f, 0.1f, 0.1f), c, pulse * 0.6f); }
        }
    }

    private void ApplyLightAndPanel(float f)
    {
        if (cabinLight != null) { cabinLight.intensity = lightBase * f; cabinLight.enabled = f > 0.01f; }
        if (panelMat != null)
        {
            ProceduralAssets.SetEmission(panelMat, Tint * (1.9f * f));
            panelMat.color = Color.Lerp(new Color(0.12f, 0.12f, 0.12f), Color.white, f);
        }
    }

    private void UpdateDisplay()
    {
        if (numText == null) return;
        bool blink = CurrentPhase == Phase.Opening && T >= TStop && ((int)(T * 5f) % 2 == 0) && T < TDoors0;
        if (numText.text != DisplayNumber) numText.text = DisplayNumber;
        string lab = DisplayLabel;
        if (labelText.text != lab) labelText.text = lab;
        float vis = CurrentPhase == Phase.Closed ? 0f : (CurrentPhase == Phase.Closing ? Mathf.Clamp01(1f - closeT / 1.2f) : 1f);
        Color c = Tint;
        c.a = (blink ? 0.25f : 1f) * vis;
        numText.color = c; labelText.color = c;
        Color ac = Tint; ac.a = (T < TStop && Begun ? 1f : 0.15f) * vis;
        arrowImg.color = ac;
        if (displayCanvas != null && vis <= 0f && displayCanvas.enabled) displayCanvas.enabled = false;
    }

    // puertas: cada hoja se encoge hacia su lado
    private void ApplyDoors(float open)
    {
        if (pivL == null || pivR == null) return;
        float k = Mathf.Lerp(1f, 0.2f, open);
        pivL.localScale = new Vector3(k, 1f, 1f);
        pivR.localScale = new Vector3(k, 1f, 1f);
    }

    // ------------------------------------------------------------------ cámara (sacudida y mirada guiada)
    private void LateUpdate()
    {
        if (frozen) return;
        if (!Locked || pivot == null || !pivotCaptured) { TryClose(Time.deltaTime); return; }
        ApplyCameraPose();
    }

    public void ApplyCameraPose()
    {
        if (pivot == null) return;
        if (!pivotCaptured) { pivotBasePos = pivot.localPosition; pivotBaseRot = pivot.localRotation; pivotCaptured = true; }
        float t = Begun ? T : 0f;
        float s = Speed01(t);
        Vector3 pos = Vector3.zero;
        float amp = Begun ? 0.0035f + 0.016f * s : 0.002f;
        float roll = 0f;
        if (Begun && t > TStart * 0.5f)
        {
            pos.x = (Perlin(t * 23f, 1.3f) - 0.5f) * 2f * amp;
            pos.y = (Perlin(t * 29f, 4.7f) - 0.5f) * 2f * amp + Mathf.Sin(t * 71f) * 0.0025f * s;
            roll = (Perlin(t * 17f, 9.2f) - 0.5f) * 2f * 0.55f * s;
            // frenado: se «aprieta» sobre las rodillas
            float br = Smooth((t - TBrake) / (TStop - TBrake));
            pos.y -= 0.035f * br * (t < TStop ? 1f : 0f);
        }
        float pitch = 0f;
        if (Begun)
        {
            // mira hacia arriba, al indicador de plantas, y luego vuelve al frente
            float look = Smooth((t - 0.5f) / 0.9f) * (1f - Smooth((t - 2.6f) / 1.0f));
            float down = Smooth((t - 4.2f) / 0.6f) * (1f - Smooth((t - 5.6f) / 0.6f));       // vistazo al suelo al frenar
            pitch = -21f * look + 6f * down;
            if (t >= TStop)
            {
                float u = t - TStop;
                float env = Mathf.Exp(-u * 7f);
                pos.y += -0.11f * env * Mathf.Cos(u * 24f);
                pitch += 3.2f * env * Mathf.Cos(u * 18f);
                pos.x += Mathf.Sin(u * 55f) * 0.012f * env;
                roll += Mathf.Sin(u * 31f) * 1.2f * env;
            }
        }
        MaxShake = Mathf.Max(MaxShake, new Vector2(pos.x, pos.y).magnitude);
        pivot.localPosition = pivotBasePos + pos;
        pivot.localRotation = pivotBaseRot * Quaternion.Euler(pitch, 0f, roll);
    }

    // ------------------------------------------------------------------ cierre de puertas al alejarse
    private void TryClose(float dt)
    {
        if (CurrentPhase != Phase.Open || !Released || fpc == null) return;
        nextFarCheck -= dt;
        if (nextFarCheck > 0f) return;
        nextFarCheck = 0.3f;
        Vector3 d = fpc.transform.position - transform.position; d.y = 0f;
        bool far = d.magnitude > 5.0f;
        if (far)
        {
            // en multijugador no se cierra con algún compañero cerca de la cabina
            foreach (RemotePlayer rp in FindObjectsOfType<RemotePlayer>())
            {
                Vector3 e = rp.transform.position - transform.position; e.y = 0f;
                if (e.magnitude < 4.2f) { far = false; break; }
            }
        }
        farTimer = far ? farTimer + 0.3f : 0f;
        if (farTimer >= 1.2f)
        {
            CurrentPhase = Phase.Closing; closeT = 0f; closeSfx = false;
            if (doorBlock != null) doorBlock.enabled = true;
            PlayOne(cDoor, ElevatorAudio.DoorGain * 0.8f);
        }
    }

    // ------------------------------------------------------------------ utilidades (pruebas / vistas previas)
    // Congela la cabina en el instante t (sin avanzar el tiempo) y aplica la pose. Sólo para vistas previas.
    public void SeekPreview(float t, bool applyCamera)
    {
        frozen = true; Begun = true;
        T = t;
        if (t < TDoors1) { CurrentPhase = t < TBrake ? Phase.Descending : (t < TStop ? Phase.Braking : Phase.Opening); Released = false; }
        else { CurrentPhase = Phase.Open; }
        brakeSfx = dinged = doorSfx = jolted = true;               // sin sonidos en la vista previa
        if (rumbleSrc != null) { rumbleSrc.Stop(); machineSrc.Stop(); airSrc.Stop(); }
        Evaluate(0.016f);
        if (rumbleSrc != null) SetAudioIdle();
        if (applyCamera) { Locked = true; ApplyCameraPose(); }
    }

    // Cierra la cabina al instante (vista previa exterior)
    public void SeekClosed()
    {
        frozen = true; Begun = true; T = TDoors1; Released = true; Locked = false;
        CurrentPhase = Phase.Closed; closeT = 5f; Open01 = 0f; ApplyDoors(0f);
        if (doorBlock != null) doorBlock.enabled = true;
        SetAudioIdle(); ApplyLightAndPanel(0f); UpdateDisplay();
    }

    public int ColliderCount { get { return GetComponentsInChildren<BoxCollider>(true).Length; } }

    // Sonido de entrada al ascensor al completar un nivel (ding + puertas) en la posición dada
    public static void PlayExitCue(Vector3 pos)
    {
        AudioClip ding = ElevatorAudio.CreateDing(), door = ElevatorAudio.CreateDoorSlide();
        AudioSource.PlayClipAtPoint(door, pos, AudioMix.S(ElevatorAudio.DoorGain * 0.8f));
        AudioSource.PlayClipAtPoint(ding, pos, AudioMix.S(ElevatorAudio.DingGain * 0.7f));
        Destroy(ding, 4f); Destroy(door, 4f);
    }
}
