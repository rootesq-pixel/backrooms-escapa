using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// v7.4.0: diagnóstico «salir andando de la cabina» en todos los niveles y varias semillas (en tiempo de juego).
//   V740W_OUT=/workspace/v740/walk/x.txt V740W_CASES="4:53890220,0:1,..." V740W_DT=0|0.1|0.33  -executeMethod V740Walk.Run
[InitializeOnLoad]
public static class V740Walk
{
    private static IEnumerator routine; private static double waitUntil; private static System.Func<bool> waitFn;
    static string Out { get { return System.Environment.GetEnvironmentVariable("V740W_OUT") ?? "/workspace/v740/walk/walk.txt"; } }
    static V740Walk() { if (SessionState.GetString("smoke_phase", "") != "v740walk") return; EditorApplication.update += Tick; }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        SessionState.SetString("smoke_phase", "v740walk");
        EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if (!EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (routine == null) routine = Main();
        if (EditorApplication.timeSinceStartup < waitUntil) return;
        if (waitFn != null) { if (!waitFn()) return; waitFn = null; }
        try
        {
            if (!routine.MoveNext()) Finish();
            else if (routine.Current is float) waitUntil = EditorApplication.timeSinceStartup + (float)routine.Current;
            else if (routine.Current is System.Func<bool>) waitFn = (System.Func<bool>)routine.Current;
        }
        catch (System.Exception e) { W("ERROR " + e); Finish(); }
    }
    private static void Finish() { Time.captureDeltaTime = 0f; SessionState.SetString("smoke_phase", ""); EditorApplication.update -= Tick; W("FIN"); EditorApplication.Exit(0); }
    static readonly System.Text.StringBuilder sb = new System.Text.StringBuilder();
    static void W(string s) { sb.AppendLine(s); Directory.CreateDirectory(Path.GetDirectoryName(Out)); File.WriteAllText(Out, sb.ToString()); Debug.Log("[V740Walk] " + s); }

    static string Blocker(FirstPersonController p, Vector3 dir)
    {
        CharacterController cc = p.GetComponent<CharacterController>();
        Vector3 c = p.transform.TransformPoint(cc.center);
        float h = Mathf.Max(cc.height * 0.5f - cc.radius, 0f);
        Vector3 a = c + Vector3.up * h, b = c - Vector3.up * h;
        RaycastHit[] hs = Physics.CapsuleCastAll(a, b, cc.radius * 0.98f, dir, 0.6f, ~0, QueryTriggerInteraction.Ignore);
        List<string> l = new List<string>();
        foreach (RaycastHit hh in hs) if (hh.collider != null && hh.collider.transform.root != p.transform.root && !hh.collider.transform.IsChildOf(p.transform))
            l.Add(hh.collider.name + "@" + hh.distance.ToString("F2") + " n" + hh.normal.ToString("F2") + " p" + hh.point.ToString("F2") + " (" + (hh.collider.transform.parent != null ? hh.collider.transform.parent.name : "-") + ")");
        Collider[] ov = Physics.OverlapCapsule(a, b, cc.radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider o in ov) if (o.transform.root != p.transform.root && !o.transform.IsChildOf(p.transform)) l.Add("OVERLAP " + o.name);
        return l.Count == 0 ? "(nada)" : string.Join("; ", l.ToArray());
    }

