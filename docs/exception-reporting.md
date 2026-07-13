# Report exceptions to Bump

When an app running in production throws an unhandled exception, you want that exception on the Bump problems page - not buried in a log file the ops team checks once a week. This guide covers what a consuming project needs to send exception reports to the Bump problems API.

Two integration paths are supported:

- **ILogger provider** (recommended). Anything logged at `Error` or `Critical` level with an exception attached is forwarded automatically. Zero call sites in application code.
- **Explicit capture** via `ExceptionReporter.CaptureAsync(ex, ...)`. Use when you want to enrich the report with extra context or capture non-throwing conditions.

## Prerequisites

- The consuming project must be registered in Bump. Log in to the admin UI, register the app under **Apps**, and note its slug. The slug is required at runtime - reports for an unregistered slug are rejected with 422.
- The environment slug you plan to report against must exist under **Environments**, or be listed among an existing environment's aliases (`live`, `production`, `prod`, `qa`, etc. all commonly alias to a single canonical row).
- Access to the shared Octopus library variable set **Bump** (contains the API endpoint and the Problems bearer key). If your project isn't already using it: **Project variables** > **Library Variable Sets** > **Include** > pick **Bump**.

## Add the SDK

`Bump.Sdk` is published as a NuGet package on GitHub Packages. Feed setup, the package reference, and CI restore are covered in [sdk-usage.md](sdk-usage.md); publishing a new version is covered in [sdk-publishing.md](sdk-publishing.md).

```xml
<PackageReference Include="Bump.Sdk" Version="0.1.0" />
```

If your solution builds Bump alongside the consumer, add a project reference to `src/Bump.Sdk/Bump.Sdk.csproj` instead.

## Configuration keys

The SDK binds a `BumpOptions` record. Consuming projects put the following keys under a `BumpSdk` section in their `appsettings.json`:

| Key                           | Purpose                                                                                   | Source                                        |
| :---------------------------- | :---------------------------------------------------------------------------------------- | :-------------------------------------------- |
| `BumpSdk:Endpoint`            | Base URL of the Bump API.                                                                 | Library set `Bump:Hosting:PublicBaseUrl`      |
| `BumpSdk:ApiKey`              | Bearer key for `POST /api/problems`. Same value across every consumer.                    | Library set `Bump:Security:Problems:ApiKey`   |
| `BumpSdk:AppSlug`             | Slug of the registered Bump app this consumer corresponds to. Unique per consumer.        | Project-local variable in the consumer.       |
| `BumpSdk:Environment`         | Environment slug reported with every problem. Usually the deploy environment name.        | Project-local, typically `#{Octopus.Environment.Name}`. |
| `BumpSdk:ProblemTypePrefix`   | Optional URI prefix for RFC 9457 `type`. Turns bare exception type names into URIs.       | Library set `Bump:ProblemTypePrefix`          |
| `BumpSdk:DefaultStatus`       | HTTP status code reported with every problem unless overridden per call. Defaults to 500. | Omit unless you need to override.             |

The `Endpoint`, `ApiKey`, and `ProblemTypePrefix` values live in the shared library set so a rotation touches one place. The `AppSlug` and `Environment` are project-local because they differ per consumer and per deploy.

## Consumer appsettings.json

```json
{
  "BumpSdk": {
    "Endpoint":          "#{Bump:Hosting:PublicBaseUrl}",
    "ApiKey":            "#{Bump:Security:Problems:ApiKey}",
    "AppSlug":           "openscorm",
    "Environment":       "#{Octopus.Environment.Name}",
    "ProblemTypePrefix": "#{Bump:ProblemTypePrefix}"
  }
}
```

The `#{...}` tokens are resolved during deploy by Octopus. The `AppSlug` value on line 5 is the one you registered in the Bump admin UI. In non-Octopus environments (local dev, CI), substitute literal values or leave the `Endpoint` and `ApiKey` empty and configure via environment variables at runtime.

## Register the ILogger provider

In `Program.cs` (minimal hosting):

```csharp
using Bump.Sdk;

var builder = WebApplication.CreateBuilder(args);

var bumpOptions = builder.Configuration
    .GetSection("BumpSdk")
    .Get<BumpOptions>() ?? new BumpOptions();

builder.Logging.AddBump(bumpOptions, userContextFactory: () =>
{
    // Optional: return a UserContext so problem reports carry the signed-in user's id and email.
    // Return null when no user is authenticated.
    var http = builder.Services.BuildServiceProvider().GetService<IHttpContextAccessor>();
    var user = http?.HttpContext?.User;
    if (user?.Identity?.IsAuthenticated != true) return null;
    return new UserContext(
        Id: Guid.TryParse(user.FindFirst("sub")?.Value, out var id) ? id : (Guid?)null,
        Email: user.FindFirst("email")?.Value);
});
```

