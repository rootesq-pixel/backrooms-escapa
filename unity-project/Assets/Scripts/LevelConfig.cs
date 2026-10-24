using UnityEngine;

public enum LayoutKind { Maze, OpenPillars, Field, Ocean }   // v7.3.0: Ocean = Talasofobia (mar abierto, OceanWorld)
public enum FixtureKind { Panel, Tube, Sconce, Emergency }
public enum AmbientEvent { None, Drip, Clank }   // v7.3.0d: sin crujido de madera (el usuario lo quitó)
public enum StepKind { Carpet, Concrete, Water, Metal, Grass }

// Parámetros de un nivel: tema visual, tamaño, luces, monstruo, batería y audio.
// Los niveles se definen en código (LevelConfig.Get) para no necesitar assets.
public class LevelConfig
{
    public int index;
    public string name;
    public string subtitle;

    // --- Geometría ---
    public LayoutKind layout = LayoutKind.Maze;
    public int gridW = 14, gridH = 14;
    public float cellSize = 4f;
    public float wallHeight = 3f;
    public float loopChance = 0.12f;
    public int roomCount = 8;
    public Vector2Int roomSize = new Vector2Int(2, 4);
    public bool floorDecals;               // v7.4.0: manchas de aceite y charcos en el suelo (aparcamiento)
    public int pillarEvery = 0;            // 0 = sin pilares; n = pilar cada n celdas (OpenPillars)
    public float pillarSize = 0.9f;
    public float roomPillars = 0f;         // v7.3.0: prob. de pilar cuadrado empapelado en cada esquina libre de las salas abiertas (nivel 0)
    public float roomPillarSize = 0.66f;
    public float darkClusters = 0f;        // v7.3.0b: fracción de grupos de celdas con todos los paneles apagados (deterministas por semilla)
    public float flickerClusters = 0f;     // v7.3.0b: fracción de grupos donde los paneles parpadean
    public int clusterCells = 4;
    public bool troffers = false;          // v7.3.0c: pantallas empotradas con marco y difusor horneado (nivel 0)
    public bool fillerPanels = false;      // v7.3.0: paneles emisivos sin luz en las celdas sin lámpara (mallas combinadas)
    public int decks = 1;                  // v7.0.4: aparcamiento con plantas a medio nivel unidas por rampas (GaragePlan)
    public float deckDrop = 1.6f;
    public int halls = 0;                  // v7.1.0: salas de juego fijas horneadas (Parque de bolas, BallpitSetPiece)
    public bool hotel = false;             // v7.5.0: hotel largo (HotelPlan): pasillos rectos larguísimos con puertas numeradas, vestíbulo, salón de baile y comedor
    public int extraSpots = 9;             // v7.5.0: sitios para notas/agua de almendras/barras/cintas (escala con el tamaño del nivel)

    // --- Superficies ---
    public SurfaceStyle wallStyle = SurfaceStyle.WallpaperYellow;
    public SurfaceStyle floorStyle = SurfaceStyle.CarpetBrown;
    public SurfaceStyle ceilingStyle = SurfaceStyle.CeilingBeige;
    public float wallTileU = 2.4f;
    public float wallTileV = 0f;           // 0 = la textura abarca toda la altura de la pared
    public float floorTileU = 2f, floorTileV = 2f;
    public float ceilTileU = 2.4f, ceilTileV = 2.4f;
    public Color wallTint = Color.white, floorTint = Color.white, ceilTint = Color.white;
    public float trimGloss = 0.4f;
    public bool pipes = false;
    public bool water = false;
    public float waterLevel = 0.22f;
    public Color waterColor = new Color(0.30f, 0.52f, 0.60f, 0.5f);
    public int debrisCount = 0;            // objetos flotantes con flotabilidad (nivel con agua)


    // --- Atmósfera ---
    public Color ambient = new Color(0.12f, 0.10f, 0.05f);
    public Color fog = new Color(0.20f, 0.17f, 0.07f);
    public float grade;   // grading/tonemap del VHS (0 = ninguno)
    public float fogDensity = 0.04f;
    public float reflectionIntensity = 0.1f;
    public float postExposure = 0.78f, postBloom = 0.10f, postThreshold = 1.0f;   // v7.2.0: PostStack por nivel (Poolrooms y campo)

