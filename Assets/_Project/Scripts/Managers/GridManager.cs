using System.Collections.Generic;
using UnityEngine;
using SnowFactory.Gameplay;

namespace SnowFactory.Managers
{
    /// <summary>
    /// Spatial grid manager for the 24x24m concrete factory pad (cell size 2x2m = 12x12 cells).
    /// Handles coordinate conversion, occupancy queries, and neighbor lookups.
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        public static GridManager Instance { get; private set; }

        [Header("Grid Configuration")]
        [SerializeField] private float cellSize = 2.0f;
        [SerializeField] private int halfExtentCells = 6; // -6 to +5 => 12x12 cells covering -12m to +12m
        [SerializeField] private float floorHeight = 0.05f;

        private readonly Dictionary<Vector2Int, PlacedMachine> _occupiedCells = new Dictionary<Vector2Int, PlacedMachine>();

        public float CellSize => cellSize;
        public float FloorHeight => floorHeight;
        public IReadOnlyDictionary<Vector2Int, PlacedMachine> OccupiedCells => _occupiedCells;

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
            // Auto-register any pre-placed machines in the scene
            PlacedMachine[] existing = FindObjectsByType<PlacedMachine>(FindObjectsSortMode.None);
            foreach (PlacedMachine machine in existing)
            {
                Vector2Int cell = WorldToCell(machine.transform.position);
                if (IsWithinBounds(cell) && !_occupiedCells.ContainsKey(cell))
                {
                    int rotStep = Mathf.RoundToInt(machine.transform.eulerAngles.y / 90f) % 4;
                    if (rotStep < 0) rotStep += 4;
                    machine.SetupOnGrid(cell, rotStep);
                    _occupiedCells[cell] = machine;
                }
            }
        }

        public Vector2Int WorldToCell(Vector3 worldPos)
        {
            int x = Mathf.FloorToInt(worldPos.x / cellSize);
            int z = Mathf.FloorToInt(worldPos.z / cellSize);
            return new Vector2Int(x, z);
        }

        public Vector3 CellToWorldCenter(Vector2Int cell)
        {
            float wx = (cell.x + 0.5f) * cellSize;
            float wz = (cell.y + 0.5f) * cellSize;
            return new Vector3(wx, floorHeight, wz);
        }

        public bool IsWithinBounds(Vector2Int cell)
        {
            return cell.x >= -halfExtentCells && cell.x < halfExtentCells &&
                   cell.y >= -halfExtentCells && cell.y < halfExtentCells;
        }

        public bool IsCellFree(Vector2Int cell)
        {
            return IsWithinBounds(cell) && !_occupiedCells.ContainsKey(cell);
        }

        public PlacedMachine GetMachineAt(Vector2Int cell)
        {
            _occupiedCells.TryGetValue(cell, out PlacedMachine machine);
            return machine;
        }

        public bool RegisterMachine(Vector2Int cell, PlacedMachine machine)
        {
            if (!IsCellFree(cell) || machine == null)
            {
                return false;
            }
            _occupiedCells[cell] = machine;
            return true;
        }

        public bool UnregisterMachine(Vector2Int cell)
        {
            return _occupiedCells.Remove(cell);
        }

        public static Vector2Int RotationStepToDirection(int rotationStep)
        {
            int step = ((rotationStep % 4) + 4) % 4;
            switch (step)
            {
                case 0: return new Vector2Int(0, 1);  // North (+Z)
                case 1: return new Vector2Int(1, 0);  // East (+X)
                case 2: return new Vector2Int(0, -1); // South (-Z)
                default: return new Vector2Int(-1, 0); // West (-X)
            }
        }
    }
}
