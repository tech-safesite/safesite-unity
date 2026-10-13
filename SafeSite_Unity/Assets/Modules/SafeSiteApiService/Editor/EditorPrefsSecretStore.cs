using Modules.SafeSiteApiService.External;
using UnityEditor;

namespace Modules.SafeSiteApiService.Editor
{
    /// <summary>Keeps the admin refresh token in EditorPrefs (per machine user), keyed by environment.</summary>
    internal sealed class EditorPrefsSecretStore : ISecretStore
    {
        private const string Prefix = "SafeSiteContentUploader.";

        public string Get(string key) => EditorPrefs.HasKey(Prefix + key) ? EditorPrefs.GetString(Prefix + key) : null;

        public void Set(string key, string value) => EditorPrefs.SetString(Prefix + key, value ?? "");

        public void Delete(string key) => EditorPrefs.DeleteKey(Prefix + key);
    }
}
