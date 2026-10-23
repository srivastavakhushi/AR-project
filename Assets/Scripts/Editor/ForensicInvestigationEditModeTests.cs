using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

public class ForensicInvestigationEditModeTests
{
    GameObject root;

    [SetUp]
    public void SetUp()
    {
        root = new GameObject("Forensic Test");
    }

    [TearDown]
    public void TearDown()
    {
        if (root != null)
            Object.DestroyImmediate(root);
    }

    [Test]
    public void CsvEscape_QuotesCommasAndQuotes()
    {
        Assert.AreEqual("", InvestigationEventLogger.Escape(null));
        Assert.AreEqual("EV-001", InvestigationEventLogger.Escape("EV-001"));
        Assert.AreEqual("\"a,b\"", InvestigationEventLogger.Escape("a,b"));
        Assert.AreEqual("\"say \"\"hi\"\"\"", InvestigationEventLogger.Escape("say \"hi\""));
    }

    [Test]
    public void GroundTruth_UsesWorldPosition_AndStaysLocked()
    {
        var parent = new GameObject("Parent");
        parent.transform.SetParent(root.transform);
        parent.transform.position = new Vector3(5f, 3f, -2f);

        var evidenceObject = new GameObject("Casing");
        evidenceObject.transform.SetParent(parent.transform, false);
        evidenceObject.transform.localPosition = new Vector3(1f, 0.5f, -4f);
        var evidence = evidenceObject.AddComponent<EvidenceObject>();
        evidence.Configure("EV-101", "Ballistic casing", "Placeholder", EvidenceCategory.Ballistic);
        evidence.LockGroundTruthAtCurrentWorldPosition();

        Assert.AreEqual(6f, evidence.GroundTruthWorldPosition.x, 0.0001f);
        Assert.AreEqual(3.5f, evidence.GroundTruthWorldPosition.y, 0.0001f);
        Assert.AreEqual(-6f, evidence.GroundTruthWorldPosition.z, 0.0001f);
        evidenceObject.transform.position += new Vector3(2f, 0f, 0f);
        LogAssert.Expect(LogType.Warning, new Regex("ground truth is locked"));
        evidence.CaptureGroundTruthFromWorldPosition();

        Assert.AreEqual(6f, evidence.GroundTruthWorldPosition.x, 0.0001f);
        Assert.AreEqual(3.5f, evidence.GroundTruthWorldPosition.y, 0.0001f);
        Assert.AreEqual(-6f, evidence.GroundTruthWorldPosition.z, 0.0001f);
    }

    [Test]
    public void RegisterEvidence_RejectsInvalidAndDuplicateIds()
    {
        var manager = CreateManager();
        var invalid = CreateEvidence("nope", Vector3.zero);
        LogAssert.Expect(LogType.Error, new Regex("was not registered"));
        Assert.IsFalse(manager.RegisterEvidence(invalid));

        var first = CreateEvidence("EV-001", Vector3.zero);
        var duplicate = CreateEvidence("EV-001", Vector3.right);
        Assert.IsTrue(manager.RegisterEvidence(first));
        LogAssert.Expect(LogType.Error, new Regex("duplicate evidence ID"));
        Assert.IsFalse(manager.RegisterEvidence(duplicate));
        Assert.AreEqual(1, manager.RegisteredCount);
    }

    [Test]
    public void Tag_RecordsWorldSpaceError_Once()
    {
        var manager = CreateManager();
        var evidence = CreateEvidence("EV-001", new Vector3(1f, 2f, 3f));
        Assert.IsTrue(manager.RegisterEvidence(evidence));
        manager.StartSession();
        Assert.IsTrue(manager.TryInspect(evidence));
        Assert.IsTrue(evidence.IsDiscovered);

        Vector3 tagPosition = evidence.GroundTruthWorldPosition + new Vector3(0.03f, 0f, 0f);
        Assert.IsTrue(manager.TryTag(evidence, tagPosition));
        Assert.AreEqual(0.03f, evidence.SpatialErrorMetres, 0.0001f);
        Assert.AreEqual(Vector3.Distance(tagPosition, evidence.GroundTruthWorldPosition), manager.CalculateSpatialError(tagPosition, evidence.GroundTruthWorldPosition), 0.0001f);
        Assert.IsTrue(manager.IsWithinAccuracyTolerance(evidence.SpatialErrorMetres));
        Assert.AreEqual(1, root.GetComponentsInChildren<EvidenceTag>(true).Length);

        LogAssert.Expect(LogType.Warning, new Regex("already has a final tag"));
        Assert.IsFalse(manager.TryTag(evidence, tagPosition + Vector3.up));
        Assert.AreEqual(1, root.GetComponentsInChildren<EvidenceTag>(true).Length);
        Assert.AreEqual(0.03f, evidence.SpatialErrorMetres, 0.0001f);

        manager.CompleteSession();
        string csv = File.ReadAllText(manager.LogFilePath);
        Assert.That(csv, Does.StartWith(InvestigationEventLogger.Header));
        Assert.AreEqual(1, CountLinesContaining(csv, "EVIDENCE_TAGGED"));
        Assert.That(csv, Does.Contain("SESSION_STARTED"));
        Assert.That(csv, Does.Contain("EVIDENCE_INSPECTED"));
        Assert.That(csv, Does.Contain("EVIDENCE_DISCOVERED"));
        Assert.That(csv, Does.Contain("SESSION_COMPLETED"));

        string tagLine = FindLineContaining(csv, "EVIDENCE_TAGGED");
        string[] fields = tagLine.Split(',');
        Assert.AreEqual(11, fields.Length);
        Assert.AreEqual("EV-001", fields[3]);
        Assert.AreEqual(tagPosition.x, float.Parse(fields[4], CultureInfo.InvariantCulture), 0.0001f);
        Assert.AreEqual(evidence.GroundTruthWorldPosition.x, float.Parse(fields[7], CultureInfo.InvariantCulture), 0.0001f);
        Assert.AreEqual(0.03f, float.Parse(fields[10], CultureInfo.InvariantCulture), 0.0001f);
    }

