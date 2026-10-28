using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Modules.SafeSiteApiService.External;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace Modules.SafeSiteApiService.Testbench
{
    public sealed class SessionsPanel : TestbenchPanel
    {
        private TMP_Dropdown _scenario;
        private TMP_InputField _version;
        private TMP_Dropdown _project;
        private TMP_Dropdown _language;
        private TMP_InputField _startedAt;
        private TMP_InputField _duration;
        private Toggle _completed;
        private Toggle _score;
        private TMP_InputField _correct;
        private TMP_InputField _total;
        private TextMeshProUGUI _queue;
        private TextMeshProUGUI _lastResult;
        private List<ScenarioSummary> _scenarios = new List<ScenarioSummary>();
        private List<ProjectRef> _projects = new List<ProjectRef>();

        public override string Title => "Sessions";

        protected override void Build(RectTransform content)
        {
            _scenario = Ui.Dropdown(content, "Scenario", new[] { "(get config first)" });
            _scenario.onValueChanged.AddListener(_ => OnScenarioPicked());
            _version = Ui.Input(content, "Scenario version");
            _project = Ui.Dropdown(content, "Project", new[] { "(none)" });
            _language = Ui.Dropdown(content, "Language", new[] { "English" });

            var startedRow = Ui.Row(content);
            Ui.Label(startedRow, "Started at", width: Ui.LabelWidth, color: Ui.Muted);
            _startedAt = Ui.InputField(startedRow, "");
            Ui.Button(startedRow, "Now - duration", ResetStartedAt);

            _duration = Ui.Input(content, "Duration (s)", "300", TMP_InputField.ContentType.IntegerNumber);
            _duration.onEndEdit.AddListener(_ => ResetStartedAt());
            ResetStartedAt();
            _completed = Ui.Toggle(content, "Completed", true);
            var scoreRow = Ui.Row(content);
            _score = Ui.Toggle(scoreRow, "Score", true);
            Ui.Label(scoreRow, "correct", width: 70, color: Ui.Muted);
            _correct = Ui.InputField(scoreRow, "8", TMP_InputField.ContentType.IntegerNumber, 80);
            Ui.Label(scoreRow, "total", width: 50, color: Ui.Muted);
            _total = Ui.InputField(scoreRow, "10", TMP_InputField.ContentType.IntegerNumber, 80);

            var row = Ui.Row(content);
            Ui.Button(row, "Add to queue", () => Run("Add to queue", () =>
            {
                var session = FromForm();
                if (session == null)
                    return "pick a scenario (get config first)";
                Service.EnqueueSessions(new[] { session });
                return "queued " + session.DeviceSessionId;
            }));
            Ui.Button(row, "Add 10 random", () => Run("Add 10 random", AddRandom));
            Ui.Button(row, "Add invalid", () => Run("Add invalid", AddInvalid));

            var uploadRow = Ui.Row(content);
            Ui.Button(uploadRow, "Upload queue", () => Run("Upload queue", async () =>
            {
                var result = await Service.UploadSessionQueueAsync();
                State.LastSentBatch = result.Sent.Take(500).ToList();
                ShowResult(result.Accepted, result.Duplicates, result.Rejected, result.Batches);
                return $"{result.Sent.Count} sent in {result.Batches} batch(es)";
            }), color: Ui.Accent);
            Ui.Button(uploadRow, "Re-upload last batch", () => Run("Re-upload last batch", async () =>
            {
                if (State.LastSentBatch.Count == 0)
                    return "nothing uploaded yet";
                var response = await Service.Client.Device.UploadSessionsAsync(State.LastSentBatch);
                ShowResult(response.Accepted, response.Duplicates, response.Rejected, 1);
                return $"{response.Duplicates} duplicates (expected {State.LastSentBatch.Count - response.Rejected.Count})";
            }));
            Ui.Button(uploadRow, "Clear queue", () => Run("Clear queue", () =>
            {
                Service.Store.ClearSessionQueue();
                return "cleared";
            }));

            _lastResult = Ui.Label(content, "");
            Ui.Heading(content, "Queue");
            _queue = Ui.Label(content, "", Ui.FontSize - 2);
        }

        private ScenarioSummary SelectedScenario() =>
            _scenarios.Count == 0 ? null : _scenarios[Mathf.Clamp(_scenario.value, 0, _scenarios.Count - 1)];

        private void ResetStartedAt()
        {
            var seconds = int.TryParse(_duration?.text, out var d) ? d : 300;
            _startedAt.text = SafeSiteTime.Format(DateTimeOffset.UtcNow.AddSeconds(-seconds));
        }

        private void OnScenarioPicked()
        {
            var scenario = SelectedScenario();
            if (scenario == null)
                return;
            _version.text = scenario.Version;
            var languages = scenario.Languages.Count > 0 ? scenario.Languages : new List<string> { "English" };
            Ui.SetOptions(_language, languages);
        }

        private SessionRecord FromForm()
        {
            var scenario = SelectedScenario();
            if (scenario == null)
                return null;

            return new SessionRecord
            {
                DeviceSessionId = Guid.NewGuid().ToString(),
                ScenarioId = scenario.Id,
                ScenarioVersion = _version.text.Trim(),
                ProjectId = _project.value == 0 || _projects.Count == 0 ? null : _projects[_project.value - 1].Id,
                Language = _language.options[_language.value].text,
                StartedAt = _startedAt.text.Trim(),
                DurationSeconds = int.TryParse(_duration.text, out var d) ? d : 0,
                Completed = _completed.isOn,
                Score = _score.isOn
                    ? new SessionScore { Correct = int.TryParse(_correct.text, out var c) ? c : 0, Total = int.TryParse(_total.text, out var t) ? t : 0 }
                    : null,
            };
        }

        private string AddRandom()
        {
            if (_scenarios.Count == 0)
                return "get config first";

            var sessions = new List<SessionRecord>();
            for (int i = 0; i < 10; i++)
            {
                var scenario = _scenarios[Random.Range(0, _scenarios.Count)];
                var duration = Random.Range(60, 1800);
                var total = Random.Range(5, 15);
                sessions.Add(new SessionRecord
                {
                    DeviceSessionId = Guid.NewGuid().ToString(),
                    ScenarioId = scenario.Id,
                    ScenarioVersion = scenario.Version,
                    ProjectId = _projects.Count > 0 && Random.value > 0.3f ? _projects[Random.Range(0, _projects.Count)].Id : null,
                    Language = scenario.Languages.Count > 0 ? scenario.Languages[Random.Range(0, scenario.Languages.Count)] : "English",
                    StartedAt = SafeSiteTime.Format(DateTimeOffset.UtcNow.AddSeconds(-duration - Random.Range(0, 3 * 24 * 3600))),
                    DurationSeconds = duration,
                    Completed = Random.value > 0.2f,
                    Score = Random.value > 0.3f ? new SessionScore { Correct = Random.Range(0, total + 1), Total = total } : null,
                });
            }

            Service.EnqueueSessions(sessions);
            return "queued 10";
        }

        /// <summary>One each of unknown scenario, correct &gt; total, and startedAt a day in the future.</summary>
        private string AddInvalid()
        {
            var scenario = SelectedScenario();
            if (scenario == null)
                return "get config first";

            SessionRecord Make(Action<SessionRecord> tweak)
            {
                var session = new SessionRecord
                {
                    DeviceSessionId = Guid.NewGuid().ToString(),
                    ScenarioId = scenario.Id,
                    ScenarioVersion = scenario.Version,
                    Language = scenario.Languages.FirstOrDefault() ?? "English",
                    StartedAt = SafeSiteTime.Format(DateTimeOffset.UtcNow.AddMinutes(-10)),
                    DurationSeconds = 300,
                    Completed = true,
                };
                tweak(session);
                return session;
            }

            Service.EnqueueSessions(new[]
            {
                Make(s => s.ScenarioId = Guid.NewGuid().ToString()),
                Make(s => s.Score = new SessionScore { Correct = 11, Total = 10 }),
                Make(s => s.StartedAt = SafeSiteTime.Format(DateTimeOffset.UtcNow.AddDays(1))),
            });
            return "queued 3 invalid (expect unknown_scenario, invalid_score, started_in_future)";
        }

        private void ShowResult(int accepted, int duplicates, List<RejectedSession> rejected, int batches)
        {
            var sb = new StringBuilder();
            sb.Append($"{Ui.Colored($"accepted {accepted}", Ui.Good)}  {Ui.Colored($"duplicates {duplicates}", Ui.Warn)}  ");
            sb.Append(Ui.Colored($"rejected {rejected.Count}", rejected.Count > 0 ? Ui.Bad : Ui.Muted));
            sb.Append($"  ({batches} batch{(batches == 1 ? "" : "es")})");
            foreach (var r in rejected)
                sb.Append($"\n  {r.DeviceSessionId}: {Ui.Colored(r.Reason, Ui.Bad)}");
            _lastResult.text = sb.ToString();
        }

        protected override void OnEnvironmentChanged()
        {
            State.LastSentBatch.Clear();
            _lastResult.text = "";
            base.OnEnvironmentChanged();
        }

        protected override void OnStateChanged()
        {
            var previous = SelectedScenario()?.Id;
            _scenarios = State.Scenarios.ToList();
            var index = Math.Max(0, _scenarios.FindIndex(s => s.Id == previous));
            Ui.SetOptions(_scenario, _scenarios.Count == 0 ? new List<string> { "(get config first)" } : _scenarios.Select(s => s.Title).ToList(), index);
            if (previous == null || _scenarios.All(s => s.Id != previous))
                OnScenarioPicked();

            _projects = State.Projects.ToList();
            var projectLabels = new List<string> { "(none)" };
            projectLabels.AddRange(_projects.Select(p => p.Name));
            Ui.SetOptions(_project, projectLabels, Mathf.Min(_project.value, projectLabels.Count - 1));

            var queue = Service.Store.SessionQueue;
            var sb = new StringBuilder($"{queue.Count} queued");
            foreach (var s in queue.Take(50))
            {
                var title = State.FindScenario(s.ScenarioId)?.Title ?? Ui.Colored("unknown scenario", Ui.Warn);
                sb.Append($"\n  {s.DeviceSessionId.Substring(0, Math.Min(8, s.DeviceSessionId.Length))}  {title} v{s.ScenarioVersion}  {s.StartedAt}  {s.DurationSeconds}s  {(s.Completed ? "completed" : "stopped")}");
                if (s.Score != null)
                    sb.Append($"  {s.Score.Correct}/{s.Score.Total}");
            }

            if (queue.Count > 50)
                sb.Append($"\n  ... and {queue.Count - 50} more");
            _queue.text = sb.ToString();
        }
    }
}
