using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    internal static class HeadsetBuildTypeUtil
    {
        /// <summary>
        /// Reads the "public const string featureId" every OpenXRFeature declares. There's no public
        /// instance accessor for it (the runtime field is internal), so this is reflection-only.
        /// </summary>
        public static string GetFeatureId(Type featureType)
        {
            var field = featureType.GetField("featureId", BindingFlags.Public | BindingFlags.Static);
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
