# Decisions, deferred work and known issues

Why things are the way they are, what was consciously postponed (not rejected), and what is known
to be incomplete. Keep this current: add a dated line when you decide something that a newcomer
would otherwise undo.

- [Decisions](#decisions)
- [Deferred (postponed, not rejected)](#deferred-postponed-not-rejected)
- [Known issues](#known-issues)
- [Open items](#open-items)
- [What changed on this branch](#what-changed-on-this-branch)

---

## Decisions

| Date | Decision | Why |
|---|---|---|
| 2026-09-18 | New features are **two-asmdef modules** (`External` contract + `Internal` implementation) under `Assets/Modules`, never additions to an existing big class. `PingService` is the template. | Callers compile against contracts only; implementations can be swapped, tested or rewritten without dragging a god class into every assembly. |
| 2026-10-24 | Scenarios ship as **packages**: Unity exports a zip, an admin imports it in the dashboard. Developers never sign in from Unity. | Keeps credentials off developer machines and keeps the admin's work to one drag-and-drop. |
| 2026-10-24 | A new slug imports as a **draft**; an update to a **published** scenario goes live on import, after an explicit warning. | Simplest safe behaviour without a staging area (see Deferred). |
| 2026-10-24 | One scenario = one scene at Addressables address **`scenario/main`** in its own group. The headset resolves keys only in the downloaded catalog. | Every package looks the same to the headset; scenarios can't clash. |
| 2026-10-25 | **Any client's login code works on any headset** and links (or moves) the headset to that client, using one of its headset slots. Registration (enrollment) codes remain as optional SafeSite set-up. | Staff type their company's code on whatever headset is in front of them; SafeSite can demo any client from one PC. Chosen over "a headset stays locked to one client". |
| 2026-10-25 | Client-facing wording is **"login code"**. Never "access code", "set-up code" or "enrollment code". | One name, everywhere. |
| 2026-11-02 | Keep Stephen's structure (`_AppCore`, `_Scenarios`, Bootstrap, his loader, his canvas). Our tools fit it: the exporter applies the package contract **only during export** and restores his group settings; the launcher screens are **generated onto his canvas**; his loader got small hooks (`PlayDownloaded`, and `UnloadScenario` ends downloaded scenarios too). | Two developers on one project; his scenes depend on his singletons, so downloaded scenarios have to run inside Bootstrap through his loader. |
| 2026-11-02 | Large scenarios are **split into ~24 MB bundles** during export, with de-duplication, and retried at 12/6 MB if a file is still too big. | Supabase Free rejects files over 50 MB; Falling Objects is ~215 MB. De-duplication took it from 817 MB to ~230 MB. |
| 2026-11-06 | In-scenario menu on the **left controller's menu button** (and left-hand menu gesture); **admin screen behind 5 logo taps + PIN**. | Trainees need restart/exit everywhere; SafeSite staff need diagnostics and environment switching on a sealed client headset. |
| 2026-11-09 | Headset location: device fix if Android gives one, otherwise the **API estimates from the request IP** (city level, hourly). | Quest/PICO have no GPS; the dashboard map still needs a rough location. |
| 2026-11-10 | **One `SafeSiteScenario` component** is the whole scenario-facing API (score, complete, exit, restart). | Scenario developers wire events in the Inspector; no SafeSite code needed in scenario scripts. |
| 2026-11-11 | **One XR rig**: `_AppCore/Player/--Player--.prefab`. The old `Assets/Prefabs/XR` rigs were deleted. Edit the prefab, never the Bootstrap copy (one intended override). | The Bootstrap copy had drifted (36 unapplied overrides), which hid real bugs: backwards controllers, missing hand-aim features, OVRManager auto-recenter. |
| 2026-11-11 | The headset build tool **owns the OpenXR feature list**; anything not in `HeadsetBuildConfig` is switched off at build time. | Mixed-vendor and missing-feature APKs (0.1.1) only failed on device. |
| 2026-11-11 | **Content Uploader removed** from Unity; the zip + dashboard import is the only upload path. | Follows from the 2026-10-24 decision; one path to maintain. |

## Deferred (postponed, not rejected)

- **Staging updates until Publish.** Today an import to a published scenario goes live
  immediately. Staging needs a backend staging folder and a promote step.
- **Per-developer upload keys / a developer role** so developers could upload from Unity or CI.
- **Per-platform content builds.** No `ISafeSitePlatformBuildHook` exists, so a Meta + PICO export
  builds once and packages the same Android bundles for both. Fine while both use the same shaders
  and settings; add a hook if PICO needs different ones.
- **Supabase plan.** The 50 MB per-file limit is the free plan's. On a paid plan raise
  `maxFileSizeMb` in `Assets/Settings/SafeSite/SafeSiteUploadSettings.asset`. Splitting still helps
  downloads resume in smaller pieces.
- **Projects.** The API supports a headset's current project (`current_project_id`); the app always
  sends `projectId: null`.

## Known issues

| Issue | Impact | Where |
|---|---|---|
| **Licence not enforced in the app.** The app reads `licence.status` but never verifies the signed licence token or its end date; only the testbench does. | An expired client keeps playing installed scenarios offline. Downloads are refused by the API (`licence_locked`). | `ScenarioLauncherService`; verification code exists in `Testbench/Panels/ConfigPanel.cs` |
| **Device token stored in PlayerPrefs.** | Test quality; should use the Android Keystore. | `TODO` in `SafeSiteApiService/Internal/DeviceStore.cs` |
| **Admin PIN lives in the scene** (`7233` in code and Bootstrap). | Change per client build; it keeps trainees out, not attackers. | `AdminPinPanel.pin` |
| **Built-in scenarios record no sessions.** | Fine for development; don't ship client content as built-in. | `SessionAnalyticsService` follows the launcher only |
| **Falling Objects reports completion directly** via `SessionAnalyticsServiceLocator…MarkCompleted()` and exits with `TrainingScenarioLoader.Instance.UnloadScenario()`; it has no `SafeSiteScenario` component and reports no score. | Works, but the export validator warns about it, and there's no score in the dashboard. Moving it to `SafeSiteScenario` is a small change in `FallingObjectsScenarioController`. | `_Scenarios/FallingObjects/Scripts/FallingObjectsScenarioController.cs:381` |
| **Working at Height reports no completion** (no `SafeSiteScenario`, no `MarkCompleted`). | Every session shows "not completed". | `_Scenarios/WorkingAtHeight` |
| **Login API tests** don't cover the headset limit or suspended/archived clients. | Logic is in SQL and mirrors enroll, but untested on the login path. | web `tests/api_test.ts` |
| **Local env on a device.** `Local` is `127.0.0.1`, and the dev server binds to `127.0.0.1`. | Device testing against a PC needs manual changes; use Demo. | `SafeSiteEnvironments.asset`, web `scripts/dev_server.ts` |
| **Legacy launcher UI.** `ScenarioLauncherUI.cs` + `ScenarioLauncherDemo.unity` predate the Bootstrap menu. | Can confuse newcomers; keep for API checks or delete. | `Modules/ScenarioLauncher/UI`, `Demo/` |
| **`ScenarioPackages/` grows.** Each Falling Objects export is ~215 MB and every version is kept. | Disk space. Gitignored. | Delete old zips by hand. |
| **Generated menu screens are overwritten** by **Build Bootstrap Menu**. | Hand edits to them are lost. | Change `BootstrapMenuBuilder.cs` instead. |
| **Stephen's merges** have carried stash-conflict markers and Android build caches (`.utmp`, `.cmake`) into `main` before. `.gitignore` now excludes the caches. | Check merges for `<<<<<<<` in assets. | — |

## Open items

Before this work is "done":

1. **Unity:** merge `suriya/addressable-integrations` into `main` (PR not opened yet). Review the
   Bootstrap scene diff carefully; it's large.
2. **Web:** merge `login-code-links-headset` (2 commits: login code links headset; IP location).
   Then deploy to Demo, then Production:
   - apply migration `20261025150000_login_code_links_headset.sql` (fails if two clients share a
     login code: check first);
   - deploy the `api` function;
   - set the `IPINFO_TOKEN` secret (free tier works without it, at a low daily limit).
3. **Web docs:** `docs/unity/UNITY_AGENT_BRIEF.md` still describes the Content Uploader (section 6)
   and enrollment-first sign-in; it now carries a note pointing to the new flow.
4. Ship an app build (Quest + PICO) with the new rig, menus and location permission before
   importing packages whose scenarios rely on new scripts.
5. Decide on the known issues above, especially licence enforcement and the token store.

## What changed on this branch

`suriya/addressable-integrations`, 2026-10-28 → 2026-11-11, against `main`:

- **Scenario authoring** (`ScenarioAuthoring`): one **SafeSite > Scenarios** window. Create in the
  `_Scenarios` layout or register an existing scene; size check with one-click headset-only fixes;
  one-button build with Android switch, validation, bundle splitting, de-duplication and retries;
  CLI; 15 EditMode tests.
- **ScenarioKit** (new): `SafeSiteScenario` component with custom Inspector, samples, template
  scene includes it; validator warns when nothing reports completion.
- **Headset app** (`ScenarioLauncher`): launcher screens generated onto the Bootstrap canvas;
  login with any client's code (moves the headset); offline sign-in; built-in scenarios for
  development; in-scenario `GlobalMenu`; admin PIN screen with diagnostics, environment switch,
  register headset, delete downloads, forget headset, quit.
- **Analytics** (`SessionAnalyticsService`, new): sessions, score, completion, offline queue,
  heartbeats.
- **Location** (`DeviceLocationService`, new) + Android location permissions in both manifests.
- **Loader** (`TrainingScenarioLoader`): plays downloaded scenarios, implements `IScenarioHost`
  (restart, exit, switch, overlay), resets player and interaction mode.
- **Rig**: one `--Player--` prefab with README; scripts moved to `_AppCore/Scripts/XR`; old rigs
  and `XRRig` editor tools deleted; **SafeSite > Add XR Rig To Active Scene**; export refuses rigs
  in scenario scenes; teleport rays no longer forced on; OVRManager auto-recenter off.
- **Build tool**: Meta Hand Tracking Aim + Hand Interaction Profile features.
- **Removed**: Content Uploader and its CLI/secret store; old sample scenarios
  (`Assets/Scenarios/*`) and their groups; `Assets/Prefabs/XR` rigs.
- **Merged in** from Stephen: hand-tracking teleport, `InteractionStateManager`, Falling Objects
  scenario, audio/voiceover/subtitle and utility scripts.

`login-code-links-headset` in `safesite-web`: `POST /v1/device/login` + `login_headset` SQL function
+ unique login codes; heartbeat IP-location fallback (`lib/geoip.ts`); `demo-setup` prints only the
login code; README and OpenAPI updated; tests.
