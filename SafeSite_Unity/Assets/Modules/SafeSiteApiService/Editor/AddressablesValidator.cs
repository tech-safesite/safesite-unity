using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Modules.SafeSiteApiService.Editor
{
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();

        public bool Ok => Errors.Count == 0;

        public IEnumerable<string> Lines =>
            Errors.Select(e => "ERROR: " + e).Concat(Warnings.Select(w => "Warning: " + w));
    }

    /// <summary>Checks the Addressables setup and build output against the API's upload rules (brief 6.2).</summary>
    public static class AddressablesValidator
    {
        public const string RecommendedRemoteLoadPath = "https://safesite.invalid/[BuildTarget]";

        /// <summary>The API's file path rule: segments of letters, digits, '.', '_', '-' separated by '/'.</summary>
        public static readonly Regex PathRule = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]*(/[A-Za-z0-9][A-Za-z0-9._-]*)*$");

        private static readonly Regex SafeGroupName = new Regex(@"^[A-Za-z0-9_-]+$");

        private static readonly BundledAssetGroupSchema.BundleNamingStyle[] HashedNaming =
        {
            BundledAssetGroupSchema.BundleNamingStyle.AppendHash,
            BundledAssetGroupSchema.BundleNamingStyle.OnlyHash,
            BundledAssetGroupSchema.BundleNamingStyle.FileNameHash,
        };

        /// <param name="onlyGroup">Checks just this group (as if it were the only one in the build) instead of every included group.</param>
        public static ValidationReport ValidateSettings(AddressableAssetGroup onlyGroup = null)
        {
            var report = new ValidationReport();

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                report.Errors.Add($"Active build target is {EditorUserBuildSettings.activeBuildTarget}; Meta Quest and Pico are both Android. Switch to Android.");

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                report.Errors.Add("No Addressables settings. Click 'Fix Addressables settings' to create them.");
                return report;
            }

            if (!settings.BuildRemoteCatalog)
                report.Errors.Add("Build Remote Catalog is off (Addressables settings > Catalog).");
            if (settings.RemoteCatalogBuildPath.GetName(settings) != AddressableAssetSettings.kRemoteBuildPath ||
                settings.RemoteCatalogLoadPath.GetName(settings) != AddressableAssetSettings.kRemoteLoadPath)
                report.Errors.Add("The remote catalog must use the Remote build/load paths.");

            var remoteLoadPath = settings.profileSettings.GetValueByName(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath) ?? "";
            if (!remoteLoadPath.StartsWith("https://safesite.invalid"))
                report.Warnings.Add($"Remote.LoadPath is '{remoteLoadPath}'. The runtime maps bundles by file name, so any URL works, " +
                                    $"but '{RecommendedRemoteLoadPath}' guarantees nothing is fetched by accident.");

            var remoteGroups = 0;
            var groups = onlyGroup != null ? new[] { onlyGroup } : settings.groups.Where(g => g != null);
            foreach (var group in groups)
            {
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema == null || (onlyGroup == null && !schema.IncludeInBuild))
                    continue;

                var buildRemote = schema.BuildPath.GetName(settings) == AddressableAssetSettings.kRemoteBuildPath;
                var loadRemote = schema.LoadPath.GetName(settings) == AddressableAssetSettings.kRemoteLoadPath;
                if (buildRemote && loadRemote)
                {
                    remoteGroups++;
                }
                else if (buildRemote != loadRemote)
                {
                    report.Errors.Add($"Group '{group.Name}' mixes Remote and Local build/load paths.");
                    continue;
                }
                else
                {
                    report.Warnings.Add($"Group '{group.Name}' uses Local paths: its bundles go into the player build, not the bucket." +
                                        (group == settings.DefaultGroup ? " It is the default group, so the built-in shader bundle stays local too." : ""));
                }

                if (!HashedNaming.Contains(schema.BundleNaming))
                    report.Errors.Add($"Group '{group.Name}' bundle naming is {schema.BundleNaming}; use AppendHash (or a hashed style) so unchanged bundles keep their names.");
                if (!SafeGroupName.IsMatch(group.Name))
                    report.Warnings.Add($"Group '{group.Name}' isn't alphanumeric; bundle names derived from it may break the API's path rule.");
            }

            if (remoteGroups == 0)
                report.Errors.Add("No group builds to the Remote paths, so there's nothing to upload.");

            return report;
        }

        public static ValidationReport ValidateOutput(IEnumerable<BuildFile> files, long maxFileBytes)
        {
            var report = new ValidationReport();
            var list = files.ToList();
            if (list.Count == 0)
                report.Errors.Add("The remote build folder is empty.");
            if (!list.Any(f => f.Path.StartsWith("catalog", System.StringComparison.OrdinalIgnoreCase)))
                report.Errors.Add("The build has no catalog file.");

            foreach (var file in list)
            {
                if (!PathRule.IsMatch(file.Path) || file.Path.Length > 300)
                    report.Errors.Add($"'{file.Path}' doesn't match the API's path rule (letters, digits, . _ - and /; no spaces).");
                if (file.SizeBytes > maxFileBytes)
                    report.Errors.Add($"'{file.Path}' is {file.SizeBytes / (1024.0 * 1024.0):0.0} MB, over the {maxFileBytes / (1024 * 1024)} MB storage limit.");
            }

            return report;
        }

        /// <summary>
        /// Creates settings if needed, turns on the remote catalog with Remote paths, points
        /// Remote.LoadPath at the recommended placeholder, and gives every bundled group hashed names.
        /// Moves the default group to Remote paths; leaves other groups' paths alone.
        /// </summary>
        public static void ApplyRecommendedSettings()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            settings.BuildRemoteCatalog = true;
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            settings.profileSettings.SetValue(settings.activeProfileId, AddressableAssetSettings.kRemoteLoadPath, RecommendedRemoteLoadPath);

            foreach (var group in settings.groups.Where(g => g != null))
            {
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema == null)
                    continue;
                if (!HashedNaming.Contains(schema.BundleNaming))
                    schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
                if (group == settings.DefaultGroup)
                {
                    schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
                    schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
                }

                EditorUtility.SetDirty(schema);
            }

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        public static void SwitchToAndroid() =>
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
    }
}
