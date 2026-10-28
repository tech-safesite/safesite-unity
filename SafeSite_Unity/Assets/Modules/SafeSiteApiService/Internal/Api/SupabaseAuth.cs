using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using Newtonsoft.Json.Linq;

namespace Modules.SafeSiteApiService.Internal
{
    /// <summary>
    /// Supabase password / refresh-token sign-in, plus the dev token for Local. Only the refresh
    /// token is persisted (per environment); passwords never are.
    /// </summary>
    internal sealed class SupabaseAuth : ISupabaseAuth
    {
        private const int RefreshMarginSeconds = 60;

        private readonly ApiHttp _http;
        private readonly SafeSiteEnvironment _environment;
        private readonly ISecretStore _secrets;

        private string _accessToken;
        private string _refreshToken;

        public SupabaseAuth(ApiHttp http, SafeSiteEnvironment environment, ISecretStore secrets)
        {
            _http = http;
            _environment = environment;
            _secrets = secrets ?? new MemorySecretStore();
            _refreshToken = _secrets.Get(Key("refreshToken"));
            Email = _secrets.Get(Key("email"));
        }

        public bool IsSignedIn => !string.IsNullOrEmpty(_accessToken);
        public string Email { get; private set; }
        public DateTimeOffset? ExpiresAt { get; private set; }
        public bool CanRefresh => !string.IsNullOrEmpty(_refreshToken) && !_environment.IsLocal;

        public event Action Changed;

        private string Key(string name) => $"SafeSite.{_environment.name}.admin.{name}";

        private string TokenUrl(string grantType) => $"{_environment.supabaseUrl.TrimEnd('/')}/auth/v1/token?grant_type={grantType}";

        private Dictionary<string, string> Headers => new Dictionary<string, string> { ["apikey"] = _environment.publishableKey ?? "" };

        public async Task SignInAsync(string email, string password, CancellationToken ct = default)
        {
            RequireSupabase();
            var body = SafeSiteJson.Serialize(new { email, password });
            var result = await _http.SendJsonAsync("POST", TokenUrl("password"), body, null, Headers, ct);
            ApplySession(result.Text);
        }

        public async Task<bool> TryRestoreAsync(CancellationToken ct = default)
        {
            if (!CanRefresh)
                return false;
            try
            {
                await RefreshAsync(ct);
                return true;
            }
            catch (ApiError)
            {
                return false;
            }
        }

        public async Task RefreshAsync(CancellationToken ct = default)
        {
            RequireSupabase();
            if (string.IsNullOrEmpty(_refreshToken))
                throw new ApiError(401, "not_signed_in", "No refresh token; sign in again.");

            var body = SafeSiteJson.Serialize(new { refresh_token = _refreshToken });
            try
            {
                var result = await _http.SendJsonAsync("POST", TokenUrl("refresh_token"), body, null, Headers, ct);
                ApplySession(result.Text);
            }
            catch (ApiError e) when (!e.IsTransient && !e.IsCancelled)
            {
                // The refresh token is dead (used, revoked or expired): forget it.
                SignOut();
                throw;
            }
        }

        public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
        {
            if (!IsSignedIn && CanRefresh)
                await RefreshAsync(ct);
            if (!IsSignedIn)
                throw new ApiError(401, "not_signed_in", $"Not signed in to {_environment.name}. Sign in first.");

            if (ExpiresAt.HasValue && ExpiresAt.Value <= DateTimeOffset.UtcNow.AddSeconds(RefreshMarginSeconds) && CanRefresh)
                await RefreshAsync(ct);
            return _accessToken;
        }

        public void UseAccessToken(string accessToken)
        {
            _accessToken = string.IsNullOrWhiteSpace(accessToken) ? null : accessToken.Trim();
            _refreshToken = null;
            Email = null;
            ExpiresAt = null;
            if (_accessToken != null && TryReadClaims(_accessToken, out var claims))
            {
                Email = (string)claims["email"];
                if (claims["exp"] != null && claims["exp"].Type == JTokenType.Integer)
                    ExpiresAt = DateTimeOffset.FromUnixTimeSeconds((long)claims["exp"]);
            }

            Changed?.Invoke();
        }

        public void UseLocalDevToken()
        {
            if (!_environment.IsLocal)
                throw new InvalidOperationException("The dev token is only accepted by the Local environment.");
            UseAccessToken(DevJwt.Mint());
        }

        public void SignOut()
        {
            _accessToken = null;
            _refreshToken = null;
            ExpiresAt = null;
            Email = null;
            _secrets.Delete(Key("refreshToken"));
            _secrets.Delete(Key("email"));
            Changed?.Invoke();
        }

        private void RequireSupabase()
        {
            if (_environment.IsLocal)
                throw new ApiError(0, "local_no_auth", "Local has no Supabase Auth. Use the dev token or paste an access token.");
        }

        private void ApplySession(string json)
        {
            var session = JObject.Parse(json);
            _accessToken = (string)session["access_token"];
            _refreshToken = (string)session["refresh_token"];
            var expiresIn = (long?)session["expires_in"] ?? 3600;
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
            Email = (string)session["user"]?["email"] ?? Email;

            _secrets.Set(Key("refreshToken"), _refreshToken);
            if (Email != null)
                _secrets.Set(Key("email"), Email);
            Changed?.Invoke();
        }

        internal static bool TryReadClaims(string jwt, out JObject claims)
        {
            claims = null;
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                return false;
            try
            {
                claims = JObject.Parse(Encoding.UTF8.GetString(Base64Url.Decode(parts[1])));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    internal sealed class MemorySecretStore : ISecretStore
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        public string Get(string key) => _values.TryGetValue(key, out var value) ? value : null;
        public void Set(string key, string value) => _values[key] = value;
        public void Delete(string key) => _values.Remove(key);
    }

    internal sealed class PlayerPrefsSecretStore : ISecretStore
    {
        public string Get(string key) => UnityEngine.PlayerPrefs.HasKey(key) ? UnityEngine.PlayerPrefs.GetString(key) : null;

        public void Set(string key, string value)
        {
            UnityEngine.PlayerPrefs.SetString(key, value ?? "");
            UnityEngine.PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            UnityEngine.PlayerPrefs.DeleteKey(key);
            UnityEngine.PlayerPrefs.Save();
        }
    }
}
