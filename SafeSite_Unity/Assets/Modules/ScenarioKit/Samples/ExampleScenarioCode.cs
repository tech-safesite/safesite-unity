using UnityEngine;

namespace Modules.ScenarioKit.Samples
{
    /// <summary>
    /// Dummy scenario logic: the code way. Same result as <see cref="ExampleScenarioSteps"/>,
    /// for scenario scripts that already drive their own flow (like a state machine).
    /// Your scenario's scripts are in Assembly-CSharp, which sees Modules.ScenarioKit automatically:
    /// add <c>using Modules.ScenarioKit;</c> and call the methods.
    /// </summary>
    [AddComponentMenu("SafeSite/Examples/Example Scenario Code")]
    public class ExampleScenarioCode : MonoBehaviour
    {
        // Optional: drag it in. Otherwise it is found in the loaded scenes.
        [SerializeField] private SafeSiteScenario scenario;

        private SafeSiteScenario Scenario => scenario != null ? scenario : scenario = SafeSiteScenario.FindInScene();

        public void HazardSpotted() => Scenario.Correct();
        public void HazardMissed() => Scenario.Incorrect();
        public void QuizAnswered(bool correct) => Scenario.Answer(correct);
        public void ReachedEnd() => Scenario.CompleteAndExit();
    }

#if UNITY_EDITOR
    [UnityEditor.CustomEditor(typeof(ExampleScenarioCode))]
    internal class ExampleScenarioCodeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            UnityEditor.EditorGUILayout.HelpBox(
                "Example: calls SafeSiteScenario from code, e.g. SafeSiteScenario.FindInScene().CompleteAndExit(). " +
                "Open ExampleScenarioCode.cs to see it.", UnityEditor.MessageType.Info);
            DrawDefaultInspector();

            var code = (ExampleScenarioCode)target;
            using (new UnityEditor.EditorGUI.DisabledScope(!Application.isPlaying))
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Spot hazard")) code.HazardSpotted();
                if (GUILayout.Button("Miss hazard")) code.HazardMissed();
                if (GUILayout.Button("Reach end")) code.ReachedEnd();
            }
        }
    }
#endif
}
