using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Controla el estado de la partida y el flujo de niveles:
//   Playing -> (llegar a la salida) -> LevelComplete -> carga el siguiente nivel
//   último nivel -> Won (pantalla de victoria; R vuelve a empezar desde el nivel 0)
//   atrapado -> Lost -> se reinicia el nivel actual (automático a los pocos segundos, o con R)
// R durante la partida reinicia el nivel actual. HUD con UnityEngine.UI (legacy Text/Image).
public class GameManager : MonoBehaviour
{
    public enum GameState { Playing, LevelComplete, Won, Lost }

    public static GameManager Instance { get; private set; }

    [Header("Referencias de juego")]
    public FirstPersonController player;
    public Flashlight flashlight;
    public Transform exit;
    public MonsterAI monster;
    public MonsterAI[] monsters;
    public Transform playerCamera;
    public BackroomsGenerator generator;
    [Tooltip("Gestiona la carga de niveles (reconstruye el mundo sin recargar la escena).")]
    public GameBootstrap bootstrap;

    [Header("Nivel")]
    public int levelIndex;
    public LevelConfig config;

    [Header("Reglas")]
    [Tooltip("Distancia (en el plano horizontal) a la puerta para ganar.")]
    public float exitRadius = 1.6f;
    public float titleSeconds = 5f;
    public float lostRestartSeconds = 3.5f;
    public float levelFadeSeconds = 1.2f;

    [Header("UI (legacy)")]
    public Text infoText;
    public Text messageText;
    public Text titleText;
    public Text subtitleText;
    public Text toastText;
    public Image toastPanel;   // v7.0.4: fondo oscuro del aviso, encima de la barra rápida
    public const float ToastMaxWidth = 1100f, ToastHeight = 64f, ToastGap = 22f, ToastAlpha = 0.72f;
    public Image overlay;
    public Image fadeImage;
    public RectTransform staminaFill;
    private UnityEngine.UI.Image staminaImg;
    public RectTransform batteryFill;
    public RectTransform healthFill;   // v7.9.0

    public GameState State { get; private set; }
    public float Elapsed { get { return elapsed; } }

    private float elapsed;
    private float stateTimer;
    private float introTimer;
    private float titleClock;
    private bool titleHold;
    private float toastTimer;
    private Transform killerTransform;
    private MonsterVisual killerVisual;
    private bool restarting;
    public Screamer screamer;
    private float scareT;
    private Vector3 scareBasePos;
    private float scareBaseFov = 70f;

    private void Awake()
    {
        Instance = this;
        State = GameState.Playing;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (config == null) config = LevelConfig.Get(levelIndex);
        if (messageText != null) messageText.text = string.Empty;
        if (titleText != null) titleText.text = "NIVEL " + config.DisplayNumber + " — " + config.name.ToUpper();
        if (subtitleText != null) subtitleText.text = config.subtitle;
        if (fadeImage != null) fadeImage.color = new Color(0f, 0f, 0f, 1f);
        introTimer = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // R: reinicia el nivel actual (o toda la partida desde el nivel 0 si ya ganaste)
        if (Input.GetKeyDown(KeyCode.R) && (!UIManager.InputBlocked || State == GameState.Won))
        {
            if (State == GameState.Won) RestartRun();
            else Restart();
            return;
        }

        if (bootstrap != null && bootstrap.debugKeys && State == GameState.Playing && Input.GetKeyDown(KeyCode.N))
        {
            Win();
        }

        stateTimer += dt;
        UpdateIntro(dt);

        switch (State)
        {
            case GameState.Playing:
                elapsed += dt;
                CheckExit();
                break;
            case GameState.LevelComplete:
                if (fadeImage != null)
                {
                    float a = Mathf.Clamp01(stateTimer / levelFadeSeconds);
                    fadeImage.color = new Color(0f, 0f, 0f, a);
                }
                if (stateTimer >= levelFadeSeconds + 0.4f) GoToNextLevel();
                break;
            case GameState.Lost:
                if (stateTimer >= lostRestartSeconds && !(NetSession.Active && !NetSession.IsHost)) Restart();
                break;
        }

        UpdateToast(dt);
        UpdateHud();
    }

    // El ascensor retiene la tarjeta de título hasta que se abren las puertas (ShowTitle)
    public void HoldTitle(bool hold) { titleHold = hold; if (hold) titleClock = 0f; }
    public void ShowTitle() { titleHold = false; titleClock = 0.4f; }
    public bool TitleHeld { get { return titleHold; } }

