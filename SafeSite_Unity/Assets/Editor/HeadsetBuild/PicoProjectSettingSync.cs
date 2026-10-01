using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Keeps PICOProjectSetting in step with the OpenXR features this project actually requires.
    ///
    /// The PICO SDK gates its AndroidManifest additions on this asset rather than on the OpenXR
    /// feature list: PICOModifyAndroidManifest only writes com.picovr.permission.HAND_TRACKING and
    /// the "handtracking" meta-data when isHandTracking is set here. HeadsetBuildConfig lists
    /// XR Hands' HandTracking as a required PICO feature, but this flag was left off, so the 0.1.1
    /// PICO APK shipped with hand tracking enabled in OpenXR and no permission to use it.
    ///
    /// Loaded by path with SerializedObject rather than by type so this file does not need a compile
    /// time reference to the PICO package.
    /// </summary>
    public static class PicoProjectSettingSync
    {
        private const string SettingAssetPath = "Assets/Resources/PICOProjectSetting.asset";
        private const string HandTrackingFeatureTypeName = "UnityEngine.XR.Hands.OpenXR.HandTracking";

        public static void Apply(HeadsetBuildConfig config)
        {
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingAssetPath);
            if (asset == null)
            {
                Debug.LogWarning($"[HeadsetBuild] No PICOProjectSetting at '{SettingAssetPath}'; skipping sync. " +
                                 "Open Project Settings > PICO once to create it.");
                return;
            }

            var wantsHandTracking = config.pico.requiredFeatureTypeNames
                .Any(name => name == HandTrackingFeatureTypeName);

            var serialized = new SerializedObject(asset);
            var handTracking = serialized.FindProperty("isHandTracking");
            if (handTracking == null)
            {
                Debug.LogWarning("[HeadsetBuild] PICOProjectSetting has no 'isHandTracking' field; skipping sync.");
                return;
            }

            if (handTracking.boolValue == wantsHandTracking)
                return;

            handTracking.boolValue = wantsHandTracking;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            Debug.Log($"[HeadsetBuild] PICOProjectSetting.isHandTracking set to {wantsHandTracking} to match " +
                      "the required OpenXR features for PICO.");
        }

        /// <summary>Fails the build rather than shipping an APK whose hand tracking cannot work.</summary>
        public static void Validate(HeadsetBuildConfig config)
        {
            var wantsHandTracking = config.pico.requiredFeatureTypeNames
                .Any(name => name == HandTrackingFeatureTypeName);

            if (!wantsHandTracking)
                return;

            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SettingAssetPath);
            if (asset == null)
                throw new BuildFailedException(
                    $"PICO hand tracking is a required feature but '{SettingAssetPath}' is missing, so the " +
                    "hand-tracking permission would not be written into the manifest.");

            var handTracking = new SerializedObject(asset).FindProperty("isHandTracking");
            if (handTracking != null && !handTracking.boolValue)
                throw new BuildFailedException(
                    "PICO hand tracking is a required OpenXR feature but PICOProjectSetting.isHandTracking is " +
                    "off, so com.picovr.permission.HAND_TRACKING would be left out of the manifest.");
        }
    }
}
