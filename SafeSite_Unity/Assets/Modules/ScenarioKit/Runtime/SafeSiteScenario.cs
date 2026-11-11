using Modules.ScenarioLauncher.External;
using Modules.SessionAnalyticsService.External;
using UnityEngine;
using UnityEngine.Events;

namespace Modules.ScenarioKit
{
    /// <summary>
    /// Everything a scenario tells SafeSite, in one component. Put one in each scenario scene
    /// (new scenarios get it from the template) and call it from any UnityEvent in the inspector,
    /// e.g. an XRI Select Entered, a trigger or a Timeline Signal, or from code via
    /// <see cref="FindInScene"/>. Start time, duration, language and uploading are automatic;
    /// a scenario only says how the trainee scored and whether they reached the end.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SafeSite/SafeSite Scenario")]
    public class SafeSiteScenario : MonoBehaviour
    {
        private const string LogPrefix = "[SafeSiteScenario]";

        [Tooltip("How many scored checks the scenario has. Checks the trainee never reached count as wrong. " +
                 "0 = the total is just the checks answered.")]
        [Min(0)] public int expectedTotal;

        [Tooltip("Raised once, when Complete or CompleteAndExit is first called.")]
        public UnityEvent onCompleted = new UnityEvent();

        public int CorrectCount { get; private set; }
        public int AnsweredCount { get; private set; }
        public bool IsCompleted { get; private set; }

        /// <summary>The total reported to SafeSite: <see cref="expectedTotal"/> if set, never less than answered.</summary>
        public int ScoreTotal => Mathf.Max(expectedTotal, AnsweredCount);

        /// <summary>The scenario component in the loaded scenes, or null.</summary>
        public static SafeSiteScenario FindInScene()
        {
#if UNITY_2023_1_OR_NEWER
            return FindAnyObjectByType<SafeSiteScenario>();
#else
            return FindObjectOfType<SafeSiteScenario>();
#endif
        }

        // ---- Scoring

        /// <summary>The trainee got a scored check right.</summary>
        public void Correct() => Answer(true);

        /// <summary>The trainee got a scored check wrong.</summary>
        public void Incorrect() => Answer(false);

        /// <summary>For UnityEvents that pass a bool (e.g. a toggle): true = correct.</summary>
        public void Answer(bool correct)
        {
            AnsweredCount++;
            if (correct)
                CorrectCount++;
            Debug.Log($"{LogPrefix} {(correct ? "Correct" : "Incorrect")}: {CorrectCount}/{ScoreTotal}.");
            SendScore();
        }

        /// <summary>Sets the whole score at once, for scenarios that keep their own tally.</summary>
        public void SetScore(int correct, int total)
        {
            CorrectCount = Mathf.Max(0, correct);
            AnsweredCount = Mathf.Max(CorrectCount, total);
            SendScore();
        }

        // ---- Ending

        /// <summary>The trainee reached the end: the dashboard counts the session as completed. Safe to call twice.</summary>
        public void Complete()
        {
            if (IsCompleted)
                return;
            IsCompleted = true;

            var analytics = SessionAnalyticsServiceLocator.FindInScene();
            if (analytics == null)
                Debug.Log($"{LogPrefix} Completed (not recorded: no SafeSite analytics, the scene was opened outside Bootstrap).");
            else
            {
                SendScore();
                analytics.MarkCompleted();
                Debug.Log($"{LogPrefix} Completed.");
            }

            onCompleted.Invoke();
        }

        /// <summary>Completes the scenario and returns to the main menu.</summary>
        public void CompleteAndExit()
        {
            Complete();
            Exit();
        }

        /// <summary>Returns to the main menu. Not completed unless <see cref="Complete"/> was called first.</summary>
        public void Exit()
        {
            var host = Host();
            if (host != null)
                host.ExitScenario();
        }

        /// <summary>Starts the scenario again from the beginning, as a new session.</summary>
        public void Restart()
        {
            var host = Host();
            if (host != null)
                host.RestartScenario();
        }

        // ----

        private void SendScore()
        {
            if (ScoreTotal < 1)
                return;
            SessionAnalyticsServiceLocator.FindInScene()?.ReportScore(CorrectCount, ScoreTotal);
        }

        private static IScenarioHost Host()
        {
            var host = ScenarioHostLocator.FindInScene();
            if (host == null)
                Debug.LogWarning($"{LogPrefix} No scenario host: open Bootstrap and play the scenario from it to exit or restart.");
            else if (host.IsBusy)
            {
                Debug.Log($"{LogPrefix} Ignored: the app is already loading or leaving a scenario.");
                return null;
            }
            return host;
        }
    }
}
