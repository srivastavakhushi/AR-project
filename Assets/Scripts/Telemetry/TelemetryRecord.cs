using System;
using UnityEngine;

namespace CrimeSceneVR.Telemetry
{
    public enum TelemetryEventType
    {
        PeriodicSample,
        SessionStart,
        SessionEnd,
        EvidenceDiscovered,
        EvidenceInspected,
        EvidenceTagged,
        EvidenceMissed,
        TimelineEventSelected,
        TimelineSubmitted,
        CustomEvent
    }

    /// <summary>
    /// Represents a single time-stamped pose snapshot or discrete forensic event in a session.
    /// </summary>
    [Serializable]
    public class TelemetryRecord
    {
        public double SessionTimestampSeconds { get; set; }
        public string IsoTimestamp { get; set; }
        public TelemetryEventType EventType { get; set; }
        public string CustomEventName { get; set; }

        public Vector3 RigPosition { get; set; }

        public Vector3 HeadPosition { get; set; }
        public Quaternion HeadRotation { get; set; }

        public Vector3 LeftHandPosition { get; set; }
        public Quaternion LeftHandRotation { get; set; }

        public Vector3 RightHandPosition { get; set; }
        public Quaternion RightHandRotation { get; set; }

        public Vector3 Velocity { get; set; }

        public string EvidenceId { get; set; }
        public Vector3? EvidencePosition { get; set; }
        public float? SpatialError { get; set; }
        public string Details { get; set; }

        public TelemetryRecord()
        {
            IsoTimestamp = DateTime.UtcNow.ToString("o");
        }

        public TelemetryRecord(
            double sessionTimestamp,
            TelemetryEventType eventType,
            string customEventName = null,
            string evidenceId = null,
            Vector3? evidencePos = null,
            float? spatialError = null,
            string details = null)
        {
            SessionTimestampSeconds = sessionTimestamp;
            IsoTimestamp = DateTime.UtcNow.ToString("o");
            EventType = eventType;
            CustomEventName = customEventName ?? eventType.ToString();
            EvidenceId = evidenceId ?? string.Empty;
            EvidencePosition = evidencePos;
            SpatialError = spatialError;
            Details = details ?? string.Empty;
        }
    }
}
