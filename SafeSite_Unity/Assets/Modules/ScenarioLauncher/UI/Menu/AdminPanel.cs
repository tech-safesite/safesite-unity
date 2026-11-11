using System.Linq;
using Modules.ScenarioLauncher.External;
using TMPro;
using UnityEngine;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>
    /// The admin screen behind the PIN: this headset's details and live stats, Register Headset, and
    /// debug switches (environment, built-in scenarios, refresh, delete downloads, forget headset, quit).
    /// </summary>
    public sealed class AdminPanel : MonoBehaviour
    {
        private const string Good = "#0C8547";
        private const string Bad = "#C53030";
        private const string Muted = "#52606D";

        [SerializeField] private LauncherMenu menu;
        [SerializeField] private TextMeshProUGUI detailsText;
        [SerializeField] private TextMeshProUGUI statusText;
        [SerializeField] private TextMeshProUGUI environmentLabel;
        [SerializeField] private TextMeshProUGUI builtInLabel;
        [SerializeField] private TextMeshProUGUI forgetLabel;

        [Tooltip("Seconds a destructive button waits for its second press.")]
        [SerializeField] private float confirmSeconds = 4;

        private IScenarioLauncherService _subscribed;
        private string _status = "";
        private string _confirming;
        private float _confirmUntil;
        private float _nextStats;
        private int _frames;
        private float _frameTime;
        private float _fps;

        private IScenarioLauncherService Launcher => menu ? menu.Launcher : null;

        private void OnEnable()
        {
            _status = "";
            _confirming = null;
            _subscribed = Launcher;
            if (_subscribed != null)
                _subscribed.Changed += Render;
            Render();
        }

        private void OnDisable()
        {
            if (_subscribed != null)
                _subscribed.Changed -= Render;
            _subscribed = null;
        }

        private void Update()
        {
            _frames++;
            _frameTime += Time.unscaledDeltaTime;
            if (Time.unscaledTime < _nextStats)
                return;
            _fps = _frameTime > 0 ? _frames / _frameTime : 0;
            _frames = 0;
            _frameTime = 0;
            _nextStats = Time.unscaledTime + 0.5f;
            if (_confirming != null && Time.unscaledTime > _confirmUntil)
                _confirming = null;
            Render();
        }

        public void Render()
        {
            var launcher = Launcher;
            if (detailsText)
            {
                var downloaded = launcher?.Scenarios.Count(s => s.InstalledVersion != null) ?? 0;
                var battery = SystemInfo.batteryLevel < 0 ? "-" : $"{SystemInfo.batteryLevel * 100:0}% ({SystemInfo.batteryStatus})";
                var registered = launcher != null && launcher.IsRegistered;
                detailsText.text =
                    $"<b>Environment</b>  {launcher?.EnvironmentName ?? "-"}\n" +
                    $"<b>Registered</b>  {(registered ? $"<color={Good}>Yes</color>" : $"<color={Bad}>No</color>")}\n" +
                    $"<b>Headset</b>  {launcher?.HeadsetLabel ?? "-"}\n" +
                    $"<b>Headset id</b>  <size=80%>{launcher?.HeadsetId ?? "-"}</size>\n" +
                    $"<b>Company</b>  {launcher?.ClientName ?? "-"}\n" +
                    $"<b>State</b>  {launcher?.State.ToString() ?? "-"}   <b>Licence</b>  {launcher?.LicenceStatus ?? "-"}\n" +
                    $"<b>Scenarios</b>  {launcher?.Scenarios.Count ?? 0} listed, {downloaded} downloaded\n" +
                    $"<b>Device</b>  {SystemInfo.deviceModel}\n" +
                    $"<b>App</b>  {Application.version}  ·  Unity {Application.unityVersion}{(Debug.isDebugBuild ? "  ·  development" : "")}\n" +
                    $"<b>FPS</b>  {_fps:0}   <b>Battery</b>  {battery}   <b>Memory</b>  {System.GC.GetTotalMemory(false) / (1024 * 1024)} MB";
            }

            if (environmentLabel)
                environmentLabel.text = $"Environment: {launcher?.EnvironmentName ?? "-"}";
            if (builtInLabel)
                builtInLabel.text = Debug.isDebugBuild
                    ? "Built-in scenarios: always (development)"
                    : $"Built-in scenarios: {(AdminSettings.BuiltInScenarios ? "On" : "Off")}";
            if (forgetLabel)
                forgetLabel.text = _confirming == nameof(ForgetHeadset) ? "Press again to forget" : "Forget headset";

            var line = _confirming == nameof(DeleteDownloads) ? "Press Delete downloads again to delete every downloaded scenario." : _status;
            if (string.IsNullOrEmpty(line))
                line = launcher?.Error ?? "";
            if (statusText)
                statusText.text = line;
        }

        public void RegisterHeadset() => menu.ShowRegisterHeadset();

        public void Close() => menu.CloseAdmin();

        /// <summary>Cycles Production → Demo → Local (whatever the environments asset lists).</summary>
        public void NextEnvironment()
        {
            var launcher = Launcher;
            if (launcher == null || launcher.EnvironmentNames.Count < 2)
                return;
            var names = launcher.EnvironmentNames;
            var index = names.ToList().IndexOf(launcher.EnvironmentName);
            var next = names[(index + 1) % names.Count];
            launcher.SetEnvironment(next);
            _status = $"Switched to {next}. It keeps its own registration and downloads.";
            Render();
        }

        public void ToggleBuiltInScenarios()
        {
            if (Debug.isDebugBuild)
                return;
            AdminSettings.BuiltInScenarios = !AdminSettings.BuiltInScenarios;
            _status = AdminSettings.BuiltInScenarios ? "Built-in scenarios are listed on this headset." : "Built-in scenarios are hidden.";
            Render();
        }

        public void Refresh()
        {
            var launcher = Launcher;
            if (launcher == null)
                return;
            if (launcher.State != LauncherState.Browsing)
            {
                _status = "Sign in first: the scenario list syncs for the signed-in company.";
                Render();
                return;
            }
            _status = "Refreshing…";
            Render();
            _ = RefreshAsync(launcher);
        }

        private async System.Threading.Tasks.Task RefreshAsync(IScenarioLauncherService launcher)
        {
            await launcher.RefreshAsync();
            _status = launcher.Error ?? $"Synced: {launcher.Scenarios.Count} scenarios.";
            Render();
        }

        public void DeleteDownloads()
        {
            if (!Confirm(nameof(DeleteDownloads)))
                return;
            var count = Launcher?.DeleteDownloads() ?? 0;
            _status = $"Deleted {count} downloaded scenario{(count == 1 ? "" : "s")}.";
            Render();
        }

        public void ForgetHeadset()
        {
            if (!Confirm(nameof(ForgetHeadset)))
                return;
            Launcher?.ForgetHeadset();
            _status = "This headset was forgotten here. Register it again, or sign in with a login code.";
            Render();
        }

        public void QuitApp()
        {
            if (!Confirm(nameof(QuitApp)))
            {
                _status = "Press Quit app again to close SafeSite.";
                Render();
                return;
            }
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        /// <summary>First press arms the action, a second press within <see cref="confirmSeconds"/> runs it.</summary>
        private bool Confirm(string action)
        {
            if (_confirming == action && Time.unscaledTime <= _confirmUntil)
            {
                _confirming = null;
                return true;
            }
            _confirming = action;
            _confirmUntil = Time.unscaledTime + confirmSeconds;
            _status = "";
            Render();
            return false;
        }
    }
}
