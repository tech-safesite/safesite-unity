using System;
using Modules.PingService.External;
using UnityEngine;

namespace Modules.PingService.Internal
{
    /// <summary>
    /// Scene-owned implementation. Put this component on a loaded scene object
    /// (or a bootstrap that survives load) for runtime code to resolve it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PingService : MonoBehaviour, IPingService
    {
        private const string LogPrefix = "[PingService]";

        private int _pingCount;
        private DateTime? _lastPingAt;

        public int PingCount => _pingCount;

        public DateTime? LastPingAt => _lastPingAt;

        public event Action<PingResult> Pinged;

        public PingResult Ping(string caller)
        {
            _pingCount++;
            _lastPingAt = DateTime.UtcNow;

            var result = new PingResult(caller, _pingCount, _lastPingAt.Value);
            Debug.Log($"{LogPrefix} Ping #{result.Count} from '{caller}'.");

            Pinged?.Invoke(result);
            return result;
        }
    }
}
