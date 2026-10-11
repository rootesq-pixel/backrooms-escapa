using UnityEngine;

// Pila recogible: recarga la linterna al acercarse. Gira y flota ligeramente.
public class BatteryPickup : MonoBehaviour
{
    public Transform target;
    public Flashlight flashlight;
    public float amount = 35f;
    public float pickupRadius = 1.3f;
    public AudioClip pickupClip;

    private Vector3 basePos;
    private float phase;

    public static int CollectedThisLevel;

    private void Start()
    {
        basePos = transform.position;
        phase = Random.Range(0f, 6.28f);
    }

    private void Update()
    {
        transform.Rotate(0f, 70f * Time.deltaTime, 0f, Space.World);
        transform.position = basePos + Vector3.up * (0.04f * Mathf.Sin(Time.time * 2f + phase));

        if (target == null) return;
        Vector3 d = target.position - basePos;
        d.y = 0f;
        if (d.sqrMagnitude <= pickupRadius * pickupRadius)
        {
            Collect();
        }
    }

    public void Collect()
    {
        Inventory inv = target != null ? target.GetComponent<Inventory>() : Inventory.Instance;
        if (inv != null)
        {
            if (inv.Add(ItemType.Battery, 1) <= 0)
            {
                if (GameManager.Instance != null) GameManager.Instance.ShowToast("Inventario lleno");
                return;
            }
        }
        else if (flashlight != null) flashlight.AddBattery(amount);   // sin inventario: recarga directa
        CollectedThisLevel++;
        if (pickupClip != null) AudioSource.PlayClipAtPoint(pickupClip, basePos + Vector3.up * 1f, 0.8f);
        if (GameManager.Instance != null) GameManager.Instance.ShowToast("+ Pila   (I: inventario · 2 / B: usar pila)");
        Destroy(gameObject);
    }
}
