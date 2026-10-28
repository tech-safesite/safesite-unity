using System;

namespace Modules.PingService.External
{
    /// <summary>
    /// Outcome of a single <see cref="IPingService.Ping"/> call.
    /// </summary>
    [Serializable]
    public struct PingResult
    {
        public string Caller;
        public int Count;
        public DateTime PingedAt;

        public PingResult(string caller, int count, DateTime pingedAt)
        {
            Caller = caller;
            Count = count;
            PingedAt = pingedAt;
        }
    }
}
