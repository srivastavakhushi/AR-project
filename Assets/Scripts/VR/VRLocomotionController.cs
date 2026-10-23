using System;
using UnityEngine;
using UnityEngine.InputSystem;
using CrimeSceneVR.Core;

namespace CrimeSceneVR.VR
{
    /// <summary>
    /// Person 1 VR Movement & Teleportation Controller for Meta Quest / OpenXR.
    /// Provides:
    /// - Left Thumbstick smooth locomotion (head-relative horizontal movement with collision & gravity)
    /// - Right Thumbstick snap turning (or smooth turning) around the player's head position
    /// - Right Thumbstick forward / B-button floor teleportation with normal & floor-bounds validation
    /// - Automatic CharacterController capsule alignment with the headset's local XZ offset
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Crime Scene VR/VR/VR Locomotion Controller")]
    public class VRLocomotionController : MonoBehaviour
    {
        [Header("Rig References")]
        [Tooltip("Reference to VRRigReferences on the OVRCameraRig. Auto-resolved if null.")]
        [SerializeField] private VRRigReferences _rigReferences;

        [Tooltip("Optional reference to CrimeSceneEvidenceManager for floor bounds validation.")]
        [SerializeField] private CrimeSceneEvidenceManager _evidenceManager;

        [Header("Smooth Locomotion (Left Thumbstick)")]
        [SerializeField] private bool _enableSmoothMove = true;
        [SerializeField] private float _moveSpeed = 2.2f;
        [SerializeField] private float _deadzone = 0.18f;
        [SerializeField] private float _gravity = -9.81f;

        [Header("Turning (Right Thumbstick)")]
        [SerializeField] private bool _useSnapTurn = true;
        [SerializeField] private float _snapTurnAngle = 45f;
        [SerializeField] private float _snapTurnThreshold = 0.65f;
        [SerializeField] private float _snapTurnResetThreshold = 0.30f;
        [SerializeField] private float _smoothTurnSpeed = 90f;

        [Header("Teleportation (Right Stick Forward / B Button)")]
        [SerializeField] private bool _enableTeleport = true;
        [SerializeField] private float _maxTeleportDistance = 12f;
        [SerializeField] private float _minFloorNormalY = 0.70f;
        [SerializeField] private float _teleportStickThreshold = 0.75f;

        private bool _snapTurnReady = true;
        private bool _isAimingTeleport;
        private bool _hasValidTeleportTarget;
        private Vector3 _pendingTeleportPoint;
        private float _verticalVelocity;
        private GameObject _teleportReticle;
        private Material _reticleMaterial;

        public VRRigReferences RigReferences => _rigReferences;
        public bool EnableSmoothMove { get => _enableSmoothMove; set => _enableSmoothMove = value; }
        public float MoveSpeed { get => _moveSpeed; set => _moveSpeed = Mathf.Max(0.1f, value); }
        public bool UseSnapTurn { get => _useSnapTurn; set => _useSnapTurn = value; }
        public float SnapTurnAngle { get => _snapTurnAngle; set => _snapTurnAngle = value; }
        public bool EnableTeleport { get => _enableTeleport; set => _enableTeleport = value; }
        public float MinFloorNormalY => _minFloorNormalY;

        public event Action<Vector3> OnTeleported;
        public event Action<float> OnSnapTurned;

        public void Configure(VRRigReferences rigReferences, CrimeSceneEvidenceManager evidenceManager = null)
        {
            _rigReferences = rigReferences;
            if (evidenceManager != null)
                _evidenceManager = evidenceManager;
        }

        private void Awake()
        {
            ResolveReferences();
            SetupTeleportReticle();
        }

        private void Start()
        {
            ResolveReferences();
        }

        private void Update()
        {
            ResolveReferences();
            SyncCharacterControllerToHead();

            Vector2 leftStick = ReadLeftThumbstick();
            Vector2 rightStick = ReadRightThumbstick();
            bool teleportButtonHeld = ReadTeleportButtonHeld();
            bool teleportButtonReleased = ReadTeleportButtonReleased();

            if (_enableSmoothMove)
            {
                SimulateMove(leftStick, Time.unscaledDeltaTime);
            }

            ProcessTurning(rightStick, Time.unscaledDeltaTime);

            if (_enableTeleport)
            {
                ProcessTeleportInput(rightStick, teleportButtonHeld, teleportButtonReleased);
            }
        }