    // --- Luces ---
    public FixtureKind fixture = FixtureKind.Panel;
    public int lightSpacing = 2;           // Panel/Tube/Emergency: un accesorio cada N celdas
    public float sconceChance = 0.25f;     // Sconce: probabilidad por celda con pared
    public float deadChance = 0.08f;
    public float flickerChance = 0.3f;
    public Color lightColor = new Color(1f, 0.95f, 0.70f);
    public Color emissionColor = new Color(1.5f, 1.4f, 0.9f);
    public float lightIntensity = 1.1f;
    public float lightRange = 9f;
    public int maxRealtimeLights = 10;
    public float buzzChance = 0.5f;
    public bool pulse = false;

    // --- Jugador ---
    public float playerSpeedFactor = 1f;
    public float flashlightDrain = 0.6f;
    public float flashlightRecharge = 0.15f;
    public float flashlightIntensity = 2.5f;
    public int batteries = 4;
    public float batteryAmount = 35f;

    // --- Monstruos ---
    public string monsterModel;            // id del modelo (Resources/Monsters/<id>); ver MonsterProfile
    public int monsterCount = 1;
    public float patrolSpeed = 1.8f;
    public float chaseSpeed = 4.2f;
    public float sightRange = 18f;
    public float fieldOfView = 120f;
    public float darkSightFactor = 0.6f;
    public float hearingScale = 1f;
    public float loseSightTime = 4f;
    public float killDistance = 1.4f;

    // --- Audio ---
    public float droneHz = 55f;
    public float droneNoise = 0.15f;
    public float droneVolume = 0.35f;
    public AmbientEvent ambientEvent = AmbientEvent.None;
    public Vector2 eventInterval = new Vector2(6f, 14f);
    public StepKind step = StepKind.Carpet;

    // --- Puerta de salida (sutil) ---
    public Color doorColor = new Color(0.55f, 0.52f, 0.38f);
    public Color doorFrameColor = new Color(0.30f, 0.24f, 0.14f);

    public const int Count = 8;   // v7.3.0: nivel 7 (Talasofobia)
    private static LevelConfig[] cache;

    public static LevelConfig Get(int index)
    {
        if (cache == null)
        {
            cache = new LevelConfig[Count];
            for (int i = 0; i < Count; i++) cache[i] = Create(i);
        }
        return cache[Mathf.Clamp(index, 0, Count - 1)];
    }

    public const string OceanCreaturesId = "OceanCreatures";
    public bool HasMonsterAI { get { return layout != LayoutKind.Ocean; } }   // v7.3.0
    public static int MonsterAILevels { get { int n = 0; for (int i = 0; i < Count; i++) if (Get(i).HasMonsterAI) n++; return n; } }

    private static LevelConfig Create(int i)
    {
        LevelConfig c;
        switch (i)
        {
            case 0: c = Level0(); break;
            case 1: c = Level1(); break;
            case 2: c = Level2(); break;
            case 3: c = Level3(); break;
            case 4: c = Level4(); break;
            case 5: c = Level5(); break;
            case 6: c = Level6(); break;
            default: c = Level7(); break;
        }
        // un monstruo distinto por nivel; en Talasofobia no hay monstruo de NavMesh: sus criaturas (Los Pálidos y El Farolero) son procedurales
        c.monsterModel = c.layout == LayoutKind.Ocean ? OceanCreaturesId : MonsterProfile.ForLevel(i).id;
        return c;
    }

