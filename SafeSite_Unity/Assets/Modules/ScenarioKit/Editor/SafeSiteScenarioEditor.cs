using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Modules.ScenarioKit.Editor
{
    /// <summary>
    /// Inspector in three parts, in the order a developer needs them: what to call from their events
    /// (and what in the scene already does), the score setting, and the event this component raises.
    /// </summary>
    [CustomEditor(typeof(SafeSiteScenario))]
    public class SafeSiteScenarioEditor : UnityEditor.Editor
    {
        private static readonly (string Method, string What, bool Ends)[] Methods =
        {
            ("CompleteAndExit", "Trainee reached the end, then back to the menu.", true),
            ("Complete", "Trainee reached the end; stay in the scenario.", true),
            ("Correct", "One scored check passed.", false),
            ("Incorrect", "One scored check failed.", false),
            ("Answer (bool)", "For events that pass true/false: true = correct.", false),
            ("Exit", "Back to the menu, not completed.", false),
            ("Restart", "Play again from the start (a new session).", false),
        };

        private readonly struct Wire
        {
            public readonly Object Source;
            public readonly string Label;
            public readonly string Method;

            public Wire(Object source, string label, string method)
            {
                Source = source;
                Label = label;
                Method = method;
            }
        }

        private readonly List<Wire> _wires = new List<Wire>();
        private SerializedProperty _expectedTotal;
        private SerializedProperty _onCompleted;
        private GUIStyle _wrap;

        private void OnEnable()
        {
            _expectedTotal = serializedObject.FindProperty(nameof(SafeSiteScenario.expectedTotal));
            _onCompleted = serializedObject.FindProperty(nameof(SafeSiteScenario.onCompleted));
            FindWires();
        }

        public override void OnInspectorGUI()
        {
            _wrap ??= new GUIStyle(EditorStyles.label) { wordWrap = true };
            var scenario = (SafeSiteScenario)target;
            serializedObject.Update();

            EditorGUILayout.HelpBox("Reports score and completion to the SafeSite dashboard. " +
                                    "Start time, duration, language and uploading are automatic.", MessageType.None);

            // 1. Inputs
            Header("1. Call these from your events");
            EditorGUILayout.LabelField("In any event (XRI Select Entered, a trigger, a Timeline Signal): click +, drag " +
                                       $"'{scenario.name}' into the slot, then pick SafeSiteScenario → a method below.", _wrap);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var (method, what, ends) in Methods)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(method, EditorStyles.boldLabel, GUILayout.Width(118));
                        EditorGUILayout.LabelField(what + (ends ? "  ★" : ""), _wrap);
                    }
                }
                EditorGUILayout.LabelField("★ Every scenario needs one of these at its end.", EditorStyles.miniLabel);
            }
            EditorGUILayout.LabelField("From code:  SafeSiteScenario.FindInScene().CompleteAndExit();", EditorStyles.miniLabel);

            DrawWires();

            // 2. Settings
            Header("2. Score");
            EditorGUILayout.PropertyField(_expectedTotal, new GUIContent("Expected Total",
                _expectedTotal.tooltip));
            EditorGUILayout.LabelField(_expectedTotal.intValue > 0
                ? $"Score is out of {_expectedTotal.intValue}: checks the trainee never reached count as wrong."
                : "0 = score is out of the checks answered. Set it to the number of scored checks, or leave 0 if the scenario isn't scored.",
                EditorStyles.miniLabel);

            // 3. Output
            Header("3. Raised by this component");
            EditorGUILayout.LabelField("Fires once, when the scenario completes. Use it to react (end voiceover, scoreboard). " +
                                       "It doesn't complete the scenario: call Complete for that.", _wrap);
            EditorGUILayout.PropertyField(_onCompleted, new GUIContent("On Completed"));

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                DrawLive(scenario);
        }

        private void DrawWires()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Wired in this scene", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Refresh", EditorStyles.miniButton, GUILayout.Width(60)))
                    FindWires();
            }

            foreach (var wire in _wires)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{wire.Label}  →  {wire.Method}", EditorStyles.miniLabel);
                    if (wire.Source != null && GUILayout.Button("Show", EditorStyles.miniButton, GUILayout.Width(44)))
                    {
                        Selection.activeObject = wire.Source;
                        EditorGUIUtility.PingObject(wire.Source);
                    }
                }
            }

            if (!_wires.Exists(w => w.Method == "Complete" || w.Method == "CompleteAndExit"))
                EditorGUILayout.HelpBox("No event in this scene calls Complete or CompleteAndExit, so sessions will show as not completed. " +
                                        "(Calls from code don't show here.)", MessageType.Warning);
        }

        private void DrawLive(SafeSiteScenario scenario)
        {
            Header("Live (Play Mode)");
            EditorGUILayout.LabelField("Score", $"{scenario.CorrectCount} / {scenario.ScoreTotal}");
            EditorGUILayout.LabelField("Completed", scenario.IsCompleted ? "Yes" : "No");
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Correct")) scenario.Correct();
                if (GUILayout.Button("Incorrect")) scenario.Incorrect();
                if (GUILayout.Button("Complete")) scenario.Complete();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Complete And Exit")) scenario.CompleteAndExit();
                if (GUILayout.Button("Exit")) scenario.Exit();
                if (GUILayout.Button("Restart")) scenario.Restart();
            }
            Repaint();
        }

        private static void Header(string text)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
        }

        /// <summary>Every inspector-wired event call in this object's scene that targets this component.</summary>
        private void FindWires()
        {
            _wires.Clear();
            var scenario = (SafeSiteScenario)target;
            if (scenario == null || !scenario.gameObject.scene.IsValid())
                return;

            foreach (var root in scenario.gameObject.scene.GetRootGameObjects())
            {
                foreach (var behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour == null)
                        continue;
                    var so = new SerializedObject(behaviour);
                    var prop = so.GetIterator();
                    while (prop.Next(prop.propertyType == SerializedPropertyType.Generic))
                    {
                        if (prop.name != "m_Target" || prop.propertyType != SerializedPropertyType.ObjectReference)
                            continue;
                        if (!(prop.objectReferenceValue == scenario || prop.objectReferenceValue == scenario.gameObject))
                            continue;

                        // ...m_PersistentCalls.m_Calls.Array.data[i].m_Target
                        var callPath = prop.propertyPath.Substring(0, prop.propertyPath.Length - ".m_Target".Length);
                        var method = so.FindProperty(callPath + ".m_MethodName")?.stringValue;
                        if (string.IsNullOrEmpty(method))
                            continue;
                        var eventPath = callPath.Contains(".m_PersistentCalls")
                            ? callPath.Substring(0, callPath.IndexOf(".m_PersistentCalls", System.StringComparison.Ordinal))
                            : callPath;
                        var eventName = so.FindProperty(eventPath)?.displayName ?? eventPath;
                        var label = behaviour == (Object)scenario
                            ? $"{eventName} (this component)"
                            : $"{behaviour.name} › {behaviour.GetType().Name}.{eventName}";
                        _wires.Add(new Wire(behaviour, label, method));
                    }
                }
            }
        }

        /// <summary>
        /// Selects the scene's SafeSiteScenario, adding one first if it has none. The Scenarios
        /// window's "Add SafeSite Scenario" button calls this; the scene must be open.
        /// </summary>
        public static SafeSiteScenario AddTo(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var existing = root.GetComponentInChildren<SafeSiteScenario>(true);
                if (existing == null)
                    continue;
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                return existing;
            }

            var go = new GameObject("SafeSiteScenario");
            SceneManager.MoveGameObjectToScene(go, scene);
            Undo.RegisterCreatedObjectUndo(go, "Add SafeSite Scenario");
            var added = Undo.AddComponent<SafeSiteScenario>(go);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeObject = go;
            return added;
        }
    }
}
