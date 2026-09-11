# Keeper's Lantern — Test Build Log

Durable handoff and acceptance record for numbered player builds.

## 1.0.9 — Accepted corrective release

- **Date:** 2026-09-11
- **Legacy development branch:** `dev/1.0.9`
- **Frozen legacy source:** `version/1.0.9-test`
- **Exact tested legacy source commit:** `1ed29f48c33768d11e7dcf75cf5ea01a234c9369`
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

### Public reproducibility build

- **Public source commit:** `45a1bc175a456224a91acbf34be820e3271a449e`
- **Public Actions run:** `34636548022`
- **Job:** `103385720318`
- **Result:** success, `0` warnings / `0` errors
- **Artifact:** `KeepersLantern-1.0.9`
- **Artifact ID:** `10278816930`
- **Artifact digest:** `sha256:13ef63d3a0a6fb0d0b134c1fc7481dcdee4d5f475b26a700d28e7c964b518950`
- **Public CI DLL SHA-256:** `11972437727ffd3fadc01007cf49b791b3c62dfe637a26741c671f1a69f3c1ba`

The public source files and project file are byte-for-byte identical to the tested legacy freeze. The rebuilt public DLL is not byte-identical because the SDK embeds the current Git `SourceRevisionId`/informational-version build identity. Direct binary comparison found the differences confined to PE/build identity metadata (timestamp/MVID/source-revision strings), not a production-source change. The **tested release binary identity remains the legacy-tested SHA-256 above**; the public CI run proves the clean public source still builds successfully.

### Requested in-game test

1. Load/reload gameplay during daytime.
2. Stay in the same session until night and verify the personal lantern does not become abnormally bright or oversized.
3. Verify a normal interior -> outdoors-at-night transition remains safe.
4. When diagnostic evidence is available, expect a normalized baseline around the native full-global values historically near Point `0.78` / Ground `1.15`.

### Tester result — ACCEPTED

The tester explicitly reported on 2026-09-11: loaded during daytime, waited until night, and the lantern **did not become abnormally bright**.

The supplied session log additionally records an `OutdoorNight` transition where baseline capture is deferred for one native frame, then captured as `live=0.768/1.133`, `globalK=0.985`, normalized to `0.78/1.15`, followed by normal lantern activation. This is consistent with the intended 1.0.9 invariant rather than the old approximately five-times-too-small daytime baseline.

### Conclusion / next action

**ACCEPTED.** 1.0.9 is the stable corrective release and supersedes 1.0.7. `main` contains the accepted public baseline; `baseline/1.0.9-accepted` freezes the exact public source/bootstrap commit. Documentation-only provenance updates after that freeze do not alter the accepted runtime source and do not trigger another build.

---

## Historical note — 1.0.7

1.0.7 correctly prevented direct interior-preset capture but was later shown to capture daytime-attenuated live intensity as if it were a full baseline. It is **superseded / not a complete fix**.
