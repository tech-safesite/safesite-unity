using System;
using System.Collections.Generic;
using Modules.ScenarioLauncher.External;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Modules.ScenarioLauncher.UI
{
    /// <summary>
    /// Screens for <see cref="IScenarioLauncherService"/>: access-code sign-in, the client's scenarios,
    /// download progress, and a small "Back to scenarios" bar while a scenario plays. Built in code
    /// on a screen-space canvas. Needs an IScenarioLauncherService in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScenarioLauncherUI : MonoBehaviour
    {
        private const string LogPrefix = "[ScenarioLauncherUI]";

        private static readonly Color Backdrop = new Color(0.07f, 0.09f, 0.10f, 0.96f);
        private static readonly Color Card = new Color(0.12f, 0.14f, 0.15f, 1f);
        private static readonly Color Row = new Color(0.16f, 0.19f, 0.20f, 1f);
        private static readonly Color Ink = new Color(0.95f, 0.96f, 0.96f);
        private static readonly Color Muted = new Color(0.62f, 0.67f, 0.69f);
        private static readonly Color Brand = new Color(0.30f, 0.72f, 0.52f);
        private static readonly Color BrandInk = new Color(0.05f, 0.10f, 0.08f);
        private static readonly Color Warn = new Color(0.95f, 0.71f, 0.29f);
        private static readonly Color Bad = new Color(0.96f, 0.49f, 0.45f);
        private static readonly Color Secondary = new Color(0.24f, 0.28f, 0.30f, 1f);

        [SerializeField] private Vector2 referenceResolution = new Vector2(1600, 1000);

        private IScenarioLauncherService _launcher;
        private RectTransform _backdrop;
        private TextMeshProUGUI _client;
        private Button _signOut;
        private GameObject _signInScreen;
        private TMP_InputField _code;
        private Button _signIn;
        private GameObject _listScreen;
        private RectTransform _list;
        private TextMeshProUGUI _listEmpty;
        private GameObject _progressScreen;
        private TextMeshProUGUI _progressTitle;
        private Image _progressFill;
        private TextMeshProUGUI _progressText;
        private TextMeshProUGUI _error;
        private RectTransform _playingBar;
        private TextMeshProUGUI _playingTitle;

        private void Start()
        {
            EnsureEventSystem();
            _launcher = ScenarioLauncherServiceLocator.FindInScene();
            Build();
            if (_launcher == null)
            {
                Debug.LogError($"{LogPrefix} No IScenarioLauncherService in the scene. Add the ScenarioLauncherService component.");
                _error.text = "No scenario launcher in this scene.";
                return;
            }

            _launcher.Changed += Render;
            Render();
        }

        private void OnDestroy()
        {
            if (_launcher != null)
                _launcher.Changed -= Render;
        }

        private void Render()
        {
            var state = _launcher.State;
            var signedIn = state != LauncherState.SignedOut && state != LauncherState.SigningIn;
            var playing = state == LauncherState.Playing;

            _backdrop.gameObject.SetActive(!playing);
            _playingBar.gameObject.SetActive(playing);
            _signInScreen.SetActive(!signedIn);
            _listScreen.SetActive(state == LauncherState.Browsing);
            _progressScreen.SetActive(state == LauncherState.Downloading || state == LauncherState.Loading);

            // Any client's code works here, so the header names nobody until someone signs in.
            _client.text = signedIn
                ? $"{_launcher.ClientName}<color=#{ColorUtility.ToHtmlStringRGB(Muted)}>  ·  {_launcher.HeadsetLabel}{LicenceNote()}</color>"
                : "";
            _signOut.gameObject.SetActive(state == LauncherState.Browsing);
            _signIn.interactable = state == LauncherState.SignedOut;
            _signIn.GetComponentInChildren<TextMeshProUGUI>().text = state == LauncherState.SigningIn ? "Signing in…" : "Sign in";
            _error.text = _launcher.Error ?? "";

            if (state == LauncherState.Browsing)
            {
                // Don't leave the code on screen for the next person who signs out.
                _code.text = "";
                RenderList();
            }

            if (_launcher.Current != null)
            {
                _progressTitle.text = (state == LauncherState.Loading ? "Starting " : "Downloading ") + _launcher.Current.Title;
                _progressFill.fillAmount = _launcher.Progress;
                _progressText.text = _launcher.ProgressText ?? "";
                _playingTitle.text = _launcher.Current.Title;
            }
        }

        private string LicenceNote()
        {
            switch (_launcher.LicenceStatus)
            {
                case "expiring": return $"  ·  <color=#{ColorUtility.ToHtmlStringRGB(Warn)}>Licence ending soon</color>";
                case "expired": return $"  ·  <color=#{ColorUtility.ToHtmlStringRGB(Bad)}>Licence expired</color>";
                case "suspended": return $"  ·  <color=#{ColorUtility.ToHtmlStringRGB(Bad)}>Suspended</color>";
                default: return "";
            }
        }

        private void RenderList()
        {
            for (int i = _list.childCount - 1; i >= 0; i--)
                Destroy(_list.GetChild(i).gameObject);

            _listEmpty.gameObject.SetActive(_launcher.Scenarios.Count == 0);
            foreach (var scenario in _launcher.Scenarios)
                ScenarioRow(scenario);
        }

        private void ScenarioRow(LauncherScenario scenario)
        {
            var row = Horizontal(_list, "Scenario " + scenario.Title, 16, new RectOffset(20, 16, 14, 14));
            Fill(row, Row);
            Layout(row, minHeight: 96);
            row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

            var text = Vertical(row, "Text", 4);
            Layout(text, flexibleWidth: 1);
            Label(text, scenario.Title, 30, Ink, FontStyles.Bold);
            var status = scenario.IsInstalled
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(Brand)}>Downloaded</color>"
                : scenario.HasUpdate
                    ? $"<color=#{ColorUtility.ToHtmlStringRGB(Warn)}>Update available</color>"
                    : "Not downloaded";
            Label(text, $"{scenario.Category}  ·  ~{scenario.DurationMinutes} min  ·  v{scenario.Version}  ·  {status}", 22, Muted);
            if (!string.IsNullOrEmpty(scenario.Description))
                Label(text, scenario.Description, 22, Muted);

            var id = scenario.Id;
            var button = Button(row, scenario.IsInstalled ? "Start" : "Download & start", Brand, BrandInk, () => _ = _launcher.PlayAsync(id));
            Layout(button.GetComponent<RectTransform>(), minWidth: 260, minHeight: 64);
        }

        // ---------- Layout ----------

        private void Build()
        {
            var canvasGo = new GameObject("LauncherCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = 0.5f;

            // Full-screen backdrop with a centred card.
            _backdrop = Rect("Backdrop", canvasGo.transform);
            Stretch(_backdrop);
            Fill(_backdrop, Backdrop);

            var card = Vertical(_backdrop, "Card", 20, new RectOffset(48, 48, 40, 40));
            Fill(card, Card);
            card.anchorMin = card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.sizeDelta = new Vector2(1100, 820);
            card.GetComponent<VerticalLayoutGroup>().childControlHeight = true;
            card.GetComponent<VerticalLayoutGroup>().childForceExpandHeight = false;

            var header = Horizontal(card, "Header", 16);
            var brand = Label(header, "SafeSite", 44, Brand, FontStyles.Bold);
            Layout(brand.rectTransform, minWidth: 220);
            _client = Label(header, "", 26, Ink);
            _client.alignment = TextAlignmentOptions.MidlineRight;
            Layout(_client.rectTransform, flexibleWidth: 1);
            _signOut = Button(header, "Sign out", Secondary, Ink, () => _launcher.SignOut());
            Layout(_signOut.GetComponent<RectTransform>(), minWidth: 160, minHeight: 52);

            BuildSignIn(card);
            BuildList(card);
            BuildProgress(card);

            _error = Label(card, "", 24, Bad);
            Layout(_error.rectTransform, minHeight: 34);

            // Shown over the loaded scenario.
            _playingBar = Horizontal(canvasGo.transform, "PlayingBar", 16, new RectOffset(24, 16, 12, 12));
            Fill(_playingBar, Backdrop);
            _playingBar.anchorMin = new Vector2(0, 1);
            _playingBar.anchorMax = new Vector2(1, 1);
            _playingBar.pivot = new Vector2(0.5f, 1);
            _playingBar.sizeDelta = new Vector2(0, 84);
            _playingBar.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
            _playingTitle = Label(_playingBar, "", 30, Ink, FontStyles.Bold);
            Layout(_playingTitle.rectTransform, flexibleWidth: 1);
            var back = Button(_playingBar, "Back to scenarios", Secondary, Ink, () => _ = _launcher.StopAsync());
            Layout(back.GetComponent<RectTransform>(), minWidth: 280, minHeight: 60);
            _playingBar.gameObject.SetActive(false);
        }

        private void BuildSignIn(RectTransform card)
        {
            var screen = Vertical(card, "SignIn", 18);
            Layout(screen, flexibleHeight: 1);
            _signInScreen = screen.gameObject;
            Spacer(screen, 30);
            Label(screen, "Sign in", 40, Ink, FontStyles.Bold);
            Label(screen, "Enter your company's login code. Your manager or SafeSite contact can give it to you.", 26, Muted);
            _code = Input(screen, "Login code, e.g. R4TW-9KXC");
            _code.onSubmit.AddListener(_ => SignIn());
            _signIn = Button(screen, "Sign in", Brand, BrandInk, SignIn);
            Layout(_signIn.GetComponent<RectTransform>(), minHeight: 72);
        }

        private void SignIn() => _ = _launcher.SignInAsync(_code.text);

        private void BuildList(RectTransform card)
        {
            var screen = Vertical(card, "List", 14);
            Layout(screen, flexibleHeight: 1);
            _listScreen = screen.gameObject;

            var top = Horizontal(screen, "ListHeader", 12);
            var title = Label(top, "Your scenarios", 36, Ink, FontStyles.Bold);
            Layout(title.rectTransform, flexibleWidth: 1);
            var refresh = Button(top, "Refresh", Secondary, Ink, () => _ = _launcher.RefreshAsync());
            Layout(refresh.GetComponent<RectTransform>(), minWidth: 160, minHeight: 52);

            _listEmpty = Label(screen, "No scenarios yet. Ask SafeSite to add scenarios to your company.", 26, Muted);

            // Scroll view
            var scroll = Rect("Scroll", screen);
            Layout(scroll, flexibleHeight: 1);
            var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.scrollSensitivity = 30;
            var viewport = Rect("Viewport", scroll);
            Stretch(viewport);
            viewport.gameObject.AddComponent<RectMask2D>();
            _list = Vertical(viewport, "Items", 12);
            _list.anchorMin = new Vector2(0, 1);
            _list.anchorMax = new Vector2(1, 1);
            _list.pivot = new Vector2(0.5f, 1);
            _list.sizeDelta = Vector2.zero;
            _list.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.viewport = viewport;
            scrollRect.content = _list;
        }

        private void BuildProgress(RectTransform card)
        {
            var screen = Vertical(card, "Progress", 18);
            Layout(screen, flexibleHeight: 1);
            _progressScreen = screen.gameObject;
            Spacer(screen, 120);
            _progressTitle = Label(screen, "", 36, Ink, FontStyles.Bold);
            _progressTitle.alignment = TextAlignmentOptions.Center;

            var track = Rect("Track", screen);
            Fill(track, Row);
            Layout(track, minHeight: 18);
            var fill = Rect("Fill", track);
            Stretch(fill);
            _progressFill = fill.gameObject.AddComponent<Image>();
            _progressFill.color = Brand;
            _progressFill.sprite = Sprite.Create(Texture2D.whiteTexture, new UnityEngine.Rect(0, 0, 4, 4), Vector2.one * 0.5f);
            _progressFill.type = Image.Type.Filled;
            _progressFill.fillMethod = Image.FillMethod.Horizontal;
            _progressFill.fillAmount = 0;

            _progressText = Label(screen, "", 24, Muted);
            _progressText.alignment = TextAlignmentOptions.Center;
            _progressScreen.SetActive(false);
        }

        // ---------- Widgets ----------

        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static Image Fill(RectTransform rect, Color color)
        {
            if (!rect.TryGetComponent(out Image image))
                image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static void Layout(RectTransform rect, float minWidth = -1, float minHeight = -1, float flexibleWidth = -1, float flexibleHeight = -1)
        {
            if (!rect.TryGetComponent(out LayoutElement element))
                element = rect.gameObject.AddComponent<LayoutElement>();
            element.minWidth = minWidth;
            element.minHeight = minHeight;
            element.flexibleWidth = flexibleWidth;
            element.flexibleHeight = flexibleHeight;
        }

        private static RectTransform Vertical(Transform parent, string name, float spacing, RectOffset padding = null)
        {
            var rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return rect;
        }

        private static RectTransform Horizontal(Transform parent, string name, float spacing, RectOffset padding = null)
        {
            var rect = Rect(name, parent);
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.padding = padding ?? new RectOffset();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = false;
            group.childForceExpandHeight = false;
            group.childAlignment = TextAnchor.MiddleLeft;
            return rect;
        }

        private static void Spacer(Transform parent, float height) => Layout(Rect("Spacer", parent), minHeight: height);

        private static TextMeshProUGUI Label(Transform parent, string text, float size, Color color, FontStyles style = FontStyles.Normal)
        {
            var rect = Rect("Label", parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.fontStyle = style;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        private static Button Button(Transform parent, string text, Color background, Color ink, Action onClick)
        {
            var rect = Rect("Button " + text, parent);
            var image = Fill(rect, background);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f);
            colors.disabledColor = new Color(0.6f, 0.6f, 0.6f, 0.7f);
            button.colors = colors;
            button.onClick.AddListener(() => onClick());

            var label = Label(rect, text, 26, ink, FontStyles.Bold);
            label.alignment = TextAlignmentOptions.Center;
            Stretch(label.rectTransform);
            label.margin = new Vector4(16, 0, 16, 0);
            Layout(rect, minHeight: 56);
            return button;
        }

        private static TMP_InputField Input(Transform parent, string placeholder)
        {
            var rect = Rect("Input", parent);
            var background = Fill(rect, Row);
            Layout(rect, minHeight: 80);

            var area = Rect("Text Area", rect);
            Stretch(area);
            area.offsetMin = new Vector2(20, 8);
            area.offsetMax = new Vector2(-20, -8);
            area.gameObject.AddComponent<RectMask2D>();

            var hint = Label(area, placeholder, 34, Muted, FontStyles.Italic);
            Stretch(hint.rectTransform);
            hint.alignment = TextAlignmentOptions.MidlineLeft;
            var text = Label(area, "", 34, Ink);
            Stretch(text.rectTransform);
            text.alignment = TextAlignmentOptions.MidlineLeft;

            var input = rect.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = area;
            input.textComponent = text;
            input.placeholder = hint;
            input.characterLimit = 32;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.characterValidation = TMP_InputField.CharacterValidation.None;
            input.onValidateInput = (_, _, c) => char.IsLetterOrDigit(c) || c == '-' ? char.ToUpperInvariant(c) : '\0';
            return input;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;

            // XRUIInputModule drives both the mouse (Editor) and XR ray interactors (headset).
            new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        }
    }
}
