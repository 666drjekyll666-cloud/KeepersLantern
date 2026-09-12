# Keeper's Lantern 1.0.12 — Accepted Baseline

Accepted on **2026-09-12** for Graveyard Keeper **1.407**.

## Identity

- Mod: **Keeper's Lantern**
- Assembly: `KeepersLantern.dll`
- Primary BepInEx GUID: `nikich.gyk.keeperslantern`
- Version: `1.0.12`
- Tested source commit: `8df0a848aa8e8bfb5748f6937731db3b01d4430a`
- Accepted merge commit on `main`: `c33daee1267c841dd5a5765de8b10d692d332fa9`
- Frozen accepted ref: `baseline/1.0.12-accepted`

## What 1.0.12 changes

1.0.12 fixes the Save Now direct-interior load edge case without changing the accepted lighting balance.

Save Now 2.5.14 restores the saved player position late through its internal `SaveNow.Plugin.RestoreLocation()` path. Earlier 1.0.10/1.0.11 attempts reapplied the current vanilla environment preset too early and were rejected.

The accepted 1.0.12 compatibility path:

- remains idle when Save Now is absent;
- hooks the verified `SaveNow.Plugin.RestoreLocation()` seam at runtime;
- waits until that method has returned, then schedules work for the next Unity frame;
- only for a settled normal interior with a real current preset, reapplies that same current vanilla preset through `EnvironmentEngine.ApplyEnvironmentPreset`;
- ignores outdoor and procedural-dungeon results;
- fails closed when required runtime members cannot be resolved;
- does not hardcode ambient values, LUT values, zone IDs, or screen-space effects;
- does not add steady-state scanning or polling.

## Preserved lighting contract

### Outdoors

- Day: vanilla world lighting; no enhanced lantern.
- Night transition/full night: Keeper's Lantern ambient profile plus smoothly scaled native Keeper light.
- Outdoor-night ambient scale: `0.70`.
- Outdoor-night cool tint: `0.16`.

### Procedural dungeon

- Dungeon ambient scale: `0.60`.
- Dungeon cool tint: `0.07`.
- Stationary dungeon practical-light radius multiplier: `1.25`.
- Keeper lantern: full strength.

### Normal interiors

- Vanilla world lighting.
- Enhanced Keeper light is hard-disabled.
- `mortuary` remains a vanilla-lighting passthrough.
- When Save Now direct-loads an interior, Keeper's Lantern now refreshes the already-selected vanilla preset only after Save Now's late location restoration completes.

## Keeper native-light targets

- Point: range `120`, intensity `1.50`, native DynamicLights coefficient `1.25`, local offset `X 0 / Y -0.40`.
- Ground: range `455`, intensity `1.65`, native DynamicLights coefficient `0.75`.
- Outdoor external-light overlap compensation remains active only where accepted; dungeon balance is not attenuated by it.

## Native baseline invariant

The accepted 1.0.9 normalized-baseline architecture remains unchanged:

1. Binding the Keeper rig does not immediately capture `Light.intensity`.
2. Baseline acquisition waits for settled native output.
3. Capture is allowed only outside normal interiors.
4. Live Point/Ground intensity is divided by the current verified `TimeOfDay.light_intensity_k` before storage.
5. If that global coefficient cannot be read, capture fails closed.
6. Once valid, the normalized baseline survives later interior/rebind transitions.

## Belt visual / shadows / performance

- Rear-belt lantern sprite remains animation-frame synchronized.
- Accepted user offsets remain `+1.5 px X / -2.8 px Y` by default.
- Belt glow follows the shared lantern factor.
- Dynamic shadows use the live `DynamicLights.shadows` registry.
- No custom GL/full-screen edge-darkening pass is part of production.
- Native DynamicLights lists/coefficients are reused.
- No normal per-frame direct `Light.intensity` fighting.
- The Save Now compatibility path is event-driven and one-shot per restore rather than a recurring scan.

## Build provenance

- Workflow: `Build Keeper's Lantern`
- Actions run: `34689021870`
- Job: `103540918239`
- Result: success, `0` warnings / `0` errors
- Artifact: `KeepersLantern-1.0.12`
- Artifact ID: `10296636948`
- Artifact ZIP digest: `sha256:b4da5478f2f3e07e932cba2c3871f684635ffde45e77b9537beeb1996986ff7d`
- Raw DLL SHA-256: `5b4fe6b302025c719827bfa2d4f863e63037c48369736470906bc9a6a1e5048a`

## Acceptance evidence

The tester directly loaded the same Save Now save inside the church basement / alchemy laboratory that had reproduced the incorrect initial image in 1.0.10 and 1.0.11.

The 1.0.12 runtime log confirms the intended sequence:

1. `Save Now compatibility hook installed on SaveNow.Plugin.RestoreLocation.`
2. `Save Now compatibility: Save Now location restore completed; environment refresh scheduled for the next frame.`
3. `Save Now compatibility: reapplied current vanilla environment preset after Save Now location restore | preset=mortuary.`

The tester then explicitly confirmed that the direct-loaded image is now correct. A normal church -> mortuary transition in the same session also continued to use the vanilla `mortuary` preset.

This makes 1.0.12 the accepted `main` baseline. The rejected 1.0.10 and 1.0.11 candidates remain historical evidence only.