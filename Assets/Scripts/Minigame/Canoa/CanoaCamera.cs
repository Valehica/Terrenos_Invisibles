using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Cámara orbital de la canoa. Con el mouse se gira alrededor de la canoa para
/// mirar el entorno, y la rueda acerca o aleja. Si se deja de mover el mouse
/// mientras se rema, vuelve sola detrás de la canoa, mirando un poco adelante
/// del rumbo para que se vean las rocas que vienen.
///
/// Se activa sobre la Main Camera mientras dura el minijuego (así conserva el
/// pase de estilo y el Volume de la escena).
/// </summary>
public class CanoaCamera : MonoBehaviour
{
    [SerializeField] private CanoaController target;

    [Header("Distancia")]
    [SerializeField] private float distance = 9.5f;
    [SerializeField] private float minDistance = 4f;
    [SerializeField] private float maxDistance = 18f;
    [SerializeField] private float zoomSpeed = 0.01f;
    [SerializeField] private float extraDistanceAtSpeed = 2.5f;

    [Header("Órbita con el mouse")]
    [SerializeField] private float mouseSensitivity = 0.08f;
    [SerializeField] private float startPitch = 22f;
    [SerializeField] private float minPitch = -5f;
    [SerializeField] private float maxPitch = 70f;
    [Tooltip("Segundos sin mover el mouse para que la cámara vuelva detrás de la canoa (solo si la canoa avanza).")]
    [SerializeField] private float recenterDelay = 2.5f;
    [SerializeField] private float recenterSpeed = 1.5f;

    [Header("Encuadre")]
    [SerializeField] private float pivotHeight = 1.4f;
    [Tooltip("Metros por delante de la canoa a los que mira la cámara cuando va detrás.")]
    [SerializeField] private float lookAhead = 8f;
    [SerializeField] private float positionSmoothing = 10f;
    [Tooltip("Capas contra las que la cámara se acerca para no meterse dentro de una roca o la costa.")]
    [SerializeField] private LayerMask collisionMask = ~0;

    private float yawOffset;   // respecto del rumbo de la canoa
    private float pitch;
    private float headingYaw;
    private float surfaceY;
    private float lastMouseTime = -10f;

    public void SetTarget(CanoaController canoe) => target = canoe;

    private void OnEnable()
    {
        pitch = startPitch;
        yawOffset = 0f;
    }

    public void SnapToTarget()
    {
        if (target == null) return;
        headingYaw = target.transform.eulerAngles.y;
        surfaceY = target.SurfaceY;
        yawOffset = 0f;
        pitch = startPitch;
        ComputePose(out Vector3 pos, out Quaternion rot);
        transform.SetPositionAndRotation(pos, rot);
    }

    private void LateUpdate()
    {
        if (target == null) return;
        float dt = Time.deltaTime;

        ReadInput();

        headingYaw = Mathf.LerpAngle(headingYaw, target.transform.eulerAngles.y, 1f - Mathf.Exp(-4f * dt));
        // La cámara acompaña la marejada, pero amortiguada para no marear.
        surfaceY = Mathf.Lerp(surfaceY, target.SurfaceY, 1f - Mathf.Exp(-1.5f * dt));

        bool moving = target.Speed > 1f;
        if (moving && Time.time - lastMouseTime > recenterDelay)
        {
            yawOffset = Mathf.LerpAngle(yawOffset, 0f, 1f - Mathf.Exp(-recenterSpeed * dt));
            pitch = Mathf.Lerp(pitch, startPitch, 1f - Mathf.Exp(-recenterSpeed * dt));
        }

        ComputePose(out Vector3 pos, out Quaternion rot);
        transform.position = Vector3.Lerp(transform.position, pos, 1f - Mathf.Exp(-positionSmoothing * dt));
        transform.rotation = rot;
    }

    private void ReadInput()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 delta = mouse.delta.ReadValue();
        if (delta.sqrMagnitude > 0.01f)
        {
            yawOffset += delta.x * mouseSensitivity;
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, minPitch, maxPitch);
            lastMouseTime = Time.time;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
            distance = Mathf.Clamp(distance - scroll * zoomSpeed * distance, minDistance, maxDistance);
    }

    private void ComputePose(out Vector3 position, out Quaternion rotation)
    {
        Vector3 pivot = target.transform.position;
        pivot.y = surfaceY + pivotHeight;

        Quaternion orbit = Quaternion.Euler(pitch, headingYaw + yawOffset, 0f);

        float speed01 = Mathf.Clamp01(Mathf.Abs(target.Speed) / Mathf.Max(0.1f, target.MaxForwardSpeed));
        float d = distance + extraDistanceAtSpeed * speed01;
        Vector3 desired = pivot - orbit * Vector3.forward * d;

        // Nunca por debajo del agua bajo la cámara.
        desired.y = Mathf.Max(desired.y, surfaceY + 0.8f);

        // Si hay una roca entre la canoa y la cámara, la cámara se acerca.
        Vector3 toCam = desired - pivot;
        if (Physics.SphereCast(pivot, 0.4f, toCam.normalized, out RaycastHit hit, toCam.magnitude, collisionMask, QueryTriggerInteraction.Ignore)
            && hit.rigidbody == null)
        {
            desired = pivot + toCam.normalized * Mathf.Max(1.5f, hit.distance - 0.2f);
        }

        // Detrás de la canoa mira un poco hacia adelante del rumbo; al girar la
        // cámara hacia un costado mira a la canoa.
        float behind = Mathf.Clamp01(Mathf.Cos(yawOffset * Mathf.Deg2Rad));
        Vector3 heading = Quaternion.Euler(0f, headingYaw, 0f) * Vector3.forward;
        Vector3 lookAt = pivot + heading * lookAhead * behind * behind;

        position = desired;
        rotation = Quaternion.LookRotation(lookAt - desired, Vector3.up);
    }
}
