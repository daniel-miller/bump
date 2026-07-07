# Bump

A status-page and observability platform built around a versioned-app registry. Bump tracks app versions, ingests RFC 7807 problem reports from client applications, probes services for uptime and latency, and publishes per-tenant public status pages with outages, announcements, and email subscribers.

- **App version management.** Register an app, read its current version, and atomically bump major, minor, or patch.
- **Problem reporting.** Ingest RFC 7807 problem reports from clients, store them, and email periodic digests.
- **Uptime monitoring.** Probe HTTP services on a fixed interval, record per-bar uptime and latency, and roll up daily summaries.
- **Outages and announcements.** Publish outage timelines and scheduled announcements, scoped globally or per tenant.
- **Public status pages.** Compose per-tenant or global status, served by a bundled React SPA.
- **Subscribers.** Confirmed-opt-in email subscribers per tenant, with per-board caps and one-click unsubscribe.
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
- A Mailgun account (optional; required for password resets, subscriber confirmations, and alert digests).

## Getting started

### 1. Configure the database connection

`Bump.Api` and `Bump.Worker` both read `Bump:Database:ConnectionString`. Both projects link `config/appsettings.json` (committed defaults) and `config/appsettings.work.json` (gitignored local secrets) at build time. Edit `config/appsettings.work.json` or override via environment variable:

```bash
export Bump__Database__ConnectionString="Host=...;Port=5432;Database=bump;Username=...;Password=..."
```

Schema migrations in `db/*.sql` are applied automatically at API startup by `Migrator`. No manual `psql` step is required for new databases — but you can apply the files manually if you prefer:

```bash
psql -U postgres -d bump -f db/001-create-server.sql
# ...etc
```

### 2. Configure authentication

Bump uses three auth schemes, summarized in the Swagger description:

- **Apps bearer key** — `/api/apps/**`. Pre-shared keys from `Bump:Security:Apps:ApiKeys` (array). Empty array rejects every request with `401`.

  ```json
  {
    "Bump": {
      "Security": {
        "Apps": {
          "ApiKeys": [ "generate-a-long-random-string-per-client" ]
        }
      }
    }
  }
  ```

- **Problems bearer key** — `POST /api/problems`. Single pre-shared key from `Bump:Security:Problems:ApiKey`.

  ```json
  {
    "Bump": {
      "Security": {
        "Problems": { "ApiKey": "change-this-to-a-real-key" }
      }
    }
  }
  ```

- **Session cookie** — `/api/auth/**`, `/api/accounts/**`, and admin surfaces under `/api/admin/**` (`/api/admin/tenants`, `/api/admin/services`, `/api/admin/outages`, `/api/admin/announcements`, `/api/admin/apps`). Established via `POST /api/auth/login`. State-changing requests must include `X-Bump-Csrf` matching the `bump_csrf` cookie. JWT signing key in `Bump:Security:Jwt:Signing`; cookie domain/SameSite/Secure in `Bump:Security:Cookie`.

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

In another terminal, run the SPA in dev mode:

```bash
cd web && npm install && npm run dev
```

The Vite dev server serves at `http://localhost:5173`; the API allows it via `Bump:Cors:AllowedOrigins`. Swagger UI is at `/swagger` — click **Authorize** and paste a bearer key (no `Bearer ` prefix) to exercise the bearer-protected endpoints.

## API

All routes are prefixed with `/api`. Full request/response shapes are in Swagger (`/swagger`).

### Apps — `/api/apps` (Apps bearer)

| Method | Route                              | Description                                                                                |
| :----- | :--------------------------------- | :----------------------------------------------------------------------------------------- |
| POST   | `/api/apps`                        | Create an app. Optional `version` (e.g. `"0.0.4"`); defaults to `0.0.1`.                   |
| GET    | `/api/apps`                        | List all apps.                                                                             |
| GET    | `/api/apps/{slug}`                 | Get one app.                                                                               |
| DELETE | `/api/apps/{slug}`                 | Delete an app.                                                                             |
| GET    | `/api/apps/{slug}/version`         | Get the current version string.                                                            |
| PATCH  | `/api/apps/{slug}/version`         | Set any subset of `major`, `minor`, `patch` to absolute values; unspecified parts unchanged. |
| POST   | `/api/apps/{slug}/version/bumps`   | Body `{ "component": "major"\|"minor"\|"patch" }`. Creates the app if missing.             |

### Problem reports — `/api/problems`

| Method | Route                  | Auth              | Description                                                                                  |
| :----- | :--------------------- | :---------------- | :------------------------------------------------------------------------------------------- |
| POST   | `/api/problems`        | Problems bearer   | Ingest a problem report. Optional `appSlug` links the report to a Bump-managed app.          |
| GET    | `/api/problems`        | Session           | Query stored reports. Filters: `environment`, `application`, `fingerprint`, `from`, `to`, `limit`, `offset`. |
| GET    | `/api/problems/{id}`   | Session           | Get one stored report.                                                                       |

### Auth — `/api/auth`

| Method | Route                              | Description                                          |
| :----- | :--------------------------------- | :--------------------------------------------------- |
| POST   | `/api/auth/login`                  | Email + password (+ TOTP if enrolled). Sets session and CSRF cookies. |
| POST   | `/api/auth/logout`                 | Revoke the current session.                          |
| POST   | `/api/auth/password-resets`        | Request a password reset email (CAPTCHA-protected).  |
| POST   | `/api/auth/password-resets/confirm`| Confirm a reset using the emailed token.             |

