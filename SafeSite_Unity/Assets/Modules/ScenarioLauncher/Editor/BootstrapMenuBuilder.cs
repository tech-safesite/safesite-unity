using System;
using System.Linq;
using System.Reflection;
using Modules.ScenarioLauncher.External;
using Modules.ScenarioLauncher.UI.Menu;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Modules.ScenarioLauncher.Editor
{
    /// <summary>
    /// SafeSite > Launcher > Build Bootstrap Menu: adds the launcher's screens (login keypad, download
    /// progress, headset details) to the designed main menu, cloned from its own panels, buttons and
    /// text so they share its look, and wires them and the existing screens to <see cref="LauncherMenu"/>.
    /// Safe to run again: it removes what it added last time first. Hand-made layout changes to the
    /// generated screens are lost on a rebuild; change the existing screens freely.
    /// </summary>
    public static class BootstrapMenuBuilder
    {
        private const string LogPrefix = "[BootstrapMenuBuilder]";
        private const string Added = "launcher_"; // prefix of objects added inside existing screens

        private static readonly Color Green = Hex("0C8547");
        private static readonly Color Ink = Hex("1F2933");
        private static readonly Color Muted = Hex("52606D");
        private static readonly Color Bad = Hex("C53030");
        private static readonly Color Key = Color.white;

        private sealed class Kit
        {
            public Transform Canvas;
            public GameObject LightBackground;
            public GameObject DarkBackground;
            public GameObject Header;
            public TextMeshProUGUI Title;
            public GameObject Box;
            public GameObject Button;
            public GameObject LoadingBar;
            public Component Tween;
        }

        [MenuItem("SafeSite/Launcher/Build Bootstrap Menu")]
        public static void Build()
        {
            var canvasObject = GameObject.Find("MenuItems")?.transform.Find("MainMenuCanvas");
            if (canvasObject == null)
            {
                EditorUtility.DisplayDialog("Build Bootstrap Menu", "Open the Bootstrap scene first (it needs MenuItems/MainMenuCanvas).", "OK");
                return;
            }

            var canvas = canvasObject;
            var select = canvas.Find("Screen_ScenarioSelect");
            var options = canvas.Find("Screen_ScenarioOptionsSelect");
            var splash = canvas.Find("Screen_HeadsetOn");
            var setup = options.Find("ScenarioSettingsSetup");
            var kit = new Kit
            {
                Canvas = canvas,
                LightBackground = select.Find("MainBG_Panel").gameObject,
                DarkBackground = splash.Find("MainBG_Panel (1)").gameObject,
                Header = select.Find("Heading_Panel").gameObject,
                Title = select.Find("title").GetComponent<TextMeshProUGUI>(),
                Box = setup.Find("Image").gameObject,
                Button = setup.Find("StartTraining").gameObject,
                LoadingBar = splash.Find("PreviewLoadingBar").gameObject,
                Tween = select.GetComponents<Component>().FirstOrDefault(c => c.GetType().Name == "TweenOnEnable"),
            };

            Undo.SetCurrentGroupName("Build Bootstrap Menu");
            RemovePrevious(canvas);

            var services = EnsureServices();
            var menuObject = new GameObject("LauncherMenu", typeof(RectTransform));
            menuObject.transform.SetParent(canvas, false);
            Undo.RegisterCreatedObjectUndo(menuObject, "Build Bootstrap Menu");
            var menu = menuObject.AddComponent<LauncherMenu>();

            var login = BuildLogin(kit, menu);
            var progress = BuildProgress(kit);
            var headset = BuildHeadset(kit, menu);
            var grid = WireScenarioSelect(kit, select, menu);
            var optionsPanel = WireOptions(options, setup, menu);

            Set(menu, "splashScreen", splash.gameObject);
            Set(menu, "loginScreen", login);
            Set(menu, "scenariosScreen", grid);
            Set(menu, "optionsScreen", optionsPanel);
            Set(menu, "progressScreen", progress);
            Set(menu, "headsetScreen", headset);

            WireApp(menu);

            foreach (var screen in new Component[] { login, progress, headset })
                screen.gameObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
            Selection.activeGameObject = menuObject;
            Debug.Log($"{LogPrefix} Built the launcher screens on {canvas.name}; services on '{services.name}'. Save the scene to keep them.");
        }

        // ---- Screens

        private static LoginPanel BuildLogin(Kit kit, LauncherMenu menu)
        {
            var screen = NewScreen(kit, "Screen_Login");
            var panel = screen.gameObject.AddComponent<LoginPanel>();
            Clone(kit.LightBackground, screen, "MainBG_Panel");
            Clone(kit.Header, screen, "Heading_Panel");
            Text(kit, screen, "title", "Sign In", new Vector2(-156, 440), new Vector2(500, 100), kit.Title.fontSize, Color.white, TextAlignmentOptions.Left);

            Text(kit, screen, "prompt", "Enter your company's login code", new Vector2(0, 325), new Vector2(800, 50), 34, Ink, TextAlignmentOptions.Center);
            var box = Clone(kit.Box, screen, "codeBox", new Vector2(0, 235), new Vector2(720, 110));
            var code = Text(kit, box, "code", "_ _ _ _ _ _ _ _", Vector2.zero, new Vector2(700, 110), 64, Ink, TextAlignmentOptions.Center);
            code.characterSpacing = 10;
            var error = Text(kit, screen, "error", "", new Vector2(0, 150), new Vector2(820, 70), 26, Bad, TextAlignmentOptions.Center);

            // Eight keys a row, the API's alphabet, then Clear / Delete / Sign in.
            const float keyW = 90, keyH = 80, gap = 10;
            var keys = LoginPanel.Alphabet;
            for (var i = 0; i < keys.Length; i++)
            {
                var col = i % 8;
                var row = i / 8;
                var pos = new Vector2(-(7 * (keyW + gap)) / 2 + col * (keyW + gap), 60 - row * (keyH + gap));
                var key = MakeButton(kit, screen, "key_" + keys[i], keys[i].ToString(), pos, new Vector2(keyW, keyH), Key, Ink, 40);
                UnityEventTools.AddStringPersistentListener(key.onClick, panel.Type, keys[i].ToString());
            }

            var lastRow = 60 - 4 * (keyH + gap) - 20;
            var clear = MakeButton(kit, screen, "Clear", "Clear", new Vector2(-300, lastRow), new Vector2(190, keyH), Key, Ink, 32);
            UnityEventTools.AddPersistentListener(clear.onClick, panel.Clear);
            var back = MakeButton(kit, screen, "Delete", "Delete", new Vector2(-100, lastRow), new Vector2(190, keyH), Key, Ink, 32);
            UnityEventTools.AddPersistentListener(back.onClick, panel.Backspace);
            var signIn = MakeButton(kit, screen, "SignIn", "Sign in", new Vector2(200, lastRow), new Vector2(390, keyH), Green, Color.white, 36);
            UnityEventTools.AddPersistentListener(signIn.onClick, panel.SignIn);

            var builtIn = MakeButton(kit, screen, "BuiltInScenarios", "Built-in scenarios (development build)", new Vector2(0, -470), new Vector2(560, 60), Key, Muted, 24);
            UnityEventTools.AddPersistentListener(builtIn.onClick, menu.ShowBuiltInScenarios);

            Set(panel, "menu", menu);
            Set(panel, "codeText", code);
            Set(panel, "errorText", error);
            Set(panel, "signInButton", signIn);
            Set(panel, "signInLabel", signIn.GetComponentInChildren<TextMeshProUGUI>());
            Set(panel, "builtInButton", builtIn.gameObject);
            return panel;
        }

        private static ProgressPanel BuildProgress(Kit kit)
        {
            var screen = NewScreen(kit, "Screen_Progress");
            var panel = screen.gameObject.AddComponent<ProgressPanel>();
            Clone(kit.DarkBackground, screen, "MainBG_Panel");
            Clone(kit.Header, screen, "Heading_Panel");
            var title = Text(kit, screen, "title", "Downloading", new Vector2(0, 440), new Vector2(820, 100), 50, Color.white, TextAlignmentOptions.Center);
            var bar = Clone(kit.LoadingBar, screen, "ProgressBar", new Vector2(0, 219));
            var fill = bar.Find("loadingFill").GetComponent<Image>();
            fill.fillAmount = 0;
            var detail = bar.Find("Text (TMP)").GetComponent<TextMeshProUGUI>();
            detail.text = "";
            Text(kit, screen, "hint", "Keep the headset on and connected to Wi-Fi.", new Vector2(0, -320), new Vector2(800, 50), 28, new Color(1, 1, 1, 0.7f), TextAlignmentOptions.Center);

            Set(panel, "titleText", title);
            Set(panel, "fill", fill);
            Set(panel, "detailText", detail);
            return panel;
        }

        private static HeadsetPanel BuildHeadset(Kit kit, LauncherMenu menu)
        {
            var screen = NewScreen(kit, "Screen_Headset");
            var panel = screen.gameObject.AddComponent<HeadsetPanel>();
            Clone(kit.LightBackground, screen, "MainBG_Panel");
            Clone(kit.Header, screen, "Heading_Panel");
            Text(kit, screen, "title", "This Headset", new Vector2(-156, 440), new Vector2(500, 100), kit.Title.fontSize, Color.white, TextAlignmentOptions.Left);

            var box = Clone(kit.Box, screen, "detailsBox", new Vector2(0, 20), new Vector2(720, 620));
            var details = Text(kit, box, "details", "", Vector2.zero, new Vector2(640, 580), 30, Ink, TextAlignmentOptions.TopLeft);

            var back = MakeButton(kit, screen, "Back", "Back", new Vector2(-260, -410), new Vector2(220, 90), Key, Ink, 34);
            UnityEventTools.AddPersistentListener(back.onClick, menu.ShowScenarios);
            var refresh = MakeButton(kit, screen, "Refresh", "Refresh", new Vector2(0, -410), new Vector2(220, 90), Key, Ink, 34);
            UnityEventTools.AddPersistentListener(refresh.onClick, menu.Refresh);
            var signOut = MakeButton(kit, screen, "SignOut", "Sign out", new Vector2(260, -410), new Vector2(220, 90), Green, Color.white, 34);
            UnityEventTools.AddPersistentListener(signOut.onClick, panel.SignOut);

            Set(panel, "menu", menu);
            Set(panel, "detailsText", details);
            return panel;
        }

        /// <summary>Adds the client line, Refresh/Headset buttons, paging and a message line to the designed scenario grid.</summary>
        private static ScenarioGridPanel WireScenarioSelect(Kit kit, Transform select, LauncherMenu menu)
        {
            var panel = GetOrAdd<ScenarioGridPanel>(select.gameObject);
            var grid = (RectTransform)select.Find("ScenarioButtonGrid");
            var template = grid.Find("scenarioButton_FallingObjects").GetComponent<Button>();

            var subtitle = Text(kit, select, Added + "client", "", new Vector2(-56, 388), new Vector2(700, 40), 26, Color.white, TextAlignmentOptions.Left);
            var refresh = MakeButton(kit, select, Added + "Refresh", "Refresh", new Vector2(345, 475), new Vector2(170, 56), Key, Ink, 26);
            UnityEventTools.AddPersistentListener(refresh.onClick, menu.Refresh);
            var headset = MakeButton(kit, select, Added + "Headset", "Headset", new Vector2(345, 410), new Vector2(170, 56), Key, Ink, 26);
            UnityEventTools.AddPersistentListener(headset.onClick, menu.ShowHeadset);

            var message = Text(kit, select, Added + "message", "", new Vector2(0, -505), new Vector2(560, 60), 24, Muted, TextAlignmentOptions.Center);
            var previous = MakeButton(kit, select, Added + "PreviousPage", "<", new Vector2(-390, -505), new Vector2(80, 60), Key, Ink, 34);
            UnityEventTools.AddPersistentListener(previous.onClick, panel.PreviousPage);
            var next = MakeButton(kit, select, Added + "NextPage", ">", new Vector2(390, -505), new Vector2(80, 60), Key, Ink, 34);
            UnityEventTools.AddPersistentListener(next.onClick, panel.NextPage);

            Set(panel, "menu", menu);
            Set(panel, "grid", grid);
            Set(panel, "cardTemplate", template);
            Set(panel, "subtitleText", subtitle);
            Set(panel, "messageText", message);
            Set(panel, "previousPage", previous);
            Set(panel, "nextPage", next);
            Set(panel, "pageText", null);

            var so = new SerializedObject(panel);
            var icons = new (string Match, string Sprite)[]
            {
                ("falling", "crane"), ("lifting", "crane"), ("height", "worker"), ("hazard", "warning-symbol"),
                ("plant", "excavator"), ("pedestrian", "excavator"), ("excavat", "excavator"),
            };
            var iconsProp = so.FindProperty("icons");
            iconsProp.arraySize = icons.Length;
            for (var i = 0; i < icons.Length; i++)
            {
                var element = iconsProp.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("match").stringValue = icons[i].Match;
                element.FindPropertyRelative("sprite").objectReferenceValue = Icon(icons[i].Sprite);
            }
            so.FindProperty("defaultIcon").objectReferenceValue = Icon("warning-symbol");

            var builtIn = so.FindProperty("builtInScenarios");
            builtIn.arraySize = 1;
            var falling = builtIn.GetArrayElementAtIndex(0);
            falling.FindPropertyRelative("title").stringValue = "Falling Objects";
            falling.FindPropertyRelative("address").stringValue = "scenario_falling_objects_scene";
            falling.FindPropertyRelative("icon").objectReferenceValue = Icon("crane");
            so.ApplyModifiedPropertiesWithoutUndo();
            return panel;
        }

        /// <summary>Points the designed options screen's texts, language list, Start and X at the launcher.</summary>
        private static ScenarioOptionsPanel WireOptions(Transform options, Transform setup, LauncherMenu menu)
        {
            var panel = GetOrAdd<ScenarioOptionsPanel>(options.gameObject);
            var start = setup.Find("StartTraining").GetComponent<Button>();
            // Named "X / Return ", which Find would read as a path.
            var close = setup.Cast<Transform>().First(t => t.name.Trim() == "X / Return").GetComponent<Button>();
            ReplaceClicks(start, panel.StartScenario);
            ReplaceClicks(close, panel.Back);

            Set(panel, "menu", menu);
            Set(panel, "titleText", setup.Find("Text (TMP)").GetComponent<TextMeshProUGUI>());
            Set(panel, "detailsText", setup.Find("Text (TMP) (1)").GetComponent<TextMeshProUGUI>());
            Set(panel, "languageDropdown", setup.Find("Dropdown_Language")?.GetComponent<TMP_Dropdown>());
            Set(panel, "startButton", start);
            Set(panel, "startLabel", start.GetComponentInChildren<TextMeshProUGUI>());
            return panel;
        }

        // ---- App wiring (Assembly-CSharp types, found by name)

        /// <summary>Splash → LauncherMenu.ShowStart; LauncherMenu play events → TrainingScenarioLoader.</summary>
        private static void WireApp(LauncherMenu menu)
        {
            var controllerType = TypeNamed("SafeSiteMenuController");
            var controller = controllerType == null ? null : Object.FindFirstObjectByType(controllerType, FindObjectsInactive.Include);
            var finished = controller == null ? null : controllerType.GetField("onLoadingFinished", BindingFlags.Instance | BindingFlags.NonPublic);
            if (finished != null)
            {
                Undo.RecordObject(controller, "Build Bootstrap Menu");
                var unityEvent = (UnityEvent)finished.GetValue(controller) ?? new UnityEvent();
                while (unityEvent.GetPersistentEventCount() > 0)
                    UnityEventTools.RemovePersistentListener(unityEvent, 0);
                UnityEventTools.AddPersistentListener(unityEvent, menu.ShowStart);
                finished.SetValue(controller, unityEvent);
                EditorUtility.SetDirty(controller);
            }
            else
            {
                Debug.LogWarning($"{LogPrefix} No SafeSiteMenuController with onLoadingFinished: call LauncherMenu.ShowStart() when the splash ends.");
            }

            var loaderType = TypeNamed("SafeSite.Core.Scenarios.TrainingScenarioLoader");
            var loader = loaderType == null ? null : Object.FindFirstObjectByType(loaderType, FindObjectsInactive.Include);
            if (loader == null)
            {
                Debug.LogWarning($"{LogPrefix} No TrainingScenarioLoader: the launcher will load scenarios without fades or player reset.");
                return;
            }

            UnityEventTools.AddPersistentListener(menu.playRequested,
                (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), loader, "PlayDownloaded"));
            UnityEventTools.AddPersistentListener(menu.playBuiltInRequested,
                (UnityAction<string>)Delegate.CreateDelegate(typeof(UnityAction<string>), loader, "LoadScenario"));
        }

        /// <summary>The API and launcher services, once per scene, set to Production.</summary>
        private static GameObject EnsureServices()
        {
            if (ScenarioLauncherServiceLocator.FindInScene() is Component existing)
                return existing.gameObject;

            var holder = GameObject.Find("--Singletons / Persistant --");
            var services = new GameObject("SafeSite Services");
            if (holder != null)
                services.transform.SetParent(holder.transform, false);
            Undo.RegisterCreatedObjectUndo(services, "Build Bootstrap Menu");

            var api = services.AddComponent(TypeNamed("Modules.SafeSiteApiService.Internal.SafeSiteApiService"));
            var environments = AssetDatabase.FindAssets("t:SafeSiteEnvironments")
                .Select(g => AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault();
            if (environments != null)
                Set(api, "environments", environments);

            var launcher = services.AddComponent(TypeNamed("Modules.ScenarioLauncher.Internal.ScenarioLauncherService"));
            var so = new SerializedObject(launcher);
            so.FindProperty("environmentName").stringValue = "Production";
            so.ApplyModifiedPropertiesWithoutUndo();
            return services;
        }

        // ---- Helpers

        private static void RemovePrevious(Transform canvas)
        {
            foreach (var name in new[] { "Screen_Login", "Screen_Progress", "Screen_Headset", "LauncherMenu" })
            {
                var old = canvas.Find(name);
                if (old != null)
                    Undo.DestroyObjectImmediate(old.gameObject);
            }

            foreach (var child in canvas.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith(Added)).ToList())
            {
                if (child != null)
                    Undo.DestroyObjectImmediate(child.gameObject);
            }
        }

        private static RectTransform NewScreen(Kit kit, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = kit.Canvas.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(kit.Canvas, false);
            rect.sizeDelta = new Vector2(100, 100);
            Undo.RegisterCreatedObjectUndo(go, "Build Bootstrap Menu");
            if (kit.Tween != null)
                EditorUtility.CopySerialized(kit.Tween, go.AddComponent(kit.Tween.GetType()));
            return rect;
        }

        private static RectTransform Clone(GameObject template, Transform parent, string name, Vector2? position = null, Vector2? size = null)
        {
            var copy = Object.Instantiate(template, parent, false);
            copy.name = name;
            copy.SetActive(true);
            var rect = (RectTransform)copy.transform;
            if (position.HasValue)
                rect.anchoredPosition = position.Value;
            if (size.HasValue)
                rect.sizeDelta = size.Value;
            foreach (var button in copy.GetComponentsInChildren<Button>(true))
                ClearClicks(button);
            Undo.RegisterCreatedObjectUndo(copy, "Build Bootstrap Menu");
            return rect;
        }

        private static TextMeshProUGUI Text(Kit kit, Transform parent, string name, string text, Vector2 position, Vector2 size,
            float fontSize, Color colour, TextAlignmentOptions alignment)
        {
            var label = Clone(kit.Title.gameObject, parent, name, position, size).GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = fontSize;
            label.enableAutoSizing = false;
            label.color = colour;
            label.alignment = alignment;
            label.margin = Vector4.zero; // the title template carries a right margin for its layout
            label.textWrappingMode = TextWrappingModes.Normal;
            return label;
        }

        private static Button MakeButton(Kit kit, Transform parent, string name, string label, Vector2 position, Vector2 size,
            Color background, Color ink, float fontSize)
        {
            var rect = Clone(kit.Button, parent, name, position, size);
            rect.GetComponent<Image>().color = background;
            var text = rect.GetComponentInChildren<TextMeshProUGUI>();
            text.text = label;
            text.color = ink;
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.alignment = TextAlignmentOptions.Center;
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            // The template's icon, if any, belongs to "Start Training" only.
            foreach (var image in rect.GetComponentsInChildren<Image>(true).Where(i => i.gameObject != rect.gameObject))
                Object.DestroyImmediate(image.gameObject);
            return rect.GetComponent<Button>();
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var existing = go.GetComponent<T>();
            return existing != null ? existing : Undo.AddComponent<T>(go);
        }

        private static void ReplaceClicks(Button button, UnityAction action)
        {
            Undo.RecordObject(button, "Build Bootstrap Menu");
            ClearClicks(button);
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }

        private static void ClearClicks(Button button)
        {
            while (button.onClick.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
        }

        private static void Set(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(property) ?? throw new InvalidOperationException($"{target.GetType().Name} has no '{property}'.");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Sprite Icon(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/_AppCore/UI/Icons/{name}.png");

        private static Type TypeNamed(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);

        private static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;
    }
}
