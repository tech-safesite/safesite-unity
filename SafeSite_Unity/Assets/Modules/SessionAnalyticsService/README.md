# SessionAnalyticsService

Training analytics for the dashboard. Records one session every time a **downloaded** scenario
plays, keeps a queue on disk, uploads it, and sends the headset's heartbeat. Works offline.

Behaviour in context: [docs/HEADSET_APP.md § 8](../../../../docs/HEADSET_APP.md#8-sessions-heartbeats-and-location).

## Contract (`External/ISessionAnalyticsService`)

| Member | Meaning |
|---|---|
| `IsRecording` | A downloaded scenario is playing and being recorded |
| `PendingUploads` | Sessions on this headset not yet accepted by SafeSite |
| `LastHeartbeat`, `LastError`, `Changed` | Status for UI/diagnostics |
| `SetLanguage(language)` | The trainee's language choice for the next scenario (options screen) |
| `ReportScore(correct, total)` | Called via `SafeSiteScenario`; ignored when nothing is recording; needs `0 ≤ correct ≤ total`, `total ≥ 1` |
| `MarkCompleted()` | Called via `SafeSiteScenario.Complete`; sessions left early stay not completed |
| `SyncNowAsync()` | Heartbeat + queue upload now; never throws |

Scenarios should call `SafeSiteScenario`, not this service directly.

## How it works (`Internal/SessionAnalyticsService`)

- Subscribes to `IScenarioLauncherService.Changed`. `Playing` starts a recording (scenario id,
  installed version, language, start time); leaving `Playing` ends it (exit, restart, switch).
  `OnApplicationQuit` also ends it.
- An ended session becomes a `SessionRecord` with a new `deviceSessionId` GUID, duration from
  realtime (clamped to 24 h), completed flag and score, and goes into the API service's on-disk
  queue. Then a sync runs (not on quit).
- **Sync** = heartbeat, then upload the queue. One at a time; a request made meanwhile runs once
  more afterwards. Network/5xx failures keep the queue; rejected sessions are logged and dropped.
- **Heartbeat** sends app version, installed scenarios limited to those in the current config, and
  a device location if `IDeviceLocationService` returns a fresh enough fix. A stale
  `configVersion` triggers a launcher refresh, but only while the launcher is browsing and idle.
- Switching environment drops any in-progress recording (a session can't span environments).

## Inspector

| Field | Default |
|---|---|
| `heartbeatIntervalSeconds` | 300 |
| `firstHeartbeatDelaySeconds` | 5 |
| `locationWaitSeconds` | 15 (never holds up an upload longer) |
| `maxFixAgeHours` | 6 |

## Needs in the scene

`ISafeSiteApiService` and `IScenarioLauncherService` (else it logs an error and stays off).
`IDeviceLocationService` is optional; without it the API uses the request IP for the map.
All four sit on Bootstrap's `SafeSite Services`.

## Depends on

`SafeSiteApiService.External`, `ScenarioLauncher.External`, `DeviceLocationService.External`.
