using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;

// v7.5.0: auto-actualizador. Al llegar al menú principal (nunca en partida) pregunta en segundo plano a la API pública de GitHub por
// la última versión publicada (6 s de espera como mucho; sin red no dice nada). Si es más nueva que GameVersion.Current muestra el
// aviso «Nueva versión disponible» con «Actualizar» y «Más tarde». Solo descarga tras pulsar «Actualizar»: baja el zip de Windows
// (HTTPS de github.com y sus servidores de archivos, nada más), comprueba tamaño y SHA-256, lo extrae a <juego>\_update\nuevo y
// escribe un pequeño ayudante de PowerShell que, en cuanto el juego se cierra, aparta los archivos viejos, coloca los nuevos en la
// misma carpeta (las partidas y opciones están en el registro/AppData y no se tocan) y vuelve a abrir el juego. Si algo falla,
// deshace el cambio y abre la versión anterior, que lo explica al arrancar.
public class AutoUpdater : MonoBehaviour
{
    public enum State { Idle, Checking, UpToDate, Available, Downloading, Verifying, Installing, Error }
    public static AutoUpdater Instance { get; private set; }
    public State Current { get; private set; }
    public UpdateCore.Release Latest { get; private set; }
    public string LastError { get; private set; }
    public float Progress { get; private set; }
    public bool DialogOpen { get { return dialog != null && dialog.activeSelf; } }
    // resultado de una actualización anterior, leído al arrancar («ok|7.5.1» o «fail|motivo»); null si no la hubo
    public static string StartupResult { get; private set; }

    // pruebas («-updatefeed <url>» con un servidor falso en 127.0.0.1, «-updatetest» para la prueba de extremo a extremo)
    public static string FeedOverride;
    public static bool TestSimulateReplaceFailure;
    public static bool ForceEnable;
    bool Loopback { get { return !string.IsNullOrEmpty(FeedOverride); } }
    string Feed { get { return Loopback ? FeedOverride : UpdateCore.LatestApi; } }
    static string UserAgent { get { return "BackroomsEscapa-Updater/" + GameVersion.Current; } }

    UIManager ui;
    GameObject dialog, cornerBtn; Text cornerVersion, title, subtitle, body, status, cornerBtnText;
    RectTransform barFill; GameObject bar, rowAsk, rowCancel, rowError;
    Button bUpdate, bLater, bCancel, bClose, bWeb;
    bool mpNotified, checkedOnce, dismissed, cancel, pendingCheck;
    string notice; float noticeUntil;

    public static string GameDir { get { return Path.GetDirectoryName(Application.dataPath); } }
    public static bool CanApplyHere { get { return Application.platform == RuntimePlatform.WindowsPlayer; } }
    // en el editor, en las pruebas por lotes y con «-noupdatecheck» no se consulta nada (salvo en la prueba del actualizador)
    public static bool Enabled
    {
        get
        {
            if (ForceEnable) return true;
            if (Application.isEditor || Application.isBatchMode) return false;
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-noupdatecheck") < 0;
        }
    }

