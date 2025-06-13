# PowerSync.Common Changelog

## 0.0.2-dev.1


## 0.0.2-alpha.3
- Minor changes to accommodate PowerSync.MAUI package extension.

## 0.0.2-alpha.2

- Updated core extension to v0.3.14
- Loading last synced time from core extension
- Expose upload and download errors on SyncStatus
- Improved credentials management and error handling. Credentials are invalidated when they expire or become invalid based on responses from the PowerSync service. The frequency of credential fetching has been reduced as a result of this work.

## 0.0.2-alpha.1

- Introduce package. Support for Desktop .NET use cases.

### Platform Runtime Support Added
* linux-arm64
* linux-x64
* osx-arm64
* osx-x64
* wind-x64