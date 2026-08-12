# Bump

A status-page and observability platform built around a versioned-app registry. Bump tracks app versions, ingests RFC 7807 problem reports from client applications, probes services for uptime and latency, and publishes per-owner public status pages with outages, announcements, and email subscribers. Owners, environments, and servers mirror the infrastructure rosters in `daniel-miller/infra/README.md`.

- **App version management.** Register an app, read its current version, and atomically bump major, minor, or patch.
- **Problem reporting.** Ingest RFC 7807 problem reports from clients, store them, and email periodic digests.
- **Uptime monitoring.** Probe HTTP services on a fixed interval, record per-bar uptime and latency, and roll up daily summaries.
- **Outages and announcements.** Publish outage timelines and scheduled announcements, scoped globally or per owner.
- **Public status pages.** Compose per-owner or global status, served by a bundled React SPA.
- **Subscribers.** Confirmed-opt-in email subscribers per owner, with per-board caps and one-click unsubscribe.
- **Accounts.** Cookie-session login with CSRF, password reset, email change, and TOTP MFA + recovery codes.

Built on .NET 10 (ASP.NET Core MVC), Dapper, Npgsql, Newtonsoft.Json, Serilog, and Mailgun. Frontend is React + Vite + Tailwind, bundled into the API's `wwwroot` at publish time.

## Projects

| Project       | Description                                                                                       |
| :------------ | :------------------------------------------------------------------------------------------------ |
| `Bump.Api`    | Web API and host for the SPA. Runs DB migrations on boot and exposes all `/api/**` endpoints.     |
| `Bump.Sdk`    | Client library for reporting unhandled exceptions from .NET applications to `/api/problems`.      |
| `Bump.Worker` | Background service: monitor probing, alert digests, announcement scheduling, idempotency sweep.   |
| `web/`        | React + Vite + Tailwind SPA. Built into `web/dist` and staged into `src/Bump.Api/wwwroot`.        |

## Requirements

- .NET SDK 10.0 or later (pinned via `global.json`).
- PostgreSQL 13 or later.
- Node.js 24+ and npm (pinned via `.nvmrc`).
- PowerShell 7+ (for the release build script).
- A Mailgun account (optional; required for password resets, subscriber confirmations, and alert digests). Both hosts start without it and log a warning at boot; nothing is delivered until it is set.

## Getting started

### 1. Configure the database connection

Copy `config/appsettings.work.example.json` to `config/appsettings.work.json` and fill it in. The example carries every key both hosts require, so you find out what is missing once instead of one restart at a time.

`Bump.Api` and `Bump.Worker` both read `Bump:Database:ConnectionString`. Both projects link `config/appsettings.json` (committed defaults) and `config/appsettings.work.json` (gitignored local secrets) at build time. Edit `config/appsettings.work.json` or override via environment variable:

```bash
export Bump__Database__ConnectionString="Host=localhost;Port=5432;Database=bump;Username=postgres;Password=YOUR_LOCAL_PASSWORD"
```

Environment variables override `config/appsettings.json`, but not `config/appsettings.work.json` - that file is added last and wins. Production ships without it, so the variables above apply there.

Schema migrations in `db/migrations/*.sql` are applied automatically at API startup by `Migrator`. No manual `psql` step is required for new databases — but you can apply the files manually if you prefer:

```bash
psql -U postgres -d bump -f db/migrations/001-create-server.sql
# ...etc
```

### 2. Configure authentication

Bump uses three auth schemes, summarized in the Swagger description:

Both bearer keys ship empty in `config/appsettings.json` and the API refuses to start until they are set. That is deliberate: an empty Apps list silently `401`s every deploy pipeline, and an empty Problems key silently drops `/api/problems` back to session-only so every SDK consumer stops reporting. Neither looks like a configuration error at the point it happens.

- **Apps bearer key** — `/api/apps/**`. Pre-shared keys from `Bump:Api:Security:Apps:ClientSecrets` (array).

  ```json
  {
    "Bump": {
      "Api": {
        "Security": {
          "Apps": {
            "ClientSecrets": [ "generate-a-long-random-string-per-client" ]
          }
        }
      }
    }
  }
  ```

