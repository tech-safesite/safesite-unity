using System;
using System.Linq;
using UnityEditor;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// Batch-mode version of SafeSite > Scenarios > Build package (for CI or a closed Editor):
    /// <code>
    /// Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod Modules.ScenarioAuthoring.Editor.ScenarioPackageCli.Export \
    ///   -scenario falling-objects [-packageVersion 1.0.3] (not -version: Unity takes that one itself) [-platforms meta,pico]
    /// </code>
    /// The version defaults to one patch above the last export; platforms default to meta. Exits non-zero on failure.
    /// </summary>
    public static class ScenarioPackageCli
    {
        private const string LogPrefix = "[ScenarioPackageCli]";

        public static void Export()
        {
            var exitCode = 0;
            try
            {
                var args = Environment.GetCommandLineArgs();
                var slug = Arg(args, "-scenario") ?? throw new ArgumentException("Pass -scenario <slug>.");
                var definition = AssetDatabase.FindAssets("t:" + nameof(ScenarioDefinition))
                                     .Select(g => AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                                     .FirstOrDefault(d => d != null && d.slug == slug)
                                 ?? throw new ArgumentException($"No scenario with slug '{slug}'.");
                var version = Arg(args, "-packageVersion") ?? definition.SuggestedVersion();
                var platforms = (Arg(args, "-platforms") ?? "meta").Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();

                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                    throw new InvalidOperationException("Run with -buildTarget Android.");

                var result = ScenarioPackageExporter.Export(definition, version, platforms, line => Console.WriteLine($"{LogPrefix} {line}"));
                Console.WriteLine($"{LogPrefix} Package ready: {result.ZipPath} ({result.FileCount} files, {ScenarioPackageExporter.Mb(result.TotalBytes)}, " +
                                  $"largest {ScenarioPackageExporter.Mb(result.LargestFileBytes)})");
            }
            catch (Exception e)
            {
                Console.WriteLine($"{LogPrefix} ERROR: {e.Message}");
                exitCode = 1;
            }

            Console.WriteLine($"{LogPrefix} Exit code {exitCode}");
            EditorApplication.Exit(exitCode);
        }

        private static string Arg(string[] args, string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
