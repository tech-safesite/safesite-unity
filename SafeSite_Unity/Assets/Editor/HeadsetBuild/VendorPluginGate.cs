using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Keeps one vendor's native plugins out of the other vendor's APK.
    ///
    /// Unity's own OpenXR build hook (OpenXRChooseRuntimeLibraries) already gates native plugins by
    /// feature, but only those that live underneath a registered OpenXR feature's PluginPath. Both
    /// vendor SDKs ship native libraries outside any feature folder, so those were included
    /// unconditionally: the 0.1.1 Quest APK shipped libpxrplatformloader.so, libPICO_TOBAPI.so,
    /// libpvrcapturelib.so and libCameraRenderingPlugin.so from the PICO SDK.
    ///
    /// SetCompatibleWithPlatform is deliberately NOT used here. These plugins live in immutable
    /// package caches, so a compatibility change is silently reverted on the next reimport (verified
    /// against com.unity.xr.openxr.picoxr). SetIncludeInBuildDelegate is a per-domain, non-persisted
    /// override, which is exactly what Unity's own hook uses for the same reason.
    /// </summary>
    public sealed class VendorPluginGate : IPreprocessBuildWithReport
    {
        /// <summary>Runs after Unity's OpenXR hook and PICO's own preprocessor (both order 0).</summary>
        public int callbackOrder => 100;

        private const string PicoFeatureTypeName = "Unity.XR.OpenXR.Features.PICOSupport.PICOFeature";

        /// <summary>
        /// Plugin path fragments owned by each vendor that sit outside every OpenXR feature
        /// PluginPath, and so are invisible to Unity's per-feature gating.
        /// </summary>
        private static readonly string[] PicoOnlyPluginPaths =
        {
            "com.unity.xr.openxr.picoxr/Platform/",
            "com.unity.xr.openxr.picoxr/Enterprise/",
        };

        private static readonly string[] MetaOnlyPluginPaths =
        {
            "com.meta.xr.sdk.core/Plugins/",
        };

        private const string MetaOpenXrAssemblyName = "Unity.XR.MetaOpenXR";
        private const string MetaOpenXrPluginPath = "com.unity.xr.meta-openxr/Runtime/Plugins/";

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            Apply(ResolveDevice(), report.summary.platform);
            EnsureMetaOpenXrNativeLibrary(report.summary.platform);
        }

        /// <summary>
        /// Guarantees libUnityARFoundationMeta ships whenever anything still calls into it.
        ///
        /// Unity's OpenXR hook includes a native plugin only if some feature whose PluginPath covers
        /// that folder is enabled. The library lives in "com.unity.xr.meta-openxr/Runtime/Plugins/...",
        /// and the only feature covering it is OpenXRLifeCycleFeature - which the Meta feature set
        /// leaves off even while enabling eleven AR features that P/Invoke straight into it. The 0.1.1
        /// Quest APK therefore carried the subsystem manifest for the library but not the library, and
        /// died with a DllNotFoundException in ARAnchorFeature.OnInstanceCreate followed by a SIGSEGV
        /// on a null function pointer in libOVRPlugin.so.
        ///
        /// HeadsetBuildConfig now disables those AR features, so normally nothing needs the library.
        /// This stays as a safety net: re-enable any Meta AR feature and the library comes with it,
        /// rather than reintroducing the crash.
        /// </summary>
        private static void EnsureMetaOpenXrNativeLibrary(BuildTarget buildTarget)
        {
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
                return;

            var needsLibrary = settings.GetFeatures()
                .Any(feature => feature != null && feature.enabled &&
                                feature.GetType().Assembly.GetName().Name == MetaOpenXrAssemblyName);

            if (!needsLibrary)
                return;

            var included = SetIncluded(new[] { MetaOpenXrPluginPath }, buildTarget, true);
            if (included.Count > 0)
            {
                Debug.Log("[HeadsetBuild] Meta OpenXR AR feature(s) are enabled, so forcing " +
                          $"{string.Join(", ", included)} into the build. Without it those features throw " +
                          "DllNotFoundException at OpenXR instance creation and the app crashes on startup.");
            }
            else
            {
                Debug.LogWarning(
                    "[HeadsetBuild] Meta OpenXR AR feature(s) are enabled but libUnityARFoundationMeta was not " +
                    "found among the Android plugins. Expect a DllNotFoundException at startup.");
            }
        }

        /// <summary>
        /// Derives the target headset from OpenXR state rather than from the build menu, so a plain
        /// File > Build Settings build is gated the same way as one started from the Build menu.
        /// </summary>
        public static HeadsetDevice ResolveDevice()
        {
            var picoType = HeadsetBuildTypeUtil.FindType(PicoFeatureTypeName);
            if (picoType == null)
                return HeadsetDevice.Quest;

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var picoFeature = settings != null ? settings.GetFeature(picoType) : null;

            return picoFeature != null && picoFeature.enabled ? HeadsetDevice.Pico : HeadsetDevice.Quest;
        }

        /// <summary>
        /// Sets the include delegate on every managed plugin every time, rather than only excluding.
        /// The delegate is domain state, so building Quest then Pico in one Editor session has to
        /// actively re-enable what the previous build turned off.
        /// </summary>
        public static void Apply(HeadsetDevice device, BuildTarget buildTarget)
        {
            var excludedPaths = device == HeadsetDevice.Quest ? PicoOnlyPluginPaths : MetaOnlyPluginPaths;
            var includedPaths = device == HeadsetDevice.Quest ? MetaOnlyPluginPaths : PicoOnlyPluginPaths;

            var excluded = SetIncluded(excludedPaths, buildTarget, false);
            SetIncluded(includedPaths, buildTarget, true);

            if (excluded.Count > 0)
            {
                Debug.Log($"[HeadsetBuild] Excluded {excluded.Count} foreign-vendor native plugin(s) from the " +
                          $"{device} build:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", excluded)}");
            }
        }

        private static List<string> SetIncluded(string[] pathFragments, BuildTarget buildTarget, bool include)
        {
            var affected = new List<string>();

            foreach (var importer in PluginImporter.GetAllImporters())
            {
                if (importer == null || !importer.GetCompatibleWithPlatform(buildTarget))
                    continue;

                // AssetImporter.assetPath is always forward-slashed, including for package assets.
                var assetPath = importer.assetPath;
                if (!pathFragments.Any(fragment => assetPath.Contains(fragment)))
                    continue;

                importer.SetIncludeInBuildDelegate(path => include);
                affected.Add(assetPath);
            }

            return affected;
        }
    }
}
