# Local testing, end to end

How to run the whole loop on one PC (API, dashboard, Unity) without touching Production or Demo:
export a scenario, import it, sign in on Bootstrap, play it, and see the session in the dashboard.
Then: tests, the API testbench, and testing on a real headset.

## Prerequisites

- Both repos checked out side by side:
  - `SafeSite-Unity/safesite-unity` (this repo)
  - `SafeSite-Web/safesite-web` (API + dashboard)
- Unity 6000.0.65f1 with Android Build Support.
- Node.js (for `npx` and the dashboard).
- **Deno.** If `deno` isn't on your PATH, prefix every `deno` command with `npx -y`
  (`npx -y deno task dev`). That's how it runs on Suriya's machine.

## The loop

### 1. Start the local API

```bash
cd SafeSite-Web/safesite-web/supabase/functions/api
npx -y deno task dev
```

API on `http://127.0.0.1:8787/api` with seed data (clients, scenarios) in an in-memory Postgres
(PGlite). Storage is on disk under `supabase/.local-storage/`. **Everything is wiped when it
restarts.** Swagger UI: `http://127.0.0.1:8787/api/docs`.

### 2. Export a scenario package

In Unity: **SafeSite > Scenarios**, pick a scenario, **Build package for headsets**. The zip lands
in `SafeSite_Unity/ScenarioPackages/`.

Use a small scenario (e.g. `test`) for quick loops; Falling Objects is ~215 MB.

### 3. Import it and get a login code

```bash
cd SafeSite-Web/safesite-web/supabase/functions/api
npx -y deno task demo-setup <path-to-a-folder-of-zips> [--client "Clancy Construction"] [--keep-existing]
```

It imports **every zip in the folder**, publishes them, gives them to the client (first active
client unless `--client`), replacing its scenarios unless `--keep-existing`, and prints:

```
Added 1 scenario(s) to Clancy Construction (1 in total).
Login code for the Unity launcher: ABCD-EFGH
```

`ScenarioPackages/` collects every export (several versions of each scenario), so copy the zips
you want into a separate folder and point `demo-setup` at that. Rerun after every API restart.

### 4. Point Bootstrap at Local

Open `Assets/_AppCore/Scenes/Bootstrap.unity`, select
`--Singletons / Persistant --/SafeSite Services`, set **Scenario Launcher Service → Environment
Name** to `Local`. (Or, in Play Mode: 5 taps on the logo → PIN → Environment until it says Local;
that's remembered in PlayerPrefs until you switch back.)

> **Set it back to `Production` before committing the scene.**

### 5. Play

Press Play. Enter the login code on the keypad, pick the scenario, **Start**. It downloads from the
local API, fades, loads. Finish it (or call Complete via the `SafeSiteScenario` Inspector buttons),
exit.

The Console shows the whole story: `[ScenarioLauncherService] Linked to … as Headset N`,
`[SessionAnalyticsService] Recording …`, `Queued session …`, `Uploaded 1 sessions: 1 new …`.

### 6. See it in the dashboard (optional)

```bash
cd SafeSite-Web/safesite-web/dashboard
npm run dev:local-api
```

Open the URL Vite prints (port 5174) and sign in with the local-only dev login from the web
repo's README. The client's page shows the headset ("Headset N", last seen, app version,
installed scenarios, location if any) and the usage pages show the session.

## Things that behave differently locally

- **No IP location.** The headset reaches the API directly (no `X-Forwarded-For`), so the IP
  fallback never runs and the headset has no location on the map unless the device sent a fix.
- **The API forgets headsets on restart.** The app's stored token becomes invalid; the next sync
  signs out with "This headset was signed out…". Sign in again.
- **On a PC, each login code is its own headset** (`serialPerCode`): signing in with two clients'
  codes creates two headsets, one per client. A real headset moves between clients instead.
- **Shaders in the Editor:** downloaded bundles are Android builds; the Editor swaps in its own
  shaders by name so the scene draws correctly.

## Testing on a headset against the local API

`127.0.0.1` on the headset is the headset itself. To test a device build against your PC:

1. Make the dev server listen on the LAN: `scripts/dev_server.ts` binds to `127.0.0.1` only
   (`Deno.serve({ port, hostname: "127.0.0.1" })`); change it to `0.0.0.0` locally (don't commit)
   and allow port 8787 through the Windows firewall.
2. Add an environment to `SafeSiteEnvironments.asset` with `http://<pc-lan-ip>:8787/api`, or use Demo
   instead (simpler: import the zip in the Demo dashboard).
3. Android blocks cleartext HTTP by default in release builds; a development build or a network
   security config is needed for `http://`.

For most device testing, use **Demo**: import the package in the Demo dashboard, give it to a demo
client, sign in on the headset with that client's login code.

## Automated tests

**Unity** (Window > General > Test Runner > EditMode):

- `Modules.ScenarioAuthoring.Tests.Editor`: 15 tests covering scenario details validation, package
  manifest JSON, scenario creation, the export contract and its restore, bundle splitting, the size
  check, naming. Run these after touching anything in `ScenarioAuthoring` or `SafeSiteApiService/Editor`.

**API** (web repo):

```bash
cd SafeSite-Web/safesite-web/supabase/functions/api
npx -y deno task test
```

Includes this branch's tests for `POST /v1/device/login` (a bad code is refused, the first sign-in
links the headset, signing in again replaces the old token, another client's code moves the headset)
and the IP-location fallback on heartbeat. The headset limit and suspended/archived clients on
login are **not** covered yet (the SQL function handles them).

## The API testbench scene

`Assets/Modules/SafeSiteApiService/Testbench/SafeSiteApiTestbench.unity` calls every device
endpoint by hand: admin helper (get an enrollment code), enroll, config & licence (verifies the
licence token), content download and Addressables loading, heartbeat, sessions, a one-click smoke
test, and a log console with secrets redacted. Use it to debug the API without the menus.

The older `Modules/ScenarioLauncher/Demo/ScenarioLauncherDemo.unity` runs the launcher with a plain
debug UI (environment Local). Quick for API checks, but not the real app.
