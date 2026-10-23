using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using CrimeSceneVR.Analytics;
using CrimeSceneVR.Core;
using CrimeSceneVR.Export;
using CrimeSceneVR.Session;
using CrimeSceneVR.Telemetry;

/// <summary>
/// High-visibility Investigation HUD/panel supporting both Desktop Screen-Space and VR World-Space rendering.
/// Displays live Person 3 telemetry (Recall %, Spatial Error in cm, Timeline Sequence, Kendall's Tau-b,
/// Movement Distance) and transforms into the Final Investigation Results Dashboard on session completion.
/// </summary>
[DisallowMultipleComponent]
public class EvidenceUI : MonoBehaviour
{
    [SerializeField] CrimeSceneEvidenceManager evidenceManager;
    [SerializeField] EvidenceInteraction interaction;
    [SerializeField] RectTransform panelRoot;
    [SerializeField] Text sessionText;
    [SerializeField] Text progressText;
    [SerializeField] Text evidenceIdText;
    [SerializeField] Text evidenceNameText;
    [SerializeField] Text descriptionText;
    [SerializeField] Text statusText;
    [SerializeField] Text detailText;
    [SerializeField] Text controlsText;
    [SerializeField] Button inspectButton;
    [SerializeField] Button tagButton;
    [SerializeField] Button endSessionButton;
    [SerializeField] bool autoSwitchToWorldSpaceInVR = true;

    bool subscribed;
    bool missingManagerLogged;
    bool isWorldSpaceActive;
    bool styleUpgraded;
    float nextTimerRefresh;

    public bool IsWorldSpaceActive => isWorldSpaceActive;

    public void Configure(CrimeSceneEvidenceManager manager, EvidenceInteraction evidenceInteraction)
    {
        if (subscribed)
            Unsubscribe();
        evidenceManager = manager;
        interaction = evidenceInteraction;
    }

