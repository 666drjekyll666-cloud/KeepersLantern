# Changelog

## 1.0.9 — Accepted release

- Fixed the remaining native Keeper-light calibration bug by normalizing settled live Point/Ground intensity against `TimeOfDay.light_intensity_k` before storing the baseline.
- Baseline capture now fails closed if the global coefficient cannot be read and never calibrates directly during initial bind.
- Preserves a valid normalized baseline across later interior/rebind transitions.
- Outdoor-night brightness default changed from `0.60` to `0.70`; cool tint remains `0.16`.
- Procedural-dungeon brightness default changed from `0.50` to `0.60`; cool tint remains `0.07`; practical-light radius remains `x1.25`.
- Corrected stale Belt Visual diagnostic version text; no belt behavior change.
- Player acceptance on 2026-09-11 confirmed a daytime load followed by night no longer produces an abnormally bright lantern. Supplied runtime log also showed a sane normalized baseline during an interior -> outdoor-night transition.
- Migrated to a new clean public repository history. Historical research/POC branches and private reverse-engineering material were intentionally not imported.

## 1.0.7 — Superseded

- Prevented direct interior-preset intensity from being captured as the outdoor baseline.
- Later broader testing showed daytime `TimeOfDay` attenuation could still be baked into the baseline, causing night over-amplification. Superseded by 1.0.9.

## 1.0.6 — Historical accepted release

- Finalized the player-facing Configuration Manager surface while preserving the accepted lighting architecture.
