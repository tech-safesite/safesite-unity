using System;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    public enum VersionBumpChoice
    {
        Cancel,
        KeepCurrent,
        BugFix,
        Feature,
        ReleaseCandidate,
    }

    public static class VersionBumper
    {
        private static readonly Regex VersionPattern =
            new Regex(@"^(\d+)\.(\d+)\.(\d+)(?:-rc\.(\d+))?$", RegexOptions.Compiled);

        /// <summary>Shows the bump/keep/cancel prompt. Returns false if the user cancelled the build.</summary>
        public static bool PromptAndMaybeBump()
        {
            var choice = EditorUtility.DisplayDialogComplex(
                "Bump Version?",
                $"Current version: {PlayerSettings.bundleVersion} (code {PlayerSettings.Android.bundleVersionCode})",
                "Bump Version",
                "Keep Current",
                "Cancel");

            if (choice == 2)
                return false;

            if (choice == 1)
                return true;

            var bumpChoice = EditorUtility.DisplayDialogComplex(
                "Bump Type",
                "Choose the kind of change this build represents.",
                "Bug Fix",
                "Feature",
                "Release Candidate");

            var kind = bumpChoice switch
            {
                0 => VersionBumpChoice.BugFix,
                1 => VersionBumpChoice.Feature,
                2 => VersionBumpChoice.ReleaseCandidate,
                _ => VersionBumpChoice.KeepCurrent,
            };

            Apply(kind);
            return true;
        }

        public static void Apply(VersionBumpChoice choice)
        {
            if (choice == VersionBumpChoice.KeepCurrent || choice == VersionBumpChoice.Cancel)
                return;

            var (major, minor, patch, rc) = Parse(PlayerSettings.bundleVersion);

            switch (choice)
            {
                case VersionBumpChoice.BugFix:
                    patch += 1;
                    rc = null;
                    break;
                case VersionBumpChoice.Feature:
                    minor += 1;
                    patch = 0;
                    rc = null;
                    break;
                case VersionBumpChoice.ReleaseCandidate:
                    if (rc.HasValue)
                    {
                        rc += 1;
                    }
                    else
                    {
                        patch += 1;
                        rc = 1;
                    }
                    break;
            }

            var newVersion = rc.HasValue ? $"{major}.{minor}.{patch}-rc.{rc}" : $"{major}.{minor}.{patch}";
            PlayerSettings.bundleVersion = newVersion;
            PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode + 1);

            Debug.Log($"[HeadsetBuild] Version bumped to {newVersion} (code {PlayerSettings.Android.bundleVersionCode}).");
        }

        private static (int major, int minor, int patch, int? rc) Parse(string version)
        {
            var match = VersionPattern.Match(version ?? string.Empty);
            if (!match.Success)
            {
                Debug.LogWarning($"[HeadsetBuild] Could not parse bundleVersion '{version}'. Falling back to 0.1.0.");
                return (0, 1, 0, null);
            }

            var major = int.Parse(match.Groups[1].Value);
            var minor = int.Parse(match.Groups[2].Value);
            var patch = int.Parse(match.Groups[3].Value);
            int? rc = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : null;
            return (major, minor, patch, rc);
        }
    }
}
