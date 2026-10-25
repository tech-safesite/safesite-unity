using Modules.SafeSiteApiService.Editor;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// Each scenario gets its own Addressables group, built only when its package is exported. The
    /// group is left out of normal builds, so the app's own content never includes scenario bundles.
    /// </summary>
    public static class ScenarioGroups
    {
        public static AddressableAssetGroup Find(ScenarioDefinition definition)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings == null ? null : settings.FindGroup(definition.GroupName);
        }

        /// <summary>Creates or repairs the scenario's group and puts its main scene in it at <see cref="ScenarioAddresses.MainScene"/>.</summary>
        public static AddressableAssetGroup Ensure(ScenarioDefinition definition)
        {
            if (AddressableAssetSettingsDefaultObject.Settings == null)
                AddressablesValidator.ApplyRecommendedSettings();
            var settings = AddressableAssetSettingsDefaultObject.Settings;

            var group = settings.FindGroup(definition.GroupName)
                        ?? settings.CreateGroup(definition.GroupName, false, false, true, null,
                            typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));

            var schema = group.GetSchema<BundledAssetGroupSchema>() ?? group.AddSchema<BundledAssetGroupSchema>();
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
            schema.IncludeInBuild = false;
            EditorUtility.SetDirty(schema);

            if (definition.mainScene != null)
            {
                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition.mainScene));
                var entry = settings.CreateOrMoveEntry(guid, group, false, false);
                entry.address = ScenarioAddresses.MainScene;
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            return group;
        }

        /// <summary>True when the main scene sits in the scenario's group at the expected address.</summary>
        public static bool HasMainScene(ScenarioDefinition definition, AddressableAssetGroup group)
        {
            if (definition.mainScene == null || group == null)
                return false;
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition.mainScene));
            var entry = group.GetAssetEntry(guid);
            return entry != null && entry.address == ScenarioAddresses.MainScene;
        }
    }
}