    // Nivel 0: oficinas amarillas (clásico)
    private static LevelConfig Level0()
    {
        LevelConfig c = new LevelConfig();
        c.index = 0;
        c.name = "Oficinas amarillas";
        c.subtitle = "Moqueta húmeda, papel pintado amarillento y el zumbido de los fluorescentes.";
        c.gridW = 16; c.gridH = 16; c.cellSize = 4f; c.wallHeight = 3f;
        c.loopChance = 0.12f; c.roomCount = 8;
        c.wallStyle = SurfaceStyle.WallpaperYellow; c.floorStyle = SurfaceStyle.CarpetBrown; c.ceilingStyle = SurfaceStyle.CeilingBeige;
        c.wallTileU = 2.4f; c.wallTileV = 0f; c.floorTileU = c.floorTileV = 2f; c.ceilTileU = c.ceilTileV = 2.4f;
        c.ambient = new Color(0.105f, 0.08f, 0.037f);   // v6.7: look más cálido y suave (inspirado en la prueba HDRP)
        c.fog = new Color(0.29f, 0.22f, 0.085f);
        c.grade = 1f;
        c.fogDensity = 0.035f;
        c.fixture = FixtureKind.Panel; c.lightSpacing = 2; c.deadChance = 0.08f; c.flickerChance = 0.3f;
        c.lightColor = new Color(1f, 0.87f, 0.60f); c.emissionColor = new Color(1.05f, 0.93f, 0.56f);
        c.lightIntensity = 1.3f; c.lightRange = 7.0f; c.maxRealtimeLights = 10;
        c.flashlightDrain = 0.5f; c.flashlightRecharge = 0.15f; c.batteries = 4; c.batteryAmount = 35f;
        c.monsterCount = 1; c.patrolSpeed = 1.8f; c.chaseSpeed = 4.0f; c.sightRange = 18f; c.hearingScale = 1f;
        c.droneHz = 60f; c.droneNoise = 0.12f; c.droneVolume = 0.30f; c.ambientEvent = AmbientEvent.None;
        c.eventInterval = new Vector2(12f, 26f);
        c.step = StepKind.Carpet;
        c.doorColor = new Color(0.50f, 0.50f, 0.36f); c.doorFrameColor = new Color(0.33f, 0.28f, 0.16f);
        if (!OldL0)
        {   // v7.3.0: look fotorrealista de la foto clásica: papel amarillo de chevrones, pilares empapelados, moqueta beige húmeda,
            // techo blanco de placas con paneles fluorescentes verdosos; luz plana, uniforme y algo enfermiza, con bruma lejana
            c.wallStyle = SurfaceStyle.WallpaperL0; c.floorStyle = SurfaceStyle.CarpetL0; c.ceilingStyle = SurfaceStyle.CeilingL0;
            c.ceilTileU = c.ceilTileV = 2.4f;
            c.roomPillars = 0.8f; c.roomPillarSize = 0.66f; c.fillerPanels = true; c.troffers = true;
            // largo e «infinito»: 28 × 44 celdas (112 × 176 m; el campo mide 264 × 264 m), muchas salas grandes y bucles = visuales largas
            c.gridW = 28; c.gridH = 44; c.roomCount = 30; c.roomSize = new Vector2Int(3, 6); c.loopChance = 0.22f;
            c.batteries = 6; c.monsterCount = 1;
            // v7.3.0b (usuario): más oscuro, con rincones en penumbra: menos ambiente, caída más corta, grupos de paneles muertos
            // o parpadeando (deterministas por semilla: iguales en todos los clientes) y bruma lejana más oscura
            c.ambient = new Color(0.17f, 0.16f, 0.071f);   // v7.3.0b4 (usuario): ~35 % menos que 0,26: rincones y detrás de los pilares en penumbra de verdad
            c.fog = new Color(0.14f, 0.14f, 0.062f); c.fogDensity = 0.030f;
            c.lightColor = new Color(1f, 0.97f, 0.70f); c.emissionColor = new Color(0.64f, 0.80f, 0.464f);   // -20 %
            c.lightIntensity = 1.12f; c.lightRange = 7.6f;   // -20 %, caída algo más corta c.maxRealtimeLights = 16;
            c.deadChance = 0.06f; c.flickerChance = 0.12f;
            c.darkClusters = 0.38f; c.flickerClusters = 0.10f; c.clusterCells = 4;
            c.grade = 0.55f;
        }
        return c;
    }
    // v7.3.0: «-oldl0» en el ejecutable: el nivel 0 de antes (solo para las capturas «antes»)
    public static bool OldL0 { get { return System.Environment.CommandLine.Contains("-oldl0"); } }

