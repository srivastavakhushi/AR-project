using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Editor and desktop input for selection, inspection, and tagging.
/// Evidence rules stay on <see cref="CrimeSceneEvidenceManager"/>.
/// </summary>
[DisallowMultipleComponent]
public class EvidenceInteraction : MonoBehaviour
{
    [SerializeField] CrimeSceneEvidenceManager evidenceManager;
    [SerializeField] Camera viewCamera;
    [SerializeField] float maxRayDistance = 40f;
    [SerializeField] bool enableDesktopNavigation = true;
    [SerializeField] float moveSpeed = 2.4f;
    [SerializeField] float sprintMultiplier = 2.2f;
    [SerializeField] float lookSensitivity = 0.12f;

    readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
    List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
    PointerEventData uiPointerData;
    EvidenceObject selected;
    float yaw;
    float pitch;
    bool lookInitialized;
    bool missingReferenceLogged;
    bool uiRaycastFailed;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    public EvidenceObject SelectedEvidence => selected;
    public CrimeSceneEvidenceManager EvidenceManager => evidenceManager;

    public void Configure(CrimeSceneEvidenceManager manager, Camera camera)
    {
        evidenceManager = manager;
        if (camera != null)
            viewCamera = camera;
    }

    void Awake()
    {
        ResolveReferences(logErrors: false);
    }

    void Start()
    {
        ResolveReferences(logErrors: true);
        InitializeLook();
    }

    void Update()
    {
        if (!ResolveReferences(logErrors: !missingReferenceLogged))
        {
            missingReferenceLogged = true;
            return;
        }

        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null)
        {
            if (!missingReferenceLogged)
            {
                Debug.Log("Forensic: EvidenceInteraction desktop mouse/keyboard not detected. Desktop input disabled; VR input active.");
                missingReferenceLogged = true;
            }
            return;
        }

        bool pointerOverUi = IsPointerOverUi(mouse);
        if (enableDesktopNavigation)
            UpdateNavigation(keyboard, mouse, pointerOverUi);

        if (keyboard.escapeKey.wasPressedThisFrame)
            evidenceManager.CompleteSession();

        if (mouse.leftButton.wasPressedThisFrame && !pointerOverUi)
            SelectFromRay(mouse.position.ReadValue());

        if (keyboard.fKey.wasPressedThisFrame)
            InspectSelected();

