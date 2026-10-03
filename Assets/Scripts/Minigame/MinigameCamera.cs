using UnityEngine;

public class MinigameCamera : MonoBehaviour
{
    [Header("Encuadre")]
    [SerializeField] private float height = 18f;
    [SerializeField] private float backDistance = 14f;
    [SerializeField] private float tiltAngle = 55f;
    [SerializeField] private float followSmoothing = 4f;

    private void LateUpdate()
    {
        if (ChainController.Instance == null) return;

        Vector3 targetPos = ChainController.Instance.CenterPosition;
        Vector3 forward = ChainController.Instance.ForwardHeading.normalized;

        Vector3 desiredPosition = targetPos - forward * backDistance + Vector3.up * height;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, followSmoothing * Time.deltaTime);

        Quaternion lookRotation = Quaternion.LookRotation(forward, Vector3.up);
        transform.rotation = lookRotation * Quaternion.Euler(tiltAngle, 0f, 0f);
    }

    public void SnapToTarget()
    {
        if (ChainController.Instance == null) return;

        Vector3 targetPos = ChainController.Instance.CenterPosition;
        Vector3 forward = ChainController.Instance.ForwardHeading.normalized;

        transform.position = targetPos - forward * backDistance + Vector3.up * height;
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(tiltAngle, 0f, 0f);
    }
}
