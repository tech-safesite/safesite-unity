using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Modules.SafeSiteApiService.Internal
{
    /// <summary>
    /// Scene-owned implementation. Put this component on a loaded scene object for runtime code
    /// to resolve it through <see cref="SafeSiteApiServiceLocator"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SafeSiteApiService : MonoBehaviour, ISafeSiteApiService
    {
        private const string LogPrefix = "[SafeSiteApiService]";
        private const string SelectedEnvironmentKey = "SafeSite.selectedEnvironment";
        private const int SessionBatchSize = 500;

        [Tooltip("Optional. Defaults to Resources/SafeSiteEnvironments.")]
        [SerializeField] private SafeSiteEnvironments environments;

        [Tooltip("Remember the last selected environment in PlayerPrefs. Production is never restored automatically.")]
        [SerializeField] private bool rememberSelectedEnvironment = true;

        [SerializeField] private bool logRequestsToConsole;

        private SafeSiteEnvironments _environments;
        private SafeSiteApiClient _client;
        private DeviceStore _store;
        private readonly ScenarioContent _content = new ScenarioContent();

        public IReadOnlyList<SafeSiteEnvironment> Environments
        {
            get
            {
                EnsureInitialized();
                return _environments.environments;
            }
        }

        public SafeSiteEnvironment Environment
        {
            get
            {
                EnsureInitialized();
                return _client.Environment;
            }
        }

        public ISafeSiteApiClient Client
        {
            get
            {
                EnsureInitialized();
                return _client;
            }
        }

        public IDeviceStore Store
        {
            get
            {
                EnsureInitialized();
                return _store;
            }
        }

        public IScenarioContent Content => _content;

        public event Action<SafeSiteEnvironment> EnvironmentChanged;
        public event Action<ApiLogEntry> Logged;

        private void Awake() => EnsureInitialized();

        private void OnDestroy() => _content.Unload();

        private void EnsureInitialized()
        {
            if (_client != null)
                return;

            _environments = environments != null ? environments : SafeSiteEnvironments.Load();
            SafeSiteEnvironment initial = null;
            if (rememberSelectedEnvironment)
            {
                initial = _environments.Find(PlayerPrefs.GetString(SelectedEnvironmentKey, ""));
                if (initial != null && initial.IsProduction)
                    initial = null;
            }

            Activate(initial ?? _environments.Default);
        }

        public void SetEnvironment(string environmentName)
        {
            EnsureInitialized();
            var environment = _environments.Find(environmentName);
            if (environment == null)
                throw new ArgumentException($"Unknown environment '{environmentName}'");
            if (environment == _client.Environment)
                return;

            _content.Unload();
            Activate(environment);
            if (rememberSelectedEnvironment)
            {
                PlayerPrefs.SetString(SelectedEnvironmentKey, environment.name);
                PlayerPrefs.Save();
            }

            Debug.Log($"{LogPrefix} Environment: {environment.name} ({environment.apiBaseUrl})");
            EnvironmentChanged?.Invoke(environment);
        }

        private void Activate(SafeSiteEnvironment environment)
        {
            if (environment == null)
                throw new InvalidOperationException($"{LogPrefix} No environments configured.");

            _client = new SafeSiteApiClient(environment, new PlayerPrefsSecretStore());
            _client.Logged += OnLogged;
            _store = new DeviceStore(environment.name);
            _client.Device.DeviceToken = _store.DeviceToken;
        }

        private void OnLogged(ApiLogEntry entry)
        {
            if (logRequestsToConsole)
                Debug.Log($"{LogPrefix} {entry}");
            Logged?.Invoke(entry);
        }

        public async Task<EnrollResponse> EnrollAsync(EnrollRequest request, CancellationToken ct = default)
        {
            EnsureInitialized();
            var response = await _client.Device.EnrollAsync(request, ct);
            _store.SaveEnrollment(response, request.Platform);
            _client.Device.DeviceToken = response.DeviceToken;
            Debug.Log($"{LogPrefix} Enrolled as {response.Label} for {response.Client?.Name}.");
            return response;
        }

        public async Task<EnrollResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            EnsureInitialized();
            var response = await _client.Device.LoginAsync(request, ct);
            _store.SaveEnrollment(response, request.Platform);
            _client.Device.DeviceToken = response.DeviceToken;
            Debug.Log($"{LogPrefix} Signed in as {response.Label} for {response.Client?.Name}.");
            return response;
        }

        public async Task<ConfigResult> SyncConfigAsync(bool useIfNoneMatch, CancellationToken ct = default)
        {
            EnsureInitialized();
            var cachedVersion = useIfNoneMatch ? _store.CachedConfig?.ConfigVersion : null;
            var result = await _client.Device.GetConfigAsync(cachedVersion, ct);
            if (!result.NotModified)
                _store.SaveConfig(result.Config);
            return result;
        }

        public async Task<HeartbeatResponse> SendHeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
        {
            EnsureInitialized();
            return await _client.Device.HeartbeatAsync(request, ct);
        }

        public bool IsConfigStale(HeartbeatResponse heartbeat) =>
            heartbeat != null && Store.CachedConfig?.ConfigVersion != heartbeat.ConfigVersion;

        public async Task<ScenarioInstallResult> DownloadScenarioAsync(string scenarioId, IProgress<DownloadProgress> progress = null, CancellationToken ct = default)
        {
            EnsureInitialized();
            var stopwatch = Stopwatch.StartNew();
            var manifest = await _client.Device.GetContentAsync(scenarioId, ct);

            var finalDirectory = _store.GetContentDirectory(scenarioId, manifest.Version);
            var partialDirectory = finalDirectory + ".partial";
            DeviceStore.TryDeleteDirectory(partialDirectory);
            Directory.CreateDirectory(partialDirectory);

            long total = manifest.Files.Sum(f => f.SizeBytes ?? 0);
            long done = 0;
            var report = new DownloadProgress { FileCount = manifest.Files.Count, BytesTotal = total };

            for (int i = 0; i < manifest.Files.Count; i++)
            {
                var file = manifest.Files[i];
                var destination = Path.Combine(partialDirectory, ValidateRelativePath(file.Path));
                var url = file.Url;
                var baseDone = done;
                var refreshed = false;

                report.FileIndex = i;
                report.CurrentFile = file.Path;

                while (true)
                {
                    try
                    {
                        await _client.Device.DownloadFileAsync(url, destination, bytes =>
                        {
                            report.BytesDone = baseDone + bytes;
                            report.BytesPerSecond = report.BytesDone / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
                            progress?.Report(report);
                        }, ct);
                        break;
                    }
                    catch (ApiError e) when (!refreshed && (e.Status == 400 || e.Status == 403))
                    {
                        // Signed URL expired or was rejected: ask for fresh URLs once.
                        refreshed = true;
                        Debug.LogWarning($"{LogPrefix} Storage refused {file.Path} ({e.Status} {e.Code}); requesting fresh URLs.");
                        var fresh = await _client.Device.GetContentAsync(scenarioId, ct);
                        if (fresh.Version != manifest.Version)
                            throw new ApiError(409, "version_changed", $"Scenario changed to {fresh.Version} during download; start again.");
                        url = fresh.Files.FirstOrDefault(f => f.Path == file.Path)?.Url
                              ?? throw new ApiError(404, ApiErrorCodes.NotFound, $"{file.Path} is no longer in the scenario.");
                    }
                }

                done = baseDone + new FileInfo(destination).Length;
                report.BytesDone = done;
                progress?.Report(report);
            }

            DeviceStore.TryDeleteDirectory(finalDirectory);
            Directory.Move(partialDirectory, finalDirectory);
            _store.SetInstalled(scenarioId, manifest.Version);

            var scenarioDirectory = _store.GetScenarioDirectory(scenarioId);
            foreach (var old in Directory.GetDirectories(scenarioDirectory))
            {
                if (!string.Equals(Path.GetFullPath(old), Path.GetFullPath(finalDirectory), StringComparison.OrdinalIgnoreCase))
                    DeviceStore.TryDeleteDirectory(old);
            }

            stopwatch.Stop();
            Debug.Log($"{LogPrefix} Installed {scenarioId} v{manifest.Version}: {manifest.Files.Count} files, {done} bytes in {stopwatch.Elapsed.TotalSeconds:0.0}s.");
            return new ScenarioInstallResult
            {
                ScenarioId = scenarioId,
                Version = manifest.Version,
                Directory = finalDirectory,
                Files = manifest.Files.Count,
                Bytes = done,
                Seconds = stopwatch.Elapsed.TotalSeconds,
            };
        }

        public void DeleteLocalScenario(string scenarioId)
        {
            EnsureInitialized();
            var directory = _store.GetScenarioDirectory(scenarioId);
            if (_content.LoadedDirectory != null && Path.GetFullPath(_content.LoadedDirectory).StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
                _content.Unload();
            DeviceStore.TryDeleteDirectory(directory);
            _store.RemoveInstalled(scenarioId);
        }

        public void EnqueueSessions(IEnumerable<SessionRecord> sessions)
        {
            EnsureInitialized();
            _store.Enqueue(sessions);
        }

        public async Task<QueueUploadResult> UploadSessionQueueAsync(CancellationToken ct = default)
        {
            EnsureInitialized();
            var result = new QueueUploadResult();
            while (_store.SessionQueue.Count > 0)
            {
                var batch = _store.SessionQueue.Take(SessionBatchSize).ToList();

                // Throws on failure, which leaves this batch and the rest in the queue.
                var response = await _client.Device.UploadSessionsAsync(batch, ct);

                result.Accepted += response.Accepted;
                result.Duplicates += response.Duplicates;
                result.Rejected.AddRange(response.Rejected);
                result.Sent.AddRange(batch);
                result.Batches++;

                // Accepted, duplicate and rejected sessions are all done with: rejected ones never succeed.
                _store.RemoveFromQueue(batch.Select(s => s.DeviceSessionId).ToList());
            }

            return result;
        }

        public void ForgetHeadset()
        {
            EnsureInitialized();
            _content.Unload();
            _store.Forget();
            _client.Device.DeviceToken = null;
            Debug.Log($"{LogPrefix} Forgot headset for {_store.EnvironmentName}.");
        }

        /// <summary>Content paths come from the server: refuse anything that could escape the folder.</summary>
        private static string ValidateRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.StartsWith("/") || path.Contains("..") || path.Contains("\\") || path.Contains(":"))
                throw new ApiError(0, "invalid_content_path", $"Refusing content path '{path}'.");
            return path.Replace('/', Path.DirectorySeparatorChar);
        }
    }
}