        private void OnDisable()
        {
            if (_teleportReticle != null)
                _teleportReticle.SetActive(false);
        }

        private void OnDestroy()
        {
            if (_reticleMaterial != null)
            {
                DestroyImmediate(_reticleMaterial);
                _reticleMaterial = null;
            }
            if (_teleportReticle != null)
            {
                DestroyImmediate(_teleportReticle);
                _teleportReticle = null;
            }
        }

        /// <summary>
        /// Moves the VR rig horizontally relative to the head's facing direction.
        /// Public for deterministic unit testing and external invocation.
        /// </summary>
        public void SimulateMove(Vector2 stickInput, float deltaTime)
        {
            if (deltaTime <= 0f)
                return;

            Transform rigRoot = _rigReferences != null ? _rigReferences.Root : transform;
            Transform head = (_rigReferences != null && _rigReferences.CenterEyeAnchor != null)
                ? _rigReferences.CenterEyeAnchor
                : rigRoot;

            Vector3 horizontalVelocity = Vector3.zero;
            if (stickInput.magnitude >= _deadzone)
            {
                Vector3 forward = head.forward;
                Vector3 right = head.right;
                forward.y = 0f;
                right.y = 0f;

                if (forward.sqrMagnitude > 0.0001f) forward.Normalize();
                else forward = Vector3.forward;

                if (right.sqrMagnitude > 0.0001f) right.Normalize();
                else right = Vector3.right;

                Vector3 moveDir = forward * stickInput.y + right * stickInput.x;
                if (moveDir.sqrMagnitude > 1f)
                    moveDir.Normalize();

                horizontalVelocity = moveDir * _moveSpeed;
            }

            CharacterController cc = _rigReferences != null ? _rigReferences.CharacterController : GetComponent<CharacterController>();
            if (cc != null && cc.enabled && cc.gameObject.activeInHierarchy)
            {
                if (cc.isGrounded && _verticalVelocity < 0f)
                    _verticalVelocity = -0.5f;
                else
                    _verticalVelocity += _gravity * deltaTime;

                Vector3 displacement = (horizontalVelocity + Vector3.up * _verticalVelocity) * deltaTime;
                if (horizontalVelocity.sqrMagnitude > 0.0001f || !cc.isGrounded)
                {
                    cc.Move(displacement);
                }
            }
            else if (horizontalVelocity.sqrMagnitude > 0.0001f)
            {
                rigRoot.position += horizontalVelocity * deltaTime;
            }
        }

