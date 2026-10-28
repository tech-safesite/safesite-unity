using Modules.SafeSiteApiService.External;
using TMPro;
using UnityEngine;

namespace Modules.SafeSiteApiService.Testbench
{
    public sealed class EnrollPanel : TestbenchPanel
    {
        private static readonly string[] Platforms = { SafeSitePlatforms.Meta, SafeSitePlatforms.Pico };

        private TMP_InputField _code;
        private TMP_InputField _serial;
        private TMP_Dropdown _platform;
        private TMP_InputField _model;
        private TMP_InputField _appVersion;
        private TextMeshProUGUI _result;

        public override string Title => "Enroll";

        protected override void Build(RectTransform content)
        {
            _code = Ui.Input(content, "Enrollment code");
            _code.onEndEdit.AddListener(code => State.SetEnrollmentCode(code, notify: false));
            State.EnrollmentCodeChanged += code => _code.text = code;

            var serialRow = Ui.Row(content);
            Ui.Label(serialRow, "Serial number", width: Ui.LabelWidth, color: Ui.Muted);
            _serial = Ui.InputField(serialRow, SystemInfo.deviceUniqueIdentifier);
            Ui.Button(serialRow, "Randomize", () => _serial.text = TestbenchState.RandomSerial());

            _platform = Ui.Dropdown(content, "Platform", Platforms);
            _platform.SetValueWithoutNotify(TestbenchState.DefaultPlatform() == SafeSitePlatforms.Pico ? 1 : 0);
            _model = Ui.Input(content, "Model", SystemInfo.deviceModel);
            _appVersion = Ui.Input(content, "App version", State.AppVersion);

            Ui.Button(Ui.Row(content), "Enroll", Enroll, color: Ui.Accent);
            _result = Ui.Label(content, "");
        }

        private void Enroll() => Run("Enroll", async () =>
        {
            if (!SafeSiteTime.IsVersion(_appVersion.text.Trim()))
                return "app version must be x.y.z";

            var request = new EnrollRequest
            {
                Code = _code.text.Trim(),
                SerialNumber = _serial.text.Trim(),
                Platform = Platforms[_platform.value],
                Model = _model.text.Trim(),
                AppVersion = _appVersion.text.Trim(),
            };
            var response = await Service.EnrollAsync(request);
            return $"{response.Label} for {response.Client?.Name}; token stored";
        });

        protected override void OnStateChanged()
        {
            var store = Service.Store;
            _result.text = store.IsEnrolled
                ? Ui.Colored($"Enrolled as {store.HeadsetLabel} for {store.ClientName} ({store.Platform})", Ui.Good)
                : Ui.Colored($"Not enrolled in {Service.Environment.name}", Ui.Muted);
        }
    }
}
