using System;
using UnityEngine;
using UnityEngine.InputSystem;
using CrimeSceneVR.Core;
using CrimeSceneVR.Session;

namespace CrimeSceneVR.Interaction
{
    /// <summary>
    /// Implements VR controller-based ray interaction for forensic evidence inspection and tagging.
    /// Supports Meta Quest Touch controllers via OVRInput and falls back gracefully to Unity Input System.
    /// Preserves existing CrimeSceneEvidenceManager rules, 5cm spatial accuracy calculations, and event logging.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Crime Scene VR/Interaction/VR Evidence Interaction")]
    public class VREvidenceInteraction : MonoBehaviour
    {
        [Header("System References")]
        [Tooltip("Reference to the scene evidence manager. Auto-resolved if null.")]
        [SerializeField] private CrimeSceneEvidenceManager _evidenceManager;

        [Tooltip("Transform from which the ray is cast. Defaults to this transform or RightHandAnchor.")]
        [SerializeField] private Transform _rayOrigin;

        [Header("VR Controller Configuration")]
        [Tooltip("OVRInput controller mapping. Defaults to RTouch (Right Touch Controller).")]
        [SerializeField] private OVRInput.Controller _controller = OVRInput.Controller.RTouch;

        [Tooltip("Maximum raycast reach in metres.")]
        [SerializeField] private float _maxRayDistance = 25f;

        [Header("Visual Laser Pointer")]
        [SerializeField] private bool _showLaser = true;
        [SerializeField] private float _laserWidth = 0.005f;
        [SerializeField] private Color _idleLaserColor = new Color(0.2f, 0.7f, 1f, 0.5f);
        [SerializeField] private Color _hoverLaserColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        [SerializeField] private Color _taggedLaserColor = new Color(0.2f, 1f, 0.3f, 0.95f);

        private LineRenderer _laserLine;
        private Material _laserMaterial;
        private EvidenceObject _hoveredEvidence;
        private EvidenceObject _selectedEvidence;
        private bool _isVrActive;
        private MaterialPropertyBlock _propertyBlock;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public CrimeSceneEvidenceManager EvidenceManager => _evidenceManager;
        public EvidenceObject HoveredEvidence => _hoveredEvidence;
        public EvidenceObject SelectedEvidence => _selectedEvidence;
        public Transform RayOrigin => _rayOrigin != null ? _rayOrigin : transform;
        public bool IsVrActive => _isVrActive;

        public event Action<EvidenceObject> OnEvidenceHovered;
        public event Action<EvidenceObject> OnEvidenceUnhovered;
        public event Action<EvidenceObject> OnEvidenceInspected;
        public event Action<EvidenceObject, Vector3> OnEvidenceTagged;

        public void Configure(CrimeSceneEvidenceManager manager, Transform rayOrigin = null)
        {
            _evidenceManager = manager;
            if (rayOrigin != null)
                _rayOrigin = rayOrigin;
        }

        private void Awake()
        {
            ResolveReferences();
            SetupLaserRenderer();
        }

        private void Start()
        {
            ResolveReferences();
        }

        private void Update()
        {
            ResolveReferences();
            CheckVrPresence();

            RaycastHit hit;
            bool hasHit = ProcessRaycast(out hit, out EvidenceObject hitEvidence);

            UpdateHoverState(hitEvidence);
            UpdateLaserVisual(hasHit, hit);
            ProcessControllerInputs(hitEvidence, hasHit, hit);
        }

        private void OnDisable()
        {
            ClearHover();
            if (_laserLine != null)
                _laserLine.enabled = false;
        }

        private void OnDestroy()
        {
            if (_laserMaterial != null)
            {
                DestroyImmediate(_laserMaterial);
                _laserMaterial = null;
            }
        }

