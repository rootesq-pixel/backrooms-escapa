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
    // v7.1.0: zumbidos que no son FlickeringLight (fluorescentes de las salas de bolas) entran en el mismo reparto
    private static readonly List<AudioSource> ext = new List<AudioSource>();
    private static readonly HashSet<AudioSource> extOn = new HashSet<AudioSource>();
    private static readonly List<KeyValuePair<float, AudioSource>> tmpE = new List<KeyValuePair<float, AudioSource>>();
    public static void RegisterExternal(AudioSource a) { if (a != null && !ext.Contains(a)) ext.Add(a); }
    public static bool ExternalOn(AudioSource a) { return Disabled || extOn.Contains(a); }

    static bool InMenuNow() { GameBootstrap gb = GameBootstrap.Instance; return gb != null && gb.InMenu; }
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
            f.buzzGateTarget = Disabled && !InMenuNow() ? 1f : 0f;
            if (Disabled) continue;
            tmp.Add(new KeyValuePair<float, FlickeringLight>((f.transform.position - p).sqrMagnitude, f));
        }
        if (Disabled) return;
        if (InMenuNow()) { extOn.Clear(); return; }   // v7.3.0b3: en el menú no suena ningún zumbido (el nivel de fondo del menú inicial tiene luces cerca)
        tmp.Sort((a, b) => a.Key.CompareTo(b.Key));
        tmpE.Clear(); extOn.Clear();
        for (int i = ext.Count - 1; i >= 0; i--) { if (ext[i] == null) { ext.RemoveAt(i); continue; } tmpE.Add(new KeyValuePair<float, AudioSource>((ext[i].transform.position - p).sqrMagnitude, ext[i])); }
        tmpE.Sort((a, b) => a.Key.CompareTo(b.Key));
        int li = 0, ei = 0;
        for (int n = 0; n < MaxBuzzing; n++)
        {
            bool takeL = li < tmp.Count && (ei >= tmpE.Count || tmp[li].Key <= tmpE[ei].Key);
            float d2 = takeL ? tmp[li].Key : (ei < tmpE.Count ? tmpE[ei].Key : float.MaxValue);
            if (d2 >= HearDistance * HearDistance) break;
            if (takeL) tmp[li++].Value.buzzGateTarget = 1f; else extOn.Add(tmpE[ei++].Value);
        }
    }
}
