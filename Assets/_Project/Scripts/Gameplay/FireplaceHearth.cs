using UnityEngine;
using SnowFactory.Core;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// Represents a fireplace with an active fire where shoveled snow can be thrown/deposited.
    /// Pulses fire light and reacts with a flare/steam burst when snow is thrown into it.
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

        private int _totalSnowDeposited;
        private float _reactionTimer;
        private Vector3 _baseFlameScale = Vector3.one;

        public int TotalSnowDeposited => _totalSnowDeposited;

        private void Awake()
        {
            if (flameCore != null)
            {
                _baseFlameScale = flameCore.localScale;
            }
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
            float reactionBoost = _reactionTimer > 0f ? 2.5f : 0f;

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
                float pulse = 1f + (noise - 0.5f) * 0.22f + (_reactionTimer > 0f ? 0.35f : 0f);
                flameCore.localScale = new Vector3(
                    _baseFlameScale.x * pulse,
                    _baseFlameScale.y * (pulse * 1.1f),
                    _baseFlameScale.z * pulse
                );
            }
        }

        public void DepositSnow()
        {
            _totalSnowDeposited++;
            _reactionTimer = 0.45f;

            if (steamBurstParticles != null)
            {
                steamBurstParticles.Play();
            }

            FactoryEvents.RaiseSnowBurnedCountChanged(_totalSnowDeposited);
            FactoryEvents.RaiseStatusMessage($"¡Nieve arrojada al fuego de la chimenea! (Total: {_totalSnowDeposited})");
        }
    }
}
