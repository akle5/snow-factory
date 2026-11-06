using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using SnowFactory.Core;
using SnowFactory.Gameplay;

namespace SnowFactory.Managers
{
    /// <summary>
    /// Handles first-person grid construction: hotbar selection (1-4), ghost hologram preview,
    /// 90-degree rotation (R), placement (LMB), demolition/refund (RMB), and manual snow deposit (E).
    /// </summary>
    public class BuildManager : MonoBehaviour
    {
        public static BuildManager Instance { get; private set; }

        [Header("Build Catalog")]
        [SerializeField] private List<BuildableMachineSO> buildCatalog = new List<BuildableMachineSO>();
        [SerializeField] private float buildReachDistance = 12f;
        [SerializeField] private Material ghostValidMaterial;
        [SerializeField] private Material ghostInvalidMaterial;

        public bool IsBuildMode { get; private set; } = true;
        public int SelectedIndex { get; private set; }
        public int CurrentRotationStep { get; private set; }
        public IReadOnlyList<BuildableMachineSO> BuildCatalog => buildCatalog;

        private GameObject _ghostRoot;
        private MeshRenderer[] _ghostRenderers;
        private Vector2Int _hoveredCell;
        private bool _hasValidHover;
        private PlacedMachine _hoveredMachine;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            CreateGhostPreviewObject();
            SelectSlot(0);
            FactoryEvents.RaiseBuildModeChanged(IsBuildMode);
        }

        public void SetCatalogAndMaterials(List<BuildableMachineSO> catalog, Material validMat, Material invalidMat)
        {
            buildCatalog = catalog;
            ghostValidMaterial = validMat;
            ghostInvalidMaterial = invalidMat;
        }

        private void Update()
        {
            HandleHotkeys();
            UpdateRaycastAndGhost();
            HandleBuildAndInteractActions();
        }

        private void HandleHotkeys()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.bKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
            {
                IsBuildMode = !IsBuildMode;
                if (_ghostRoot != null)
                {
                    _ghostRoot.SetActive(IsBuildMode);
                }
                FactoryEvents.RaiseBuildModeChanged(IsBuildMode);
                FactoryEvents.RaiseStatusMessage(IsBuildMode ? "Modo Construcción Activado" : "Modo Inspección / Operación");
            }

            if (kb.digit1Key.wasPressedThisFrame) SelectSlot(0);
            if (kb.digit2Key.wasPressedThisFrame) SelectSlot(1);
            if (kb.digit3Key.wasPressedThisFrame) SelectSlot(2);
            if (kb.digit4Key.wasPressedThisFrame) SelectSlot(3);