    public void BuildInterfaceIfNeeded()
    {
        if (panelRoot != null && evidenceIdText != null && tagButton != null)
        {
            ApplyHighVisibilityStyle();
            return;
        }

        Debug.LogWarning("Forensic: EvidenceUI created a screen-space canvas because its Inspector references were empty.");
        EnsureEventSystem();

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            Debug.LogError("Forensic: LegacyRuntime.ttf was not found. Investigation text may not render.");

        var canvasObject = new GameObject("Investigation Canvas");
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        scaler.dynamicPixelsPerUnit = 4f;
        canvasObject.AddComponent<GraphicRaycaster>();

        panelRoot = CreatePanel(canvasObject.transform);
        var layout = panelRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(22, 22, 20, 20);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panelRoot.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateText(panelRoot, font, "FORENSIC INVESTIGATION", 30, new Color(1f, 0.85f, 0.25f), FontStyle.Bold, 42f);
        sessionText = CreateText(panelRoot, font, "Session", 21, Color.white, FontStyle.Bold, 60f);
        progressText = CreateText(panelRoot, font, "Progress", 24, new Color(0.4f, 0.95f, 1f), FontStyle.Bold, 62f);
        evidenceIdText = CreateText(panelRoot, font, "Evidence", 28, new Color(1f, 0.9f, 0.35f), FontStyle.Bold, 38f);
        evidenceNameText = CreateText(panelRoot, font, "Name", 24, Color.white, FontStyle.Bold, 34f);
        descriptionText = CreateText(panelRoot, font, "Description", 20, new Color(0.95f, 0.96f, 0.98f), FontStyle.Bold, 68f);
        statusText = CreateText(panelRoot, font, "Status", 22, new Color(0.45f, 1f, 0.55f), FontStyle.Bold, 48f);
        detailText = CreateText(panelRoot, font, "Detail", 21, Color.white, FontStyle.Bold, 110f);

        var buttonRow = new GameObject("Actions");
        buttonRow.transform.SetParent(panelRoot, false);
        var rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 10f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;
        var rowElement = buttonRow.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 46f;
        rowElement.minHeight = 46f;

        inspectButton = CreateButton(buttonRow.transform, font, "Inspect (F / Trigger)", new Color(0.18f, 0.38f, 0.62f));
        tagButton = CreateButton(buttonRow.transform, font, "Tag (T / A / Grip)", new Color(0.16f, 0.48f, 0.30f));
        endSessionButton = CreateButton(buttonRow.transform, font, "Finish (Esc / X)", new Color(0.55f, 0.22f, 0.20f));
        inspectButton.onClick.AddListener(InspectClicked);
        tagButton.onClick.AddListener(TagClicked);
        endSessionButton.onClick.AddListener(EndSessionClicked);

        controlsText = CreateText(
            panelRoot,
            font,
            "VR: Left Stick = Move | Right Stick = Turn / Teleport | Trigger = Inspect | A / Grip = Tag | X / Y = Finish\n" +
            "Desktop: WASD+RMB = Move & Look | Click = Select | F = Inspect | T = Tag | Esc / F2 = Finish Session",
            18,
            new Color(0.92f, 0.94f, 0.98f),
            FontStyle.Bold,
            72f);

        CreateCrosshair(canvasObject.transform);
        ApplyHighVisibilityStyle();
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRoot);
    }

    /// <summary>
    /// Upgrades existing serialized UI elements in CrimeScene.unity with larger fonts, bold weights,
    /// crisp text outlines, and a high-contrast dark background so text is clearly legible in VR.
    /// </summary>
    public void ApplyHighVisibilityStyle()
    {
        if (panelRoot == null)
            return;

        if (panelRoot.TryGetComponent<Image>(out Image bg))
        {
            bg.color = new Color(0.03f, 0.05f, 0.09f, 0.97f);
        }

        panelRoot.sizeDelta = new Vector2(640f, 820f);

        if (panelRoot.TryGetComponent<VerticalLayoutGroup>(out VerticalLayoutGroup layout))
        {
            layout.padding = new RectOffset(22, 22, 20, 20);
            layout.spacing = 10f;
        }

        Canvas canvas = panelRoot.GetComponentInParent<Canvas>();
        if (canvas != null && canvas.TryGetComponent<CanvasScaler>(out CanvasScaler scaler))
        {
            scaler.dynamicPixelsPerUnit = 4f;
        }

        // Upgrade title if present
        var allTexts = panelRoot.GetComponentsInChildren<Text>(true);
        for (int i = 0; i < allTexts.Length; i++)
        {
            Text t = allTexts[i];
            if (t == null) continue;
            EnsureTextOutline(t);
            if (t.gameObject.name.Contains("FORENSIC"))
            {
                t.fontSize = 30;
                t.fontStyle = FontStyle.Bold;
                t.color = new Color(1f, 0.85f, 0.25f);
            }
        }

        UpgradeTextStyle(sessionText, 21, Color.white, FontStyle.Bold);
        UpgradeTextStyle(progressText, 24, new Color(0.4f, 0.95f, 1f), FontStyle.Bold);
        UpgradeTextStyle(evidenceIdText, 28, new Color(1f, 0.9f, 0.35f), FontStyle.Bold);
        UpgradeTextStyle(evidenceNameText, 24, Color.white, FontStyle.Bold);
        UpgradeTextStyle(descriptionText, 20, new Color(0.95f, 0.96f, 0.98f), FontStyle.Bold);
        UpgradeTextStyle(statusText, 22, new Color(0.45f, 1f, 0.55f), FontStyle.Bold);
        UpgradeTextStyle(detailText, 21, Color.white, FontStyle.Bold);
        UpgradeTextStyle(controlsText, 18, new Color(0.90f, 0.93f, 0.98f), FontStyle.Bold);

        if (controlsText != null)
        {
            controlsText.text =
                "VR: Left Stick = Move | Right Stick = Turn / Teleport | Trigger = Inspect | A / Grip = Tag | X / Y = Finish\n" +
                "Desktop: WASD+RMB = Move & Look | Click = Select | F = Inspect | T = Tag | Esc / F2 = Finish Session";
        }

        styleUpgraded = true;
    }

    static void UpgradeTextStyle(Text text, int fontSize, Color color, FontStyle style)
    {
        if (text == null)
            return;

        text.fontSize = fontSize;
        text.color = color;
        text.fontStyle = style;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        EnsureTextOutline(text);
    }

    static void EnsureTextOutline(Text text)
    {
        if (text == null)
            return;

        Outline outline = text.GetComponent<Outline>();
        if (outline == null)
            outline = text.gameObject.AddComponent<Outline>();

        outline.effectColor = new Color(0f, 0f, 0f, 0.92f);
        outline.effectDistance = new Vector2(1.4f, -1.4f);
    }

    /// <summary>
    /// Switches the existing canvas to world space at a large, readable scale in front of the VR investigator.
    /// </summary>
    public void UseWorldSpaceCanvas(Transform anchor, float distance, Vector2 size)
    {
        if (panelRoot == null)
            BuildInterfaceIfNeeded();

        ApplyHighVisibilityStyle();

        Canvas canvas = panelRoot != null ? panelRoot.GetComponentInParent<Canvas>() : null;
        if (canvas == null || anchor == null)
        {
            Debug.LogError("Forensic: world-space canvas was not applied because the canvas or anchor is missing.");
            return;
        }

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        RectTransform canvasRect = canvas.GetComponent<RectTransform>();
        canvasRect.sizeDelta = size;

        // Center panel inside the world-space canvas
        panelRoot.anchorMin = new Vector2(0.5f, 0.5f);
        panelRoot.anchorMax = new Vector2(0.5f, 0.5f);
        panelRoot.pivot = new Vector2(0.5f, 0.5f);
        panelRoot.anchoredPosition = Vector2.zero;

        // Hide desktop screen-center crosshair in VR world-space mode
        Transform crosshair = canvas.transform.Find("Crosshair");
        if (crosshair != null)
            crosshair.gameObject.SetActive(false);

        canvas.transform.SetParent(anchor, false);
        canvas.transform.localPosition = new Vector3(-0.18f, -0.05f, distance);
        canvas.transform.localRotation = Quaternion.Euler(6f, -10f, 0f);
        canvas.transform.localScale = Vector3.one * 0.00115f;
        isWorldSpaceActive = true;
    }

    void Start()
    {
        BuildInterfaceIfNeeded();
        ApplyHighVisibilityStyle();
        Subscribe();
        Refresh();
    }

    void Update()
    {
        if (!styleUpgraded)
            ApplyHighVisibilityStyle();

        if (autoSwitchToWorldSpaceInVR && !isWorldSpaceActive && IsVrHeadsetOrControllerActive())
        {
            VRRigReferences rig = VRRigReferences.Instance;
            Transform anchor = rig != null && rig.CenterEyeAnchor != null ? rig.CenterEyeAnchor : (Camera.main != null ? Camera.main.transform : null);
            if (anchor != null)
            {
                UseWorldSpaceCanvas(anchor, 0.82f, new Vector2(660f, 840f));
                Refresh();
            }
        }

        // Refresh elapsed time & distance once per second while session is running
        if (Time.unscaledTime >= nextTimerRefresh)
        {
            nextTimerRefresh = Time.unscaledTime + 1.0f;
            if (evidenceManager != null && evidenceManager.IsSessionStarted && !evidenceManager.IsSessionCompleted)
            {
                Refresh();
            }
        }
    }

    static bool IsVrHeadsetOrControllerActive()
    {
        try
        {
            return OVRManager.isHmdPresent || (OVRInput.GetConnectedControllers() != OVRInput.Controller.None);
        }
        catch
        {
            return false;
        }
    }

    void OnEnable()
    {
        Subscribe();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void InspectClicked()
    {
        ResolveInteraction();
        if (interaction == null)
        {
            Debug.LogError("Forensic: Inspect was not run because EvidenceInteraction is missing.");
            return;
        }

        interaction.InspectSelected();
        Refresh();
    }

    void TagClicked()
    {
        ResolveInteraction();
        if (interaction == null)
        {
            Debug.LogError("Forensic: Tag Evidence was not run because EvidenceInteraction is missing.");
            return;
        }

        interaction.TagSelectedAtCrosshair();
        Refresh();
    }

    void EndSessionClicked()
    {
        if (SessionManager.Instance != null && SessionManager.Instance.IsSessionActive)
        {
            SessionManager.Instance.EndSession();
        }
        else
        {
            ResolveManager();
            if (evidenceManager != null)
                evidenceManager.CompleteSession();
        }
        Refresh();
    }

    void Subscribe()
    {
        if (subscribed)
            return;

        ResolveManager();
        if (evidenceManager == null)
        {
            if (!missingManagerLogged)
            {
                Debug.LogError("Forensic: EvidenceUI found no CrimeSceneEvidenceManager. The investigation panel cannot show progress.");
                missingManagerLogged = true;
            }
            return;
        }

        evidenceManager.SessionStarted += Refresh;
        evidenceManager.EvidenceSelected += OnEvidenceChanged;
        evidenceManager.EvidenceInspected += OnEvidenceChanged;
        evidenceManager.EvidenceDiscovered += OnEvidenceChanged;
        evidenceManager.EvidenceTagged += OnEvidenceChanged;
        evidenceManager.ProgressChanged += Refresh;
        evidenceManager.SessionCompleted += Refresh;

        if (ResearchDataExporter.Instance != null)
        {
            ResearchDataExporter.Instance.OnDatasetsExported -= OnDatasetsExported;
            ResearchDataExporter.Instance.OnDatasetsExported += OnDatasetsExported;
        }

        subscribed = true;
    }

    void Unsubscribe()
    {
        if (!subscribed || evidenceManager == null)
        {
            subscribed = false;
            return;
        }

        evidenceManager.SessionStarted -= Refresh;
        evidenceManager.EvidenceSelected -= OnEvidenceChanged;
        evidenceManager.EvidenceInspected -= OnEvidenceChanged;
        evidenceManager.EvidenceDiscovered -= OnEvidenceChanged;
        evidenceManager.EvidenceTagged -= OnEvidenceChanged;
        evidenceManager.ProgressChanged -= Refresh;
        evidenceManager.SessionCompleted -= Refresh;

        if (ResearchDataExporter.Instance != null)
        {
            ResearchDataExporter.Instance.OnDatasetsExported -= OnDatasetsExported;
        }

        subscribed = false;
    }

    void OnDatasetsExported(SessionResearchSummary summary, string trackingPath, string summaryPath)
    {
        Refresh();
    }

    void OnEvidenceChanged(EvidenceObject _)
    {
        Refresh();
    }

    void Refresh()
    {
        if (sessionText == null)
            return;

        ResolveManager();
        if (evidenceManager == null)
        {
            sessionText.text = "No evidence manager is assigned.";
            FitTexts();
            return;
        }

        SessionManager sm = SessionManager.Instance;
        string participantId = (sm != null && sm.CurrentSession != null) ? sm.CurrentSession.ParticipantId : "P001";
        string condition = (sm != null && sm.CurrentSession != null) ? sm.CurrentSession.Condition : "VR_Standard";
        float elapsedSeconds = sm != null ? sm.ElapsedSessionTime : evidenceManager.RelativeTimeSeconds;
        float distanceMeters = TelemetryCollector.Instance != null ? TelemetryCollector.Instance.TotalDistanceMeters : 0f;

        // If session is completed, render the Person 3 Final Research Results Dashboard!
        if (evidenceManager.IsSessionCompleted || (sm != null && sm.CurrentSession != null && sm.CurrentSession.Status == SessionStatus.Completed))
        {
            RenderCompletedResultsDashboard(participantId, condition, elapsedSeconds, distanceMeters);
            FitTexts();
            return;
        }

        sessionText.text = $"Participant: {participantId}  |  Condition: {condition}\n" +
                           $"Time: {FormatDuration(elapsedSeconds)}  |  Distance Walked: {distanceMeters:0.0} m";

        float recallPct = evidenceManager.RegisteredCount > 0
            ? (evidenceManager.TaggedCount / (float)evidenceManager.RegisteredCount) * 100f
            : 0f;

        progressText.text = $"Discovered: {evidenceManager.DiscoveredCount} / {evidenceManager.RegisteredCount}   |   " +
                            $"Tagged: {evidenceManager.TaggedCount} / {evidenceManager.RegisteredCount} ({recallPct:0}%)\n" +
                            $"Accuracy Threshold: <= {(evidenceManager.AccuracyThresholdMetres * 100f):0.0} cm";

        string liveTelemetryLine = BuildLiveTelemetrySummary();

        EvidenceObject selected = evidenceManager.CurrentSelection;
        if (selected == null)
        {
            evidenceIdText.text = "No Evidence Selected";
            evidenceNameText.text = "Point VR laser ray (or click) at an evidence item.";
            descriptionText.text = "Pull Index Trigger (or F) to Inspect, then press A / Grip (or T) to place a 3D tag.";
            statusText.text = "Status: Waiting for target";
            detailText.text = liveTelemetryLine;
            FitTexts();
            return;
        }

        evidenceIdText.text = $"{selected.EvidenceId} — {(string.IsNullOrEmpty(selected.EvidenceName) ? selected.Category.ToString() : selected.EvidenceName)}";
        evidenceNameText.text = $"Category: {selected.Category}";
        descriptionText.text = selected.Description;
        statusText.text = (selected.IsDiscovered ? "[DISCOVERED]" : "[NOT DISCOVERED]")
            + "     "
            + (selected.IsTagged ? "[TAGGED]" : "[NOT TAGGED]");

        if (!selected.IsTagged)
        {
            detailText.text = $"Ground Truth: ({FormatVector(selected.GroundTruthWorldPosition)})\n{liveTelemetryLine}";
            FitTexts();
            return;
        }

        float errorCm = selected.SpatialErrorMetres * 100f;
        bool accurate = evidenceManager.IsWithinAccuracyTolerance(selected.SpatialErrorMetres);
        string toleranceLabel = accurate ? "ACCURATE <= 5 cm" : "OUTSIDE 5 cm TOLERANCE";

        detailText.text = $"Spatial Error: {errorCm:0.00} cm ({toleranceLabel})\n" +
                          $"Tag Pos: ({FormatVector(selected.TagWorldPosition)})\n" +
                          $"{liveTelemetryLine}";
        FitTexts();
    }

    string BuildLiveTelemetrySummary()
    {
        TimelineManager tm = TimelineManager.Instance;
        if (tm == null || tm.TaggedSequence.Count == 0)
        {
            return "Timeline Sequence: (No items tagged yet)";
        }

        string seq = string.Join(" -> ", tm.TaggedSequence);
        var eval = tm.EvaluateCurrentTaggedSequence();
        return $"Timeline: {seq}\nLive Kendall's Tau-b: {eval.KendallTauScore:0.000} ({eval.ExactMatchCount}/{eval.GroundTruthCount} exact order)";
    }

    void RenderCompletedResultsDashboard(string participantId, string condition, float elapsedSeconds, float distanceMeters)
    {
        SessionResearchSummary summary = ResearchDataExporter.Instance != null ? ResearchDataExporter.Instance.LastExportedSummary : null;
        TimelineValidationResult timelineEval = TimelineManager.Instance != null ? TimelineManager.Instance.EvaluateCurrentTaggedSequence() : null;

        int found = summary != null ? summary.EvidenceTaggedCount : evidenceManager.TaggedCount;
        int total = summary != null ? summary.TotalRegisteredEvidence : evidenceManager.RegisteredCount;
        double recall = total > 0 ? (found / (double)total) * 100.0 : 0.0;

        double meanErrorCm = (summary != null && summary.MeanSpatialErrorMeters.HasValue)
            ? summary.MeanSpatialErrorMeters.Value * 100.0
            : ComputeMeanErrorCmFromScene();
        double medianErrorCm = (summary != null && summary.MedianSpatialErrorMeters.HasValue)
            ? summary.MedianSpatialErrorMeters.Value * 100.0
            : meanErrorCm;

        double tau = summary != null ? summary.KendallTauScore : (timelineEval != null ? timelineEval.KendallTauScore : 0.0);
        int concordant = summary != null ? summary.ConcordantPairs : (timelineEval != null ? timelineEval.KendallResult.ConcordantPairs : 0);
        int discordant = summary != null ? summary.DiscordantPairs : (timelineEval != null ? timelineEval.KendallResult.DiscordantPairs : 0);
        string seq = TimelineManager.Instance != null && TimelineManager.Instance.TaggedSequence.Count > 0
            ? string.Join(" -> ", TimelineManager.Instance.TaggedSequence)
            : "None";

        string summaryFile = (ResearchDataExporter.Instance != null && !string.IsNullOrEmpty(ResearchDataExporter.Instance.LastSummaryCsvPath))
            ? Path.GetFileName(ResearchDataExporter.Instance.LastSummaryCsvPath)
            : "summary_session.csv";

        sessionText.text = $"SESSION COMPLETED  |  Participant: {participantId} ({condition})\n" +
                           $"Investigation Time: {FormatDuration(elapsedSeconds)}  |  Distance: {distanceMeters:0.0} m";

        progressText.text = $"Evidence Found: {found} / {total}   |   Evidence Recall: {recall:0.0}%";

        evidenceIdText.text = "=== INVESTIGATION RESULTS ===";
        evidenceNameText.text = $"Mean Spatial Error: {meanErrorCm:0.00} cm   (Median: {medianErrorCm:0.00} cm)";
        descriptionText.text = $"Timeline Kendall's Tau-b: {tau:0.000}   (Concordant: {concordant} | Discordant: {discordant})";
        statusText.text = $"Timeline Sequence: {seq}";
        detailText.text = $"Telemetry & Summary Exported to ResearchData:\n{summaryFile}";
    }

    double ComputeMeanErrorCmFromScene()
    {
        if (evidenceManager == null || evidenceManager.RegisteredEvidence == null)
            return 0.0;

        float sum = 0f;
        int count = 0;
        var list = evidenceManager.RegisteredEvidence;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].IsTagged && list[i].SpatialErrorMetres >= 0f)
            {
                sum += list[i].SpatialErrorMetres;
                count++;
            }
        }
        return count > 0 ? (sum / count) * 100.0 : 0.0;
    }

    static string FormatDuration(float totalSeconds)
    {
        int sec = Mathf.Max(0, Mathf.FloorToInt(totalSeconds));
        int mins = sec / 60;
        int remSec = sec % 60;
        return $"{mins:00}:{remSec:00}";
    }

    void FitTexts()
    {
        Fit(sessionText);
        Fit(progressText);
        Fit(evidenceIdText);
        Fit(evidenceNameText);
        Fit(descriptionText);
        Fit(statusText);
        Fit(detailText);
        Fit(controlsText);
        if (panelRoot != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRoot);
    }

    static void Fit(Text text)
    {
        if (text == null)
            return;

        LayoutElement element = text.GetComponent<LayoutElement>();
        if (element == null)
            return;

        float height = text.preferredHeight + 8f;
        if (height < text.fontSize + 8f)
            height = text.fontSize + 8f;

        element.minHeight = height;
        element.preferredHeight = height;
    }

    void ResolveManager()
    {
        if (evidenceManager == null)
            evidenceManager = FindAnyObjectByType<CrimeSceneEvidenceManager>();
    }

    void ResolveInteraction()
    {
        if (interaction == null)
            interaction = FindAnyObjectByType<EvidenceInteraction>();
    }

    static string FormatVector(Vector3 value)
    {
        return value.x.ToString("0.00") + ", " + value.y.ToString("0.00") + ", " + value.z.ToString("0.00");
    }

    static void EnsureEventSystem()
    {
        EventSystem eventSystem = FindAnyObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            var eventObject = new GameObject("EventSystem");
            eventObject.AddComponent<EventSystem>();
            eventObject.AddComponent<InputSystemUIInputModule>();
            return;
        }

        var standalone = eventSystem.GetComponent<StandaloneInputModule>();
        if (standalone != null && standalone.enabled)
        {
            standalone.enabled = false;
            Debug.LogWarning("Forensic: StandaloneInputModule was disabled because this project uses the Input System package.");
        }

        if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
    }

    static RectTransform CreatePanel(Transform parent)
    {
        var panelObject = new GameObject("Investigation Panel");
        panelObject.transform.SetParent(parent, false);
        var rect = panelObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -24f);
        rect.sizeDelta = new Vector2(640f, 820f);
        var image = panelObject.AddComponent<Image>();
        image.color = new Color(0.03f, 0.05f, 0.09f, 0.97f);
        image.raycastTarget = true;
        return rect;
    }

    static Text CreateText(Transform parent, Font font, string value, int size, Color color, FontStyle style, float preferredHeight)
    {
        var textObject = new GameObject(value.Length > 32 ? "Text" : value);
        textObject.transform.SetParent(parent, false);
        var text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.UpperLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.text = value;
        EnsureTextOutline(text);
        var element = textObject.AddComponent<LayoutElement>();
        element.preferredHeight = preferredHeight;
        element.minHeight = preferredHeight;
        return text;
    }

    static Button CreateButton(Transform parent, Font font, string label, Color color)
    {
        var buttonObject = new GameObject(label);
        buttonObject.transform.SetParent(parent, false);
        var image = buttonObject.AddComponent<Image>();
        image.color = color;
        var button = buttonObject.AddComponent<Button>();
        button.targetGraphic = image;
        var navigation = button.navigation;
        navigation.mode = Navigation.Mode.None;
        button.navigation = navigation;
        var colors = button.colors;
        colors.normalColor = color;
        colors.highlightedColor = Color.Lerp(color, Color.white, 0.25f);
        colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
        colors.selectedColor = color;
        button.colors = colors;

        var textObject = new GameObject("Label");
        textObject.transform.SetParent(buttonObject.transform, false);
        var rect = textObject.AddComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = textObject.AddComponent<Text>();
        text.font = font;
        text.fontSize = 18;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = label;
        EnsureTextOutline(text);
        return button;
    }

    static void CreateCrosshair(Transform parent)
    {
        var crosshair = new GameObject("Crosshair");
        crosshair.transform.SetParent(parent, false);
        var rect = crosshair.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(10f, 10f);
        rect.anchoredPosition = Vector2.zero;
        var image = crosshair.AddComponent<Image>();
        image.color = new Color(1f, 0.84f, 0.25f, 0.95f);
        image.raycastTarget = false;
    }
}
