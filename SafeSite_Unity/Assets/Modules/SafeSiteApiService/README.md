# SafeSiteApiService

The headset's connection to the SafeSite API: HTTP client, per-environment device store, content
download, and loading downloaded Addressables catalogs. Everything else (launcher, analytics) sits
on top of this. Also holds the Addressables helpers the scenario exporter uses, and the API
testbench scene.

Endpoints and the contract with the web repo: [docs/WEB_API_CONTRACT.md](../../../../docs/WEB_API_CONTRACT.md).

## Assemblies

| Folder | Assembly | Contents |
|---|---|---|
| `External/` | `Modules.SafeSiteApiService.External` | `ISafeSiteApiService`, `IDeviceStore`, `IScenarioContent`, `ISafeSiteApiClient`, `ApiError` + `ApiErrorCodes`, request/response models (`Models/DeviceModels.cs`, `AdminModels.cs`), `SafeSiteEnvironments` (+ `Resources/SafeSiteEnvironments.asset`), `ScenarioAddresses.MainScene` (`scenario/main`), `SafeSiteTime` (ISO time, `x.y.z` version helpers), locator, client factory |
| `Internal/` | `Modules.SafeSiteApiService.Internal` | `SafeSiteApiService` (MonoBehaviour), `DeviceStore`, `ScenarioContent`, `Api/` (`DeviceApi`, `AdminApi`, `SupabaseAuth`, `DevJwt`), `Http/` (`ApiHttp`, `LogRedactor`, JSON, `UnityWebRequest` async) |
| `Editor/` | `Modules.SafeSiteApiService.Editor` | `AddressablesContentBuilder` (clean content build of one group, lists output files), `AddressablesValidator` (settings and output rules, `ApplyRecommendedSettings`), `ISafeSitePlatformBuildHook` (per-platform build hook, none implemented yet), `SafeSiteUploadSettings` (50 MB per-file limit) |
| `Testbench/` | `Modules.SafeSiteApiService.Testbench` (not auto-referenced) | `SafeSiteApiTestbench.unity` + panels: admin helper, enroll, config & licence, content, heartbeat, sessions, smoke test, log console |

## Contract highlights (`ISafeSiteApiService`)

| Member | Notes |
|---|---|
| `Environments`, `Environment`, `SetEnvironment(name)`, `EnvironmentChanged` | Each environment keeps its own token and caches |
| `EnrollAsync`, `LoginAsync` | Store the returned device token |
| `SyncConfigAsync(useIfNoneMatch)` | `304` keeps the cached config |
| `SendHeartbeatAsync`, `IsConfigStale(heartbeat)` | |
| `DownloadScenarioAsync(id, progress)` | All files to the version folder, refreshes expired URLs once, records installed, deletes older versions |
| `DeleteLocalScenario(id)` | |
| `EnqueueSessions`, `UploadSessionQueueAsync` | Batches of 500; failures keep the queue |
| `ForgetHeadset()` | Deletes this environment's token, caches, queue and content |
| `Store` (`IDeviceStore`) | Token/ids in PlayerPrefs (`SafeSite.<env>.device.*`), JSON under `persistentDataPath/SafeSite/env/<env>/` (`config.json`, `installed.json`, `session-queue.json`), content under `persistentDataPath/SafeSite/content/<id>/<version>/` |
| `Content` (`IScenarioContent`) | `LoadCatalogAsync(dir)`, `HasMainScene`, `LoadSceneAsync()` (additive, becomes active), `UnloadSceneAsync()`, `Unload()` |
| `Logged` | Every request with secrets redacted (testbench console; `logRequestsToConsole` on the component) |

### ScenarioContent details

- Loads the downloaded catalog from disk and redirects each bundle it references to the downloaded
  file (matched by file name; bundle names are unique per build).
- Resolves keys **only in that catalog**, because every scenario uses `scenario/main` and other
  catalogs (other scenarios, the Editor's own Addressables data) would answer otherwise.
- In the Editor only, swaps each material's shader for the Editor's shader of the same name
  (Android-built shaders draw magenta on PC).

## In the scene

`SafeSiteApiService` on Bootstrap's `SafeSite Services`. Fields: `environments` (defaults to the
Resources asset), `rememberSelectedEnvironment` (never restores Production automatically),
`logRequestsToConsole`. The launcher picks the environment on top of this.

## Known gaps

- Device token in PlayerPrefs; should be the Android Keystore (`TODO` in `DeviceStore.cs`).
- The app doesn't verify the licence token; only the testbench's Config panel does.
