# PowerSync .NET SDK

## Releasing

1. Ensure all changes you want to release are merged into `main`.

2. Ensure the [Common changelog](./PowerSync/PowerSync.Common/CHANGELOG.md) has been updated with a _new, well-formatted version number._
   - The [release workflow](./.github/workflows/release.yml) obtains the version number by searching for the top-most version line (prefixed with `## `), stripping the prefix, and taking the remaining text as the version number.
   - By convention, we generally update the changelog within the PRs that add the changes. This sometimes leads to the version number in the changelog being a version ahead of the released version (eg. latest released version is v0.0.3, but changelog has v0.0.4.)

3. Run the `Release` workflow on Github. This will:
   - Extract the version number from the changelog.
   - Create a release for the package on Nuget.
   - Create a Github release containing the changelog contents.

## Dev Releases

1. Ensure all changes you want to release are merged into `main`.

2. **!! IMPORTANT !!** Ensure the changelog files have release numbers ending in `-dev.xxx`, eg. `0.1.0-dev.1`.
   - If you don't add the suffix, the workflow might try to create a release with a version number you didn't expect.

3. Run the `Create Dev Release` workflow on Github, selecting the branch with your changes as the base branch.
