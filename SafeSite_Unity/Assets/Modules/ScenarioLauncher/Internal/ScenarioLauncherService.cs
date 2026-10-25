using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using Modules.ScenarioLauncher.External;
using UnityEngine;

namespace Modules.ScenarioLauncher.Internal
{
    /// <summary>
    /// Client sign-in, scenario list, download and play, on top of the SafeSite API service. A client
    /// "signs in" by enrolling this device with an access code (enrollment code) from the dashboard.
    /// Needs an ISafeSiteApiService in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenarioLauncherService : MonoBehaviour, IScenarioLauncherService
    {
        private const string LogPrefix = "[ScenarioLauncherService]";

        [Tooltip("SafeSite environment to use (Local, Demo or Production). Empty keeps the API service's choice.")]
        [SerializeField] private string environmentName = "Local";

        [Tooltip("Derive this device's serial from the access code, so one PC can sign in to several clients " +
                 "(each client sees it as its own headset). Off: the real device id is used, as on a headset.")]
        [SerializeField] private bool serialPerAccessCode = true;

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

            // Already signed in on this device: go straight to the list.
            if (_api.Store.IsEnrolled)
            {
                ClientName = _api.Store.ClientName;
                HeadsetLabel = _api.Store.HeadsetLabel;
                ApplyConfig(_api.Store.CachedConfig);
                SetState(LauncherState.Browsing);
                await RefreshAsync();
            }
            else
            {
                SetState(LauncherState.SignedOut);
            }
        }

        public async Task SignInAsync(string accessCode)
        {
            var code = (accessCode ?? "").Trim();
            if (code.Length == 0)
            {
                Fail("Enter your access code.");
                return;
            }

            if (!Begin(LauncherState.SigningIn))
                return;
            try
            {
                var response = await _api.EnrollAsync(new EnrollRequest
                {
                    Code = code,
                    SerialNumber = SerialFor(code),
                    Platform = Application.platform == RuntimePlatform.Android ? CurrentAndroidPlatform() : SafeSitePlatforms.Meta,
                    Model = SystemInfo.deviceModel,
                    AppVersion = AppVersion(),
                });
                ClientName = response.Client?.Name;
                HeadsetLabel = response.Label;
                Debug.Log($"{LogPrefix} Signed in to {ClientName} as {HeadsetLabel}.");
                await SyncAsync();
                SetState(LauncherState.Browsing);
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

        public async Task RefreshAsync()
        {
            if (_busy || State != LauncherState.Browsing)
                return;
            _busy = true;
            Error = null;
            try
            {
                await SyncAsync();
                Raise();
            }
            catch (ApiError e) when (e.Code == ApiErrorCodes.InvalidToken || e.Code == ApiErrorCodes.HeadsetDeactivated)
            {
                // Removed in the dashboard (or the local API restarted): sign in again.
                _api.ForgetHeadset();
                ClientName = HeadsetLabel = LicenceStatus = null;
                _scenarios = new List<LauncherScenario>();
                SetState(LauncherState.SignedOut);
                Fail("This device was signed out. Enter your access code again.");
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
            if (_busy)
                return;
            _api.Content.Unload();
            foreach (var scenario in _scenarios)
                _api.DeleteLocalScenario(scenario.Id);
            _api.ForgetHeadset();
            ClientName = HeadsetLabel = LicenceStatus = null;
            Current = null;
            Error = null;
            _scenarios = new List<LauncherScenario>();
            SetState(LauncherState.SignedOut);
        }

        private async Task SyncAsync()
        {
            var result = await _api.SyncConfigAsync(true);
            ApplyConfig(result.NotModified ? _api.Store.CachedConfig : result.Config);
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

        private string SerialFor(string code)
        {
            var device = SystemInfo.deviceUniqueIdentifier;
            if (!serialPerAccessCode)
                return device;
            var normalized = code.ToUpperInvariant().Replace("-", "").Replace(" ", "");
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(device + ":" + normalized));
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
                case ApiErrorCodes.InvalidCode: return "That access code isn't valid. Check it with your SafeSite contact.";
                case ApiErrorCodes.ClientSuspended: return "Your company's SafeSite account is suspended. Contact SafeSite.";
                case ApiErrorCodes.ClientArchived: return "Your company's SafeSite account is closed. Contact SafeSite.";
                case ApiErrorCodes.HeadsetLimitReached: return "Your company has reached its headset limit. Contact SafeSite to add more.";
                case ApiErrorCodes.SerialInUse: return "This headset is registered to another company. Contact SafeSite.";
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