    void Awake()
    {
        Instance = this;
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == "-updatefeed" && i + 1 < a.Length && UpdateCore.IsAllowedFeed(a[i + 1], true)) FeedOverride = a[i + 1];
            if (a[i] == "-updatetest") ForceEnable = true;
            if (a[i] == "-updatetest-failreplace") TestSimulateReplaceFailure = true;
        }
        ReadStartupResult();
        if (StartupResult != null && Application.isBatchMode) ForceEnable = false;   // prueba: la instancia reabierta no vuelve a consultar
    }
    // pruebas en el editor: simula que la consulta encontró una versión (sin red) y lo deja todo como estaba
    public void InjectForTests(UpdateCore.Release r) { Latest = r; Current = State.Available; dismissed = false; if (InMainMenu) ShowDialog(); }
    public void ResetForTests() { Latest = null; Current = State.Idle; dismissed = false; mpNotified = false; if (dialog != null) dialog.SetActive(false); }
    public bool CornerButtonVisible { get { return cornerBtn != null && cornerBtn.activeInHierarchy; } }
    public string DialogTitle { get { return title != null ? title.text : ""; } }
    public string CornerText { get { return cornerVersion != null && cornerVersion.gameObject.activeInHierarchy ? cornerVersion.text : ""; } }
    public bool UpdateButtonsVisible { get { return bUpdate != null && bUpdate.gameObject.activeInHierarchy && bLater.gameObject.activeInHierarchy; } }
    public void ClickLaterForTests() { if (bLater != null) bLater.onClick.Invoke(); }
    public void ClickUpdateForTests() { if (bUpdate != null && bUpdate.gameObject.activeInHierarchy) bUpdate.onClick.Invoke(); else BeginUpdate(); }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void ReadStartupResult()
    {
        if (!CanApplyHere && !ForceEnable) return;
        try
        {
            string dir = GameDir, rf = Path.Combine(dir, UpdateCore.ResultFile);
            if (File.Exists(rf)) { StartupResult = File.ReadAllText(rf).Trim(); File.Delete(rf); Debug.Log("[AutoUpdater] resultado anterior: " + StartupResult); }
            string w = Path.Combine(dir, UpdateCore.WorkDirName), old = Path.Combine(w, "viejo");
            // restos de una actualización: se borran salvo que quede una copia de seguridad sin restaurar
            if (Directory.Exists(w) && !(Directory.Exists(old) && Directory.GetFileSystemEntries(old).Length > 0)) Directory.Delete(w, true);
        }
        catch (Exception e) { Debug.LogWarning("[AutoUpdater] limpieza: " + e.Message); }
        if (StartupResult != null)
        {
            if (StartupResult.StartsWith("ok|")) notice = "Actualizado a la v" + StartupResult.Substring(3) + ". ¡Bienvenido de vuelta!";
            else { Current = State.Error; LastError = "La actualización no se completó y se ha restaurado la versión anterior.\n" + StartupResult.Substring(StartupResult.IndexOf('|') + 1); pendingShowError = true; }
        }
    }
    bool pendingShowError;

    // ------------------------------------------------------------------ UI
    public void Build(UIManager m, Transform canvas)
    {
        ui = m;
        // esquina del menú principal: versión y, si la hay, el aviso de actualización
        cornerVersion = ui.Label(canvas, GameVersion.Label, 22, TextAnchor.LowerRight, 0f, 0f, 400f, 34f, UiStyle.Dim);
        RectTransform cv = cornerVersion.rectTransform; cv.anchorMin = cv.anchorMax = cv.pivot = new Vector2(1f, 0f); cv.anchoredPosition = new Vector2(-28f, 18f);
        cornerVersion.name = "GameVersionLabel";
        Button cb = ui.MakeButton(canvas, "", 0f, 0f, 430f, 44f, () => { Click(); dismissed = false; ShowDialog(); }, 22);
        cornerBtn = cb.gameObject; cornerBtn.name = "UpdateCornerBtn";
        RectTransform cr = (RectTransform)cornerBtn.transform; cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(1f, 0f); cr.anchoredPosition = new Vector2(-24f, 58f);
        cornerBtnText = cornerBtn.GetComponentInChildren<Text>(); cornerBtnText.color = UiStyle.Gold;
        cornerBtn.SetActive(false);

        // diálogo modal
        dialog = new GameObject("UpdateDialog", typeof(RectTransform)); dialog.transform.SetParent(canvas, false);
        RectTransform dr = (RectTransform)dialog.transform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = dr.offsetMax = Vector2.zero;
        Image block = dialog.AddComponent<Image>(); block.color = new Color(0f, 0f, 0f, 0.62f);   // tapa el menú: no se pulsa nada detrás
        RectTransform panel = ui.Panel(dialog.transform, "Panel", UiStyle.Panel, false); panel.sizeDelta = new Vector2(1000f, 520f);
        Outline ol = panel.gameObject.AddComponent<Outline>(); ol.effectColor = UiStyle.Border; ol.effectDistance = new Vector2(3f, -3f);
        RectTransform stripe = ui.Panel(panel, "Stripe", UiStyle.Gold, false); stripe.sizeDelta = new Vector2(1000f, 6f); stripe.anchoredPosition = new Vector2(0f, 257f);
        Text kicker = ui.Label(panel, "ACTUALIZACIÓN", 20, TextAnchor.MiddleLeft, 0f, 212f, 900f, 30f, UiStyle.Dim); kicker.font = UiStyle.SansBold();
        title = ui.Label(panel, "", 50, TextAnchor.MiddleLeft, 0f, 166f, 900f, 64f, UiStyle.Gold); title.font = UiStyle.SansBold();
        subtitle = ui.Label(panel, "", 28, TextAnchor.MiddleLeft, 0f, 116f, 900f, 40f, UiStyle.Text);
        body = ui.Label(panel, "", 25, TextAnchor.UpperLeft, 0f, 10f, 900f, 150f, new Color(0.86f, 0.82f, 0.66f));
        bar = ui.Panel(panel, "Bar", UiStyle.Slot, false).gameObject; RectTransform br = (RectTransform)bar.transform; br.sizeDelta = new Vector2(900f, 26f); br.anchoredPosition = new Vector2(0f, -88f);
        Outline bo = bar.AddComponent<Outline>(); bo.effectColor = UiStyle.Border; bo.effectDistance = new Vector2(2f, -2f);
        barFill = ui.Panel(bar.transform, "Fill", new Color(0.85f, 0.7f, 0.2f, 1f), true); barFill.anchorMax = new Vector2(0f, 1f);
        status = ui.Label(panel, "", 24, TextAnchor.MiddleLeft, 0f, -130f, 900f, 36f, UiStyle.Dim);

        rowAsk = Row(panel, "RowAsk");
        bUpdate = ui.MakeButton(rowAsk.transform, "Actualizar", -170f, 0f, 320f, 66f, () => { Click(); BeginUpdate(); });
        bLater = ui.MakeButton(rowAsk.transform, "Más tarde", 170f, 0f, 320f, 66f, () => { Click(); Later(); });
        rowCancel = Row(panel, "RowCancel");
        bCancel = ui.MakeButton(rowCancel.transform, "Cancelar", 0f, 0f, 320f, 66f, () => { Click(); cancel = true; });
        rowError = Row(panel, "RowError");
        bWeb = ui.MakeButton(rowError.transform, "Abrir la página de descargas", -150f, 0f, 480f, 66f, () => { Click(); Application.OpenURL(Latest != null && Latest.page != null && Latest.page.StartsWith("https://github.com/" + UpdateCore.Owner + "/") ? Latest.page : UpdateCore.ReleasesPage); }, 28);
        bClose = ui.MakeButton(rowError.transform, "Cerrar", 270f, 0f, 260f, 66f, () => { Click(); dismissed = true; dialog.SetActive(false); if (Current == State.Error) Current = Latest != null ? State.Available : State.Idle; });
        dialog.SetActive(false);
    }
    GameObject Row(RectTransform panel, string n)
    {
        GameObject r = new GameObject(n, typeof(RectTransform)); r.transform.SetParent(panel, false);
        RectTransform rt = (RectTransform)r.transform; rt.sizeDelta = new Vector2(900f, 70f); rt.anchoredPosition = new Vector2(0f, -205f);
        return r;
    }
    void Click() { if (ui != null) ui.Click(); }

    bool InMainMenu { get { return ui != null && ui.Current == UIManager.Screen.Main && GameBootstrap.Instance != null && GameBootstrap.Instance.InMenu && !NetSession.Active; } }

    void Update()
    {
        if (ui == null) return;
        bool main = InMainMenu;
        if (cornerVersion != null) cornerVersion.gameObject.SetActive(main);
        bool avail = Latest != null && (Current == State.Available || Current == State.Error);
        if (cornerBtn != null)
        {
            cornerBtn.SetActive(main && avail && !DialogOpen);
            if (avail) cornerBtnText.text = "Actualización disponible: v" + Latest.version;
        }
        if (notice != null && main && cornerVersion != null)
        {
            if (noticeUntil == 0f) noticeUntil = Time.unscaledTime + 8f;
            cornerVersion.text = notice + "   " + GameVersion.Label; cornerVersion.color = UiStyle.Good;
            if (Time.unscaledTime > noticeUntil) { notice = null; cornerVersion.text = GameVersion.Label; cornerVersion.color = UiStyle.Dim; }
        }

        // la consulta: una vez, al llegar al menú principal (no en partida ni en la sala multijugador)
        if (Enabled && main && (!checkedOnce || pendingCheck) && Current != State.Checking && Current != State.Downloading && Current != State.Verifying && Current != State.Installing)
        { checkedOnce = true; pendingCheck = false; StartCoroutine(Check()); }

        // en la sala multijugador: aviso en el chat para que todos actualicen antes de jugar juntos
        if (avail && !mpNotified && NetSession.Active && NetSession.Instance != null)
        { mpNotified = true; NetSession.Instance.LocalNotice("Hay una versión nueva del juego (v" + Latest.version + "). Salid al menú principal y pulsad «Actualizar» para jugar todos con la misma."); }
        if (pendingShowError && main) { pendingShowError = false; ShowDialog(); }
        // el diálogo solo en el menú principal; si se sale de él (p. ej. «Jugar» no es posible con el diálogo abierto) se oculta
        if (DialogOpen && !main && Current != State.Downloading && Current != State.Verifying && Current != State.Installing) dialog.SetActive(false);
        if (DialogOpen && Current == State.Available && Input.GetKeyDown(KeyCode.Escape)) Later();
        if (DialogOpen) RefreshDialog();
    }

    // la sala multijugador rechazó la conexión por versión distinta: se vuelve a consultar al volver al menú
    public void RequestCheck() { pendingCheck = true; dismissed = false; }

    public void Later() { dismissed = true; if (dialog != null) dialog.SetActive(false); }

    public void ShowDialog()
    {
        if (dialog == null) return;
        dialog.SetActive(true); dialog.transform.SetAsLastSibling(); RefreshDialog();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(Current == State.Available ? bUpdate.gameObject : (Current == State.Error ? bClose.gameObject : bCancel.gameObject));
    }

    void RefreshDialog()
    {
        bool ask = Current == State.Available, busy = Current == State.Downloading || Current == State.Verifying || Current == State.Installing, err = Current == State.Error;
        rowAsk.SetActive(ask); rowCancel.SetActive(Current == State.Downloading); rowError.SetActive(err);
        bar.SetActive(busy); barFill.anchorMax = new Vector2(Mathf.Clamp01(Progress), 1f);
        string v = Latest != null ? "v" + Latest.version : "";
        title.text = err ? "No se pudo actualizar" : "Nueva versión disponible: " + v;
        subtitle.text = Latest != null ? (string.IsNullOrEmpty(Latest.title) || Latest.title == Latest.tag ? v : Latest.title) : "";
        if (ask) body.text = "Tienes la " + GameVersion.Label + ". La nueva versión se descarga desde GitHub (" + UpdateCore.Mb(Latest.zipSize) + "), se comprueba y se instala en esta misma carpeta. El juego se cerrará y volverá a abrirse solo. Tus partidas y opciones se conservan.";
        else if (busy) body.text = "No cierres el juego: se reiniciará solo cuando termine.";
        else if (err) body.text = LastError ?? "";
        if (Current == State.Downloading) status.text = "Descargando… " + UpdateCore.Mb((long)(Progress * Latest.zipSize)) + " de " + UpdateCore.Mb(Latest.zipSize) + "  (" + Mathf.FloorToInt(Progress * 100f) + " %)";
        else if (Current == State.Verifying) status.text = "Comprobando la descarga (tamaño y SHA-256)…";
        else if (Current == State.Installing) status.text = "Preparando la instalación… el juego se cerrará y volverá a abrirse.";
        else status.text = ask ? "" : "";
    }

    // ------------------------------------------------------------------ consulta
    IEnumerator Check()
    {
        Current = State.Checking;
        string url = Feed;
        if (!UpdateCore.IsAllowedFeed(url, Loopback)) { Current = State.Idle; yield break; }
        using (UnityWebRequest r = UnityWebRequest.Get(url))
        {
            r.timeout = 6;
            r.SetRequestHeader("User-Agent", UserAgent);
            r.SetRequestHeader("Accept", "application/vnd.github+json");
            yield return r.SendWebRequest();
            if (r.result != UnityWebRequest.Result.Success) { Debug.Log("[AutoUpdater] sin respuesta (" + r.error + "): se sigue sin avisar"); Current = State.Idle; yield break; }
            string why; UpdateCore.Release rel = UpdateCore.ParseRelease(r.downloadHandler.text, out why);
            if (rel == null) { Debug.Log("[AutoUpdater] versión publicada no utilizable: " + why); Current = State.Idle; yield break; }
            if (!UpdateCore.IsNewer(rel.tag, GameVersion.Current)) { Debug.Log("[AutoUpdater] al día (" + GameVersion.Label + ", última " + rel.tag + ")"); Current = State.UpToDate; yield break; }
            Latest = rel; Current = State.Available;
            Debug.Log("[AutoUpdater] nueva versión " + rel.tag + " «" + rel.title + "» " + rel.zipName + " " + rel.zipSize + " B");
            if (!dismissed && InMainMenu) ShowDialog();
        }
    }

    // ------------------------------------------------------------------ actualización
    public void BeginUpdate()
    {
        if (Latest == null || Current == State.Downloading || Current == State.Verifying || Current == State.Installing) return;
        StartCoroutine(UpdateFlow());
    }

    void Fail(string msg) { LastError = msg; Current = State.Error; Debug.LogWarning("[AutoUpdater] " + msg); if (dialog != null) ShowDialog(); }

    IEnumerator UpdateFlow()
    {
        cancel = false; Progress = 0f; LastError = null;
        if (!CanApplyHere && !ForceEnable) { Fail("La actualización automática solo funciona en la versión de Windows. Descárgala desde la web."); yield break; }
        string gameDir = GameDir, work = Path.Combine(gameDir, UpdateCore.WorkDirName), zip = Path.Combine(work, "descarga.zip"), fresh = Path.Combine(work, "nuevo");
        string why;
        if (!UpdateCore.CanWrite(gameDir, out why))
        { Fail("No se puede escribir en la carpeta del juego (" + gameDir + ").\nMueve la carpeta a un sitio tuyo, como el Escritorio o Documentos, o descarga la versión nueva a mano."); Debug.LogWarning(why); yield break; }
        try
        {
            DriveInfo d = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(gameDir)));
            if (d.IsReady && d.AvailableFreeSpace < Latest.zipSize * 4) { Fail("No hay espacio suficiente en el disco: hacen falta unos " + UpdateCore.Mb(Latest.zipSize * 4) + " libres."); yield break; }
        }
        catch (Exception) { }
        Current = State.Downloading; ShowDialog();

        // SHA-256 publicado aparte (archivo «.sha256») si no venía en la propia versión
        if (Latest.sha256 == null && Latest.shaUrl != null)
        {
            string shaFile = Path.Combine(work, "zip.sha256"); string err = null;
            yield return Download(Latest.shaUrl, shaFile, 0, e => err = e);
            if (err == null) { try { Latest.sha256 = UpdateCore.ShaFromText(File.ReadAllText(shaFile)); } catch (Exception) { } }
        }
        if (Latest.sha256 == null) { Fail("La versión publicada no trae su huella SHA-256, así que no se puede comprobar. Descárgala a mano desde la web."); yield break; }

        string derr = null;
        yield return Download(Latest.zipUrl, zip, Latest.zipSize, e => derr = e);
        if (cancel) { TryDelete(work); Current = State.Available; dismissed = false; ShowDialog(); yield break; }
        if (derr != null) { TryDelete(work); Fail("La descarga falló: " + derr + "\nComprueba la conexión e inténtalo de nuevo."); yield break; }

        Current = State.Verifying; Progress = 1f;
        string got = null, root = null; long len = 0;
        System.Threading.Tasks.Task t = System.Threading.Tasks.Task.Run(() =>
        {
            len = new FileInfo(zip).Length;
            if (len == Latest.zipSize) got = UpdateCore.FileSha256(zip);
            if (got == Latest.sha256) root = UpdateCore.InspectZip(zip, out why);
        });
        while (!t.IsCompleted) yield return null;
        if (t.IsFaulted) { TryDelete(work); Fail("No se pudo comprobar la descarga: " + t.Exception.GetBaseException().Message); yield break; }
        if (len != Latest.zipSize) { TryDelete(work); Fail("La descarga no tiene el tamaño esperado (" + len + " de " + Latest.zipSize + " bytes). No se ha cambiado nada."); yield break; }
        if (got != Latest.sha256) { TryDelete(work); Fail("La descarga está dañada (la huella SHA-256 no coincide). No se ha cambiado nada."); yield break; }
        if (root == null) { TryDelete(work); Fail("El zip descargado no es válido (" + why + "). No se ha cambiado nada."); yield break; }

        Current = State.Installing;
        System.Threading.Tasks.Task x = System.Threading.Tasks.Task.Run(() => { UpdateCore.ExtractZip(zip, root, fresh); File.Delete(zip); });
        while (!x.IsCompleted) yield return null;
        if (x.IsFaulted) { TryDelete(work); Fail("No se pudo descomprimir la versión nueva: " + x.Exception.GetBaseException().Message + ". No se ha cambiado nada."); yield break; }

        string script = Path.Combine(work, "actualizar.ps1");
        try
        {
            string args = UpdateCore.RelaunchArgs(Environment.GetCommandLineArgs());
            File.WriteAllText(script, UpdateCore.HelperScript(Process.GetCurrentProcess().Id, gameDir, fresh, Path.Combine(work, "viejo"), args, Latest.version, TestSimulateReplaceFailure), new UTF8Encoding(true));
            string ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            if (!File.Exists(ps)) ps = "powershell.exe";
            ProcessStartInfo psi = new ProcessStartInfo(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = work };
            Process.Start(psi);
        }
        catch (Exception e) { TryDelete(work); Fail("No se pudo iniciar el instalador: " + e.Message + ". No se ha cambiado nada."); yield break; }
        Debug.Log("[AutoUpdater] ayudante en marcha; el juego se cierra para actualizarse a v" + Latest.version);
        if (OnBeforeQuit != null) { IEnumerator e = OnBeforeQuit(); while (e.MoveNext()) yield return e.Current; }
        yield return new WaitForSecondsRealtime(0.6f);
        Application.Quit(0);
    }
    // para la prueba: capturas del diálogo antes de cerrar
    public static Func<IEnumerator> OnBeforeQuit;

    static void TryDelete(string dir) { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (Exception) { } }

    // Descarga siguiendo a mano las redirecciones (cada salto se comprueba contra la lista blanca). expected > 0: barra de progreso
    IEnumerator Download(string url, string path, long expected, Action<string> done)
    {
        for (int hop = 0; hop < 6; hop++)
        {
            if (!UpdateCore.IsAllowedUrl(url, hop == 0, Loopback)) { done("dirección no permitida: " + new Uri(url).Host); yield break; }
            using (UnityWebRequest r = new UnityWebRequest(url, "GET"))
            {
                r.redirectLimit = 0;
                r.downloadHandler = new DownloadHandlerFile(path) { removeFileOnAbort = true };
                r.SetRequestHeader("User-Agent", UserAgent);
                r.SetRequestHeader("Accept", "application/octet-stream");
                UnityWebRequestAsyncOperation op = r.SendWebRequest();
                ulong last = 0; float lastT = Time.unscaledTime;
                while (!op.isDone)
                {
                    if (cancel) { r.Abort(); done("cancelada"); yield break; }
                    if (r.downloadedBytes != last) { last = r.downloadedBytes; lastT = Time.unscaledTime; }
                    else if (Time.unscaledTime - lastT > 30f) { r.Abort(); done("la conexión se quedó parada"); yield break; }
                    if (expected > 0) Progress = Mathf.Clamp01((float)((double)r.downloadedBytes / expected));
                    yield return null;
                }
                long code = r.responseCode;
                if (code >= 300 && code < 400)
                {
                    string loc = r.GetResponseHeader("Location");
                    if (string.IsNullOrEmpty(loc)) { done("redirección sin destino"); yield break; }
                    url = new Uri(new Uri(url), loc).AbsoluteUri; continue;
                }
                if (r.result != UnityWebRequest.Result.Success || code != 200) { done(string.IsNullOrEmpty(r.error) ? "HTTP " + code : r.error); yield break; }
                if (expected > 0) Progress = 1f;
                done(null); yield break;
            }
        }
        done("demasiadas redirecciones");
    }
}
