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
        private FirstPersonController _firstPersonController;
        private WheelbarrowController _drivingWheelbarrow;
        private Material _snowMaterial;

        private Material _shovelSnowMaterialInstance;
        private Material _flyingChunkMaterialInstance;
        private Mesh _shovelSnowMesh;
        private GameObject _shovelSnowMoundGo;
        private ProceduralSnowMeshUtility.ContainerCavityMap _panCavityMap;
        private readonly List<FlyingSnowChunk> _flyingChunks = new List<FlyingSnowChunk>();
        private int _clodVariantCounter;

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
            scoopHead = null;
            ResolveReferences();
            if (Application.isPlaying && scoopHead != null)
            {
                BuildMultiClumpBedOnShovel();
                RefreshBladeClumpBed();
            }
        }

        public void SetWheelbarrowControlActive(bool driving, WheelbarrowController wheelbarrow)
        {
            _drivingWheelbarrow = driving ? wheelbarrow : null;
            if (shovelPivot != null)
            {
                shovelPivot.gameObject.SetActive(!driving);
            }
        }

        private void ResolveReferences()
        {
            if (_firstPersonController == null)
            {
                _firstPersonController = GetComponent<FirstPersonController>();
                if (_firstPersonController == null)
                {
                    _firstPersonController = GetComponentInParent<FirstPersonController>();
                }
            }

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
                _snowMaterial = ProceduralSnowMeshUtility.FindSceneSnowMaterial();
            }
        }

        private void BuildMultiClumpBedOnShovel()
        {
            if (scoopHead == null) return;

            ProceduralSnowMeshUtility.GetOrCreateCarriedSnowMaterial(
                ref _shovelSnowMaterialInstance,
                _snowMaterial,
                "M_ShovelSnowRuntime",
                ProceduralSnowMeshUtility.CarriedSnowMaterialProfile.ShovelMound);

            Transform existingRoot = scoopHead.Find("ClumpBedRoot");
            if (existingRoot != null)
            {
                Destroy(existingRoot.gameObject);
            }

            if (_shovelSnowMesh != null)
            {
                Destroy(_shovelSnowMesh);
                _shovelSnowMesh = null;
            }
            _shovelSnowMoundGo = null;
            _panCavityMap = null;

            GameObject bedRoot = new GameObject("ClumpBedRoot");
            bedRoot.transform.SetParent(scoopHead, false);
            bedRoot.transform.localPosition = Vector3.zero;
            bedRoot.transform.localRotation = Quaternion.identity;

            Transform plasticTr = scoopHead.Find("Sketchfab_ShovelSnow_Model/Shovel_BlackPlastic");
            MeshFilter plasticMf = plasticTr != null ? plasticTr.GetComponent<MeshFilter>() : null;
            if (plasticMf == null)
            {
                MeshFilter[] filters = scoopHead.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    if (filters[i].name == "Shovel_BlackPlastic")
                    {
                        plasticMf = filters[i];
                        break;
                    }
                }
            }

            if (plasticMf != null && plasticMf.sharedMesh != null)
            {
                Matrix4x4 plasticToScoopHead = Matrix4x4.identity;
                Transform cursor = plasticMf.transform;
                while (cursor != null && cursor != scoopHead)
                {
                    plasticToScoopHead = Matrix4x4.TRS(cursor.localPosition, cursor.localRotation, cursor.localScale) * plasticToScoopHead;
                    cursor = cursor.parent;
                }
                if (cursor != scoopHead)
                {
                    plasticToScoopHead = scoopHead.worldToLocalMatrix * plasticMf.transform.localToWorldMatrix;
                }

                _panCavityMap = ProceduralSnowMeshUtility.BuildShovelPanCavityMap(plasticMf.sharedMesh, plasticToScoopHead);
            }
            else
            {
                _panCavityMap = ProceduralSnowMeshUtility.BuildShovelPanCavityMap(null, Matrix4x4.identity);
            }

            _shovelSnowMesh = new Mesh
            {
                name = "ShovelScoopSnowMesh"
            };
            _shovelSnowMesh.MarkDynamic();

            _shovelSnowMoundGo = new GameObject("ShovelSnowMound");
            _shovelSnowMoundGo.transform.SetParent(bedRoot.transform, false);
            _shovelSnowMoundGo.transform.localPosition = Vector3.zero;
            _shovelSnowMoundGo.transform.localRotation = Quaternion.identity;
            _shovelSnowMoundGo.transform.localScale = Vector3.one;

            MeshFilter mf = _shovelSnowMoundGo.AddComponent<MeshFilter>();
            mf.sharedMesh = _shovelSnowMesh;

            MeshRenderer mr = _shovelSnowMoundGo.AddComponent<MeshRenderer>();
            Material matToUse = _shovelSnowMaterialInstance != null ? _shovelSnowMaterialInstance : _snowMaterial;
            if (matToUse != null)
            {
                mr.sharedMaterial = matToUse;
            }

            _shovelSnowMoundGo.SetActive(false);
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
            if (_drivingWheelbarrow != null)
            {
                UpdateDrivingWheelbarrowInteraction();
                return;
            }

            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            SnowPile hoveredPile = null;
            FireplaceHearth hoveredFireplace = null;
            WheelbarrowController hoveredWheelbarrow = null;
            Vector3 targetHitPoint = Vector3.zero;

            RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Max(interactDistance, 4.0f));
            float closestPileDist = float.MaxValue;
            float closestFireDist = float.MaxValue;
            float closestWbDist = float.MaxValue;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit h = hits[i];
                if (h.distance <= interactDistance)
                {
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

                WheelbarrowController wb = h.collider.GetComponentInParent<WheelbarrowController>();
                if (wb != null && h.distance < closestWbDist)
                {
                    closestWbDist = h.distance;
                    hoveredWheelbarrow = wb;
                }
            }

            if (_continuousDigCooldown > 0f)
            {
                _continuousDigCooldown -= Time.deltaTime;
            }

            WheelbarrowController nearbyWheelbarrow = hoveredWheelbarrow != null
                ? hoveredWheelbarrow
                : FindNearestWheelbarrow(3.4f);

            string testTag = infiniteTestingMode ? "   |   [I] MODO INFINITO: ACTIVO" : "   |   [I] Pala Infinita (Test)";
            string thermalUpgradeTag = _thermalEdgeLevel < 3
                ? $"   |   [T] Filo Térmico Nv.{_thermalEdgeLevel + 1} ({nextThermalUpgradeCost} Monedas)"
                : "   |   Filo Térmico MÁX (Nv.3)";
            string driveTag = nearbyWheelbarrow != null ? "   |   [F] Conducir Carretilla" : "";

            if (hoveredFireplace != null && _currentSnowLoad > 0)
            {
                FactoryEvents.RaisePromptChanged(
                    $"[Clic Izq / E] Lanzar {_currentSnowLoad} palada(s) al fuego (Valor x{(float)Mathf.Max(1, _accumulatedValueUnits) / _currentSnowLoad:F1})   |   [U] Pala Ancha ({nextUpgradeCost}){thermalUpgradeTag}");
            }
            else if (hoveredWheelbarrow != null)
            {
                if (_currentSnowLoad > 0 && hoveredWheelbarrow.HasCapacity)
                {
                    FactoryEvents.RaisePromptChanged(
                        $"[Clic Izq / E] Echar {_currentSnowLoad} palada(s) en la carretilla ({hoveredWheelbarrow.CurrentLoad}/{hoveredWheelbarrow.MaxCapacity})   |   [F] Conducir carretilla{thermalUpgradeTag}");
                }
                else if (_currentSnowLoad > 0 && !hoveredWheelbarrow.HasCapacity)
                {
                    FactoryEvents.RaisePromptChanged(
                        $"¡Carretilla llena ({hoveredWheelbarrow.CurrentLoad}/{hoveredWheelbarrow.MaxCapacity})!   |   [F] Conducir hacia la hoguera");
                }
                else
                {
                    FactoryEvents.RaisePromptChanged(
                        $"[F / E] Conducir carretilla ({hoveredWheelbarrow.CurrentLoad}/{hoveredWheelbarrow.MaxCapacity})   |   Carga nieve con la pala y échala dentro");
                }
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
                        $"[{layerDesc}]{hardnessWarning} — Mantén Clic Izq para excavar ({_currentSnowLoad} acum.){driveTag}{thermalUpgradeTag}");
                }
                else if (_currentSnowLoad < maxSnowCapacity)
                {
                    FactoryEvents.RaisePromptChanged(
                        $"[Clic Izq / E] Excavar {layerDesc}{hardnessWarning} ({_currentSnowLoad}/{maxSnowCapacity} en pala){driveTag}{thermalUpgradeTag}");
                }
                else
                {
                    FactoryEvents.RaisePromptChanged(
                        $"¡Pala llena ({_currentSnowLoad}/{maxSnowCapacity})! Échala a la carretilla o a la chimenea{driveTag}{testTag}");
                }
            }
            else if (_currentSnowLoad > 0)
            {
                FactoryEvents.RaisePromptChanged(
                    $"Pala con {_currentSnowLoad}/{(infiniteTestingMode ? 999 : maxSnowCapacity)} (Valor x{(float)Mathf.Max(1, _accumulatedValueUnits) / _currentSnowLoad:F1}) — Apunta a la carretilla o chimenea{driveTag}{thermalUpgradeTag}{testTag}");
            }
            else
            {
                FactoryEvents.RaisePromptChanged(
                    $"Apunta a la nieve (0/{maxSnowCapacity})   |   [U] Pala Ancha ({nextUpgradeCost})   |   [T] Filo Térmico Nv.{_thermalEdgeLevel}{driveTag}   |   [R] Nevada{testTag}");
            }

            if (Cursor.lockState != CursorLockMode.Locked) return;

            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame && nearbyWheelbarrow != null && _firstPersonController != null)
            {
                nearbyWheelbarrow.EnterDriving(_firstPersonController, this);
                return;
            }

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

            // Priority 2: Pour shovel load into parked wheelbarrow OR mount it when shovel is empty
            if (hoveredWheelbarrow != null && singlePress)
            {
                if (_currentSnowLoad > 0)
                {
                    int loadBefore = _currentSnowLoad;
                    int totalValBefore = Mathf.Max(loadBefore, _accumulatedValueUnits);
                    if (hoveredWheelbarrow.TryReceiveFromShovel(loadBefore, totalValBefore, out int acceptedCount) && acceptedCount > 0)
                    {
                        int transferredVal = acceptedCount >= loadBefore
                            ? totalValBefore
                            : Mathf.Max(acceptedCount, Mathf.RoundToInt(totalValBefore * ((float)acceptedCount / loadBefore)));

                        _currentSnowLoad = Mathf.Max(0, _currentSnowLoad - acceptedCount);
                        _accumulatedValueUnits = Mathf.Max(0, _accumulatedValueUnits - transferredVal);
                        _swingAnimTimer = 0.34f;
                        RefreshBladeClumpBed();
                        FactoryEvents.RaiseShovelLoadChanged(_currentSnowLoad, infiniteTestingMode ? 999 : maxSnowCapacity);

                        SpawnPourChunksToWheelbarrow(
                            hoveredWheelbarrow,
                            acceptedCount,
                            (float)transferredVal / Mathf.Max(1, acceptedCount));
                        FactoryEvents.RaiseStatusMessage(
                            $"Cargadas +{acceptedCount} palada(s) en la carretilla ({hoveredWheelbarrow.CurrentLoad}/{hoveredWheelbarrow.MaxCapacity}).");
                    }
                    else
                    {
                        FactoryEvents.RaiseStatusMessage(
                            $"¡La carretilla ya está llena ({hoveredWheelbarrow.CurrentLoad}/{hoveredWheelbarrow.MaxCapacity})! Pulsa [F] para llevarla a la hoguera.");
                    }
                }
                else if (_firstPersonController != null)
                {
                    hoveredWheelbarrow.EnterDriving(_firstPersonController, this);
                }

                return;
            }

            // Priority 3: Scoop snow from the pile — carve wider crater and launch a chunk from hit.point onto the blade!
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

                    SpawnScoopChunkFromPile(targetHitPoint, scoopedMultiplier);

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

        private void SpawnScoopChunkFromPile(Vector3 hitWorldPoint, float scoopMultiplier)
        {
            if (scoopHead == null)
            {
                RefreshBladeClumpBed();
                return;
            }

            GameObject chunk = CreateFlyingSnowClump(
                new Vector3(Random.Range(0.15f, 0.19f), Random.Range(0.11f, 0.15f), Random.Range(0.14f, 0.18f)),
                _clodVariantCounter++,
                scoopMultiplier);
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

        private void UpdateDrivingWheelbarrowInteraction()
        {
            if (_drivingWheelbarrow == null) return;

            bool nearFire = _drivingWheelbarrow.CanDumpAtFireplace(out FireplaceHearth hearth);
            int load = _drivingWheelbarrow.CurrentLoad;
            int cap = _drivingWheelbarrow.MaxCapacity;

            if (nearFire && load > 0)
            {
                float avgMult = (float)Mathf.Max(1, _drivingWheelbarrow.AccumulatedValueUnits) / load;
                FactoryEvents.RaisePromptChanged(
                    $"[Clic Izq / E] Volcar {load} palada(s) en la hoguera (Valor x{avgMult:F1})   |   [F] Aparcar carretilla");
            }
            else if (load > 0)
            {
                FactoryEvents.RaisePromptChanged(
                    $"Carretilla cargada ({load}/{cap}) — Llévala junto a la hoguera para volcar   |   [WASD + Ratón] Conducir   |   [Shift] Turbo   |   [F] Aparcar");
            }
            else
            {
                FactoryEvents.RaisePromptChanged(
                    $"Conduciendo carretilla (0/{cap}) — Acércate a la nieve y pulsa [F] para bajar y cargarla   |   [WASD + Ratón] Conducir   |   [Shift] Turbo");
            }

            if (Cursor.lockState != CursorLockMode.Locked) return;

            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
            {
                _drivingWheelbarrow.ExitDriving();
                return;
            }

            bool dumpPressed = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                               (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);

            if (dumpPressed && nearFire && load > 0)
            {
                _drivingWheelbarrow.TryDumpIntoFireplace(hearth);
            }
        }

        private WheelbarrowController FindNearestWheelbarrow(float maxDistance)
        {
            WheelbarrowController wb = Object.FindFirstObjectByType<WheelbarrowController>();
            if (wb == null) return null;

            Vector3 pPos = transform.position;
            Vector3 wPos = wb.transform.position;
            float distHoriz = Vector2.Distance(new Vector2(pPos.x, pPos.z), new Vector2(wPos.x, wPos.z));
            return distHoriz <= maxDistance ? wb : null;
        }

        private void SpawnPourChunksToWheelbarrow(WheelbarrowController wb, int pouredCount, float avgMultiplier = 1f)
        {
            if (wb == null) return;
            Vector3 startPos = scoopHead != null ? scoopHead.position : transform.position + transform.forward * 0.6f;
            Vector3 targetPos = wb.GetTrayReceiveWorldPoint();

            int chunkCount = Mathf.Clamp(pouredCount, 1, 5);
            for (int i = 0; i < chunkCount; i++)
            {
                Vector3 jitter = new Vector3(
                    Random.Range(-0.14f, 0.14f),
                    Random.Range(0.0f, 0.08f),
                    Random.Range(-0.14f, 0.14f));

                GameObject chunk = CreateFlyingSnowClump(
                    new Vector3(Random.Range(0.15f, 0.19f), Random.Range(0.11f, 0.15f), Random.Range(0.14f, 0.18f)),
                    i + _clodVariantCounter++,
                    avgMultiplier);
                chunk.transform.position = startPos + jitter * 0.25f;

                _flyingChunks.Add(new FlyingSnowChunk
                {
                    ChunkTransform = chunk.transform,
                    FollowTarget = null,
                    StartWorldPos = chunk.transform.position,
                    EndWorldPos = targetPos + jitter,
                    Elapsed = -i * 0.025f,
                    Duration = 0.22f,
                    ArcHeight = 0.25f + i * 0.03f,
                    IsScoopInward = false,
                    TargetFireplace = null,
                    DeliveredScoopReward = 0,
                    DeliveredValueUnits = 0
                });
            }
        }

        private void SpawnThrowChunksToFireplace(FireplaceHearth fireplace, int deliveredScoops, int deliveredValueUnits)
        {
            Vector3 startPos = scoopHead != null ? scoopHead.position : transform.position + transform.forward * 0.6f;
            Vector3 targetFireCenter = fireplace.transform.position + new Vector3(0f, 0.68f, 0f);
            float avgMultiplier = (float)deliveredValueUnits / Mathf.Max(1, deliveredScoops);

            int visualChunkCount = Mathf.Clamp(deliveredScoops, 1, 6);
            for (int i = 0; i < visualChunkCount; i++)
            {
                Vector3 jitter = new Vector3(
                    Random.Range(-0.18f, 0.18f),
                    Random.Range(-0.05f, 0.12f),
                    Random.Range(-0.12f, 0.12f));

                GameObject chunk = CreateFlyingSnowClump(
                    new Vector3(Random.Range(0.16f, 0.21f), Random.Range(0.12f, 0.16f), Random.Range(0.15f, 0.20f)),
                    i + _clodVariantCounter++,
                    avgMultiplier);
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

        private GameObject CreateFlyingSnowClump(Vector3 scale, int variant, float avgMultiplier)
        {
            GameObject go = new GameObject("FlyingSnowClump");
            go.transform.localScale = scale;
            go.transform.rotation = Random.rotation;

            int iceTier = ProceduralSnowMeshUtility.HardnessMultiplierToIceTier(avgMultiplier);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = ProceduralSnowMeshUtility.GetOrCreateSharedSnowClodMesh(variant, iceTier);

            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            Material baseMat = _shovelSnowMaterialInstance != null ? _shovelSnowMaterialInstance : _snowMaterial;
            Material matToUse = ProceduralSnowMeshUtility.GetOrCreateCarriedSnowMaterial(
                ref _flyingChunkMaterialInstance,
                baseMat,
                "M_ShovelFlyingChunkRuntime",
                ProceduralSnowMeshUtility.CarriedSnowMaterialProfile.FlyingClod,
                avgMultiplier);
            if (matToUse != null)
            {
                mr.sharedMaterial = matToUse;
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
                    ? item.FollowTarget.TransformPoint(new Vector3(0f, 0.03f, 0.04f))
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
            if (_shovelSnowMoundGo == null || _shovelSnowMesh == null || _panCavityMap == null) return;

            if (_currentSnowLoad <= 0)
            {
                _shovelSnowMoundGo.SetActive(false);
                return;
            }

            _shovelSnowMoundGo.SetActive(true);
            float avgMultiplier = Mathf.Clamp((float)_accumulatedValueUnits / Mathf.Max(1, _currentSnowLoad), 1f, 3f);
            ProceduralSnowMeshUtility.ApplyCarriedSnowHardnessTint(_shovelSnowMaterialInstance, avgMultiplier);

            float fillRatio = Mathf.Clamp01((float)_currentSnowLoad / Mathf.Max(1, maxSnowCapacity));
            ProceduralSnowMeshUtility.UpdateShovelSnowMesh(_shovelSnowMesh, _panCavityMap, fillRatio, avgMultiplier);
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _flyingChunks.Count; i++)
            {
                if (_flyingChunks[i].ChunkTransform != null)
                {
                    Destroy(_flyingChunks[i].ChunkTransform.gameObject);
                }
            }
            _flyingChunks.Clear();

            if (_shovelSnowMesh != null)
            {
                Destroy(_shovelSnowMesh);
                _shovelSnowMesh = null;
            }

            if (_shovelSnowMaterialInstance != null)
            {
                Destroy(_shovelSnowMaterialInstance);
                _shovelSnowMaterialInstance = null;
            }

            if (_flyingChunkMaterialInstance != null)
            {
                Destroy(_flyingChunkMaterialInstance);
                _flyingChunkMaterialInstance = null;
            }
        }

        private void AnimateShovelSwing()
        {
            if (_drivingWheelbarrow != null || shovelPivot == null) return;

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
