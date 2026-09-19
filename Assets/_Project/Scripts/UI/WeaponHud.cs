using UnityEngine;
using UnityEngine.UI;
using WeaponControl.Core;
using WeaponControl.Player;
using WeaponControl.Weapons;

namespace WeaponControl.UI
{
    /// <summary>
    /// Showcase HUD: dynamic crosshair, ammo, fire mode, weapon name, reload hint, hit marker.
    /// Builds its own canvas at runtime so you only have to add this component to the Player.
    /// </summary>
    [DisallowMultipleComponent]
    public class WeaponHud : MonoBehaviour
    {
        [Header("References (auto-filled if left empty)")]
        [SerializeField] private WeaponInventory inventory;
        [SerializeField] private WeaponProceduralMotion motion;
        [SerializeField] private PlayerMovement movement;

        [Header("Crosshair")]
        [SerializeField] private float crosshairGap = 8f;
        [SerializeField] private float spreadPixelsPerDegree = 14f;
        [SerializeField] private float movementBloom = 18f;
        [SerializeField] private float crosshairSmooth = 18f;
        [SerializeField] private float adsFadeSpeed = 14f;
        [SerializeField] private Color crosshairColor = new Color(1f, 1f, 1f, 0.92f);

        [Header("Hit Marker")]
        [SerializeField] private float hitMarkerDuration = 0.16f;
        [SerializeField] private Color hitColor = Color.white;
        [SerializeField] private Color killColor = new Color(1f, 0.22f, 0.22f);

        private WeaponFire _fire;
        private WeaponAmmo _ammo;

        private CanvasGroup _crosshairGroup;
        private RectTransform[] _ticks;
        private CanvasGroup _hitGroup;
        private Image[] _hitArms;
        private Text _ammoMag;
        private Text _ammoReserve;
        private Text _fireMode;
        private Text _weaponName;
        private Text _reloadHint;
        private Image _reloadBar;

        private float _spreadPixels;
        private float _hitTimer;
        private float _reloadDuration;
        private float _reloadLeft;
        private bool _reloading;

        private static Sprite _whiteSprite;

        private void Awake()
        {
            if (inventory == null) inventory = GetComponentInChildren<WeaponInventory>(true);
            if (motion == null) motion = GetComponentInChildren<WeaponProceduralMotion>(true);
            if (movement == null) movement = GetComponent<PlayerMovement>();
            if (movement == null) movement = GetComponentInParent<PlayerMovement>();

            BuildUi();
        }

        private void OnEnable()
        {
            if (inventory != null) inventory.WeaponChanged += OnWeaponChanged;
        }

        private void OnDisable()
        {
            if (inventory != null) inventory.WeaponChanged -= OnWeaponChanged;
            UnbindWeapon();
        }

        private void Start()
        {
            if (inventory != null && inventory.CurrentFire != null)
                OnWeaponChanged(inventory.CurrentIndex, inventory.CurrentFire);
        }

        private void Update()
        {
            UpdateCrosshair();
            UpdateHitMarker();
            UpdateReload();
        }

        private void OnWeaponChanged(int index, WeaponFire fire)
        {
            BindWeapon(fire);
        }

        private void BindWeapon(WeaponFire fire)
        {
            UnbindWeapon();
            _fire = fire;
            _ammo = fire != null ? fire.GetComponent<WeaponAmmo>() : null;

            if (_fire != null)
            {
                _fire.HitConfirmed += OnHitConfirmed;
                _fire.FireModeChanged += OnFireModeChanged;
                RefreshWeaponText();
            }

            if (_ammo != null)
            {
                _ammo.AmmoChanged += OnAmmoChanged;
                _ammo.ReloadStarted += OnReloadStarted;
                _ammo.ReloadCompleted += OnReloadEnded;
                _ammo.ReloadCancelled += OnReloadEnded;
                OnAmmoChanged(_ammo.MagazineCount, _ammo.ReserveCount);
                if (_ammo.IsReloading)
                    OnReloadStarted(0.01f);
            }
            else
            {
                SetAmmoText("--", "--");
            }
        }

        private void UnbindWeapon()
        {
            if (_fire != null)
            {
                _fire.HitConfirmed -= OnHitConfirmed;
                _fire.FireModeChanged -= OnFireModeChanged;
            }

            if (_ammo != null)
            {
                _ammo.AmmoChanged -= OnAmmoChanged;
                _ammo.ReloadStarted -= OnReloadStarted;
                _ammo.ReloadCompleted -= OnReloadEnded;
                _ammo.ReloadCancelled -= OnReloadEnded;
            }

            _fire = null;
            _ammo = null;
            _reloading = false;
            if (_reloadHint != null) _reloadHint.enabled = false;
            if (_reloadBar != null) _reloadBar.fillAmount = 0f;
        }

