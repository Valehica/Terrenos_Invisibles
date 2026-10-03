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
    [SerializeField] private float firstPersonMinFov = 15f;
    [SerializeField] private float firstPersonMaxFov = 60f;
    [SerializeField] private float firstPersonZoomSpeed = 15f;
    [SerializeField] private GameObject phoneUI;


    [Header("Dialogue View")]
    [SerializeField] private float dialogueHeight = 1.3f;
    [SerializeField] private float dialogueSideDistance = 1.5f;



    private float yaw;
    private float pitch;
    private bool cursorLocked;
    private bool isFirstPerson;
    private Renderer[] playerRenderers;
    private bool isDialogueView;
    private Transform dialogueTarget;
    private Camera cam;
    private float defaultFov;
    public static bool IsFirstPerson { get; private set; }




private void Start()
    {
        yaw = transform.eulerAngles.y;
        pitch = startPitch;
        SetCursorLocked(true);

        cam = GetComponent<Camera>();
        if (cam != null)
            defaultFov = cam.fieldOfView;

        if (target != null)
            playerRenderers = target.GetComponentsInChildren<Renderer>(true);

        if (phoneUI != null)
            phoneUI.SetActive(isFirstPerson);
    }

private void Update()
    {
        if (DialogueManager.IsOpen || IntroSequenceController.IsPlaying) return;

        if (Keyboard.current == null) return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(!cursorLocked);

        if (Keyboard.current.eKey.wasPressedThisFrame)
            SetFirstPerson(!isFirstPerson);

        if (isFirstPerson && cam != null && Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
                cam.fieldOfView = Mathf.Clamp(cam.fieldOfView - scroll * firstPersonZoomSpeed * Time.deltaTime, firstPersonMinFov, firstPersonMaxFov);
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

        if (cursorLocked && Mouse.current != null)
        {
            Vector2 lookDelta = Mouse.current.delta.ReadValue();
            yaw += lookDelta.x * mouseSensitivity;
            pitch -= lookDelta.y * mouseSensitivity;
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

        if (cam != null)
            cam.fieldOfView = defaultFov;

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

    private void SetCursorLocked(bool locked)
    {
        cursorLocked = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
