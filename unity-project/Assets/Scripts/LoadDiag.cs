using System.Diagnostics;
using System.Threading;
using UnityEngine;

// v7.0.3: tiempos de carga paso a paso en el Player.log («[Carga] ...»). Un hilo vigía escribe cada 5 s si un paso
// sigue sin terminar, para que un log de un PC real diga exactamente dónde se va el tiempo.
public static class LoadDiag
{
    private static readonly Stopwatch total = new Stopwatch(), step = new Stopwatch();
    private static volatile string current = "";
    private static volatile bool active;
    private static string level = "";
    private static Thread watchdog;
    public static float LastTotalSeconds { get; private set; }
    public static int StepCount { get; private set; }
    public static string Report { get; private set; }
    private static readonly System.Text.StringBuilder sb = new System.Text.StringBuilder();

    public static void Begin(string lvl)
    {
        level = lvl; total.Restart(); step.Restart(); current = "inicio"; active = true; StepCount = 0; sb.Length = 0;
        UnityEngine.Debug.Log("[Carga] " + level + ": empieza (" + SystemInfo.graphicsDeviceType + ", " + SystemInfo.graphicsDeviceName + ", calidad " + GameSettings.Quality + ", " + SystemInfo.processorCount + " hilos)");
        if (watchdog == null)
        {
            watchdog = new Thread(Watch) { IsBackground = true, Name = "LoadDiagWatchdog" };
            watchdog.Start();
        }
    }

    // cierra el paso en curso (anotando su duración) y abre el siguiente
    public static void Step(string next)
    {
        if (!active) return;
        double ms = step.Elapsed.TotalMilliseconds;
        string line = current + " " + ms.ToString("F0") + " ms";
        sb.Append(line).Append(" | ");
        UnityEngine.Debug.Log("[Carga] " + level + ": " + line + " (total " + total.Elapsed.TotalSeconds.ToString("F2") + " s) -> " + next);
        current = next; step.Restart(); StepCount++;
    }

    public static void End(string extra)
    {
        if (!active) return;
        Step("fin");
        active = false;
        LastTotalSeconds = (float)total.Elapsed.TotalSeconds;
        Report = sb.ToString();
        UnityEngine.Debug.Log("[Carga] " + level + ": LISTO en " + LastTotalSeconds.ToString("F2") + " s" + (string.IsNullOrEmpty(extra) ? "" : " · " + extra));
    }

    private static void Watch()
    {
        double lastWarn = 0;
        while (true)
        {
            Thread.Sleep(1000);
            if (!active) { lastWarn = 0; continue; }
            double s = step.Elapsed.TotalSeconds;
            if (s >= 5.0 && s - lastWarn >= 5.0)
            {
                lastWarn = s;
                UnityEngine.Debug.LogWarning("[Carga] " + level + ": el paso «" + current + "» sigue en curso tras " + s.ToString("F0") + " s (total " + total.Elapsed.TotalSeconds.ToString("F0") + " s)");
            }
            if (s < lastWarn) lastWarn = 0;
        }
    }
}
