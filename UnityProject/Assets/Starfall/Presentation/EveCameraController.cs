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
        private Vector3 smoothedFocus;
        private float pinchStartPixelDistance = 1f;
        private float pinchStartOrbitDistance;

        public Camera Camera => controlledCamera;
        public Transform FocusTarget => focusTarget;
        public float Distance => distance;

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
            if (focusTarget) smoothedFocus = focusTarget.position;
        }

        // Orbit and zoom are fed by WorldBackdropInput (HUD pointer events), so
        // touch, trackpad and mouse all share one sensitivity path. Units are
        // screen pixels; wheelDelta follows UI Toolkit WheelEvent.delta.y.
        public void AddOrbitInput(float deltaX, float deltaY)
        {
            yaw += deltaX * 0.18f;
            pitch = Mathf.Clamp(pitch - deltaY * 0.14f, 8f, 78f);
        }

        public void AddZoomInput(float wheelDelta)
        {
            if (Mathf.Abs(wheelDelta) < 0.01f) return;
            distance = Mathf.Clamp(distance * Mathf.Exp(-wheelDelta * 0.008f), minDistance, maxDistance);
        }

        public void BeginPinchZoom(float startPixelDistance)
        {
            pinchStartPixelDistance = Mathf.Max(1f, startPixelDistance);
            pinchStartOrbitDistance = distance;
        }

        public void UpdatePinchZoom(float currentPixelDistance)
        {
            var scale = pinchStartPixelDistance / Mathf.Max(1f, currentPixelDistance);
            distance = Mathf.Clamp(pinchStartOrbitDistance * scale, minDistance, maxDistance);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.vKey.wasPressedThisFrame) ToggleSelectedFocus();
            if (keyboard.xKey.wasPressedThisFrame) ResetToPlayer();
        }

        private void LateUpdate()
        {
            if (!focusTarget || !controlledCamera) return;
            var blend = 1f - Mathf.Exp(-damping * Time.unscaledDeltaTime);
            smoothedFocus = Vector3.Lerp(smoothedFocus, focusTarget.position, blend);
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            var desired = smoothedFocus + rotation * new Vector3(0, 0, -distance);
            transform.position = Vector3.Lerp(transform.position, desired, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(smoothedFocus - transform.position, Vector3.up), blend);
        }
    }
}
