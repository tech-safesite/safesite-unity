using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Configures OpenXR for the Standalone build target - the one Editor Play Mode over Quest Link
    /// (and a PC VR player) actually uses.
    ///
    /// Everything else in this folder targets BuildTargetGroup.Android, because that is what ships.
    /// Standalone was therefore never configured, and ended up with just three features enabled
    /// (MetaXRFeature, MetaXRFoveationFeature, OpenXRCompositionLayersFeature) and *no controller
    /// interaction profile at all*. Over Link that means OpenXR never creates an XRController device,
    /// so every TrackedPoseDriver bound to &lt;XRController&gt;{LeftHand}/... receives nothing and
    /// XRInputModalityManager - which shows the controller objects only while controllers are
    /// detected - hides them outright. The symptom is simply "I can't see my controllers".
    ///
    /// Standalone is a development path, never shipped, so unlike OpenXrTargetSwitcher this only ever
    /// turns features ON. It does not enforce the one-vendor-only rule, which exists to keep vendor
    /// native libraries out of the shipped Android APK and has no bearing here.
    /// </summary>
    public static class StandaloneXrSetup
    {
        private const BuildTargetGroup Group = BuildTargetGroup.Standalone;

        public static void Apply(HeadsetBuildConfig config)
        {
            var manager = GetManagerSettings();
            XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, Group);

            UnityEditor.XR.OpenXR.Features.FeatureHelpers.RefreshFeatures(Group);

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(Group);
            if (settings == null)
            {
                Debug.LogWarning("[HeadsetBuild] No OpenXR settings for Standalone; skipping Link setup.");
                return;
            }

            var enabled = 0;
            var missing = 0;

            foreach (var type in HeadsetBuildTypeUtil.ResolveTypes(config.linkFeatureTypeNames, "Quest Link features"))
            {
                var feature = settings.GetFeature(type);
                if (feature == null)
                {
                    // Plenty of features are Android-only; that is expected, not an error.
                    missing++;
                    continue;
                }

                if (feature.enabled)
                    continue;

                feature.enabled = true;
                enabled++;
            }

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log($"[HeadsetBuild] Quest Link (Standalone) OpenXR configured: enabled {enabled} feature(s), " +
                      $"{missing} not available for Standalone. Active profiles: {DescribeProfiles(settings)}");
        }

        /// <summary>
        /// Warns - rather than fails - when Standalone has no controller profile. A device build is
        /// still perfectly valid in that state; it is only Link/Play Mode that breaks, so this must
        /// not stop a build.
        /// </summary>
        public static void WarnIfUnusableOverLink()
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(Group);
            if (settings == null)
                return;

            if (CountProfiles(settings) > 0)
                return;

            Debug.LogWarning(
                "[HeadsetBuild] No OpenXR interaction profile is enabled for Standalone, so controllers will not " +
                "appear in Play Mode over Quest Link (XRInputModalityManager hides them when no controller is " +
                "detected). Run Build > Set SDK > Configure Quest Link (PC).");
        }

        private static int CountProfiles(OpenXRSettings settings) =>
            settings.GetFeatures().Count(f => f != null && f.enabled && IsInteractionProfile(f.GetType()));

        private static string DescribeProfiles(OpenXRSettings settings)
        {
            var names = settings.GetFeatures()
                .Where(f => f != null && f.enabled && IsInteractionProfile(f.GetType()))
                .Select(f => f.GetType().Name)
                .ToArray();

            return names.Length == 0 ? "NONE" : string.Join(", ", names);
        }

        /// <summary>
        /// Every controller profile derives from OpenXRInteractionFeature - that is the real test,
        /// rather than matching on a "...ControllerProfile" name, which vendors do not follow
        /// consistently.
        /// </summary>
        private static bool IsInteractionProfile(System.Type type) =>
            typeof(UnityEngine.XR.OpenXR.Features.OpenXRInteractionFeature).IsAssignableFrom(type);

        private static UnityEngine.XR.Management.XRManagerSettings GetManagerSettings()
        {
            EditorBuildSettings.TryGetConfigObject(
                UnityEngine.XR.Management.XRGeneralSettings.settingsKey,
                out UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget buildTargetSettings);

            if (buildTargetSettings == null)
                throw new BuildFailedException("XR Plug-in Management is not configured for this project.");

            if (!buildTargetSettings.HasManagerSettingsForBuildTarget(Group))
                buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(Group);

            var manager = buildTargetSettings.ManagerSettingsForBuildTarget(Group);
            if (manager == null)
                throw new BuildFailedException("Could not resolve XR Manager Settings for Standalone.");

            return manager;
        }
    }
}
