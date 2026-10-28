using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using UnityEngine;

namespace Modules.SafeSiteApiService.Internal
{
    internal sealed class SafeSiteApiClient : ISafeSiteApiClient
    {
        private readonly ApiHttp _http;
        private readonly DeviceApi _device;
        private readonly AdminApi _admin;
        private readonly SupabaseAuth _auth;

        public SafeSiteApiClient(SafeSiteEnvironment environment, ISecretStore secrets)
        {
            Environment = environment;
            _http = new ApiHttp(environment.apiBaseUrl);
            _http.Logged += entry => Logged?.Invoke(entry);
            _auth = new SupabaseAuth(_http, environment, secrets);
            _device = new DeviceApi(_http);
            _admin = new AdminApi(_http, _auth);
        }

        public SafeSiteEnvironment Environment { get; }
        public IDeviceApi Device => _device;
        public IAdminApi Admin => _admin;
        public ISupabaseAuth Auth => _auth;

        public event Action<ApiLogEntry> Logged;

        public async Task<ApiPingResult> PingAsync(CancellationToken ct = default)
        {
            var result = await _http.SendJsonAsync("GET", "/openapi.json", null, null, null, ct);
            return new ApiPingResult { Status = result.Status, DurationMs = result.DurationMs };
        }
    }

    /// <summary>Hands the implementation to <see cref="SafeSiteApiClientFactory"/> in players and in the editor.</summary>
    internal static class SafeSiteApiClientRegistration
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void Register() =>
            SafeSiteApiClientFactory.Register((environment, secrets) => new SafeSiteApiClient(environment, secrets));
    }
}
