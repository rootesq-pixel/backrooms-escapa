using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Renderiza /workspace/monster-previews/flashlight_hand.png: la linterna en la mano desde los ojos del jugador (encendida, apagada, andando,
// pegado a una pared (anti-clipping), instante del clic) y un primer plano exterior.
// Necesita gráficos:  xvfb-run -a Unity -batchmode -quit -projectPath <p> -executeMethod FlashlightPreview.Export -force-glcore
public static class FlashlightPreview
{
    private static void SetField(object o, string name, object v)
    {
        FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
        if (f != null) { f.SetValue(o, v); return; }
        f = o.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) f.SetValue(o, v);
        else Debug.LogWarning("[FlashlightPreview] campo no encontrado: " + name);
    }

    public static void Export()
    {
        Directory.CreateDirectory("/workspace/monster-previews");
        Shader std = Shader.Find("Standard");
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(0f, -0.05f, 0f); floor.transform.localScale = new Vector3(14f, 0.1f, 14f);
        floor.GetComponent<Renderer>().sharedMaterial = new Material(std) { color = new Color(0.50f, 0.45f, 0.26f) };
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0f, 1.5f, 4.5f); wall.transform.localScale = new Vector3(14f, 3f, 0.1f);
        wall.GetComponent<Renderer>().sharedMaterial = new Material(std) { color = new Color(0.62f, 0.56f, 0.32f) };
        GameObject near = GameObject.CreatePrimitive(PrimitiveType.Cube);
        near.transform.position = new Vector3(0f, 1.5f, 0.78f); near.transform.localScale = new Vector3(14f, 3f, 0.1f);
        near.GetComponent<Renderer>().sharedMaterial = wall.GetComponent<Renderer>().sharedMaterial;
        near.SetActive(false);
        // ambiente oscuro como en el juego + una luz tenue de techo
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.20f, 0.18f, 0.12f);
        GameObject top = new GameObject("Top"); Light tl = top.AddComponent<Light>(); tl.type = LightType.Point; tl.range = 9f; tl.intensity = 0.55f; tl.color = new Color(1f, 0.93f, 0.7f);
        top.transform.position = new Vector3(0f, 2.8f, 1.5f);

        GameObject player = new GameObject("Player");
        player.AddComponent<CharacterController>();
        FirstPersonController fpc = player.AddComponent<FirstPersonController>();
        GameObject camGo = new GameObject("PlayerCamera");
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Camera cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.08f; cam.fieldOfView = 70f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.03f, 0.03f, 0.02f);
        Flashlight fl = camGo.AddComponent<Flashlight>();
        GameObject sg = new GameObject("FlashlightSpot");
        sg.transform.SetParent(camGo.transform, false);
        Light spot = sg.AddComponent<Light>();
        spot.type = LightType.Spot; spot.range = 22f; spot.spotAngle = 60f; spot.intensity = 2.5f; spot.color = new Color(1f, 0.96f, 0.82f); spot.shadows = LightShadows.None;
        fl.spot = spot;
        SetField(fl, "battery", 100f);
        SetField(fl, "IsOn", true);
        fpc.cameraPivot = camGo.transform;

        PlayerBody body = PlayerBody.Create(fpc, cam, fl);
        if (body == null || !body.Loaded) { Debug.LogError("[FlashlightPreview] cuerpo no cargado"); return; }
        body.dtOverride = 0.03f;
        {
            Transform t0 = body.RightHand; string path = "";
            while (t0 != null && t0 != body.transform) { path = t0.name + "/" + path; t0 = t0.parent; }
            Debug.Log("[FlashlightPreview] hand path " + path);
            Transform wr = body.RightHand.parent;
            foreach (Transform ch in wr.GetComponentsInChildren<Transform>()) Debug.Log("[FlashlightPreview] bone " + ch.name + " pos=" + body.transform.InverseTransformPoint(ch.position) + " lossy=" + ch.lossyScale);
        }
        if (System.Environment.GetEnvironmentVariable("FL_DEBUG") == "1")
            foreach (Renderer r in body.FlashlightProp.GetComponentsInChildren<Renderer>())
                if (r.name != "Lens" && r.name != "LensGlow") { r.sharedMaterial.color = Color.red; r.sharedMaterial.EnableKeyword("_EMISSION"); r.sharedMaterial.SetColor("_EmissionColor", new Color(0.8f, 0f, 0f)); }

        const int W = 960, H = 540;
        RenderTexture rt = new RenderTexture(W, H, 24);
        string[] names = { "encendida", "apagada", "andando", "pared", "clic", "exterior", "ext_frente", "ext_lado", "ext_arriba" };
        Texture2D[] tiles = new Texture2D[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            bool on = i != 1;
            bool walk = i == 2;
            float pitch = i == 5 ? 6f : 6f;
            near.SetActive(i == 3);
            Physics.SyncTransforms();
            spot.enabled = on;
            SetField(fl, "IsOn", on);
            SetField(fpc, "pitch", pitch);
            camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camGo.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            spot.transform.rotation = camGo.transform.rotation;
            SetField(fpc, "FlatSpeed", walk ? 3.5f : 0f);
            SetField(fpc, "IsCrouching", false);
            SetField(fpc, "IsSprinting", false);
            for (int k = 0; k < 40; k++) { body.Tick(); body.animator.Update(0.03f); }
            if (i == 4) { SetField(body, "clickT", 0.5f); }
            body.Tick();
            if (i == 0) foreach (Renderer r in body.FlashlightProp.GetComponentsInChildren<Renderer>()) Debug.Log("[FlashlightPreview] prop r " + r.name + " en=" + r.enabled + " ctr=" + r.bounds.center + " size=" + r.bounds.size + " shader=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null"));
            Debug.Log("[FlashlightPreview] " + names[i] + " estado=" + body.CurrentState + " prop=" + body.FlashlightProp.transform.position + " lente=" + body.LensTransform.position + " luzEnLente=" + body.LightAtLens
                + " glow=" + body.LensGlow + " clip=" + body.ClipPull + " kick=" + body.ClickKick + " cam=" + camGo.transform.position + " ik=" + body.IkActive + " handErr=" + Vector3.Distance(body.RightHand.position, body.HandTarget) + " gripAngle=" + body.GripAngle);

            Camera use = cam;
            if (i >= 5)
            {
                GameObject c2 = new GameObject("ext");
                use = c2.AddComponent<Camera>();
                use.clearFlags = CameraClearFlags.SolidColor; use.backgroundColor = cam.backgroundColor;
                Vector3 pc = body.FlashlightProp.transform.position + body.FlashlightProp.transform.forward * 0.03f;
                Vector3[] offs = { new Vector3(-0.38f, 0.14f, 0.34f), new Vector3(0f, 0.05f, 0.9f), new Vector3(-0.9f, 0.05f, 0f), new Vector3(0f, 0.9f, 0.1f) };
                c2.transform.position = pc + offs[i - 5];
                c2.transform.LookAt(pc); if (i == 8) c2.transform.rotation = Quaternion.LookRotation(pc - c2.transform.position, Vector3.forward);
                use.fieldOfView = i == 5 ? 30f : 40f; use.nearClipPlane = 0.03f;
                cam.enabled = false;
            }
            use.targetTexture = rt;
            use.Render();
            RenderTexture.active = rt;
            Texture2D t = new Texture2D(W, H, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, W, H), 0, 0); t.Apply();
            RenderTexture.active = null;
            use.targetTexture = null;
            tiles[i] = t;
            File.WriteAllBytes("/workspace/monster-previews/flashlight_hand_" + names[i] + ".png", t.EncodeToPNG());
            if (i >= 5) Object.DestroyImmediate(use.gameObject);
        }
        Texture2D sheet = new Texture2D(W * 3, H * 2, TextureFormat.RGB24, false);
        for (int i = 0; i < 6; i++)
            sheet.SetPixels((i % 3) * W, (1 - i / 3) * H, W, H, tiles[i].GetPixels());
        File.WriteAllBytes("/workspace/monster-previews/flashlight_hand.png", sheet.EncodeToPNG());
        Debug.Log("[FlashlightPreview] OK");
    }
}