        /// <summary>
        /// Performs raycast forward from the ray origin and identifies any hit EvidenceObject.
        /// Public for testing and inspection.
        /// </summary>
        public bool ProcessRaycast(out RaycastHit hit, out EvidenceObject evidence)
        {
            hit = default;
            evidence = null;

            Transform origin = RayOrigin;
            if (origin == null)
                return false;

            if (!Application.isPlaying)
                Physics.SyncTransforms();

            Ray ray = new Ray(origin.position, origin.forward);
            bool hasHit = Physics.Raycast(ray, out hit, _maxRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            if (hasHit && hit.collider != null)
            {
                evidence = hit.collider.GetComponentInParent<EvidenceObject>();
            }

            return hasHit;
        }

        private void UpdateHoverState(EvidenceObject newHover)
        {
            if (_hoveredEvidence == newHover)
                return;

            if (_hoveredEvidence != null && _hoveredEvidence != _selectedEvidence)
            {
                ClearEvidenceHighlight(_hoveredEvidence);
                OnEvidenceUnhovered?.Invoke(_hoveredEvidence);
            }

            _hoveredEvidence = newHover;

            if (_hoveredEvidence != null)
            {
                ApplyEvidenceHighlight(_hoveredEvidence, _hoverLaserColor);
                OnEvidenceHovered?.Invoke(_hoveredEvidence);
            }
        }

        private void ClearHover()
        {
            if (_hoveredEvidence != null && _hoveredEvidence != _selectedEvidence)
            {
                ClearEvidenceHighlight(_hoveredEvidence);
            }
            _hoveredEvidence = null;
        }

        private void ProcessControllerInputs(EvidenceObject hitEvidence, bool hasHit, RaycastHit hit)
        {
            // 1. Check for Inspect/Select input (Index Trigger)
            bool inspectPressed = IsInspectButtonPressed();

            // 2. Check for Tag input (A Button or Grip Trigger)
            bool tagPressed = IsTagButtonPressed();

            // Handle Inspect/Select
            if (inspectPressed && hitEvidence != null)
            {
                InspectEvidence(hitEvidence);
            }

            // Handle Tagging
            if (tagPressed)
            {
                if (hitEvidence != null)
                {
                    Vector3 tagPosition = hit.point;
                    TagEvidence(hitEvidence, tagPosition);
                }
                else if (_selectedEvidence != null && hasHit)
                {
                    // If evidence is selected and user aims at floor or adjacent spot, place tag at hit point
                    TagEvidence(_selectedEvidence, hit.point);
                }
                else if (_selectedEvidence != null)
                {
                    TagEvidence(_selectedEvidence, _selectedEvidence.transform.position);
                }
            }
        }

        /// <summary>
        /// Selects and inspects the specified evidence object.
        /// </summary>
        public bool InspectEvidence(EvidenceObject evidence)
        {
            if (evidence == null || _evidenceManager == null)
                return false;

            if (!_evidenceManager.IsSessionStarted || _evidenceManager.IsSessionCompleted)
            {
                _evidenceManager.StartSession();
                SessionManager.Instance?.StartSession();
            }

            // Highlight as selected
            if (_selectedEvidence != evidence)
            {
                if (_selectedEvidence != null)
                    ClearEvidenceHighlight(_selectedEvidence);
                _selectedEvidence = evidence;
                ApplyEvidenceHighlight(_selectedEvidence, _hoverLaserColor);
            }

            _evidenceManager.NotifySelected(evidence);
            bool success = _evidenceManager.TryInspect(evidence);
            if (success)
            {
                OnEvidenceInspected?.Invoke(evidence);
            }
            return success;
        }

        /// <summary>
        /// Tags the specified evidence at the given world position.
        /// Auto-inspects the evidence first if not already discovered, ensuring valid tagging order.
        /// </summary>
        public bool TagEvidence(EvidenceObject evidence, Vector3 tagPosition)
        {
            if (evidence == null || _evidenceManager == null)
                return false;

            // Ensure evidence is registered
            if (!_evidenceManager.IsRegistered(evidence))
            {
                Debug.LogWarning($"[VREvidenceInteraction] '{evidence.name}' is not registered with CrimeSceneEvidenceManager.");
                return false;
            }

            // Prevent duplicate tagging
            if (evidence.IsTagged)
            {
                Debug.LogWarning($"[VREvidenceInteraction] '{evidence.EvidenceId}' is already tagged. Duplicate tag prevented.");
                return false;
            }

            // Ensure session is started/active
            if (!_evidenceManager.IsSessionStarted || _evidenceManager.IsSessionCompleted)
            {
                _evidenceManager.StartSession();
                SessionManager.Instance?.StartSession();
            }

            // Evidence must be discovered/inspected before tagging
            if (!evidence.IsDiscovered)
            {
                _evidenceManager.NotifySelected(evidence);
                _evidenceManager.TryInspect(evidence);
            }

            bool success = _evidenceManager.TryTag(evidence, tagPosition);
            if (success)
            {
                FlashLaserColor(_taggedLaserColor);
                OnEvidenceTagged?.Invoke(evidence, tagPosition);
            }
            return success;
        }

        private bool IsInspectButtonPressed()
        {
            // OVRInput Primary Index Trigger
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, _controller) ||
                    OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
                {
                    return true;
                }
            }
            catch
            {
                // Safe fallback when OVR runtime is inactive
            }

