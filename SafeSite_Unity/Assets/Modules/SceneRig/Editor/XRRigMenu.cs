using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Modules.SceneRig.Editor
{
    internal static class XRRigMenu
    {
        [MenuItem("Build/XR/Create Rig In Active Scene", priority = 400)]
        public static void CreateRigInActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            var kind = SceneRigValidator.Classify(scene.path);
            if (kind == SceneRigKind.AppShell || kind == SceneRigKind.Scenario)
            {
                // Bootstrap's rig is --Player--, and scenarios have none: the standalone rig is wrong for both.
                if (EditorUtility.DisplayDialog("XR Rig",
                        $"'{scene.name}' is a {kind} scene. {SceneRigValidator.Describe(kind)}\n\nOpen Scene Rig to check and fix it instead?",
                        "Open Scene Rig", "Cancel"))
                    SceneRigWindow.Open();
                return;
            }
            XRRigBuilder.BuildInScene(scene, interactive: true);
        }

        [MenuItem("Build/XR/Check Scene Rig", priority = 399)]
        public static void CheckSceneRig() => SceneRigWindow.Open();

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
