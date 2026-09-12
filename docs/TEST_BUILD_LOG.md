# Keeper's Lantern — Test Build Log

Durable handoff and acceptance record for numbered player builds.

## 1.0.10 — REJECTED Save Now compatibility candidate

- **Date:** 2026-09-12
- **Development branch:** `dev/1.0.10`
- **Frozen handed source:** `candidate/1.0.10`
- **Exact handed source commit:** `ba5d1ef54354dc0b191b1b5ee93491e14d91547f`
- **Draft PR:** `#1` — `1.0.10: refresh vanilla environment after Save Now direct interior load`
- **Purpose / hypothesis:** Save Now restores saved coordinates with `Player.PlaceAtPos` after the normal load events but does not refresh the environment preset. A direct interior load can therefore retain a stale temporary vanilla LUT/filter state until a normal exit/re-entry makes Graveyard Keeper reapply the current preset.

### Exact runtime change

- detect Save Now by BepInEx GUID `p1xel8ted.gyk.savenow`;
- arm one compatibility refresh for each newly spawned gameplay `Player(Clone)`;
- preserve the existing `0.50 s` save/environment settle gate;
- if the settled first location is a normal interior with a real current preset, reapply that **same current vanilla preset** once through `EnvironmentEngine.ApplyEnvironmentPreset`;
- clear the compatibility request without action for outdoor or dungeon starts;
- fail closed if the vanilla engine/current preset/method cannot be resolved;
- do **not** hardcode ambient/LUT values;
- do **not** call `SetEngineGlobalState` or `UpdateZone`;
- release any stale KeepersLantern ambient override before asking vanilla to reapply its preset;
- all four bundled component versions aligned to `1.0.10`.

### Must remain unchanged from 1.0.9

- outdoor night `0.70 / cool 0.16`;
- dungeon `0.60 / cool 0.07`, practical-light radius `x1.25`;
- Point target `120 / 1.50 / K1.25 / offset 0,-0.40`;
- Ground target `455 / 1.65 / K0.75`;
- normalized/deferred native Keeper intensity baseline architecture;
- normal-interior enhanced-light hard-off;
- vanilla mortuary passthrough;
- rear-belt visual behavior;
- live `DynamicLights.shadows` architecture;
- outdoor strong-light overlap compensation;
- no custom GL/full-screen edge-darkening renderer;
- no normal per-frame direct `Light.intensity` fighting.

### Build provenance

The first PR build (`34686020066`) correctly failed during compilation because a version-sync edit had accidentally changed the already-accepted belt glow parent call from `_lanternObject.transform` to `_lanternObject`. That accidental change was restored before any DLL was handed out. A full PR diff review then confirmed that the belt, dungeon, and shadow source differ from 1.0.9 only in version/diagnostic text.

Successful candidate build:

- **Workflow:** `Build Keeper's Lantern`
- **Actions run:** `34686144572`
- **Job:** `103533354602`
- **Conclusion:** success, `0` warnings / `0` errors
- **Candidate branch head:** `ba5d1ef54354dc0b191b1b5ee93491e14d91547f`
- **PR merge checkout built by Actions:** `14efe7118b7bbfbe2bf02b3f955c1b63cad4e8fc`
- **Artifact:** `KeepersLantern-1.0.10`
- **Artifact ID:** `10296070959`
- **Artifact ZIP digest:** `sha256:26261a0fb592a78ff91c08b9475b3a9a401a185a2964980bcb8cc76e913d68a9`
- **Raw DLL size:** `66,560` bytes
- **Raw DLL SHA-256:** `1463362c53d8bcfca6f7bc4ab7a65f031a2282615a5723012e26a2ad31027b2b`

The raw DLL hash was independently rechecked after downloading and extracting the Actions artifact and matched the build log exactly.

### Requested in-game test

1. With Save Now enabled, direct-load a save made inside `mortuary`. The initial appearance should already match the appearance after leaving and re-entering; there should no longer be a first-load-only LUT/filter mismatch.
2. Repeat with the church or another normal interior that Save Now can restore into.
3. Return to the main menu and load an interior save again, proving that the one-shot compatibility refresh rearms for a new gameplay `Player(Clone)`.
4. Sanity-check an outdoor daytime load followed by night so the accepted 1.0.9 normalized Keeper-light baseline still behaves normally.
5. Sanity-check dungeon entry/exit; the Save Now compatibility path must not refresh dungeon presets or alter accepted dungeon lighting.

Useful log evidence on a successful direct interior load:

`Save Now compatibility: reapplied current vanilla environment preset after direct interior load | preset=<current preset>.`

### Tester result — REJECTED

Direct-load lighting remained incorrect. The supplied 2026-09-12 runtime log established that the intended compatibility path never ran: Keeper's Lantern initializes before Save Now in the observed BepInEx load order, so the `Awake()` GUID lookup happens before Save Now is registered. The log contains neither the expected `Save Now detected...` line nor the one-shot compatibility refresh line, even though Save Now 2.5.14 loads later during the same startup.

This result therefore does **not** establish whether the delayed vanilla preset reapply itself is effective. It establishes that 1.0.10 never armed the refresh.

### Status

**REJECTED.** Preserve `candidate/1.0.10` as the immutable handed source. Do not merge PR #1 or publish 1.0.10. The next candidate must use a new version and re-check Save Now after plugin startup before arming the refresh.

---

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
