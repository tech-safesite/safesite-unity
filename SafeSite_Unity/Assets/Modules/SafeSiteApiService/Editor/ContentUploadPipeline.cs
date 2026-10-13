using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Modules.SafeSiteApiService.Editor
{
    [Serializable]
    public struct BuildFile
    {
        public string Path;
        public string FullPath;
        public long SizeBytes;
    }

    public sealed class UploadOptions
    {
        public List<string> Platforms = new List<string>();
        public string Version;
        public bool CleanBuild = true;
        public bool SkipUnchangedBundles = true;
        public bool RemoveOldFiles = true;
        public bool DryRun;

        /// <summary>Skip building; upload the files recorded by the last build.</summary>
        public bool UploadOnly;

        public long MaxFileBytes = 50L * 1024 * 1024;
    }

    [Serializable]
    public sealed class PlatformUploadResult
    {
        public string platform;
        public string folder;
        public int files;
        public int uploadedFiles;
        public long uploadedBytes;
        public int skippedFiles;
        public List<string> removedFiles = new List<string>();
        public bool committed;
    }

    [Serializable]
    public sealed class UploadReport
    {
        public string environment;
        public string scenarioId;
        public string scenarioSlug;
        public string previousVersion;
        public string version;
        public bool dryRun;
        public bool uploadOnly;
        public string startedAt;
        public double elapsedSeconds;
        public bool succeeded;
        public string error;
        public List<PlatformUploadResult> platforms = new List<PlatformUploadResult>();

        public string Summary()
        {
            var uploaded = platforms.Sum(p => p.uploadedFiles);
            var bytes = platforms.Sum(p => p.uploadedBytes);
            var skipped = platforms.Sum(p => p.skippedFiles);
            var removed = platforms.Sum(p => p.removedFiles.Count);
            var head = succeeded ? (dryRun ? "Dry run complete" : "Upload complete") : "FAILED: " + error;
            return $"{head}. {scenarioSlug}: uploaded {uploaded} files / {bytes / (1024.0 * 1024.0):0.00} MB, skipped {skipped}, removed {removed}, " +
                   $"version {previousVersion} -> {(succeeded && !dryRun ? version : previousVersion)}, {elapsedSeconds:0.0}s.";
        }
    }

    /// <summary>Progress sink. Return true from <see cref="Report"/> to cancel.</summary>
    public interface IUploadProgress
    {
        bool Report(string title, string info, float fraction);
        void Clear();
    }

    public sealed class UploadFailedException : Exception
    {
        public UploadFailedException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>
    /// Build &amp; Upload (brief 6.3): platform hooks, Addressables build, collect, diff against the
    /// bucket, request signed URLs, upload 4 at a time with retries, commit, report.
    /// Shared by the Content Uploader window and the CLI.
    /// </summary>
    public sealed class ContentUploadPipeline
    {
        private const int UrlChunk = 500;
        private const int Parallel = 4;
        private static readonly int[] RetryDelaysMs = { 1000, 4000, 10000 };

        public static readonly string ReportsFolder = Path.GetFullPath("Library/SafeSiteUploads");
        private static readonly string LastBuildManifest = Path.Combine(ReportsFolder, "last-build.json");

        private readonly ISafeSiteApiClient _client;
        private readonly Action<string> _log;
        private readonly IUploadProgress _progress;

        public ContentUploadPipeline(ISafeSiteApiClient client, Action<string> log, IUploadProgress progress)
        {
            _client = client;
            _log = log ?? (_ => { });
            _progress = progress;
        }

        [Serializable]
        private sealed class BuildManifest
        {
            public string version;
            public string builtAt;
            public List<BuildFile> files = new List<BuildFile>();
        }

        public static string RemoteBuildFolder()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings == null ? null : Path.GetFullPath(settings.RemoteCatalogBuildPath.GetValue(settings));
        }

        public static List<ISafeSitePlatformBuildHook> FindHooks() =>
            TypeCache.GetTypesDerivedFrom<ISafeSitePlatformBuildHook>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (ISafeSitePlatformBuildHook)Activator.CreateInstance(t))
                .ToList();

        public async Task<UploadReport> RunAsync(AdminScenario scenario, UploadOptions options, CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            var report = new UploadReport
            {
                environment = _client.Environment.name,
                scenarioId = scenario.Id,
                scenarioSlug = scenario.Slug,
                previousVersion = scenario.Version,
                version = options.Version,
                dryRun = options.DryRun,
                uploadOnly = options.UploadOnly,
                startedAt = SafeSiteTime.Now(),
            };

            try
            {
                await RunCoreAsync(scenario, options, report, ct);
                report.succeeded = true;
            }
            catch (Exception e)
            {
                report.error = e is ApiError api ? Describe(api) : e.Message;
                if (!(e is ApiError) && !(e is UploadFailedException) && !(e is OperationCanceledException))
                    UnityEngine.Debug.LogException(e);
            }
            finally
            {
                _progress?.Clear();
                stopwatch.Stop();
                report.elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                WriteReport(report);
                _log(report.Summary());
            }

            return report;
        }

        private async Task RunCoreAsync(AdminScenario scenario, UploadOptions options, UploadReport report, CancellationToken ct)
        {
            if (options.Platforms.Count == 0)
                throw new UploadFailedException("Select at least one platform.");
            if (!SafeSiteTime.IsVersion(options.Version))
                throw new UploadFailedException($"Version '{options.Version}' isn't x.y.z.");
            if (SafeSiteTime.CompareVersions(options.Version, scenario.Version) < 0)
                throw new UploadFailedException($"Version {options.Version} is lower than the current {scenario.Version}; versions can't go backwards.");
            if (options.Version == scenario.Version)
                _log($"Version stays {scenario.Version}: headsets that already have it won't re-download.");

            foreach (var platform in options.Platforms)
            {
                if (string.IsNullOrEmpty(scenario.ContentPath(platform)))
                    throw new UploadFailedException($"platform_not_configured: '{scenario.Slug}' has no {platform} content folder. Set it in the dashboard (Scenarios > edit), then try again.");
            }

            if (!options.UploadOnly)
            {
                var validation = AddressablesValidator.ValidateSettings();
                foreach (var line in validation.Lines)
                    _log(line);
                if (!validation.Ok)
                    throw new UploadFailedException("Validation failed; fix the errors above.");
            }

            var hooks = options.UploadOnly ? new List<ISafeSitePlatformBuildHook>() : FindHooks();
            var perPlatformBuild = hooks.Count > 0;
            if (!options.UploadOnly && !perPlatformBuild && options.Platforms.Count > 1)
                _log("No ISafeSitePlatformBuildHook found: building once and uploading the same output to every platform.");
            if (options.UploadOnly)
                _log("Upload only: using the files recorded by the last build.");

            List<BuildFile> shared = null;
            for (int i = 0; i < options.Platforms.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var platform = options.Platforms[i];
                var isLast = i == options.Platforms.Count - 1;

                List<BuildFile> files;
                if (options.UploadOnly)
                {
                    files = shared ??= LoadLastBuild(options.Version);
                }
                else if (perPlatformBuild)
                {
                    _log($"[{platform}] Running {hooks.Count} platform hook(s) and building.");
                    foreach (var hook in hooks)
                        hook.BeforeContentBuild(platform);
                    try
                    {
                        files = Build(options);
                    }
                    finally
                    {
                        foreach (var hook in hooks)
                            hook.AfterContentBuild(platform);
                    }
                }
                else
                {
                    files = shared ??= Build(options);
                }

                var output = AddressablesValidator.ValidateOutput(files, options.MaxFileBytes);
                foreach (var line in output.Lines)
                    _log(line);
                if (!output.Ok)
                    throw new UploadFailedException("Build output breaks the upload rules; see above.");

                // Commit the version only with the last platform, so headsets don't sync a half-published pair.
                var result = await UploadPlatformAsync(scenario, platform, files, options, isLast ? options.Version : null, ct);
                report.platforms.Add(result);
            }
        }

        private List<BuildFile> Build(UploadOptions options)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var folder = RemoteBuildFolder();
            var previousOverride = settings.OverridePlayerVersion;
            settings.OverridePlayerVersion = options.Version;
            try
            {
                if (options.CleanBuild)
                {
                    _log("Cleaning Addressables content and " + folder);
                    AddressableAssetSettings.CleanPlayerContent();
                    if (Directory.Exists(folder))
                        Directory.Delete(folder, true);
                }

                _progress?.Report("SafeSite: building content", "Addressables build", 0f);
                _log("Building Addressables content (catalog_" + options.Version + ").");
                AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
                if (!string.IsNullOrEmpty(result.Error))
                    throw new UploadFailedException("Addressables build failed: " + result.Error);

                var files = FilesOf(result, folder);
                SaveLastBuild(options.Version, files);
                _log($"Built {files.Count} files ({files.Sum(f => f.SizeBytes) / (1024.0 * 1024.0):0.00} MB) in {folder}");
                return files;
            }
            finally
            {
                settings.OverridePlayerVersion = previousOverride;
                EditorUtility.SetDirty(settings);
            }
        }

        /// <summary>The remote bundles and catalog of this build only (older bundles may still sit in the folder).</summary>
        private List<BuildFile> FilesOf(AddressablesPlayerBuildResult result, string folder)
        {
            var root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var bundle in result.AssetBundleBuildResults)
                paths.Add(Path.GetFullPath(bundle.FilePath));
            if (!string.IsNullOrEmpty(result.RemoteCatalogJsonFilePath))
                paths.Add(Path.GetFullPath(result.RemoteCatalogJsonFilePath));
            if (!string.IsNullOrEmpty(result.RemoteCatalogHashFilePath))
                paths.Add(Path.GetFullPath(result.RemoteCatalogHashFilePath));

            var remote = paths.Where(p => p.StartsWith(root, StringComparison.OrdinalIgnoreCase) && File.Exists(p)).ToList();
            if (remote.Count == 0)
            {
                _log("Build result listed no remote files; collecting the whole remote build folder.");
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

        private static void SaveLastBuild(string version, List<BuildFile> files)
        {
            Directory.CreateDirectory(ReportsFolder);
            File.WriteAllText(LastBuildManifest, JsonUtility.ToJson(new BuildManifest { version = version, builtAt = SafeSiteTime.Now(), files = files }, true));
        }

        private List<BuildFile> LoadLastBuild(string version)
        {
            if (!File.Exists(LastBuildManifest))
                throw new UploadFailedException("No previous build recorded. Run Build & Upload (or a dry run) first.");
            var manifest = JsonUtility.FromJson<BuildManifest>(File.ReadAllText(LastBuildManifest));
            if (manifest.version != version)
                _log($"Warning: the last build was made for version {manifest.version} (catalog_{manifest.version}); uploading it as {version}.");
            var missing = manifest.files.Where(f => !File.Exists(f.FullPath)).Select(f => f.Path).ToList();
            if (missing.Count > 0)
                throw new UploadFailedException("Files from the last build are gone: " + string.Join(", ", missing.Take(10)));
            return manifest.files.Select(f => new BuildFile { Path = f.Path, FullPath = f.FullPath, SizeBytes = new FileInfo(f.FullPath).Length }).ToList();
        }

        private async Task<PlatformUploadResult> UploadPlatformAsync(AdminScenario scenario, string platform, List<BuildFile> files,
            UploadOptions options, string commitVersion, CancellationToken ct)
        {
            var result = new PlatformUploadResult { platform = platform, files = files.Count };

            var remote = await _client.Admin.GetContentFilesAsync(scenario.Id, platform, ct);
            result.folder = remote.Folder;
            var remoteSizes = remote.Files.ToDictionary(f => f.Path, f => f.SizeBytes ?? -1);

            var toUpload = files.Where(f =>
                !(options.SkipUnchangedBundles && f.Path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) &&
                  remoteSizes.TryGetValue(f.Path, out var size) && size == f.SizeBytes)).ToList();
            result.skippedFiles = files.Count - toUpload.Count;

            var stale = remote.Files.Select(f => f.Path).Except(files.Select(f => f.Path)).ToList();
            _log($"[{platform}] {remote.Folder}: {toUpload.Count} to upload ({toUpload.Sum(f => f.SizeBytes) / (1024.0 * 1024.0):0.00} MB), " +
                 $"{result.skippedFiles} unchanged bundles skipped, {stale.Count} old file(s) {(options.RemoveOldFiles ? "to remove" : "kept")}.");

            if (options.DryRun)
            {
                foreach (var file in toUpload)
                    _log($"  would upload {file.Path} ({file.SizeBytes} bytes)");
                foreach (var path in stale)
                    _log($"  would {(options.RemoveOldFiles ? "remove" : "keep")} {path}");
                return result;
            }

            if (toUpload.Count > 0)
                await UploadFilesAsync(scenario, platform, toUpload, result, ct);

            ct.ThrowIfCancellationRequested();
            _progress?.Report("SafeSite: committing", $"{platform} {commitVersion ?? "(version unchanged)"}", 1f);
            var commit = await _client.Admin.CommitAsync(scenario.Id, platform, new CommitBuildRequest
            {
                Files = files.Select(f => f.Path).ToList(),
                Version = commitVersion,
                RemoveStale = options.RemoveOldFiles,
            }, ct);

            result.removedFiles = commit.RemovedFiles ?? new List<string>();
            result.committed = true;
            _log($"[{platform}] Committed{(commitVersion != null ? " version " + commitVersion : "")}; removed {result.removedFiles.Count}: {string.Join(", ", result.removedFiles)}");
            if (commit.Scenario != null)
                scenario.Version = commit.Scenario.Version;
            return result;
        }

        private async Task UploadFilesAsync(AdminScenario scenario, string platform, List<BuildFile> files, PlatformUploadResult result, CancellationToken ct)
        {
            var urls = await RequestUrlsAsync(scenario.Id, platform, files.Select(f => f.Path).ToList(), ct);
            var totalBytes = Math.Max(1, files.Sum(f => f.SizeBytes));
            var sent = new Dictionary<string, long>();
            var completed = 0;
            var stopwatch = Stopwatch.StartNew();

            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var queue = new Queue<BuildFile>(files);

            void Progress(string current)
            {
                var bytes = sent.Values.Sum();
                var mbps = bytes / (1024.0 * 1024.0) / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                if (_progress != null && _progress.Report($"SafeSite: uploading {platform}",
                        $"{current}  {completed}/{files.Count}  {mbps:0.0} MB/s", (float)bytes / totalBytes))
                    cancel.Cancel();
            }

            async Task Worker()
            {
                while (queue.Count > 0)
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    var file = queue.Dequeue();
                    await UploadOneAsync(scenario.Id, platform, file, urls, bytes =>
                    {
                        sent[file.Path] = bytes;
                        Progress(file.Path);
                    }, cancel.Token);
                    sent[file.Path] = file.SizeBytes;
                    completed++;
                    result.uploadedFiles++;
                    result.uploadedBytes += file.SizeBytes;
                    Progress(file.Path);
                }
            }

            try
            {
                await Task.WhenAll(Enumerable.Range(0, Math.Min(Parallel, files.Count)).Select(_ => Worker()));
            }
            catch (Exception) when (cancel.IsCancellationRequested)
            {
                throw new OperationCanceledException("Upload cancelled; nothing was committed and the scenario version is unchanged.");
            }

            _log($"[{platform}] Uploaded {files.Count} files in {stopwatch.Elapsed.TotalSeconds:0.0}s.");
        }

        private async Task<Dictionary<string, string>> RequestUrlsAsync(string scenarioId, string platform, List<string> paths, CancellationToken ct)
        {
            var urls = new Dictionary<string, string>();
            for (int i = 0; i < paths.Count; i += UrlChunk)
            {
                var chunk = paths.Skip(i).Take(UrlChunk).ToList();
                var response = await _client.Admin.RequestUploadUrlsAsync(scenarioId, platform, chunk, ct);
                foreach (var upload in response.Uploads)
                    urls[upload.Path] = upload.Url;
            }

            return urls;
        }

        private async Task UploadOneAsync(string scenarioId, string platform, BuildFile file, Dictionary<string, string> urls,
            Action<long> progress, CancellationToken ct)
        {
            var refreshedUrl = false;
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    await _client.Admin.UploadFileAsync(urls[file.Path], file.FullPath, progress, ct);
                    return;
                }
                catch (ApiError e) when (e.Status == 413 || (e.RawBody ?? "").IndexOf("Payload too large", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    throw new UploadFailedException($"{file.Path} ({file.SizeBytes / (1024.0 * 1024.0):0.0} MB) is over the storage per-file size limit. " +
                                                    "Split the group or raise the limit (Supabase > Storage settings).", e);
                }
                catch (ApiError e) when (!refreshedUrl && (e.Status == 400 || e.Status == 403) && LooksExpired(e))
                {
                    refreshedUrl = true;
                    _log($"Upload URL for {file.Path} expired; requesting fresh URLs.");
                    var fresh = await _client.Admin.RequestUploadUrlsAsync(scenarioId, platform, new[] { file.Path }, ct);
                    urls[file.Path] = fresh.Uploads.First().Url;
                }
                catch (ApiError e) when (e.IsTransient && attempt < RetryDelaysMs.Length)
                {
                    _log($"Upload of {file.Path} failed ({e.Status} {e.Code}); retry {attempt + 1} in {RetryDelaysMs[attempt] / 1000}s.");
                    await Task.Delay(RetryDelaysMs[attempt], ct);
                }
            }
        }

        private static bool LooksExpired(ApiError e)
        {
            var text = ((e.RawBody ?? "") + " " + e.ErrorMessage).ToLowerInvariant();
            return text.Contains("signature") || text.Contains("expired") || text.Contains("jwt");
        }

        private static string Describe(ApiError e)
        {
            var text = $"{e.Status} {e.Code}: {e.ErrorMessage}";
            switch (e.Code)
            {
                case ApiErrorCodes.PlatformNotConfigured:
                    text += " The scenario has no folder for this platform. Set it in the dashboard (Scenarios > edit).";
                    break;
                case ApiErrorCodes.VersionNotIncreased:
                    text += " Bump the version above the scenario's current one.";
                    break;
                case ApiErrorCodes.MissingFiles:
                    text += " Missing: " + string.Join(", ", e.DetailStrings.Take(20));
                    break;
                case ApiErrorCodes.NotAdmin:
                    text += " This account isn't a SafeSite admin.";
                    break;
            }

            foreach (var detail in e.ValidationDetails)
                text += $"\n  {detail.Path}: {detail.Message}";
            return text;
        }

        private static void WriteReport(UploadReport report)
        {
            try
            {
                Directory.CreateDirectory(ReportsFolder);
                var name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                File.WriteAllText(Path.Combine(ReportsFolder, name), JsonUtility.ToJson(report, true));
            }
            catch (IOException e)
            {
                UnityEngine.Debug.LogWarning("[SafeSiteContentUploader] Could not write report: " + e.Message);
            }
        }
    }
}
