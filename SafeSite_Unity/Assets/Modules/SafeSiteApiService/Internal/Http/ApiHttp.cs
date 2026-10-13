using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Modules.SafeSiteApiService.Internal
{
    internal struct HttpResult
    {
        public long Status;
        public string Text;
        public string ETag;
        public long DurationMs;
    }

    /// <summary>
    /// HTTP core over UnityWebRequest: JSON in/out, non-2xx mapped to <see cref="ApiError"/>,
    /// every exchange logged (redacted). JSON calls time out after 30 s; file transfers don't
    /// time out and stream straight to/from disk.
    /// </summary>
    internal sealed class ApiHttp
    {
        public const int JsonTimeoutSeconds = 30;

        private readonly string _baseUrl;

        public event Action<ApiLogEntry> Logged;

        public ApiHttp(string baseUrl)
        {
            _baseUrl = (baseUrl ?? "").TrimEnd('/');
        }

        public string BaseUrl => _baseUrl;

        public async Task<HttpResult> SendJsonAsync(string method, string urlOrPath, string jsonBody, string bearer,
            IReadOnlyDictionary<string, string> headers, CancellationToken ct)
        {
            var url = ResolveUrl(urlOrPath);
            using var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = JsonTimeoutSeconds,
            };

            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody)) { contentType = "application/json" };
                request.SetRequestHeader("Content-Type", "application/json");
            }

            request.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrEmpty(bearer))
                request.SetRequestHeader("Authorization", "Bearer " + bearer);
            if (headers != null)
            {
                foreach (var header in headers)
                    request.SetRequestHeader(header.Key, header.Value);
            }

            var stopwatch = Stopwatch.StartNew();
            var cancelled = false;
            try
            {
                await request.SendAsync(ct);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            stopwatch.Stop();

            var status = cancelled ? 0 : request.responseCode;
            var text = cancelled ? null : request.downloadHandler?.text;
            var transportError = TransportError(request, cancelled);
            Log(method, url, status, stopwatch.ElapsedMilliseconds, jsonBody, text, transportError);

            ThrowIfFailed(request, status, text, cancelled);

            return new HttpResult
            {
                Status = status,
                Text = text,
                ETag = request.GetResponseHeader("ETag"),
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }

        public async Task DownloadFileAsync(string url, string destinationPath, Action<long> progress, CancellationToken ct)
        {
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var stopwatch = Stopwatch.StartNew();
            var cancelled = false;
            long status;
            string errorBody = null;
            string transportError;

            using (var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET))
            {
                request.downloadHandler = new DownloadHandlerFile(destinationPath) { removeFileOnAbort = true };
                request.timeout = 0;

                try
                {
                    await request.SendAsync(ct, r => progress?.Invoke((long)r.downloadedBytes));
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }

                stopwatch.Stop();
                status = cancelled ? 0 : request.responseCode;
                transportError = TransportError(request, cancelled);

                // DownloadHandlerFile writes error bodies to disk too; read them back for the error.
                if (!cancelled && (status >= 400 || status == 0))
                {
                    request.downloadHandler.Dispose();
                    errorBody = ReadAndDelete(destinationPath);
                }

                Log(UnityWebRequest.kHttpVerbGET, url, status, stopwatch.ElapsedMilliseconds, null,
                    errorBody ?? (status < 400 && !cancelled ? $"<{request.downloadedBytes} bytes → {destinationPath}>" : null),
                    transportError);

                ThrowIfFailed(request, status, errorBody, cancelled);
            }
        }

        public async Task UploadFileAsync(string url, string localPath, Action<long> progress, CancellationToken ct)
        {
            if (!File.Exists(localPath))
                throw new FileNotFoundException("Upload source not found", localPath);

            var stopwatch = Stopwatch.StartNew();
            var cancelled = false;
            var size = new FileInfo(localPath).Length;

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPUT)
            {
                uploadHandler = new UploadHandlerFile(localPath) { contentType = "application/octet-stream" },
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 0,
            };
            request.SetRequestHeader("Content-Type", "application/octet-stream");

            try
            {
                await request.SendAsync(ct, r => progress?.Invoke((long)r.uploadedBytes));
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            stopwatch.Stop();
            var status = cancelled ? 0 : request.responseCode;
            var text = cancelled ? null : request.downloadHandler?.text;
            Log(UnityWebRequest.kHttpVerbPUT, url, status, stopwatch.ElapsedMilliseconds, $"<{size} bytes from {localPath}>", text,
                TransportError(request, cancelled));

            ThrowIfFailed(request, status, text, cancelled);
        }

        private string ResolveUrl(string urlOrPath) =>
            urlOrPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || urlOrPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? urlOrPath
                : _baseUrl + urlOrPath;

        private static string TransportError(UnityWebRequest request, bool cancelled)
        {
            if (cancelled)
                return "cancelled";
            return request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.DataProcessingError
                ? request.error
                : null;
        }

        private static void ThrowIfFailed(UnityWebRequest request, long status, string body, bool cancelled)
        {
            if (cancelled)
                throw new ApiError(0, ApiErrorCodes.Cancelled, "Request cancelled");
            if (status == 0)
                throw new ApiError(0, ApiErrorCodes.NetworkError, string.IsNullOrEmpty(request.error) ? "No response" : request.error);
            if (status >= 400)
                throw ParseError(status, body, request.error);
        }

        private static string ReadAndDelete(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                var info = new FileInfo(path);
                var text = info.Length <= 64 * 1024 ? File.ReadAllText(path) : $"<{info.Length} byte error body>";
                File.Delete(path);
                return text;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Maps an error body to <see cref="ApiError"/>. Understands the API's
        /// <c>{ error: { code, message, details } }</c>, Supabase Auth's <c>{ error, error_description }</c>
        /// and <c>{ error_code, msg }</c>, and Supabase Storage's <c>{ statusCode, error, message }</c>.
        /// </summary>
        internal static ApiError ParseError(long status, string body, string transportError)
        {
            string code = null;
            string message = null;
            string detailsJson = null;
            var validation = new List<ApiErrorDetail>();
            var strings = new List<string>();

            try
            {
                if (!string.IsNullOrEmpty(body))
                {
                    using var reader = new JsonTextReader(new StringReader(body)) { DateParseHandling = DateParseHandling.None };
                    if (JToken.ReadFrom(reader) is JObject root)
                    {
                        var error = root["error"];
                        if (error is JObject apiError)
                        {
                            code = (string)apiError["code"];
                            message = (string)apiError["message"];
                            var details = apiError["details"];
                            if (details != null && details.Type != JTokenType.Null)
                            {
                                detailsJson = details.ToString(Formatting.None);
                                if (details is JArray array)
                                {
                                    foreach (var item in array)
                                    {
                                        if (item is JObject detail)
                                        {
                                            var path = detail["path"];
                                            validation.Add(new ApiErrorDetail
                                            {
                                                Path = path == null ? "" : path.Type == JTokenType.String ? (string)path : path.ToString(Formatting.None),
                                                Message = (string)detail["message"],
                                            });
                                        }
                                        else if (item.Type == JTokenType.String)
                                        {
                                            strings.Add((string)item);
                                        }
                                    }
                                }
                            }
                        }
                        else if (error != null && error.Type == JTokenType.String)
                        {
                            code = (string)error;
                            message = (string)(root["error_description"] ?? root["message"] ?? root["msg"]);
                        }
                        else if (root["msg"] != null || root["message"] != null)
                        {
                            code = (string)(root["error_code"] ?? root["code"]);
                            message = (string)(root["msg"] ?? root["message"]);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Not JSON: fall back to the status text.
            }

            if (string.IsNullOrEmpty(code))
                code = ApiErrorCodes.Http;
            if (string.IsNullOrEmpty(message))
                message = !string.IsNullOrEmpty(transportError) ? transportError : $"HTTP {status}";

            return new ApiError(status, code, message, detailsJson, validation, strings, body);
        }

        private void Log(string method, string url, long status, long durationMs, string requestBody, string responseBody, string error)
        {
            var handler = Logged;
            if (handler == null)
                return;

            handler(new ApiLogEntry
            {
                Time = DateTime.Now,
                Method = method,
                Url = LogRedactor.Url(url),
                Path = LogRedactor.DisplayPath(url, _baseUrl),
                Status = status,
                DurationMs = durationMs,
                RequestBody = LogRedactor.Body(requestBody),
                ResponseBody = LogRedactor.Body(responseBody),
                Error = error,
            });
        }
    }
}
