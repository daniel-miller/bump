# Use Bump.Sdk from your project

Bump.Sdk is a private NuGet package hosted on GitHub Packages under the `daniel-miller` account. It's not on nuget.org, and GitHub Packages requires authentication for every restore - unlike nuget.org, even read access needs a token. So consuming the package is a three-part job: authenticate to the feed once per machine, reference the package in the project, and (if the project has CI) give the pipeline the same access.

Feed URL:

```
https://nuget.pkg.github.com/daniel-miller/index.json
```

## One-time machine setup

1. **Create a token.** On GitHub, under [Settings > Developer settings > Personal access tokens (classic)](https://github.com/settings/tokens), generate a classic token with only the `read:packages` scope. Fine-grained tokens don't cover GitHub Packages for NuGet - it has to be classic.

2. **Register the feed** in your user-level NuGet config:

   ```powershell
   dotnet nuget add source https://nuget.pkg.github.com/daniel-miller/index.json --name github-daniel-miller --username daniel-miller --password <PAT>
   ```

   On Windows the password is encrypted in the user-level config automatically. On Linux and macOS, append `--store-password-in-clear-text` - NuGet can't encrypt credentials there.

   Never put the token in a repo-level `nuget.config`. The user-level config lives outside every clone, which is the point.

## Reference the package

In the consuming project:

```xml
<PackageReference Include="Bump.Sdk" Version="0.1.0" />
```

Check the [package page](https://github.com/daniel-miller?tab=packages) for the latest version. Then:

```powershell
dotnet restore
```

If restore fails with 401, the source isn't registered on this machine or the token expired - rerun the setup above.

**Same-solution alternative.** If your solution builds Bump alongside the consumer, skip the package and add a project reference to `src/Bump.Sdk/Bump.Sdk.csproj` instead.

## CI restore

A pipeline that restores the consuming project needs the same feed access. Add a `read:packages` classic PAT as a repo secret (for example `PACKAGES_PAT`), then register the source before restore:

```yaml
- name: Add Bump.Sdk feed
  run: >
    dotnet nuget add source https://nuget.pkg.github.com/daniel-miller/index.json
    --name github-daniel-miller --username daniel-miller
    --password ${{ secrets.PACKAGES_PAT }} --store-password-in-clear-text

- run: dotnet restore
```

The clear-text flag is required on the Linux runners; the config only lives for the duration of the job. If the consuming repo is under the same `daniel-miller` account, the workflow's built-in `GITHUB_TOKEN` also works in place of a PAT - grant the job `packages: read` permission and pass `${{ secrets.GITHUB_TOKEN }}` as the password.

## Update to a new version

Releases are manual, so consumers never move on their own. When a new version is published:

```powershell
dotnet add package Bump.Sdk --version 0.2.0
```

or edit the `Version` attribute directly. Available versions are listed on the [package page](https://github.com/daniel-miller?tab=packages).

## Wire it up

Referencing the package gets you the assembly, not a working integration. Registration in the Bump admin UI, configuration keys, `Program.cs` wiring, and troubleshooting are covered in [exception-reporting.md](exception-reporting.md).