        private void OnAmmoChanged(int mag, int reserve) => SetAmmoText(mag.ToString(), reserve.ToString());

        private void OnFireModeChanged(FireMode mode)
        {
            if (_fireMode != null) _fireMode.text = ModeLabel(mode);
        }

        private void OnHitConfirmed(DamageInfo info, bool killed)
        {
            _hitTimer = hitMarkerDuration;
            Color c = killed ? killColor : hitColor;
            foreach (var arm in _hitArms)
                if (arm != null) arm.color = c;
        }

        private void OnReloadStarted(float duration)
        {
            _reloading = true;
            _reloadDuration = Mathf.Max(0.01f, duration);
            _reloadLeft = _reloadDuration;
            if (_reloadHint != null) _reloadHint.enabled = true;
        }

        private void OnReloadEnded()
        {
            _reloading = false;
            if (_reloadHint != null) _reloadHint.enabled = false;
            if (_reloadBar != null) _reloadBar.fillAmount = 0f;
        }

        private void RefreshWeaponText()
        {
            if (_fire == null) return;
            if (_weaponName != null)
                _weaponName.text = _fire.Data != null ? _fire.Data.weaponName : _fire.gameObject.name;
            if (_fireMode != null)
                _fireMode.text = ModeLabel(_fire.CurrentMode);
        }

        private void SetAmmoText(string mag, string reserve)
        {
            if (_ammoMag != null) _ammoMag.text = mag;
            if (_ammoReserve != null) _ammoReserve.text = reserve;
        }

        private void UpdateCrosshair()
        {
            float aim = motion != null ? motion.AimBlend : 0f;
            float targetAlpha = Mathf.Lerp(1f, 0.08f, aim);
            _crosshairGroup.alpha = Mathf.Lerp(_crosshairGroup.alpha, targetAlpha, 1f - Mathf.Exp(-adsFadeSpeed * Time.deltaTime));

            float spreadDeg = _fire != null ? _fire.CurrentSpread : 2.5f;
            float move = movement != null ? movement.NormalizedSpeed : 0f;
            float target = crosshairGap + spreadDeg * spreadPixelsPerDegree + move * movementBloom;
            _spreadPixels = Mathf.Lerp(_spreadPixels, target, 1f - Mathf.Exp(-crosshairSmooth * Time.deltaTime));

            float g = _spreadPixels;
            _ticks[0].anchoredPosition = new Vector2(0f, g);   // up
            _ticks[1].anchoredPosition = new Vector2(0f, -g);  // down
            _ticks[2].anchoredPosition = new Vector2(-g, 0f);  // left
            _ticks[3].anchoredPosition = new Vector2(g, 0f);   // right
        }

        private void UpdateHitMarker()
        {
            if (_hitTimer > 0f)
            {
                _hitTimer -= Time.deltaTime;
                _hitGroup.alpha = Mathf.Clamp01(_hitTimer / hitMarkerDuration);
            }
            else
            {
                _hitGroup.alpha = 0f;
            }
        }

        private void UpdateReload()
        {
            if (!_reloading || _reloadBar == null) return;
            _reloadLeft -= Time.deltaTime;
            _reloadBar.fillAmount = 1f - Mathf.Clamp01(_reloadLeft / _reloadDuration);
        }

