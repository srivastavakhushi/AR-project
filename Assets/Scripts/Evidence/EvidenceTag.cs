using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Numbered world-space marker placed at the investigator's chosen tag position.
/// The visual can be replaced later with a forensic marker prefab.
/// </summary>
[DisallowMultipleComponent]
public class EvidenceTag : MonoBehaviour
{
    [SerializeField] EvidenceObject evidence;
    [SerializeField] int markerNumber;
    [SerializeField] Text label;
    [SerializeField] Transform labelRoot;

    Material runtimeMaterial;
    bool initialized;

    public EvidenceObject Evidence => evidence;
    public int MarkerNumber => markerNumber;

    public static EvidenceTag Create(
        EvidenceObject evidenceObject,
        Vector3 worldPosition,
        int number,
        Transform parent,
        GameObject markerPrefab,
        Material sharedMaterial)
    {
        if (evidenceObject == null)
        {
            Debug.LogError("Forensic: a marker was not created because the evidence object is missing.");
            return null;
        }

        if (evidenceObject.TagMarker != null)
        {
            Debug.LogWarning("Forensic: " + evidenceObject.EvidenceId + " already has marker " + evidenceObject.TagMarker.MarkerNumber + ". A second marker was not created.");
            return evidenceObject.TagMarker;
        }

        EvidenceTag tag;
        if (markerPrefab != null)
        {
            GameObject instance = Instantiate(markerPrefab, worldPosition, Quaternion.identity, parent);
            tag = instance.GetComponent<EvidenceTag>();
            if (tag == null)
                tag = instance.AddComponent<EvidenceTag>();
        }
        else
        {
            tag = BuildDefaultMarker(sharedMaterial, parent).GetComponent<EvidenceTag>();
            tag.transform.position = worldPosition;
        }

        tag.Initialize(evidenceObject, number, sharedMaterial);
        return tag;
    }

    public static GameObject BuildDefaultMarker(Material sharedMaterial, Transform parent)
    {
        var root = new GameObject("Evidence Marker");
        if (parent != null)
            root.transform.SetParent(parent, false);

        var tag = root.AddComponent<EvidenceTag>();

        var pin = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pin.name = "Pin";
        pin.transform.SetParent(root.transform, false);
        pin.transform.localPosition = new Vector3(0f, 0.04f, 0f);
        pin.transform.localScale = new Vector3(0.045f, 0.04f, 0.045f);
        ApplyMaterial(pin.GetComponent<Renderer>(), sharedMaterial, tag);

        var canvasObject = new GameObject("Label");
        canvasObject.transform.SetParent(root.transform, false);
        canvasObject.transform.localPosition = new Vector3(0f, 0.16f, 0f);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(220f, 90f);
        rect.localScale = Vector3.one * 0.0022f;

        var textObject = new GameObject("Text");
        textObject.transform.SetParent(canvasObject.transform, false);
        var textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        var text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 42;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color(0.12f, 0.1f, 0.05f, 1f);
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.text = "#";

        var background = canvasObject.AddComponent<Image>();
        background.color = new Color(1f, 0.86f, 0.35f, 0.94f);
        background.raycastTarget = false;

        tag.label = text;
        tag.labelRoot = canvasObject.transform;
        return root;
    }

    public void Initialize(EvidenceObject evidenceObject, int number, Material sharedMaterial)
    {
        if (initialized && evidence == evidenceObject)
            return;

        evidence = evidenceObject;
        markerNumber = number;
        if (label == null)
            label = GetComponentInChildren<Text>(true);
        if (labelRoot == null && label != null)
            labelRoot = label.canvas != null ? label.canvas.transform : label.transform;

        if (label != null)
            label.text = number.ToString() + "\n" + evidenceObject.EvidenceId;
        else
            Debug.LogWarning("Forensic: marker " + number + " for " + evidenceObject.EvidenceId + " has no label. The marker number is stored on the component.");

        if (sharedMaterial != null)
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterial = sharedMaterial;
        }

        evidenceObject.AssignTagMarker(this);
        gameObject.name = "Marker " + number + " " + evidenceObject.EvidenceId;
        initialized = true;
    }

    static void ApplyMaterial(Renderer renderer, Material sharedMaterial, EvidenceTag owner)
    {
        if (renderer == null)
            return;

        if (sharedMaterial != null)
        {
            renderer.sharedMaterial = sharedMaterial;
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("Forensic: Universal Render Pipeline/Lit was not found. The evidence marker is using the primitive default material.");
            return;
        }

        var material = new Material(shader);
        material.SetColor("_BaseColor", new Color(0.95f, 0.72f, 0.18f, 1f));
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", new Color(0.35f, 0.22f, 0.05f, 1f));
        renderer.material = material;
        if (owner != null)
            owner.runtimeMaterial = material;
    }

    void LateUpdate()
    {
        if (labelRoot == null)
            return;

        Camera view = Camera.main;
        if (view == null)
            return;

        if (labelRoot.TryGetComponent<Canvas>(out Canvas canvas) && canvas.worldCamera == null)
            canvas.worldCamera = view;

        Vector3 facing = labelRoot.position - view.transform.position;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f)
            return;

        labelRoot.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }
}
