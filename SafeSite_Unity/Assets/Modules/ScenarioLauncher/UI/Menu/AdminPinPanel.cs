using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>
    /// The admin PIN on a number keypad. Too many wrong tries lock it for a while. The PIN is set on
    /// this component (Inspector), so change it before shipping a build to a new client.
    /// </summary>
    public sealed class AdminPinPanel : MonoBehaviour
    {
        [SerializeField] private LauncherMenu menu;
        [SerializeField] private TextMeshProUGUI pinText;
        [SerializeField] private TextMeshProUGUI errorText;
        [SerializeField] private Button enterButton;

        [Tooltip("Digits only. Its length is how many digits the keypad takes.")]
        [SerializeField] private string pin = "7233";
        [SerializeField] private int triesBeforeLock = 5;
        [SerializeField] private float lockSeconds = 30;

        private string _typed = "";
        private int _wrong;
        private float _lockedUntil;
        private string _error = "";

        private int Length => Mathf.Max(1, string.IsNullOrEmpty(pin) ? 4 : pin.Length);
        private bool Locked => Time.unscaledTime < _lockedUntil;

        private void OnEnable()
        {
            _typed = "";
            _error = "";
            Render();
        }

        private void Update()
        {
            if (Locked || _lockedUntil > 0)
            {
                if (!Locked)
                {
                    _lockedUntil = 0;
                    _error = "";
                }
                Render();
            }
        }

        /// <summary>Keypad key: one digit (ignored once the PIN is full).</summary>
        public void Type(string digit)
        {
            if (Locked || string.IsNullOrEmpty(digit) || !char.IsDigit(digit[0]) || _typed.Length >= Length)
                return;
            _typed += digit[0];
            _error = "";
            Render();
            if (_typed.Length == Length)
                Submit();
        }

        public void Backspace()
        {
            if (_typed.Length == 0)
                return;
            _typed = _typed.Substring(0, _typed.Length - 1);
            Render();
        }

        public void Clear()
        {
            _typed = "";
            Render();
        }

        public void Submit()
        {
            if (Locked || _typed.Length < Length)
                return;

            if (Matches(_typed, pin))
            {
                _wrong = 0;
                _typed = "";
                menu.ShowAdmin();
                return;
            }

            _typed = "";
            _wrong++;
            if (_wrong >= triesBeforeLock)
            {
                _wrong = 0;
                _lockedUntil = Time.unscaledTime + lockSeconds;
            }
            else
            {
                _error = "Wrong PIN. Try again.";
            }
            Render();
        }

        public void Cancel() => menu.CloseAdmin();

        private void Render()
        {
            if (pinText)
            {
                var dots = new string[Length];
                for (var i = 0; i < Length; i++)
                    dots[i] = i < _typed.Length ? "*" : "_";
                pinText.text = string.Join(" ", dots);
            }
            if (errorText)
                errorText.text = Locked ? $"Too many wrong tries. Wait {Mathf.CeilToInt(_lockedUntil - Time.unscaledTime)} s." : _error;
            if (enterButton)
                enterButton.interactable = !Locked && _typed.Length == Length;
        }

        /// <summary>Compares every digit so a wrong PIN takes as long as a right one.</summary>
        private static bool Matches(string typed, string expected)
        {
            if (string.IsNullOrEmpty(expected) || typed.Length != expected.Length)
                return false;
            var diff = 0;
            for (var i = 0; i < typed.Length; i++)
                diff |= typed[i] ^ expected[i];
            return diff == 0;
        }
    }
}
