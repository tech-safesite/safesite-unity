---
name: unity-openxr-headset-build
description: >-
  Recreates a Unity Editor headset build pipeline that ships Meta Quest and Pico
  APKs from a single OpenXR loader, with semantic versioning, Android signing,
  store profiles, and APK validation. Use when setting up Quest/Pico builds,
  OpenXR feature switching, headset APKs, bundleVersionCode, or Android XR
  store settings in a new Unity project.
---

# Unity OpenXR Headset Build Tool

Implement a generic Unity Editor **Build** menu that produces Quest and Pico APKs from **one OpenXR loader**. Vendor differences are OpenXR feature groups + Android store profile, not competing XR loaders.

This skill is a handoff spec. Read [implementation-spec.md](implementation-spec.md) before writing code. Read [shine-source-notes.md](shine-source-notes.md) only if porting lessons from the Shine project.

## Mission

In the target Unity project:

1. Install current OpenXR packages (look up latest compatible versions; do not copy frozen Shine versions blindly).
2. Keep **OpenXRLoader** as the only Android XR provider for both Quest and Pico.
3. Add `Assets/Editor/HeadsetBuild/` scripts + a ScriptableObject config.
4. Menu items: set SDK, bump version, build Quest, build Pico, build both, validate.
5. Output `Builds/{bundleVersion}/{Product}_{Device}_v{bundleVersion}.apk`.

Do **not** recreate Cardboard, iOS, Play Store AAB, painting preprocessors, or git hooks unless the user asks.

## Hard rules

