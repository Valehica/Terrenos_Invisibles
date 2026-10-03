using UnityEngine;

/// <summary>
/// Punto de recolección de la costa. Puede ser luga (recolectable) o otro
/// elemento del intermareal (piedra, otra alga), que el jugador debe descartar.
/// Los modelos son cubos de colores; se pueden reemplazar por mallas propias.
/// </summary>
public class LugaPatch : MonoBehaviour
{
    [SerializeField] private bool isLuga = true;
    [SerializeField] private string label = "Luga";

    [Tooltip("Cuánto crece el modelo cuando el jugador lo tiene a mano.")]
    [SerializeField] private float focusScale = 1.25f;

    public bool IsLuga => isLuga;
    public string Label => label;
    public bool Collected { get; private set; }

    private Vector3 baseScale;
    private bool focused;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    public void Configure(bool luga, string newLabel)
    {
        isLuga = luga;
        label = newLabel;
    }

    public void SetFocused(bool value)
    {
        if (focused == value) return;
        focused = value;
        transform.localScale = value ? baseScale * focusScale : baseScale;
    }

    public void Collect()
    {
        Collected = true;
        SetFocused(false);
        gameObject.SetActive(false);
    }
}
