using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using TMPro;
using UnityEngine;

namespace Modules.SafeSiteApiService.Testbench
{
    /// <summary>Runs the whole headset lifecycle once, stopping at the first failure.</summary>
    public sealed class SmokeTestPanel : TestbenchPanel
    {
        private sealed class Step
        {
            public string Name;
            public string Result;
            public Color Color = Ui.Muted;
        }

        private readonly List<Step> _steps = new List<Step>();
        private TextMeshProUGUI _list;
        private bool _running;

        public override string Title => "Smoke test";

        protected override void Build(RectTransform content)
        {
            Ui.Label(content, "Ping, (admin: create code), enroll with a random serial, config, If-None-Match 304, heartbeat, " +
                              "download and load the first scenario, then valid / duplicate / invalid session uploads.", color: Ui.Muted);
            Ui.Button(Ui.Row(content), "Run all", () =>
            {
                if (!_running)
                    Run("Smoke test", () => RunAll());
            }, color: Ui.Accent);
            _list = Ui.Label(content, "");
        }

        private async Task<string> RunAll()
        {
            _running = true;
            _steps.Clear();
            Render();
            try
            {
                var device = Service.Client.Device;

                await StepAsync("Ping", async () =>
                {
                    var ping = await Service.Client.PingAsync();
                    return $"{ping.Status} in {ping.DurationMs} ms";
                });

                var auth = Service.Client.Auth;
                if (auth.IsSignedIn)
                {
                    await StepAsync("Admin: create enrollment code", async () =>
                    {
                        var clients = await Service.Client.Admin.GetClientsAsync();
                        var client = clients.FirstOrDefault(c => !c.Suspended && string.IsNullOrEmpty(c.ArchivedAt))
                                     ?? throw new InvalidOperationException("no active client");
                        var code = await Service.Client.Admin.CreateEnrollmentCodeAsync(client.Id, 1, 1);
                        State.SetEnrollmentCode(code.Code);
                        return $"{code.Code} for {client.Name}";
                    });
                }
                else
                {
                    Skip("Admin: create enrollment code", "not signed in; using the Enroll panel's code");
                }

                await StepAsync("Enroll (random serial)", async () =>
                {
                    if (string.IsNullOrWhiteSpace(State.EnrollmentCode))
                        throw new InvalidOperationException("no enrollment code: sign in (Admin helper) or type one in Enroll");
                    var response = await Service.EnrollAsync(new EnrollRequest
                    {
                        Code = State.EnrollmentCode,
                        SerialNumber = TestbenchState.RandomSerial(),
                        Platform = TestbenchState.DefaultPlatform(),
                        Model = SystemInfo.deviceModel,
                        AppVersion = State.AppVersion,
                    });
                    return $"{response.Label} for {response.Client?.Name}";
                });

                DeviceConfig config = null;
                await StepAsync("Get config", async () =>
                {
                    config = (await Service.SyncConfigAsync(false)).Config;
                    return $"licence {config.Licence?.Status}, {config.Scenarios.Count} scenarios";
                });

                await StepAsync("Get config (If-None-Match), expect 304", async () =>
                {
                    var result = await Service.SyncConfigAsync(true);
                    if (!result.NotModified)
                        throw new InvalidOperationException("expected 304, got 200");
                    return "304 Not Modified";
                });

                await StepAsync("Heartbeat", async () =>
                {
                    var response = await Service.SendHeartbeatAsync(new HeartbeatRequest
                    {
                        AppVersion = State.AppVersion,
                        InstalledScenarios = Service.Store.Installed.ToList(),
                        Location = new DeviceLocation { Lat = 53.28695, Lng = -6.24362, AccuracyM = 25, RecordedAt = SafeSiteTime.Now() },
                    });
                    return "configVersion " + response.ConfigVersion;
                });

                var scenario = config.Scenarios.FirstOrDefault()
                               ?? throw Fail("First scenario", "config has no scenarios (no content for this platform, or licence locked)");

                await StepAsync($"Download '{scenario.Title}'", async () =>
                {
                    var result = await Service.DownloadScenarioAsync(scenario.Id);
                    return $"v{result.Version}: {result.Files} files, {Ui.Bytes(result.Bytes)}";
                });

                await StepAsync("Load catalog", async () =>
                {
                    var keys = await Service.Content.LoadCatalogAsync(Service.Store.GetContentDirectory(scenario.Id, scenario.Version));
                    return $"{keys.Count} keys";
                });

                var valid = NewSession(scenario);
                await StepAsync("Upload one valid session, expect accepted 1", async () =>
                {
                    var response = await device.UploadSessionsAsync(new[] { valid });
                    Expect(response.Accepted == 1, $"accepted {response.Accepted}, duplicates {response.Duplicates}, rejected {response.Rejected.Count}");
                    return "accepted 1";
                });

                await StepAsync("Re-upload it, expect duplicates 1", async () =>
                {
                    var response = await device.UploadSessionsAsync(new[] { valid });
                    Expect(response.Duplicates == 1, $"accepted {response.Accepted}, duplicates {response.Duplicates}");
                    return "duplicates 1";
                });

                await StepAsync("Upload an invalid one, expect rejected 1", async () =>
                {
                    var invalid = NewSession(scenario);
                    invalid.Score = new SessionScore { Correct = 11, Total = 10 };
                    var response = await device.UploadSessionsAsync(new[] { invalid });
                    Expect(response.Rejected.Count == 1, $"accepted {response.Accepted}, rejected {response.Rejected.Count}");
                    return "rejected: " + response.Rejected[0].Reason;
                });

                return "all steps passed";
            }
            catch (SmokeFailure failure)
            {
                throw new InvalidOperationException(failure.Message);
            }
            finally
            {
                _running = false;
            }
        }

