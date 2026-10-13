using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Modules.SafeSiteApiService.External
{
    /// <summary>
    /// Scene service for the headset lifecycle: enroll once, sync config, download scenario
    /// content, heartbeat while online, upload recorded sessions. Wraps an
    /// <see cref="ISafeSiteApiClient"/> and persists state per environment in <see cref="Store"/>.
    /// Resolve with <see cref="SafeSiteApiServiceLocator"/>.
    /// </summary>
    public interface ISafeSiteApiService
    {
        IReadOnlyList<SafeSiteEnvironment> Environments { get; }
        SafeSiteEnvironment Environment { get; }

        /// <summary>Raw client for the current environment (also exposes Admin and Auth).</summary>
        ISafeSiteApiClient Client { get; }

        /// <summary>Persisted device state for the current environment.</summary>
        IDeviceStore Store { get; }

        /// <summary>Addressables loading for downloaded scenario content.</summary>
        IScenarioContent Content { get; }

        event Action<SafeSiteEnvironment> EnvironmentChanged;

        /// <summary>Every request from the current client, with secrets redacted.</summary>
        event Action<ApiLogEntry> Logged;

        /// <summary>Switches environment; each keeps its own device token and caches.</summary>
        void SetEnvironment(string environmentName);

        /// <summary>Enrolls and stores the returned device token.</summary>
        Task<EnrollResponse> EnrollAsync(EnrollRequest request, CancellationToken ct = default);

        /// <summary>Fetches config and caches it. With <paramref name="useIfNoneMatch"/>, a 304 keeps the cached copy.</summary>
        Task<ConfigResult> SyncConfigAsync(bool useIfNoneMatch, CancellationToken ct = default);

        Task<HeartbeatResponse> SendHeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default);

        /// <summary>True when the heartbeat's configVersion differs from the cached config.</summary>
        bool IsConfigStale(HeartbeatResponse heartbeat);

        /// <summary>
        /// Downloads every file of a scenario to its version folder, refreshing expired URLs once,
        /// then records it as installed and deletes older version folders.
        /// </summary>
        Task<ScenarioInstallResult> DownloadScenarioAsync(string scenarioId, IProgress<DownloadProgress> progress = null, CancellationToken ct = default);

        void DeleteLocalScenario(string scenarioId);

        /// <summary>Adds sessions to the persisted upload queue.</summary>
        void EnqueueSessions(IEnumerable<SessionRecord> sessions);

        /// <summary>
        /// Uploads the queue in batches of 500. Accepted, duplicate and rejected sessions leave the
        /// queue; on a network/5xx failure the remaining queue is kept and the error is thrown.
        /// </summary>
        Task<QueueUploadResult> UploadSessionQueueAsync(CancellationToken ct = default);

        /// <summary>Deletes the stored token and caches for the current environment.</summary>
        void ForgetHeadset();
    }

    /// <summary>
    /// Per-environment persistence. Test quality: PlayerPrefs for the token and ids, JSON files under
    /// <c>persistentDataPath/SafeSite/</c> for the config cache, installed manifest and session queue.
    /// </summary>
    public interface IDeviceStore
    {
        string EnvironmentName { get; }

        bool IsEnrolled { get; }
        string DeviceToken { get; }
        string HeadsetId { get; }
        string HeadsetLabel { get; }
        string ClientName { get; }
        string Platform { get; }

        DeviceConfig CachedConfig { get; }

        IReadOnlyList<InstalledScenario> Installed { get; }

        /// <summary>Installed version of a scenario, or null.</summary>
        string GetInstalledVersion(string scenarioId);

        /// <summary><c>persistentDataPath/SafeSite/content/&lt;scenarioId&gt;/&lt;version&gt;/</c></summary>
        string GetContentDirectory(string scenarioId, string version);

        IReadOnlyList<SessionRecord> SessionQueue { get; }
        void ClearSessionQueue();

        event Action Changed;
    }

    /// <summary>Loads a downloaded Addressables catalog and instantiates from it (brief section 5.1).</summary>
    public interface IScenarioContent
    {
        /// <summary>Directory of the currently loaded catalog, or null.</summary>
        string LoadedDirectory { get; }

        /// <summary>
        /// Loads the catalog in <paramref name="localDirectory"/>, redirecting its bundles to the
        /// downloaded files. Returns the keys that resolve to GameObjects.
        /// </summary>
        Task<IReadOnlyList<string>> LoadCatalogAsync(string localDirectory);

        Task<GameObject> InstantiateAsync(string key, Vector3 position, Quaternion rotation);

        /// <summary>Releases instances and the loaded catalog.</summary>
        void Unload();
    }
}
