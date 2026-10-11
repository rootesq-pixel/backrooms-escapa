using System.Collections.Generic;
using UnityEngine;

// Limita los zumbidos de los fluorescentes: antes sonaban TODOS a la vez (decenas de bucles 3D apilados, hasta 9 m cada uno);
// ahora solo suenan los más cercanos (máx. MaxBuzzing, a menos de HearDistance), con fundido de entrada y salida.
public static class AudioBuzzLimiter
{
    public const int MaxBuzzing = 3;
    public const float HearDistance = 6f;

    private static readonly List<FlickeringLight> lights = new List<FlickeringLight>();
    private static readonly List<KeyValuePair<float, FlickeringLight>> tmp = new List<KeyValuePair<float, FlickeringLight>>();
    private static float next;
    public static bool Disabled;   // solo para la auditoría «antes/después» en el editor

    public static void Register(FlickeringLight f) { if (f != null && !lights.Contains(f)) lights.Add(f); }
    public static void Unregister(FlickeringLight f) { lights.Remove(f); }
    public static int Registered { get { return lights.Count; } }

    public static void Tick()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 0.15f;
        AudioListener al = Object.FindObjectOfType<AudioListener>();
        Vector3 p = al != null ? al.transform.position : Vector3.zero;
        tmp.Clear();
        for (int i = lights.Count - 1; i >= 0; i--)
        {
            FlickeringLight f = lights[i];
            if (f == null) { lights.RemoveAt(i); continue; }
            if (f.buzz == null) continue;
            f.buzzGateTarget = Disabled ? 1f : 0f;
            if (Disabled) continue;
            tmp.Add(new KeyValuePair<float, FlickeringLight>((f.transform.position - p).sqrMagnitude, f));
        }
        if (Disabled) return;
        tmp.Sort((a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < tmp.Count && i < MaxBuzzing; i++)
            if (tmp[i].Key < HearDistance * HearDistance) tmp[i].Value.buzzGateTarget = 1f;
    }
}