- **Problems bearer key** — `POST /api/problems`. Single pre-shared key from `Bump:Api:Hosting:ClientSecret`. Same key every `Bump.Sdk` consumer presents, so it is named identically on both sides of the exchange.

  ```json
  {
    "Bump": {
      "Api": {
        "Hosting": { "ClientSecret": "change-this-to-a-real-key" }
      }
    }
  }
  ```

- **Session cookie** — `/api/auth/**`, `/api/accounts/**`, and admin surfaces under `/api/admin/**` (`/api/admin/owners`, `/api/admin/services`, `/api/admin/outages`, `/api/admin/announcements`, `/api/admin/apps`). Established via `POST /api/auth/login`. State-changing requests must include `X-Bump-Csrf` matching the `bump_csrf` cookie. JWT signing key in `Bump:Api:Security:Jwt:Key`; cookie domain/SameSite/Secure in `Bump:Api:Security:Cookie`.

Public surfaces (`/api/health`, `/api/status/**`, `/api/subscribers/confirm`, `/api/subscribers/unsubscribe`, `/swagger`) require no auth.

### 3. Seed an admin account

The API CLI generates a password hash and the matching seed SQL:

```bash
dotnet run --project src/Bump.Api/Bump.Api.csproj -- hash 'your-password'
```

Paste the printed `INSERT INTO account ...` into `psql`. See also `db/seed-admin.sql`.

### 4. Run

```bash
dotnet run --project src/Bump.Api/Bump.Api.csproj
dotnet run --project src/Bump.Worker/Bump.Worker.csproj
```

The two hosts listen on different ports, set per host by `Bump:Api:Hosting:Urls` and `Bump:Worker:Hosting:Urls`. The example work config puts the API on `5135` (what `web/vite.config.ts` proxies to) and the worker on `8080`; committed defaults are `8080` and `8081` for deployment. `tools/start.ps1` runs both and writes pid files.

In another terminal, run the SPA in dev mode:

```bash
cd web && npm install && npm run dev
```

The Vite dev server serves at `http://localhost:5173`; the API allows it via `Bump:Api:AllowedOrigins`. Swagger UI is at `/swagger` — click **Authorize** and paste a bearer key (no `Bearer ` prefix) to exercise the bearer-protected endpoints.

## API

All routes are prefixed with `/api`. Full request/response shapes are in Swagger (`/swagger`).

### Apps — `/api/apps` (Apps bearer)

| Method | Route                              | Description                                                                                |
| :----- | :--------------------------------- | :----------------------------------------------------------------------------------------- |
| POST   | `/api/apps`                        | Create an app. Optional `version` (e.g. `"0.0.4"`); defaults to `0.0.1`.                   |
| GET    | `/api/apps`                        | List all apps.                                                                             |
| GET    | `/api/apps/{handle}`                 | Get one app.                                                                               |
| DELETE | `/api/apps/{handle}`                 | Delete an app.                                                                             |
| GET    | `/api/apps/{handle}/version`         | Get the current version string.                                                            |
| PATCH  | `/api/apps/{handle}/version`         | Set any subset of `major`, `minor`, `patch` to absolute values; unspecified parts unchanged. |
| POST   | `/api/apps/{handle}/version/bumps`   | Body `{ "component": "major"\|"minor"\|"patch" }`. Creates the app if missing.             |

### Problem reports — `/api/problems`

| Method | Route                  | Auth              | Description                                                                                  |
| :----- | :--------------------- | :---------------- | :------------------------------------------------------------------------------------------- |
| POST   | `/api/problems`        | Problems bearer   | Ingest a problem report. Optional `appHandle` links the report to a Bump-managed app.          |
| GET    | `/api/problems`        | Session           | Query stored reports. Filters: `environment`, `application`, `fingerprint`, `from`, `to`, `limit`, `offset`. |
| GET    | `/api/problems/{id}`   | Session           | Get one stored report. `Accept: text/markdown` renders it for pasting into a bug tracker.      |
| POST   | `/api/problems/{id}/resolve`   | Session   | Mark one report resolved.                                                            |
| POST   | `/api/problems/{id}/unresolve` | Session   | Clear the resolved flag on one report.                                               |
| DELETE | `/api/problems/{id}`   | Session           | Permanently delete one report.                                                               |
| POST   | `/api/problems/delete` | Session           | Body `{ "problemKeys": [1, 2] }`. Permanently delete a batch, 500 keys max, in one statement. Returns `{ "deleted": n }`; keys already gone are not counted. |

