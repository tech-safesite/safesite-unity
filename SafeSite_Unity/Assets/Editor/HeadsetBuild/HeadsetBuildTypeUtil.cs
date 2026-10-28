using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    internal static class HeadsetBuildTypeUtil
    {
        /// <summary>
        /// Reads the "public const string featureId" an OpenXRFeature declares. There's no public
        /// instance accessor for it (the runtime field is internal), so this is reflection-only.
        ///
        /// The casing is not consistent across packages: most features use "featureId", but some -
        /// OpenXRCompositionLayersFeature among them - declare "FeatureId". Matching only the
        /// lowercase spelling returned null for those, which made DisableEverythingNotAllowed treat
        /// them as unaccounted-for and switch them off again immediately after ApplyRequiredFeatures
        /// had enabled them.
        /// </summary>
        public static string GetFeatureId(Type featureType)
        {
            var field = featureType.GetField("featureId", BindingFlags.Public | BindingFlags.Static)
                        ?? featureType.GetField("FeatureId", BindingFlags.Public | BindingFlags.Static)
                        ?? featureType.GetFields(BindingFlags.Public | BindingFlags.Static)
                            .FirstOrDefault(f => f.FieldType == typeof(string) &&
                                                 string.Equals(f.Name, "featureId", StringComparison.OrdinalIgnoreCase));

            return field?.GetValue(null) as string;
        }

        public static Type FindType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch (Exception)
                {
                    continue;
                }

                if (type != null)
                    return type;
            }

            return null;
        }

        public static Type[] ResolveTypes(string[] fullNames, string context)
        {
            return fullNames
                .Select(name =>
                {
                    var type = FindType(name);
                    if (type == null)
                        Debug.LogWarning($"[HeadsetBuild] {context}: type '{name}' not found (package not installed or renamed). Skipping.");
                    return type;
                })
                .Where(t => t != null)
                .ToArray();
        }
    }
}
