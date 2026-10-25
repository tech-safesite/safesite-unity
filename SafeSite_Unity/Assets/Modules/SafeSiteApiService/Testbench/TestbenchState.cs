using System;
using System.Collections.Generic;
using System.Linq;
using Modules.SafeSiteApiService.External;
using UnityEngine;

namespace Modules.SafeSiteApiService.Testbench
{
    /// <summary>State shared by the testbench panels.</summary>
    public sealed class TestbenchState
    {
        public TestbenchState(ISafeSiteApiService service)
        {
            Service = service;
        }

        public ISafeSiteApiService Service { get; }

        /// <summary>Most recent enrollment code created by the Admin helper (or typed into Enroll).</summary>
        public string EnrollmentCode { get; private set; }

        /// <summary>Sessions sent by the last queue upload, for "Re-upload last batch".</summary>
        public List<SessionRecord> LastSentBatch { get; set; } = new List<SessionRecord>();

        public event Action<string> EnrollmentCodeChanged;

        /// <summary>Raised after any action that may have changed stored state, so panels can redraw.</summary>
        public event Action Changed;

        public void SetEnrollmentCode(string code, bool notify = true)
        {
            EnrollmentCode = code;
            if (notify)
                EnrollmentCodeChanged?.Invoke(code);
        }

        public void NotifyChanged() => Changed?.Invoke();

        public DeviceConfig Config => Service.Store.CachedConfig;

        public IReadOnlyList<ScenarioSummary> Scenarios => (IReadOnlyList<ScenarioSummary>)Config?.Scenarios ?? Array.Empty<ScenarioSummary>();

        public IReadOnlyList<ProjectRef> Projects => (IReadOnlyList<ProjectRef>)Config?.Projects ?? Array.Empty<ProjectRef>();

        public ScenarioSummary FindScenario(string id) => Scenarios.FirstOrDefault(s => s.Id == id);

        public string AppVersion => SafeSiteTime.IsVersion(Application.version) ? Application.version : "0.0.0";

        public static string RandomSerial() => "TEST-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();

        public static string DefaultPlatform()
        {
            if (Application.platform != RuntimePlatform.Android)
                return SafeSitePlatforms.Meta;
            var model = (SystemInfo.deviceModel ?? "").ToLowerInvariant();
            return model.Contains("pico") ? SafeSitePlatforms.Pico : SafeSitePlatforms.Meta;
        }

        public static string ScenarioState(string serverVersion, string installedVersion)
        {
            if (string.IsNullOrEmpty(installedVersion))
                return "Not installed";
            return installedVersion == serverVersion ? "Installed" : "Update available";
        }

        public static string DescribeError(Exception exception)
        {
            if (!(exception is ApiError e))
                return exception.Message;

            var text = e.Status > 0 ? $"{e.Status} {e.Code}: {e.ErrorMessage}" : $"{e.Code}: {e.ErrorMessage}";
            foreach (var detail in e.ValidationDetails)
                text += $"\n  - {detail.Path}: {detail.Message}";
            foreach (var detail in e.DetailStrings.Take(20))
                text += $"\n  - {detail}";
            return text;
        }
    }
}
