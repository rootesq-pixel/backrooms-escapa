using System;
using System.Collections.Generic;
using UnityEngine;

// Inventario del jugador: 15 casillas (5x3) con objetos apilables, notas de ambiente y barra rápida (casillas 1-5, teclas 1-5).
// Va en el objeto Player. Lo recogido se conserva al pasar de nivel (CommitForNextLevel) y se revierte al reintentar.
public class Inventory : MonoBehaviour
{
    public const int SlotCount = 15;
    public const int HotbarSize = 5;

    public static Inventory Instance { get; private set; }

    [Serializable]
    public struct Slot { public ItemType type; public int count; public bool Empty { get { return type == ItemType.None || count <= 0; } } }

    // Estado que se lleva al empezar el siguiente nivel / al reintentar
    private static int[] carryCounts = new int[ItemDefs.Count];
    private static List<int> carryNotes = new List<int>();
    public static int Collected;   // total de objetos recogidos en la partida (informativo)

    public float batteryAmount = 35f;
    public Flashlight flashlight;
    public FirstPersonController fpc;
    public Sanity sanity;
    public Camera cam;

    public Slot[] slots = new Slot[SlotCount];
    public readonly List<int> notes = new List<int>();
    public readonly HashSet<ItemType> wrongKeys = new HashSet<ItemType>();   // v7.0.4: llaves probadas que no abren
    public bool IsWrongKey(ItemType t) { return wrongKeys.Contains(t); }
    public void MarkWrongKey(ItemType t) { if (wrongKeys.Add(t)) Notify(); }
    public bool HasKey(ItemType t) { return ItemDefs.IsKey(t) && GetCount(t) > 0; }

    private int[] startCounts;
    private List<int> startNotes;

    public event Action Changed;
    public string LastMessage { get; private set; }
    public int BatteryCount { get { return GetCount(ItemType.Battery); } }
    public int NotesFound { get { return notes.Count; } }

    // ------------------------------------------------------------------
    private void Awake()
    {
        Instance = this;
        if (flashlight == null) flashlight = GetComponentInChildren<Flashlight>();
        if (fpc == null) fpc = GetComponent<FirstPersonController>();
        if (sanity == null) sanity = GetComponent<Sanity>();
        if (cam == null) cam = GetComponentInChildren<Camera>();

        for (int i = 0; i < slots.Length; i++) slots[i] = default(Slot);
        slots[0] = new Slot { type = ItemType.Flashlight, count = 1 };
        for (int i = 0; i < ItemDefs.All.Length; i++)
        {
            ItemType t = ItemDefs.All[i];
            if (t == ItemType.Flashlight) continue;
            int n = carryCounts[(int)t];
            if (n > 0) Add(t, n, false);
        }
        notes.AddRange(carryNotes);
        startCounts = (int[])carryCounts.Clone();
        startNotes = new List<int>(carryNotes);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Glowstick.ClearAll();
    }

