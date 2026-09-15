using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Pre-build sanity checks on the OpenXR/Android configuration, and post-build
    /// inspection of the produced APK to confirm the right native OpenXR runtime shipped.
    /// </summary>
    public static class ApkOpenXrValidator
    {
        private static readonly string[] GenericOpenXrNativeMarkers = { "libopenxr_loader.so", "libUnityOpenXR.so" };
        private const string PicoNativeMarker = "libopenxr_pico.so";

        public static void PreValidate(HeadsetBuildConfig config, HeadsetDevice device)
        {
            var manager = OpenXrTargetSwitcher.GetManagerSettingsForAndroid();
            var loaders = manager.loaders.Where(l => l != null).ToArray();

            if (loaders.Length != 1 || loaders[0].GetType() != typeof(UnityEngine.XR.OpenXR.OpenXRLoader))
            {
                throw new BuildFailedException(
                    $"Android XR loader list must contain exactly OpenXRLoader. Found: {string.Join(", ", loaders.Select(l => l.GetType().FullName))}");
            }

            var target = config.GetTarget(device);
            OpenXrTargetSwitcher.EnsureRequiredFeatureTypesPresent(target);
            AndroidStoreProfile.Validate(config, device);
            KeystoreSigner.EnsureConfigured();
        }

        public static void PostValidate(string apkPath, HeadsetDevice device)
        {
            if (!File.Exists(apkPath))
                throw new BuildFailedException($"Expected APK not found at {apkPath}");

            using var archive = ZipFile.OpenRead(apkPath);
            var entryNames = archive.Entries.Select(e => e.FullName).ToArray();

            var hasGenericLoader = GenericOpenXrNativeMarkers.Any(marker =>
                entryNames.Any(n => n.EndsWith(marker, StringComparison.OrdinalIgnoreCase)));

            if (!hasGenericLoader)
            {
                throw new BuildFailedException(
                    $"APK does not contain an OpenXR native runtime ({string.Join(" or ", GenericOpenXrNativeMarkers)}). This is not a valid OpenXR build.");
            }

            if (device == HeadsetDevice.Pico)
            {
                var hasPicoNative = entryNames.Any(n => n.EndsWith(PicoNativeMarker, StringComparison.OrdinalIgnoreCase));
                if (!hasPicoNative)
                    Debug.LogWarning($"[HeadsetBuild] APK does not contain '{PicoNativeMarker}'. If this APK crashes on device, confirm the PICO OpenXR feature set is enabled.");
            }
            else
            {
                var manifestEntry = archive.Entries.FirstOrDefault(e => e.FullName == "AndroidManifest.xml");
                var manifestMentionsQuest = manifestEntry != null &&
                    (ContainsMarker(manifestEntry, "com.oculus.intent.category.VR") ||
                     ContainsMarker(manifestEntry, "com.oculus.supportedDevices"));

                if (!manifestMentionsQuest)
                {
                    Debug.LogWarning(
                        "[HeadsetBuild] Could not confirm Quest VR manifest markers (com.oculus.intent.category.VR / com.oculus.supportedDevices) in AndroidManifest.xml. " +
                        "This is a best-effort binary-XML scan; if minification or manifest merging changed things, verify on-device instead of failing the build.");
                }
            }

            Debug.Log($"[HeadsetBuild] APK validated: {apkPath}");
        }

        private static bool ContainsMarker(ZipArchiveEntry entry, string marker)
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();

            var ascii = Encoding.ASCII.GetBytes(marker);
            var utf16 = Encoding.Unicode.GetBytes(marker); // Android binary XML string pools are UTF-16LE.

            return Contains(bytes, ascii) || Contains(bytes, utf16);
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length)
                return false;

            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                    return true;
            }

            return false;
        }
    }
}
