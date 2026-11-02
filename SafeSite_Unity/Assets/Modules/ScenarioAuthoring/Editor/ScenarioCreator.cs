using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// Creates a scenario in the scenario team's layout (<c>Assets/_Scenarios/&lt;Name&gt;/Scenes/&lt;Name&gt;.unity</c>
    /// plus the usual asset folders), or registers a scene that already exists there. The definition sits
    /// at the scenario folder's root and the scene goes in an Addressables group.
    /// </summary>
    public static class ScenarioCreator
    {
        public const string RootFolder = "Assets/_Scenarios";
        public const string TemplateScenePath = "Assets/Modules/ScenarioAuthoring/Editor/Templates/ScenarioTemplate.unity";

        /// <summary>The folders every scenario has, matching FallingObjects and WorkingAtHeight.</summary>
        public static readonly string[] Subfolders =
            { "Animations", "Audio", "LightingData", "Materials", "Models", "Prefabs", "Scenes", "Scripts", "Textures", "_temp" };

        /// <param name="existingScene">A scene already in the project; its scenario folder is the one above its Scenes folder.</param>
        public static ScenarioDefinition Create(string slug, string title, string category, string description, int durationMinutes,
            IEnumerable<string> languages, SceneAsset existingScene = null)
        {
            var definition = ScriptableObject.CreateInstance<ScenarioDefinition>();
            definition.slug = slug;
            definition.title = title;
            definition.category = category;
            definition.description = description;
            definition.durationMinutes = durationMinutes;
            definition.languages = languages.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();

            // Everything but the scene, which may not exist yet.
            var errors = definition.Validate().Where(e => !e.Contains("main scene")).ToList();
            if (errors.Count > 0)
            {
                Object.DestroyImmediate(definition);
                throw new System.ArgumentException(string.Join("\n", errors));
            }

            string folder;
            if (existingScene != null)
            {
                folder = FolderOf(existingScene);
                definition.mainScene = existingScene;
            }
            else
            {
                var name = FolderName(title);
                folder = $"{RootFolder}/{name}";
                if (AssetDatabase.IsValidFolder(folder))
                {
                    Object.DestroyImmediate(definition);
                    throw new System.ArgumentException($"{folder} already exists. To use its scene, pick it under 'Existing scene'.");
                }

                CreateFolders(name);
                var scenePath = $"{folder}/Scenes/{name}.unity";
                if (!AssetDatabase.CopyAsset(TemplateScenePath, scenePath))
                    throw new System.InvalidOperationException($"Couldn't copy the scenario template {TemplateScenePath} to {scenePath}.");
                definition.mainScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            }

            var definitionPath = $"{folder}/{slug}.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(definitionPath) != null)
            {
                Object.DestroyImmediate(definition);
                throw new System.ArgumentException($"{definitionPath} already exists.");
            }

            AssetDatabase.CreateAsset(definition, definitionPath);
            ScenarioGroups.Ensure(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        /// <summary>"Working at Height" → "WorkingAtHeight".</summary>
        public static string FolderName(string title)
        {
            var words = Regex.Split(title ?? "", "[^A-Za-z0-9]+").Where(w => w.Length > 0);
            return string.Concat(words.Select(w => char.ToUpper(w[0], CultureInfo.InvariantCulture) + w.Substring(1)));
        }

        /// <summary>"WorkingAtHeight" → "working-at-height".</summary>
        public static string SlugFor(string folderName) =>
            Regex.Replace(folderName ?? "", "(?<=[a-z0-9])(?=[A-Z])", "-").ToLowerInvariant();

        /// <summary>"WorkingAtHeight" → "Working At Height".</summary>
        public static string TitleFor(string folderName) =>
            Regex.Replace(folderName ?? "", "(?<=[a-z0-9])(?=[A-Z])", " ");

        /// <summary>The scenario folder of a scene: the parent of its Scenes folder, else its own folder.</summary>
        public static string FolderOf(SceneAsset scene)
        {
            var folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(scene))?.Replace('\\', '/') ?? "Assets";
            return System.IO.Path.GetFileName(folder) == "Scenes" ? System.IO.Path.GetDirectoryName(folder)?.Replace('\\', '/') : folder;
        }

        private static void CreateFolders(string name)
        {
            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "_Scenarios");
            AssetDatabase.CreateFolder(RootFolder, name);
            foreach (var sub in Subfolders)
                AssetDatabase.CreateFolder($"{RootFolder}/{name}", sub);
        }
    }
}
