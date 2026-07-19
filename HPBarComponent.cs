using System;
using UnityEngine;
using UnityEngine.UI;

namespace MonsterHPBars
{
    /// <summary>
    /// World-space HP bar component attached to each tracked unit's GameObject.
    /// It spawns its own Canvas + UI elements and faces the camera every frame.
    /// Optimized for zero GC allocation and minimal CPU overhead.
    /// </summary>
    public sealed class HPBarComponent : MonoBehaviour
    {
        // ─── references set on Init ───────────────────────────────────────────
        private IDamageable? _damageable;
        private bool         _isBoss;
        private bool         _isElite;
        private string       _unitName = "";

        // ─── cached UI ────────────────────────────────────────────────────────
        private Canvas?        _canvas;
        private RectTransform? _canvasRT;
        private Image?         _bgImage;
        private Image?         _fillImage;
        private RectTransform? _fillRT;
        private Image?         _delayedFillImage;
        private RectTransform? _delayedFillRT;
        private Text?          _hpText;
        private Text?          _nameText;
        private Image?         _eliteBorder;

        // ─── state ────────────────────────────────────────────────────────────
        private float _lastDamageTime = -999f;
        private float _delayedFill    = 1f;   // lags behind real fill for the ghost bar
        private bool  _initialized    = false;
        private Camera? _cam;
        private float _cachedHeight   = -1f;
        private int   _heightCalculationsCount = 0;

        // ─── optimization caches ──────────────────────────────────────────────
        private float _lastSetHeight   = -99f;
        private float _lastSetPadding  = -99f;
        private float _lastFillAmount  = -99f;
        private float _lastDelayedFill = -99f;
        private int   _lastHpValue     = -1;
        private int   _lastMaxHpValue  = -1;

        // ─── constants ────────────────────────────────────────────────────────
        private const float DelayedFillSpeed = 0.8f;   // how fast ghost bar drains
        private const float DelayedFillDelay = 0.3f;   // seconds before ghost starts moving

        // ─────────────────────────────────────────────────────────────────────
        public void Init(IDamageable damageable, string unitName, bool isBoss, bool isElite)
        {
            _damageable = damageable;
            
            // Clean up name: remove "boss_" prefix and format nicely (e.g. "boss_goose" -> "Goose")
            if (!string.IsNullOrEmpty(unitName))
            {
                if (unitName.StartsWith("boss_", StringComparison.OrdinalIgnoreCase))
                {
                    unitName = unitName.Substring(5);
                }
                try
                {
                    unitName = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(unitName.Replace('_', ' ').Trim());
                }
                catch
                {
                    unitName = unitName.Replace('_', ' ').Trim();
                }
            }

            _unitName   = unitName ?? "";
            _isBoss     = isBoss;
            _isElite    = isElite;
            _initialized = false;
            _cachedHeight = -1f;
            _heightCalculationsCount = 0;
            _lastSetHeight = -99f;
            _lastSetPadding = -99f;
            _lastFillAmount = -99f;
            _lastDelayedFill = -99f;
            _lastHpValue = -1;
            _lastMaxHpValue = -1;
        }

        private void Start()
        {
            BuildUI();
            _initialized = true;
            _delayedFill = 1f;
        }

