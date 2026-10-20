using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using CrimeSceneVR.Core;
using CrimeSceneVR.Session;

namespace CrimeSceneVR.Telemetry
{
    /// <summary>
    /// Captures 10 Hz periodic head and hand pose telemetry and timestamped forensic events
    /// during an active participant session using VRRigReferences.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Crime Scene VR/Telemetry/Telemetry Collector")]
    public class TelemetryCollector : MonoBehaviour
    {
        private static TelemetryCollector _instance;
        public static TelemetryCollector Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<TelemetryCollector>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        [Header("Sampling Configuration")]
        [Tooltip("Sampling frequency in Hertz (samples per second). Configured to 10 Hz by default.")]
        [Range(1f, 60f)]
        [SerializeField] private float _samplingRateHz = 10f;

        [Tooltip("Enable continuous 10 Hz periodic pose recording.")]
        [SerializeField] private bool _recordMovement = true;

        [Header("Runtime Telemetry State")]
        [SerializeField] private int _totalRecordsCount = 0;

        private readonly List<TelemetryRecord> _records = new List<TelemetryRecord>(4096);
        private Coroutine _samplingCoroutine;
        private Vector3 _lastSampledHeadPos;
        private float _lastSampleTime;
        private CrimeSceneEvidenceManager _evidenceManager;

        public IReadOnlyList<TelemetryRecord> Records => _records;
        public int RecordCount => _records.Count;
        public float SamplingRateHz => _samplingRateHz;
        public float SamplingInterval => 1f / Mathf.Max(1f, _samplingRateHz);

        public event Action<TelemetryRecord> OnRecordCaptured;

        private void Awake()
        {
            if (_instance == null || _instance == this)
            {
                _instance = this;
            }
            else
            {
                Debug.LogWarning("[TelemetryCollector] Duplicate instance detected. Destroying duplicate.", this);
                Destroy(this);
                return;
            }
        }

        private void OnEnable()
        {
            SubscribeToEvents();
        }

        private void OnDisable()
        {
            UnsubscribeFromEvents();
            StopSampling();
        }

        private void Start()
        {
            SubscribeToEvents();
        }

        private void SubscribeToEvents()
        {
            var sm = SessionManager.Instance;
            if (sm != null)
            {
                sm.OnSessionStarted -= HandleSessionStarted;
                sm.OnSessionStarted += HandleSessionStarted;
                sm.OnSessionEnded -= HandleSessionEnded;
                sm.OnSessionEnded += HandleSessionEnded;
            }

            if (_evidenceManager == null)
            {
                _evidenceManager = FindAnyObjectByType<CrimeSceneEvidenceManager>();
            }

            if (_evidenceManager != null)
            {
                _evidenceManager.EvidenceSelected -= HandleEvidenceSelected;
                _evidenceManager.EvidenceSelected += HandleEvidenceSelected;
                _evidenceManager.EvidenceInspected -= HandleEvidenceInspected;
                _evidenceManager.EvidenceInspected += HandleEvidenceInspected;
                _evidenceManager.EvidenceDiscovered -= HandleEvidenceDiscovered;
                _evidenceManager.EvidenceDiscovered += HandleEvidenceDiscovered;
                _evidenceManager.EvidenceTagged -= HandleEvidenceTagged;
                _evidenceManager.EvidenceTagged += HandleEvidenceTagged;
            }
        }

        private void UnsubscribeFromEvents()
        {
            var sm = SessionManager.Instance;
            if (sm != null)
            {
                sm.OnSessionStarted -= HandleSessionStarted;
                sm.OnSessionEnded -= HandleSessionEnded;
            }

            if (_evidenceManager != null)
            {
                _evidenceManager.EvidenceSelected -= HandleEvidenceSelected;
                _evidenceManager.EvidenceInspected -= HandleEvidenceInspected;
                _evidenceManager.EvidenceDiscovered -= HandleEvidenceDiscovered;
                _evidenceManager.EvidenceTagged -= HandleEvidenceTagged;
            }
        }

        private void HandleSessionStarted(SessionData session)
        {
            _records.Clear();
            _totalRecordsCount = 0;

            RecordEvent(TelemetryEventType.SessionStart, "SESSION_START", details: $"Participant: {session.ParticipantId}, Condition: {session.Condition}");

            if (_recordMovement)
            {
                StartSampling();
            }
        }

        private void HandleSessionEnded(SessionData session)
        {
            StopSampling();
            RecordEvent(TelemetryEventType.SessionEnd, "SESSION_END", details: $"Duration: {session.DurationSeconds:F2}s");
        }

