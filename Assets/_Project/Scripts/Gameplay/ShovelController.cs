using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SnowFactory.Core;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// First-person equipped shovel controller combining:
    /// - Option A: Organic multi-clump snow bed filling the concave pan horizontally (never a giant vertical egg)
    ///   + physical parabolic flying snow chunks when scooping from a drift and when throwing into the fireplace.
    /// - Option B: Shovel upgrades [U] physically widen the metal ScoopHead blade so a larger pan holds more snow
    ///   naturally and carves wider craters per scoop.
    /// </summary>
    public class ShovelController : MonoBehaviour
    {
        private class FlyingSnowChunk
        {
            public Transform ChunkTransform;
            public Transform FollowTarget;
            public Vector3 StartWorldPos;
            public Vector3 EndWorldPos;
            public float Elapsed;
            public float Duration;
            public float ArcHeight;
            public bool IsScoopInward;
            public FireplaceHearth TargetFireplace;
            public int DeliveredScoopReward;
            public int DeliveredValueUnits;
        }

        [Header("Shovel References")]
        [SerializeField] private Transform shovelPivot;
        [SerializeField] private Transform scoopHead;
        [SerializeField] private GameObject heldSnowVisual;
        [SerializeField] private float interactDistance = 5.5f;

        [Header("Capacity & Wide-Blade Upgrades")]
        [SerializeField] private int maxSnowCapacity = 3;
        [SerializeField] private int upgradeCapacityStep = 2;
        [SerializeField] private int nextUpgradeCost = 30;
        [SerializeField] private int nextThermalUpgradeCost = 40;
        [SerializeField] private bool infiniteTestingMode;

        private int _currentSnowLoad;
        private int _accumulatedValueUnits;
        private int _bladeUpgradeLevel;
        private int _thermalEdgeLevel = 1;
        private float _swingAnimTimer;
        private float _continuousDigCooldown;
        private Quaternion _restLocalRotation;
        private Vector3 _restLocalPosition;
        private Vector3 _baseScoopHeadScale = Vector3.one;
        private FireplaceHearth _cachedHearth;
        private Material _snowMaterial;

        private readonly List<Transform> _bladeClumpVisuals = new List<Transform>();
        private readonly List<FlyingSnowChunk> _flyingChunks = new List<FlyingSnowChunk>();

        // Pre-computed organic positions inside the concave pan (rear-to-front, left-to-right, low profile!)
        private static readonly Vector3[] ClumpLocalOffsets =
        {
            new Vector3(-0.07f, 0.015f, -0.01f),
            new Vector3( 0.07f, 0.016f,  0.00f),
            new Vector3( 0.00f, 0.014f,  0.06f),
            new Vector3(-0.09f, 0.018f,  0.06f),
            new Vector3( 0.09f, 0.018f,  0.06f),
            new Vector3( 0.00f, 0.036f,  0.02f),
            new Vector3(-0.06f, 0.034f,  0.05f),
            new Vector3( 0.06f, 0.034f,  0.05f),
            new Vector3( 0.00f, 0.028f,  0.10f)
        };

        private static readonly Vector3[] ClumpLocalScales =
        {
            new Vector3(0.18f, 0.065f, 0.16f),
            new Vector3(0.17f, 0.068f, 0.16f),
            new Vector3(0.19f, 0.060f, 0.16f),
            new Vector3(0.16f, 0.062f, 0.15f),
            new Vector3(0.16f, 0.062f, 0.15f),
            new Vector3(0.20f, 0.065f, 0.17f),
            new Vector3(0.17f, 0.060f, 0.15f),
            new Vector3(0.17f, 0.060f, 0.15f),
            new Vector3(0.18f, 0.055f, 0.14f)
        };

        public int CurrentSnowLoad => _currentSnowLoad;
        public int MaxSnowCapacity => maxSnowCapacity;
        public int NextUpgradeCost => nextUpgradeCost;

        private void Awake()
        {
            ResolveReferences();
            BuildMultiClumpBedOnShovel();
        }

        private void Start()
        {
            _cachedHearth = Object.FindFirstObjectByType<FireplaceHearth>();
            FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, maxSnowCapacity);
            FactoryEvents.RaiseThermalEdgeLevelChanged(_thermalEdgeLevel);
        }

        public void BindShovelVisuals(Transform pivot, GameObject snowChunkOnBlade)
        {
            shovelPivot = pivot;
            heldSnowVisual = snowChunkOnBlade;
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (shovelPivot != null)
            {
                _restLocalPosition = shovelPivot.localPosition;
                _restLocalRotation = shovelPivot.localRotation;

                if (scoopHead == null)
                {
                    scoopHead = shovelPivot.Find("ScoopHead");
                }
            }

            if (scoopHead != null)
            {
                _baseScoopHeadScale = scoopHead.localScale;
            }

            if (heldSnowVisual != null)
            {
                MeshRenderer mr = heldSnowVisual.GetComponent<MeshRenderer>();
                if (mr != null && mr.sharedMaterial != null)
                {
                    _snowMaterial = mr.sharedMaterial;
                }
                // Hide the old single sphere so it never turns into an egg
                heldSnowVisual.SetActive(false);
            }

            if (_snowMaterial == null)
            {
                SnowPile anyPile = Object.FindFirstObjectByType<SnowPile>();
                if (anyPile != null)
                {
                    MeshRenderer pileMr = anyPile.GetComponent<MeshRenderer>();
                    if (pileMr != null)
                    {
                        _snowMaterial = pileMr.sharedMaterial;
                    }
                }
            }
        }

        private void BuildMultiClumpBedOnShovel()
        {
            if (scoopHead == null) return;

            Transform existingRoot = scoopHead.Find("ClumpBedRoot");
            if (existingRoot != null)
            {
                Destroy(existingRoot.gameObject);
            }

            GameObject bedRoot = new GameObject("ClumpBedRoot");
            bedRoot.transform.SetParent(scoopHead, false);
            bedRoot.transform.localPosition = Vector3.zero;
            bedRoot.transform.localRotation = Quaternion.identity;

            _bladeClumpVisuals.Clear();

            for (int i = 0; i < ClumpLocalOffsets.Length; i++)
            {
                GameObject clump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                clump.name = $"SnowClump_{i}";
                clump.transform.SetParent(bedRoot.transform, false);
                clump.transform.localPosition = ClumpLocalOffsets[i];
                clump.transform.localRotation = Quaternion.Euler(
                    (i * 13f) % 18f - 9f,
                    (i * 47f) % 360f,
                    (i * 19f) % 16f - 8f);
                clump.transform.localScale = ClumpLocalScales[i];

                Collider col = clump.GetComponent<Collider>();
                if (col != null) Destroy(col);

                if (_snowMaterial != null)
                {
                    MeshRenderer mr = clump.GetComponent<MeshRenderer>();
                    if (mr != null) mr.sharedMaterial = _snowMaterial;
                }

                clump.SetActive(false);
                _bladeClumpVisuals.Add(clump.transform);
            }
        }

        private void Update()
        {
            UpdateFlyingSnowChunks();
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
            Vector3 targetHitPoint = Vector3.zero;

            RaycastHit[] hits = Physics.RaycastAll(ray, interactDistance);
            float closestPileDist = float.MaxValue;
            float closestFireDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
                FireplaceHearth fire = h.collider.GetComponentInParent<FireplaceHearth>();
                if (fire != null && h.distance < closestFireDist)
                {
                    closestFireDist = h.distance;
                    hoveredFireplace = fire;
                }

                SnowPile pile = h.collider.GetComponentInParent<SnowPile>();
                if (pile != null && h.distance < closestPileDist)
                {
                    closestPileDist = h.distance;
                    hoveredPile = pile;
                    targetHitPoint = h.point;
                }
            }

            if (_continuousDigCooldown > 0f)
            {
                _continuousDigCooldown -= Time.deltaTime;
            }

            string testTag = infiniteTestingMode ? "   |   [I] MODO INFINITO: ACTIVO" : "   |   [I] Pala Infinita (Test)";
            string thermalUpgradeTag = _thermalEdgeLevel < 3
                ? $"   |   [T] Filo Térmico Nv.{_thermalEdgeLevel + 1} ({nextThermalUpgradeCost} Monedas)"
                : "   |   Filo Térmico MÁX (Nv.3)";

            if (hoveredFireplace != null && _currentSnowLoad > 0)
            {
                FactoryEvents.RaisePromptChanged(
                    $"[Clic Izq / E] Lanzar {_currentSnowLoad} palada(s) al fuego (Valor x{(float)Mathf.Max(1, _accumulatedValueUnits) / _currentSnowLoad:F1})   |   [U] Pala Ancha ({nextUpgradeCost}){thermalUpgradeTag}");
            }
            else if (hoveredPile != null && (hoveredPile.HasSnow || infiniteTestingMode))
            {
                string layerDesc = hoveredPile.GetLayerInfoAtWorldPoint(targetHitPoint, out int layerTier, out int coinMult);
                string hardnessWarning = (layerTier == 1 && _thermalEdgeLevel < 2)
                    ? " ⚠️ [¡Dura! Mejora Filo Térmico con T]"
                    : (layerTier == 2 && _thermalEdgeLevel < 3 ? " ⚠️ [¡Glaciar Duro! Mejora Filo Térmico con T]" : "");

                if (infiniteTestingMode)
                {
                    FactoryEvents.RaisePromptChanged(
                        $"[{layerDesc}]{hardnessWarning} — Mantén Clic Izq para excavar ({_currentSnowLoad} acum.){thermalUpgradeTag}");
                }
                else if (_currentSnowLoad < maxSnowCapacity)
                {
                    FactoryEvents.RaisePromptChanged(
                        $"[Clic Izq / E] Excavar {layerDesc}{hardnessWarning} ({_currentSnowLoad}/{maxSnowCapacity} en pala){thermalUpgradeTag}");
                }
                else
                {
                    FactoryEvents.RaisePromptChanged(
                        $"¡Pala llena ({_currentSnowLoad}/{maxSnowCapacity})! Apunta a la chimenea para lanzar la nieve al fuego{testTag}");
                }
            }
            else if (_currentSnowLoad > 0)
            {
                FactoryEvents.RaisePromptChanged(
                    $"Pala con {_currentSnowLoad}/{(infiniteTestingMode ? 999 : maxSnowCapacity)} (Valor x{(float)Mathf.Max(1, _accumulatedValueUnits) / _currentSnowLoad:F1}) — Apunta a la chimenea{thermalUpgradeTag}{testTag}");
            }
            else
            {
                FactoryEvents.RaisePromptChanged(
                    $"Apunta a la nieve (0/{maxSnowCapacity})   |   [U] Pala Ancha ({nextUpgradeCost})   |   [T] Filo Térmico Nv.{_thermalEdgeLevel}   |   [R] Nevada{testTag}");
            }

            if (Cursor.lockState != CursorLockMode.Locked) return;

            bool singlePress = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                               (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);

            bool holdDig = infiniteTestingMode &&
                           _continuousDigCooldown <= 0f &&
                           ((Mouse.current != null && Mouse.current.leftButton.isPressed) ||
                            (Keyboard.current != null && Keyboard.current.eKey.isPressed));

            if (!singlePress && !holdDig) return;

            // Priority 1: Throw snow chunks in a physical parabolic arc into the fireplace!
            if (hoveredFireplace != null && _currentSnowLoad > 0 && singlePress)
            {
                int deliveredCount = _currentSnowLoad;
                int deliveredValueUnits = Mathf.Max(deliveredCount, _accumulatedValueUnits);
                _currentSnowLoad = 0;
                _accumulatedValueUnits = 0;
                _swingAnimTimer = 0.36f;
                RefreshBladeClumpBed();
                FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, infiniteTestingMode ? 999 : maxSnowCapacity);

                SpawnThrowChunksToFireplace(hoveredFireplace, deliveredCount, deliveredValueUnits);
                return;
            }

            // Priority 2: Scoop snow from the pile — carve wider crater and launch a chunk from hit.point onto the blade!
            if (hoveredPile != null && (hoveredPile.HasSnow || infiniteTestingMode))
            {
                if (!infiniteTestingMode && _currentSnowLoad >= maxSnowCapacity)
                {
                    FactoryEvents.RaiseStatusMessage(
                        $"¡La cuchara está llena ({_currentSnowLoad}/{maxSnowCapacity})! Lanza la nieve al fuego, pulsa [U] para ensanchar o [I] para Pala Infinita.");
                    return;
                }

                float radiusMult = 1.0f + _bladeUpgradeLevel * 0.22f;
                int effectiveThermalLevel = infiniteTestingMode ? Mathf.Max(_thermalEdgeLevel, 1) : _thermalEdgeLevel;

                if (hoveredPile.TryScoopAtPoint(targetHitPoint, radiusMult, infiniteTestingMode, effectiveThermalLevel))
                {
                    int scoopedTier = hoveredPile.LastScoopTier;
                    int scoopedMultiplier = hoveredPile.LastScoopRewardMultiplier;

                    // If the pile is part of a multi-mesh terrain group (e.g. Sketchfab_LowPolySnowIsland with Terrain + Cave submeshes),
                    // also excavate sibling SnowPile meshes around the same world hit point so no rocky/ice sub-mesh remains floating!
                    Transform parentGroup = hoveredPile.transform.parent;
                    if (parentGroup != null)
                    {
                        SnowPile[] siblingPiles = parentGroup.GetComponentsInChildren<SnowPile>();
                        for (int s = 0; s < siblingPiles.Length; s++)
                        {
                            if (siblingPiles[s] != hoveredPile)
                            {
                                siblingPiles[s].TryScoopAtPoint(targetHitPoint, radiusMult, true, effectiveThermalLevel);
                            }
                        }
                    }

                    _currentSnowLoad++;
                    _accumulatedValueUnits += scoopedMultiplier;
                    _swingAnimTimer = 0.24f;
                    _continuousDigCooldown = 0.11f;
                    FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, infiniteTestingMode ? 999 : maxSnowCapacity);

                    SpawnScoopChunkFromPile(targetHitPoint);

                    if (scoopedTier == 2)
                    {
                        FactoryEvents.RaiseStatusMessage(
                            _thermalEdgeLevel < 3
                                ? $"❄️ ¡Núcleo Glaciar Azul (Valor x3)! Muy duro para tu pala actual (Nv.{_thermalEdgeLevel}) — Pulsa [T] para mejorar el Filo Térmico."
                                : $"💎 ¡Núcleo Glaciar Azul extraído con Filo Térmico Nv.{_thermalEdgeLevel}! Vale TRIPLE Monedas de Calor (x3) en la chimenea.");
                    }
                    else if (scoopedTier == 1)
                    {
                        FactoryEvents.RaiseStatusMessage(
                            _thermalEdgeLevel < 2
                                ? $"🧊 Capa de Hielo Compacto (Valor x2): Con la pala básica apenas cede — Pulsa [T] ({nextThermalUpgradeCost} Monedas) para mejorar el Filo Térmico."
                                : $"🧊 Hielo Compacto cortado limpiamente (Valor x2 Monedas de Calor).");
                    }
                    else if (!infiniteTestingMode)
                    {
                        FactoryEvents.RaiseStatusMessage(
                            $"Nieve Polvo recogida: {_currentSnowLoad}/{maxSnowCapacity} (Valor x1)");
                    }
                }
            }
        }

        private void SpawnScoopChunkFromPile(Vector3 hitWorldPoint)
        {
            if (scoopHead == null)
            {
                RefreshBladeClumpBed();
                return;
            }

            GameObject chunk = CreateFlyingSnowSphere(new Vector3(0.18f, 0.085f, 0.16f));
            chunk.transform.position = hitWorldPoint + Vector3.up * 0.04f;

            _flyingChunks.Add(new FlyingSnowChunk
            {
                ChunkTransform = chunk.transform,
                FollowTarget = scoopHead,
                StartWorldPos = chunk.transform.position,
                EndWorldPos = scoopHead.position,
                Elapsed = 0f,
                Duration = 0.17f,
                ArcHeight = 0.18f,
                IsScoopInward = true
            });
        }

        private void SpawnThrowChunksToFireplace(FireplaceHearth fireplace, int deliveredScoops, int deliveredValueUnits)
        {
            Vector3 startPos = scoopHead != null ? scoopHead.position : transform.position + transform.forward * 0.6f;
            Vector3 targetFireCenter = fireplace.transform.position + new Vector3(0f, 0.68f, 0f);

            int visualChunkCount = Mathf.Clamp(deliveredScoops, 1, 6);
            for (int i = 0; i < visualChunkCount; i++)
            {
                Vector3 jitter = new Vector3(
                    Random.Range(-0.18f, 0.18f),
                    Random.Range(-0.05f, 0.12f),
                    Random.Range(-0.12f, 0.12f));

                GameObject chunk = CreateFlyingSnowSphere(new Vector3(0.20f, 0.095f, 0.18f));
                chunk.transform.position = startPos + jitter * 0.35f;

                _flyingChunks.Add(new FlyingSnowChunk
                {
                    ChunkTransform = chunk.transform,
                    FollowTarget = null,
                    StartWorldPos = chunk.transform.position,
                    EndWorldPos = targetFireCenter + jitter,
                    Elapsed = -i * 0.035f, // slight stagger so multiple clumps fly in a natural burst
                    Duration = 0.28f,
                    ArcHeight = 0.38f + i * 0.04f,
                    IsScoopInward = false,
                    TargetFireplace = i == visualChunkCount - 1 ? fireplace : null,
                    DeliveredScoopReward = deliveredScoops,
                    DeliveredValueUnits = deliveredValueUnits
                });
            }
        }

        private GameObject CreateFlyingSnowSphere(Vector3 scale)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "FlyingSnowClump";
            go.transform.localScale = scale;
            go.transform.rotation = Random.rotation;

            Collider col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            if (_snowMaterial != null)
            {
                MeshRenderer mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.sharedMaterial = _snowMaterial;
            }
            return go;
        }

        private void UpdateFlyingSnowChunks()
        {
            for (int i = _flyingChunks.Count - 1; i >= 0; i--)
            {
                FlyingSnowChunk item = _flyingChunks[i];
                item.Elapsed += Time.deltaTime;

                if (item.Elapsed < 0f)
                {
                    continue;
                }

                float t = Mathf.Clamp01(item.Elapsed / item.Duration);
                Vector3 endPos = item.IsScoopInward && item.FollowTarget != null
                    ? item.FollowTarget.TransformPoint(new Vector3(0f, 0.04f, 0.0f))
                    : item.EndWorldPos;

                Vector3 linearPos = Vector3.Lerp(item.StartWorldPos, endPos, t);
                float parabola = 4f * t * (1f - t) * item.ArcHeight;
                if (item.ChunkTransform != null)
                {
                    item.ChunkTransform.position = linearPos + Vector3.up * parabola;
                }

                if (t >= 1f)
                {
                    if (item.ChunkTransform != null)
                    {
                        Destroy(item.ChunkTransform.gameObject);
                    }

                    if (item.IsScoopInward)
                    {
                        RefreshBladeClumpBed();
                    }
                    else if (item.TargetFireplace != null)
                    {
                        item.TargetFireplace.DepositSnow(item.DeliveredScoopReward, item.DeliveredValueUnits);
                    }

                    _flyingChunks.RemoveAt(i);
                }
            }
        }

        private void HandleUpgradeAndRefillShortcuts()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            // [I] Testing Shortcut: Toggle Infinite Excavation Shovel (no capacity cap, no pile depletion, hold LMB/E to dig continuously)
            if (kb.iKey.wasPressedThisFrame)
            {
                infiniteTestingMode = !infiniteTestingMode;
                FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, infiniteTestingMode ? 999 : maxSnowCapacity);
                FactoryEvents.RaiseStatusMessage(infiniteTestingMode
                    ? "⚡ [MODO TEST] Pala de Excavación Infinita ACTIVADA (Sin límite de carga y puedes mantener pulsado Clic Izquierdo para cavar)."
                    : "[MODO TEST] Pala de Excavación Infinita DESACTIVADA.");
            }

            // [U] Option B: Wide-Blade Shovel Upgrade! Widens the physical metal ScoopHead and increases capacity
            if (kb.uKey.wasPressedThisFrame)
            {
                if (_cachedHearth == null)
                {
                    _cachedHearth = Object.FindFirstObjectByType<FireplaceHearth>();
                }

                if (_cachedHearth != null && _cachedHearth.TrySpendHeatPoints(nextUpgradeCost))
                {
                    _bladeUpgradeLevel++;
                    maxSnowCapacity += upgradeCapacityStep;
                    nextUpgradeCost = Mathf.RoundToInt(nextUpgradeCost * 1.65f);

                    ApplyWideBladeUpgradeVisual();
                    RefreshBladeClumpBed();
                    FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, maxSnowCapacity);
                    FactoryEvents.RaiseStatusMessage(
                        $"¡Pala Ensanchada a Nivel {_bladeUpgradeLevel + 1}! Cuchara más ancha, cráteres mayores y capacidad para {maxSnowCapacity} paladas.");
                }
                else
                {
                    int currentCoins = _cachedHearth != null ? _cachedHearth.HeatPoints : 0;
                    FactoryEvents.RaiseStatusMessage(
                        $"Necesitas {nextUpgradeCost} Monedas de Calor para ensanchar la pala (Tienes {currentCoins}). ¡Echa más nieve al fuego!");
                }
            }

            // [T] Thermal Edge Upgrade! Allows the shovel to slice through Compacted Ice (Lv.2) and Deep Glacier Core (Lv.3)
            if (kb.tKey.wasPressedThisFrame)
            {
                if (_thermalEdgeLevel >= 3)
                {
                    FactoryEvents.RaiseStatusMessage("🔥 ¡Tu Filo Térmico ya está al Nivel Máximo (Nv.3 - Corte Glaciar Total)!");
                }
                else
                {
                    if (_cachedHearth == null)
                    {
                        _cachedHearth = Object.FindFirstObjectByType<FireplaceHearth>();
                    }

                    // Allow free upgrade if Infinite Testing Mode [I] is active so the user can test Thermal Edge levels immediately!
                    bool canAfford = infiniteTestingMode || (_cachedHearth != null && _cachedHearth.TrySpendHeatPoints(nextThermalUpgradeCost));
                    if (canAfford)
                    {
                        _thermalEdgeLevel++;
                        nextThermalUpgradeCost = Mathf.RoundToInt(nextThermalUpgradeCost * 2.0f);
                        ApplyThermalEdgeVisualGlow();
                        FactoryEvents.RaiseThermalEdgeLevelChanged(_thermalEdgeLevel);

                        string tierUnlocked = _thermalEdgeLevel == 2
                            ? "¡Ahora cortas Hielo Compacto (Azul Claro · x2) a máxima velocidad!"
                            : "¡Ahora perforas el Núcleo Glaciar Azul (x3 Monedas) sin resistencia!";

                        FactoryEvents.RaiseStatusMessage($"🔥 ¡Filo Térmico mejorado a Nivel {_thermalEdgeLevel}! {tierUnlocked}");
                    }
                    else
                    {
                        int currentCoins = _cachedHearth != null ? _cachedHearth.HeatPoints : 0;
                        FactoryEvents.RaiseStatusMessage(
                            $"Necesitas {nextThermalUpgradeCost} Monedas de Calor para el Filo Térmico Nv.{_thermalEdgeLevel + 1} (Tienes {currentCoins}). (O activa [I] Test para probarlo gratis).");
                    }
                }
            }

            // [R] Call a fresh snowfall to refill all snow piles
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

        private void ApplyWideBladeUpgradeVisual()
        {
            if (scoopHead == null) return;

            // Widen the physical metal scoop blade horizontally (+16% width per upgrade, up to 1.85x) and slightly deeper
            float widthFactor = Mathf.Min(1.0f + _bladeUpgradeLevel * 0.16f, 1.85f);
            float lengthFactor = Mathf.Min(1.0f + _bladeUpgradeLevel * 0.06f, 1.30f);

            scoopHead.localScale = new Vector3(
                _baseScoopHeadScale.x * widthFactor,
                _baseScoopHeadScale.y,
                _baseScoopHeadScale.z * lengthFactor
            );
        }

        private void ApplyThermalEdgeVisualGlow()
        {
            if (scoopHead == null) return;

            Transform cuttingEdge = scoopHead.Find("BladeCuttingEdge");
            if (cuttingEdge == null) return;

            MeshRenderer mr = cuttingEdge.GetComponent<MeshRenderer>();
            if (mr == null) return;

            Material mat = mr.material;
            Color glowColor = _thermalEdgeLevel >= 3
                ? new Color(1.0f, 0.38f, 0.05f) * 3.2f
                : new Color(1.0f, 0.55f, 0.12f) * 1.8f;

            mat.color = _thermalEdgeLevel >= 3
                ? new Color(1.0f, 0.52f, 0.18f)
                : new Color(1.0f, 0.68f, 0.28f);

            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", glowColor);
            }
        }

        /// <summary>
        /// Activates low-profile snow clumps across the concave shovel pan according to fill ratio.
        /// Never stretches vertically into a giant egg!
        /// </summary>
        private void RefreshBladeClumpBed()
        {
            if (_bladeClumpVisuals.Count == 0) return;

            if (_currentSnowLoad <= 0)
            {
                for (int i = 0; i < _bladeClumpVisuals.Count; i++)
                {
                    _bladeClumpVisuals[i].gameObject.SetActive(false);
                }
                return;
            }

            float fillRatio = Mathf.Clamp01((float)_currentSnowLoad / Mathf.Max(1, maxSnowCapacity));
            int activeClumps = Mathf.Clamp(
                Mathf.CeilToInt(fillRatio * _bladeClumpVisuals.Count),
                1,
                _bladeClumpVisuals.Count);

            for (int i = 0; i < _bladeClumpVisuals.Count; i++)
            {
                bool shouldShow = i < activeClumps;
                _bladeClumpVisuals[i].gameObject.SetActive(shouldShow);
            }
        }

        private void AnimateShovelSwing()
        {
            if (shovelPivot == null) return;

            if (_swingAnimTimer > 0f)
            {
                _swingAnimTimer -= Time.deltaTime;
                float t = Mathf.Sin((1f - (_swingAnimTimer / 0.34f)) * Mathf.PI);
                shovelPivot.localPosition = _restLocalPosition + new Vector3(0f, -0.07f * t, 0.20f * t);
                shovelPivot.localRotation = _restLocalRotation * Quaternion.Euler(24f * t, -6f * t, 0f);
            }
            else
            {
                float bob = Mathf.Sin(Time.time * 2.4f) * 0.008f;
                float fillRatio = Mathf.Clamp01((float)_currentSnowLoad / Mathf.Max(1, maxSnowCapacity));
                float weightDip = fillRatio * 0.022f;
                shovelPivot.localPosition = _restLocalPosition + new Vector3(0f, bob - weightDip, 0f);
                shovelPivot.localRotation = _restLocalRotation;
            }
        }
    }
}