Once wired, any call site that does `logger.LogError(ex, "...")` or `logger.LogCritical(ex, "...")` results in a POST to `/api/problems`. Nothing else in application code needs to change.

If you don't need per-request user context, drop the `userContextFactory` argument entirely.

## Explicit capture (optional)

For enriched reports or non-throwing captures, use `ExceptionReporter` directly. Resolve or new it up, then call `CaptureAsync`:

```csharp
var reporter = new ExceptionReporter(bumpOptions);

try
{
    await DoWork();
}
catch (Exception ex)
{
    await reporter.CaptureAsync(
        ex,
        user: new UserContext(Id: currentUserId, Email: currentUserEmail),
        extensions: new Dictionary<string, object>
        {
            ["orderId"] = orderId,
            ["step"]    = "ChargeCard",
        },
        instance: $"/orders/{orderId}/charge");
}
```

Values in `extensions` land in the problem report's `problem_extensions` JSON column and are visible on the problem detail page. Use them for the debugger-relevant context that the exception message alone won't carry.

## Deploy-time version bump (optional)

To have Bump's About page reflect the deployed version of each consumer, add a PowerShell step to the consumer project's Octopus deployment process, after the deploy step:

```powershell
Invoke-RestMethod -Method Post `
  -Uri "#{Bump:Hosting:PublicBaseUrl}/api/apps/#{BumpSdk:AppSlug}/version/bumps" `
  -Headers @{ Authorization = "Bearer #{Bump:Deploy:ApiKey}" } `
  -ContentType "application/json" `
  -Body '{"level":"patch"}'
```

The `Bump:Deploy:ApiKey` variable is a separate bearer key (different auth surface than the Problems key) provided by the same library set. Use `major`, `minor`, or `patch` depending on the release semantics.

If the consumer app auto-registers itself on startup via `POST /api/apps` instead, this step is unnecessary.

## Verify integration

After deploying the consumer with the new configuration:

1. Trigger a known exception in the consumer app (a `/debug/throw` route, a failed action, whatever exists).
2. Check the consumer's own log for a warning like `Bump rejected problem report: 401 ...` or `422 ...`. If present, the wiring is off - see troubleshooting below.
3. If no warning, open the Bump admin UI. The report should appear at `/admin/problems` within a few seconds.

## Troubleshooting

| Symptom                                                          | Likely cause                                                                                              | Fix                                                                                             |
| :--------------------------------------------------------------- | :-------------------------------------------------------------------------------------------------------- | :---------------------------------------------------------------------------------------------- |
| Consumer logs `Bump rejected problem report: 401`                | `BumpSdk:ApiKey` is empty, wrong, or was rotated in Bump without redeploying the consumer.                | Redeploy the consumer so the current library-set value takes effect.                            |
| Consumer logs `422 Unknown app`                                  | The `AppSlug` value doesn't match a row in Bump's `app` table.                                            | Register the app in the Bump admin UI, or fix the `AppSlug` variable in the consumer project.   |
| Consumer logs `422 Unknown environment`                          | The `Environment` value isn't a canonical environment slug and isn't in any environment's aliases.        | Add the value as an alias in the Bump admin UI, or change the consumer's variable to match.     |
| Consumer logs a warning about DNS or timeout                     | `BumpSdk:Endpoint` is wrong, or the Bump API isn't reachable from the consumer host.                      | Check the resolved value; confirm firewall or proxy rules allow outbound to the Bump host.      |
| Reports never appear despite no errors in the consumer log       | You captured explicitly but forgot to `await` `CaptureAsync`, or the process exited before the POST fired. | Await the call, or hand the reporter to a background service that outlives the immediate scope. |

## Rotation

`BumpSdk:ApiKey` (the Problems key) is a shared secret in the library set. Rotating it invalidates every existing consumer's ability to report until they redeploy and pick up the new value. For zero-downtime rotation:

1. In the Bump project, temporarily accept both old and new keys - though the Problems side only allows one key at a time, so this specific rotation causes a short window where reports fail.
2. Alternatively, coordinate a maintenance window: rotate the key, redeploy the API, redeploy every consumer in quick succession.

The Apps side (`Bump:Deploy:ApiKey`, referenced by `Bump:Security:Apps:ApiKeys`) supports multi-key configuration - Bump.Api accepts an array. That side rotates without downtime.

## Related

- `src/Bump.Sdk/BumpOptions.cs` - all fields and their defaults.
- `src/Bump.Sdk/ExceptionReporter.cs` - the HTTP path and payload shape.
- `src/Bump.Api/Controllers/ProblemsController.cs` - the server-side contract.
- `README.md` `## Configuration reference` - the full Bump.Api key list.
