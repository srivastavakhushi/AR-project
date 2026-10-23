using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CrimeSceneVR.Session;
using CrimeSceneVR.Telemetry;

namespace CrimeSceneVR.Analytics
{
    /// <summary>
    /// Aggregated research analytics, trajectory distance, and spatial error metrics for a completed session.
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
        public int MissedEvidenceCount => Math.Max(0, TotalRegisteredEvidence - EvidenceTaggedCount);
        public double CompletionPercent => TotalRegisteredEvidence > 0 ? (double)EvidenceTaggedCount / TotalRegisteredEvidence * 100.0 : 0.0;
        public double EvidenceRecallPercent => CompletionPercent;

        public double? TimeToFirstDiscoverySeconds { get; set; }
        public double? AverageDiscoveryIntervalSeconds { get; set; }

        public double? MeanSpatialErrorMeters { get; set; }
        public double? MedianSpatialErrorMeters { get; set; }
        public double? MinSpatialErrorMeters { get; set; }
        public double? MaxSpatialErrorMeters { get; set; }

        public int SpatialWithinToleranceCount { get; set; }
        public double SpatialWithinTolerancePercent => EvidenceTaggedCount > 0 ? (double)SpatialWithinToleranceCount / EvidenceTaggedCount * 100.0 : 0.0;

        public double TotalMovementDistanceMeters { get; set; }

        public double KendallTauScore { get; set; }
        public double NormalizedSequencingScore { get; set; }
        public int ConcordantPairs { get; set; }
        public int DiscordantPairs { get; set; }
        public string TaggedTimelineSequence { get; set; } = string.Empty;
        public string SequencingInterpretation { get; set; }
    }

    /// <summary>
    /// Aggregates spatial accuracy, temporal milestones, trajectory path length, and Kendall's Tau-b sequencing metrics.
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
            var taggedOrder = new List<string>();

            double totalDistance = 0.0;
            Vector3? prevPose = null;

            if (records != null)
            {
                foreach (var rec in records)
                {
                    if (rec == null) continue;

                    if (rec.EventType == TelemetryEventType.PeriodicSample)
                    {
                        Vector3 currentPose = rec.HeadPosition;
                        if (prevPose.HasValue)
                        {
                            float step = Vector2.Distance(
                                new Vector2(currentPose.x, currentPose.z),
                                new Vector2(prevPose.Value.x, prevPose.Value.z));
                            if (step > 0.003f && step < 10f)
                            {
                                totalDistance += step;
                            }
                        }
                        prevPose = currentPose;
                    }
                    else if (rec.EventType == TelemetryEventType.EvidenceDiscovered && !string.IsNullOrWhiteSpace(rec.EvidenceId))
                    {
                        if (seenDiscovered.Add(rec.EvidenceId))
                        {
                            discTimes.Add(rec.SessionTimestampSeconds);
                        }
                    }
                    else if (rec.EventType == TelemetryEventType.EvidenceTagged && !string.IsNullOrWhiteSpace(rec.EvidenceId))
                    {
                        if (seenTagged.Add(rec.EvidenceId))
                        {
                            taggedOrder.Add(rec.EvidenceId);
                        }
                        if (rec.SpatialError.HasValue)
                        {
                            spatialErrors.Add(rec.SpatialError.Value);
                        }
                    }
                }
            }

            summary.EvidenceDiscoveredCount = seenDiscovered.Count;
            summary.EvidenceTaggedCount = seenTagged.Count;
            summary.TotalMovementDistanceMeters = totalDistance;
            summary.TaggedTimelineSequence = string.Join("->", taggedOrder);

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

            // Spatial errors (Mean, Median, Min, Max, <= 5 cm tolerance)
            if (spatialErrors.Count > 0)
            {
                spatialErrors.Sort();
                summary.MeanSpatialErrorMeters = spatialErrors.Average();
                summary.MinSpatialErrorMeters = spatialErrors[0];
                summary.MaxSpatialErrorMeters = spatialErrors[spatialErrors.Count - 1];

                int count = spatialErrors.Count;
                if (count % 2 == 1)
                {
                    summary.MedianSpatialErrorMeters = spatialErrors[count / 2];
                }
                else
                {
                    summary.MedianSpatialErrorMeters = (spatialErrors[(count / 2) - 1] + spatialErrors[count / 2]) * 0.5;
                }

                summary.SpatialWithinToleranceCount = spatialErrors.Count(e => e <= SpatialToleranceThresholdMeters);
            }

            // Sequencing metrics
            if (timelineResult != null)
            {
                summary.KendallTauScore = timelineResult.KendallTauScore;
                summary.NormalizedSequencingScore = timelineResult.KendallResult.NormalizedScore;
                summary.ConcordantPairs = timelineResult.KendallResult.ConcordantPairs;
                summary.DiscordantPairs = timelineResult.KendallResult.DiscordantPairs;
                summary.SequencingInterpretation = timelineResult.KendallResult.Interpretation;
            }
            else
            {
                summary.KendallTauScore = 0.0;
                summary.NormalizedSequencingScore = 0.5;
                summary.ConcordantPairs = 0;
                summary.DiscordantPairs = 0;
                summary.SequencingInterpretation = "Timeline not evaluated.";
            }

            return summary;
        }
    }
}
