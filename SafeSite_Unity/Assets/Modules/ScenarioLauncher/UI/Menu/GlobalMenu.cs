using System.Collections.Generic;
using System.Linq;
using Modules.ScenarioLauncher.External;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>
    /// The menu on the left controller's menu button (Quest, Pico and other OpenXR controllers; Esc
    /// in the Editor). Opens in front of the user, wherever they are: while a scenario plays it can
    /// resume, restart, switch to another scenario or exit to the main menu; on the main menu it goes
    /// back to the start or switches straight to a scenario. Hidden until the button is pressed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GlobalMenu : MonoBehaviour
    {
        private const string LogPrefix = "[GlobalMenu]";

        [SerializeField] private LauncherMenu launcherMenu;

        [Header("Toggle")]
        [Tooltip("Opens and closes the menu. By default the left controller's menu button, and Esc for the Editor.")]
        [SerializeField] private InputActionProperty toggleAction = new InputActionProperty(DefaultToggleAction());

        [Header("Screens")]
        [Tooltip("The menu's world-space canvas; shown while open.")]
        [SerializeField] private GameObject canvasRoot;
        [SerializeField] private GameObject mainScreen;
        [SerializeField] private GameObject switchScreen;

        [Header("Main screen")]
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button switchButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private Button homeButton;

        [Header("Switch screen")]
        [SerializeField] private RectTransform list;
        [Tooltip("One scenario row: a Button with a TMP label. Cloned per scenario.")]
        [SerializeField] private Button itemTemplate;
        [SerializeField] private Button previousPage;
        [SerializeField] private Button nextPage;
        [SerializeField] private TextMeshProUGUI pageText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private int itemsPerPage = 5;

        [Header("Placement")]
        [Tooltip("Defaults to the main camera.")]
        [SerializeField] private Transform head;
        [SerializeField] private float distance = 1.5f;
        [Tooltip("Metres above (or below, negative) eye height for the menu's centre.")]
        [SerializeField] private float heightOffset = -0.1f;

        private readonly List<GameObject> _items = new List<GameObject>();
        private readonly List<XRInputSubsystem> _inputSubsystems = new List<XRInputSubsystem>();
        private IScenarioHost _host;
        private int _page;
        // Frame of the last recenter; the head pose catches up a frame later.
        private int _recenteredFrame = -1;

        public bool IsOpen => canvasRoot && canvasRoot.activeSelf;

        private IScenarioLauncherService Launcher => launcherMenu ? launcherMenu.Launcher : null;

        private static InputAction DefaultToggleAction()
        {
            var action = new InputAction("Toggle Menu", InputActionType.Button);
            action.AddBinding("<XRController>{LeftHand}/{MenuButton}");
            action.AddBinding("<Keyboard>/escape");
            return action;
        }

        private void Awake()
        {
            _host = ScenarioHostLocator.FindInScene();
            if (_host == null)
                Debug.LogWarning($"{LogPrefix} No IScenarioHost (TrainingScenarioLoader) in the scene: Restart, Exit and Switch won't work.");
            if (itemTemplate)
                itemTemplate.gameObject.SetActive(false);
            if (canvasRoot)
                canvasRoot.SetActive(false);
        }

        private void OnEnable()
        {
            var action = toggleAction.action;
            if (action != null)
            {
                action.performed += OnToggle;
                // A referenced action belongs to its asset; an inline one is ours to enable.
                if (toggleAction.reference == null || !action.enabled)
                    action.Enable();
            }
            if (_host != null)
                _host.Changed += OnHostChanged;

            SubsystemManager.GetSubsystems(_inputSubsystems);
            foreach (var subsystem in _inputSubsystems)
                subsystem.trackingOriginUpdated += OnRecentered;
        }

        private void OnDisable()
        {
            var action = toggleAction.action;
            if (action != null)
            {
                action.performed -= OnToggle;
                if (toggleAction.reference == null)
                    action.Disable();
            }
            if (_host != null)
                _host.Changed -= OnHostChanged;

            foreach (var subsystem in _inputSubsystems)
                subsystem.trackingOriginUpdated -= OnRecentered;
            _inputSubsystems.Clear();
        }

        /// <summary>The user recentred (or the headset did): an open menu would now be off to the side or behind.</summary>
        private void OnRecentered(XRInputSubsystem _) => _recenteredFrame = Time.frameCount;

        private void LateUpdate()
        {
            if (_recenteredFrame < 0 || Time.frameCount <= _recenteredFrame)
                return;
            _recenteredFrame = -1;
            if (IsOpen)
                PlaceInFrontOfHead();
        }

        private void OnToggle(InputAction.CallbackContext _) => Toggle();

        public void Toggle()
        {
            if (IsOpen)
                Close();
            else
                Open();
        }

        public void Open()
        {
            if (IsOpen || !canvasRoot)
                return;
            // Mid-fade, or a download or sign-in in progress: the menu would only get in the way.
            if (_host != null && _host.IsBusy)
                return;
            var launcher = Launcher;
            if (launcher != null && launcher.IsBusy && !(_host != null && _host.IsPlaying))
                return;

            PlaceInFrontOfHead();
            canvasRoot.SetActive(true);
            ShowMain();
            _host?.SetOverlayOpen(true);
        }

        public void Close()
        {
            if (!IsOpen)
                return;
            canvasRoot.SetActive(false);
            _host?.SetOverlayOpen(false);
        }

        // ---- Main screen

        public void ShowMain()
        {
            SetScreen(mainScreen);
            RenderMain();
        }

        private void RenderMain()
        {
            var playing = _host != null && _host.IsPlaying;
            if (subtitleText)
                subtitleText.text = playing ? $"Now playing: {CurrentTitle() ?? "scenario"}" : "Main menu";
            if (restartButton)
                restartButton.gameObject.SetActive(playing);
            if (exitButton)
                exitButton.gameObject.SetActive(playing);
            if (homeButton)
                homeButton.gameObject.SetActive(!playing);
            if (switchButton)
                switchButton.interactable = launcherMenu && launcherMenu.ScenarioCards().Count > 0;
        }

        public void Resume() => Close();

        public void Restart()
        {
            if (_host == null || !_host.IsPlaying)
                return;
            Close();
            _host.RestartScenario();
        }

        /// <summary>Ends the scenario and goes back to the main menu.</summary>
        public void ExitScenario()
        {
            if (_host == null || !_host.IsPlaying)
                return;
            Close();
            _host.ExitScenario();
        }

        /// <summary>Back to the beginning of the main menu (login or the client's scenarios).</summary>
        public void Home()
        {
            if (_host != null && _host.IsPlaying)
            {
                ExitScenario();
                return;
            }
            Close();
            if (launcherMenu)
                launcherMenu.ShowHome();
        }

        // ---- Switch screen

        public void ShowSwitch()
        {
            _page = 0;
            SetScreen(switchScreen);
            RenderSwitch();
        }

        public void PreviousPage()
        {
            _page = Mathf.Max(0, _page - 1);
            RenderSwitch();
        }

        public void NextPage()
        {
            _page++;
            RenderSwitch();
        }

        private void RenderSwitch()
        {
            foreach (var item in _items)
                Destroy(item);
            _items.Clear();

            var cards = launcherMenu ? launcherMenu.ScenarioCards() : new List<ScenarioCard>();
            var pages = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)itemsPerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            foreach (var card in cards.Skip(_page * itemsPerPage).Take(itemsPerPage))
                _items.Add(SpawnItem(card));

            if (previousPage)
                previousPage.gameObject.SetActive(pages > 1 && _page > 0);
            if (nextPage)
                nextPage.gameObject.SetActive(pages > 1 && _page < pages - 1);
            if (pageText)
                pageText.text = pages > 1 ? $"{_page + 1} / {pages}" : "";
            if (messageText)
                messageText.text = cards.Count == 0 ? "No scenarios yet. Sign in on the main menu first." : "";
        }

        private GameObject SpawnItem(ScenarioCard card)
        {
            var button = Instantiate(itemTemplate, list);
            button.gameObject.name = "item_" + card.Title;
            button.gameObject.SetActive(true);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => Choose(card));

            var label = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (label)
            {
                var status = IsCurrent(card) ? "Playing" : launcherMenu.StatusText(card);
                label.text = $"{card.Title}  <color=#52606D><size=80%>·  {status}</size></color>";
            }
            return button.gameObject;
        }

        private void Choose(ScenarioCard card)
        {
            var playing = _host != null && _host.IsPlaying;
            Close();
            if (!playing)
            {
                // On the main menu: its own screen, with languages and Start.
                launcherMenu.OpenOptions(card);
                return;
            }

            if (_host == null)
                return;
            if (card.IsBuiltIn)
                _host.SwitchToBuiltIn(card.BuiltInAddress);
            else
                _host.SwitchToDownloaded(card.Scenario.Id);
        }

        // ---- Helpers

        private void OnHostChanged()
        {
            if (!IsOpen)
                return;
            if (mainScreen && mainScreen.activeSelf)
                RenderMain();
            else if (switchScreen && switchScreen.activeSelf)
                RenderSwitch();
        }

        private bool IsCurrent(ScenarioCard card)
        {
            if (_host == null || !_host.IsPlaying)
                return false;
            return card.IsBuiltIn ? card.BuiltInAddress == _host.CurrentBuiltInAddress : card.Scenario.Id == _host.CurrentScenarioId;
        }

        private string CurrentTitle()
        {
            if (!launcherMenu)
                return null;
            var current = launcherMenu.ScenarioCards().FirstOrDefault(IsCurrent);
            if (current != null)
                return current.Title;
            return Launcher?.Current?.Title;
        }

        private void SetScreen(GameObject screen)
        {
            if (mainScreen)
                mainScreen.SetActive(screen == mainScreen);
            if (switchScreen)
                switchScreen.SetActive(screen == switchScreen);
        }

        private void PlaceInFrontOfHead()
        {
            var eyes = head ? head : Camera.main ? Camera.main.transform : null;
            if (eyes == null)
                return;
            var forward = Vector3.ProjectOnPlane(eyes.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.ProjectOnPlane(eyes.up, Vector3.up); // looking straight up or down
            forward.Normalize();
            var target = canvasRoot.transform;
            target.position = eyes.position + forward * distance + Vector3.up * heightOffset;
            // A world-space canvas reads correctly when it faces the same way as the viewer.
            target.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
