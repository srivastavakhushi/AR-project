using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using CrimeSceneVR.Analytics;
using CrimeSceneVR.Session;
using CrimeSceneVR.Telemetry;

namespace CrimeSceneVR.Export
{
    /// <summary>
    /// Exports research CSV files (continuous 10 Hz tracking, event log, and session summary)
    /// to Application.persistentDataPath/ResearchData with RFC 4180 escaping and InvariantCulture formatting.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Crime Scene VR/Export/Research Data Exporter")]
    public class ResearchDataExporter : MonoBehaviour
    {
        private static ResearchDataExporter _instance;
        public static ResearchDataExporter Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindAnyObjectByType<ResearchDataExporter>();
                }
                return _instance;
            }
            private set => _instance = value;
        }

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public const string TRACKING_HEADER =
            "ParticipantId,Condition,SessionId,SessionTimestampSeconds,IsoTimestamp,EventType," +
            "RigPosX,RigPosY,RigPosZ," +
            "HeadPosX,HeadPosY,HeadPosZ,HeadRotX,HeadRotY,HeadRotZ,HeadRotW," +
            "LeftHandPosX,LeftHandPosY,LeftHandPosZ,LeftHandRotX,LeftHandRotY,LeftHandRotZ,LeftHandRotW," +
            "RightHandPosX,RightHandPosY,RightHandPosZ,RightHandRotX,RightHandRotY,RightHandRotZ,RightHandRotW," +
            "VelocityX,VelocityY,VelocityZ," +
            "EvidenceId,EvidencePosX,EvidencePosY,EvidencePosZ,SpatialError,Details";

        public const string SUMMARY_HEADER =
            "SessionId,ParticipantId,Condition,Status,StartTimeUtc,EndTimeUtc,DurationSeconds," +
            "TotalRegisteredEvidence,DiscoveredCount,TaggedCount,CompletionPercent," +
            "TimeToFirstDiscoverySeconds,AverageDiscoveryIntervalSeconds," +
            "MeanSpatialErrorMeters,MedianSpatialErrorMeters,MinSpatialErrorMeters,MaxSpatialErrorMeters," +
            "SpatialWithinToleranceCount,SpatialWithinTolerancePercent,TotalMovementDistanceMeters," +
            "KendallTauScore,NormalizedSequencingScore,ConcordantPairs,DiscordantPairs,TaggedSequence,Interpretation";

        [Header("Export Settings")]
        [Tooltip("Subdirectory under Application.persistentDataPath for research outputs.")]
        [SerializeField] private string _subDirectory = "ResearchData";

        [Tooltip("If true, automatically exports all research CSVs upon SessionManager.OnSessionEnded.")]
        [SerializeField] private bool _autoExportOnSessionEnd = true;

        public string OutputDirectory => Path.Combine(Application.persistentDataPath, _subDirectory);
        public SessionResearchSummary LastExportedSummary { get; private set; }
        public string LastTrackingCsvPath { get; private set; }
        public string LastSummaryCsvPath { get; private set; }

        public event Action<SessionResearchSummary, string, string> OnDatasetsExported;

        private void Awake()
        {
            if (_instance == null || _instance == this)
            {
                _instance = this;
            }
        }

        private void OnEnable()
        {
            if (_instance == null)
                _instance = this;

            if (SessionManager.Instance != null)
            {
                SessionManager.Instance.OnSessionEnded -= HandleSessionEnded;
                SessionManager.Instance.OnSessionEnded += HandleSessionEnded;
            }
        }

        private void Start()
        {
            if (SessionManager.Instance != null)
            {
                SessionManager.Instance.OnSessionEnded -= HandleSessionEnded;
                SessionManager.Instance.OnSessionEnded += HandleSessionEnded;
            }
        }

        private void OnDisable()
        {
            if (SessionManager.Instance != null)
            {
                SessionManager.Instance.OnSessionEnded -= HandleSessionEnded;
            }
        }

        private void HandleSessionEnded(SessionData session)
        {
            if (!_autoExportOnSessionEnd || session == null) return;

            try
            {
                var records = TelemetryCollector.Instance != null ? TelemetryCollector.Instance.Records : null;
                var timelineResult = TimelineManager.Instance != null ? TimelineManager.Instance.EvaluateCurrentTaggedSequence() : null;
                var evMgr = FindAnyObjectByType<CrimeSceneEvidenceManager>();
                int targetCount = (evMgr != null && evMgr.RegisteredCount > 0) ? evMgr.RegisteredCount : 8;

                var summary = ResearchMetrics.ComputeSummary(session, records, timelineResult, targetCount);

                string trackingPath = ExportTrackingData(session, records);
                string summaryPath = ExportSummary(summary);

                LastExportedSummary = summary;
                LastTrackingCsvPath = trackingPath;
                LastSummaryCsvPath = summaryPath;

                Debug.Log($"[ResearchDataExporter] Successfully exported research datasets for session '{session.SessionId}':\n• Tracking: {trackingPath}\n• Summary: {summaryPath}");
                OnDatasetsExported?.Invoke(summary, trackingPath, summaryPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ResearchDataExporter] Failed to export session datasets: {ex.Message}");
            }
        }

        public string ExportTrackingData(SessionData session, IEnumerable<TelemetryRecord> records, string targetDir = null)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));

            string folder = string.IsNullOrWhiteSpace(targetDir) ? OutputDirectory : targetDir;
            Directory.CreateDirectory(folder);

            string fileName = $"tracking_{SanitizeFileName(session.SessionId)}.csv";
            string fullPath = Path.Combine(folder, fileName);

            var sb = new StringBuilder(64 * 1024);
            sb.AppendLine(TRACKING_HEADER);

            string pId = EscapeCsv(session.ParticipantId);
            string cond = EscapeCsv(session.Condition);
            string sId = EscapeCsv(session.SessionId);

            if (records != null)
            {
                foreach (var rec in records)
                {
                    if (rec == null) continue;

                    sb.Append(pId).Append(',');
                    sb.Append(cond).Append(',');
                    sb.Append(sId).Append(',');
                    sb.Append(rec.SessionTimestampSeconds.ToString("F3", Inv)).Append(',');
                    sb.Append(EscapeCsv(rec.IsoTimestamp)).Append(',');
                    sb.Append(EscapeCsv(rec.CustomEventName ?? rec.EventType.ToString())).Append(',');

                    AppendVector3(sb, rec.RigPosition);
                    AppendVector3(sb, rec.HeadPosition);
                    AppendQuaternion(sb, rec.HeadRotation);
                    AppendVector3(sb, rec.LeftHandPosition);
                    AppendQuaternion(sb, rec.LeftHandRotation);
                    AppendVector3(sb, rec.RightHandPosition);
                    AppendQuaternion(sb, rec.RightHandRotation);
                    AppendVector3(sb, rec.Velocity);

                    sb.Append(EscapeCsv(rec.EvidenceId)).Append(',');
                    AppendVector3(sb, rec.EvidencePosition);
                    sb.Append(rec.SpatialError.HasValue ? rec.SpatialError.Value.ToString("F4", Inv) : string.Empty).Append(',');
                    sb.Append(EscapeCsv(rec.Details));
                    sb.AppendLine();
                }
            }

            File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(false));
            return fullPath;
        }

        public string ExportSummary(SessionResearchSummary summary, string targetDir = null)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));

            string folder = string.IsNullOrWhiteSpace(targetDir) ? OutputDirectory : targetDir;
            Directory.CreateDirectory(folder);

            string fileName = $"summary_{SanitizeFileName(summary.SessionId)}.csv";
            string fullPath = Path.Combine(folder, fileName);

            var sb = new StringBuilder(2048);
            sb.AppendLine(SUMMARY_HEADER);

            sb.Append(EscapeCsv(summary.SessionId)).Append(',');
            sb.Append(EscapeCsv(summary.ParticipantId)).Append(',');
            sb.Append(EscapeCsv(summary.Condition)).Append(',');
            sb.Append(summary.Status.ToString()).Append(',');
            sb.Append(EscapeCsv(summary.StartTimeUtc)).Append(',');
            sb.Append(EscapeCsv(summary.EndTimeUtc)).Append(',');
            sb.Append(summary.DurationSeconds.ToString("F2", Inv)).Append(',');
            sb.Append(summary.TotalRegisteredEvidence.ToString(Inv)).Append(',');
            sb.Append(summary.EvidenceDiscoveredCount.ToString(Inv)).Append(',');
            sb.Append(summary.EvidenceTaggedCount.ToString(Inv)).Append(',');
            sb.Append(summary.CompletionPercent.ToString("F1", Inv)).Append(',');
            sb.Append(summary.TimeToFirstDiscoverySeconds.HasValue ? summary.TimeToFirstDiscoverySeconds.Value.ToString("F2", Inv) : "").Append(',');
            sb.Append(summary.AverageDiscoveryIntervalSeconds.HasValue ? summary.AverageDiscoveryIntervalSeconds.Value.ToString("F2", Inv) : "").Append(',');
            sb.Append(summary.MeanSpatialErrorMeters.HasValue ? summary.MeanSpatialErrorMeters.Value.ToString("F4", Inv) : "").Append(',');
            sb.Append(summary.MedianSpatialErrorMeters.HasValue ? summary.MedianSpatialErrorMeters.Value.ToString("F4", Inv) : "").Append(',');
            sb.Append(summary.MinSpatialErrorMeters.HasValue ? summary.MinSpatialErrorMeters.Value.ToString("F4", Inv) : "").Append(',');
            sb.Append(summary.MaxSpatialErrorMeters.HasValue ? summary.MaxSpatialErrorMeters.Value.ToString("F4", Inv) : "").Append(',');
            sb.Append(summary.SpatialWithinToleranceCount.ToString(Inv)).Append(',');
            sb.Append(summary.SpatialWithinTolerancePercent.ToString("F1", Inv)).Append(',');
            sb.Append(summary.TotalMovementDistanceMeters.ToString("F2", Inv)).Append(',');
            sb.Append(summary.KendallTauScore.ToString("F3", Inv)).Append(',');
            sb.Append(summary.NormalizedSequencingScore.ToString("F3", Inv)).Append(',');
            sb.Append(summary.ConcordantPairs.ToString(Inv)).Append(',');
            sb.Append(summary.DiscordantPairs.ToString(Inv)).Append(',');
            sb.Append(EscapeCsv(summary.TaggedTimelineSequence)).Append(',');
            sb.Append(EscapeCsv(summary.SequencingInterpretation));
            sb.AppendLine();

            File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(false));
            return fullPath;
        }

        public static string EscapeCsv(string field)
        {
            if (string.IsNullOrEmpty(field)) return string.Empty;

            bool containsSpecial = field.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            if (containsSpecial)
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }

        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";
            char[] invalidChars = Path.GetInvalidFileNameChars();
            var clean = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                clean.Append(Array.IndexOf(invalidChars, c) >= 0 ? '_' : c);
            }
            return clean.ToString();
        }

        private static void AppendVector3(StringBuilder sb, Vector3? v)
        {
            if (v.HasValue)
            {
                sb.Append(v.Value.x.ToString("F4", Inv)).Append(',');
                sb.Append(v.Value.y.ToString("F4", Inv)).Append(',');
                sb.Append(v.Value.z.ToString("F4", Inv)).Append(',');
            }
            else
            {
                sb.Append(",,,");
            }
        }

        private static void AppendQuaternion(StringBuilder sb, Quaternion? q)
        {
            if (q.HasValue)
            {
                sb.Append(q.Value.x.ToString("F4", Inv)).Append(',');
                sb.Append(q.Value.y.ToString("F4", Inv)).Append(',');
                sb.Append(q.Value.z.ToString("F4", Inv)).Append(',');
                sb.Append(q.Value.w.ToString("F4", Inv)).Append(',');
            }
            else
            {
                sb.Append(",,,,");
            }
        }
    }
}
