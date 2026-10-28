using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Modules.ScenarioAuthoring.Editor
{
    /// <summary>
    /// <c>scenario.json</c> at the root of a scenario package: the contract with the dashboard's
    /// import. Bump <see cref="CurrentFormatVersion"/> on any breaking change.
    /// </summary>
    public sealed class ScenarioPackageManifest
    {
        public const int CurrentFormatVersion = 1;
        public const string FileName = "scenario.json";

        public int FormatVersion = CurrentFormatVersion;
        public string Slug;
        public string Title;
        public string Category;
        public string Description;
        public int DurationMinutes;
        public List<string> Languages = new List<string>();

        /// <summary>The developer's suggestion; the dashboard may pick a higher one so versions never go backwards.</summary>
        public string SuggestedVersion;

        public List<string> Platforms = new List<string>();
        public string MainScene;
        public BuiltWith BuiltWith = new BuiltWith();
        public string BuiltAt;

        /// <summary>Every content file, stored in the zip at <c>&lt;platform&gt;/&lt;path&gt;</c>.</summary>
        public List<PackageFile> Files = new List<PackageFile>();

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.Indented,
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Settings);

        public static ScenarioPackageManifest FromJson(string json) => JsonConvert.DeserializeObject<ScenarioPackageManifest>(json, Settings);
    }

    public sealed class BuiltWith
    {
        public string UnityVersion;

        /// <summary>The app version in Player Settings when this was exported; headsets need a build with the same scripts.</summary>
        public string AppVersion;
    }

    public sealed class PackageFile
    {
        public string Platform;

        /// <summary>Relative to the platform folder, as the API stores it.</summary>
        public string Path;

        public long SizeBytes;
        public string Sha256;
    }
}
