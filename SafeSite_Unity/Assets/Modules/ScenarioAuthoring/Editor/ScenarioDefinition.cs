using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEngine;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// One scenario: its details for the dashboard and the scene that headsets load. Exported as a
    /// scenario package (zip) that the dashboard imports. The details are only used when the
    /// dashboard creates the scenario; after that the dashboard owns them.
    /// </summary>
    [CreateAssetMenu(menuName = "SafeSite/Scenario Definition", fileName = "ScenarioDefinition")]
    public sealed class ScenarioDefinition : ScriptableObject
    {
        public static readonly Regex SlugPattern = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$");

        [Tooltip("Stable id shared with the dashboard, e.g. ladder-access-setup. Lowercase letters, digits and dashes.")]
        public string slug;

        public string title;

        [Tooltip("The dashboard's folder for this scenario, e.g. Working at Height.")]
        public string category;

        [TextArea(2, 4)]
        public string description;

        [Range(1, 120)]
        public int durationMinutes = 10;

        public List<string> languages = new List<string> { "English" };

        [Tooltip("Loaded on the headset at the address 'scenario/main'. Must not contain a camera or XR rig; the app provides those.")]
        public SceneAsset mainScene;

        [Tooltip("Version of the last exported package. The next export suggests one patch higher.")]
        public string lastExportedVersion;

        public string GroupName => "Scenario_" + slug;

        /// <summary>Folder holding this definition; scripts in it aren't part of the app (see the export check).</summary>
        public string Folder => System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(this))?.Replace('\\', '/');

        public string SuggestedVersion() =>
            SafeSiteTime.IsVersion(lastExportedVersion) ? SafeSiteTime.BumpPatch(lastExportedVersion) : "1.0.0";

        /// <summary>The API's rules for creating a scenario (CreateScenario in the OpenAPI spec).</summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (string.IsNullOrEmpty(slug) || !SlugPattern.IsMatch(slug) || slug.Length > 80)
                errors.Add("Slug must be lowercase letters, digits and single dashes (max 80), e.g. ladder-access-setup.");
            Require(errors, "Title", title, 60);
            Require(errors, "Category", category, 60);
            Require(errors, "Description", description, 200);
            if (durationMinutes < 1 || durationMinutes > 120)
                errors.Add("Duration must be 1 to 120 minutes.");
            if (languages == null || languages.Count(l => !string.IsNullOrWhiteSpace(l)) == 0)
                errors.Add("Add at least one language.");
            if (mainScene == null)
                errors.Add("Set the main scene.");
            return errors;
        }

        private static void Require(List<string> errors, string field, string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value))
                errors.Add($"{field} is required.");
            else if (value.Trim().Length > max)
                errors.Add($"{field} is longer than {max} characters.");
        }
    }
}
