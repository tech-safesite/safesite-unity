using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using Modules.ScenarioLauncher.External;
using UnityEngine;
using UnityEngine.Serialization;

namespace Modules.ScenarioLauncher.Internal
{
    /// <summary>
    /// Client sign-in, scenario list, download and play, on top of the SafeSite API service. Any
    /// client's login code links the headset to that client (online); after that the same code is
    /// checked against the hash in the synced config, so signing in again also works offline.
    /// Needs an ISafeSiteApiService in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenarioLauncherService : MonoBehaviour, IScenarioLauncherService
    {
        private const string LogPrefix = "[ScenarioLauncherService]";

        [Tooltip("SafeSite environment to use (Local, Demo or Production). Empty keeps the API service's choice.")]
        [SerializeField] private string environmentName = "Local";

        [Tooltip("Derive this device's serial from the login code, so one PC can sign in to several clients " +
                 "(each client sees it as its own headset). Off: the real device id is used, so the headset " +
                 "moves to whichever client signs in, as on a real headset.")]
        [FormerlySerializedAs("serialPerSetUpCode"), FormerlySerializedAs("serialPerAccessCode")]
        [SerializeField] private bool serialPerCode = true;

        private ISafeSiteApiService _api;
        private List<LauncherScenario> _scenarios = new List<LauncherScenario>();
        private bool _busy;

        public LauncherState State { get; private set; } = LauncherState.SignedOut;
        public string ClientName { get; private set; }
        public string HeadsetLabel { get; private set; }
        public string LicenceStatus { get; private set; }
        public IReadOnlyList<LauncherScenario> Scenarios => _scenarios;
        public LauncherScenario Current { get; private set; }
        public float Progress { get; private set; }
        public string ProgressText { get; private set; }
        public string Error { get; private set; }

        public event Action Changed;

        /// <summary>Remembers which login code this headset was signed in with, per environment and headset.</summary>
        private string LoginKey => $"SafeSite.Launcher.{_api.Store.EnvironmentName}.{_api.Store.HeadsetId}.Login";

        private async void Start()
        {
            _api = SafeSiteApiServiceLocator.FindInScene();
            if (_api == null)
            {
                Fail("This scene has no SafeSite API service.");
                Debug.LogError($"{LogPrefix} No ISafeSiteApiService in the scene. Add the SafeSiteApiService component.");
                return;
            }

            if (!string.IsNullOrEmpty(environmentName))
                _api.SetEnvironment(environmentName);
            Debug.Log($"{LogPrefix} Using {_api.Environment.name} ({_api.Environment.apiBaseUrl}).");

            if (!_api.Store.IsEnrolled)
            {
                SetState(LauncherState.SignedOut);
                return;
            }

            ClientName = _api.Store.ClientName;
            HeadsetLabel = _api.Store.HeadsetLabel;
            ApplyConfig(_api.Store.CachedConfig);
            if (IsSignedIn(_api.Store.CachedConfig))
            {
                // Still signed in from last time: go straight to the list.
                SetState(LauncherState.Browsing);
                await RefreshAsync();
            }
            else
            {
                SetState(LauncherState.SignedOut);
            }
        }

        public async Task SignInAsync(string loginCode)
        {
            var code = Normalise(loginCode);
            if (code.Length == 0)
            {
                Fail("Enter your login code.");
                return;
            }

            if (!Begin(LauncherState.SigningIn))
                return;
            try
            {
                // Already this client's headset: the code matches its config. Sync first, in case an
                // admin replaced the code; offline, use the last synced config.
                if (_api.Store.IsEnrolled)
                {
                    DeviceConfig config = null;
                    var offline = false;
                    try
                    {
                        config = await SyncAsync();
                    }
                    catch (ApiError e) when (IsRemoved(e))
                    {
                        // Deactivated (or the local API restarted): the login below links it again.
                        _api.ForgetHeadset();
                    }
                    catch (ApiError e) when (e.Code == ApiErrorCodes.NetworkError || e.IsTransient)
                    {
                        Debug.Log($"{LogPrefix} Offline ({e.Code}); checking the login code against the last synced config.");
                        config = _api.Store.CachedConfig;
                        offline = true;
                    }

                    if (Matches(config, code))
                    {
                        SignedIn(config);
                        return;
                    }
                    if (offline)
                    {
                        SetState(LauncherState.SignedOut);
                        Fail("Can't reach SafeSite to check that code. Check the internet connection and try again.");
                        return;
                    }
                }

                // Any other client's code: link (or move) this headset to that client.
                var response = await _api.LoginAsync(new LoginRequest
                {
                    LoginCode = code,
                    SerialNumber = SerialFor(code),
                    Platform = Application.platform == RuntimePlatform.Android ? CurrentAndroidPlatform() : SafeSitePlatforms.Meta,
                    Model = SystemInfo.deviceModel,
                    AppVersion = AppVersion(),
                });
                ClientName = response.Client?.Name;
                HeadsetLabel = response.Label;
                Debug.Log($"{LogPrefix} Linked to {ClientName} as {HeadsetLabel}.");
                SignedIn(await SyncAsync());
            }
            catch (Exception e)
            {
                SetState(LauncherState.SignedOut);
                Fail(Describe(e));
            }
            finally
            {
                _busy = false;
            }
        }

        private void SignedIn(DeviceConfig config)
        {
            PlayerPrefs.SetString(LoginKey, config.Client.LoginCodeHash);
            PlayerPrefs.Save();
            Debug.Log($"{LogPrefix} Signed in to {ClientName} on {HeadsetLabel}.");
            SetState(LauncherState.Browsing);
        }

        public async Task RefreshAsync()
        {
            if (_busy || State != LauncherState.Browsing)
                return;
            _busy = true;
            Error = null;
            try
            {
                var config = await SyncAsync();
                if (!IsSignedIn(config))
                {
                    PlayerPrefs.DeleteKey(LoginKey);
                    SetState(LauncherState.SignedOut);
                    Fail("Your company's login code has changed. Enter the new one.");
                    return;
                }
                Raise();
            }
            catch (ApiError e) when (IsRemoved(e))
            {
                HeadsetRemoved();
            }
            catch (Exception e)
            {
                // Keep showing the cached list; downloaded scenarios still work offline.
                Fail(Describe(e));
            }
            finally
            {
                _busy = false;
            }
        }

        public async Task PlayAsync(string scenarioId)
        {
            var scenario = _scenarios.FirstOrDefault(s => s.Id == scenarioId);
            if (scenario == null || State != LauncherState.Browsing || !Begin(LauncherState.Downloading))
                return;

            Current = scenario;
            try
            {
                if (!scenario.IsInstalled)
                {
                    Progress = 0;
                    ProgressText = "Starting download…";
                    Raise();
                    var progress = new Progress<DownloadProgress>(p =>
                    {
                        Progress = p.Fraction;
                        ProgressText = $"{Mb(p.BytesDone)} of {Mb(p.BytesTotal)}";
                        Raise();
                    });
                    await _api.DownloadScenarioAsync(scenario.Id, progress);
                    scenario.InstalledVersion = scenario.Version;
                }

                Progress = 1;
                ProgressText = "Loading…";
                SetState(LauncherState.Loading);
                var directory = _api.Store.GetContentDirectory(scenario.Id, scenario.Version);
                await _api.Content.LoadCatalogAsync(directory);
                if (!_api.Content.HasMainScene)
                    throw new InvalidOperationException($"'{scenario.Title}' has no '{ScenarioAddresses.MainScene}' scene. Re-export it from Unity.");
                await _api.Content.LoadSceneAsync();
                SetState(LauncherState.Playing);
            }
            catch (Exception e)
            {
                _api.Content.Unload();
                Current = null;
                SetState(LauncherState.Browsing);
                Fail(Describe(e));
            }
            finally
            {
                _busy = false;
            }
        }

        public async Task StopAsync()
        {
            if (State != LauncherState.Playing)
                return;
            await _api.Content.UnloadSceneAsync();
            _api.Content.Unload();
            Current = null;
            SetState(LauncherState.Browsing);
        }

        public void SignOut()
        {
            if (_busy || State != LauncherState.Browsing)
                return;
            PlayerPrefs.DeleteKey(LoginKey);
            Current = null;
            Error = null;
            SetState(LauncherState.SignedOut);
        }

        /// <summary>Syncs the config and returns the current one (the cached copy when unchanged).</summary>
        private async Task<DeviceConfig> SyncAsync()
        {
            var result = await _api.SyncConfigAsync(true);
            var config = result.NotModified ? _api.Store.CachedConfig : result.Config;
            ApplyConfig(config);
            return config;
        }

        private void ApplyConfig(DeviceConfig config)
        {
            if (config == null)
                return;
            ClientName = config.Client?.Name ?? ClientName;
            HeadsetLabel = config.Headset?.Label ?? HeadsetLabel;
            LicenceStatus = config.Licence?.Status;
            _scenarios = config.Scenarios
                .OrderBy(s => s.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
                .Select(s => new LauncherScenario
                {
                    Id = s.Id,
                    Title = s.Title,
                    Category = s.Category,
                    Description = s.Description,
                    DurationMinutes = s.DurationMinutes,
                    Version = s.Version,
                    InstalledVersion = _api.Store.GetInstalledVersion(s.Id),
                })
                .ToList();
        }

        private bool IsSignedIn(DeviceConfig config)
        {
            var hash = config?.Client?.LoginCodeHash;
            return !string.IsNullOrEmpty(hash) && PlayerPrefs.GetString(LoginKey, "") == hash;
        }

        private static bool Matches(DeviceConfig config, string normalisedCode)
        {
            var hash = config?.Client?.LoginCodeHash;
            return !string.IsNullOrEmpty(hash) && LoginHash(config.Client.Id, normalisedCode) == hash;
        }

        private static bool IsRemoved(ApiError e) =>
            e.Code == ApiErrorCodes.InvalidToken || e.Code == ApiErrorCodes.HeadsetDeactivated;

        /// <summary>Deactivated in the dashboard (or the local API restarted): signing in again links it again.</summary>
        private void HeadsetRemoved()
        {
            PlayerPrefs.DeleteKey(LoginKey);
            _api.ForgetHeadset();
            ClientName = HeadsetLabel = LicenceStatus = null;
            Current = null;
            _scenarios = new List<LauncherScenario>();
            SetState(LauncherState.SignedOut);
            Fail("This headset was signed out. Enter your login code again.");
        }

        private bool Begin(LauncherState state)
        {
            if (_busy)
                return false;
            _busy = true;
            Error = null;
            SetState(state);
            return true;
        }

        private void SetState(LauncherState state)
        {
            State = state;
            Raise();
        }

        private void Fail(string message)
        {
            Error = message;
            Debug.LogWarning($"{LogPrefix} {message}");
            Raise();
        }

        private void Raise() => Changed?.Invoke();

        /// <summary>Same as the API: uppercase, letters and digits only.</summary>
        private static string Normalise(string code) =>
            new string((code ?? "").ToUpperInvariant().Where(c => (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')).ToArray());

        private static string LoginHash(string clientId, string normalisedCode)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(clientId + ":" + normalisedCode));
            return string.Concat(hash.Select(b => b.ToString("x2")));
        }

        private string SerialFor(string code)
        {
            var device = SystemInfo.deviceUniqueIdentifier;
            if (!serialPerCode)
                return device;
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(device + ":" + Normalise(code)));
            return "DEMO-" + string.Concat(hash.Take(6).Select(b => b.ToString("X2")));
        }

        private static string CurrentAndroidPlatform() =>
            SystemInfo.deviceModel.IndexOf("pico", StringComparison.OrdinalIgnoreCase) >= 0 ? SafeSitePlatforms.Pico : SafeSitePlatforms.Meta;

        /// <summary>The API wants x.y.z; Player Settings may hold something else.</summary>
        private static string AppVersion() =>
            SafeSiteTime.IsVersion(Application.version) ? Application.version : "0.0.0";

        private static string Mb(long bytes) => $"{bytes / (1024.0 * 1024.0):0.0} MB";

        private static string Describe(Exception e)
        {
            if (!(e is ApiError api))
            {
                Debug.LogException(e);
                return e.Message;
            }

            switch (api.Code)
            {
                case ApiErrorCodes.InvalidLoginCode: return "That login code isn't right. Check it with your manager or SafeSite contact.";
                case ApiErrorCodes.ClientSuspended: return "Your company's SafeSite account is suspended. Contact SafeSite.";
                case ApiErrorCodes.ClientArchived: return "Your company's SafeSite account is closed. Contact SafeSite.";
                case ApiErrorCodes.HeadsetLimitReached: return "Your company is already using all of its headsets. Contact SafeSite to add more.";
                case ApiErrorCodes.LicenceLocked: return "Your licence has expired, so new scenarios can't be downloaded. Contact SafeSite.";
                case ApiErrorCodes.NoContent:
                case ApiErrorCodes.NotFound: return "That scenario isn't available any more. Refresh the list.";
                case ApiErrorCodes.NetworkError: return "Can't reach SafeSite. Check the internet connection and try again.";
                default:
                    return api.IsTransient ? "SafeSite is having trouble right now. Try again in a minute." : $"{api.ErrorMessage} ({api.Code})";
            }
        }
    }
}
