using TMPro;
using UnityEngine;

/// <summary>
/// Muestra los FPS del juego. Promedia los frames de cada intervalo para que
/// el número no salte, y usa SetText para no generar basura en cada refresco.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class FpsCounter : MonoBehaviour
{
    [Tooltip("Cada cuántos segundos se actualiza el número.")]
    [SerializeField] private float refreshInterval = 0.25f;

    private TMP_Text label;
    private int frames;
    private float elapsed;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    private void Update()
    {
        frames++;
        elapsed += Time.unscaledDeltaTime;
        if (elapsed < refreshInterval) return;

        label.SetText("{0} FPS", Mathf.RoundToInt(frames / elapsed));
        frames = 0;
        elapsed = 0f;
    }
}
