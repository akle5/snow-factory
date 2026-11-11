using UnityEngine;
using UnityEngine.InputSystem;

namespace SnowFactory.Gameplay
{
    /// <summary>
    /// First-person character controller driven by Unity's Input System and CharacterController.
    /// Supports WASD movement, Left Shift sprint, Space jump, and Mouse look with pitch clamping.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float walkSpeed = 5.5f;
        [SerializeField] private float sprintSpeed = 8.5f;
        [SerializeField] private float jumpHeight = 1.35f;
        [SerializeField] private float gravity = -19.62f;

        [Header("Look Settings")]
        [SerializeField] private Transform cameraRoot;
        [SerializeField] private float mouseSensitivity = 0.14f;
        [SerializeField] private float maxPitchAngle = 85f;
        [SerializeField] private float initialPitchAngle = 14f;
        [SerializeField] private bool lockCursorOnStart = true;

        [Header("Wheelbarrow Driving Settings")]
        [SerializeField] private float vehiclePushSpeed = 6.8f;
        [SerializeField] private float vehicleSprintSpeed = 9.4f;
        [SerializeField] private float vehicleReverseSpeed = 4.2f;
        [SerializeField] private float vehicleSteerSpeed = 95f;

        private CharacterController _controller;
        private float _verticalVelocity;
        private float _cameraPitch;
        private int _ignoreMouseFrames = 5;
        private bool _isDrivingWheelbarrow;
        private WheelbarrowController _activeWheelbarrow;

        public Transform CameraRoot => cameraRoot;
        public bool IsGrounded => _controller != null && _controller.isGrounded;
        public Vector3 HorizontalVelocity { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _cameraPitch = initialPitchAngle;

            if (cameraRoot == null)
            {
                Transform found = transform.Find("CameraRoot");
                if (found != null)
                {
                    cameraRoot = found;
                }
            }

            if (cameraRoot != null)
            {
                cameraRoot.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
            }
        }

        private void Start()
        {
            if (lockCursorOnStart)
            {
                SetCursorLocked(true);
            }
        }

        private void Update()
        {
            HandleCursorToggle();
            HandleMouseLook();
            HandleMovement();
        }

        public void SetCameraRoot(Transform targetRoot)
        {
            cameraRoot = targetRoot;
        }

        public void SetWheelbarrowDriving(
            bool driving,
            WheelbarrowController wheelbarrow,
            Vector3 alignedWorldPos,
            float targetYaw)
        {
            _isDrivingWheelbarrow = driving;
            _activeWheelbarrow = wheelbarrow;

            if (driving)
            {
                bool wasEnabled = _controller != null && _controller.enabled;
                if (wasEnabled) _controller.enabled = false;
                transform.position = alignedWorldPos;
                transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
                if (wasEnabled) _controller.enabled = true;

                _verticalVelocity = -2f;
                if (_cameraPitch < 8f || _cameraPitch > 35f)
                {
                    _cameraPitch = 15f;
                    if (cameraRoot != null)
                    {
                        cameraRoot.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
                    }
                }
            }
        }

        private void HandleCursorToggle()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);
            }
        }

        private void HandleMouseLook()
        {
            if (cameraRoot == null) return;

            Mouse mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked && Application.isFocused)
            {
                if (_ignoreMouseFrames > 0)
                {
                    _ignoreMouseFrames--;
                    return;
                }

                Vector2 lookDelta = mouse.delta.ReadValue() * mouseSensitivity;
                if (lookDelta.sqrMagnitude < 400f)
                {
                    _cameraPitch = Mathf.Clamp(_cameraPitch - lookDelta.y, -maxPitchAngle, maxPitchAngle);
                    cameraRoot.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
                    transform.Rotate(Vector3.up * lookDelta.x);
                }
            }
        }

        private void HandleMovement()
        {
            Vector2 moveInput = ReadMoveInput();
            bool isSprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;

            if (_isDrivingWheelbarrow && _activeWheelbarrow != null)
            {
                HandleWheelbarrowMovement(moveInput, isSprinting);
                return;
            }

            bool jumpPressed = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;

            float targetSpeed = isSprinting ? sprintSpeed : walkSpeed;
            Vector3 moveDirection = (transform.right * moveInput.x + transform.forward * moveInput.y);
            if (moveDirection.sqrMagnitude > 1f)
            {
                moveDirection.Normalize();
            }

            HorizontalVelocity = moveDirection * targetSpeed;

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            if (jumpPressed && _controller.isGrounded)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }

            _verticalVelocity += gravity * Time.deltaTime;

            Vector3 finalVelocity = HorizontalVelocity + Vector3.up * _verticalVelocity;
            _controller.Move(finalVelocity * Time.deltaTime);
        }

        private void HandleWheelbarrowMovement(Vector2 moveInput, bool isSprinting)
        {
            // A/D steers smoothly while Mouse X also steers the heading naturally
            if (Mathf.Abs(moveInput.x) > 0.01f)
            {
                float steerScale = moveInput.y < -0.1f ? -0.8f : 1.0f;
                transform.Rotate(Vector3.up * (moveInput.x * steerScale * vehicleSteerSpeed * Time.deltaTime));
            }

            float loadRatio = _activeWheelbarrow.MaxCapacity > 0
                ? (float)_activeWheelbarrow.CurrentLoad / _activeWheelbarrow.MaxCapacity
                : 0f;
            float loadWeightFactor = Mathf.Lerp(1.0f, 0.82f, loadRatio);

            float forwardSpeed = isSprinting ? vehicleSprintSpeed : vehiclePushSpeed;
            float speed = moveInput.y >= 0f
                ? moveInput.y * forwardSpeed * loadWeightFactor
                : moveInput.y * vehicleReverseSpeed * loadWeightFactor;

            Vector3 forwardFlat = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

            if (speed > 0.01f && _activeWheelbarrow.CheckFrontObstacle(transform.position, forwardFlat, speed * Time.deltaTime))
            {
                speed = 0f;
            }

            HorizontalVelocity = forwardFlat * speed;

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }

            _verticalVelocity += gravity * Time.deltaTime;
            _controller.Move((HorizontalVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        private static Vector2 ReadMoveInput()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return Vector2.zero;

            float x = 0f;
            float y = 0f;

            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;

            return new Vector2(x, y);
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
