using UnityEngine;
using SnowFactory.Core;
using SnowFactory.Managers;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// Runtime behavior for any machine placed on the factory grid (Extractor, Conveyor, Compactor, DeliveryHub).
    /// Visually animates items moving across its slot and transfers them to the forward neighbor cell.
    /// </summary>
    public class PlacedMachine : MonoBehaviour
    {
        [Header("Machine Settings")]
        [SerializeField] private BuildableMachineSO machineData;
        [SerializeField] private MachineCategory category = MachineCategory.Conveyor;
        [SerializeField] private float cycleDuration = 1.2f;
        [SerializeField] private Transform itemAnchor;
        [SerializeField] private MeshRenderer itemVisualRenderer;

        public Vector2Int GridCell { get; private set; }
        public int RotationStep { get; private set; }
        public BuildableMachineSO MachineData => machineData;
        public MachineCategory Category => machineData != null ? machineData.category : category;

        public SnowItemType HeldItem { get; private set; } = SnowItemType.None;
        public float CycleProgress { get; private set; }

        private Vector3 _itemStartLocalPos;
        private Vector3 _itemEndLocalPos;

        private void Awake()
        {
            if (machineData != null)
            {
                category = machineData.category;
                cycleDuration = Mathf.Max(0.2f, machineData.cycleDuration);
            }

            _itemStartLocalPos = new Vector3(0f, 0.48f, -0.55f);
            _itemEndLocalPos = new Vector3(0f, 0.48f, 0.55f);

            UpdateItemVisual();
        }

        public void InitializeData(BuildableMachineSO data)
        {
            machineData = data;
            if (data != null)
            {
                category = data.category;
                cycleDuration = Mathf.Max(0.2f, data.cycleDuration);
            }
        }

        public void SetupOnGrid(Vector2Int cell, int rotationStep)
        {
            GridCell = cell;
            RotationStep = ((rotationStep % 4) + 4) % 4;
            transform.rotation = Quaternion.Euler(0f, RotationStep * 90f, 0f);
            UpdateItemVisual();
        }

        private void Update()
        {
            if (GridManager.Instance == null) return;

            switch (Category)
            {
                case MachineCategory.Extractor:
                    TickExtractor();
                    break;
                case MachineCategory.Conveyor:
                    TickConveyor();
                    break;
                case MachineCategory.Compactor:
                    TickCompactor();
                    break;
                case MachineCategory.DeliveryHub:
                    TickDeliveryHub();
                    break;
            }
        }

        private void TickExtractor()
        {
            if (HeldItem == SnowItemType.None)
            {
                CycleProgress += Time.deltaTime / cycleDuration;
                if (CycleProgress >= 1f)
                {
                    CycleProgress = 0f;
                    HeldItem = SnowItemType.RawSnow;
                    UpdateItemVisual();
                }
            }
            else
            {
                if (TryPushToForwardNeighbor(HeldItem))
                {
                    HeldItem = SnowItemType.None;
                    CycleProgress = 0f;
                    UpdateItemVisual();
                }
            }
        }

        private void TickConveyor()
        {
            if (HeldItem == SnowItemType.None) return;

            CycleProgress = Mathf.Min(1f, CycleProgress + Time.deltaTime / cycleDuration);

            if (itemVisualRenderer != null)
            {
                itemVisualRenderer.transform.localPosition = Vector3.Lerp(_itemStartLocalPos, _itemEndLocalPos, CycleProgress);
            }

            if (CycleProgress >= 1f)
            {
                if (TryPushToForwardNeighbor(HeldItem))
                {
                    HeldItem = SnowItemType.None;
                    CycleProgress = 0f;
                    UpdateItemVisual();
                }
            }
        }

        private void TickCompactor()
        {
            if (HeldItem == SnowItemType.None) return;

            CycleProgress = Mathf.Min(1f, CycleProgress + Time.deltaTime / cycleDuration);

            if (HeldItem == SnowItemType.RawSnow && CycleProgress >= 0.5f)
            {
                // Transform RawSnow into CompactedIce halfway through the machine
                HeldItem = SnowItemType.CompactedIce;
                UpdateItemVisual();
            }

            if (itemVisualRenderer != null)
            {
                itemVisualRenderer.transform.localPosition = Vector3.Lerp(_itemStartLocalPos, _itemEndLocalPos, CycleProgress);
            }

            if (CycleProgress >= 1f)
            {
                if (TryPushToForwardNeighbor(HeldItem))
                {
                    HeldItem = SnowItemType.None;
                    CycleProgress = 0f;
                    UpdateItemVisual();
                }
            }
        }

        private void TickDeliveryHub()
        {
            if (HeldItem == SnowItemType.None) return;

            CycleProgress += Time.deltaTime / cycleDuration;
            if (CycleProgress >= 1f)
            {
                int payout = HeldItem == SnowItemType.CompactedIce ? 25 : 8;
                if (EconomyManager.Instance != null)
                {
                    EconomyManager.Instance.AddCredits(payout);
                }

                string itemLabel = HeldItem == SnowItemType.CompactedIce ? "Bloque Criogénico" : "Nieve Virgen";
                FactoryEvents.RaiseStatusMessage($"Vendido: {itemLabel} (+${payout})");

                HeldItem = SnowItemType.None;
                CycleProgress = 0f;
                UpdateItemVisual();
            }
        }

        public bool CanAcceptItem(SnowItemType item)
        {
            if (item == SnowItemType.None || HeldItem != SnowItemType.None)
            {
                return false;
            }

            switch (Category)
            {
                case MachineCategory.Conveyor:
                case MachineCategory.DeliveryHub:
                    return true;
                case MachineCategory.Compactor:
                    return item == SnowItemType.RawSnow || item == SnowItemType.CompactedIce;
                default:
                    return false;
            }
        }

        public bool TryAcceptItem(SnowItemType item)
        {
            if (!CanAcceptItem(item)) return false;

            HeldItem = item;
            CycleProgress = 0f;
            UpdateItemVisual();
            return true;
        }

        private bool TryPushToForwardNeighbor(SnowItemType item)
        {
            Vector2Int forwardDir = GridManager.RotationStepToDirection(RotationStep);
            Vector2Int targetCell = GridCell + forwardDir;
            PlacedMachine neighbor = GridManager.Instance.GetMachineAt(targetCell);

            if (neighbor != null && neighbor.TryAcceptItem(item))
            {
                return true;
            }
            return false;
        }

        private void UpdateItemVisual()
        {
            if (itemVisualRenderer == null)
            {
                Transform found = transform.Find("ItemVisual");
                if (found != null)
                {
                    itemVisualRenderer = found.GetComponent<MeshRenderer>();
                }
            }

            if (itemVisualRenderer == null) return;

            if (HeldItem == SnowItemType.None)
            {
                itemVisualRenderer.gameObject.SetActive(false);
                return;
            }

            itemVisualRenderer.gameObject.SetActive(true);
            if (Category == MachineCategory.Extractor || Category == MachineCategory.DeliveryHub)
            {
                itemVisualRenderer.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            }
            else
            {
                itemVisualRenderer.transform.localPosition = Vector3.Lerp(_itemStartLocalPos, _itemEndLocalPos, CycleProgress);
            }

            if (HeldItem == SnowItemType.RawSnow)
            {
                itemVisualRenderer.transform.localScale = new Vector3(0.42f, 0.35f, 0.42f);
                itemVisualRenderer.material.SetColor("_BaseColor", Palette.RawSnowItem);
            }
            else if (HeldItem == SnowItemType.CompactedIce)
            {
                itemVisualRenderer.transform.localScale = new Vector3(0.48f, 0.42f, 0.58f);
                itemVisualRenderer.material.SetColor("_BaseColor", Palette.IceBrickItem);
            }
        }
    }
}
