using System;
using System.Collections.Generic;
using UnityEngine;

namespace Modules.SafeSiteApiService.External
{
    [Serializable]
    public sealed class SafeSiteEnvironment
    {
        public const string ProductionName = "Production";
        public const string DemoName = "Demo";
        public const string LocalName = "Local";

        public string name;
        public string apiBaseUrl;
        public string supabaseUrl;

        [Tooltip("Supabase publishable (anon) key. Safe to commit; grants nothing on its own.")]
        public string publishableKey;

        public bool IsProduction => name == ProductionName;

        /// <summary>Local has no Supabase Auth; admin calls use a minted dev token instead.</summary>
        public bool IsLocal => string.IsNullOrEmpty(supabaseUrl);

        public override string ToString() => name;
    }

    /// <summary>
    /// The API environments (brief section 1). Lives at Resources/SafeSiteEnvironments.
    /// Demo is the default everywhere; nothing defaults to Production.
    /// </summary>
    [CreateAssetMenu(menuName = "SafeSite/Environments", fileName = "SafeSiteEnvironments")]
    public sealed class SafeSiteEnvironments : ScriptableObject
    {
        public const string ResourcePath = "SafeSiteEnvironments";

        public string defaultEnvironment = SafeSiteEnvironment.DemoName;
        public List<SafeSiteEnvironment> environments = new List<SafeSiteEnvironment>();

        public static SafeSiteEnvironments Load()
        {
            var asset = Resources.Load<SafeSiteEnvironments>(ResourcePath);
            if (asset != null)
                return asset;

            Debug.LogWarning($"[SafeSiteApiService] Resources/{ResourcePath} not found; using built-in defaults.");
            asset = CreateInstance<SafeSiteEnvironments>();
            asset.ResetToDefaults();
            return asset;
        }

        public SafeSiteEnvironment Find(string environmentName)
        {
            foreach (var env in environments)
            {
                if (string.Equals(env.name, environmentName, StringComparison.OrdinalIgnoreCase))
                    return env;
            }

            return null;
        }

        public SafeSiteEnvironment Default => Find(defaultEnvironment) ?? Find(SafeSiteEnvironment.DemoName) ?? (environments.Count > 0 ? environments[0] : null);

        public void ResetToDefaults()
        {
            defaultEnvironment = SafeSiteEnvironment.DemoName;
            environments = new List<SafeSiteEnvironment>
            {
                new SafeSiteEnvironment
                {
                    name = SafeSiteEnvironment.ProductionName,
                    apiBaseUrl = "https://mjuilbohduzofdrgpqbt.supabase.co/functions/v1/api",
                    supabaseUrl = "https://mjuilbohduzofdrgpqbt.supabase.co",
                },
                new SafeSiteEnvironment
                {
                    name = SafeSiteEnvironment.DemoName,
                    apiBaseUrl = "https://jqwmaffqjmafwdrggmlh.supabase.co/functions/v1/api",
                    supabaseUrl = "https://jqwmaffqjmafwdrggmlh.supabase.co",
                },
                new SafeSiteEnvironment
                {
                    name = SafeSiteEnvironment.LocalName,
                    apiBaseUrl = "http://127.0.0.1:8787/api",
                    supabaseUrl = "",
                },
            };
        }

        private void Reset() => ResetToDefaults();
    }
}