        private void BuildUi()
        {
            EnsureWhiteSprite();

            var canvasGo = new GameObject("WeaponHUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            BuildCrosshair(canvasGo.transform);
            BuildHitMarker(canvasGo.transform);
            BuildAmmoPanel(canvasGo.transform);
            BuildReloadHint(canvasGo.transform);
        }

        private void BuildCrosshair(Transform parent)
        {
            var root = CreateRect("Crosshair", parent, Vector2.zero, Vector2.zero, Vector2.one * 0.5f, Vector2.zero);
            _crosshairGroup = root.gameObject.AddComponent<CanvasGroup>();
            _crosshairGroup.blocksRaycasts = false;
            _ticks = new RectTransform[4];
            _ticks[0] = CreateTick(root, "Up", new Vector2(3f, 14f));
            _ticks[1] = CreateTick(root, "Down", new Vector2(3f, 14f));
            _ticks[2] = CreateTick(root, "Left", new Vector2(14f, 3f));
            _ticks[3] = CreateTick(root, "Right", new Vector2(14f, 3f));

            var dot = CreateImage("Dot", root, new Vector2(3f, 3f), Vector2.zero);
            dot.color = crosshairColor;
        }

        private RectTransform CreateTick(Transform parent, string name, Vector2 size)
        {
            var img = CreateImage(name, parent, size, Vector2.zero);
            img.color = crosshairColor;
            return img.rectTransform;
        }

        private void BuildHitMarker(Transform parent)
        {
            var root = CreateRect("HitMarker", parent, Vector2.zero, Vector2.zero, Vector2.one * 0.5f, Vector2.zero);
            _hitGroup = root.gameObject.AddComponent<CanvasGroup>();
            _hitGroup.alpha = 0f;
            _hitGroup.blocksRaycasts = false;
            _hitArms = new Image[4];
            _hitArms[0] = CreateHitArm(root, new Vector2(16f, 16f), 45f);
            _hitArms[1] = CreateHitArm(root, new Vector2(-16f, 16f), -45f);
            _hitArms[2] = CreateHitArm(root, new Vector2(16f, -16f), -45f);
            _hitArms[3] = CreateHitArm(root, new Vector2(-16f, -16f), 45f);
        }

        private Image CreateHitArm(Transform parent, Vector2 pos, float zRot)
        {
            var img = CreateImage("Arm", parent, new Vector2(16f, 3f), pos);
            img.rectTransform.localEulerAngles = new Vector3(0f, 0f, zRot);
            img.color = hitColor;
            return img;
        }

        private void BuildAmmoPanel(Transform parent)
        {
            var panel = CreateRect("AmmoPanel", parent, new Vector2(360f, 110f), new Vector2(-48f, 42f), new Vector2(1f, 0f), new Vector2(1f, 0f));
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = _whiteSprite;
            bg.color = new Color(0f, 0f, 0f, 0.35f);

            _weaponName = CreateText("WeaponName", panel, new Vector2(-16f, -10f), new Vector2(1f, 1f), new Vector2(1f, 1f), 20, TextAnchor.UpperRight, new Color(0.85f, 0.85f, 0.85f));
            _weaponName.text = "WEAPON";

            _fireMode = CreateText("FireMode", panel, new Vector2(-16f, -34f), new Vector2(1f, 1f), new Vector2(1f, 1f), 16, TextAnchor.UpperRight, new Color(0.75f, 0.72f, 0.55f));
            _fireMode.text = "AUTO";

            _ammoMag = CreateText("Magazine", panel, new Vector2(-92f, 12f), new Vector2(1f, 0f), new Vector2(1f, 0f), 42, TextAnchor.LowerRight, Color.white);
            _ammoMag.text = "31";
            _ammoMag.fontStyle = FontStyle.Bold;

            var slash = CreateText("Slash", panel, new Vector2(-72f, 16f), new Vector2(1f, 0f), new Vector2(1f, 0f), 22, TextAnchor.LowerRight, new Color(0.7f, 0.7f, 0.7f));
            slash.text = "/";

            _ammoReserve = CreateText("Reserve", panel, new Vector2(-16f, 16f), new Vector2(1f, 0f), new Vector2(1f, 0f), 22, TextAnchor.LowerRight, new Color(0.78f, 0.78f, 0.78f));
            _ammoReserve.text = "120";
        }

        private void BuildReloadHint(Transform parent)
        {
            _reloadHint = CreateText("ReloadHint", parent, new Vector2(0f, 140f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), 20, TextAnchor.MiddleCenter, new Color(0.95f, 0.9f, 0.65f));
            _reloadHint.rectTransform.sizeDelta = new Vector2(320f, 30f);
            _reloadHint.text = "RELOADING";
            _reloadHint.enabled = false;

            var barRoot = CreateRect("ReloadBar", parent, new Vector2(220f, 4f), new Vector2(0f, 122f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
            var track = barRoot.gameObject.AddComponent<Image>();
            track.sprite = _whiteSprite;
            track.color = new Color(1f, 1f, 1f, 0.15f);

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barRoot, false);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _reloadBar = fillGo.GetComponent<Image>();
            _reloadBar.sprite = _whiteSprite;
            _reloadBar.type = Image.Type.Filled;
            _reloadBar.fillMethod = Image.FillMethod.Horizontal;
            _reloadBar.fillAmount = 0f;
            _reloadBar.color = new Color(0.95f, 0.88f, 0.45f);
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 anchored, Vector2 anchor, Vector2 pivot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;
            return rt;
        }

        private static Image CreateImage(string name, Transform parent, Vector2 size, Vector2 anchored)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchored;
            var img = go.GetComponent<Image>();
            img.sprite = _whiteSprite;
            img.raycastTarget = false;
            return img;
        }

        private static Text CreateText(string name, Transform parent, Vector2 anchored, Vector2 anchor, Vector2 pivot, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.sizeDelta = new Vector2(280f, 48f);
            rt.anchoredPosition = anchored;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static void EnsureWhiteSprite()
        {
            if (_whiteSprite != null) return;
            var tex = Texture2D.whiteTexture;
            _whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static string ModeLabel(FireMode mode)
        {
            switch (mode)
            {
                case FireMode.Semi: return "SEMI";
                case FireMode.Burst: return "BURST";
                default: return "AUTO";
            }
        }
    }
}