            // Unity Input System XR Trigger or spacebar test fallback
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                return true;
            }

            return false;
        }

        private bool IsTagButtonPressed()
        {
            // OVRInput 'A' button (Button.One) or Grip Trigger (PrimaryHandTrigger)
            try
            {
                if (OVRInput.GetDown(OVRInput.Button.One, _controller) ||
                    OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, _controller) ||
                    OVRInput.GetDown(OVRInput.RawButton.A) ||
                    OVRInput.GetDown(OVRInput.RawButton.RHandTrigger))
                {
                    return true;
                }
            }
            catch
            {
                // Safe fallback when OVR runtime is inactive
            }

            // Keyboard 'T' key fallback for editor testing
            if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
            {
                return true;
            }

            return false;
        }

        private void CheckVrPresence()
        {
            try
            {
                _isVrActive = OVRManager.isHmdPresent || (OVRInput.GetConnectedControllers() != OVRInput.Controller.None);
            }
            catch
            {
                _isVrActive = false;
            }
        }

        private void SetupLaserRenderer()
        {
            _laserLine = GetComponent<LineRenderer>();
            if (_laserLine == null)
            {
                _laserLine = gameObject.AddComponent<LineRenderer>();
            }

            _laserLine.positionCount = 2;
            _laserLine.startWidth = _laserWidth;
            _laserLine.endWidth = _laserWidth * 0.5f;
            _laserLine.useWorldSpace = true;

            // Simple unlit material for clear visibility in VR
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            _laserMaterial = new Material(shader) { name = "VRLaserMaterial" };
            _laserMaterial.color = _idleLaserColor;
            _laserLine.material = _laserMaterial;
            _laserLine.startColor = _idleLaserColor;
            _laserLine.endColor = _idleLaserColor;
            _laserLine.enabled = _showLaser;
        }

        private void UpdateLaserVisual(bool hasHit, RaycastHit hit)
        {
            if (_laserLine == null) return;

            Transform origin = RayOrigin;
            if (origin == null)
            {
                _laserLine.enabled = false;
                return;
            }

            _laserLine.enabled = _showLaser;

            Vector3 startPos = origin.position;
            Vector3 endPos = hasHit ? hit.point : (startPos + origin.forward * _maxRayDistance);

            _laserLine.SetPosition(0, startPos);
            _laserLine.SetPosition(1, endPos);

            Color targetColor = _hoveredEvidence != null ? _hoverLaserColor : _idleLaserColor;
            if (_laserMaterial != null)
                _laserMaterial.color = targetColor;
            _laserLine.startColor = targetColor;
            _laserLine.endColor = targetColor;
        }

        private void FlashLaserColor(Color color)
        {
            if (_laserLine != null)
            {
                _laserLine.startColor = color;
                _laserLine.endColor = color;
            }
        }

        private void ApplyEvidenceHighlight(EvidenceObject evidence, Color color)
        {
            if (evidence == null) return;
            var renderers = evidence.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                _propertyBlock ??= new MaterialPropertyBlock();
                r.GetPropertyBlock(_propertyBlock);
                if (r.sharedMaterial != null && r.sharedMaterial.HasProperty(BaseColorId))
                {
                    Color baseColor = r.sharedMaterial.GetColor(BaseColorId);
                    _propertyBlock.SetColor(BaseColorId, Color.Lerp(baseColor, color, 0.75f));
                    r.SetPropertyBlock(_propertyBlock);
                }
            }
        }

        private void ClearEvidenceHighlight(EvidenceObject evidence)
        {
            if (evidence == null) return;
            var renderers = evidence.GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                renderers[i].SetPropertyBlock(null);
            }
        }

        private void ResolveReferences()
        {
            if (_evidenceManager == null)
            {
                _evidenceManager = FindAnyObjectByType<CrimeSceneEvidenceManager>();
            }

            if (_rayOrigin == null)
            {
                // Attempt to resolve from VRRigReferences
                var rig = VRRigReferences.Instance;
                if (rig != null && rig.RightHandAnchor != null)
                {
                    _rayOrigin = rig.RightHandAnchor;
                }
                else
                {
                    _rayOrigin = transform;
                }
            }
        }
    }
}
