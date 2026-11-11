using System;

namespace Modules.ScenarioLauncher.External
{
    /// <summary>
    /// The app's side of playing a scenario: fades, player reset and the menu around it. The
    /// in-scenario menu uses it to restart, leave or switch scenarios. Resolve with
    /// <see cref="ScenarioHostLocator"/>; the app's scenario loader implements it.
    /// </summary>
    public interface IScenarioHost
    {
        /// <summary>A scenario, downloaded or built into the app, is loaded.</summary>
        bool IsPlaying { get; }

        /// <summary>Fading, loading or unloading: don't start anything else.</summary>
        bool IsBusy { get; }

        /// <summary>Id of the playing downloaded scenario, or null.</summary>
        string CurrentScenarioId { get; }

        /// <summary>Addressables address of the playing built-in scenario, or null.</summary>
        string CurrentBuiltInAddress { get; }

        /// <summary>Raised whenever any of the above changes.</summary>
        event Action Changed;

        /// <summary>Unloads the playing scenario and loads it again from the start.</summary>
        void RestartScenario();

        /// <summary>Unloads the playing scenario and returns to the main menu.</summary>
        void ExitScenario();

        /// <summary>Ends the playing scenario (if any) and plays a downloaded one, downloading it first if needed.</summary>
        void SwitchToDownloaded(string scenarioId);

        /// <summary>Ends the playing scenario (if any) and plays one built into the app.</summary>
        void SwitchToBuiltIn(string address);

        /// <summary>
        /// An overlay menu opened or closed over the scenario. While open the controllers' UI rays
        /// must work whatever the scenario set; on close the scenario's own interaction comes back.
        /// </summary>
        void SetOverlayOpen(bool open);
    }

    public static class ScenarioHostLocator
    {
        public static IScenarioHost FindInScene()
        {
#if UNITY_2023_1_OR_NEWER
            var behaviours = UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(UnityEngine.FindObjectsInactive.Include, UnityEngine.FindObjectsSortMode.None);
#else
            var behaviours = UnityEngine.Object.FindObjectsOfType<UnityEngine.MonoBehaviour>(true);
#endif
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IScenarioHost host)
                    return host;
            }
            return null;
        }
    }
}
