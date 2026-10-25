using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Modules.ScenarioLauncher.External
{
    public enum LauncherState
    {
        /// <summary>No client signed in on this device: show the access-code screen.</summary>
        SignedOut,
        SigningIn,
        /// <summary>Signed in: show the client's scenarios.</summary>
        Browsing,
        Downloading,
        Loading,
        /// <summary>A scenario's scene is loaded.</summary>
        Playing,
    }

    /// <summary>
    /// The client-facing flow on a headset: sign in with the client's access code, see the scenarios
    /// the client has, download one and play its scene. Resolve with <see cref="ScenarioLauncherServiceLocator"/>.
    /// </summary>
    public interface IScenarioLauncherService
    {
        LauncherState State { get; }

        /// <summary>Signed-in client's name, or null.</summary>
        string ClientName { get; }

        /// <summary>This device's name in the dashboard, e.g. "Headset 4".</summary>
        string HeadsetLabel { get; }

        /// <summary>active, expiring, expired or suspended; null before the first sync.</summary>
        string LicenceStatus { get; }

        IReadOnlyList<LauncherScenario> Scenarios { get; }

        /// <summary>The scenario being downloaded, loaded or played.</summary>
        LauncherScenario Current { get; }

        /// <summary>0..1 while downloading.</summary>
        float Progress { get; }

        string ProgressText { get; }

        /// <summary>The last failure, worded for the person holding the headset; cleared by the next action.</summary>
        string Error { get; }

        /// <summary>Raised on the main thread whenever any of the above changes.</summary>
        event Action Changed;

        Task SignInAsync(string accessCode);

        /// <summary>Re-fetches the client's scenarios.</summary>
        Task RefreshAsync();

        /// <summary>Downloads the scenario if it isn't installed at the current version, then loads its scene.</summary>
        Task PlayAsync(string scenarioId);

        /// <summary>Unloads the playing scenario and returns to the list.</summary>
        Task StopAsync();

        /// <summary>Forgets the client on this device (and its downloads).</summary>
        void SignOut();
    }

    public sealed class LauncherScenario
    {
        public string Id;
        public string Title;
        public string Category;
        public string Description;
        public int DurationMinutes;
        public string Version;

        /// <summary>Version downloaded on this device, or null.</summary>
        public string InstalledVersion;

        public bool IsInstalled => InstalledVersion == Version;
        public bool HasUpdate => InstalledVersion != null && InstalledVersion != Version;
    }
}
