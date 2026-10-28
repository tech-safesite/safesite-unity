using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Modules.SafeSiteApiService.External;
using TMPro;
using UnityEngine;

namespace Modules.SafeSiteApiService.Testbench
{
    public sealed class ContentPanel : TestbenchPanel
    {
        private static readonly Regex TokenParam = new Regex("([?&]token=)([^&]*)");

        private TMP_Dropdown _scenario;
        private TMP_Dropdown _keys;
        private TextMeshProUGUI _files;
        private ProgressBar _progress;
        private CancellationTokenSource _download;
        private List<ScenarioSummary> _scenarioOptions = new List<ScenarioSummary>();
        private List<string> _catalogKeys = new List<string>();

        public override string Title => "Content";

        protected override void Build(RectTransform content)
        {
            _scenario = Ui.Dropdown(content, "Scenario", new[] { "(get config first)" });

            var row = Ui.Row(content);
            Ui.Button(row, "Get download URLs", GetUrls);
            Ui.Button(row, "Download", Download, color: Ui.Accent);
            Ui.Button(row, "Cancel", () => _download?.Cancel());
            Ui.Button(row, "Delete local", () => Run("Delete local", () =>
            {
                var scenario = Selected();
                if (scenario == null)
                    return "pick a scenario";
                Service.DeleteLocalScenario(scenario.Id);
                _catalogKeys.Clear();
                Ui.SetOptions(_keys, new[] { "(load catalog)" });
                return "deleted " + scenario.Title;
            }));
            Ui.Button(row, "Test expired URL", TestExpiredUrl);

            _progress = Ui.Progress(content);

            var catalogRow = Ui.Row(content);
            Ui.Button(catalogRow, "Load catalog", LoadCatalog);
            _keys = Ui.DropdownField(catalogRow, new[] { "(load catalog)" });
            Ui.Button(catalogRow, "Instantiate", InstantiateSelected, color: Ui.Accent);

            var sceneRow = Ui.Row(content);
            Ui.Button(sceneRow, "Load scenario scene", () => Run("Load scenario scene", async () =>
            {
                if (Service.Content.LoadedDirectory == null)
                    return "load a catalog first";
                if (!Service.Content.HasMainScene)
                    return $"this catalog has no '{ScenarioAddresses.MainScene}' scene (not a scenario package build)";
                var scene = await Service.Content.LoadSceneAsync();
                return $"loaded {scene.name} ({scene.rootCount} root objects)";
            }), color: Ui.Accent);
            Ui.Button(sceneRow, "Unload scene", () => Run("Unload scene", async () =>
            {
                await Service.Content.UnloadSceneAsync();
                return "unloaded";
            }));

            _files = Ui.Label(content, "", Ui.FontSize - 2);
        }

        private ScenarioSummary Selected() =>
            _scenarioOptions.Count == 0 ? null : _scenarioOptions[Mathf.Clamp(_scenario.value, 0, _scenarioOptions.Count - 1)];

        private void GetUrls() => Run("Get download URLs", async () =>
        {
            var scenario = Selected();
            if (scenario == null)
                return "pick a scenario (get config first)";
            var manifest = await Service.Client.Device.GetContentAsync(scenario.Id);
            ShowManifest(manifest);
            return $"{manifest.Files.Count} files, v{manifest.Version}";
        });

