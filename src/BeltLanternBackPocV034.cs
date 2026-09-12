using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace KeepersLantern
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("nikich.gyk.keeperslantern", BepInDependency.DependencyFlags.HardDependency)]
    [DefaultExecutionOrder(20000)]
    public sealed class BeltLanternBackPocV034Plugin : BaseUnityPlugin
    {
        private const string PluginGuid = "nikich.gyk.keeperslantern.beltlanternpoc";
        private const string PluginName = "Keeper's Lantern - Belt Visual";
        private const string PluginVersion = "1.0.10";

        private static readonly Vector2[] BackAnchors =
        {
            new Vector2(-4f, 0f),
            new Vector2(-5f, -2f),
            new Vector2(-5f, -4f),
            new Vector2(5f, -2f)
        };

        private Transform _charHero;
        private SpriteRenderer _directionRenderer;
        private SpriteRenderer[] _heroRenderers;
        private int _rendererHierarchyScanCount;
        private int _rendererReselectCount;

        private GameObject _lanternObject;
        private SpriteRenderer _lanternRenderer;
        private SpriteRenderer _lanternGlowRenderer;
        private int _cachedSortingLayerId;
        private int _cachedSortingOrder;
        private bool _sortingCached;
        private int _sortingScanCount;
        private Texture2D _litTexture;
        private Texture2D _darkTexture;
        private Sprite _litSprite;
        private Sprite _darkSprite;
        private Material _material;

        private bool _bound;
        private float _nextBind;
        private int _anchorPreset = 1;
        private ConfigEntry<float> _visualOffsetXPx;
        private ConfigEntry<float> _visualOffsetYPx;
        private Facing34 _lastFacing = Facing34.Down;
        private float _localUnitsPerPixelX = 1f;
        private float _localUnitsPerPixelY = 1f;
        private Vector3 _anchorLocal;
        private Vector3 _anchorWorld;
        private Vector3 _lastHeroWorld;
        private bool _haveLastHeroWorld;
        private bool _moving;
        private bool _lit;

        // The lateral motion is driven by actual sprite-frame changes, not elapsed time.
        // This prevents the lantern motion from drifting out of phase with the walk animation.
        private string _lastMotionSpriteName;
        private string _cycleStartSpriteName;
        private int _cycleFrameIndex;
        private int _framesSinceCycleStart;
        private int _observedCycleLength;
        private float _frameFollowXPx;

        private void Awake()
        {
            _visualOffsetXPx = Config.Bind(
                "Belt Visual",
                "Position X (pixels)",
                1.5f,
                new ConfigDescription(
                    "Live horizontal offset of the belt-lantern sprite. Positive values move right; negative values move left. This changes only the visual sprite, not the light source or shadows.",
                    new AcceptableValueRange<float>(-20f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true, DispName = "Lantern Position X" }));
            _visualOffsetYPx = Config.Bind(
                "Belt Visual",
                "Position Y (pixels)",
                -2.8f,
                new ConfigDescription(
                    "Live vertical offset of the belt-lantern sprite. Positive values move up; negative values move down. This changes only the visual sprite, not the light source or shadows.",
                    new AcceptableValueRange<float>(-20f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true, DispName = "Lantern Position Y" }));

            Logger.LogInfo("Keeper belt lantern visual 1.0.10 loaded.");
            Logger.LogInfo("Visual policy: lantern is mounted on the rear belt and is visible only when the Keeper faces away from the camera.");
            Logger.LogInfo("The physical lantern remains present when its gameplay light is off: dark-glass sprite by day/indoors, lit-glass sprite outdoors at night/in dungeon.");
            Logger.LogInfo("Accepted synchronized belt sway to actual body animation-frame changes; there is no free-running sine animation.");
            Logger.LogInfo("F1 Configuration Manager exposes live belt-sprite Position X/Y pixel offsets; F7 still cycles legacy anchor candidates; F10 dumps visual diagnostics.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F7))
            {
                _anchorPreset = (_anchorPreset + 1) % BackAnchors.Length;
                Vector2 p = BackAnchors[_anchorPreset];
                Logger.LogInfo("F7 BACK BELT ANCHOR #" + (_anchorPreset + 1) + "/" + BackAnchors.Length + " = (" + p.x + "," + p.y + ") px");
            }

            if (Input.GetKeyDown(KeyCode.F10)) DumpState();
        }

        private void LateUpdate()
        {
            if (!_bound)
            {
                if (Time.unscaledTime < _nextBind) return;
                _nextBind = Time.unscaledTime + 0.5f;
                TryBind();
                if (!_bound) return;
            }

            if (_charHero == null || _lanternRenderer == null)
            {
                ForgetBinding();
                return;
            }

            UpdateDirectionRenderer();
            CalibratePixelScale();
            Facing34 facing = DetermineFacing();
            UpdateMovementState();
            ApplyBackBeltAnchor(facing);

            float glowFactor = Mathf.Clamp01(UnifiedLightingPlugin.SharedLanternFactor * 3f);
            _lit = glowFactor > 0.001f;

            bool visible = facing == Facing34.Up;
            _lanternRenderer.enabled = visible;
            _lanternRenderer.sprite = _darkSprite;
            _lanternRenderer.color = Color.white;

            if (_lanternGlowRenderer != null)
            {
                _lanternGlowRenderer.enabled = visible && _lit;
                _lanternGlowRenderer.sprite = _litSprite;
                _lanternGlowRenderer.color = new Color(1f, 1f, 1f, glowFactor);
            }
        }

        private void TryBind()
        {
            GameObject player = GameObject.Find("World/Player(Clone)");
            if (player == null) return;

            _charHero = player.transform.Find("content/character/char_hero");
            if (_charHero == null) return;

            CacheHeroRenderers();
            UpdateDirectionRenderer();
            CreateLanternVisual();
            CacheLanternSorting();
            CalibratePixelScale();

            _bound = _lanternRenderer != null;
            _haveLastHeroWorld = false;
            ResetWalkCycle();
            if (_bound)
            {
                Logger.LogInfo("Keeper belt lantern 1.0.10 bound to " + UnifiedLightingPlugin.PathOf(_charHero) + ".");
                DumpState();
            }
        }

        private void ForgetBinding()
        {
            _bound = false;
            _charHero = null;
            _directionRenderer = null;
            _heroRenderers = null;
            _sortingCached = false;
            _haveLastHeroWorld = false;
            ResetWalkCycle();
            DestroyVisual();
        }

        private void CacheHeroRenderers()
        {
            if (_charHero == null)
            {
                _heroRenderers = null;
                return;
            }

            // Full hierarchy discovery is intentionally bind-only. Crafting animations can
            // toggle several existing SpriteRenderer components on/off every frame; doing
            // GetComponentsInChildren + LINQ sorting whenever the previous renderer becomes
            // disabled is extremely expensive and caused the workbench FPS collapse.
            _heroRenderers = _charHero.GetComponentsInChildren<SpriteRenderer>(true);
            _rendererHierarchyScanCount++;
        }

        private static bool IsDirectionCandidate(SpriteRenderer r, SpriteRenderer lantern, SpriteRenderer glow)
        {
            if (r == null || r == lantern || r == glow || !r.enabled || r.sprite == null) return false;
            string n = r.sprite.name ?? string.Empty;
            return n.IndexOf("shadow", StringComparison.OrdinalIgnoreCase) < 0 &&
                   n.IndexOf("_sh_", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private void UpdateDirectionRenderer()
        {
            if (_charHero == null) return;
            if (IsDirectionCandidate(_directionRenderer, _lanternRenderer, _lanternGlowRenderer)) return;

            if (_heroRenderers == null || _heroRenderers.Length == 0) CacheHeroRenderers();
            if (_heroRenderers == null) return;

            SpriteRenderer best = null;
            bool bestMid = false;
            float bestArea = -1f;

            // Cheap allocation-free selection from the bind-time cache. All animation
            // renderers are normally already present even when inactive, so crafting only
            // changes which cached entry is enabled. Use sprite pixel area instead of world
            // bounds to avoid renderer-bound calculations while selecting.
            for (int i = 0; i < _heroRenderers.Length; i++)
            {
                SpriteRenderer r = _heroRenderers[i];
                if (!IsDirectionCandidate(r, _lanternRenderer, _lanternGlowRenderer)) continue;

                string n = r.sprite.name ?? string.Empty;
                bool mid = n.IndexOf("_mid_", StringComparison.OrdinalIgnoreCase) >= 0;
                Rect rect = r.sprite.rect;
                float area = rect.width * rect.height;
                if (best == null || (mid && !bestMid) || (mid == bestMid && area > bestArea))
                {
                    best = r;
                    bestMid = mid;
                    bestArea = area;
                }
            }

            if (best != null && best != _directionRenderer)
            {
                _directionRenderer = best;
                _rendererReselectCount++;
            }
        }

        private void CalibratePixelScale()
        {
            if (_charHero == null || _directionRenderer == null || _directionRenderer.sprite == null || _lanternObject == null) return;

            Sprite source = _directionRenderer.sprite;
            float pxW = Mathf.Max(1f, source.rect.width);
            float pxH = Mathf.Max(1f, source.rect.height);
            float worldPerPxX = Mathf.Abs(_directionRenderer.bounds.size.x) / pxW;
            float worldPerPxY = Mathf.Abs(_directionRenderer.bounds.size.y) / pxH;
            float parentScaleX = Mathf.Max(0.0001f, Mathf.Abs(_charHero.lossyScale.x));
            float parentScaleY = Mathf.Max(0.0001f, Mathf.Abs(_charHero.lossyScale.y));

            _localUnitsPerPixelX = worldPerPxX / parentScaleX;
            _localUnitsPerPixelY = worldPerPxY / parentScaleY;
            _lanternObject.transform.localScale = new Vector3(_localUnitsPerPixelX, _localUnitsPerPixelY, 1f);
        }

        private Facing34 DetermineFacing()
        {
            if (_directionRenderer == null || _directionRenderer.sprite == null) return _lastFacing;

            string spriteName = _directionRenderer.sprite.name ?? string.Empty;
            Facing34 result = _lastFacing;

            if (spriteName.IndexOf("_up", StringComparison.OrdinalIgnoreCase) >= 0 || spriteName.IndexOf("up_", StringComparison.OrdinalIgnoreCase) >= 0 || spriteName.IndexOf("_u_", StringComparison.OrdinalIgnoreCase) >= 0) result = Facing34.Up;
            else if (spriteName.IndexOf("_down", StringComparison.OrdinalIgnoreCase) >= 0 || spriteName.IndexOf("down_", StringComparison.OrdinalIgnoreCase) >= 0 || spriteName.IndexOf("_d_", StringComparison.OrdinalIgnoreCase) >= 0) result = Facing34.Down;
            else if (spriteName.IndexOf("_right", StringComparison.OrdinalIgnoreCase) >= 0 || spriteName.IndexOf("right_", StringComparison.OrdinalIgnoreCase) >= 0 || spriteName.IndexOf("_r_", StringComparison.OrdinalIgnoreCase) >= 0) result = Facing34.Right;
            else if (spriteName.IndexOf("_left", StringComparison.OrdinalIgnoreCase) >= 0 || spriteName.IndexOf("left_", StringComparison.OrdinalIgnoreCase) >= 0 || spriteName.IndexOf("_l_", StringComparison.OrdinalIgnoreCase) >= 0) result = _directionRenderer.flipX ? Facing34.Right : Facing34.Left;

            _lastFacing = result;
            return result;
        }

        private void UpdateMovementState()
        {
            if (_charHero == null)
            {
                _moving = false;
                return;
            }

            Vector3 now = _charHero.position;
            if (!_haveLastHeroWorld)
            {
                _lastHeroWorld = now;
                _haveLastHeroWorld = true;
                _moving = false;
                return;
            }

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
            float speed = (now - _lastHeroWorld).magnitude / dt;
            _lastHeroWorld = now;
            _moving = speed > 1f;
        }

        private float GetFrameSyncedLateralOffset(Facing34 facing)
        {
            if (facing != Facing34.Up || !_moving || _directionRenderer == null || _directionRenderer.sprite == null)
            {
                ResetWalkCycle();
                return 0f;
            }

            string spriteName = _directionRenderer.sprite.name ?? string.Empty;
            if (!string.Equals(spriteName, _lastMotionSpriteName, StringComparison.Ordinal))
            {
                _lastMotionSpriteName = spriteName;

                if (string.IsNullOrEmpty(_cycleStartSpriteName))
                {
                    _cycleStartSpriteName = spriteName;
                    _cycleFrameIndex = 0;
                    _framesSinceCycleStart = 0;
                }
                else if (string.Equals(spriteName, _cycleStartSpriteName, StringComparison.Ordinal) && _framesSinceCycleStart >= 2)
                {
                    // The sprite sequence has returned to its first frame: we now know the
                    // real frame count of this walk cycle. From here the phase cannot drift.
                    _observedCycleLength = Mathf.Max(3, _framesSinceCycleStart + 1);
                    _cycleFrameIndex = 0;
                    _framesSinceCycleStart = 0;
                }
                else
                {
                    _framesSinceCycleStart++;
                    _cycleFrameIndex++;
                    if (_observedCycleLength > 0)
                        _cycleFrameIndex %= _observedCycleLength;
                }
            }

            int cycleLength = _observedCycleLength > 0 ? _observedCycleLength : 4;
            float phase = (Mathf.PI * 2f * _cycleFrameIndex) / Mathf.Max(1, cycleLength);

            // About three pixels peak-to-peak. Quantising to half-pixels keeps the motion
            // crisp enough for the game's pixel-art presentation while remaining visible.
            float raw = -Mathf.Sin(phase);
            _frameFollowXPx = Mathf.Abs(raw) < 0.25f ? 0f : (raw < 0f ? -1f : 1f);
            return _frameFollowXPx;
        }

        private void ResetWalkCycle()
        {
            _lastMotionSpriteName = null;
            _cycleStartSpriteName = null;
            _cycleFrameIndex = 0;
            _framesSinceCycleStart = 0;
            _observedCycleLength = 0;
            _frameFollowXPx = 0f;
        }

        private void ApplyBackBeltAnchor(Facing34 facing)
        {
            if (_lanternObject == null || _charHero == null) return;

            Vector2 basePx = BackAnchors[_anchorPreset];
            float followXPx = GetFrameSyncedLateralOffset(facing);
            float userXPx = _visualOffsetXPx != null ? _visualOffsetXPx.Value : 0f;
            float userYPx = _visualOffsetYPx != null ? _visualOffsetYPx.Value : 0f;

            _anchorLocal = new Vector3(
                (basePx.x + userXPx + followXPx) * _localUnitsPerPixelX,
                (basePx.y + userYPx + (followXPx < 0f ? 1f : 0f)) * _localUnitsPerPixelY,
                0f);
            _lanternObject.transform.localPosition = _anchorLocal;
            _anchorWorld = _lanternObject.transform.position;

            if (!_sortingCached) CacheLanternSorting();
        }

        private void CacheLanternSorting()
        {
            if (_charHero == null || _lanternRenderer == null) return;

            SpriteRenderer[] renderers = _charHero.GetComponentsInChildren<SpriteRenderer>(true);
            _sortingScanCount++;
            SpriteRenderer best = null;
            int bestLayer = int.MinValue;
            int bestOrder = int.MinValue;

            for (int i = 0; i < renderers.Length; i++)
            {
                SpriteRenderer r = renderers[i];
                if (r == null || r == _lanternRenderer || r == _lanternGlowRenderer) continue;
                int layer = SortingLayer.GetLayerValueFromID(r.sortingLayerID);
                if (best == null || layer > bestLayer || (layer == bestLayer && r.sortingOrder > bestOrder))
                {
                    best = r;
                    bestLayer = layer;
                    bestOrder = r.sortingOrder;
                }
            }

            if (best == null) return;
            _cachedSortingLayerId = best.sortingLayerID;
            _cachedSortingOrder = best.sortingOrder + 20;
            _sortingCached = true;

            _lanternRenderer.sortingLayerID = _cachedSortingLayerId;
            _lanternRenderer.sortingOrder = _cachedSortingOrder;
            if (_lanternGlowRenderer != null)
            {
                _lanternGlowRenderer.sortingLayerID = _cachedSortingLayerId;
                _lanternGlowRenderer.sortingOrder = _cachedSortingOrder + 1;
            }
        }

        private void CreateLanternVisual()
        {
            DestroyVisual();

            _darkTexture = BuildLanternTexture(false);
            _litTexture = BuildLanternTexture(true);
            _darkSprite = CreateSprite(_darkTexture, "KeepersLantern_Back_Dark_038");
            _litSprite = CreateSprite(_litTexture, "KeepersLantern_Back_Lit_038");

            _lanternObject = new GameObject("KeepersLantern Back Belt Sprite");
            _lanternObject.transform.SetParent(_charHero, false);
            _lanternRenderer = _lanternObject.AddComponent<SpriteRenderer>();
            _lanternRenderer.sprite = _darkSprite;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                _material = new Material(shader);
                _material.hideFlags = HideFlags.HideAndDontSave;
                _lanternRenderer.material = _material;
            }

            GameObject glowObject = new GameObject("KeepersLantern Back Belt Glow");
            glowObject.transform.SetParent(_lanternObject.transform, false);
            _lanternGlowRenderer = glowObject.AddComponent<SpriteRenderer>();
            _lanternGlowRenderer.sprite = _litSprite;
            _lanternGlowRenderer.enabled = false;
            if (_material != null) _lanternGlowRenderer.material = _material;
        }

        private static Sprite CreateSprite(Texture2D texture, string name)
        {
            Sprite s = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.68f),
                1f,
                0,
                SpriteMeshType.FullRect);
            s.name = name;
            return s;
        }

        private static Texture2D BuildLanternTexture(bool lit)
        {
            const int w = 9;
            const int h = 11;
            Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            t.name = lit ? "KeepersLantern_Back_Lit_038_Texture" : "KeepersLantern_Back_Dark_038_Texture";
            t.filterMode = FilterMode.Point;
            t.wrapMode = TextureWrapMode.Clamp;

            Color clear = new Color(0f, 0f, 0f, 0f);
            Color outline = new Color32(58, 40, 27, 255);
            Color metalDark = new Color32(102, 68, 36, 255);
            Color metal = new Color32(151, 101, 48, 255);
            Color glassDark = new Color32(65, 48, 30, 255);
            Color glassWarm = new Color32(213, 132, 43, 255);
            Color glassBright = new Color32(247, 190, 82, 255);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    t.SetPixel(x, y, clear);

            t.SetPixel(3,10,outline); t.SetPixel(4,10,outline); t.SetPixel(5,10,outline);
            t.SetPixel(2,9,outline);  t.SetPixel(6,9,outline);
            t.SetPixel(2,8,metalDark); t.SetPixel(6,8,metalDark);
            for (int x = 2; x <= 6; x++) t.SetPixel(x,7,outline);
            for (int x = 3; x <= 5; x++) t.SetPixel(x,6,metalDark);
            for (int y = 2; y <= 5; y++) { t.SetPixel(2,y,outline); t.SetPixel(6,y,outline); }
            t.SetPixel(3,1,outline); t.SetPixel(4,1,outline); t.SetPixel(5,1,outline);

            Color g1 = lit ? glassWarm : glassDark;
            Color g2 = lit ? glassBright : (Color)new Color32(79, 55, 31, 255);
            t.SetPixel(3,5,metal); t.SetPixel(4,5,g1); t.SetPixel(5,5,metal);
            t.SetPixel(3,4,g1); t.SetPixel(4,4,g2); t.SetPixel(5,4,g1);
            t.SetPixel(3,3,metal); t.SetPixel(4,3,g1); t.SetPixel(5,3,metal);
            t.SetPixel(3,2,metalDark); t.SetPixel(4,2,metal); t.SetPixel(5,2,metalDark);

            t.Apply(false, false);
            return t;
        }

        private float LanternFactor(LightingSnapshot21 state)
        {
            if (state.DungeonKnown && state.IsDungeon) return 1f;

            if (!string.IsNullOrEmpty(state.PresetName) && !state.PresetName.StartsWith("dungeon_", StringComparison.OrdinalIgnoreCase))
                return 0f;
            if (state.IndoorKnown && state.IsIndoor) return 0f;
            if (!state.TimeKnown || !state.IndoorKnown) return 0f;

            float t = state.TimeK - Mathf.Floor(state.TimeK);
            const float dusk = 0.72f;
            const float dawn = 0.14f;
            if (t >= dusk)
            {
                float x = Mathf.Clamp01(Mathf.InverseLerp(dusk, 1f, t));
                return x * x * (3f - 2f * x);
            }
            if (t <= dawn)
            {
                float x = Mathf.Clamp01(Mathf.InverseLerp(dawn, 0f, t));
                return x * x * (3f - 2f * x);
            }
            return 0f;
        }

        private void DumpState()
        {
            if (!_bound)
            {
                Logger.LogInfo("F10 BACK BELT LANTERN DUMP: not bound.");
                return;
            }

            string sprite = _directionRenderer != null && _directionRenderer.sprite != null ? _directionRenderer.sprite.name : "<none>";
            Vector2 p = BackAnchors[_anchorPreset];
            Logger.LogInfo(
                "F10 BACK BELT LANTERN DUMP | v=1.0.10" +
                " facing=" + _lastFacing +
                " sourceSprite=" + sprite +
                " anchorPreset=" + (_anchorPreset + 1) + "/" + BackAnchors.Length +
                " basePx=(" + p.x + "," + p.y + ")" +
                " userOffsetPx=(" + (_visualOffsetXPx != null ? _visualOffsetXPx.Value.ToString("0.00") : "0.00") + "," + (_visualOffsetYPx != null ? _visualOffsetYPx.Value.ToString("0.00") : "0.00") + ")" +
                " frameFollowXPx=" + _frameFollowXPx.ToString("0.00") +
                " cycleFrame=" + _cycleFrameIndex +
                " cycleLength=" + _observedCycleLength +
                " pxLocal=" + _localUnitsPerPixelX.ToString("0.0000") + "," + _localUnitsPerPixelY.ToString("0.0000") +
                " anchorLocal=" + Fmt(_anchorLocal) +
                " anchorWorld=" + Fmt(_anchorWorld) +
                " visible=" + (_lanternRenderer != null && _lanternRenderer.enabled) +
                " lit=" + _lit +
                " fade=" + UnifiedLightingPlugin.SharedLanternFactor.ToString("0.00") +
                " sortScans=" + _sortingScanCount +
                " rendererHierarchyScans=" + _rendererHierarchyScanCount +
                " rendererReselects=" + _rendererReselectCount +
                " cachedRenderers=" + (_heroRenderers != null ? _heroRenderers.Length : 0) +
                " moving=" + _moving +
                " sort=" + (_lanternRenderer != null ? _lanternRenderer.sortingOrder.ToString() : "?") +
                " bounds=" + (_lanternRenderer != null ? Fmt(_lanternRenderer.bounds.size) : "<null>"));
        }

        private static string Fmt(Vector3 v)
        {
            return "(" + v.x.ToString("0.##") + "," + v.y.ToString("0.##") + "," + v.z.ToString("0.##") + ")";
        }

        private void DestroyVisual()
        {
            if (_lanternObject != null) Destroy(_lanternObject);
            if (_darkSprite != null) Destroy(_darkSprite);
            if (_litSprite != null) Destroy(_litSprite);
            if (_darkTexture != null) Destroy(_darkTexture);
            if (_litTexture != null) Destroy(_litTexture);
            if (_material != null) Destroy(_material);
            _lanternObject = null;
            _lanternRenderer = null;
            _lanternGlowRenderer = null;
            _darkSprite = null;
            _litSprite = null;
            _darkTexture = null;
            _litTexture = null;
            _material = null;
        }

        private void OnDestroy()
        {
            DestroyVisual();
        }

        private enum Facing34 { Down, Up, Left, Right }
    }
}
