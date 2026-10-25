using System.Collections.Generic;
using UnityEngine;

// Objeto recogible del mundo (agua de almendras, barra luminosa, cinta, nota, o cualquier objeto soltado desde el inventario).
// Gira y flota ligeramente; se recoge al acercarse. Las pilas del nivel siguen usando BatteryPickup.
public class ItemPickup : MonoBehaviour
{
    public static readonly List<ItemPickup> Active = new List<ItemPickup>();

    public ItemType type;
    public int amount = 1;
    public int noteId = -1;           // >= 0: es una nota de ambiente
    public float pickupRadius = 1.2f;
    public float cooldown;            // segundos antes de poder recogerse (objetos soltados)
    public int netId = -1;            // multijugador: identificador común a todos los equipos (>= 0)
    private float reqTimer;

    private Vector3 basePos;
    public bool resting;              // v7.0.2: modelo realista apoyado (sin girar ni flotar; brillo sutil)
    private float phase;
    private bool collected;
    private static AudioClip pickupClip;
    private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    private void OnEnable() { Active.Add(this); }
    private void OnDisable() { Active.Remove(this); }

    private void Start()
    {
        basePos = transform.position;
        phase = Random.Range(0f, 6.28f);
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (cooldown > 0f) cooldown -= dt;
        if (!resting)
        {
            transform.Rotate(0f, (noteId >= 0 ? 35f : 60f) * dt, 0f, Space.World);
            transform.position = basePos + Vector3.up * (0.035f * Mathf.Sin(Time.time * 2f + phase));
        }

        Inventory inv = Inventory.Instance;
        if (inv == null || collected || cooldown > 0f) return;
        Vector3 d = inv.transform.position - basePos;
        d.y = 0f;
        if (d.sqrMagnitude <= pickupRadius * pickupRadius && Mathf.Abs(inv.transform.position.y - basePos.y) < 2.2f)
        {
            if (NetSession.Active && netId >= 0) NetTryRequest(inv);
            else Collect();
        }
    }

    // Multijugador: el anfitrión decide quién se lo queda (el primero que lo pide)
    private void NetTryRequest(Inventory inv)
    {
        reqTimer -= Time.deltaTime;
        if (reqTimer > 0f) return;
        NetSession ns = NetSession.Instance;
        if (ns == null || ns.LocalDowned) return;
        if (noteId < 0 && !inv.CanAdd(type, amount))
        {
            reqTimer = 1.5f;
            if (GameManager.Instance != null) GameManager.Instance.ShowToast("Inventario lleno");
            return;
        }
        reqTimer = 0.7f;
        ns.RequestPickup(netId);
    }

    // Multijugador: el anfitrión ha concedido el objeto. mine = me lo quedo yo; silent = reaparición de sincronización
    public void NetApply(bool mine, bool silent, string byName)
    {
        if (collected) return;
        Inventory inv = Inventory.Instance;
        GameManager gm = GameManager.Instance;
        if (noteId >= 0)
        {
            // las notas son de todo el equipo: cada uno la recibe en su inventario
            if (inv != null) inv.AddNote(noteId);
            if (gm != null && !silent) gm.ShowToast(mine ? "Nota encontrada: " + LoreNotes.Get(noteId).title + "   (I: inventario → Notas)" : byName + " ha encontrado una nota (te la copia en Notas)");
        }
        else if (mine && inv != null)
        {
            int added = inv.Add(type, amount);
            if (added <= 0)
            {
                if (NetSession.Instance != null) NetSession.Instance.DropItem(type, amount, basePos);   // se llenó entretanto: vuelve al suelo
                if (gm != null) gm.ShowToast("Inventario lleno");
            }
            else if (gm != null) gm.ShowToast("+ " + ItemDefs.Get(type).name + (added > 1 ? " x" + added : "") + "   (I: inventario)");
        }
        if (silent) { collected = true; Destroy(gameObject); return; }
        Finish();
    }

    public static int GlowCollectedThisLevel, AlmondCollectedThisLevel;   // pruebas: barras recogidas al pasar (se reinicia al cargar el nivel)
    public bool Collect()
    {
        Inventory inv = Inventory.Instance;
        if (inv == null || collected) return false;
        GameManager gm = GameManager.Instance;
        if (noteId >= 0)
        {
            bool isNew = inv.AddNote(noteId);
            if (gm != null) gm.ShowToast("Nota encontrada: " + LoreNotes.Get(noteId).title + "   (I: inventario → Notas)");
            if (!isNew) { Finish(); return true; }
        }
        else
        {
            int added = inv.Add(type, amount);
            if (added <= 0)
            {
                cooldown = 1.5f;
                if (gm != null) gm.ShowToast("Inventario lleno");
                return false;
            }
            if (type == ItemType.Glowstick) GlowCollectedThisLevel++;
            if (type == ItemType.AlmondWater) AlmondCollectedThisLevel++;
            if (gm != null) gm.ShowToast("+ " + ItemDefs.Get(type).name + (added > 1 ? " x" + added : "") + "   (I: inventario)");
        }
        Finish();
        return true;
    }

