using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.SafeSiteApiService.Testbench
{
    /// <summary>Environment picker, headset identity, Ping, Forget headset and the world-space toggle.</summary>
    public sealed class HeaderBar : TestbenchPanel
    {
        private TMP_Dropdown _environment;
        private TextMeshProUGUI _info;
        private Button _worldSpace;

        public override string Title => "Header";

        /// <summary>Set by <see cref="TestbenchRoot"/>.</summary>
        public Action ToggleWorldSpace;

        protected override void Build(RectTransform content)
        {
            var top = Ui.Row(content, 8);
            Ui.Label(top, "SafeSite API Testbench", Ui.FontSize + 6, Ui.Text, 300, FontStyles.Bold);
            _environment = Ui.DropdownField(top, Service.Environments.Select(e => e.name), 220);
            _environment.onValueChanged.AddListener(OnEnvironmentPicked);
            Ui.Button(top, "Ping", () => Run("Ping", async () =>
            {
                var ping = await Service.Client.PingAsync();
                return $"{ping.Status} in {ping.DurationMs} ms";
            }));
            Ui.Button(top, "Forget headset", () => Run("Forget headset", () =>
            {
                Service.ForgetHeadset();
                return "token and caches deleted for " + Service.Environment.name;
            }), color: new Color(0.45f, 0.18f, 0.18f));
            _worldSpace = Ui.Button(top, "World space", () => ToggleWorldSpace?.Invoke());

            _info = Ui.Label(content, "", Ui.FontSize - 1, Ui.Muted);
        }

        public void SetWorldSpaceLabel(bool worldSpace) => Ui.SetButtonText(_worldSpace, worldSpace ? "Screen space" : "World space");

        private void OnEnvironmentPicked(int index)
        {
            var name = _environment.options[index].text;
            Run("Switch environment", () =>
            {
                Service.SetEnvironment(name);
                return name;
            });
        }

        protected override void OnStateChanged()
        {
            var environment = Service.Environment;
            var index = Service.Environments.ToList().FindIndex(e => e.name == environment.name);
            if (index >= 0)
                _environment.SetValueWithoutNotify(index);

            var store = Service.Store;
            var banner = environment.IsProduction ? Ui.Colored("  PRODUCTION: real clients and headsets", Ui.Bad) : "";
            _info.text =
                $"API {Ui.Colored(environment.apiBaseUrl, Ui.Text)}{banner}\n" +
                $"Headset {Ui.Colored(store.HeadsetLabel ?? "(not enrolled)", Ui.Text)}  id {store.HeadsetId ?? "-"}  " +
                $"client {Ui.Colored(store.ClientName ?? "-", Ui.Text)}  platform {store.Platform ?? "-"}  token {Ui.Mask(store.DeviceToken)}";
        }
    }
}