        private void BuildUI()
        {
            _cam = Camera.main;

            // Destroy any pre-existing orphan HPBarCanvas child to prevent duplicates/leaks
            var oldCanvas = transform.Find("HPBarCanvas");
            if (oldCanvas != null)
            {
                DestroyImmediate(oldCanvas.gameObject);
            }

            // ─ Canvas ─────────────────────────────────────────────────────────
            var canvasGo = new GameObject("HPBarCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 200;

            _canvasRT = canvasGo.GetComponent<RectTransform>();
            float w = MonsterHPBarsPlugin.BarWidth.Value;
            float h = MonsterHPBarsPlugin.BarHeight.Value;
            _canvasRT.sizeDelta = new Vector2(w + 20f, 50f);
            _canvasRT.localScale = Vector3.one * 0.012f;   // world-space scale

            // Initial positioning
            _canvasRT.localPosition = new Vector3(0f, 2.0f, 0f);

            // ─ Name label ─────────────────────────────────────────────────────
            bool hasValidName = !string.IsNullOrWhiteSpace(_unitName) && 
                                !_unitName.Equals("Unit", StringComparison.OrdinalIgnoreCase);

            if (MonsterHPBarsPlugin.ShowLabel.Value && hasValidName)
            {
                _nameText = CreateText(canvasGo, "NameLabel", _unitName, 14,
                    new Vector2(0f, 30f), new Vector2(w + 20f, 20f), TextAnchor.MiddleCenter);
                _nameText.color = _isBoss   ? MonsterHPBarsPlugin.BossColor.Value :
                                  _isElite  ? MonsterHPBarsPlugin.EliteColor.Value :
                                  new Color(1f, 1f, 1f, 0.90f);
                _nameText.fontStyle = (_isBoss || _isElite) ? FontStyle.Bold : FontStyle.Normal;
            }

            // ─ Background ─────────────────────────────────────────────────────
            var bgGo = new GameObject("HPBg");
            bgGo.transform.SetParent(canvasGo.transform, false);
            _bgImage = bgGo.AddComponent<Image>();
            _bgImage.color = new Color(0.06f, 0.06f, 0.08f, 0.88f);
            var bgRT = bgGo.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0.5f, 0.5f);
            bgRT.anchorMax = new Vector2(0.5f, 0.5f);
            bgRT.pivot = new Vector2(0.5f, 0.5f);
            bgRT.anchoredPosition = new Vector2(0f, 0f);
            bgRT.sizeDelta = new Vector2(w + 4f, h + 4f);

            // ─ Delayed / ghost fill (behind real bar) ─────────────────────────
            var delayGo = new GameObject("HPDelayFill");
            delayGo.transform.SetParent(canvasGo.transform, false);
            _delayedFillImage = delayGo.AddComponent<Image>();
            _delayedFillImage.color = new Color(0.95f, 0.90f, 0.20f, 0.55f);
            
            _delayedFillRT = delayGo.GetComponent<RectTransform>();
            _delayedFillRT.anchorMin = new Vector2(0.5f, 0.5f);
            _delayedFillRT.anchorMax = new Vector2(0.5f, 0.5f);
            _delayedFillRT.pivot = new Vector2(0f, 0.5f); // Left-aligned
            _delayedFillRT.anchoredPosition = new Vector2(-w / 2f, 0f); // Starts at left edge
            _delayedFillRT.sizeDelta = new Vector2(w, h);

            // ─ Real fill ──────────────────────────────────────────────────────
            var fillGo = new GameObject("HPFill");
            fillGo.transform.SetParent(canvasGo.transform, false);
            _fillImage = fillGo.AddComponent<Image>();
            _fillImage.color = MonsterHPBarsPlugin.HealthyColor.Value;
            
            _fillRT = fillGo.GetComponent<RectTransform>();
            _fillRT.anchorMin = new Vector2(0.5f, 0.5f);
            _fillRT.anchorMax = new Vector2(0.5f, 0.5f);
            _fillRT.pivot = new Vector2(0f, 0.5f); // Left-aligned
            _fillRT.anchoredPosition = new Vector2(-w / 2f, 0f); // Starts at left edge
            _fillRT.sizeDelta = new Vector2(w, h);

            // ─ Elite / Boss border ────────────────────────────────────────────
            if (_isBoss || _isElite)
            {
                var borderGo = new GameObject("HPBorder");
                borderGo.transform.SetParent(canvasGo.transform, false);
                _eliteBorder = borderGo.AddComponent<Image>();
                Color borderCol = _isBoss ? MonsterHPBarsPlugin.BossColor.Value
                                           : MonsterHPBarsPlugin.EliteColor.Value;
                borderCol.a = 0.8f;
                _eliteBorder.color = borderCol;
                var bRT = borderGo.GetComponent<RectTransform>();
                bRT.anchorMin = new Vector2(0.5f, 0.5f);
                bRT.anchorMax = new Vector2(0.5f, 0.5f);
                bRT.pivot = new Vector2(0.5f, 0.5f);
                bRT.anchoredPosition = new Vector2(0f, 0f);
                bRT.sizeDelta = new Vector2(w + 8f, h + 8f);
                // Push behind bg
                borderGo.transform.SetSiblingIndex(0);
            }

            // ─ HP numbers ─────────────────────────────────────────────────────
            if (MonsterHPBarsPlugin.ShowNumbers.Value)
            {
                _hpText = CreateText(canvasGo, "HPText", "", 11,
                    new Vector2(0f, 0f), new Vector2(w, h), TextAnchor.MiddleCenter);
                _hpText.color = new Color(1f, 1f, 1f, 0.95f);
            }
        }

