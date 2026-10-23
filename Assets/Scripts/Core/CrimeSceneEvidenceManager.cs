using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registers scene evidence and records discovery and tagging.
/// Future VR input should call these methods instead of duplicating the rules.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public class CrimeSceneEvidenceManager : MonoBehaviour
{
    [SerializeField] InvestigationEventLogger eventLogger;
    [SerializeField] float accuracyThresholdMetres = 0.05f;
    [SerializeField] bool autoStartSession = true;
    [SerializeField] Transform investigationFloorReference;
    [SerializeField] bool investigationFloorAssigned;
    [SerializeField] Bounds investigationFloorBounds;
    [SerializeField] Transform markersRoot;
    [SerializeField] GameObject markerPrefab;
    [SerializeField] Material markerMaterial;

    readonly List<EvidenceObject> registered = new List<EvidenceObject>();
    readonly Dictionary<string, EvidenceObject> byId = new Dictionary<string, EvidenceObject>();

    EvidenceObject currentSelection;
    string sessionId = "";
    float sessionStartUnscaledTime;
    bool sessionStarted;
    bool sessionCompleted;
    bool sessionStartLogged;

    public float AccuracyThresholdMetres => accuracyThresholdMetres;
    public bool HasInvestigationFloor => investigationFloorAssigned;
    public Bounds InvestigationFloorBounds => investigationFloorBounds;
    public Transform InvestigationFloorReference => investigationFloorReference;
    public string SessionId => sessionId;
    public bool IsSessionStarted => sessionStarted;
    public bool IsSessionCompleted => sessionCompleted;
    public int RegisteredCount => registered.Count;
    public EvidenceObject CurrentSelection => currentSelection;
    public InvestigationEventLogger EventLogger => eventLogger;
    public string LogFilePath => eventLogger != null ? eventLogger.LogFilePath : "";
    public IReadOnlyList<EvidenceObject> RegisteredEvidence => registered;

    public event Action SessionStarted;
    public event Action<EvidenceObject> EvidenceSelected;
    public event Action<EvidenceObject> EvidenceInspected;
    public event Action<EvidenceObject> EvidenceDiscovered;
    public event Action<EvidenceObject> EvidenceTagged;
    public event Action ProgressChanged;
    public event Action SessionCompleted;

    public int DiscoveredCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < registered.Count; i++)
            {
                if (registered[i] != null && registered[i].IsDiscovered)
                    count++;
            }
            return count;
        }
    }

    public int TaggedCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < registered.Count; i++)
            {
                if (registered[i] != null && registered[i].IsTagged)
                    count++;
            }
            return count;
        }
    }

    public float RelativeTimeSeconds
    {
        get
        {
            if (!sessionStarted)
                return 0f;
            return Mathf.Max(0f, Time.unscaledTime - sessionStartUnscaledTime);
        }
    }

    void Awake()
    {
        ResolveLogger();
    }

    void Start()
    {
        RegisterAllInScene();
        if (autoStartSession)
            StartSession();
    }

    public void AssignInvestigationFloor(Transform floorReference)
    {
        investigationFloorReference = floorReference;
        if (!RefreshInvestigationFloorBounds())
            Debug.LogError("Forensic: the selected floor reference has no renderers, so floor bounds were not assigned. The building was not modified.");
    }

    public bool RefreshInvestigationFloorBounds()
    {
        if (investigationFloorReference == null)
        {
            investigationFloorAssigned = false;
            return false;
        }

        var renderers = investigationFloorReference.GetComponentsInChildren<Renderer>();
        if (renderers == null || renderers.Length == 0)
        {
            investigationFloorAssigned = false;
            return false;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        bounds.Expand(new Vector3(1f, 1.5f, 1f));
        investigationFloorBounds = bounds;
        investigationFloorAssigned = true;
        return true;
    }

    public void RegisterAllInScene()
    {
        RefreshInvestigationFloorBounds();
        var found = FindObjectsByType<EvidenceObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        System.Array.Sort(found, (a, b) => string.CompareOrdinal(a.EvidenceId, b.EvidenceId));
        for (int i = 0; i < found.Length; i++)
            RegisterEvidence(found[i]);

        Debug.Log("Forensic: registered " + registered.Count + " evidence object(s).");
        ProgressChanged?.Invoke();
    }

    public bool RegisterEvidence(EvidenceObject evidence)
    {
        if (evidence == null)
        {
            Debug.LogError("Forensic: ignored a null evidence registration.");
            return false;
        }

        if (!EvidenceObject.IsValidEvidenceId(evidence.EvidenceId))
        {
            Debug.LogError("Forensic: '" + evidence.name + "' was not registered. Evidence ID '" + evidence.EvidenceId + "' is empty or not in the form EV-001.", evidence);
            return false;
        }

        if (byId.TryGetValue(evidence.EvidenceId, out EvidenceObject existing))
        {
            if (existing == evidence)
                return true;

            Debug.LogError("Forensic: duplicate evidence ID '" + evidence.EvidenceId + "' on '" + evidence.name + "' and '" + existing.name + "'. The second object was not registered.", evidence);
            return false;
        }

        evidence.LockGroundTruthAtCurrentWorldPosition();
        registered.Add(evidence);
        byId.Add(evidence.EvidenceId, evidence);
        ReportFloorMismatch(evidence);
        return true;
    }

    public bool TryGetEvidence(string evidenceId, out EvidenceObject evidence)
    {
        evidence = null;
        if (string.IsNullOrWhiteSpace(evidenceId))
            return false;
        return byId.TryGetValue(evidenceId.Trim(), out evidence) && evidence != null;
    }

    public bool IsRegistered(EvidenceObject evidence)
    {
        return evidence != null && byId.TryGetValue(evidence.EvidenceId, out EvidenceObject registeredEvidence) && registeredEvidence == evidence;
    }

    public float CalculateSpatialError(Vector3 tagPosition, Vector3 groundTruthPosition)
    {
        return Vector3.Distance(tagPosition, groundTruthPosition);
    }

    public bool IsWithinAccuracyTolerance(float errorMetres)
    {
        return errorMetres <= accuracyThresholdMetres;
    }

    public void NotifySelected(EvidenceObject evidence)
    {
        if (sessionCompleted || !IsRegistered(evidence))
        {
            if (evidence != null && !IsRegistered(evidence))
                Debug.LogError("Forensic: " + evidence.name + " cannot be selected because it is not registered.", evidence);
            return;
        }

        currentSelection = evidence;
        LogEvent("EVIDENCE_SELECTED", evidence, evidence.transform.position, null, null);
        EvidenceSelected?.Invoke(evidence);
    }

    public void ClearSelection()
    {
        currentSelection = null;
        EvidenceSelected?.Invoke(null);
    }

    public bool TryInspect(EvidenceObject evidence)
    {
        if (!EnsureSessionAcceptsAction(evidence, "inspected"))
            return false;

        float time = RelativeTimeSeconds;
        evidence.MarkInspected(time);
        LogEvent("EVIDENCE_INSPECTED", evidence, evidence.transform.position, null, null);
        EvidenceInspected?.Invoke(evidence);

        if (!evidence.IsDiscovered)
            TryDiscover(evidence);

        return true;
    }

    public bool TryDiscover(EvidenceObject evidence)
    {
        if (!EnsureSessionAcceptsAction(evidence, "discovered"))
            return false;

        if (evidence.IsDiscovered)
            return false;

        evidence.MarkDiscovered(RelativeTimeSeconds);
        LogEvent("EVIDENCE_DISCOVERED", evidence, evidence.GroundTruthWorldPosition, null, null);
        EvidenceDiscovered?.Invoke(evidence);
        ProgressChanged?.Invoke();
        return true;
    }

    public bool TryTag(EvidenceObject evidence, Vector3 tagWorldPosition)
    {
        if (!IsRegistered(evidence))
        {
            Debug.LogError("Forensic: evidence cannot be tagged because it is not registered.");
            return false;
        }

        if (evidence.IsTagged)
        {
            Debug.LogWarning("Forensic: " + evidence.EvidenceId + " already has a final tag. A second tag record was not created.");
            return false;
        }

        if (!sessionStarted || sessionCompleted)
        {
            StartSession();
        }

        if (!evidence.IsDiscovered)
        {
            TryInspect(evidence);
        }

        Vector3 groundTruth = evidence.GroundTruthWorldPosition;
        float error = CalculateSpatialError(tagWorldPosition, groundTruth);
        evidence.MarkTagged(RelativeTimeSeconds, tagWorldPosition, error);

        int markerNumber = TaggedCount;
        var marker = EvidenceTag.Create(evidence, tagWorldPosition, markerNumber, GetMarkersRoot(), markerPrefab, markerMaterial);
        if (marker == null)
            Debug.LogError("Forensic: " + evidence.EvidenceId + " was tagged but its marker could not be created.");

        LogEvent("EVIDENCE_TAGGED", evidence, tagWorldPosition, groundTruth, error);
        EvidenceTagged?.Invoke(evidence);
        ProgressChanged?.Invoke();

        if (registered.Count > 0 && TaggedCount == registered.Count)
            CompleteSession();

        return true;
    }

    public void StartSession()
    {
        if (sessionStarted && !sessionCompleted)
            return;

        ResolveLogger();
        sessionId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        sessionStartUnscaledTime = Time.unscaledTime;
        sessionStarted = true;
        sessionCompleted = false;

        if (eventLogger != null && eventLogger.Begin(sessionId))
        {
            LogEvent("SESSION_STARTED", null, null, null, null);
            sessionStartLogged = true;
        }

        Debug.Log("Forensic: investigation session " + sessionId + " started. Accuracy threshold is " + accuracyThresholdMetres.ToString("0.00") + " m.");
        SessionStarted?.Invoke();
        ProgressChanged?.Invoke();
    }

    public void CompleteSession()
    {
        if (!sessionStarted || sessionCompleted)
            return;

        sessionCompleted = true;
        if (sessionStartLogged)
            LogEvent("SESSION_COMPLETED", null, null, null, null);

        if (eventLogger != null)
            eventLogger.Close();

        Debug.Log("Forensic: investigation session " + sessionId + " completed. Discovered " + DiscoveredCount + "/" + RegisteredCount + ", tagged " + TaggedCount + "/" + RegisteredCount + ".");
        SessionCompleted?.Invoke();
    }

    public void ConfigureLogger(InvestigationEventLogger logger)
    {
        if (logger != null)
            eventLogger = logger;
    }

    public void SetMarkerPresentation(Transform root, GameObject prefab, Material material)
    {
        markersRoot = root;
        markerPrefab = prefab;
        markerMaterial = material;
    }

    bool EnsureSessionAcceptsAction(EvidenceObject evidence, string action)
    {
        if (!sessionStarted || sessionCompleted)
        {
            StartSession();
        }

        if (!IsRegistered(evidence))
        {
            Debug.LogError("Forensic: evidence cannot be " + action + " because it is not registered.");
            return false;
        }

        return true;
    }

    void ReportFloorMismatch(EvidenceObject evidence)
    {
        if (!investigationFloorAssigned)
            return;

        if (!investigationFloorBounds.Contains(evidence.GroundTruthWorldPosition))
        {
            Debug.LogError(
                "Forensic: " + evidence.EvidenceId + " world position " + evidence.GroundTruthWorldPosition.ToString("F3")
                + " is outside the selected investigation floor bounds (center " + investigationFloorBounds.center.ToString("F3")
                + ", size " + investigationFloorBounds.size.ToString("F3") + ").",
                evidence);
        }
    }

    void LogEvent(string eventType, EvidenceObject evidence, Vector3? worldPosition, Vector3? groundTruth, float? spatialError)
    {
        if (eventLogger == null)
        {
            Debug.LogError("Forensic: " + eventType + " was not logged because InvestigationEventLogger is missing.");
            return;
        }

        eventLogger.Log(
            sessionId,
            RelativeTimeSeconds,
            eventType,
            evidence != null ? evidence.EvidenceId : "",
            worldPosition,
            groundTruth,
            spatialError);
    }

    void ResolveLogger()
    {
        if (eventLogger != null)
            return;

        eventLogger = GetComponent<InvestigationEventLogger>();
        if (eventLogger == null)
            eventLogger = FindAnyObjectByType<InvestigationEventLogger>();
        if (eventLogger == null)
        {
            eventLogger = gameObject.AddComponent<InvestigationEventLogger>();
            Debug.LogWarning("Forensic: CrimeSceneEvidenceManager had no InvestigationEventLogger. One was added to " + name + ".");
        }
    }

    Transform GetMarkersRoot()
    {
        if (markersRoot != null)
            return markersRoot;

        var markers = new GameObject("InvestigationMarkers");
        markersRoot = markers.transform;
        return markersRoot;
    }

    void OnDestroy()
    {
        if (sessionStarted && !sessionCompleted && eventLogger != null)
            eventLogger.Close();
    }
}
