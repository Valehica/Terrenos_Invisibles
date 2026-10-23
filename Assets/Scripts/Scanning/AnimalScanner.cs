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

    /// <summary>
    /// Se dispara cada vez que se abre la ficha de algo con el teléfono, aunque ya estuviera
    /// registrado. Lo usa el flujo de la zona para los pasos de "sácale una foto".
    /// </summary>
    public static event System.Action<Scannable> Fotografiado;

    private Camera cam;
    private Scannable currentTarget;
    private float scanTimer;
    private bool panelOpen;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        scanMask = LimiteJugable.MascaraSin(scanMask); // se escanea a traves de las paredes invisibles
        SetScanUIVisible(false);
        if (descriptionPanel != null)
            descriptionPanel.SetActive(false);
    }

    private void Update()
    {
        if (!UIManager.EstaAbierta(CapaUI.Telefono) || UIManager.BloqueaAcciones)
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
        {
            if (hit.collider is TerrainCollider terrainCollider)
                return TerrainTreeScannable(terrainCollider, hit.point);
            return hit.collider.GetComponentInParent<Scannable>();
        }
        return null;
    }

    // Terrain trees have no GameObject: a hit on a tree's collider reports the TerrainCollider. If the hit is
    // above the ground it was a tree, and the nearest instance gives its prototype, whose prefab carries the
    // Scannable.
    private static Scannable TerrainTreeScannable(TerrainCollider terrainCollider, Vector3 point)
    {
        Terrain terrain = terrainCollider.GetComponent<Terrain>();
        if (terrain == null) return null;
        Vector3 origin = terrain.transform.position;
        if (point.y - (terrain.SampleHeight(point) + origin.y) < 0.15f) return null;

        TerrainData data = terrain.terrainData;
        int nearest = -1;
        float nearestSqr = 9f; // the hit is on the trunk's capsule: within 3 m of the instance
        for (int i = 0; i < data.treeInstanceCount; i++)
        {
            TreeInstance tree = data.GetTreeInstance(i);
            Vector3 p = Vector3.Scale(tree.position, data.size) + origin;
            float dx = p.x - point.x, dz = p.z - point.z;
            float sqr = dx * dx + dz * dz;
            if (sqr < nearestSqr) { nearestSqr = sqr; nearest = i; }
        }
        if (nearest < 0) return null;

        GameObject prefab = data.treePrototypes[data.GetTreeInstance(nearest).prototypeIndex].prefab;
        return prefab != null ? prefab.GetComponent<Scannable>() : null;
    }

    private void OpenPanel(Scannable target)
    {
        panelOpen = true;
        SetScanUIVisible(false);

        if (scientificNameText != null) scientificNameText.text = target.ScientificName;
        if (standardNameText != null) standardNameText.text = target.DisplayName;
        if (descriptionText != null) descriptionText.text = target.Description;
        if (descriptionPanel != null) descriptionPanel.SetActive(true);

        Fotografiado?.Invoke(target);
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
