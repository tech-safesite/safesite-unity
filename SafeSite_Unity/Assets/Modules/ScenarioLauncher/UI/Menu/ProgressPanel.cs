using Modules.ScenarioLauncher.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>Download and loading progress for the scenario being started.</summary>
    public sealed class ProgressPanel : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI titleText;
        [Tooltip("A Filled image.")]
        [SerializeField] private Image fill;
        [SerializeField] private TextMeshProUGUI detailText;

        public void Render(IScenarioLauncherService launcher)
        {
            var name = launcher.Current?.Title ?? "scenario";
            if (titleText)
                titleText.text = (launcher.State == LauncherState.Loading ? "Starting " : "Downloading ") + name;
            if (fill)
                fill.fillAmount = launcher.Progress;
            if (detailText)
                detailText.text = launcher.ProgressText ?? "";
        }
    }
}