        private void ShowManifest(ContentManifest manifest)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"v{manifest.Version} ({manifest.Platform}), URLs expire {manifest.ExpiresAt}");
            foreach (var file in manifest.Files)
                sb.AppendLine($"  {file.Path}  {(file.SizeBytes.HasValue ? Ui.Bytes(file.SizeBytes.Value) : "?")}");
            _files.text = sb.ToString().TrimEnd();
        }

        private void Download() => Run("Download", async () =>
        {
            var scenario = Selected();
            if (scenario == null)
                return "pick a scenario (get config first)";

            _download?.Cancel();
            _download = new CancellationTokenSource();
            _progress.Set(0, "starting");
            var progress = new Progress<DownloadProgress>(p => _progress.Set(p.Fraction,
                $"{p.FileIndex + 1}/{p.FileCount} {p.CurrentFile}  {Ui.Bytes(p.BytesDone)} / {Ui.Bytes(p.BytesTotal)}  {Ui.Bytes(p.BytesPerSecond)}/s"));
            try
            {
                var result = await Service.DownloadScenarioAsync(scenario.Id, progress, _download.Token);
                _progress.Set(1, $"done: {Ui.Bytes(result.Bytes)} in {result.Seconds:0.0}s");
                return $"v{result.Version} installed to {result.Directory}";
            }
            catch (ApiError e) when (e.IsCancelled)
            {
                _progress.Set(0, "cancelled");
                return "cancelled";
            }
        });

        private void LoadCatalog() => Run("Load catalog", async () =>
        {
            var scenario = Selected();
            if (scenario == null)
                return "pick a scenario";
            var version = Service.Store.GetInstalledVersion(scenario.Id);
            if (version == null)
                return "not installed: download first";

            var keys = await Service.Content.LoadCatalogAsync(Service.Store.GetContentDirectory(scenario.Id, version));
            _catalogKeys = keys.ToList();
            Ui.SetOptions(_keys, _catalogKeys.Count == 0 ? new[] { "(no GameObject keys)" } : _catalogKeys);
            return $"{_catalogKeys.Count} keys" + (Service.Content.HasMainScene ? $", has '{ScenarioAddresses.MainScene}'" : "");
        });

        private void InstantiateSelected() => Run("Instantiate", async () =>
        {
            if (_catalogKeys.Count == 0)
                return "load a catalog first";
            var key = _catalogKeys[Mathf.Clamp(_keys.value, 0, _catalogKeys.Count - 1)];
            var camera = Camera.main;
            var origin = camera != null ? camera.transform : null;
            var forward = origin != null ? Vector3.ProjectOnPlane(origin.forward, Vector3.up).normalized : Vector3.forward;
            if (forward == Vector3.zero)
                forward = Vector3.forward;
            var position = (origin != null ? origin.position : Vector3.zero) + forward * 2f;
            var instance = await Service.Content.InstantiateAsync(key, position, Quaternion.LookRotation(-forward));
            return $"{instance.name} at {position}";
        });

        /// <summary>Corrupts a signed URL's token, shows the storage error, then recovers with fresh URLs.</summary>
        private void TestExpiredUrl() => Run("Test expired URL", async () =>
        {
            var scenario = Selected();
            if (scenario == null)
                return "pick a scenario";

            var manifest = await Service.Client.Device.GetContentAsync(scenario.Id);
            var file = manifest.Files.OrderBy(f => f.SizeBytes ?? long.MaxValue).FirstOrDefault();
            if (file == null)
                return "scenario has no files";

            var corrupted = TokenParam.IsMatch(file.Url)
                ? TokenParam.Replace(file.Url, m => m.Groups[1].Value + "expired" + m.Groups[2].Value.Substring(Math.Min(12, m.Groups[2].Value.Length)))
                : file.Url + (file.Url.Contains("?") ? "&" : "?") + "token=expired";
            var temp = Path.Combine(Application.temporaryCachePath, "safesite-expired-url-test", Path.GetFileName(file.Path));

            string storageError;
            try
            {
                await Service.Client.Device.DownloadFileAsync(corrupted, temp);
                return "unexpected: the corrupted URL downloaded fine";
            }
            catch (ApiError e)
            {
                storageError = TestbenchState.DescribeError(e);
            }

            var fresh = await Service.Client.Device.GetContentAsync(scenario.Id);
            var freshUrl = fresh.Files.First(f => f.Path == file.Path).Url;
            await Service.Client.Device.DownloadFileAsync(freshUrl, temp);
            var size = new FileInfo(temp).Length;
            File.Delete(temp);
            return $"corrupted URL failed with [{storageError}]; fresh URL recovered ({Ui.Bytes(size)})";
        });

        protected override void OnStateChanged()
        {
            var previous = Selected()?.Id;
            _scenarioOptions = State.Scenarios.ToList();
            var labels = _scenarioOptions.Count == 0
                ? new List<string> { "(get config first)" }
                : _scenarioOptions.Select(s => $"{s.Title} v{s.Version} [{TestbenchState.ScenarioState(s.Version, Service.Store.GetInstalledVersion(s.Id))}]").ToList();
            var index = Math.Max(0, _scenarioOptions.FindIndex(s => s.Id == previous));
            Ui.SetOptions(_scenario, labels, index);
        }

        protected override void OnDestroy()
        {
            _download?.Cancel();
            base.OnDestroy();
        }
    }
}
