using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Keeps OpenXRLoader as the single Android XR loader and switches which OpenXR
    /// feature set / vendor features are enabled for the target headset.
    /// </summary>
    public static class OpenXrTargetSwitcher
    {
        private static readonly string[] LegacyLoaderTypeNames =
        {
            "Unity.XR.PXR.PXR_Loader",
            "Unity.XR.Oculus.OculusLoader",
            "Google.XR.Cardboard.XRLoader",
        };

        public static void Apply(HeadsetBuildConfig config, HeadsetDevice device)
        {
            var target = config.GetTarget(device);

            var manager = GetManagerSettingsForAndroid();

            foreach (var legacyLoader in LegacyLoaderTypeNames)
                XRPackageMetadataStore.RemoveLoader(manager, legacyLoader, BuildTargetGroup.Android);

            XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, BuildTargetGroup.Android);

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);

            var allSets = OpenXRFeatureSetManager.FeatureSetsForBuildTarget(BuildTargetGroup.Android);
            var allowedSetIds = new HashSet<string>(target.featureSetIds);

            // The primary feature set (index 0) must exist. Later entries (e.g. an optional Meta XR
            // Core SDK feature set) are opportunistic and only applied if that package is installed.
            var primarySetId = target.featureSetIds.FirstOrDefault();
            if (string.IsNullOrEmpty(primarySetId) || allSets.All(s => s.featureSetId != primarySetId))
            {
                var known = allSets.Select(s => s.featureSetId);
                throw new BuildFailedException(
                    $"Missing OpenXR feature set '{primarySetId}'. Installed feature sets: {string.Join(", ", known)}");
            }

            // Disable every feature set this target doesn't claim - not just "the other known target's"
            // set. A third-party package (e.g. Meta XR Core SDK) can register its own feature set, and
            // leaving it enabled while building for the other vendor is exactly what produces stray
            // Project Validation errors on switch.
            foreach (var set in allSets)
                set.isEnabled = allowedSetIds.Contains(set.featureSetId);

            OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(BuildTargetGroup.Android);

            ApplyRequiredFeatures(target);
            DisableEverythingNotAllowed(target, allSets, allowedSetIds);

            EditorUtility.SetDirty(manager);
            var oxrSettings = UnityEngine.XR.OpenXR.OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (oxrSettings != null)
                EditorUtility.SetDirty(oxrSettings);
            AssetDatabase.SaveAssets();

            VerifyOnlyOpenXrLoaderAssigned(manager);
        }

        private static void ApplyRequiredFeatures(HeadsetTarget target)
        {
            var requiredTypes = HeadsetBuildTypeUtil.ResolveTypes(target.requiredFeatureTypeNames, "required features");
            foreach (var type in requiredTypes)
                SetFeatureEnabled(type, true);
        }

        /// <summary>
        /// Disables every currently-enabled OpenXR feature that isn't accounted for by this target -
        /// not just features that came from a feature set we're turning off. Some packages (Meta XR
        /// Core SDK, notably) enable individual features like MetaXRFeature directly, bypassing the
        /// feature-set mechanism entirely, so a disabled feature *set* alone doesn't guarantee those
        /// get switched off. This is what enforces the "one vendor's features only" hard rule for real.
        /// </summary>
        private static void DisableEverythingNotAllowed(HeadsetTarget target, List<OpenXRFeatureSetManager.FeatureSet> allSets, HashSet<string> allowedSetIds)
        {
            var keepIds = new HashSet<string>(
                allSets.Where(s => allowedSetIds.Contains(s.featureSetId) && s.featureIds != null)
                    .SelectMany(s => s.featureIds));

            keepIds.UnionWith(
                HeadsetBuildTypeUtil.ResolveTypes(target.requiredFeatureTypeNames, "required features")
                    .Select(HeadsetBuildTypeUtil.GetFeatureId)
                    .Where(id => !string.IsNullOrEmpty(id)));

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
                return;

            var features = settings.GetFeatures();
            foreach (var feature in features)
            {
                if (!feature.enabled || IsHidden(feature))
                    continue;

                var featureId = HeadsetBuildTypeUtil.GetFeatureId(feature.GetType());
                if (string.IsNullOrEmpty(featureId) || !keepIds.Contains(featureId))
                    feature.enabled = false;
            }

            // Hidden features (e.g. Meta OpenXR's OpenXRLifeCycleFeature) aren't in any feature set and
            // have no public featureId, but OpenXR only ships a package's native plugins when a feature
            // whose script sits above the plugin folder is enabled - for com.unity.xr.meta-openxr that's
            // the hidden lifecycle feature. Disabling it strips libUnityARFoundationMeta.so and the app
            // crashes on launch with DllNotFoundException. So keep each hidden feature in step with the
            // visible features of its own package instead.
            var enabledAssemblies = new HashSet<Assembly>(
                features.Where(f => f.enabled && !IsHidden(f)).Select(f => f.GetType().Assembly));

            foreach (var feature in features.Where(IsHidden))
                feature.enabled = enabledAssemblies.Contains(feature.GetType().Assembly);
        }

        private static bool IsHidden(OpenXRFeature feature)
        {
            return feature.GetType().GetCustomAttribute<OpenXRFeatureAttribute>()?.Hidden ?? false;
        }

        private static void SetFeatureEnabled(System.Type featureType, bool enabled)
        {
            // Resolve by concrete Type, not featureId: some shipped OpenXR feature classes reuse the
            // same featureId string (e.g. UnityEngine.XR.Hands.OpenXR.HandTracking and
            // UnityEngine.XR.OpenXR.Features.Interactions.MicrosoftHandInteraction both declare
            // "com.unity.openxr.feature.input.handtracking"), so id-based lookup can silently toggle
            // the wrong feature.
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var feature = settings != null ? settings.GetFeature(featureType) : null;
            if (feature == null)
            {
                Debug.LogWarning($"[HeadsetBuild] OpenXR feature '{featureType.Name}' not found for Android. Skipping.");
                return;
            }

            if (feature.enabled != enabled)
                feature.enabled = enabled;
        }

        public static UnityEngine.XR.Management.XRManagerSettings GetManagerSettingsForAndroid()
        {
            EditorBuildSettings.TryGetConfigObject(
                UnityEngine.XR.Management.XRGeneralSettings.settingsKey,
                out UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget buildTargetSettings);

            if (buildTargetSettings == null)
                throw new BuildFailedException("XR Plug-in Management is not configured for this project.");

            if (!buildTargetSettings.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                buildTargetSettings.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            var manager = buildTargetSettings.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (manager == null)
                throw new BuildFailedException("Could not resolve XR Manager Settings for Android.");

            return manager;
        }

        private static void VerifyOnlyOpenXrLoaderAssigned(UnityEngine.XR.Management.XRManagerSettings manager)
        {
            var nonOpenXrLoaders = manager.loaders
                .Where(l => l != null && l.GetType() != typeof(OpenXRLoader))
                .Select(l => l.GetType().FullName)
                .ToArray();

            if (nonOpenXrLoaders.Length > 0)
            {
                throw new BuildFailedException(
                    $"Non-OpenXR Android loader(s) still assigned after switch: {string.Join(", ", nonOpenXrLoaders)}");
            }

            if (!manager.loaders.Any(l => l != null && l.GetType() == typeof(OpenXRLoader)))
                throw new BuildFailedException("OpenXRLoader was not assigned to the Android loader list.");
        }

        public static void EnsureRequiredFeatureTypesPresent(HeadsetTarget target)
        {
            foreach (var name in target.requiredFeatureTypeNames)
            {
                var type = HeadsetBuildTypeUtil.FindType(name);
                if (type == null)
                {
                    throw new BuildFailedException(
                        $"Required OpenXR feature type '{name}' is missing. Check package installation.");
                }

                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
                var feature = settings != null ? settings.GetFeature(type) : null;

                if (feature == null || !feature.enabled)
                {
                    throw new BuildFailedException(
                        $"Required OpenXR feature '{name}' is not enabled for Android.");
                }
            }
        }
    }
}
