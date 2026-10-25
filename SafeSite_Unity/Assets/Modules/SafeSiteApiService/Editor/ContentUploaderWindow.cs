using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Modules.SafeSiteApiService.Editor
{
    /// <summary>SafeSite > Content Uploader: builds a scenario's Addressables content and publishes it to the backend.</summary>
    public sealed class ContentUploaderWindow : EditorWindow, IUploadProgress
    {
        private const string LogPrefix = "[SafeSiteContentUploader]";
        private const int MaxLogLines = 1000;

        private SafeSiteEnvironments _environments;
        private SafeSiteUploadSettings _settings;
        private ISafeSiteApiClient _client;
        private List<AdminScenario> _scenarios = new List<AdminScenario>();
        private CancellationTokenSource _cancel;
        private bool _busy;
        private readonly List<string> _logLines = new List<string>();

        private DropdownField _environment;
        private Label _productionBanner;
        private TextField _email;
        private TextField _password;
        private TextField _accessToken;
        private Button _devToken;
        private Label _signedIn;
        private DropdownField _scenario;
        private Label _scenarioInfo;
        private Toggle _meta;
        private Toggle _pico;
        private TextField _version;
        private Toggle _clean;
        private Toggle _skipUnchanged;
        private Toggle _removeOld;
        private Toggle _dryRun;
        private ProgressBar _progress;
        private ScrollView _log;
        private readonly List<VisualElement> _actions = new List<VisualElement>();

        [MenuItem("SafeSite/Content Uploader")]
        public static void Open() => GetWindow<ContentUploaderWindow>("Content Uploader").minSize = new Vector2(480, 640);

        private AdminScenario SelectedScenario =>
            _scenario == null || _scenario.index < 0 || _scenario.index >= _scenarios.Count ? null : _scenarios[_scenario.index];

        private void OnDisable() => _cancel?.Cancel();

        public void CreateGUI()
        {
            _environments = SafeSiteEnvironments.Load();
            _settings = SafeSiteUploadSettings.LoadOrCreate();

            var scroll = new ScrollView();
            rootVisualElement.Add(scroll);
            var root = scroll.contentContainer;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = 6;

            // 1. Environment & sign-in
            root.Add(Section("Environment & sign-in"));
            var names = _environments.environments.Select(e => e.name).ToList();
            // Never reopen on Production; it must be picked explicitly each time.
            var saved = _environments.Find(_settings.environment);
            var initial = saved != null && !saved.IsProduction ? saved.name : _environments.Default.name;
            _environment = new DropdownField("Environment", names, Math.Max(0, names.IndexOf(initial)));
            _environment.RegisterValueChangedCallback(e => SelectEnvironment(e.newValue));
            root.Add(_environment);
            _productionBanner = new Label("PRODUCTION: uploads reach real clients' headsets");
            _productionBanner.style.backgroundColor = new Color(0.7f, 0.1f, 0.1f);
            _productionBanner.style.color = Color.white;
            _productionBanner.style.unityFontStyleAndWeight = FontStyle.Bold;
            _productionBanner.style.paddingLeft = _productionBanner.style.paddingTop = _productionBanner.style.paddingBottom = 4;
            root.Add(_productionBanner);

            _signedIn = new Label();
            root.Add(_signedIn);
            _email = new TextField("Email");
            _password = new TextField("Password") { isPasswordField = true };
            root.Add(_email);
            root.Add(_password);
            var authRow = Row(root);
            authRow.Add(ActionButton("Sign in", SignIn));
            authRow.Add(ActionButton("Sign out", () =>
            {
                _client.Auth.SignOut();
                Log("Signed out; refresh token deleted.");
            }));
            _devToken = ActionButton("Use dev token (Local)", () =>
            {
                _client.Auth.UseLocalDevToken();
                Log("Using the local dev token.");
            });
            authRow.Add(_devToken);
            _accessToken = new TextField("Access token") { isPasswordField = true };
            root.Add(_accessToken);
            Row(root).Add(ActionButton("Use access token", () =>
            {
                _client.Auth.UseAccessToken(_accessToken.value);
                _accessToken.value = "";
                Log(_client.Auth.IsSignedIn ? "Access token set." : "Access token cleared.");
            }));

            // 2. Scenario
            root.Add(Section("Scenario"));
            var scenarioRow = Row(root);
            scenarioRow.Add(ActionButton("Refresh", () => Run(RefreshScenarios)));
            _scenario = new DropdownField(new List<string> { "(refresh to load)" }, 0);
            _scenario.style.flexGrow = 1;
            _scenario.RegisterValueChangedCallback(_ => OnScenarioSelected());
            scenarioRow.Add(_scenario);
            _scenarioInfo = new Label();
            _scenarioInfo.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_scenarioInfo);

            // 3. Build options
            root.Add(Section("Build options"));
            _meta = new Toggle("Meta") { value = _settings.meta };
            _pico = new Toggle("Pico") { value = _settings.pico };
            _version = new TextField("New version");
            _clean = new Toggle("Clean build") { value = _settings.cleanBuild };
            _skipUnchanged = new Toggle("Skip unchanged bundles") { value = _settings.skipUnchangedBundles };
            _removeOld = new Toggle("Remove old files") { value = _settings.removeOldFiles };
            _dryRun = new Toggle("Dry run (build + diff, no uploads)");
            foreach (var element in new VisualElement[] { _meta, _pico, _version, _clean, _skipUnchanged, _removeOld, _dryRun })
                root.Add(element);

            // 4. Actions
            root.Add(Section("Actions"));
            var actions = Row(root);
            actions.Add(ActionButton("Validate", Validate));
            actions.Add(ActionButton("Build & Upload", () => Run(() => BuildAndUpload(false))));
            actions.Add(ActionButton("Upload only (last build)", () => Run(() => BuildAndUpload(true))));
            var tools = Row(root);
            tools.Add(ActionButton("Open build folder", OpenBuildFolder));
            tools.Add(ActionButton("Fix Addressables settings", () =>
            {
                AddressablesValidator.ApplyRecommendedSettings();
                Log("Applied recommended Addressables settings.");
                Validate();
            }));
            tools.Add(ActionButton("Switch to Android", AddressablesValidator.SwitchToAndroid));
            var cancel = new Button(() => _cancel?.Cancel()) { text = "Cancel" };
            tools.Add(cancel);

            _progress = new ProgressBar { lowValue = 0, highValue = 1, title = "" };
            root.Add(_progress);

            var logHeader = Row(root);
            logHeader.Add(new Label("Log") { style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1 } });
            logHeader.Add(new Button(() => GUIUtility.systemCopyBuffer = string.Join("\n", _logLines)) { text = "Copy" });
            logHeader.Add(new Button(() =>
            {
                _logLines.Clear();
                _log.Clear();
            }) { text = "Clear" });
            _log = new ScrollView { style = { minHeight = 220, backgroundColor = new Color(0, 0, 0, 0.2f) } };
            root.Add(_log);

            SelectEnvironment(initial);
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

        private Button ActionButton(string text, Action action)
        {
            var button = new Button(() =>
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Log("ERROR: " + e.Message);
                }

                UpdateUi();
            }) { text = text };
            _actions.Add(button);
            return button;
        }

        private void SelectEnvironment(string name)
        {
            var environment = _environments.Find(name);
            if (environment == null)
                return;

            _cancel?.Cancel();
            _client = SafeSiteApiClientFactory.Create(environment, new EditorPrefsSecretStore());
            _client.Logged += entry => Log((entry.IsError ? "HTTP ERROR " : "HTTP ") + entry);
            _client.Auth.Changed += UpdateUi;
            _settings.environment = environment.name;
            _settings.Save();
            _scenarios.Clear();
            _scenario.choices = new List<string> { "(refresh to load)" };
            _scenario.index = 0;
            UpdateUi();

            if (!environment.IsLocal)
            {
                Run(async () =>
                {
                    if (await _client.Auth.TryRestoreAsync())
                    {
                        Log($"Signed in to {environment.name} as {_client.Auth.Email}.");
                        await RefreshScenarios();
                    }
                });
            }
        }

        private void SignIn()
        {
            var password = _password.value;
            _password.value = "";
            Run(async () =>
            {
                await _client.Auth.SignInAsync(_email.value.Trim(), password);
                Log($"Signed in as {_client.Auth.Email}.");
                await RefreshScenarios();
            });
        }

        private async Task RefreshScenarios()
        {
            _scenarios = await _client.Admin.GetScenariosAsync();
            var choices = _scenarios.Select(s => $"{s.Title} ({s.Slug}) v{s.Version} [{s.Status}]").ToList();
            var savedId = _settings.For(_client.Environment.name).scenarioId;
            _scenario.choices = choices.Count == 0 ? new List<string> { "(no scenarios)" } : choices;
            _scenario.index = Math.Max(0, _scenarios.FindIndex(s => s.Id == savedId));
            Log($"Loaded {_scenarios.Count} scenarios.");
            OnScenarioSelected();
        }

        private void OnScenarioSelected()
        {
            var scenario = SelectedScenario;
            if (scenario != null)
            {
                var selection = _settings.For(_client.Environment.name);
                if (selection.scenarioId != scenario.Id)
                {
                    selection.scenarioId = scenario.Id;
                    _settings.Save();
                }

                _version.value = SafeSiteTime.BumpPatch(scenario.Version);
                var hasPico = !string.IsNullOrEmpty(scenario.PicoContentPath);
                _pico.SetEnabled(hasPico);
                if (!hasPico)
                    _pico.value = false;
            }

            UpdateUi();
        }

        private void UpdateUi()
        {
            if (_environment == null || _client == null)
                return;

            var environment = _client.Environment;
            _productionBanner.style.display = environment.IsProduction ? DisplayStyle.Flex : DisplayStyle.None;
            _devToken.style.display = environment.IsLocal ? DisplayStyle.Flex : DisplayStyle.None;
            _email.style.display = _password.style.display = environment.IsLocal ? DisplayStyle.None : DisplayStyle.Flex;
            _signedIn.text = _client.Auth.IsSignedIn ? $"Signed in: {_client.Auth.Email ?? "(token)"}" : "Not signed in";

            var scenario = SelectedScenario;
            _scenarioInfo.text = scenario == null
                ? ""
                : $"Meta: {Describe(scenario.MetaContentPath, scenario.MetaContent)}\n" +
                  $"Pico: {(scenario.PicoContentPath == null ? "no folder (set it in the dashboard to enable Pico)" : Describe(scenario.PicoContentPath, scenario.PicoContent))}\n" +
                  $"Last uploaded from here: {_settings.For(environment.name).lastUploadedVersion ?? "-"}";

            foreach (var action in _actions)
                action.SetEnabled(!_busy);
        }

        private static string Describe(string folder, PlatformContentSummary content) =>
            $"{folder}  {content?.Files ?? 0} files · {(content?.SizeBytes ?? 0) / (1024.0 * 1024.0):0.00} MB";

        private async void Run(Func<Task> action)
        {
            if (_busy)
                return;
            _busy = true;
            _cancel = new CancellationTokenSource();
            UpdateUi();
            try
            {
                await action();
            }
            catch (ApiError e)
            {
                Log($"ERROR {e.Status} {e.Code}: {e.ErrorMessage}");
            }
            catch (OperationCanceledException)
            {
                Log("Cancelled.");
            }
            catch (Exception e)
            {
                Log("ERROR: " + e.Message);
                Debug.LogException(e);
            }
            finally
            {
                _busy = false;
                Clear();
                UpdateUi();
            }
        }

        private void Validate()
        {
            var report = AddressablesValidator.ValidateSettings();
            foreach (var line in report.Lines)
                Log(line);
            Log(report.Ok ? $"Validation passed ({report.Warnings.Count} warning(s))." : $"Validation failed with {report.Errors.Count} error(s).");
        }

        private async Task BuildAndUpload(bool uploadOnly)
        {
            var scenario = SelectedScenario ?? throw new InvalidOperationException("Refresh and pick a scenario first.");
            var environment = _client.Environment;
            if (!_client.Auth.IsSignedIn)
                throw new InvalidOperationException("Sign in first.");
            if (environment.IsProduction && !_dryRun.value &&
                !EditorUtility.DisplayDialog("Upload to PRODUCTION?",
                    $"This publishes '{scenario.Title}' v{_version.value} to real clients' headsets.", "Upload to Production", "Cancel"))
                return;

            var options = new UploadOptions
            {
                Version = _version.value.Trim(),
                CleanBuild = _clean.value,
                SkipUnchangedBundles = _skipUnchanged.value,
                RemoveOldFiles = _removeOld.value,
                DryRun = _dryRun.value,
                UploadOnly = uploadOnly,
                MaxFileBytes = _settings.MaxFileBytes,
            };
            if (_meta.value)
                options.Platforms.Add(SafeSitePlatforms.Meta);
            if (_pico.value)
                options.Platforms.Add(SafeSitePlatforms.Pico);

            _settings.meta = _meta.value;
            _settings.pico = _pico.value;
            _settings.cleanBuild = _clean.value;
            _settings.skipUnchangedBundles = _skipUnchanged.value;
            _settings.removeOldFiles = _removeOld.value;
            _settings.Save();

            Log($"--- {(uploadOnly ? "Upload only" : "Build & Upload")} {scenario.Slug} v{options.Version} to {environment.name} ({string.Join(", ", options.Platforms)}){(options.DryRun ? " [dry run]" : "")}");
            var pipeline = new ContentUploadPipeline(_client, Log, this);
            var report = await pipeline.RunAsync(scenario, options, _cancel.Token);
            if (report.succeeded && !report.dryRun)
            {
                _settings.For(environment.name).lastUploadedVersion = report.version;
                _settings.Save();
                await RefreshScenarios();
            }
        }

        private void OpenBuildFolder()
        {
            var folder = AddressablesContentBuilder.RemoteBuildFolder();
            if (folder == null || !Directory.Exists(folder))
            {
                Log("No remote build folder yet" + (folder != null ? $" ({folder})" : "") + ".");
                return;
            }

            EditorUtility.RevealInFinder(folder);
        }

        private void Log(string line)
        {
            var text = $"{DateTime.Now:HH:mm:ss} {line}";
            Debug.Log($"{LogPrefix} {line}");
            _logLines.Add(text);
            if (_log == null)
                return;
            var label = new Label(text) { style = { whiteSpace = WhiteSpace.Normal } };
            label.selection.isSelectable = true;
            if (line.StartsWith("ERROR") || line.StartsWith("HTTP ERROR") || line.StartsWith("FAILED"))
                label.style.color = new Color(1f, 0.45f, 0.45f);
            else if (line.StartsWith("Warning"))
                label.style.color = new Color(1f, 0.8f, 0.3f);
            _log.Add(label);
            while (_log.childCount > MaxLogLines)
                _log.RemoveAt(0);
            _log.schedule.Execute(() => _log.scrollOffset = new Vector2(0, float.MaxValue));
        }

        // IUploadProgress
        public bool Report(string title, string info, float fraction)
        {
            if (_progress != null)
            {
                _progress.value = fraction;
                _progress.title = info;
            }

            if (EditorUtility.DisplayCancelableProgressBar(title, info, fraction))
            {
                _cancel?.Cancel();
                return true;
            }

            return false;
        }

        public void Clear()
        {
            EditorUtility.ClearProgressBar();
            if (_progress != null)
            {
                _progress.value = 0;
                _progress.title = "";
            }
        }
    }
}
