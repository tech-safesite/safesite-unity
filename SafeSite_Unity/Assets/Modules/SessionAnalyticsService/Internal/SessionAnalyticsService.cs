using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Modules.DeviceLocationService.External;
using Modules.SafeSiteApiService.External;
using Modules.ScenarioLauncher.External;
using Modules.SessionAnalyticsService.External;
using UnityEngine;

namespace Modules.SessionAnalyticsService.Internal
{
    /// <summary>
    /// Watches the scenario launcher: a session starts when a downloaded scenario reaches
    /// <see cref="LauncherState.Playing"/> and ends when it leaves it (exit, restart, switch or quit).
    /// Ended sessions go into the API service's on-disk queue, which is uploaded straight away and
    /// again with every heartbeat until it is empty. Needs an ISafeSiteApiService and an
    /// IScenarioLauncherService in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionAnalyticsService : MonoBehaviour, ISessionAnalyticsService
    {
        private const string LogPrefix = "[SessionAnalyticsService]";
        private const string FallbackLanguage = "English";
        private const int MaxDurationSeconds = 24 * 3600;

        [Tooltip("Seconds between heartbeats while the app runs. Each one also uploads any queued sessions.")]
        [SerializeField, Min(30)] private float heartbeatIntervalSeconds = 300;

        [Tooltip("Seconds after start-up before the first heartbeat, so the launcher has picked its environment.")]
        [SerializeField, Min(0)] private float firstHeartbeatDelaySeconds = 5;

        [Tooltip("Seconds a heartbeat waits for a device location before sending without one (the API then estimates one from the IP).")]
        [SerializeField, Min(0)] private float locationWaitSeconds = 15;

        [Tooltip("Hours a device fix is still sent after location services stop answering.")]
        [SerializeField, Min(0)] private float maxFixAgeHours = 6;

        private ISafeSiteApiService _api;
        private IDeviceLocationService _location;
        private IScenarioLauncherService _launcher;
        private LauncherState _lastState;
        private string _language;
        private string _installedSignature;

        private Recording _recording;
        private bool _syncing;
        private bool _syncAgain;
        private bool _offlineLogged;

        public bool IsRecording => _recording != null;
        public int PendingUploads => _api != null && _api.Store != null ? _api.Store.SessionQueue.Count : 0;
        public DateTime? LastHeartbeat { get; private set; }
        public string LastError { get; private set; }

        public event Action Changed;

        /// <summary>The session being played, until it is queued.</summary>
        private sealed class Recording
        {
            public string ScenarioId;
            public string ScenarioVersion;
            public string Language;
            public string StartedAt;
            public float StartedRealtime;
            public bool Completed;
            public SessionScore Score;
        }

        private void Start()
        {
            _api = SafeSiteApiServiceLocator.FindInScene();
            _launcher = ScenarioLauncherServiceLocator.FindInScene();
            if (_api == null || _launcher == null)
            {
                Debug.LogError($"{LogPrefix} Needs an ISafeSiteApiService and an IScenarioLauncherService in the scene; analytics are off.");
                enabled = false;
                return;
            }

            // Optional: without it (or without a fix) the API places the headset by its IP.
            _location = DeviceLocationServiceLocator.FindInScene();
            if (_location == null)
                Debug.Log($"{LogPrefix} No IDeviceLocationService in the scene; the dashboard map will use IP locations.");

            _lastState = _launcher.State;
            _launcher.Changed += OnLauncherChanged;
            _api.EnvironmentChanged += OnEnvironmentChanged;
            StartCoroutine(HeartbeatLoop());
        }

        private void OnDestroy()
        {
            if (_launcher != null)
                _launcher.Changed -= OnLauncherChanged;
            if (_api != null)
                _api.EnvironmentChanged -= OnEnvironmentChanged;
        }

        private void OnApplicationQuit()
        {
            // Quitting mid-scenario: keep what was played (not completed). The queue is on disk, so
            // it is uploaded on the next start.
            EndRecording("app quit");
        }

        // ---- Public API

        public void SetLanguage(string language)
        {
            _language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
        }

        public void ReportScore(int correct, int total)
        {
            if (_recording == null)
            {
                Debug.Log($"{LogPrefix} Score {correct}/{total} ignored: no downloaded scenario is being recorded.");
                return;
            }
            if (total < 1 || correct < 0 || correct > total)
            {
                Debug.LogWarning($"{LogPrefix} Score {correct}/{total} ignored: needs 0 ≤ correct ≤ total and total ≥ 1.");
                return;
            }

            _recording.Score = new SessionScore { Correct = correct, Total = total };
            Debug.Log($"{LogPrefix} Score {correct}/{total} for {_recording.ScenarioId}.");
        }

        public void MarkCompleted()
        {
            if (_recording == null)
                return;
            _recording.Completed = true;
            Debug.Log($"{LogPrefix} {_recording.ScenarioId} completed.");
        }

        public Task SyncNowAsync() => SyncAsync();

        // ---- Launcher events

        private void OnLauncherChanged()
        {
            var state = _launcher.State;
            var previous = _lastState;
            _lastState = state;

            if (state == LauncherState.Playing && _recording == null)
                BeginRecording(_launcher.Current);
            else if (state != LauncherState.Playing && _recording != null)
                EndRecording("scenario ended");

            // Just signed in, or a download changed what's installed: tell SafeSite now.
            var signedIn = state == LauncherState.Browsing && (previous == LauncherState.SigningIn || previous == LauncherState.SignedOut);
            if (signedIn || InstalledChanged())
                _ = SyncAsync();
        }

        private void OnEnvironmentChanged(SafeSiteEnvironment environment)
        {
            // Each environment has its own headset and queue; a session can't span two.
            _recording = null;
            _installedSignature = null;
            LastHeartbeat = null;
            LastError = null;
            Raise();
        }

        private void BeginRecording(LauncherScenario scenario)
        {
            if (scenario == null || string.IsNullOrEmpty(scenario.Id))
                return;

            var language = _language;
            if (language == null || (scenario.Languages.Count > 0 && !scenario.Languages.Contains(language)))
                language = scenario.Languages.FirstOrDefault() ?? FallbackLanguage;

            _recording = new Recording
            {
                ScenarioId = scenario.Id,
                ScenarioVersion = scenario.InstalledVersion ?? scenario.Version,
                Language = language,
                StartedAt = SafeSiteTime.Now(),
                StartedRealtime = Time.realtimeSinceStartup,
            };
            Debug.Log($"{LogPrefix} Recording {scenario.Title} v{_recording.ScenarioVersion} ({language}).");
            Raise();
        }

        private void EndRecording(string why)
        {
            var recording = _recording;
            if (recording == null)
                return;
            _recording = null;

            if (_api == null || !_api.Store.IsEnrolled)
            {
                Debug.LogWarning($"{LogPrefix} Dropped the session for {recording.ScenarioId}: the headset isn't registered.");
                Raise();
                return;
            }

            var seconds = Mathf.Clamp(Mathf.RoundToInt(Time.realtimeSinceStartup - recording.StartedRealtime), 0, MaxDurationSeconds);
            _api.EnqueueSessions(new[]
            {
                new SessionRecord
                {
                    DeviceSessionId = Guid.NewGuid().ToString(),
                    ScenarioId = recording.ScenarioId,
                    ScenarioVersion = recording.ScenarioVersion,
                    ProjectId = null,
                    Language = recording.Language,
                    StartedAt = recording.StartedAt,
                    DurationSeconds = seconds,
                    Completed = recording.Completed,
                    Score = recording.Score,
                },
            });
            Debug.Log($"{LogPrefix} Queued session ({why}): {recording.ScenarioId}, {seconds}s, " +
                      $"{(recording.Completed ? "completed" : "not completed")}" +
                      $"{(recording.Score != null ? $", score {recording.Score.Correct}/{recording.Score.Total}" : "")}. {PendingUploads} waiting.");
            Raise();

            if (why != "app quit")
                _ = SyncAsync();
        }

        // ---- Heartbeat and upload

        private IEnumerator HeartbeatLoop()
        {
            yield return new WaitForSecondsRealtime(firstHeartbeatDelaySeconds);
            while (true)
            {
                _ = SyncAsync();
                yield return new WaitForSecondsRealtime(heartbeatIntervalSeconds);
            }
        }

        /// <summary>Heartbeat, then the session queue. One at a time; a request made meanwhile runs once more after.</summary>
        private async Task SyncAsync()
        {
            if (_syncing)
            {
                _syncAgain = true;
                return;
            }

            _syncing = true;
            try
            {
                do
                {
                    _syncAgain = false;
                    await SyncOnceAsync();
                } while (_syncAgain);
            }
            finally
            {
                _syncing = false;
            }
        }

        private async Task SyncOnceAsync()
        {
            if (_api == null || !_api.Store.IsEnrolled)
                return;

            try
            {
                var store = _api.Store;
                _installedSignature = InstalledSignature();
                var location = await DeviceLocationAsync();
                var heartbeat = await _api.SendHeartbeatAsync(new HeartbeatRequest
                {
                    AppVersion = SafeSiteTime.IsVersion(Application.version) ? Application.version : "0.0.0",
                    InstalledScenarios = ReportableInstalled(store),
                    Location = location,
                });
                LastHeartbeat = DateTime.Now;

                if (store.SessionQueue.Count > 0)
                {
                    var result = await _api.UploadSessionQueueAsync();
                    Debug.Log($"{LogPrefix} Uploaded {result.Sent.Count} sessions: {result.Accepted} new, {result.Duplicates} already there, {result.Rejected.Count} rejected.");
                    foreach (var rejected in result.Rejected)
                        Debug.LogWarning($"{LogPrefix} SafeSite rejected session {rejected.DeviceSessionId}: {rejected.Reason}.");
                }

                LastError = null;
                _offlineLogged = false;

                // The dashboard changed something (scenarios, licence, login code): let the list catch up.
                if (_api.IsConfigStale(heartbeat) && _launcher.State == LauncherState.Browsing && !_launcher.IsBusy)
                    _ = _launcher.RefreshAsync();
            }
            catch (ApiError e)
            {
                LastError = $"{e.ErrorMessage} ({e.Code})";
                var offline = e.Code == ApiErrorCodes.NetworkError || e.IsTransient;
                if (!offline)
                    Debug.LogWarning($"{LogPrefix} Heartbeat/upload failed: {LastError}. {PendingUploads} sessions kept for later.");
                else if (!_offlineLogged)
                    Debug.Log($"{LogPrefix} Offline ({e.Code}); {PendingUploads} sessions kept for later.");
                _offlineLogged |= offline;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogException(e);
            }
            finally
            {
                Raise();
            }
        }

        /// <summary>
        /// The headset's own position, waiting at most <see cref="locationWaitSeconds"/> so a slow
        /// or missing fix never holds up the session upload. Null sends the heartbeat without one,
        /// and the API falls back to the request IP.
        /// </summary>
        private async Task<DeviceLocation> DeviceLocationAsync()
        {
            if (_location == null)
                return null;

            try
            {
                var refresh = _location.RefreshAsync();
                await Task.WhenAny(refresh, Task.Delay(TimeSpan.FromSeconds(locationWaitSeconds)));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{LogPrefix} Device location failed: {e.Message}");
            }

            var fix = _location.LastFix;
            if (fix == null || DateTimeOffset.UtcNow - fix.Value.RecordedAt > TimeSpan.FromHours(maxFixAgeHours))
                return null;

            return new DeviceLocation
            {
                Lat = fix.Value.Latitude,
                Lng = fix.Value.Longitude,
                AccuracyM = fix.Value.AccuracyMeters,
                RecordedAt = SafeSiteTime.Format(fix.Value.RecordedAt),
            };
        }

        /// <summary>
        /// Downloads of scenarios in the client's current config. The API refuses the whole heartbeat
        /// if one id no longer exists (deleted, or a local API that restarted), and leftovers from
        /// another client or environment mean nothing to this one.
        /// </summary>
        private static System.Collections.Generic.List<InstalledScenario> ReportableInstalled(IDeviceStore store)
        {
            var offered = new System.Collections.Generic.HashSet<string>(
                store.CachedConfig?.Scenarios?.Select(s => s.Id) ?? Enumerable.Empty<string>());
            return store.Installed
                .Where(i => offered.Contains(i.ScenarioId))
                .GroupBy(i => i.ScenarioId).Select(g => g.Last())
                .Take(200)
                .Select(i => new InstalledScenario(i.ScenarioId, i.Version))
                .ToList();
        }

        private bool InstalledChanged() =>
            _installedSignature != null && _api != null && _installedSignature != InstalledSignature();

        private string InstalledSignature() =>
            string.Join(",", _api.Store.Installed.Select(i => i.ScenarioId + "@" + i.Version).OrderBy(s => s));

        private void Raise() => Changed?.Invoke();
    }
}
