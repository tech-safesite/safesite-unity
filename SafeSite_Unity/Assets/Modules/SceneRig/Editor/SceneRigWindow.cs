using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Modules.SceneRig.Editor
{
    /// <summary>
    /// SafeSite > Scene Rig: checks each open scene has the right XR rig for what it is
    /// (Bootstrap, scenario or standalone) and fixes it. Re-checks as the hierarchy changes.
    /// </summary>
    public sealed class SceneRigWindow : EditorWindow
    {
        private static readonly Color ErrorColor = new Color(0.9f, 0.35f, 0.35f);
        private static readonly Color WarningColor = new Color(0.9f, 0.65f, 0.2f);
        private static readonly Color InfoColor = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color OkColor = new Color(0.4f, 0.8f, 0.45f);

        // Per scene path, for this editor session: "Check as" when the folder guess is wrong.
        private static readonly Dictionary<string, SceneRigKind> KindOverrides = new Dictionary<string, SceneRigKind>();

        private VisualElement _scenes;
        private Label _projectResult;
        private bool _refreshQueued;

        [MenuItem("SafeSite/Scene Rig")]
        public static void Open() => GetWindow<SceneRigWindow>("Scene Rig").minSize = new Vector2(420, 300);

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += QueueRefresh;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.sceneClosed += OnSceneClosed;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            Undo.undoRedoPerformed += QueueRefresh;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= QueueRefresh;
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.sceneClosed -= OnSceneClosed;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            Undo.undoRedoPerformed -= QueueRefresh;
        }

        private void OnSceneOpened(Scene scene, OpenSceneMode mode) => QueueRefresh();
        private void OnSceneClosed(Scene scene) => QueueRefresh();
        private void OnActiveSceneChanged(Scene from, Scene to) => QueueRefresh();

        public void CreateGUI()
        {
            var scroll = new ScrollView();
            rootVisualElement.Add(scroll);
            var root = scroll.contentContainer;
            root.style.paddingLeft = root.style.paddingRight = root.style.paddingTop = 6;

            var intro = new Label("Each open scene, checked for the rig it should have. Fixes can be undone.")
            {
                style = { whiteSpace = WhiteSpace.Normal, marginBottom = 4 },
            };
            root.Add(intro);

            var top = Row(root);
            top.Add(new Button(Refresh) { text = "Re-check" });
            top.Add(new Button(CheckProjectScenes)
            {
                text = "Check all project scenes",
                tooltip = "Bootstrap, every scenario scene and other project scenes, opened in turn. Reports only; nothing is changed.",
            });

            _scenes = new VisualElement();
            root.Add(_scenes);

            _projectResult = new Label { style = { whiteSpace = WhiteSpace.Normal, marginTop = 8 } };
            _projectResult.selection.isSelectable = true;
            root.Add(_projectResult);

            Refresh();
        }

        private void QueueRefresh()
        {
            if (_refreshQueued || rootVisualElement == null)
                return;
            _refreshQueued = true;
            rootVisualElement.schedule.Execute(() =>
            {
                _refreshQueued = false;
                Refresh();
            }).StartingIn(250);
        }

        private void Refresh()
        {
            if (_scenes == null)
                return;
            _scenes.Clear();

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _scenes.Add(new Label("Exit Play mode to check scenes."));
                return;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    _scenes.Add(SceneCard(scene));
            }
        }

        private VisualElement SceneCard(Scene scene)
        {
            var kind = KindFor(scene);
            var report = SceneRigValidator.Validate(scene, kind);

            var card = new VisualElement
            {
                style =
                {
                    marginTop = 8, paddingTop = 6, paddingBottom = 6, paddingLeft = 6, paddingRight = 6,
                    borderTopWidth = 1, borderBottomWidth = 1, borderLeftWidth = 1, borderRightWidth = 1,
                    borderTopColor = Border, borderBottomColor = Border, borderLeftColor = Border, borderRightColor = Border,
                    borderTopLeftRadius = 4, borderTopRightRadius = 4, borderBottomLeftRadius = 4, borderBottomRightRadius = 4,
                },
            };

            var header = Row(card);
            var name = string.IsNullOrEmpty(scene.name) ? "Untitled" : scene.name;
            header.Add(new Label(name + (scene.isDirty ? " *" : ""))
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft },
            });

            var choices = Enum.GetNames(typeof(SceneRigKind)).ToList();
            var kindField = new DropdownField(choices, (int)kind) { tooltip = "Check as. Guessed from the scene's folder; change it if the guess is wrong." };
            var path = scene.path ?? "";
            kindField.RegisterValueChangedCallback(e =>
            {
                var picked = (SceneRigKind)choices.IndexOf(e.newValue);
                if (picked == SceneRigValidator.Classify(path))
                    KindOverrides.Remove(path);
                else
                    KindOverrides[path] = picked;
                QueueRefresh();
            });
            header.Add(kindField);

            card.Add(new Label(SceneRigValidator.Describe(kind)) { style = { whiteSpace = WhiteSpace.Normal, color = InfoColor, marginBottom = 4 } });

            if (kind == SceneRigKind.Ignored)
                return card;

            var problems = report.Issues.Count(i => i.Severity != RigIssueSeverity.Info);
            var summary = problems == 0
                ? "✓ Rig is set up correctly."
                : $"{report.ErrorCount} error(s), {report.WarningCount} warning(s).";
            var status = Row(card);
            status.Add(new Label(summary)
            {
                style = { color = report.ErrorCount > 0 ? ErrorColor : report.WarningCount > 0 ? WarningColor : OkColor, flexGrow = 1, unityTextAlign = TextAnchor.MiddleLeft },
            });
            if (report.FixableCount > 0)
            {
                status.Add(new Button(() => FixAll(scene, kind, report))
                {
                    text = $"Fix all ({report.FixableCount})",
                    tooltip = "Applies every fix below as one undo step.",
                });
            }

            foreach (var issue in report.Issues)
                card.Add(IssueRow(scene, issue));

            return card;
        }

        private VisualElement IssueRow(Scene scene, RigIssue issue)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 3, alignItems = Align.FlexStart } };
            var color = issue.Severity == RigIssueSeverity.Error ? ErrorColor : issue.Severity == RigIssueSeverity.Warning ? WarningColor : InfoColor;
            row.Add(new Label(issue.Severity == RigIssueSeverity.Error ? "●" : issue.Severity == RigIssueSeverity.Warning ? "▲" : "i")
            {
                style = { color = color, width = 14, unityTextAlign = TextAnchor.UpperCenter, marginTop = 2 },
            });
            var message = new Label(issue.Message) { style = { whiteSpace = WhiteSpace.Normal, flexGrow = 1, flexShrink = 1, marginTop = 2 } };
            message.selection.isSelectable = true;
            row.Add(message);

            if (issue.Context != null)
            {
                row.Add(new Button(() =>
                {
                    Selection.activeObject = issue.Context;
                    EditorGUIUtility.PingObject(issue.Context);
                }) { text = "Select" });
            }

            if (issue.CanFix)
            {
                row.Add(new Button(() =>
                {
                    try
                    {
                        SceneRigValidator.Apply(issue, scene);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                    QueueRefresh();
                }) { text = issue.FixLabel });
            }

            return row;
        }

        private void FixAll(Scene scene, SceneRigKind kind, SceneRigReport report)
        {
            var list = string.Join("\n", report.Issues.Where(i => i.CanFix).Select(i => $"• {i.FixLabel}: {i.Message}"));
            if (!EditorUtility.DisplayDialog("Fix scene rig", $"Apply these to '{scene.name}'?\n\n{list}\n\nYou can undo this in one step.", "Fix all", "Cancel"))
                return;
            var applied = SceneRigValidator.FixAll(scene, kind);
            Debug.Log($"[SceneRig] {scene.name}: applied {applied} fix(es). Save the scene to keep them.");
            QueueRefresh();
        }

        private void CheckProjectScenes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            var paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => SceneRigValidator.Classify(p) != SceneRigKind.Ignored)
                .OrderBy(p => SceneRigValidator.Classify(p))
                .ThenBy(p => p)
                .ToList();

            var lines = new List<string>();
            var failing = 0;
            try
            {
                for (var i = 0; i < paths.Count; i++)
                {
                    var path = paths[i];
                    EditorUtility.DisplayProgressBar("Scene Rig", path, (float)i / paths.Count);
                    var errors = new List<string>();
                    var warnings = new List<string>();
                    SceneRigValidator.ValidateScenePath(path, errors, warnings);
                    var kind = SceneRigValidator.Classify(path);
                    if (errors.Count > 0)
                        failing++;
                    lines.Add($"{(errors.Count > 0 ? "✗" : "✓")} {path}  [{kind}]");
                    lines.AddRange(errors.Select(e => "      " + e));
                    lines.AddRange(warnings.Select(w => "      " + w));
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            var text = $"{paths.Count} scene(s) checked, {failing} with errors.\n" + string.Join("\n", lines);
            _projectResult.text = text;
            Debug.Log("[SceneRig] " + text);
            QueueRefresh();
        }

        private static SceneRigKind KindFor(Scene scene) =>
            KindOverrides.TryGetValue(scene.path ?? "", out var kind) ? kind : SceneRigValidator.Classify(scene.path);

        private static readonly Color Border = new Color(0.5f, 0.5f, 0.5f, 0.35f);

        private static VisualElement Row(VisualElement parent)
        {
            var row = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2, alignItems = Align.Center } };
            parent.Add(row);
            return row;
        }
    }
}
