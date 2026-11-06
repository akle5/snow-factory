using UnityEngine;
using SnowFactory.Core;

namespace SnowFactory.Managers
{
    /// <summary>
    /// Manages player SnowCredits currency for building and selling processed snow/ice.
    /// </summary>
    public class EconomyManager : MonoBehaviour
    {
        public static EconomyManager Instance { get; private set; }

        [SerializeField] private int startingCredits = 250;

        public int CurrentCredits { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            CurrentCredits = startingCredits;
        }

        private void Start()
        {
            FactoryEvents.RaiseCreditsChanged(CurrentCredits);
        }

        public bool CanAfford(int amount)
        {
            return CurrentCredits >= amount;
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0 || CurrentCredits < amount)
            {
                return false;
            }

            CurrentCredits -= amount;
            FactoryEvents.RaiseCreditsChanged(CurrentCredits);
            return true;
        }

        public void AddCredits(int amount)
        {
            if (amount <= 0) return;

            CurrentCredits += amount;
            FactoryEvents.RaiseCreditsChanged(CurrentCredits);
        }
    }
}
