using UnityEngine;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>Debug switches an admin sets on the admin screen, kept on this headset.</summary>
    public static class AdminSettings
    {
        private const string BuiltInKey = "SafeSite.Admin.BuiltInScenarios";

        /// <summary>List the scenarios built into the app in non-development builds too.</summary>
        public static bool BuiltInScenarios
        {
            get => PlayerPrefs.GetInt(BuiltInKey, 0) == 1;
            set
            {
                PlayerPrefs.SetInt(BuiltInKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }
    }
}
