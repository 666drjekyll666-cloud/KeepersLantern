using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx;
using UnityEngine;

namespace KeepersLantern
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("nikich.gyk.keeperslantern", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(32000)]
    public sealed class LanternShadowBoostOptimizedPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "nikich.gyk.keeperslantern.shadowboost";
        private const string PluginName = "Keeper's Lantern - Dynamic Shadows";
        private const string PluginVersion = "1.0.10";

        private const float ShadowAlphaMultiplier = 3.4f;
        private const float KeeperLightMatchRadius = 1.0f;
        private const float NearbyShadowRadius = 480f;
        private const float EvaluationSeconds = 0.10f;
        private const float NightActiveThreshold = 0.0005f;

        private sealed class ShadowEntry
        {
            public Component Parent;
            public readonly Component[] SlotChildren = new Component[3];
            public readonly float[] OriginalAlpha = new float[3];
        }


        private Type _shadowType;
        private Type _shadowChildType;
        private MethodInfo _getLight;
        private FieldInfo _shadowN;
        private FieldInfo _shadowAlpha;
        private FieldInfo _nativeShadowsRegistry;
        private Transform _keeperPointTransform;
        private Component _keeperDynamicLight;
        private FieldInfo _dynamicPos;

        private readonly List<ShadowEntry> _nearby = new List<ShadowEntry>(96);
        private readonly Dictionary<Component, ShadowEntry> _liveEntries = new Dictionary<Component, ShadowEntry>();
        private readonly List<Component> _liveEntryPruneBuffer = new List<Component>(32);
        private float _nextLiveEntryPrune;
        private readonly Dictionary<Component, float> _boostedOriginals = new Dictionary<Component, float>();
        private readonly HashSet<Component> _seenBoosted = new HashSet<Component>();
        private readonly List<Component> _restoreBuffer = new List<Component>(32);
        private readonly object[] _getLightArgs = new object[1];


        private float _nextResolve;
        private float _nextEvaluation;
        private int _lastNearby;
        private int _lastBoosted;
        private int _lastFallbackCalls;
        private float _lastEvalMs;
        private float _maxEvalMs;
        private double _evalTotalMs;
        private long _evalCount;
        private float _lastAppliedFade = -1f;

        private void Awake()
        {
            ResolveTypes();
            Logger.LogInfo("Dynamic shadows 1.0.10 loaded. Live DynamicLights.shadows registry only; obsolete snapshot-cache code removed.");
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextEvaluation) return;
            _nextEvaluation = Time.unscaledTime + EvaluationSeconds;

            if (!UnifiedLightingPlugin.SharedLightingSnapshotValid) return;
            LightingSnapshot21 state = UnifiedLightingPlugin.SharedLightingSnapshot;

            float lanternFactor = Mathf.Clamp01(UnifiedLightingPlugin.SharedLanternFactor);
            bool active = lanternFactor > NightActiveThreshold;
            if (!active)
            {
                RestoreAllBoosted();
                _lastNearby = 0;
                _lastBoosted = 0;
                _lastFallbackCalls = 0;
                return;
            }

            if (!EnsureKeeperLight()) return;
            if (_shadowType == null || _shadowChildType == null || _getLight == null || _shadowAlpha == null || _nativeShadowsRegistry == null)
            {
                if (Time.unscaledTime >= _nextResolve)
                {
                    _nextResolve = Time.unscaledTime + 2f;
                    ResolveTypes();
                }
                return;
            }

            Vector2 keeperLightPos;
            if (!TryGetKeeperLightPos(out keeperLightPos)) return;

            long start = Stopwatch.GetTimestamp();
            RefreshNearbyFromNativeRegistry(keeperLightPos);
            ApplyNativeAlphaBoost(keeperLightPos, lanternFactor);
            _lastEvalMs = (float)((Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency);
            if (_lastEvalMs > _maxEvalMs) _maxEvalMs = _lastEvalMs;
            _evalTotalMs += _lastEvalMs;
            _evalCount++;
        }

        private void ApplyNativeAlphaBoost(Vector2 keeperLightPos, float lanternFactor)
        {
            _seenBoosted.Clear();
            float matchRadiusSqr = KeeperLightMatchRadius * KeeperLightMatchRadius;
            bool fadeChanged = Mathf.Abs(lanternFactor - _lastAppliedFade) > 0.005f;
            int boosted = 0;
            int fallbackCalls = 0;

            for (int i = 0; i < _nearby.Count; i++)
            {
                ShadowEntry entry = _nearby[i];
                if (entry == null || entry.Parent == null || entry.Parent.gameObject == null) continue;
                if (!entry.Parent.gameObject.activeInHierarchy) continue;

                for (int slot = 0; slot < 3; slot++)
                {
                    Component child = entry.SlotChildren[slot];
                    if (child == null) continue;

                    Vector2 selected;
                    if (!TryGetSelectedLight(entry, slot, out selected)) continue;
                    fallbackCalls++;
                    if ((selected - keeperLightPos).sqrMagnitude > matchRadiusSqr) continue;

                    float original = entry.OriginalAlpha[slot];
                    bool newlyBoosted = !_boostedOriginals.ContainsKey(child);
                    if (newlyBoosted)
                        _boostedOriginals[child] = original;

                    if (newlyBoosted || fadeChanged)
                    {
                        float fullBoostAlpha = Mathf.Clamp01(original * ShadowAlphaMultiplier);
                        float fadedAlpha = Mathf.Lerp(original, fullBoostAlpha, lanternFactor);
                        SafeWriteFloat(_shadowAlpha, child, fadedAlpha);
                    }

                    _seenBoosted.Add(child);
                    boosted++;
                }
            }

            _restoreBuffer.Clear();
            foreach (KeyValuePair<Component, float> pair in _boostedOriginals)
            {
                if (pair.Key == null || !_seenBoosted.Contains(pair.Key))
                    _restoreBuffer.Add(pair.Key);
            }

            for (int i = 0; i < _restoreBuffer.Count; i++)
            {
                Component child = _restoreBuffer[i];
                float original;
                if (child != null && _boostedOriginals.TryGetValue(child, out original))
                    SafeWriteFloat(_shadowAlpha, child, original);
                _boostedOriginals.Remove(child);
            }

            _lastBoosted = boosted;
            _lastFallbackCalls = fallbackCalls;
            _lastAppliedFade = lanternFactor;
        }

        private void RestoreAllBoosted()
        {
            if (_boostedOriginals.Count == 0) return;
            foreach (KeyValuePair<Component, float> pair in _boostedOriginals)
            {
                if (pair.Key != null)
                    SafeWriteFloat(_shadowAlpha, pair.Key, pair.Value);
            }
            _boostedOriginals.Clear();
            _seenBoosted.Clear();
            _lastAppliedFade = -1f;
        }

        private void ResolveTypes()
        {
            _shadowType = FindType("ObjectDynamicShadow");
            _shadowChildType = FindType("ObjectDynamicShadowChild");
            Type dynamicLightsType = FindType("DynamicLights");
            if (dynamicLightsType != null)
                _nativeShadowsRegistry = dynamicLightsType.GetField("shadows", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (_shadowType != null)
                _getLight = _shadowType.GetMethod("GetLight", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (_shadowChildType != null)
            {
                _shadowN = _shadowChildType.GetField("shadow_n", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                _shadowAlpha = _shadowChildType.GetField("shadow_alpha", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
        }

        private bool EnsureKeeperLight()
        {
            if (_keeperPointTransform != null && _keeperDynamicLight != null) return true;
            GameObject player = GameObject.Find("World/Player(Clone)");
            if (player == null) return false;
            Transform point = player.transform.Find("content/character/char_hero/Char light/Point light");
            if (point == null) return false;

            Component dyn = null;
            foreach (Component c in point.GetComponents<Component>())
            {
                if (c != null && string.Equals(c.GetType().Name, "DynamicLight", StringComparison.Ordinal))
                {
                    dyn = c;
                    break;
                }
            }
            if (dyn == null) return false;

            _keeperPointTransform = point;
            _keeperDynamicLight = dyn;
            _dynamicPos = dyn.GetType().GetField("pos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Logger.LogInfo("Keeper DynamicLight bound for shadow-source matching.");
            return true;
        }

        private bool TryGetKeeperLightPos(out Vector2 pos)
        {
            pos = Vector2.zero;
            if (_keeperDynamicLight == null || _keeperPointTransform == null) return false;
            object raw = SafeRead(_dynamicPos, _keeperDynamicLight);
            if (raw is Vector3)
            {
                Vector3 v = (Vector3)raw;
                pos = new Vector2(v.x, v.y);
                return true;
            }
            Vector3 wp = _keeperPointTransform.position;
            pos = new Vector2(wp.x, wp.y);
            return true;
        }

        private void RefreshNearbyFromNativeRegistry(Vector2 keeperLightPos)
        {
            _nearby.Clear();
            PruneDestroyedLiveEntries();
            if (_nativeShadowsRegistry == null)
            {
                _lastNearby = 0;
                return;
            }

            float radiusSqr = NearbyShadowRadius * NearbyShadowRadius;
            try
            {
                object raw = _nativeShadowsRegistry.GetValue(null);
                System.Collections.IEnumerable enumerable = raw as System.Collections.IEnumerable;
                if (enumerable == null)
                {
                    _lastNearby = 0;
                    return;
                }

                foreach (object item in enumerable)
                {
                    Component parent = item as Component;
                    if (parent == null || parent.gameObject == null || !parent.gameObject.activeInHierarchy) continue;

                    Vector3 wp = parent.transform.position;
                    float dx = wp.x - keeperLightPos.x;
                    float dy = wp.y - keeperLightPos.y;
                    if (dx * dx + dy * dy > radiusSqr) continue;

                    ShadowEntry entry;
                    if (!_liveEntries.TryGetValue(parent, out entry) || entry == null)
                    {
                        entry = BuildLiveEntry(parent);
                        if (entry != null) _liveEntries[parent] = entry;
                    }
                    if (entry != null) _nearby.Add(entry);
                }
            }
            catch { }

            _lastNearby = _nearby.Count;
        }

        private void PruneDestroyedLiveEntries()
        {
            if (Time.unscaledTime < _nextLiveEntryPrune) return;
            _nextLiveEntryPrune = Time.unscaledTime + 5f;
            if (_liveEntries.Count == 0) return;

            _liveEntryPruneBuffer.Clear();
            foreach (KeyValuePair<Component, ShadowEntry> pair in _liveEntries)
            {
                if (pair.Key == null)
                    _liveEntryPruneBuffer.Add(pair.Key);
            }
            for (int i = 0; i < _liveEntryPruneBuffer.Count; i++)
                _liveEntries.Remove(_liveEntryPruneBuffer[i]);
        }

        private ShadowEntry BuildLiveEntry(Component parent)
        {
            if (parent == null || _shadowChildType == null) return null;

            ShadowEntry entry = new ShadowEntry
            {
                Parent = parent
            };

            int validSlots = 0;
            try
            {
                Component[] children = parent.GetComponentsInChildren(_shadowChildType, true);
                for (int c = 0; c < children.Length; c++)
                {
                    Component child = children[c];
                    if (child == null) continue;
                    object rawN = SafeRead(_shadowN, child);
                    if (!(rawN is int)) continue;
                    int shadowN = (int)rawN;
                    if (shadowN < 1 || shadowN > 3) continue;

                    int slot = shadowN - 1;
                    if (entry.SlotChildren[slot] == null) validSlots++;
                    entry.SlotChildren[slot] = child;
                    object rawAlpha = SafeRead(_shadowAlpha, child);
                    entry.OriginalAlpha[slot] = rawAlpha is float ? (float)rawAlpha : 1f;
                }
            }
            catch { }

            return validSlots > 0 ? entry : null;
        }

        private bool TryGetSelectedLight(ShadowEntry entry, int slot, out Vector2 pos)
        {
            pos = Vector2.zero;
            if (entry == null || entry.Parent == null || _getLight == null) return false;
            try
            {
                _getLightArgs[0] = slot;
                object raw = _getLight.Invoke(entry.Parent, _getLightArgs);
                if (!(raw is Vector2)) return false;
                pos = (Vector2)raw;
                return true;
            }
            catch { return false; }
        }

        private static object SafeRead(FieldInfo field, object instance)
        {
            if (field == null || instance == null) return null;
            try { return field.GetValue(instance); }
            catch { return null; }
        }

        private static void SafeWriteFloat(FieldInfo field, object instance, float value)
        {
            if (field == null || instance == null) return;
            try { field.SetValue(instance, value); }
            catch { }
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

        private void Update()
        {
            if (!Input.GetKeyDown(KeyCode.F10)) return;
            Vector2 pos;
            bool hasPos = TryGetKeeperLightPos(out pos);
            float sharedFade = Mathf.Clamp01(UnifiedLightingPlugin.SharedLanternFactor);
            Logger.LogInfo("F10 SHADOW BOOST | v=1.0.10 active=" + (sharedFade > NightActiveThreshold) +
                " fade=" + sharedFade.ToString("0.00") +
                " source=DynamicLights.shadows-live" +
                " liveEntries=" + _liveEntries.Count +
                " nearby=" + _lastNearby +
                " boosted=" + _lastBoosted +
                " reflectionCalls=" + _lastFallbackCalls +
                " evalMs=" + _lastEvalMs.ToString("0.000") +
                " avgEvalMs=" + (_evalCount > 0 ? (_evalTotalMs / _evalCount).ToString("0.000") : "0.000") +
                " maxEvalMs=" + _maxEvalMs.ToString("0.000") +
                " keeperLight=" + (hasPos ? pos.ToString("F2") : "?") + ".");
        }

        private void OnDestroy()
        {
            RestoreAllBoosted();
            _liveEntries.Clear();
            _liveEntryPruneBuffer.Clear();
        }
    }
}
