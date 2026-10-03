using UnityEngine;

/// <summary>
/// Aturdimiento del jugador: lo tumba y le bloquea el control durante unos
/// segundos. Lo usa el minijuego de la luga cuando una ola alcanza al jugador.
/// </summary>
public class PlayerStun : MonoBehaviour
{
    public static bool IsStunned { get; private set; }

    [Tooltip("Modelo que se inclina al caer. Si queda vacío se busca 'PlayerModel'.")]
    [SerializeField] private Transform model;

    [SerializeField] private float fallAngle = 80f;
    [SerializeField] private float tiltSpeed = 9f;

    private Quaternion baseRotation;
    private float stunnedUntil;

    private void Awake()
    {
        IsStunned = false;

        if (model == null)
        {
            var found = transform.Find("PlayerModel");
            model = found != null ? found : transform;
        }

        baseRotation = model.localRotation;
    }

    public void Stun(float seconds)
    {
        stunnedUntil = Mathf.Max(stunnedUntil, Time.time + seconds);
        IsStunned = true;
    }

    public void ClearStun()
    {
        stunnedUntil = 0f;
        IsStunned = false;
    }

    private void Update()
    {
        IsStunned = Time.time < stunnedUntil;

        Quaternion target = IsStunned
            ? baseRotation * Quaternion.Euler(fallAngle, 0f, 0f)
            : baseRotation;

        model.localRotation = Quaternion.Slerp(model.localRotation, target, Time.deltaTime * tiltSpeed);
    }

    private void OnDisable()
    {
        IsStunned = false;
    }
}
