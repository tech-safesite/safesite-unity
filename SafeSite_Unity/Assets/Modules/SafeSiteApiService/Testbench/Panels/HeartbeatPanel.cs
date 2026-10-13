using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Modules.SafeSiteApiService.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.SafeSiteApiService.Testbench
{
    public sealed class HeartbeatPanel : TestbenchPanel
    {
        private const string Unchanged = "(unchanged)";
        private const string ClearProject = "(none: send null)";

        private TMP_InputField _appVersion;
        private TMP_Dropdown _project;
        private Toggle _includeInstalled;
        private Toggle _includeLocation;
        private TMP_InputField _lat;
        private TMP_InputField _lng;
        private TMP_InputField _accuracy;
        private TMP_InputField _recordedAt;
        private Toggle _auto;
        private TMP_InputField _interval;
        private TextMeshProUGUI _result;
        private Button _resync;
        private List<ProjectRef> _projects = new List<ProjectRef>();
        private float _nextAuto;

        public override string Title => "Heartbeat";

        protected override void Build(RectTransform content)
        {
            _appVersion = Ui.Input(content, "App version", State.AppVersion);
            _project = Ui.Dropdown(content, "Project", new[] { Unchanged, ClearProject });
            _includeInstalled = Ui.Toggle(content, "Include installed scenarios (from the manifest)", true);

            _includeLocation = Ui.Toggle(content, "Include location", true);
            _lat = Ui.Input(content, "Latitude", "53.28695", TMP_InputField.ContentType.DecimalNumber);
            _lng = Ui.Input(content, "Longitude", "-6.24362", TMP_InputField.ContentType.DecimalNumber);
            _accuracy = Ui.Input(content, "Accuracy (m)", "25", TMP_InputField.ContentType.DecimalNumber);
            var recordedRow = Ui.Row(content);
            Ui.Label(recordedRow, "Recorded at", width: Ui.LabelWidth, color: Ui.Muted);
            _recordedAt = Ui.InputField(recordedRow, SafeSiteTime.Now());
            Ui.Button(recordedRow, "Now", () => _recordedAt.text = SafeSiteTime.Now());
            Ui.Button(recordedRow, "+2 h (future)", () => _recordedAt.text = SafeSiteTime.Format(DateTimeOffset.UtcNow.AddHours(2)));
            Ui.Button(recordedRow, "Use device location", () => StartCoroutine(UseDeviceLocation()));

            var row = Ui.Row(content);
            Ui.Button(row, "Send heartbeat", () => Run("Heartbeat", () => SendHeartbeat()), color: Ui.Accent);
            _resync = Ui.Button(row, "Re-sync config", () => Run("Re-sync config", async () =>
            {
                var result = await Service.SyncConfigAsync(false);
                return "configVersion " + result.Config.ConfigVersion;
            }));

            var autoRow = Ui.Row(content);
            _auto = Ui.Toggle(autoRow, "Auto heartbeat every N s", false, on => _nextAuto = Time.unscaledTime);
            _interval = Ui.InputField(autoRow, "300", TMP_InputField.ContentType.IntegerNumber, 100);

            _result = Ui.Label(content, "");
        }

        private async System.Threading.Tasks.Task<string> SendHeartbeat()
        {
            var request = new HeartbeatRequest { AppVersion = _appVersion.text.Trim() };

            var option = _project.options[_project.value].text;
            if (option == ClearProject)
            {
                request.ProjectMode = HeartbeatProjectMode.Clear;
            }
            else if (option != Unchanged)
            {
                request.ProjectMode = HeartbeatProjectMode.Set;
                request.ProjectId = _projects[_project.value - 2].Id;
            }

            if (_includeInstalled.isOn)
                request.InstalledScenarios = Service.Store.Installed.ToList();

            if (_includeLocation.isOn)
            {
                request.Location = new DeviceLocation
                {
                    Lat = ParseDouble(_lat.text),
                    Lng = ParseDouble(_lng.text),
                    AccuracyM = double.TryParse(_accuracy.text, NumberStyles.Float, CultureInfo.InvariantCulture, out var accuracy) ? accuracy : (double?)null,
                    RecordedAt = _recordedAt.text.Trim(),
                };
            }

            var response = await Service.SendHeartbeatAsync(request);
            var stale = Service.IsConfigStale(response);
            _resync.gameObject.SetActive(stale);
            _result.text = $"serverTime {response.ServerTime}  configVersion {response.ConfigVersion}  " +
                           (stale ? Ui.Colored("differs from cached config: re-sync", Ui.Warn) : Ui.Colored("matches cached config", Ui.Good));
            return "200";
        }

        private static double ParseDouble(string text) => double.Parse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

        private IEnumerator UseDeviceLocation()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.FineLocation))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.FineLocation);
                yield return new WaitForSeconds(1f);
            }
#endif
            LocationService location;
            try
            {
                location = Input.location;
            }
            catch (Exception e)
            {
                SetStatus("Location unavailable: " + e.Message, Ui.Bad);
                yield break;
            }

            if (!location.isEnabledByUser)
            {
                SetStatus("Location services are off or permission was denied", Ui.Bad);
                yield break;
            }

            SetStatus("Waiting for location...", Ui.Muted);
            location.Start(10f, 10f);
            var deadline = Time.unscaledTime + 20f;
            while (location.status == LocationServiceStatus.Initializing && Time.unscaledTime < deadline)
                yield return null;

            if (location.status != LocationServiceStatus.Running)
            {
                SetStatus("Location failed: " + location.status, Ui.Bad);
                location.Stop();
                yield break;
            }

            var data = location.lastData;
            _lat.text = data.latitude.ToString("0.00000", CultureInfo.InvariantCulture);
            _lng.text = data.longitude.ToString("0.00000", CultureInfo.InvariantCulture);
            _accuracy.text = data.horizontalAccuracy.ToString("0", CultureInfo.InvariantCulture);
            _recordedAt.text = SafeSiteTime.Format(DateTimeOffset.FromUnixTimeMilliseconds((long)(data.timestamp * 1000)));
            location.Stop();
            SetStatus("Location filled from the device", Ui.Good);
        }

        private void Update()
        {
            if (!_auto.isOn || Busy || Time.unscaledTime < _nextAuto)
                return;
            var seconds = int.TryParse(_interval.text, out var s) ? Mathf.Max(10, s) : 300;
            _nextAuto = Time.unscaledTime + seconds;
            _recordedAt.text = SafeSiteTime.Now();
            Run("Auto heartbeat", () => SendHeartbeat());
        }

        protected override void OnStateChanged()
        {
            var previous = _project.options.Count > _project.value ? _project.options[_project.value].text : Unchanged;
            _projects = State.Projects.ToList();
            var labels = new List<string> { Unchanged, ClearProject };
            labels.AddRange(_projects.Select(p => p.Name));
            Ui.SetOptions(_project, labels, Math.Max(0, labels.IndexOf(previous)));
            if (_resync != null && _result.text.Length == 0)
                _resync.gameObject.SetActive(false);
        }
    }
}
