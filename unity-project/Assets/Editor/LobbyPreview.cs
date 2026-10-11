using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Capturas PNG del flujo multijugador (menú, alojar, sala y partida con jugadores remotos simulados).
// Los "amigos" son clientes TCP crudos por bucle local que hablan el protocolo real.
[InitializeOnLoad]
public static class LobbyPreview
{
    private static IEnumerator routine;
    private static double waitUntil;

    static LobbyPreview()
    {
        if (SessionState.GetString("smoke_phase", "") != "lobbyprev") return;
        EditorApplication.update += Tick;
    }

    public static void Export()
    {
        Directory.CreateDirectory("/workspace/monster-previews");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "lobbyprev");
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try
        {
            PumpFakes();
            if (!routine.MoveNext()) Finish();
            else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current;
        }
        catch (System.Exception e) { Debug.LogError("[LobbyPreview] " + e); Finish(); }
    }

    private static void Finish()
    {
        SessionState.SetString("smoke_phase", "");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    // ---------- amigos simulados ----------
    private class Fake { public NetConn c; public string name; public int flags; public float speed; public Vector3 pos; public float yaw; public bool readySent; }
    private static readonly List<Fake> fakes = new List<Fake>();
    private static float nextState;

    private static void PumpFakes()
    {
        NetSession ns = NetSession.Instance;
        foreach (Fake f in fakes)
        {
            if (f.c == null || f.c.Dead) continue;
            f.c.Pump(0f);
            while (f.c.Incoming.Count > 0) f.c.Incoming.Dequeue();
            if (ns != null && ns.InGame && ns.WorldEpoch == ns.Epoch && ns.WorldEpoch != 0)
            {
                if (!f.readySent) { f.c.Send(new NetWriter(NetMsg.Ready).Int(ns.Epoch).Long((long)ns.LocalWorldHash).ToArray()); f.readySent = true; }
                if (Time.unscaledTime >= nextState)
                    f.c.Send(new NetWriter(NetMsg.State).Int(ns.Epoch).Vec3(f.pos).Float(f.yaw).Float(0f).Float(f.speed).Byte(f.flags).ToArray());
            }
            f.c.Flush();
        }
        if (Time.unscaledTime >= nextState) nextState = Time.unscaledTime + 0.04f;
    }

    private static IEnumerator Join(string name, int port)
    {
        NetConnector cn = new NetConnector("127.0.0.1", port, 3000);
        while (!cn.Done) yield return 0.05f;
        NetConn nc = new NetConn(cn.Result, 0f);
        nc.Send(new NetWriter(NetMsg.Hello).Str(NetSession.Protocol).Str(name).ToArray()); nc.Flush();
        fakes.Add(new Fake { c = nc, name = name });
    }

    // ---------- guion ----------
    private static IEnumerator Run()
    {
        yield return 3f;
        UIManager ui = UIManager.Instance;
        SetupUi();
        yield return 1.0f;
        Capture("/workspace/monster-previews/mp_main_menu.png");
        ui.ClickButton("Multijugador"); yield return 0.8f;
        MultiplayerUI mui = ui.Multiplayer;
        mui.NameField.text = "Carlos";
        SetupUi(); yield return 0.5f;
        Capture("/workspace/monster-previews/mp_menu.png");
        ui.ClickButton("Alojar partida"); yield return 0.8f;
        mui.HostPortField.text = "47794";
        SetupUi(); yield return 0.5f;
        Capture("/workspace/monster-previews/mp_host.png");
        ui.ClickButton("Unirse"); // no hace nada en la página Host; volvemos
        ui.PressEscape(); yield return 0.3f;
        ui.ClickButton("Unirse"); yield return 0.6f;
        mui.JoinIpField.text = "192.168.1.34";
        SetupUi(); yield return 0.5f;
        Capture("/workspace/monster-previews/mp_join.png");
        ui.PressEscape(); yield return 0.3f;
        ui.ClickButton("Alojar partida"); yield return 0.4f;
        mui.HostPortField.text = "47794";
        ui.ClickButton("Crear sala"); yield return 0.5f;
        { IEnumerator j = Join("Marta", 47794); while (j.MoveNext()) yield return 0.05f; }
        { IEnumerator j = Join("Luis", 47794); while (j.MoveNext()) yield return 0.05f; }
        yield return 1.0f;
        SetupUi(); yield return 0.6f;
        Capture("/workspace/monster-previews/lobby_preview.png");

        // partida
        mui.OnStartGame();
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded || GameManager.Instance == null) yield return 0.3f;
        NetSession ns = NetSession.Instance;
        float tw = 0; while ((ns.WorldEpoch != ns.Epoch) && tw < 30f) { tw += 0.2f; yield return 0.2f; }
        yield return 2.0f;
        // sitúa a los amigos delante del jugador
        FirstPersonController fpc = Object.FindObjectOfType<FirstPersonController>();
        Camera cam = Camera.main;
        Vector3 fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);
        Vector3 floor = fpc.transform.position; floor.y = fpc.transform.position.y - 0.9f;
        RaycastHit hit;
        if (Physics.Raycast(fpc.transform.position, Vector3.down, out hit, 4f)) floor.y = hit.point.y;
        float camYaw = cam.transform.eulerAngles.y;
        Fake m = fakes[0], l = fakes[1];
        m.pos = floor + fwd * 3.4f + right * 0.9f; m.yaw = camYaw + 200f; m.speed = 0f; m.flags = RemotePlayer.FlagFlash;
        l.pos = floor + fwd * 5.5f - right * 1.2f; l.yaw = camYaw + 160f; l.speed = 0f; l.flags = RemotePlayer.FlagCrouch;
        yield return 2.5f;
        SetupUi(); yield return 0.6f;
        Capture("/workspace/monster-previews/mp_ingame_preview.png");
        // caminando
        m.speed = 2.2f; m.flags = RemotePlayer.FlagFlash;
        for (int i = 0; i < 25; i++) { m.pos += -fwd * 0.05f; yield return 0.04f; }
        yield return 0.4f;
        SetupUi();
        Capture("/workspace/monster-previews/mp_ingame_preview2.png");
        yield return 0.5f;
        NetSession.Instance.Leave(null);
        yield return 0.5f;
    }

    private static Camera uiCam;

    private static void SetupUi()
    {
        if (uiCam == null)
        {
            GameObject ugo = new GameObject("PreviewUiCam");
            uiCam = ugo.AddComponent<Camera>();
            uiCam.clearFlags = CameraClearFlags.Depth;
            uiCam.cullingMask = 1 << 31;
            uiCam.orthographic = true;
            uiCam.depth = 100;
            uiCam.enabled = false;
        }
        foreach (Canvas c in Object.FindObjectsOfType<Canvas>())
        {
            if (!c.isRootCanvas || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = uiCam; c.planeDistance = 1f;
            foreach (Transform t in c.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
        }
    }

    private static void Capture(string path)
    {
        const int W = 1280, H = 720;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        if (game == null) { Debug.LogError("[LobbyPreview] sin cámara principal"); return; }
        RenderTexture oldTarget = game.targetTexture;
        game.targetTexture = rt;
        game.Render();
        game.targetTexture = oldTarget;
        Canvas.ForceUpdateCanvases();
        uiCam.targetTexture = rt;
        uiCam.Render();
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        RenderTexture.ReleaseTemporary(rt);
        Debug.Log("[LobbyPreview] captura " + path);
    }
}
