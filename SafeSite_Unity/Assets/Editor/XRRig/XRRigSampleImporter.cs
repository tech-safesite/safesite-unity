using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace SafeSite.Build.XRRig
{
    internal static class XRRigSampleImporter
    {
        public const string PackageName = "com.unity.xr.interaction.toolkit";
        public const string StarterAssetsSample = "Starter Assets";
        public const string HandsDemoSample = "Hands Interaction Demo";

        public static bool EnsureSamplesImported()
        {
            var package = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .FirstOrDefault(p => p.name == PackageName);

            if (package == null)
            {
                Debug.LogError(
                    $"[XRRig] Package '{PackageName}' is not installed/resolved yet. Let the Unity Editor finish " +
                    "resolving packages (Packages/manifest.json was updated) and try again.");
                return false;
            }

            var samples = Sample.FindByPackage(PackageName, package.version)?.ToList();
            if (samples == null || samples.Count == 0)
            {
                Debug.LogError($"[XRRig] No samples found for {PackageName}@{package.version}.");
                return false;
            }

            var ok = true;
            var importedAnything = false;
            ok &= ImportSample(samples, StarterAssetsSample, ref importedAnything);
            ok &= ImportSample(samples, HandsDemoSample, ref importedAnything);

            if (importedAnything)
            {
                // Sample.Import() copies files to disk but does not guarantee the AssetDatabase has
                // finished resolving cross-sample nested-prefab references (e.g. the hands rig prefab
                // pointing at hand-visual prefabs from this same sample) before we go looking for them.
                // Force a full synchronous import pass so those references are resolved before we proceed.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }

            return ok;
        }

        private static bool ImportSample(System.Collections.Generic.List<Sample> samples, string displayName, ref bool importedAnything)
        {
            var sample = samples.FirstOrDefault(s => s.displayName == displayName);
            if (sample.displayName != displayName)
            {
                Debug.LogWarning($"[XRRig] Sample '{displayName}' not found in {PackageName}. Skipping.");
                return false;
            }

            if (sample.isImported)
                return true;

            if (!sample.Import(Sample.ImportOptions.OverridePreviousImports))
            {
                Debug.LogError($"[XRRig] Failed to import sample '{displayName}'.");
                return false;
            }

            importedAnything = true;
            Debug.Log($"[XRRig] Imported sample '{displayName}'.");
            return true;
        }
    }
}
