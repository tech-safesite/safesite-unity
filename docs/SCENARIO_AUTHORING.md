# Scenario authoring

Everything a scenario developer needs: create a scenario, report score and completion, test it
in the app, and export the package (zip) that an admin imports in the dashboard.

You never sign in to anything in Unity. The dashboard does the upload.

- [The short version](#the-short-version)
- [1. Create a scenario](#1-create-a-scenario)
- [2. Build the scene](#2-build-the-scene)
- [3. Report score and completion (SafeSiteScenario)](#3-report-score-and-completion-safesitescenario)
- [4. Test it inside the app](#4-test-it-inside-the-app)
- [5. Check sizes](#5-check-sizes)
- [6. Build the package](#6-build-the-package)
- [7. Hand it to the dashboard](#7-hand-it-to-the-dashboard)
- [How export treats your Addressables group](#how-export-treats-your-addressables-group)
- [Bundle splitting and the 50 MB limit](#bundle-splitting-and-the-50-mb-limit)
- [What the validator checks](#what-the-validator-checks)
- [Command line / CI](#command-line--ci)
- [Troubleshooting](#troubleshooting)

---

## The short version

1. **SafeSite > Scenarios → New scenario**: fill in the details, **Create scenario**.
2. Build the scene in `Assets/_Scenarios/<Name>/Scenes/<Name>.unity`. No rig, no camera.
3. Wire the scene's **SafeSite Scenario** component: `Correct` / `Incorrect` from your checks,
   `CompleteAndExit` at the end.
4. Test in Bootstrap (built-in scenarios list) or export and test the real download.
5. **Check sizes** → apply fixes → **Build package for headsets**.
6. Send `ScenarioPackages/<slug>_<version>.zip` to the admin, or import it yourself in the
   dashboard (**Scenarios > Import package**).

---

## 1. Create a scenario

Open **SafeSite > Scenarios** (the only scenario menu item; everything is in this window).

Expand **New scenario** and fill in:

| Field | Rule | Notes |
|---|---|---|
| Existing scene | optional | Pick a scene already in `Assets/_Scenarios/<Name>/Scenes/` to register it in place instead of creating a new folder. |
| Slug | lowercase letters, digits, single dashes, max 80 | The permanent id shared with the dashboard, e.g. `ladder-access-setup`. **Never change it after the first import**: a new slug is a new scenario. |
| Title | required, max 60 | Shown on the headset card and in the dashboard. |
| Category | required, max 60 | The dashboard's folder, e.g. `Working at Height`. Also picks the card icon on the headset. |
| Description | required, max 200 | |
| Duration | 1–120 minutes | |
| Languages | at least one, comma separated | The trainee picks one on the options screen; it's recorded with the session. |

**Create scenario** then:

- creates `Assets/_Scenarios/<TitleInPascalCase>/` with `Animations, Audio, LightingData, Materials,
  Models, Prefabs, Scenes, Scripts, Textures, _temp`;
- copies the template scene to `Scenes/<Name>.unity`. The template already has a **SafeSite
  Scenario** component on `ScenarioRoot`;
- writes the `ScenarioDefinition` asset `<slug>.asset` at the folder root;
- creates the Addressables group `Scenario_<Folder_Name>` (e.g. `Scenario_Working_At_Height`) with
  the scene at `scenario/main`, remote paths, and **Include in Build off**.

It refuses if the folder or the `<slug>.asset` already exists, or if any detail breaks the rules
above. Nothing is created in that case.

The **Details** foldout lets you edit these later. They are only used when the dashboard *creates*
the scenario from the first import; after that the dashboard owns them (an admin can opt to
overwrite from the package).

## 2. Build the scene

Rules for a scenario scene:

- **No rig of any kind, camera, EventSystem or player setup of your own.** That includes Meta's
  `OVRCameraRig` and the OVR rig prefabs in `_AppCore/Player`. The scene always loads on top of
  Bootstrap, which brings `--Player--`; a second rig means two cameras and doubled input. Export
  fails on an `XROrigin`; it doesn't catch `OVRCameraRig` yet, so check by hand. To place the
  player at the start, use `XRPlayerPositioner.MovePlayerTo(transform)` (on `--Player--/…/Locomotion`).
- **Interactions use the XR Interaction Toolkit** (`XRGrabInteractable`, `XRSocketInteractor`,
  `TeleportationArea`/`TeleportationAnchor`), which `--Player--` drives with controllers and hands
  on both Quest and PICO. Don't use Meta Interaction SDK components (`Grabbable`,
  `HandGrabInteractable`, `SnapInteractable`, Meta teleport nodes): nothing in the app's rig
  interacts with them, and they don't run on PICO.
- **Lighting:** the scenario scene becomes the active scene when loaded, so its lighting settings
  and skybox apply. Bake lighting into the scenario's `LightingData/` folder.
- **Interaction:** set what the trainee may do with
  `FindFirstObjectByType<InteractionStateManager>().SetMode(...)` (`None`, `UIAndTeleport`,
  `ToolUse`, `Full`). The app sets `UIAndTeleport` again when the scenario ends. See the Player
  README for what each mode allows.
- **Teleport targets** must be on the Teleport interaction layer.
- **Scripts** go in the scenario's `Scripts/` folder, not the `Assets/` root (the export lists only
  scripts inside the scenario folder in its "needs an app build" warning). They compile into the app
  (bundles carry no code), so a headset needs an app build containing them. Keep scripts generic and reusable when
  you can, so new scenarios can ship without a new APK.
- **Exiting:** never load another scene or call `SceneManager` yourself. Use `SafeSiteScenario`
  (`Exit`, `CompleteAndExit`, `Restart`) or, in existing code, `TrainingScenarioLoader.Instance.UnloadScenario()`;
  both go through the same fade-unload-reset path for downloaded and built-in scenarios.
- **Loading:** scenarios are always loaded additively. `TrainingScenarioLoader.LoadScenarioSingle`
  (replace Bootstrap with the scenario) exists only for the off-architecture
  `Bootstrap_MetaShowcase` scene and is being removed; don't use it.

## 3. Report score and completion (SafeSiteScenario)

`Modules/ScenarioKit/Runtime/SafeSiteScenario.cs` is the one component a scenario talks to.
Put exactly one in the scene (the template has it; for an older scene use **Advanced > Add SafeSite
Scenario** in the Scenarios window with the scene open).

Start time, duration, language, headset and upload are automatic. A scenario only says how the
trainee scored and whether they reached the end.

| Method | Use it when | Effect |
|---|---|---|
| `Correct()` / `Incorrect()` | The trainee passes or fails one scored check | Adds to the tally and reports the running score |
| `Answer(bool)` | From an event that passes a bool (e.g. a toggle) | Same as above |
| `SetScore(correct, total)` | Your scenario keeps its own tally | Replaces the score |
| `Complete()` | The trainee reached the end | Session counts as **completed**. Safe to call twice. Raises `onCompleted` once. |
| `CompleteAndExit()` | End of the scenario, go back to the menu | `Complete()` then `Exit()` |
| `Exit()` | Leave without completing | Back to the menu; session stays **not completed** |
| `Restart()` | Start over | New session from the beginning |

Inspector settings:

- **Expected Total**: how many scored checks the scenario has. Checks the trainee never reached
  count as wrong (score `3/10`, not `3/3`). 0 means the total is just the checks answered.
- **On Completed**: event raised once on completion, e.g. to play an end voiceover.

### Wiring it without code (recommended)

Your own scripts raise their own `UnityEvent`s; the Inspector wires them to the component. Click
**+** on the event, drag `ScenarioRoot` in, pick **SafeSiteScenario → Correct** (etc.). XRI events
(Select Entered), triggers and Timeline Signals all work the same way.

The component's custom Inspector lists every event in the scene that already calls it (with a
**Show** button), and in Play Mode has buttons (Correct, Incorrect, Complete, Complete And Exit,
Exit, Restart) to fake a trainee.

### Wiring it from code

Scenario scripts are in `Assembly-CSharp`, which sees `Modules.ScenarioKit` automatically:

```csharp
using Modules.ScenarioKit;

public class HazardCheck : MonoBehaviour
{
    SafeSiteScenario _scenario;
    void Awake() => _scenario = SafeSiteScenario.FindInScene();

    public void Spotted() => _scenario.Correct();
    public void Missed()  => _scenario.Incorrect();
    public void Finished() => _scenario.CompleteAndExit();
}
```

### Examples

`Modules/ScenarioKit/Samples/ScenarioKitExample.unity` shows both ways side by side:
`ExampleScenarioSteps` (Inspector wiring) and `ExampleScenarioCode` (code). Both have Play Mode
buttons. Samples are in their own assembly and never ship in the app.

> **If nothing calls `Complete`, every session shows as "not completed" in the dashboard.** The
> validator warns about this when it finds no `Complete`/`CompleteAndExit` wired in the scene
> and no call in the scenario's scripts.

## 4. Test it inside the app

Two options, from quickest to most realistic:

**A. As a built-in scenario (no export).** Your Addressables group can stay as the team sets it up:
local paths, your own address (e.g. `scenario_falling_objects_scene`), **Include in Build on**.
Add an entry to `ScenarioGridPanel → Built In Scenarios` on Bootstrap's menu (title, address, icon).
In the Editor and development builds the scenarios screen lists it without signing in (**Built-in
scenarios** on the login screen). Sessions are **not** recorded for built-in scenarios.

**B. As a real download.** Export the package, import it into the local API with `demo-setup`, sign
in on Bootstrap with the printed login code and play it. This is the only way to test the package
itself, analytics and the dashboard. See [LOCAL_TESTING.md](LOCAL_TESTING.md).

In the Editor, downloaded bundles are Android builds whose shaders can't draw on PC; the loader
swaps each material's shader for the Editor's copy of the same name, so it looks right. Headsets
use the bundle's own shaders.

## 5. Check sizes

**Check sizes** (main section of the window) runs in seconds without building. It lists the ten
biggest assets the scenario brings in and flags:

| Flag | Automatic fix | What the fix changes |
|---|---|---|
| Texture larger than 2048 px | **Use 2048 px on headsets** | Android import override only: max size 2048. The Editor/PC look is unchanged. |
| Uncompressed (PCM) audio over 1 MB | **Compress on headsets** | Android override: Vorbis, quality 0.6, Compressed In Memory |
| A texture, audio or video clip near the per-file limit on its own | none if no fix applies (**blocking**) | Make it smaller or split it; for video re-encode at lower resolution/bitrate |
| A model or other file near the limit | none (warning only) | Only the parts the scene uses are packed, so usually fine |

Each flagged asset has its fix button and **Show** (pings it in the Project window). With more than
one fixable issue, **Fix all N** applies every automatic fix in one import pass. The size check
re-runs after each fix.

## 6. Build the package

Pick the scenario at the top of the window, then **Build package for headsets**:

- **Version**: defaults to one patch above the last export (`lastExportedVersion` on the definition),
  `1.0.0` the first time. Must be `x.y.z`. The dashboard may go higher so versions never go backwards.
- **Platforms**: `meta` and/or `pico`. If a project has an `ISafeSitePlatformBuildHook` it builds
  once per platform; otherwise (today) it builds once and packages the same output for both.

One button runs every step:

1. Switches the build target to **Android** if needed. Unity may reload scripts; the build carries
   on by itself afterwards (the pending build is remembered across the reload).
2. Fixes Addressables settings if missing, makes sure the scene is in the group.
3. Validates (see below). Errors stop the build; warnings are shown in the log.
4. Applies the export contract to the group, splits large assets into ~24 MB bundles, builds
   Addressables content into `ServerData/Android`.
5. If any file is still over the limit, rebuilds with 12 MB then 6 MB bundles (two retries).
6. Copies each platform's files to `Library/SafeSiteScenarioPackages/<slug>/<platform>/`, hashes
   them (SHA-256), writes `scenario.json`, zips to `ScenarioPackages/<slug>_<version>.zip`
   (bundles stored, not recompressed).
7. Restores the group's own settings and saves `lastExportedVersion`.

The result panel shows the zip, file count, total size and largest file, with **Show zip** and
**Copy path**.

**Advanced** has the single steps: Open scene, Add SafeSite Scenario, Repair group, Validate only,
Fix Addressables settings, Switch to Android, Open packages folder. **Log** has the full log with
Copy and Clear.

## 7. Hand it to the dashboard

In the dashboard: **Scenarios > Import package**, drop the zip. Then:

- **New slug** → created as a **draft** with the package's details. Publish it and assign it to
  clients as usual.
- **Existing slug** → updated. If the scenario is **published**, the new version goes live to
  assigned headsets as soon as the upload finishes; the dashboard warns and asks first.

Headsets see the update on their next config sync (sign-in, refresh, or heartbeat) and show
**Update** on the card. The format is documented in the web repo:
`docs/unity/SCENARIO_PACKAGES.md`.

---

## How export treats your Addressables group

The scenario team's groups (e.g. `Scenario_Falling_Objects`) are set up for testing in the app:
local build/load paths, a readable address, included in the app build. The exporter **never
changes those settings permanently**. For the length of an export only, it:

- sets the main scene's address to `scenario/main` (every package uses this address; headsets
  resolve it only inside the downloaded catalog, so scenarios don't clash);
- switches build/load paths to Remote and bundle naming to Append Hash;
- with splitting, adds temporary entries labelled `safesite-split-NN` and packs by label.

All of it is put back when the export finishes (or fails). New groups made by **Create scenario**
use the contract from the start and are excluded from the app build.

How a group is found for a definition: the group containing the main scene, else
`Scenario_<Folder_Name>`, else `Scenario_<slug>`. **Repair group** puts the scene back in a group if
it went missing.

## Bundle splitting and the 50 MB limit

Supabase Storage on the current plan rejects files over **50 MB** (setting: `maxFileSizeMb` in
`Assets/Settings/SafeSite/SafeSiteUploadSettings.asset`). A scenario like Falling Objects is about
210–240 MB, so it can't be one bundle. During export:

- Every asset inside the group's folder entries, plus every texture, audio or video clip of
  **512 KB or more** the scene uses from elsewhere, becomes its own entry.
- Entries are binned first-fit-decreasing into bundles of about **24 MB** (before compression).
- Assets that two or more bundles (or a bundle and the scene) would each copy are pulled into the
  bins too, so nothing is duplicated. Without this Falling Objects came out at 817 MB instead of
  ~230 MB.
- Models are not split by size: as an entry a model brings every mesh and animation in the file,
  where a scene that only uses one mesh brings only that.

## What the validator checks

Run on every export, or alone with **Advanced > Validate only**.

**Errors (block the export)**

- Details break the API rules (slug, title, category, description, duration, languages, scene).
- No group, or the main scene isn't in it (fix: **Repair group**).
- Addressables settings that break the upload rules, checked as they will be during export.
- The scene contains an XR rig (`XROrigin`). A Meta `OVRCameraRig` isn't detected yet; check for
  it by hand until the check is extended.

**Warnings**

- The scene uses scripts from the scenario's folder: headsets need an app build (current
  `bundleVersion` or later) with these scripts.
- Nothing calls `SafeSiteScenario.Complete`, or the scene has no `SafeSiteScenario`.

## Command line / CI

```bash
Unity -batchmode -quit -projectPath SafeSite_Unity -buildTarget Android \
  -executeMethod Modules.ScenarioAuthoring.Editor.ScenarioPackageCli.Export \
  -scenario falling-objects -packageVersion 1.0.4 -platforms meta,pico
```

- `-scenario` (required) is the slug.
- `-packageVersion` (not `-version`, which Unity takes itself) defaults to one patch above the last export.
- `-platforms` defaults to `meta`.
- Must run with `-buildTarget Android`. Exits 1 on any failure; log lines start with `[ScenarioPackageCli]`.

## Tests

`Modules/ScenarioAuthoring/Tests/Editor/ScenarioAuthoringTests.cs` (Test Runner → EditMode):
details validation, manifest JSON shape and round-trip, scenario creation in the team layout,
export contract applied and restored, large-texture and folder splitting, size-check texture
capping, registering an existing scene, refusing existing folders and bad details, group/address
naming.

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| "Unity didn't switch to Android" | Android Build Support isn't installed: Unity Hub > Installs > Add modules. |
| "Some files are still over the 50 MB limit after splitting" | One asset is too big on its own. Run **Check sizes**, apply fixes; for video, re-encode. |
| Validation: "The main scene isn't in group …" | **Advanced > Repair group**. |
| Validation: "… is an XR rig" | Delete the rig from the scenario scene; Bootstrap brings the player. |
| Two cameras, doubled hands or rays after loading a scenario | The scene has its own rig, often an `OVRCameraRig` (not caught by validation yet). Delete it. |
| Grab or teleport does nothing in a scenario | The objects use Meta Interaction SDK components. Replace them with XRI `XRGrabInteractable` / `TeleportationArea`/`TeleportationAnchor` on the Teleport layer. |
| Headset: "'…' has no 'scenario/main' scene. Re-export it from Unity." | The package was built outside the exporter or with an old tool. Export again. |
| Scenario loads but a component is missing / "The referenced script is missing" on device | The scenario uses a script the headset's app build doesn't have. Build and install a new APK. |
| Everything is magenta in the Editor after a download | Expected only if the Editor has no shader of the same name; check the scenario uses URP/project shaders, not ones that exist only in an Android build. |
| Dashboard shows sessions as "not completed" | Nothing calls `Complete`/`CompleteAndExit`. See the validator warning. |
| Group settings look changed after a failed export | They're restored in a `finally`; if Unity crashed mid-export, use **Repair group** and re-check the group's paths. |
