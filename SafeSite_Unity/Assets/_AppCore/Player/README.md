# The Player (XR rig)

`--Player--.prefab` in this folder is the **only** XR rig in the project. It is the person in the
headset: head, controllers, hands, rays, teleporting and the event system for world-space UI.

## The three rules

1. **There is one rig and it lives in Bootstrap.** `Assets/_AppCore/Scenes/Bootstrap.unity` has the
   one `--Player--`. Scenarios (`Assets/_Scenarios/...`) load *into* Bootstrap, so a scenario scene
   must never contain a rig. Scenario export refuses a scene that has one.
2. **Change the prefab, not the scene copy.** Open `--Player--.prefab` and edit it there. The
   Bootstrap copy has one intentional override (see below); anything else showing as an override
   (bold in the Inspector) is a change that only Bootstrap gets. Apply it to the prefab or revert it.
3. **Build with the headset build tool, not File > Build.** It switches on the OpenXR features the
   rig needs for Quest or PICO (see "Build settings the rig depends on").

To add the rig to a new non-scenario scene: **SafeSite > Add XR Rig To Active Scene**.

> **Not the rig:** `SafesitePlayer_OVRCameraRig.prefab` and `OVRCameraRig Variant.prefab` in this
> folder came from a Meta OVR experiment on `main` (2026-11-11). They are not part of the app's
> architecture: don't use them in Bootstrap or in a scenario, and don't build on them. They are
> deleted once the scenarios that still contain them are migrated (see `docs/DECISIONS.md`).
> The same applies to Meta Interaction SDK components (`Grabbable`, `HandGrabInteractable`,
> `SnapInteractable`, Meta teleport nodes): use the XR Interaction Toolkit equivalents this rig
> already supports.

## What's inside

```
--Player--
├─ EventSystem                      XR UI input for every world-space canvas
├─ XR Origin Hands (XR Rig)         Unity's XRI "Hands Interaction Demo" rig, plus our scripts:
│  │                                  RecenterHandler (see below)
│  ├─ Camera Offset
│  │  ├─ Main Camera                the head; FadeCanvas (screen fades) hangs off it
│  │  ├─ Left / Right Controller    controller rays, teleport ray, controller model
│  │  │   └─ … Controller Visual    PlatformControllerModel swaps in the real Quest/PICO model
│  │  ├─ Left / Right Hand          tracked hands: poke, ray, pinch-grab
│  │  │   └─ (Right) HandTeleportInteractor   teleport with a hand gesture
│  │  └─ Hand Visualizer            draws the hands
│  ├─ Locomotion                    Teleportation and Snap Turn are on. Move, Grab Move, Climb,
│  │                                  Jump and Gravity come from the XRI sample and are switched off.
│  │                                  XRPlayerPositioner lives here.
│  └─ ToolbeltSocket                socket that follows the user, for putting tools away
└─ InteractionStateManager          what the user is allowed to do right now (see below)
```

Controllers and hands swap automatically: put the controllers down and the hands take over
(XRI's `XRInputModalityManager` on the XR Origin does this).

## Our scripts on the rig

All in `Assets/_AppCore/Scripts/XR/`.

| Script | On | What it does |
|---|---|---|
| `InteractionStateManager` | `InteractionStateManager` | Turns grabbing, teleporting and UI rays on/off by mode. Scenarios call `SetMode(...)`. |
| `XRPlayerPositioner` | `Locomotion` | `MovePlayerTo(transform)` puts the user at a spot, facing its forward. Used for the start point. |
| `PlatformControllerModel` | `Left/Right Controller Visual` | On device, replaces the generic controller mesh with the real Quest or PICO model. Nothing to wire. |
| `RecenterHandler` | `XR Origin Hands (XR Rig)` | After a recenter (Meta button held, or headset put back on) turns the user back to face the content. |

### Interaction modes (`InteractionStateManager.SetMode`)

| Mode | Grab things | Teleport | UI rays | Used for |
|---|---|---|---|---|
| `None` | no | no | no | cut-scenes, fades |
| `UIAndTeleport` | no | yes | yes | menus, walking around a scenario (the default) |
| `ToolUse` | yes | no | yes | a task where the user holds a tool |
| `Full` | yes | yes | yes | free play |

Scenarios find it with `FindFirstObjectByType<InteractionStateManager>()` and call
`SetMode(InteractionStateManager.InteractionMode.ToolUse)` etc. `TrainingScenarioLoader` puts it
back to `UIAndTeleport` when a scenario ends.

## Controls

| | Controllers | Hands |
|---|---|---|
| Point / press UI | trigger | pinch |
| Grab (when the mode allows) | grip | pinch on the object |
| Teleport | push thumbstick forward, aim, release (either hand) | right hand, XRI teleport hand shape, release to go |
| Turn | thumbstick left/right (snap turn) | — |
| In-scenario menu | left controller menu button (Esc in the Editor) | left palm towards you, pinch |
| Recenter | hold the Meta button | hold the Meta gesture |

Teleport targets must be on the **Teleport** interaction layer (the default for XRI's
`TeleportationArea` / `TeleportationAnchor`).

## The one Bootstrap override

In Bootstrap, `--Player--/…/Left Hand` → `MetaSystemGestureDetector` → **Menu Pressed** also calls
`GlobalMenu.Toggle`. It points at the `GlobalMenu` object in the Bootstrap scene, and a prefab can't
reference a scene object, so it stays a scene override. If you replace the rig in Bootstrap, re-add it.

## Build settings the rig depends on

The headset build tool (`Assets/HeadsetBuildConfig.asset`, built from
`Assets/Editor/HeadsetBuild/`) switches OpenXR features on and **switches everything else off**, so a
feature ticked by hand in Project Settings is undone at build time. Add it to the config instead.

The rig needs, for Quest:

- **Oculus Touch / Meta Quest Touch Pro / Touch Plus controller profiles**: controllers.
- **Hand Tracking Subsystem**: hands.
- **Meta Hand Tracking Aim** and **Hand Interaction Profile**: the direction hand rays point, and
  the left-hand menu gesture. Without them hand rays point in random directions.

PICO has its own controller profiles plus Hand Tracking and Hand Interaction Profile.

The build tool also defines `VR_META` or `VR_PICO` for the device build. `PlatformControllerModel`
and part of `RecenterHandler` only run under those, so **in the Editor you see the generic controller
mesh. That's expected.**

## If something looks wrong

| You see | Likely cause |
|---|---|
| Controller models backwards or floating | Something reparented the vendor model. `PlatformControllerModel` must stay on the Controller Visual; it builds its own grip anchor under Camera Offset. |
| Hand rays point the wrong way, hand menu doesn't open | Meta Hand Tracking Aim / Hand Interaction Profile missing from the build config. |
| A thumbstick does nothing | That controller's `ControllerInputActionManager` has *Smooth Motion Enabled* on while `Locomotion/Move` is off. Keep Smooth Motion off. |
| Teleport ray doesn't show | Interaction mode is `None` or `ToolUse`, or the target isn't on the Teleport interaction layer. |
| Scene jumps when the headset goes back on | Something else is recentering. Only the system and `RecenterHandler` should. OVRManager's own recenter is switched off in `PlatformControllerModel`. |
| Two rigs / everything doubled | A scenario scene contains a rig (an `XROrigin` or a Meta `OVRCameraRig`). Delete it. |
