using Modules.ScenarioLauncher.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>
    /// Registers this headset with a registration code made in the SafeSite dashboard (the device
    /// enroll API), typed on an in-world keypad like the login code.
    /// </summary>
    public sealed class RegisterHeadsetPanel : MonoBehaviour
    {
        /// <summary>Registration codes are letters and digits; the API ignores anything else.</summary>
        public const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        public const int MaxLength = 16;
        public const int MinLength = 4;

        private const string Good = "#0C8547";
        private const string Bad = "#C53030";

        [SerializeField] private LauncherMenu menu;
        [SerializeField] private TextMeshProUGUI currentText;
        [SerializeField] private TextMeshProUGUI codeText;
        [SerializeField] private TextMeshProUGUI resultText;
        [SerializeField] private Button registerButton;
        [SerializeField] private TextMeshProUGUI registerLabel;

        private string _code = "";
        private bool _registering;
        private HeadsetRegistration _result;
        private IScenarioLauncherService _subscribed;

        private IScenarioLauncherService Launcher => menu ? menu.Launcher : null;

        private void OnEnable()
        {
            _code = "";
            _result = null;
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

        public void Type(string key)
        {
            if (_registering || string.IsNullOrEmpty(key) || _code.Length >= MaxLength)
                return;
            var c = char.ToUpperInvariant(key[0]);
            if (Alphabet.IndexOf(c) < 0)
                return;
            _code += c;
            _result = null;
            Render();
        }

        public void Backspace()
        {
            if (_registering || _code.Length == 0)
                return;
            _code = _code.Substring(0, _code.Length - 1);
            Render();
        }

        public void Clear()
        {
            if (_registering)
                return;
            _code = "";
            _result = null;
            Render();
        }

        public async void Register()
        {
            var launcher = Launcher;
            if (launcher == null || _registering || _code.Length < MinLength)
                return;
            _registering = true;
            _result = null;
            Render();
            try
            {
                _result = await launcher.RegisterHeadsetAsync(_code);
                if (_result.Succeeded)
                    _code = ""; // don't leave it for the next person
            }
            finally
            {
                _registering = false;
                if (this)
                    Render();
            }
        }

        public void Back() => menu.ShowAdmin();

        public void Render()
        {
            var launcher = Launcher;
            if (currentText)
            {
                currentText.text = launcher == null || !launcher.IsRegistered
                    ? $"Not registered in {launcher?.EnvironmentName ?? "-"} yet"
                    : $"Now: {launcher.HeadsetLabel ?? "Headset"} for {launcher.ClientName ?? "-"} ({launcher.EnvironmentName})";
            }
            if (codeText)
                codeText.text = _code.Length == 0 ? "_ _ _ _ _ _ _ _" : _code;
            if (resultText)
                resultText.text = _result == null ? "" : $"<color={(_result.Succeeded ? Good : Bad)}>{_result.Message}</color>";
            if (registerButton)
                registerButton.interactable = !_registering && _code.Length >= MinLength && (launcher == null || !launcher.IsBusy);
            if (registerLabel)
                registerLabel.text = _registering ? "Registering…" : "Register";
        }
    }
}
