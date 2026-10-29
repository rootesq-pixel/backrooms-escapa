using UnityEngine;

// v7.10.3 (pt522u / pt524u): pasos REALES. Cada superficie tiene 5 tomas en Resources/Steps/<Tipo>_<i>.wav (mono 22 kHz, sin
// retumbo < 45 Hz, nada por encima de 7 kHz —sin siseo—, cola recortada con fundido; mismo volumen por pisada que antes o algo
// menos). Licencias en CREDITS.md. Preparadas con Tools/audio/prep_steps.py (moqueta, hormigón) y prep_steps2.py (el resto):
//   Carpet   = moqueta (Kenney, CC0)                    Concrete = hormigón (Kenney, CC0)
//   Grass    = suelo de madera de las casas del Campo: tacón (hormigón Kenney) + golpe grave de tarima (swuing, CC-BY 3.0)
//              + cuerpo hueco del entarimado (modos 118–515 Hz y «toc» 640–1330 Hz que se apagan en 10–50 ms: SIN crujidos)
//   Metal    = suelo industrial: pisada de hormigón + chapa apoyada muy amortiguada (modos 165–780 Hz, 12–30 ms) y un roce
//              metálico mínimo (1,2–1,7 kHz, 9–12 ms): pisada sorda, no martillazo
//   Sand     = arena (Fantozzi, CC0): más grave y apagada (paso bajo a 2 kHz + compresión sorda), ataque suave, sin siseo
//   Water    = charco / agua poco profunda (C-Dogs «water»: EminYILDIRIM + swuing, CC-BY 3.0): salpicadura suave con ataque
//              de 4 ms y pico limitado (antes un «bloop» sintético con chasquido). Poolrooms y Talasofobia: solo cambia esto.
// Cada pisada elige una toma al azar (nunca la misma dos veces seguidas) con un poco de tono y fuerza distintos.
public static class StepBank
{
    public const int Takes = 5;
    private static readonly AudioClip[][] cache = new AudioClip[6][];

    public static bool Real(StepKind k) { return true; }
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