    private void Update()
    {
        if (Time.unscaledDeltaTime <= 0f) return;
        GameManager gm = GameManager.Instance;
        if (gm == null || gm.State != GameManager.GameState.Playing) return;
        UIManager ui = UIManager.Instance;
        if (ui != null && ui.Current != UIManager.Screen.None && ui.Current != UIManager.Screen.Inventory) return;
        if (MultiplayerUI.ChatTyping) return;
        if (FirstPersonController.LocalInstance != null && FirstPersonController.LocalInstance.IsMounted && DesertCars.Active != null) return;   // v7.9.0: en el coche no se usan objetos

        // Barra rápida: teclas 1-5 (también con el teclado numérico)
        for (int i = 0; i < HotbarSize; i++)
        {
            if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) UseSlot(i);
        }
    }

    private void Notify() { if (Changed != null) Changed(); }

    // ------------------------------------------------------------------
    // Consulta
    // ------------------------------------------------------------------
    public int GetCount(ItemType t)
    {
        int n = 0;
        for (int i = 0; i < slots.Length; i++) if (slots[i].type == t) n += slots[i].count;
        return n;
    }

    public Slot GetSlot(int i) { return i >= 0 && i < slots.Length ? slots[i] : default(Slot); }

    // Arrastrar y soltar en el inventario: intercambia dos casillas (la linterna se queda en la casilla 1)
    public bool Swap(int a, int b)
    {
        if (a == b || a < 1 || b < 1 || a >= slots.Length || b >= slots.Length) return false;
        if (slots[a].Empty && slots[b].Empty) return false;
        Slot t = slots[a]; slots[a] = slots[b]; slots[b] = t;
        Notify();
        return true;
    }

    public int UsedSlots { get { int n = 0; for (int i = 0; i < slots.Length; i++) if (!slots[i].Empty) n++; return n; } }

    // ------------------------------------------------------------------
    // Añadir / quitar
    // ------------------------------------------------------------------
    private int FindEmptySlot(ItemType t)
    {
        int pref = ItemDefs.Get(t).preferredSlot;
        if (pref > 0 && pref < slots.Length && slots[pref].Empty) return pref;
        for (int i = HotbarSize; i < slots.Length; i++) if (slots[i].Empty) return i;
        for (int i = 1; i < slots.Length; i++) if (slots[i].Empty) return i;
        return -1;
    }

    // Devuelve cuántos se añadieron realmente (0 si no hay sitio)
    public int Add(ItemType t, int n, bool count = true)
    {
        if (t == ItemType.None || t == ItemType.Flashlight || n <= 0) return 0;
        ItemDefs.Def def = ItemDefs.Get(t);
        int remaining = n;
        for (int i = 0; i < slots.Length && remaining > 0; i++)
        {
            if (slots[i].type != t || slots[i].count >= def.maxStack) continue;
            int add = Mathf.Min(def.maxStack - slots[i].count, remaining);
            slots[i].count += add; remaining -= add;
        }
        while (remaining > 0)
        {
            int idx = FindEmptySlot(t);
            if (idx < 0) break;
            int add = Mathf.Min(def.maxStack, remaining);
            slots[idx] = new Slot { type = t, count = add };
            remaining -= add;
        }
        int added = n - remaining;
        if (added > 0 && count) Collected += added;
        if (added > 0) Notify();
        return added;
    }

    // ¿Cabe (aunque sea en parte)? Lo usa el multijugador antes de pedir un objeto al anfitrión
    public bool CanAdd(ItemType t, int n)
    {
        if (t == ItemType.None || t == ItemType.Flashlight || n <= 0) return false;
        ItemDefs.Def def = ItemDefs.Get(t);
        for (int i = 0; i < slots.Length; i++) if (slots[i].type == t && slots[i].count < def.maxStack) return true;
        return FindEmptySlot(t) >= 0;
    }

    public void AddBattery(int n) { Add(ItemType.Battery, n); }

    public bool Remove(int slotIndex, int n = 1)
    {
        if (slotIndex <= 0 || slotIndex >= slots.Length || slots[slotIndex].Empty || n <= 0) return false;
        slots[slotIndex].count -= n;
        if (slots[slotIndex].count <= 0) slots[slotIndex] = default(Slot);
        Notify();
        return true;
    }

    // Quita una unidad del tipo (donde esté)
    public bool RemoveOne(ItemType t)
    {
        for (int i = slots.Length - 1; i >= 1; i--) if (slots[i].type == t && slots[i].count > 0) return Remove(i, 1);
        return false;
    }

    public bool AddNote(int id)
    {
        if (!LoreNotes.Valid(id) || notes.Contains(id)) return false;
        notes.Add(id);
        notes.Sort();
        Notify();
        return true;
    }

    public bool HasNote(int id) { return notes.Contains(id); }

    // ------------------------------------------------------------------
    // Usar / soltar
    // ------------------------------------------------------------------
    // Usa el objeto de la casilla. Devuelve true si se aplicó.
    public bool UseSlot(int i)
    {
        if (i < 0 || i >= slots.Length || slots[i].Empty) return false;
        GameManager gm = GameManager.Instance;
        string msg = null;
        bool ok = false;
        switch (slots[i].type)
        {
            case ItemType.Flashlight:
                if (flashlight != null)
                {
                    if (!flashlight.IsOn && flashlight.Battery01 <= 0.001f) msg = "Sin batería: usa una pila";
                    else { bool on = flashlight.Toggle(); msg = on ? "Linterna encendida" : "Linterna apagada"; ok = true; }
                }
                break;
            case ItemType.Battery:
                if (UseBattery()) { msg = "Pila usada: linterna recargada"; ok = true; }
                else msg = flashlight != null && flashlight.Battery01 > 0.985f ? "La linterna ya está llena" : "No puedes usar la pila ahora";
                break;
            case ItemType.AlmondWater:
                if (sanity != null && sanity.Sanity01 > 0.98f && (fpc == null || fpc.Stamina01 > 0.98f)) { msg = "Ahora mismo no la necesitas"; break; }
                if (fpc != null) fpc.RestoreStamina(1f);
                if (sanity != null) sanity.Add(35f);
                Remove(i, 1);
                msg = "Agua de almendras: recuperas resistencia y cordura";
                ok = true;
                break;
            case ItemType.Key0: case ItemType.Key1: case ItemType.Key2: case ItemType.Key3: case ItemType.Key4:
                if (ExitLock.Active != null) { ok = ExitLock.Active.TryKeyFromInventory(this, slots[i].type, out msg); }
                else msg = "Aquí no hay ninguna cerradura";
                break;
            case ItemType.Glowstick:
                if (ThrowGlowstick(true)) { Remove(i, 1); msg = "Barra luminosa lanzada"; ok = true; }
                break;
            case ItemType.RoyalRations:   // v7.7.0
            case ItemType.CashewWater:
            case ItemType.NeonWater:
            {
                ItemType t = slots[i].type;
                if (t != ItemType.NeonWater && sanity != null && sanity.Sanity01 > 0.98f && (fpc == null || fpc.Stamina01 > 0.98f)) { msg = "Ahora mismo no lo necesitas"; break; }
                if (t == ItemType.RoyalRations)
                {
                    if (fpc != null) fpc.RestoreStamina(0.4f);
                    bool bad = UnityEngine.Random.value < 0.3f;
                    if (sanity != null) sanity.Add(bad ? 2f : 10f);
                    msg = bad ? "Raciones Reales: caducadas y saben fatal, pero quitan el hambre" : "Raciones Reales: caducadas, pero recuperas algo de resistencia";
                }
                else if (t == ItemType.CashewWater)
                {
                    if (fpc != null) fpc.RestoreStamina(0.7f);
                    if (sanity != null) sanity.Add(20f);
                    msg = "Agua de anacardo: recuperas resistencia y algo de cordura";
                }
                else
                {
                    if (fpc != null) fpc.RestoreStamina(1f);
                    if (sanity != null) sanity.Add(55f);
                    if (flashlight != null) flashlight.Recharge(batteryAmount * 0.6f);
                    msg = "Agua de neón: te recorre un calor brillante (resistencia, cordura y linterna)";
                }
                Remove(i, 1);
                ok = true;
                break;
            }
            case ItemType.FlareGun:   // v7.3.0
                if (OceanFlare.TryFire(cam != null ? cam.transform : transform, out msg)) { Remove(i, 1); ok = true; }
                break;
            default:
                msg = ItemDefs.Get(slots[i].type).name + ": no se puede usar";
                break;
        }
        LastMessage = msg;
        if (msg != null && gm != null) gm.ShowToast(msg);
        if (ok) Notify();
        return ok;
    }

    // Usa una pila: recarga la linterna. false si no quedan o la linterna ya está llena.
    public bool UseBattery()
    {
        if (flashlight == null || flashlight.Battery01 > 0.985f) return false;
        if (!RemoveOne(ItemType.Battery)) return false;
        flashlight.Recharge(batteryAmount);
        return true;
    }

    // Suelta una unidad del objeto de la casilla en el suelo, delante del jugador (la barra luminosa se deja encendida)
    public bool DropSlot(int i)
    {
        if (i <= 0 || i >= slots.Length || slots[i].Empty) return false;
        ItemType t = slots[i].type;
        if (!ItemDefs.Get(t).droppable) return false;
        Vector3 fwd = cam != null ? cam.transform.forward : transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
        fwd.Normalize();
        Vector3 p = transform.position + fwd * 0.9f;
        Vector3 floor = FloorPoint(p);
        Remove(i, 1);
        if (t == ItemType.Glowstick)
        {
            Glowstick.Spawn(floor + Vector3.up * 0.06f, Vector3.zero, null);
            if (NetSession.Active) NetSession.Instance.NotifyGlow(floor + Vector3.up * 0.06f, Vector3.zero);
        }
        else if (NetSession.Active) NetSession.Instance.DropItem(t, 1, floor + Vector3.up * 0.02f);
        else ItemPickup.Spawn(t, floor + Vector3.up * 0.02f, -1, transform.parent, 2.5f);
        string msg = ItemDefs.Get(t).name + (t == ItemType.Glowstick ? " colocada en el suelo" : " soltada");
        LastMessage = msg;
        if (GameManager.Instance != null) GameManager.Instance.ShowToast(msg);
        return true;
    }

    private Vector3 FloorPoint(Vector3 p)
    {
        RaycastHit hit;
        if (Physics.Raycast(p + Vector3.up * 1.0f, Vector3.down, out hit, 3f, ~0, QueryTriggerInteraction.Ignore)) return hit.point;
        return new Vector3(p.x, transform.position.y - 0.9f, p.z);
    }

    public bool ThrowGlowstick(bool throwIt)
    {
        Transform c = cam != null ? cam.transform : transform;
        Vector3 start = c.position + c.forward * 0.45f + Vector3.down * 0.15f;
        Vector3 vel = c.forward * 7.5f + Vector3.up * 2.0f;
        Collider own = GetComponent<Collider>();
        Glowstick.Spawn(start, throwIt ? vel : Vector3.zero, own);
        if (NetSession.Active) NetSession.Instance.NotifyGlow(start, throwIt ? vel : Vector3.zero);
        return true;
    }

    // ------------------------------------------------------------------
    // Persistencia entre niveles
    // ------------------------------------------------------------------
    private int[] CurrentCounts()
    {
        int[] c = new int[ItemDefs.Count];
        for (int i = 1; i < slots.Length; i++) if (!slots[i].Empty) c[(int)slots[i].type] += slots[i].count;
        return c;
    }

    // Al completar el nivel: lo que llevo pasa al siguiente. Al morir/reiniciar: vuelvo a lo del inicio del nivel.
    public void CommitForNextLevel()
    {
        carryCounts = CurrentCounts();
        foreach (ItemType k in ItemDefs.Keys) carryCounts[(int)k] = 0;   // v7.0.4: las llaves son de cada nivel
        if ((int)ItemType.FlareGun < carryCounts.Length) carryCounts[(int)ItemType.FlareGun] = 0;   // v7.3.0: la pistola de bengalas tampoco pasa
        carryNotes = new List<int>(notes);
        if (sanity != null) sanity.Commit();
        if (Health.Instance != null) Health.Instance.Commit();
    }

    public void RevertToLevelStart()
    {
        carryCounts = (int[])startCounts.Clone();
        carryNotes = new List<int>(startNotes);
        wrongKeys.Clear();   // v7.0.4
        if (sanity != null) sanity.Revert();
        if (Health.Instance != null) Health.Instance.Revert();
    }

    public static void ResetAll()
    {
        carryCounts = new int[ItemDefs.Count];
        carryNotes = new List<int>();
        Collected = 0;
        Sanity.ResetAll();
        Health.ResetAll();
    }
}
