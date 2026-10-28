using System;
using System.Security.Cryptography;
using System.Text;

namespace Modules.SafeSiteApiService.Internal
{
    /// <summary>
    /// Mints the dev-only HS256 admin token the Local API accepts, matching the web repo's
    /// dashboard/src/lib/devAuth.ts. Hosted environments reject it.
    /// </summary>
    internal static class DevJwt
    {
        private const string Secret = "safesite-local-dev-only-secret-do-not-use-in-production";
        private const string Subject = "aaaaaaaa-0000-0000-0000-000000000001";
        private const string Email = "test@safesite.com";

        public static string Mint()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            const string header = "{\"alg\":\"HS256\",\"typ\":\"JWT\"}";
            var payload = $"{{\"sub\":\"{Subject}\",\"role\":\"authenticated\",\"email\":\"{Email}\",\"iat\":{now},\"exp\":{now + 86400}}}";

            var signingInput = Base64Url.Encode(Encoding.UTF8.GetBytes(header)) + "." + Base64Url.Encode(Encoding.UTF8.GetBytes(payload));
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
            var signature = hmac.ComputeHash(Encoding.ASCII.GetBytes(signingInput));
            return signingInput + "." + Base64Url.Encode(signature);
        }
    }

    internal static class Base64Url
    {
        public static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public static byte[] Decode(string value)
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }

            return Convert.FromBase64String(s);
        }
    }
}
