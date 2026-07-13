# Publish a new version of Bump.Sdk

When SDK changes land on main, consuming projects can't see them until you cut a release. Publishing is tag-driven: push a `sdk-v*` tag and the `sdk-publish` workflow packs the project and pushes the package to GitHub Packages. Nothing is packed or pushed from a developer machine, so every published version maps to an exact commit on main.

The moving parts:

- **Package metadata.** Lives in `src/Bump.Sdk/Bump.Sdk.csproj` (PackageId, Description, RepositoryUrl). The `VersionPrefix` there (0.1.0) is only a fallback for local builds; the real version comes from the tag.
- **Workflow.** `.github/workflows/sdk-publish.yml`. Triggers on any tag matching `sdk-v*`, strips the prefix to get the version, runs `dotnet pack -p:Version=<version>`, and pushes with the built-in `GITHUB_TOKEN`. No secrets to manage or rotate.
- **Versioning.** Manual semver, independent of the app deploy scheme in `build/version-prefix.txt`. The SDK and the deployed app are different artifacts with different lifecycles.

## Steps

1. **Confirm main is ready.** The SDK changes are merged and the `build` workflow is green on main. The workflow packs whatever the tagged commit contains.

2. **Pick the version.** Look at the latest published version under [github.com/daniel-miller?tab=packages](https://github.com/daniel-miller?tab=packages). While the SDK is at 0.x, bump the minor for new capability or breaking changes and the patch for fixes. First release was `0.1.0`.

3. **Optional local sanity pack.** Worth doing when the csproj or dependencies changed:

   ```powershell
   dotnet pack src/Bump.Sdk/Bump.Sdk.csproj -c Release -p:Version=0.2.0 -o tmp/pack
   ```

   Open the nupkg (it's a zip) and confirm the nuspec metadata and `lib/net10.0/Bump.Sdk.dll` look right. `tmp/` is gitignored.

4. **Tag and push.**

   ```powershell
   git checkout main
   git pull
   git tag sdk-v0.2.0
   git push origin sdk-v0.2.0
   ```

5. **Watch the run.** `gh run watch` or the Actions tab. The run takes about 20 seconds. The push step should end with `Your package was pushed.`

6. **Verify.** The new version appears under [github.com/daniel-miller?tab=packages](https://github.com/daniel-miller?tab=packages) on the Bump.Sdk package page.

## Troubleshooting

| Symptom | Cause | Fix |
| :--- | :--- | :--- |
| Push step fails with 409 Conflict | A package with that version already exists. GitHub Packages rejects re-publishing a version, even a deleted one is reserved for a while. | Cut a new patch version with a new tag. Don't try to reuse version numbers. |
| Tagged the wrong commit | The tag points somewhere other than the intended main commit. | If the workflow hasn't run yet: `git push origin :refs/tags/sdk-v0.2.0`, retag, push. If it published: treat the version as burned and publish the correct commit as the next patch. |
| Workflow didn't trigger | Tag doesn't match `sdk-v*`, or it was created on GitHub without a push event. | Check the tag name; push tags from the command line. |

## After publishing

Consumers pin an explicit version, so nothing updates automatically. Bump the `Version` in each consuming project when you want it to pick up the release - see [sdk-usage.md](sdk-usage.md).