    // Fundido de entrada y tarjeta de título "Nivel N — Nombre" durante unos segundos
    private void UpdateIntro(float dt)
    {
        introTimer += dt;
        if (!titleHold) titleClock += dt;
        if (State == GameState.Playing && fadeImage != null)
        {
            float a = 1f - Mathf.Clamp01(introTimer / 1.5f);
            fadeImage.color = new Color(0f, 0f, 0f, a);
        }

        if (titleText != null && subtitleText != null)
        {
            float alpha = 0f;
            if (!titleHold && titleClock > 0.4f && titleClock < titleSeconds)
            {
                float fin = Mathf.Clamp01((titleClock - 0.4f) / 0.8f);
                float fout = Mathf.Clamp01((titleSeconds - titleClock) / 1f);
                alpha = Mathf.Min(fin, fout);
            }
            Color c = titleText.color; c.a = alpha; titleText.color = c;
            Color c2 = subtitleText.color; c2.a = alpha * 0.9f; subtitleText.color = c2;
        }
    }

    private void LateUpdate()
    {
        // Susto "simple" (solo si el Screamer está desactivado): cámara clavada en la cara con temblor y rojo
        if (State == GameState.Lost && screamer == null && killerTransform != null && playerCamera != null)
        {
            float dt = Time.unscaledDeltaTime;
            scareT += dt;
            float headY = killerVisual != null && killerVisual.profile != null ? killerVisual.profile.ai_eyeHeight * 0.95f : 1.8f;
            Vector3 dir = (killerTransform.position + Vector3.up * headY) - playerCamera.position;
            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                playerCamera.rotation = Quaternion.Slerp(playerCamera.rotation, look, 1f - Mathf.Exp(-22f * dt));
            }
            float decay = Mathf.Exp(-scareT * 2.5f);
            playerCamera.localPosition = scareBasePos + Random.insideUnitSphere * 0.05f * decay;
            if (VhsEffect.Active != null && scareT < 2.5f) VhsEffect.Active.Spike(Mathf.Lerp(1f, 0.5f, scareT / 2.5f));
        }
    }

    // Victoria del nivel: el jugador está cerca de la puerta de salida
    // v7.3.0: salida con forma propia (Talasofobia: salir a la superficie en la poza de la gruta; no basta la distancia en planta)
    public static System.Func<Vector3, bool> ExitTest;

    private void CheckExit()
    {
        if (player == null || exit == null)
        {
            return;
        }
        if (ExitTest != null)
        {
            bool at = ExitTest(player.transform.position);
            if (NetSession.Active) { NetSession.LocalAtExit = at && !NetSession.Instance.LocalDowned; return; }
            if (at) Win();
            return;
        }

        Vector3 d = FieldWrap.Delta(exit.position, player.transform.position);   // v7.0.4: la puerta del campo también al otro lado del borde
        d.y = 0f;
        if (NetSession.Active)
        {
            // multijugador: el anfitrión da el nivel por superado cuando todos los jugadores en pie están en la salida
            bool here = d.magnitude <= exitRadius && !NetSession.Instance.LocalDowned;
            // v7.0.4: puerta con llave. La abre quien tenga la buena (el anfitrión la abre para todos).
            if (here && ExitLock.Active != null && !ExitLock.Active.AtDoor(Inventory.Instance)) here = false;
            NetSession.LocalAtExit = here;
            return;
        }
        if (d.magnitude <= exitRadius)
        {
            if (ExitLock.Active != null && !ExitLock.Active.AtDoor(Inventory.Instance)) return;   // v7.0.4: cerrada con llave
            Win();
        }
    }

    // Llegar a la salida: siguiente nivel, o victoria final tras el último
    public void Win()
    {
        if (State != GameState.Playing) return;
        stateTimer = 0f;
        StopMonsters();
        if (Inventory.Instance != null) Inventory.Instance.CommitForNextLevel();
        if (player != null) player.InputEnabled = false;
        if (bootstrap != null) bootstrap.RunElapsed += elapsed;

        bool last = levelIndex >= LevelConfig.Count - 1;
        if (last)
        {
            State = GameState.Won;
            SetOverlayColor(new Color(0f, 0.25f, 0.05f, 0.35f));
            float total = bootstrap != null ? bootstrap.RunElapsed : elapsed;
            int deaths = bootstrap != null ? bootstrap.Deaths : 0;
            if (messageText != null)
            {
                messageText.text = "¡HAS ESCAPADO!\nHas superado los " + LevelConfig.Count + " niveles\nTiempo total: " + FormatTime(total)
                    + "   Intentos fallidos: " + deaths + (NetSession.Active ? "\n\nEl anfitrión puede pulsar R para volver a empezar o elige una opción" : "\n\nPulsa R para volver a empezar desde el Nivel 0 o elige una opción");
            }
        }
        else
        {
            State = GameState.LevelComplete;
            if (playerCamera != null) ElevatorCabin.PlayExitCue(playerCamera.position);   // ding + puertas: «entras» en el ascensor mientras funde a negro
            if (messageText != null)
            {
                messageText.text = "NIVEL SUPERADO";
            }
        }
    }

    // Derrota: lo llama MonsterAI cuando alcanza al jugador
    // v7.3.0: muerte sin monstruo «de tierra» (ahogado, mordisco de los peces, el Farolero): sin screamer, con su propio texto
    public string LastDeathReason { get; private set; }
    public void PlayerKilled(string reason)
    {
        if (State != GameState.Playing) return;
        LastDeathReason = reason;
        killReason = string.IsNullOrEmpty(reason) ? "TE HAN ATRAPADO" : reason;
        bool was = Screamer.Enabled;
        Screamer.Enabled = false;
        try { PlayerCaught(null); }
        finally { Screamer.Enabled = was; killReason = null; }
    }
    private string killReason;

    public void PlayerCaught(MonsterAI by)
    {
        if (State != GameState.Playing) return;
        if (killReason == null) LastDeathReason = null;
        State = GameState.Lost;
        stateTimer = 0f;
        if (player != null) player.InputEnabled = false;
        if (bootstrap != null)
        {
            bootstrap.Deaths++;
            bootstrap.RunElapsed += elapsed;
        }

        killerTransform = by != null ? by.transform : null;
        killerVisual = by != null ? by.visual : null;
        scareT = 0f;
        if (playerCamera != null)
        {
            scareBasePos = playerCamera.localPosition;
            Camera cc = playerCamera.GetComponent<Camera>();
            if (cc != null) scareBaseFov = cc.fieldOfView;
        }
        if (VhsEffect.Active != null) VhsEffect.Active.Spike(1f);
        screamer = Screamer.Begin(this, playerCamera, by);
        if (screamer != null)
        {
            // la pantalla "HAS MUERTO" y el reinicio automático los gestiona el Screamer (salvo que ya se haya acortado a mano)
            if (lostRestartSeconds >= 3f) lostRestartSeconds = screamer.autoRestartAt;
        }
        else
        {
            SetOverlayColor(new Color(0.4f, 0f, 0f, 0.55f));
            if (messageText != null)
            {
                messageText.text = (killReason ?? "TE HAN ATRAPADO") + "\n\nReiniciando el nivel...  (R para reintentar ya)";
            }
        }
    }

    private void StopMonsters()
    {
        if (monsters != null)
        {
            for (int i = 0; i < monsters.Length; i++) if (monsters[i] != null) monsters[i].Stop();
        }
        if (monster != null) monster.Stop();
    }

    // Reinicia el nivel actual (nuevo laberinto si la semilla es aleatoria)
    private float lastMpToast = -99f;
    private bool MpHostOnly(string what)
    {
        if (!NetSession.Active || NetSession.IsHost) return false;
        if (Time.unscaledTime - lastMpToast > 3f) { lastMpToast = Time.unscaledTime; ShowToast(what == null ? "Esperando a que el anfitrión reinicie el nivel..." : "Solo el anfitrión puede " + what); }
        return true;
    }

    public void Restart()
    {
        if (restarting) return;
        if (MpHostOnly(State == GameState.Lost ? null : "reiniciar el nivel")) return;
        restarting = true;
        if (Inventory.Instance != null) Inventory.Instance.RevertToLevelStart();
        if (NetSession.Active) { NetSession.Instance.HostRestartLevel(); return; }

        if (bootstrap != null)
        {
            bootstrap.NextIntroSkippable = true;      // tras reiniciar el nivel el ascensor se puede saltar
            bootstrap.LoadLevel(levelIndex);
        }
        else
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }

    // Vuelve a empezar desde el nivel 0
    public void RestartRun()
    {
        if (restarting) return;
        if (MpHostOnly("volver a empezar la partida")) return;
        restarting = true;
        if (NetSession.Active) { NetSession.Instance.HostLoadLevel(0, true, false); return; }
        if (bootstrap != null) bootstrap.RestartRun();
        else SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void GoToNextLevel()
    {
        if (restarting) return;
        if (NetSession.Active && !NetSession.IsHost) return;      // el anfitrión envía el nivel siguiente
        restarting = true;
        if (NetSession.Active) { NetSession.Instance.HostLoadLevel(levelIndex + 1, false, false); return; }
        if (bootstrap != null) bootstrap.LoadLevel(levelIndex + 1);
    }

    public void ShowToast(string text)
    {
        if (toastText != null)
        {
            toastText.text = text;
            toastTimer = 1.6f;
            if (toastPanel != null)
            {
                toastPanel.gameObject.SetActive(!string.IsNullOrEmpty(text));
                // ancho según el texto (a 32 px), sin pasar del máximo ni del ancho del lienzo
                float cw = ToastMaxWidth;
                RectTransform cr = toastPanel.canvas != null ? toastPanel.canvas.transform as RectTransform : null;
                if (cr != null && cr.rect.width > 100f) cw = Mathf.Min(cw, cr.rect.width - 48f);
                TextGenerationSettings gs = toastText.GetGenerationSettings(new Vector2(10000f, ToastHeight));
                gs.resizeTextForBestFit = false; gs.fontSize = 32;
                float pw = toastText.cachedTextGeneratorForLayout.GetPreferredWidth(text ?? "", gs) / Mathf.Max(0.01f, toastText.pixelsPerUnit);
                toastPanel.rectTransform.sizeDelta = new Vector2(Mathf.Clamp(pw + 56f, 240f, cw), ToastHeight);
                Color pc = toastPanel.color; pc.a = ToastAlpha; toastPanel.color = pc;
            }
        }
    }

    private void UpdateToast(float dt)
    {
        if (toastText == null) return;
        if (toastTimer > 0f)
        {
            toastTimer -= dt;
            Color c = toastText.color; c.a = Mathf.Clamp01(toastTimer / 0.6f); toastText.color = c;
            if (toastPanel != null) { Color pc = toastPanel.color; pc.a = ToastAlpha * c.a; toastPanel.color = pc; }
            if (toastTimer <= 0f) { toastText.text = string.Empty; if (toastPanel != null) toastPanel.gameObject.SetActive(false); }
        }
    }

    // ------------------------------------------------------------------
    // HUD
    // ------------------------------------------------------------------

    private void UpdateHud()
    {
        if (player != null && staminaFill != null)
        {
            staminaFill.anchorMax = new Vector2(Mathf.Clamp01(player.Stamina01), 1f);
            if (staminaImg == null) staminaImg = staminaFill.GetComponent<UnityEngine.UI.Image>();
            if (staminaImg != null) staminaImg.color = Adrenaline.Tint(new Color(0.9f, 0.85f, 0.3f));
        }
        if (healthFill != null && Health.Instance != null) healthFill.anchorMax = new Vector2(Health.Instance.Health01, 1f);
        if (flashlight != null && batteryFill != null)
        {
            batteryFill.anchorMax = new Vector2(Mathf.Clamp01(flashlight.Battery01), 1f);
        }
        if (infoText != null && HideControlsHint) { if (infoText.text.Length > 0) infoText.text = ""; }
        else if (infoText != null)
        {
            float total = (bootstrap != null ? bootstrap.RunElapsed : 0f) + elapsed;
            string name = config != null ? config.name : string.Empty;
            infoText.text = "Nivel " + levelIndex + " · " + name + "   Tiempo: " + FormatTime(total)
                + (ExitLock.Active != null && !ExitLock.Active.Unlocked ? "\nEncuentra la salida y la llave que la abre (solo una de las llaves sirve)" : "\nEncuentra la salida")
                + "\nWASD mover · Shift correr · Espacio saltar · Ctrl/C agacharse · E reanimar (multijugador) · F linterna · G silbar"
                + "\nB usar pila · I inventario · V VHS · R reiniciar · Esc pausa";
        }
    }

    // Grabación del tráiler: oculta el texto de nivel/controles de la esquina superior
    public static bool HideControlsHint;

    private void SetOverlayColor(Color c)
    {
        if (overlay != null)
        {
            overlay.color = c;
        }
    }

    private static string FormatTime(float seconds)
    {
        int total = Mathf.FloorToInt(seconds);
        return string.Format("{0:00}:{1:00}", total / 60, total % 60);
    }
}
