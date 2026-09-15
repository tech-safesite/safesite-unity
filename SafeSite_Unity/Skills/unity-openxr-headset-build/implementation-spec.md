# Implementation spec

Code-level recipes for the OpenXR headset build tool. Adapt names to the host project. Prefer this over copying Shine `Assets/Editor/Build/Build.cs`.

## Config asset

```csharp
[CreateAssetMenu(menuName = "Headset Build/Config")]
public class HeadsetBuildConfig : ScriptableObject
{
    public string reverseDomainPrefix = "com.company";
    public string appName = "App";
    public string productName = "App";
    public string[] scenes = { "Main" }; // names in Assets/Scenes, no .unity
    public string outputRoot = "Builds";
    public string keystoreSearchFolder = ""; // empty = ~/Downloads
    public KeystoreRef headsetKeystore;
    public KeystoreRef storeKeystore; // unused unless a store target exists

    public HeadsetTarget quest;
    public HeadsetTarget pico;
}

[Serializable]
public class KeystoreRef
{
    public string fileName;
    public string alias;
}

[Serializable]
public class HeadsetTarget
{
    public string deviceToken;          // MetaQuest2 / PicoNeo3 — used in id and filename
    public string featureSetId;         // discovered, not guessed
    public string[] extraDefines;
    public AndroidSdkVersions minSdk;
    public AndroidSdkVersions targetSdk;
    public bool useGameActivity = true;
    public UnityEngine.Rendering.GraphicsDeviceType[] graphicsApis;
    public string[] requiredFeatureTypeNames; // assembly-qualified or FullName
}
```

Create `Assets/Editor/HeadsetBuild/HeadsetBuildConfig.asset`. Menu code loads it with `AssetDatabase.FindAssets("t:HeadsetBuildConfig")` and errors if missing/duplicate.

## Feature-set switch

```csharp
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

public static void ApplyOpenXrTarget(string enableSetId, string[] disableSetIds)
{
    var perTarget = /* EditorBuildSettings XRGeneralSettingsPerBuildTarget */;
    var settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);

    RemoveLoaderIfPresent(settings, "Unity.XR.PXR.PXR_Loader");
    RemoveLoaderIfPresent(settings, "Unity.XR.Oculus.OculusLoader");
    RemoveLoaderIfPresent(settings, "Google.XR.Cardboard.XRLoader");

    XRPackageMetadataStore.AssignLoader(
        settings.Manager,
        typeof(UnityEngine.XR.OpenXR.OpenXRLoader).FullName,
        BuildTargetGroup.Android);

    FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);

    foreach (string id in disableSetIds)
        SetFeatureSet(id, false);
    SetFeatureSet(enableSetId, true);

    OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(BuildTargetGroup.Android);

    var oxr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
    foreach (var feature in oxr.GetFeatures())
    {
        bool required = IsRequiredForCurrentTarget(feature.GetType());
        if (feature.enabled != required)
            feature.enabled = required;
    }

    EditorUtility.SetDirty(settings);
    EditorUtility.SetDirty(oxr);
    AssetDatabase.SaveAssets();
}

static void SetFeatureSet(string id, bool enabled)
{
    var set = OpenXRFeatureSetManager.GetFeatureSetWithId(BuildTargetGroup.Android, id);
    if (set == null)
        throw new BuildFailedException(
            "Missing OpenXR feature set '" + id + "'. Installed: " + ListFeatureSetIds());
    set.isEnabled = enabled;
}
```

`RemoveLoaderIfPresent`: `Type.GetType(fullName + ", Assembly")` or iterate `activeLoaders` by `GetType().FullName`. `AssignLoader`/`RemoveLoader` returning true is not proof the UI updated — re-read `activeLoaders` after SaveAssets.

## Required OpenXR features

Enable by type when present; skip with a warning if a type is absent (package version skew).

**Quest baseline**

- `UnityEngine.XR.OpenXR.Features.MetaQuestSupport` / feature id `com.unity.openxr.feature.metaquest` (class name varies; match `featureIdInternal` if type moved)
- `UnityEngine.XR.OpenXR.Features.Interactions.OculusTouchControllerProfile`
- `UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchProControllerProfile`
- `UnityEngine.XR.OpenXR.Features.Interactions.MetaQuestTouchPlusControllerProfile` (if type exists)
- `UnityEngine.XR.Hands.OpenXR.HandTracking`

Also enable Meta Quest Support target devices: quest2, cambria (Pro), eureka (3), quest3s.

**Pico baseline** (PICO Unity OpenXR SDK)