        private Text CreateText(GameObject parent, string name, string content, int fontSize,
            Vector2 anchoredPos, Vector2 size, TextAnchor anchor)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var t = go.AddComponent<Text>();
            t.text      = content;
            t.fontSize  = fontSize;
            t.font      = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.alignment = anchor;
            t.color     = Color.white;
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return t;
        }

        private float GetUnitHeight()
        {
            if (_damageable == null || _damageable.Owner == null) return -1f;
            var unit = _damageable.Owner;

            // 1. Primary Method: Use developers' cylinder definition (CollisionCenterY + CollisionHeight / 2)
            if (unit.SpatialEntity != null)
            {
                float cy = unit.SpatialEntity.CollisionCenterYFloat;
                float ch = unit.SpatialEntity.CollisionHeightFloat;
                if (ch > 0.1f)
                {
                    return cy + (ch / 2f);
                }
            }

            // 2. Fallback: Try SkinnedMeshRenderers (the main body of animated monsters)
            // Note: GetComponentsInChildren causes GC allocation, so we limit attempts to prevent lag
            var renderers = unit.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                float maxHeight = 0f;
                float unitBaseY = unit.transform.position.y;
                bool foundRealVisual = false;

                foreach (var r in renderers)
                {
                    if (r.enabled && r.gameObject.activeInHierarchy && 
                        r.GetType().Name.Contains("SkinnedMeshRenderer"))
                    {
                        float rTopY = r.bounds.max.y;
                        float height = rTopY - unitBaseY;
                        if (height > maxHeight)
                        {
                            maxHeight = height;
                            foundRealVisual = true;
                        }
                    }
                }

                // 3. Third level: Try standard MeshRenderers (for static structures/slimes)
                if (!foundRealVisual)
                {
                    foreach (var r in renderers)
                    {
                        if (r.enabled && r.gameObject.activeInHierarchy && 
                            r.GetType().Name.Contains("MeshRenderer"))
                        {
                            string nameLower = r.gameObject.name.ToLower();
                            if (nameLower.Contains("shadow") || nameLower.Contains("decal") || 
                                nameLower.Contains("ui") || nameLower.Contains("outline") ||
                                nameLower.Contains("range") || nameLower.Contains("weapon"))
                                continue;

                            float rTopY = r.bounds.max.y;
                            float height = rTopY - unitBaseY;
                            if (height > maxHeight)
                            {
                                maxHeight = height;
                                foundRealVisual = true;
                            }
                        }
                    }
                }

                if (foundRealVisual && maxHeight > 0.3f)
                {
                    return maxHeight;
                }
            }
            
