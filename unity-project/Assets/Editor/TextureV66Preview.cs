using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Capturas antes/después de las texturas de Blender (v6.6): por cada nivel, vista de pasillo, pared de cerca y suelo,
// con texturas horneadas (after) y con las generadas por CPU (before) -> /workspace/texture-previews/v66_L<n>_<vista>_<before|after>.png
//   xvfb-run -a Unity -batchmode -projectPath <p> -executeMethod TextureV66Preview.Export -force-glcore
[InitializeOnLoad]
public static class TextureV66Preview
{
    private static IEnumerator routine;
    private static double waitUntil;

    static TextureV66Preview()
    {
        if (SessionState.GetString("smoke_phase", "") != "texprev66") return;
        EditorApplication.update += Tick;
    }

    public static void Export()
    {
        Directory.CreateDirectory("/workspace/texture-previews");
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "texprev66");
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Run();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        try
        {
            if (!routine.MoveNext()) Finish();
            else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current;
        }
        catch (System.Exception e) { Debug.LogError("[TextureV66Preview] " + e); Finish(); }
    }

    private static void Finish()
    {
        SessionState.SetString("smoke_phase", "");
        EditorApplication.update -= Tick;
        EditorApplication.Exit(0);
    }

    private static void Capture(string path)
    {
        const int W = 1280, H = 720;
        RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
        Camera game = Camera.main;
        RenderTexture old = game.targetTexture;
        game.targetTexture = rt; game.Render(); game.targetTexture = old;
        RenderTexture prev = RenderTexture.active; RenderTexture.active = rt;
        Texture2D tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex); RenderTexture.ReleaseTemporary(rt);
    }

    private static void Teleport(GameManager gm, Vector3 pos, float yaw)
    {
        CharacterController cc = gm.player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        gm.player.transform.position = pos;
        gm.player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cc != null) cc.enabled = true;
    }

    private static IEnumerator Run()
    {
        ElevatorCabin.InstantForTests = true;
        GameSettings.ResetDefaults();
        while (GameBootstrap.Instance == null || !GameBootstrap.Instance.IsLoaded) yield return 0.3f;
        yield return 2f;
        GameBootstrap.Instance.useRandomSeed = false;   // misma semilla en "antes" y "después"
        string d = "/workspace/texture-previews/";
        for (int lv = 0; lv < LevelConfig.Count; lv++)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool baked = pass == 0;
                ProceduralTextures.UseBaked = baked;
                TextureLibrary.ReleaseAll();
                GameBootstrap gb = GameBootstrap.Instance;
                int ld = gb.LoadCount;
                UIManager.Instance.StartGame(lv);
                while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu)) yield return 0.2f;
                yield return 1.0f;
                GameManager gm = GameManager.Instance;
                foreach (MonsterAI m in gm.monsters) m.Stop();
                Camera cam = Camera.main;
                VhsEffect vhs = cam.GetComponent<VhsEffect>(); if (vhs != null) vhs.enabled = false;
                Transform ct = cam.transform;
                string tag = "v66_L" + lv + "_";
                string suffix = baked ? "_after.png" : "_before.png";
                ElevatorCabin cab = ElevatorCabin.Current;
                Vector3 fwd = cab != null ? cab.Forward : gm.player.transform.forward;
                Vector3 feet = (cab != null ? cab.ExitPoint : gm.player.transform.position) + fwd * 2.5f;
                float yaw = Quaternion.LookRotation(new Vector3(fwd.x, 0, fwd.z)).eulerAngles.y;
                Teleport(gm, feet, yaw);
                yield return 0.6f;
                Vector3 eye = ct.position;
                // 1) pasillo (vista del jugador al salir del ascensor)
                ct.rotation = Quaternion.Euler(4f, yaw, 0f);
                Capture(d + tag + "pasillo" + suffix);
                // 2) pared de cerca
                Vector3 best = Vector3.zero, bestN = Vector3.zero; float bestD = -1f;
                for (int a = 0; a < 360; a += 15)
                {
                    Vector3 dir = Quaternion.Euler(0, a, 0) * Vector3.forward;
                    RaycastHit hit;
                    if (Physics.Raycast(eye, dir, out hit, 9f) && Mathf.Abs(hit.normal.y) < 0.2f && hit.distance > 2.0f && hit.distance > bestD) { bestD = hit.distance; best = hit.point; bestN = hit.normal; }
                }
                if (bestD > 0f)
                {
                    Vector3 f2 = best + bestN * 1.5f; f2.y = feet.y;
                    float wy = Quaternion.LookRotation(new Vector3(-bestN.x, 0, -bestN.z)).eulerAngles.y;
                    Teleport(gm, f2, wy);
                    yield return 0.6f;
                    ct.rotation = Quaternion.Euler(3f, wy, 0f);
                    Capture(d + tag + "pared" + suffix);
                }
                // 3) suelo
                Teleport(gm, feet, yaw);
                yield return 0.6f;
                ct.rotation = Quaternion.Euler(55f, yaw, 0f);
                Capture(d + tag + "suelo" + suffix);
                Debug.Log("[TextureV66Preview] nivel " + lv + (baked ? " horneadas" : " CPU") + " pared=" + (bestD > 0f));
            }
        }
        ProceduralTextures.UseBaked = true;
    }
}
