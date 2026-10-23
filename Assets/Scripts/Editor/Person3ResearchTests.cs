using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using CrimeSceneVR.Analytics;
using CrimeSceneVR.Export;
using CrimeSceneVR.Session;
using CrimeSceneVR.Telemetry;

namespace CrimeSceneVR.Tests
{
    [TestFixture]
    public class Person3ResearchTests
    {
        private GameObject _sessionGo;
        private GameObject _telemetryGo;
        private GameObject _timelineGo;
        private string _testExportDir;

        [SetUp]
        public void SetUp()
        {
            _sessionGo = new GameObject("Test_SessionManager");
            _telemetryGo = new GameObject("Test_TelemetryCollector");
            _timelineGo = new GameObject("Test_TimelineManager");

            _testExportDir = Path.Combine(Application.temporaryCachePath, "ResearchTestExports_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testExportDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_sessionGo != null)
                UnityEngine.Object.DestroyImmediate(_sessionGo);
            if (_telemetryGo != null)
                UnityEngine.Object.DestroyImmediate(_telemetryGo);
            if (_timelineGo != null)
                UnityEngine.Object.DestroyImmediate(_timelineGo);

            if (Directory.Exists(_testExportDir))
            {
                try { Directory.Delete(_testExportDir, true); } catch { }
            }
        }

        [Test]
        public void SessionManager_SessionLifecycle_GeneratesUniqueSessionIdAndTiming()
        {
            var sm = _sessionGo.AddComponent<SessionManager>();

            var session1 = sm.StartSession("P101", "VR_Standard");
            Assert.IsTrue(sm.IsSessionActive);
            Assert.AreEqual("P101", session1.ParticipantId);
            Assert.AreEqual("VR_Standard", session1.Condition);
            Assert.IsNotEmpty(session1.SessionId);
            Assert.AreEqual(SessionStatus.InProgress, session1.Status);

            var endedSession = sm.EndSession();
            Assert.IsFalse(sm.IsSessionActive);
            Assert.AreEqual(SessionStatus.Completed, endedSession.Status);
            Assert.IsNotEmpty(endedSession.EndTimeUtc);

            var session2 = sm.StartSession("P101", "VR_Standard");
            Assert.AreNotEqual(session1.SessionId, session2.SessionId, "Each session must have a unique identifier.");
            sm.EndSession();
        }

        [Test]
        public void TelemetryCollector_PeriodicSampling_Captures10HzPosesAndVelocities()
        {
            var tc = _telemetryGo.AddComponent<TelemetryCollector>();
            Assert.AreEqual(10f, tc.SamplingRateHz, 0.01f, "Default sampling rate must be 10 Hz.");
            Assert.AreEqual(0.1f, tc.SamplingInterval, 0.001f, "10 Hz implies 0.1s interval.");

            var sample = tc.CapturePeriodicSample();

            Assert.IsNotNull(sample);
            Assert.AreEqual(TelemetryEventType.PeriodicSample, sample.EventType);
            Assert.AreEqual(1, tc.RecordCount);
        }

        [Test]
        public void TelemetryCollector_DiscreteEvents_RecordsTaggedAndDiscoveredWithAccuracy()
        {
            var tc = _telemetryGo.AddComponent<TelemetryCollector>();

            var recDisc = tc.RecordEvent(TelemetryEventType.EvidenceDiscovered, "EVIDENCE_DISCOVERED", "EV-001", new Vector3(1f, 0f, 2f));
            var recTag = tc.RecordEvent(TelemetryEventType.EvidenceTagged, "EVIDENCE_TAGGED", "EV-001", new Vector3(1.02f, 0f, 2f), spatialError: 0.02f, details: "Ballistic casing");

            Assert.AreEqual(2, tc.RecordCount);
            Assert.AreEqual("EV-001", recDisc.EvidenceId);
            Assert.AreEqual(0.02f, recTag.SpatialError.Value, 0.001f);
            Assert.AreEqual("Ballistic casing", recTag.Details);
        }

        [Test]
        public void KendallTau_PerfectAgreement_ReturnsOne()
        {
            var gt = new List<string> { "EV-001", "EV-002", "EV-003", "EV-004", "EV-005" };
            var sub = new List<string> { "EV-001", "EV-002", "EV-003", "EV-004", "EV-005" };

            var result = KendallTau.Calculate(gt, sub);

            Assert.AreEqual(1.0, result.Tau, 0.001, "Identical sequence must yield Tau = 1.0.");
            Assert.AreEqual(1.0, result.NormalizedScore, 0.001);
            Assert.AreEqual(0, result.DiscordantPairs);
        }

        [Test]
        public void KendallTau_PerfectInversion_ReturnsMinusOne()
        {
            var gt = new List<string> { "EV-001", "EV-002", "EV-003", "EV-004" };
            var sub = new List<string> { "EV-004", "EV-003", "EV-002", "EV-001" };

            var result = KendallTau.Calculate(gt, sub);

            Assert.AreEqual(-1.0, result.Tau, 0.001, "Completely inverted sequence must yield Tau = -1.0.");
            Assert.AreEqual(0.0, result.NormalizedScore, 0.001);
            Assert.AreEqual(0, result.ConcordantPairs);
        }

        [Test]
        public void KendallTau_TiedRanks_CalculatesTauBCorrectly()
        {
            var gt = new List<string> { "EV-001", "EV-002", "EV-003", "EV-004" };
            // Participant submits duplicate EV-002: sequence with ties
            var sub = new List<string> { "EV-001", "EV-002", "EV-002", "EV-004" };

            var result = KendallTau.Calculate(gt, sub, penalizeOmissions: true);

            Assert.IsFalse(double.IsNaN(result.Tau), "Tau-b with ties must not be NaN.");
            Assert.IsTrue(result.TiesParticipant > 0, "Ties in participant sequence must be registered.");
            Assert.IsTrue(result.Tau > 0.0, "Partially matching sequence with tie must yield positive Tau.");
        }

        [Test]
        public void KendallTau_Omissions_AppliesPenaltiesCorrectly()
        {
            var gt = new List<string> { "EV-001", "EV-002", "EV-003", "EV-004" };
            var sub = new List<string> { "EV-001", "EV-002" }; // Omits EV-003 and EV-004

            var resultPenalized = KendallTau.Calculate(gt, sub, penalizeOmissions: true);
            var resultUnpenalized = KendallTau.Calculate(gt, sub, penalizeOmissions: false);

            Assert.IsTrue(resultPenalized.Tau < resultUnpenalized.Tau, "Penalized Tau must be strictly less than unpenalized Tau when omissions occur.");
        }

        [Test]
        public void KendallTau_EmptyAndNullInputs_HandlesEdgeCasesSafely()
        {
            var gt = new List<string> { "EV-001", "EV-002", "EV-003" };

            var nullSubResult = KendallTau.Calculate(gt, null);
            var emptySubResult = KendallTau.Calculate(gt, new List<string>());
            var nullGtResult = KendallTau.Calculate(null, gt);
            var emptyGtResult = KendallTau.Calculate(new List<string>(), gt);

            Assert.IsFalse(double.IsNaN(nullSubResult.Tau));
            Assert.IsFalse(double.IsNaN(emptySubResult.Tau));
            Assert.IsFalse(double.IsNaN(nullGtResult.Tau));
            Assert.IsFalse(double.IsNaN(emptyGtResult.Tau));
        }

        [Test]
        public void TimelineManager_Sequencing_IdentifiesOmissionsDuplicatesAndTau()
        {
            var tm = _timelineGo.AddComponent<TimelineManager>();
            tm.SetGroundTruthSequence(new[] { "EV-001", "EV-002", "EV-003", "EV-004" });

            // Submission has duplicate EV-001, omits EV-004, adds unexpected EV-099
            var sub = new List<string> { "EV-001", "EV-001", "EV-099", "EV-003" };
            var result = tm.SubmitTimeline(sub);

            Assert.IsFalse(result.IsComplete);
            Assert.IsTrue(result.OmittedEvidenceIds.Contains("EV-004"));
            Assert.IsTrue(result.DuplicateEvidenceIds.Contains("EV-001"));
            Assert.IsTrue(result.UnexpectedEvidenceIds.Contains("EV-099"));
            Assert.AreEqual(1, result.ExactMatchCount); // EV-001 at index 0
        }

        [Test]
        public void ResearchMetrics_AggregatesSpatialAccuracyAndToleranceRate()
        {
            var session = new SessionData("P001", "VR_Standard");
            session.MarkComplete(90.0f);

            var records = new List<TelemetryRecord>
            {
                new TelemetryRecord(10.0, TelemetryEventType.EvidenceDiscovered, evidenceId: "EV-001"),
                new TelemetryRecord(20.0, TelemetryEventType.EvidenceTagged, evidenceId: "EV-001", spatialError: 0.03f), // <= 5cm
                new TelemetryRecord(30.0, TelemetryEventType.EvidenceDiscovered, evidenceId: "EV-002"),
                new TelemetryRecord(40.0, TelemetryEventType.EvidenceTagged, evidenceId: "EV-002", spatialError: 0.07f)  // > 5cm
            };

            var summary = ResearchMetrics.ComputeSummary(session, records, targetEvidenceCount: 8);

            Assert.AreEqual(2, summary.EvidenceDiscoveredCount);
            Assert.AreEqual(2, summary.EvidenceTaggedCount);
            Assert.AreEqual(25.0, summary.CompletionPercent, 0.1); // 2 / 8 = 25%
            Assert.AreEqual(0.05f, summary.MeanSpatialErrorMeters.Value, 0.001f); // (0.03 + 0.07)/2 = 0.05m
            Assert.AreEqual(1, summary.SpatialWithinToleranceCount, "1 of 2 tags was within 5cm.");
            Assert.AreEqual(50.0, summary.SpatialWithinTolerancePercent, 0.1);
        }

        [Test]
        public void ResearchDataExporter_CsvEscaping_HandlesQuotesCommasAndNewlines()
        {
            Assert.AreEqual("", ResearchDataExporter.EscapeCsv(null));
            Assert.AreEqual("NormalText", ResearchDataExporter.EscapeCsv("NormalText"));
            Assert.AreEqual("\"Hello, World\"", ResearchDataExporter.EscapeCsv("Hello, World"));
            Assert.AreEqual("\"Quoted \"\"Text\"\"\"", ResearchDataExporter.EscapeCsv("Quoted \"Text\""));
            Assert.AreEqual("\"Multi\nLine\"", ResearchDataExporter.EscapeCsv("Multi\nLine"));
        }

        [Test]
        public void ResearchDataExporter_ExportFiles_CreatesValidTrackingAndSummaryCsvs()
        {
            var exporter = _sessionGo.AddComponent<ResearchDataExporter>();
            var session = new SessionData("P001", "VR_Standard");
            session.MarkComplete(60.0f);

            var records = new List<TelemetryRecord>
            {
                new TelemetryRecord(0.0, TelemetryEventType.SessionStart),
                new TelemetryRecord(0.1, TelemetryEventType.PeriodicSample)
                {
                    HeadPosition = new Vector3(1f, 1.7f, -2f),
                    HeadRotation = Quaternion.identity
                },
                new TelemetryRecord(15.0, TelemetryEventType.EvidenceTagged, evidenceId: "EV-001", spatialError: 0.02f)
            };

            var summary = ResearchMetrics.ComputeSummary(session, records, targetEvidenceCount: 8);

            string trackingPath = exporter.ExportTrackingData(session, records, _testExportDir);
            string summaryPath = exporter.ExportSummary(summary, _testExportDir);

            Assert.IsTrue(File.Exists(trackingPath), "Tracking CSV file must exist.");
            Assert.IsTrue(File.Exists(summaryPath), "Summary CSV file must exist.");

            string trackingContent = File.ReadAllText(trackingPath);
            Assert.IsTrue(trackingContent.Contains("ParticipantId,Condition,SessionId"));
            Assert.IsTrue(trackingContent.Contains("EV-001"));

            string summaryContent = File.ReadAllText(summaryPath);
            Assert.IsTrue(summaryContent.Contains("SessionId,ParticipantId,Condition"));
            Assert.IsTrue(summaryContent.Contains("P001"));
        }
    }
}
