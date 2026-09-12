using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace KeepersLantern
{
    // Optional Configuration Manager metadata. Configuration Manager discovers this
    // class by name through ConfigDescription tags, so KeepersLantern does not need a
    // runtime/compile-time dependency on ConfigurationManager.dll.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
        public bool? IsAdvanced;
        public string DispName;
        public int? Order;
    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class UnifiedLightingPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "nikich.gyk.keeperslantern";
        private const string PluginName = "Keeper's Lantern";
        private const string PluginVersion = "1.0.11";
        private const string DarkerNightsGuid = "com.thalethegreat.darkernights";
        private const string SaveNowGuid = "p1xel8ted.gyk.savenow";

        private EnvironmentProbe21 _probe;
        private KeeperLight21 _keeperLight;
        private WorldLightingPass21 _worldPass;
        private Camera _worldCamera;

        private ConfigEntry<bool> _nightEnabled;
        private ConfigEntry<float> _dusk;
        private ConfigEntry<float> _dawn;
        private ConfigEntry<float> _nightAmbientScale;
        private ConfigEntry<float> _nightTint;

        private ConfigEntry<bool> _dungeonEnabled;
        private ConfigEntry<float> _dungeonAmbientScale;
        private ConfigEntry<float> _dungeonTint;

        private ConfigEntry<bool> _lanternEnabled;
        private ConfigEntry<float> _pointRange;
        private ConfigEntry<float> _pointIntensity;
        private ConfigEntry<float> _pointK;
        private ConfigEntry<float> _pointOffsetX;
        private ConfigEntry<float> _pointOffsetY;
        private ConfigEntry<float> _groundRange;
        private ConfigEntry<float> _groundIntensity;
        private ConfigEntry<float> _groundK;

        private ConfigEntry<bool> _verbose;

        private bool _darkerNightsPresent;
        private bool _saveNowPresent;
        private bool _saveNowEnvironmentRefreshPending;
        private float _lanternFactor;
        private bool _keeperLightHardOff;
        private bool _keeperExternalCompensation;
        private bool _keeperBaselineCaptureAllowed;
        internal static float SharedLanternFactor { get; private set; }
        internal static LightingSnapshot21 SharedLightingSnapshot { get; private set; }
        internal static bool SharedLightingSnapshotValid { get; private set; }
        private float _nextCameraBind;
        private Transform _gameplayPlayer;
        private float _nextGameplayPlayerResolve;
        private float _gameplayReadyAfter;
        private bool _gameplayReady;
        private LightingRegion21 _lastRegion = LightingRegion21.Unresolved;
        private string _lastPreset;
        private long _perfFrames;
        private double _perfFrameTotalMs;
        private float _perfFrameMaxMs;
        private long _perfOver10Ms;
        private long _perfOver12_5Ms;
        private long _perfOver16_7Ms;
        private long _perfOver25Ms;
        private long _perfUpdateCount;
        private double _perfUpdateTotalMs;
        private float _perfUpdateMaxMs;
        private int _perfGc0Base;
        private int _perfGc1Base;
        private int _perfGc2Base;

        private void Awake()
        {
            BindConfig();
            _probe = new EnvironmentProbe21();
            _keeperLight = new KeeperLight21(Logger);

            try { _darkerNightsPresent = Chainloader.PluginInfos.ContainsKey(DarkerNightsGuid); }
            catch { _darkerNightsPresent = false; }
            try { _saveNowPresent = Chainloader.PluginInfos.ContainsKey(SaveNowGuid); }
            catch { _saveNowPresent = false; }

            Logger.LogInfo("Keeper's Lantern 1.0.11 loaded. Release lighting profile active; pre-game ambient mutation blocked; mortuary uses vanilla lighting passthrough.");
            Logger.LogInfo("Outdoor/dungeon darkness uses one final ambientLight adjustment after vanilla TimeOfDay; normal interiors are untouched.");
            Logger.LogInfo("Keeper light uses vanilla DynamicLights cached coefficients; dungeon darkness remains immediate.");

            if (_darkerNightsPresent)
                Logger.LogWarning("Darker Nights detected. This combination is not supported for the intended experience; KeepersLantern automatically suppresses its own outdoor-night ambient pass to avoid double-darkening.");
            if (_saveNowPresent)
                Logger.LogInfo("Save Now detected. Direct-load interior compatibility refresh is armed for each new gameplay load.");
        }

        private void BindConfig()
        {
            // Release-facing F1 surface: keep only player-meaningful controls visible.
            // Technical tuning remains in the config for migration/debug compatibility,
            // but Configuration Manager does not display it.
            _nightEnabled = Config.Bind(
                "Outdoor Night", "Enabled", true,
                new ConfigDescription("Enable KeepersLantern's outdoor night-darkening profile.", null,
                    new ConfigurationManagerAttributes { Order = 30 }));
            _dusk = Config.Bind(
                "Outdoor Night", "Dusk TimeK", 0.72f,
                new ConfigDescription("Internal dusk boundary used by the accepted night curve.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _dawn = Config.Bind(
                "Outdoor Night", "Dawn TimeK", 0.14f,
                new ConfigDescription("Internal dawn boundary used by the accepted night curve.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _nightAmbientScale = Config.Bind(
                "Outdoor Night", "Midnight Ambient Scale", 0.70f,
                new ConfigDescription("Brightness of the darkest part of the outdoor night. Lower values are darker; 1.0 is vanilla ambient brightness.", null,
                    new ConfigurationManagerAttributes { DispName = "Night Brightness", Order = 20 }));
            _nightTint = Config.Bind(
                "Outdoor Night", "Midnight Cool Tint", 0.16f,
                new ConfigDescription("How cool/blue the outdoor night ambient light becomes. 0 keeps the source colour neutral.", null,
                    new ConfigurationManagerAttributes { DispName = "Night Coolness", Order = 10 }));

            _dungeonEnabled = Config.Bind(
                "Dungeon", "Enabled", true,
                new ConfigDescription("Enable KeepersLantern's darker procedural-dungeon profile.", null,
                    new ConfigurationManagerAttributes { Order = 30 }));
            _dungeonAmbientScale = Config.Bind(
                "Dungeon", "Ambient Scale", 0.60f,
                new ConfigDescription("Brightness of unlit procedural-dungeon areas. Lower values are darker; 1.0 is vanilla ambient brightness.", null,
                    new ConfigurationManagerAttributes { DispName = "Dungeon Brightness", Order = 20 }));
            _dungeonTint = Config.Bind(
                "Dungeon", "Cool Tint", 0.07f,
                new ConfigDescription("How cool/blue the procedural-dungeon ambient light becomes.", null,
                    new ConfigurationManagerAttributes { DispName = "Dungeon Coolness", Order = 10 }));

            _lanternEnabled = Config.Bind(
                "Keeper Lantern", "Enabled", true,
                new ConfigDescription("Enable the Keeper's enhanced belt-lantern light outdoors at night and in the procedural dungeon.", null,
                    new ConfigurationManagerAttributes { Order = 30 }));
            _pointRange = Config.Bind(
                "Keeper Lantern", "Point Range", 120f,
                new ConfigDescription("Internal accepted Point-light range.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _pointIntensity = Config.Bind(
                "Keeper Lantern", "Point Intensity", 1.50f,
                new ConfigDescription("Internal accepted Point-light intensity.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _pointK = Config.Bind(
                "Keeper Lantern", "Point Dynamic Intensity", 1.25f,
                new ConfigDescription("Internal DynamicLight coefficient for the Point source.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _pointOffsetX = Config.Bind(
                "Keeper Lantern", "Point Offset X", 0f,
                new ConfigDescription("Internal local horizontal offset of the native Keeper Point light.",
                    new AcceptableValueRange<float>(-200f, 200f),
                    new ConfigurationManagerAttributes { Browsable = false }));
            _pointOffsetY = Config.Bind(
                "Keeper Lantern", "Point Offset Y", -0.40f,
                new ConfigDescription("Internal local vertical offset of the native Keeper Point light.",
                    new AcceptableValueRange<float>(-200f, 200f),
                    new ConfigurationManagerAttributes { Browsable = false }));
            _groundRange = Config.Bind(
                "Keeper Lantern", "Ground Range", 455f,
                new ConfigDescription("Internal accepted ground-light range.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _groundIntensity = Config.Bind(
                "Keeper Lantern", "Ground Intensity", 1.65f,
                new ConfigDescription("Internal accepted ground-light intensity.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            _groundK = Config.Bind(
                "Keeper Lantern", "Ground Dynamic Intensity", 0.75f,
                new ConfigDescription("Internal DynamicLight coefficient for the ground source.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));

            _verbose = Config.Bind(
                "Diagnostics", "Verbose State Logging", false,
                new ConfigDescription("Write repeated lighting-state information to the BepInEx log. Useful only for troubleshooting.", null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // 0.4.16: migrate only the exact previous shipped baseline. This makes the
            // visual change effective for existing POC installs while preserving any
            // genuinely customized value other than 0.68.
            if (Mathf.Abs(_nightAmbientScale.Value - 0.68f) < 0.0001f)
            {
                _nightAmbientScale.Value = 0.60f;
                Config.Save();
                Logger.LogInfo("0.4.16 migrated Outdoor Night / Midnight Ambient Scale: 0.68 -> 0.60.");
            }
            // 1.0.9: 0.60 was the previous shipped default. Move only that exact
            // value to the new user-approved default so deliberate custom values survive.
            if (Mathf.Abs(_nightAmbientScale.Value - 0.60f) < 0.0001f)
            {
                _nightAmbientScale.Value = 0.70f;
                Config.Save();
                Logger.LogInfo("1.0.9 migrated Outdoor Night / Midnight Ambient Scale: 0.60 -> 0.70.");
            }
            // 0.5.1: 0.42 was the exact previous shipped/test default. Raise only that
            // value so existing deliberate custom dungeon tuning is preserved.
            if (Mathf.Abs(_dungeonAmbientScale.Value - 0.42f) < 0.0001f)
            {
                _dungeonAmbientScale.Value = 0.50f;
                Config.Save();
                Logger.LogInfo("0.5.1 migrated Dungeon / Ambient Scale: 0.42 -> 0.50.");
            }
            // 1.0.9: 0.50 was the previous shipped default. Move only that exact
            // value to the new user-approved default so deliberate custom values survive.
            if (Mathf.Abs(_dungeonAmbientScale.Value - 0.50f) < 0.0001f)
            {
                _dungeonAmbientScale.Value = 0.60f;
                Config.Save();
                Logger.LogInfo("1.0.9 migrated Dungeon / Ambient Scale: 0.50 -> 0.60.");
            }
            bool keeperConfigMigrated = false;
            if (Mathf.Abs(_pointRange.Value - 440f) < 0.0001f) { _pointRange.Value = 312f; keeperConfigMigrated = true; }
            if (Mathf.Abs(_pointIntensity.Value - 1.8f) < 0.0001f) { _pointIntensity.Value = 0.78f; keeperConfigMigrated = true; }
            if (Mathf.Abs(_pointK.Value - 0.85f) < 0.0001f) { _pointK.Value = 1.25f; keeperConfigMigrated = true; }
            if (Mathf.Abs(_groundRange.Value - 390f) < 0.0001f) { _groundRange.Value = 455f; keeperConfigMigrated = true; }
            if (Mathf.Abs(_groundIntensity.Value - 1.8f) < 0.0001f) { _groundIntensity.Value = 1.45f; keeperConfigMigrated = true; }
            if (Mathf.Abs(_groundK.Value - 0.95f) < 0.0001f) { _groundK.Value = 0.75f; keeperConfigMigrated = true; }
            if (keeperConfigMigrated)
            {
                Config.Save();
                Logger.LogInfo("0.4.17 migrated legacy Keeper-light config defaults to the accepted 0.4.16 tuning.");
            }

            bool previous055Defaults =
                Mathf.Abs(_pointRange.Value - 312f) < 0.0001f &&
                Mathf.Abs(_pointIntensity.Value - 0.78f) < 0.0001f &&
                Mathf.Abs(_pointK.Value - 1.25f) < 0.0001f &&
                Mathf.Abs(_pointOffsetX.Value) < 0.0001f &&
                (Mathf.Abs(_pointOffsetY.Value) < 0.0001f || Mathf.Abs(_pointOffsetY.Value + 0.40f) < 0.0001f) &&
                Mathf.Abs(_groundRange.Value - 455f) < 0.0001f &&
                Mathf.Abs(_groundIntensity.Value - 1.45f) < 0.0001f &&
                Mathf.Abs(_groundK.Value - 0.75f) < 0.0001f;
            if (previous055Defaults)
            {
                _pointRange.Value = 120f;
                _pointIntensity.Value = 1.50f;
                _pointOffsetX.Value = 0f;
                _pointOffsetY.Value = -0.40f;
                _groundIntensity.Value = 1.65f;
                Config.Save();
                Logger.LogInfo("1.0.0 migrated untouched 0.5.5 Keeper-light defaults to the accepted release tuning.");
            }
        }

        private void Update()
        {
            TrackFramePerf();
            if (Input.GetKeyDown(KeyCode.F9))
            {
                ResetPerfCounters();
                Logger.LogInfo("F9 KEEPER LANTERN PERF COUNTERS RESET.");
            }
            if (Input.GetKeyDown(KeyCode.F10)) DumpPerfCounters();

            long perfStart = System.Diagnostics.Stopwatch.GetTimestamp();
            LightingSnapshot21 state = _probe.Read();
            SharedLightingSnapshot = state;
            SharedLightingSnapshotValid = true;
            float night = state.TimeKnown ? NightAmount(state.TimeK) : 0f;
            LightingRegion21 region = ResolveRegion(state, night);
            bool gameplayReady = IsGameplayWorldReady();
            _gameplayReady = gameplayReady;
            // The GUI/world bootstrap exposes TimeK=0 and no preset before a save is actually
            // restored. Treat that bootstrap state as unresolved so the outdoor night pass
            // cannot capture or write a fake midnight ambient into the world being loaded.
            if (!gameplayReady) region = LightingRegion21.Unresolved;
            bool suppressNight = _darkerNightsPresent;

            float ambientScale = 1f;
            float tint = 0f;
            float lantern = 0f;

            if (region == LightingRegion21.Dungeon)
            {
                if (_dungeonEnabled.Value)
                {
                    ambientScale = Mathf.Clamp(_dungeonAmbientScale.Value, 0.05f, 1f);
                    tint = Mathf.Clamp01(_dungeonTint.Value);
                }
                if (_lanternEnabled.Value) lantern = 1f;
            }
            else if (region == LightingRegion21.OutdoorNight)
            {
                if (_nightEnabled.Value && !suppressNight)
                {
                    ambientScale = Mathf.Lerp(1f, Mathf.Clamp(_nightAmbientScale.Value, 0.05f, 1f), night);
                    tint = Mathf.Clamp01(_nightTint.Value) * night;
                }
                if (_lanternEnabled.Value) lantern = 1f;
            }

            // Keeper lantern follows the game's canonical outdoor night schedule,
            // independently from the atmospheric darkness curve used above.
            if (gameplayReady && _lanternEnabled.Value && region != LightingRegion21.Dungeon &&
                region != LightingRegion21.Interior && state.TimeKnown &&
                state.IndoorKnown && !state.IsIndoor)
            {
                lantern = VanillaOutdoorLanternFactor(state.TimeK);
            }

            EnsureWorldCamera();
            TryRefreshSaveNowEnvironment(state, region, gameplayReady);
            if (_worldPass != null)
            {
                _worldPass.Configure(ambientScale, tint, region == LightingRegion21.Dungeon);
            }

            _keeperLight.Configure(
                _pointRange.Value,
                _pointIntensity.Value,
                _pointK.Value,
                _pointOffsetX.Value,
                _pointOffsetY.Value,
                _groundRange.Value,
                _groundIntensity.Value,
                _groundK.Value);
            float targetLantern = Mathf.Clamp01(lantern);
            if (region == LightingRegion21.Interior || !_lanternEnabled.Value)
            {
                // Interiors are a hard gameplay boundary: no lantern light indoors.
                _lanternFactor = 0f;
            }
            else if (region == LightingRegion21.Dungeon)
            {
                // Dungeon policy stays immediate and fully active.
                _lanternFactor = 1f;
            }
            else
            {
                // Outdoors the target already contains the short smooth fade and reaches
                // its exact endpoints at the same TimeK boundaries as vanilla night logic.
                _lanternFactor = targetLantern;
            }
            // 1.0.4: the vanilla mortuary already has a deliberate local-light composition.
            // Do not dim/tint it, do not replace the Keeper Point/Ground rig, and do not
            // hard-off the native Keeper light there. Other normal interiors keep the
            // previously accepted hard-off policy.
            bool mortuaryVanillaPassthrough =
                region == LightingRegion21.Interior &&
                state.IndoorKnown && state.IsIndoor &&
                string.Equals(state.PresetName, "mortuary", StringComparison.OrdinalIgnoreCase);
            _keeperLightHardOff =
                !mortuaryVanillaPassthrough &&
                (region == LightingRegion21.Interior || !_lanternEnabled.Value);
            _keeperExternalCompensation = region == LightingRegion21.OutdoorNight && _lanternEnabled.Value;
            // 1.0.9: never calibrate native Keeper intensity from an interior preset.
            // Dungeon is treated as non-interior by EnvironmentProbe21, so direct dungeon
            // loads keep the existing behavior while house/mortuary binds are deferred.
            _keeperBaselineCaptureAllowed =
                gameplayReady &&
                region != LightingRegion21.Interior &&
                region != LightingRegion21.Unresolved;
            SharedLanternFactor = _lanternFactor;

            bool changed = region != _lastRegion || !string.Equals(state.PresetName, _lastPreset, StringComparison.Ordinal);
            if (changed || _verbose.Value)
            {
                Logger.LogInfo(
                    "Lighting -> " + region +
                    " | TimeK=" + (state.TimeKnown ? state.TimeK.ToString("0.000") : "?") +
                    " night=" + night.ToString("0.00") +
                    " | dungeon=" + (state.DungeonKnown ? state.IsDungeon.ToString() : "?") +
                    " | indoor=" + (state.IndoorKnown ? state.IsIndoor.ToString() : "?") +
                    " | preset=" + (string.IsNullOrEmpty(state.PresetName) ? "<none>" : state.PresetName) +
                    " | ambient=" + ambientScale.ToString("0.00") +
                    (suppressNight ? " | own outdoor night suppressed" : string.Empty));
            }

            _lastRegion = region;
            _lastPreset = state.PresetName;
            RecordUpdatePerf(perfStart);
        }

        private void TrackFramePerf()
        {
            float ms = Time.unscaledDeltaTime * 1000f;
            if (ms <= 0f || ms > 2000f) return;
            _perfFrames++;
            _perfFrameTotalMs += ms;
            if (ms > _perfFrameMaxMs) _perfFrameMaxMs = ms;
            if (ms > 10f) _perfOver10Ms++;
            if (ms > 12.5f) _perfOver12_5Ms++;
            if (ms > 16.7f) _perfOver16_7Ms++;
            if (ms > 25f) _perfOver25Ms++;
        }

        private void RecordUpdatePerf(long start)
        {
            float ms = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
            _perfUpdateCount++;
            _perfUpdateTotalMs += ms;
            if (ms > _perfUpdateMaxMs) _perfUpdateMaxMs = ms;
        }

        private void ResetPerfCounters()
        {
            _perfFrames = 0;
            _perfFrameTotalMs = 0.0;
            _perfFrameMaxMs = 0f;
            _perfOver10Ms = 0;
            _perfOver12_5Ms = 0;
            _perfOver16_7Ms = 0;
            _perfOver25Ms = 0;
            _perfUpdateCount = 0;
            _perfUpdateTotalMs = 0.0;
            _perfUpdateMaxMs = 0f;
            _perfGc0Base = GC.CollectionCount(0);
            _perfGc1Base = GC.CollectionCount(1);
            _perfGc2Base = GC.CollectionCount(2);
        }

        private void DumpPerfCounters()
        {
            double avgFrame = _perfFrames > 0 ? _perfFrameTotalMs / _perfFrames : 0.0;
            double avgUpdate = _perfUpdateCount > 0 ? _perfUpdateTotalMs / _perfUpdateCount : 0.0;
            Logger.LogInfo("F10 KEEPER LANTERN PERF | frames=" + _perfFrames +
                " avgFrameMs=" + avgFrame.ToString("0.000") +
                " maxFrameMs=" + _perfFrameMaxMs.ToString("0.000") +
                " >10=" + _perfOver10Ms +
                " >12.5=" + _perfOver12_5Ms +
                " >16.7=" + _perfOver16_7Ms +
                " >25=" + _perfOver25Ms +
                " updateAvgMs=" + avgUpdate.ToString("0.000") +
                " updateMaxMs=" + _perfUpdateMaxMs.ToString("0.000") +
                " gameplayReady=" + _gameplayReady +
                " saveNowCompat=" + _saveNowPresent +
                " saveNowRefreshPending=" + _saveNowEnvironmentRefreshPending +
                " gc0=" + (GC.CollectionCount(0) - _perfGc0Base) +
                " gc1=" + (GC.CollectionCount(1) - _perfGc1Base) +
                " gc2=" + (GC.CollectionCount(2) - _perfGc2Base) +
                " keeperLight={" + (_keeperLight != null ? _keeperLight.PerfSummary : "<null>") + "}");
        }

        private void LateUpdate()
        {
            // Vanilla TimeOfDay.Update has already produced the frame's base ambient.
            // Apply one final ambient colour instead of repeatedly changing/restoring
            // five global RenderSettings values around camera rendering.
            if (_worldPass != null) _worldPass.ApplyAmbientLate();
            // 1.0.4 no longer applies a special mortuary/workshop fill. Release any stale
            // state from an older in-session profile, then let the normal native-light
            // path restore/preserve vanilla values when factor is zero.
            _keeperLight.ReleaseWorkshopGroundFill();
            _keeperLight.Apply(_lanternFactor, _keeperLightHardOff, _keeperExternalCompensation, _keeperBaselineCaptureAllowed);
        }

        private float VanillaOutdoorLanternFactor(float timeK)
        {
            // 0.5.1 evening policy: do not pre-light the Keeper while sunset is still
            // visibly bright. Start shortly after vanilla's 0.75 night threshold, then
            // take about two in-game hours (2/24 = 0.08333 TimeK) to reach full output.
            const float eveningStart = 0.76f;
            const float eveningFull = eveningStart + (2f / 24f); // ~= 0.84333
            const float streetLampDawn = 0.25f;

            float dawnStart = Mathf.Min(_dawn.Value, streetLampDawn - 0.0001f);
            float t = timeK - Mathf.Floor(timeK);

            // Full night wraps through midnight. Preserve the accepted broad morning fade.
            if (t >= eveningFull || t < dawnStart) return 1f;
            if (t < streetLampDawn)
                return 1f - Mathf.InverseLerp(dawnStart, streetLampDawn, t);

            if (t < eveningStart) return 0f;
            return Smooth01(Mathf.InverseLerp(eveningStart, eveningFull, t));
        }

        private float NightAmount(float timeK)
        {
            float t = timeK - Mathf.Floor(timeK);
            float dusk = Mathf.Clamp(_dusk.Value, 0.51f, 0.98f);
            float dawn = Mathf.Clamp(_dawn.Value, 0.02f, 0.49f);

            if (t >= dusk)
            {
                float x = Mathf.InverseLerp(dusk, 1f, t);
                return Smooth01(x);
            }
            if (t <= dawn)
            {
                float x = Mathf.InverseLerp(dawn, 0f, t);
                return Smooth01(x);
            }
            return 0f;
        }

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static LightingRegion21 ResolveRegion(LightingSnapshot21 state, float night)
        {
            if (state.DungeonKnown && state.IsDungeon) return LightingRegion21.Dungeon;
            if (state.IndoorKnown && state.IsIndoor) return LightingRegion21.Interior;
            if (!state.IndoorKnown || !state.TimeKnown) return LightingRegion21.Unresolved;
            return night > 0.0005f ? LightingRegion21.OutdoorNight : LightingRegion21.OutdoorDay;
        }

        private bool IsGameplayWorldReady()
        {
            // During scene/bootstrap loading Graveyard Keeper already exposes a TimeOfDay
            // object and an empty environment preset, which looks like outdoor midnight.
            // Only unlock world-lighting mutations after the real local Player(Clone) has
            // spawned, then wait a short settle window so save time and environment preset
            // restoration finish before we can capture an ambient source.
            if (_gameplayPlayer == null)
            {
                if (Time.unscaledTime < _nextGameplayPlayerResolve) return false;
                _nextGameplayPlayerResolve = Time.unscaledTime + 0.25f;
                GameObject player = GameObject.Find("World/Player(Clone)");
                if (player == null) return false;
                _gameplayPlayer = player.transform;
                _gameplayReadyAfter = Time.unscaledTime + 0.50f;

                // 1.0.10 checked this only in Awake. In the observed BepInEx load order,
                // Keeper's Lantern initializes before Save Now, so the early lookup can be
                // false even though Save Now is registered before gameplay begins. Re-read
                // the completed plugin registry exactly once per spawned gameplay Player.
                bool saveNowWasPresent = _saveNowPresent;
                try { _saveNowPresent = Chainloader.PluginInfos.ContainsKey(SaveNowGuid); }
                catch { _saveNowPresent = false; }
                if (_saveNowPresent && !saveNowWasPresent)
                    Logger.LogInfo("Save Now detected after plugin startup. Direct-load interior compatibility refresh is armed for this gameplay load.");

                _saveNowEnvironmentRefreshPending = _saveNowPresent;
                Logger.LogInfo("Gameplay Player(Clone) detected; delaying ambient-pass activation by 0.50s for save/environment settle.");
            }

            return _gameplayPlayer != null && Time.unscaledTime >= _gameplayReadyAfter;
        }

        private void TryRefreshSaveNowEnvironment(LightingSnapshot21 state, LightingRegion21 region, bool gameplayReady)
        {
            if (!_saveNowEnvironmentRefreshPending || !gameplayReady) return;
            if (region == LightingRegion21.Unresolved) return;

            // Save Now restores the stored coordinates with Player.PlaceAtPos after the
            // game's load events. That can leave an already-selected interior preset with
            // stale LUT/filter state until a normal exit/re-entry makes vanilla apply the
            // preset again. Do exactly that one missing vanilla operation, once per newly
            // spawned gameplay Player, and only when the settled load really starts indoors.
            if (region != LightingRegion21.Interior || !state.IndoorKnown || !state.IsIndoor ||
                string.IsNullOrEmpty(state.PresetName))
            {
                _saveNowEnvironmentRefreshPending = false;
                return;
            }

            _saveNowEnvironmentRefreshPending = false;
            if (_worldPass != null) _worldPass.PrepareForVanillaEnvironmentRefresh();

            string refreshedPreset;
            if (_probe.TryReapplyCurrentPreset(out refreshedPreset))
            {
                Logger.LogInfo("Save Now compatibility: reapplied current vanilla environment preset after direct interior load | preset=" +
                    (string.IsNullOrEmpty(refreshedPreset) ? state.PresetName : refreshedPreset) + ".");
            }
            else
            {
                Logger.LogWarning("Save Now compatibility: current interior preset was detected but vanilla ApplyEnvironmentPreset could not be invoked; leaving game state untouched.");
            }
        }

        private void EnsureWorldCamera()
        {
            if (_worldCamera != null && _worldPass != null) return;
            if (Time.unscaledTime < _nextCameraBind) return;
            _nextCameraBind = Time.unscaledTime + 1f;

            Camera[] cameras = Resources.FindObjectsOfTypeAll<Camera>();
            _worldCamera = cameras
                .Where(c => c != null && c.gameObject != null && c.gameObject.scene.IsValid() && c.gameObject.scene.isLoaded)
                .Where(c => string.Equals(c.name, "Main Camera (gs)", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.gameObject.activeInHierarchy)
                .FirstOrDefault();

            if (_worldCamera == null) return;
            _worldPass = _worldCamera.GetComponent<WorldLightingPass21>();
            if (_worldPass == null) _worldPass = _worldCamera.gameObject.AddComponent<WorldLightingPass21>();
            Logger.LogInfo("World lighting pass bound to " + PathOf(_worldCamera.transform) + ".");
        }

        private void OnDestroy()
        {
            SharedLanternFactor = 0f;
            SharedLightingSnapshotValid = false;
            if (_keeperLight != null) _keeperLight.Restore();
            if (_worldPass != null) Destroy(_worldPass);
        }

        internal static string PathOf(Transform transform)
        {
            if (transform == null) return "<null>";
            var pieces = new Stack<string>();
            Transform p = transform;
            while (p != null)
            {
                pieces.Push(p.name);
                p = p.parent;
            }
            return string.Join("/", pieces.ToArray());
        }
    }

    internal enum LightingRegion21 { Unresolved, OutdoorDay, OutdoorNight, Interior, Dungeon }

    internal struct LightingSnapshot21
    {
        public bool TimeKnown;
        public float TimeK;
        public bool DungeonKnown;
        public bool IsDungeon;
        public bool IndoorKnown;
        public bool IsIndoor;
        public string PresetName;
    }

    internal sealed class EnvironmentProbe21
    {
        private Type _timeType;
        private Type _mainGameType;
        private Type _environmentType;
        private MethodInfo _getTimeK;
        private MemberInfo _mainGameMe;
        private MemberInfo _currentPreset;
        private MemberInfo _environmentMe;
        private MethodInfo _applyEnvironmentPreset;
        private UnityEngine.Object _timeObject;
        private float _nextResolve;

        public EnvironmentProbe21() { Resolve(); }

        public LightingSnapshot21 Read()
        {
            if ((_timeType == null || _mainGameType == null || _environmentType == null) && Time.unscaledTime >= _nextResolve)
            {
                _nextResolve = Time.unscaledTime + 2f;
                Resolve();
            }

            var state = new LightingSnapshot21();
            state.TimeKnown = ReadTime(out state.TimeK);
            state.DungeonKnown = ReadDungeonWithoutGetter(out state.IsDungeon);
            state.IndoorKnown = ReadPreset(out state.IsIndoor, out state.PresetName);

            if (IsDungeonPreset(state.PresetName))
            {
                state.DungeonKnown = true;
                state.IsDungeon = true;
            }

            if (state.DungeonKnown && state.IsDungeon)
            {
                state.IndoorKnown = true;
                state.IsIndoor = false;
            }
            return state;
        }

        private void Resolve()
        {
            _timeType = FindType("TimeOfDay");
            _mainGameType = FindType("MainGame");
            _environmentType = FindType("EnvironmentEngine");

            if (_timeType != null)
                _getTimeK = _timeType.GetMethod("GetTimeK", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (_mainGameType != null)
                _mainGameMe = FindMember(_mainGameType, "me", true);
            if (_environmentType != null)
            {
                _currentPreset = FindMember(_environmentType, "cur_preset", true);
                _environmentMe = FindMember(_environmentType, "me", true);
                _applyEnvironmentPreset = _environmentType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => string.Equals(m.Name, "ApplyEnvironmentPreset", StringComparison.Ordinal) &&
                        m.GetParameters().Length == 1);
            }
        }

        public bool TryReapplyCurrentPreset(out string name)
        {
            name = null;
            if (_environmentType == null || _currentPreset == null || _environmentMe == null || _applyEnvironmentPreset == null)
                return false;

            try
            {
                object engine = ReadMember(null, _environmentMe);
                object preset = ReadMember(null, _currentPreset);
                if (UnityNull(engine) || UnityNull(preset)) return false;

                ParameterInfo[] parameters = _applyEnvironmentPreset.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(preset.GetType()))
                    return false;

                UnityEngine.Object uo = preset as UnityEngine.Object;
                if (uo != null) name = uo.name;
                else
                {
                    object rawName = ReadNamed(preset, "name");
                    if (rawName != null) name = rawName.ToString();
                }

                _applyEnvironmentPreset.Invoke(engine, new[] { preset });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool ReadTime(out float value)
        {
            value = 0f;
            if (_timeType == null || _getTimeK == null) return false;
            try
            {
                if (_timeObject == null)
                {
                    UnityEngine.Object[] all = Resources.FindObjectsOfTypeAll(_timeType);
                    _timeObject = all.FirstOrDefault(x => x != null);
                }
                if (_timeObject == null) return false;
                object raw = _getTimeK.Invoke(_timeObject, null);
                if (raw == null) return false;
                value = Convert.ToSingle(raw);
                return true;
            }
            catch
            {
                _timeObject = null;
                return false;
            }
        }

        private bool ReadDungeonWithoutGetter(out bool isDungeon)
        {
            isDungeon = false;
            if (_mainGameType == null || _mainGameMe == null) return false;
            try
            {
                object mainGame = ReadMember(null, _mainGameMe);
                if (UnityNull(mainGame)) return false;

                bool initialized;
                if (!TryBool(mainGame, "_dungeon_root_set", out initialized)) return false;
                if (!initialized) return true;

                object root = ReadNamed(mainGame, "_dungeon_root");
                if (UnityNull(root)) return true;

                bool loaded;
                if (!TryBool(root, "dungeon_is_loaded_now", out loaded)) return false;
                if (!loaded) return true;

                object player = ReadNamed(mainGame, "player_component");
                if (UnityNull(player))
                {
                    isDungeon = true;
                    return true;
                }

                object currentZone = ReadNamed(player, "current_zone");
                isDungeon = UnityNull(currentZone);
                return true;
            }
            catch { return false; }
        }

        private bool ReadPreset(out bool indoor, out string name)
        {
            indoor = false;
            name = null;
            if (_environmentType == null || _currentPreset == null) return false;
            try
            {
                object preset = ReadMember(null, _currentPreset);
                if (UnityNull(preset)) return true;
                UnityEngine.Object uo = preset as UnityEngine.Object;
                if (uo != null) name = uo.name;
                else
                {
                    object rawName = ReadNamed(preset, "name");
                    if (rawName != null) name = rawName.ToString();
                }
                indoor = true;
                return true;
            }
            catch { return false; }
        }

        private static bool IsDungeonPreset(string value)
        {
            return !string.IsNullOrEmpty(value) && value.StartsWith("dungeon_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryBool(object instance, string name, out bool value)
        {
            value = false;
            object raw = ReadNamed(instance, name);
            if (!(raw is bool)) return false;
            value = (bool)raw;
            return true;
        }

        private static object ReadNamed(object instance, string name)
        {
            if (instance == null) return null;
            MemberInfo member = FindMember(instance.GetType(), name, false);
            return member == null ? null : ReadMember(instance, member);
        }

        private static MemberInfo FindMember(Type type, string name, bool isStatic)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            FieldInfo f = type.GetField(name, flags);
            if (f != null) return f;
            return type.GetProperty(name, flags);
        }

        private static object ReadMember(object instance, MemberInfo member)
        {
            FieldInfo f = member as FieldInfo;
            if (f != null) return f.GetValue(instance);
            PropertyInfo p = member as PropertyInfo;
            return p == null ? null : p.GetValue(instance, null);
        }

        private static bool UnityNull(object value)
        {
            if (value == null) return true;
            UnityEngine.Object uo = value as UnityEngine.Object;
            return uo != null && uo == null;
        }

        private static Type FindType(string simpleName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type direct = assembly.GetType(simpleName, false);
                    if (direct != null) return direct;
                    Type match = assembly.GetTypes().FirstOrDefault(t => t != null && t.Name == simpleName);
                    if (match != null) return match;
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Type match = ex.Types.FirstOrDefault(t => t != null && t.Name == simpleName);
                    if (match != null) return match;
                }
                catch { }
            }
            return null;
        }
    }

    internal sealed class WorldLightingPass21 : MonoBehaviour
    {
        private float _ambientScale = 1f;
        private float _coolTint;
        private bool _haveAmbientOverride;
        private Color _baseAmbient;
        private Color _lastAppliedAmbient;
        private bool _freezeAmbientSource;
        private bool _frozenSourceReady;

        public void Configure(float ambientScale, float coolTint, bool freezeAmbientSource)
        {
            _ambientScale = Mathf.Clamp(ambientScale, 0.05f, 1f);
            _coolTint = Mathf.Clamp01(coolTint);
            if (_freezeAmbientSource != freezeAmbientSource)
            {
                _freezeAmbientSource = freezeAmbientSource;
                _frozenSourceReady = false;
            }
        }

        public void PrepareForVanillaEnvironmentRefresh()
        {
            RestoreAmbient();
            _frozenSourceReady = false;
        }

        public void ApplyAmbientLate()
        {
            Color current = RenderSettings.ambientLight;

            if (_ambientScale >= 0.9995f && _coolTint <= 0.0005f)
            {
                // If vanilla did not refresh ambient this frame (pause/transition), avoid
                // leaving our previous dark value behind when the effect becomes inactive.
                if (_haveAmbientOverride && ColorsClose(current, _lastAppliedAmbient))
                    RenderSettings.ambientLight = _baseAmbient;
                _haveAmbientOverride = false;
                _frozenSourceReady = false;
                return;
            }

            Color source;
            if (_freezeAmbientSource)
            {
                // Dungeon owns a fixed interior-like profile. Capture a dungeon-local
                // vanilla source once, then ignore subsequent TimeOfDay ambient changes.
                // If the previous frame still contains our own old override during a
                // transition, keep the remembered vanilla source for this frame and wait
                // for vanilla/environment code to provide a fresh source before freezing.
                if (!_frozenSourceReady)
                {
                    if (_haveAmbientOverride && ColorsClose(current, _lastAppliedAmbient))
                    {
                        source = _baseAmbient;
                    }
                    else
                    {
                        _baseAmbient = current;
                        _frozenSourceReady = true;
                        source = _baseAmbient;
                    }
                }
                else
                {
                    source = _baseAmbient;
                }
            }
            else if (_haveAmbientOverride && ColorsClose(current, _lastAppliedAmbient))
            {
                // Outdoor TimeOfDay did not overwrite it this frame: reuse the remembered
                // vanilla colour so darkness never compounds frame over frame.
                _frozenSourceReady = false;
                source = _baseAmbient;
            }
            else
            {
                _frozenSourceReady = false;
                source = current;
                _baseAmbient = current;
            }

            Color target = Transform(source);
            if (!ColorsClose(current, target))
                RenderSettings.ambientLight = target;

            _lastAppliedAmbient = target;
            _haveAmbientOverride = true;
        }

        private Color Transform(Color source)
        {
            Color dim = new Color(source.r * _ambientScale, source.g * _ambientScale, source.b * _ambientScale, source.a);
            Color cool = new Color(dim.r * 0.74f, dim.g * 0.87f, Mathf.Min(1f, dim.b * 1.07f + 0.012f), dim.a);
            return Color.Lerp(dim, cool, _coolTint);
        }

        private void RestoreAmbient()
        {
            if (!_haveAmbientOverride) return;
            Color current = RenderSettings.ambientLight;
            if (ColorsClose(current, _lastAppliedAmbient))
                RenderSettings.ambientLight = _baseAmbient;
            _haveAmbientOverride = false;
        }

        private static bool ColorsClose(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.0005f &&
                   Mathf.Abs(a.g - b.g) < 0.0005f &&
                   Mathf.Abs(a.b - b.b) < 0.0005f &&
                   Mathf.Abs(a.a - b.a) < 0.0005f;
        }

        private void OnDisable() { RestoreAmbient(); }

        private void OnDestroy()
        {
            RestoreAmbient();
        }

    }

    internal sealed class KeeperLight21
    {
        private readonly BepInEx.Logging.ManualLogSource _log;
        private Transform _charLight;
        private Light _point;
        private Light _ground;
        private float _vanillaPointRange;
        private float _vanillaPointIntensity;
        private bool _vanillaPointEnabled;
        private Vector3 _vanillaPointLocalPosition;
        private long _pointOffsetWrites;
        private float _vanillaGroundRange;
        private float _vanillaGroundIntensity;
        private bool _vanillaGroundEnabled;
        private readonly List<FloatField21> _pointDynamic = new List<FloatField21>();
        private readonly List<FloatField21> _groundDynamic = new List<FloatField21>();
        private List<Light> _nativeDefaultLights;
        private List<float> _nativeDefaultK;
        private List<Light> _nativeGroundLights;
        private List<float> _nativeGroundK;
        private int _nativePointIndex = -1;
        private int _nativeGroundIndex = -1;
        private float _nativePointOriginalK;
        private float _nativeGroundOriginalK;
        private bool _nativeOriginalsCaptured;
        private bool _nativeIntensityBound;
        private FieldInfo _timeLightIntensityK;
        private float _nextNativeBind;
        private float _nextNativeIntensityUpdate;
        private long _nativeCoefficientWrites;
        private long _directIntensityFallbackWrites;
        private bool _hardOffApplied;
        private long _hardOffWrites;
        private bool _workshopGroundFillApplied;
        private float _nextWorkshopGroundFillUpdate;
        private float _workshopGroundFillRange;
        private float _workshopGroundFillIntensity;
        private long _workshopGroundFillWrites;
        private readonly HashSet<Light> _externalSeen = new HashSet<Light>();
        private int _externalNearbyCount;
        private float _externalNearestDistance = -1f;
        private float _externalStrongestScore;
        private float _externalAttenuation = 1f;
        private float _lastEffectiveFactor = -1f;
        private long _rangeCorrectionWrites;
        private float _lastDesiredPointRange;
        private float _lastDesiredGroundRange;
        private bool _bound;
        private float _nextBind;
        private float _lastFactor = -1f;
        private bool _vanillaIntensityBaselineValid;
        private bool _baselineCaptureBlockedLastApply;
        private int _baselineCaptureAfterFrame;
        private long _baselineDeferredCount;
        private float _baselineGlobalK = -1f;

        private float _targetPointRange = 312f;
        private float _targetPointIntensity = 0.78f;
        private float _targetPointK = 1.25f;
        private float _targetPointOffsetX;
        private float _targetPointOffsetY;
        private float _targetGroundRange = 455f;
        private float _targetGroundIntensity = 1.45f;
        private float _targetGroundK = 0.75f;

        public KeeperLight21(BepInEx.Logging.ManualLogSource log) { _log = log; }

        public void Configure(float pointRange, float pointIntensity, float pointK, float pointOffsetX, float pointOffsetY, float groundRange, float groundIntensity, float groundK)
        {
            _targetPointRange = Mathf.Max(0f, pointRange);
            _targetPointIntensity = Mathf.Max(0f, pointIntensity);
            _targetPointK = Mathf.Max(0f, pointK);
            _targetPointOffsetX = Mathf.Clamp(pointOffsetX, -200f, 200f);
            _targetPointOffsetY = Mathf.Clamp(pointOffsetY, -200f, 200f);
            _targetGroundRange = Mathf.Max(0f, groundRange);
            _targetGroundIntensity = Mathf.Max(0f, groundIntensity);
            _targetGroundK = Mathf.Max(0f, groundK);
        }

        public void Apply(float factor, bool hardOff, bool allowExternalCompensation, bool allowBaselineCapture)
        {
            if (!_bound)
            {
                if (Time.unscaledTime < _nextBind) return;
                _nextBind = Time.unscaledTime + 0.75f;
                Bind(allowBaselineCapture);
                if (!_bound) return;
            }

            if (_charLight == null || _point == null || _ground == null)
            {
                _bound = false;
                return;
            }

            factor = Mathf.Clamp01(factor);

            if (!allowBaselineCapture)
                _baselineCaptureBlockedLastApply = true;

            // Normal interiors are a real hard-off boundary, including the important
            // first-bind-inside-house case. 0.4.18 only restored after a previously active
            // lantern, so an initial indoor bind could leave vanilla Keeper lights visible.
            if (hardOff)
            {
                RestorePointOffset();
                ApplyHardOff();
                _lastFactor = 0f;
                return;
            }

            // 1.0.9: if the first bind happened while an interior preset had already
            // attenuated Light.intensity, do not use that value as the native baseline.
            // Release only the hard-off boolean state, give vanilla DynamicLights one full
            // outdoor frame to restore the rig, then capture the real baseline.
            if (!_vanillaIntensityBaselineValid)
            {
                if (!allowBaselineCapture)
                {
                    _lastFactor = 0f;
                    return;
                }

                if (_baselineCaptureBlockedLastApply)
                {
                    _baselineCaptureBlockedLastApply = false;
                    _baselineCaptureAfterFrame = Time.frameCount + 1;
                    if (_hardOffApplied) ReleaseHardOffWithoutIntensityRestore();
                    _baselineDeferredCount++;
                    _log.LogInfo("Keeper native intensity baseline deferred for one outdoor frame after interior bind.");
                    _lastFactor = 0f;
                    return;
                }

                if (Time.frameCount < _baselineCaptureAfterFrame)
                {
                    if (_hardOffApplied) ReleaseHardOffWithoutIntensityRestore();
                    _lastFactor = 0f;
                    return;
                }

                if (!CaptureVanillaIntensityBaseline())
                {
                    _baselineCaptureAfterFrame = Time.frameCount + 1;
                    _lastFactor = 0f;
                    return;
                }
            }
            else if (allowBaselineCapture)
            {
                _baselineCaptureBlockedLastApply = false;
            }

            if (_hardOffApplied)
            {
                Restore();
                _hardOffApplied = false;
                _log.LogInfo("Keeper interior hard-off released; native light state restored.");
            }

            if (factor <= 0.001f)
            {
                RestorePointOffset();
                if (_lastFactor > 0.001f)
                {
                    Restore();
                    _log.LogInfo("Keeper lantern released native lights back to game control.");
                }
                _externalAttenuation = 1f;
                _lastEffectiveFactor = 0f;
                _lastDesiredPointRange = _vanillaPointRange;
                _lastDesiredGroundRange = _vanillaGroundRange;
                _lastFactor = 0f;
                return;
            }

            ApplyPointOffset();

            bool enteringActive = _lastFactor <= 0.001f;
            if (enteringActive)
            {
                _point.enabled = true;
                _ground.enabled = true;
            }

            // 0.4.20: sample existing DynamicLights at the same cheap 10 Hz cadence used
            // for Keeper coefficients. Only outdoor night uses overlap compensation;
            // accepted dungeon lighting is intentionally untouched.
            bool nativeTick = enteringActive || Time.unscaledTime >= _nextNativeIntensityUpdate;
            if (!allowExternalCompensation)
            {
                _externalNearbyCount = 0;
                _externalNearestDistance = -1f;
                _externalStrongestScore = 0f;
                _externalAttenuation = 1f;
            }
            else if (nativeTick)
            {
                UpdateExternalLightTelemetry();
            }

            float effectiveFactor = factor * _externalAttenuation;
            bool coefficientChanged = enteringActive || _lastEffectiveFactor < 0f ||
                Mathf.Abs(effectiveFactor - _lastEffectiveFactor) >= 0.002f ||
                Mathf.Abs(factor - _lastFactor) >= 0.002f;

            // Radius follows the raw gameplay fade, not overlap attenuation. 0.4.20 wrote
            // range only while the fade was changing; vanilla can later restore its own
            // range, leaving the full-night pool stuck at the small daytime radius. Keep
            // the exact smooth curve, but verify/reassert it at the already-existing 10 Hz
            // native-light cadence. Writes happen only when the component actually differs.
            float desiredPointRange = Mathf.Lerp(_vanillaPointRange, _targetPointRange, factor);
            float desiredGroundRange = Mathf.Lerp(_vanillaGroundRange, _targetGroundRange, factor);
            _lastDesiredPointRange = desiredPointRange;
            _lastDesiredGroundRange = desiredGroundRange;

            // 0.4.15 architecture remains: vanilla DynamicLights.Update is the normal
            // per-frame Light.intensity writer. We only alter its cached coefficients at 10 Hz.
            if (nativeTick)
            {
                _nextNativeIntensityUpdate = Time.unscaledTime + 0.10f;
                if (Mathf.Abs(_point.range - desiredPointRange) > 0.05f)
                {
                    _point.range = desiredPointRange;
                    _rangeCorrectionWrites++;
                }
                if (Mathf.Abs(_ground.range - desiredGroundRange) > 0.05f)
                {
                    _ground.range = desiredGroundRange;
                    _rangeCorrectionWrites++;
                }

                if (!ApplyNativeIntensityCoefficients(effectiveFactor))
                {
                    // Compatibility fallback only. GK 1.407 should bind the native lists.
                    _point.intensity = Mathf.Lerp(_vanillaPointIntensity, _targetPointIntensity, effectiveFactor);
                    _ground.intensity = Mathf.Lerp(_vanillaGroundIntensity, _targetGroundIntensity, effectiveFactor);
                    _directIntensityFallbackWrites += 2;
                }
            }

            if (coefficientChanged)
            {
                foreach (FloatField21 value in _pointDynamic)
                {
                    if (Mathf.Abs(_targetPointK - value.Original) > 0.001f)
                        value.Set(Mathf.Lerp(value.Original, _targetPointK, effectiveFactor));
                }
                foreach (FloatField21 value in _groundDynamic)
                    value.Set(Mathf.Lerp(value.Original, _targetGroundK, effectiveFactor));
            }

            if ((_lastFactor <= 0.001f && factor > 0.001f) || (_lastFactor > 0.001f && factor <= 0.001f))
                _log.LogInfo("Keeper lantern " + (factor > 0.001f ? "active" : "returned to vanilla") + ".");
            _lastEffectiveFactor = effectiveFactor;
            _lastFactor = factor;
        }

        public void ApplyWorkshopGroundFill(float range, float intensity)
        {
            // Reuse the already-bound native Keeper rig. Calling the normal zero-factor
            // path releases a prior hard-off once, but does not activate the lantern.
            Apply(0f, false, false, false);
            if (!_bound || _point == null || _ground == null) return;

            range = Mathf.Max(0f, range);
            intensity = Mathf.Max(0f, intensity);
            bool entering = !_workshopGroundFillApplied;
            _workshopGroundFillApplied = true;
            _workshopGroundFillRange = range;
            _workshopGroundFillIntensity = intensity;
            // Workshop fill owns the ground radius while active; keep F10 desired-range
            // telemetry aligned with that mode instead of reporting the vanilla radius.
            _lastDesiredPointRange = _vanillaPointRange;
            _lastDesiredGroundRange = range;

            // Point is the accepted shadow source. Keep it physically disabled in workshop
            // fill mode so this visual experiment cannot alter shadow-source matching.
            if (_point.enabled)
            {
                _point.enabled = false;
                _workshopGroundFillWrites++;
            }
            if (!_ground.enabled)
            {
                _ground.enabled = true;
                _workshopGroundFillWrites++;
            }

            if (entering)
                _log.LogInfo("Keeper workshop ground-only fill active | range=" + range.ToString("0.0") + " intensity=" + intensity.ToString("0.00") + ".");

            // Same cheap cadence as the native night-light bridge. The final LateUpdate
            // point/ground enabled-state guards above are only boolean writes when needed.
            if (Time.unscaledTime < _nextWorkshopGroundFillUpdate) return;
            _nextWorkshopGroundFillUpdate = Time.unscaledTime + 0.10f;

            if (Mathf.Abs(_ground.range - range) > 0.05f)
            {
                _ground.range = range;
                _workshopGroundFillWrites++;
            }

            if (!_nativeIntensityBound || !ValidateNativeIntensityIndices())
            {
                TryBindNativeIntensityControl();
            }

            if (_nativeIntensityBound && ValidateNativeIntensityIndices() && _nativeOriginalsCaptured)
            {
                float globalK = Mathf.Clamp(ReadGlobalLightIntensityK(), 0.2f, 1f);
                float scale = _vanillaGroundIntensity > 0.0001f ? intensity / _vanillaGroundIntensity : 1f;
                float desiredGroundK = _nativeGroundOriginalK * (scale / globalK);
                if (Mathf.Abs(_nativeGroundK[_nativeGroundIndex] - desiredGroundK) > 0.0001f)
                {
                    _nativeGroundK[_nativeGroundIndex] = desiredGroundK;
                    _nativeCoefficientWrites++;
                    _workshopGroundFillWrites++;
                }
            }
            else
            {
                // Compatibility fallback only. Normal GK 1.407 uses the cached native K.
                if (Mathf.Abs(_ground.intensity - intensity) > 0.001f)
                {
                    _ground.intensity = intensity;
                    _directIntensityFallbackWrites++;
                    _workshopGroundFillWrites++;
                }
            }
        }

        public void ReleaseWorkshopGroundFill()
        {
            if (!_workshopGroundFillApplied) return;
            Restore();
            _workshopGroundFillApplied = false;
            _nextWorkshopGroundFillUpdate = 0f;
            _workshopGroundFillRange = 0f;
            _workshopGroundFillIntensity = 0f;
            _log.LogInfo("Keeper workshop ground-only fill released; native light state restored.");
        }

        private void ApplyHardOff()
        {
            if (!_hardOffApplied)
            {
                RestoreNativeIntensityCoefficients();
                foreach (FloatField21 value in _pointDynamic) value.Restore();
                foreach (FloatField21 value in _groundDynamic) value.Restore();
                _point.range = _vanillaPointRange;
                if (_vanillaIntensityBaselineValid) _point.intensity = _vanillaPointIntensity;
                _ground.range = _vanillaGroundRange;
                if (_vanillaIntensityBaselineValid) _ground.intensity = _vanillaGroundIntensity;
                _hardOffApplied = true;
                _log.LogInfo("Keeper native lights hard-off for normal interior.");
            }

            // DynamicLights may touch the components earlier in the frame. LateUpdate is
            // the final safety gate before rendering; only write when vanilla re-enabled one.
            if (_point.enabled) { _point.enabled = false; _hardOffWrites++; }
            if (_ground.enabled) { _ground.enabled = false; _hardOffWrites++; }
        }

        private void UpdateExternalLightTelemetry()
        {
            _externalSeen.Clear();
            _externalNearbyCount = 0;
            _externalNearestDistance = -1f;
            _externalStrongestScore = 0f;
            if (_charLight == null) return;

            EvaluateExternalLights(_nativeDefaultLights);
            EvaluateExternalLights(_nativeGroundLights);
        }

        private void EvaluateExternalLights(List<Light> lights)
        {
            if (lights == null || _charLight == null) return;
            Vector3 keeper = _charLight.position;
            for (int i = 0; i < lights.Count; i++)
            {
                Light light = lights[i];
                if (light == null || light == _point || light == _ground || !_externalSeen.Add(light)) continue;
                if (!light.enabled || light.gameObject == null || !light.gameObject.activeInHierarchy) continue;
                if (!light.gameObject.scene.IsValid() || !light.gameObject.scene.isLoaded) continue;
                if (light.type != LightType.Point || light.range <= 0.01f || light.intensity <= 0.001f) continue;

                float distance = Vector3.Distance(keeper, light.transform.position);
                if (distance > light.range) continue;

                _externalNearbyCount++;
                if (_externalNearestDistance < 0f || distance < _externalNearestDistance)
                    _externalNearestDistance = distance;

                float proximity = 1f - Mathf.Clamp01(distance / light.range);
                float score = light.intensity * proximity * proximity;
                if (score > _externalStrongestScore) _externalStrongestScore = score;
            }

            float x = Mathf.InverseLerp(0.75f, 2.25f, _externalStrongestScore);
            x = x * x * (3f - 2f * x);
            _externalAttenuation = Mathf.Lerp(1f, 0.45f, x);
        }

        private bool CaptureVanillaIntensityBaseline()
        {
            if (_point == null || _ground == null) return false;

            float pointIntensity = _point.intensity;
            float groundIntensity = _ground.intensity;
            if (pointIntensity <= 0.0001f || groundIntensity <= 0.0001f)
                return false;

            // DynamicLights writes a live output that already contains TimeOfDay's global
            // light_intensity_k. Store the equivalent full-global-K baseline so calibration
            // is invariant to whether capture occurs during day, dusk or night.
            float globalK;
            if (!TryReadGlobalLightIntensityK(out globalK))
                return false;
            globalK = Mathf.Clamp(globalK, 0.2f, 1f);

            float normalizedPointIntensity = pointIntensity / globalK;
            float normalizedGroundIntensity = groundIntensity / globalK;
            if (normalizedPointIntensity <= 0.0001f || normalizedGroundIntensity <= 0.0001f)
                return false;

            _vanillaPointRange = _point.range;
            _vanillaPointIntensity = normalizedPointIntensity;
            _vanillaPointEnabled = _point.enabled;
            _vanillaPointLocalPosition = _point.transform.localPosition;
            _vanillaGroundRange = _ground.range;
            _vanillaGroundIntensity = normalizedGroundIntensity;
            _vanillaGroundEnabled = _ground.enabled;
            _lastDesiredPointRange = _vanillaPointRange;
            _lastDesiredGroundRange = _vanillaGroundRange;
            _baselineGlobalK = globalK;
            _vanillaIntensityBaselineValid = true;

            _log.LogInfo("Keeper native intensity baseline captured | live=" +
                pointIntensity.ToString("0.###") + "/" + groundIntensity.ToString("0.###") +
                " | globalK=" + globalK.ToString("0.###") +
                " | normalized=" + _vanillaPointIntensity.ToString("0.###") + "/" +
                _vanillaGroundIntensity.ToString("0.###") + ".");
            return true;
        }

        private void ReleaseHardOffWithoutIntensityRestore()
        {
            if (!_hardOffApplied || _point == null || _ground == null) return;

            RestoreNativeIntensityCoefficients();
            RestorePointOffset();
            _point.range = _vanillaPointRange;
            _point.enabled = _vanillaPointEnabled;
            _ground.range = _vanillaGroundRange;
            _ground.enabled = _vanillaGroundEnabled;
            foreach (FloatField21 value in _pointDynamic) value.Restore();
            foreach (FloatField21 value in _groundDynamic) value.Restore();
            _hardOffApplied = false;
        }

        private void Bind(bool allowBaselineCapture)
        {
            GameObject player = GameObject.Find("World/Player(Clone)");
            if (player == null) return;
            Transform charLight = player.transform.Find("content/character/char_hero/Char light");
            if (charLight == null) return;
            Transform pointTransform = charLight.Find("Point light");
            Transform groundTransform = charLight.Find("ground light");
            if (pointTransform == null || groundTransform == null) return;
            Light point = pointTransform.GetComponent<Light>();
            Light ground = groundTransform.GetComponent<Light>();
            if (point == null || ground == null) return;

            _charLight = charLight;
            _point = point;
            _ground = ground;
            _vanillaPointRange = point.range;
            _vanillaPointEnabled = point.enabled;
            _vanillaPointLocalPosition = pointTransform.localPosition;
            _vanillaGroundRange = ground.range;
            _vanillaGroundEnabled = ground.enabled;
            if (!_vanillaIntensityBaselineValid)
            {
                // 1.0.9: a freshly bound Light may still expose prefab/raw intensity before
                // DynamicLights applies the current TimeOfDay coefficient. Never calibrate
                // during Bind; wait for at least one settled native frame instead.
                _baselineCaptureAfterFrame = Math.Max(_baselineCaptureAfterFrame, Time.frameCount + 1);
                if (!allowBaselineCapture)
                    _baselineCaptureBlockedLastApply = true;
            }
            else if (allowBaselineCapture)
            {
                _baselineCaptureBlockedLastApply = false;
            }
            _lastDesiredPointRange = _vanillaPointRange;
            _lastDesiredGroundRange = _vanillaGroundRange;
            _pointDynamic.Clear();
            _groundDynamic.Clear();
            CaptureDynamic(pointTransform, _pointDynamic);
            CaptureDynamic(groundTransform, _groundDynamic);
            _bound = true;
            TryBindNativeIntensityControl();
            _log.LogInfo("Keeper native light bound | currentPoint=" + point.range.ToString("0.##") + "/" + point.intensity.ToString("0.##") +
                " | currentGround=" + ground.range.ToString("0.##") + "/" + ground.intensity.ToString("0.##") +
                " | baseline=" + (_vanillaIntensityBaselineValid
                    ? (_vanillaPointIntensity.ToString("0.##") + "/" + _vanillaGroundIntensity.ToString("0.##"))
                    : "deferred") +
                " | nativeDynamicLists=" + _nativeIntensityBound + ".");
        }

        private Vector3 DesiredPointLocalPosition()
        {
            return _vanillaPointLocalPosition + new Vector3(_targetPointOffsetX, _targetPointOffsetY, 0f);
        }

        private void ApplyPointOffset()
        {
            if (_point == null) return;
            Vector3 desired = DesiredPointLocalPosition();
            if ((_point.transform.localPosition - desired).sqrMagnitude > 0.000001f)
            {
                _point.transform.localPosition = desired;
                _pointOffsetWrites++;
            }
        }

        private void RestorePointOffset()
        {
            if (_point == null) return;
            if ((_point.transform.localPosition - _vanillaPointLocalPosition).sqrMagnitude > 0.000001f)
            {
                _point.transform.localPosition = _vanillaPointLocalPosition;
                _pointOffsetWrites++;
            }
        }

        private bool ApplyNativeIntensityCoefficients(float factor)
        {
            if (!_vanillaIntensityBaselineValid) return false;
            if (!_nativeIntensityBound || !ValidateNativeIntensityIndices())
            {
                if (Time.unscaledTime < _nextNativeBind) return false;
                _nextNativeBind = Time.unscaledTime + 0.75f;
                if (!TryBindNativeIntensityControl()) return false;
            }

            float globalK = ReadGlobalLightIntensityK();
            globalK = Mathf.Clamp(globalK, 0.2f, 1f);

            float pointScale = _vanillaPointIntensity > 0.0001f ? _targetPointIntensity / _vanillaPointIntensity : 1f;
            float groundScale = _vanillaGroundIntensity > 0.0001f ? _targetGroundIntensity / _vanillaGroundIntensity : 1f;

            // DynamicLights computes: baseIntensity * globalK * cachedK * presetAlpha.
            // Interpolate between vanilla output and our accepted constant/base output,
            // then solve only for cachedK. Flicker/presetAlpha remains fully vanilla.
            float desiredPointK = _nativePointOriginalK * Mathf.Lerp(1f, pointScale / globalK, factor);
            float desiredGroundK = _nativeGroundOriginalK * Mathf.Lerp(1f, groundScale / globalK, factor);

            if (Mathf.Abs(_nativeDefaultK[_nativePointIndex] - desiredPointK) > 0.0001f)
            {
                _nativeDefaultK[_nativePointIndex] = desiredPointK;
                _nativeCoefficientWrites++;
            }
            if (Mathf.Abs(_nativeGroundK[_nativeGroundIndex] - desiredGroundK) > 0.0001f)
            {
                _nativeGroundK[_nativeGroundIndex] = desiredGroundK;
                _nativeCoefficientWrites++;
            }
            return true;
        }

        private bool TryBindNativeIntensityControl()
        {
            try
            {
                Type dynamicLights = FindRuntimeType("DynamicLights");
                if (dynamicLights == null) return false;

                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo defaultLightsField = dynamicLights.GetField("_lights_default", flags);
                FieldInfo defaultKField = dynamicLights.GetField("_lights_default_k", flags);
                FieldInfo groundLightsField = dynamicLights.GetField("_lights_ground", flags);
                FieldInfo groundKField = dynamicLights.GetField("_lights_ground_k", flags);
                if (defaultLightsField == null || defaultKField == null || groundLightsField == null || groundKField == null)
                    return false;

                _nativeDefaultLights = defaultLightsField.GetValue(null) as List<Light>;
                _nativeDefaultK = defaultKField.GetValue(null) as List<float>;
                _nativeGroundLights = groundLightsField.GetValue(null) as List<Light>;
                _nativeGroundK = groundKField.GetValue(null) as List<float>;
                if (_nativeDefaultLights == null || _nativeDefaultK == null || _nativeGroundLights == null || _nativeGroundK == null)
                    return false;

                _nativePointIndex = _nativeDefaultLights.IndexOf(_point);
                _nativeGroundIndex = _nativeGroundLights.IndexOf(_ground);
                if (!ValidateNativeIntensityIndices()) return false;

                if (!_nativeOriginalsCaptured)
                {
                    _nativePointOriginalK = _nativeDefaultK[_nativePointIndex];
                    _nativeGroundOriginalK = _nativeGroundK[_nativeGroundIndex];
                    _nativeOriginalsCaptured = true;
                }

                if (_timeLightIntensityK == null)
                {
                    Type timeType = FindRuntimeType("TimeOfDay");
                    if (timeType != null)
                        _timeLightIntensityK = timeType.GetField("light_intensity_k", flags);
                }

                _nativeIntensityBound = true;
                return true;
            }
            catch
            {
                _nativeIntensityBound = false;
                return false;
            }
        }

        private bool ValidateNativeIntensityIndices()
        {
            if (_nativeDefaultLights == null || _nativeDefaultK == null || _nativeGroundLights == null || _nativeGroundK == null)
                return false;
            if (_nativePointIndex < 0 || _nativePointIndex >= _nativeDefaultLights.Count || _nativePointIndex >= _nativeDefaultK.Count ||
                _nativeDefaultLights[_nativePointIndex] != _point)
            {
                _nativePointIndex = _nativeDefaultLights.IndexOf(_point);
            }
            if (_nativeGroundIndex < 0 || _nativeGroundIndex >= _nativeGroundLights.Count || _nativeGroundIndex >= _nativeGroundK.Count ||
                _nativeGroundLights[_nativeGroundIndex] != _ground)
            {
                _nativeGroundIndex = _nativeGroundLights.IndexOf(_ground);
            }
            return _nativePointIndex >= 0 && _nativePointIndex < _nativeDefaultK.Count &&
                   _nativeGroundIndex >= 0 && _nativeGroundIndex < _nativeGroundK.Count;
        }

        private bool TryReadGlobalLightIntensityK(out float value)
        {
            value = 1f;
            if (_timeLightIntensityK == null)
            {
                TryBindNativeIntensityControl();
                if (_timeLightIntensityK == null) return false;
            }
            try
            {
                object raw = _timeLightIntensityK.GetValue(null);
                if (!(raw is float)) return false;
                value = (float)raw;
                return !float.IsNaN(value) && !float.IsInfinity(value) && value > 0.0001f;
            }
            catch
            {
                value = 1f;
                return false;
            }
        }

        private float ReadGlobalLightIntensityK()
        {
            float value;
            return TryReadGlobalLightIntensityK(out value) ? value : 1f;
        }

        private static Type FindRuntimeType(string simpleName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type direct = assembly.GetType(simpleName, false);
                    if (direct != null) return direct;
                    Type[] types = assembly.GetTypes();
                    for (int i = 0; i < types.Length; i++)
                        if (types[i] != null && types[i].Name == simpleName) return types[i];
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Type[] types = ex.Types;
                    if (types == null) continue;
                    for (int i = 0; i < types.Length; i++)
                        if (types[i] != null && types[i].Name == simpleName) return types[i];
                }
                catch { }
            }
            return null;
        }

        public string PerfSummary
        {
            get
            {
                return "nativeLists=" + _nativeIntensityBound +
                    ",nativeKWrites=" + _nativeCoefficientWrites +
                    ",directFallbackWrites=" + _directIntensityFallbackWrites +
                    ",baselineValid=" + _vanillaIntensityBaselineValid +
                    ",baselineDeferred=" + _baselineDeferredCount +
                    ",baselineGlobalK=" + (_baselineGlobalK > 0f ? _baselineGlobalK.ToString("0.###") : "-") +
                    ",baselineIntensity=" + (_vanillaIntensityBaselineValid ? (_vanillaPointIntensity.ToString("0.###") + "/" + _vanillaGroundIntensity.ToString("0.###")) : "-") +
                    ",hardOff=" + _hardOffApplied +
                    ",hardOffWrites=" + _hardOffWrites +
                    ",workshopFill=" + _workshopGroundFillApplied +
                    ",workshopFillRange=" + _workshopGroundFillRange.ToString("0.0") +
                    ",workshopFillIntensity=" + _workshopGroundFillIntensity.ToString("0.00") +
                    ",workshopFillWrites=" + _workshopGroundFillWrites +
                    ",externalNearby=" + _externalNearbyCount +
                    ",nearestExternal=" + (_externalNearestDistance >= 0f ? _externalNearestDistance.ToString("0.0") : "-") +
                    ",externalScore=" + _externalStrongestScore.ToString("0.000") +
                    ",overlapAtten=" + _externalAttenuation.ToString("0.000") +
                    ",rangeWrites=" + _rangeCorrectionWrites +
                    ",pointOffset=" + _targetPointOffsetX.ToString("0.00") + "/" + _targetPointOffsetY.ToString("0.00") +
                    ",pointOffsetWrites=" + _pointOffsetWrites +
                    ",pointLocal=" + (_point != null ? _point.transform.localPosition.ToString("F2") : "-") +
                    ",pointWorld=" + (_point != null ? _point.transform.position.ToString("F2") : "-") +
                    ",pointRange=" + (_point != null ? _point.range.ToString("0.0") : "-") + "/" + _lastDesiredPointRange.ToString("0.0") +
                    ",groundRange=" + (_ground != null ? _ground.range.ToString("0.0") : "-") + "/" + _lastDesiredGroundRange.ToString("0.0");
            }
        }

        private static void CaptureDynamic(Transform transform, List<FloatField21> output)
        {
            foreach (Component component in transform.GetComponents<Component>())
            {
                if (component == null) continue;
                FieldInfo field = component.GetType().GetField("intensity_k", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field == null || field.FieldType != typeof(float) || field.IsStatic) continue;
                try { output.Add(new FloatField21(component, field, (float)field.GetValue(component))); }
                catch { }
            }
        }

        public void Restore()
        {
            if (!_bound || _point == null || _ground == null) return;
            RestoreNativeIntensityCoefficients();
            RestorePointOffset();
            _point.range = _vanillaPointRange;
            if (_vanillaIntensityBaselineValid) _point.intensity = _vanillaPointIntensity;
            _point.enabled = _vanillaPointEnabled;
            _ground.range = _vanillaGroundRange;
            if (_vanillaIntensityBaselineValid) _ground.intensity = _vanillaGroundIntensity;
            _ground.enabled = _vanillaGroundEnabled;
            _lastDesiredPointRange = _vanillaPointRange;
            _lastDesiredGroundRange = _vanillaGroundRange;
            foreach (FloatField21 value in _pointDynamic) value.Restore();
            foreach (FloatField21 value in _groundDynamic) value.Restore();
            _hardOffApplied = false;
        }

        private void RestoreNativeIntensityCoefficients()
        {
            if (!_nativeOriginalsCaptured) return;
            try
            {
                if (ValidateNativeIntensityIndices())
                {
                    _nativeDefaultK[_nativePointIndex] = _nativePointOriginalK;
                    _nativeGroundK[_nativeGroundIndex] = _nativeGroundOriginalK;
                    _nativeCoefficientWrites += 2;
                }
            }
            catch { }
        }
    }

    internal sealed class FloatField21
    {
        private readonly Component _component;
        private readonly FieldInfo _field;
        public readonly float Original;
        public FloatField21(Component component, FieldInfo field, float original)
        {
            _component = component;
            _field = field;
            Original = original;
        }
        public void Set(float value)
        {
            if (_component == null) return;
            try { _field.SetValue(_component, value); } catch { }
        }
        public void Restore() { Set(Original); }
    }
}
