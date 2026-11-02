using System;
using System.Collections.Generic;
using Modules.ScenarioLauncher.External;
using UnityEngine;
using UnityEngine.Events;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>
    /// Drives the Bootstrap main menu from <see cref="IScenarioLauncherService"/>: after the splash it
    /// shows the login keypad or the client's scenarios, then options, download progress and headset
    /// details. Playing and leaving a scenario is the app's job (fades, player reset), so it is handed
    /// out through <see cref="playRequested"/> and <see cref="playBuiltInRequested"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LauncherMenu : MonoBehaviour
    {
        private const string LogPrefix = "[LauncherMenu]";

        [Serializable]
        public sealed class StringEvent : UnityEvent<string> { }

        [Header("Screens")]
        [Tooltip("Shown until the splash calls ShowStart().")]
        [SerializeField] private GameObject splashScreen;
        [SerializeField] private LoginPanel loginScreen;
        [SerializeField] private ScenarioGridPanel scenariosScreen;
        [SerializeField] private ScenarioOptionsPanel optionsScreen;
        [SerializeField] private ProgressPanel progressScreen;
        [SerializeField] private HeadsetPanel headsetScreen;

        [Header("Playing")]
        [Tooltip("Plays a downloaded scenario by id (TrainingScenarioLoader.PlayDownloaded). Without a listener the launcher plays it directly.")]
        public StringEvent playRequested = new StringEvent();

        [Tooltip("Plays a scenario built into the app by its Addressables address (TrainingScenarioLoader.LoadScenario). Development builds only.")]
        public StringEvent playBuiltInRequested = new StringEvent();

        private IScenarioLauncherService _launcher;
        private bool _started;
        private GameObject _current;

        public IScenarioLauncherService Launcher => _launcher;

        /// <summary>True on the scenarios screen opened from "Built-in scenarios" without signing in.</summary>
        public bool BuiltInOnly { get; private set; }

        /// <summary>Development builds (and the Editor) can test scenarios built into the app without signing in.</summary>
        public static bool AllowBuiltIn => Debug.isDebugBuild;

        private IEnumerable<GameObject> Screens
        {
            get
            {
                yield return splashScreen;
                if (loginScreen) yield return loginScreen.gameObject;
                if (scenariosScreen) yield return scenariosScreen.gameObject;
                if (optionsScreen) yield return optionsScreen.gameObject;
                if (progressScreen) yield return progressScreen.gameObject;
                if (headsetScreen) yield return headsetScreen.gameObject;
            }
        }

        private void Awake()
        {
            _launcher = ScenarioLauncherServiceLocator.FindInScene();
            if (_launcher == null)
                Debug.LogError($"{LogPrefix} No IScenarioLauncherService in the scene. Add the ScenarioLauncherService component.");
        }

        private void OnEnable()
        {
            if (_launcher != null)
                _launcher.Changed += Render;
        }

        private void OnDisable()
        {
            if (_launcher != null)
                _launcher.Changed -= Render;
        }

        /// <summary>Called when the splash finishes; from here on the launcher's state picks the screen.</summary>
        public void ShowStart()
        {
            _started = true;
            BuiltInOnly = false;
            _current = null;
            Render();
        }

        public void ShowScenarios()
        {
            if (_launcher != null && _launcher.State == LauncherState.SignedOut && !BuiltInOnly)
            {
                Show(loginScreen.gameObject);
                return;
            }
            scenariosScreen.Render();
            Show(scenariosScreen.gameObject);
        }

        /// <summary>The scenarios built into the app, without signing in (development builds).</summary>
        public void ShowBuiltInScenarios()
        {
            if (!AllowBuiltIn)
                return;
            BuiltInOnly = true;
            ShowScenarios();
        }

        public void ShowHeadset()
        {
            headsetScreen.Render();
            Show(headsetScreen.gameObject);
        }

        public void OpenOptions(ScenarioCard card)
        {
            optionsScreen.Open(card);
            Show(optionsScreen.gameObject);
        }

        public void Play(ScenarioCard card)
        {
            if (card.IsBuiltIn)
            {
                Debug.Log($"{LogPrefix} Playing built-in '{card.Title}' at '{card.BuiltInAddress}'.");
                playBuiltInRequested.Invoke(card.BuiltInAddress);
                return;
            }

            if (_launcher == null || _launcher.State != LauncherState.Browsing)
                return;
            if (playRequested.GetPersistentEventCount() > 0)
                playRequested.Invoke(card.Scenario.Id);
            else
                _ = _launcher.PlayAsync(card.Scenario.Id);
        }

        public void Refresh()
        {
            if (_launcher != null)
                _ = _launcher.RefreshAsync();
        }

        public void SignOut()
        {
            _launcher?.SignOut();
            BuiltInOnly = false;
        }

        /// <summary>Back to the login keypad, e.g. from the built-in scenarios.</summary>
        public void ShowLogin()
        {
            BuiltInOnly = false;
            Show(loginScreen.gameObject);
        }

        private void Render()
        {
            if (!_started || _launcher == null)
                return;

            loginScreen.Render();
            switch (_launcher.State)
            {
                case LauncherState.SignedOut:
                case LauncherState.SigningIn:
                    if (!BuiltInOnly || _current == null || _current == loginScreen.gameObject)
                        Show(loginScreen.gameObject);
                    else
                        scenariosScreen.Render();
                    break;
                case LauncherState.Browsing:
                    BuiltInOnly = false;
                    // Stay on options or headset details; anything else goes to the list.
                    if (_current == optionsScreen.gameObject)
                        optionsScreen.Render();
                    else if (_current == headsetScreen.gameObject)
                        headsetScreen.Render();
                    else
                        ShowScenarios();
                    break;
                case LauncherState.Downloading:
                case LauncherState.Loading:
                    progressScreen.Render(_launcher);
                    Show(progressScreen.gameObject);
                    break;
                case LauncherState.Playing:
                    // The app hides the menu while a scenario plays; come back to the list afterwards.
                    _current = scenariosScreen.gameObject;
                    break;
            }
        }

        private void Show(GameObject screen)
        {
            if (_current == screen && screen.activeSelf)
                return;
            foreach (var other in Screens)
            {
                if (other != null && other != screen)
                    other.SetActive(false);
            }
            screen.SetActive(true);
            _current = screen;
        }
    }
}
