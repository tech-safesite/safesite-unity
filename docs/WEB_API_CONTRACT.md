# The contract with the web repo

The headset app talks to the SafeSite API (a Supabase Edge Function in `safesite-web`), and the
dashboard imports the scenario packages this project exports. This page lists what each side
expects from the other and where the source of truth lives.

## Where the truth lives (in order of authority)

1. `safesite-web/docs/api/openapi.json`: generated from the API routes (`deno task openapi`).
   If anything disagrees with it, it wins. Swagger UI: `<api-base>/docs`.
2. `safesite-web/docs/unity/SCENARIO_PACKAGES.md`: the package (zip) format.
   On the Unity side `ScenarioPackageManifest.cs` defines it; on the dashboard side
   `dashboard/src/lib/scenarioPackage.ts` and `scenarioImport.ts`.
3. `safesite-web/supabase/functions/api/routes/device.ts`: the headset endpoints.
4. `safesite-web/docs/unity/UNITY_AGENT_BRIEF.md`: the original brief the Unity API client and
   testbench were built from. Its **section 6 (Content Uploader) is obsolete**; see below.

**Cross-repo changes:** change the API first, run `deno task openapi` (and `npm run api:types` in
the dashboard), then update the Unity side (`Modules/SafeSiteApiService/External/Models/`) against
the regenerated spec.

## Endpoints the headset uses

All under the environment's `apiBaseUrl` (see `SafeSiteEnvironments.asset`). Device endpoints use
`Authorization: Bearer <device token>` except the two public ones.

| Call | Auth | Used by | When |
|---|---|---|---|
| `POST /v1/device/login` | none | `ScenarioLauncherService.SignInAsync` | Client staff type a login code: links/moves the headset, returns a device token. **New on this branch.** |
| `POST /v1/device/enroll` | none | Admin → Register headset | Optional SafeSite pre-setup with a registration (enrollment) code |
| `GET /v1/device/config` | device | launcher sync | After sign-in, on refresh, when a heartbeat reports a new `configVersion`. Sends `If-None-Match`; a `304` keeps the cache. |
| `GET /v1/device/scenarios/{id}/content` | device | download | Lists the scenario's files for the headset's platform with signed URLs (6 h) |
| `POST /v1/device/heartbeat` | device | `SessionAnalyticsService` | 5 s after start, every 5 min, after sign-in/download |
| `POST /v1/device/sessions` | device | `SessionAnalyticsService` | Queue upload, up to 500 per call, idempotent by `deviceSessionId` |
| `GET /v1/licence/jwks` | none | testbench only | Public key for the ES256 licence token |

The testbench and the (removed) uploader also use `/v1/admin/*` with a Supabase admin sign-in or,
against the local API only, a dev token.

### `POST /v1/device/login`

```json
// request
{ "loginCode": "R4TW-9KXC", "serialNumber": "PA8E10MGH7240305G",
  "platform": "meta", "model": "Oculus Quest 3", "appVersion": "0.3.3" }

// 201
{ "headsetId": "…", "label": "Headset 4", "deviceToken": "…",
  "client": { "id": "…", "name": "Clancy Construction" } }
```

- The code is normalised (uppercase, letters and digits only) and must be 8 characters.
- Same client: keeps label and project, issues a fresh token (the old one stops working).
- Another client: the headset moves, gets that client's next `Headset N` label, project cleared.
- Needs a free headset slot unless the headset already belongs to the client.
- Errors: `403 invalid_login_code`, `403 client_suspended`, `403 client_archived`,
  `409 headset_limit_reached`.
- Implemented by the SQL function `login_headset(...)` (migration
  `20261025150000_login_code_links_headset.sql`), which also makes `clients.login_code` unique.

### Heartbeat location

`location` in the heartbeat is optional. When it's missing, the API looks up the request IP
(`X-Forwarded-For`, set by Supabase's edge) with ipinfo.io and stores a city-level location
(accuracy 20 km), at most once an hour and never over a stored location newer than an hour.
Private/loopback IPs are skipped, so this never runs against the local API. Production should set
the `IPINFO_TOKEN` secret (without it ipinfo's free daily limit applies). **New on this branch.**

### Config fields the app relies on

- `client.id`, `client.name`, `client.loginCodeHash` (`sha256("<clientId>:<CODE>")`): offline
  sign-in and "the code has changed" detection.
- `headset.label`, `headset.platform`.
- `licence.status` (`active`, `expiring`, `expired`, `suspended`) and the signed `licence.token`.
- `scenarios[]`: `id`, `slug`, `title`, `category`, `description`, `durationMinutes`, `version`,
  `languages`. Only scenarios with content for the headset's platform are listed.

## The package format, in brief

`<slug>_<version>.zip`:

```
scenario.json                         manifest (formatVersion 1)
meta/catalog_<version>.bin            Addressables catalog
meta/<hashed>.bundle …                bundles, each ≤ 50 MB
pico/…                                same, when exported for PICO
```

`scenario.json` carries the details (slug, title, category, description, durationMinutes,
languages), `suggestedVersion`, `platforms`, `mainScene` (always `scenario/main`), `builtWith`
(Unity version, app version), `builtAt`, and `files[]` with platform, path, size and SHA-256.
The dashboard checks every file against the manifest before uploading anything. Bump
`formatVersion` on both sides for any breaking change.

## What was removed from Unity on this branch

The **Content Uploader** (`SafeSite > Content Uploader`, `ContentUploadPipeline`,
`ContentUploaderCli`, `ContentUploaderWindow`, `EditorPrefsSecretStore`) uploaded builds straight
from Unity with an admin sign-in. It's gone: developers export a package and the dashboard
uploads it, so no credentials live on developer machines. `SafeSiteUploadSettings` stays (its name
kept so the existing asset loads) and only holds the 50 MB per-file limit.

## Environments

| Name | Who uses it | Data |
|---|---|---|
| Production | Clients | Real. Never default anything to it in tools. |
| Demo | Sales demos, device testing | Fake but realistic; safe to break |
| Local | Development | Seed data, wiped on every API restart |

URLs and publishable keys are in `SafeSiteEnvironments.asset` (Unity) and the web repo's README.
