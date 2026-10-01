using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace SafeSite.Build.XRRig
{
    internal static class XRRigBuilder
    {
        public const string RigRootName = "XR Rig";
        private const string HandsRigPrefabName = "XR Origin Hands (XR Rig)";
        private const string ControllerOnlyRigPrefabName = "XR Origin (XR Rig)";

        /// <summary>
        /// The project-owned copy of the hands rig. It carries the grip-pose Controller Model Anchors
        /// and the PlatformControllerModel wiring, none of which survives in the Package Manager
        /// samples - re-importing "Hands Interaction Demo" or "Starter Assets" overwrites those files
        /// and silently drops the wiring. Prefer this; the sample prefabs are only a fallback for a
        /// project that has not been set up yet.
        /// </summary>
        private const string ProjectRigPrefabPath = "Assets/Prefabs/XR/SafeSite XR Rig.prefab";

        public static bool BuildInScene(Scene scene, bool interactive)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("[XRRig] No valid, loaded scene to build the rig into.");
                return false;
            }

            var existing = FindRigRoot(scene);
            if (existing != null)
            {
                if (interactive)
                {
                    if (!EditorUtility.DisplayDialog("XR Rig",
                            $"A '{RigRootName}' already exists in '{scene.name}'. Replace it?", "Replace", "Cancel"))
                        return false;
                }
                else
                {
                    return false;
                }

                Undo.DestroyObjectImmediate(existing);
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectRigPrefabPath);

            if (prefab == null && !XRRigSampleImporter.EnsureSamplesImported())
            {
                if (interactive)
                {
                    EditorUtility.DisplayDialog("XR Rig",
                        "Could not import the required XR Interaction Toolkit samples. See the Console for details.",
                        "OK");
                }
                return false;
            }

            prefab ??= FindRigPrefab(HandsRigPrefabName) ?? FindRigPrefab(ControllerOnlyRigPrefabName);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[XRRig] Could not find '{HandsRigPrefabName}' or '{ControllerOnlyRigPrefabName}' prefab " +
                    "under Assets/Samples. Re-import the 'Starter Assets' and 'Hands Interaction Demo' samples " +
                    "for com.unity.xr.interaction.toolkit from the Package Manager.");
                return false;
            }

            RemoveDefaultMainCamera(scene);

            var rigInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            rigInstance.name = RigRootName;
            rigInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Undo.RegisterCreatedObjectUndo(rigInstance, "Create XR Rig");

            EnsureEventSystem(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = rigInstance;

            var prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (prefabPath == ProjectRigPrefabPath)
            {
                Debug.Log($"[XRRig] Created '{RigRootName}' from the project-owned rig in scene '{scene.name}'.");
            }
            else
            {
                var usedHandsRig = prefab.name == HandsRigPrefabName;
                Debug.LogWarning(
                    $"[XRRig] Created '{RigRootName}' from the package sample '{prefab.name}' " +
                    (usedHandsRig ? "(controllers + OpenXR hand tracking)" : "(controllers only — hand-tracking sample was unavailable)") +
                    $" in scene '{scene.name}'. This rig has no grip-pose Controller Model Anchors, so vendor " +
                    $"controller models will render at the aim pose. Expected the project rig at '{ProjectRigPrefabPath}'.");
            }

            return true;
        }

        private static GameObject FindRigRoot(Scene scene)
        {
            return scene.GetRootGameObjects().FirstOrDefault(go => go.name == RigRootName);
        }

        private static GameObject FindRigPrefab(string prefabName)
        {
            var guids = AssetDatabase.FindAssets($"\"{prefabName}\" t:Prefab");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/Samples/"))
                    continue;

                if (Path.GetFileNameWithoutExtension(path) == prefabName)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            return null;
        }

        private static void RemoveDefaultMainCamera(Scene scene)
        {
            var mainCam = scene.GetRootGameObjects()
                .FirstOrDefault(go => go.CompareTag("MainCamera") && go.GetComponent<Camera>() != null);

            if (mainCam == null)
                return;

            var looksDefault = mainCam.GetComponents<Component>()
                .All(c => c is Transform || c is Camera || c is AudioListener ||
                          c.GetType().Name == "UniversalAdditionalCameraData");

            if (looksDefault)
            {
                Debug.Log($"[XRRig] Removing default '{mainCam.name}' — the XR Rig brings its own camera.");
                Undo.DestroyObjectImmediate(mainCam);
            }
            else
            {
                Debug.LogWarning(
                    $"[XRRig] Found an existing camera '{mainCam.name}' with custom components; leaving it in " +
                    "place. Remove or disable it manually if it conflicts with the XR Rig's camera.");
            }
        }

        private static void EnsureEventSystem(Scene scene)
        {
            var hasEventSystem = scene.GetRootGameObjects()
                .Any(go => go.GetComponentInChildren<EventSystem>(true) != null);

            if (hasEventSystem)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }
    }
}
