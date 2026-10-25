using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace Modules.SafeSiteApiService.Internal
{
    /// <summary>
    /// Loads a downloaded Addressables catalog from disk and redirects the bundles it references
    /// to the downloaded files, mapping by file name (bundle names are unique within a build).
    /// </summary>
    internal sealed class ScenarioContent : IScenarioContent
    {
        private const string LogPrefix = "[SafeSiteApiService]";
        private static readonly Regex GuidKey = new Regex("^[0-9a-f]{32}$");

        private readonly List<GameObject> _instances = new List<GameObject>();
        private AsyncOperationHandle<IResourceLocator> _catalogHandle;
        private IResourceLocator _locator;
        private Dictionary<string, string> _bundleFiles;
        private AsyncOperationHandle<SceneInstance> _sceneHandle;

        public string LoadedDirectory { get; private set; }

        public bool HasMainScene =>
            _locator != null && _locator.Locate(ScenarioAddresses.MainScene, typeof(SceneInstance), out IList<IResourceLocation> locations) && locations.Count > 0;

        public async Task<IReadOnlyList<string>> LoadCatalogAsync(string localDirectory)
        {
            Unload();

            if (!Directory.Exists(localDirectory))
                throw new DirectoryNotFoundException($"No downloaded content at {localDirectory}");

            var files = Directory.EnumerateFiles(localDirectory, "*", SearchOption.AllDirectories).ToList();
            var catalogPath = files.FirstOrDefault(f =>
            {
                var name = Path.GetFileName(f);
                return name.StartsWith("catalog", StringComparison.OrdinalIgnoreCase) && (name.EndsWith(".json") || name.EndsWith(".bin"));
            });
            if (catalogPath == null)
                throw new FileNotFoundException($"No catalog_*.json or catalog_*.bin in {localDirectory}");

            _bundleFiles = files
                .Where(f => f.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                .GroupBy(Path.GetFileName)
                .ToDictionary(g => g.Key, g => g.First());

            Addressables.ResourceManager.InternalIdTransformFunc = TransformInternalId;

            _catalogHandle = Addressables.LoadContentCatalogAsync(catalogPath.Replace('\\', '/'), false);
            await _catalogHandle.Task;

            if (_catalogHandle.Status != AsyncOperationStatus.Succeeded)
            {
                var error = _catalogHandle.OperationException;
                Addressables.Release(_catalogHandle);
                _catalogHandle = default;
                Addressables.ResourceManager.InternalIdTransformFunc = null;
                throw new InvalidOperationException($"{LogPrefix} Catalog failed to load: {catalogPath}", error);
            }

            _locator = _catalogHandle.Result;
            LoadedDirectory = localDirectory;

            var keys = new List<string>();
            foreach (var key in _locator.Keys)
            {
                if (!(key is string address) || GuidKey.IsMatch(address) || address.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (_locator.Locate(address, typeof(GameObject), out IList<IResourceLocation> locations) && locations.Count > 0)
                    keys.Add(address);
            }

            keys = keys.Distinct().OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
            Debug.Log($"{LogPrefix} Loaded catalog {Path.GetFileName(catalogPath)} with {keys.Count} GameObject keys, " +
                      $"{(HasMainScene ? "a" : "no")} '{ScenarioAddresses.MainScene}' scene and {_bundleFiles.Count} local bundles.");
            return keys;
        }

        public async Task<GameObject> InstantiateAsync(string key, Vector3 position, Quaternion rotation)
        {
            if (_locator == null)
                throw new InvalidOperationException("Load a catalog first.");

            var handle = Addressables.InstantiateAsync(Locate(key, typeof(GameObject)), position, rotation);
            var instance = await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded || instance == null)
                throw new InvalidOperationException($"{LogPrefix} Could not instantiate '{key}'", handle.OperationException);

            _instances.Add(instance);
#if UNITY_EDITOR
            UseEditorShaders(new[] { instance }, false);
#endif
            return instance;
        }

        public async Task<Scene> LoadSceneAsync(string key = ScenarioAddresses.MainScene)
        {
            if (_locator == null)
                throw new InvalidOperationException("Load a catalog first.");

            var location = Locate(key, typeof(SceneInstance));
            await UnloadSceneAsync();
            _sceneHandle = Addressables.LoadSceneAsync(location, LoadSceneMode.Additive);
            await _sceneHandle.Task;
            if (_sceneHandle.Status != AsyncOperationStatus.Succeeded)
            {
                var error = _sceneHandle.OperationException;
                Addressables.Release(_sceneHandle);
                _sceneHandle = default;
                throw new InvalidOperationException($"{LogPrefix} Could not load scene '{key}'", error);
            }

            var scene = _sceneHandle.Result.Scene;
            SceneManager.SetActiveScene(scene);
#if UNITY_EDITOR
            UseEditorShaders(scene.GetRootGameObjects(), true);
#endif
            Debug.Log($"{LogPrefix} Loaded scene '{key}' ({scene.name}, {scene.rootCount} root objects).");
            return scene;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Scenario bundles are built for Android, and their compiled shaders can't draw in the Editor
        /// (everything renders magenta). In the Editor only, swap each material's shader for the
        /// Editor's copy with the same name. Headsets load the bundles' own shaders.
        /// </summary>
        private static void UseEditorShaders(IEnumerable<GameObject> roots, bool includeSkybox)
        {
            var fixedMaterials = new HashSet<Material>();
            void Fix(Material material)
            {
                if (material == null || material.shader == null || !fixedMaterials.Add(material))
                    return;
                var editorShader = Shader.Find(material.shader.name);
                if (editorShader != null)
                    material.shader = editorShader;
            }

            foreach (var root in roots)
            {
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.sharedMaterials)
                        Fix(material);
                }
            }

            if (includeSkybox)
                Fix(RenderSettings.skybox);
            if (fixedMaterials.Count > 0)
                Debug.Log($"{LogPrefix} Editor: using the Editor's shaders for {fixedMaterials.Count} material(s) built for another platform.");
        }
#endif

        /// <summary>
        /// Resolves a key in the downloaded catalog only. Every scenario uses the same addresses
        /// (scenario/main), and other catalogs (other scenarios, or the Editor's own Addressables
        /// data in Play Mode) would otherwise answer with the wrong content.
        /// </summary>
        private IResourceLocation Locate(string key, Type type)
        {
            if (_locator.Locate(key, type, out IList<IResourceLocation> locations) && locations.Count > 0)
                return locations[0];
            throw new InvalidOperationException($"{LogPrefix} '{key}' isn't in the loaded catalog ({Path.GetFileName(LoadedDirectory)}).");
        }

        public async Task UnloadSceneAsync()
        {
            if (!_sceneHandle.IsValid())
                return;
            var handle = _sceneHandle;
            _sceneHandle = default;
            await Addressables.UnloadSceneAsync(handle).Task;
        }

        public void Unload()
        {
            foreach (var instance in _instances)
            {
                if (instance != null)
                    Addressables.ReleaseInstance(instance);
            }

            _instances.Clear();

            if (_sceneHandle.IsValid())
            {
                Addressables.UnloadSceneAsync(_sceneHandle);
                _sceneHandle = default;
            }

            if (_locator != null)
                Addressables.RemoveResourceLocator(_locator);
            if (_catalogHandle.IsValid())
                Addressables.Release(_catalogHandle);

            _catalogHandle = default;
            _locator = null;
            _bundleFiles = null;
            LoadedDirectory = null;
            Addressables.ResourceManager.InternalIdTransformFunc = null;
        }

        private string TransformInternalId(IResourceLocation location)
        {
            var id = location.InternalId;
            if (_bundleFiles == null || !id.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase))
                return id;

            var fileName = id.Substring(id.LastIndexOfAny(new[] { '/', '\\' }) + 1);
            return _bundleFiles.TryGetValue(fileName, out var local) ? local : id;
        }
    }
}
