using UnityEngine;
using UnityEngine.InputSystem;

public class ThirdPersonCameraController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Distance")]
    [SerializeField] private float distance = 4.5f;
    [SerializeField] private float minDistance = 1.0f;
    [SerializeField] private float collisionRadius = 0.2f;
    [SerializeField] private LayerMask collisionMask = ~0;

    [Header("Look")]
    [SerializeField] private float mouseSensitivity = 0.06f;
    [SerializeField] private float minPitch = -30f;
    [SerializeField] private float maxPitch = 50f;
    [SerializeField] private float startPitch = 5f;

    [Header("First Person (Phone Camera)")]
    [SerializeField] private Vector3 firstPersonOffset = new Vector3(0f, 1.6f, 0f);
    [SerializeField] private float firstPersonMinPitch = -80f;
    [SerializeField] private float firstPersonMaxPitch = 85f;
    [SerializeField] private float zoomMinFov = 5f;
    [SerializeField] private float firstPersonMaxFov = 60f;
    [Tooltip("How much each scroll notch zooms in (1.25 = 25% closer per notch).")]
    [SerializeField] private float zoomPerNotch = 1.25f;
    [Tooltip("How fast the FOV catches up with the target zoom.")]
    [SerializeField] private float zoomSmoothing = 12f;
    [Tooltip("Scale mouse look with the zoom so aiming stays steady when zoomed in.")]
    [SerializeField] private bool scaleLookWithZoom = true;
    [SerializeField] private GameObject phoneUI;


    [Header("Dialogue View")]
    [SerializeField] private float dialogueHeight = 1.3f;
    [SerializeField] private float dialogueSideDistance = 1.5f;



    private float yaw;
    private float pitch;
    private bool isFirstPerson;
    private Renderer[] playerRenderers;
    private bool isDialogueView;
    private Transform dialogueTarget;
    private Camera cam;
    private float defaultFov;
    private float targetFov;
    public static bool IsFirstPerson { get; private set; }

    /// <summary>Phone camera magnification (1 = no zoom).</summary>
    public static float ZoomFactor { get; private set; } = 1f;




private void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = startPitch;
        collisionMask = LimiteJugable.MascaraSin(collisionMask); // las paredes invisibles no empujan la camara
        UIManager.Cerrar(CapaUI.CursorSuelto);

        cam = GetComponent<Camera>();
        if (cam != null)
            defaultFov = cam.fieldOfView;
        targetFov = defaultFov;

        if (target != null)
            playerRenderers = target.GetComponentsInChildren<Renderer>(true);

        if (phoneUI != null)
            phoneUI.SetActive(isFirstPerson);
    }

private void Update()
    {
        if (Keyboard.current != null && !UIManager.BloqueaAcciones)
        {
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                if (UIManager.EstaAbierta(CapaUI.CursorSuelto)) UIManager.Cerrar(CapaUI.CursorSuelto);
                else UIManager.Abrir(CapaUI.CursorSuelto, this);
            }

            if (Keyboard.current.eKey.wasPressedThisFrame)
                SetFirstPerson(!isFirstPerson);
        }

        if (isFirstPerson && cam != null)
        {
            // Scroll is a per-frame delta (±1 per notch), so it must not be scaled by deltaTime.
            // Zooming by a factor per notch feels the same at 60° and at 5°.
            float scroll = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
            if (Mathf.Abs(scroll) > 0.01f)
                targetFov = Mathf.Clamp(targetFov / Mathf.Pow(zoomPerNotch, scroll), zoomMinFov, firstPersonMaxFov);

            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFov, 1f - Mathf.Exp(-zoomSmoothing * Time.deltaTime));
            ZoomFactor = defaultFov / cam.fieldOfView;
        }

    }

private void LateUpdate()
    {
        if (target == null) return;

        if (isDialogueView)
        {
            UpdateDialogueView();
            return;
        }

        float pitchMin = isFirstPerson ? firstPersonMinPitch : minPitch;
        float pitchMax = isFirstPerson ? firstPersonMaxPitch : maxPitch;

        if (!UIManager.CursorLibre && Mouse.current != null)
        {
            Vector2 lookDelta = Mouse.current.delta.ReadValue();
            float sensitivity = mouseSensitivity;
            if (isFirstPerson && scaleLookWithZoom) sensitivity /= ZoomFactor;
            yaw += lookDelta.x * sensitivity;
            pitch -= lookDelta.y * sensitivity;
            pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
        }

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        if (isFirstPerson)
        {
            transform.SetPositionAndRotation(target.position + firstPersonOffset, rotation);
            return;
        }

        Vector3 pivot = target.position + targetOffset;
        float resolvedDistance = ResolveCollisionDistance(pivot, rotation);

        transform.SetPositionAndRotation(pivot - rotation * Vector3.forward * resolvedDistance, rotation);
    }

private void SetFirstPerson(bool enabled)
    {
        isFirstPerson = enabled;
        IsFirstPerson = enabled;
        if (enabled) UIManager.Abrir(CapaUI.Telefono, this);
        else UIManager.Cerrar(CapaUI.Telefono);

        if (cam != null)
            cam.fieldOfView = defaultFov;
        targetFov = defaultFov;
        ZoomFactor = 1f;

        if (phoneUI != null)
            phoneUI.SetActive(enabled);

        if (playerRenderers == null) return;
        foreach (Renderer r in playerRenderers)
        {
            if (r != null)
                r.enabled = !enabled;
        }
    }

public void EnterDialogueView(Transform npc)
    {
        if (isFirstPerson)
            SetFirstPerson(false);

        isDialogueView = true;
        dialogueTarget = npc;
    }

    /// <summary>Guarda el teléfono si estaba en la mano.</summary>
    public void ExitPhone()
    {
        if (isFirstPerson)
            SetFirstPerson(false);
    }

    public void ExitDialogueView()
    {
        isDialogueView = false;
        dialogueTarget = null;
    }

private void UpdateDialogueView()
    {
        if (dialogueTarget == null) return;

        Vector3 heightOffset = Vector3.up * dialogueHeight;
        Vector3 playerPoint = target.position + heightOffset;
        Vector3 npcPoint = dialogueTarget.position + heightOffset;
        Vector3 midpoint = (playerPoint + npcPoint) * 0.5f;

        Vector3 lineDir = (npcPoint - playerPoint);
        lineDir.y = 0f;
        if (lineDir.sqrMagnitude < 0.0001f)
            lineDir = Vector3.forward;
        lineDir.Normalize();

        Vector3 side = Vector3.Cross(Vector3.up, lineDir);
        float pairDistance = Vector3.Distance(playerPoint, npcPoint);
        Vector3 cameraPosition = midpoint + side * (pairDistance * 0.9f + dialogueSideDistance);

        Quaternion rotation = Quaternion.LookRotation((midpoint - cameraPosition).normalized, Vector3.up);
        transform.SetPositionAndRotation(cameraPosition, rotation);
    }



private float ResolveCollisionDistance(Vector3 pivot, Quaternion rotation)
    {
        Vector3 direction = -(rotation * Vector3.forward);
        RaycastHit[] hits = Physics.SphereCastAll(pivot, collisionRadius, direction, distance, collisionMask, QueryTriggerInteraction.Ignore);

        float closest = distance;
        foreach (RaycastHit hit in hits)
        {
            if (hit.transform == target || hit.transform.IsChildOf(target)) continue;
            if (hit.distance < closest)
                closest = hit.distance;
        }

        return Mathf.Clamp(closest, minDistance, distance);
    }
}
