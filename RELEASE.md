# PowerSync .NET SDK

## Releasing

1. Ensure all changes you want to release are merged into `main`.

2. Ensure the changelog files for [Common](./PowerSync/PowerSync.Common/CHANGELOG.md) and [SourceGenerators](./PowerSync/PowerSync.Common.SourceGenerators/CHANGELOG.md) have been updated with _new, well-formatted version numbers._
   - The [release workflow](./.github/workflows/release.yml) obtains each package's version number from its own changelog by searching for the top-most version line (prefixed with `## `), stripping the prefix, and taking the remaining text as the version number.
   - The packages are versioned independently. A package whose version is already on NuGet is skipped, so only bump the changelogs of the packages you want to release.
   - By convention, we generally update the changelog within the PRs that add the changes. This sometimes leads to the version number in the changelog being a version ahead of the released version (eg. latest released version is v0.0.3, but changelog has v0.0.4.)

3. Run the `Release` workflow on Github. This will:
   - Extract version numbers from the changelogs.
   - Create a release for each package on Nuget.
   - Create a draft Github release for each package.

## Dev Releases

1. Ensure all changes you want to release are merged into `main`.

2. **!! IMPORTANT !!** Ensure the changelog files of both packages have release numbers ending in `-dev.xxx`, eg. `0.1.0-dev.1`.
   - If you don't add the suffix, the workflow might try to create a release with a version number you didn't expect. There are checks for this, but better safe than sorry.

3. Run the `Create Dev Release` workflow on Github, selecting the branch with your changes as the base branch.
