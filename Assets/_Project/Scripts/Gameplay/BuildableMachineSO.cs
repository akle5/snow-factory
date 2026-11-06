using UnityEngine;
using SnowFactory.Core;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// Data definition for a buildable factory machine.
    /// </summary>
    [CreateAssetMenu(fileName = "NewBuildableMachine", menuName = "Snow Factory/Buildable Machine")]
    public class BuildableMachineSO : ScriptableObject
    {
        public string machineId = "machine_01";
        public string displayName = "Machine";
        [TextArea(2, 3)]
        public string description = "Factory machine.";
        public MachineCategory category = MachineCategory.Extractor;
        public int buildCost = 25;
        public float cycleDuration = 1.5f;
        public PlacedMachine prefab;
    }
}
