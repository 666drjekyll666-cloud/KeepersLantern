using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace KeepersLantern
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("nikich.gyk.keeperslantern", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(36000)]
    public sealed class SaveNowEnvironmentCompatibilityPlugin : BaseUnityPlugin
    {
        private const string PluginGuid = "nikich.gyk.keeperslantern.savenowcompat";
        private const string PluginName = "Keeper's Lantern - Save Now Compatibility";
        private const string PluginVersion = "1.0.12";
        private const string SaveNowGuid = "p1xel8ted.gyk.savenow";
        private const string HarmonyId = "nikich.gyk.keeperslantern.savenowcompat.harmony";

        private static SaveNowEnvironmentCompatibilityPlugin _instance;

        private bool _saveNowPresent;
        private bool _hookInstalled;
        private bool _refreshPending;
        private int _refreshAfterFrame = -1;

        private object _harmonyInstance;
        private MethodBase _restoreLocationMethod;
        private MethodInfo _postfixMethod;

        private Type _environmentType;
        private MemberInfo _environmentMe;
        private MemberInfo _currentPreset;
        private MethodInfo _applyEnvironmentPreset;

        private void Awake()
        {
            _instance = this;
            Logger.LogInfo("Save Now compatibility 1.0.12 loaded. It remains idle unless Save Now is present.");
        }

        private void Start()
        {
            // BepInEx completes all plugin Awake calls before Unity reaches Start. The
            // 1.0.10/1.0.11 startup-order failure came from querying Save Now in our
            // earlier Awake; resolving here sees the completed Chainloader registry.
            try { _saveNowPresent = Chainloader.PluginInfos.ContainsKey(SaveNowGuid); }
            catch { _saveNowPresent = false; }

            if (!_saveNowPresent) return;

            if (TryInstallPostRestoreHook())
            {
                _hookInstalled = true;
                Logger.LogInfo("Save Now compatibility hook installed on SaveNow.Plugin.RestoreLocation.");
            }
            else
            {
                Logger.LogWarning("Save Now detected, but its RestoreLocation compatibility hook could not be installed. No environment state will be changed.");
            }
        }

        private void Update()
        {
            if (!_hookInstalled || !_refreshPending || Time.frameCount < _refreshAfterFrame) return;

            // Save Now's verified RestoreLocation method ends by calling
            // MainGame.me.player.PlaceAtPos(savedPosition). The Harmony postfix below only
            // schedules work; waiting until the next Unity frame guarantees PlaceAtPos and
            // the full RestoreLocation call stack have returned before touching lighting.
            _refreshPending = false;

            if (!UnifiedLightingPlugin.SharedLightingSnapshotValid)
            {
                Logger.LogWarning("Save Now compatibility reached the post-restore frame before Keeper lighting state was available; leaving game state untouched.");
                return;
            }

            LightingSnapshot21 state = UnifiedLightingPlugin.SharedLightingSnapshot;
            if (state.DungeonKnown && state.IsDungeon) return;
            if (!state.IndoorKnown || !state.IsIndoor || string.IsNullOrEmpty(state.PresetName)) return;
            if (state.PresetName.StartsWith("dungeon_", StringComparison.OrdinalIgnoreCase)) return;

            string refreshedPreset;
            if (TryReapplyCurrentPreset(out refreshedPreset))
            {
                Logger.LogInfo("Save Now compatibility: reapplied current vanilla environment preset after Save Now location restore | preset=" +
                    (string.IsNullOrEmpty(refreshedPreset) ? state.PresetName : refreshedPreset) + ".");
            }
            else
            {
                Logger.LogWarning("Save Now compatibility: post-restore interior was detected, but vanilla ApplyEnvironmentPreset could not be invoked; leaving game state untouched.");
            }
        }

        private bool TryInstallPostRestoreHook()
        {
            try
            {
                var saveNowInfo = Chainloader.PluginInfos[SaveNowGuid];
                if (saveNowInfo == null || saveNowInfo.Instance == null) return false;

                Type saveNowType = saveNowInfo.Instance.GetType();
                _restoreLocationMethod = saveNowType.GetMethod(
                    "RestoreLocation",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                if (_restoreLocationMethod == null) return false;

                Type harmonyType = FindRuntimeType("HarmonyLib.Harmony", "Harmony");
                Type harmonyMethodType = FindRuntimeType("HarmonyLib.HarmonyMethod", "HarmonyMethod");
                if (harmonyType == null || harmonyMethodType == null) return false;

                _postfixMethod = typeof(SaveNowEnvironmentCompatibilityPlugin).GetMethod(
                    "SaveNowRestoreLocationPostfix",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (_postfixMethod == null) return false;

                _harmonyInstance = Activator.CreateInstance(harmonyType, new object[] { HarmonyId });
                if (_harmonyInstance == null) return false;

                object harmonyPostfix;
                ConstructorInfo methodCtor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
                if (methodCtor != null)
                {
                    harmonyPostfix = methodCtor.Invoke(new object[] { _postfixMethod });
                }
                else
                {
                    harmonyPostfix = Activator.CreateInstance(harmonyMethodType);
                    FieldInfo methodField = harmonyMethodType.GetField("method", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (harmonyPostfix == null || methodField == null) return false;
                    methodField.SetValue(harmonyPostfix, _postfixMethod);
                }

                MethodInfo patchMethod = harmonyType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => string.Equals(m.Name, "Patch", StringComparison.Ordinal))
                    .Where(m =>
                    {
                        ParameterInfo[] p = m.GetParameters();
                        return p.Length >= 3 &&
                               p[0].ParameterType == typeof(MethodBase) &&
                               p[2].ParameterType == harmonyMethodType;
                    })
                    .OrderByDescending(m => m.GetParameters().Length)
                    .FirstOrDefault();
                if (patchMethod == null) return false;

                ParameterInfo[] parameters = patchMethod.GetParameters();
                object[] args = new object[parameters.Length];
                args[0] = _restoreLocationMethod;
                args[2] = harmonyPostfix;
                patchMethod.Invoke(_harmonyInstance, args);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogWarning("Save Now compatibility hook install failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private static void SaveNowRestoreLocationPostfix()
        {
            SaveNowEnvironmentCompatibilityPlugin instance = _instance;
            if (instance == null || !instance._hookInstalled || !instance._saveNowPresent) return;

            instance._refreshPending = true;
            instance._refreshAfterFrame = Time.frameCount + 1;
            instance.Logger.LogInfo("Save Now compatibility: Save Now location restore completed; environment refresh scheduled for the next frame.");
        }

        private bool TryReapplyCurrentPreset(out string name)
        {
            name = null;
            if (!ResolveEnvironmentEngine()) return false;

            try
            {
                object engine = ReadMember(null, _environmentMe);
                object preset = ReadMember(null, _currentPreset);
                if (UnityNull(engine) || UnityNull(preset)) return false;

                ParameterInfo[] parameters = _applyEnvironmentPreset.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsAssignableFrom(preset.GetType()))
                    return false;

                UnityEngine.Object unityPreset = preset as UnityEngine.Object;
                if (unityPreset != null)
                {
                    name = unityPreset.name;
                }
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

        private bool ResolveEnvironmentEngine()
        {
            if (_environmentType != null && _environmentMe != null && _currentPreset != null && _applyEnvironmentPreset != null)
                return true;

            _environmentType = FindRuntimeType("EnvironmentEngine", "EnvironmentEngine");
            if (_environmentType == null) return false;

            _environmentMe = FindMember(_environmentType, "me", true);
            _currentPreset = FindMember(_environmentType, "cur_preset", true);
            _applyEnvironmentPreset = _environmentType
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => string.Equals(m.Name, "ApplyEnvironmentPreset", StringComparison.Ordinal) &&
                    m.GetParameters().Length == 1);

            return _environmentMe != null && _currentPreset != null && _applyEnvironmentPreset != null;
        }

        private static Type FindRuntimeType(string fullName, string simpleName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type direct = assembly.GetType(fullName, false);
                    if (direct != null) return direct;

                    Type[] types = assembly.GetTypes();
                    for (int i = 0; i < types.Length; i++)
                        if (types[i] != null && string.Equals(types[i].Name, simpleName, StringComparison.Ordinal)) return types[i];
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Type[] types = ex.Types;
                    if (types == null) continue;
                    for (int i = 0; i < types.Length; i++)
                        if (types[i] != null && string.Equals(types[i].Name, simpleName, StringComparison.Ordinal)) return types[i];
                }
                catch { }
            }
            return null;
        }

        private static MemberInfo FindMember(Type type, string name, bool isStatic)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
            FieldInfo field = type.GetField(name, flags);
            if (field != null) return field;
            return type.GetProperty(name, flags);
        }

        private static object ReadNamed(object instance, string name)
        {
            if (instance == null) return null;
            MemberInfo member = FindMember(instance.GetType(), name, false);
            return member == null ? null : ReadMember(instance, member);
        }

        private static object ReadMember(object instance, MemberInfo member)
        {
            FieldInfo field = member as FieldInfo;
            if (field != null) return field.GetValue(instance);
            PropertyInfo property = member as PropertyInfo;
            return property == null ? null : property.GetValue(instance, null);
        }

        private static bool UnityNull(object value)
        {
            if (value == null) return true;
            UnityEngine.Object unityObject = value as UnityEngine.Object;
            return unityObject != null && unityObject == null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;

            if (_harmonyInstance == null || _restoreLocationMethod == null || _postfixMethod == null) return;
            try
            {
                MethodInfo unpatch = _harmonyInstance.GetType().GetMethod(
                    "Unpatch",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(MethodBase), typeof(MethodInfo) },
                    null);
                if (unpatch != null)
                    unpatch.Invoke(_harmonyInstance, new object[] { _restoreLocationMethod, _postfixMethod });
            }
            catch { }
        }
    }
}
