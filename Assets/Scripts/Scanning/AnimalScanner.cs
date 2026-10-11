using UnityEngine;
using TMPro;

[RequireComponent(typeof(Camera))]
public class AnimalScanner : MonoBehaviour
{
    [Header("Detección")]
    [SerializeField] private float scanRange = 15f;
    [Tooltip("With the phone zoomed in the range grows with the zoom, up to this multiple of Scan Range.")]
    [SerializeField] private float maxZoomRangeMultiplier = 4f;
    [SerializeField] private LayerMask scanMask = ~0;
    [SerializeField] private float scanDuration = 4f;

    [Header("UI - Escaneo")]
    [SerializeField] private GameObject reticleImage;
    [SerializeField] private GameObject scanningText;

    [Header("UI - Descripción")]
    [SerializeField] private GameObject descriptionPanel;
    [SerializeField] private TMP_Text scientificNameText;
    [SerializeField] private TMP_Text standardNameText;
    [SerializeField] private TMP_Text descriptionText;

    private Camera cam;
    private Scannable currentTarget;
    private float scanTimer;
    private bool panelOpen;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        SetScanUIVisible(false);
        if (descriptionPanel != null)
            descriptionPanel.SetActive(false);
    }

    private void Update()
    {
        if (!ThirdPersonCameraController.IsFirstPerson || DialogueManager.IsOpen)
        {
            ResetScan();
            return;
        }

        Scannable target = RaycastForScannable();

        if (target != currentTarget)
        {
            currentTarget = target;
            scanTimer = 0f;
            if (panelOpen) ClosePanel();
        }

        if (currentTarget == null)
        {
            SetScanUIVisible(false);
            return;
        }

        if (panelOpen) return;

        if (AnimalCodex.IsUnlocked(currentTarget.SpeciesId))
        {
            OpenPanel(currentTarget);
            return;
        }

        SetScanUIVisible(true);
        scanTimer += Time.deltaTime;
        if (scanTimer >= scanDuration)
        {
            AnimalCodex.Unlock(currentTarget.SpeciesId);
            OpenPanel(currentTarget);
        }
    }

    private Scannable RaycastForScannable()
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);
        float range = scanRange * Mathf.Clamp(ThirdPersonCameraController.ZoomFactor, 1f, maxZoomRangeMultiplier);
        if (Physics.Raycast(ray, out RaycastHit hit, range, scanMask, QueryTriggerInteraction.Collide))
            return hit.collider.GetComponentInParent<Scannable>();
        return null;
    }

    private void OpenPanel(Scannable target)
    {
        panelOpen = true;
        SetScanUIVisible(false);

        if (scientificNameText != null) scientificNameText.text = target.ScientificName;
        if (standardNameText != null) standardNameText.text = target.DisplayName;
        if (descriptionText != null) descriptionText.text = target.Description;
        if (descriptionPanel != null) descriptionPanel.SetActive(true);
    }

    private void ClosePanel()
    {
        panelOpen = false;
        if (descriptionPanel != null) descriptionPanel.SetActive(false);
    }

    private void ResetScan()
    {
        currentTarget = null;
        scanTimer = 0f;
        if (panelOpen) ClosePanel();
        SetScanUIVisible(false);
    }

    private void SetScanUIVisible(bool visible)
    {
        if (reticleImage != null) reticleImage.SetActive(visible);
        if (scanningText != null) scanningText.SetActive(visible);
    }
}
