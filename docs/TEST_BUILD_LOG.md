# Keeper's Lantern — Test Build Log

Durable handoff and acceptance record for numbered player builds.

## 1.0.11 — REJECTED Save Now late-detection compatibility candidate

- **Date:** 2026-09-12
- **Development branch:** `dev/1.0.11`
- **Frozen handed source:** `candidate/1.0.11`
- **Exact handed source commit:** `263aaed2a7c3f71d0b41d6b90f29946bed906f81`
- **Draft PR:** `#2` — `1.0.11: detect Save Now after plugin startup`
- **Purpose / hypothesis:** 1.0.10 never exercised its delayed vanilla environment-preset refresh because Keeper's Lantern initialized before Save Now and cached a false GUID lookup in `Awake()`. Re-detect Save Now once when the gameplay `Player(Clone)` appears, after BepInEx plugin startup has completed, then exercise the same narrow one-shot interior refresh.

### Exact runtime change

- preserve the 1.0.10 one-shot direct-interior compatibility path;
- keep the early Save Now GUID lookup as a harmless fast path;
- when a new gameplay `Player(Clone)` is detected, re-read `Chainloader.PluginInfos.ContainsKey("p1xel8ted.gyk.savenow")` exactly once before arming the refresh;
- if the late lookup newly finds Save Now, log `Save Now detected after plugin startup...`;
- after the existing `0.50 s` save/environment settle gate, if the settled first location is a normal interior with a real current preset, reapply that same current vanilla preset once through `EnvironmentEngine.ApplyEnvironmentPreset`;
- clear the request without action for outdoor or procedural-dungeon starts;
- no repeated plugin-registry lookup in steady state;
- no hardcoded ambient/LUT values;
- no `SetEngineGlobalState` or `UpdateZone`;
- preserve all accepted 1.0.9 lighting balance and normalized Keeper-light baseline behavior;
- all four bundled component versions aligned to `1.0.11`; belt/dungeon/shadow runtime logic is unchanged.

### Build provenance

- **Workflow:** `Build Keeper's Lantern`
- **Actions run:** `34687039884`
- **Job:** `103535720523`
- **Conclusion:** success, `0` warnings / `0` errors
- **Candidate branch head:** `263aaed2a7c3f71d0b41d6b90f29946bed906f81`
- **PR merge checkout built by Actions:** `29cc5ec5688ee0a83476a46e4567fe8ab55d2314`
- **Artifact:** `KeepersLantern-1.0.11`
- **Artifact ID:** `10295847975`
- **Artifact ZIP digest:** `sha256:369a0d9d81087e1fa273d67b8f57a7bbf4c1ebf9cb40d8154620a9f057736c1b`
- **Raw DLL size:** `67,072` bytes
- **Raw DLL SHA-256:** `a446a6883a2e7b4152cf70e49c794b296509aa58b762f569ec0f0fbaa1e98137`

The raw DLL hash was independently rechecked after downloading and extracting the Actions artifact and matched the CI build log exactly.

### Requested in-game test

1. Direct-load the same Save Now save inside the church basement / alchemy laboratory that reproduced the mismatch.
2. Compare the initial appearance with the known-correct appearance after a normal exit/re-entry.
3. The log must contain both:
   - `Save Now detected after plugin startup. Direct-load interior compatibility refresh is armed for this gameplay load.`
   - `Save Now compatibility: reapplied current vanilla environment preset after direct interior load | preset=<current preset>.`
4. If both lines are present but the visual state is still wrong, treat the delayed preset-reapply hypothesis as disproven and research the additional vanilla transition/zone state instead of adding fixed lighting values.
5. If the visual state is corrected, repeat after returning to the main menu and optionally sanity-check outdoor night and dungeon entry/exit for regressions.

### Tester result — REJECTED

The visual mismatch remained after direct-loading the church basement/alchemy-lab save. The supplied runtime log proves that 1.0.11 did execute the intended compatibility path: it logged late Save Now detection and successfully called `EnvironmentEngine.ApplyEnvironmentPreset` for `mortuary`. The image still remained wrong until a normal exit/re-entry.

The same log also establishes the ordering error in the attempted fix. Keeper's Lantern reapplied `mortuary` before the game's final `StartPlayingGame 3/4` phase and before the later player teleport. Save Now 2.5.14 public source independently shows that its `GameSave.GlobalEventsCheck` postfix calls `RestoreLocation()`, which performs `MainGame.me.player.PlaceAtPos(pos)`. Therefore the 1.0.11 refresh was not actually post-Save-Now-location-restore even though it was post-plugin-startup.

The hypothesis "reapply the current preset after the early 0.50 s Player(Clone) settle gate" is disproven. The narrower remaining hypothesis is that the same vanilla preset reapply may work only after Save Now's late location restore / `GameSave.GlobalEventsCheck` completion.

### Conclusion / next action

**REJECTED.** Do not merge 1.0.11. Preserve `candidate/1.0.11` as the immutable handed source. The next numbered runtime candidate must wait until after Save Now's late `PlaceAtPos` stage instead of using elapsed time from `Player(Clone)` creation.

---

## 1.0.10 — REJECTED Save Now compatibility candidate

- **Date:** 2026-09-12
- **Development branch:** `dev/1.0.10`
- **Frozen handed source:** `candidate/1.0.10`
- **Exact handed source commit:** `ba5d1ef54354dc0b191b1b5ee93491e14d91547f`
- **Purpose / hypothesis:** after a Save Now direct-load into an interior, reapply the already-selected current vanilla `EnvironmentEngine` preset once after the gameplay player appears and the existing 0.50 s settle window completes.

### Candidate implementation

- detects Save Now by GUID `p1xel8ted.gyk.savenow`;
- arms one environment refresh per newly spawned gameplay `Player(Clone)`;
- refresh is limited to settled normal interiors with a real current preset;
- reuses the current vanilla preset through `EnvironmentEngine.ApplyEnvironmentPreset`;
- does not hardcode ambient/LUT values;
- does not call `SetEngineGlobalState` or `UpdateZone`;
- leaves outdoor/dungeon behavior and the accepted 1.0.9 normalized Keeper-light baseline unchanged.

### Tested build provenance

- **PR:** `#1`
- **Actions run:** `34686144572`
- **Result:** success, `0` warnings / `0` errors
- **Artifact:** `KeepersLantern-1.0.10`
- **Artifact ID:** `10296070959`
- **Artifact digest:** `sha256:26261a0fb592a78ff91c08b9475b3a9a401a185a2964980bcb8cc76e913d68a9`
- **Raw handed DLL SHA-256:** `1463362c53d8bcfca6f7bc4ab7a65f031a2282615a5723012e26a2ad31027b2b`

### Tester result — REJECTED

Direct-load lighting remained incorrect. The supplied 2026-09-12 runtime log then established why the intended compatibility path never ran: Keeper's Lantern is initialized before Save Now in the observed BepInEx load order. `Awake()` therefore evaluated `Chainloader.PluginInfos.ContainsKey(SaveNowGuid)` before Save Now had been registered, leaving `_saveNowPresent=false` for the session. The expected `Save Now detected...` and `Save Now compatibility: reapplied...` messages are absent even though Save Now 2.5.14 loads later in the same startup.

The test therefore does **not** establish whether the one-shot vanilla preset reapply itself fixes the visual state; it establishes that 1.0.10 failed to reach that code path.

### Conclusion / next action

**REJECTED.** Do not merge 1.0.10. Preserve `candidate/1.0.10` as the immutable handed source. The next runtime candidate must use a new version and detect Save Now only after plugin startup/load ordering can no longer hide it.

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
