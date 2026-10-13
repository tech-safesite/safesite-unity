using System;
using System.Linq;
using System.Text;
using Modules.SafeSiteApiService.External;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

namespace Modules.SafeSiteApiService.Testbench
{
    public sealed class ConfigPanel : TestbenchPanel
    {
        private TextMeshProUGUI _summary;
        private TextMeshProUGUI _scenarios;
        private TextMeshProUGUI _licence;
        private Jwks _jwks;
        private DateTimeOffset? _licenceExpiry;
        private string _licenceText = "";
        private float _nextTick;
        private double? _skewSeconds;

        public override string Title => "Config & licence";

        protected override void Build(RectTransform content)
        {
            var row = Ui.Row(content);
            Ui.Button(row, "Get config", () => Run("Get config", async () =>
            {
                var result = await Service.SyncConfigAsync(false);
                MeasureSkew(result.Config);
                return $"configVersion {result.Config.ConfigVersion}, {result.Config.Scenarios.Count} scenarios";
            }), color: Ui.Accent);
            Ui.Button(row, "Get config (If-None-Match)", () => Run("Get config (If-None-Match)", async () =>
            {
                var result = await Service.SyncConfigAsync(true);
                if (result.NotModified)
                    return "304 Not Modified";
                MeasureSkew(result.Config);
                return $"200: changed to {result.Config.ConfigVersion}";
            }));
            Ui.Button(row, "Fetch JWKS", () => Run("Fetch JWKS", async () =>
            {
                _jwks = await Service.Client.Device.GetJwksAsync();
                return string.Join(", ", _jwks.Keys.Select(k => $"{k.Kid} ({k.Kty} {k.Crv})"));
            }));
            Ui.Button(row, "Decode licence", () => Run("Decode licence", DecodeLicence));

            _summary = Ui.Label(content, "");
            Ui.Heading(content, "Scenarios");
            _scenarios = Ui.Label(content, "");
            Ui.Heading(content, "Licence token");
            _licence = Ui.Label(content, "", Ui.FontSize - 2);
        }

        private void MeasureSkew(DeviceConfig config)
        {
            if (config != null && SafeSiteTime.TryParse(config.ServerTime, out var serverTime))
                _skewSeconds = (DateTimeOffset.UtcNow - serverTime).TotalSeconds;
        }

        private string DecodeLicence()
        {
            var token = Service.Store.CachedConfig?.Licence?.Token;
            if (string.IsNullOrEmpty(token))
                return "no licence token: get config first";

            var parts = token.Split('.');
            if (parts.Length != 3)
                return "not a JWT";

            var header = ParseSegment(parts[0]);
            var claims = ParseSegment(parts[1]);
            _licenceExpiry = claims["exp"]?.Type == JTokenType.Integer ? DateTimeOffset.FromUnixTimeSeconds((long)claims["exp"]) : (DateTimeOffset?)null;

            var kid = (string)header["kid"];
            var signature = _jwks == null
                ? "Signature not verified (fetch JWKS; ES256 verification needs BouncyCastle, which isn't in the project)"
                : _jwks.Keys.Any(k => k.Kid == kid)
                    ? $"Signature not verified (key {kid} is in the JWKS; ES256 verification needs BouncyCastle)"
                    : Ui.Colored($"Signing key {kid} is NOT in the fetched JWKS", Ui.Bad);

            _licenceText = $"Header\n{header.ToString(Formatting.Indented)}\nClaims\n{claims.ToString(Formatting.Indented)}\n{signature}";
            RenderLicence();
            return $"sts {(string)claims["sts"]}, {((JArray)claims["scn"])?.Count ?? 0} scenarios unlocked";
        }

        private static JObject ParseSegment(string segment)
        {
            var s = segment.Replace('-', '+').Replace('_', '/');
            s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
            using var reader = new JsonTextReader(new System.IO.StringReader(Encoding.UTF8.GetString(Convert.FromBase64String(s))))
            {
                DateParseHandling = DateParseHandling.None,
            };
            return JObject.Load(reader);
        }

