using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.DeviceLocationService.External
{
    /// <summary>
    /// The headset's own position from Android location services. Quest and PICO headsets have
    /// no GPS, so a fix may never come; callers treat "no fix" as normal, not an error.
    /// </summary>
    public interface IDeviceLocationService
    {
        /// <summary>The most recent fix, or null if none has been taken since start-up.</summary>
        DeviceFix? LastFix { get; }

        /// <summary>Why the last attempt failed (permission denied, location off, timed out), or null.</summary>
        string LastError { get; }

        /// <summary>Raised when a new fix arrives or an attempt fails.</summary>
        event Action Changed;

        /// <summary>
        /// Takes a new fix unless one was attempted recently, asking for the location permission
        /// the first time. Returns the newest fix (possibly older), or null if there is none.
        /// Never throws for a missing fix.
        /// </summary>
        Task<DeviceFix?> RefreshAsync(CancellationToken ct = default);
    }
}
