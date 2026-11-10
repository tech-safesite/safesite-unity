using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using Modules.SafeSiteApiService.Editor;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// Builds one scenario's content for each platform and writes a package
    /// (<c>ScenarioPackages/&lt;slug&gt;_&lt;version&gt;.zip</c>) for the dashboard to import.
    /// Needs no sign-in: the dashboard does the upload.
    /// </summary>
    public static class ScenarioPackageExporter
    {
        public static readonly string OutputFolder = Path.GetFullPath("ScenarioPackages");
        private static readonly string StagingRoot = Path.GetFullPath("Library/SafeSiteScenarioPackages");
        private static readonly string[] KnownPlatforms = { "meta", "pico" };

        /// <summary>Rebuilds after the first (24 MB → 12 → 6 MB bundles) when a file comes out over the limit.</summary>
        private const int MaxRetries = 2;

        public sealed class ExportResult
        {
            public string ZipPath;
            public string Version;
            public List<string> Platforms;
            public int FileCount;
            public long TotalBytes;
            public long LargestFileBytes;
        }

        public static ExportResult Export(ScenarioDefinition definition, string version, IReadOnlyList<string> platforms, Action<string> log)
        {
            log ??= _ => { };
            if (!SafeSiteTime.IsVersion(version))
                throw new UploadFailedException($"Version '{version}' isn't x.y.z.");
            if (platforms == null || platforms.Count == 0)
                throw new UploadFailedException("Select at least one platform.");
            var unknown = platforms.Where(p => !KnownPlatforms.Contains(p)).ToList();
            if (unknown.Count > 0)
                throw new UploadFailedException("Unknown platform: " + string.Join(", ", unknown));

            // The Addressables build unloads assets, which destroys this reference; reload it by path afterwards.
            var definitionPath = AssetDatabase.GetAssetPath(definition);
            var slug = definition.slug;

            var group = ScenarioGroups.Ensure(definition);
            var validation = ScenarioValidator.Validate(definition, group);
            foreach (var line in validation.Lines)
                log(line);
            if (!validation.Ok)
                throw new UploadFailedException("Validation failed; fix the errors above.");

            var staging = Path.Combine(StagingRoot, slug);
            if (Directory.Exists(staging))
                Directory.Delete(staging, true);
            Directory.CreateDirectory(staging);

            var manifest = new ScenarioPackageManifest
            {
                Slug = slug,
                Title = definition.title.Trim(),
                Category = definition.category.Trim(),
                Description = definition.description.Trim(),
                DurationMinutes = definition.durationMinutes,
                Languages = definition.languages.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()).Distinct().ToList(),
                SuggestedVersion = version,
                Platforms = platforms.ToList(),
                MainScene = ScenarioAddresses.MainScene,
                BuiltWith = new BuiltWith { UnityVersion = Application.unityVersion, AppVersion = PlayerSettings.bundleVersion },
                BuiltAt = SafeSiteTime.Now(),
            };

            var maxFileBytes = SafeSiteUploadSettings.LoadOrCreate().MaxFileBytes;
            var hooks = ContentUploadPipeline.FindHooks();
            if (hooks.Count == 0 && platforms.Count > 1)
                log("No ISafeSitePlatformBuildHook found: building once and packaging the same output for every platform.");

            // If a file still comes out over the limit, build again with smaller bundles before giving up.
            var binBytes = ScenarioGroups.SplitBundleBytes;
            for (var attempt = 1; ; attempt++)
            {
                if (attempt > 1)
                {
                    // The last build unloaded these; load them again by path.
                    definition = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(definitionPath);
                    group = ScenarioGroups.Find(definition);
                }
                var oversized = BuildPlatforms(definition, group, version, platforms, hooks, binBytes, maxFileBytes, staging, manifest, log);
                if (oversized.Count == 0)
                    break;
                var list = string.Join(", ", oversized.Select(f => $"{f.Path} ({Mb(f.SizeBytes)})"));
                if (attempt > MaxRetries)
                    throw new UploadFailedException(
                        $"Some files are still over the {maxFileBytes / (1024 * 1024)} MB limit after splitting into {binBytes / (1024 * 1024)} MB bundles: {list}. " +
                        "Usually one asset is too big on its own: run 'Check sizes' and use its fixes, then build again.");
                binBytes /= 2;
                log($"Warning: {list} came out over the {maxFileBytes / (1024 * 1024)} MB limit. Building again with {binBytes / (1024 * 1024)} MB bundles.");
                manifest.Files.Clear();
                Directory.Delete(staging, true);
                Directory.CreateDirectory(staging);
            }

            File.WriteAllText(Path.Combine(staging, ScenarioPackageManifest.FileName), manifest.ToJson());

            Directory.CreateDirectory(OutputFolder);
            var zipPath = Path.Combine(OutputFolder, $"{slug}_{version}.zip");
            WriteZip(staging, manifest, zipPath);

            definition = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(definitionPath);
            definition.lastExportedVersion = version;
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssetIfDirty(definition);

            var result = new ExportResult
            {
                ZipPath = zipPath,
                Version = version,
                Platforms = platforms.ToList(),
                FileCount = manifest.Files.Count,
                TotalBytes = manifest.Files.Sum(f => f.SizeBytes),
                LargestFileBytes = manifest.Files.Count == 0 ? 0 : manifest.Files.Max(f => f.SizeBytes),
            };
            log($"Exported {result.FileCount} files ({Mb(result.TotalBytes)}) for {string.Join(" + ", platforms)} to {zipPath}");
            return result;
        }

        /// <summary>Builds once per platform into the staging folder.</summary>
        /// <returns>Files over <paramref name="maxFileBytes"/> (empty when the build is good).</returns>
        private static List<BuildFile> BuildPlatforms(ScenarioDefinition definition, AddressableAssetGroup group, string version,
            IReadOnlyList<string> platforms, IReadOnlyList<ISafeSitePlatformBuildHook> hooks, long binBytes, long maxFileBytes,
            string staging, ScenarioPackageManifest manifest, Action<string> log)
        {
            // The group's own address and paths (used to test in the app) come back after the build.
            List<BuildFile> shared = null;
            using (ScenarioGroups.ForExport(definition, group, true, log, binBytes))
            {
                foreach (var platform in platforms)
                {
                    List<BuildFile> files;
                    if (hooks.Count > 0)
                    {
                        log($"[{platform}] Running {hooks.Count} platform hook(s) and building.");
                        foreach (var hook in hooks)
                            hook.BeforeContentBuild(platform);
                        try
                        {
                            files = AddressablesContentBuilder.Build(version, true, log, group);
                        }
                        finally
                        {
                            foreach (var hook in hooks)
                                hook.AfterContentBuild(platform);
                        }
                    }
                    else
                    {
                        files = shared ??= AddressablesContentBuilder.Build(version, true, log, group);
                    }

                    var oversized = files.Where(f => f.SizeBytes > maxFileBytes).ToList();
                    var output = AddressablesValidator.ValidateOutput(files.Where(f => f.SizeBytes <= maxFileBytes), maxFileBytes);
                    foreach (var line in output.Lines)
                        log(line);
                    if (!output.Ok)
                        throw new UploadFailedException("Build output breaks the upload rules; see above.");
                    if (oversized.Count > 0)
                        return oversized;

                    // Copy now: the next platform's clean build deletes the build folder.
                    foreach (var file in files)
                    {
                        var target = Path.Combine(staging, platform, file.Path);
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(file.FullPath, target, true);
                        manifest.Files.Add(new PackageFile { Platform = platform, Path = file.Path, SizeBytes = file.SizeBytes, Sha256 = Sha256Of(target) });
                    }
                }
            }
            return new List<BuildFile>();
        }

        public static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.0} MB";

        /// <summary>Bundles are already compressed, so they're written without recompressing; scenario.json and catalogs are deflated.</summary>
        private static void WriteZip(string staging, ScenarioPackageManifest manifest, string zipPath)
        {
            if (File.Exists(zipPath))
                File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            zip.CreateEntryFromFile(Path.Combine(staging, ScenarioPackageManifest.FileName), ScenarioPackageManifest.FileName, CompressionLevel.Optimal);
            foreach (var file in manifest.Files)
            {
                var entryName = file.Platform + "/" + file.Path;
                var level = file.Path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                zip.CreateEntryFromFile(Path.Combine(staging, file.Platform, file.Path), entryName, level);
            }
        }

        private static string Sha256Of(string path)
        {
            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return string.Concat(sha.ComputeHash(stream).Select(b => b.ToString("x2")));
        }
    }
}
