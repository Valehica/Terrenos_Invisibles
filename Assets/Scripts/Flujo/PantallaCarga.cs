using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Pantalla de carga entre zonas (escena "Carga").
///
/// Carga la escena pedida a <see cref="CargadorEscenas"/> en segundo plano, con
/// una barra de progreso, y solo la activa cuando terminó de cargar y pasó un
/// tiempo mínimo. La activación de una zona pesada congela unos segundos el
/// juego: por eso se hace con la pantalla ya en negro, y la zona aparece con su
/// propio fundido (SceneFadeIn).
/// </summary>
public class PantallaCarga : MonoBehaviour
{
    [Header("Destino")]
    [Tooltip("Escena que se carga si se da Play directo en esta escena (para probar).")]
    [SerializeField] private string escenaPorDefecto = "Norte";

    [Header("UI")]
    [Tooltip("Relleno de la barra: se estira de izquierda a derecha moviendo su ancla derecha.")]
    [SerializeField] private RectTransform barraRelleno;
    [SerializeField] private TMP_Text porcentajeText;
    [SerializeField] private TMP_Text tituloText;
    [SerializeField] private TMP_Text datoText;
    [Tooltip("Imagen negra a pantalla completa, encima de todo, para los fundidos.")]
    [SerializeField] private Image fundido;

    [Header("Animación (opcional)")]
    [SerializeField] private Image animacionImage;
    [SerializeField] private Sprite[] framesAnimacion;
    [SerializeField] private float framesPorSegundo = 12f;

    [Header("Ritmo")]
    [Tooltip("Segundos mínimos en pantalla, aunque la escena cargue antes: que alcance a leerse el dato.")]
    [SerializeField] private float duracionMinima = 3f;
    [Tooltip("Lo más rápido que avanza la barra, en fracción por segundo.")]
    [SerializeField] private float velocidadBarra = 0.8f;
    [SerializeField] private float duracionFundido = 0.6f;

    private float animTime;

    private void Awake()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (tituloText != null && !string.IsNullOrEmpty(CargadorEscenas.Titulo)) tituloText.text = CargadorEscenas.Titulo;
        if (datoText != null && !string.IsNullOrEmpty(CargadorEscenas.Dato)) datoText.text = CargadorEscenas.Dato;

        SetProgreso(0f);
        SetFundido(1f);

        bool hayAnimacion = framesAnimacion != null && framesAnimacion.Length > 0;
        if (animacionImage != null) animacionImage.gameObject.SetActive(hayAnimacion);
    }

    private IEnumerator Start()
    {
        string destino = CargadorEscenas.TomarDestino(escenaPorDefecto);

        yield return Fundir(1f, 0f);

        // La zona anterior ya se descargó al entrar aquí: se liberan sus texturas
        // y mallas antes de empezar a cargar la siguiente.
        yield return Resources.UnloadUnusedAssets();
        System.GC.Collect();

        AsyncOperation carga = SceneManager.LoadSceneAsync(destino);
        if (carga == null)
        {
            Debug.LogError($"PantallaCarga: no se pudo cargar la escena \"{destino}\". ¿Está en Build Settings?");
            yield break;
        }
        carga.allowSceneActivation = false;

        float tiempo = 0f;
        float mostrado = 0f;
        while (true)
        {
            tiempo += Time.unscaledDeltaTime;

            // Con allowSceneActivation = false el progreso se detiene en 0.9.
            float real = Mathf.Clamp01(carga.progress / 0.9f);
            float porTiempo = Mathf.Clamp01(tiempo / Mathf.Max(0.01f, duracionMinima));
            mostrado = Mathf.MoveTowards(mostrado, Mathf.Min(real, porTiempo), velocidadBarra * Time.unscaledDeltaTime);
            SetProgreso(mostrado);

            if (mostrado >= 1f && carga.progress >= 0.9f) break;
            yield return null;
        }

        yield return Fundir(0f, 1f);
        carga.allowSceneActivation = true;
    }

    private void Update()
    {
        if (animacionImage == null || framesAnimacion == null || framesAnimacion.Length == 0) return;
        animTime += Time.unscaledDeltaTime;
        animacionImage.sprite = framesAnimacion[(int)(animTime * framesPorSegundo) % framesAnimacion.Length];
    }

    private void SetProgreso(float t)
    {
        if (barraRelleno != null)
        {
            barraRelleno.anchorMin = new Vector2(0f, barraRelleno.anchorMin.y);
            barraRelleno.anchorMax = new Vector2(t, barraRelleno.anchorMax.y);
        }
        if (porcentajeText != null) porcentajeText.text = Mathf.RoundToInt(t * 100f) + "%";
    }

    private IEnumerator Fundir(float desde, float hasta)
    {
        float t = 0f;
        while (t < duracionFundido)
        {
            t += Time.unscaledDeltaTime;
            SetFundido(Mathf.Lerp(desde, hasta, Mathf.SmoothStep(0f, 1f, t / Mathf.Max(0.0001f, duracionFundido))));
            yield return null;
        }
        SetFundido(hasta);
    }

    private void SetFundido(float alpha)
    {
        if (fundido == null) return;
        Color c = fundido.color;
        c.a = alpha;
        fundido.color = c;
        fundido.raycastTarget = false;
    }
}
