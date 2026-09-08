using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Starfall.Presentation
{
    [DisallowMultipleComponent]
    public sealed class EveCameraController : MonoBehaviour
    {
        [SerializeField] private Camera controlledCamera;
        [SerializeField] private float distance = 32f;
        [SerializeField] private float minDistance = 7f;
        [SerializeField] private float maxDistance = 240f;
        [SerializeField] private float yaw = 28f;
        [SerializeField] private float pitch = 28f;
        [SerializeField] private float damping = 10f;

        private Transform playerTarget;
        private Transform selectedTarget;
        private Transform focusTarget;
        private float smoothedDistance;
        private bool poseInitialized;

        public Camera Camera => controlledCamera;
        public Transform FocusTarget => focusTarget;
#if STARFALL_ANDROID_CI
        internal float AndroidCiYawDegrees => yaw;
        internal float AndroidCiDistance => distance;
#endif

        private void Awake()
        {
            if (!controlledCamera) controlledCamera = GetComponent<Camera>();
            if (!controlledCamera) controlledCamera = gameObject.AddComponent<Camera>();
            controlledCamera.tag = "MainCamera";
            controlledCamera.fieldOfView = 55f;
            controlledCamera.nearClipPlane = 0.15f;
            controlledCamera.farClipPlane = 12000f;
            controlledCamera.clearFlags = CameraClearFlags.SolidColor;
            controlledCamera.backgroundColor = new Color(0.0015f, 0.0035f, 0.011f, 1f);
            CameraRenderQuality.Configure(controlledCamera);
        }

        public void SetPlayerTarget(Transform target)
        {
            playerTarget = target;
            if (!focusTarget) SetFocus(target);
        }

        public void SetSelectedTarget(Transform target) => selectedTarget = target;

        public void ToggleSelectedFocus()
        {
            if (selectedTarget && focusTarget != selectedTarget) SetFocus(selectedTarget);
            else ResetToPlayer();
        }

        public void ResetToPlayer()
        {
            yaw = 28f;
            pitch = 28f;
            SetFocus(playerTarget);
        }

        public void SetFocus(Transform target)
        {
            focusTarget = target ? target : playerTarget;

        }

        public void ApplyOrbit(Vector2 deltaPixels)
        {
            yaw += deltaPixels.x * 0.18f;
            pitch = Mathf.Clamp(pitch - deltaPixels.y * 0.14f, 8f, 78f);
        }

        public void ApplyZoom(float scaleRatio)
        {
            if (scaleRatio <= 0f || float.IsNaN(scaleRatio) || float.IsInfinity(scaleRatio)) return;
            distance = Mathf.Clamp(distance / scaleRatio, minDistance, maxDistance);
        }

        private void Update()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse != null && !HasActiveTouch())
            {
                if (mouse.rightButton.isPressed)
                {
                    ApplyOrbit(mouse.delta.ReadValue());
                }
                var scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    ApplyZoom(Mathf.Exp(scroll * 0.0014f));
            }
            if (keyboard != null)
            {
                if (keyboard.vKey.wasPressedThisFrame) ToggleSelectedFocus();
                if (keyboard.xKey.wasPressedThisFrame) ResetToPlayer();
            }
        }

        private static bool HasActiveTouch()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return false;
            foreach (var touch in touchscreen.touches)
                if (touch.press.isPressed) return true;
            return false;
        }

        private void LateUpdate() => UpdatePose(Time.unscaledDeltaTime);

        // Dampen orbit/zoom in target-relative space. Damping two world-space
        // positions lets a fast target pass the camera and reverses LookRotation.
        private void UpdatePose(float deltaSeconds)
        {
            if (!focusTarget || !controlledCamera) return;
            var desiredRotation = Quaternion.Euler(pitch, yaw, 0);
            var blend = 1f - Mathf.Exp(-damping * Mathf.Max(0f, deltaSeconds));
            if (!poseInitialized)
            {
                transform.rotation = desiredRotation;
                smoothedDistance = distance;
                poseInitialized = true;
            }
            else
            {
                transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, blend);
                smoothedDistance = Mathf.Lerp(smoothedDistance, distance, blend);
            }
            transform.position = focusTarget.position - transform.forward * smoothedDistance;
        }
    }
}
