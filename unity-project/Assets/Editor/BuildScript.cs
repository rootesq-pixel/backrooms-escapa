using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

// Script de build por línea de comandos:
// Unity -batchmode -nographics -quit -projectPath <proyecto> -executeMethod BuildScript.BuildWindows
public static class BuildScript
{
    private const string ScenePath = "Assets/Scenes/Main.unity";
    // v7.1.1: BUILD_OUTPUT (ruta del .exe) permite compilar en otra máquina (p. ej. el PC con Windows)
    private static string OutputPath { get { string e = Environment.GetEnvironmentVariable("BUILD_OUTPUT"); return string.IsNullOrEmpty(e) ? "/workspace/BackroomsEscape_Build/BackroomsEscape.exe" : e; } }

    public static void BuildWindows()
    {
        ConfigurePlayerSettings();
        EnsureAlwaysIncludedShaders();
        ShaderVariantKeeper.Generate();
        EnsureFogVariantsKept();

        string outDir = Path.GetDirectoryName(OutputPath);
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutputPath,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.None // sin Development Build
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log("Build result: " + summary.result + " size=" + summary.totalSize + " bytes, errors=" + summary.totalErrors + ", warnings=" + summary.totalWarnings);
        if (summary.result != BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }

    private static void ConfigurePlayerSettings()
    {
        PlayerSettings.productName = "Backrooms Escape";
        PlayerSettings.companyName = "Pixel";
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        PlayerSettings.defaultIsNativeResolution = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.Mono2x);
        // Sin pantalla de Unity: se pide desactivarla; con la licencia Personal Unity puede forzarla (no se elude la licencia).
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;
        PlayerSettings.SplashScreen.logos = new PlayerSettings.SplashScreenLogo[0];
        PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
        Debug.Log("[Splash] SplashScreen.show = " + PlayerSettings.SplashScreen.show + ", showUnityLogo = " + PlayerSettings.SplashScreen.showUnityLogo + ", licencia Pro/Plus = " + Application.HasProLicense());
    }

    // Los materiales se crean en runtime y la niebla se activa por código: hay que conservar las variantes de niebla
    // (Fog Modes = Custom con Linear/Exp/Exp2 incluidos) o el shader Standard las eliminaría en el build.
    private static void EnsureFogVariantsKept()
    {
        const string path = "ProjectSettings/GraphicsSettings.asset";
        UnityEngine.Object gs = AssetDatabase.LoadAllAssetsAtPath(path)[0];
        SerializedObject so = new SerializedObject(gs);
        SerializedProperty fogStripping = so.FindProperty("m_FogStripping");
        if (fogStripping != null) fogStripping.intValue = 1; // 1 = Custom
        string[] keep = { "m_FogKeepLinear", "m_FogKeepExp", "m_FogKeepExp2" };
        for (int i = 0; i < keep.Length; i++)
        {
            SerializedProperty p = so.FindProperty(keep[i]);
            if (p != null) p.boolValue = true;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }

    // Añade "Standard" a Always Included Shaders para que no se elimine en el build
    // (el juego crea materiales por código con Shader.Find).
    private static void EnsureAlwaysIncludedShaders()
    {
        Shader standard = Shader.Find("Standard");
        if (standard == null)
        {
            Debug.LogWarning("Standard shader not found");
            return;
        }
        // El shader del efecto VHS también se incluye siempre (además de estar en Resources)
        Shader vhs = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/VHS/VhsPost.shader");

        const string path = "ProjectSettings/GraphicsSettings.asset";
        UnityEngine.Object gs = AssetDatabase.LoadAllAssetsAtPath(path)[0];
        SerializedObject so = new SerializedObject(gs);
        SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
        foreach (Shader wanted in new[] { standard, vhs })
        {
            if (wanted == null) continue;
            bool present = false;
            for (int i = 0; i < arr.arraySize; i++)
            {
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == wanted) { present = true; break; }
            }
            if (present) continue;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = wanted;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
    }
}
