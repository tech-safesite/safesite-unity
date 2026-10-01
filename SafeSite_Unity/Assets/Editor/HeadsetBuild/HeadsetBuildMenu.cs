using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    public static class HeadsetBuildMenu
    {
        [MenuItem("Build/Quest", priority = 0)]
        public static void BuildQuest() => HeadsetBuildPipeline.Build(HeadsetDevice.Quest);

        [MenuItem("Build/Pico", priority = 1)]
        public static void BuildPico() => HeadsetBuildPipeline.Build(HeadsetDevice.Pico);

        [MenuItem("Build/All (Quest + Pico)", priority = 2)]
        public static void BuildAll() => HeadsetBuildPipeline.BuildAll();

        [MenuItem("Build/Set SDK/Quest OpenXR", priority = 100)]
        public static void SetSdkQuest()
        {
            var config = HeadsetBuildConfig.FindOrThrow();
            OpenXrTargetSwitcher.Apply(config, HeadsetDevice.Quest);
            AndroidStoreProfile.Apply(config, HeadsetDevice.Quest);
            HeadsetAndroidManifest.Apply(HeadsetDevice.Quest);
            MetaProjectSetupGate.Apply(HeadsetDevice.Quest);
            StandaloneXrSetup.Apply(config);
            Debug.Log("[HeadsetBuild] Switched to Quest OpenXR.");
        }

        [MenuItem("Build/Set SDK/Pico OpenXR", priority = 101)]
        public static void SetSdkPico()
        {
            var config = HeadsetBuildConfig.FindOrThrow();
            OpenXrTargetSwitcher.Apply(config, HeadsetDevice.Pico);
            AndroidStoreProfile.Apply(config, HeadsetDevice.Pico);
            HeadsetAndroidManifest.Apply(HeadsetDevice.Pico);
            PicoProjectSettingSync.Apply(config);
            MetaProjectSetupGate.Apply(HeadsetDevice.Pico);
            // Standalone is the Link/Play Mode path and stays on the Meta runtime either way.
            StandaloneXrSetup.Apply(config);
            Debug.Log("[HeadsetBuild] Switched to Pico OpenXR.");
        }

        [MenuItem("Build/Set SDK/Configure Quest Link (PC)", priority = 102)]
        public static void ConfigureQuestLink()
        {
            StandaloneXrSetup.Apply(HeadsetBuildConfig.FindOrThrow());
        }

        [MenuItem("Build/Validate/Quest Runtime", priority = 200)]
        public static void ValidateQuestRuntime()
        {
            var config = HeadsetBuildConfig.FindOrThrow();
            ApkOpenXrValidator.PreValidate(config, HeadsetDevice.Quest);
            Debug.Log("[HeadsetBuild] Quest runtime validation passed.");
        }

        [MenuItem("Build/Validate/Pico Runtime", priority = 201)]
        public static void ValidatePicoRuntime()
        {
            var config = HeadsetBuildConfig.FindOrThrow();
            ApkOpenXrValidator.PreValidate(config, HeadsetDevice.Pico);
            Debug.Log("[HeadsetBuild] Pico runtime validation passed.");
        }

        [MenuItem("Build/Validate/Android Store Profile", priority = 202)]
        public static void ValidateStoreProfile()
        {
            var config = HeadsetBuildConfig.FindOrThrow();
            var appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            var device = appId.Contains(config.quest.deviceToken) ? HeadsetDevice.Quest : HeadsetDevice.Pico;
            AndroidStoreProfile.Validate(config, device);
            Debug.Log($"[HeadsetBuild] Android store profile matches {device}.");
        }

        [MenuItem("Open/Builds Folder", priority = 300)]
        public static void OpenBuildsFolder()
        {
            var config = HeadsetBuildConfig.FindOrThrow();
            var path = Path.GetFullPath(config.outputRoot);
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }
    }
}
