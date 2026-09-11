# Keeper's Lantern — Test Build Log

Durable handoff and acceptance record for numbered player builds.

## 1.0.9 — Accepted corrective release

- **Date:** 2026-09-11
- **Legacy development branch:** `dev/1.0.9`
- **Frozen legacy source:** `version/1.0.9-test`
- **Exact tested legacy source commit:** `1ed29f48c33768d11e7dcf75cf5ea01a234c9369`
- **Original PR merge checkout used by the tested CI run:** `9a0e5e76ff0b646ffd08b104b4ada1e83f8ea635`
- **Public accepted source freeze:** `baseline/1.0.9-accepted` at `45a1bc175a456224a91acbf34be820e3271a449e`
- **Purpose / hypothesis:** fix the remaining 1.0.7 overexposure bug by calibrating the Keeper's native light from settled DynamicLights output without baking the current `TimeOfDay.light_intensity_k` into the stored baseline.

### Exact runtime changes

- all four bundled components versioned `1.0.9`;
- `Bind()` no longer captures intensity baseline immediately;
- first baseline acquisition waits for settled native output;
- live Point/Ground intensity is normalized by the verified `TimeOfDay.light_intensity_k` before storage;
- baseline capture fails closed if the global coefficient cannot be read;
- a valid normalized baseline is preserved across later interior/rebind transitions;
- F10 telemetry reports baseline validity, capture coefficient, and normalized intensity;
- stale Belt Visual diagnostic version text corrected, with no belt behavior change;
- outdoor night default changed `0.60 -> 0.70` while cool tint remains `0.16`;
- procedural dungeon default changed `0.50 -> 0.60` while cool tint remains `0.07` and practical-light radius remains `x1.25`;
- exact prior shipped defaults migrate to the new values; deliberate custom values remain untouched.

### Must remain unchanged

- Point target `120 / 1.50 / K1.25 / offset 0,-0.40`;
- Ground target `455 / 1.65 / K0.75`;
- normal-interior hard-off;
- vanilla mortuary passthrough;
- rear-belt visual position/sway;
- live-shadow architecture;
- outdoor strong-light overlap compensation;
- Configuration Manager release surface;
- no custom GL edge-darkening pass;
- no normal per-frame direct intensity fighting.

### Tested build provenance

- **Workflow:** `Build KeepersLantern pull request`
- **Legacy Actions run:** `34547268851`
- **Conclusion:** success
- **Artifact:** `KeepersLantern-1.0.9`
- **Artifact ID:** `10179493642`
- **Artifact digest:** `sha256:130c07842482ca3903b8d83bedaa231c04947acef131e94817f53e7952efabc4`
- **Raw tested DLL SHA-256:** `2c3a2ea5da5204153c00eaa0ba77c36f96a0977535837a450d2cbe22e4cef09a`

The workflow built GitHub's PR merge checkout `9a0e5e76ff0b646ffd08b104b4ada1e83f8ea635`, which merged the accepted head `1ed29f48c33768d11e7dcf75cf5ea01a234c9369` into its then-current base. Production source/project bytes relevant to the DLL match the accepted head; the merge checkout matters only to build identity metadata embedded by the SDK.

### Public source reproducibility build

- **Public source commit:** `45a1bc175a456224a91acbf34be820e3271a449e`
- **Public Actions run:** `34636548022`
- **Job:** `103385720318`
- **Result:** success, `0` warnings / `0` errors
- **Artifact:** `KeepersLantern-1.0.9`
- **Artifact ID:** `10278816930`
- **Artifact digest:** `sha256:13ef63d3a0a6fb0d0b134c1fc7481dcdee4d5f475b26a700d28e7c964b518950`
- **Public CI DLL SHA-256:** `11972437727ffd3fadc01007cf49b791b3c62dfe637a26741c671f1a69f3c1ba`

The public source files and project file are byte-for-byte identical to the tested legacy freeze. A normal rebuild from the public commit is not byte-identical because the SDK embeds the current Git source revision/build identity. Direct binary comparison found those differences confined to build identity metadata, not production-source behavior.

### Exact public reproduction for stable publication

To publish the exact tested bytes rather than a merely equivalent rebuild, a one-time public reproduction run built the accepted public source while restoring the original PR merge `SourceRevisionId` used by the tested CI checkout.

- **Actions run:** `34645213316`
- **Job:** `103414235120`
- **Result:** success; the build hash gate required exact equality with the accepted DLL.
- **Artifact:** `KeepersLantern-1.0.9-exact`
- **Artifact ID:** `10281782375`
- **Artifact digest:** `sha256:7fb79855311e57ded3bae442e9cdb483a59e1591ff442fe313086b0539ea2ad5`
- **Raw DLL SHA-256:** `2c3a2ea5da5204153c00eaa0ba77c36f96a0977535837a450d2cbe22e4cef09a`

This establishes that the accepted binary can be reproduced exactly from the accepted public production source when the original PR merge build identity is restored.

### Requested in-game test

1. Load/reload gameplay during daytime.
2. Stay in the same session until night and verify the personal lantern does not become abnormally bright or oversized.
3. Verify a normal interior -> outdoors-at-night transition remains safe.
4. When diagnostic evidence is available, expect a normalized baseline around the native full-global values historically near Point `0.78` / Ground `1.15`.

### Tester result — ACCEPTED

The tester explicitly reported on 2026-09-11: loaded during daytime, waited until night, and the lantern **did not become abnormally bright**.

The supplied session log additionally records an `OutdoorNight` transition where baseline capture is deferred for one native frame, then captured as `live=0.768/1.133`, `globalK=0.985`, normalized to `0.78/1.15`, followed by normal lantern activation. This is consistent with the intended 1.0.9 invariant rather than the old approximately five-times-too-small daytime baseline.

### Stable GitHub Release

- **Tag:** `v1.0.9`
- **Release ID:** `387322337`
- **Target:** exact accepted public source `45a1bc175a456224a91acbf34be820e3271a449e`
- **Publication run:** `34645481093`
- **Asset ID:** `557974941`
- **Asset:** `KeepersLantern-1.0.9.dll`
- **Asset SHA-256:** `2c3a2ea5da5204153c00eaa0ba77c36f96a0977535837a450d2cbe22e4cef09a`

The GitHub Release therefore contains the exact player-tested binary bytes, not a different rebuild under the same version.

### Conclusion / next action

**ACCEPTED AND PUBLISHED.** 1.0.9 is the stable corrective release and supersedes 1.0.7. `main` contains the accepted public line; `baseline/1.0.9-accepted` freezes the accepted public production source. GitHub Releases is the canonical stable-download surface.

---

## Historical note — 1.0.7

1.0.7 correctly prevented direct interior-preset capture but was later shown to capture daytime-attenuated live intensity as if it were a full baseline. It is **superseded / not a complete fix**.
