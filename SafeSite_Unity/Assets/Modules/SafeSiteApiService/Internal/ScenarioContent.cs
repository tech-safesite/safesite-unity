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

        public string LoadedDirectory { get; private set; }

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
            Debug.Log($"{LogPrefix} Loaded catalog {Path.GetFileName(catalogPath)} with {keys.Count} GameObject keys and {_bundleFiles.Count} local bundles.");
            return keys;
        }

        public async Task<GameObject> InstantiateAsync(string key, Vector3 position, Quaternion rotation)
        {
            if (_locator == null)
                throw new InvalidOperationException("Load a catalog first.");

            var handle = Addressables.InstantiateAsync(key, position, rotation);
            var instance = await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded || instance == null)
                throw new InvalidOperationException($"{LogPrefix} Could not instantiate '{key}'", handle.OperationException);

            _instances.Add(instance);
            return instance;
        }

        public void Unload()
        {
            foreach (var instance in _instances)
            {
                if (instance != null)
                    Addressables.ReleaseInstance(instance);
            }

            _instances.Clear();

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
