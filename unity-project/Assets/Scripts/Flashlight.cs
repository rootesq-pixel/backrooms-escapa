using UnityEngine;

// Linterna: se enciende/apaga con F. Tiene batería que se gasta y parpadea cuando queda poca.
// Se añade al mismo objeto que la cámara (o a un hijo suyo). Crea un Spot Light si no existe.
public class Flashlight : MonoBehaviour
{
    [Header("Control")]
    public KeyCode toggleKey = KeyCode.F;
    public bool startOn = true;

    [Header("Luz")]
    public Light spot;
    public float maxIntensity = 2.5f;
    public float range = 22f;
    [Range(10f, 120f)] public float spotAngle = 60f;
    public Color color = new Color(1f, 0.96f, 0.82f);
    public bool softCookie = false;   // v7.5.0: haz suave (centro y halo amplio) en vez del borde duro
    public float bounce = 0f;         // v7.5.0: luz de rebote tenue donde da el haz (fracción de la intensidad)
    private Light bounceLight; private static Texture2D softTex;

    [Header("Batería")]
    public float batteryMax = 100f;
    [Tooltip("Unidades de batería gastadas por segundo con la linterna encendida.")]
    public float drainPerSecond = 0.6f;
    [Tooltip("Recarga lenta por segundo con la linterna apagada (0 = sin recarga).")]
    public float rechargePerSecond = 0.15f;
    [Tooltip("Por debajo de esta fracción de batería la luz empieza a parpadear.")]
    [Range(0f, 1f)] public float flickerThreshold = 0.25f;

    public bool IsOn { get; private set; }
    public float Battery01 { get { return batteryMax > 0f ? battery / batteryMax : 0f; } }

    private float battery;
    private float blackoutTimer;
    private Quaternion lagRotation = Quaternion.identity;
    private Vector3 lastCamPos;

    [Header("Inercia")]
    public float intensityMultiplier = 1f;
    private float nearT, nearHit = 99f, nearK = 1f;
    public float NearDim { get { return nearK; } }   // p. ej. compensa el cookie de cáusticas
    public float lagSpeed = 11f;
    public float swayAmount = 0.35f;

    public event System.Action<bool> Toggled;     // true = encendida
    public int ToggleCount { get; private set; }

    // Enciende / apaga (no se puede encender sin batería). Devuelve el nuevo estado.
    public bool Toggle()
    {
        if (IsOn || battery > 0.01f)
        {
            IsOn = !IsOn;
            ToggleCount++;
            if (Toggled != null) Toggled(IsOn);
        }
        return IsOn;
    }

    public void SetOn(bool on) { IsOn = on && battery > 0f; }   // pruebas / capturas
    public void SetBattery(float value) { battery = Mathf.Clamp(value, 0f, batteryMax); }   // pruebas
    public float Battery100 { get { return battery; } }

    // Gasta una pila del inventario: recarga la linterna. Devuelve la cantidad realmente recargada.
    public float Recharge(float amount)
    {
        float before = battery;
        battery = Mathf.Min(batteryMax, battery + amount);
        return battery - before;
    }

