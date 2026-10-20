using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CrimeSceneVR.Analytics
{
    /// <summary>
    /// Validation and statistical scoring summary of a participant's timeline reconstruction.
    /// </summary>
    [Serializable]
    public class TimelineValidationResult
    {
        public bool IsComplete { get; }
        public int GroundTruthCount { get; }
        public int SubmittedCount { get; }
        public int ExactMatchCount { get; }
        public double ExactMatchPercentage => GroundTruthCount > 0 ? (double)ExactMatchCount / GroundTruthCount * 100.0 : 0.0;

        public IReadOnlyList<string> OmittedEvidenceIds { get; }
        public IReadOnlyList<string> UnexpectedEvidenceIds { get; }
        public IReadOnlyList<string> DuplicateEvidenceIds { get; }
        public IReadOnlyList<string> MisplacedEvidenceIds { get; }

        public KendallTauResult KendallResult { get; }
        public double KendallTauScore => KendallResult.Tau;
        public string SummaryNotes { get; }

        public TimelineValidationResult(
            bool isComplete,
            int groundTruthCount,
            int submittedCount,
            int exactMatchCount,
            List<string> omitted,
            List<string> unexpected,
            List<string> duplicates,
            List<string> misplaced,
            KendallTauResult kendallResult,
            string summaryNotes)
        {
            IsComplete = isComplete;
            GroundTruthCount = groundTruthCount;
            SubmittedCount = submittedCount;
            ExactMatchCount = exactMatchCount;
            OmittedEvidenceIds = omitted ?? new List<string>();
            UnexpectedEvidenceIds = unexpected ?? new List<string>();
            DuplicateEvidenceIds = duplicates ?? new List<string>();
            MisplacedEvidenceIds = misplaced ?? new List<string>();
            KendallResult = kendallResult;
            SummaryNotes = summaryNotes;
        }

        public override string ToString()
        {
            return $"Timeline Submission: Matches={ExactMatchCount}/{GroundTruthCount} ({ExactMatchPercentage:F1}%), Tau-b={KendallTauScore:F3}, Omitted={OmittedEvidenceIds.Count}, Duplicates={DuplicateEvidenceIds.Count} | {SummaryNotes}";
        }
    }

    /// <summary>
    /// Manages crime scene chronological reconstruction, ground-truth narrative sequencing,
    /// and Kendall's Tau sequencing analytics.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Crime Scene VR/Analytics/Timeline Manager")]
    public class TimelineManager : MonoBehaviour
    {
        private static TimelineManager _instance;
        public static TimelineManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<TimelineManager>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Canonical Ground Truth Sequence")]
        [Tooltip("The canonical chronological narrative sequence of the crime scene evidence.")]
        [SerializeField]
        private List<string> _groundTruthSequence = new List<string>
        {
            "EV-001", // 1. Ballistic casing
            "EV-002", // 2. Weapon
            "EV-003", // 3. Blood-spatter pattern
            "EV-004", // 4. Footwear impression
            "EV-005", // 5. Broken glass
            "EV-006", // 6. Mobile phone
            "EV-007", // 7. Bloodied object
            "EV-008"  // 8. Trace evidence
        };

        [Header("Recorded Participant Sequence")]
        [SerializeField] private List<string> _participantDiscoveredSequence = new List<string>();
        [SerializeField] private List<string> _participantTaggedSequence = new List<string>();

        private CrimeSceneEvidenceManager _evidenceManager;

        public IReadOnlyList<string> GroundTruthSequence => _groundTruthSequence;
        public IReadOnlyList<string> DiscoveredSequence => _participantDiscoveredSequence;
        public IReadOnlyList<string> TaggedSequence => _participantTaggedSequence;

        public event Action<TimelineValidationResult> OnTimelineEvaluated;

        private void Awake()
        {
            if (_instance == null || _instance == this)
            {
                _instance = this;
            }
            else
            {
                Debug.LogWarning("[TimelineManager] Duplicate instance detected. Destroying duplicate.", this);
                Destroy(this);
                return;
            }
        }

        private void Start()
        {
            HookEvidenceManager();
        }

        private void OnDestroy()
        {
            UnhookEvidenceManager();
            if (_instance == this)
                _instance = null;
        }

        private void HookEvidenceManager()
        {
            if (_evidenceManager == null)
            {
                _evidenceManager = FindAnyObjectByType<CrimeSceneEvidenceManager>();
            }

            if (_evidenceManager != null)
            {
                _evidenceManager.EvidenceDiscovered -= HandleEvidenceDiscovered;
                _evidenceManager.EvidenceDiscovered += HandleEvidenceDiscovered;
                _evidenceManager.EvidenceTagged -= HandleEvidenceTagged;
                _evidenceManager.EvidenceTagged += HandleEvidenceTagged;
            }
        }

        private void UnhookEvidenceManager()
        {
            if (_evidenceManager != null)
            {
                _evidenceManager.EvidenceDiscovered -= HandleEvidenceDiscovered;
                _evidenceManager.EvidenceTagged -= HandleEvidenceTagged;
            }
        }

        private void HandleEvidenceDiscovered(EvidenceObject evidence)
        {
            if (evidence != null && !string.IsNullOrWhiteSpace(evidence.EvidenceId))
            {
                RecordDiscovery(evidence.EvidenceId);
            }
        }

        private void HandleEvidenceTagged(EvidenceObject evidence)
        {
            if (evidence != null && !string.IsNullOrWhiteSpace(evidence.EvidenceId))
            {
                RecordTag(evidence.EvidenceId);
            }
        }

        public void SetGroundTruthSequence(IEnumerable<string> sequence)
        {
            if (sequence == null) return;
            _groundTruthSequence = sequence.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();
        }

        public void RecordDiscovery(string evidenceId)
        {
            if (string.IsNullOrWhiteSpace(evidenceId)) return;
            string id = evidenceId.Trim();
            if (!_participantDiscoveredSequence.Contains(id))
            {
                _participantDiscoveredSequence.Add(id);
            }
        }

        public void RecordTag(string evidenceId)
        {
            if (string.IsNullOrWhiteSpace(evidenceId)) return;
            string id = evidenceId.Trim();
            if (!_participantTaggedSequence.Contains(id))
            {
                _participantTaggedSequence.Add(id);
            }
        }

        public void ClearTimeline()
        {
            _participantDiscoveredSequence.Clear();
            _participantTaggedSequence.Clear();
        }

        /// <summary>
        /// Evaluates a submitted timeline sequence against ground truth using Kendall's Tau-b.
        /// </summary>
        public TimelineValidationResult SubmitTimeline(IList<string> submittedSequence)
        {
            if (submittedSequence == null)
            {
                submittedSequence = new List<string>();
            }

            var cleanSubmitted = submittedSequence
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .ToList();

            var gtSet = new HashSet<string>(_groundTruthSequence, StringComparer.OrdinalIgnoreCase);
            var seenSubmitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicates = new List<string>();
            var unexpected = new List<string>();

            foreach (var item in cleanSubmitted)
            {
                if (!gtSet.Contains(item))
                {
                    unexpected.Add(item);
                }
                if (!seenSubmitted.Add(item))
                {
                    duplicates.Add(item);
                }
            }

            var omitted = _groundTruthSequence.Where(gt => !seenSubmitted.Contains(gt)).ToList();

            // Calculate exact position matches
            int exactMatches = 0;
            var misplaced = new List<string>();
            for (int i = 0; i < Math.Min(_groundTruthSequence.Count, cleanSubmitted.Count); i++)
            {
                if (string.Equals(_groundTruthSequence[i], cleanSubmitted[i], StringComparison.OrdinalIgnoreCase))
                {
                    exactMatches++;
                }
                else
                {
                    misplaced.Add(cleanSubmitted[i]);
                }
            }

            // Calculate Kendall's Tau-b
            KendallTauResult kendallResult = KendallTau.Calculate(_groundTruthSequence, cleanSubmitted, penalizeOmissions: true);

            bool isComplete = (omitted.Count == 0 && unexpected.Count == 0 && duplicates.Count == 0);
            string notes = isComplete
                ? "Full sequence submitted with complete ground-truth coverage."
                : $"Incomplete: {omitted.Count} omitted, {duplicates.Count} duplicates, {unexpected.Count} unexpected.";

            var result = new TimelineValidationResult(
                isComplete,
                _groundTruthSequence.Count,
                cleanSubmitted.Count,
                exactMatches,
                omitted,
                unexpected,
                duplicates,
                misplaced,
                kendallResult,
                notes);

            OnTimelineEvaluated?.Invoke(result);
            return result;
        }

        /// <summary>
        /// Evaluates the current runtime tagging sequence against ground truth.
        /// </summary>
        public TimelineValidationResult EvaluateCurrentTaggedSequence()
        {
            return SubmitTimeline(_participantTaggedSequence);
        }
    }
}
