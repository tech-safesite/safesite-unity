using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>Creates a scenario's folder, scene, definition and Addressables group in one step.</summary>
    public static class ScenarioCreator
    {
        public const string RootFolder = "Assets/Scenarios";
        public const string TemplateScenePath = "Assets/Modules/ScenarioAuthoring/Editor/Templates/ScenarioTemplate.unity";

        public static ScenarioDefinition Create(string slug, string title, string category, string description, int durationMinutes, IEnumerable<string> languages)
        {
            var definition = ScriptableObject.CreateInstance<ScenarioDefinition>();
            definition.slug = slug;
            definition.title = title;
            definition.category = category;
            definition.description = description;
            definition.durationMinutes = durationMinutes;
            definition.languages = languages.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList();

            // Everything but the scene, which doesn't exist yet.
            var errors = definition.Validate().Where(e => !e.Contains("main scene")).ToList();
            if (errors.Count > 0)
            {
                Object.DestroyImmediate(definition);
                throw new System.ArgumentException(string.Join("\n", errors));
            }

            var folder = $"{RootFolder}/{slug}";
            if (AssetDatabase.IsValidFolder(folder))
            {
                Object.DestroyImmediate(definition);
                throw new System.ArgumentException($"{folder} already exists.");
            }

            if (!AssetDatabase.IsValidFolder(RootFolder))
                AssetDatabase.CreateFolder("Assets", "Scenarios");
            AssetDatabase.CreateFolder(RootFolder, slug);

            var scenePath = $"{folder}/{slug}.unity";
            CreateScene(scenePath);
            definition.mainScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);

            AssetDatabase.CreateAsset(definition, $"{folder}/{slug}.asset");
            ScenarioGroups.Ensure(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        /// <summary>
        /// Copies the template (an empty scene with a ScenarioRoot object), so the open scenes are never
        /// touched. No camera or lights: the app provides the XR rig and loads scenarios additively.
        /// </summary>
        private static void CreateScene(string path)
        {
            if (!AssetDatabase.CopyAsset(TemplateScenePath, path))
                throw new System.InvalidOperationException($"Couldn't copy the scenario template {TemplateScenePath} to {path}.");
        }
    }
}
