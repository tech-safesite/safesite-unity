using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Modules.SafeSiteApiService.Editor
{
    /// <summary>
    /// Implement in any editor assembly to adjust the project per headset platform (XR settings,
    /// shaders, scripting defines) around each Addressables content build. Found with TypeCache;
    /// needs a public parameterless constructor.
    /// When no hook exists, the scenario package exporter builds once and packages that output
    /// for every selected platform.
    /// </summary>
    public interface ISafeSitePlatformBuildHook
    {
        /// <param name="platform">"meta" or "pico".</param>
        void BeforeContentBuild(string platform);

        /// <param name="platform">"meta" or "pico".</param>
        void AfterContentBuild(string platform);
    }

    public static class SafeSitePlatformBuildHooks
    {
        /// <summary>One instance of every hook in the project.</summary>
        public static List<ISafeSitePlatformBuildHook> Find() =>
            TypeCache.GetTypesDerivedFrom<ISafeSitePlatformBuildHook>()
                .Where(t => !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (ISafeSitePlatformBuildHook)Activator.CreateInstance(t))
                .ToList();
    }
}
