using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Silences Meta XR Core SDK's Project Setup Tool while the PICO target is active, and restores
    /// it when switching back to Quest.
    ///
    /// The Meta SDK registers ~98 configuration tasks that assume the project is a Quest project, and
    /// they surface in the shared XR Project Validation window regardless of which headset is being
    /// built. Against a PICO configuration they are not merely noisy, they are wrong, and their
    /// "Fix" buttons actively break the PICO build:
    ///
    ///   "at least the Oculus Touch Interaction Profile should be included" - adds a Meta controller
    ///       profile to a PICO build, which is precisely the cross-vendor mixing this tooling exists
    ///       to prevent.
    ///   "Minimum Android API Level must be at least 32" - HeadsetBuildConfig targets API 29 for
    ///       PICO on purpose; "fixing" this silently narrows PICO device support.
    ///
    /// Only tasks this class ignored are un-ignored later, tracked by Uid in EditorPrefs, so a task
    /// somebody ignored by hand in the Meta UI stays ignored.
    ///
    /// All access is reflective: the Meta SDK stays an optional dependency as far as this tooling is
    /// concerned, matching how HeadsetBuildConfig treats its feature sets.
    /// </summary>
    public static class MetaProjectSetupGate
    {
        private const string PrefsKey = "SafeSite.HeadsetBuild.MetaTasksIgnoredByTooling";

        /// <summary>
        /// Android only, deliberately. Standalone is the Quest Link / PC VR path, which runs on the
        /// Meta runtime whichever Android headset is selected, so Meta's checks are correct there and
        /// must keep reporting. Silencing them on Standalone hid a real defect: Standalone had no
        /// OpenXR interaction profile enabled, and the "Oculus Touch Interaction Profile should be
        /// included" error that would have said so was being suppressed.
        /// </summary>
        private static readonly BuildTargetGroup[] AffectedGroups =
        {
            BuildTargetGroup.Android,
        };

        public static void Apply(HeadsetDevice device)
        {
            var tasks = GetTasks();
            if (tasks == null)
                return; // Meta XR Core SDK not installed - nothing to silence.

            if (device == HeadsetDevice.Pico)
                Suppress(tasks);
            else
                Restore(tasks);
        }

        private static void Suppress(IReadOnlyList<object> tasks)
        {
            var ignored = new HashSet<string>(LoadTrackedUids());
            var newlyIgnored = 0;

            foreach (var task in tasks)
            {
                var uid = GetUid(task);
                if (uid == null)
                    continue;

                foreach (var group in AffectedGroups)
                {
                    if (IsIgnored(task, group))
                        continue; // Already ignored - by us previously, or by hand. Leave it.

                    SetIgnored(task, group, true);
                    ignored.Add(uid);
                    newlyIgnored++;
                }
            }

            SaveTrackedUids(ignored);

            if (newlyIgnored > 0)
                Debug.Log($"[HeadsetBuild] Silenced {newlyIgnored} Meta XR project-setup check(s) for the PICO " +
                          "target. They return automatically when you switch back to Quest.");
        }

        private static void Restore(IReadOnlyList<object> tasks)
        {
            var tracked = new HashSet<string>(LoadTrackedUids());
            if (tracked.Count == 0)
                return;

            var restored = 0;
            foreach (var task in tasks)
            {
                var uid = GetUid(task);
                if (uid == null || !tracked.Contains(uid))
                    continue;

                foreach (var group in AffectedGroups)
                {
                    if (!IsIgnored(task, group))
                        continue;

                    SetIgnored(task, group, false);
                    restored++;
                }
            }

            EditorPrefs.DeleteKey(PrefsKey);

            if (restored > 0)
                Debug.Log($"[HeadsetBuild] Restored {restored} Meta XR project-setup check(s) for the Quest target.");
        }

        private static IReadOnlyList<object> GetTasks()
        {
            var setupType = HeadsetBuildTypeUtil.FindType("OVRProjectSetup");
            var registry = setupType
                ?.GetProperty("Registry", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);

            var getTasks = registry?.GetType()
                .GetMethod("GetTasks", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(BuildTargetGroup) }, null);

            if (getTasks == null)
                return null;

            var collected = new List<object>();
            foreach (var group in AffectedGroups)
            {
                if (getTasks.Invoke(registry, new object[] { group }) is System.Collections.IEnumerable tasks)
                    collected.AddRange(tasks.Cast<object>());
            }

            return collected.Distinct().ToList();
        }

        private static string GetUid(object task)
        {
            var uid = task.GetType().GetProperty("Uid")?.GetValue(task);
            return uid?.ToString();
        }

        private static bool IsIgnored(object task, BuildTargetGroup group)
        {
            var method = task.GetType().GetMethod("IsIgnored", new[] { typeof(BuildTargetGroup) });
            return method != null && (bool)method.Invoke(task, new object[] { group });
        }

        private static void SetIgnored(object task, BuildTargetGroup group, bool ignored)
        {
            task.GetType()
                .GetMethod("SetIgnored", new[] { typeof(BuildTargetGroup), typeof(bool) })
                ?.Invoke(task, new object[] { group, ignored });
        }

        private static IEnumerable<string> LoadTrackedUids()
        {
            var raw = EditorPrefs.GetString(PrefsKey, string.Empty);
            return string.IsNullOrEmpty(raw)
                ? Array.Empty<string>()
                : raw.Split('\n');
        }

        private static void SaveTrackedUids(IEnumerable<string> uids)
        {
            EditorPrefs.SetString(PrefsKey, string.Join("\n", uids));
        }
    }
}
