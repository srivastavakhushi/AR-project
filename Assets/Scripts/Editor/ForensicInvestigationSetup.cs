using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ForensicInvestigationSetup
{
    const string ScenePath = "Assets/Scenes/CrimeScene.unity";
    const string MaterialFolder = "Assets/Materials/Evidence";
    const string PrefabFolder = "Assets/Prefabs/Evidence";
    const float SingleFloorMaxHeight = 4.5f;

    struct EvidenceSpec
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public EvidenceCategory Category;
        public PrimitiveType Primitive;
        public Vector3 Scale;
        public Color Color;
        public float FloorLift;
        public bool Cluster;
    }

    [MenuItem("Forensic/Setup Investigation Systems")]
    public static void SetupSystemsMenu()
    {
        SetupSystems();
        SaveOpenScene();
        Debug.Log("Forensic: investigation systems are in the open scene. Select one room and run Forensic/Place Eight Evidence Items On Selected Floor.");
    }

    [MenuItem("Forensic/Place Eight Evidence Items On Selected Floor")]
    public static void PlaceOnSelectionMenu()
    {
        Transform selection = Selection.activeTransform;
        if (selection == null)
        {
            Notify("Select one floor", "Select one room in the Hierarchy, then run this command again. Evidence was not placed and the building was not modified.");
            return;
        }

        if (PlaceOnFloor(selection))
        {
            SaveOpenScene();
            Validate(out string report);
            Debug.Log(report);
        }
    }

    [MenuItem("Forensic/Validate Investigation Setup")]
    public static void ValidateMenu()
    {
        bool valid = Validate(out string report);
        Debug.Log(report);
        Notify(valid ? "Investigation setup is valid" : "Investigation setup needs attention", report);
    }

    public static void ConfigureCrimeSceneFromBatch()
    {
        if (!System.IO.File.Exists(ScenePath))
        {
            Debug.LogError("Forensic: " + ScenePath + " was not found. The scene was not modified.");
            return;
        }

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        List<string> originalRoots = CaptureRootNames();
        SetupSystems();

        Transform floor = FindLowestFurnishedFloor();
        bool placed = false;
        if (floor == null)
        {
            Debug.LogError("Forensic: no single furnished floor could be measured from renderer bounds. Evidence was not placed. Select one room and run Forensic/Place Eight Evidence Items On Selected Floor. The building was not modified.");
        }
        else
        {
            Debug.Log("Forensic: initial floor '" + floor.name + "' was chosen from measured renderer bounds. Re-run Forensic/Place Eight Evidence Items On Selected Floor to choose a different room.");
            placed = PlaceOnFloor(floor);
        }

        bool rootsIntact = OriginalRootsRemain(originalRoots);
        bool valid = Validate(out string report);
        Debug.Log(report);
        SaveOpenScene();
        Debug.Log("FORENSIC_SETUP_RESULT placed=" + (placed ? "yes" : "no") + " valid=" + (valid ? "yes" : "no") + " rootsIntact=" + (rootsIntact ? "yes" : "no"));
    }

    static void SetupSystems()
    {
        Shader shader = FindUrpLitShader();
        if (shader == null)
        {
            Debug.LogError("Forensic: Universal Render Pipeline/Lit was not found. Investigation materials were not created.");
            return;
        }

        CrimeSceneEvidenceManager manager = Object.FindAnyObjectByType<CrimeSceneEvidenceManager>();
        GameObject root;
        if (manager == null)
        {
            root = new GameObject("Investigation");
            Undo.RegisterCreatedObjectUndo(root, "Create investigation");
            manager = root.AddComponent<CrimeSceneEvidenceManager>();
        }
        else
        {
            root = manager.gameObject;
        }

        InvestigationEventLogger logger = root.GetComponent<InvestigationEventLogger>();
        if (logger == null)
            logger = root.AddComponent<InvestigationEventLogger>();
        manager.ConfigureLogger(logger);

        EvidenceUI ui = root.GetComponent<EvidenceUI>();
        if (ui == null)
            ui = root.AddComponent<EvidenceUI>();

        Transform evidenceRoot = GetOrCreateChild(root.transform, "InvestigationEvidence");
        Transform markersRoot = GetOrCreateChild(root.transform, "InvestigationMarkers");

        Material markerMaterial = GetOrCreateMaterial("Marker", new Color(0.95f, 0.72f, 0.18f, 1f), shader, emission: true);
        GameObject markerPrefab = GetOrCreateMarkerPrefab(markerMaterial);
        manager.SetMarkerPresentation(markersRoot, markerPrefab, markerMaterial);

        Camera camera = Camera.main;
        EvidenceInteraction interaction = null;
        if (camera == null)
        {
            Debug.LogError("Forensic: no Main Camera was found. EvidenceInteraction was not added. Tag the desktop camera as MainCamera.");
        }
        else
        {
            interaction = camera.GetComponent<EvidenceInteraction>();
            if (interaction == null)
                interaction = camera.gameObject.AddComponent<EvidenceInteraction>();
            interaction.Configure(manager, camera);
            if (camera.nearClipPlane > 0.05f)
                camera.nearClipPlane = 0.05f;
        }

        ui.Configure(manager, interaction);
        ui.BuildInterfaceIfNeeded();
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(ui);
        if (interaction != null)
            EditorUtility.SetDirty(interaction);
    }

    static bool PlaceOnFloor(Transform floor)
    {
        if (floor == null)
            return false;

        if (floor.GetComponentInChildren<EvidenceObject>(true) != null)
        {
            Notify("Choose the room, not an evidence item", "The selection already contains evidence. Select the room or floor object. The building was not modified.");
            return false;
        }

        var renderers = floor.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Notify("No room geometry", "The selection has no renderers, so there is no measured floor to place evidence on. Choose a room that contains geometry. The building was not modified.");
            return false;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        if (bounds.size.y > SingleFloorMaxHeight)
        {
            Notify(
                "Selection spans more than one floor",
                "The selection is " + bounds.size.y.ToString("0.00") + " m tall. Select a single room. Evidence was not placed and the building was not modified.");
            return false;
        }

        const float inset = 0.35f;
        float minX = bounds.min.x + inset;
        float maxX = bounds.max.x - inset;
        float minZ = bounds.min.z + inset;
        float maxZ = bounds.max.z - inset;
        if (maxX - minX < 0.8f || maxZ - minZ < 0.4f)
        {
            Notify(
                "Floor area is too small",
                "The measured bounds are " + bounds.size.x.ToString("0.00") + " x " + bounds.size.z.ToString("0.00") + " m after inset. Eight items were not scattered outside that floor. The building was not modified.");
            return false;
        }

        Shader shader = FindUrpLitShader();
        if (shader == null)
        {
            Debug.LogError("Forensic: Universal Render Pipeline/Lit was not found. Evidence was not placed.");
            return false;
        }

        SetupSystems();
        Transform evidenceRoot = GetOrCreateChild(Object.FindAnyObjectByType<CrimeSceneEvidenceManager>().transform, "InvestigationEvidence");
        for (int i = evidenceRoot.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(evidenceRoot.GetChild(i).gameObject);

        EvidenceSpec[] specs = CreateSpecs();
        for (int i = 0; i < specs.Length; i++)
        {
            int column = i % 4;
            int row = i / 4;
            float x = Mathf.Lerp(minX, maxX, column / 3f);
            float z = Mathf.Lerp(minZ, maxZ, row);
            Vector3 position = new Vector3(x, bounds.min.y + specs[i].FloorLift, z);
            GameObject prefab = GetOrCreateEvidencePrefab(specs[i], shader);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(instance, "Place evidence");
            instance.transform.SetParent(evidenceRoot, true);
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.identity;
            EvidenceObject evidence = instance.GetComponent<EvidenceObject>();
            evidence.CaptureGroundTruthFromWorldPosition();
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(evidence);
        }

        CrimeSceneEvidenceManager manager = Object.FindAnyObjectByType<CrimeSceneEvidenceManager>();
        manager.AssignInvestigationFloor(floor);

        Camera camera = Camera.main;
        if (camera != null)
        {
            Vector3 viewpoint = new Vector3(bounds.center.x, bounds.min.y + 1.6f, bounds.center.z);
            camera.transform.position = viewpoint;
            Vector3 lookTarget = new Vector3(bounds.center.x, bounds.min.y + 0.2f, bounds.center.z + 0.75f);
            camera.transform.rotation = Quaternion.LookRotation(lookTarget - viewpoint, Vector3.up);
            EditorUtility.SetDirty(camera);
        }

        Debug.Log("Forensic: placed 8 evidence items using the measured bounds of '" + floor.name + "' (center " + bounds.center.ToString("F3") + ", size " + bounds.size.ToString("F3") + "). Move any item that intersects furniture. Ground truth follows the world position while you edit.");
        return true;
    }

    static Transform FindLowestFurnishedFloor()
    {
        var transforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        var candidates = new List<Transform>();
        var boundsByCandidate = new List<Bounds>();
        float lowest = float.PositiveInfinity;

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate.GetComponentInParent<CrimeSceneEvidenceManager>() != null)
                continue;
            if (candidate.GetComponentInChildren<EvidenceObject>(true) != null)
                continue;

            var renderers = candidate.GetComponentsInChildren<Renderer>();
            if (renderers.Length < 4)
                continue;

            Bounds bounds = renderers[0].bounds;
            for (int r = 1; r < renderers.Length; r++)
                bounds.Encapsulate(renderers[r].bounds);

            if (bounds.size.y > SingleFloorMaxHeight || bounds.size.y < 0.4f)
                continue;
            if (bounds.size.x < 2f || bounds.size.z < 2f)
                continue;

            candidates.Add(candidate);
            boundsByCandidate.Add(bounds);
            if (bounds.min.y < lowest)
                lowest = bounds.min.y;
        }

        Transform best = null;
        float bestArea = -1f;
        for (int i = 0; i < candidates.Count; i++)
        {
            if (boundsByCandidate[i].min.y > lowest + 0.75f)
                continue;

            float area = boundsByCandidate[i].size.x * boundsByCandidate[i].size.z;
            if (area <= bestArea)
                continue;

            bestArea = area;
            best = candidates[i];
        }

        return best;
    }

    static bool Validate(out string report)
    {
        var builder = new StringBuilder();
        bool valid = true;
        CrimeSceneEvidenceManager manager = Object.FindAnyObjectByType<CrimeSceneEvidenceManager>();
        EvidenceInteraction interaction = Object.FindAnyObjectByType<EvidenceInteraction>();
        EvidenceUI ui = Object.FindAnyObjectByType<EvidenceUI>();
        InvestigationEventLogger logger = Object.FindAnyObjectByType<InvestigationEventLogger>();
        var evidence = Object.FindObjectsByType<EvidenceObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        builder.AppendLine("Investigation validation");
        valid &= Report(builder, manager != null, "Evidence manager is present.");
        valid &= Report(builder, logger != null, "Event logger is present.");
        valid &= Report(builder, interaction != null, "Desktop interaction is on a camera.");
        valid &= Report(builder, ui != null, "Investigation UI is present.");
        valid &= Report(builder, Camera.main != null, "A Main Camera is present.");
        valid &= Report(builder, Object.FindAnyObjectByType<LODGroup>() != null, "The building LOD group is still in the scene.");

        var ids = new HashSet<string>();
        int outsideFloor = 0;
        int insideBuilding = 0;
        float minY = float.PositiveInfinity;
        float maxY = float.NegativeInfinity;
        for (int i = 0; i < evidence.Length; i++)
        {
            EvidenceObject item = evidence[i];
            if (!ids.Add(item.EvidenceId))
                valid &= Report(builder, false, "Duplicate evidence ID " + item.EvidenceId + ".");

            bool hasCollider = item.GetComponentInChildren<Collider>() != null;
            valid &= Report(builder, hasCollider, item.EvidenceId + " has a collider.");
            valid &= Report(builder, item.HasCapturedGroundTruth, item.EvidenceId + " has a captured world-space ground truth.");
            if (item.HasCapturedGroundTruth && Vector3.Distance(item.GroundTruthWorldPosition, item.transform.position) > 0.001f)
                valid &= Report(builder, false, item.EvidenceId + " ground truth does not match its current world position.");

            if (item.GetComponentInParent<LODGroup>() != null)
            {
                insideBuilding++;
                valid &= Report(builder, false, item.EvidenceId + " is parented inside the building.");
            }

            minY = Mathf.Min(minY, item.transform.position.y);
            maxY = Mathf.Max(maxY, item.transform.position.y);
            if (manager != null && manager.HasInvestigationFloor && !manager.InvestigationFloorBounds.Contains(item.GroundTruthWorldPosition))
                outsideFloor++;
        }

        valid &= Report(builder, evidence.Length == 8, "Evidence count is " + evidence.Length + " (expected 8).");
        for (int number = 1; number <= 8; number++)
        {
            string id = "EV-00" + number;
            valid &= Report(builder, ids.Contains(id), id + " is present.");
        }

        if (evidence.Length > 0)
            valid &= Report(builder, maxY - minY < 1.25f, "Evidence vertical span is " + (maxY - minY).ToString("0.00") + " m.");

        if (manager != null && manager.HasInvestigationFloor)
        {
            valid &= Report(builder, outsideFloor == 0, outsideFloor + " evidence item(s) are outside the selected floor bounds.");
            builder.AppendLine("Floor reference: " + (manager.InvestigationFloorReference != null ? manager.InvestigationFloorReference.name : "(missing)"));
            builder.AppendLine("Floor bounds center " + manager.InvestigationFloorBounds.center.ToString("F3") + " size " + manager.InvestigationFloorBounds.size.ToString("F3"));
        }
        else
        {
            valid &= Report(builder, false, "No investigation floor has been assigned.");
        }

        valid &= Report(builder, insideBuilding == 0, "No evidence is parented into the building.");
        report = builder.ToString();
        return valid;
    }

    static bool Report(StringBuilder builder, bool passed, string message)
    {
        builder.AppendLine((passed ? "PASS  " : "FAIL  ") + message);
        return passed;
    }

    static EvidenceSpec[] CreateSpecs()
    {
        return new[]
        {
            Spec("EV-001", "Ballistic casing", "Placeholder cartridge casing for the spatial investigation.", EvidenceCategory.Ballistic, PrimitiveType.Cylinder, new Vector3(0.05f, 0.02f, 0.05f), new Color(0.72f, 0.55f, 0.22f), 0.02f, false),
            Spec("EV-002", "Weapon", "Placeholder weapon represented by a cube.", EvidenceCategory.Weapon, PrimitiveType.Cube, new Vector3(0.16f, 0.1f, 0.4f), new Color(0.22f, 0.23f, 0.25f), 0.05f, false),
            Spec("EV-003", "Blood-spatter pattern", "Placeholder blood-spatter pattern on a flat plane. Ground truth is the plane pivot.", EvidenceCategory.Biological, PrimitiveType.Plane, new Vector3(0.045f, 1f, 0.045f), new Color(0.45f, 0.08f, 0.08f), 0.01f, false),
            Spec("EV-004", "Footwear impression", "Placeholder footwear impression on a flat plane. Ground truth is the plane pivot.", EvidenceCategory.Impression, PrimitiveType.Plane, new Vector3(0.03f, 1f, 0.045f), new Color(0.42f, 0.32f, 0.22f), 0.01f, false),
            Spec("EV-005", "Broken glass", "Placeholder cluster of broken glass fragments.", EvidenceCategory.Glass, PrimitiveType.Cube, Vector3.one, new Color(0.75f, 0.9f, 0.95f), 0.02f, true),
            Spec("EV-006", "Mobile phone", "Placeholder mobile phone represented by a thin cube.", EvidenceCategory.Digital, PrimitiveType.Cube, new Vector3(0.075f, 0.012f, 0.15f), new Color(0.12f, 0.14f, 0.18f), 0.006f, false),
            Spec("EV-007", "Bloodied object", "Placeholder bloodied object represented by a cube.", EvidenceCategory.Biological, PrimitiveType.Cube, new Vector3(0.14f, 0.1f, 0.14f), new Color(0.55f, 0.1f, 0.1f), 0.05f, false),
            Spec("EV-008", "Trace evidence", "Placeholder trace evidence represented by a small sphere.", EvidenceCategory.Trace, PrimitiveType.Sphere, Vector3.one * 0.06f, new Color(0.92f, 0.78f, 0.25f), 0.03f, false)
        };
    }

    static EvidenceSpec Spec(string id, string name, string description, EvidenceCategory category, PrimitiveType primitive, Vector3 scale, Color color, float floorLift, bool cluster)
    {
        return new EvidenceSpec
        {
            Id = id,
            DisplayName = name,
            Description = description,
            Category = category,
            Primitive = primitive,
            Scale = scale,
            Color = color,
            FloorLift = floorLift,
            Cluster = cluster
        };
    }

    static GameObject GetOrCreateEvidencePrefab(EvidenceSpec spec, Shader shader)
    {
        EnsureFolder(PrefabFolder);
        string path = PrefabFolder + "/" + spec.Id + ".prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing;

        Material material = GetOrCreateMaterial(spec.Id, spec.Color, shader, emission: false);
        GameObject template = spec.Cluster ? CreateGlassCluster(spec, material) : CreatePrimitiveEvidence(spec, material);
        template.transform.position = Vector3.zero;
        var prefab = PrefabUtility.SaveAsPrefabAsset(template, path);
        Object.DestroyImmediate(template);
        return prefab;
    }

    static GameObject CreatePrimitiveEvidence(EvidenceSpec spec, Material material)
    {
        var evidenceObject = GameObject.CreatePrimitive(spec.Primitive);
        evidenceObject.name = spec.Id + " " + spec.DisplayName;
        evidenceObject.transform.localScale = spec.Scale;
        evidenceObject.isStatic = false;
        var renderer = evidenceObject.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = material;
        evidenceObject.AddComponent<EvidenceObject>().Configure(spec.Id, spec.DisplayName, spec.Description, spec.Category);
        return evidenceObject;
    }

    static GameObject CreateGlassCluster(EvidenceSpec spec, Material material)
    {
        var root = new GameObject(spec.Id + " " + spec.DisplayName);
        root.isStatic = false;
        var box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.02f, 0f);
        box.size = new Vector3(0.16f, 0.05f, 0.16f);
        CreateShard(root.transform, material, new Vector3(0.035f, 0.012f, 0.01f), new Vector3(0.045f, 0.012f, 0.03f));
        CreateShard(root.transform, material, new Vector3(-0.04f, 0.01f, -0.02f), new Vector3(0.03f, 0.01f, 0.04f));
        CreateShard(root.transform, material, new Vector3(0.01f, 0.014f, -0.045f), new Vector3(0.05f, 0.01f, 0.025f));
        CreateShard(root.transform, material, new Vector3(-0.015f, 0.01f, 0.04f), new Vector3(0.025f, 0.01f, 0.035f));
        root.AddComponent<EvidenceObject>().Configure(spec.Id, spec.DisplayName, spec.Description, spec.Category);
        return root;
    }

    static void CreateShard(Transform parent, Material material, Vector3 localPosition, Vector3 scale)
    {
        var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
        shard.name = "Glass fragment";
        shard.transform.SetParent(parent, false);
        shard.transform.localPosition = localPosition;
        shard.transform.localScale = scale;
        shard.isStatic = false;
        shard.GetComponent<Renderer>().sharedMaterial = material;
    }

    static GameObject GetOrCreateMarkerPrefab(Material material)
    {
        EnsureFolder(PrefabFolder);
        string path = PrefabFolder + "/EvidenceMarker.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing;

        GameObject template = EvidenceTag.BuildDefaultMarker(material, null);
        template.transform.position = Vector3.zero;
        var prefab = PrefabUtility.SaveAsPrefabAsset(template, path);
        Object.DestroyImmediate(template);
        return prefab;
    }

    static Material GetOrCreateMaterial(string assetName, Color color, Shader shader, bool emission)
    {
        EnsureFolder(MaterialFolder);
        string path = MaterialFolder + "/" + assetName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader) { name = assetName };
            AssetDatabase.CreateAsset(material, path);
        }

        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.18f);
        material.SetFloat("_Metallic", 0f);
        if (emission)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 0.35f);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;

        var childObject = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(childObject, "Create " + name);
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }

    static Shader FindUrpLitShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader != null)
            return shader;

        return AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Lit.shader");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(path);
        if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    static List<string> CaptureRootNames()
    {
        var names = new List<string>();
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            names.Add(roots[i].name);
        return names;
    }

    static bool OriginalRootsRemain(List<string> originalRoots)
    {
        var current = new HashSet<string>();
        GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            current.Add(roots[i].name);

        bool intact = true;
        for (int i = 0; i < originalRoots.Count; i++)
        {
            if (current.Contains(originalRoots[i]))
                continue;

            Debug.LogError("Forensic: scene root '" + originalRoots[i] + "' is missing after setup.");
            intact = false;
        }

        return intact;
    }

    static void SaveOpenScene()
    {
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();
    }

    static void Notify(string title, string message)
    {
        Debug.Log("Forensic: " + title + ". " + message);
        if (!Application.isBatchMode)
            EditorUtility.DisplayDialog(title, message, "OK");
    }
}
