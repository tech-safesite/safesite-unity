# ScenarioLauncher

The client-facing headset flow: sign in with a client's login code, list that client's scenarios,
download one and play it. Also the Bootstrap menu screens, the hidden admin screen and the
in-scenario menu.

Full behaviour (screens, login rules, admin, offline, error messages):
[docs/HEADSET_APP.md](../../../../docs/HEADSET_APP.md).

## Assemblies

| Folder | Assembly | Contents |
|---|---|---|
| `External/` | `Modules.ScenarioLauncher.External` | `IScenarioLauncherService` (+ `LauncherState`, `LauncherScenario`, `HeadsetRegistration`), `ScenarioLauncherServiceLocator`, `IScenarioHost` + `ScenarioHostLocator` |
| `Internal/` | `Modules.ScenarioLauncher.Internal` | `ScenarioLauncherService` (MonoBehaviour) |
| `UI/` | `Modules.ScenarioLauncher.UI` (not auto-referenced) | `Menu/`: `LauncherMenu`, `LoginPanel`, `ScenarioGridPanel`, `ScenarioOptionsPanel`, `ProgressPanel`, `HeadsetPanel`, `AdminUnlock`, `AdminPinPanel`, `AdminPanel`, `AdminSettings`, `RegisterHeadsetPanel`, `GlobalMenu`. Also the legacy flat `ScenarioLauncherUI`. |
| `Editor/` | `Modules.ScenarioLauncher.Editor` | `BootstrapMenuBuilder` (**SafeSite > Launcher > Build Bootstrap Menu**) |
| `Demo/` | — | `ScenarioLauncherDemo.unity`: the old debug UI against Local |

## Contract

```csharp
var launcher = ScenarioLauncherServiceLocator.FindInScene();
launcher.Changed += Render;                 // raised on the main thread for any state change
await launcher.SignInAsync("ABCD-EFGH");    // SignedOut → SigningIn → Browsing (or Error set)
await launcher.DownloadAsync(id);           // Browsing → Downloading → Browsing (Progress, ProgressText)
await launcher.PlayAsync(id);               // → Loading → Playing (scene "scenario/main" loaded)
await launcher.StopAsync();                 // Playing → Browsing
launcher.SignOut();                         // keeps the headset's client and downloads
```

Admin operations: `RegisterHeadsetAsync(code)`, `SetEnvironment(name)`, `ForgetHeadset()`,
`DeleteDownloads()`, plus `IsBusy`, `IsRegistered`, `HeadsetId`, `EnvironmentName(s)`.

`IScenarioHost` is implemented by the app (`TrainingScenarioLoader`), not this module: it owns
fades, player reset and the menu, and the in-scenario menu and `SafeSiteScenario` call it to
restart, exit, switch, or open an overlay.

## In the scene

On Bootstrap's `--Singletons / Persistant --/SafeSite Services` next to `SafeSiteApiService`:

| Field | Default | Meaning |
|---|---|---|
| `environmentName` | Bootstrap: `Production` | `Local`, `Demo` or `Production`. An admin's choice on the headset overrides it. |
| `serialPerCode` | on | On PC/Editor, one serial per login code so one PC can be a headset for several clients. Ignored on Android. |

The UI lives on `MenuItems/MainMenuCanvas/LauncherMenu` and `GlobalMenu`. Regenerate the launcher
screens with **Build Bootstrap Menu**; hand edits to generated screens are lost on a rebuild.

Inspector settings worth knowing: `AdminPinPanel.pin` (change per client build),
`AdminPinPanel.triesBeforeLock` (5) / `lockSeconds` (30), `AdminUnlock.tapsNeeded` (5),
`AdminPanel.confirmSeconds` (4), `ScenarioGridPanel.icons` and `.builtInScenarios`,
`GlobalMenu.distance` (1.5 m) / `heightOffset` / `itemsPerPage` (5).

## Depends on

`SafeSiteApiService.External` (Internal); `SessionAnalyticsService.External` (UI, for the
language choice).
