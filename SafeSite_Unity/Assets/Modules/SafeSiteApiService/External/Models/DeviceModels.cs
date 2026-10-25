using System;
using System.Collections.Generic;

namespace Modules.SafeSiteApiService.External
{
    // DTOs for the device API (brief section 3.1). Field names are PascalCase here and are
    // mapped to the API's camelCase by the Internal serializer. Timestamps stay as the ISO 8601
    // strings the API sends; use SafeSiteTime to parse or format them.

    public static class SafeSitePlatforms
    {
        public const string Meta = "meta";
        public const string Pico = "pico";
    }

    [Serializable]
    public sealed class EnrollRequest
    {
        public string Code;
        public string SerialNumber;
        public string Platform;
        public string Model;
        public string AppVersion;
    }

    [Serializable]
    public sealed class ClientRef
    {
        public string Id;
        public string Name;
    }

    [Serializable]
    public sealed class EnrollResponse
    {
        public string HeadsetId;
        public string Label;
        public string DeviceToken;
        public ClientRef Client;
    }

    [Serializable]
    public sealed class HeadsetInfo
    {
        public string Id;
        public string Label;
        public string Platform;
    }

    [Serializable]
    public sealed class ConfigClient
    {
        public string Id;
        public string Name;
        public string Tier;
        public string LogoUrl;
    }

    public static class LicenceStatuses
    {
        public const string Active = "active";
        public const string Expiring = "expiring";
        public const string Expired = "expired";
        public const string Suspended = "suspended";
    }

    [Serializable]
    public sealed class LicenceInfo
    {
        public string Status;
        public string StartsOn;
        public string EndsOn;
        public List<int> WarnAtDaysBeforeEnd = new List<int>();
        public string Token;
    }

    [Serializable]
    public sealed class ScenarioSummary
    {
        public string Id;
        public string Slug;
        public string Title;
        public string Category;
        public string Description;
        public int DurationMinutes;
        public string Version;
        public List<string> Languages = new List<string>();
    }

    [Serializable]
    public sealed class ProjectRef
    {
        public string Id;
        public string Name;
    }

    [Serializable]
    public sealed class DeviceConfig
    {
        public string ConfigVersion;
        public string ServerTime;
        public HeadsetInfo Headset;
        public ConfigClient Client;
        public LicenceInfo Licence;
        public List<ScenarioSummary> Scenarios = new List<ScenarioSummary>();
        public List<ProjectRef> Projects = new List<ProjectRef>();
    }

    /// <summary>Result of GET /v1/device/config. <see cref="Config"/> is null when <see cref="NotModified"/>.</summary>
    public sealed class ConfigResult
    {
        public bool NotModified;
        public DeviceConfig Config;
        public string ETag;
    }

    [Serializable]
    public sealed class Jwk
    {
        public string Kty;
        public string Crv;
        public string X;
        public string Y;
        public string Kid;
        public string Alg;
        public string Use;
    }

    [Serializable]
    public sealed class Jwks
    {
        public List<Jwk> Keys = new List<Jwk>();
    }

    [Serializable]
    public sealed class ContentFile
    {
        public string Path;
        public string Url;
        public long? SizeBytes;
    }

    [Serializable]
    public sealed class ContentManifest
    {
        public string ScenarioId;
        public string Version;
        public string Platform;
        public string ExpiresAt;
        public List<ContentFile> Files = new List<ContentFile>();
    }

    [Serializable]
    public sealed class InstalledScenario
    {
        public string ScenarioId;
        public string Version;

        public InstalledScenario() { }

        public InstalledScenario(string scenarioId, string version)
        {
            ScenarioId = scenarioId;
            Version = version;
        }
    }

    [Serializable]
    public sealed class DeviceLocation
    {
        public double Lat;
        public double Lng;
        public double? AccuracyM;
        public string RecordedAt;
    }

    /// <summary>How the heartbeat treats <c>projectId</c>: omit it, send null, or send a value.</summary>
    public enum HeartbeatProjectMode
    {
        Unchanged,
        Clear,
        Set,
    }

    public sealed class HeartbeatRequest
    {
        public string AppVersion;
        public HeartbeatProjectMode ProjectMode = HeartbeatProjectMode.Unchanged;
        public string ProjectId;

        /// <summary>Null omits the field; a list (even empty) replaces the stored list.</summary>
        public List<InstalledScenario> InstalledScenarios;

        /// <summary>Null omits the field.</summary>
        public DeviceLocation Location;
    }

    [Serializable]
    public sealed class HeartbeatResponse
    {
        public string ServerTime;
        public string ConfigVersion;
    }

    [Serializable]
    public sealed class SessionScore
    {
        public int Correct;
        public int Total;
    }

    [Serializable]
    public sealed class SessionRecord
    {
        public string DeviceSessionId;
        public string ScenarioId;
        public string ScenarioVersion;
        public string ProjectId;
        public string Language;
        public string StartedAt;
        public int DurationSeconds;
        public bool Completed;
        public SessionScore Score;
    }

    public static class SessionRejectReasons
    {
        public const string UnknownScenario = "unknown_scenario";
        public const string UnknownProject = "unknown_project";
        public const string StartedInFuture = "started_in_future";
        public const string InvalidScore = "invalid_score";
    }

    [Serializable]
    public sealed class RejectedSession
    {
        public string DeviceSessionId;
        public string Reason;
    }

    [Serializable]
    public sealed class SessionUploadResponse
    {
        public int Accepted;
        public int Duplicates;
        public List<RejectedSession> Rejected = new List<RejectedSession>();
    }

    /// <summary>Totals across every batch sent by <see cref="ISafeSiteApiService.UploadSessionQueueAsync"/>.</summary>
    public sealed class QueueUploadResult
    {
        public int Accepted;
        public int Duplicates;
        public List<RejectedSession> Rejected = new List<RejectedSession>();
        public List<SessionRecord> Sent = new List<SessionRecord>();
        public int Batches;
    }

    public struct DownloadProgress
    {
        public int FileIndex;
        public int FileCount;
        public string CurrentFile;
        public long BytesDone;
        public long BytesTotal;
        public double BytesPerSecond;

        public float Fraction => BytesTotal > 0 ? Math.Min(1f, (float)BytesDone / BytesTotal) : 0f;
    }

    public sealed class ScenarioInstallResult
    {
        public string ScenarioId;
        public string Version;
        public string Directory;
        public int Files;
        public long Bytes;
        public double Seconds;
    }

    /// <summary>Result of the header's Ping (<c>GET /openapi.json</c>).</summary>
    public struct ApiPingResult
    {
        public long Status;
        public long DurationMs;
    }
}
