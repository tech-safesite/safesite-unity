using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;

namespace Modules.SafeSiteApiService.Internal
{
    internal sealed class AdminApi : IAdminApi
    {
        private readonly ApiHttp _http;
        private readonly SupabaseAuth _auth;

        public AdminApi(ApiHttp http, SupabaseAuth auth)
        {
            _http = http;
            _auth = auth;
        }

        private sealed class ItemsEnvelope<T>
        {
            public List<T> Items = new List<T>();
        }

        public async Task<List<AdminClient>> GetClientsAsync(CancellationToken ct = default) =>
            (await SendAsync<ItemsEnvelope<AdminClient>>("GET", "/v1/admin/clients", null, ct))?.Items ?? new List<AdminClient>();

        public Task<NewEnrollmentCode> CreateEnrollmentCodeAsync(string clientId, int? maxUses, int? expiresInDays, CancellationToken ct = default) =>
            SendAsync<NewEnrollmentCode>("POST", $"/v1/admin/clients/{Escape(clientId)}/enrollment-codes",
                new { maxUses, expiresInDays }, ct);

        public async Task<List<AdminScenario>> GetScenariosAsync(CancellationToken ct = default) =>
            (await SendAsync<ItemsEnvelope<AdminScenario>>("GET", "/v1/admin/scenarios", null, ct))?.Items ?? new List<AdminScenario>();

        public Task<BuildFiles> GetContentFilesAsync(string scenarioId, string platform, CancellationToken ct = default) =>
            SendAsync<BuildFiles>("GET", ContentPath(scenarioId, platform), null, ct);

        public Task<UploadUrls> RequestUploadUrlsAsync(string scenarioId, string platform, IReadOnlyList<string> files, CancellationToken ct = default) =>
            SendAsync<UploadUrls>("POST", ContentPath(scenarioId, platform) + "/uploads", new { files }, ct);

        public Task UploadFileAsync(string url, string localPath, Action<long> progress = null, CancellationToken ct = default) =>
            _http.UploadFileAsync(url, localPath, progress, ct);

        public Task<CommitBuildResult> CommitAsync(string scenarioId, string platform, CommitBuildRequest request, CancellationToken ct = default) =>
            SendAsync<CommitBuildResult>("POST", ContentPath(scenarioId, platform) + "/commit", request, ct);

        private static string ContentPath(string scenarioId, string platform) =>
            $"/v1/admin/scenarios/{Escape(scenarioId)}/content/{Escape(platform)}";

        private static string Escape(string value) => Uri.EscapeDataString(value ?? "");

        /// <summary>Sends with the admin token; on 401 <c>invalid_token</c> refreshes once and retries.</summary>
        private async Task<T> SendAsync<T>(string method, string path, object body, CancellationToken ct)
        {
            var json = body == null ? null : SafeSiteJson.Serialize(body);
            var token = await _auth.GetAccessTokenAsync(ct);
            try
            {
                var result = await _http.SendJsonAsync(method, path, json, token, null, ct);
                return SafeSiteJson.Deserialize<T>(result.Text);
            }
            catch (ApiError e) when (e.Status == 401 && e.Code == ApiErrorCodes.InvalidToken && _auth.CanRefresh)
            {
                await _auth.RefreshAsync(ct);
                token = await _auth.GetAccessTokenAsync(ct);
                var result = await _http.SendJsonAsync(method, path, json, token, null, ct);
                return SafeSiteJson.Deserialize<T>(result.Text);
            }
        }
    }
}
