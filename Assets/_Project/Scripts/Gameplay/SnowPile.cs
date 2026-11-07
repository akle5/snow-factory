using UnityEngine;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// Represents a pile/mound of snow that can be gradually scooped with a shovel.
    /// Shrinks visually each time a scoop is taken until depleted.
    /// </summary>
    public class SnowPile : MonoBehaviour
    {
        [Header("Snow Pile Settings")]
        [SerializeField] private int maxScoops = 10;
        [SerializeField] private float minScaleRatio = 0.2f;

        private int _remainingScoops;
        private Vector3 _initialScale;
        private Vector3 _initialPosition;
        private Collider _collider;
        private Renderer _renderer;

        public int RemainingScoops => _remainingScoops;
        public int MaxScoops => maxScoops;
        public bool HasSnow => _remainingScoops > 0;

        private void Awake()
        {
            _remainingScoops = maxScoops;
            _initialScale = transform.localScale;
            _initialPosition = transform.localPosition;
            _collider = GetComponent<Collider>();
            _renderer = GetComponent<Renderer>();
        }

        public void ConfigureScoops(int scoops)
        {
            maxScoops = Mathf.Max(1, scoops);
            _remainingScoops = maxScoops;
        }

        public bool TryScoop()
        {
            if (_remainingScoops <= 0)
            {
                return false;
            }

            _remainingScoops--;
            UpdateVisualScale();
            return true;
        }

        private void UpdateVisualScale()
        {
            if (_remainingScoops <= 0)
            {
                if (_renderer != null) _renderer.enabled = false;
                if (_collider != null) _collider.enabled = false;
                return;
            }

            float t = (float)_remainingScoops / maxScoops;
            float scaleFactor = Mathf.Lerp(minScaleRatio, 1f, t);

            Vector3 newScale = new Vector3(
                _initialScale.x * Mathf.Lerp(0.45f, 1f, t),
                _initialScale.y * scaleFactor,
                _initialScale.z * Mathf.Lerp(0.45f, 1f, t)
            );

            transform.localScale = newScale;

            // Lower slightly so it stays grounded as it shrinks
            float heightDiff = (_initialScale.y - newScale.y) * 0.35f;
            transform.localPosition = new Vector3(_initialPosition.x, _initialPosition.y - heightDiff, _initialPosition.z);
        }
    }
}
