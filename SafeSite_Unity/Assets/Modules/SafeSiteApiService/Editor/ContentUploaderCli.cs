using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modules.SafeSiteApiService.External;
using UnityEditor;

namespace Modules.SafeSiteApiService.Editor
{
    /// <summary>
    /// Batch-mode entry point (brief 6.4):
    /// <code>
    /// Unity -batchmode -quit -projectPath . -executeMethod Modules.SafeSiteApiService.Editor.ContentUploaderCli.Run \
    ///   -safesiteEnv Demo -scenario ladder-access-setup -platforms meta,pico -version 1.4.0
    /// </code>
    /// Optional: -dryRun, -uploadOnly, -noClean, -keepOldFiles, -noSkip, -confirmProduction.
    /// Credentials only from env vars SAFESITE_ADMIN_EMAIL / SAFESITE_ADMIN_PASSWORD or SAFESITE_ACCESS_TOKEN
    /// (Local falls back to the dev token). Exits non-zero on any failure.
    /// </summary>
    public static class ContentUploaderCli
    {
        private const string LogPrefix = "[SafeSiteContentUploader]";

        public static void Run()
        {
            int exitCode;
            var previous = SynchronizationContext.Current;
            var pump = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(pump);
            try
            {
                // The player loop doesn't tick while executeMethod runs, so pump continuations here.
                var task = RunAsync(Environment.GetCommandLineArgs());
                while (!task.IsCompleted)
                {
                    pump.Drain();
                    Thread.Sleep(5);
                }

                pump.Drain();
                exitCode = task.IsFaulted ? Fail(task.Exception?.GetBaseException().Message) : task.Result;
            }
            catch (Exception e)
            {
                exitCode = Fail(e.Message);
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }

            Console.WriteLine($"{LogPrefix} Exit code {exitCode}");
            EditorApplication.Exit(exitCode);
        }

        private static async Task<int> RunAsync(string[] args)
        {
            var environmentName = Arg(args, "-safesiteEnv") ?? SafeSiteEnvironment.DemoName;
            var scenarioKey = Arg(args, "-scenario");
            if (string.IsNullOrEmpty(scenarioKey))
                return Fail("-scenario <slug or id> is required.");

            var environment = SafeSiteEnvironments.Load().Find(environmentName);
            if (environment == null)
                return Fail($"Unknown environment '{environmentName}'.");
            if (environment.IsProduction && !Flag(args, "-confirmProduction"))
                return Fail("Refusing to upload to Production without -confirmProduction.");

            var client = SafeSiteApiClientFactory.Create(environment);
            client.Logged += entry => Console.WriteLine($"{LogPrefix} HTTP {entry}");

            var accessToken = System.Environment.GetEnvironmentVariable("SAFESITE_ACCESS_TOKEN");
            var email = System.Environment.GetEnvironmentVariable("SAFESITE_ADMIN_EMAIL");
            var password = System.Environment.GetEnvironmentVariable("SAFESITE_ADMIN_PASSWORD");
            if (!string.IsNullOrEmpty(accessToken))
                client.Auth.UseAccessToken(accessToken);
            else if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(password))
                await client.Auth.SignInAsync(email, password);
            else if (environment.IsLocal)
                client.Auth.UseLocalDevToken();
            else
                return Fail("Set SAFESITE_ADMIN_EMAIL and SAFESITE_ADMIN_PASSWORD (or SAFESITE_ACCESS_TOKEN).");

            var scenarios = await client.Admin.GetScenariosAsync();
            var scenario = scenarios.FirstOrDefault(s => s.Slug == scenarioKey || s.Id == scenarioKey);
            if (scenario == null)
                return Fail($"No scenario with slug or id '{scenarioKey}' in {environment.name}.");

            var options = new UploadOptions
            {
                Version = Arg(args, "-version") ?? SafeSiteTime.BumpPatch(scenario.Version),
                DryRun = Flag(args, "-dryRun"),
                UploadOnly = Flag(args, "-uploadOnly"),
                CleanBuild = !Flag(args, "-noClean"),
                RemoveOldFiles = !Flag(args, "-keepOldFiles"),
                SkipUnchangedBundles = !Flag(args, "-noSkip"),
                MaxFileBytes = SafeSiteUploadSettings.LoadOrCreate().MaxFileBytes,
            };

            var platforms = Arg(args, "-platforms");
            options.Platforms = string.IsNullOrEmpty(platforms)
                ? new[] { SafeSitePlatforms.Meta, string.IsNullOrEmpty(scenario.PicoContentPath) ? null : SafeSitePlatforms.Pico }.Where(p => p != null).ToList()
                : platforms.Split(',').Select(p => p.Trim().ToLowerInvariant()).Where(p => p.Length > 0).ToList();
            var unknown = options.Platforms.Where(p => p != SafeSitePlatforms.Meta && p != SafeSitePlatforms.Pico).ToList();
            if (unknown.Count > 0)
                return Fail("Unknown platform(s): " + string.Join(", ", unknown));

            Console.WriteLine($"{LogPrefix} {scenario.Slug} v{scenario.Version} -> v{options.Version} on {environment.name} ({string.Join(", ", options.Platforms)})");
            var pipeline = new ContentUploadPipeline(client, line => Console.WriteLine($"{LogPrefix} {line}"), null);
            var report = await pipeline.RunAsync(scenario, options, CancellationToken.None);
            return report.succeeded ? 0 : 1;
        }

        private static string Arg(string[] args, string name)
        {
            var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static bool Flag(string[] args, string name) => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

        private static int Fail(string message)
        {
            Console.Error.WriteLine($"{LogPrefix} ERROR: {message}");
            return 1;
        }

        /// <summary>Single-threaded SynchronizationContext drained by <see cref="Run"/>.</summary>
        private sealed class PumpContext : SynchronizationContext
        {
            private readonly ConcurrentQueue<(SendOrPostCallback callback, object state)> _queue =
                new ConcurrentQueue<(SendOrPostCallback, object)>();

            public override void Post(SendOrPostCallback d, object state) => _queue.Enqueue((d, state));

            public override void Send(SendOrPostCallback d, object state) => d(state);

            public override SynchronizationContext CreateCopy() => this;

            public void Drain()
            {
                while (_queue.TryDequeue(out var work))
                    work.callback(work.state);
            }
        }
    }
}