    private void Finish()
    {
        collected = true;
        if (pickupClip == null) pickupClip = ProceduralAudio.CreatePickup();
        AudioSource.PlayClipAtPoint(pickupClip, basePos + Vector3.up, AudioMix.S(0.8f));
        Destroy(gameObject);
    }

    // ------------------------------------------------------------------
    // Creación
    // ------------------------------------------------------------------
    public static ItemPickup Spawn(ItemType t, Vector3 position, int noteId, Transform parent, float cooldownSeconds)
    {
        GameObject go = new GameObject(noteId >= 0 ? "Note" : "Item_" + t);
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        bool real = BuildVisual(noteId >= 0 ? ItemType.None : t, go.transform);
        ItemPickup p = go.AddComponent<ItemPickup>();
        p.type = t; p.noteId = noteId; p.cooldown = cooldownSeconds; p.resting = real;
        if (!real) go.transform.position = position + Vector3.up * 0.0f;
        return p;
    }

    private static Material M(string key, Color c, float smooth, Color? emit = null)
    {
        Material m;
        if (mats.TryGetValue(key, out m) && m != null) return m;
        m = ProceduralAssets.CreateMaterial(c, null, Vector2.one, smooth);
        if (emit.HasValue) ProceduralAssets.SetEmission(m, emit.Value);
        mats[key] = m;
        return m;
    }

    // v7.0.2: modelo realista (Resources/Items) si está; si no, el de primitivas de siempre. Devuelve true si es el realista.
    public static bool BuildVisual(ItemType t, Transform root)
    {
        if (UseModels && ItemModels.Build(ItemModels.IdFor(t, t == ItemType.None), root) != null) return true;
        BuildPrimitiveVisual(t, root);
        return false;
    }
    public static bool UseModels = true;   // capturas «antes»