    // Nivel 1: aparcamiento de hormigón (rejilla abierta con pilares, oscuro)
    private static LevelConfig Level1()
    {
        LevelConfig c = new LevelConfig();
        c.index = 1;
        c.name = "Aparcamiento";
        c.subtitle = "Hormigón, pilares y manchas de aceite. Las luces están muy lejos unas de otras.";
        c.layout = LayoutKind.OpenPillars;
        c.gridW = 32; c.gridH = 36; c.cellSize = 5f; c.wallHeight = 3.6f;   // v7.0.4: 160 x 180 m en 4 plantas (antes 100 x 100 m, una planta)
        c.decks = 4; c.deckDrop = 1.6f;
        c.loopChance = 0.80f; c.roomCount = 0; c.pillarEvery = 2; c.pillarSize = 0.9f;
        c.wallStyle = SurfaceStyle.ParkingWall; c.floorStyle = SurfaceStyle.ParkingFloor; c.ceilingStyle = SurfaceStyle.ParkingCeiling;   // v7.4.0: PBR CC0
        c.wallTileU = 3f; c.wallTileV = 0f; c.floorTileU = 2.7f; c.floorTileV = 5.4f; c.ceilTileU = c.ceilTileV = 3f;
        c.floorDecals = true;   // v7.4.0: manchas de aceite y charcos
        c.ambient = new Color(0.07f, 0.075f, 0.08f);
        c.fog = new Color(0.10f, 0.105f, 0.115f);
        c.fogDensity = 0.032f;
        c.reflectionIntensity = 0.12f;
        c.fixture = FixtureKind.Tube; c.lightSpacing = 3; c.deadChance = 0.30f; c.flickerChance = 0.5f;
        c.lightColor = new Color(0.85f, 0.95f, 1f); c.emissionColor = new Color(1.1f, 1.3f, 1.5f);
        c.lightIntensity = 1.5f; c.lightRange = 13f; c.maxRealtimeLights = 9; c.buzzChance = 0.6f;
        c.flashlightDrain = 0.7f; c.flashlightRecharge = 0.08f; c.batteries = 7; c.batteryAmount = 35f;
        c.monsterCount = 2; c.patrolSpeed = 2.0f; c.chaseSpeed = 4.4f; c.sightRange = 24f; c.hearingScale = 1.1f; c.darkSightFactor = 0.5f;
        c.droneHz = 40f; c.droneNoise = 0.28f; c.droneVolume = 0.35f; c.ambientEvent = AmbientEvent.Drip;
        c.eventInterval = new Vector2(5f, 12f);
        c.step = StepKind.Concrete;
        c.doorColor = new Color(0.22f, 0.30f, 0.24f); c.doorFrameColor = new Color(0.12f, 0.14f, 0.12f);
        return c;
    }

