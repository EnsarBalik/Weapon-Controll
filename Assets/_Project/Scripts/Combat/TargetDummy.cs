using UnityEngine;
using WeaponControl.Core;

namespace WeaponControl.Combat
{
    /// <summary>
    /// Visual feedback for a test target: flashes on hit and reacts on death.
    /// Uses a MaterialPropertyBlock so it doesn't create material instances.
    /// Assumes the Built-in Standard shader ("_Color" / "_EmissionColor").
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TargetDummy : MonoBehaviour
    {
        [SerializeField] private Renderer[] renderers;
        [SerializeField] private Color hitFlashColor = new Color(1f, 0.25f, 0.15f);
        [SerializeField] private float flashDuration = 0.09f;
        [Tooltip("Fade the target out and disable it when it dies.")]
        [SerializeField] private bool collapseOnDeath = true;
        [SerializeField] private float collapseTime = 0.6f;

        private Health _health;
        private MaterialPropertyBlock _mpb;
        private Color[] _baseColors;
        private float _flashTimer;
        private bool _collapsing;
        private float _collapseTimer;
        private Vector3 _startScale;

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private void Awake()
        {
            _health = GetComponent<Health>();
            if (renderers == null || renderers.Length == 0)
                renderers = GetComponentsInChildren<Renderer>();

            _mpb = new MaterialPropertyBlock();
            _baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var mat = renderers[i].sharedMaterial;
                _baseColors[i] = mat != null && mat.HasProperty(ColorId) ? mat.GetColor(ColorId) : Color.white;
            }
            _startScale = transform.localScale;
        }

        private void OnEnable()
        {
            _health.Damaged += OnDamaged;
            _health.Died += OnDied;
        }

        private void OnDisable()
        {
            _health.Damaged -= OnDamaged;
            _health.Died -= OnDied;
        }

        private void OnDamaged(DamageInfo info)
        {
            _flashTimer = flashDuration;
        }

        private void OnDied(DamageInfo info)
        {
            if (collapseOnDeath)
            {
                _collapsing = true;
                _collapseTimer = collapseTime;
            }
        }

        private void Update()
        {
            // Hit flash
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_flashTimer / flashDuration);
                for (int i = 0; i < renderers.Length; i++)
                {
                    renderers[i].GetPropertyBlock(_mpb);
                    _mpb.SetColor(ColorId, Color.Lerp(_baseColors[i], hitFlashColor, t));
                    renderers[i].SetPropertyBlock(_mpb);
                }
            }

            // Death collapse
            if (_collapsing)
            {
                _collapseTimer -= Time.deltaTime;
                float k = Mathf.Clamp01(_collapseTimer / collapseTime);
                transform.localScale = _startScale * k;
                if (_collapseTimer <= 0f)
                {
                    _collapsing = false;
                    gameObject.SetActive(false);
                }
            }
        }
    }
}
