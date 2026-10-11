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

    private Vector3 basePos;
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
        transform.Rotate(0f, (noteId >= 0 ? 35f : 60f) * dt, 0f, Space.World);
        transform.position = basePos + Vector3.up * (0.035f * Mathf.Sin(Time.time * 2f + phase));

        Inventory inv = Inventory.Instance;
        if (inv == null || collected || cooldown > 0f) return;
        Vector3 d = inv.transform.position - basePos;
        d.y = 0f;
        if (d.sqrMagnitude <= pickupRadius * pickupRadius && Mathf.Abs(inv.transform.position.y - basePos.y) < 2.2f) Collect();
    }

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
            if (gm != null) gm.ShowToast("+ " + ItemDefs.Get(type).name + (added > 1 ? " x" + added : "") + "   (I: inventario)");
        }
        Finish();
        return true;
    }

    private void Finish()
    {
        collected = true;
        if (pickupClip == null) pickupClip = ProceduralAudio.CreatePickup();
        AudioSource.PlayClipAtPoint(pickupClip, basePos + Vector3.up, 0.8f);
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
        BuildVisual(noteId >= 0 ? ItemType.None : t, go.transform);
        ItemPickup p = go.AddComponent<ItemPickup>();
        p.type = t; p.noteId = noteId; p.cooldown = cooldownSeconds;
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

    public static void BuildVisual(ItemType t, Transform root)
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