### Auth — `/api/auth`

| Method | Route                              | Description                                          |
| :----- | :--------------------------------- | :--------------------------------------------------- |
| POST   | `/api/auth/login`                  | Email + password (+ TOTP if enrolled). Sets session and CSRF cookies. |
| POST   | `/api/auth/logout`                 | Revoke the current session.                          |
| POST   | `/api/auth/password-resets`        | Request a password reset email. Rate-limited per IP. |
| POST   | `/api/auth/password-resets/confirm`| Confirm a reset using the emailed token.             |

### Accounts — `/api/accounts/me` (Session)

Profile read/update, email change with confirmation, password change, and TOTP MFA setup / verify / disable, with recovery codes. See Swagger for shapes.

### Monitoring (Session, admin)

| Group               | Routes                                                                                                  |
| :------------------ | :------------------------------------------------------------------------------------------------------ |
| Owners              | `GET/POST /api/admin/owners`, `GET/PATCH/DELETE /api/admin/owners/{handle}`, `GET/DELETE .../subscribers`. (Public `POST /api/owners/{handle}/subscribers` is the subscribe-form endpoint.) |
| Services            | `GET/POST /api/admin/services`, `GET/PATCH/DELETE /api/admin/services/{handle}`, `POST .../pause`, `POST .../resume`, `GET .../uptime`, `GET .../latency`. |
| Outages             | `GET/POST /api/admin/outages`, `GET/PATCH /api/admin/outages/{id}`, `POST .../updates`, `POST .../resolve`. |
| Announcements       | `GET/POST /api/admin/announcements`, `PATCH/DELETE /api/admin/announcements/{id}`.                      |
| Apps (read-only)    | `GET /api/admin/apps` — admin-UI listing; bearer-keyed mutations live at `/api/apps`.                   |

### Public status — `/api/status` (no auth)

| Method | Route                                            | Description                                  |
| :----- | :----------------------------------------------- | :------------------------------------------- |
| GET    | `/api/status`                                    | Global status (cross-owner rollup).          |
| GET    | `/api/status/owners/{handle}`                      | Per-owner status (services, outages).        |
| GET    | `/api/status/global/announcements`               | Active global announcements.                 |
| GET    | `/api/status/owners/{handle}/announcements`        | Active per-owner announcements.              |

### Subscribers — `/api/subscribers` (no auth, double-opt-in)

| Method | Route                          | Description                                          |
| :----- | :----------------------------- | :--------------------------------------------------- |
| POST   | `/api/owners/{handle}/subscribers` | Subscribe to an owner board (sends confirmation).   |
| GET    | `/api/subscribers/confirm`     | Confirm a subscription via the emailed token.        |
| POST   | `/api/subscribers/unsubscribe` | Unsubscribe via the emailed one-click token.         |

### Health

| Method | Route          | Description                                  |
| :----- | :------------- | :------------------------------------------- |
| GET    | `/api/health`  | Liveness probe (200 healthy / 503 unhealthy).|

## Error responses

All non-2xx responses use [RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) `application/problem+json`:

```json
{
  "title": "App not found",
  "status": 404,
  "detail": "No app with handle 'does-not-exist'."
}
```

Fields not relevant to a particular error are omitted.

## Rate limiting

Per-bearer-key fixed-window buckets. 429 responses include `Retry-After`.

- `/api/apps/*` — 120 requests per minute per API key.
- `/api/problems/*` — 600 requests per minute per API key.

## Idempotency

POST endpoints accept an optional `Idempotency-Key` header. Resending the same key with the same body replays the cached response (`Idempotent-Replayed: true`) instead of re-running the handler.

- The key is any string up to 255 characters; a UUID is fine.
- Cached responses are kept for 24 hours; `Bump.Worker` sweeps expired rows.
- Reusing a key with a different body is rejected with `422 Unprocessable Entity`.

