using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using Newtonsoft.Json.Linq;

namespace Modules.SafeSiteApiService.Internal
{
    internal sealed class DeviceApi : IDeviceApi
    {
        private readonly ApiHttp _http;

        public DeviceApi(ApiHttp http)
        {
            _http = http;
        }

        public string DeviceToken { get; set; }

        public async Task<EnrollResponse> EnrollAsync(EnrollRequest request, CancellationToken ct = default)
        {
            var result = await _http.SendJsonAsync("POST", "/v1/device/enroll", SafeSiteJson.Serialize(request), null, null, ct);
            return SafeSiteJson.Deserialize<EnrollResponse>(result.Text);
        }

        public async Task<EnrollResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
        {
            var result = await _http.SendJsonAsync("POST", "/v1/device/login", SafeSiteJson.Serialize(request), null, null, ct);
            return SafeSiteJson.Deserialize<EnrollResponse>(result.Text);
        }

        public async Task<ConfigResult> GetConfigAsync(string ifNoneMatchConfigVersion = null, CancellationToken ct = default)
        {
            Dictionary<string, string> headers = null;
            if (!string.IsNullOrEmpty(ifNoneMatchConfigVersion))
                headers = new Dictionary<string, string> { ["If-None-Match"] = $"\"{ifNoneMatchConfigVersion}\"" };

            // UnityWebRequest treats 304 as success, so check it before parsing.
            var result = await _http.SendJsonAsync("GET", "/v1/device/config", null, DeviceToken, headers, ct);
            if (result.Status == 304)
                return new ConfigResult { NotModified = true, ETag = result.ETag };

            return new ConfigResult { Config = SafeSiteJson.Deserialize<DeviceConfig>(result.Text), ETag = result.ETag };
        }

        public async Task<Jwks> GetJwksAsync(CancellationToken ct = default)
        {
            var result = await _http.SendJsonAsync("GET", "/v1/licence/jwks", null, null, null, ct);
            return SafeSiteJson.Deserialize<Jwks>(result.Text);
        }

        public async Task<ContentManifest> GetContentAsync(string scenarioId, CancellationToken ct = default)
        {
            var path = $"/v1/device/scenarios/{Uri.EscapeDataString(scenarioId ?? "")}/content";
            var result = await _http.SendJsonAsync("GET", path, null, DeviceToken, null, ct);
            return SafeSiteJson.Deserialize<ContentManifest>(result.Text);
        }

        public Task DownloadFileAsync(string url, string destinationPath, Action<long> progress = null, CancellationToken ct = default) =>
            _http.DownloadFileAsync(url, destinationPath, progress, ct);

        public async Task<HeartbeatResponse> HeartbeatAsync(HeartbeatRequest request, CancellationToken ct = default)
        {
            // Built as a JObject so projectId can be omitted (unchanged) or sent as an explicit null (clear).
            var body = new JObject();
            if (request.AppVersion != null)
                body["appVersion"] = request.AppVersion;

            switch (request.ProjectMode)
            {
                case HeartbeatProjectMode.Clear:
                    body["projectId"] = JValue.CreateNull();
                    break;
                case HeartbeatProjectMode.Set:
                    body["projectId"] = request.ProjectId;
                    break;
            }

            if (request.InstalledScenarios != null)
                body["installedScenarios"] = JArray.FromObject(request.InstalledScenarios, SafeSiteJson.Serializer);
            if (request.Location != null)
                body["location"] = JObject.FromObject(request.Location, SafeSiteJson.Serializer);

            var result = await _http.SendJsonAsync("POST", "/v1/device/heartbeat", body.ToString(Newtonsoft.Json.Formatting.None), DeviceToken, null, ct);
            return SafeSiteJson.Deserialize<HeartbeatResponse>(result.Text);
        }

        public async Task<SessionUploadResponse> UploadSessionsAsync(IReadOnlyList<SessionRecord> sessions, CancellationToken ct = default)
        {
            var body = SafeSiteJson.Serialize(new { sessions });
            var result = await _http.SendJsonAsync("POST", "/v1/device/sessions", body, DeviceToken, null, ct);
            return SafeSiteJson.Deserialize<SessionUploadResponse>(result.Text);
        }
    }
}
