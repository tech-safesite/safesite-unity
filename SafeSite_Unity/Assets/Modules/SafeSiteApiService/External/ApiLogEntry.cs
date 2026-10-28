using System;

namespace Modules.SafeSiteApiService.External
{
    /// <summary>
    /// One HTTP exchange, raised by <see cref="ISafeSiteApiClient.Logged"/>. Secrets
    /// (Authorization, device tokens, Supabase tokens, passwords, signed URL tokens) are
    /// already redacted, and bodies are truncated to 20 KB.
    /// </summary>
    public sealed class ApiLogEntry
    {
        public DateTime Time;
        public string Method;
        public string Url;
        public string Path;
        public long Status;
        public long DurationMs;
        public string RequestBody;
        public string ResponseBody;
        public string Error;

        public bool IsError => Status == 0 || Status >= 400 || !string.IsNullOrEmpty(Error);

        public override string ToString() =>
            $"{Time:HH:mm:ss.fff} {Method} {Path} -> {(Status == 0 ? "ERR" : Status.ToString())} ({DurationMs} ms){(string.IsNullOrEmpty(Error) ? "" : " " + Error)}";
    }
}
