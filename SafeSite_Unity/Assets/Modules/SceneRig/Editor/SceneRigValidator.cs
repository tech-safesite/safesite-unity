using System;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Modules.SceneRig.Editor
{
    /// <summary>
    /// Checks that a scene has the right XR rig for what it is, and fixes it.
    ///
    /// - App shell (Bootstrap): exactly one --Player--, whose XR Origin, camera, audio listener and
    ///   EventSystem are the only ones; TrainingScenarioLoader.playerRig points at that XR Origin.
    /// - Scenario: none of those. Scenarios load additively into Bootstrap, and their scripts find
    ///   the player with FindAnyObjectByType&lt;XROrigin&gt;, so a second rig gets picked at random.
    /// - Standalone: one project rig (SafeSite XR Rig) plus an XR UI EventSystem.
    /// </summary>
    public static class SceneRigValidator
    {
        public const string PlayerPrefabPath = "Assets/_AppCore/Player/--Player--.prefab";
        private const string ProjectOriginPrefabPath = "Assets/Prefabs/XR/SafeSite XR Origin.prefab";
        private const string AppShellFolder = "Assets/_AppCore/Scenes/";
        private const string ScenariosFolder = "Assets/_Scenarios/";
        private const string ScenarioTemplatesFolder = "Assets/Modules/ScenarioAuthoring/Editor/Templates/";
        private const string ModulesFolder = "Assets/Modules/";
        private const string LogPrefix = "[SceneRig]";

        public static string Describe(SceneRigKind kind)
        {
            switch (kind)
            {
                case SceneRigKind.AppShell:
                    return "App shell: the player comes from --Player--, and scenarios load into this scene.";
                case SceneRigKind.Scenario:
                    return "Scenario: loads into Bootstrap, which brings the player. No rig, camera, audio listener or EventSystem of its own.";
                case SceneRigKind.Standalone:
                    return "Standalone scene, played on its own: needs the project rig (SafeSite XR Rig).";
                default:
                    return "Package, sample or module dev scene: not checked.";
            }
        }

        public static SceneRigKind Classify(string scenePath)
        {
            if (string.IsNullOrEmpty(scenePath))
                return SceneRigKind.Standalone; // untitled scene
            if (!scenePath.StartsWith("Assets/") || scenePath.StartsWith("Assets/Samples/"))
                return SceneRigKind.Ignored;
            if (scenePath.StartsWith(AppShellFolder) || IsFirstBuildScene(scenePath))
                return SceneRigKind.AppShell;
            // New scenarios are copies of the template, so it must stay rig-free like them.
            if (scenePath.StartsWith(ScenariosFolder) || scenePath.StartsWith(ScenarioTemplatesFolder) || IsScenarioMainScene(scenePath))
                return SceneRigKind.Scenario;
            // Modules' testbench and demo scenes are flat-screen dev harnesses, not XR scenes.
            if (scenePath.StartsWith(ModulesFolder))
                return SceneRigKind.Ignored;
            return SceneRigKind.Standalone;
        }

        public static SceneRigReport Validate(Scene scene, SceneRigKind? kind = null)
        {
            var report = new SceneRigReport { Scene = scene, Kind = kind ?? Classify(scene.path) };
            if (!scene.IsValid() || !scene.isLoaded || report.Kind == SceneRigKind.Ignored)
                return report;

            var roots = scene.GetRootGameObjects();
            switch (report.Kind)
            {
                case SceneRigKind.AppShell:
                    CheckAppShell(scene, roots, report);
                    break;
                case SceneRigKind.Scenario:
                    CheckScenario(roots, report);
                    break;
                case SceneRigKind.Standalone:
                    CheckStandalone(scene, roots, report);
                    break;
            }

            CheckNestedPoseDrivers(roots, report);
            return report;
        }

        /// <summary>Applies every fix (re-checking between passes, since one fix can reveal another) as one undo step.</summary>
        public static int FixAll(Scene scene, SceneRigKind? kind = null)
        {
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Fix scene rig");

            var applied = 0;
            var seen = new HashSet<string>();
            for (var pass = 0; pass < 4; pass++)
            {
                // A fix that can't take (e.g. inside a locked prefab) would otherwise repeat every pass.
                var fixable = Validate(scene, kind).Issues.Where(i => i.CanFix && seen.Add(i.Message)).ToList();
                if (fixable.Count == 0)
                    break;
                foreach (var issue in fixable)
                {
                    try
                    {
                        issue.Fix();
                        applied++;
                        Debug.Log($"{LogPrefix} {scene.name}: {issue.FixLabel} — {issue.Message}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }

            Undo.CollapseUndoOperations(group);
            if (applied > 0 && scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
            return applied;
        }

        /// <summary>Applies one fix as its own undo step.</summary>
        public static void Apply(RigIssue issue, Scene scene)
        {
            if (issue?.Fix == null)
                return;
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(issue.FixLabel);
            issue.Fix();
            Undo.CollapseUndoOperations(group);
            if (scene.IsValid())
                EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"{LogPrefix} {scene.name}: {issue.FixLabel} — {issue.Message}");
        }

        /// <summary>Checks a scene by path, opening it additively if it isn't loaded. Used by the scenario export check.</summary>
        public static void ValidateScenePath(string scenePath, List<string> errors, List<string> warnings)
        {
            var scene = SceneManager.GetSceneByPath(scenePath);
            var opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                opened = true;
            }

            try
            {
                var report = Validate(scene);
                foreach (var issue in report.Issues)
                {
                    if (issue.Severity == RigIssueSeverity.Error)
                        errors.Add($"Rig: {issue.Message} (SafeSite > Scene Rig can fix this.)");
                    else if (issue.Severity == RigIssueSeverity.Warning)
                        warnings.Add($"Rig: {issue.Message}");
                }
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---- App shell ----------------------------------------------------------------------------

        private static void CheckAppShell(Scene scene, GameObject[] roots, SceneRigReport report)
        {
            var players = All<Transform>(roots).Select(t => t.gameObject).Where(IsPlayerInstanceRoot).ToList();
            var player = players.FirstOrDefault();

            if (player == null)
            {
                report.Add(RigIssueSeverity.Error, "No --Player-- in the scene. Bootstrap brings the player that every scenario uses.",
                    null, "Add --Player--", () => AddPlayer(scene));
            }

            foreach (var extra in players.Skip(1))
            {
                var target = extra;
                report.Add(RigIssueSeverity.Error, $"Extra --Player-- '{PathOf(extra)}'.", extra, "Remove", () => RemoveOrDisable(target));
            }

            XROrigin playerOrigin = null;
            if (player != null)
            {
                playerOrigin = player.GetComponentInChildren<XROrigin>(true);
                if (playerOrigin == null)
                    report.Add(RigIssueSeverity.Error, "--Player-- has no XR Origin.", player);
                else
                    CheckRigBasics(playerOrigin, report);

                if (!player.GetComponentsInChildren<MonoBehaviour>(true).Any(m => m != null && m.GetType().Name == "PlatformControllerModel"))
                    report.Add(RigIssueSeverity.Warning,
                        "--Player-- is built on the XRI sample rig, which has no PlatformControllerModel: headsets show the generic " +
                        $"controller mesh, not the Meta or Pico one. The project rig ({XRRigBuilder.ProjectRigPrefabPath}) has them; " +
                        "moving them into --Player-- is a change to that prefab.", player);
            }

            Func<Transform, bool> isPlayer = t => player != null && t.IsChildOf(player.transform);
            var removedRigs = new List<Transform>();
            foreach (var origin in Active<XROrigin>(roots).Where(o => !isPlayer(o.transform)))
            {
                var rig = RigRoot(origin);
                removedRigs.Add(rig.transform);
                report.Add(RigIssueSeverity.Error,
                    $"Second XR rig '{PathOf(rig)}'. Only --Player-- should have one: scenario scripts find the player with FindAnyObjectByType<XROrigin>.",
                    rig, "Remove rig", () => RemoveOrDisable(rig));
            }

            Func<Transform, bool> allowed = t => isPlayer(t) || removedRigs.Any(t.IsChildOf);
            CheckStrayCameras(roots, allowed, "renders on top of the player's camera", report);
            CheckEventSystems(scene, roots, player != null ? player.transform : null, removedRigs, report);
            CheckLoader(roots, playerOrigin, report);

            var managers = Active<XRInteractionManager>(roots);
            if (managers.Count > 1)
                report.Add(RigIssueSeverity.Warning,
                    $"{managers.Count} XR Interaction Managers. Interactors and interactables can register with different ones and then can't see each other.",
                    managers[1]);
        }

        private static void CheckLoader(GameObject[] roots, XROrigin playerOrigin, SceneRigReport report)
        {
            // TrainingScenarioLoader lives in Assembly-CSharp, which a module can't reference: match it by name.
            var loader = All<MonoBehaviour>(roots).FirstOrDefault(m => m != null && m.GetType().Name == "TrainingScenarioLoader");
            if (loader == null || playerOrigin == null)
                return;

            var serialized = new SerializedObject(loader);
            var property = serialized.FindProperty("playerRig");
            if (property == null || property.objectReferenceValue == playerOrigin.transform)
                return;

            var current = property.objectReferenceValue as Transform;
            report.Add(RigIssueSeverity.Error,
                current == null
                    ? "TrainingScenarioLoader's Player Rig is empty, so the player isn't reset between scenarios."
                    : $"TrainingScenarioLoader's Player Rig is '{PathOf(current.gameObject)}', not the player's XR Origin.",
                loader, "Assign player", () =>
                {
                    var so = new SerializedObject(loader);
                    so.FindProperty("playerRig").objectReferenceValue = playerOrigin.transform;
                    so.ApplyModifiedProperties();
                });
        }

        private static void AddPlayer(Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"{LogPrefix} No player prefab at {PlayerPrefabPath}.");
            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            Undo.RegisterCreatedObjectUndo(player, "Add --Player--");
            Selection.activeGameObject = player;
        }

        // ---- Scenario -----------------------------------------------------------------------------

        private static void CheckScenario(GameObject[] roots, SceneRigReport report)
        {
            var removedRigs = new List<Transform>();
            foreach (var origin in Active<XROrigin>(roots))
            {
                var rig = RigRoot(origin);
                removedRigs.Add(rig.transform);
                report.Add(RigIssueSeverity.Error,
                    $"Has its own XR rig '{PathOf(rig)}'. In Bootstrap that's a second player, and FindAnyObjectByType<XROrigin> may pick it.",
                    rig, "Remove rig", () => RemoveOrDisable(rig));
            }

            Func<Transform, bool> inRemovedRig = t => removedRigs.Any(t.IsChildOf);
            CheckStrayCameras(roots, inRemovedRig, "renders on top of the player's view in Bootstrap", report);

            foreach (var system in Active<EventSystem>(roots).Where(s => !inRemovedRig(s.transform)))
            {
                var target = system;
                report.Add(RigIssueSeverity.Error,
                    $"Has its own EventSystem '{PathOf(system.gameObject)}'. The player brings one, and two fight over UI input.",
                    system.gameObject, "Remove", () => RemoveEventSystem(target));
            }

            foreach (var manager in Active<XRInteractionManager>(roots).Where(m => !inRemovedRig(m.transform)))
            {
                var target = manager;
                report.Add(RigIssueSeverity.Warning,
                    $"Has its own XR Interaction Manager '{PathOf(manager.gameObject)}'. Its interactables may register here, not with the player's, and can't be grabbed.",
                    manager.gameObject, "Remove", () => RemoveComponentOrObject(target));
            }
        }

        // ---- Standalone ---------------------------------------------------------------------------

        private static void CheckStandalone(Scene scene, GameObject[] roots, SceneRigReport report)
        {
            var origins = Active<XROrigin>(roots);
            if (origins.Count == 0)
            {
                report.Add(RigIssueSeverity.Error, "No XR rig.", null, "Add SafeSite XR Rig",
                    () => XRRigBuilder.BuildInScene(scene, interactive: false));
                CheckStrayCameras(roots, _ => false, "would be the only view; the rig brings its own camera", report);
                return;
            }

            var keep = origins.FirstOrDefault(IsProjectRig) ?? origins.FirstOrDefault(o => IsPlayerInstanceRoot(RigRoot(o))) ?? origins[0];
            var keepRig = RigRoot(keep);
            var removedRigs = new List<Transform>();
            foreach (var extra in origins.Where(o => o != keep))
            {
                var rig = RigRoot(extra);
                removedRigs.Add(rig.transform);
                report.Add(RigIssueSeverity.Error, $"Extra XR rig '{PathOf(rig)}'; '{PathOf(keepRig)}' is kept.", rig, "Remove rig", () => RemoveOrDisable(rig));
            }

            if (IsPlayerInstanceRoot(keepRig))
            {
                report.Add(RigIssueSeverity.Info, "Uses --Player--, the same player as Bootstrap.", keepRig);
            }
            else if (!IsProjectRig(keep))
            {
                report.Add(RigIssueSeverity.Warning,
                    $"'{PathOf(keepRig)}' isn't the project rig, so it has no grip-pose controller anchors or Meta/Pico controller models.",
                    keepRig, "Replace with SafeSite XR Rig", () =>
                    {
                        RemoveOrDisable(keepRig);
                        XRRigBuilder.BuildInScene(scene, interactive: false);
                    });
            }

            CheckRigBasics(keep, report);
            Func<Transform, bool> allowed = t => t.IsChildOf(keepRig.transform) || removedRigs.Any(t.IsChildOf);
            CheckStrayCameras(roots, allowed, "renders on top of the rig's camera", report);
            CheckEventSystems(scene, roots, keepRig.transform, removedRigs, report);
        }

        // ---- Shared checks ------------------------------------------------------------------------

        private static void CheckRigBasics(XROrigin origin, SceneRigReport report)
        {
            if (!origin.gameObject.activeInHierarchy)
            {
                report.Add(RigIssueSeverity.Error, $"The rig '{PathOf(origin.gameObject)}' is inactive.", origin.gameObject, "Activate", () =>
                {
                    for (var t = origin.transform; t != null; t = t.parent)
                    {
                        if (t.gameObject.activeSelf)
                            continue;
                        Undo.RecordObject(t.gameObject, "Activate rig");
                        t.gameObject.SetActive(true);
                    }
                });
            }

            var camera = origin.Camera;
            if (camera == null)
            {
                report.Add(RigIssueSeverity.Error, $"The XR Origin '{PathOf(origin.gameObject)}' has no camera assigned.", origin);
            }
            else if (!camera.CompareTag("MainCamera"))
            {
                report.Add(RigIssueSeverity.Warning, $"The rig's camera '{PathOf(camera.gameObject)}' isn't tagged MainCamera, so Camera.main returns null.",
                    camera.gameObject, "Tag MainCamera", () =>
                    {
                        Undo.RecordObject(camera.gameObject, "Tag MainCamera");
                        camera.gameObject.tag = "MainCamera";
                    });
            }
        }

        /// <summary>Screen cameras and audio listeners outside the rig; render-texture cameras are fine.</summary>
        private static void CheckStrayCameras(GameObject[] roots, Func<Transform, bool> allowed, string why, SceneRigReport report)
        {
            var reported = new HashSet<GameObject>();
            foreach (var camera in All<Camera>(roots))
            {
                if (!camera.enabled || !camera.gameObject.activeInHierarchy || camera.targetTexture != null || allowed(camera.transform))
                    continue;
                var target = camera;
                reported.Add(camera.gameObject);
                var remove = LooksLikeDefaultCamera(camera.gameObject);
                report.Add(RigIssueSeverity.Error, $"Camera '{PathOf(camera.gameObject)}' {why}.", camera.gameObject,
                    remove ? "Remove" : "Disable camera", () => RemoveCamera(target));
            }

            foreach (var listener in All<AudioListener>(roots))
            {
                if (!listener.enabled || !listener.gameObject.activeInHierarchy || allowed(listener.transform) || reported.Contains(listener.gameObject))
                    continue;
                var target = listener;
                report.Add(RigIssueSeverity.Error, $"Audio listener on '{PathOf(listener.gameObject)}': there should be one, on the rig's camera.",
                    listener.gameObject, "Disable listener", () =>
                    {
                        Undo.RecordObject(target, "Disable audio listener");
                        target.enabled = false;
                    });
            }
        }

        /// <summary>One EventSystem, preferably the rig's own, driven by the XR UI Input Module.</summary>
        private static void CheckEventSystems(Scene scene, GameObject[] roots, Transform rig, List<Transform> removedRigs, SceneRigReport report)
        {
            var systems = Active<EventSystem>(roots).Where(s => !removedRigs.Any(s.transform.IsChildOf)).ToList();
            if (systems.Count == 0)
            {
                report.Add(RigIssueSeverity.Error, "No EventSystem, so menus don't take input.", null, "Add EventSystem",
                    () => XRRigBuilder.EnsureEventSystem(scene));
                return;
            }

            var keep = systems.FirstOrDefault(s => rig != null && s.transform.IsChildOf(rig)) ?? systems[0];
            foreach (var extra in systems.Where(s => s != keep))
            {
                var target = extra;
                report.Add(RigIssueSeverity.Error, $"Extra EventSystem '{PathOf(extra.gameObject)}'; Unity uses only one.",
                    extra.gameObject, "Remove", () => RemoveEventSystem(target));
            }

            if (keep.GetComponent<XRUIInputModule>() == null)
            {
                report.Add(RigIssueSeverity.Error,
                    $"EventSystem '{PathOf(keep.gameObject)}' has no XR UI Input Module, so controllers and hands can't press UI.",
                    keep.gameObject, "Use XR UI Input Module", () =>
                    {
                        foreach (var module in keep.GetComponents<BaseInputModule>())
                            Undo.DestroyObjectImmediate(module);
                        Undo.AddComponent<XRUIInputModule>(keep.gameObject);
                    });
            }
        }

        private static void CheckNestedPoseDrivers(GameObject[] roots, SceneRigReport report)
        {
            foreach (var driver in All<TrackedPoseDriver>(roots))
            {
                var parent = driver.transform.parent;
                var outer = parent != null ? parent.GetComponentInParent<TrackedPoseDriver>(true) : null;
                if (outer != null)
                    report.Add(RigIssueSeverity.Error,
                        $"Tracked Pose Driver '{PathOf(driver.gameObject)}' is under another one ('{PathOf(outer.gameObject)}'), so it's posed twice. " +
                        "Move it under the XR Origin's Camera Offset. Headset builds fail on this.", driver.gameObject);
            }
        }

        // ---- Helpers ------------------------------------------------------------------------------

        private static List<T> All<T>(IEnumerable<GameObject> roots) where T : Component =>
            roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).ToList();

        /// <summary>Inactive objects are left alone: they don't render, take input, or turn up in FindAnyObjectByType.</summary>
        private static List<T> Active<T>(IEnumerable<GameObject> roots) where T : Component =>
            All<T>(roots).Where(c => c.gameObject.activeInHierarchy).ToList();

        private static bool IsFirstBuildScene(string scenePath) =>
            EditorBuildSettings.scenes.FirstOrDefault(s => s.enabled)?.path == scenePath;

        /// <summary>A scene referenced by a ScenarioDefinition (matched by type name: that module depends on this one).</summary>
        private static bool IsScenarioMainScene(string scenePath) =>
            AssetDatabase.FindAssets("t:ScenarioDefinition")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Any(p => AssetDatabase.GetDependencies(p, false).Contains(scenePath));

        private static bool IsPlayerInstanceRoot(GameObject go) =>
            go != null && PrefabUtility.IsAnyPrefabInstanceRoot(go) &&
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go) == PlayerPrefabPath;

        private static bool IsProjectRig(XROrigin origin)
        {
            for (var t = origin.transform; t != null; t = t.parent)
            {
                if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject))
                    continue;
                var path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject);
                if (path == XRRigBuilder.ProjectRigPrefabPath || path == ProjectOriginPrefabPath)
                    return true;
            }
            return false;
        }

        /// <summary>The object that is "the rig": the whole prefab instance when it is a rig prefab, else the XR Origin.</summary>
        private static GameObject RigRoot(XROrigin origin)
        {
            var outer = PrefabUtility.GetOutermostPrefabInstanceRoot(origin.gameObject);
            if (outer != null && outer.GetComponentsInChildren<XROrigin>(true).Length == 1 && LooksLikeRigName(outer.name))
                return outer;
            var nearest = PrefabUtility.GetNearestPrefabInstanceRoot(origin.gameObject);
            return nearest != null && nearest.GetComponentsInChildren<XROrigin>(true).Length == 1 ? nearest : origin.gameObject;
        }

        private static bool LooksLikeRigName(string name)
        {
            var n = name.ToLowerInvariant();
            return n.Contains("player") || n.Contains("rig") || n.Contains("origin");
        }

        private static bool LooksLikeDefaultCamera(GameObject go) =>
            go.transform.childCount == 0 &&
            go.GetComponents<Component>().All(c => c is Transform || c is Camera || c is AudioListener ||
                                                  c.GetType().Name == "UniversalAdditionalCameraData" ||
                                                  c.GetType().Name == "FlareLayer");

        private static void RemoveCamera(Camera camera)
        {
            if (camera == null)
                return;
            if (LooksLikeDefaultCamera(camera.gameObject))
            {
                RemoveOrDisable(camera.gameObject);
                return;
            }
            Undo.RecordObject(camera, "Disable camera");
            camera.enabled = false;
            var listener = camera.GetComponent<AudioListener>();
            if (listener != null)
            {
                Undo.RecordObject(listener, "Disable audio listener");
                listener.enabled = false;
            }
        }

        private static void RemoveEventSystem(EventSystem system)
        {
            if (system == null)
                return;
            var onlyInput = system.transform.childCount == 0 &&
                            system.GetComponents<Component>().All(c => c is Transform || c is EventSystem || c is BaseInputModule);
            if (onlyInput)
            {
                RemoveOrDisable(system.gameObject);
                return;
            }
            foreach (var module in system.GetComponents<BaseInputModule>())
                Undo.DestroyObjectImmediate(module);
            Undo.DestroyObjectImmediate(system);
        }

        private static void RemoveComponentOrObject(Component component)
        {
            if (component == null)
                return;
            var go = component.gameObject;
            if (go.transform.childCount == 0 && go.GetComponents<Component>().Length == 2)
                RemoveOrDisable(go);
            else
                Undo.DestroyObjectImmediate(component);
        }

        /// <summary>Deletes the object, or deactivates it when it's a child inside a prefab instance.</summary>
        private static void RemoveOrDisable(GameObject go)
        {
            if (go == null)
                return;
            if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
            {
                Undo.RecordObject(go, "Deactivate");
                go.SetActive(false);
                Debug.Log($"{LogPrefix} '{PathOf(go)}' is inside a prefab instance, so it was deactivated rather than deleted.");
                return;
            }
            Undo.DestroyObjectImmediate(go);
        }

        internal static string PathOf(GameObject go)
        {
            if (go == null)
                return "(none)";
            var path = go.name;
            for (var t = go.transform.parent; t != null; t = t.parent)
                path = t.name + "/" + path;
            return path;
        }
    }
}
