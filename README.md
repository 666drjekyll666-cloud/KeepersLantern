# Keeper's Lantern

A lightweight lighting overhaul for **Graveyard Keeper 1.407** built around darker nights, darker procedural dungeons, and a visible belt lantern that reuses the game's native lighting systems.

**Current stable version: 1.0.12**

## Download

Stable binaries are available from [GitHub Releases](https://github.com/666drjekyll666-cloud/KeepersLantern/releases).

## What it changes

- Outdoor nights use a darker, slightly cooler ambient profile while preserving local world lights.
- Procedural dungeons use a dedicated darker ambient profile and slightly larger stationary practical-light radii.
- The Keeper gets an enhanced native Point/Ground light outdoors at night and in procedural dungeons.
- A small physical lantern sprite is mounted on the Keeper's rear belt and follows the character animation rather than running on a free timer.
- Dynamic shadows continue to use Graveyard Keeper's live `DynamicLights.shadows` registry.
- Normal interiors keep vanilla world lighting and hard-disable the enhanced Keeper light.
- The mortuary remains a vanilla-lighting passthrough.
- When Save Now is installed, 1.0.12 fixes direct-loading into an interior by refreshing the already-selected vanilla environment preset only after Save Now finishes restoring the saved player location.

## Accepted defaults

- Outdoor night brightness: `0.70`
- Outdoor night cool tint: `0.16`
- Dungeon brightness: `0.60`
- Dungeon cool tint: `0.07`
- Dungeon stationary practical-light radius: `x1.25`
- Keeper Point light target: range `120`, intensity `1.50`, native coefficient `1.25`, offset `X 0 / Y -0.40`
- Keeper Ground light target: range `455`, intensity `1.65`, native coefficient `0.75`

The accepted normalized Keeper-light baseline remains calibrated against `TimeOfDay.light_intensity_k`, so starting during daytime and later reaching night does not produce an abnormally bright lantern.

## Installation

1. Install BepInEx 5 for Graveyard Keeper.
2. Copy `KeepersLantern.dll` into `BepInEx/plugins`.
3. Start the game.

Configuration Manager is optional. When installed, F1 exposes the player-facing outdoor-night, dungeon, lantern, and advanced belt-position/diagnostic controls.

## Compatibility

`Darker Nights` is not required and is not the intended visual combination. If it is detected, Keeper's Lantern suppresses its own outdoor ambient-darkening pass to avoid double-darkening.

Save Now is optional. The dedicated compatibility path remains idle when Save Now is not installed.
