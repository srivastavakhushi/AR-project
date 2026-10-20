using UnityEngine;

namespace CrimeSceneVR.Core
{
    /// <summary>
    /// Central access point exposing VR Rig anchors and components for:
    /// - Person 2: Evidence grab, inspection, interaction tools (hands, controllers, eye ray).
    /// - Person 3: Player position/rotation telemetry and tracking loggers.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Crime Scene VR/VR Rig References")]
    public class VRRigReferences : MonoBehaviour
    {
        private static VRRigReferences _instance;
        public static VRRigReferences Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<VRRigReferences>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Primary Tracking Anchors")]
        [Tooltip("Root Transform of the VR player rig.")]
        [SerializeField] private Transform _rigTransform;

        [Tooltip("TrackingSpace Transform containing all eye and hand anchors.")]
        [SerializeField] private Transform _trackingSpace;

        [Tooltip("Center Eye Anchor representing player's head and VR viewpoint.")]
        [SerializeField] private Transform _centerEyeAnchor;

        [Tooltip("Left Hand tracking anchor.")]
        [SerializeField] private Transform _leftHandAnchor;

        [Tooltip("Right Hand tracking anchor.")]
        [SerializeField] private Transform _rightHandAnchor;

        [Header("Controller Specific Anchors")]
        [SerializeField] private Transform _leftControllerAnchor;
        [SerializeField] private Transform _rightControllerAnchor;

        [Header("Core VR Components")]
        [SerializeField] private Camera _mainCamera;
        [SerializeField] private CharacterController _characterController;
        [SerializeField] private OVRCameraRig _ovrCameraRig;

        #region Public Properties for Person 2 & Person 3
        public Transform Root => _rigTransform != null ? _rigTransform : transform;
        public Transform RigTransform => Root;
        public Transform TrackingSpace => _trackingSpace;
        public Transform CenterEyeAnchor => _centerEyeAnchor;
        public Transform LeftHandAnchor => _leftHandAnchor;
        public Transform RightHandAnchor => _rightHandAnchor;
        public Transform LeftControllerAnchor => _leftControllerAnchor;
        public Transform RightControllerAnchor => _rightControllerAnchor;

        public Camera MainCamera => _mainCamera != null ? _mainCamera : ResolveMainCamera();
        public CharacterController CharacterController => _characterController;
        public OVRCameraRig OVRCameraRig => _ovrCameraRig;

        // Convenient Telemetry & Interaction Accessors
        public Vector3 HeadPosition => _centerEyeAnchor != null ? _centerEyeAnchor.position : transform.position;
        public Quaternion HeadRotation => _centerEyeAnchor != null ? _centerEyeAnchor.rotation : transform.rotation;
        public Vector3 HeadForward => _centerEyeAnchor != null ? _centerEyeAnchor.forward : transform.forward;

        public Vector3 LeftHandPosition => _leftHandAnchor != null ? _leftHandAnchor.position : transform.position;
        public Quaternion LeftHandRotation => _leftHandAnchor != null ? _leftHandAnchor.rotation : transform.rotation;

        public Vector3 RightHandPosition => _rightHandAnchor != null ? _rightHandAnchor.position : transform.position;
        public Quaternion RightHandRotation => _rightHandAnchor != null ? _rightHandAnchor.rotation : transform.rotation;
        #endregion

        private void Awake()
        {
            if (_instance == null || _instance == this)
            {
                _instance = this;
            }
            else
            {
                Destroy(this);
                return;
            }

            ResolveReferences();
        }

        private void OnEnable()
        {
            if (_instance == null)
            {
                _instance = this;
            }
        }

        private void Start()
        {
            ResolveReferences();
        }

        private void OnValidate()
        {
            ResolveReferences();
        }

        private Camera ResolveMainCamera()
        {
            if (_centerEyeAnchor != null)
            {
                var cam = _centerEyeAnchor.GetComponent<Camera>();
                if (cam != null) return cam;
            }
            if (Camera.main != null) return Camera.main;
            return FindAnyObjectByType<Camera>();
        }

        public void ResolveReferences()
        {
            if (_rigTransform == null) _rigTransform = transform;
            if (_ovrCameraRig == null) _ovrCameraRig = GetComponent<OVRCameraRig>();
            if (_characterController == null) _characterController = GetComponent<CharacterController>();

            if (_ovrCameraRig != null)
            {
                if (_trackingSpace == null && _ovrCameraRig.trackingSpace != null) _trackingSpace = _ovrCameraRig.trackingSpace;
                if (_centerEyeAnchor == null && _ovrCameraRig.centerEyeAnchor != null) _centerEyeAnchor = _ovrCameraRig.centerEyeAnchor;
                if (_leftHandAnchor == null && _ovrCameraRig.leftHandAnchor != null) _leftHandAnchor = _ovrCameraRig.leftHandAnchor;
                if (_rightHandAnchor == null && _ovrCameraRig.rightHandAnchor != null) _rightHandAnchor = _ovrCameraRig.rightHandAnchor;
            }

            if (_trackingSpace == null)
            {
                _trackingSpace = transform.Find("TrackingSpace");
            }

            if (_trackingSpace != null)
            {
                if (_centerEyeAnchor == null) _centerEyeAnchor = _trackingSpace.Find("CenterEyeAnchor");
                if (_leftHandAnchor == null) _leftHandAnchor = _trackingSpace.Find("LeftHandAnchor");
                if (_rightHandAnchor == null) _rightHandAnchor = _trackingSpace.Find("RightHandAnchor");

                if (_leftControllerAnchor == null && _leftHandAnchor != null)
                    _leftControllerAnchor = _leftHandAnchor.Find("LeftControllerAnchor");

                if (_rightControllerAnchor == null && _rightHandAnchor != null)
                    _rightControllerAnchor = _rightHandAnchor.Find("RightControllerAnchor");
            }

            if (_centerEyeAnchor != null && _mainCamera == null)
            {
                _mainCamera = _centerEyeAnchor.GetComponent<Camera>();
            }

            if (_mainCamera == null)
            {
                _mainCamera = ResolveMainCamera();
            }

            // Ensure near clip plane is 0.05m
            if (_mainCamera != null && _mainCamera.nearClipPlane > 0.05f)
            {
                _mainCamera.nearClipPlane = 0.05f;
            }
        }
    }
}
