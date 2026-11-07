using UnityEngine;
using UnityEngine.InputSystem;
using SnowFactory.Core;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// First-person equipped shovel controller with stackable snow capacity.
    /// Allows scooping multiple snow loads onto the blade (growing visually in size and height)
    /// before delivering the full stack into the FireplaceHearth for Heat Coins + Batch Bonus.
    /// Also supports upgrading shovel capacity with [U] and refilling depleted snow piles with [R].
    /// </summary>
    public class ShovelController : MonoBehaviour
    {
        [Header("Shovel References")]
        [SerializeField] private Transform shovelPivot;
        [SerializeField] private GameObject heldSnowVisual;
        [SerializeField] private float interactDistance = 5.5f;

        [Header("Stackable Capacity & Upgrades")]
        [SerializeField] private int maxSnowCapacity = 3;
        [SerializeField] private int upgradeCapacityStep = 2;
        [SerializeField] private int nextUpgradeCost = 30;

        private int _currentSnowLoad;
        private float _swingAnimTimer;
        private Quaternion _restLocalRotation;
        private Vector3 _restLocalPosition;
        private Vector3 _baseSnowLocalPos = new Vector3(0f, 0.055f, 0.01f);
        private Vector3 _baseSnowLocalScale = new Vector3(0.27f, 0.13f, 0.26f);
        private FireplaceHearth _cachedHearth;

        public int CurrentSnowLoad => _currentSnowLoad;
        public int MaxSnowCapacity => maxSnowCapacity;
        public int NextUpgradeCost => nextUpgradeCost;
        public bool HasSnowLoaded => _currentSnowLoad > 0;
        public bool IsFull => _currentSnowLoad >= maxSnowCapacity;

        private void Awake()
        {
            if (shovelPivot != null)
            {
                _restLocalPosition = shovelPivot.localPosition;
                _restLocalRotation = shovelPivot.localRotation;
            }

            if (heldSnowVisual != null)
            {
                _baseSnowLocalPos = heldSnowVisual.transform.localPosition;
                _baseSnowLocalScale = heldSnowVisual.transform.localScale;
                heldSnowVisual.SetActive(false);
            }
        }

        private void Start()
        {
            _cachedHearth = Object.FindFirstObjectByType<FireplaceHearth>();
            FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, maxSnowCapacity);
        }

        public void BindShovelVisuals(Transform pivot, GameObject snowChunkOnBlade)
        {
            shovelPivot = pivot;
            heldSnowVisual = snowChunkOnBlade;

            if (shovelPivot != null)
            {
                _restLocalPosition = shovelPivot.localPosition;
                _restLocalRotation = shovelPivot.localRotation;
            }

            if (heldSnowVisual != null)
            {
                _baseSnowLocalPos = heldSnowVisual.transform.localPosition;
                _baseSnowLocalScale = heldSnowVisual.transform.localScale;
                heldSnowVisual.SetActive(false);
            }
        }

        private void Update()
        {
            UpdateHoverAndInteract();
            HandleUpgradeAndRefillShortcuts();
            AnimateShovelSwing();
        }

        private void UpdateHoverAndInteract()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            SnowPile hoveredPile = null;
            FireplaceHearth hoveredFireplace = null;

            if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
            {
                hoveredPile = hit.collider.GetComponentInParent<SnowPile>();
                hoveredFireplace = hit.collider.GetComponentInParent<FireplaceHearth>();
            }

            // Build contextual prompt based on current stack load and what the player is looking at
            if (hoveredFireplace != null && _currentSnowLoad > 0)
            {
                FactoryEvents.RaisePromptChanged(
                    $"[Clic Izq / E] Echar {_currentSnowLoad} palada(s) al fuego   |   [U] Mejorar Capacidad (+{upgradeCapacityStep}) por {nextUpgradeCost} Monedas");
            }
            else if (hoveredPile != null && hoveredPile.HasSnow)
            {
                if (_currentSnowLoad < maxSnowCapacity)
                {
                    FactoryEvents.RaisePromptChanged(
                        $"[Clic Izq / E] Recoger nieve en la pala ({_currentSnowLoad}/{maxSnowCapacity} cargadas) — Quedan {hoveredPile.RemainingScoops} en el montón");
                }
                else
                {
                    FactoryEvents.RaisePromptChanged(
                        $"¡Pala llena al máximo ({_currentSnowLoad}/{maxSnowCapacity})! Llévala a la chimenea para fundirla");
                }
            }
            else if (_currentSnowLoad > 0)
            {
                if (_currentSnowLoad < maxSnowCapacity)
                {
                    FactoryEvents.RaisePromptChanged(
                        $"Pala con {_currentSnowLoad}/{maxSnowCapacity} de nieve — Sigue recogiendo más o échalas a la chimenea");
                }
                else
                {
                    FactoryEvents.RaisePromptChanged(
                        $"¡Pala llena ({_currentSnowLoad}/{maxSnowCapacity})! Apunta a la chimenea con fuego para vaciarla");
                }
            }
            else
            {
                FactoryEvents.RaisePromptChanged(
                    $"Apunta al montón de nieve para cargar la pala (0/{maxSnowCapacity})   |   [U] Mejorar Pala ({nextUpgradeCost} Monedas)   |   [R] Nueva Nevada");
            }

            if (Cursor.lockState != CursorLockMode.Locked) return;

            bool actionPressed = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                                 (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);

            if (!actionPressed) return;

            // Priority 1: If looking at the fireplace and carrying at least 1 scoop, throw all accumulated scoops into the fire!
            if (hoveredFireplace != null && _currentSnowLoad > 0)
            {
                int deliveredCount = _currentSnowLoad;
                _currentSnowLoad = 0;
                _swingAnimTimer = 0.34f;
                UpdateHeldSnowStackVisual();
                FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, maxSnowCapacity);
                hoveredFireplace.DepositSnow(deliveredCount);
                return;
            }

            // Priority 2: If looking at a snow pile and shovel is not yet at max capacity, scoop and stack onto the blade!
            if (hoveredPile != null && hoveredPile.HasSnow)
            {
                if (_currentSnowLoad >= maxSnowCapacity)
                {
                    FactoryEvents.RaiseStatusMessage(
                        $"¡La pala ya está llena ({_currentSnowLoad}/{maxSnowCapacity})! Echa la nieve a la chimenea o pulsa [U] para ampliar capacidad.");
                    return;
                }

                if (hoveredPile.TryScoopAtPoint(hit.point))
                {
                    _currentSnowLoad++;
                    _swingAnimTimer = 0.30f;
                    UpdateHeldSnowStackVisual();
                    FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, maxSnowCapacity);

                    FactoryEvents.RaiseStatusMessage(
                        $"Nieve apilada en la pala: {_currentSnowLoad}/{maxSnowCapacity} (Quedan {hoveredPile.RemainingScoops} en este montón)");
                }
            }
        }

        private void HandleUpgradeAndRefillShortcuts()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // [U] Upgrade Shovel Capacity using Heat Coins
            if (kb.uKey.wasPressedThisFrame)
            {
                if (_cachedHearth == null)
                {
                    _cachedHearth = Object.FindFirstObjectByType<FireplaceHearth>();
                }

                if (_cachedHearth != null && _cachedHearth.TrySpendHeatPoints(nextUpgradeCost))
                {
                    maxSnowCapacity += upgradeCapacityStep;
                    nextUpgradeCost = Mathf.RoundToInt(nextUpgradeCost * 1.65f);
                    UpdateHeldSnowStackVisual();
                    FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, maxSnowCapacity);
                    FactoryEvents.RaiseStatusMessage(
                        $"¡Pala Mejorada! Ahora puedes apilar hasta {maxSnowCapacity} paladas de nieve a la vez.");
                }
                else
                {
                    int currentCoins = _cachedHearth != null ? _cachedHearth.HeatPoints : 0;
                    FactoryEvents.RaiseStatusMessage(
                        $"Necesitas {nextUpgradeCost} Monedas de Calor para mejorar la pala (Tienes {currentCoins}). ¡Echa más nieve al fuego!");
                }
            }

            // [R] Call a fresh snowfall to refill all snow piles when the player wants more snow
            if (kb.rKey.wasPressedThisFrame)
            {
                SnowPile[] piles = Object.FindObjectsByType<SnowPile>(FindObjectsSortMode.None);
                foreach (SnowPile pile in piles)
                {
                    pile.RefillPile();
                }
                FactoryEvents.RaiseStatusMessage("¡Nueva ventisca! Todos los montones de nieve se han regenerado al 100%.");
            }
        }

        /// <summary>
        /// Visually grows the snow mound sitting on the shovel blade as more scoops are stacked.
        /// </summary>
        private void UpdateHeldSnowStackVisual()
        {
            if (heldSnowVisual == null) return;

            if (_currentSnowLoad <= 0)
            {
                heldSnowVisual.SetActive(false);
                return;
            }

            heldSnowVisual.SetActive(true);

            // Grow width slightly and height substantially with each stacked scoop so the player sees a big mound forming on the blade
            int extraScoops = _currentSnowLoad - 1;
            float widthMult = 1.0f + Mathf.Min(extraScoops * 0.12f, 0.65f);
            float heightMult = 1.0f + extraScoops * 0.48f;
            float lengthMult = 1.0f + Mathf.Min(extraScoops * 0.10f, 0.55f);

            Vector3 newScale = new Vector3(
                _baseSnowLocalScale.x * widthMult,
                _baseSnowLocalScale.y * heightMult,
                _baseSnowLocalScale.z * lengthMult
            );

            heldSnowVisual.transform.localScale = newScale;

            // Shift upward as height grows so the bottom of the snow mound stays planted inside the shovel pan
            float extraHeightOffset = (newScale.y - _baseSnowLocalScale.y) * 0.42f;
            heldSnowVisual.transform.localPosition = new Vector3(
                _baseSnowLocalPos.x,
                _baseSnowLocalPos.y + extraHeightOffset,
                _baseSnowLocalPos.z
            );
        }

        private void AnimateShovelSwing()
        {
            if (shovelPivot == null) return;

            if (_swingAnimTimer > 0f)
            {
                _swingAnimTimer -= Time.deltaTime;
                float t = Mathf.Sin((1f - (_swingAnimTimer / 0.32f)) * Mathf.PI);
                shovelPivot.localPosition = _restLocalPosition + new Vector3(0f, -0.08f * t, 0.18f * t);
                shovelPivot.localRotation = _restLocalRotation * Quaternion.Euler(26f * t, -6f * t, 0f);
            }
            else
            {
                float bob = Mathf.Sin(Time.time * 2.4f) * 0.008f;
                // Slight weight sag when carrying a heavy stack of snow
                float weightDip = _currentSnowLoad * 0.006f;
                shovelPivot.localPosition = _restLocalPosition + new Vector3(0f, bob - weightDip, 0f);
                shovelPivot.localRotation = _restLocalRotation;
            }
        }
    }
}
