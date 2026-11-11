using UnityEngine;
using UnityEngine.UIElements;
using SnowFactory.Core;

namespace SnowFactory.UI
{
    /// <summary>
    /// Modern Unity 6 UI Toolkit HUD (UIDocument) displaying Heat Coins earned from melting snow,
    /// current stack load on the shovel, total scoops burned, and contextual interaction prompts.
    /// Does not spawn any giant 1920x1080 world-space Canvas rectangle in the 3D Scene View!
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class FactoryHudController : MonoBehaviour
    {
        private const string DefaultStatusHint =
            "[Clic Izq / E] Apilar Nieve / Echar al Fuego   |   [U] Mejorar Pala   |   [R] Nueva Nevada   |   [I] Pala Infinita (Test)";

        [SerializeField] private UIDocument uiDocument;

        private Label _counterLabel;
        private Label _promptLabel;
        private Label _statusLabel;

        private int _burnedScoops;
        private int _heatPoints;
        private int _currentLoad;
        private int _maxCapacity = 3;
        private float _statusClearTimer;
        private string _lastPrompt = "Apunta al montón de nieve para excavar (0/3)   |   [U] Ensanchar Pala (30 Monedas)   |   [R] Nueva Nevada   |   [I] Pala Infinita (Test)";

        private void Awake()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }
        }

        private void OnEnable()
        {
            BindVisualTreeElements();

            FactoryEvents.OnSnowBurnedCountChanged += HandleSnowBurnedChanged;
            FactoryEvents.OnHeatPointsChanged += HandleHeatPointsChanged;
            FactoryEvents.OnShovelLoadChanged += HandleShovelLoadChanged;
            FactoryEvents.OnPromptChanged += HandlePromptChanged;
            FactoryEvents.OnStatusMessage += HandleStatusMessage;
        }

        private void OnDisable()
        {
            FactoryEvents.OnSnowBurnedCountChanged -= HandleSnowBurnedChanged;
            FactoryEvents.OnHeatPointsChanged -= HandleHeatPointsChanged;
            FactoryEvents.OnShovelLoadChanged -= HandleShovelLoadChanged;
            FactoryEvents.OnPromptChanged -= HandlePromptChanged;
            FactoryEvents.OnStatusMessage -= HandleStatusMessage;
        }

        private void Update()
        {
            if (_counterLabel == null)
            {
                BindVisualTreeElements();
            }

            if (_statusClearTimer > 0f)
            {
                _statusClearTimer -= Time.deltaTime;
                if (_statusClearTimer <= 0f && _statusLabel != null)
                {
                    _statusLabel.text = DefaultStatusHint;
                }
            }
        }

        public void BindVisualTreeElements()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }
            if (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                return;
            }

            VisualElement root = uiDocument.rootVisualElement;
            _counterLabel = root.Q<Label>("snowBurnedLabel");
            _promptLabel = root.Q<Label>("interactionPromptLabel");
            _statusLabel = root.Q<Label>("statusLabel");

            RefreshHeaderText();
            if (_promptLabel != null && !string.IsNullOrEmpty(_lastPrompt))
            {
                _promptLabel.text = _lastPrompt;
            }
            if (_statusLabel != null && _statusClearTimer <= 0f)
            {
                _statusLabel.text = DefaultStatusHint;
            }
        }

        private void HandleSnowBurnedChanged(int count)
        {
            _burnedScoops = count;
            RefreshHeaderText();
        }

        private void HandleHeatPointsChanged(int points)
        {
            _heatPoints = points;
            RefreshHeaderText();
        }

        private void HandleShovelLoadChanged(int currentLoad, int maxCapacity)
        {
            _currentLoad = currentLoad;
            _maxCapacity = maxCapacity;
            RefreshHeaderText();
        }

        private void RefreshHeaderText()
        {
            if (_counterLabel != null)
            {
                _counterLabel.text = $"MONEDAS DE CALOR: {_heatPoints}   |   PALA: {_currentLoad}/{_maxCapacity}   |   FUNDIDAS: {_burnedScoops}";
            }
        }

        private void HandlePromptChanged(string prompt)
        {
            _lastPrompt = prompt;
            if (_promptLabel != null)
            {
                _promptLabel.text = prompt;
            }
        }

        private void HandleStatusMessage(string message)
        {
            if (_statusLabel != null)
            {
                _statusLabel.text = message;
                _statusClearTimer = 3.5f;
            }
        }
    }
}
