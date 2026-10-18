using System.Text.RegularExpressions;
using UnityEngine;

public enum EvidenceCategory
{
    Unspecified = 0,
    Ballistic = 1,
    Weapon = 2,
    Biological = 3,
    Impression = 4,
    Glass = 5,
    Digital = 6,
    Trace = 7
}

/// <summary>
/// Scene component for one evidence item. Ground truth is always a world-space position.
/// </summary>
[DisallowMultipleComponent]
public class EvidenceObject : MonoBehaviour
{
    public const string EvidenceIdPattern = @"^EV-\d{3}$";

    [SerializeField] string evidenceId = "";
    [SerializeField] string evidenceName = "";
    [SerializeField] [TextArea(2, 5)] string description = "";
    [SerializeField] EvidenceCategory category = EvidenceCategory.Unspecified;
    [SerializeField] Vector3 groundTruthWorldPosition;
    [SerializeField] bool groundTruthCaptured;

    [SerializeField] bool isDiscovered;
    [SerializeField] bool isTagged;
    [SerializeField] bool isInspected;
    [SerializeField] float discoverySessionTime = -1f;
    [SerializeField] float tagSessionTime = -1f;
    [SerializeField] Vector3 tagWorldPosition;
    [SerializeField] float spatialErrorMetres = -1f;

    EvidenceTag tagMarker;
    bool groundTruthLockedForSession;

    public string EvidenceId => evidenceId;
    public string EvidenceName => evidenceName;
    public string Description => description;
    public EvidenceCategory Category => category;
    public Vector3 GroundTruthWorldPosition => groundTruthWorldPosition;
    public bool HasCapturedGroundTruth => groundTruthCaptured;
    public bool IsGroundTruthLockedForSession => groundTruthLockedForSession;
    public bool IsDiscovered => isDiscovered;
    public bool IsTagged => isTagged;
    public bool IsInspected => isInspected;
    public float DiscoverySessionTime => discoverySessionTime;
    public float TagSessionTime => tagSessionTime;
    public Vector3 TagWorldPosition => tagWorldPosition;
    public float SpatialErrorMetres => spatialErrorMetres;
    public EvidenceTag TagMarker => tagMarker;

    public static bool IsValidEvidenceId(string id)
    {
        return !string.IsNullOrWhiteSpace(id) && Regex.IsMatch(id.Trim(), EvidenceIdPattern);
    }

    public void Configure(string id, string displayName, string itemDescription, EvidenceCategory itemCategory)
    {
        evidenceId = string.IsNullOrWhiteSpace(id) ? "" : id.Trim();
        evidenceName = displayName ?? "";
        description = itemDescription ?? "";
        category = itemCategory;
    }

    public void CaptureGroundTruthFromWorldPosition()
    {
        if (groundTruthLockedForSession)
        {
            Debug.LogWarning($"Forensic: {name} ground truth is locked for this investigation session and was not changed.");
            return;
        }

        groundTruthWorldPosition = transform.position;
        groundTruthCaptured = true;
    }

    public void LockGroundTruthAtCurrentWorldPosition()
    {
        if (groundTruthLockedForSession)
            return;

        groundTruthWorldPosition = transform.position;
        groundTruthCaptured = true;
        groundTruthLockedForSession = true;
    }

    public void MarkInspected(float sessionTimeSeconds)
    {
        isInspected = true;
    }

    public void MarkDiscovered(float sessionTimeSeconds)
    {
        if (isDiscovered)
            return;

        isDiscovered = true;
        discoverySessionTime = sessionTimeSeconds;
    }

    public void MarkTagged(float sessionTimeSeconds, Vector3 worldTagPosition, float errorMetres)
    {
        isTagged = true;
        tagSessionTime = sessionTimeSeconds;
        tagWorldPosition = worldTagPosition;
        spatialErrorMetres = errorMetres;
    }

    public void AssignTagMarker(EvidenceTag marker)
    {
        tagMarker = marker;
    }

    void OnValidate()
    {
        if (!string.IsNullOrWhiteSpace(evidenceId))
        {
            evidenceId = evidenceId.Trim();
            if (!IsValidEvidenceId(evidenceId))
            {
                Debug.LogError($"Forensic: '{name}' has invalid evidence ID '{evidenceId}'. Use the form EV-001.", this);
            }
            else
            {
                WarnIfDuplicateId();
            }
        }

        if (Application.isPlaying || groundTruthLockedForSession)
            return;

#if UNITY_EDITOR
        if (UnityEditor.EditorUtility.IsPersistent(this) || UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this))
            return;
#endif
        groundTruthWorldPosition = transform.position;
        groundTruthCaptured = true;
    }

    void WarnIfDuplicateId()
    {
        var others = FindObjectsByType<EvidenceObject>(FindObjectsInactive.Include);
        for (int i = 0; i < others.Length; i++)
        {
            EvidenceObject other = others[i];
            if (other == this || other.evidenceId != evidenceId)
                continue;

            Debug.LogError($"Forensic: duplicate evidence ID '{evidenceId}' on '{name}' and '{other.name}'.", this);
            return;
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.84f, 0.2f, 0.9f);
        Vector3 point = groundTruthCaptured ? groundTruthWorldPosition : transform.position;
        Gizmos.DrawWireSphere(point, 0.05f);
    }
}