- **One Android XR loader:** `UnityEngine.XR.OpenXR.OpenXRLoader`. Never assign `PXR_Loader` or `OculusLoader` for these builds.
- **One feature group per APK:** Meta Quest group XOR Pico OpenXR group. Enabling both in the same APK is a launch/runtime failure.
- **No Oculus XR Plugin.** Deprecated. Use Unity OpenXR + Unity OpenXR Meta (and Meta XR Core only if the project needs Platform/Avatar/Interaction SDK).
- **No Pico Integration `PXR_Loader` path.** Use [PICO Unity OpenXR SDK](https://github.com/Pico-Developer/PICO-Unity-OpenXR-SDK), not `com.unity.xr.picoxr` Integration SDK 2.x.
- **Do not commit keystore passwords.** Prompt once per Editor session and cache in memory.
- **Unity 6 keystore paths must be absolute** with forward slashes. `{inproject}:` / `{dedicated}:` prefixes break custom keystores.
- **Do not commit `ProjectSettings.asset`** after a one-off SDK switch unless the user wants that profile as the repo default.
- Look up **current store API levels** at implement time. Defaults below are the 2026 baseline, not forever-correct.

## Packages (resolve latest at implement time)

Minimum stack (Unity 6 / 6000.0 LTS or newer, Android Build Support, ARM64, IL2CPP):

| Package | Role |
|---|---|
| `com.unity.xr.management` | XR Plug-in Management |
| `com.unity.xr.openxr` | OpenXR loader + feature system |
| `com.unity.xr.meta-openxr` | Meta Quest OpenXR feature group / AR Foundation Meta features |
| PICO Unity OpenXR SDK (GitHub / Pico developer portal) | Pico OpenXR feature group |
| `com.unity.xr.hands` | Hand tracking OpenXR feature |
| `com.unity.inputsystem` | Required by OpenXR |

Pico OpenXR SDK v1.4.0+ is the Unity 6 / AR Foundation 6 line (PICO OS 5.13+). Prefer that over Integration SDK 2.5.x.

After install, log every OpenXR feature-set id:

```csharp
foreach (var set in OpenXRFeatureSetManager.FeatureSetsForBuildTarget(BuildTargetGroup.Android))
    Debug.Log(set.featureSetId + " enabled=" + set.isEnabled);
```

Known ids (verify; they differ if Meta XR All-in-One is also installed):

- Unity Meta Quest group: `com.unity.openxr.featureset.meta`
- Meta XR All-in-One group (only if that SDK is present): `com.meta.openxr.featureset.metaxr`
- Pico OpenXR group: `com.picoxr.openxr.features`

If `GetFeatureSetWithId` returns null, list ids and fail the build with the list. Do not guess.

## Target profiles

Config-driven. Defaults:

| | Quest | Pico |
|---|---|---|
| Loader | OpenXRLoader | OpenXRLoader |
| Feature group | Meta Quest (`com.unity.openxr.featureset.meta`) | Pico (`com.picoxr.openxr.features`) |
| Interaction profiles | Oculus Touch, Touch Pro, Touch Plus | Pico Neo 3, Pico 4, Pico 4 Ultra, Pico G3 |
| Always-on OpenXR features | Meta Quest Support (`com.unity.openxr.feature.metaquest`), Hand Tracking | `PICOFeature`, Pico controller profiles, Hand Tracking |
| Android application entry | GameActivity | GameActivity (OpenXR path). UnityPlayerActivity was only required for legacy PXR JNI `SysActivity` |
| minSdk | 32 | 29 |
| targetSdk | **34** (Horizon rejects >34 on upload; new apps from 2026-03-01 require 34) | 34 |
| Graphics | Vulkan first, GLES3 fallback. If custom shaders/video RT break on Quest, GLES3-only is an allowed override | Vulkan only |
| Color space | Linear | Linear |
| Architecture | ARM64 only | ARM64 only |
| Install location | Auto | Auto |
| Package id | `{prefix}.{app}.{questToken}` | `{prefix}.{app}.{picoToken}` |
| Scripting defines | project prefix + `VR_OPENXR` + `VR_META` | project prefix + `VR_OPENXR` + `VR_PICO` |

Quest Meta Quest Support must enable current devices: Quest 2, Pro, 3, 3S (and Quest 1 only if the product still supports it).

Do not generate a hand-written `Assets/Plugins/Android/AndroidManifest.xml` unless store validation requires it. Prefer OpenXR / Meta Quest Support to inject VR intent + `com.oculus.supportedDevices`. A custom manifest + GameActivity is a known Quest launch-crash combo.

## Architecture to create

```
Assets/Editor/HeadsetBuild/
  HeadsetBuildConfig.cs            # ScriptableObject
  HeadsetBuildConfig.asset
  HeadsetBuildMenu.cs              # MenuItem entry points
  OpenXrTargetSwitcher.cs          # loader + feature groups + interaction profiles
  AndroidStoreProfile.cs           # sdk, package id, activity, graphics, signing
  VersionBumper.cs                 # semver + bundleVersionCode
  HeadsetBuildPipeline.cs          # orchestrate switch → sign → build → validate
  ApkOpenXrValidator.cs            # pre/post APK checks
  EditorInputDialog.cs             # modal password / scene prompt
```

Optional: `IPreprocessBuildWithReport` that re-applies the store profile from `applicationIdentifier` so File > Build Settings cannot ship the wrong minSdk/keystore.

Keep project-specific prep (shaders, unique assets) out of this folder. Expose `public static event Action BeforeAndroidBuild` so the host project can hook in.

## Menu surface

```
Build/Quest
Build/Pico
Build/All (Quest then Pico)
Build/Set SDK/Quest OpenXR
Build/Set SDK/Pico OpenXR
Build/Validate/Quest Runtime
Build/Validate/Pico Runtime
Build/Validate/Android Store Profile
Open/Builds Folder
```

Each build prompts: bump version? → Bug Fix / Feature / Release Candidate, or keep current, or cancel.

## Versioning

`bundleVersion` is semver with optional RC: `MAJOR.MINOR.PATCH` or `MAJOR.MINOR.PATCH-rc.N`.

| Choice | bundleVersion | Android `bundleVersionCode` |
|---|---|---|
| Bug Fix | patch + 1, strip rc | +1 |
| Feature | minor + 1, patch = 0, strip rc | +1 |
| Release Candidate | if already `-rc.N` then N+1, else patch+1 and `-rc.1` | +1 |
| Keep same | unchanged | unchanged |

Unparseable versions fall back to `0.1.0` and warn. Incrementing `bundleVersionCode` is mandatory on bump (store uniqueness). Do not decrement.

## Signing

Two named keystores in config (headset vs store). Headset Quest and Pico typically share one upload cert; Play Store is a different cert. Quest Store rejects a Play-signed APK (`OCULUS_PLATFORM__APK_DEVELOPER_CERT_CHANGED`).

- `useCustomKeystore = true`
- Resolve file: absolute path already in Player Settings, else `{UserProfile}/Downloads/{fileName}` (override via config)
- Assign `PlayerSettings.Android.keystoreName` as `fullPath.Replace('\\','/')`
- Prompt keystore + alias passwords if empty; cache per filename for the Editor session
- Fail the build if the file is missing

## Build orchestration

```
PromptAndMaybeBumpVersion
→ OpenXrTargetSwitcher.Apply(target)
→ AndroidStoreProfile.Apply(target)
→ Prompt/validate keystore
→ optional BeforeAndroidBuild
→ PreValidate(target)
→ BuildPipeline.BuildPlayer (Android, ARM64, extraScriptingDefines)
→ PostValidate APK
→ RevealInFinder
```

`BuildPlayerOptions.locationPathName` = `./Builds/{Application.version}/{product}_{deviceToken}_v{Application.version}.apk`

Scenes: from config scene names under `Assets/Scenes/{name}.unity`, or enabled Editor Build Settings scenes if config list is empty.

Set `PlayerSettings.productName` and `applicationIdentifier` before `BuildPlayer`. Restore `EditorUserBuildSettings.buildAppBundle` in `finally` if you touch it.

## Validation

**Pre-build**

- OpenXRLoader is the Android loader; PXR_Loader and OculusLoader are absent
- Correct feature set enabled, the other disabled
- Required feature types exist and `enabled == true` (use Type.GetType / GetFeatures — types move between package versions)
- Android entry is GameActivity for OpenXR headset builds
- minSdk / targetSdk / ARM64 / applicationId match the target
- Keystore file exists

**Post-build APK zip inspect**

- Contains `libopenxr_loader.so` (or current OpenXR native name)
- Quest: dex or manifest mentions `com.oculus.intent.category.VR` and/or `com.oculus.supportedDevices`
- Pico: Pico OpenXR native / feature markers from the installed SDK (discover from the AAR, do not hardcode legacy `com.psmart.aosoperation.SysActivity` — that is PXR Integration only)
- Fail loud if native lib missing; treat missing dex strings as warnings if minify is on (print minify hint)

## OpenXR switcher (required behavior)

1. `XRPackageMetadataStore.RemoveLoader` every non-OpenXR Android loader (discover types; skip if type missing).
2. `AssignLoader(..., typeof(OpenXRLoader).FullName, Android)`.
3. Disable all Android OpenXR feature sets, then enable only the target set.
4. `OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(Android)`.
5. Walk `OpenXRSettings.GetSettingsForBuildTargetGroup(Android).GetFeatures()` and force-enable required types, force-disable vendor features from the other headset.
6. `FeatureHelpers.RefreshFeatures` if features look stale after package import.
7. `SetDirty` + `SaveAssets`.

Application entry: reflect `PlayerSettings.Android.applicationEntry` (enum names differ by Unity version). Prefer `GameActivity` for OpenXR. Log available names if unresolved.

## Graphics

Helper that no-ops if already set. Defaults: Quest `Vulkan, OpenGLES3`; Pico `Vulkan`. Document in config why a project might force GLES3-only on Quest (custom dome/video RT shaders have shipped black under Vulkan).

## What success looks like

- `Build/Set SDK/Quest OpenXR` and `Build/Set SDK/Pico OpenXR` leave a single OpenXR loader and a single vendor feature group.
- `Build/Quest` and `Build/Pico` produce signed APKs in `Builds/{version}/` without swapping XR packages.
- Runtime code can use Unity XR / OpenXR / XR Hands for both devices. `#if VR_PICO` is only for Pico vendor extensions, not for starting XR.
- Validation menus fail closed with actionable errors.

## Implementation order

Copy this checklist:

```
- [ ] Read implementation-spec.md
- [ ] Confirm Unity version, existing XR packages, and feature-set ids in the target project
- [ ] Add/upgrade OpenXR + Meta OpenXR + Pico OpenXR SDK; remove PXR_Loader / OculusLoader from Android providers
- [ ] Create HeadsetBuildConfig ScriptableObject and one asset
- [ ] Implement switcher, store profile, version bumper, signing, pipeline, validator
- [ ] Wire menu items
- [ ] Run Set SDK Quest, then Set SDK Pico, and confirm XR Plug-in Management UI matches
- [ ] Dry-run Validate menus
```

If the target project still has Pico Integration SDK (`PXR_Loader`, `SHINE_VR_PICO` JNI), migrate runtime off `Unity.XR.PXR` as part of the same change or the Pico OpenXR APK will still crash. See [shine-source-notes.md](shine-source-notes.md).
