using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Re-applies the correct Android store profile right before any build (including a manual
    /// File > Build Settings build), inferred from the current applicationIdentifier, so a stray
    /// build can't ship the wrong minSdk/keystore for the device it's named after.
    /// </summary>
    internal class HeadsetStoreProfilePreprocessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -50;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            HeadsetBuildConfig config;
            try
            {
                config = HeadsetBuildConfig.FindOrThrow();
            }
            catch (BuildFailedException)
            {
                return;
            }

            var appId = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);

            if (appId.Contains(config.quest.deviceToken))
                AndroidStoreProfile.Apply(config, HeadsetDevice.Quest);
            else if (appId.Contains(config.pico.deviceToken))
                AndroidStoreProfile.Apply(config, HeadsetDevice.Pico);
        }
    }
}
