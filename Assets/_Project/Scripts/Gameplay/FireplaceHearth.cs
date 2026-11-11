using UnityEngine;
using SnowFactory.Core;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// Represents a fireplace with an active fire where shoveled snow can be thrown/deposited.
    /// Converts melted snow into Heat Coins / Points, with multi-scoop batch bonuses,
    /// and allows spending coins to upgrade the shovel's capacity.
    /// </summary>
    public class FireplaceHearth : MonoBehaviour
    {
        [Header("Fireplace References")]
        [SerializeField] private Light fireLight;
        [SerializeField] private Transform flameCore;
        [SerializeField] private ParticleSystem steamBurstParticles;

        [Header("Fire Animation")]
        [SerializeField] private float baseLightIntensity = 4.5f;
        [SerializeField] private float flickerSpeed = 8f;

        [Header("Economy & Rewards")]
        [SerializeField] private int basePointsPerScoop = 10;
        [SerializeField] private int multiScoopBonusPerExtra = 5;

        private int _totalSnowDeposited;
        private int _heatPoints;
        private float _reactionTimer;
        private float _reactionScaleBoost;
        private Vector3 _baseFlameScale = Vector3.one;

        public int TotalSnowDeposited => _totalSnowDeposited;
        public int HeatPoints => _heatPoints;

        private void Awake()
        {
            if (flameCore != null)
            {
                _baseFlameScale = flameCore.localScale;
            }
        }

        private void Start()
        {
            FactoryEvents.RaiseSnowBurnedCountChanged(_totalSnowDeposited);
            FactoryEvents.RaiseHeatPointsChanged(_heatPoints);
        }

        public void BindReferences(Light lightSource, Transform flameTransform, ParticleSystem steamFx)
        {
            fireLight = lightSource;
            flameCore = flameTransform;
            steamBurstParticles = steamFx;
            if (flameCore != null)
            {
                _baseFlameScale = flameCore.localScale;
            }
        }

        private void Update()
        {
            float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, 0.5f);
            float reactionBoost = _reactionTimer > 0f ? (2.0f + _reactionScaleBoost * 1.5f) : 0f;

            if (_reactionTimer > 0f)
            {
                _reactionTimer -= Time.deltaTime;
            }

            if (fireLight != null)
            {
                fireLight.intensity = baseLightIntensity + (noise - 0.5f) * 1.4f + reactionBoost;
            }

            if (flameCore != null)
            {
                float pulse = 1f + (noise - 0.5f) * 0.22f + (_reactionTimer > 0f ? (0.25f + _reactionScaleBoost * 0.15f) : 0f);
                flameCore.localScale = new Vector3(
                    _baseFlameScale.x * pulse,
                    _baseFlameScale.y * (pulse * 1.1f),
                    _baseFlameScale.z * pulse
                );
            }
        }

        /// <summary>
        /// Deposits one or multiple accumulated scoops of snow at once into the fireplace.
        /// Grants base coins * layerValueMultiplier + batch bonus when delivering multiple scoops together.
        /// </summary>
        public int DepositSnow(int scoopCount = 1, int totalBaseValueUnits = 0)
        {
            int count = Mathf.Max(1, scoopCount);
            int effectiveUnits = totalBaseValueUnits > 0 ? totalBaseValueUnits : count;
            _totalSnowDeposited += count;

            // Calculate reward: 10 pts per value unit (Soft Snow = 1x, Compact Ice = 2x, Glacier Core = 3x) + stack bonus
            int baseEarned = effectiveUnits * basePointsPerScoop;
            int batchBonus = count > 1 ? (count - 1) * count * multiScoopBonusPerExtra / 2 : 0;
            int totalEarned = baseEarned + batchBonus;

            _heatPoints += totalEarned;

            _reactionTimer = Mathf.Clamp(0.45f + count * 0.12f, 0.45f, 1.4f);
            _reactionScaleBoost = Mathf.Clamp(effectiveUnits * 0.35f, 0.35f, 2.6f);

            if (steamBurstParticles != null)
            {
                steamBurstParticles.Play();
            }

            FactoryEvents.RaiseSnowBurnedCountChanged(_totalSnowDeposited);
            FactoryEvents.RaiseHeatPointsChanged(_heatPoints);

            string glacierTag = effectiveUnits > count
                ? $" [¡Incluye Hielo/Glaciar x{(float)effectiveUnits / count:F1}!]"
                : string.Empty;

            if (batchBonus > 0)
            {
                FactoryEvents.RaiseStatusMessage(
                    $"¡+{totalEarned} Monedas de Calor! ({count} paladas fundidas{glacierTag}, +{batchBonus} Bonus de Pila)");
            }
            else
            {
                FactoryEvents.RaiseStatusMessage(
                    $"¡+{totalEarned} Monedas de Calor! (1 palada fundida en la chimenea{glacierTag})");
            }

            return totalEarned;
        }

        public bool TrySpendHeatPoints(int amount)
        {
            if (_heatPoints < amount)
            {
                return false;
            }

            _heatPoints -= amount;
            FactoryEvents.RaiseHeatPointsChanged(_heatPoints);
            return true;
        }
    }
}
