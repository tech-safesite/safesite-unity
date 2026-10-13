using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Modules.SafeSiteApiService.Editor
{
    /// <summary>Per-project Content Uploader settings. No credentials live here.</summary>
    public sealed class SafeSiteUploadSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/Settings/SafeSite/SafeSiteUploadSettings.asset";

        [Serializable]
        public sealed class EnvironmentSelection
        {
            public string environment;
            public string scenarioId;
            public string lastUploadedVersion;
        }

        public string environment = "Demo";
        public List<EnvironmentSelection> selections = new List<EnvironmentSelection>();

        [Header("Build options")]
        public bool meta = true;
        public bool pico = true;
        public bool cleanBuild = true;
        public bool skipUnchangedBundles = true;
        public bool removeOldFiles = true;

        [Tooltip("Supabase Storage per-file limit (50 MB on the free plan).")]
        public int maxFileSizeMb = 50;

        public long MaxFileBytes => (long)Mathf.Max(1, maxFileSizeMb) * 1024 * 1024;

        public EnvironmentSelection For(string environmentName)
        {
            var selection = selections.Find(s => s.environment == environmentName);
            if (selection == null)
            {
                selection = new EnvironmentSelection { environment = environmentName };
                selections.Add(selection);
            }

            return selection;
        }

        public void Save()
        {
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
        }

        public static SafeSiteUploadSettings LoadOrCreate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SafeSiteUploadSettings>(AssetPath);
            if (settings != null)
                return settings;

            settings = CreateInstance<SafeSiteUploadSettings>();
            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }
    }
}
