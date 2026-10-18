using System.Collections.Generic;
using UnityEngine;

// v7.0.4: la puerta de salida de cada nivel está cerrada con llave. Por el nivel hay 5 llaves parecidas (KeyCount) y solo
// una la abre (depende de la semilla: igual en todos los equipos). Al llegar a la puerta se prueban solas: la que no es
// suena a cerradura atascada y queda marcada en el inventario. En multijugador decide el anfitrión: cualquier jugador con la
// llave buena abre la puerta para todos.
public class ExitLock : MonoBehaviour
{
    public const int KeyCount = 5;
    public static ExitLock Active { get; private set; }
    public static bool DisabledForTests;
    public static bool AutoKeyForTests;           // pruebas largas: quien llega a la puerta «tiene» la llave buena (se prueba aparte en V704Checks)
    public void ResetCooldownForTests() { tryCd = 0f; }          // pruebas antiguas: la salida se comporta como antes (sin llave)

    public ItemType CorrectKey { get; private set; }
    public bool Unlocked { get; private set; }
    public string UnlockedBy { get; private set; }
    public int WrongAttempts { get; private set; }
    public int Rattles { get; private set; }
    public int LockedNotices { get; private set; }
    public Transform exit;
    public float radius = 1.6f;
    private float tryCd;
    private AudioSource src;
    private AudioClip rattleClip, unlockClip;

    public static ItemType KeyFor(int seed, int level) { return ItemType.Key0 + new System.Random(seed * 131 + level * 7 + 17).Next(KeyCount); }

    public static ExitLock Create(GameObject host, int seed, int level, Transform exit, float radius)
    {
        ExitLock l = host.AddComponent<ExitLock>();
        l.CorrectKey = KeyFor(seed, level); l.exit = exit; l.radius = radius;
        if (DisabledForTests) l.Unlocked = true;
        if (exit != null)
        {
            GameObject a = new GameObject("ExitLockAudio"); a.transform.SetParent(exit, false); a.transform.localPosition = new Vector3(0f, 1.0f, 0f);
            l.src = a.AddComponent<AudioSource>();
            l.src.playOnAwake = false; l.src.spatialBlend = 1f; l.src.minDistance = 1.5f; l.src.maxDistance = 18f; l.src.rolloffMode = AudioRolloffMode.Linear; l.src.dopplerLevel = 0f;
        }
        l.rattleClip = ProceduralAudio.CreateLockRattle(); l.unlockClip = ProceduralAudio.CreateUnlock();
        return l;
    }

    private void Awake() { Active = this; }
    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (rattleClip != null) ProceduralAssets.SafeDestroy(rattleClip);
        if (unlockClip != null) ProceduralAssets.SafeDestroy(unlockClip);
    }
    private void Update() { if (tryCd > 0f) tryCd -= Time.deltaTime; }

    private static void Toast(string m) { if (GameManager.Instance != null) GameManager.Instance.ShowToast(m); }
    public bool Near(Vector3 p)
    {
        if (exit == null) return false;
        Vector3 d = FieldWrap.Delta(exit.position, p); d.y = 0f;
        return d.magnitude <= radius + 1.2f;
    }

    // el jugador local está en la puerta y sigue cerrada: prueba la llave buena o la siguiente sin probar. true si queda abierta.
    public bool AtDoor(Inventory inv)
    {
        if (Unlocked) return true;
        if (tryCd > 0f) return false;
        if ((inv != null && inv.HasKey(CorrectKey)) || AutoKeyForTests) { Unlock(true, LocalName()); return true; }
        if (inv != null)
            foreach (ItemType k in ItemDefs.Keys)
            {
                if (!inv.HasKey(k) || inv.IsWrongKey(k)) continue;
                inv.MarkWrongKey(k); WrongAttempts++; Rattle();
                Toast(ItemDefs.Get(k).name + ": entra, pero no gira. No es esta.");
                tryCd = 1.4f;
                return false;
            }
        Rattle(); LockedNotices++;
        bool any = false; if (inv != null) foreach (ItemType k in ItemDefs.Keys) if (inv.HasKey(k)) any = true;
        Toast(any ? "Ninguna de tus llaves abre esta puerta. Busca otra." : "Cerrada con llave. Por aquí debe de haber una llave que la abra.");
        tryCd = 6f;
        return false;
    }

    // «Probar en la puerta» desde el inventario
    public bool TryKeyFromInventory(Inventory inv, ItemType k, out string msg)
    {
        msg = null;
        if (Unlocked) { msg = "La puerta ya está abierta"; return false; }
        if (inv == null || !Near(inv.transform.position)) { msg = "Tienes que estar en la puerta de salida para probarla"; return false; }
        if (k == CorrectKey) { Unlock(true, LocalName()); msg = null; return true; }
        inv.MarkWrongKey(k); WrongAttempts++; Rattle();
        msg = ItemDefs.Get(k).name + ": entra, pero no gira. No es esta.";
        return false;
    }

    public void Rattle() { Rattles++; if (src != null && rattleClip != null) src.PlayOneShot(rattleClip, AudioMix.A(0.9f)); }

    // local: la ha abierto este equipo (en multijugador se pide al anfitrión). fromNet: aviso del anfitrión.
    public void Unlock(bool local, string who)
    {
        if (Unlocked) return;
        Unlocked = true; UnlockedBy = who;
        if (src != null && unlockClip != null) src.PlayOneShot(unlockClip, AudioMix.A(1f));
        Toast(local ? "La llave gira. La puerta de salida está abierta." : (string.IsNullOrEmpty(who) ? "Alguien" : who) + " ha abierto la puerta de salida");
        if (local && NetSession.Active && NetSession.Instance != null) NetSession.Instance.RequestUnlock();
    }

    private static string LocalName() { return NetSession.Active && NetSession.Instance != null ? NetSession.Instance.LocalName : "Tú"; }
}
