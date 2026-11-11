using UnityEngine;
using UnityEngine.Events;

namespace Modules.ScenarioKit.Samples
{
    /// <summary>
    /// Dummy scenario logic: the inspector way (no SafeSite code in your script).
    ///
    /// Your script only raises its own events. In the inspector, each event is wired to the
    /// scene's SafeSiteScenario: click +, drag ScenarioRoot in, pick SafeSiteScenario → method.
    /// In ScenarioKitExample.unity they are already wired:
    ///   On Hazard Spotted → SafeSiteScenario.Correct
    ///   On Hazard Missed  → SafeSiteScenario.Incorrect
    ///   On Reached End    → SafeSiteScenario.CompleteAndExit
    /// and SafeSiteScenario's own On Completed → ExampleScenarioSteps.ShowEndMessage.
    ///
    /// In Play Mode, use the buttons on this component to fake a trainee.
    /// </summary>
    [AddComponentMenu("SafeSite/Examples/Example Scenario Steps")]
    public class ExampleScenarioSteps : MonoBehaviour
    {
        [Tooltip("Wire to SafeSiteScenario.Correct.")]
        public UnityEvent onHazardSpotted = new UnityEvent();

        [Tooltip("Wire to SafeSiteScenario.Incorrect.")]
        public UnityEvent onHazardMissed = new UnityEvent();

        [Tooltip("Wire to SafeSiteScenario.CompleteAndExit (or Complete to stay in the scene).")]
        public UnityEvent onReachedEnd = new UnityEvent();

        // In a real scenario these would be called by XRI interactables, triggers, timelines...
        public void HazardSpotted() => onHazardSpotted.Invoke();
        public void HazardMissed() => onHazardMissed.Invoke();
        public void ReachedEnd() => onReachedEnd.Invoke();

        /// <summary>Wired to SafeSiteScenario's On Completed: shows reacting to completion.</summary>
        public void ShowEndMessage() => Debug.Log("[ExampleScenarioSteps] Scenario completed: play the end voiceover here.");
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(ExampleScenarioSteps))]
    internal class ExampleScenarioStepsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUILayout.HelpBox(
                "Example: this script raises its own events; the inspector wires them to SafeSiteScenario. " +
                "No SafeSite code needed in your script.", UnityEditor.MessageType.Info);
            DrawDefaultInspector();

            var steps = (ExampleScenarioSteps)target;
            using (new UnityEditor.EditorGUI.DisabledScope(!Application.isPlaying))
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Spot hazard")) steps.HazardSpotted();
                if (GUILayout.Button("Miss hazard")) steps.HazardMissed();
                if (GUILayout.Button("Reach end")) steps.ReachedEnd();
            }
            if (!Application.isPlaying)
                UnityEditor.EditorGUILayout.LabelField("Enter Play Mode to use the buttons.", UnityEditor.EditorStyles.miniLabel);
        }
    }
#endif
}
