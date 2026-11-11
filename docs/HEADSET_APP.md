# The headset app

What the trainee and the SafeSite admin see on the headset, and the code behind each screen:
sign-in, scenario list, download and play, the in-scenario menu, the hidden admin screen,
sessions and heartbeats, location, and offline behaviour.

- [1. Screens at a glance](#1-screens-at-a-glance)
- [2. Signing in (login codes)](#2-signing-in-login-codes)
- [3. Scenario list, options and download](#3-scenario-list-options-and-download)
- [4. Playing, leaving, switching](#4-playing-leaving-switching)
- [5. The in-scenario menu (GlobalMenu)](#5-the-in-scenario-menu-globalmenu)
- [6. The admin screen](#6-the-admin-screen)
- [7. Environments](#7-environments)
- [8. Sessions, heartbeats and location](#8-sessions-heartbeats-and-location)
- [9. Offline behaviour](#9-offline-behaviour)
- [10. Error messages](#10-error-messages)
- [11. Changing the menus (Build Bootstrap Menu)](#11-changing-the-menus-build-bootstrap-menu)

---

## 1. Screens at a glance

```
Splash
  │
  ├─ not signed in ─────► Login keypad ──(code)──► Scenarios grid
  │                          │                         │
  │                          └─ Built-in scenarios     ├─► Options (details, language, Start)
  │                             (dev builds / admin)   │      └─► Download progress ─► fade ─► scenario
  │                                                    ├─► Headset (company, label, sign out)
  └─ signed in last time ──────────────────────────────┘

Logo ×5 taps (any screen) ─► Admin PIN ─► Admin ─► Register headset

Left controller menu button / left-hand menu gesture / Esc ─► GlobalMenu (overlay, anywhere)
```

| Screen | Component (`Modules/ScenarioLauncher/UI/Menu/`) |
|---|---|
| Whole menu, screen switching | `LauncherMenu` on `MenuItems/MainMenuCanvas/LauncherMenu` |
| Login keypad | `LoginPanel` |
| Scenarios grid | `ScenarioGridPanel` (4 cards per page, icons by slug/category) |
| Scenario options | `ScenarioOptionsPanel` |
| Download progress | `ProgressPanel` |
| Headset details | `HeadsetPanel` |
| Admin PIN | `AdminPinPanel` (opened by `AdminUnlock` on the logo) |
| Admin | `AdminPanel` |
| Register headset | `RegisterHeadsetPanel` |
| In-scenario menu | `GlobalMenu` (its own world-space canvas) |

All of them read and drive one service, `IScenarioLauncherService` (`ScenarioLauncherService` on
`SafeSite Services`). The screens hold no state of their own; they re-render on its `Changed` event.

Launcher states: `SignedOut → SigningIn → Browsing → Downloading → Loading → Playing`.

## 2. Signing in (login codes)

Each client (company) has **one login code**, the same on all of its headsets, shown at the top of
the client's page in the dashboard with a **New code** button.

- 8 characters from `ABCDEFGHJKMNPQRSTUVWXYZ23456789` (no I, L, O, 0, 1). The dashboard shows it
  as `XXXX-XXXX`; case, spaces and dashes don't matter.
- Typed on an in-world keypad (headsets have no keyboard).

What `SignInAsync(code)` does:

1. **If this headset already belongs to a client**, it first tries to sync the config (in case the
   code was replaced). If the code matches that client's `loginCodeHash`
   (`sha256("<clientId>:<CODE>")`), the user is in. If the sync failed because the headset is
   offline, it checks against the cached config instead, so a trainee can sign in with no internet.
2. **Otherwise** (new headset, or another client's code) it calls `POST /v1/device/login`. The API
   links the headset to the code's client, or **moves** it from another client, gives it that
   client's next label (`Headset N`) and a new device token. This uses one of the client's
   headset slots.
3. It syncs the config (scenarios, licence, login-code hash) and goes to **Browsing**.

Signing in is remembered on the headset (the code's hash in PlayerPrefs). Next launch goes straight
to the scenarios. **Sign out** (Headset screen) only forgets that; the headset keeps its client and
its downloads.

When an admin replaces the client's code in the dashboard, every headset signs out on its next
sync with "Your company's login code has changed. Enter the new one."

**PC / Editor:** with `serialPerCode` on (default), the device serial is derived from the PC's id
plus the code (`DEMO-xxxxxxxxxxxx`), so one PC can sign in to several clients and each sees it as
its own headset. Real headsets always use their device id, so the headset moves between clients.

**Registration codes** (enrollment codes in the API) are optional SafeSite set-up, not part of the
client flow. See [Admin](#6-the-admin-screen).

## 3. Scenario list, options and download

- The grid shows the client's scenarios sorted by category, then title, four per page.
- Each card's pill: **Ready** (installed at the current version), **Download** (not installed),
  **Update** (an older version is installed), **Built-in**.
- Card icons come from `ScenarioGridPanel.icons`, matched against the slug or category.
- **Options**: title, description, duration, languages. The chosen language goes to
  `ISessionAnalyticsService.SetLanguage` and is recorded with the session; if it isn't one of the
  scenario's languages, the scenario's first language is used.
- **Start**: `LauncherMenu.playRequested` → `TrainingScenarioLoader.PlayDownloaded(id)`. If the
  scenario isn't installed at the current version it downloads first while the menu shows progress
  (`x.x MB of y.y MB`). Files go to `persistentDataPath/SafeSite/content/<scenarioId>/<version>/`;
  older version folders are deleted once the new one is complete. Signed URLs that expire mid-
  download are refreshed once.
- A refused download (licence expired, scenario removed) leaves the user on the list with a message.

## 4. Playing, leaving, switching

`TrainingScenarioLoader` (on `--Singletons / Persistant --/ScenarioLoader`) owns everything visual
around a scenario, and implements `IScenarioHost` for the module code:

| Action | Sequence |
|---|---|
| Play downloaded | download (menu visible) → fade to black 1 s → hide menu → load catalog + `scenario/main` additively, make it active → fade in 2 s |
| Play built-in | fade to black → `Addressables.LoadSceneAsync(address, Additive)` → fade in |
| Exit (`ExitScenario`, `UnloadScenario`, `SafeSiteScenario.Exit`) | fade to black 2 s → unload scene (and catalog) → player to `StartingTransform` → menu visible → fade in → interaction mode `UIAndTeleport` |
| Restart | switch to the same scenario |
| Switch | fade → unload current → `UIAndTeleport` → load next → fade in. If the next downloaded scenario needs downloading, the menu comes back to show progress first. |

While a load or unload is running `IsBusy` is true and further requests are ignored. If a load
fails the menu comes back and the error is shown.

## 5. The in-scenario menu (GlobalMenu)

Opened by the **left controller's menu button**, the **left-hand menu gesture** (palm towards you,
pinch: the one scene override on `--Player--`) or **Esc** in the Editor. Opens 1.5 m in front of
the user at about eye height, wherever they are; re-positions after a recenter.

- **While a scenario plays:** subtitle "Now playing: <title>", **Resume**, **Restart**,
  **Switch scenario** (list, 5 per page, includes built-in scenarios where allowed), **Exit to
  main menu**.
- **On the main menu:** **Home** (back to the start screen) and Switch (start a scenario directly).
- It won't open mid-fade, or while a download or sign-in is running.
- While it's open, `IScenarioHost.SetOverlayOpen(true)` forces interaction mode `UIAndTeleport` so
  the controller rays work whatever the scenario set; closing restores the scenario's mode.

## 6. The admin screen

For SafeSite staff, hidden from trainees.

**Open it:** tap the SafeSite logo **5 times** quickly (`AdminUnlock.tapsNeeded`), then enter the
PIN.

> **PIN:** set on `AdminPinPanel.pin` in the Bootstrap scene (the code default is `7233`, and
> Bootstrap currently uses the same). **Change it before shipping a build to a new client.**
> 5 wrong tries (`triesBeforeLock`) lock the keypad for 30 s (`lockSeconds`). The comparison is
> constant-time. The PIN is in the scene file, so anyone with the APK and the right tools can find it;
> it keeps trainees out, not attackers.

The admin screen shows:

```
Environment   Production
Registered    Yes
Headset       Headset 4
Headset id    1f6e…
Company       Clancy Construction
State         Browsing   Licence  active
Scenarios     6 listed, 2 downloaded
Device        Oculus Quest 3
App           0.3.3 · Unity 6000.0.65f1 · development
FPS 72   Battery 81% (Discharging)   Memory 312 MB
```

and these buttons (the destructive ones need a second press within 4 s):

| Button | Does |
|---|---|
| Register headset | Opens a keypad for a **registration code** from the dashboard (`POST /v1/device/enroll`). Links the headset to that code's client ahead of time. Optional: signing in with a login code does the same. |
| Environment: … | Cycles Production → Demo → Local. Remembered on this headset. Each environment keeps its own registration and downloads. Only while signed out or browsing. |
| Built-in scenarios: On/Off | Lists the app's built-in scenarios in non-development builds. Always on in development builds. |
| Refresh | Re-syncs the client's scenarios (signed in only). |
| Delete downloads | Deletes every downloaded scenario for this environment. |
| Forget headset | Deletes this environment's token, caches, queue and downloads and signs out. The dashboard still lists the headset until someone removes it there. |
| Quit app | Closes SafeSite. |

## 7. Environments

Defined in `Modules/SafeSiteApiService/External/Resources/SafeSiteEnvironments.asset`:

| Name | API | Use |
|---|---|---|
| Production | Supabase project `mjuilbohduzofdrgpqbt` | Real clients |
| Demo | Supabase project `jqwmaffqjmafwdrggmlh` | Realistic fake data, safe to break |
| Local | `http://127.0.0.1:8787/api` | API on your PC (`deno task dev`). A headset on Wi-Fi can't reach `127.0.0.1`; use the PC's LAN address if testing on device. |

Which one is used, in order: the admin's choice on this headset → `ScenarioLauncherService.environmentName`
in the scene (Bootstrap: **Production**) → the API service's default (Demo). Production is never
restored automatically by the API service's own "remember" setting; the launcher's admin override is
what persists it.

## 8. Sessions, heartbeats and location

`SessionAnalyticsService` (module `SessionAnalyticsService`) watches the launcher:

- A **session** starts when a downloaded scenario reaches `Playing` and ends when it leaves it
  (exit, restart, switch, app quit). It records: scenario id and installed version,
  language, start time, duration (clamped to 24 h), completed, score (if reported). No person is
  ever linked to a session.
- Ended sessions go into an on-disk queue (`session-queue.json`) and are uploaded straight away
  and with every heartbeat until empty (`POST /v1/device/sessions`, batches of 500). Each session has
  a `deviceSessionId` GUID, so retries never double-count. Rejected sessions are logged and dropped.
- Built-in scenarios are **not** recorded.
- **Heartbeat** (`POST /v1/device/heartbeat`): 5 s after start, then every 5 min, plus right after
  sign-in and after a download. Sends app version, installed scenarios (only those in the current
  client's config, because the API refuses the whole heartbeat for an unknown id), and location.
  If the response's `configVersion` differs from the cached config, the config re-syncs.
- **Location** (`DeviceLocationService`): before a heartbeat it asks Android for one fix (asks for
  the location permission the first time), waiting at most 15 s, at most one attempt every 15 min,
  and discards fixes older than 6 h. Quest and PICO have no GPS, so often there's no fix; then the
  heartbeat goes without one and **the API estimates the location from the request IP** (city level,
  about 20 km, at most hourly). The location permissions are in both Android manifest templates.

Inspector settings on `SessionAnalyticsService`: `heartbeatIntervalSeconds` (300),
`firstHeartbeatDelaySeconds` (5), `locationWaitSeconds` (15), `maxFixAgeHours` (6).
On `DeviceLocationService`: `minMinutesBetweenAttempts` (15), `startTimeoutSeconds` (20),
`permissionTimeoutSeconds` (30).

## 9. Offline behaviour

| Situation | What happens |
|---|---|
| Launch offline, signed in last time | Goes to the scenario list from the cached config; refresh fails quietly with a message. |
| Sign in offline with this headset's client's code | Works (checked against the cached hash). |
| Sign in offline with another client's code | "Can't reach SafeSite to check that code…" |
| Play an installed scenario offline | Works. |
| Download offline | Fails with "Can't reach SafeSite…"; the list stays. |
| Sessions recorded offline | Queued on disk, uploaded at the next successful heartbeat. |
| Licence expires while offline | **Not enforced yet.** The API refuses downloads with `licence_locked`, but the app doesn't verify the signed licence token or its end date itself (only the testbench's Config panel does), so installed scenarios keep playing offline. See [DECISIONS.md](DECISIONS.md#known-issues). |
| Headset deactivated in the dashboard | Next sync: signed out, token forgotten, "This headset was signed out. Enter your login code again." Signing in links it again. |

## 10. Error messages

Messages are worded for the person holding the headset (`ScenarioLauncherService.Describe`):

| API error code | Message |
|---|---|
| `invalid_login_code` | That login code isn't right. Check it with your manager or SafeSite contact. |
| `client_suspended` | Your company's SafeSite account is suspended. Contact SafeSite. |
| `client_archived` | Your company's SafeSite account is closed. Contact SafeSite. |
| `headset_limit_reached` | Your company is already using all of its headsets. Contact SafeSite to add more. |
| `licence_locked` | Your licence has expired, so new scenarios can't be downloaded. Contact SafeSite. |
| `no_content`, `not_found` | That scenario isn't available any more. Refresh the list. |
| network error | Can't reach SafeSite. Check the internet connection and try again. |
| any 5xx / transient | SafeSite is having trouble right now. Try again in a minute. |

Registration has its own: invalid/used/expired code, `serial_in_use` (registered to another company:
remove it in the dashboard first).

## 11. Changing the menus (Build Bootstrap Menu)

The main menu canvas (`MenuItems/MainMenuCanvas`) is Stephen's design. The launcher screens (login
keypad, progress, headset, admin PIN, admin, register headset) and the `GlobalMenu` content are
**generated** onto it by **SafeSite > Launcher > Build Bootstrap Menu**
(`Modules/ScenarioLauncher/Editor/BootstrapMenuBuilder.cs`), cloned from the canvas's own panels,
buttons and text so they share its look, and wired to `LauncherMenu`.

- Open Bootstrap first; the builder needs `MenuItems/MainMenuCanvas`.
- It's **re-runnable**: it deletes what it added last time (objects prefixed `launcher_` inside
  existing screens, and the generated screens) and builds them again.
- **Hand edits to generated screens are lost on a rebuild.** Change the builder instead. Edits to
  Stephen's existing screens are kept.
- After running it, check the one `--Player--` override (Left Hand menu gesture →
  `GlobalMenu.Toggle`) is still there, and the `AdminPinPanel.pin` value is what you want, then save
  the scene.

The older `ScenarioLauncherUI.cs` and `ScenarioLauncher/Demo/ScenarioLauncherDemo.unity` are the
first, flat version of the launcher UI. They still work for quick API testing but aren't the app.
