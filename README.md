# SafeSite Unity

The SafeSite headset app (Meta Quest and PICO, Unity 6000.0.65f1, OpenXR) and the Editor tools
that turn training scenarios into downloadable packages.

The Unity project is the `SafeSite_Unity/` folder. The backend API and dashboard live in a separate
repo, `safesite-web` (see [docs/WEB_API_CONTRACT.md](docs/WEB_API_CONTRACT.md)).

## What the app does, in one paragraph

A trainee puts on a headset. The app opens on the **Bootstrap** scene: splash, then a keypad
asking for their company's **login code**. The code links the headset to that company (the
"client") in the SafeSite dashboard and lists the scenarios the client has bought. The trainee
picks one; the app downloads it (Addressables bundles from Supabase storage), fades to black,
loads the scenario scene into Bootstrap and fades back in. The scenario reports score and
completion through one component (`SafeSiteScenario`); the app records a session (start, duration,
language, score, completed) and uploads it to the dashboard, offline-safe. A menu on the left
controller lets the trainee restart, switch or leave a scenario at any time.

## Start here

| If you are… | Read |
|---|---|
| New to the project | [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): folders, scenes, modules, how a scenario runs |
| Building a scenario | [docs/SCENARIO_AUTHORING.md](docs/SCENARIO_AUTHORING.md): create, wire score/completion, export the zip |
| Working on the headset menus or sign-in | [docs/HEADSET_APP.md](docs/HEADSET_APP.md): launcher, login, admin screen, in-scenario menu, analytics |
| Building an APK | [docs/BUILDING.md](docs/BUILDING.md): the headset build tool, OpenXR features, versions |
| Testing end to end on your PC | [docs/LOCAL_TESTING.md](docs/LOCAL_TESTING.md): local API, demo setup, tests |
| Touching the API | [docs/WEB_API_CONTRACT.md](docs/WEB_API_CONTRACT.md): endpoints, package format, where the truth lives |
| Wondering "why is it like this?" | [docs/DECISIONS.md](docs/DECISIONS.md): decisions, what was deferred, known issues |
| Changing the XR rig | [SafeSite_Unity/Assets/_AppCore/Player/README.md](SafeSite_Unity/Assets/_AppCore/Player/README.md) |

Every module under `SafeSite_Unity/Assets/Modules/` has its own `README.md` next to its code.

## Quick start (first hour)

1. Open `SafeSite_Unity/` in Unity **6000.0.65f1** with **Android Build Support** installed.
2. Open `Assets/_AppCore/Scenes/Bootstrap.unity`. This is the app. Press Play.
   - For hands and controllers in Play Mode use Quest Link (**Build > Set SDK > Configure Quest
     Link (PC)** once). Esc stands in for the left controller's menu button.
3. Look at the menus that matter:
   - **SafeSite > Scenarios**: everything for scenario authors (create, check sizes, build package).
   - **SafeSite > Launcher > Build Bootstrap Menu**: regenerates the launcher screens in Bootstrap.
   - **SafeSite > Add XR Rig To Active Scene**: adds `--Player--` to a non-scenario scene.
   - **Build > Quest / Pico / All**: the only supported way to build an APK.
4. To see the whole loop (sign in, download, play, session in the dashboard) without touching
   production, follow [docs/LOCAL_TESTING.md](docs/LOCAL_TESTING.md).

## Repo layout (top level)

```
safesite-unity/
├─ README.md                  this file
├─ docs/                      project documentation (start with ARCHITECTURE.md)
└─ SafeSite_Unity/            the Unity project
   ├─ Assets/
   │  ├─ _AppCore/            the app shell: Bootstrap scene, --Player-- rig, shared scripts
   │  ├─ _Scenarios/          one folder per training scenario (FallingObjects, WorkingAtHeight, Test)
   │  ├─ Modules/             self-contained services and tools (two-asmdef modules)
   │  ├─ Editor/HeadsetBuild/ the Quest/PICO build tool
   │  └─ TrainingScenarioLoader.cs, SafeSiteMenuController.cs, …  (app scripts in Assembly-CSharp)
   ├─ ScenarioPackages/       exported scenario zips (gitignored)
   ├─ ServerData/             Addressables remote build output (gitignored)
   └─ Skills/                 agent notes for the headset build tool
```

## Branch status (as of 2026-11-11)

All of this work is on `suriya/addressable-integrations` (not yet merged to `main`); the matching
web work is on `login-code-links-headset` in `safesite-web`. See
[docs/DECISIONS.md](docs/DECISIONS.md#open-items) for what is left before merging.
