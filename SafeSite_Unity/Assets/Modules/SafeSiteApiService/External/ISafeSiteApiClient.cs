using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.SafeSiteApiService.External
{
    /// <summary>
    /// Typed HTTP client for one environment. Plain C# (no scene object), so the Content
    /// Uploader can use it at edit time. Create one with <see cref="SafeSiteApiClientFactory"/>.
    /// All calls throw <see cref="ApiError"/> on failure and must be awaited on the main thread.
    /// </summary>
    public interface ISafeSiteApiClient
    {
        SafeSiteEnvironment Environment { get; }
        IDeviceApi Device { get; }
        IAdminApi Admin { get; }
        ISupabaseAuth Auth { get; }

        /// <summary>Every request, with secrets redacted.</summary>
        event Action<ApiLogEntry> Logged;

        /// <summary>GET /openapi.json: reachability and latency.</summary>
        Task<ApiPingResult> PingAsync(CancellationToken ct = default);
    }

    /// <summary>Device endpoints (brief section 3.1). Authenticated with <see cref="DeviceToken"/>.</summary>
    public interface IDeviceApi
    {
        string DeviceToken { get; set; }

        Task<EnrollResponse> EnrollAsync(EnrollRequest request, CancellationToken ct = default);

        /// <param name="ifNoneMatchConfigVersion">Sends <c>If-None-Match</c> when not null; a 304 returns <c>NotModified</c>.</param>
        Task<ConfigResult> GetConfigAsync(string ifNoneMatchConfigVersion = null, CancellationToken ct = default);

        Task<Jwks> GetJwksAsync(CancellationToken ct = default);

        Task<ContentManifest> GetContentAsync(string scenarioId, CancellationToken ct = default);

        /// <summary>Streams a signed storage URL to disk. <paramref name="progress"/> receives bytes downloaded so far.</summary>
        Task DownloadFileAsync(string url, string destinationPath, Action<long> progress = null, CancellationToken ct = default);

        Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default);

        /// <summary>1..500 sessions per call.</summary>
        Task<SessionUploadResponse> UploadSessionsAsync(IReadOnlyList<SessionRecord> sessions, CancellationToken ct = default);
    }

    /// <summary>Admin endpoints (brief section 3.2). Authenticated through <see cref="ISafeSiteApiClient.Auth"/>.</summary>
    public interface IAdminApi
    {
        Task<List<AdminClient>> GetClientsAsync(CancellationToken ct = default);

        Task<NewEnrollmentCode> CreateEnrollmentCodeAsync(string clientId, int? maxUses, int? expiresInDays, CancellationToken ct = default);

        Task<List<AdminScenario>> GetScenariosAsync(CancellationToken ct = default);

        Task<BuildFiles> GetContentFilesAsync(string scenarioId, string platform, CancellationToken ct = default);

        Task<UploadUrls> RequestUploadUrlsAsync(string scenarioId, string platform, IReadOnlyList<string> files, CancellationToken ct = default);

        /// <summary>PUTs a file to a signed upload URL (no Authorization header). <paramref name="progress"/> receives bytes sent.</summary>
        Task UploadFileAsync(string url, string localPath, Action<long> progress = null, CancellationToken ct = default);

        Task<CommitBuildResult> CommitAsync(string scenarioId, string platform, CommitBuildRequest request, CancellationToken ct = default);
    }

    /// <summary>
    /// Admin sign-in (brief section 3.3). Never stores passwords; the refresh token is kept in
    /// the <see cref="ISecretStore"/> given to the factory, keyed by environment.
    /// </summary>
    public interface ISupabaseAuth
    {
        bool IsSignedIn { get; }
        string Email { get; }
        DateTimeOffset? ExpiresAt { get; }

        event Action Changed;

        Task SignInAsync(string email, string password, CancellationToken ct = default);

        /// <summary>Signs back in from a stored refresh token. False if there is none or it was rejected.</summary>
        Task<bool> TryRestoreAsync(CancellationToken ct = default);

        Task RefreshAsync(CancellationToken ct = default);

        /// <summary>Returns a valid access token, refreshing first if it expires within a minute.</summary>
        Task<string> GetAccessTokenAsync(CancellationToken ct = default);

        /// <summary>Uses a pasted access token as-is (fallback for any environment). Not persisted.</summary>
        void UseAccessToken(string accessToken);

        /// <summary>Local only: mints the dev HS256 token the local API accepts.</summary>
        void UseLocalDevToken();

        /// <summary>Forgets the session and deletes the stored refresh token.</summary>
        void SignOut();
    }

    /// <summary>Key/value persistence for tokens: PlayerPrefs at runtime, EditorPrefs in editor tools.</summary>
    public interface ISecretStore
    {
        string Get(string key);
        void Set(string key, string value);
        void Delete(string key);
    }
}
