# SceneRig

Editor-only. One menu item and one check, both about the XR rig. The rig itself is documented in
[Assets/_AppCore/Player/README.md](../../_AppCore/Player/README.md).

| | |
|---|---|
| **SafeSite > Add XR Rig To Active Scene** | Instantiates `Assets/_AppCore/Player/--Player--.prefab` (the project's only rig) into the active scene. Refuses scenario scenes (`Assets/_Scenarios/…`) and scenes that already have an `XROrigin`. |
| `XRRigMenu.ValidateScenePath(path, errors, warnings)` | Used by the scenario exporter: any `XROrigin` in a scenario scene is an error, because scenarios load into Bootstrap, which brings the player. **Gap:** it doesn't yet catch a Meta `OVRCameraRig`, a stray `Camera` or an `EventSystem`; extend it to those. |

File: `Editor/XRRigMenu.cs` (assembly `Modules.SceneRig.Editor`).

This replaced the old `Assets/Editor/XRRig/` tools (`XRRigAutoCreate`, `XRRigBuilder`,
`XRRigSampleImporter`, `XRRigMenu`) and the `Assets/Prefabs/XR` rigs, which were removed on
2026-11-11.