    // Nivel 2: Poolrooms — pasillos de azulejos inundados
    private static LevelConfig Level2()
    {
        LevelConfig c = new LevelConfig();
        c.index = 2;
        c.name = "Poolrooms";
        c.subtitle = "Azulejos blancos, agua estancada y una luz azulada que no debería existir. Una piscina interminable.";
        c.gridW = 15; c.gridH = 15; c.cellSize = 4f; c.wallHeight = 3.2f;
        c.loopChance = 0.20f; c.roomCount = 10; c.roomSize = new Vector2Int(2, 4);
        c.wallStyle = SurfaceStyle.PoolWall; c.floorStyle = SurfaceStyle.PoolFloor; c.ceilingStyle = SurfaceStyle.CeilingPool;
        c.wallTileU = 2f; c.wallTileV = 2f; c.floorTileU = c.floorTileV = 2f; c.ceilTileU = c.ceilTileV = 2.4f;
        c.water = true; c.waterLevel = 0.22f; c.waterColor = new Color(0.28f, 0.52f, 0.62f, 0.55f);
        c.ambient = new Color(0.09f, 0.13f, 0.16f);   // v7.2.0: algo más claro (antes 0,05/0,09/0,12: «se ve un poco oscuro»)
        c.fog = new Color(0.07f, 0.17f, 0.21f);
        c.fogDensity = 0.034f;
        c.reflectionIntensity = 0.2f;
        c.fixture = FixtureKind.Panel; c.lightSpacing = 2; c.deadChance = 0.05f; c.flickerChance = 0.15f;
        c.lightColor = new Color(0.65f, 0.92f, 1f); c.emissionColor = new Color(0.9f, 1.5f, 1.8f);
        c.lightIntensity = 1.7f; c.lightRange = 10f; c.maxRealtimeLights = 10; c.buzzChance = 0.3f;   // v7.2.0: 1,3 -> 1,7 (pasillos inundados)
        c.postExposure = 0.9f; c.postBloom = 0.03f; c.postThreshold = 1.3f;   // v7.2.0: más claro y ventanas sin halo (como Cycles)
        c.playerSpeedFactor = 0.88f;
        c.flashlightDrain = 0.75f; c.flashlightRecharge = 0.05f; c.batteries = 5; c.batteryAmount = 35f;
        c.monsterCount = 1; c.patrolSpeed = 2.0f; c.chaseSpeed = 4.5f; c.sightRange = 20f; c.hearingScale = 1.3f;
        c.droneHz = 48f; c.droneNoise = 0.35f; c.droneVolume = 0.40f; c.ambientEvent = AmbientEvent.Drip;
        c.eventInterval = new Vector2(2.5f, 7f);
        c.debrisCount = 8;
        c.step = StepKind.Water;
        c.doorColor = new Color(0.78f, 0.84f, 0.84f); c.doorFrameColor = new Color(0.45f, 0.55f, 0.55f);
        return c;
    }

    // Nivel 3: pasillos de hotel muy oscuros, solo lámparas de pared
    private static LevelConfig Level3()
    {
        LevelConfig c = new LevelConfig();
        c.index = 3;
        c.name = "Hotel abandonado";
        c.subtitle = "Moqueta roja, lámparas casi muertas. Sin linterna no verás nada.";
        c.gridW = 17; c.gridH = 17; c.cellSize = 3.2f; c.wallHeight = 2.8f;
        c.loopChance = 0.06f; c.roomCount = 3; c.roomSize = new Vector2Int(2, 3);
        c.wallStyle = SurfaceStyle.WallpaperHotel; c.floorStyle = SurfaceStyle.CarpetRed; c.ceilingStyle = SurfaceStyle.CeilingDark;
        c.wallTileU = 2f; c.wallTileV = 0f; c.floorTileU = c.floorTileV = 2f; c.ceilTileU = c.ceilTileV = 2.4f;
        c.ambient = new Color(0.018f, 0.012f, 0.012f);
        c.fog = new Color(0.045f, 0.022f, 0.018f);
        c.fogDensity = 0.055f;
        c.reflectionIntensity = 0.05f;
        c.fixture = FixtureKind.Sconce; c.sconceChance = 0.14f; c.deadChance = 0.25f; c.flickerChance = 0.5f;
        c.lightColor = new Color(1f, 0.62f, 0.28f); c.emissionColor = new Color(1.6f, 0.95f, 0.45f);
        c.lightIntensity = 0.75f; c.lightRange = 6f; c.maxRealtimeLights = 8; c.buzzChance = 0f;
        c.flashlightDrain = 0.9f; c.flashlightRecharge = 0f; c.batteries = 7; c.batteryAmount = 40f; c.flashlightIntensity = 2.8f;
        c.monsterCount = 2; c.patrolSpeed = 2.0f; c.chaseSpeed = 4.4f; c.sightRange = 14f; c.darkSightFactor = 0.4f; c.hearingScale = 1.2f;
        c.droneHz = 36f; c.droneNoise = 0.2f; c.droneVolume = 0.38f; c.ambientEvent = AmbientEvent.None;
        c.eventInterval = new Vector2(5f, 13f);
        c.step = StepKind.Carpet;
        c.doorColor = new Color(0.21f, 0.11f, 0.065f); c.doorFrameColor = new Color(0.34f, 0.24f, 0.10f);
        if (!OldL3)
        {   // v7.5.0: hotel mucho más largo (22 x 34 celdas de 3,2 m = 70 x 109 m, 2,6 veces el de antes; el campo mide 264 x 264 m):
            // pasillos rectos de hasta 70 m con puertas numeradas a los dos lados, vestíbulo, salón de baile y comedor con columnas,
            // y el laberinto de pasillos de servicio entre medias. Damasco burdeos, zócalo de madera, moqueta con rosetones (PBR CC0 + propio).
            c.hotel = true;
            c.gridW = 22; c.gridH = 34; c.roomCount = 4; c.roomSize = new Vector2Int(2, 3); c.loopChance = 0.07f;
            c.wallStyle = SurfaceStyle.HotelWall; c.floorStyle = SurfaceStyle.HotelCarpet; c.ceilingStyle = SurfaceStyle.HotelCeiling;
            c.wallTileU = 1.6f; c.wallTileV = 0f; c.floorTileU = c.floorTileV = 2f; c.ceilTileU = c.ceilTileV = 2.4f;
            c.roomPillars = 0.55f; c.roomPillarSize = 0.5f;
            c.batteries = 14; c.monsterCount = 3; c.extraSpots = 18;
            c.sconceChance = 0.16f; c.maxRealtimeLights = 10;
        }
        return c;
    }
    // v7.5.0: «-oldl3» en el ejecutable: el hotel de antes (solo para las capturas «antes»)
    public static bool OldL3 { get { return System.Environment.CommandLine.Contains("-oldl3"); } }

