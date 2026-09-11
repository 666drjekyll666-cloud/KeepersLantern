# Keeper's Lantern 1.0.9 — Accepted Baseline

Accepted on **2026-09-11** for Graveyard Keeper **1.407**.

## Identity

- Mod: **Keeper's Lantern**
- Assembly: `KeepersLantern.dll`
- Primary BepInEx GUID: `nikich.gyk.keeperslantern`
- Version: `1.0.9`

## Lighting contract

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
- `mortuary` is a vanilla-lighting passthrough rather than a custom profile.

## Keeper native-light targets

- Point: range `120`, intensity `1.50`, native DynamicLights coefficient `1.25`, local offset `X 0 / Y -0.40`.
- Ground: range `455`, intensity `1.65`, native DynamicLights coefficient `0.75`.
- Outdoor external-light overlap compensation remains active only where accepted; dungeon balance is not attenuated by it.

## Native baseline invariant

1. Binding the Keeper rig does not immediately capture `Light.intensity`.
2. Baseline acquisition waits for settled native output.
3. Capture is allowed only outside normal interiors.
4. Live Point/Ground intensity is divided by the current verified `TimeOfDay.light_intensity_k` before being stored.
5. If that global coefficient cannot be read, capture fails closed.
6. Once valid, the normalized baseline survives later interior/rebind transitions.

This prevents daytime or interior attenuation from being mistaken for a full-strength native baseline and then amplified at night.

## Belt visual / shadows

- Rear-belt lantern sprite remains animation-frame synchronized.
- Accepted user offsets remain `+1.5 px X / -2.8 px Y` by default.
- Belt glow follows the shared lantern factor.
- Dynamic shadows use the live `DynamicLights.shadows` registry.
- No custom GL/full-screen edge-darkening pass is part of production.

## Performance architecture

- Native DynamicLights lists/coefficients are reused.
- No normal per-frame direct `Light.intensity` fighting.
- Expensive hierarchy/object discovery is bind-time or slow compatibility fallback only.
- Shadow work is scoped to nearby live entries.

## Acceptance evidence

The 1.0.9 player build was compiled from legacy frozen source commit `1ed29f48c33768d11e7dcf75cf5ea01a234c9369` and handed to the tester.

On 2026-09-11 the tester explicitly confirmed the formerly failing path: load during daytime, wait until night, and the lantern **does not become abnormally bright**. The supplied session log also records a safe transition to `OutdoorNight`, a one-frame deferred baseline, and a normalized capture of approximately `Point 0.78 / Ground 1.15` before normal lantern activation.

That closes the 1.0.7 regression and makes 1.0.9 the accepted release baseline.
