using System.Linq;
using Modules.SessionAnalyticsService.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>The scenario's own screen: its title and details, its languages, and Start.</summary>
    public sealed class ScenarioOptionsPanel : MonoBehaviour
    {
        [SerializeField] private LauncherMenu menu;
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI detailsText;
        [SerializeField] private TMP_Dropdown languageDropdown;
        [SerializeField] private Button startButton;
        [SerializeField] private TextMeshProUGUI startLabel;

        private ScenarioCard _card;

        public ScenarioCard Card => _card;

        public void Open(ScenarioCard card)
        {
            _card = card;
            if (languageDropdown && card.Languages.Count > 0)
            {
                languageDropdown.ClearOptions();
                languageDropdown.AddOptions(card.Languages.ToList());
                languageDropdown.value = 0;
            }
            Render();
        }

        public void Render()
        {
            if (_card == null)
                return;

            // The list may have been refreshed since the card was made.
            var scenario = _card.Scenario == null ? null : menu.Launcher?.Scenarios.FirstOrDefault(s => s.Id == _card.Scenario.Id) ?? _card.Scenario;
            if (titleText)
                titleText.text = _card.Title;
            if (detailsText)
                detailsText.text = scenario == null
                    ? "Built into this app"
                    : $"{scenario.DurationMinutes} min  ·  {(scenario.IsInstalled ? "Ready to play" : scenario.HasUpdate ? "Update available" : "Needs download")}";
            if (startLabel)
                startLabel.text = scenario == null || scenario.IsInstalled ? "Start Training" : scenario.HasUpdate ? "Update and Start" : "Download and Start";
            if (startButton)
                startButton.interactable = scenario == null || menu.Launcher?.State == External.LauncherState.Browsing;
        }

        public void StartScenario()
        {
            if (_card == null)
                return;
            // Recorded with the session for the dashboard.
            if (languageDropdown && _card.Languages.Count > 0 && languageDropdown.value < languageDropdown.options.Count)
                SessionAnalyticsServiceLocator.FindInScene()?.SetLanguage(languageDropdown.options[languageDropdown.value].text);
            menu.Play(_card);
        }

        public void Back() => menu.ShowScenarios();
    }
}
