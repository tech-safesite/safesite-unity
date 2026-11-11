using System.Linq;
using Modules.ScenarioLauncher.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>
    /// The client's login code, typed on an in-world keypad (headsets have no keyboard). Codes are
    /// <see cref="CodeLength"/> characters from <see cref="Alphabet"/>, the API's set without look-alikes.
    /// </summary>
    public sealed class LoginPanel : MonoBehaviour
    {
        /// <summary>Same as the API's code alphabet: no I, L, O, 0 or 1.</summary>
        public const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";
        public const int CodeLength = 8;

        [SerializeField] private LauncherMenu menu;
        [SerializeField] private TextMeshProUGUI codeText;
        [SerializeField] private TextMeshProUGUI errorText;
        [SerializeField] private Button signInButton;
        [SerializeField] private TextMeshProUGUI signInLabel;
        [Tooltip("'Built-in scenarios' shortcut, shown in development builds only.")]
        [SerializeField] private GameObject builtInButton;

        private string _code = "";

        private IScenarioLauncherService Launcher => menu ? menu.Launcher : null;

        private void OnEnable()
        {
            if (builtInButton)
                builtInButton.SetActive(LauncherMenu.AllowBuiltIn);
            Render();
        }

        /// <summary>Keypad key: adds one character (ignored once the code is full).</summary>
        public void Type(string key)
        {
            if (string.IsNullOrEmpty(key) || _code.Length >= CodeLength || Busy)
                return;
            var c = char.ToUpperInvariant(key[0]);
            if (Alphabet.IndexOf(c) < 0)
                return;
            _code += c;
            Render();
        }

        public void Backspace()
        {
            if (_code.Length == 0 || Busy)
                return;
            _code = _code.Substring(0, _code.Length - 1);
            Render();
        }

        public void Clear()
        {
            if (Busy)
                return;
            _code = "";
            Render();
        }

        public void SignIn()
        {
            if (Launcher == null || Busy || _code.Length < CodeLength)
                return;
            _ = Launcher.SignInAsync(_code);
        }

        private bool Busy => Launcher != null && Launcher.State == LauncherState.SigningIn;

        public void Render()
        {
            var launcher = Launcher;
            if (launcher != null && launcher.State == LauncherState.Browsing)
                _code = ""; // don't leave it for the next person who signs out

            if (codeText)
                codeText.text = string.Join(" ", Enumerable.Range(0, CodeLength).Select(i => i < _code.Length ? _code[i].ToString() : "_"));
            if (errorText)
                errorText.text = launcher?.Error ?? "";
            if (signInButton)
                signInButton.interactable = !Busy && _code.Length == CodeLength;
            if (signInLabel)
                signInLabel.text = Busy ? "Signing in…" : "Sign in";
        }
    }
}
