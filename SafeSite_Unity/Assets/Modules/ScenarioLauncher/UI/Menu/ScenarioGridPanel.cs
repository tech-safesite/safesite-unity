using System;
using System.Collections.Generic;
using System.Linq;
using Modules.ScenarioLauncher.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Modules.ScenarioLauncher.UI.Menu
{
    /// <summary>One card on the scenarios screen: a client scenario, or one built into the app.</summary>
    public sealed class ScenarioCard
    {
        public LauncherScenario Scenario;
        public string BuiltInAddress;
        public string Title;
        public Sprite Icon;

        public bool IsBuiltIn => Scenario == null;
        public IReadOnlyList<string> Languages => Scenario?.Languages ?? Array.Empty<string>();
    }

    /// <summary>
    /// The scenarios screen: the client's scenarios as cards cloned from the designed card
    /// (scenarioName, the status pill and its icon), four to a page. Cards placed in the grid by hand
    /// are hidden while it runs.
    /// </summary>
    public sealed class ScenarioGridPanel : MonoBehaviour
    {
        [Serializable]
        public sealed class Icon
        {
            [Tooltip("Matches the scenario's slug, or a word in its category, e.g. 'falling-objects' or 'height'.")]
            public string match;
            public Sprite sprite;
        }

        [Serializable]
        public sealed class BuiltInScenario
        {
            public string title;
            [Tooltip("Addressables address of a scene in a group that is included in the app build.")]
            public string address;
            public Sprite icon;
        }

        [SerializeField] private LauncherMenu menu;
        [SerializeField] private RectTransform grid;
        [Tooltip("A designed card with children 'scenarioName', a status pill with a TMP label, and 'Image' for the icon.")]
        [SerializeField] private Button cardTemplate;
        [SerializeField] private string pillName = "Available";
        [SerializeField] private TextMeshProUGUI subtitleText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button previousPage;
        [SerializeField] private Button nextPage;
        [SerializeField] private TextMeshProUGUI pageText;
        [SerializeField] private int cardsPerPage = 4;

        [Header("Look")]
        [SerializeField] private Color readyPill = new Color(0.49f, 0.75f, 0.60f);
        [SerializeField] private Color downloadPill = new Color(0.85f, 0.85f, 0.85f);
        [SerializeField] private Color updatePill = new Color(0.98f, 0.80f, 0.45f);
        [SerializeField] private Icon[] icons = Array.Empty<Icon>();
        [SerializeField] private Sprite defaultIcon;

        [Header("Development builds")]
        [Tooltip("Scenes built into the app, listed in development builds so they can be tested without the dashboard.")]
        [SerializeField] private BuiltInScenario[] builtInScenarios = Array.Empty<BuiltInScenario>();

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private int _page;

        public void PreviousPage()
        {
            _page = Mathf.Max(0, _page - 1);
            Render();
        }

        public void NextPage()
        {
            _page++;
            Render();
        }

        public void Render()
        {
            var launcher = menu.Launcher;
            foreach (var card in _spawned)
                Destroy(card);
            _spawned.Clear();
            foreach (Transform child in grid)
                child.gameObject.SetActive(false);

            var cards = new List<ScenarioCard>();
            if (launcher != null && !menu.BuiltInOnly)
                cards.AddRange(launcher.Scenarios.Select(s => new ScenarioCard { Scenario = s, Title = s.Title, Icon = IconFor(s.Slug, s.Category) }));
            if (LauncherMenu.AllowBuiltIn)
                cards.AddRange(builtInScenarios.Where(b => !string.IsNullOrEmpty(b.address))
                    .Select(b => new ScenarioCard { BuiltInAddress = b.address, Title = b.title, Icon = b.icon ? b.icon : defaultIcon }));

            var pages = Mathf.Max(1, Mathf.CeilToInt(cards.Count / (float)cardsPerPage));
            _page = Mathf.Clamp(_page, 0, pages - 1);
            foreach (var card in cards.Skip(_page * cardsPerPage).Take(cardsPerPage))
                _spawned.Add(Spawn(card, launcher));

            if (previousPage)
                previousPage.gameObject.SetActive(pages > 1 && _page > 0);
            if (nextPage)
                nextPage.gameObject.SetActive(pages > 1 && _page < pages - 1);
            if (pageText)
                pageText.text = pages > 1 ? $"{_page + 1} / {pages}" : "";

            if (subtitleText)
                subtitleText.text = menu.BuiltInOnly || launcher == null
                    ? "Built-in scenarios (development build)"
                    : $"{launcher.ClientName}  ·  {launcher.HeadsetLabel}{LicenceNote(launcher.LicenceStatus)}";
            if (messageText)
                messageText.text = launcher?.Error ?? (cards.Count == 0 ? "No scenarios yet. Ask your SafeSite contact to add some, then press Refresh." : "");
        }

        private GameObject Spawn(ScenarioCard card, IScenarioLauncherService launcher)
        {
            var button = Instantiate(cardTemplate, grid);
            button.gameObject.name = "card_" + card.Title;
            button.gameObject.SetActive(true);
            // The template's own click (wired by hand) belongs to the template.
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => menu.OpenOptions(card));

            var name = button.transform.Find("scenarioName")?.GetComponent<TextMeshProUGUI>();
            if (name)
                name.text = card.Title;
            var icon = button.transform.Find("Image")?.GetComponent<Image>();
            if (icon && card.Icon)
                icon.sprite = card.Icon;

            var pill = button.transform.Find(pillName)?.GetComponent<Image>();
            if (pill)
            {
                var (text, colour) = Status(card);
                pill.color = colour;
                var label = pill.GetComponentInChildren<TextMeshProUGUI>();
                if (label)
                    label.text = text;
            }

            return button.gameObject;
        }

        private (string, Color) Status(ScenarioCard card)
        {
            if (card.IsBuiltIn)
                return ("Built-in", downloadPill);
            if (card.Scenario.HasUpdate)
                return ("Update", updatePill);
            return card.Scenario.IsInstalled ? ("Ready", readyPill) : ("Download", downloadPill);
        }

        public Sprite IconFor(string slug, string category)
        {
            foreach (var icon in icons)
            {
                if (string.IsNullOrEmpty(icon.match) || icon.sprite == null)
                    continue;
                if (string.Equals(slug, icon.match, StringComparison.OrdinalIgnoreCase) ||
                    (category ?? "").IndexOf(icon.match, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (slug ?? "").IndexOf(icon.match, StringComparison.OrdinalIgnoreCase) >= 0)
                    return icon.sprite;
            }
            return defaultIcon;
        }

        private static string LicenceNote(string status)
        {
            switch (status)
            {
                case "expiring": return "  ·  <color=#B7791F>Licence ending soon</color>";
                case "expired": return "  ·  <color=#C53030>Licence expired</color>";
                case "suspended": return "  ·  <color=#C53030>Suspended</color>";
                default: return "";
            }
        }
    }
}
