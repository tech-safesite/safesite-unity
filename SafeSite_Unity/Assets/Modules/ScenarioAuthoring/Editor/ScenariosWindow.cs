using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Modules.SafeSiteApiService.Editor;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// SafeSite > Scenarios: pick a scenario, check its size, and build a package (zip) for the dashboard's
    /// Scenarios > Import package. One button does every Addressables step (Android target, settings, group,
    /// validation, bundle splitting, retries); the individual steps stay under Advanced. No sign-in needed.
    /// </summary>
    public sealed class ScenariosWindow : EditorWindow
    {
        private const string LogPrefix = "[ScenarioAuthoring]";
        private const int MaxLogLines = 1000;

        /// <summary>A build waiting for the switch to Android (which may reload scripts): slug|version|platforms.</summary>
        private const string PendingBuildKey = "SafeSite.Scenarios.PendingBuild";

        private readonly List<ScenarioDefinition> _definitions = new List<ScenarioDefinition>();
        private readonly List<string> _logLines = new List<string>();

        private Foldout _newSection;
        private ObjectField _newScene;
        private TextField _newSlug;
        private TextField _newTitle;
        private TextField _newCategory;
        private TextField _newDescription;
        private IntegerField _newDuration;
        private TextField _newLanguages;
        private DropdownField _scenario;
        private Label _status;
        private Foldout _details;
        private VisualElement _inspector;
        private VisualElement _sizePanel;
        private TextField _version;
        private Toggle _meta;
        private Toggle _pico;
        private Button _buildButton;
        private VisualElement _resultPanel;
        private Label _groupInfo;
        private Foldout _logSection;
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

            // Scenario
            root.Add(Section("Scenario"));
            var pickRow = Row(root);
            _scenario = new DropdownField(new List<string>(), 0) { style = { flexGrow = 1 } };
            _scenario.RegisterValueChangedCallback(_ => OnSelected());
            pickRow.Add(_scenario);
            pickRow.Add(new Button(RefreshList) { text = "Refresh" });
            _status = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 2, marginBottom = 4 } };
            root.Add(_status);
            _details = new Foldout { text = "Details (shown on the dashboard)", value = false };
            _inspector = new VisualElement();
            _details.Add(_inspector);
            root.Add(_details);

            // Step 1: size check
            root.Add(Section("1. Check sizes (optional, takes seconds)"));
            root.Add(Hint("Finds what makes the scenario big and offers fixes. Fixes only change the headset (Android) copy."));
            root.Add(new Button(CheckSizes) { text = "Check sizes" });
            _sizePanel = new VisualElement();
            root.Add(_sizePanel);

            // Step 2: build
            root.Add(Section("2. Build package"));
            var options = Row(root);
            _version = new TextField("Version") { style = { flexGrow = 1 }, tooltip = "Goes up by one each build. Change it only if the dashboard asks for a specific version." };
            options.Add(_version);
            _meta = new Toggle("Meta") { value = true };
            _pico = new Toggle("Pico") { value = false };
            options.Add(_meta);
            options.Add(_pico);
            _buildButton = new Button(BuildPackage)
            {
                text = "Build package for headsets",
                style = { height = 36, fontSize = 14, unityFontStyleAndWeight = FontStyle.Bold, marginTop = 4 },
            };
            root.Add(_buildButton);
            root.Add(Hint("Takes a few minutes. Everything else (Android, Addressables, splitting files under the 50 MB limit) is handled for you."));
            _resultPanel = new VisualElement();
            root.Add(_resultPanel);

            // New scenario
            _newSection = new Foldout { text = "New scenario", value = false, style = { marginTop = 10 } };
            _newScene = new ObjectField("Existing scene")
            {
                objectType = typeof(SceneAsset),
                tooltip = "Optional. A scene already in Assets/_Scenarios/<Name>/Scenes. Leave empty to create the folders and an empty scene.",
            };
            _newScene.RegisterValueChangedCallback(e => SuggestFromScene(e.newValue as SceneAsset));
            _newSlug = new TextField("Slug") { tooltip = "e.g. ladder-access-setup" };
            _newTitle = new TextField("Title");
            _newCategory = new TextField("Category") { tooltip = "The dashboard folder, e.g. Working at Height" };
            _newDescription = new TextField("Description") { multiline = true };
            _newDuration = new IntegerField("Duration (min)") { value = 10 };
            _newLanguages = new TextField("Languages") { value = "English", tooltip = "Comma separated" };
            foreach (var element in new VisualElement[] { _newScene, _newSlug, _newTitle, _newCategory, _newDescription, _newDuration, _newLanguages })
                _newSection.Add(element);
            _newSection.Add(new Button(CreateScenario) { text = "Create scenario" });
            root.Add(_newSection);

            // Advanced: the individual steps the build button runs.
            var advanced = new Foldout { text = "Advanced", value = false };
            _groupInfo = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4, marginBottom = 4 } };
            advanced.Add(_groupInfo);
            var advancedRow = Row(advanced);
            advancedRow.Add(new Button(OpenScene) { text = "Open scene" });
            advancedRow.Add(new Button(() => Guard(() =>
            {
                var group = ScenarioGroups.Ensure(Selected);
                Log($"Group '{group.Name}' ready, main scene at '{ScenarioGroups.MainSceneAddress(Selected)}'.");
                OnSelected();
            })) { text = "Repair group" });
            advancedRow.Add(new Button(Validate) { text = "Validate only" });
            var advancedRow2 = Row(advanced);
            advancedRow2.Add(new Button(() => Guard(() =>
            {
                AddressablesValidator.ApplyRecommendedSettings();
                Log("Applied recommended Addressables settings.");
            })) { text = "Fix Addressables settings" });
            advancedRow2.Add(new Button(AddressablesValidator.SwitchToAndroid) { text = "Switch to Android" });
            advancedRow2.Add(new Button(RevealPackages) { text = "Open packages folder" });
            root.Add(advanced);

            // Log
            _logSection = new Foldout { text = "Log", value = false };
            var logHeader = Row(_logSection);
            logHeader.Add(new Button(() => GUIUtility.systemCopyBuffer = string.Join("\n", _logLines)) { text = "Copy" });
            logHeader.Add(new Button(() =>
            {
                _logLines.Clear();
                _log.Clear();
            }) { text = "Clear" });
            _log = new ScrollView { style = { minHeight = 200, backgroundColor = new Color(0, 0, 0, 0.2f) } };
            _logSection.Add(_log);
            root.Add(_logSection);

            RefreshList();

            // A build that switched to Android carries on after the switch (and any script reload).
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingBuildKey, "")))
                EditorApplication.delayCall += ResumePendingBuild;
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
                ? new List<string> { "(no scenarios yet: open 'New scenario' below)" }
                : _definitions.Select(d => $"{d.title} ({d.slug})").ToList();
            _scenario.index = Math.Max(0, previous != null ? _definitions.IndexOf(previous) : 0);
            OnSelected();
        }

        private void RefreshList() => RefreshList(null);

        private void OnSelected()
        {
            _inspector.Clear();
            _sizePanel.Clear();
            _resultPanel.Clear();
            _groupInfo.text = "";
            _status.text = "";
            var definition = Selected;
            _buildButton.SetEnabled(definition != null);
            if (definition == null)
                return;
            _inspector.Add(new InspectorElement(definition));
            _version.value = definition.SuggestedVersion();

            var problems = definition.Validate();
            _details.value = problems.Count > 0;
            _status.text = StatusOf(definition, problems);

            var group = ScenarioGroups.Find(definition);
            _groupInfo.text = group == null
                ? "No Addressables group yet: building creates one (or click 'Repair group')."
                : $"Group '{group.Name}', scene address '{ScenarioGroups.MainSceneAddress(definition) ?? "(not in the group)"}'. " +
                  $"Exports always load it at '{ScenarioAddresses.MainScene}' on headsets; the group's own settings are kept.";
        }

        private static string StatusOf(ScenarioDefinition definition, List<string> problems)
        {
            if (problems.Count > 0)
                return "Needs details: " + string.Join(" ", problems);
            if (!SafeSiteTime.IsVersion(definition.lastExportedVersion))
                return "Never packaged. Click 'Build package for headsets'.";
            var zip = ZipPathFor(definition.slug, definition.lastExportedVersion);
            return File.Exists(zip)
                ? $"Last packaged: v{definition.lastExportedVersion} ({Path.GetFileName(zip)})."
                : $"Last packaged: v{definition.lastExportedVersion} (the zip isn't on this computer).";
        }

        private static string ZipPathFor(string slug, string version) =>
            Path.Combine(ScenarioPackageExporter.OutputFolder, $"{slug}_{version}.zip");

        // ---- Step 1: size check ----

        private void CheckSizes() => Guard(() =>
        {
            var definition = Selected ?? throw new InvalidOperationException("Pick a scenario.");
            var group = ScenarioGroups.Ensure(definition);
            ShowSizeReport(ScenarioSizeCheck.Run(group, SafeSiteUploadSettings.LoadOrCreate().MaxFileBytes));
        });

        private void ShowSizeReport(ScenarioSizeCheck.Report report)
        {
            _sizePanel.Clear();
            _sizePanel.Add(new Label($"{report.AssetCount} assets, about {ScenarioPackageExporter.Mb(report.TotalBytes)} before compression (the package is usually smaller).")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } });

            if (report.Issues.Count == 0)
            {
                _sizePanel.Add(new HelpBox("Nothing to fix. Build when ready.", HelpBoxMessageType.Info));
            }
            else
            {
                foreach (var issue in report.Issues)
                {
                    var row = Row(_sizePanel);
                    row.style.alignItems = Align.Center;
                    row.Add(new HelpBox(issue.Message, issue.Blocking ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning) { style = { flexGrow = 1, flexShrink = 1 } });
                    if (issue.FixLabel != null)
                        row.Add(new Button(() => Guard(() =>
                        {
                            issue.Apply();
                            Log($"Fixed {issue.Path}: {issue.FixLabel}.");
                            CheckSizes();
                        })) { text = issue.FixLabel });
                    var ping = new Button(() => EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(issue.Path))) { text = "Show" };
                    row.Add(ping);
                }

                var fixable = report.Fixable.Count();
                if (fixable > 1)
                    _sizePanel.Add(new Button(() => Guard(() =>
                    {
                        var fixedCount = ScenarioSizeCheck.FixAll(report);
                        Log($"Applied {fixedCount} fix(es).");
                        CheckSizes();
                    })) { text = $"Fix all {fixable}" });
            }

            var largest = new Foldout { text = "Biggest assets", value = false };
            foreach (var (path, bytes) in report.Largest)
            {
                var row = Row(largest);
                row.Add(new Label($"{ScenarioPackageExporter.Mb(bytes),9}  {path}") { style = { flexGrow = 1, flexShrink = 1, whiteSpace = WhiteSpace.Normal } });
                row.Add(new Button(() => EditorGUIUtility.PingObject(AssetDatabase.LoadMainAssetAtPath(path))) { text = "Show" });
            }
            _sizePanel.Add(largest);
        }

        // ---- Step 2: build ----

        private void BuildPackage()
        {
            var definition = Selected;
            if (definition == null)
                return;
            var platforms = new List<string>();
            if (_meta.value)
                platforms.Add("meta");
            if (_pico.value)
                platforms.Add("pico");
            RunBuild(definition, _version.value.Trim(), platforms);
        }

        private void RunBuild(ScenarioDefinition definition, string version, List<string> platforms)
        {
            _resultPanel.Clear();
            var errors = new List<string>();
            void Collect(string line)
            {
                Log(line);
                if (line.StartsWith("ERROR: "))
                    errors.Add(line.Substring("ERROR: ".Length));
            }

            try
            {
                var problems = definition.Validate();
                if (problems.Count > 0)
                {
                    _details.value = true;
                    throw new InvalidOperationException("Fill in the scenario's details first: " + string.Join(" ", problems));
                }
                if (platforms.Count == 0)
                    throw new InvalidOperationException("Tick Meta, Pico or both.");
                if (!SafeSiteTime.IsVersion(version))
                    throw new InvalidOperationException($"Version '{version}' should look like 1.0.3.");

                // Headsets are Android. Switching can reload scripts, so the build resumes afterwards.
                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    SessionState.SetString(PendingBuildKey, string.Join("|", definition.slug, version, string.Join(",", platforms)));
                    Log("Switching Unity to Android (headsets run Android). This happens once and can take a few minutes; the build continues by itself.");
                    ShowResult(new HelpBox("Switching to Android first. The build continues by itself when Unity is ready.", HelpBoxMessageType.Info));
                    AddressablesValidator.SwitchToAndroid();
                    EditorApplication.delayCall += ResumePendingBuild;
                    return;
                }

                if (NeedsSettingsFix())
                {
                    AddressablesValidator.ApplyRecommendedSettings();
                    Log("Set up Addressables for headset packages.");
                }

                var group = ScenarioGroups.Ensure(definition);
                if (!ConfirmTooBigAssets(group))
                    return;

                // The Addressables build unloads assets, so reload the definition afterwards.
                var path = AssetDatabase.GetAssetPath(definition);
                ScenarioPackageExporter.ExportResult result;
                EditorUtility.DisplayProgressBar("SafeSite: building scenario package", $"{definition.slug} v{version}: this takes a few minutes", 0.2f);
                try
                {
                    result = ScenarioPackageExporter.Export(definition, version, platforms, Collect);
                }
                finally
                {
                    EditorUtility.ClearProgressBar();
                    RefreshList(AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(path));
                }
                ShowSuccess(result);
            }
            catch (Exception e)
            {
                Log("ERROR: " + e.Message);
                if (!(e is UploadFailedException) && !(e is ArgumentException) && !(e is InvalidOperationException))
                    Debug.LogException(e);
                var text = errors.Count > 0 && (e.Message.Contains("see above") || e.Message.Contains("errors above"))
                    ? "The build stopped:\n• " + string.Join("\n• ", errors.Distinct())
                    : "The build stopped: " + e.Message;
                ShowResult(new HelpBox(text + "\n\nThe Log below has the details. Copy it to the tools team if you're stuck.", HelpBoxMessageType.Error));
                _logSection.value = true;
            }
        }

        private void ResumePendingBuild()
        {
            var pending = SessionState.GetString(PendingBuildKey, "");
            if (string.IsNullOrEmpty(pending))
                return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ResumePendingBuild;
                return;
            }
            SessionState.EraseString(PendingBuildKey);
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                ShowResult(new HelpBox("Unity didn't switch to Android. Check that Android Build Support is installed (Unity Hub > Installs > Add modules), then build again.", HelpBoxMessageType.Error));
                return;
            }

            var parts = pending.Split('|');
            RefreshList();
            var definition = _definitions.FirstOrDefault(d => d.slug == parts[0]);
            if (definition == null || parts.Length < 3)
                return;
            _scenario.index = _definitions.IndexOf(definition);
            RunBuild(definition, parts[1], parts[2].Split(',').Where(p => p.Length > 0).ToList());
        }

        private static bool NeedsSettingsFix()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings == null
                   || !settings.BuildRemoteCatalog
                   || settings.RemoteCatalogBuildPath.GetName(settings) != AddressableAssetSettings.kRemoteBuildPath
                   || settings.RemoteCatalogLoadPath.GetName(settings) != AddressableAssetSettings.kRemoteLoadPath;
        }

        /// <summary>
        /// Assets too big for one file fail the build after minutes of work, so catch them first. Fixable ones
        /// are fixed with the user's OK; ones with no automatic fix stop the build with what to do.
        /// </summary>
        private bool ConfirmTooBigAssets(AddressableAssetGroup group)
        {
            var maxFileBytes = SafeSiteUploadSettings.LoadOrCreate().MaxFileBytes;
            var report = ScenarioSizeCheck.Run(group, maxFileBytes);
            var tooBig = report.Issues.Where(i => i.TooBigForOneFile).ToList();
            if (tooBig.Count == 0)
                return true;

            var blocking = tooBig.Where(i => i.Blocking).ToList();
            if (blocking.Count > 0)
            {
                ShowSizeReport(report);
                throw new InvalidOperationException("Some assets are too big for one file and need a manual fix (see '1. Check sizes'): " +
                                                    string.Join("; ", blocking.Select(i => Path.GetFileName(i.Path))));
            }

            var names = string.Join("\n", tooBig.Select(i => "• " + Path.GetFileName(i.Path)));
            if (!EditorUtility.DisplayDialog("Some assets are too big",
                    $"These are too big to fit in one file on the server:\n{names}\n\nShrink them for headsets (the Editor copy stays the same) and build?",
                    "Shrink and build", "Cancel"))
                return false;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var issue in tooBig)
                    issue.Apply();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Log($"Shrank {tooBig.Count} asset(s) for headsets.");
            return true;
        }

        private void ShowSuccess(ScenarioPackageExporter.ExportResult result)
        {
            var box = new VisualElement
            {
                style =
                {
                    backgroundColor = new Color(0.2f, 0.55f, 0.3f, 0.25f),
                    paddingLeft = 8, paddingRight = 8, paddingTop = 6, paddingBottom = 6, marginTop = 6,
                    borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                },
            };
            box.Add(new Label($"Package ready: v{result.Version} for {string.Join(" + ", result.Platforms)}")
                { style = { unityFontStyleAndWeight = FontStyle.Bold, fontSize = 13 } });
            box.Add(new Label($"{Path.GetFileName(result.ZipPath)}: {result.FileCount} files, {ScenarioPackageExporter.Mb(result.TotalBytes)} " +
                              $"(largest file {ScenarioPackageExporter.Mb(result.LargestFileBytes)}).") { style = { whiteSpace = WhiteSpace.Normal } });
            box.Add(new Label("Next: in the SafeSite dashboard go to Scenarios > Import package and drop this zip in, or send it to your admin.")
                { style = { whiteSpace = WhiteSpace.Normal, marginTop = 4 } });
            var buttons = Row(box);
            buttons.Add(new Button(() => EditorUtility.RevealInFinder(result.ZipPath)) { text = "Show zip" });
            buttons.Add(new Button(() =>
            {
                GUIUtility.systemCopyBuffer = result.ZipPath;
                Log("Copied the zip's path.");
            }) { text = "Copy path" });
            ShowResult(box);
        }

        private void ShowResult(VisualElement element)
        {
            _resultPanel.Clear();
            _resultPanel.Add(element);
        }

        // ---- Scenario management ----

        /// <summary>Fills in the slug and title from the scenario folder's name (FallingObjects → falling-objects).</summary>
        private void SuggestFromScene(SceneAsset scene)
        {
            if (scene == null)
                return;
            var name = Path.GetFileName(ScenarioCreator.FolderOf(scene));
            if (string.IsNullOrWhiteSpace(_newSlug.value))
                _newSlug.value = ScenarioCreator.SlugFor(name);
            if (string.IsNullOrWhiteSpace(_newTitle.value))
                _newTitle.value = ScenarioCreator.TitleFor(name);
        }

        private void CreateScenario() => Guard(() =>
        {
            var definition = ScenarioCreator.Create(_newSlug.value.Trim(), _newTitle.value.Trim(), _newCategory.value.Trim(),
                _newDescription.value.Trim(), _newDuration.value, _newLanguages.value.Split(','), _newScene.value as SceneAsset);
            Log($"Created {AssetDatabase.GetAssetPath(definition)} with scene {AssetDatabase.GetAssetPath(definition.mainScene)}.");
            _newSection.value = false;
            _newScene.value = null;
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
            _logSection.value = true;
        });

        private static void RevealPackages() =>
            EditorUtility.RevealInFinder(Directory.Exists(ScenarioPackageExporter.OutputFolder) ? ScenarioPackageExporter.OutputFolder : Path.GetFullPath("."));

        private void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log("ERROR: " + e.Message);
                ShowResult(new HelpBox(e.Message, HelpBoxMessageType.Error));
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

        private static Label Hint(string text) =>
            new Label(text) { style = { whiteSpace = WhiteSpace.Normal, opacity = 0.7f, marginBottom = 4 } };

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, flexWrap = Wrap.Wrap } };
            parent.Add(row);
            return row;
        }
    }
}
