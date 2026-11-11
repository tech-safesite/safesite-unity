# ScenarioAuthoring

Editor-only tools for scenario developers: **SafeSite > Scenarios**. Create a scenario in the
`_Scenarios` layout, check its size, and build the package (zip) the dashboard imports. No sign-in.

Step-by-step guide: [docs/SCENARIO_AUTHORING.md](../../../../docs/SCENARIO_AUTHORING.md).

## Files (`Editor/`, assembly `Modules.ScenarioAuthoring.Editor`)

| File | What it does |
|---|---|
| `ScenariosWindow.cs` | The window: scenario picker, Details, Check sizes, Build package, New scenario, Advanced, Log. Survives the Android switch's script reload (pending build in `SessionState`). |
| `ScenarioDefinition.cs` | ScriptableObject per scenario (`<slug>.asset` at the scenario folder root): slug, title, category, description, duration, languages, main scene, last exported version. `Validate()` mirrors the API's `CreateScenario` rules. |
| `ScenarioCreator.cs` | Creates `Assets/_Scenarios/<Name>/` with the standard subfolders, copies `Templates/ScenarioTemplate.unity`, writes the definition, ensures the group. Or registers an existing scene in place. |
| `ScenarioGroups.cs` | Finds/creates the scenario's Addressables group; `ForExport(...)` applies the package contract (`scenario/main`, remote paths, hashed names, split bundles) and restores the group's own settings on dispose. Bundle splitting and de-duplication live here. |
| `ScenarioSizeCheck.cs` | Fast, no-build size report with headset-only fixes (cap textures at 2048 px, compress PCM audio) on the Android import override. |
| `ScenarioValidator.cs` | Errors and warnings before export: details, group, Addressables settings, XR rig in scene, scripts that need an app build, missing completion call. |
| `ScenarioPackageExporter.cs` | Builds per platform, retries with smaller bundles if a file is over the limit, hashes files, writes `scenario.json` and the zip to `ScenarioPackages/`. |
| `ScenarioPackageManifest.cs` | The `scenario.json` format (`formatVersion` 1). The dashboard's import reads exactly this. |
| `ScenarioPackageCli.cs` | Batch mode: `-executeMethod Modules.ScenarioAuthoring.Editor.ScenarioPackageCli.Export -scenario <slug> [-packageVersion x.y.z] [-platforms meta,pico]` |
| `Templates/ScenarioTemplate.unity` | New scenario scene: `ScenarioRoot` with `SafeSiteScenario`, no rig. |

## Tests

`Tests/Editor/ScenarioAuthoringTests.cs` (assembly `Modules.ScenarioAuthoring.Tests.Editor`), 15
EditMode tests. Run them after any change here or in `SafeSiteApiService/Editor`.

## Key constants

| Constant | Value | Where |
|---|---|---|
| Per-file limit | 50 MB (`maxFileSizeMb`) | `Assets/Settings/SafeSite/SafeSiteUploadSettings.asset` |
| Split threshold for outside dependencies | 512 KB | `ScenarioGroups.SplitAboveBytes` |
| Target bundle size | 24 MB, then 12, then 6 on retry | `ScenarioGroups.SplitBundleBytes`, `ScenarioPackageExporter.MaxRetries` (2) |
| Headset texture cap | 2048 px | `ScenarioSizeCheck.HeadsetMaxTextureSize` |
| Audio flagged when PCM over | 1 MB | `ScenarioSizeCheck.UncompressedAudioBytes` |
| Main scene address | `scenario/main` | `SafeSiteApiService.External.ScenarioAddresses.MainScene` |

## Depends on

`SafeSiteApiService.External` and `.Editor` (content build, Addressables validation, settings),
`SceneRig.Editor` (rig check), `ScenarioKit` and `ScenarioKit.Editor` (completion check, Add
SafeSite Scenario).

**If you change the package format**, bump `ScenarioPackageManifest.CurrentFormatVersion` and update
the dashboard (`dashboard/src/lib/scenarioPackage.ts`) and `docs/unity/SCENARIO_PACKAGES.md` in the
web repo in the same change.
