using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Applies the Android player settings (sdk levels, package id, graphics, entry point)
    /// that distinguish a Quest build from a Pico build.
    /// </summary>
    public static class AndroidStoreProfile
    {
        public static void Apply(HeadsetBuildConfig config, HeadsetDevice device)
        {
            var target = config.GetTarget(device);

            PlayerSettings.productName = config.productName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, config.ApplicationId(device));

            PlayerSettings.Android.minSdkVersion = target.minSdk;
            PlayerSettings.Android.targetSdkVersion = target.targetSdk;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;

            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

            PlayerSettings.colorSpace = ColorSpace.Linear;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, target.graphicsApis);

            PlayerSettings.Android.applicationEntry = target.useGameActivity
                ? AndroidApplicationEntry.GameActivity
                : AndroidApplicationEntry.Activity;

            AssetDatabase.SaveAssets();
        }

        public static void Validate(HeadsetBuildConfig config, HeadsetDevice device)
        {
            var target = config.GetTarget(device);
            var expectedId = config.ApplicationId(device);
            var actualId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);

            if (actualId != expectedId)
                throw new UnityEditor.Build.BuildFailedException(
                    $"Android applicationIdentifier is '{actualId}', expected '{expectedId}'.");

            if (PlayerSettings.Android.minSdkVersion != target.minSdk)
                throw new UnityEditor.Build.BuildFailedException(
                    $"Android minSdk is {PlayerSettings.Android.minSdkVersion}, expected {target.minSdk}.");

            if (PlayerSettings.Android.targetSdkVersion != target.targetSdk)
                throw new UnityEditor.Build.BuildFailedException(
                    $"Android targetSdk is {PlayerSettings.Android.targetSdkVersion}, expected {target.targetSdk}.");

            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
                throw new UnityEditor.Build.BuildFailedException("Android target architecture must be ARM64 only.");

            if (PlayerSettings.colorSpace != ColorSpace.Linear)
                throw new UnityEditor.Build.BuildFailedException("Color space must be Linear for headset builds.");
        }
    }
}
