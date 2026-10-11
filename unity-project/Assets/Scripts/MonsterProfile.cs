using UnityEngine;

public enum GrowlKind { LowRumble, Rasp, Metal, Hiss, Moan }
public enum FitMode { Height, MaxExtent }

// Datos de cada criatura: modelo (Resources/Monsters/<id>), escala, ojos, sonido y créditos.
// El editor (MonsterAssetBuilder) y el juego (MonsterVisual, créditos) usan esta misma tabla.
public class MonsterProfile
{
    public string id;               // nombre del prefab en Resources/Monsters
    public string modelFile;        // FBX en Assets/Monsters/Models (null = id): permite reutilizar un rig con otra textura
    public string displayName;      // nombre en español (créditos)
    public string modelAuthor;
    public string modelSource;      // URL
    public string licence;
    public string note;             // modificaciones (retextura, escala...)

    public FitMode fit = FitMode.Height;
    public float size = 1.9f;       // altura (o extensión máxima) objetivo en metros
    public Vector3 stretch = Vector3.one;   // deformación extra (alto/estrecho)
    public float yaw = 0f;          // giro para que el modelo mire a +Z
    public float hover = 0f;        // elevación sobre el suelo (robots flotantes)
    public float ai_eyeHeight = 1.5f; // altura de "ojos" para la línea de visión
    public float killDistance = 1.4f;

    // Ojos brillantes procedurales (fracciones de la altura del modelo, en espacio local del modelo normalizado)
    public bool eyes = true;
    public Vector3 eyePos = new Vector3(0f, 0.92f, 0.12f); // x,y,z en fracción de "size"
    public float eyeSpacing = 0.035f;
    public float eyeSize = 0.016f;
    public Color eyeColor = new Color(1f, 0.1f, 0.05f);
    public float eyeLightIntensity = 0.6f;
    public float eyeLightRange = 4f;

    // Animación
    public float walkAnimSpeed = 1f;    // multiplicador de la animación al patrullar
    public float runAnimSpeed = 1f;     // al perseguir
    public bool walkUsesRun = false;    // si no hay clip "Walk": usa "Run" ralentizado
    public float walkRunSlowdown = 0.5f;

    // Efectos de terror
    public float jitter = 0.0f;         // temblor de la animación (0..1)
    public float stutterChance = 0.0f;  // probabilidad por segundo de "teleport-stutter" cuando está cerca del jugador
    public bool lightReactive = false;  // más visible/silueta a oscuras
    public GrowlKind growl = GrowlKind.LowRumble;
    public float growlPitch = 1f;
    public float footstepPitch = 1f;
    public float footstepVolume = 0.6f;
    public bool footsteps = true;
    public float stridePatrol = 1.6f, strideChase = 2.2f;

    // Materiales (los usa el editor al generar el prefab)
    public Color tint = Color.white;
    public float smoothness = -1f;      // <0 = usar la textura
    public Color emission = Color.black;

    private static MonsterProfile[] all;

    public static MonsterProfile[] All
    {
        get
        {
            if (all == null) all = Build();
            return all;
        }
    }

    public static MonsterProfile ForLevel(int level)
    {
        return All[Mathf.Clamp(level, 0, All.Length - 1)];
    }

    public static MonsterProfile ById(string id)
    {
        for (int i = 0; i < All.Length; i++) if (All[i].id == id) return All[i];
        return null;
    }

