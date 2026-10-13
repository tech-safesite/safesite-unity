using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Modules.SafeSiteApiService.External
{
    /// <summary>Helpers for the API's string formats (ISO 8601 timestamps, x.y.z versions).</summary>
    public static class SafeSiteTime
    {
        private static readonly Regex VersionPattern = new Regex(@"^\d+\.\d+\.\d+$");

        public static string Format(DateTimeOffset value) =>
            value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        public static string Now() => Format(DateTimeOffset.UtcNow);

        public static bool TryParse(string value, out DateTimeOffset result) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);

        public static bool IsVersion(string value) => !string.IsNullOrEmpty(value) && VersionPattern.IsMatch(value);

        /// <summary>Compares x.y.z versions numerically. Invalid versions sort first.</summary>
        public static int CompareVersions(string a, string b)
        {
            var pa = ParseVersion(a);
            var pb = ParseVersion(b);
            for (int i = 0; i < 3; i++)
            {
                int c = pa[i].CompareTo(pb[i]);
                if (c != 0)
                    return c;
            }

            return 0;
        }

        public static string BumpPatch(string version)
        {
            if (!IsVersion(version))
                return "1.0.0";
            var p = ParseVersion(version);
            return $"{p[0]}.{p[1]}.{p[2] + 1}";
        }

        private static long[] ParseVersion(string value)
        {
            var result = new long[] { -1, -1, -1 };
            if (!IsVersion(value))
                return result;
            var parts = value.Split('.');
            for (int i = 0; i < 3; i++)
                result[i] = long.Parse(parts[i], CultureInfo.InvariantCulture);
            return result;
        }
    }
}