    public static void BuildPrimitiveVisual(ItemType t, Transform root)
    {
        Transform r = new GameObject("Visual").transform;
        r.SetParent(root, false);
        r.localScale = Vector3.one * 1.35f;
        switch (t)
        {
            case ItemType.Battery:
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Body", r, new Vector3(0f, 0.09f, 0f), new Vector3(0.11f, 0.09f, 0.11f), M("bat_body", new Color(0.08f, 0.08f, 0.09f), 0.5f), false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Band", r, new Vector3(0f, 0.11f, 0f), new Vector3(0.115f, 0.025f, 0.115f), M("bat_band", new Color(0.1f, 0.3f, 0.1f), 0.4f, new Color(0.15f, 0.8f, 0.25f) * 0.9f), false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Cap", r, new Vector3(0f, 0.19f, 0f), new Vector3(0.05f, 0.015f, 0.05f), M("bat_cap", new Color(0.75f, 0.55f, 0.2f), 0.7f), false);
                break;
            case ItemType.AlmondWater:
            {
                Material glass = M("al_glass", new Color(0.72f, 0.84f, 0.86f), 0.85f, new Color(0.10f, 0.14f, 0.15f));
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Bottle", r, new Vector3(0f, 0.11f, 0f), new Vector3(0.085f, 0.11f, 0.085f), glass, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Neck", r, new Vector3(0f, 0.245f, 0f), new Vector3(0.04f, 0.03f, 0.04f), glass, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Cap", r, new Vector3(0f, 0.285f, 0f), new Vector3(0.046f, 0.012f, 0.046f), M("al_cap", new Color(0.2f, 0.2f, 0.22f), 0.5f), false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Label", r, new Vector3(0f, 0.12f, 0f), new Vector3(0.089f, 0.05f, 0.089f), M("al_label", new Color(0.82f, 0.45f, 0.2f), 0.2f, new Color(0.25f, 0.12f, 0.04f)), false);
                break;
            }
            case ItemType.Glowstick:
            {
                Material g = M("gl_body", new Color(0.3f, 0.85f, 0.25f), 0.6f, new Color(0.35f, 1f, 0.3f) * 1.8f);
                GameObject s = ProceduralAssets.CreatePrimitive(PrimitiveType.Capsule, "Stick", r, new Vector3(0f, 0.03f, 0f), new Vector3(0.026f, 0.07f, 0.026f), g, false);
                s.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                break;
            }
            case ItemType.VhsTape:
            {
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Tape", r, new Vector3(0f, 0.015f, 0f), new Vector3(0.19f, 0.03f, 0.105f), M("vh_body", new Color(0.06f, 0.06f, 0.07f), 0.4f), false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Label", r, new Vector3(0f, 0.0315f, -0.015f), new Vector3(0.14f, 0.002f, 0.05f), M("vh_label", new Color(0.85f, 0.8f, 0.62f), 0.1f, new Color(0.2f, 0.18f, 0.1f)), false);
                break;
            }
            case ItemType.FlareGun:
            {
                Material o = M("fg_body", new Color(0.86f, 0.36f, 0.08f), 0.45f, new Color(0.12f, 0.04f, 0f));
                Material d = M("fg_dark", new Color(0.12f, 0.1f, 0.09f), 0.4f);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Body", r, new Vector3(0f, 0.045f, 0.02f), new Vector3(0.05f, 0.05f, 0.16f), o, false);
                GameObject b = ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Barrel", r, new Vector3(0f, 0.05f, 0.12f), new Vector3(0.05f, 0.06f, 0.05f), o, false);
                b.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                GameObject g = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Grip", r, new Vector3(0f, 0.025f, -0.06f), new Vector3(0.042f, 0.11f, 0.045f), o, false);
                g.transform.localRotation = Quaternion.Euler(-70f, 0f, 0f);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Trigger", r, new Vector3(0f, 0.012f, -0.005f), new Vector3(0.01f, 0.03f, 0.02f), d, false);
                break;
            }
            case ItemType.RoyalRations:   // v7.7.0: lata de raciones abollada con sello real descolorido
            {
                Material tin = M("rr_tin", new Color(0.42f, 0.44f, 0.30f), 0.55f);
                Material lab = M("rr_label", new Color(0.78f, 0.70f, 0.46f), 0.15f, new Color(0.10f, 0.08f, 0.03f));
                Material seal = M("rr_seal", new Color(0.55f, 0.12f, 0.10f), 0.3f);
                GameObject c0 = ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Tin", r, new Vector3(0f, 0.045f, 0f), new Vector3(0.10f, 0.045f, 0.10f), tin, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Label", r, new Vector3(0f, 0.045f, 0f), new Vector3(0.102f, 0.028f, 0.102f), lab, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Seal", r, new Vector3(0f, 0.045f, 0.05f), new Vector3(0.03f, 0.002f, 0.03f), seal, false).transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Lid", r, new Vector3(0f, 0.091f, 0f), new Vector3(0.096f, 0.003f, 0.096f), M("rr_lid", new Color(0.62f, 0.62f, 0.58f), 0.7f), false);
                c0.transform.localRotation = Quaternion.Euler(0f, 0f, 4f);
                break;
            }
            case ItemType.CashewWater:   // v7.7.0: botella como la de almendras, líquido tostado y etiqueta verde
            case ItemType.NeonWater:     // v7.7.0: botella con líquido que brilla (se ve de noche en la arena)
            {
                bool neon = t == ItemType.NeonWater;
                Material glass = M(neon ? "nw_glass" : "cw_glass", new Color(0.72f, 0.84f, 0.86f), 0.85f, new Color(0.10f, 0.14f, 0.15f));
                Material liquid = neon ? M("nw_liquid", new Color(0.2f, 1f, 0.85f), 0.9f, new Color(0.25f, 1.6f, 1.3f)) : M("cw_liquid", new Color(0.74f, 0.56f, 0.32f), 0.6f);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Liquid", r, new Vector3(0f, 0.085f, 0f), new Vector3(0.078f, 0.08f, 0.078f), liquid, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Bottle", r, new Vector3(0f, 0.11f, 0f), new Vector3(0.085f, 0.11f, 0.085f), glass, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Neck", r, new Vector3(0f, 0.245f, 0f), new Vector3(0.04f, 0.03f, 0.04f), glass, false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Cap", r, new Vector3(0f, 0.285f, 0f), new Vector3(0.046f, 0.012f, 0.046f), M(neon ? "nw_cap" : "cw_cap", neon ? new Color(0.9f, 0.2f, 0.7f) : new Color(0.22f, 0.3f, 0.18f), 0.5f), false);
                ProceduralAssets.CreatePrimitive(PrimitiveType.Cylinder, "Label", r, new Vector3(0f, 0.15f, 0f), new Vector3(0.089f, 0.035f, 0.089f), neon ? M("nw_label", new Color(0.12f, 0.1f, 0.16f), 0.3f, new Color(0.6f, 0.1f, 0.5f)) : M("cw_label", new Color(0.32f, 0.48f, 0.26f), 0.2f, new Color(0.04f, 0.07f, 0.03f)), false);
                if (neon)
                {
                    GameObject lg = new GameObject("NeonGlow"); lg.transform.SetParent(r, false); lg.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                    Light l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.3f, 1f, 0.85f); l.range = 2.2f; l.intensity = 0.7f; l.shadows = LightShadows.None;
                }
                break;
            }
            default:     // nota de papel
            {
                GameObject p = ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Paper", r, new Vector3(0f, 0.004f, 0f), new Vector3(0.17f, 0.004f, 0.22f), M("note_paper", new Color(0.86f, 0.8f, 0.6f), 0.1f, new Color(0.32f, 0.28f, 0.14f)), false);
                p.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
                for (int i = 0; i < 4; i++)
                    ProceduralAssets.CreatePrimitive(PrimitiveType.Cube, "Line", r, new Vector3(-0.01f, 0.0075f, 0.07f - i * 0.04f), new Vector3(0.12f - (i == 3 ? 0.06f : 0f), 0.001f, 0.006f), M("note_ink", new Color(0.18f, 0.14f, 0.08f), 0.1f), false);
                break;
            }
        }
    }
}
