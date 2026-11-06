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
        [SerializeField] private bool lockCursorOnStart = true;

        private CharacterController _controller;
        private float _verticalVelocity;
        private float _cameraPitch;

        public Transform CameraRoot => cameraRoot;
        public bool IsGrounded => _controller != null && _controller.isGrounded;
        public Vector3 HorizontalVelocity { get; private set; }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();

            if (cameraRoot == null)
            {
                Transform found = transform.Find("CameraRoot");
                if (found != null)
                {
                    cameraRoot = found;
                }
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
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 lookDelta = mouse.delta.ReadValue() * mouseSensitivity;

                _cameraPitch = Mathf.Clamp(_cameraPitch - lookDelta.y, -maxPitchAngle, maxPitchAngle);
                cameraRoot.localRotation = Quaternion.Euler(_cameraPitch, 0f, 0f);
                transform.Rotate(Vector3.up * lookDelta.x);
            }
        }

        private void HandleMovement()
        {
            Vector2 moveInput = ReadMoveInput();
            bool isSprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
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