        private static SessionRecord NewSession(ScenarioSummary scenario) => new SessionRecord
        {
            DeviceSessionId = Guid.NewGuid().ToString(),
            ScenarioId = scenario.Id,
            ScenarioVersion = scenario.Version,
            Language = scenario.Languages.FirstOrDefault() ?? "English",
            StartedAt = SafeSiteTime.Format(DateTimeOffset.UtcNow.AddMinutes(-10)),
            DurationSeconds = 300,
            Completed = true,
            Score = new SessionScore { Correct = 8, Total = 10 },
        };

        private static void Expect(bool condition, string actual)
        {
            if (!condition)
                throw new InvalidOperationException("unexpected result: " + actual);
        }

        private sealed class SmokeFailure : Exception
        {
            public SmokeFailure(string message) : base(message) { }
        }

        private async Task StepAsync(string name, Func<Task<string>> action)
        {
            var step = new Step { Name = name, Result = "running..." };
            _steps.Add(step);
            Render();
            try
            {
                step.Result = await action();
                step.Color = Ui.Good;
                Render();
            }
            catch (Exception e)
            {
                step.Result = TestbenchState.DescribeError(e);
                step.Color = Ui.Bad;
                Render();
                throw new SmokeFailure($"'{name}' failed: {step.Result}");
            }
        }

        private void Skip(string name, string reason)
        {
            _steps.Add(new Step { Name = name, Result = "skipped: " + reason, Color = Ui.Warn });
            Render();
        }

        private SmokeFailure Fail(string name, string reason)
        {
            _steps.Add(new Step { Name = name, Result = reason, Color = Ui.Bad });
            Render();
            return new SmokeFailure($"'{name}' failed: {reason}");
        }

        private void Render()
        {
            var sb = new StringBuilder();
            foreach (var step in _steps)
            {
                var mark = step.Color == Ui.Good ? "PASS" : step.Color == Ui.Bad ? "FAIL" : step.Color == Ui.Warn ? "SKIP" : "....";
                sb.AppendLine($"{Ui.Colored($"[{mark}]", step.Color)} {step.Name}  {Ui.Colored(step.Result, Ui.Muted)}");
            }

            _list.text = sb.ToString().TrimEnd();
        }
    }
}
