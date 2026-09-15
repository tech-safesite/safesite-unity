using UnityEditor;
using UnityEditor.Build;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Confirms a custom Android keystore is configured before a headset build. The keystore
    /// itself (path, alias, passwords) is managed entirely through Unity's own
    /// Player Settings > Publishing Settings, not through project config.
    /// </summary>
    public static class KeystoreSigner
    {
        public static void EnsureConfigured()
        {
            if (!PlayerSettings.Android.useCustomKeystore)
                throw new BuildFailedException(
                    "No custom keystore is set. Configure one in Player Settings > Publishing Settings.");

            var path = StripKeystorePrefix(PlayerSettings.Android.keystoreName);
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                throw new BuildFailedException(
                    $"Keystore file not found: '{path}'. Check Player Settings > Publishing Settings.");

            if (string.IsNullOrEmpty(PlayerSettings.Android.keyaliasName))
                throw new BuildFailedException(
                    "No key alias is set. Configure one in Player Settings > Publishing Settings.");
        }

        private static string StripKeystorePrefix(string keystoreName)
        {
            if (string.IsNullOrEmpty(keystoreName))
                return keystoreName;

            const string inProject = "{inproject}: ";
            const string dedicated = "{dedicated}: ";
            if (keystoreName.StartsWith(inProject))
                return keystoreName.Substring(inProject.Length);
            if (keystoreName.StartsWith(dedicated))
                return keystoreName.Substring(dedicated.Length);
            return keystoreName;
        }
    }
}
