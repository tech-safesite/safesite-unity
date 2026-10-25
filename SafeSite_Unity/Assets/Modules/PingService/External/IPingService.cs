using System;

namespace Modules.PingService.External
{
    /// <summary>
    /// Contract other code may depend on. Implementations are scene-owned MonoBehaviours
    /// that live in Modules.PingService.Internal and must never be imported directly.
    /// </summary>
    public interface IPingService
    {
        /// <summary>Total number of pings received since the service woke.</summary>
        int PingCount { get; }

        /// <summary>When the most recent ping arrived, or null if none has.</summary>
        DateTime? LastPingAt { get; }

        /// <summary>Raised after each successful ping.</summary>
        event Action<PingResult> Pinged;

        /// <summary>Records a ping from <paramref name="caller"/> and returns the result.</summary>
        PingResult Ping(string caller);
    }
}
