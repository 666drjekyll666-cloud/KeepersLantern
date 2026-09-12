using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace KeepersLantern
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("nikich.gyk.keeperslantern", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(25000)]
    public sealed class DungeonPracticalLightBoostPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "nikich.gyk.keeperslantern.dungeonpracticalboost";
        private const string PluginName = "Keeper's Lantern - Dungeon Practical Lights";
        private const string PluginVersion = "1.0.11";
        private const float RangeMultiplier = 1.25f;

        private readonly Dictionary<Light, float> _originalRanges = new Dictionary<Light, float>();
        private bool _dungeonActive;
        private float _nextScan;
        private float _nextNativeResolve;
        private float _nextFallbackScan;
        private List<Light> _nativeDefaultLights;
        private List<Light> _nativeGroundLights;
        private readonly HashSet<Light> _scanSeen = new HashSet<Light>();

        private void Awake()
        {
            Logger.LogInfo("Dungeon practical-light boost 1.0.11 loaded. Dungeon stationary light radius x" + RangeMultiplier.ToString("0.00") + ".");
        }

        private void LateUpdate()
        {
            if (!UnifiedLightingPlugin.SharedLightingSnapshotValid) return;
            LightingSnapshot21 state = UnifiedLightingPlugin.SharedLightingSnapshot;
            bool inDungeon = state.DungeonKnown && state.IsDungeon;

            if (!inDungeon)
            {
                if (_dungeonActive)
                {
                    RestoreAll();
                    _dungeonActive = false;
                    Logger.LogInfo("Dungeon practical-light ranges restored.");
                }
                return;
            }

            _dungeonActive = true;
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 1.0f;
            ScanAndBoost();
        }

        private void ScanAndBoost()
        {
            int newlyBoosted = 0;
            if (EnsureNativeLightLists())
            {
                _scanSeen.Clear();
                newlyBoosted += BoostNativeList(_nativeDefaultLights);
                newlyBoosted += BoostNativeList(_nativeGroundLights);
            }
            else
            {
                // Compatibility fallback only. Do not globally scan every second.
                if (Time.unscaledTime < _nextFallbackScan) return;
                _nextFallbackScan = Time.unscaledTime + 5f;
                Light[] lights = Resources.FindObjectsOfTypeAll<Light>();
                for (int i = 0; i < lights.Length; i++)
                    newlyBoosted += BoostCandidate(lights[i]);
            }

            if (newlyBoosted > 0)
                Logger.LogInfo("Dungeon practical lights boosted: +" + newlyBoosted + " (tracked " + _originalRanges.Count + ").");
        }

        private int BoostNativeList(List<Light> lights)
        {
            if (lights == null) return 0;
            int added = 0;
            for (int i = 0; i < lights.Count; i++)
            {
                Light light = lights[i];
                if (light == null || !_scanSeen.Add(light)) continue;
                added += BoostCandidate(light);
            }
            return added;
        }

        private int BoostCandidate(Light light)
        {
            if (light == null) return 0;

            float original;
            if (_originalRanges.TryGetValue(light, out original))
            {
                light.range = original * RangeMultiplier;
                return 0;
            }

            if (!IsDungeonPracticalCandidate(light)) return 0;
            original = light.range;
            _originalRanges[light] = original;
            light.range = original * RangeMultiplier;
            return 1;
        }

        private bool EnsureNativeLightLists()
        {
            if (_nativeDefaultLights != null && _nativeGroundLights != null) return true;
            if (Time.unscaledTime < _nextNativeResolve) return false;
            _nextNativeResolve = Time.unscaledTime + 1f;

            try
            {
                Type dynamicLights = FindRuntimeType("DynamicLights");
                if (dynamicLights == null) return false;
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo defaults = dynamicLights.GetField("_lights_default", flags);
                FieldInfo grounds = dynamicLights.GetField("_lights_ground", flags);
                if (defaults == null || grounds == null) return false;
                _nativeDefaultLights = defaults.GetValue(null) as List<Light>;
                _nativeGroundLights = grounds.GetValue(null) as List<Light>;
                return _nativeDefaultLights != null && _nativeGroundLights != null;
            }
            catch
            {
                _nativeDefaultLights = null;
                _nativeGroundLights = null;
                return false;
            }
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

        private static bool IsDungeonPracticalCandidate(Light light)
        {
            if (light == null || light.gameObject == null) return false;
            if (!light.gameObject.scene.IsValid() || !light.gameObject.scene.isLoaded) return false;
            if (!light.gameObject.activeInHierarchy || !light.enabled) return false;
            if (light.range < 20f || light.range > 700f) return false;

            string path = UnifiedLightingPlugin.PathOf(light.transform);
            if (path.IndexOf("Player(Clone)", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            string n = light.name ?? string.Empty;
            bool conventionalName = n.IndexOf("Point light", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                    n.IndexOf("ground light", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!conventionalName) return false;

            Component[] components = light.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component c = components[i];
                if (c == null) continue;
                string typeName = c.GetType().Name;
                if (string.Equals(typeName, "DynamicLight", StringComparison.Ordinal) ||
                    string.Equals(typeName, "GroundLight", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void RestoreAll()
        {
            foreach (KeyValuePair<Light, float> pair in _originalRanges)
            {
                if (pair.Key != null)
                    pair.Key.range = pair.Value;
            }
            _originalRanges.Clear();
            _scanSeen.Clear();
            _nativeDefaultLights = null;
            _nativeGroundLights = null;
            _nextNativeResolve = 0f;
            _nextFallbackScan = 0f;
        }

        private void OnDestroy()
        {
            RestoreAll();
        }
    }
}