### Accounts — `/api/accounts/me` (Session)

Profile read/update, email change with confirmation, password change, and TOTP MFA setup / verify / disable, with recovery codes. See Swagger for shapes.

### Monitoring (Session, admin)

| Group               | Routes                                                                                                  |
| :------------------ | :------------------------------------------------------------------------------------------------------ |
| Tenants             | `GET/POST /api/admin/tenants`, `GET/PATCH/DELETE /api/admin/tenants/{slug}`, `GET/DELETE .../subscribers`. (Public `POST /api/tenants/{slug}/subscribers` is the subscribe-form endpoint.) |
| Services            | `GET/POST /api/admin/services`, `GET/PATCH/DELETE /api/admin/services/{slug}`, `POST .../pause`, `POST .../resume`, `GET .../uptime`, `GET .../latency`. |
| Outages             | `GET/POST /api/admin/outages`, `GET/PATCH /api/admin/outages/{id}`, `POST .../updates`, `POST .../resolve`. |
| Announcements       | `GET/POST /api/admin/announcements`, `PATCH/DELETE /api/admin/announcements/{id}`.                      |
| Apps (read-only)    | `GET /api/admin/apps` — admin-UI listing; bearer-keyed mutations live at `/api/apps`.                   |

### Public status — `/api/status` (no auth)

| Method | Route                                            | Description                                  |
| :----- | :----------------------------------------------- | :------------------------------------------- |
| GET    | `/api/status`                                    | Global status (cross-tenant rollup).         |
| GET    | `/api/status/tenants/{slug}`                     | Per-tenant status (services, outages).       |
| GET    | `/api/status/global/announcements`               | Active global announcements.                 |
| GET    | `/api/status/tenants/{slug}/announcements`       | Active per-tenant announcements.             |

### Subscribers — `/api/subscribers` (no auth, double-opt-in)

| Method | Route                          | Description                                          |
| :----- | :----------------------------- | :--------------------------------------------------- |
| POST   | `/api/tenants/{slug}/subscribers` | Subscribe to a tenant board (sends confirmation).  |
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
  "detail": "No app with slug 'does-not-exist'."
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
- `POST /api/apps/{slug}/version/bumps`
- `POST /api/problems`
- `POST /api/admin/outages`, `POST /api/admin/outages/{id}/updates`, `POST /api/admin/outages/{id}/resolve`
- `POST /api/admin/announcements`
- `POST /api/tenants/{slug}/subscribers`

## Input limits

- Request body: 4 KB for app endpoints, 64 KB for problem reports.
- String fields are capped to match the column widths in `db/*.sql`. Out-of-range input returns `422 Unprocessable Entity`.
- Slugs: lowercase letters, digits, single hyphens; start and end with a letter or digit; max 50 characters.

## SSRF protection (monitor probes)

The probe HTTP client in `Bump.Worker` re-resolves DNS on every connection and rejects any address flagged as private, loopback, link-local, CGNAT, ULA, or multicast (`ProbeAddressGuard`). Auto-redirect is disabled so a 30x to an internal host cannot bypass the guard. URL validation in `MonitorsController` is best-effort because DNS can change between create and probe — the connect-time check is the authoritative barrier.

## Semantic versioning

Bump follows [Semantic Versioning 2.0.0](https://semver.org/). The `version/bumps` endpoint applies the SemVer reset rules atomically: bumping `major` resets `minor` and `patch` to `0`; bumping `minor` resets `patch` to `0`.

## Hosting behind a reverse proxy

If the API is deployed under a path prefix (e.g. `https://host/bump/...`) and the proxy forwards the prefix, set `PathBase`:

```bash
export PathBase=/bump
```

## Configuration reference

Top-level keys under `Bump:` in `config/appsettings.json` / `config/appsettings.work.json`:

| Key                       | Purpose                                                                |
| :------------------------ | :--------------------------------------------------------------------- |
| `Database:ConnectionString` | Postgres connection string.                                          |
| `Security:Apps:ApiKeys`   | Bearer keys for `/api/apps/**`.                                        |
| `Security:Problems:ApiKey`| Bearer key for `POST /api/problems`.                                   |
| `Security:Jwt:*`          | JWT signing key, issuer, audience.                                     |
| `Security:Cookie:*`       | Session cookie domain, SameSite, Secure.                               |
| `Cors:AllowedOrigins`     | SPA origin allowlist.                                                  |
| `Mailgun:*`               | Mailgun API key, domain, From, Region (`us` or `eu`).                  |
| `Monitors:*`              | Probe interval, timeout, degraded-latency threshold, history bars, UA. |
| `Subscribers:MaxPerBoard` | Cap on confirmed subscribers per board.                                |
| `Captcha:Turnstile*`      | Cloudflare Turnstile site key + secret for password-reset CAPTCHA.     |
| `PublicBaseUrl`           | Base URL embedded in outgoing emails.                                  |
| `PollMinutes` *(worker)*  | Worker poll cadence; health turns unhealthy after 3× this without a tick. |

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
├── web/                       # React + Vite + Tailwind SPA
├── build/
│   └── build.ps1              # Builds SPA + publishes dist/<project>.<version>.zip
├── db/                        # SQL migrations (applied automatically on API boot)
├── docs/
│   └── schema.dot             # GraphViz schema diagram source
├── dist/                      # Release artifacts (gitignored)
└── README.md
```

## License

Released under the [MIT License](LICENSE).
