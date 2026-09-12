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
        private const float BaseFieldOfView = 55f;
        private const float ResetYaw = 28f;
        private const float ResetPitch = 28f;

        private Transform playerTarget;
        private Transform selectedTarget;
        private Transform focusTarget;
        private Vector3 smoothedFocus;
        private float pinchStartPixelDistance = 1f;
        private float pinchStartOrbitDistance;
        private bool resettingView;
        private float fovPunch;

        public Camera Camera => controlledCamera;
        public Transform FocusTarget => focusTarget;
        public float Distance => distance;

        private void Awake()
        {
            if (!controlledCamera) controlledCamera = GetComponent<Camera>();
            if (!controlledCamera) controlledCamera = gameObject.AddComponent<Camera>();
            controlledCamera.tag = "MainCamera";
            controlledCamera.fieldOfView = BaseFieldOfView;
            // 0.15/12000 was an 80,000:1 depth range — z-fighting bait on 24-bit
            // mobile depth buffers. Nothing renders closer than half a metre.
            controlledCamera.nearClipPlane = 0.5f;
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
            // The view glides home instead of snapping; the focus target moves
            // immediately so tracking does not stall mid-reset.
            resettingView = true;
            SetFocus(playerTarget);
        }

        /// <summary>Brief extra field of view, used as warp/jump acceleration feedback.</summary>
        public void PunchFieldOfView(float extraDegrees)
        {
            fovPunch = Mathf.Max(fovPunch, extraDegrees);
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
            if (resettingView)
            {
                yaw = Mathf.LerpAngle(yaw, ResetYaw, blend * 1.6f);
                pitch = Mathf.Lerp(pitch, ResetPitch, blend * 1.6f);
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, ResetYaw)) < 0.2f && Mathf.Abs(pitch - ResetPitch) < 0.2f)
                    resettingView = false;
            }
            var rotation = Quaternion.Euler(pitch, yaw, 0);
            var desired = smoothedFocus + rotation * new Vector3(0, 0, -distance);
            transform.position = Vector3.Lerp(transform.position, desired, blend);
            transform.rotation = Quaternion.Slerp(transform.rotation,
                Quaternion.LookRotation(smoothedFocus - transform.position, Vector3.up), blend);
            if (fovPunch > 0.01f)
            {
                fovPunch = Mathf.Lerp(fovPunch, 0f, blend * 1.8f);
                controlledCamera.fieldOfView = BaseFieldOfView + fovPunch;
            }
            else if (!Mathf.Approximately(controlledCamera.fieldOfView, BaseFieldOfView))
            {
                controlledCamera.fieldOfView = BaseFieldOfView;
            }
        }
    }
}