        /// <summary>
        /// Processes right-thumbstick horizontal input for snap or smooth turning.
        /// Public for deterministic unit testing.
        /// </summary>
        public bool ProcessTurning(Vector2 rightStick, float deltaTime)
        {
            float turnX = rightStick.x;

            if (_useSnapTurn)
            {
                if (Mathf.Abs(turnX) < _snapTurnResetThreshold)
                {
                    _snapTurnReady = true;
                    return false;
                }

                if (_snapTurnReady && Mathf.Abs(turnX) >= _snapTurnThreshold)
                {
                    _snapTurnReady = false;
                    float signedAngle = Mathf.Sign(turnX) * _snapTurnAngle;
                    RotateRigAroundHead(signedAngle);
                    OnSnapTurned?.Invoke(signedAngle);
                    return true;
                }

                return false;
            }

            if (Mathf.Abs(turnX) >= _deadzone && deltaTime > 0f)
            {
                float angle = turnX * _smoothTurnSpeed * deltaTime;
                RotateRigAroundHead(angle);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Validates whether a raycast hit represents a walkable floor surface for teleportation.
        /// </summary>
        public bool IsValidTeleportSurface(RaycastHit hit)
        {
            if (hit.collider == null)
                return false;

            if (hit.normal.y < _minFloorNormalY)
                return false;

            if (_evidenceManager != null && _evidenceManager.HasInvestigationFloor)
            {
                Bounds floorBounds = _evidenceManager.InvestigationFloorBounds;
                // Expand slightly in Y so floor surface hits at min.y are always accepted
                floorBounds.Expand(new Vector3(0.25f, 0.5f, 0.25f));
                if (!floorBounds.Contains(hit.point))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Teleports the VR rig so the player's head lands horizontally over targetWorldPosition
        /// while placing the rig base at the target floor height.
        /// </summary>
        public bool TeleportTo(Vector3 targetWorldPosition)
        {
            Transform rigRoot = _rigReferences != null ? _rigReferences.Root : transform;
            Transform head = (_rigReferences != null && _rigReferences.CenterEyeAnchor != null)
                ? _rigReferences.CenterEyeAnchor
                : rigRoot;

            Vector3 headOffset = head.position - rigRoot.position;
            headOffset.y = 0f;

            Vector3 destination = targetWorldPosition - headOffset;

            CharacterController cc = _rigReferences != null ? _rigReferences.CharacterController : GetComponent<CharacterController>();
            bool wasEnabled = cc != null && cc.enabled;
            if (wasEnabled)
                cc.enabled = false;

            rigRoot.position = destination;
            _verticalVelocity = 0f;

            if (wasEnabled)
                cc.enabled = true;

            OnTeleported?.Invoke(targetWorldPosition);
            return true;
        }

        /// <summary>
        /// Casts a ray from the controller (or head fallback) and teleports if it hits a valid floor surface.
        /// </summary>
        public bool TryTeleportFromRay(out Vector3 teleportedPoint)
        {
            teleportedPoint = Vector3.zero;
            if (!TryFindTeleportTarget(out RaycastHit hit))
                return false;

            teleportedPoint = hit.point;
            return TeleportTo(hit.point);
        }

        private bool TryFindTeleportTarget(out RaycastHit hit)
        {
            hit = default;
            Transform rayOrigin = GetTeleportRayOrigin();
            if (rayOrigin == null)
                return false;

            Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
            if (!Physics.Raycast(ray, out hit, _maxTeleportDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return false;

            return IsValidTeleportSurface(hit);
        }

        private void ProcessTeleportInput(Vector2 rightStick, bool buttonHeld, bool buttonReleased)
        {
            bool stickAiming = rightStick.y >= _teleportStickThreshold && Mathf.Abs(rightStick.x) < 0.45f;
            bool aimingNow = stickAiming || buttonHeld;

            if (aimingNow)
            {
                _isAimingTeleport = true;
                _hasValidTeleportTarget = TryFindTeleportTarget(out RaycastHit hit);
                if (_hasValidTeleportTarget)
                {
                    _pendingTeleportPoint = hit.point;
                    if (_teleportReticle != null)
                    {
                        _teleportReticle.SetActive(true);
                        _teleportReticle.transform.position = hit.point + Vector3.up * 0.01f;
                    }
                }
                else if (_teleportReticle != null)
                {
                    _teleportReticle.SetActive(false);
                }
            }
            else if (_isAimingTeleport || buttonReleased)
            {
                if (_hasValidTeleportTarget)
                {
                    TeleportTo(_pendingTeleportPoint);
                }

                _isAimingTeleport = false;
                _hasValidTeleportTarget = false;
                if (_teleportReticle != null)
                    _teleportReticle.SetActive(false);
            }
        }

        private void RotateRigAroundHead(float angleDegrees)
        {
            Transform rigRoot = _rigReferences != null ? _rigReferences.Root : transform;
            Transform head = (_rigReferences != null && _rigReferences.CenterEyeAnchor != null)
                ? _rigReferences.CenterEyeAnchor
                : rigRoot;

            Vector3 pivot = new Vector3(head.position.x, rigRoot.position.y, head.position.z);
            rigRoot.RotateAround(pivot, Vector3.up, angleDegrees);
        }

        private void SyncCharacterControllerToHead()
        {
            if (_rigReferences == null || _rigReferences.CharacterController == null || _rigReferences.CenterEyeAnchor == null)
                return;

            CharacterController cc = _rigReferences.CharacterController;
            if (!cc.enabled)
                return;

            Vector3 localHead = transform.InverseTransformPoint(_rigReferences.CenterEyeAnchor.position);
            float height = Mathf.Clamp(localHead.y, 1.0f, 2.2f);
            cc.height = height;
            cc.center = new Vector3(localHead.x, height * 0.5f, localHead.z);
        }

        private Transform GetTeleportRayOrigin()
        {
            if (_rigReferences != null)
            {
                if (_rigReferences.RightHandAnchor != null)
                    return _rigReferences.RightHandAnchor;
                if (_rigReferences.LeftHandAnchor != null)
                    return _rigReferences.LeftHandAnchor;
                if (_rigReferences.CenterEyeAnchor != null)
                    return _rigReferences.CenterEyeAnchor;
            }
            return transform;
        }

        private Vector2 ReadLeftThumbstick()
        {
            try
            {
                Vector2 ovrStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
                if (ovrStick.sqrMagnitude > 0.001f)
                    return ovrStick;

                Vector2 rawLeft = OVRInput.Get(OVRInput.RawAxis2D.LThumbstick);
                if (rawLeft.sqrMagnitude > 0.001f)
                    return rawLeft;
            }
            catch
            {
                // Fallback when OVR runtime is not active
            }

            if (Gamepad.current != null)
            {
                Vector2 gp = Gamepad.current.leftStick.ReadValue();
                if (gp.sqrMagnitude > 0.001f)
                    return gp;
            }

            return Vector2.zero;
        }

        private Vector2 ReadRightThumbstick()
        {
            try
            {
                Vector2 ovrStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
                if (ovrStick.sqrMagnitude > 0.001f)
                    return ovrStick;

                Vector2 rawRight = OVRInput.Get(OVRInput.RawAxis2D.RThumbstick);
                if (rawRight.sqrMagnitude > 0.001f)
                    return rawRight;
            }
            catch
            {
                // Fallback when OVR runtime is not active
            }

            if (Gamepad.current != null)
            {
                Vector2 gp = Gamepad.current.rightStick.ReadValue();
                if (gp.sqrMagnitude > 0.001f)
                    return gp;
            }

            return Vector2.zero;
        }

        private bool ReadTeleportButtonHeld()
        {
            try
            {
                if (OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch) ||
                    OVRInput.Get(OVRInput.RawButton.B))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore when OVR runtime is inactive
            }

            return false;
        }

        private bool ReadTeleportButtonReleased()
        {
            try
            {
                if (OVRInput.GetUp(OVRInput.Button.Two, OVRInput.Controller.RTouch) ||
                    OVRInput.GetUp(OVRInput.RawButton.B))
                {
                    return true;
                }
            }
            catch
            {
                // Ignore when OVR runtime is inactive
            }

            return false;
        }

        private void SetupTeleportReticle()
        {
            if (_teleportReticle != null)
                return;

            _teleportReticle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _teleportReticle.name = "VRTeleportReticle";
            _teleportReticle.transform.SetParent(transform, false);
            _teleportReticle.transform.localScale = new Vector3(0.35f, 0.005f, 0.35f);

            Collider col = _teleportReticle.GetComponent<Collider>();
            if (col != null)
                DestroyImmediate(col);

            Renderer rend = _teleportReticle.GetComponent<Renderer>();
            if (rend != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    _reticleMaterial = new Material(shader) { name = "TeleportReticleMat" };
                    _reticleMaterial.color = new Color(0.2f, 0.9f, 1f, 0.85f);
                    rend.sharedMaterial = _reticleMaterial;
                }
            }

            _teleportReticle.SetActive(false);
        }

        private void ResolveReferences()
        {
            if (_rigReferences == null)
                _rigReferences = GetComponent<VRRigReferences>();
            if (_rigReferences == null)
                _rigReferences = VRRigReferences.Instance;

            if (_evidenceManager == null)
                _evidenceManager = FindAnyObjectByType<CrimeSceneEvidenceManager>();
        }
    }
}
