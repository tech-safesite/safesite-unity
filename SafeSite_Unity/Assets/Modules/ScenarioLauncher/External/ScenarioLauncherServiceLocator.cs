using UnityEngine;

namespace Modules.ScenarioLauncher.External
{
    /// <summary>
    /// Scene locator. Lives in External so callers never import Internal.
    /// </summary>
    public static class ScenarioLauncherServiceLocator
    {
        public static IScenarioLauncherService FindInScene()
        {
#if UNITY_2023_1_OR_NEWER
            var behaviours = Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var behaviours = Object.FindObjectsOfType<MonoBehaviour>(true);
#endif
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IScenarioLauncherService service)
                    return service;
            }

            return null;
        }
    }
}
