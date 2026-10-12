using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PVDesigner.EditorTools
{
    /// <summary>
    /// Build entry points. Used from the menu and by CI (game-ci/unity-builder with
    /// buildMethod: PVDesigner.EditorTools.BuildScript.BuildFromCommandLine).
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("PV Designer/Build/Windows x64", priority = 40)]
        public static void BuildWindowsMenu() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/PVDesigner.exe", null);

        [MenuItem("PV Designer/Build/Meta Quest (Android APK)", priority = 41)]
        public static void BuildQuestMenu() => Build(BuildTarget.Android, "Builds/Quest/PVDesigner-Quest.apk", null);

        /// <summary>
        /// Command line: -buildTarget StandaloneWindows64|Android -customBuildPath &lt;file&gt; [-buildVersion 1.2.3]
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name)
            {
                int i = Array.IndexOf(args, name);
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }

            var targetName = Arg("-buildTarget") ?? "StandaloneWindows64";
            if (!Enum.TryParse(targetName, true, out BuildTarget target))
                throw new ArgumentException("Unknown build target " + targetName);
            string path = Arg("-customBuildPath") ?? (target == BuildTarget.Android ? "Builds/Quest/PVDesigner-Quest.apk" : "Builds/Windows/PVDesigner.exe");
            // unity-builder passes a folder for some targets: append a file name when needed.
            if (target == BuildTarget.StandaloneWindows64 && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(path, "PVDesigner.exe");
            if (target == BuildTarget.Android && !path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                path = Path.Combine(path, "PVDesigner-Quest.apk");

            string version = Arg("-buildVersion");
            int code = int.TryParse(Arg("-androidVersionCode"), out int c) ? c : 0;
            var report = Build(target, path, version, code);
            EditorApplication.Exit(report != null && report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        public static BuildReport Build(BuildTarget target, string path, string version, int androidVersionCode = 0)
        {
            ProjectSetup.ConfigureAll();
            if (!string.IsNullOrEmpty(version))
            {
                version = version.TrimStart('v', 'V');
                PlayerSettings.bundleVersion = version;
                if (androidVersionCode <= 0) androidVersionCode = VersionCode(version);
            }
            if (androidVersionCode > 0) PlayerSettings.Android.bundleVersionCode = androidVersionCode;

            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (EditorUserBuildSettings.activeBuildTarget != target)
                EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? "Builds");
            var options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = path,
                target = target,
                targetGroup = group,
                options = BuildOptions.None
            };
            Debug.Log($"[PV Designer] Building {target} {PlayerSettings.bundleVersion} -> {path}");
            var report = BuildPipeline.BuildPlayer(options);
            Debug.Log($"[PV Designer] Build {report.summary.result}: {report.summary.totalSize / 1048576.0:0.0} MB, {report.summary.totalErrors} error(s)");
            return report;
        }

        /// <summary>1.2.3 -> 10203 (monotonic Android version code).</summary>
        public static int VersionCode(string version)
        {
            var main = version.Split('-')[0].Split('.');
            int Part(int i) => i < main.Length && int.TryParse(main[i], out int v) ? v : 0;
            return Math.Max(1, Part(0) * 10000 + Part(1) * 100 + Part(2));
        }
    }
}