            return -1f;
        }

        private float GetOrCalculateHeight()
        {
            if (_cachedHeight > 0.1f) return _cachedHeight;
            
            _heightCalculationsCount++;
            float h = GetUnitHeight();
            if (h > 0.3f)
            {
                _cachedHeight = h;
                return _cachedHeight;
            }

            // If we have tried 10 times, freeze calculation to prevent GC allocation lag
            if (_heightCalculationsCount > 10)
            {
                if (_damageable != null && _damageable.Owner != null && _damageable.Owner.SpatialEntity != null)
                {
                    float spatialHeight = _damageable.Owner.SpatialEntity.CollisionHeightFloat;
                    _cachedHeight = spatialHeight > 0.3f ? spatialHeight : 2.0f;
                }
                else
                {
                    _cachedHeight = 2.0f;
                }
                return _cachedHeight;
            }
            
            // Temporary un-cached fallback using game's CollisionHeight
            if (_damageable != null && _damageable.Owner != null && _damageable.Owner.SpatialEntity != null)
            {
                float spatialHeight = _damageable.Owner.SpatialEntity.CollisionHeightFloat;
                if (spatialHeight > 0.3f)
                {
                    return spatialHeight;
                }
            }
            
            return 2.0f; // Standard default height fallback
        }

        private void Update()
        {
            if (!_initialized || _canvas == null || _damageable == null) return;

            // Destroy self immediately if the unit died or is despawning
            if (_damageable.Owner == null || 
                !_damageable.IsAlive || 
                _damageable.Owner.IsDead || 
                _damageable.CurrentHealth <= 0f)
            {
                Destroy(this); // OnDestroy handles the canvas deletion
                return;
            }

            // ─ Filter checks ──────────────────────────────────────────────────
            bool shouldShow = true;
            
            if (_damageable.Owner.isPlayerCharacter)
            {
                shouldShow = false;
            }
            else if (MonsterHPBarsPlugin.ShowBossOnly.Value && !_damageable.Owner.isBoss)
            {
                shouldShow = false;
            }
            else if (MonsterHPBarsPlugin.ShowOnlyEnemies.Value && _damageable.Owner.Team != PlayerTeam.Neutral)
            {
                shouldShow = false;
            }
            
            _canvas.gameObject.SetActive(shouldShow);
            if (!shouldShow) return;

            // ─ Dynamic vertical positioning (optimized to only set on change) ─
            if (_canvasRT != null)
            {
                float height = GetOrCalculateHeight();
                float padding = MonsterHPBarsPlugin.BarHeightPadding.Value;
                
                if (Mathf.Abs(height - _lastSetHeight) > 0.01f || Mathf.Abs(padding - _lastSetPadding) > 0.01f)
                {
                    _canvasRT.localPosition = new Vector3(0f, height + padding, 0f);
                    _lastSetHeight = height;
                    _lastSetPadding = padding;
                }
            }

            // ─ Camera billboard (caching main camera to prevent tag search) ───
            if (_cam == null || !_cam.gameObject.activeInHierarchy) _cam = Camera.main;
            if (_cam != null && _canvasRT != null)
                _canvasRT.rotation = Quaternion.LookRotation(_canvasRT.position - _cam.transform.position);

            // ─ Fill calculation ───────────────────────────────────────────────
            float maxHp     = Mathf.Max(_damageable.MaxHealth, 1f);
            float currentHp = Mathf.Clamp(_damageable.CurrentHealth, 0f, maxHp);
            float fill      = currentHp / maxHp;

            // ─ Ghost bar ──────────────────────────────────────────────────────
            if (fill < _delayedFill)
            {
                if (Time.time - _lastDamageTime > DelayedFillDelay)
                    _delayedFill = Mathf.MoveTowards(_delayedFill, fill, DelayedFillSpeed * Time.deltaTime);
                _lastDamageTime = Mathf.Min(_lastDamageTime, Time.time - DelayedFillDelay + 0.001f);
            }
            else
            {
                _delayedFill = fill;  // hp was restored
            }

            float w = MonsterHPBarsPlugin.BarWidth.Value;
            float h = MonsterHPBarsPlugin.BarHeight.Value;

            if (_delayedFillRT != null && Mathf.Abs(_delayedFill - _lastDelayedFill) > 0.001f)
            {
                _delayedFillRT.sizeDelta = new Vector2(w * _delayedFill, h);
                _lastDelayedFill = _delayedFill;
            }

            // ─ Real fill ──────────────────────────────────────────────────────
            if (_fillRT != null && _fillImage != null && Mathf.Abs(fill - _lastFillAmount) > 0.001f)
            {
                _fillRT.sizeDelta = new Vector2(w * fill, h);
                _lastFillAmount = fill;

                // Lerp color based on thresholds
                Color targetColor;
                if (fill <= MonsterHPBarsPlugin.CriticalThreshold.Value)
                    targetColor = MonsterHPBarsPlugin.CriticalColor.Value;
                else if (fill <= MonsterHPBarsPlugin.DamagedThreshold.Value)
                    targetColor = MonsterHPBarsPlugin.DamagedColor.Value;
                else
                    targetColor = MonsterHPBarsPlugin.HealthyColor.Value;

                _fillImage.color = Color.Lerp(_fillImage.color, targetColor, Time.deltaTime * 8f);
            }

            // ─ HP numbers (optimized string format lookup) ───────────────────
            int curInt = Mathf.RoundToInt(currentHp);
            int maxInt = Mathf.RoundToInt(maxHp);
            if (_hpText != null && (curInt != _lastHpValue || maxInt != _lastMaxHpValue))
            {
                _hpText.text = $"{curInt} / {maxInt}";
                _lastHpValue = curInt;
                _lastMaxHpValue = maxInt;
            }

            // ─ Visibility timeout ─────────────────────────────────────────────
            if (!MonsterHPBarsPlugin.AlwaysVisible.Value)
            {
                bool timeVisible = fill < 1f && (Time.time - _lastDamageTime) < MonsterHPBarsPlugin.VisibilityDuration.Value;
                _canvas.gameObject.SetActive(timeVisible);
            }
        }

        private void OnDestroy()
        {
            // Fully clean up the canvas GameObject when the component is destroyed
            if (_canvas != null && _canvas.gameObject != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        /// <summary>Called externally when the unit takes damage to reset the visibility timer.</summary>
        public void NotifyDamage()
        {
            _lastDamageTime = Time.time;
        }
    }
}
