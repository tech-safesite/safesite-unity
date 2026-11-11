# DeviceLocationService

The headset's own position from Android location services, so the dashboard map can show where
headsets are. Quest and PICO have no GPS, so often there is **no fix**; callers treat that as
normal. When the heartbeat goes without a location, the API estimates one from the request IP.

## Contract (`External/IDeviceLocationService`)

```csharp
DeviceFix? LastFix { get; }      // latitude, longitude, accuracy (m, optional), recorded at (UTC)
string LastError { get; }        // permission denied, location off, timed out… or null
event Action Changed;
Task<DeviceFix?> RefreshAsync(CancellationToken ct = default);   // never throws for a missing fix
```

`RefreshAsync` takes a new fix unless one was attempted recently, asking for the location
permission the first time, and returns the newest fix (possibly older) or null.

## Implementation (`Internal/DeviceLocationService`)

Starts Unity's `LocationService`, reads one fix, and stops it straight away so location isn't left
running. Asks for fine and coarse location together, at most once per app run (Android stops
showing the prompt after repeated denials anyway); either one granted is enough. In the Editor
there's no permission step.

| Field | Default | Meaning |
|---|---|---|
| `minMinutesBetweenAttempts` | 15 | Throttle between attempts |
| `startTimeoutSeconds` | 20 | How long to wait for the service to start |
| `permissionTimeoutSeconds` | 30 | How long to wait for the permission dialog |

The Android permissions (`ACCESS_COARSE_LOCATION`, `ACCESS_FINE_LOCATION`) are declared in
`Assets/Editor/HeadsetBuild/ManifestTemplates/AndroidManifest.Quest.xml` and `.Pico.xml`.

## Used by

`SessionAnalyticsService` before each heartbeat (waits at most `locationWaitSeconds`, ignores fixes
older than `maxFixAgeHours`). Lives on Bootstrap's `SafeSite Services`.
