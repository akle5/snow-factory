using UnityEngine;
using UnityEngine.UI;
using SnowFactory.Core;

namespace SnowFactory.UI
{
    /// <summary>
    /// Simple HUD showing how many snow scoops have been thrown into the fireplace and interaction prompts.
    /// </summary>
    public class FactoryHudController : MonoBehaviour
    {
        [SerializeField] private Text counterText;
        [SerializeField] private Text promptText;
        [SerializeField] private Text statusText;

        private float _statusClearTimer;

        private void OnEnable()
        {
            FactoryEvents.OnSnowBurnedCountChanged += HandleSnowBurnedChanged;
            FactoryEvents.OnPromptChanged += HandlePromptChanged;
            FactoryEvents.OnStatusMessage += HandleStatusMessage;
        }

        private void OnDisable()
        {
            FactoryEvents.OnSnowBurnedCountChanged -= HandleSnowBurnedChanged;
            FactoryEvents.OnPromptChanged -= HandlePromptChanged;
            FactoryEvents.OnStatusMessage -= HandleStatusMessage;
        }

        private void Update()
        {
            if (_statusClearTimer > 0f)
            {
                _statusClearTimer -= Time.deltaTime;
                if (_statusClearTimer <= 0f && statusText != null)
                {
                    statusText.text = "[WASD] Moverse | [Clic Izq / E] Usar Pala (Recoger Nieve / Echar a la Chimenea)";
                }
            }
        }

        public void BindLabels(Text counter, Text prompt, Text status)
        {
            counterText = counter;
            promptText = prompt;
            statusText = status;
        }

        private void HandleSnowBurnedChanged(int count)
        {
            if (counterText != null)
            {
                counterText.text = $"NIEVE EN LA CHIMENEA: {count} PALADAS";
            }
        }

        private void HandlePromptChanged(string prompt)
        {
            if (promptText != null)
            {
                promptText.text = prompt;
            }
        }

        private void HandleStatusMessage(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
                _statusClearTimer = 3.0f;
            }
        }
    }
}
