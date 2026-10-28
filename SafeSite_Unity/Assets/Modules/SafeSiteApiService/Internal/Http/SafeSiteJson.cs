using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Modules.SafeSiteApiService.Internal
{
    /// <summary>
    /// The API's JSON conventions: camelCase, nulls omitted. Dates are left as strings
    /// (DateParseHandling.None) so ISO 8601 values round-trip untouched.
    /// </summary>
    internal static class SafeSiteJson
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        public static readonly JsonSerializer Serializer = JsonSerializer.Create(Settings);

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Formatting.None, Settings);

        public static string SerializeIndented(object value) => JsonConvert.SerializeObject(value, Formatting.Indented, Settings);

        public static T Deserialize<T>(string json) => string.IsNullOrEmpty(json) ? default : JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