    // Nivel 4: complejo industrial con tuberías y emergencia roja; monstruo más rápido
    private static LevelConfig Level4()
    {
        LevelConfig c = new LevelConfig();
        c.index = 4;
        c.name = "Complejo industrial";
        c.subtitle = "Tuberías, chapa oxidada y luces de emergencia. Algo corre más rápido que tú.";
        c.gridW = 20; c.gridH = 20; c.cellSize = 4f; c.wallHeight = 3.4f;
        c.loopChance = 0.10f; c.roomCount = 6; c.roomSize = new Vector2Int(2, 3);
        c.wallStyle = SurfaceStyle.MetalWall; c.floorStyle = SurfaceStyle.MetalFloor; c.ceilingStyle = SurfaceStyle.CeilingDark;
        c.wallTileU = 2.4f; c.wallTileV = 2.4f; c.floorTileU = c.floorTileV = 1f; c.ceilTileU = c.ceilTileV = 2.4f;
        c.pipes = true;
        c.ambient = new Color(0.075f, 0.025f, 0.02f);
        c.fog = new Color(0.14f, 0.045f, 0.035f);
        c.fogDensity = 0.045f;
        c.reflectionIntensity = 0.12f;
        c.fixture = FixtureKind.Emergency; c.lightSpacing = 2; c.deadChance = 0.12f; c.flickerChance = 0.35f; c.pulse = true;
        c.lightColor = new Color(1f, 0.12f, 0.06f); c.emissionColor = new Color(2.6f, 0.2f, 0.1f);
        c.lightIntensity = 1.5f; c.lightRange = 9f; c.maxRealtimeLights = 10; c.buzzChance = 0.2f;
        c.flashlightDrain = 0.95f; c.flashlightRecharge = 0f; c.batteries = 6; c.batteryAmount = 40f;
        c.monsterCount = 2; c.patrolSpeed = 2.3f; c.chaseSpeed = 5.3f; c.sightRange = 22f; c.darkSightFactor = 0.55f;
        c.hearingScale = 1.4f; c.loseSightTime = 5f; c.killDistance = 1.5f;
        c.droneHz = 45f; c.droneNoise = 0.3f; c.droneVolume = 0.36f; c.ambientEvent = AmbientEvent.Clank;
        c.eventInterval = new Vector2(4f, 10f);
        c.step = StepKind.Metal;
        c.doorColor = new Color(0.30f, 0.32f, 0.30f); c.doorFrameColor = new Color(0.18f, 0.14f, 0.08f);
        return c;
    }

