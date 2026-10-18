using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Desktop investigation panel. The same widget references can later be placed on a world-space canvas.
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

    bool subscribed;
    bool missingManagerLogged;

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
            return;

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
        canvasObject.AddComponent<GraphicRaycaster>();

        panelRoot = CreatePanel(canvasObject.transform);
        var layout = panelRoot.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 16, 16);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panelRoot.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        CreateText(panelRoot, font, "FORENSIC INVESTIGATION", 26, new Color(0.97f, 0.78f, 0.32f), FontStyle.Bold, 36f);
        sessionText = CreateText(panelRoot, font, "Session", 16, Color.white, FontStyle.Normal, 72f);
        progressText = CreateText(panelRoot, font, "Progress", 20, Color.white, FontStyle.Bold, 52f);
        evidenceIdText = CreateText(panelRoot, font, "Evidence", 22, Color.white, FontStyle.Bold, 30f);
        evidenceNameText = CreateText(panelRoot, font, "Name", 20, new Color(0.86f, 0.9f, 0.95f), FontStyle.Normal, 28f);
        descriptionText = CreateText(panelRoot, font, "Description", 17, new Color(0.9f, 0.91f, 0.93f), FontStyle.Normal, 72f);
        statusText = CreateText(panelRoot, font, "Status", 18, Color.white, FontStyle.Bold, 48f);
        detailText = CreateText(panelRoot, font, "Detail", 16, new Color(0.82f, 0.86f, 0.9f), FontStyle.Normal, 72f);

        var buttonRow = new GameObject("Actions");
        buttonRow.transform.SetParent(panelRoot, false);
        var rowLayout = buttonRow.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;
        var rowElement = buttonRow.AddComponent<LayoutElement>();
        rowElement.preferredHeight = 40f;
        rowElement.minHeight = 40f;

        inspectButton = CreateButton(buttonRow.transform, font, "Inspect", new Color(0.18f, 0.34f, 0.55f));
        tagButton = CreateButton(buttonRow.transform, font, "Tag Evidence", new Color(0.16f, 0.42f, 0.28f));
        endSessionButton = CreateButton(buttonRow.transform, font, "End Session", new Color(0.45f, 0.2f, 0.18f));
        inspectButton.onClick.AddListener(InspectClicked);
        tagButton.onClick.AddListener(TagClicked);
        endSessionButton.onClick.AddListener(EndSessionClicked);

        controlsText = CreateText(
            panelRoot,
            font,
            "Left click selects the evidence under the pointer. Solid walls and floors block the ray.\n" +
            "F inspects the selection and marks it discovered.\n" +
            "T, or Tag Evidence, places one marker at the yellow crosshair.\n" +
            "WASD moves. Q and E move down and up. Shift moves faster.\n" +
            "Hold the right mouse button to look. Esc ends the session and closes the log.",
            15,
            new Color(0.78f, 0.8f, 0.84f),
            FontStyle.Normal,
            120f);

        CreateCrosshair(canvasObject.transform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(panelRoot);
    }

    /// <summary>
    /// Switches the existing canvas to world space so a later VR rig can place this panel in front of the investigator.
    /// </summary>
    public void UseWorldSpaceCanvas(Transform anchor, float distance, Vector2 size)
    {
        if (panelRoot == null)
            BuildInterfaceIfNeeded();

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
        canvas.transform.SetParent(anchor, false);
        canvas.transform.localPosition = new Vector3(0f, 0f, distance);
        canvas.transform.localRotation = Quaternion.identity;
        canvas.transform.localScale = Vector3.one * 0.001f;
    }

    void Start()
    {
        BuildInterfaceIfNeeded();
        Subscribe();
        Refresh();
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
        ResolveManager();
        if (evidenceManager == null)
        {
            Debug.LogError("Forensic: End Session was not run because CrimeSceneEvidenceManager is missing.");
            return;
        }

        evidenceManager.CompleteSession();
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
        subscribed = false;
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

        string logName = string.IsNullOrEmpty(evidenceManager.LogFilePath)
            ? "Log file is not open."
            : "Log: " + evidenceManager.LogFilePath;
        sessionText.text = evidenceManager.IsSessionStarted
            ? "Session " + evidenceManager.SessionId + "\n" + logName
            : "Session has not started.\n" + logName;

        progressText.text = "Discovered " + evidenceManager.DiscoveredCount + " / " + evidenceManager.RegisteredCount
            + "     Tagged " + evidenceManager.TaggedCount + " / " + evidenceManager.RegisteredCount
            + "\nTolerance " + evidenceManager.AccuracyThresholdMetres.ToString("0.00") + " m";

        EvidenceObject selected = evidenceManager.CurrentSelection;
        if (selected == null)
        {
            evidenceIdText.text = "No evidence selected";
            evidenceNameText.text = "Left-click an evidence object.";
            descriptionText.text = "";
            statusText.text = "Not discovered     Not tagged";
            detailText.text = "";
            FitTexts();
            return;
        }

        evidenceIdText.text = selected.EvidenceId;
        evidenceNameText.text = string.IsNullOrEmpty(selected.EvidenceName) ? selected.Category.ToString() : selected.EvidenceName;
        descriptionText.text = selected.Description;
        statusText.text = (selected.IsDiscovered ? "Discovered" : "Not discovered")
            + "     "
            + (selected.IsTagged ? "Tagged" : "Not tagged");

        if (!selected.IsTagged)
        {
            detailText.text = "Ground truth " + FormatVector(selected.GroundTruthWorldPosition);
            FitTexts();
            return;
        }

        string tolerance = evidenceManager.IsWithinAccuracyTolerance(selected.SpatialErrorMetres)
            ? "within tolerance"
            : "outside tolerance";
        detailText.text = "Tag " + FormatVector(selected.TagWorldPosition)
            + "\nGround truth " + FormatVector(selected.GroundTruthWorldPosition)
            + "\nSpatial error " + selected.SpatialErrorMetres.ToString("0.000") + " m (" + tolerance + ")";
        FitTexts();
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

        float height = text.preferredHeight + 6f;
        if (height < text.fontSize + 6f)
            height = text.fontSize + 6f;

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
        return value.x.ToString("0.000") + ", " + value.y.ToString("0.000") + ", " + value.z.ToString("0.000");
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
        rect.sizeDelta = new Vector2(460f, 640f);
        var image = panelObject.AddComponent<Image>();
        image.color = new Color(0.07f, 0.08f, 0.11f, 0.92f);
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
        text.fontSize = 16;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        text.text = label;
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