    private void LateUpdate()
    {
        if (spot == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        // La luz sigue a la cámara con un pequeño retraso y se balancea al andar
        Quaternion target = transform.rotation;
        FirstPersonController f = GetComponentInParent<FirstPersonController>();
        if (f != null && f.IsMoving)
        {
            float t = Time.time * (f.IsSprinting ? 11f : 7f);
            target *= Quaternion.Euler(Mathf.Sin(t * 2f) * swayAmount * 0.8f, Mathf.Sin(t) * swayAmount, 0f);
        }
        lagRotation = Quaternion.Slerp(lagRotation, target, 1f - Mathf.Exp(-lagSpeed * dt));
        spot.transform.rotation = lagRotation;
    }

    private void Awake()
    {
        battery = batteryMax;
        if (spot == null)
        {
            // La luz va en un hijo para poder "arrastrarla" un poco respecto a la cámara (inercia de la linterna)
            GameObject child = new GameObject("FlashlightSpot");
            child.transform.SetParent(transform, false);
            spot = child.AddComponent<Light>();
        }
        lagRotation = transform.rotation;
        ConfigureLight();
        IsOn = startOn;
        spot.enabled = IsOn;
    }

    // Recoge una pila: recarga la batería (y permite encender de nuevo la linterna)
    public void AddBattery(float amount)
    {
        battery = Mathf.Min(batteryMax, battery + amount);
    }

    public void Reconfigure() { if (spot != null) ConfigureLight(); }   // v7.3.0: lámpara del casco de buzo

    private void ConfigureLight()
    {
        spot.type = LightType.Spot;
        spot.range = range;
        spot.spotAngle = spotAngle;
        spot.color = color;
        spot.intensity = maxIntensity;
        spot.shadows = LightShadows.None; // más rendimiento; cámbialo a Soft si quieres sombras
        if (softCookie) spot.cookie = SoftCookie();
        if (bounce > 0f && bounceLight == null)
        {
            GameObject bg = new GameObject("FlashlightBounce"); bg.transform.SetParent(transform, false);
            bounceLight = bg.AddComponent<Light>(); bounceLight.type = LightType.Point; bounceLight.shadows = LightShadows.None;
            bounceLight.color = new Color(1f, 0.72f, 0.6f); bounceLight.renderMode = LightRenderMode.ForcePixel; bounceLight.enabled = false;
        }
    }

    // v7.5.0: cookie radial: centro algo más brillante y caída suave hasta el borde del cono (sin anillo duro)
    private static Texture2D SoftCookie()
    {
        if (softTex != null) return softTex;
        const int N = 128; softTex = new Texture2D(N, N, TextureFormat.Alpha8, false) { wrapMode = TextureWrapMode.Clamp, name = "FlashSoftCookie" };
        Color32[] px = new Color32[N * N];
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            float dx = (x + 0.5f) / N * 2f - 1f, dy = (y + 0.5f) / N * 2f - 1f, r = Mathf.Sqrt(dx * dx + dy * dy);
            float v = 0.42f * Mathf.Exp(-r * r / 0.10f) + 0.58f * Mathf.Exp(-r * r / 0.42f);
            v *= 1f - Mathf.SmoothStep(0.78f, 1f, r);
            px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255));
        }
        softTex.SetPixels32(px); softTex.Apply(false, true);
        return softTex;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        bool inCar = FirstPersonController.LocalInstance != null && FirstPersonController.LocalInstance.IsMounted && DesertCars.Active != null;   // v7.9.0: guardada en el coche
        if (Input.GetKeyDown(toggleKey) && !inCar && (UIManager.Instance == null || !UIManager.InputBlocked)) Toggle();

        if (IsOn)
        {
            battery = Mathf.Max(0f, battery - drainPerSecond * dt);
            if (battery <= 0f)
            {
                IsOn = false; // batería agotada
            }
        }
        else if (rechargePerSecond > 0f)
        {
            battery = Mathf.Min(batteryMax, battery + rechargePerSecond * dt);
        }

        UpdateLight(dt);
    }

    // Aplica intensidad, parpadeo y apagones breves cuando queda poca batería
    private void UpdateLight(float dt)
    {
        if (!IsOn)
        {
            spot.enabled = false;
            if (bounceLight != null) bounceLight.enabled = false;
            return;
        }

        float intensity = maxIntensity;
        float b = Battery01;

        if (b < flickerThreshold)
        {
            // 0 = batería casi vacía, 1 = justo en el umbral
            float low = flickerThreshold > 0f ? Mathf.Clamp01(b / flickerThreshold) : 1f;

            float noise = Mathf.PerlinNoise(Time.time * 25f, 0.37f);
            intensity *= Mathf.Lerp(0.25f, 1f, noise) * Mathf.Lerp(0.5f, 1f, low);

            if (blackoutTimer > 0f)
            {
                blackoutTimer -= dt;
            }
            else if (Random.value < dt * (1f - low) * 3f)
            {
                blackoutTimer = Random.Range(0.04f, 0.2f);
            }
        }
        else
        {
            blackoutTimer = 0f;
        }

        spot.enabled = blackoutTimer <= 0f;
        // v7.0.1: a muy poca distancia (puertas, paredes a 1-3 m) la linterna se atenúa como una exposición automática: sin blancos quemados
        nearT -= dt;
        if (nearT <= 0f)
        {
            nearT = 0.1f;
            RaycastHit nh;
            nearHit = Physics.Raycast(spot.transform.position, spot.transform.forward, out nh, 4.5f, ~0, QueryTriggerInteraction.Ignore) ? nh.distance : 99f;
        }
        nearK = Mathf.Lerp(nearK, Mathf.Lerp(0.42f, 1f, Mathf.InverseLerp(0.6f, 4.5f, nearHit)), 1f - Mathf.Exp(-8f * dt));
        spot.intensity = intensity * intensityMultiplier * nearK;
        if (bounceLight != null)
        {   // rebote: un punto tenue un poco delante de donde da el haz (lo que ilumina la linterna «rellena» un poco el entorno)
            bounceT -= dt;
            if (bounceT <= 0f)
            {
                bounceT = 0.05f;
                RaycastHit bh;
                bool hit = Physics.Raycast(spot.transform.position, spot.transform.forward, out bh, 14f, ~0, QueryTriggerInteraction.Ignore);
                bounceDist = hit ? bh.distance : 14f;
                bounceTarget = hit ? bh.point + bh.normal * 0.6f : spot.transform.position + spot.transform.forward * 10f;
            }
            Transform bt = bounceLight.transform;
            bt.position = Vector3.Lerp(bt.position, bounceTarget, 1f - Mathf.Exp(-14f * dt));
            float k = Mathf.Clamp01(1.4f - bounceDist / 12f);
            bounceLight.range = 2.5f + bounceDist * 0.35f;
            bounceLight.intensity = spot.intensity * bounce * k;
            bounceLight.enabled = spot.enabled && k > 0.02f;
        }
    }
    private float bounceT, bounceDist = 14f; private Vector3 bounceTarget;
}
