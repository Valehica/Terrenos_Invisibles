using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Linterna del jugador: una luz spot que se enciende y apaga. Va en la cámara,
/// así apunta hacia donde mira el jugador.
/// </summary>
public class Flashlight : MonoBehaviour
{
    [SerializeField] private Light lamp;

    [Tooltip("Si está activo, el click izquierdo prende y apaga la linterna.")]
    [SerializeField] private bool handleInput = true;

    public bool IsOn => lamp != null && lamp.enabled;

    private void Awake()
    {
        if (lamp == null) lamp = GetComponent<Light>();
        SetOn(false);
    }

    private void Update()
    {
        if (!handleInput || DialogueManager.IsOpen || PlayerStun.IsStunned) return;
        if (Mouse.current == null) return;

        // Solo con el cursor bloqueado: con un panel abierto el click es para sus botones.
        if (Cursor.lockState != CursorLockMode.Locked) return;

        if (Mouse.current.leftButton.wasPressedThisFrame) Toggle();
    }

    public void Toggle()
    {
        SetOn(!IsOn);
    }

    public void SetOn(bool value)
    {
        if (lamp != null) lamp.enabled = value;
    }
}
