using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Modules.SafeSiteApiService.External;
using UnityEngine;

namespace Modules.SafeSiteApiService.Internal
{
    /// <summary>
    /// Device state for one environment. PlayerPrefs hold the token and ids; JSON files under
    /// persistentDataPath/SafeSite/env/&lt;environment&gt;/ hold the config cache, installed manifest
    /// and session queue. Content lives in persistentDataPath/SafeSite/content/.
    /// </summary>
    internal sealed class DeviceStore : IDeviceStore
    {
        // TODO: Android Keystore. The device token is a credential; PlayerPrefs is test quality only.
        private const string ConfigFile = "config.json";
        private const string InstalledFile = "installed.json";
        private const string QueueFile = "session-queue.json";

        private DeviceConfig _config;
        private List<InstalledScenario> _installed;
        private List<SessionRecord> _queue;

        public DeviceStore(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; }

        public event Action Changed;

        public static string Root => Path.Combine(Application.persistentDataPath, "SafeSite");

        private string EnvironmentDirectory => Path.Combine(Root, "env", EnvironmentName);

        private string Key(string name) => $"SafeSite.{EnvironmentName}.device.{name}";

        private string Pref(string name) => PlayerPrefs.GetString(Key(name), null);

        public bool IsEnrolled => !string.IsNullOrEmpty(DeviceToken);
        public string DeviceToken => Pref("token");
        public string HeadsetId => Pref("headsetId");
        public string HeadsetLabel => Pref("label");
        public string ClientName => Pref("clientName");
        public string Platform => Pref("platform");

        public DeviceConfig CachedConfig => _config ??= ReadJson<DeviceConfig>(ConfigFile);

        public IReadOnlyList<InstalledScenario> Installed => InstalledList;

        private List<InstalledScenario> InstalledList => _installed ??= ReadJson<List<InstalledScenario>>(InstalledFile) ?? new List<InstalledScenario>();

        public IReadOnlyList<SessionRecord> SessionQueue => QueueList;

        private List<SessionRecord> QueueList => _queue ??= ReadJson<List<SessionRecord>>(QueueFile) ?? new List<SessionRecord>();

        public string GetInstalledVersion(string scenarioId) => InstalledList.FirstOrDefault(s => s.ScenarioId == scenarioId)?.Version;

        public string GetScenarioDirectory(string scenarioId) => Path.Combine(Root, "content", SafeSegment(scenarioId));

        public string GetContentDirectory(string scenarioId, string version) => Path.Combine(GetScenarioDirectory(scenarioId), SafeSegment(version));

        public void SaveEnrollment(EnrollResponse enrollment, string platform)
        {
            PlayerPrefs.SetString(Key("token"), enrollment.DeviceToken ?? "");
            PlayerPrefs.SetString(Key("headsetId"), enrollment.HeadsetId ?? "");
            PlayerPrefs.SetString(Key("label"), enrollment.Label ?? "");
            PlayerPrefs.SetString(Key("clientName"), enrollment.Client?.Name ?? "");
            PlayerPrefs.SetString(Key("platform"), platform ?? "");
            PlayerPrefs.Save();

            // A new enrollment may belong to another client: the old config no longer applies.
            SaveConfig(null);
        }

        public void SaveConfig(DeviceConfig config)
        {
            _config = config;
            WriteJson(ConfigFile, config);
            if (config?.Headset != null)
            {
                PlayerPrefs.SetString(Key("label"), config.Headset.Label ?? "");
                PlayerPrefs.SetString(Key("platform"), config.Headset.Platform ?? "");
            }

            if (config?.Client != null)
                PlayerPrefs.SetString(Key("clientName"), config.Client.Name ?? "");
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public void SetInstalled(string scenarioId, string version)
        {
            InstalledList.RemoveAll(s => s.ScenarioId == scenarioId);
            InstalledList.Add(new InstalledScenario(scenarioId, version));
            WriteJson(InstalledFile, InstalledList);
            Changed?.Invoke();
        }

        public void RemoveInstalled(string scenarioId)
        {
            InstalledList.RemoveAll(s => s.ScenarioId == scenarioId);
            WriteJson(InstalledFile, InstalledList);
            Changed?.Invoke();
        }

        public void Enqueue(IEnumerable<SessionRecord> sessions)
        {
            QueueList.AddRange(sessions);
            WriteJson(QueueFile, QueueList);
            Changed?.Invoke();
        }

        public void RemoveFromQueue(ICollection<string> deviceSessionIds)
        {
            var ids = new HashSet<string>(deviceSessionIds);
            QueueList.RemoveAll(s => ids.Contains(s.DeviceSessionId));
            WriteJson(QueueFile, QueueList);
            Changed?.Invoke();
        }

        public void ClearSessionQueue()
        {
            QueueList.Clear();
            WriteJson(QueueFile, QueueList);
            Changed?.Invoke();
        }

        /// <summary>Deletes the token, ids, caches, queue and installed content for this environment.</summary>
        public void Forget()
        {
            foreach (var scenario in InstalledList.ToList())
                TryDeleteDirectory(GetScenarioDirectory(scenario.ScenarioId));

            foreach (var name in new[] { "token", "headsetId", "label", "clientName", "platform" })
                PlayerPrefs.DeleteKey(Key(name));
            PlayerPrefs.Save();

            TryDeleteDirectory(EnvironmentDirectory);
            _config = null;
            _installed = null;
            _queue = null;
            Changed?.Invoke();
        }

        internal static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[SafeSiteApiService] Could not delete {path}: {e.Message}");
            }
        }

        private static string SafeSegment(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains(".."))
                throw new ArgumentException($"Unsafe path segment '{value}'");
            return value;
        }

        private T ReadJson<T>(string fileName) where T : class
        {
            var path = Path.Combine(EnvironmentDirectory, fileName);
            try
            {
                return File.Exists(path) ? SafeSiteJson.Deserialize<T>(File.ReadAllText(path)) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SafeSiteApiService] Ignoring unreadable {path}: {e.Message}");
                return null;
            }
        }

        private void WriteJson(string fileName, object value)
        {
            var path = Path.Combine(EnvironmentDirectory, fileName);
            if (value == null)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }

            Directory.CreateDirectory(EnvironmentDirectory);
            var temp = path + ".tmp";
            File.WriteAllText(temp, SafeSiteJson.SerializeIndented(value));
            if (File.Exists(path))
                File.Delete(path);
            File.Move(temp, path);
        }
    }
}
