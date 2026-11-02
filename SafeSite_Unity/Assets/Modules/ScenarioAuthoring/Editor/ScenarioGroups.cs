using System;
using System.Text.RegularExpressions;
using Modules.SafeSiteApiService.Editor;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// Each scenario has its own Addressables group. A group the scenario team already set up (its own
    /// address, local paths, included in the app build for testing) is left as it is: only during an
    /// export does the group take the package contract (main scene at <see cref="ScenarioAddresses.MainScene"/>,
    /// remote paths), and its own settings are put back afterwards. New groups get the contract from the start.
    /// </summary>
    public static class ScenarioGroups
    {
        /// <summary>
        /// The group holding the main scene (directly or through its folder), or else the one named
        /// after the scenario folder (Scenario_Working_At_Height) or the slug.
        /// </summary>
        public static AddressableAssetGroup Find(ScenarioDefinition definition)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return null;
            return SceneEntry(settings, definition, true)?.parentGroup
                   ?? settings.FindGroup(GroupNameFor(definition))
                   ?? settings.FindGroup(definition.GroupName);
        }

        /// <summary>
        /// Makes sure the main scene is in a group. Existing groups and entries keep their settings and
        /// addresses; a new group follows the package contract.
        /// </summary>
        public static AddressableAssetGroup Ensure(ScenarioDefinition definition)
        {
            if (AddressableAssetSettingsDefaultObject.Settings == null)
                AddressablesValidator.ApplyRecommendedSettings();
            var settings = AddressableAssetSettingsDefaultObject.Settings;

            var group = Find(definition);
            var created = group == null;
            if (created)
            {
                group = settings.CreateGroup(GroupNameFor(definition), false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                ApplyContract(settings, schema);
                schema.IncludeInBuild = false;
                EditorUtility.SetDirty(schema);
            }

            // The scene needs its own entry so an export can give it the contract address. In an existing
            // group it gets the team's style of address (scenario_falling_objects_scene) for in-app loading.
            if (definition.mainScene != null && SceneEntry(settings, definition, false) == null)
            {
                var entry = settings.CreateOrMoveEntry(SceneGuid(definition), group, false, false);
                entry.address = created ? ScenarioAddresses.MainScene : InAppAddressFor(definition);
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            return group;
        }

        /// <summary>True when the main scene sits in the scenario's group (at any address).</summary>
        public static bool HasMainScene(ScenarioDefinition definition, AddressableAssetGroup group)
        {
            if (definition.mainScene == null || group == null)
                return false;
            return group.GetAssetEntry(SceneGuid(definition)) != null;
        }

        /// <summary>The main scene's address in its group, or null.</summary>
        public static string MainSceneAddress(ScenarioDefinition definition)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings == null ? null : SceneEntry(settings, definition, false)?.address;
        }

        /// <summary>
        /// Gives the group the package contract until disposed: main scene at <see cref="ScenarioAddresses.MainScene"/>,
        /// remote build and load paths, hashed bundle names. The group's own settings come back on dispose.
        /// </summary>
        public static IDisposable ForExport(ScenarioDefinition definition, AddressableAssetGroup group)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var schema = group.GetSchema<BundledAssetGroupSchema>()
                         ?? throw new UploadFailedException($"Group '{group.Name}' has no Bundled Asset Group schema.");
            var entry = group.GetAssetEntry(SceneGuid(definition))
                        ?? throw new UploadFailedException($"The main scene isn't in group '{group.Name}'.");

            var restore = new ExportScope(settings, schema, entry);
            ApplyContract(settings, schema);
            entry.address = ScenarioAddresses.MainScene;
            EditorUtility.SetDirty(schema);
            return restore;
        }

        /// <summary>FallingObjects → Scenario_Falling_Objects, the scenario team's naming.</summary>
        public static string GroupNameFor(ScenarioDefinition definition)
        {
            var folder = System.IO.Path.GetFileName(definition.Folder ?? "");
            if (string.IsNullOrEmpty(folder) || folder == definition.slug)
                return definition.GroupName;
            return "Scenario_" + Regex.Replace(folder, "(?<=[a-z0-9])(?=[A-Z])", "_");
        }

        /// <summary>FallingObjects → scenario_falling_objects_scene.</summary>
        public static string InAppAddressFor(ScenarioDefinition definition) =>
            "scenario_" + Regex.Replace(System.IO.Path.GetFileName(definition.Folder ?? definition.slug), "(?<=[a-z0-9])(?=[A-Z])", "_").Replace('-', '_').ToLowerInvariant() + "_scene";

        private static void ApplyContract(AddressableAssetSettings settings, BundledAssetGroupSchema schema)
        {
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
        }

        /// <param name="includeFolders">Also accept the scene through a folder entry. Addressables returns those even
        /// when asked not to, so they're filtered out here.</param>
        private static AddressableAssetEntry SceneEntry(AddressableAssetSettings settings, ScenarioDefinition definition, bool includeFolders)
        {
            if (definition.mainScene == null)
                return null;
            var entry = settings.FindAssetEntry(SceneGuid(definition), includeFolders);
            return entry == null || (!includeFolders && entry.IsSubAsset) ? null : entry;
        }

        private static string SceneGuid(ScenarioDefinition definition) =>
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition.mainScene));

        private sealed class ExportScope : IDisposable
        {
            private readonly AddressableAssetSettings _settings;
            private readonly BundledAssetGroupSchema _schema;
            private readonly AddressableAssetEntry _entry;
            private readonly string _buildPath;
            private readonly string _loadPath;
            private readonly BundledAssetGroupSchema.BundleNamingStyle _naming;
            private readonly string _address;

            public ExportScope(AddressableAssetSettings settings, BundledAssetGroupSchema schema, AddressableAssetEntry entry)
            {
                _settings = settings;
                _schema = schema;
                _entry = entry;
                _buildPath = schema.BuildPath.Id;
                _loadPath = schema.LoadPath.Id;
                _naming = schema.BundleNaming;
                _address = entry.address;
            }

            public void Dispose()
            {
                _schema.BuildPath.SetVariableById(_settings, _buildPath);
                _schema.LoadPath.SetVariableById(_settings, _loadPath);
                _schema.BundleNaming = _naming;
                _entry.address = _address;
                EditorUtility.SetDirty(_schema);
                _settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