    private static IEnumerator Main()
    {
        GameSettings.ResetDefaults();
        ElevatorCabin.InstantForTests = true;
        GameBootstrap gb = GameBootstrap.Instance; float w0 = 0f;
        while ((gb == null || UIManager.Instance == null || !gb.IsLoaded || !gb.InMenu) && w0 < 120f) { yield return 0.2f; w0 += 0.2f; gb = GameBootstrap.Instance; }
        string cases = System.Environment.GetEnvironmentVariable("V740W_CASES") ?? "";
        float fdt = float.Parse(System.Environment.GetEnvironmentVariable("V740W_DT") ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        W("casos " + cases + " dt " + fdt);
        bool first = true; int fails = 0, n = 0;
        foreach (string cs in cases.Split(','))
        {
            if (cs.Trim() == "") continue;
            string[] pp = cs.Split(':'); int lv = int.Parse(pp[0]); int s = int.Parse(pp[1]);
            gb.useRandomSeed = false; gb.seed = s - lv * 101;
            int ld = gb.LoadCount;
            if (first) { UIManager.Instance.StartGame(lv); first = false; } else gb.LoadLevel(lv);
            float wt = 0f;
            while (!(gb.LoadCount > ld && gb.IsLoaded && GameManager.Instance != null && !gb.InMenu) && wt < 240f) { yield return 0.2f; wt += 0.2f; }
            float tg0 = Time.time; yield return (System.Func<bool>)(() => Time.time - tg0 >= 1.6f);
            GameManager gm = GameManager.Instance; ElevatorCabin cb = ElevatorCabin.Current;
            if (gm == null || cb == null) { W(cs + " sin cabina"); continue; }
            foreach (MonsterAI mm in gm.monsters) if (mm != null) mm.Stop();
            FirstPersonController p = gm.player;
            p.Teleport(gm.generator.PlayerStart, gm.generator.PlayerStartYaw);
            yield return (System.Func<bool>)(() => true); yield return null;
            Vector3 cp = cb.transform.position, fw = cb.Forward;
            float exitA = Vector3.Dot(cb.ExitPoint - cp, fw);
            float a0 = Vector3.Dot(p.transform.position - cp, fw);
            float ang = Vector3.Angle(p.transform.forward, fw);
            if (fdt > 0f) Time.captureDeltaTime = fdt;
            p.debugInput = true; p.debugMove = new Vector2(0f, 1f);
            float t0 = Time.time; float lastA = a0; float stuckT = 0f; string blk = ""; float stuckAt = -99f; int frames = 0;
            System.Text.StringBuilder tr = new System.Text.StringBuilder();
            while (Time.time - t0 < 2.6f)
            {
                yield return null; frames++;
                float a = Vector3.Dot(p.transform.position - cp, fw);
                if (frames % Mathf.Max(1, Mathf.RoundToInt(0.2f / Mathf.Max(Time.deltaTime, 1e-4f))) == 0) tr.Append(a.ToString("F2")).Append(' ');
                if (Time.deltaTime > 0f && (a - lastA) / Time.deltaTime < 0.3f && Time.time - t0 > 0.4f) { stuckT += Time.deltaTime; if (stuckT > 0.25f && blk == "") { blk = Blocker(p, fw); stuckAt = a; } } else stuckT = 0f;
                lastA = a;
                if (!p.InputEnabled || UIManager.InputBlocked || p.IsMounted) blk += " [input " + p.InputEnabled + " blocked " + UIManager.InputBlocked + " mounted " + p.IsMounted + "]";
            }
            p.debugInput = false; Time.captureDeltaTime = 0f;
            float al = Vector3.Dot(p.transform.position - cp, fw);
            bool ok = al > 1.5f && al - a0 > 1.5f;
            n++; if (!ok || blk != "") fails += ok ? 0 : 1;
            W((ok ? "OK  " : "MAL ") + "L" + lv + " semilla " + gm.generator.UsedSeed + " avance " + al.ToString("F2") + " (inicio " + a0.ToString("F2") + ", ExitPoint " + exitA.ToString("F2") + ", giro " + ang.ToString("F0") + "°, " + frames + " fot.) open " + cb.Open01.ToString("F2") + " locked " + cb.Locked + " blockDoor " + cb.CollidersBlockDoor
              + (blk != "" ? " ATASCO a " + stuckAt.ToString("F2") + ": " + blk : "") + " | tray " + tr);
        }
        W("RESUMEN casos " + n + " fallos " + fails);
    }
}
