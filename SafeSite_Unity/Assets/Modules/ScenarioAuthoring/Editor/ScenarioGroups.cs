using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Modules.SafeSiteApiService.Editor;
using Modules.SafeSiteApiService.External;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// Each scenario has its own Addressables group. A group the scenario team already set up (its own
    /// address, local paths, included in the app build for testing) is left as it is: only during an
    /// export does the group take the package contract (main scene at <see cref="ScenarioAddresses.MainScene"/>,
    /// remote paths), and its own settings are put back afterwards. New groups get the contract from the start.
    /// </summary>
    public static class ScenarioGroups
    {
        /// <summary>
        /// Dependencies at least this big get their own bundle during export, so no single file nears the
        /// storage limit (50 MB per file on Supabase Free). Smaller ones stay in the bundles that use them.
        /// </summary>
        public const long SplitAboveBytes = 512 * 1024;

        /// <summary>Split assets are packed into bundles of about this much (before compression), well under the limit.</summary>
        public const long SplitBundleBytes = 24L * 1024 * 1024;

        private const string SplitLabelPrefix = "safesite-split-";

        /// <summary>
        /// The group holding the main scene (directly or through its folder), or else the one named
        /// after the scenario folder (Scenario_Working_At_Height) or the slug.
        /// </summary>
        public static AddressableAssetGroup Find(ScenarioDefinition definition)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return null;
            return SceneEntry(settings, definition, true)?.parentGroup
                   ?? settings.FindGroup(GroupNameFor(definition))
                   ?? settings.FindGroup(definition.GroupName);
        }

        /// <summary>
        /// Makes sure the main scene is in a group. Existing groups and entries keep their settings and
        /// addresses; a new group follows the package contract.
        /// </summary>
        public static AddressableAssetGroup Ensure(ScenarioDefinition definition)
        {
            if (AddressableAssetSettingsDefaultObject.Settings == null)
                AddressablesValidator.ApplyRecommendedSettings();
            var settings = AddressableAssetSettingsDefaultObject.Settings;

            var group = Find(definition);
            var created = group == null;
            if (created)
            {
                group = settings.CreateGroup(GroupNameFor(definition), false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                ApplyContract(settings, schema);
                schema.IncludeInBuild = false;
                EditorUtility.SetDirty(schema);
            }

            // The scene needs its own entry so an export can give it the contract address. In an existing
            // group it gets the team's style of address (scenario_falling_objects_scene) for in-app loading.
            if (definition.mainScene != null && SceneEntry(settings, definition, false) == null)
            {
                var entry = settings.CreateOrMoveEntry(SceneGuid(definition), group, false, false);
                entry.address = created ? ScenarioAddresses.MainScene : InAppAddressFor(definition);
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            return group;
        }

        /// <summary>True when the main scene sits in the scenario's group (at any address).</summary>
        public static bool HasMainScene(ScenarioDefinition definition, AddressableAssetGroup group)
        {
            if (definition.mainScene == null || group == null)
                return false;
            return group.GetAssetEntry(SceneGuid(definition)) != null;
        }

        /// <summary>The main scene's address in its group, or null.</summary>
        public static string MainSceneAddress(ScenarioDefinition definition)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            return settings == null ? null : SceneEntry(settings, definition, false)?.address;
        }

        /// <summary>
        /// Gives the group the package contract until disposed: main scene at <see cref="ScenarioAddresses.MainScene"/>,
        /// remote build and load paths, hashed bundle names. With <paramref name="split"/>, everything in the
        /// group's folder entries and every large texture, audio or video clip it uses becomes an entry
        /// (<see cref="SplitCandidates"/>), labelled so they pack into bundles of about <paramref name="binBytes"/>
        /// (Pack Together By Label; Pack Separately fails on folder entries).
        /// The group's own settings, entries and labels come back on dispose.
        /// </summary>
        /// <param name="binBytes">Target bundle size for split assets; defaults to <see cref="SplitBundleBytes"/>. The exporter halves it when a build still comes out too big.</param>
        public static IDisposable ForExport(ScenarioDefinition definition, AddressableAssetGroup group, bool split = false, Action<string> log = null, long binBytes = SplitBundleBytes)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var schema = group.GetSchema<BundledAssetGroupSchema>()
                         ?? throw new UploadFailedException($"Group '{group.Name}' has no Bundled Asset Group schema.");
            var entry = group.GetAssetEntry(SceneGuid(definition))
                        ?? throw new UploadFailedException($"The main scene isn't in group '{group.Name}'.");

            var restore = new ExportScope(settings, schema, entry);
            ApplyContract(settings, schema);
            entry.address = ScenarioAddresses.MainScene;
            if (split)
            {
                schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel;
                var bins = DedupedBins(settings, group, binBytes, out var shared);
                if (shared > 0)
                    log?.Invoke($"{shared} asset(s) used by several bundles get a bundle of their own instead of a copy in each.");
                for (var i = 0; i < bins.Count; i++)
                {
                    var label = SplitLabelPrefix + i.ToString("00");
                    settings.AddLabel(label, false);
                    restore.Labels.Add(label);
                    foreach (var path in bins[i])
                    {
                        // Addressed by guid: paths can hold characters addresses can't ('[ ]'), and nothing loads these by name.
                        var added = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(path), group, false, false);
                        added.address = added.guid;
                        added.SetLabel(label, true, false, false);
                        restore.Added.Add(added.guid);
                    }
                }
                // Without the event, folder entries keep a stale (null) list of their contents and the build fails.
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                log?.Invoke($"Packing {restore.Added.Count} asset(s) into {bins.Count} bundle(s) of about {binBytes / (1024 * 1024)} MB.");
            }
            EditorUtility.SetDirty(schema);
            return restore;
        }

        /// <summary>
        /// Textures, audio and video the group's assets use that are at least <see cref="SplitAboveBytes"/>
        /// and not already an entry of any group.
        /// </summary>
        public static List<string> LargeDependencies(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            var roots = new List<AddressableAssetEntry>();
            foreach (var e in group.entries)
                e.GatherAllAssets(roots, true, true, true);
            var rootPaths = roots.Select(e => e.AssetPath).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();

            return AssetDatabase.GetDependencies(rootPaths, true)
                .Distinct()
                .Where(p => p.StartsWith("Assets/") && IsSplittable(p))
                .Where(p =>
                {
                    var existing = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(p), false);
                    return existing == null || existing.IsSubAsset;
                })
                .Where(p => EstimatedBytes(p) >= SplitAboveBytes)
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Everything that would otherwise land in one of the group's own bundles in bulk:
        /// every asset inside the group's folder entries (any type and size: the folder already brings the whole
        /// file, so packing a model on its own costs nothing extra), plus the large textures, audio and video
        /// the group uses from elsewhere (<see cref="LargeDependencies"/>). Scenes are left out: they always get their own bundle.
        /// </summary>
        public static List<string> SplitCandidates(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            // Read from the AssetDatabase: a folder entry's own list of contents is filled lazily and can be stale.
            // (IsFolder isn't set yet on an entry created in this Editor session, so ask the AssetDatabase.)
            var folders = group.entries.Select(e => e.AssetPath).Where(p => !string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p)).ToArray();
            var folderAssets = (folders.Length == 0 ? Array.Empty<string>() : AssetDatabase.FindAssets("", folders))
                .Select(AssetDatabase.GUIDToAssetPath)
                .Distinct()
                .Where(IsPackable)
                .Where(p =>
                {
                    // Skip anything with its own explicit entry (the main scene, an asset the team addressed themselves).
                    var existing = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(p), false);
                    return existing == null || existing.IsSubAsset;
                });
            return folderAssets.Concat(LargeDependencies(settings, group))
                .Distinct()
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Bins the <see cref="SplitCandidates"/>, then looks for assets that aren't entries but that two or more
        /// bundles (or a bundle and the scene) use: Addressables copies such an asset into every bundle that needs it,
        /// which made Falling Objects 817 MB instead of about 230. Those become candidates too (the fix Addressables'
        /// own "Check Duplicate Bundle Dependencies" rule applies), repeated until nothing is shared.
        /// </summary>
        private static List<List<string>> DedupedBins(AddressableAssetSettings settings, AddressableAssetGroup group, long binBytes, out int sharedCount)
        {
            var candidates = new HashSet<string>(SplitCandidates(settings, group), StringComparer.Ordinal);
            var initial = candidates.Count;
            var scenes = group.entries.Select(e => e.AssetPath).Where(p => p != null && p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)).ToList();
            var bins = Bins(candidates, binBytes);
            for (var pass = 0; pass < 4; pass++)
            {
                var users = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var unit in bins.Concat(scenes.Select(s => new List<string> { s })))
                {
                    foreach (var dep in AssetDatabase.GetDependencies(unit.ToArray(), true).Distinct())
                    {
                        if (candidates.Contains(dep) || unit.Contains(dep) || !IsPackable(dep))
                            continue;
                        var existing = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(dep), false);
                        if (existing != null && !existing.IsSubAsset)
                            continue;
                        users[dep] = users.TryGetValue(dep, out var n) ? n + 1 : 1;
                    }
                }
                var shared = users.Where(kv => kv.Value > 1).Select(kv => kv.Key).ToList();
                if (shared.Count == 0)
                    break;
                candidates.UnionWith(shared);
                bins = Bins(candidates, binBytes);
            }
            sharedCount = candidates.Count - initial;
            return bins;
        }

        /// <summary>What a folder entry puts in bundles: assets, not folders, scenes (own bundle), code or Editor-only files.</summary>
        private static bool IsPackable(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path) || path.Contains("/Editor/"))
                return false;
            if (!path.StartsWith("Assets/") && !path.StartsWith("Packages/")) // built-in resources can't be entries
                return false;
            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                return false;
            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type != null
                   && type != typeof(MonoScript)
                   && type != typeof(DefaultAsset)
                   && type != typeof(UnityEditorInternal.AssemblyDefinitionAsset)
                   && type != typeof(UnityEditorInternal.AssemblyDefinitionReferenceAsset)
                   && !(AssetImporter.GetAtPath(path) is PluginImporter)
                   && !EditorOnlyAssemblies.Value.Contains(type.Assembly.GetName().Name); // e.g. the ScenarioDefinition asset itself
        }

        /// <summary>Project assemblies compiled for the Editor only; assets of their types can't go in a bundle.</summary>
        private static readonly Lazy<HashSet<string>> EditorOnlyAssemblies = new Lazy<HashSet<string>>(() =>
        {
            var player = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.PlayerWithoutTestAssemblies)
                .Select(a => a.name);
            return new HashSet<string>(UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor)
                .Select(a => a.name).Except(player));
        });

        /// <summary>First-fit decreasing: biggest assets first, each into the first bin with room.</summary>
        private static List<List<string>> Bins(IEnumerable<string> paths, long binBytes)
        {
            var bins = new List<(long Bytes, List<string> Paths)>();
            foreach (var (path, bytes) in paths.Select(p => (p, EstimatedBytes(p))).OrderByDescending(x => x.Item2).ThenBy(x => x.p, StringComparer.Ordinal))
            {
                var index = bins.FindIndex(b => b.Bytes + bytes <= binBytes);
                if (index < 0)
                    bins.Add((bytes, new List<string> { path }));
                else
                    bins[index] = (bins[index].Bytes + bytes, bins[index].Paths.Concat(new[] { path }).ToList());
            }
            return bins.Select(b => b.Paths).ToList();
        }

        /// <summary>
        /// Textures, audio and video. Not models: as entries they bring every mesh and animation in the file,
        /// where a scene that merely uses one only brings what it uses.
        /// </summary>
        private static bool IsSplittable(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            return importer is TextureImporter || importer is AudioImporter || importer is VideoClipImporter;
        }

        /// <summary>Textures by their size once imported (what a bundle holds); everything else by file size.</summary>
        public static long EstimatedBytes(string path)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter)
            {
                var texture = AssetDatabase.LoadAssetAtPath<UnityEngine.Texture>(path);
                if (texture != null)
                    return UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);
            }
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }

        /// <summary>FallingObjects → Scenario_Falling_Objects, the scenario team's naming.</summary>
        public static string GroupNameFor(ScenarioDefinition definition)
        {
            var folder = System.IO.Path.GetFileName(definition.Folder ?? "");
            if (string.IsNullOrEmpty(folder) || folder == definition.slug)
                return definition.GroupName;
            return "Scenario_" + Regex.Replace(folder, "(?<=[a-z0-9])(?=[A-Z])", "_");
        }

        /// <summary>FallingObjects → scenario_falling_objects_scene.</summary>
        public static string InAppAddressFor(ScenarioDefinition definition) =>
            "scenario_" + Regex.Replace(System.IO.Path.GetFileName(definition.Folder ?? definition.slug), "(?<=[a-z0-9])(?=[A-Z])", "_").Replace('-', '_').ToLowerInvariant() + "_scene";

        private static void ApplyContract(AddressableAssetSettings settings, BundledAssetGroupSchema schema)
        {
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
            schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
        }

        /// <param name="includeFolders">Also accept the scene through a folder entry. Addressables returns those even
        /// when asked not to, so they're filtered out here.</param>
        private static AddressableAssetEntry SceneEntry(AddressableAssetSettings settings, ScenarioDefinition definition, bool includeFolders)
        {
            if (definition.mainScene == null)
                return null;
            var entry = settings.FindAssetEntry(SceneGuid(definition), includeFolders);
            return entry == null || (!includeFolders && entry.IsSubAsset) ? null : entry;
        }

        private static string SceneGuid(ScenarioDefinition definition) =>
            AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(definition.mainScene));

        private sealed class ExportScope : IDisposable
        {
            private readonly AddressableAssetSettings _settings;
            private readonly BundledAssetGroupSchema _schema;
            private readonly AddressableAssetEntry _entry;
            private readonly string _buildPath;
            private readonly string _loadPath;
            private readonly BundledAssetGroupSchema.BundleNamingStyle _naming;
            private readonly string _address;
            private readonly BundledAssetGroupSchema.BundlePackingMode _packing;

            /// <summary>Guids of the entries and the labels added for splitting, removed again on dispose.</summary>
            public readonly List<string> Added = new List<string>();
            public readonly List<string> Labels = new List<string>();

            public ExportScope(AddressableAssetSettings settings, BundledAssetGroupSchema schema, AddressableAssetEntry entry)
            {
                _settings = settings;
                _schema = schema;
                _entry = entry;
                _buildPath = schema.BuildPath.Id;
                _loadPath = schema.LoadPath.Id;
                _naming = schema.BundleNaming;
                _address = entry.address;
                _packing = schema.BundleMode;
            }

            public void Dispose()
            {
                _schema.BuildPath.SetVariableById(_settings, _buildPath);
                _schema.LoadPath.SetVariableById(_settings, _loadPath);
                _schema.BundleNaming = _naming;
                _schema.BundleMode = _packing;
                _entry.address = _address;
                foreach (var guid in Added)
                    _settings.RemoveAssetEntry(guid, false);
                foreach (var label in Labels)
                    _settings.RemoveLabel(label, false);
                EditorUtility.SetDirty(_schema);
                _settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
