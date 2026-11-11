# Architecture

How the SafeSite Unity project is put together: who owns which folders, which scene is the app,
how the modules depend on each other, and what happens from power-on to a recorded session.

- [1. Two kinds of code](#1-two-kinds-of-code)
- [2. Folder ownership](#2-folder-ownership)
- [3. The Bootstrap scene is the app](#3-the-bootstrap-scene-is-the-app)
- [4. Modules](#4-modules)
- [5. Runtime flow, end to end](#5-runtime-flow-end-to-end)
- [6. Built-in vs downloaded scenarios](#6-built-in-vs-downloaded-scenarios)
- [7. Data kept on the headset](#7-data-kept-on-the-headset)
- [8. Conventions](#8-conventions)

---

## 1. Two kinds of code

| | App shell and scenarios | Modules |
|---|---|---|
| Where | `Assets/_AppCore/`, `Assets/_Scenarios/`, a few scripts in `Assets/` (e.g. `TrainingScenarioLoader.cs`) | `Assets/Modules/<Name>/` |
| Assembly | `Assembly-CSharp` (no asmdef) | One asmdef per folder (`External`, `Internal`, `UI`, `Editor`, …) |
| Style | Unity-style MonoBehaviours and singletons (`TrainingScenarioLoader.Instance`, `ScreenFadeController.Instance`) | Interface + locator + implementation, no singletons |
| Written by | Stephen (scenario/front-end dev) | Suriya (tools/backend), with Claude |

`Assembly-CSharp` sees every module assembly marked `autoReferenced` automatically, so app and
scenario scripts can call `SafeSiteScenario`, `ScenarioLauncherServiceLocator` and friends with a
`using` and nothing else. Module assemblies **cannot** see `Assembly-CSharp`; when a module needs
something from the app, the app implements a module interface instead (see `IScenarioHost` below).

## 2. Folder ownership

```
Assets/
├─ _AppCore/                     APP SHELL (Stephen) – keep his structure, improve it in place
│  ├─ Scenes/Bootstrap.unity     the one start scene; everything runs inside it
│  ├─ Player/--Player--.prefab   the one XR rig (README.md beside it)
│  ├─ Scripts/
│  │  ├─ XR/                     InteractionStateManager, XRPlayerPositioner, PlatformControllerModel,
│  │  │                          RecenterHandler, AttachPointSwapper, SafeSiteDropOnlySocketInteractor
│  │  ├─ Audio/                  SafeSiteAudioManager, VoiceoverHandler/Data/Entry, SubtitleHandler
│  │  ├─ UI/                     TeleportFadeHandler
│  │  └─ Utility/                ScreenFadeController, CameraShaker, TweenOnEnable/OnHover,
│  │                             SafeSiteFollowUserCenter, AnimationEventRelay
│  └─ …                          menu art, fonts, prefabs
├─ _Scenarios/                   SCENARIOS – one folder per scenario
│  ├─ FallingObjects/            falling-objects.asset + Scenes/ Scripts/ Prefabs/ Models/ …
│  ├─ WorkingAtHeight/           working-at-height.asset + …
│  └─ Test/                      a small test scenario (Scenes/Test.unity)
├─ Modules/                      SERVICES AND TOOLS (Suriya) – see section 4
├─ Editor/HeadsetBuild/          the Quest/PICO APK build tool (see BUILDING.md)
├─ AddressableAssetsData/        Addressables settings and groups (one group per scenario)
├─ TrainingScenarioLoader.cs     plays scenarios around the menu; implements IScenarioHost
└─ SafeSiteMenuController.cs     Stephen's main-menu controller
```

The scenario folder layout (`Animations, Audio, LightingData, Materials, Models, Prefabs, Scenes,
Scripts, Textures, _temp`) is created by **SafeSite > Scenarios > New scenario**. A scenario's
scripts belong in its own `Scripts/` folder, not the `Assets/` root.

**Not part of the architecture** (arrived from `main` on 2026-11-11, to be migrated and deleted;
don't build on them): `_AppCore/Scenes/Bootstrap_MetaShowcase.unity`, the OVR rig prefabs in
`_AppCore/Player/`, `_AppCore/Prefabs/TeleportNode_Meta.prefab`, Meta Interaction SDK prefabs in
scenarios, and `TrainingScenarioLoader.LoadScenarioSingle`. Details in
[DECISIONS.md](DECISIONS.md#known-issues). The scenario's
`ScenarioDefinition` asset (`<slug>.asset`) sits at the folder root.

## 3. The Bootstrap scene is the app

> **This is the final architecture (decided 2026-11-11).** One start scene (Bootstrap), one rig
> (`--Player--`, XR Interaction Toolkit on OpenXR), every scenario loaded additively on top. Work
> that doesn't fit it gets migrated, not supported alongside it. See [DECISIONS.md](DECISIONS.md).

`Assets/_AppCore/Scenes/Bootstrap.unity` is scene 0 and the **only** scene in the build. Scenario
scenes are loaded **additively** into it and unloaded again; Bootstrap is never unloaded or
replaced. That is why:

- Scenario scenes must **not** contain any rig (XRI `XROrigin` or Meta `OVRCameraRig`), camera or
  EventSystem. Export refuses a scene with an `XROrigin` in it; the check doesn't catch
  `OVRCameraRig` yet (known issue).
- Scenario scripts can rely on Bootstrap's singletons (`ScreenFadeController`, `SafeSiteAudioManager`,
  `InteractionStateManager`, …) being there.
- Opening a scenario scene on its own in the Editor works for layout, but nothing SafeSite-related
  (sessions, exit, restart) happens. `SafeSiteScenario` logs that and carries on.

Root objects in Bootstrap:

| Object | What's on it |
|---|---|
| `--Player--` (prefab instance) | The XR rig. One intended override: Left Hand → `MetaSystemGestureDetector.menuPressed` → `GlobalMenu.Toggle`. |
| `--Singletons / Persistant --/SafeSite Services` | `SafeSiteApiService`, `ScenarioLauncherService` (environment **Production**), `SessionAnalyticsService`, `DeviceLocationService` |
| `--Singletons / Persistant --/ScenarioLoader` | `TrainingScenarioLoader` (the `IScenarioHost`) |
| `MenuItems/MainMenuCanvas` | Stephen's designed main menu, plus the launcher screens added by **Build Bootstrap Menu**; `LauncherMenu` drives it |
| `GlobalMenu` | The in-scenario menu (left controller menu button / left-hand menu gesture / Esc) |
| `StartingTransform` | Where the player is put at start and after every scenario |

> **Environment.** `ScenarioLauncherService.environmentName` is saved as **Production** in Bootstrap.
> For a local test change it to `Local` and change it back before committing. An admin on a
> headset can also switch environment from the admin screen, which is remembered on that headset
> and wins over the scene value.

## 4. Modules

Each module is a folder under `Assets/Modules/` with its own README.

| Module | Kind | Purpose |
|---|---|---|
| `SafeSiteApiService` | runtime + editor | HTTP client for the SafeSite API, per-environment device store, content download, Addressables catalog loading. Also a testbench scene and content-build helpers used by the exporter. |
| `ScenarioLauncher` | runtime + UI + editor | Sign in with a login code, list/download/play a client's scenarios. The Bootstrap menu screens, the admin screen and the in-scenario `GlobalMenu`. `IScenarioHost` contract. |
| `SessionAnalyticsService` | runtime | Records a session for every downloaded scenario played, queues it on disk, uploads it; sends heartbeats. |
| `DeviceLocationService` | runtime | One Android location fix per refresh, for the dashboard map. |
| `ScenarioKit` | runtime + editor | `SafeSiteScenario`, the one component a scenario talks to (score, complete, exit, restart). Samples. |
| `ScenarioAuthoring` | editor | **SafeSite > Scenarios** window: create scenarios, size check, export a package zip. CLI for CI. |
| `SceneRig` | editor | **SafeSite > Add XR Rig To Active Scene**, and the "no rig in a scenario" export check. |
| `PingService` | runtime | Template only. Copy it to start a new module; don't use or delete it. |

### Dependency graph (assemblies)

Arrows mean "references". Every runtime reference goes to an `External` (contract) assembly, never
to another module's `Internal`.

```
                     ┌──────────────────────────────┐
                     │ SafeSiteApiService.External   │◄───────────────┐
                     └──────────────▲───────────────┘                 │
                                    │                                 │
  ScenarioLauncher.Internal ────────┤                                 │
          │                         │                                 │
          ▼                         │                                 │
  ScenarioLauncher.External ◄───────┼────── ScenarioLauncher.UI       │
          ▲      ▲                  │              │                  │
          │      │                  │              ▼                  │
          │      └──── SessionAnalyticsService.Internal ──► SessionAnalyticsService.External
          │                         │      │                          ▲
          │                         │      └──► DeviceLocationService.External ◄── DeviceLocationService.Internal
          │                         │                                 │
          └────────────── ScenarioKit (runtime) ──────────────────────┘

Editor only:
  ScenarioAuthoring.Editor ──► SafeSiteApiService.External, SafeSiteApiService.Editor,
                               SceneRig.Editor, ScenarioKit, ScenarioKit.Editor
  ScenarioLauncher.Editor  ──► ScenarioLauncher.External, ScenarioLauncher.UI
  ScenarioKit.Editor       ──► ScenarioKit
```

`ScenarioLauncher.UI`, `ScenarioLauncher.Editor`, `ScenarioKit.Samples`, the testbench and the
tests are **not** auto-referenced, so app code cannot accidentally depend on them.

### How services find each other

There is no DI container. Each `External` assembly has a locator:

```csharp
var api      = SafeSiteApiServiceLocator.FindInScene();       // ISafeSiteApiService
var launcher = ScenarioLauncherServiceLocator.FindInScene();  // IScenarioLauncherService
var analytics= SessionAnalyticsServiceLocator.FindInScene();  // ISessionAnalyticsService
var host     = ScenarioHostLocator.FindInScene();             // IScenarioHost (TrainingScenarioLoader)
```

They scan loaded MonoBehaviours for the interface, so a service exists only if its `Internal`
component is on an object in a loaded scene (Bootstrap's `SafeSite Services`). Call them once
(`Awake`/`Start`) and keep the reference; don't call them every frame.

## 5. Runtime flow, end to end

```
App start (Bootstrap)
 │
 ├─ SafeSiteApiService       loads SafeSiteEnvironments (Production / Demo / Local) and the device
 │                           store for the chosen environment (token, cached config, downloads)
 ├─ ScenarioLauncherService  picks the environment (admin override on this headset > scene value),
 │                           then: has a token AND was signed in last time? → Browsing (refresh list)
 │                                                                  otherwise → SignedOut
 ├─ SessionAnalyticsService  first heartbeat after 5 s, then every 5 min; uploads any queued sessions
 └─ LauncherMenu             after the splash shows the login keypad or the scenario grid
 │
 ▼
Login keypad  ── code ──► ScenarioLauncherService.SignInAsync
 │                          ├─ headset already this client's? check code against cached
 │                          │  config hash (works offline) after a sync attempt
 │                          └─ else POST /v1/device/login → links/moves headset, new token
 │                          then GET /v1/device/config → scenarios, licence, login-code hash
 ▼
Scenario grid (4 per page) → Options (title, details, language) → Start
 │
 ▼
TrainingScenarioLoader.PlayDownloaded(id)
 ├─ launcher.DownloadAsync  GET /v1/device/scenarios/{id}/content → signed URLs → files to
 │                          persistentDataPath/SafeSite/content/<id>/<version>/  (menu shows progress)
 ├─ fade to black (1 s)
 ├─ launcher.PlayAsync      load that folder's Addressables catalog, load "scenario/main" additively,
 │                          make it the active scene            → LauncherState.Playing
 │                                                               → analytics starts a recording
 └─ fade in (2 s)
 │
 ▼
Scenario runs. Its scripts call SafeSiteScenario: Correct()/Incorrect()/SetScore(), Complete(),
CompleteAndExit(), Exit(), Restart(). Left menu button opens GlobalMenu (resume/restart/switch/exit).
 │
 ▼
Exit (or switch/restart/quit)
 ├─ fade to black, unload scene, release catalog, player back to StartingTransform,
 │  interaction mode back to UIAndTeleport, menu visible, fade in
 └─ analytics: session queued to disk → POST /v1/device/sessions now and on every heartbeat
```

## 6. Built-in vs downloaded scenarios

There are two ways a scenario scene gets into the app. Both end up loaded additively into Bootstrap.

| | Built-in | Downloaded |
|---|---|---|
| Where the scene comes from | Addressables group **included in the app build**, local paths, team's own address (e.g. `scenario_falling_objects_scene`) | A scenario package imported in the dashboard, downloaded at runtime |
| Who lists it | `ScenarioGridPanel.builtInScenarios` (set in the Inspector on the Bootstrap menu) | The client's config from the API |
| When it's listed | Development builds and the Editor; other builds only if an admin turns on **Built-in scenarios** on the admin screen | After signing in |
| How it loads | `TrainingScenarioLoader.LoadScenario(address)` → `Addressables.LoadSceneAsync` | `TrainingScenarioLoader.PlayDownloaded(id)` → launcher → `ScenarioContent.LoadSceneAsync("scenario/main")` |
| Sessions recorded? | **No** (analytics only follows the launcher) | Yes |
| Use it for | Scenario developers testing in the app without exporting | Everything clients see |

The exporter makes the same group work for both: during an export it temporarily gives the group
the package contract (`scenario/main` address, remote paths, split bundles) and then restores the
team's settings. See [SCENARIO_AUTHORING.md](SCENARIO_AUTHORING.md#how-export-treats-your-addressables-group).

**Bundles cannot carry code.** Every script a scenario uses (its `Scripts/` folder) is compiled
into the app. A scenario that adds or changes scripts needs a new app build on headsets before
its package will work. The export warns about this, listing the scripts.

## 7. Data kept on the headset

All per environment, so Production, Demo and Local never mix.

| What | Where | Cleared by |
|---|---|---|
| Device token, headset id/label, client name, platform | `PlayerPrefs` (`SafeSite.<env>.device.*`). Test quality: should move to the Android Keystore (TODO in `DeviceStore.cs`) | Admin → Forget headset; an invalid-token or deactivated response |
| "Signed in" marker (hash of the login code) | `PlayerPrefs` `SafeSite.Launcher.<env>.<headsetId>.Login` | Sign out; login code changed in dashboard |
| Environment override | `PlayerPrefs` `SafeSite.Launcher.EnvironmentOverride` | Picking another environment |
| Built-in scenarios switch | `PlayerPrefs` `SafeSite.Admin.BuiltInScenarios` | Admin screen |
| Config cache, installed list, session queue | `persistentDataPath/SafeSite/env/<env>/*.json` | Forget headset |
| Downloaded scenario files | `persistentDataPath/SafeSite/content/<scenarioId>/<version>/` | Newer version download (old folder deleted); Admin → Delete downloads |

## 8. Conventions

- **New feature = new module.** Copy `Modules/PingService`, rename types, namespaces and asmdefs,
  replace the ping API. `External` references nothing; `Internal` references its own `External`
  and other modules' `External` only (by assembly name). See [Modules/PingService/README.md](../SafeSite_Unity/Assets/Modules/PingService/README.md).
- Public surface of a module is operations, state getters and events. Parsing, networking, prefabs
  and Unity wiring stay `Internal`.
- `[DisallowMultipleComponent]` on service components; log lines start with `[ServiceName]`.
- No new `static Instance` singletons in modules (the app shell has its own, that's fine).
- Wording shown to people: clients type a **login code**; SafeSite staff use a **registration
  code** (the API calls it an enrollment code). Never "access code" or "set-up code".
- Edit the `--Player--` prefab, never the Bootstrap copy (see the Player README).
- Interactions use the XR Interaction Toolkit (XRI grab, sockets, teleport areas/anchors), not
  the Meta Interaction SDK. Vendor-specific code only behind `VR_META`/`VR_PICO`.
- Scenarios load additively; never `LoadSceneMode.Single`, never `SceneManager` calls from a
  scenario.
- Build APKs with the **Build** menu, never File > Build Settings.
