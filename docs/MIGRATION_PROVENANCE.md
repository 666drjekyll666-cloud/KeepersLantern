# Public Migration Provenance

This public repository intentionally starts with a **new clean Git history**. Historical development, experiments, diagnostics, and reverse-engineering material remain in the private legacy repository and were not imported.

## Legacy source

- Legacy private repository: `666drjekyll666-cloud/KeepersLantern-legacy-private`
- Accepted release version: `1.0.9`
- Frozen legacy source ref: `version/1.0.9-test`
- Exact tested legacy source commit: `1ed29f48c33768d11e7dcf75cf5ea01a234c9369`
- Legacy source tree: `3c6a7f77ca85234842fe9ad3ea46cf492ae2ec7c`

## Tested build provenance

- GitHub Actions workflow: `Build KeepersLantern pull request`
- Run: `34547268851`
- Result: success
- Head SHA: `1ed29f48c33768d11e7dcf75cf5ea01a234c9369`
- Artifact: `KeepersLantern-1.0.9`
- Artifact ID: `10179493642`
- Artifact archive digest: `sha256:130c07842482ca3903b8d83bedaa231c04947acef131e94817f53e7952efabc4`
- Tested raw `KeepersLantern.dll` SHA-256: `2c3a2ea5da5204153c00eaa0ba77c36f96a0977535837a450d2cbe22e4cef09a`

## What was imported

The public bootstrap deliberately imports only the current production baseline and maintainable public metadata:

- the four source files compiled by `KeepersLantern.csproj`;
- `KeepersLantern.csproj`;
- `nuget.config`;
- public README / AGENTS / changelog;
- accepted baseline and build/provenance documentation;
- economical public CI;
- `.gitignore`.

The imported production source blobs are byte-for-byte identical to the frozen legacy 1.0.9 source.

## What was intentionally excluded

- historical `agent/*`, `version/*`, POC, and research branches;
- obsolete source variants not compiled by the production project;
- temporary diagnostics and one-off scripts;
- generated DLL/base64 transfer material;
- bulk runtime dumps and research archives;
- game assemblies, extracted game assets, and decompiled game source;
- stale CI experiments and historical workflow clutter.

Deep reverse-engineering evidence belongs in the private research layer rather than becoming a build dependency of this public repository.

## Acceptance

On 2026-09-11 the tester explicitly accepted 1.0.9 for release after reproducing the formerly failing daytime-start -> night path without the lantern becoming abnormally bright.
