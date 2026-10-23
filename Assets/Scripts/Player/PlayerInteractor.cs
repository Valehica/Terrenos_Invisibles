using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private float interactionRadius = 1f;
    [SerializeField] private LayerMask interactableMask = ~0;
    [SerializeField] private GameObject interactMessage;

    private Interactable current;

    private void Update()
    {
        if (UIManager.BloqueaAcciones)
        {
            SetPromptVisible(false);
            return;
        }

        current = FindNearestInteractable();
        SetPromptVisible(current != null);

        if (current != null && Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
        {
            DialogueManager.Instance?.StartDialogue(current);
        }
    }

    // Si un minijuego apaga al Player con el aviso a la vista, el aviso quedaba
    // pegado en pantalla porque ya no corre el Update que lo esconde.
    private void OnDisable()
    {
        current = null;
        SetPromptVisible(false);
    }

    private Interactable FindNearestInteractable()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, interactionRadius, interactableMask, QueryTriggerInteraction.Collide);

        Interactable nearest = null;
        float nearestDistanceSqr = float.MaxValue;

        foreach (Collider hit in hits)
        {
            Interactable interactable = hit.GetComponentInParent<Interactable>();
            // Apagado = no se le puede hablar por ahora (por ejemplo, un guía mientras camina).
            if (interactable == null || !interactable.isActiveAndEnabled) continue;

            float distanceSqr = (interactable.transform.position - transform.position).sqrMagnitude;
            if (distanceSqr < nearestDistanceSqr)
            {
                nearestDistanceSqr = distanceSqr;
                nearest = interactable;
            }
        }

        return nearest;
    }

    private void SetPromptVisible(bool visible)
    {
        if (interactMessage != null && interactMessage.activeSelf != visible)
            interactMessage.SetActive(visible);
    }

    public bool HasNearbyInteractable => current != null;
}
