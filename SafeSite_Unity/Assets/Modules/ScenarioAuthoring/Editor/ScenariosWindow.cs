using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Modules.SafeSiteApiService.Editor;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// SafeSite > Scenarios: create scenarios and export them as packages for the dashboard's
    /// Scenarios > Import package. No sign-in needed.
    /// </summary>
    public sealed class ScenariosWindow : EditorWindow
    {
        private const string LogPrefix = "[ScenarioAuthoring]";
        private const int MaxLogLines = 1000;

        private readonly List<ScenarioDefinition> _definitions = new List<ScenarioDefinition>();
        private readonly List<string> _logLines = new List<string>();

        private Foldout _newSection;
        private TextField _newSlug;
        private TextField _newTitle;
        private TextField _newCategory;
        private TextField _newDescription;
        private IntegerField _newDuration;
        private TextField _newLanguages;
        private DropdownField _scenario;
        private VisualElement _inspector;
        private TextField _version;
        private Toggle _meta;
        private Toggle _pico;
        private ScrollView _log;

        [MenuItem("SafeSite/Scenarios")]
        public static void Open() => GetWindow<ScenariosWindow>("Scenarios").minSize = new Vector2(460, 600);

        [MenuItem("SafeSite/New Scenario")]
        public static void OpenNew()
        {
            Open();
            var window = GetWindow<ScenariosWindow>();
            if (window._newSection != null)
                window._newSection.value = true;
        }

        private ScenarioDefinition Selected =>
            _scenario == null || _scenario.index < 0 || _scenario.index >= _definitions.Count ? null : _definitions[_scenario.index];

        public void CreateGUI()
        {
            var scroll = new ScrollView();
            rootVisualElement.Add(scroll);
            var root = scroll.contentContainer;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = 6;

            // New scenario
            _newSection = new Foldout { text = "New scenario", value = false };
            _newSlug = new TextField("Slug") { tooltip = "e.g. ladder-access-setup" };
            _newTitle = new TextField("Title");
            _newCategory = new TextField("Category") { tooltip = "The dashboard folder, e.g. Working at Height" };
            _newDescription = new TextField("Description") { multiline = true };
            _newDuration = new IntegerField("Duration (min)") { value = 10 };
            _newLanguages = new TextField("Languages") { value = "English", tooltip = "Comma separated" };
            foreach (var element in new VisualElement[] { _newSlug, _newTitle, _newCategory, _newDescription, _newDuration, _newLanguages })
                _newSection.Add(element);
            _newSection.Add(new Button(CreateScenario) { text = "Create scenario" });
            root.Add(_newSection);

            // Scenario
            root.Add(Section("Scenario"));
            var pickRow = Row(root);
            pickRow.Add(new Button(RefreshList) { text = "Refresh" });
            _scenario = new DropdownField(new List<string>(), 0) { style = { flexGrow = 1 } };
            _scenario.RegisterValueChangedCallback(_ => OnSelected());
            pickRow.Add(_scenario);
            _inspector = new VisualElement();
            root.Add(_inspector);
            var sceneRow = Row(root);
            sceneRow.Add(new Button(OpenScene) { text = "Open scene" });
            sceneRow.Add(new Button(() => Guard(() =>
            {
                ScenarioGroups.Ensure(Selected);
                Log($"Group '{Selected.GroupName}' ready, main scene at '{ScenarioAddresses.MainScene}'.");
            })) { text = "Repair group" });

            // Export
            root.Add(Section("Export package"));
            _version = new TextField("Version");
            _meta = new Toggle("Meta") { value = true };
            _pico = new Toggle("Pico") { value = false };
            root.Add(_version);
            root.Add(_meta);
            root.Add(_pico);
            var actions = Row(root);
            actions.Add(new Button(Validate) { text = "Validate" });
            actions.Add(new Button(Export) { text = "Export package" });
            actions.Add(new Button(() => EditorUtility.RevealInFinder(Directory.Exists(ScenarioPackageExporter.OutputFolder)
                ? ScenarioPackageExporter.OutputFolder
                : Path.GetFullPath("."))) { text = "Open packages folder" });
            var tools = Row(root);
            tools.Add(new Button(() => Guard(() =>
            {
                AddressablesValidator.ApplyRecommendedSettings();
                Log("Applied recommended Addressables settings.");
            })) { text = "Fix Addressables settings" });
            tools.Add(new Button(AddressablesValidator.SwitchToAndroid) { text = "Switch to Android" });

            var logHeader = Row(root);
            logHeader.Add(new Label("Log") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
            logHeader.Add(new Button(() => GUIUtility.systemCopyBuffer = string.Join("\n", _logLines)) { text = "Copy" });
            logHeader.Add(new Button(() =>
            {
                _logLines.Clear();
                _log.Clear();
            }) { text = "Clear" });
            _log = new ScrollView { style = { minHeight = 200, backgroundColor = new Color(0, 0, 0, 0.2f) } };
            root.Add(_log);

            RefreshList();
        }

        private void RefreshList(ScenarioDefinition select = null)
        {
            var previous = select ?? Selected;
            _definitions.Clear();
            _definitions.AddRange(AssetDatabase.FindAssets("t:" + nameof(ScenarioDefinition))
                .Select(guid => AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(d => d != null)
                .OrderBy(d => d.slug, StringComparer.Ordinal));
            _scenario.choices = _definitions.Count == 0
                ? new List<string> { "(no scenarios yet: create one above)" }
                : _definitions.Select(d => $"{d.title} ({d.slug})").ToList();
            _scenario.index = Math.Max(0, previous != null ? _definitions.IndexOf(previous) : 0);
            OnSelected();
        }

        private void RefreshList() => RefreshList(null);

        private void OnSelected()
        {
            _inspector.Clear();
            var definition = Selected;
            if (definition == null)
                return;
            _inspector.Add(new InspectorElement(definition));
            _version.value = definition.SuggestedVersion();
        }

        private void CreateScenario() => Guard(() =>
        {
            var definition = ScenarioCreator.Create(_newSlug.value.Trim(), _newTitle.value.Trim(), _newCategory.value.Trim(),
                _newDescription.value.Trim(), _newDuration.value, _newLanguages.value.Split(','));
            Log($"Created {AssetDatabase.GetAssetPath(definition)} with scene {AssetDatabase.GetAssetPath(definition.mainScene)}.");
            _newSection.value = false;
            RefreshList(definition);
            EditorGUIUtility.PingObject(definition);
        });

        private void OpenScene() => Guard(() =>
        {
            var definition = Selected ?? throw new InvalidOperationException("Pick a scenario.");
            if (definition.mainScene == null)
                throw new InvalidOperationException("The scenario has no main scene.");
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(definition.mainScene));
        });

        private void Validate() => Guard(() =>
        {
            var definition = Selected ?? throw new InvalidOperationException("Pick a scenario.");
            var report = ScenarioValidator.Validate(definition, ScenarioGroups.Find(definition));
            foreach (var line in report.Lines)
                Log(line);
            Log(report.Ok ? $"{definition.slug}: ready to export." : $"{definition.slug}: fix the errors above.");
        });

        private void Export() => Guard(() =>
        {
            var definition = Selected ?? throw new InvalidOperationException("Pick a scenario.");
            var platforms = new List<string>();
            if (_meta.value)
                platforms.Add("meta");
            if (_pico.value)
                platforms.Add("pico");

            // The Addressables build unloads assets, so reload the definition afterwards.
            var path = AssetDatabase.GetAssetPath(definition);
            EditorUtility.DisplayProgressBar("SafeSite: exporting scenario", definition.slug, 0.1f);
            try
            {
                var zip = ScenarioPackageExporter.Export(definition, _version.value.Trim(), platforms, Log);
                EditorUtility.RevealInFinder(zip);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                RefreshList(AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(path));
            }
        });

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log("ERROR: " + e.Message);
                if (!(e is UploadFailedException) && !(e is ArgumentException) && !(e is InvalidOperationException))
                    Debug.LogException(e);
            }
        }

        private void Log(string line)
        {
            var stamped = $"{DateTime.Now:HH:mm:ss}  {line}";
            _logLines.Add(stamped);
            Debug.Log($"{LogPrefix} {line}");
            if (_log == null)
                return;
            var label = new Label(stamped) { style = { whiteSpace = WhiteSpace.Normal } };
            if (line.StartsWith("ERROR"))
                label.style.color = new Color(1f, 0.45f, 0.45f);
            else if (line.StartsWith("Warning"))
                label.style.color = new Color(1f, 0.8f, 0.4f);
            _log.Add(label);
            while (_log.childCount > MaxLogLines)
                _log.RemoveAt(0);
            _log.scrollOffset = new Vector2(0, float.MaxValue);
        }

        private static Label Section(string title)
        {
            var label = new Label(title);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.fontSize = 14;
            label.style.marginTop = 10;
            label.style.marginBottom = 2;
            return label;
        }

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            parent.Add(row);
            return row;
        }
    }
}
