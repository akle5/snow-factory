using UnityEngine;
using UnityEngine.InputSystem;
using SnowFactory.Core;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// First-person equipped shovel controller.
    /// Allows scooping snow gradually from a SnowPile (LMB or E) and throwing it into a FireplaceHearth (LMB or E).
    /// </summary>
    public class ShovelController : MonoBehaviour
    {
        [Header("Shovel References")]
        [SerializeField] private Transform shovelPivot;
        [SerializeField] private GameObject heldSnowVisual;
        [SerializeField] private float interactDistance = 5.5f;

        private bool _hasSnowLoaded;
        private float _swingAnimTimer;
        private Quaternion _restLocalRotation;
        private Vector3 _restLocalPosition;

        public bool HasSnowLoaded => _hasSnowLoaded;

        private void Awake()
        {
            if (shovelPivot != null)
            {
                _restLocalPosition = shovelPivot.localPosition;
                _restLocalRotation = shovelPivot.localRotation;
            }

            if (heldSnowVisual != null)
            {
                heldSnowVisual.SetActive(false);
            }
        }

        public void BindShovelVisuals(Transform pivot, GameObject snowChunkOnBlade)
        {
            shovelPivot = pivot;
            heldSnowVisual = snowChunkOnBlade;

            if (shovelPivot != null)
            {
                _restLocalPosition = shovelPivot.localPosition;
                _restLocalRotation = shovelPivot.localRotation;
            }

            if (heldSnowVisual != null)
            {
                heldSnowVisual.SetActive(false);
            }
        }

        private void Update()
        {
            UpdateHoverAndInteract();
            AnimateShovelSwing();
        }

        private void UpdateHoverAndInteract()
        {
            Camera cam = Camera.main;
            if (cam == null) return;

            Ray ray = new Ray(cam.transform.position, cam.transform.forward);
            SnowPile hoveredPile = null;
            FireplaceHearth hoveredFireplace = null;

            if (Physics.Raycast(ray, out RaycastHit hit, interactDistance))
            {
                hoveredPile = hit.collider.GetComponentInParent<SnowPile>();
                hoveredFireplace = hit.collider.GetComponentInParent<FireplaceHearth>();
            }

            // Update contextual prompt
            if (!_hasSnowLoaded)
            {
                if (hoveredPile != null && hoveredPile.HasSnow)
                {
                    FactoryEvents.RaisePromptChanged($"[Clic Izquierdo / E] Recoger nieve con la pala ({hoveredPile.RemainingScoops}/{hoveredPile.MaxScoops})");
                }
                else
                {
                    FactoryEvents.RaisePromptChanged("Acércate al montón de nieve para cargarla con la pala");
                }
            }
            else
            {
                if (hoveredFireplace != null)
                {
                    FactoryEvents.RaisePromptChanged("[Clic Izquierdo / E] Echar nieve al fuego de la chimenea");
                }
                else
                {
                    FactoryEvents.RaisePromptChanged("Pala cargada de nieve — Lleva la nieve a la chimenea con fuego");
                }
            }

            if (Cursor.lockState != CursorLockMode.Locked) return;

            bool actionPressed = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                                 (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame);

            if (!actionPressed) return;

            if (!_hasSnowLoaded && hoveredPile != null && hoveredPile.HasSnow)
            {
                if (hoveredPile.TryScoopAtPoint(hit.point))
                {
                    _hasSnowLoaded = true;
                    _swingAnimTimer = 0.32f;
                    if (heldSnowVisual != null)
                    {
                        heldSnowVisual.SetActive(true);
                    }
                    FactoryEvents.RaiseStatusMessage($"Nieve recogida con la pala (Quedan {hoveredPile.RemainingScoops} paladas en este montón)");
                }
            }
            else if (_hasSnowLoaded && hoveredFireplace != null)
            {
                _hasSnowLoaded = false;
                _swingAnimTimer = 0.32f;
                if (heldSnowVisual != null)
                {
                    heldSnowVisual.SetActive(false);
                }
                hoveredFireplace.DepositSnow();
            }
        }

        private void AnimateShovelSwing()
        {
            if (shovelPivot == null) return;

            if (_swingAnimTimer > 0f)
            {
                _swingAnimTimer -= Time.deltaTime;
                float t = Mathf.Sin((1f - (_swingAnimTimer / 0.32f)) * Mathf.PI);
                shovelPivot.localPosition = _restLocalPosition + new Vector3(0f, -0.08f * t, 0.18f * t);
                shovelPivot.localRotation = _restLocalRotation * Quaternion.Euler(28f * t, -8f * t, 0f);
            }
            else
            {
                // Subtle breathing bob in first person
                float bob = Mathf.Sin(Time.time * 2.4f) * 0.008f;
                shovelPivot.localPosition = _restLocalPosition + new Vector3(0f, bob, 0f);
                shovelPivot.localRotation = _restLocalRotation;
            }
        }
    }
}
