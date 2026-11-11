using System.IO;
using UnityEditor;
using UnityEngine;

namespace Modules.SafeSiteApiService.Editor
{
    /// <summary>
    /// Per-project content build settings used by the scenario size check and package exporter.
    /// (The name is kept from the old Content Uploader so the existing settings asset still loads.)
    /// </summary>
    public sealed class SafeSiteUploadSettings : ScriptableObject
    {
        public const string AssetPath = "Assets/Settings/SafeSite/SafeSiteUploadSettings.asset";

        [Tooltip("Supabase Storage per-file limit (50 MB on the free plan). Bigger files are split or flagged.")]
        public int maxFileSizeMb = 50;

        public long MaxFileBytes => (long)Mathf.Max(1, maxFileSizeMb) * 1024 * 1024;

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
