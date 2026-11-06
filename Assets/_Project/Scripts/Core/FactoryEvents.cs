using System;
using UnityEngine;

namespace SnowFactory.Core
{
    /// <summary>
    /// Lightweight static event bus for decoupled communication between Managers, Gameplay, and UI.
    /// </summary>
    public static class FactoryEvents
    {
        public static event Action<int> OnCreditsChanged;
        public static event Action<bool> OnBuildModeChanged;
        public static event Action<int, string, int> OnSelectedMachineChanged;
        public static event Action<string> OnStatusMessage;

        public static void RaiseCreditsChanged(int currentCredits)
        {
            OnCreditsChanged?.Invoke(currentCredits);
        }

        public static void RaiseBuildModeChanged(bool isBuildMode)
        {
            OnBuildModeChanged?.Invoke(isBuildMode);
        }

        public static void RaiseSelectedMachineChanged(int slotIndex, string machineName, int cost)
        {
            OnSelectedMachineChanged?.Invoke(slotIndex, machineName, cost);
        }

        public static void RaiseStatusMessage(string message)
        {
            OnStatusMessage?.Invoke(message);
        }
    }
}
