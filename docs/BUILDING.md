# Building the headset app

How to build and install a Quest or PICO APK, what the build tool changes for you, and the rules
that keep app builds and scenario packages compatible.

## The one rule

**Build from the `Build` menu, never File > Build Settings.** The build tool switches the OpenXR
features, Android store profile, manifest, scripting defines and native plugins for the target
headset, then validates the APK. A plain build can ship the wrong vendor's runtime or miss the
hand-tracking features, and those failures only show up on the device.

## Menu

| Item | Does |
|---|---|
| **Build > Quest** | Version prompt → Quest profile → build → validate → `Builds/<version>/SafeSite_MetaQuest_v<version>.apk` |
| **Build > Pico** | Same for PICO |
| **Build > All (Quest + Pico)** | Both, one after the other |
| **Build > Set SDK > Quest OpenXR / Pico OpenXR** | Switch the project's settings to that headset without building (to test or to commit a default) |
| **Build > Set SDK > Configure Quest Link (PC)** | Set up the Standalone (PC) OpenXR features so Play Mode works over Quest Link |
| **Build > Validate > Quest Runtime / Pico Runtime / Android Store Profile** | Run the pre-build checks only |
| **Open > Builds Folder** | Reveal `Builds/` |

The version prompt offers **Bump Version / Keep Current / Cancel**, then **Bug Fix / Feature / …**
for the kind of bump. It updates `PlayerSettings.bundleVersion` (now `0.3.3`) and the Android
`bundleVersionCode`.

Code: `Assets/Editor/HeadsetBuild/` (namespace `SafeSite.Build.Headset`). Config:
`Assets/HeadsetBuildConfig.asset`. Background and design notes for the tool:
`SafeSite_Unity/Skills/unity-openxr-headset-build/`.

## What the build tool does per headset

| Step | Quest | PICO |
|---|---|---|
| XR loader | OpenXR only | OpenXR only |
| OpenXR features | Meta Quest feature group + the list in the config | PICO OpenXR feature group + the list in the config |
| Package id | `com.safesite.SafeSite.MetaQuest` | `com.safesite.SafeSite.Pico` |
| Android manifest | Generated from `ManifestTemplates/AndroidManifest.Quest.xml` | from `AndroidManifest.Pico.xml` |
| Scripting defines | `VR_OPENXR`, `VR_META` | `VR_OPENXR`, `VR_PICO` |
| Native plugins | PICO libraries excluded (`VendorPluginGate`) | Meta libraries excluded |
| After build | `ApkOpenXrValidator` opens the APK and fails if the other vendor's native libraries are in it | same |

**Everything not listed in the config is switched off.** A feature ticked by hand in Project Settings
> XR Plug-in Management > OpenXR is undone at build time. To add one, add its type name to the
right list in `HeadsetBuildConfig.cs` (defaults) **and** `HeadsetBuildConfig.asset`.

On this branch the following were added for the rig's hand rays and hand menu gesture:

- Quest: `MetaHandTrackingAim`, `HandInteractionProfile` (Android and Standalone/Link lists)
- PICO: `HandInteractionProfile`

Without them the hand rays point in random directions and the left-hand menu gesture does nothing.
Meta's AR Foundation features are deliberately all **off**: enabling them without the lifecycle
feature crashed the 0.1.1 Quest build (see the comment in `HeadsetBuildConfig.cs`).

Both manifest templates now also request `ACCESS_COARSE_LOCATION` and `ACCESS_FINE_LOCATION` for
`DeviceLocationService` (headset on the dashboard map). Edit the templates, never
`Assets/Plugins/Android/AndroidManifest.xml`, which is regenerated per build.

## `VR_META` / `VR_PICO` code

`PlatformControllerModel` (real controller models) and parts of `RecenterHandler` compile only
under these defines. In the Editor you see the generic controller mesh: that's expected. To check
such code compiles, temporarily add the define in Player Settings, or run **Build > Set SDK > …**.

## Signing

`KeystoreSigner` refuses to build until a custom keystore is set in **Player Settings > Publishing
Settings**. The project's keystore is `SafeSite_Unity/safesite.keystore` (committed). Passwords are
**not** committed: you're asked once per Editor session. Ask Suriya for the passwords; they belong
in the team's password manager, not in this repo. Unity 6 needs the keystore path absolute, with
forward slashes.

## Installing

```bash
adb install -r "SafeSite_Unity/Builds/0.3.3/SafeSite_MetaQuest_v0.3.3.apk"
```

Logs from the headset:

```bash
adb logcat -s Unity
```

Useful filters: `[ScenarioLauncherService]`, `[SessionAnalyticsService]`, `[DeviceLocationService]`,
`[GlobalMenu]`, `[SafeSiteScenario]`.

## App builds and scenario packages

Scenario packages (Addressables bundles) contain **assets, never code**. So:

- A scenario whose scripts are new or changed needs a **new app build** installed on headsets
  before its package will work. The export lists those scripts and the app version it was exported
  against (`builtWith.appVersion` in `scenario.json`).
- Changing a shared script's serialized fields (renaming, removing) can break already-published
  packages that use it. Add new fields instead of renaming, or re-export the affected scenarios.
- The headset loads bundles built with the same Unity version as the app. After a Unity upgrade,
  re-export every scenario.
- Release order: build and install the app first, then import the package in the dashboard.

## Build checklist

1. `Bootstrap.unity` is the only scene in Build Settings (not `Bootstrap_MetaShowcase`), and
   Bootstrap's `ScenarioLauncherService.environmentName` is **Production** (not Local).
2. `AdminPinPanel.pin` in Bootstrap is the PIN you intend to ship.
3. The `--Player--` instance in Bootstrap has only its one intended override.
4. Development Build off for client builds (built-in scenarios show only to admins who turn them on).
5. **Build > Quest** (and/or Pico), bump the version.
6. Install, sign in on Demo or Production, play a downloaded scenario to the end, check the session
   shows up in the dashboard.
