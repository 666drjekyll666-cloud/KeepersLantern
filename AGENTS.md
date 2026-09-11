# Keeper's Lantern — Working Rules

These rules are mandatory for development in this public repository.

## Global engineering contract

Before substantive changes, consult the current global rules in `666drjekyll666-cloud/DevRules`: `ENGINEERING_RULES.md`, `CI_POLICY.md`, `GIT_WORKFLOW.md`, and `PROJECT_BOOTSTRAP.md`. Repository evidence outranks chat memory.

Use the standard flow: **discover -> verify -> implement narrowly -> test -> accept**. Keep research separate from production, preserve exact source/build identity, and do not spend hosted CI on documentation-only or research-only changes.

## Project identity

- Public mod name: **Keeper's Lantern**
- Repository / project / assembly: `KeepersLantern`
- Game: `Graveyard Keeper 1.407`
- Stable primary BepInEx GUID: `nikich.gyk.keeperslantern`
- Current accepted stable runtime baseline: **1.0.9**
- `main` is the stable accepted public line.

The production assembly currently compiles exactly:

- `src/UnifiedLightingV021.cs`
- `src/BeltLanternBackPocV034.cs`
- `src/DungeonPracticalLightBoost.cs`
- `src/LanternShadowBoostOptimized.cs`

Do not silently add old POC/runtime files to `KeepersLantern.csproj`.

## Accepted 1.0.9 contract

Treat `docs/BASELINE_1.0.9.md`, `docs/TEST_BUILD_LOG.md`, the production source compiled by `KeepersLantern.csproj`, and the frozen accepted baseline ref as authoritative.

Accepted behavior includes:

- outdoor-night ambient scale `0.70`, cool tint `0.16`;
- procedural-dungeon ambient scale `0.60`, cool tint `0.07`;
- dungeon stationary practical-light radius boost `x1.25`;
- Keeper Point target `120 / 1.50 / K1.25 / offset 0,-0.40`;
- Keeper Ground target `455 / 1.65 / K0.75`;
- normal interiors use vanilla world lighting and hard-disable the enhanced Keeper light;
- `mortuary` uses vanilla-lighting passthrough;
- belt visual remains rear-belt mounted and animation-frame synchronized;
- shadows use the live `DynamicLights.shadows` registry;
- no custom GL/full-screen edge-darkening renderer;
- normal Keeper-light control uses native `DynamicLights` cached coefficients instead of per-frame direct intensity fighting;
- Keeper native intensity baseline is captured only from settled non-interior native output and normalized by `TimeOfDay.light_intensity_k`;
- a valid normalized baseline survives later interior/rebind transitions;
- Darker Nights, when detected, suppresses this mod's outdoor ambient-darkening pass to avoid stacking.

Do not change accepted lighting balance, lantern timing, overlap compensation, belt behavior, dungeon practical-light strength, shadow behavior, or interior policy while implementing an unrelated fix.

## Public / private boundary

This repository is public production code. It may contain our source, documentation, workflows, and other redistributable project material.

Do **not** commit Graveyard Keeper assemblies, extracted copyrighted game assets, decompiled game source, bulk runtime dumps, research archives, temporary probes, or private reverse-engineering material here.

Deep game research belongs in the private `666drjekyll666-cloud/GraveyardKeeperResearch` evidence layer or another explicitly private research location. Production must depend on distilled verified facts, public packages, and runtime APIs—not on downloading private research data during CI.

## Runtime architecture and performance

The mod should remain effectively free relative to the game during normal play.

Preserve the accepted architecture:

- reuse native `DynamicLights` data and cached objects;
- avoid unconditional global scans in steady state;
- avoid repeated hierarchy searches, reflection, LINQ, allocations, and logging in hot paths;
- do not write Keeper `Light.intensity` every frame during normal operation;
- low-frequency maintenance is acceptable only when verified vanilla behavior can overwrite required state;
- keep shadow work narrow and local;
- restore modified Unity/native state on release, teardown, or destruction.

The rejected custom GL edge-darkening path must not return without new evidence and explicit approval.

## Save/load and transition invariants

Lighting code must correctly handle:

- fresh/reloaded gameplay during daytime followed by night;
- first load inside a normal interior;
- interior -> outdoors transitions, including at night;
- outdoors <-> interior transitions;
- dungeon entry/exit;
- player/light-rig recreation or rebind;
- main-menu return and subsequent reload.

Never capture an interior-attenuated or raw/unsettled light value as the full Keeper baseline. The 1.0.9 normalized baseline design is a release invariant unless a separately tested architecture replaces it.

## Workflow

- `main` = accepted stable public state.
- Runtime changes start on `dev/X.Y.Z` or an explicitly named research branch.
- Do not promote runtime changes without explicit player acceptance such as `фиксируем`, `релизим`, or `сливай`.
- Every handed numbered DLL is immutable and tied to exact committed source.
- Important accepted checkpoints receive a frozen `baseline/...` branch/ref.
- `docs/TEST_BUILD_LOG.md` is the durable handoff/acceptance record.

Documentation-only changes do not require a runtime rebuild and should not trigger hosted CI.

## Build / handoff

Before handing a new runtime DLL to the user:

- version metadata is consistent;
- Release build succeeds from canonical committed source;
- no unintended diagnostics or temporary probes ship;
- exact source SHA and artifact provenance are recorded;
- the user receives a raw versioned DLL when a file handoff is needed;
- the required in-game test is concise and specific.

Standard public CI is permitted when it proves a concrete build/release property. Use the established Windows build until a cheaper equivalent runner has been proven. Keep artifact retention short.

## Long-lived sources of truth

- `AGENTS.md`
- `docs/BASELINE_1.0.9.md` or a newer accepted baseline
- `docs/TEST_BUILD_LOG.md`
- `docs/MIGRATION_PROVENANCE.md`
- `README.md`
- `KeepersLantern.csproj`
- frozen accepted baseline refs and CI evidence

When chat history conflicts with accepted repository evidence, investigate before changing code.
