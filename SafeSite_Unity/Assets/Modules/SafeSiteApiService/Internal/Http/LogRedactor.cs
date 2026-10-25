using System;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Modules.SafeSiteApiService.Internal
{
    /// <summary>Redacts secrets and truncates bodies before they reach <c>ApiLogEntry</c>.</summary>
    internal static class LogRedactor
    {
        public const int MaxBodyChars = 20 * 1024;
        private const int KeepChars = 8;

        private static readonly Regex TokenFields = new Regex(
            "\"(deviceToken|access_token|refresh_token|provider_token|provider_refresh_token)\"(\\s*:\\s*)\"([^\"]*)\"",
            RegexOptions.Compiled);

        private static readonly Regex PasswordField = new Regex("\"(password)\"(\\s*:\\s*)\"([^\"]*)\"", RegexOptions.Compiled);

        private static readonly Regex UrlToken = new Regex("([?&]token=)([^&]*)", RegexOptions.Compiled);

        public static string Mask(string secret)
        {
            if (string.IsNullOrEmpty(secret))
                return secret;
            return secret.Length <= KeepChars ? secret : secret.Substring(0, KeepChars) + "…";
        }

        public static string Body(string body)
        {
            if (string.IsNullOrEmpty(body))
                return body;

            var pretty = body;
            if (body.Length <= MaxBodyChars * 4)
            {
                try
                {
                    using var reader = new JsonTextReader(new System.IO.StringReader(body)) { DateParseHandling = DateParseHandling.None };
                    pretty = JToken.ReadFrom(reader).ToString(Formatting.Indented);
                }
                catch (JsonException)
                {
                    // Not JSON; log as-is.
                }
            }

            pretty = TokenFields.Replace(pretty, m => $"\"{m.Groups[1].Value}\"{m.Groups[2].Value}\"{Mask(m.Groups[3].Value)}\"");
            pretty = PasswordField.Replace(pretty, m => $"\"{m.Groups[1].Value}\"{m.Groups[2].Value}\"***\"");

            if (pretty.Length > MaxBodyChars)
                pretty = pretty.Substring(0, MaxBodyChars) + $"\n… truncated ({pretty.Length - MaxBodyChars} more chars)";
            return pretty;
        }

        public static string Url(string url) => string.IsNullOrEmpty(url) ? url : UrlToken.Replace(url, m => m.Groups[1].Value + Mask(m.Groups[2].Value));

        /// <summary>Path relative to the API base when it is an API call; otherwise the redacted URL path.</summary>
        public static string DisplayPath(string url, string apiBaseUrl)
        {
            if (!string.IsNullOrEmpty(apiBaseUrl) && url.StartsWith(apiBaseUrl, StringComparison.OrdinalIgnoreCase))
                return Url(url.Substring(apiBaseUrl.Length));

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return Url(uri.Host + uri.PathAndQuery);
            return Url(url);
        }
    }
}
