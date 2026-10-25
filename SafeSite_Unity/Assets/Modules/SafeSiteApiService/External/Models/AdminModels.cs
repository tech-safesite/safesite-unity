using System;
using System.Collections.Generic;

namespace Modules.SafeSiteApiService.External
{
    // DTOs for the admin endpoints used by the testbench and the Content Uploader (brief section 3.2).

    [Serializable]
    public sealed class AdminClient
    {
        public string Id;
        public string Name;
        public string Tier;
        public int HeadsetLimit;
        public int ActiveHeadsets;
        public string LicenceStatus;
        public bool Suspended;
        public string ArchivedAt;
    }

    [Serializable]
    public sealed class NewEnrollmentCode
    {
        public string Id;
        public string Code;
        public int MaxUses;
        public int Uses;
        public string ExpiresAt;
        public string Status;
    }

    [Serializable]
    public sealed class PlatformContentSummary
    {
        public int Files;
        public long SizeBytes;
    }

    public static class ScenarioStatuses
    {
        public const string Draft = "draft";
        public const string Published = "published";
        public const string Retired = "retired";
    }

    [Serializable]
    public sealed class AdminScenario
    {
        public string Id;
        public string Slug;
        public string Title;
        public string Category;
        public string Description;
        public int DurationMinutes;
        public string Version;
        public List<string> Languages = new List<string>();
        public string MetaContentPath;
        public string PicoContentPath;
        public PlatformContentSummary MetaContent;
        public PlatformContentSummary PicoContent;
        public string Status;

        public string ContentPath(string platform) => platform == SafeSitePlatforms.Pico ? PicoContentPath : MetaContentPath;

        public PlatformContentSummary Content(string platform) => platform == SafeSitePlatforms.Pico ? PicoContent : MetaContent;
    }

    [Serializable]
    public sealed class RemoteFile
    {
        public string Path;
        public long? SizeBytes;
    }

    [Serializable]
    public sealed class BuildFiles
    {
        public string Folder;
        public List<RemoteFile> Files = new List<RemoteFile>();
    }

    [Serializable]
    public sealed class UploadTarget
    {
        public string Path;
        public string Url;
    }

    [Serializable]
    public sealed class UploadUrls
    {
        public string Folder;
        public string ExpiresAt;
        public List<UploadTarget> Uploads = new List<UploadTarget>();
    }

    [Serializable]
    public sealed class CommitBuildRequest
    {
        public List<string> Files = new List<string>();

        /// <summary>Null leaves the scenario's version unchanged.</summary>
        public string Version;

        public bool RemoveStale = true;
    }

    [Serializable]
    public sealed class CommitBuildResult
    {
        public List<string> RemovedFiles = new List<string>();
        public AdminScenario Scenario;
    }
}