    // Nivel 5 (v7.0): Campo infinito — hierba alta bajo un cielo nublado, colinas suaves, casas vacías y algo en el horizonte
    private static LevelConfig Level5()
    {
        LevelConfig c = new LevelConfig();
        c.index = 5;
        c.name = "Campo infinito";
        c.subtitle = "Hierba hasta las rodillas, un cielo blanco que no cambia y casas donde no vive nadie. El campo no se acaba.";
        c.layout = LayoutKind.Field;
        c.gridW = 22; c.gridH = 22; c.cellSize = 12f; c.wallHeight = 3f;
        c.loopChance = 1f; c.roomCount = 0;
        c.wallStyle = SurfaceStyle.ConcreteWall; c.floorStyle = SurfaceStyle.ConcreteFloor; c.ceilingStyle = SurfaceStyle.ConcreteCeiling;
        c.ambient = new Color(0.50f, 0.53f, 0.55f);
        c.fog = new Color(0.66f, 0.69f, 0.69f);
        c.fogDensity = 0.016f;
        c.grade = 0.25f;
        c.reflectionIntensity = 0.5f;
        c.fixture = FixtureKind.Panel; c.lightSpacing = 99; c.deadChance = 0f; c.flickerChance = 0f;
        c.lightColor = new Color(1f, 0.97f, 0.9f); c.emissionColor = Color.white;
        c.lightIntensity = 0.55f; c.lightRange = 0f; c.maxRealtimeLights = 4; c.buzzChance = 0f;
        c.flashlightDrain = 0.35f; c.flashlightRecharge = 0.2f; c.batteries = 3; c.batteryAmount = 35f;
        c.monsterCount = 1; c.patrolSpeed = 1.9f; c.chaseSpeed = 4.7f; c.sightRange = 26f; c.fieldOfView = 110f; c.darkSightFactor = 1f;
        c.hearingScale = 0.9f; c.loseSightTime = 5f; c.killDistance = 1.6f;
        c.droneHz = 33f; c.droneNoise = 0.1f; c.droneVolume = 0.12f; c.ambientEvent = AmbientEvent.None;
        c.eventInterval = new Vector2(20f, 40f);
        c.step = StepKind.Grass;
        c.doorColor = new Color(0.62f, 0.58f, 0.50f); c.doorFrameColor = new Color(0.34f, 0.30f, 0.25f);
        return c;
    }

