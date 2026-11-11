using System;

namespace Modules.DeviceLocationService.External
{
    /// <summary>One position reading from the device.</summary>
    [Serializable]
    public struct DeviceFix
    {
        public double Latitude;
        public double Longitude;

        /// <summary>Horizontal accuracy radius in metres, or null if the device didn't say.</summary>
        public double? AccuracyMeters;

        /// <summary>When the device took the reading (UTC).</summary>
        public DateTimeOffset RecordedAt;

        public DeviceFix(double latitude, double longitude, double? accuracyMeters, DateTimeOffset recordedAt)
        {
            Latitude = latitude;
            Longitude = longitude;
            AccuracyMeters = accuracyMeters;
            RecordedAt = recordedAt;
        }
    }
}