        if (keyboard.tKey.wasPressedThisFrame)
            TagSelectedAtCrosshair();
    }

    public bool SelectEvidence(EvidenceObject evidence)
    {
        if (evidenceManager == null)
        {
            Debug.LogError("Forensic: evidence cannot be selected because CrimeSceneEvidenceManager is missing.");
            return false;
        }

        if (evidence == null || !evidenceManager.IsRegistered(evidence))
        {
            Debug.LogWarning("Forensic: the selection was ignored because it is not a registered evidence object.");
            return false;
        }

        if (selected != evidence)
        {
            ClearHighlight();
            selected = evidence;
            ApplyHighlight(selected);
        }

        evidenceManager.NotifySelected(selected);
        return true;
    }

    public void ClearSelection()
    {
        ClearHighlight();
        selected = null;
        if (evidenceManager != null)
            evidenceManager.ClearSelection();
    }

    public bool InspectSelected()
    {
        if (selected == null)
        {
            Debug.LogWarning("Forensic: select an evidence object before inspecting it.");
            return false;
        }

        if (evidenceManager == null)
        {
            Debug.LogError("Forensic: evidence cannot be inspected because CrimeSceneEvidenceManager is missing.");
            return false;
        }

        return evidenceManager.TryInspect(selected);
    }

    public bool TagSelectedAt(Vector3 worldPosition)
    {
        if (selected == null)
        {
            Debug.LogWarning("Forensic: select an evidence object before tagging it.");
            return false;
        }

        if (evidenceManager == null)
        {
            Debug.LogError("Forensic: evidence cannot be tagged because CrimeSceneEvidenceManager is missing.");
            return false;
        }

        return evidenceManager.TryTag(selected, worldPosition);
    }

    public bool TagSelectedAtCrosshair()
    {
        if (viewCamera == null)
        {
            Debug.LogError("Forensic: the tag position could not be chosen because no camera is available.");
            return false;
        }

        Vector3 center = new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        return TagSelectedAtScreenPoint(center);
    }

    public bool TagSelectedAtScreenPoint(Vector2 screenPosition)
    {
        if (!TryRaycast(screenPosition, out RaycastHit hit))
        {
            Debug.LogWarning("Forensic: the tag ray did not hit a solid surface. Move closer or aim at the floor, a wall, or the evidence.");
            return false;
        }

        return TagSelectedAt(hit.point);
    }

    public bool ValidateConfiguration()
    {
        return ResolveReferences(logErrors: true);
    }

    bool SelectFromRay(Vector2 screenPosition)
    {
        if (!TryRaycast(screenPosition, out RaycastHit hit))
        {
            ClearSelection();
            return false;
        }

        EvidenceObject evidence = hit.collider.GetComponentInParent<EvidenceObject>();
        if (evidence == null)
        {
            ClearSelection();
            return false;
        }

        return SelectEvidence(evidence);
    }

    bool TryRaycast(Vector2 screenPosition, out RaycastHit hit)
    {
        hit = default;
        if (viewCamera == null)
            return false;

        Ray ray = viewCamera.ScreenPointToRay(screenPosition);
        return Physics.Raycast(ray, out hit, maxRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    void UpdateNavigation(Keyboard keyboard, Mouse mouse, bool pointerOverUi)
    {
        if (!lookInitialized)
            InitializeLook();

        if (mouse.rightButton.isPressed && !pointerOverUi)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * lookSensitivity;
            pitch -= delta.y * lookSensitivity;
            pitch = Mathf.Clamp(pitch, -85f, 85f);
            viewCamera.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        Vector3 input = Vector3.zero;
        if (keyboard.wKey.isPressed) input.z += 1f;
        if (keyboard.sKey.isPressed) input.z -= 1f;
        if (keyboard.dKey.isPressed) input.x += 1f;
        if (keyboard.aKey.isPressed) input.x -= 1f;
        if (keyboard.eKey.isPressed) input.y += 1f;
        if (keyboard.qKey.isPressed) input.y -= 1f;
        if (input.sqrMagnitude < 0.001f)
            return;

        float speed = moveSpeed * (keyboard.leftShiftKey.isPressed ? sprintMultiplier : 1f);
        Vector3 forward = viewCamera.transform.forward;
        Vector3 right = viewCamera.transform.right;
        forward.y = 0f;
        right.y = 0f;
        forward.Normalize();
        right.Normalize();
        Vector3 motion = (forward * input.z + right * input.x + Vector3.up * input.y);
        if (motion.sqrMagnitude > 1f)
            motion.Normalize();
        viewCamera.transform.position += motion * (speed * Time.unscaledDeltaTime);
    }

    void InitializeLook()
    {
        if (viewCamera == null)
            return;

        Vector3 euler = viewCamera.transform.eulerAngles;
        yaw = euler.y;
        pitch = euler.x;
        if (pitch > 180f)
            pitch -= 360f;
        lookInitialized = true;
    }

    void ApplyHighlight(EvidenceObject evidence)
    {
        var renderers = evidence.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            renderer.GetPropertyBlock(propertyBlock);
            if (renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty(BaseColorId))
            {
                Color baseColor = renderer.sharedMaterial.GetColor(BaseColorId);
                propertyBlock.SetColor(BaseColorId, Color.Lerp(baseColor, new Color(1f, 0.82f, 0.25f), 0.72f));
                renderer.SetPropertyBlock(propertyBlock);
            }
        }
    }

    void ClearHighlight()
    {
        if (selected == null)
            return;

        var renderers = selected.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].SetPropertyBlock(null);
    }

    bool ResolveReferences(bool logErrors)
    {
        if (viewCamera == null)
            viewCamera = GetComponent<Camera>();
        if (viewCamera == null)
            viewCamera = Camera.main;

        if (evidenceManager == null)
            evidenceManager = FindAnyObjectByType<CrimeSceneEvidenceManager>();

        bool valid = true;
        if (viewCamera == null)
        {
            valid = false;
            if (logErrors)
                Debug.LogError("Forensic: EvidenceInteraction has no camera. Assign a camera, or tag one as MainCamera. Selection is disabled.");
        }

        if (evidenceManager == null)
        {
            valid = false;
            if (logErrors)
                Debug.LogError("Forensic: EvidenceInteraction found no CrimeSceneEvidenceManager. Selection, inspection, and tagging are disabled.");
        }

        return valid;
    }

    bool IsPointerOverUi(Mouse mouse)
    {
        if (mouse == null || mouse.position == null || EventSystem.current == null)
            return false;

        try
        {
            if (uiRaycastResults == null)
                uiRaycastResults = new List<RaycastResult>();

            if (uiPointerData == null)
                uiPointerData = new PointerEventData(EventSystem.current);

            uiPointerData.Reset();
            uiPointerData.position = mouse.position.ReadValue();
            uiRaycastResults.Clear();
            EventSystem.current.RaycastAll(uiPointerData, uiRaycastResults);
            return uiRaycastResults.Count > 0;
        }
        catch (Exception exception)
        {
            if (!uiRaycastFailed)
            {
                uiRaycastFailed = true;
                Debug.LogError("Forensic: the pointer UI test failed, so clicks are treated as scene clicks. " + exception);
            }

            return false;
        }
    }

    void OnDisable()
    {
        ClearHighlight();
    }
}
