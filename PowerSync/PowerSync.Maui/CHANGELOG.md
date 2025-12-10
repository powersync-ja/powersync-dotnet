# PowerSync.Maui Changelog

## 0.0.4-alpha.1
- Upstream PowerSync.Common version bump

## 0.0.3-alpha.1
- Upstream PowerSync.Common version bump
- Using the latest (0.4.9) version of the core extension, it introduces support for the Rust Sync implementation and also makes it the default - users can still opt out and use the legacy C# sync implementation as option when calling `connect()`.

## 0.0.2-alpha.1
- Fixed issues related to extension loading when installing package outside of the monorepo.

## 0.0.1-alpha.1

- Introduce package. Support for iOS/Android use cases.

### Platform Runtime Support Added
* MAUI iOS
* MAUI Android