        private void HandleEvidenceSelected(EvidenceObject evidence)
        {
            if (evidence == null) return;
            RecordEvent(TelemetryEventType.CustomEvent, "EVIDENCE_SELECTED", evidence.EvidenceId, evidence.transform.position, details: evidence.EvidenceName);
        }

        private void HandleEvidenceInspected(EvidenceObject evidence)
        {
            if (evidence == null) return;
            RecordEvent(TelemetryEventType.EvidenceInspected, "EVIDENCE_INSPECTED", evidence.EvidenceId, evidence.transform.position, details: evidence.EvidenceName);
        }

        private void HandleEvidenceDiscovered(EvidenceObject evidence)
        {
            if (evidence == null) return;
            RecordEvent(TelemetryEventType.EvidenceDiscovered, "EVIDENCE_DISCOVERED", evidence.EvidenceId, evidence.GroundTruthWorldPosition, details: evidence.EvidenceName);
        }

        private void HandleEvidenceTagged(EvidenceObject evidence)
        {
            if (evidence == null) return;
            RecordEvent(TelemetryEventType.EvidenceTagged, "EVIDENCE_TAGGED", evidence.EvidenceId, evidence.TagWorldPosition, evidence.SpatialErrorMetres, evidence.EvidenceName);
        }

        public void StartSampling()
        {
            StopSampling();
            if (gameObject.activeInHierarchy)
            {
                _samplingCoroutine = StartCoroutine(SamplingRoutine());
            }
        }

        public void StopSampling()
        {
            if (_samplingCoroutine != null)
            {
                StopCoroutine(_samplingCoroutine);
                _samplingCoroutine = null;
            }
        }

        private IEnumerator SamplingRoutine()
        {
            _lastSampleTime = Time.unscaledTime;
            var rig = VRRigReferences.Instance;
            if (rig != null && rig.MainCamera != null)
            {
                _lastSampledHeadPos = rig.MainCamera.transform.position;
            }

            while (true)
            {
                yield return new WaitForSecondsRealtime(SamplingInterval);
                CapturePeriodicSample();
            }
        }

        /// <summary>
        /// Captures a single 10 Hz pose telemetry snapshot.
        /// Public for testing and inspection.
        /// </summary>
        public TelemetryRecord CapturePeriodicSample()
        {
            float currentTime = Time.unscaledTime;
            float dt = Mathf.Max(0.001f, currentTime - _lastSampleTime);
            double sessionTime = SessionManager.Instance != null ? SessionManager.Instance.ElapsedSessionTime : currentTime;

            var rec = new TelemetryRecord(sessionTime, TelemetryEventType.PeriodicSample);
            PopulateRigPose(rec, dt);

            _records.Add(rec);
            _totalRecordsCount = _records.Count;
            _lastSampleTime = currentTime;

            OnRecordCaptured?.Invoke(rec);
            return rec;
        }

        /// <summary>
        /// Records a discrete research or evidence interaction event.
        /// </summary>
        public TelemetryRecord RecordEvent(
            TelemetryEventType eventType,
            string eventName = null,
            string evidenceId = null,
            Vector3? evidencePos = null,
            float? spatialError = null,
            string details = null)
        {
            double sessionTime = SessionManager.Instance != null ? SessionManager.Instance.ElapsedSessionTime : Time.unscaledTime;
            var rec = new TelemetryRecord(sessionTime, eventType, eventName, evidenceId, evidencePos, spatialError, details);
            PopulateRigPose(rec, 0.001f);

            _records.Add(rec);
            _totalRecordsCount = _records.Count;

            OnRecordCaptured?.Invoke(rec);
            return rec;
        }

        private void PopulateRigPose(TelemetryRecord rec, float dt)
        {
            var rig = VRRigReferences.Instance;
            if (rig != null)
            {
                if (rig.Root != null)
                    rec.RigPosition = rig.Root.position;

                if (rig.MainCamera != null)
                {
                    rec.HeadPosition = rig.MainCamera.transform.position;
                    rec.HeadRotation = rig.MainCamera.transform.rotation;
                    rec.Velocity = (rec.HeadPosition - _lastSampledHeadPos) / dt;
                    _lastSampledHeadPos = rec.HeadPosition;
                }

                if (rig.LeftHandAnchor != null)
                {
                    rec.LeftHandPosition = rig.LeftHandAnchor.position;
                    rec.LeftHandRotation = rig.LeftHandAnchor.rotation;
                }

                if (rig.RightHandAnchor != null)
                {
                    rec.RightHandPosition = rig.RightHandAnchor.position;
                    rec.RightHandRotation = rig.RightHandAnchor.rotation;
                }
            }
        }
    }
}
