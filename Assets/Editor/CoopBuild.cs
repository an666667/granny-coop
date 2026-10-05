using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GrannyCoop.EditorTools
{
    /// <summary>
    /// Headless Android build entry point for CI (GitHub Actions + GameCI).
    /// Also callable from the editor menu: Coop -> Build Android APK.
    /// </summary>
    public static class CoopBuild
    {
        static readonly string[] Scenes =
        {
            "Assets/HorrorEyes/HE Project/Scenes/GameStartScene.unity",
            "Assets/HorrorEyes/HE Project/Scenes/MainMenu.unity",
            "Assets/HorrorEyes/HE Project/Scenes/LoadScene.unity",
            "Assets/HorrorEyes/HE Project/Scenes/Morgue.unity",
            "Assets/HorrorEyes/HE Project/Scenes/School_Demo.unity",
        };

        [MenuItem("Coop/Build Android APK")]
        public static void BuildAndroid()
        {
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "build", "Android");
            Directory.CreateDirectory(outDir);
            string outPath = Path.Combine(outDir, "granny-coop.apk");

            // match the project's original ABI set (ARMv7 + ARM64)
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel22;
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.useCustomKeystore = false;   // debug keystore, fine for testing

            var opts = new BuildPlayerOptions
            {
                scenes = Scenes,
                locationPathName = outPath,
                target = BuildTarget.Android,
                options = BuildOptions.None,
            };

            Debug.Log("[CoopBuild] building -> " + outPath);
            BuildReport report = BuildPipeline.BuildPlayer(opts);
            BuildSummary s = report.summary;
            Debug.Log("[CoopBuild] result=" + s.result + " size=" + s.totalSize + " time=" + s.totalTime);

            if (s.result != BuildResult.Succeeded)
            {
                foreach (var step in report.steps)
                    foreach (var msg in step.messages)
                        if (msg.type == LogType.Error || msg.type == LogType.Exception)
                            Debug.LogError("[CoopBuild] " + msg.content);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // GameCI's validateBuild() only accepts the literal marker
            // "Build succeeded!" (Unity itself prints "Build succeeded" without
            // the bang on some versions). BuildResult.Succeeded was already
            // verified above, so this just gives the CLI its expected marker.
            Debug.Log("Build succeeded!");

            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
