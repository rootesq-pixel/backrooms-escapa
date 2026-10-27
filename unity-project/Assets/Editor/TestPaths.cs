// Carpeta de informes de las pruebas por lotes. Por defecto /workspace/logs; BE_LOG_DIR permite que dos copias del
// proyecto prueben a la vez sin pisarse los informes (p. ej. la rama de un nivel nuevo en otro árbol de trabajo).
public static class TestPaths
{
    public static readonly string Logs = (System.Environment.GetEnvironmentVariable("BE_LOG_DIR") ?? "/workspace/logs").TrimEnd('/');
}
