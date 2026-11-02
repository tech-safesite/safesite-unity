using TMPro;
using UnityEngine;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>Who this headset belongs to, its licence and app version, with Refresh and Sign out.</summary>
    public sealed class HeadsetPanel : MonoBehaviour
    {
        [SerializeField] private LauncherMenu menu;
        [SerializeField] private TextMeshProUGUI detailsText;

        public void Render()
        {
            var launcher = menu.Launcher;
            if (!detailsText || launcher == null)
                return;
            var installed = 0;
            foreach (var scenario in launcher.Scenarios)
                if (scenario.InstalledVersion != null)
                    installed++;

            detailsText.text =
                $"<b>Company</b>\n{launcher.ClientName ?? "-"}\n\n" +
                $"<b>Headset</b>\n{launcher.HeadsetLabel ?? "-"}\n\n" +
                $"<b>Licence</b>\n{Licence(launcher.LicenceStatus)}\n\n" +
                $"<b>Scenarios</b>\n{launcher.Scenarios.Count} available, {installed} downloaded\n\n" +
                $"<b>App version</b>\n{Application.version}";
        }

        public void SignOut()
        {
            menu.SignOut();
        }

        private static string Licence(string status)
        {
            switch (status)
            {
                case "active": return "Active";
                case "expiring": return "Ending soon";
                case "expired": return "Expired: downloaded scenarios still play";
                case "suspended": return "Suspended";
                default: return "-";
            }
        }
    }
}
