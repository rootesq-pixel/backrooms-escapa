// v7.5.0: versión del juego en el propio código (la misma que PlayerSettings.bundleVersion; SmokeTest lo comprueba).
// La usan la esquina del menú principal y el auto-actualizador para compararla con la última versión publicada en GitHub.
public static class GameVersion
{
    public const string Current = "7.10.1";
    public static string Label { get { return "v" + Current; } }
}
