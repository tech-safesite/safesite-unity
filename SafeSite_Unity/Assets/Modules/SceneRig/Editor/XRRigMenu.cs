using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Modules.SceneRig.Editor
{
    /// <summary>
    /// One button: add the XR rig (--Player--, the project's only rig) to the active scene.
    /// Scenario scenes (Assets/_Scenarios/) never get one: they load into Bootstrap, which brings
    /// the player. See Assets/_AppCore/Player/README.md.
    /// </summary>
    public static class XRRigMenu
    {
        private const string PlayerPrefabPath = "Assets/_AppCore/Player/--Player--.prefab";
        private const string ScenariosFolder = "Assets/_Scenarios/";

        [MenuItem("SafeSite/Add XR Rig To Active Scene")]
        private static void AddRigToActiveScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path.StartsWith(ScenariosFolder))
            {
                EditorUtility.DisplayDialog("XR Rig",
                    "Scenario scenes don't get a rig: they load into Bootstrap, which brings the player.", "OK");
                return;
            }

            if (Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include) != null)
            {
                EditorUtility.DisplayDialog("XR Rig", "This scene already has an XR rig.", "OK");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[XRRig] Prefab not found at '{PlayerPrefabPath}'.");
                return;
            }

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(rig, "Add XR Rig");
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = rig;
        }

        /// <summary>Scenario export check: a scenario scene must not carry its own XR rig.</summary>
        public static void ValidateScenePath(string scenePath, List<string> errors, List<string> warnings)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);

            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    foreach (var origin in root.GetComponentsInChildren<XROrigin>(true))
                        errors.Add($"Rig: '{origin.name}' is an XR rig. Scenarios load into Bootstrap, which brings the player; remove it.");
                }
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
