using System;

namespace SnowFactory.Core
{
    /// <summary>
    /// Lightweight static event bus for decoupled communication between Shovel gameplay and UI.
    /// </summary>
    public static class FactoryEvents
    {
        public static event Action<int> OnSnowBurnedCountChanged;
        public static event Action<int> OnHeatPointsChanged;
        public static event Action<int, int> OnShovelLoadChanged;
        public static event Action<string> OnPromptChanged;
        public static event Action<string> OnStatusMessage;

        public static void RaiseSnowBurnedCountChanged(int totalBurned)
        {
            OnSnowBurnedCountChanged?.Invoke(totalBurned);
        }

        public static void RaiseHeatPointsChanged(int totalPoints)
        {
            OnHeatPointsChanged?.Invoke(totalPoints);
        }

        public static void RaiseShovelLoadChanged(int currentLoad, int maxCapacity)
        {
            OnShovelLoadChanged?.Invoke(currentLoad, maxCapacity);
        }

        public static void RaisePromptChanged(string prompt)
        {
            OnPromptChanged?.Invoke(prompt);
        }

        public static void RaiseStatusMessage(string message)
        {
            OnStatusMessage?.Invoke(message);
        }
    }
}