- `Unity.XR.OpenXR.Features.PICOSupport.PICOFeature`
- Pico controller profiles in `UnityEngine.XR.OpenXR.Features.Interactions`: `PICONeo3ControllerProfile`, `PICO4ControllerProfile`, `PICO4UltraControllerProfile`, `PICOG3ControllerProfile`
- `UnityEngine.XR.Hands.OpenXR.HandTracking`
- Enable Pico Support project settings for hand tracking if the SDK exposes `PICOProjectSetting`

Disable the other vendor's features explicitly after applying feature sets. `SetFeaturesFromEnabledFeatureSets` can leave overlaps enabled.

## Android store profile

```csharp
PlayerSettings.Android.minSdkVersion = target.minSdk;
PlayerSettings.Android.targetSdkVersion = target.targetSdk;
PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
PlayerSettings.applicationIdentifier = prefix + "." + appName + "." + target.deviceToken;
PlayerSettings.colorSpace = ColorSpace.Linear;
PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, target.graphicsApis);
```

Set Android application entry via reflection on `PlayerSettings.Android.applicationEntry`. Match enum names containing `GameActivity` vs `UnityPlayerActivity`/`Activity`.

Mark PlayerSettings dirty:

```csharp
typeof(PlayerSettings)
    .GetMethod("SetDirty", BindingFlags.Static | BindingFlags.NonPublic)
    ?.Invoke(null, null);
```

## Keystore (Unity 6)

```csharp
string abs = Path.GetFullPath(Path.Combine(folder, fileName)).Replace('\\', '/');
if (!File.Exists(abs)) throw ...
PlayerSettings.Android.useCustomKeystore = true;
PlayerSettings.Android.keystoreName = abs; // NOT "{inproject}:" + fileName
PlayerSettings.Android.keyaliasName = alias;
```

Strip `{inproject}:` / `{dedicated}:` if reading an existing setting. Cache passwords in a static `Dictionary<string,string>` keyed by filename.

## Version bump

Regex: `^(\d+)\.(\d+)\.(\d+)(?:-rc\.(\d+))?$`

`EditorUtility.DisplayDialogComplex` for bump / keep / cancel, then Bug Fix / Feature / RC.

On bump:

```csharp
PlayerSettings.bundleVersion = newVersion;
PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode + 1);
```

## BuildPlayer

```csharp
var opts = new BuildPlayerOptions
{
    scenes = scenePaths,
    locationPathName = $"{outputRoot}/{Application.version}/{product}_{token}_v{Application.version}.apk",
    target = BuildTarget.Android,
    extraScriptingDefines = target.extraDefines
};
PlayerSettings.productName = config.productName;
PlayerSettings.applicationIdentifier = appId;
var report = BuildPipeline.BuildPlayer(opts);
```

Create the version folder first. Do not leave `buildAppBundle` true for headset APKs.

## APK validation

Zip-open the APK (`System.IO.Compression.ZipArchive`):

- Any entry ending in `libopenxr_loader.so`
- Quest: ASCII search in `.dex` or `AndroidManifest.xml` (binary XML may not be readable as ASCII — also check `META-INF` and `assets` as needed). Prefer checking `AndroidManifest.xml` after Unity build via `UnityEditor.Android` APIs if available; otherwise look for `libopenxr_loader.so` + correct applicationId in PlayerSettings as the hard gate.
- Pico OpenXR: inspect the Pico OpenXR SDK AAR/plugins the same way Shine inspected `pxr_api-release.aar`, but look for OpenXR Pico natives from that SDK (names change — discover from `Packages/` or the GitHub package Runtime/Android folder).

Do **not** require `com.psmart.aosoperation.SysActivity` on the OpenXR Pico path.

Minify hint via reflection: `PlayerSettings.Android.minifyRelease`. If classes are missing while the AAR has them, keep-rules are the first suspect.

## Preprocess hook

```csharp
class HeadsetStoreProfilePreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => -50;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android) return;
        // If applicationIdentifier contains questToken → apply Quest profile
        // If it contains picoToken → apply Pico profile
    }
}
```

## Runtime guidance (host project)

Headset gameplay should use:

- `XRGeneralSettings.Instance.Manager` / `XROrigin`
- OpenXR interaction profiles / Input System
- `XRHandSubsystem` for hands

Vendor `#if` only around Pico/Meta extension APIs (passthrough vendor extras, platform IAP). Starting/stopping XR must not depend on `PXR_Loader`.

## Lookup at implement time

- Meta targetSdk: https://developers.meta.com/horizon/blog/meta-quest-apps-android-14-march-1/
- Manifest / minSdk: https://developers.meta.com/horizon/resources/publish-mobile-manifest/
- Unity OpenXR Meta project settings: `com.unity.xr.meta-openxr` package manual
- Pico OpenXR SDK: https://github.com/Pico-Developer/PICO-Unity-OpenXR-SDK
- Unity XR packages: https://docs.unity3d.com/Manual/xr-support-packages.html
