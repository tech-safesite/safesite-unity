using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    public static class HeadsetBuildPipeline
    {
        /// <summary>Host projects can hook in project-specific prep (shaders, unique assets) here.</summary>
        public static event Action BeforeAndroidBuild;

        public static void Build(HeadsetDevice device)
        {
            var config = HeadsetBuildConfig.FindOrThrow();

            if (!VersionBumper.PromptAndMaybeBump())
            {
                Debug.Log("[HeadsetBuild] Build cancelled.");
                return;
            }

            var previousBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
            try
            {
                OpenXrTargetSwitcher.Apply(config, device);
                AndroidStoreProfile.Apply(config, device);
                KeystoreSigner.EnsureConfigured();

                BeforeAndroidBuild?.Invoke();

                ApkOpenXrValidator.PreValidate(config, device);

                EditorUserBuildSettings.buildAppBundle = false;

                var target = config.GetTarget(device);
                var version = PlayerSettings.bundleVersion;
                var outputDir = Path.Combine(config.outputRoot, version);
                Directory.CreateDirectory(outputDir);
                var apkPath = Path.Combine(outputDir, $"{config.productName}_{target.deviceToken}_v{version}.apk")
                    .Replace('\\', '/');

                var options = new BuildPlayerOptions
                {
                    scenes = ResolveScenePaths(config),
                    locationPathName = apkPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    extraScriptingDefines = target.extraDefines,
                };

                var report = BuildPipeline.BuildPlayer(options);

                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new UnityEditor.Build.BuildFailedException(
                        $"Build failed for {device}: {report.summary.result} ({report.summary.totalErrors} errors).");
                }

                ApkOpenXrValidator.PostValidate(apkPath, device);

                EditorUtility.RevealInFinder(apkPath);
                Debug.Log($"[HeadsetBuild] {device} build complete: {apkPath}");
            }
            finally
            {
                EditorUserBuildSettings.buildAppBundle = previousBuildAppBundle;
            }
        }

        public static void BuildAll()
        {
            Build(HeadsetDevice.Quest);
            Build(HeadsetDevice.Pico);
        }

        private static string[] ResolveScenePaths(HeadsetBuildConfig config)
        {
            if (config.scenes != null && config.scenes.Length > 0)
            {
                return config.scenes
                    .Select(name => $"Assets/Scenes/{name}.unity")
                    .ToArray();
            }

            var enabled = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (enabled.Length == 0)
                throw new UnityEditor.Build.BuildFailedException(
                    "No scenes configured on HeadsetBuildConfig and no enabled scenes in Build Settings.");

            return enabled;
        }
    }
}
