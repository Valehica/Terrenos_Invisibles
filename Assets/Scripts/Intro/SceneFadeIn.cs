using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Cubre la pantalla de negro al cargar la escena y la va aclarando hasta
/// dejarla transparente. Pensado para escenas que no tienen intro narrada.
/// </summary>
[RequireComponent(typeof(Image))]
public class SceneFadeIn : MonoBehaviour
{
    [Header("Ritmo")]
    [SerializeField] private float holdTime = 0.2f;
    [SerializeField] private float fadeDuration = 1.75f;

    [Header("Apariencia")]
    [SerializeField] private Color fadeColor = Color.black;

    [Tooltip("Desactiva el panel al terminar para que no gaste dibujado.")]
    [SerializeField] private bool disableWhenDone = true;

    private Image image;

    private void Awake()
    {
        image = GetComponent<Image>();
        image.raycastTarget = false;
        SetAlpha(1f);
    }

    private void OnEnable()
    {
        SetAlpha(1f);
        StartCoroutine(Fade());
    }

    private IEnumerator Fade()
    {
        if (holdTime > 0f) yield return new WaitForSecondsRealtime(holdTime);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            SetAlpha(1f - Mathf.SmoothStep(0f, 1f, elapsed / Mathf.Max(0.0001f, fadeDuration)));
            yield return null;
        }

        SetAlpha(0f);
        if (disableWhenDone) gameObject.SetActive(false);
    }

    private void SetAlpha(float a)
    {
        if (image == null) image = GetComponent<Image>();
        image.color = new Color(fadeColor.r, fadeColor.g, fadeColor.b, a);
    }
}