            if (kb.rKey.wasPressedThisFrame)
            {
                CurrentRotationStep = (CurrentRotationStep + 1) % 4;
            }
        }

        public void SelectSlot(int index)
        {
            if (buildCatalog == null || buildCatalog.Count == 0) return;

            SelectedIndex = Mathf.Clamp(index, 0, buildCatalog.Count - 1);
            BuildableMachineSO selected = buildCatalog[SelectedIndex];
            if (selected != null)
            {
                FactoryEvents.RaiseSelectedMachineChanged(SelectedIndex, selected.displayName, selected.buildCost);
            }
        }

        private void UpdateRaycastAndGhost()
        {
            _hasValidHover = false;
            _hoveredMachine = null;

            Camera cam = Camera.main;
            if (cam == null || GridManager.Instance == null)
            {
                if (_ghostRoot != null) _ghostRoot.SetActive(false);
                return;
            }

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            if (Physics.Raycast(ray, out RaycastHit hit, buildReachDistance))
            {
                _hoveredMachine = hit.collider.GetComponentInParent<PlacedMachine>();
                Vector3 samplePoint = _hoveredMachine != null ? _hoveredMachine.transform.position : hit.point;
                _hoveredCell = GridManager.Instance.WorldToCell(samplePoint);
                _hasValidHover = GridManager.Instance.IsWithinBounds(_hoveredCell);
            }

            if (!IsBuildMode || !_hasValidHover || buildCatalog.Count == 0)
            {
                if (_ghostRoot != null) _ghostRoot.SetActive(false);
                return;
            }

            if (_ghostRoot != null)
            {
                _ghostRoot.SetActive(true);
                Vector3 snapPos = GridManager.Instance.CellToWorldCenter(_hoveredCell);
                _ghostRoot.transform.position = snapPos;
                _ghostRoot.transform.rotation = Quaternion.Euler(0f, CurrentRotationStep * 90f, 0f);

                BuildableMachineSO selected = buildCatalog[SelectedIndex];
                bool canBuild = GridManager.Instance.IsCellFree(_hoveredCell) &&
                                EconomyManager.Instance != null &&
                                selected != null &&
                                EconomyManager.Instance.CanAfford(selected.buildCost);

                Material targetMat = canBuild ? ghostValidMaterial : ghostInvalidMaterial;
                if (targetMat != null && _ghostRenderers != null)
                {
                    foreach (MeshRenderer r in _ghostRenderers)
                    {
                        r.sharedMaterial = targetMat;
                    }
                }
            }
        }

        private void HandleBuildAndInteractActions()
        {
            if (Cursor.lockState != CursorLockMode.Locked) return;

            Mouse mouse = Mouse.current;
            Keyboard kb = Keyboard.current;

            // Manual interaction with E key: feed RawSnow into hovered machine or trigger extractor
            if (kb != null && kb.eKey.wasPressedThisFrame && _hoveredMachine != null)
            {
                if (_hoveredMachine.TryAcceptItem(SnowItemType.RawSnow))
                {
                    FactoryEvents.RaiseStatusMessage($"Nieve depositada manualmente en {_hoveredMachine.MachineData?.displayName ?? _hoveredMachine.name}");
                }
            }

            if (!IsBuildMode || !_hasValidHover || mouse == null) return;

            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                TryPlaceSelectedMachine();
            }
            else if ((mouse != null && mouse.rightButton.wasPressedThisFrame) ||
                     (kb != null && kb.xKey.wasPressedThisFrame))
            {
                TryDemolishAtHoveredCell();
            }
        }

        private void TryPlaceSelectedMachine()
        {
            if (buildCatalog == null || SelectedIndex >= buildCatalog.Count) return;

            BuildableMachineSO selected = buildCatalog[SelectedIndex];
            if (selected == null || selected.prefab == null) return;

            if (!GridManager.Instance.IsCellFree(_hoveredCell))
            {
                FactoryEvents.RaiseStatusMessage("Celda ocupada: usa Clic Derecho para demoler");
                return;
            }

            if (EconomyManager.Instance == null || !EconomyManager.Instance.TrySpend(selected.buildCost))
            {
                FactoryEvents.RaiseStatusMessage($"Créditos insuficientes (${selected.buildCost} requeridos)");
                return;
            }

            Vector3 worldPos = GridManager.Instance.CellToWorldCenter(_hoveredCell);
            Quaternion worldRot = Quaternion.Euler(0f, CurrentRotationStep * 90f, 0f);

            PlacedMachine instance = Instantiate(selected.prefab, worldPos, worldRot);
            instance.name = $"{selected.displayName}_{_hoveredCell.x}_{_hoveredCell.y}";
            instance.InitializeData(selected);
            instance.SetupOnGrid(_hoveredCell, CurrentRotationStep);

            GridManager.Instance.RegisterMachine(_hoveredCell, instance);
            FactoryEvents.RaiseStatusMessage($"Construido: {selected.displayName} (-${selected.buildCost})");
        }

        private void TryDemolishAtHoveredCell()
        {
            PlacedMachine machine = GridManager.Instance.GetMachineAt(_hoveredCell);
            if (machine == null) return;

            int refund = machine.MachineData != null ? Mathf.RoundToInt(machine.MachineData.buildCost * 0.75f) : 10;
            string machineName = machine.MachineData != null ? machine.MachineData.displayName : machine.name;

            GridManager.Instance.UnregisterMachine(_hoveredCell);
            Destroy(machine.gameObject);

            if (EconomyManager.Instance != null)
            {
                EconomyManager.Instance.AddCredits(refund);
            }

            FactoryEvents.RaiseStatusMessage($"Demolido: {machineName} (+${refund} reembolso)");
        }

        private void CreateGhostPreviewObject()
        {
            if (_ghostRoot != null) Destroy(_ghostRoot);

            _ghostRoot = new GameObject("BuildGhostPreview");

            // Base footprint plate (1.85m x 1.85m inside 2m cell)
            GameObject basePlate = GameObject.CreatePrimitive(PrimitiveType.Cube);
            basePlate.name = "GhostBase";
            basePlate.transform.SetParent(_ghostRoot.transform, false);
            basePlate.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            basePlate.transform.localScale = new Vector3(1.75f, 0.5f, 1.75f);
            Destroy(basePlate.GetComponent<Collider>());

            // Forward direction nose indicator (+Z)
            GameObject dirArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
            dirArrow.name = "GhostDirectionNose";
            dirArrow.transform.SetParent(_ghostRoot.transform, false);
            dirArrow.transform.localPosition = new Vector3(0f, 0.6f, 0.65f);
            dirArrow.transform.localScale = new Vector3(0.45f, 0.25f, 0.55f);
            Destroy(dirArrow.GetComponent<Collider>());

            _ghostRenderers = _ghostRoot.GetComponentsInChildren<MeshRenderer>();
        }
    }
}
