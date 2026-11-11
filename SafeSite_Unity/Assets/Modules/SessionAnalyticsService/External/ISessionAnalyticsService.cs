using System;
using System.Threading.Tasks;

namespace Modules.SessionAnalyticsService.External
{
    /// <summary>
    /// Training analytics for the dashboard. Records one session each time a downloaded scenario
    /// plays (start time, duration, language, completed, score), queues it on the headset and
    /// uploads the queue; also sends the headset's heartbeat (app version, installed scenarios).
    /// Works offline: the queue is kept on disk and sent the next time the headset is online.
    /// Resolve with <see cref="SessionAnalyticsServiceLocator"/>.
    /// </summary>
    public interface ISessionAnalyticsService
    {
        /// <summary>True while a downloaded scenario is playing and being recorded.</summary>
        bool IsRecording { get; }

        /// <summary>Sessions recorded on this headset that haven't reached SafeSite yet.</summary>
        int PendingUploads { get; }

        /// <summary>Local time of the last successful heartbeat, or null.</summary>
        DateTime? LastHeartbeat { get; }

        /// <summary>Why the last upload or heartbeat failed, or null after a success.</summary>
        string LastError { get; }

        /// <summary>Raised on the main thread whenever any of the above changes.</summary>
        event Action Changed;

        /// <summary>The language the trainee picked for the next scenario they start.</summary>
        void SetLanguage(string language);

        /// <summary>
        /// Called by a scenario that keeps score, e.g. at its end-of-session scoreboard.
        /// Ignored when nothing is being recorded. Never linked to a person.
        /// </summary>
        void ReportScore(int correct, int total);

        /// <summary>Called by a scenario when the trainee reaches its end. Sessions left early stay not completed.</summary>
        void MarkCompleted();

        /// <summary>Sends a heartbeat and uploads the queue now. Never throws; see <see cref="LastError"/>.</summary>
        Task SyncNowAsync();
    }
}
