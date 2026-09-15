# Shine source notes (legacy)

What this repo actually does today, and what **not** to copy into a new OpenXR-first tool.

## Where it lives

- `Assets/Editor/Build/Build.cs` — menu, SDK switch, version bump, signing, BuildPlayer, APK validators
- `Assets/Editor/Build/EditorInputDialog.cs` — modal UI Toolkit prompt (reuse)
- `Assets/Editor/Build/GitCommitGuard.cs` — **out of scope** for the generic tool
- `Assets/Modules/GameService/Editor/PaintingMobileBuildSupport.cs` — Shine painting assets; hook via event, do not copy
- `Assets/XR/OpenXR/MetaColorSpaceFeature.cs` — Quest `XR_FB_color_space` (washed-out Linear). Worth porting for Quest, not Pico-specific
- Unity `6000.0.65f1`, URP 17, `com.unity.xr.openxr` 1.16.1, `com.unity.xr.meta-openxr` 2.4.0
- Pico: **local Integration SDK 2.5.3** at `LocalPackages/PICO Unity Integration SDK_250` (`com.unity.xr.picoxr`) — this is the old `PXR_Loader` path

## What the current menu does well (keep)

- One-click Quest / Pico / All
- Prompted semver bump (`BugFix` / `Feature` / `RC`) plus `bundleVersionCode++`
- Absolute keystore paths from `~/Downloads`, session password cache, headset vs Play Store certs
- Android profile: minSdk, targetSdk, installLocation auto, applicationId, graphics APIs, color space, application entry
- Scripting defines per target (`SHINE_VR_OPENXR` vs `SHINE_VR_PICO`)
- Output `Builds/{version}/{name}_v{version}.apk` and RevealInFinder
- `IPreprocessBuildWithReport` so File > Build Settings still gets the right store profile
- Pre/post APK inspection for native libs and JNI classes
- Unity 6 `applicationEntry` via reflection (API not stable)

Identity in Shine today:

- prefix `ie.vStream`, app `ShineVR`
- Quest id `ie.vStream.ShineVR.MetaQuest2`
- Pico id `ie.vStream.ShineVR.PicoNeo3`
- Headset keystore `user.keystore` alias `vstream-dev`
- Play Store keystore `shine mobile playstore.keystore` alias `shine` (Cardboard/Play — skip unless asked)
- Quest min 32 / target 34, Pico min 29 / target 34
- Quest GLES3-only (Vulkan black panorama dome + video RT)
- Pico Vulkan-only (UnityGLTF/PBRGraph blows GLES3 compiler)
- Quest GameActivity; Pico UnityPlayerActivity (because PXR `SysActivity` JNI)

## What must change in the smarter tool

### Pico is not OpenXR in Shine

`SetPicoXRSDK()` **removes** `OpenXRLoader` and **assigns** `Unity.XR.PXR.PXR_Loader`. Pico validation requires:

- `LocalPackages/.../Runtime/Android/pxr_api-release.aar`
- class `com.psmart.aosoperation.SysActivity`
- APK contains `libpxr_api.so`
- Application entry **not** GameActivity (`Pxr_InitPsensor` crash)

That entire Pico JNI/AAR gate is obsolete if the new project uses PICO Unity OpenXR SDK. Replace with OpenXR loader + `com.picoxr.openxr.features`.

Runtime still branches on `SHINE_VR_PICO`:

- `Assets/Modules/ModalityService/Internal/ModalityService.cs`
- `Assets/Scripts/PicoModalityManager.cs`
- `Assets/Scripts/SDKManager.cs`
- `PXR_Loader.cs` patch with `#if SHINE_VR_PICO`
- `TimelineContentPlacement` Pico floor-origin via `PXR_Loader`

A new project must use XR Hands / OpenXR for modality. If migrating Shine itself, those PXR `#if` paths have to move or Pico OpenXR builds will be incomplete.

### Quest already is OpenXR

`SetOculusXRSDK()` assigns `OpenXRLoader`, enables Meta Quest Support. Keep this model; rename away from "Oculus XR".

Enabled Android Meta Quest Support devices already include Quest, Quest 2, Pro, Quest 3, Quest 3S.

### Do not copy

- Cardboard Android/iOS, Gamma color space, GLES3-only phone path, Play Store AAB, `CARDBOARDSDK`
- `PaintingMobileBuildSupport.PrepareAndroidBuild()` as a hard dependency
- `EditorCompileIndicator.CompileIndicator` if the host project lacks it
- Git commit hook / color-space commit guard
- Hardcoded Shine package ids, keystore filenames, scene `"Scene1 - New"`
- Pico AAR `SysActivity` checks on an OpenXR Pico build

## Pitfalls learned the hard way

1. **Unity 6 keystore:** setting a relative or `{dedicated}:` path gets rewritten to `{inproject}:` and the build fails with "keystore file not found". Always set a real absolute path.
2. **Two upload certs:** Quest Store treats a Play Store-signed APK as a developer-cert change.
3. **Color space is global.** Switching to Cardboard Gamma and committing `ProjectSettings.asset` poisons Quest/Pico Linear. Headset tool should always restore Linear for Quest/Pico.
4. **GameActivity vs UnityPlayerActivity:** correct for OpenXR Quest; fatal for legacy Pico JNI. OpenXR Pico should follow GameActivity unless the installed Pico OpenXR SDK docs say otherwise — verify on device once.
5. **`AssignLoader` can lie:** Pico Integration SDK GitHub issue #31 — API returns true but the loader is not in the manager. Re-read `activeLoaders` after save. Another reason to stop using `PXR_Loader`.
6. **Custom AndroidManifest + OpenXR + GameActivity** can crash on Quest launch. Prefer generated manifest from OpenXR Meta Quest Support.
7. **Horizon targetSdk:** Meta requires **34** for new apps (from 2026-03-01). Uploading **35/36** has been rejected even when Unity/Meta validation tools suggested "latest". Headset targetSdk 34 is intentional.
8. **Vulkan on Quest** broke Shine panorama domes; GLES3 was the last known-good. New projects should try Vulkan first and keep GLES3 as a config override, not a silent default, unless they share that shader path.
9. **Do not enable Meta and Pico OpenXR feature groups together.**

## Mapping Shine menus → new menus

| Shine | New |
|---|---|
| Build/Set SDK/Oculus XR (OpenXR) | Build/Set SDK/Quest OpenXR |
| Build/Set SDK/Pico XR (`PXR_Loader`) | Build/Set SDK/Pico OpenXR (feature group) |
| Build/Quest | Build/Quest |
| Build/Pico | Build/Pico |
| Build/Validate/Pico Runtime (AAR/SysActivity) | Validate OpenXR Pico (loader + feature set + natives) |
| Build/All (Quest+Pico+Cardboard) | Build/All (Quest+Pico only) |