Idempotency applies to:

- `POST /api/apps`
- `POST /api/apps/{handle}/version/bumps`
- `POST /api/problems`
- `POST /api/admin/outages`, `POST /api/admin/outages/{id}/updates`, `POST /api/admin/outages/{id}/resolve`
- `POST /api/admin/announcements`
- `POST /api/owners/{handle}/subscribers`

## Input limits

- Request body: 4 KB for app endpoints, 64 KB for problem reports.
- String fields are capped to match the column widths in `db/migrations/*.sql`. Out-of-range input returns `422 Unprocessable Entity`.
- Handles: lowercase letters, digits, single hyphens; start and end with a letter or digit; max 50 characters.

## SSRF protection (monitor probes)

The probe HTTP client in `Bump.Worker` re-resolves DNS on every connection and rejects any address flagged as private, loopback, link-local, CGNAT, ULA, or multicast (`ProbeAddressGuard`). Auto-redirect is disabled so a 30x to an internal host cannot bypass the guard. URL validation in `MonitorsController` is best-effort because DNS can change between create and probe — the connect-time check is the authoritative barrier.

## Semantic versioning

Bump follows [Semantic Versioning 2.0.0](https://semver.org/). The `version/bumps` endpoint applies the SemVer reset rules atomically: bumping `major` resets `minor` and `patch` to `0`; bumping `minor` resets `patch` to `0`.

## Hosting

Bump expects to own a hostname and serve from the root, e.g. `https://bump.example.com/api/...`. Running behind a reverse proxy is fine, as long as it forwards to the root.

Sub-path hosting - mounting Bump at `https://example.com/bump/...` - is not supported, and there is no setting for it. Two reasons, in order of importance.

**It puts Bump on a shared origin.** Anything else served from the same hostname is same-origin with Bump, and path does not divide that boundary. An XSS in a neighboring app can read Bump's DOM and its CSRF cookie, which is deliberately not `HttpOnly` so the SPA can read it. A subdomain per app keeps each one in its own origin.

**Two assumptions are baked into the build.** `web/vite.config.ts` sets no `base`, so the bundled SPA fetches `/assets/*` from the root, and the session and CSRF cookies hardcode `Path = "/"`. Prefix-aware routing alone would fix neither, so a partial fix would produce a blank status page and cookies visible to every other app on the hostname.

If sub-path hosting ever becomes a requirement, it is deliberate work - a Vite `base`, cookie paths derived from the mount point, and prefix-aware routing - not a configuration value.

## Configuration reference

Keys in `config/appsettings.json` / `config/appsettings.work.json`. Each key sits under the process that reads it - `Bump:Api:*` for the API, `Bump:Worker:*` for the worker - and only genuinely shared values stay at the `Bump:` root. Look at the path and you know which host restarts when you change it.

Defaults are not repeated at call sites. Each section binds to a class in `src/Bump.Api/BumpSettings.cs` whose property initializers *are* the defaults, so a value has one home and a section that loses a key during deploy-time substitution cannot silently fall back to a stale literal.

Deploy-time facts, read by both hosts:

| Key                     | Purpose                                                                     |
| :---------------------- | :-------------------------------------------------------------------------- |
| `Release:Environment`   | Deployment label (`work`, `demo`, `test`, `live`). Not `ASPNETCORE_ENVIRONMENT`, which switches behavior. The API refuses to start when empty. |
| `Release:Version`       | Deployed semver, surfaced in the probe user agent and on the About page. Stamped into the published `appsettings.json` by `build/build.ps1`; do not hand-edit. |
| `Serilog:MinimumLevel`  | Log level and per-namespace overrides. Raise a level without cutting a release. |

Shared by the API and the worker:

| Key                              | Purpose                                                                |
| :------------------------------- | :--------------------------------------------------------------------- |
| `Bump:Database:ConnectionString` | Postgres connection string.                                            |
| `Bump:Mailgun:*`                 | Mailgun API key, domain, From, Region (`us` or `eu`). Optional; empty disables outbound email with a warning at boot. |
| `Bump:Services:*`                | Probe interval, timeout, degraded-latency threshold, history bars, abuse contact, maintenance windows. |
| `Bump:Web:BaseUrl`               | Public status-page URL, embedded in outgoing emails and the probe user agent. Both hosts refuse to start when empty. |

API only:

| Key                                           | Purpose                                                                |
| :-------------------------------------------- | :--------------------------------------------------------------------- |
| `Bump:Api:LogPath`                            | Serilog file directory. Defaults to `tmp/logs/api` when empty.         |
| `Bump:Api:Hosting:Urls`                       | Kestrel listen address. Refuses to start when empty.                   |
| `Bump:Api:Hosting:ClientSecret`               | Bearer key for `POST /api/problems`. Same string every `Bump.Sdk` consumer presents. Refuses to start when empty. |
| `Bump:Api:AllowedOrigins`                     | SPA origin allowlist.                                                  |
| `Bump:Api:Security:Apps:ClientSecrets`        | Bearer keys for `/api/apps/**`. Refuses to start when empty.           |
| `Bump:Api:Security:Jwt:{Key,Issuer,Audience}` | JWT signing key, issuer, audience.                                     |
| `Bump:Api:Security:Cookie:*`                  | Session cookie domain, SameSite, Secure.                               |
| `Bump:Api:Security:Tokens:*`                  | Password-reset and email-change link lifetimes, in hours.              |
| `Bump:Api:RateLimits:*`                       | `PermitLimit` and `WindowMinutes` per policy: `Apps`, `Problems`, `Auth`, `AuthLogin`, `Subscribe`, `Status`. Both must be greater than zero. |
| `Bump:Api:Subscribers:MaxPerOwner`            | Cap on confirmed subscribers per owner.                                |

Worker only:

| Key                                | Purpose                                                                |
| :--------------------------------- | :--------------------------------------------------------------------- |
| `Bump:Worker:LogPath`              | Serilog file directory. Defaults to `tmp/logs/worker` when empty.      |
| `Bump:Worker:Hosting:Urls`         | Health-endpoint listen address. Refuses to start when empty.           |
| `Bump:Worker:Alerts:PollSeconds`   | Poll cadence; health turns unhealthy after 3× this without a tick.     |
| `Bump:Worker:Alerts:Contact`       | Recipient of alert-digest emails.                                      |
| `Bump:Worker:Announcements:TickSeconds` | Announcement scheduler tick interval.                             |

## Building release packages

```powershell
pwsh build/build.ps1 -Version 1.2.3
```

This:

1. Builds the SPA with `npm ci && npm run build`.
2. Stages `web/dist` into `src/Bump.Api/wwwroot`.
3. Publishes `Bump.Api`, `Bump.Sdk`, and `Bump.Worker` in Release mode.
4. Produces one `dist/<project>.<version>.zip` per project.

## Repository layout

```
bump/
├── bump.sln
├── src/
│   ├── Bump.Api/              # Web API + SPA host (packages, problems, auth, monitoring, status)
│   ├── Bump.Sdk/              # Client library for exception reporting
│   └── Bump.Worker/           # Probes, alert digests, announcement scheduler, idempotency sweep
├── tests/
│   └── Bump.Api.Tests/        # xUnit tests for Bump.Api
├── web/                       # React + Vite + Tailwind SPA
├── build/
│   └── build.ps1              # Builds SPA + publishes dist/<project>.<version>.zip
├── db/
│   ├── migrations/            # SQL migrations (applied automatically on API boot)
│   ├── export-schema.ps1      # Regenerates schema.sql + schema.dot + schema.svg from the live DB
│   ├── schema.sql             # Starting schema (generated)
│   ├── schema.dot             # GraphViz ER diagram (generated)
│   ├── schema.svg             # Rendered ER diagram (generated)
│   └── seed-admin.sql         # Admin account seed template
├── docs/
│   └── exception-reporting.md # SDK exception-reporting guide
├── tools/                     # Dev-loop scripts (start, stop, reset-database, restore-database)
├── dist/                      # Release artifacts (gitignored)
└── README.md
```

## License

Released under the [MIT License](LICENSE).
