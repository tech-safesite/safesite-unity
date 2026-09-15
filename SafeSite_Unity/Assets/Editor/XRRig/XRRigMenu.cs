using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SafeSite.Build.XRRig
{
    internal static class XRRigMenu
    {
        [MenuItem("Build/XR/Create Rig In Active Scene", priority = 400)]
        public static void CreateRigInActiveScene()
        {
            XRRigBuilder.BuildInScene(SceneManager.GetActiveScene(), interactive: true);
        }

        [MenuItem("Build/XR/Import Rig Samples", priority = 401)]
        public static void ImportSamples()
        {
            if (XRRigSampleImporter.EnsureSamplesImported())
                Debug.Log("[XRRig] Samples ready.");
        }

        [MenuItem("Build/XR/Auto-Create Rig On Scene Open", priority = 420)]
        public static void ToggleAutoCreate()
        {
            XRRigAutoCreate.Enabled = !XRRigAutoCreate.Enabled;
        }

        [MenuItem("Build/XR/Auto-Create Rig On Scene Open", true)]
        public static bool ToggleAutoCreateValidate()
        {
            Menu.SetChecked("Build/XR/Auto-Create Rig On Scene Open", XRRigAutoCreate.Enabled);
            return true;
        }
    }
}
