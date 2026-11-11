using System;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeviceLocationService.External;
using UnityEngine;

namespace Modules.DeviceLocationService.Internal
{
    /// <summary>
    /// Reads the position once per refresh through Unity's <see cref="LocationService"/> and stops
    /// it straight away, so location services aren't left running. Put this component on a
    /// loaded scene object (the Bootstrap's services object).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeviceLocationService : MonoBehaviour, IDeviceLocationService
    {
        private const string LogPrefix = "[DeviceLocationService]";

        [Tooltip("Minimum minutes between attempts; RefreshAsync returns the last fix in between.")]
        [SerializeField, Min(1)] private float minMinutesBetweenAttempts = 15;

        [Tooltip("Seconds to wait for location services to produce a fix before giving up.")]
        [SerializeField, Min(1)] private float startTimeoutSeconds = 20;

        [Tooltip("Seconds to wait for the person to answer the Android permission prompt.")]
        [SerializeField, Min(1)] private float permissionTimeoutSeconds = 30;

        private Task<DeviceFix?> _inFlight;
        private float? _lastAttemptAt;
#if UNITY_ANDROID && !UNITY_EDITOR
        private bool _permissionAsked;
#endif

        public DeviceFix? LastFix { get; private set; }
        public string LastError { get; private set; }
        public event Action Changed;

        public Task<DeviceFix?> RefreshAsync(CancellationToken ct = default)
        {
            if (_inFlight != null && !_inFlight.IsCompleted)
                return _inFlight;
            if (_lastAttemptAt.HasValue && Time.realtimeSinceStartup - _lastAttemptAt.Value < minMinutesBetweenAttempts * 60f)
                return Task.FromResult(LastFix);

            _lastAttemptAt = Time.realtimeSinceStartup;
            _inFlight = AttemptAsync(ct);
            return _inFlight;
        }

        private async Task<DeviceFix?> AttemptAsync(CancellationToken ct)
        {
            string error = null;
            try
            {
                error = await ReadFixAsync(ct);
            }
            catch (OperationCanceledException)
            {
                error = "cancelled";
            }
            catch (Exception e)
            {
                error = e.Message;
            }
            finally
            {
                if (Input.location.status != LocationServiceStatus.Stopped)
                    Input.location.Stop();
            }

            if (error != null)
            {
                if (error != LastError)
                    Debug.Log($"{LogPrefix} No device location: {error}.");
                LastError = error;
            }
            else
            {
                LastError = null;
            }

            Changed?.Invoke();
            return LastFix;
        }

        /// <summary>Takes one fix into <see cref="LastFix"/>; returns why it couldn't, or null.</summary>
        private async Task<string> ReadFixAsync(CancellationToken ct)
        {
            if (!await EnsurePermissionAsync(ct))
                return "location permission not granted";

            var location = Input.location;
            if (!location.isEnabledByUser)
                return "location services are off or unavailable on this device";

            location.Start(100f, 50f);
            var deadline = Time.realtimeSinceStartup + startTimeoutSeconds;
            while (location.status == LocationServiceStatus.Initializing && Time.realtimeSinceStartup < deadline)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(250, ct);
            }

            if (location.status != LocationServiceStatus.Running)
                return $"location services didn't start ({location.status})";

            var data = location.lastData;
            if (data.timestamp <= 0 || (Math.Abs(data.latitude) < 1e-6 && Math.Abs(data.longitude) < 1e-6))
                return "location services returned no position";

            var now = DateTimeOffset.UtcNow;
            var recordedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)(data.timestamp * 1000));
            if (recordedAt > now)
                recordedAt = now;

            LastFix = new DeviceFix(
                data.latitude,
                data.longitude,
                data.horizontalAccuracy > 0 ? data.horizontalAccuracy : (double?)null,
                recordedAt);
            Debug.Log($"{LogPrefix} Fix {data.latitude:0.0000}, {data.longitude:0.0000} (±{data.horizontalAccuracy:0} m).");
            return null;
        }

        private async Task<bool> EnsurePermissionAsync(CancellationToken ct)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string fine = UnityEngine.Android.Permission.FineLocation;
            const string coarse = UnityEngine.Android.Permission.CoarseLocation;
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(fine) ||
                UnityEngine.Android.Permission.HasUserAuthorizedPermission(coarse))
                return true;

            // Ask once per run: Android stops showing the prompt after repeated denials anyway.
            if (_permissionAsked)
                return false;
            _permissionAsked = true;

            var answer = new TaskCompletionSource<bool>();
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => answer.TrySetResult(true);
            callbacks.PermissionDenied += _ => answer.TrySetResult(false);
            callbacks.PermissionDeniedAndDontAskAgain += _ => answer.TrySetResult(false);
            UnityEngine.Android.Permission.RequestUserPermissions(new[] { fine, coarse }, callbacks);

            var timeout = Task.Delay(TimeSpan.FromSeconds(permissionTimeoutSeconds), ct);
            await Task.WhenAny(answer.Task, timeout);
            ct.ThrowIfCancellationRequested();
            return UnityEngine.Android.Permission.HasUserAuthorizedPermission(fine) ||
                   UnityEngine.Android.Permission.HasUserAuthorizedPermission(coarse);
#else
            await Task.CompletedTask;
            return true;
#endif
        }
    }
}
