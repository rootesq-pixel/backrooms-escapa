using UnityEngine;

// v7.10.3 (pt522u): pasos REALES. Cada superficie tiene 5 grabaciones de pisadas CC0 (Kenney «Impact Sounds», ver CREDITS.md)
// en Resources/Steps/<Tipo>_<i>.wav, preparadas con Tools/audio/prep_steps.py (mono 22 kHz, sin retumbo < 45 Hz, sin nada por
// encima de 7 kHz —sin siseo—, cola recortada con fundido, mismo volumen que los pasos de antes o algo menos):
//   Carpet = moqueta, Concrete = hormigón, Grass = suelo de madera de las casas del Campo (golpe sordo, SIN crujidos),
//   Metal = pisada de hormigón + vibración grave de una plancha (nivel industrial), Sand = nieve apagada + moqueta (arena blanda).
// Cada pisada elige una toma al azar (nunca la misma dos veces seguidas) con un poco de tono y fuerza distintos, así que no
// suenan dos pasos iguales. El agua (Poolrooms y Talasofobia) sigue con su paso de antes + chapoteo (Poolrooms intacto).
public static class StepBank
{
    public const int Takes = 5;
    private static readonly AudioClip[][] cache = new AudioClip[6][];

    public static bool Real(StepKind k) { return k != StepKind.Water; }
    public static int Count(StepKind k) { return Real(k) ? Takes : 0; }

    public static AudioClip Get(StepKind k, int i)
    {
        if (!Real(k)) return null;
        int ki = (int)k;
        if (cache[ki] == null) cache[ki] = new AudioClip[Takes];
        if (cache[ki][i] == null) cache[ki][i] = Resources.Load<AudioClip>("Steps/" + k + "_" + i);
        return cache[ki][i];
    }

    // toma al azar sin repetir la anterior; null si la superficie no tiene grabaciones (agua) o faltan los recursos
    public static AudioClip Next(StepKind k, ref int last)
    {
        if (!Real(k)) return null;
        int i = Random.Range(0, Takes);
        if (i == last) i = (i + 1 + Random.Range(0, Takes - 1)) % Takes;
        last = i;
        return Get(k, i);
    }

    // variación por pisada: tono ±5 % y fuerza ±1,5 dB (las grabaciones ya son distintas entre sí)
    public static float Pitch() { return Random.Range(0.95f, 1.05f); }
    public static float Gain() { return Random.Range(0.84f, 1f); }
}
