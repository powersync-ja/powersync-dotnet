# PowerSync .NET SDK

## Releasing

1. Ensure all changes you want to release are merged into `main`.

2. Ensure the changelog files for both [Common](./PowerSync/PowerSync.Common/CHANGELOG.md) and [Maui](./PowerSync/PowerSync.Common/CHANGELOG.md) have been updated with _new, well-formatted version numbers._
   - The [release workflow](./.github/workflows/release.yml) obtains the version number by searching for the top-most version line (prefixed with `## `), stripping the prefix, and taking the remaining text as the version number. It does this for both changelogs.
   - If a release only includes changes to `PowerSync.Common`, don't forget to also update `PowerSync.Maui`'s changelog so that a new release is created that uses the updated version of `PowerSync.Common`.
   - By convention, we generally update the changelog within the PRs that add the changes. This sometimes leads to the version number in the changelog being a version ahead of the released version (eg. latest released version is v0.0.3, but changelog has v0.0.4.)

3. Run the `Release` workflow on Github. This will:
   - Extract version numbers from the changelogs.
   - Create a release for both packages on Nuget.
   - Create a Github release containing the changelog contents.
