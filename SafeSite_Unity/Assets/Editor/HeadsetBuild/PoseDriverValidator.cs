using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem.XR;

namespace SafeSite.Build.Headset
{
    /// <summary>
    /// Fails the build when one TrackedPoseDriver is nested underneath another.
    ///
    /// TrackedPoseDriver writes transform.localPosition/localRotation, but the pose it writes is
    /// already expressed in XR Origin tracking space. Nesting one driver under another therefore
    /// applies the second pose on top of the first: the object lands at roughly (poseA + poseB)
    /// from the origin instead of at poseB.
    ///
    /// This is silent - nothing logs, nothing throws, the object simply renders in the wrong place -
    /// and it is exactly what a "Controller Model Anchor" parented under the aim-pose-driven
    /// "Left Controller" produced: the interaction ray tracked the real controller correctly while
    /// the controller mesh floated somewhere else entirely.
    ///
    /// Every correctly-authored driver in the XRI rig (Main Camera, Gaze Interactor, the controllers
    /// themselves) is a direct child of Camera Offset. Pose drivers belong in tracking space.
    /// </summary>
    public static class PoseDriverValidator
    {
        public static void ValidateScenes(IEnumerable<string> scenePaths)
        {
            var problems = new List<string>();

            foreach (var scenePath in scenePaths)
            {
                var scene = EditorSceneManager.GetSceneByPath(scenePath);
                var opened = false;

                if (!scene.isLoaded)
                {
                    scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                    opened = true;
                }

                foreach (var root in scene.GetRootGameObjects())
                    CollectNestedDrivers(root, scenePath, problems);

                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }

            if (problems.Count == 0)
                return;

            throw new BuildFailedException(
                "TrackedPoseDriver nested under another TrackedPoseDriver - the child's pose is applied on top " +
                "of the parent's, so it will render in the wrong place. Re-parent it to the XR Origin's " +
                $"Camera Offset:\n  {string.Join("\n  ", problems)}");
        }

        private static void CollectNestedDrivers(GameObject root, string scenePath, List<string> problems)
        {
            foreach (var driver in root.GetComponentsInChildren<TrackedPoseDriver>(true))
            {
                var ancestor = driver.transform.parent;
                while (ancestor != null)
                {
                    if (ancestor.GetComponent<TrackedPoseDriver>() != null)
                    {
                        problems.Add($"{scenePath}: '{GetPath(driver.transform)}' is nested under " +
                                     $"'{GetPath(ancestor)}'");
                        break;
                    }

                    ancestor = ancestor.parent;
                }
            }
        }

        private static string GetPath(Transform transform)
        {
            var path = transform.name;
            for (var parent = transform.parent; parent != null; parent = parent.parent)
                path = parent.name + "/" + path;

            return path;
        }
    }
}
