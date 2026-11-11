using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Modules.SafeSiteApiService.Editor;
using Modules.ScenarioKit;
using Modules.SafeSiteApiService.External;
using Modules.SceneRig.Editor;
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
            {
                report.Errors.Add($"The main scene isn't in group '{group.Name}'. Click 'Repair group'.");
                return report;
            }

            // Checked as exported: the group may use local paths and its own address for in-app testing.
            using (ScenarioGroups.ForExport(definition, group))
            {
                var settings = AddressablesValidator.ValidateSettings(group);
                report.Errors.AddRange(settings.Errors);
                report.Warnings.AddRange(settings.Warnings);
            }

            if (definition.mainScene != null)
            {
                CheckScripts(definition, report);
                CheckCompletion(definition, report);
                // Scenarios load into Bootstrap, which brings the player: a rig of their own breaks that.
                XRRigMenu.ValidateScenePath(AssetDatabase.GetAssetPath(definition.mainScene), report.Errors, report.Warnings);
            }

            return report;
        }

        /// <summary>
        /// Without a completion call the dashboard shows every session as not completed. Looks for
        /// Complete/CompleteAndExit wired in the scene's events, or a call in the scenario's scripts.
        /// </summary>
        private static void CheckCompletion(ScenarioDefinition definition, ValidationReport report)
        {
            var scenePath = AssetDatabase.GetAssetPath(definition.mainScene);
            var dependencies = AssetDatabase.GetDependencies(scenePath, true);
            var hasComponent = dependencies
                .Where(p => p.EndsWith(".cs"))
                .Select(AssetDatabase.LoadAssetAtPath<MonoScript>)
                .Any(s => s != null && s.GetClass() == typeof(SafeSiteScenario));

            var sceneText = File.ReadAllText(scenePath);
            var wiredInScene = hasComponent && Regex.IsMatch(sceneText, @"m_MethodName: (Complete|CompleteAndExit)\s*$", RegexOptions.Multiline);

            var folder = definition.Folder + "/";
            var calledInCode = dependencies
                .Where(p => p.EndsWith(".cs") && p.StartsWith(folder))
                .Any(p => Regex.IsMatch(File.ReadAllText(p), @"\.(Complete|CompleteAndExit|MarkCompleted)\s*\("));

            if (wiredInScene || calledInCode)
                return;

            report.Warnings.Add(hasComponent
                ? "Nothing calls SafeSiteScenario.Complete: every session will show as not completed. " +
                  "Wire Complete or CompleteAndExit into the event at the scenario's end."
                : "The scene has no SafeSiteScenario component, and nothing reports completion: every session will show as not completed. " +
                  "Click Advanced > Add SafeSite Scenario, then wire Complete into the event at the scenario's end.");
        }

        /// <summary>
        /// Bundles can't carry code: the scenario's own scripts (its Scripts folder) are compiled into
        /// the headset app, so headsets need an app build that already has them.
        /// </summary>
        private static void CheckScripts(ScenarioDefinition definition, ValidationReport report)
        {
            var folder = definition.Folder + "/";
            var scenePath = AssetDatabase.GetAssetPath(definition.mainScene);
            var scripts = AssetDatabase.GetDependencies(scenePath, true)
                .Where(p => p.EndsWith(".cs") && p.StartsWith(folder))
                .ToList();
            if (scripts.Count > 0)
                report.Warnings.Add($"The scene uses {scripts.Count} script(s) from {folder} ({string.Join(", ", scripts.Select(System.IO.Path.GetFileName))}). " +
                                    $"Bundles can't carry code: headsets need an app build ({PlayerSettings.bundleVersion} or later) with these scripts.");
        }
    }
}