    // Nivel 6 (v7.1.0): Parque de bolas — parque infantil cubierto abandonado: piscinas de bolas, estructuras acolchadas, toboganes
    private static LevelConfig Level6()
    {
        LevelConfig c = new LevelConfig();
        c.index = 6;
        c.name = "Parque de bolas";
        c.subtitle = "Colchonetas descoloridas, piscinas de bolas y una música de cajita que suena muy lejos. Aquí ya no viene nadie a jugar.";
        c.gridW = 22; c.gridH = 22; c.cellSize = 4f; c.wallHeight = 3.0f;
        c.loopChance = 0.18f; c.roomCount = 9; c.roomSize = new Vector2Int(2, 3);
        c.halls = 3;
        c.wallStyle = SurfaceStyle.PlayWall; c.floorStyle = SurfaceStyle.PlayFloor; c.ceilingStyle = SurfaceStyle.CeilingBeige;
        c.wallTileU = 4.8f; c.wallTileV = 4.8f; c.floorTileU = c.floorTileV = 2.4f; c.ceilTileU = c.ceilTileV = 2.4f;
        c.ceilTint = new Color(0.92f, 0.92f, 0.94f);
        c.ambient = new Color(0.085f, 0.08f, 0.07f);
        c.fog = new Color(0.20f, 0.19f, 0.17f);
        c.grade = 0.6f;
        c.fogDensity = 0.03f;
        c.reflectionIntensity = 0.12f;
        c.fixture = FixtureKind.Panel; c.lightSpacing = 2; c.deadChance = 0.18f; c.flickerChance = 0.35f;
        c.lightColor = new Color(1f, 0.96f, 0.88f); c.emissionColor = new Color(1.25f, 1.2f, 1.05f);
        c.lightIntensity = 1.2f; c.lightRange = 8f; c.maxRealtimeLights = 10; c.buzzChance = 0.45f;
        c.flashlightDrain = 0.6f; c.flashlightRecharge = 0.12f; c.batteries = 5; c.batteryAmount = 35f;
        c.monsterCount = 1; c.patrolSpeed = 1.9f; c.chaseSpeed = 4.6f; c.sightRange = 20f; c.hearingScale = 1.3f;
        c.droneHz = 52f; c.droneNoise = 0.10f; c.droneVolume = 0.22f; c.ambientEvent = AmbientEvent.None;
        c.eventInterval = new Vector2(10f, 22f);
        c.step = StepKind.Carpet;
        c.doorColor = new Color(0.62f, 0.55f, 0.42f); c.doorFrameColor = new Color(0.36f, 0.30f, 0.22f);
        return c;
    }

    // Nivel 7 (v7.3.0): Talasofobia — una casa inundada se abre a un mar gris sin orillas; casas sobre pilotes, un faro lejano
    // y, bajo el muelle, el agua más negra. Mar abierto construido por OceanWorld (sin laberinto ni monstruo de tierra).
    private static LevelConfig Level7()
    {
        LevelConfig c = new LevelConfig();
        c.index = 7;
        c.name = "Talasofobia";
        c.subtitle = "Agua gris hasta donde alcanza la niebla. Las casas se sostienen sobre patas larguísimas y algo se mueve justo debajo.";
        c.layout = LayoutKind.Ocean;
        c.gridW = 3; c.gridH = 3; c.cellSize = 10f; c.wallHeight = 2.85f;   // la rejilla solo sitúa la cabina del ascensor (celda 1,1)
        c.loopChance = 1f; c.roomCount = 0;
        c.wallStyle = SurfaceStyle.ConcreteWall; c.floorStyle = SurfaceStyle.ConcreteFloor; c.ceilingStyle = SurfaceStyle.ConcreteCeiling;
        c.water = true; c.waterLevel = 0f; c.waterColor = new Color(0.24f, 0.36f, 0.40f, 0.6f);
        c.ambient = new Color(0.36f, 0.40f, 0.43f);
        c.fog = new Color(0.47f, 0.52f, 0.54f);
        c.fogDensity = 0.026f;
        c.grade = 0.35f;
        c.reflectionIntensity = 0.45f;
        c.fixture = FixtureKind.Panel; c.lightSpacing = 99; c.deadChance = 0f; c.flickerChance = 0f;
        c.lightColor = new Color(0.92f, 0.95f, 1f); c.emissionColor = Color.white;
        c.lightIntensity = 0.5f; c.lightRange = 0f; c.maxRealtimeLights = 6; c.buzzChance = 0f;
        c.postExposure = 0.86f; c.postBloom = 0.06f; c.postThreshold = 1.2f;
        c.flashlightDrain = 0.45f; c.flashlightRecharge = 0.1f; c.batteries = 4; c.batteryAmount = 35f;
        c.monsterCount = 0; c.killDistance = 1.3f;
        c.droneHz = 31f; c.droneNoise = 0.05f; c.droneVolume = 0.16f; c.ambientEvent = AmbientEvent.None;
        c.eventInterval = new Vector2(9f, 20f);
        c.step = StepKind.Water;
        c.doorColor = new Color(0.50f, 0.52f, 0.50f); c.doorFrameColor = new Color(0.30f, 0.28f, 0.24f);
        return c;
    }
}
