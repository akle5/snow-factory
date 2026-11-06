using UnityEngine;
using UnityEngine.UI;
using SnowFactory.Core;

namespace SnowFactory.UI
{
    /// <summary>
    /// Minimalist prototype HUD showing SnowCredits, active build selection, and controls hint.
    /// </summary>
    public class FactoryHudController : MonoBehaviour
    {
        [SerializeField] private Text creditsText;
        [SerializeField] private Text selectionText;
        [SerializeField] private Text statusText;

        private float _statusClearTimer;

        private void OnEnable()
        {
            FactoryEvents.OnCreditsChanged += HandleCreditsChanged;
            FactoryEvents.OnSelectedMachineChanged += HandleSelectedMachineChanged;
            FactoryEvents.OnBuildModeChanged += HandleBuildModeChanged;
            FactoryEvents.OnStatusMessage += HandleStatusMessage;
        }

        private void OnDisable()
        {
            FactoryEvents.OnCreditsChanged -= HandleCreditsChanged;
            FactoryEvents.OnSelectedMachineChanged -= HandleSelectedMachineChanged;
            FactoryEvents.OnBuildModeChanged -= HandleBuildModeChanged;
            FactoryEvents.OnStatusMessage -= HandleStatusMessage;
        }

        private void Update()
        {
            if (_statusClearTimer > 0f)
            {
                _statusClearTimer -= Time.deltaTime;
                if (_statusClearTimer <= 0f && statusText != null)
                {
                    statusText.text = "[1-4] Máquina | [R] Rotar | [Clic Izq] Construir | [Clic Der] Demoler | [E] Depositar Nieve | [B] Modo";
                }
            }
        }

        public void BindLabels(Text credits, Text selection, Text status)
        {
            creditsText = credits;
            selectionText = selection;
            statusText = status;
        }

        private void HandleCreditsChanged(int credits)
        {
            if (creditsText != null)
            {
                creditsText.text = $"CRÉDITOS: ${credits}";
            }
        }

        private void HandleSelectedMachineChanged(int slotIndex, string machineName, int cost)
        {
            if (selectionText != null)
            {
                selectionText.text = $"[{slotIndex + 1}] {machineName.ToUpper()} (${cost})";
            }
        }

        private void HandleBuildModeChanged(bool isBuildMode)
        {
            if (!isBuildMode && selectionText != null)
            {
                selectionText.text = "MODO OPERACIÓN (Pulsa [B] para Construir)";
            }
        }

        private void HandleStatusMessage(string message)
        {
            if (statusText != null)
            {
                statusText.text = message;
                _statusClearTimer = 3.5f;
            }
        }
    }
}
