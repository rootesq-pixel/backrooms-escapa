using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

// Renderiza /workspace/monster-previews/player_hazmat.png: vistas desde los ojos del jugador (mirando al frente, hacia abajo, agachado)
// y una vista exterior (con la cabeza visible). Necesita gráficos:  xvfb-run -a Unity -batchmode -quit -projectPath <p> -executeMethod PlayerBodyPreview.Export -force-glcore
public static class PlayerBodyPreview
{
    private static void SetField(object o, string name, object v)
    {
        FieldInfo f = o.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) { f.SetValue(o, v); return; }
        f = o.GetType().GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) f.SetValue(o, v);
        else Debug.LogWarning("[PlayerBodyPreview] campo no encontrado: " + name);
    }

    public static void Export()
    {
        Directory.CreateDirectory("/workspace/monster-previews");
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(0f, -0.05f, 0f); floor.transform.localScale = new Vector3(12f, 0.1f, 12f);
        floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.55f, 0.5f, 0.3f) };
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = new Vector3(0f, 1.5f, 4f); wall.transform.localScale = new Vector3(12f, 3f, 0.1f);
        wall.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = new Color(0.6f, 0.56f, 0.34f) };
        GameObject lg = new GameObject("L"); Light l = lg.AddComponent<Light>(); l.type = LightType.Directional; l.intensity = 0.9f; l.shadows = LightShadows.Soft;
        lg.transform.rotation = Quaternion.Euler(60f, -25f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.6f, 0.58f, 0.5f);

        GameObject player = new GameObject("Player");
        player.AddComponent<CharacterController>();
        FirstPersonController fpc = player.AddComponent<FirstPersonController>();
        GameObject camGo = new GameObject("PlayerCamera");
        camGo.transform.SetParent(player.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Camera cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.08f; cam.fieldOfView = 70f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.08f, 0.07f, 0.05f);
        Flashlight fl = camGo.AddComponent<Flashlight>();
        fpc.cameraPivot = camGo.transform;

        PlayerBody body = PlayerBody.Create(fpc, cam, fl);
        if (body == null || !body.Loaded) { Debug.LogError("[PlayerBodyPreview] cuerpo no cargado"); return; }

        RenderTexture rt = new RenderTexture(960, 540, 24);
        Texture2D[] tiles = new Texture2D[6];
        string[] names = { "frente", "abajo", "abajo-andando", "agachado", "exterior", "exterior-correr" };
        for (int i = 0; i < 6; i++)
        {
            bool crouch = i == 3;
            bool walk = i == 2, run = i == 5;
            SetField(fpc, "pitch", i == 1 || i == 2 ? 62f : i == 3 ? 35f : 8f);
            camGo.transform.localPosition = new Vector3(0f, crouch ? 0.95f : 1.6f, 0f);
            camGo.transform.localRotation = Quaternion.Euler(i == 1 || i == 2 ? 62f : i == 3 ? 35f : 8f, 0f, 0f);
            SetField(fpc, "FlatSpeed", walk ? 3.5f : run ? 6f : 0f);
            SetField(fpc, "IsCrouching", crouch);
            SetField(fpc, "IsSprinting", run);
            // avanzar la animación unos fotogramas
            for (int k = 0; k < 40; k++) { body.Tick(); body.animator.Update(0.03f); }
            body.Tick();
            Debug.Log("[PlayerBodyPreview] " + names[i] + " estado=" + body.CurrentState + " prop=" + (body.FlashlightProp != null ? body.FlashlightProp.transform.position.ToString() + " activo=" + body.FlashlightProp.activeInHierarchy : "null") + " hand=" + body.RightHand.position + " cam=" + camGo.transform.position + " ik=" + body.IkActive + " head=" + body.Head.position);
            foreach (Renderer r in body.FlashlightProp.GetComponentsInChildren<Renderer>()) Debug.Log("[PlayerBodyPreview] prop r " + r.name + " en=" + r.enabled + " b=" + r.bounds + " mat=" + (r.sharedMaterial != null ? r.sharedMaterial.shader.name : "null") + " layer=" + r.gameObject.layer);
            Camera use = cam;
            if (i >= 4)
            {
                GameObject c2 = new GameObject("ext");
                use = c2.AddComponent<Camera>();
                use.clearFlags = CameraClearFlags.SolidColor; use.backgroundColor = cam.backgroundColor;
                c2.transform.position = new Vector3(1.9f, 1.3f, 2.1f);
                c2.transform.LookAt(new Vector3(0f, 1.0f, 0f));
                use.fieldOfView = 45f;
                if (i == 5) { c2.transform.position = body.RightHand.position + new Vector3(0.9f, 0.1f, 0.5f); c2.transform.LookAt(body.RightHand.position); use.fieldOfView = 30f; }
                // la cabeza se ve en la vista exterior
                foreach (Renderer r in body.GetComponentsInChildren<Renderer>()) if (r.name.ToLowerInvariant().Contains("head")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                cam.enabled = false;
            }
            use.targetTexture = rt;
            use.Render();
            RenderTexture.active = rt;
            Texture2D t = new Texture2D(960, 540, TextureFormat.RGB24, false);
            t.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); t.Apply();
            RenderTexture.active = null;
            use.targetTexture = null;
            tiles[i] = t;
            File.WriteAllBytes("/workspace/monster-previews/player_hazmat_" + names[i] + ".png", t.EncodeToPNG());
            if (i >= 4) Object.DestroyImmediate(use.gameObject);
        }
        // hoja de contactos 3x2
        Texture2D sheet = new Texture2D(960 * 3, 540 * 2, TextureFormat.RGB24, false);
        for (int i = 0; i < 6; i++)
        {
            int cx = (i % 3) * 960, cy = (1 - i / 3) * 540;
            sheet.SetPixels(cx, cy, 960, 540, tiles[i].GetPixels());
        }
        File.WriteAllBytes("/workspace/monster-previews/player_hazmat.png", sheet.EncodeToPNG());
        Debug.Log("[PlayerBodyPreview] OK");
    }
}