    private static MonsterProfile[] Build()
    {
        MonsterProfile stalker = new MonsterProfile
        {
            id = "HorrorStalker", displayName = "El Alto (Nivel 0)",
            modelAuthor = "HorrorGameMaker.com (City Building Game Art)",
            modelSource = "https://opengameart.org/content/3d-horror-game-monster",
            licence = "CC0 (dominio público)",
            note = "Retextura pálida/ceniza, estirado en altura; ojos y sonidos procedurales.",
            fit = FitMode.Height, size = 2.7f, stretch = new Vector3(0.88f, 1f, 0.88f), yaw = 0f,
            ai_eyeHeight = 2.2f, killDistance = 1.55f,
            eyes = false, jitter = 0.35f, stutterChance = 0.22f, lightReactive = true,
            growl = GrowlKind.Moan, growlPitch = 0.8f, footstepPitch = 0.85f, footstepVolume = 0.35f,
            walkUsesRun = false, walkAnimSpeed = 0.8f, runAnimSpeed = 1.15f, stridePatrol = 2.0f, strideChase = 2.8f,
            tint = new Color(1f, 1f, 1f), smoothness = 0.25f,
        };
        MonsterProfile giant = new MonsterProfile
        {
            id = "GiantMutant", displayName = "El Coloso (Nivel 1)",
            modelAuthor = "Eldritch Grim", modelSource = "https://opengameart.org/content/giant-mutant",
            licence = "CC0 (dominio público)",
            note = "Malla reducida (decimado ~30 %), oscurecida; ojos y sonidos procedurales.",
            fit = FitMode.Height, size = 3.0f, stretch = new Vector3(0.95f, 1f, 0.95f), yaw = 0f,
            ai_eyeHeight = 2.6f, killDistance = 2.0f,
            eyes = true, eyePos = new Vector3(0f, 0.80f, 0.13f), eyeSpacing = 0.03f, eyeSize = 0.012f,
            eyeColor = new Color(1f, 0.55f, 0.1f), eyeLightIntensity = 0.5f, eyeLightRange = 5f,
            growl = GrowlKind.LowRumble, growlPitch = 0.6f, footstepPitch = 0.5f, footstepVolume = 0.9f,
            walkUsesRun = true, walkRunSlowdown = 0.45f, runAnimSpeed = 1.0f, stridePatrol = 2.4f, strideChase = 3.4f,
            tint = new Color(0.75f, 0.72f, 0.7f), smoothness = 0.15f, jitter = 0f,
        };
        MonsterProfile zombie = new MonsterProfile
        {
            id = "CityZombie", displayName = "El Huésped (Nivel 3)",
            modelAuthor = "Rikindle3D", modelSource = "https://opengameart.org/content/male-city-zombie-ready-for-use-in-game-engines",
            licence = "CC0 (dominio público)",
            note = "Texturas desaturadas y reteñidas (gris verdoso) con Pillow; animaciones Mixamo-style reasignadas con Blender; ojos y sonidos procedurales.",
            fit = FitMode.Height, size = 1.9f, yaw = 0f,
            ai_eyeHeight = 1.6f, killDistance = 1.4f,
            eyes = true, eyePos = new Vector3(0f, 0.935f, 0.04f), eyeSpacing = 0.022f, eyeSize = 0.008f,
            eyeColor = new Color(0.9f, 0.95f, 1f), eyeLightIntensity = 0.45f, eyeLightRange = 3.5f,
            growl = GrowlKind.Rasp, growlPitch = 0.9f, footstepPitch = 0.95f, footstepVolume = 0.5f,
            walkAnimSpeed = 0.9f, runAnimSpeed = 1.0f, stridePatrol = 1.5f, strideChase = 2.3f,
            tint = new Color(0.78f, 0.80f, 0.74f), lightReactive = true, stutterChance = 0.08f, jitter = 0.1f,
        };
        MonsterProfile bot = new MonsterProfile
        {
            id = "SootHorror", displayName = "El Hollín (Nivel 4)",
            modelAuthor = "EmoPreben", modelSource = "https://opengameart.org/content/horror-monster-0",
            licence = "CC0 (dominio público)",
            note = "Malla decimada (~45 %) y estirada/encorvada; textura reteñida a negro hollín grasiento con parches pálidos; animaciones de zombi Mixamo reasignadas con Blender; ojos pálidos, temblor y sonidos procedurales.",
            fit = FitMode.Height, size = 2.35f, stretch = new Vector3(0.72f, 1.0f, 0.8f), yaw = 0f,
            ai_eyeHeight = 2.0f, killDistance = 1.65f,
            eyes = true, eyePos = new Vector3(0f, 0.93f, 0.06f), eyeSpacing = 0.028f, eyeSize = 0.009f,
            eyeColor = new Color(0.85f, 0.92f, 1f), eyeLightIntensity = 0.55f, eyeLightRange = 4.5f,
            growl = GrowlKind.Rasp, growlPitch = 0.62f, footstepPitch = 0.7f, footstepVolume = 0.5f,
            walkAnimSpeed = 0.7f, runAnimSpeed = 1.1f, stridePatrol = 1.8f, strideChase = 2.6f,
            tint = new Color(1f, 1f, 1f), smoothness = 0.35f, jitter = 0.5f, stutterChance = 0.25f, lightReactive = true,
        };
        MonsterProfile drowned = new MonsterProfile
        {
            id = "DrownedZombie", modelFile = "CityZombie", displayName = "El Ahogado (Nivel 2)",
            modelAuthor = "Rikindle3D", modelSource = "https://opengameart.org/content/male-city-zombie-ready-for-use-in-game-engines",
            licence = "CC0 (dominio público)",
            note = "Mismo rig que El Huésped, reteñido con Pillow (piel azulada, hinchada y empapada, ropa verdiazul), más ancho y lento; ojos cian, gemidos húmedos y chapoteo procedurales.",
            fit = FitMode.Height, size = 2.0f, stretch = new Vector3(1.12f, 0.98f, 1.12f), yaw = 0f,
            ai_eyeHeight = 1.7f, killDistance = 1.5f,
            eyes = true, eyePos = new Vector3(0f, 0.935f, 0.04f), eyeSpacing = 0.022f, eyeSize = 0.009f,
            eyeColor = new Color(0.55f, 0.95f, 1f), eyeLightIntensity = 0.55f, eyeLightRange = 4f,
            growl = GrowlKind.Moan, growlPitch = 0.72f, footstepPitch = 0.8f, footstepVolume = 0.55f,
            walkAnimSpeed = 0.8f, runAnimSpeed = 0.95f, stridePatrol = 1.4f, strideChase = 2.2f,
            tint = new Color(0.85f, 0.95f, 1f), smoothness = 0.55f, lightReactive = true, stutterChance = 0.12f, jitter = 0.08f,
        };
        return new[] { stalker, giant, drowned, zombie, bot };
    }
}
