using System.Linq;
using Modules.SafeSiteApiService.Editor;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>Checks a scenario before export: its details, its group, the Addressables setup, and its scripts.</summary>
    public static class ScenarioValidator
    {
        public static ValidationReport Validate(ScenarioDefinition definition, AddressableAssetGroup group)
        {
            var report = new ValidationReport();
            report.Errors.AddRange(definition.Validate());

            if (group == null)
            {
                report.Errors.Add($"No Addressables group '{definition.GroupName}'. Click 'Repair group'.");
                return report;
            }

            if (!ScenarioGroups.HasMainScene(definition, group))
                report.Errors.Add($"The main scene isn't in group '{group.Name}' at '{ScenarioAddresses.MainScene}'. Click 'Repair group'.");

            var settings = AddressablesValidator.ValidateSettings(group);
            report.Errors.AddRange(settings.Errors);
            report.Warnings.AddRange(settings.Warnings);

            if (definition.mainScene != null)
                CheckScripts(definition, report);

            return report;
        }

        /// <summary>
        /// Bundles can't carry code: every script the scene uses must already be in the headset app.
        /// Scripts kept inside the scenario's folder are a sign they were written for this scenario.
        /// </summary>
        private static void CheckScripts(ScenarioDefinition definition, ValidationReport report)
        {
            var folder = definition.Folder + "/";
            var scenePath = AssetDatabase.GetAssetPath(definition.mainScene);
            var scripts = AssetDatabase.GetDependencies(scenePath, true)
                .Where(p => p.EndsWith(".cs") && p.StartsWith(folder))
                .ToList();
            foreach (var script in scripts)
                report.Warnings.Add($"Script '{script}' lives in the scenario folder. Bundles can't carry code, so headsets need an app update that includes it before this scenario works.");
        }
    }
}
