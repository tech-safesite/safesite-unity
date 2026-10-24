using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Modules.SafeSiteApiService.Editor
{
    /// <summary>
    /// Builds Addressables content into the remote build folder and lists the files of that build.
    /// Shared by the Content Uploader (whole project) and the scenario package exporter (one group).
    /// </summary>
    public static class AddressablesContentBuilder
    {
        public static string RemoteBuildFolder()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings == null ? null : Path.GetFullPath(settings.RemoteCatalogBuildPath.GetValue(settings));
        }

        /// <summary>
        /// Builds content with the catalog named after <paramref name="version"/>.
        /// With <paramref name="onlyGroup"/>, every other group is left out of the build and that group
        /// stands in as the default group, so the built-in shader and MonoScript bundles land next to
        /// its bundles. Group and settings changes are always restored afterwards.
        /// </summary>
        public static List<BuildFile> Build(string version, bool clean, Action<string> log, AddressableAssetGroup onlyGroup = null)
        {
            log ??= _ => { };
            var settings = AddressableAssetSettingsDefaultObject.Settings
                           ?? throw new UploadFailedException("No Addressables settings. Run 'Fix Addressables settings' first.");
            var folder = RemoteBuildFolder();

            var previousVersion = settings.OverridePlayerVersion;
            var previousDefault = settings.DefaultGroup;
            var included = new List<(BundledAssetGroupSchema Schema, bool IncludeInBuild)>();
            try
            {
                settings.OverridePlayerVersion = version;
                if (onlyGroup != null)
                {
                    foreach (var group in settings.groups.Where(g => g != null))
                    {
                        var schema = group.GetSchema<BundledAssetGroupSchema>();
                        if (schema == null)
                            continue;
                        included.Add((schema, schema.IncludeInBuild));
                        schema.IncludeInBuild = group == onlyGroup;
                    }

                    settings.DefaultGroup = onlyGroup;
                    log($"Building only group '{onlyGroup.Name}'.");
                }

                if (clean)
                {
                    log("Cleaning Addressables content and " + folder);
                    AddressableAssetSettings.CleanPlayerContent();
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                }

                log("Building Addressables content (catalog_" + version + ").");
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
                if (!string.IsNullOrEmpty(result.Error))
                    throw new UploadFailedException("Addressables build failed: " + result.Error);

                var files = FilesOf(result, folder, version, log);
                log($"Built {files.Count} files ({files.Sum(f => f.SizeBytes) / (1024.0 * 1024.0):0.00} MB) in {folder}");
                return files;
            }
            finally
            {
                foreach (var (schema, includeInBuild) in included)
                {
                    schema.IncludeInBuild = includeInBuild;
                    EditorUtility.SetDirty(schema);
                }

                if (onlyGroup != null)
                    settings.DefaultGroup = previousDefault;
                settings.OverridePlayerVersion = previousVersion;
                EditorUtility.SetDirty(settings);
            }
        }

        /// <summary>The remote bundles and catalog of this build only (older bundles may still sit in the folder).</summary>
        private static List<BuildFile> FilesOf(AddressablesPlayerBuildResult result, string folder, string version, Action<string> log)
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in result.AssetBundleBuildResults)
                paths.Add(Path.GetFullPath(bundle.FilePath));
            if (!string.IsNullOrEmpty(result.RemoteCatalogJsonFilePath))
                paths.Add(Path.GetFullPath(result.RemoteCatalogJsonFilePath));
            if (!string.IsNullOrEmpty(result.RemoteCatalogHashFilePath))
                paths.Add(Path.GetFullPath(result.RemoteCatalogHashFilePath));
            // Addressables 2.x writes a binary catalog (catalog_<version>.bin) that the build result doesn't list.
            if (Directory.Exists(folder))
            {
                foreach (var catalog in Directory.EnumerateFiles(folder, $"catalog_{version}.*"))
                    paths.Add(Path.GetFullPath(catalog));
            }

            var remote = paths.Where(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(p)).ToList();
            if (remote.Count == 0)
            {
                log("Build result listed no remote files; collecting the whole remote build folder.");
                remote = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList() : new List<string>();
            }

            return remote
                .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(p) != ".DS_Store")
                .Select(p => new BuildFile
                {
                    FullPath = p,
                    Path = p.Substring(root.Length).Replace('\\', '/'),
                    SizeBytes = new FileInfo(p).Length,
                })
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .ToList();
        }
    }
}
