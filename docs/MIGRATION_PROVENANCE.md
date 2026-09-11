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

## Public accepted source

- Public bootstrap / accepted source commit: `45a1bc175a456224a91acbf34be820e3271a449e`
- Frozen public ref: `baseline/1.0.9-accepted`
- Public Actions reproducibility run: `34636548022`
- Public job: `103385720318`
- Result: success, `0` warnings / `0` errors
- Public artifact ID: `10278816930`
- Public artifact digest: `sha256:13ef63d3a0a6fb0d0b134c1fc7481dcdee4d5f475b26a700d28e7c964b518950`
- Public CI DLL SHA-256: `11972437727ffd3fadc01007cf49b791b3c62dfe637a26741c671f1a69f3c1ba`

The four production source blobs, `KeepersLantern.csproj`, and `nuget.config` in the public bootstrap are byte-for-byte identical to the frozen legacy 1.0.9 source.

The public rebuild is intentionally recorded separately from the tested binary identity. The .NET SDK embeds the checked-out Git revision in assembly informational/build metadata, so rebuilding the same source under the new public commit produces a different raw DLL hash. Direct binary comparison found the differences confined to PE/build identity metadata (timestamp, MVID, and embedded source-revision strings). No runtime source changed. The tester-accepted binary remains identified by SHA-256 `2c3a2ea5da5204153c00eaa0ba77c36f96a0977535837a450d2cbe22e4cef09a`.

## What was imported

The public bootstrap deliberately imports only the current production baseline and maintainable public metadata:

- the four source files compiled by `KeepersLantern.csproj`;
- `KeepersLantern.csproj`;
- `nuget.config`;
- public README / AGENTS / changelog;
- accepted baseline and build/provenance documentation;
- economical public CI;
- `.gitignore`.

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