        private void Update()
        {
            if (_licenceExpiry == null || Time.unscaledTime < _nextTick)
                return;
            _nextTick = Time.unscaledTime + 1f;
            RenderLicence();
        }

        private void RenderLicence()
        {
            var countdown = "";
            if (_licenceExpiry.HasValue)
            {
                var left = _licenceExpiry.Value - DateTimeOffset.UtcNow;
                countdown = left > TimeSpan.Zero
                    ? Ui.Colored($"exp in {(int)left.TotalDays}d {left.Hours:00}:{left.Minutes:00}:{left.Seconds:00} ({_licenceExpiry.Value.ToLocalTime():yyyy-MM-dd HH:mm})", Ui.Good)
                    : Ui.Colored($"EXPIRED {_licenceExpiry.Value.ToLocalTime():yyyy-MM-dd HH:mm}", Ui.Bad);
            }

            _licence.text = countdown + "\n" + _licenceText;
        }

        protected override void OnEnvironmentChanged()
        {
            _jwks = null;
            _licenceExpiry = null;
            _licenceText = "";
            _licence.text = "";
            _skewSeconds = null;
            base.OnEnvironmentChanged();
        }

        protected override void OnStateChanged()
        {
            var config = Service.Store.CachedConfig;
            if (config == null)
            {
                _summary.text = Ui.Colored("No cached config. Enroll, then Get config.", Ui.Muted);
                _scenarios.text = "";
                return;
            }

            var skew = "";
            if (_skewSeconds.HasValue)
            {
                var seconds = _skewSeconds.Value;
                var color = Math.Abs(seconds) > 60 ? Ui.Warn : Ui.Good;
                skew = "  clock skew " + Ui.Colored($"{(seconds >= 0 ? "+" : "")}{seconds:0.0}s", color) + " (local minus server, incl. latency)";
            }

            var licence = config.Licence;
            var statusColor = licence?.Status == LicenceStatuses.Active ? Ui.Good : licence?.Status == LicenceStatuses.Expiring ? Ui.Warn : Ui.Bad;
            var sb = new StringBuilder();
            sb.AppendLine($"configVersion {config.ConfigVersion}   serverTime {config.ServerTime}{skew}");
            sb.AppendLine($"Headset {config.Headset?.Label} ({config.Headset?.Platform})   client {config.Client?.Name} [{config.Client?.Tier}]");
            if (licence != null)
                sb.AppendLine($"Licence {Ui.Colored((licence.Status ?? "?").ToUpperInvariant(), statusColor)}  {licence.StartsOn} to {licence.EndsOn}  warn at {string.Join(", ", licence.WarnAtDaysBeforeEnd)} days");
            sb.Append($"Projects: {(config.Projects.Count == 0 ? "(none)" : string.Join(", ", config.Projects.Select(p => p.Name)))}");
            _summary.text = sb.ToString();

            if (config.Scenarios.Count == 0)
            {
                _scenarios.text = Ui.Colored("(none: no content for this platform, or the licence is expired/suspended)", Ui.Muted);
                return;
            }

            var table = new StringBuilder();
            table.AppendLine(Ui.Colored("Title | Category | Server | Installed | State", Ui.Muted));
            foreach (var scenario in config.Scenarios)
            {
                var installed = Service.Store.GetInstalledVersion(scenario.Id);
                var state = TestbenchState.ScenarioState(scenario.Version, installed);
                var color = state == "Installed" ? Ui.Good : state == "Update available" ? Ui.Warn : Ui.Muted;
                table.AppendLine($"{scenario.Title} | {scenario.Category} | {scenario.Version} | {installed ?? "-"} | {Ui.Colored(state, color)}");
            }

            _scenarios.text = table.ToString().TrimEnd();
        }
    }
}