    [Test]
    public void EightEvidenceItems_RegisterAndReportPositionsOutsideTheFloor()
    {
        var manager = CreateManager();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor Reference";
        floor.transform.SetParent(root.transform);
        floor.transform.position = new Vector3(0f, 2f, 0f);
        floor.transform.localScale = new Vector3(4f, 0.2f, 4f);
        manager.AssignInvestigationFloor(floor.transform);

        for (int i = 1; i <= 8; i++)
        {
            Vector3 position = new Vector3(-1.2f + (i - 1) * 0.3f, 2.05f, 0.2f);
            Assert.IsTrue(manager.RegisterEvidence(CreateEvidence("EV-00" + i, position)));
        }

        Assert.AreEqual(8, manager.RegisteredCount);
        var outside = CreateEvidence("EV-009", new Vector3(0f, 8f, 0f));
        LogAssert.Expect(LogType.Error, new Regex("outside the selected investigation floor"));
        Assert.IsTrue(manager.RegisterEvidence(outside));
        Assert.AreEqual(9, manager.RegisteredCount);
    }

    [Test]
    public void MissingCamera_IsReported()
    {
        var cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var managers = Object.FindObjectsByType<CrimeSceneEvidenceManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var cameraStates = new bool[cameras.Length];
        var managerStates = new bool[managers.Length];
        for (int i = 0; i < cameras.Length; i++)
        {
            cameraStates[i] = cameras[i].enabled;
            cameras[i].enabled = false;
        }

        for (int i = 0; i < managers.Length; i++)
        {
            managerStates[i] = managers[i].enabled;
            managers[i].enabled = false;
        }

        var interactionObject = new GameObject("Interaction");
        interactionObject.transform.SetParent(root.transform);
        var interaction = interactionObject.AddComponent<EvidenceInteraction>();
        LogAssert.Expect(LogType.Error, new Regex("no camera"));
        LogAssert.Expect(LogType.Error, new Regex("no CrimeSceneEvidenceManager"));
        try
        {
            Assert.IsFalse(interaction.ValidateConfiguration());
        }
        finally
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null)
                    cameras[i].enabled = cameraStates[i];
            }

            for (int i = 0; i < managers.Length; i++)
            {
                if (managers[i] != null)
                    managers[i].enabled = managerStates[i];
            }
        }
    }

    CrimeSceneEvidenceManager CreateManager()
    {
        var manager = root.AddComponent<CrimeSceneEvidenceManager>();
        var logger = root.AddComponent<InvestigationEventLogger>();
        manager.ConfigureLogger(logger);
        var markers = new GameObject("Markers");
        markers.transform.SetParent(root.transform);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
        Material material = shader != null ? new Material(shader) : null;
        manager.SetMarkerPresentation(markers.transform, null, material);
        return manager;
    }

    EvidenceObject CreateEvidence(string id, Vector3 worldPosition)
    {
        var evidenceObject = new GameObject(id);
        evidenceObject.transform.SetParent(root.transform);
        evidenceObject.transform.position = worldPosition;
        var evidence = evidenceObject.AddComponent<EvidenceObject>();
        evidence.Configure(id, id, "Placeholder", EvidenceCategory.Unspecified);
        return evidence;
    }

    static int CountLinesContaining(string csv, string value)
    {
        int count = 0;
        var reader = new StringReader(csv);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains(value))
                count++;
        }
        return count;
    }

    static string FindLineContaining(string csv, string value)
    {
        var reader = new StringReader(csv);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains(value))
                return line;
        }
        return "";
    }
}
