using System;
using System.Collections.Generic;

namespace Modules.SafeSiteApiService.External
{
    public static class ApiErrorCodes
    {
        // Produced by the client itself, not the API.
        public const string NetworkError = "network_error";
        public const string Cancelled = "cancelled";
        public const string Http = "http_error";

        public const string InvalidRequest = "invalid_request";
        public const string InvalidToken = "invalid_token";
        public const string InvalidCode = "invalid_code";
        public const string InvalidLoginCode = "invalid_login_code";
        public const string ClientSuspended = "client_suspended";
        public const string ClientArchived = "client_archived";
        public const string SerialInUse = "serial_in_use";
        public const string HeadsetLimitReached = "headset_limit_reached";
        public const string HeadsetDeactivated = "headset_deactivated";
        public const string LicenceLocked = "licence_locked";
        public const string NotFound = "not_found";
        public const string NoContent = "no_content";
        public const string UnknownProject = "unknown_project";
        public const string UnknownScenario = "unknown_scenario";
        public const string InvalidLocation = "invalid_location";
        public const string NotAdmin = "not_admin";
        public const string MissingFiles = "missing_files";
        public const string VersionNotIncreased = "version_not_increased";
        public const string PlatformNotConfigured = "platform_not_configured";

        // Supabase Auth's own shape ({ error, error_description }).
        public const string InvalidGrant = "invalid_grant";
    }

    public struct ApiErrorDetail
    {
        public string Path;
        public string Message;
    }

    /// <summary>
    /// A non-2xx response (or a transport failure, with <see cref="Status"/> 0).
    /// Shows <c>code</c> and <c>message</c> from the API's error body.
    /// </summary>
    public sealed class ApiError : Exception
    {
        public long Status { get; }
        public string Code { get; }
        public string ErrorMessage { get; }

        /// <summary>The raw <c>details</c> JSON, or null.</summary>
        public string DetailsJson { get; }

        /// <summary>Parsed <c>details</c> when they are <c>{path, message}</c> entries (<c>invalid_request</c>).</summary>
        public IReadOnlyList<ApiErrorDetail> ValidationDetails { get; }

        /// <summary>Parsed <c>details</c> when they are a list of strings (e.g. <c>missing_files</c>).</summary>
        public IReadOnlyList<string> DetailStrings { get; }

        /// <summary>The unparsed response body, useful for storage errors that don't use the API's shape.</summary>
        public string RawBody { get; }

        public ApiError(long status, string code, string message, string detailsJson = null,
            IReadOnlyList<ApiErrorDetail> validationDetails = null, IReadOnlyList<string> detailStrings = null,
            string rawBody = null)
            : base($"{(status > 0 ? status + " " : "")}{code}: {message}")
        {
            Status = status;
            Code = code;
            ErrorMessage = message;
            DetailsJson = detailsJson;
            ValidationDetails = validationDetails ?? Array.Empty<ApiErrorDetail>();
            DetailStrings = detailStrings ?? Array.Empty<string>();
            RawBody = rawBody;
        }

        /// <summary>Network failure or 5xx: worth retrying, and queued data must be kept.</summary>
        public bool IsTransient => (Status == 0 && Code == ApiErrorCodes.NetworkError) || Status >= 500;

        public bool IsCancelled => Code == ApiErrorCodes.Cancelled;
    }
}
