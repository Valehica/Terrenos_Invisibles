using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Menú de inicio: Jugar lleva a la cinemática del Acto 1, Salir cierra el juego.
/// La cinemática es una escena liviana, así que se carga directo, sin pasar por
/// la pantalla de carga.
/// </summary>
public class MenuPrincipal : MonoBehaviour
{
    [SerializeField] private string escenaInicio = "Intro";

    [Header("UI")]
    [SerializeField] private Button jugarButton;
    [SerializeField] private Button salirButton;
    [Tooltip("Imagen negra a pantalla completa, encima de todo, para los fundidos.")]
    [SerializeField] private Image fundido;
    [SerializeField] private float duracionFundido = 0.8f;

    private bool saliendo;

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (jugarButton != null) jugarButton.onClick.AddListener(Jugar);
        if (salirButton != null) salirButton.onClick.AddListener(Salir);
    }

    private IEnumerator Start()
    {
        // Seleccionado de entrada para que también funcione con teclado (flechas + Enter).
        if (EventSystem.current != null && jugarButton != null)
            EventSystem.current.SetSelectedGameObject(jugarButton.gameObject);

        yield return Fundir(1f, 0f);
    }

    public void Jugar()
    {
        if (saliendo) return;
        saliendo = true;
        StartCoroutine(FundirYCargar());
    }

    public void Salir()
    {
        if (saliendo) return;
        saliendo = true;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator FundirYCargar()
    {
        if (fundido != null) fundido.raycastTarget = true; // bloquea clics durante el fundido
        yield return Fundir(0f, 1f);
        SceneManager.LoadScene(escenaInicio);
    }

    private IEnumerator Fundir(float desde, float hasta)
    {
        if (fundido == null) yield break;

        float t = 0f;
        while (t < duracionFundido)
        {
            t += Time.unscaledDeltaTime;
            SetFundido(Mathf.Lerp(desde, hasta, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.0001f, duracionFundido))));
            yield return null;
        }
        SetFundido(hasta);
        if (hasta <= 0f) fundido.raycastTarget = false;
    }

    private void SetFundido(float alpha)
    {
        Color c = fundido.color;
        c.a = alpha;
        fundido.color = c;
    }
}
