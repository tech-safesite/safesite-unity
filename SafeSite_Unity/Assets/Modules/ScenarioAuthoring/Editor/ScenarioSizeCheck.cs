using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// A quick look (seconds, no build) at what makes a scenario big, in plain words, with a one-click fix
    /// where there is a safe one: headset-only texture sizes and audio compression. Fixes only touch the
    /// Android import override, so the scenario looks the same in the Editor on PC.
    /// </summary>
    public static class ScenarioSizeCheck
    {
        /// <summary>Headsets rarely show textures bigger than this; above it the Android copy is capped.</summary>
        public const int HeadsetMaxTextureSize = 2048;

        /// <summary>Uncompressed (PCM) audio files bigger than this are worth compressing.</summary>
        public const long UncompressedAudioBytes = 1024 * 1024;

        private const string Android = "Android";

        public sealed class Issue
        {
            public string Path;
            public string Message;
            /// <summary>Short button text, or null when there's no safe automatic fix.</summary>
            public string FixLabel;
            /// <summary>True when the build will fail unless this is fixed.</summary>
            public bool Blocking;
            /// <summary>The asset alone nears the per-file limit, so the build would likely fail without a fix.</summary>
            public bool TooBigForOneFile;
            internal Action Fix;

            public void Apply() => Fix?.Invoke();
        }

        public sealed class Report
        {
            public long TotalBytes;
            public int AssetCount;
            public readonly List<(string Path, long Bytes)> Largest = new List<(string, long)>();
            public readonly List<Issue> Issues = new List<Issue>();
            public IEnumerable<Issue> Fixable => Issues.Where(i => i.FixLabel != null);
        }

        public static Report Run(AddressableAssetGroup group, long maxFileBytes)
        {
            var report = new Report();
            var sizes = AssetsOf(group)
                .Select(p => (Path: p, Bytes: ScenarioGroups.EstimatedBytes(p)))
                .OrderByDescending(x => x.Bytes)
                .ToList();
            report.AssetCount = sizes.Count;
            report.TotalBytes = sizes.Sum(x => x.Bytes);
            report.Largest.AddRange(sizes.Take(10));

            foreach (var (path, bytes) in sizes)
            {
                var name = System.IO.Path.GetFileName(path);
                var importer = AssetImporter.GetAtPath(path);
                Issue issue = null;
                if (importer is TextureImporter texture && TextureTooBig(path, texture, out var w, out var h))
                {
                    issue = new Issue
                    {
                        Path = path,
                        Message = $"{name} is {w}×{h} px ({ScenarioPackageExporter.Mb(bytes)}). Headsets rarely need more than {HeadsetMaxTextureSize} px.",
                        FixLabel = $"Use {HeadsetMaxTextureSize} px on headsets",
                        Fix = () => CapTexture(texture),
                    };
                }
                else if (importer is AudioImporter audio && IsUncompressed(audio) && FileBytes(path) > UncompressedAudioBytes)
                {
                    issue = new Issue
                    {
                        Path = path,
                        Message = $"{name} is uncompressed audio ({ScenarioPackageExporter.Mb(FileBytes(path))}).",
                        FixLabel = "Compress on headsets",
                        Fix = () => CompressAudio(audio),
                    };
                }

                // Only textures, audio and video go into a bundle whole. Models and other files bring just the parts
                // the scenario uses, so their file size says little: warn, never block.
                var wholeInBundle = importer is TextureImporter || importer is AudioImporter || importer is VideoClipImporter;
                if (!wholeInBundle && bytes > maxFileBytes * 9 / 10 && issue == null)
                {
                    issue = new Issue
                    {
                        Path = path,
                        Message = $"{name} is a {ScenarioPackageExporter.Mb(bytes)} file. Only the parts the scenario uses are packed, so this is usually fine; " +
                                  "if the build says a file is too big, simplify this one (fewer animations or a lighter mesh).",
                    };
                }
                else if (wholeInBundle && bytes > maxFileBytes * 9 / 10)
                {
                    var tip = importer is VideoClipImporter ? " Re-encode the video at a lower resolution or bitrate, or cut it into parts."
                        : issue != null ? "" : " Make it smaller or split it into parts.";
                    issue ??= new Issue { Path = path, Message = "" };
                    issue.Message = $"{name} is about {ScenarioPackageExporter.Mb(bytes)} on its own, close to the {maxFileBytes / (1024 * 1024)} MB per-file limit. " +
                                    (issue.Message.Length > 0 ? issue.Message : "") + tip;
                    issue.TooBigForOneFile = true;
                    issue.Blocking = issue.FixLabel == null;
                }

                if (issue != null)
                    report.Issues.Add(issue);
            }

            return report;
        }

        /// <summary>Applies every automatic fix in one import pass.</summary>
        public static int FixAll(Report report)
        {
            var fixes = report.Fixable.ToList();
            if (fixes.Count == 0)
                return 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var issue in fixes)
                    issue.Apply();
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            return fixes.Count;
        }

        /// <summary>Everything the group's entries bring into its bundles: the entries, folder contents and their dependencies.</summary>
        private static IEnumerable<string> AssetsOf(AddressableAssetGroup group)
        {
            var entries = new List<AddressableAssetEntry>();
            foreach (var e in group.entries)
                e.GatherAllAssets(entries, true, true, false);
            var roots = entries.Select(e => e.AssetPath).Where(p => !string.IsNullOrEmpty(p) && !AssetDatabase.IsValidFolder(p)).Distinct().ToArray();
            return AssetDatabase.GetDependencies(roots, true)
                .Distinct()
                .Where(p => p.StartsWith("Assets/") && !p.EndsWith(".cs") && !p.EndsWith(".unity") && !AssetDatabase.IsValidFolder(p));
        }

        private static bool TextureTooBig(string path, TextureImporter importer, out int width, out int height)
        {
            width = height = 0;
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(path);
            if (texture == null)
                return false;
            width = texture.width;
            height = texture.height;
            return Math.Max(width, height) > HeadsetMaxTextureSize && HeadsetMaxSize(importer) > HeadsetMaxTextureSize;
        }

        private static int HeadsetMaxSize(TextureImporter importer)
        {
            var android = importer.GetPlatformTextureSettings(Android);
            return android.overridden ? android.maxTextureSize : importer.maxTextureSize;
        }

        private static void CapTexture(TextureImporter importer)
        {
            var android = importer.GetPlatformTextureSettings(Android);
            if (!android.overridden)
            {
                android.overridden = true;
                android.format = TextureImporterFormat.Automatic;
                android.textureCompression = importer.textureCompression;
            }
            android.maxTextureSize = HeadsetMaxTextureSize;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static AudioImporterSampleSettings HeadsetAudio(AudioImporter importer) =>
            importer.ContainsSampleSettingsOverride(Android) ? importer.GetOverrideSampleSettings(Android) : importer.defaultSampleSettings;

        private static bool IsUncompressed(AudioImporter importer) =>
            HeadsetAudio(importer).compressionFormat == AudioCompressionFormat.PCM;

        private static void CompressAudio(AudioImporter importer)
        {
            var settings = HeadsetAudio(importer);
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            if (settings.loadType == AudioClipLoadType.DecompressOnLoad)
                settings.loadType = AudioClipLoadType.CompressedInMemory;
            importer.SetOverrideSampleSettings(Android, settings);
            importer.SaveAndReimport();
        }

        private static long FileBytes(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
    }
}
