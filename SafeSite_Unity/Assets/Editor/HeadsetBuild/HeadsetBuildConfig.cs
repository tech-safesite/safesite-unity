using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace SafeSite.Build.Headset
{
    public enum HeadsetDevice
    {
        Quest,
        Pico
    }

    [Serializable]
    public class HeadsetTarget
    {
        [Tooltip("Used in the applicationId and output filename, e.g. MetaQuest / Pico")]
        public string deviceToken = "";

        [Tooltip("OpenXR feature set ids allowed for this target. The first entry must exist; later entries " +
                 "(e.g. an optional Meta XR Core SDK feature set) are enabled only if installed. Every OTHER " +
                 "installed feature set is force-disabled when this target is applied.")]
        public string[] featureSetIds = Array.Empty<string>();

        [Tooltip("Assembly-independent full type names of OpenXR features that must be enabled for this target.")]
        public string[] requiredFeatureTypeNames = Array.Empty<string>();

        [Tooltip("Full type names of OpenXR features to force OFF even when an allowed feature set turns them on. " +
                 "A feature set is all-or-nothing, so this is the only way to drop individual features it pulls in.")]
        public string[] disabledFeatureTypeNames = Array.Empty<string>();

        public string[] extraDefines = Array.Empty<string>();

        public AndroidSdkVersions minSdk = AndroidSdkVersions.AndroidApiLevelAuto;
        public AndroidSdkVersions targetSdk = AndroidSdkVersions.AndroidApiLevelAuto;

        public bool useGameActivity = true;

        public GraphicsDeviceType[] graphicsApis = Array.Empty<GraphicsDeviceType>();
    }

    [CreateAssetMenu(menuName = "Headset Build/Config", fileName = "HeadsetBuildConfig")]
    public class HeadsetBuildConfig : ScriptableObject
    {
        /// <summary>
        /// Every feature in com.unity.xr.meta-openxr. They all P/Invoke into libUnityARFoundationMeta,
        /// and that library only ships when OpenXRLifeCycleFeature is enabled, because it is the one
        /// feature whose PluginPath ("Packages/com.unity.xr.meta-openxr/Runtime") covers the folder the
        /// library sits in. The Meta feature set enables these AR features WITHOUT enabling
        /// OpenXRLifeCycleFeature, which shipped a 0.1.1 Quest APK containing the subsystem manifest for
        /// libUnityARFoundationMeta but not the .so - so ARAnchorFeature.OnInstanceCreate threw
        /// DllNotFoundException, OpenXR Display_Initialize failed, and the app died on a null function
        /// pointer inside libOVRPlugin.so.
        ///
        /// Nothing in this project uses AR Foundation, so they are all switched off. If passthrough,
        /// plane detection or anchors are wanted later, remove the ones needed from this list AND make
        /// sure the native library actually ships (VendorPluginGate force-includes it).
        /// </summary>
        private static readonly string[] MetaArFoundationFeatures =
        {
            "UnityEngine.XR.OpenXR.Features.Meta.ARSessionFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.ARCameraFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.ARPlaneFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.ARAnchorFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.ARRaycastFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.ARMeshFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.ARBoundingBoxFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.AROcclusionFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.ColocationDiscoveryFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.BoundaryVisibilityFeature",
            "UnityEngine.XR.OpenXR.Features.Meta.DisplayUtilitiesFeature",
        };

        [Header("Identity")]
        public string reverseDomainPrefix = "com.safesite";
        public string appName = "SafeSite";
        public string productName = "SafeSite";

        [Header("Scenes")]
        [Tooltip("Scene names under Assets/Scenes (no .unity). Leave empty to use the enabled scenes from Build Settings.")]
        public string[] scenes = Array.Empty<string>();

        [Header("Output")]
        public string outputRoot = "Builds";

        /// <summary>
        /// OpenXR features for the Standalone target, i.e. Editor Play Mode over Quest Link and any
        /// PC VR player. Link runs on the Meta runtime, so these are the Meta profiles regardless of
        /// which Android headset is selected. Anything unavailable for Standalone is skipped.
        ///
        /// Without at least one interaction profile here, OpenXR creates no XRController device over
        /// Link and XRInputModalityManager hides the controller objects entirely.
        /// </summary>
        [Header("Quest Link (Standalone / Play Mode)")]
        [Tooltip("Features enabled for the Standalone target, used by Editor Play Mode over Quest Link.")]
        public string[] linkFeatureTypeNames =
        {
            "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchProControllerProfile",
            "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile",
            "UnityEngine.XR.Hands.OpenXR.HandTracking",
            "UnityEngine.XR.OpenXR.Features.CompositionLayers.OpenXRCompositionLayersFeature",
        };

        [Header("Targets")]
        public HeadsetTarget quest = new HeadsetTarget
        {
            deviceToken = "MetaQuest",
            featureSetIds = new[]
            {
                "com.unity.openxr.featureset.meta",
                "com.meta.openxr.featureset.metaxr", // optional: only present if Meta XR Core SDK is installed
            },
            requiredFeatureTypeNames = new[]
            {
                "UnityEngine.XR.OpenXR.Features.MetaQuestSupport.MetaQuestFeature",
                "UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile",
                "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchProControllerProfile",
                "UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile",
                "UnityEngine.XR.Hands.OpenXR.HandTracking",
                "UnityEngine.XR.OpenXR.Features.CompositionLayers.OpenXRCompositionLayersFeature",
            },
            disabledFeatureTypeNames = MetaArFoundationFeatures,
            extraDefines = new[] { "VR_OPENXR", "VR_META" },
            minSdk = AndroidSdkVersions.AndroidApiLevel32,
            targetSdk = AndroidSdkVersions.AndroidApiLevel34,
            useGameActivity = true,
            graphicsApis = new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLES3 },
        };

        public HeadsetTarget pico = new HeadsetTarget
        {
            deviceToken = "Pico",
            featureSetIds = new[] { "com.picoxr.openxr.features" },
            requiredFeatureTypeNames = new[]
            {
                "Unity.XR.OpenXR.Features.PICOSupport.PICOFeature",
                "UnityEngine.XR.OpenXR.Features.Interactions.PICONeo3ControllerProfile",
                "UnityEngine.XR.OpenXR.Features.Interactions.PICO4ControllerProfile",
                "UnityEngine.XR.OpenXR.Features.Interactions.PICO4UltraControllerProfile",
                "UnityEngine.XR.OpenXR.Features.Interactions.PICOG3ControllerProfile",
                "UnityEngine.XR.Hands.OpenXR.HandTracking",
                "UnityEngine.XR.OpenXR.Features.CompositionLayers.OpenXRCompositionLayersFeature",
            },
            disabledFeatureTypeNames = MetaArFoundationFeatures,
            extraDefines = new[] { "VR_OPENXR", "VR_PICO" },
            minSdk = AndroidSdkVersions.AndroidApiLevel29,
            targetSdk = AndroidSdkVersions.AndroidApiLevel34,
            useGameActivity = true,
            graphicsApis = new[] { GraphicsDeviceType.Vulkan },
        };

        public HeadsetTarget GetTarget(HeadsetDevice device) => device == HeadsetDevice.Quest ? quest : pico;

        public string ApplicationId(HeadsetDevice device) =>
            $"{reverseDomainPrefix}.{appName}.{GetTarget(device).deviceToken}";

        private static HeadsetBuildConfig _cached;

        public static HeadsetBuildConfig FindOrThrow()
        {
            if (_cached != null)
                return _cached;

            var guids = AssetDatabase.FindAssets("t:HeadsetBuildConfig");
            if (guids.Length == 0)
            {
                throw new BuildFailedException(
                    "No HeadsetBuildConfig asset found. Create one via Assets > Create > Headset Build > Config.");
            }

            if (guids.Length > 1)
            {
                throw new BuildFailedException(
                    "Multiple HeadsetBuildConfig assets found. Keep exactly one in the project.");
            }

            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            _cached = AssetDatabase.LoadAssetAtPath<HeadsetBuildConfig>(path);
            return _cached;
        }
    }
}
