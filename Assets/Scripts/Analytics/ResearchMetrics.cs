using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CrimeSceneVR.Session;
using CrimeSceneVR.Telemetry;

namespace CrimeSceneVR.Analytics
{
    /// <summary>
    /// Aggregated research analytics and spatial error metrics for a completed session.
    /// </summary>
    [Serializable]
    public class SessionResearchSummary
    {
        public string ParticipantId { get; set; }
        public string Condition { get; set; }
        public string SessionId { get; set; }
        public SessionStatus Status { get; set; }
        public string StartTimeUtc { get; set; }
        public string EndTimeUtc { get; set; }
        public float DurationSeconds { get; set; }

        public int TotalRegisteredEvidence { get; set; }
        public int EvidenceDiscoveredCount { get; set; }
        public int EvidenceTaggedCount { get; set; }
        public double CompletionPercent => TotalRegisteredEvidence > 0 ? (double)EvidenceTaggedCount / TotalRegisteredEvidence * 100.0 : 0.0;

        public double? TimeToFirstDiscoverySeconds { get; set; }
        public double? AverageDiscoveryIntervalSeconds { get; set; }

        public double? MeanSpatialErrorMeters { get; set; }
        public double? MinSpatialErrorMeters { get; set; }
        public double? MaxSpatialErrorMeters { get; set; }

        public int SpatialWithinToleranceCount { get; set; }
        public double SpatialWithinTolerancePercent => EvidenceTaggedCount > 0 ? (double)SpatialWithinToleranceCount / EvidenceTaggedCount * 100.0 : 0.0;

        public double KendallTauScore { get; set; }
        public double NormalizedSequencingScore { get; set; }
        public string SequencingInterpretation { get; set; }
    }

    /// <summary>
    /// Aggregates spatial accuracy, temporal milestones, and sequencing metrics from session telemetry.
    /// </summary>
    public static class ResearchMetrics
    {
        public const float SpatialToleranceThresholdMeters = 0.05f; // 5 cm

        public static SessionResearchSummary ComputeSummary(
            SessionData session,
            IEnumerable<TelemetryRecord> records,
            TimelineValidationResult timelineResult = null,
            int targetEvidenceCount = 8)
        {
            var summary = new SessionResearchSummary
            {
                ParticipantId = session?.ParticipantId ?? "Unknown",
                Condition = session?.Condition ?? "Default",
                SessionId = session?.SessionId ?? "Unknown",
                Status = session?.Status ?? SessionStatus.Completed,
                StartTimeUtc = session?.StartTimeUtc ?? string.Empty,
                EndTimeUtc = session?.EndTimeUtc ?? string.Empty,
                DurationSeconds = session?.DurationSeconds ?? 0f,
                TotalRegisteredEvidence = targetEvidenceCount
            };

            var discTimes = new List<double>();
            var spatialErrors = new List<float>();
            var seenDiscovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenTagged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (records != null)
            {
                foreach (var rec in records)
                {
                    if (rec == null) continue;

                    if (rec.EventType == TelemetryEventType.EvidenceDiscovered && !string.IsNullOrWhiteSpace(rec.EvidenceId))
                    {
                        if (seenDiscovered.Add(rec.EvidenceId))
                        {
                            discTimes.Add(rec.SessionTimestampSeconds);
                        }
                    }
                    else if (rec.EventType == TelemetryEventType.EvidenceTagged && !string.IsNullOrWhiteSpace(rec.EvidenceId))
                    {
                        seenTagged.Add(rec.EvidenceId);
                        if (rec.SpatialError.HasValue)
                        {
                            spatialErrors.Add(rec.SpatialError.Value);
                        }
                    }
                }
            }

            summary.EvidenceDiscoveredCount = seenDiscovered.Count;
            summary.EvidenceTaggedCount = seenTagged.Count;

            // Discovery timings
            if (discTimes.Count > 0)
            {
                discTimes.Sort();
                summary.TimeToFirstDiscoverySeconds = discTimes[0];

                if (discTimes.Count > 1)
                {
                    double totalSpan = discTimes[discTimes.Count - 1] - discTimes[0];
                    summary.AverageDiscoveryIntervalSeconds = totalSpan / (discTimes.Count - 1);
                }
                else
                {
                    summary.AverageDiscoveryIntervalSeconds = 0.0;
                }
            }

            // Spatial errors
            if (spatialErrors.Count > 0)
            {
                summary.MeanSpatialErrorMeters = spatialErrors.Average();
                summary.MinSpatialErrorMeters = spatialErrors.Min();
                summary.MaxSpatialErrorMeters = spatialErrors.Max();
                summary.SpatialWithinToleranceCount = spatialErrors.Count(e => e <= SpatialToleranceThresholdMeters);
            }

            // Sequencing metrics
            if (timelineResult != null)
            {
                summary.KendallTauScore = timelineResult.KendallTauScore;
                summary.NormalizedSequencingScore = timelineResult.KendallResult.NormalizedScore;
                summary.SequencingInterpretation = timelineResult.KendallResult.Interpretation;
            }
            else
            {
                summary.KendallTauScore = 0.0;
                summary.NormalizedSequencingScore = 0.5;
                summary.SequencingInterpretation = "Timeline not evaluated.";
            }

            return summary;
        }
    }
}
