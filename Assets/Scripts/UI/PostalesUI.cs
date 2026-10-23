using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Las postales que guardaron los papás de Maite. Con R se despliegan en abanico sobre la
/// pantalla (la cámara se queda quieta y aparece el cursor); al pasar el mouse una postal crece
/// y sube, y al hacer clic queda grande a la izquierda y se vuelve a jugar normal (sin cursor,
/// con cámara y sin fondo oscuro) para compararla con el lugar. Esc o clic derecho vuelven al
/// abanico; R guarda las postales, y Esc también desde el abanico.
///
/// Arma su propio Canvas por código. Cada postal tiene una zona de clic que no se mueve con el
/// hover y una imagen hija que es la que se anima: así, al crecer, el borde no sale de debajo
/// del mouse y no parpadea.
/// </summary>
public class PostalesUI : MonoBehaviour
{
    [SerializeField] private Sprite[] postales;

    [Header("Abanico (en píxeles de una pantalla de 1920x1080)")]
    [SerializeField] private float altoPostal = 560f;
    [SerializeField] private float separacion = 340f;
    [SerializeField] private float alturaAbanico = 20f;
    [Tooltip("Cuánto más abajo quedan las postales de los costados.")]
    [SerializeField] private float caidaBordes = 50f;
    [Tooltip("Grados que se giran las postales de los costados.")]
    [SerializeField] private float giroBordes = 12f;

    [Header("Mouse encima")]
    [SerializeField] private float escalaHover = 1.12f;
    [SerializeField] private float subidaHover = 45f;

    [Header("Seleccionada")]
    [SerializeField] private Vector2 posicionSeleccionada = new Vector2(-480f, 10f);
    [SerializeField] private float escalaSeleccionada = 1.55f;

    [Header("Barra del punto de la postal (a la derecha, a la altura del borde de arriba)")]
    [SerializeField] private Vector2 tamanoBarra = new Vector2(520f, 8f);
    [Tooltip("Parte de la imagen que es margen transparente arriba del marco blanco.")]
    [SerializeField] private float margenArribaPostal = 0.05f;
    [SerializeField] private float segundosBarraCompleta = 2f;

    [Header("Aspecto")]
    [SerializeField] private float suavizado = 12f;
    [SerializeField] private Color colorFondo = new Color(0f, 0f, 0f, 0.45f);
    [SerializeField] private int ordenCanvas = 60;

    private static readonly Vector2 posicionGuardada = new Vector2(0f, -800f);

    private class Carta
    {
        public RectTransform zona;
        public RectTransform imagen;
        public CanvasGroup grupo;
        public Image golpe;
        public Sprite sprite;
        public Vector2 posAbanico;
        public float giroAbanico;
        public float distanciaCentro;
    }

    private readonly List<Carta> cartas = new List<Carta>();
    private GameObject raiz;
    private CanvasGroup raizGrupo;
    private Image fondo;
    private RectTransform contenedor;
    private TMP_Text ayuda;
    private ThirdPersonCameraController camara;

    private CanvasGroup barraGrupo;
    private RectTransform barraRelleno;
    private Object duenoBarra;
    private bool barraVisible;
    private float ocultarBarraEn = -1f;

    private bool abierto;
    private int hover = -1;
    private int seleccionada = -1;

    public bool Abierto => abierto;

    /// <summary>La postal elegida, la que está a la izquierda con el juego andando; null si no hay.</summary>
    public Sprite PostalEnMano => abierto && seleccionada >= 0 ? cartas[seleccionada].sprite : null;

    /// <summary>
    /// Llena la barra de la derecha hasta <paramref name="valor"/> (0 a 1); un valor negativo la
    /// esconde. Completada, queda llena unos segundos y se va sola. La usa <see cref="PuntoPostal"/>.
    /// </summary>
    public void MostrarProgreso(Object dueno, float valor, bool completado = false)
    {
        if (valor < 0f)
        {
            if (duenoBarra == dueno) barraVisible = false;
            return;
        }

        duenoBarra = dueno;
        barraVisible = true;
        ocultarBarraEn = completado ? Time.time + segundosBarraCompleta : -1f;
        barraRelleno.anchorMax = new Vector2(Mathf.Clamp01(valor), 1f);
    }

    private void Awake()
    {
        Construir();
        raiz.SetActive(false);
    }

    private void OnDisable()
    {
        if (!abierto) return;
        abierto = false;
        UIManager.Cerrar(CapaUI.Postales, this);
        UIManager.Cerrar(CapaUI.PostalEnMano, this);
    }

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (!abierto)
            {
                if (kb.rKey.wasPressedThisFrame && !UIManager.BloqueaAcciones) Abrir();
            }
            else if (kb.rKey.wasPressedThisFrame)
            {
                Cerrar();
            }
            else if (kb.escapeKey.wasPressedThisFrame)
            {
                if (seleccionada >= 0) Volver();
                else Cerrar();
            }
        }

        if (abierto && seleccionada >= 0 && Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            Volver();

        if (raiz.activeSelf) Animar(Time.unscaledDeltaTime);
    }

    public void Abrir()
    {
        if (abierto || cartas.Count == 0) return;
        abierto = true;
        hover = -1;
        seleccionada = -1;

        if (camara == null) camara = FindAnyObjectByType<ThirdPersonCameraController>();
        if (camara != null) camara.ExitPhone();
        UIManager.Abrir(CapaUI.Postales, this);
        AsegurarEventSystem();

        // Si ya estaban guardadas, salen desde abajo de la pantalla.
        if (!raiz.activeSelf)
        {
            raizGrupo.alpha = 0f;
            foreach (Carta c in cartas)
            {
                c.zona.anchoredPosition = posicionGuardada;
                c.zona.localRotation = Quaternion.identity;
                c.zona.localScale = Vector3.one * 0.8f;
                c.imagen.anchoredPosition = Vector2.zero;
                c.imagen.localScale = Vector3.one;
                c.grupo.alpha = 1f;
            }
            raiz.SetActive(true);
        }

        Reordenar();
    }

    public void Cerrar()
    {
        if (!abierto) return;
        abierto = false;
        hover = -1;
        seleccionada = -1;
        UIManager.Cerrar(CapaUI.Postales, this);
        UIManager.Cerrar(CapaUI.PostalEnMano, this);
        Reordenar();
    }

    private void Elegir(int i)
    {
        if (!abierto || seleccionada >= 0) return;
        seleccionada = i;
        hover = -1;
        // Primero la postal en mano, después se suelta el abanico: así nunca queda un frame
        // sin capa en el que E o Esc lleguen a la exploración.
        UIManager.Abrir(CapaUI.PostalEnMano, this);
        UIManager.Cerrar(CapaUI.Postales, this);
        Reordenar();
    }

    private void Volver()
    {
        seleccionada = -1;
        UIManager.Abrir(CapaUI.Postales, this);
        UIManager.Cerrar(CapaUI.PostalEnMano, this);
        Reordenar();
    }

    private void Hover(int i, bool encima)
    {
        if (encima) hover = i;
        else if (hover == i) hover = -1;
        Reordenar();
    }

    private void Animar(float dt)
    {
        float k = 1f - Mathf.Exp(-suavizado * dt);

        raizGrupo.alpha = Mathf.Lerp(raizGrupo.alpha, abierto ? 1f : 0f, k);
        if (!abierto && raizGrupo.alpha < 0.02f)
        {
            raiz.SetActive(false);
            return;
        }

        // Con la postal en mano el juego se ve normal.
        Color cf = fondo.color;
        cf.a = Mathf.Lerp(cf.a, seleccionada >= 0 ? 0f : colorFondo.a, k);
        fondo.color = cf;

        if (ocultarBarraEn >= 0f && Time.time >= ocultarBarraEn)
        {
            barraVisible = false;
            ocultarBarraEn = -1f;
        }
        barraGrupo.alpha = Mathf.Lerp(barraGrupo.alpha, barraVisible && seleccionada >= 0 ? 1f : 0f, k);

        for (int i = 0; i < cartas.Count; i++)
        {
            Carta c = cartas[i];
            Vector2 pos = c.posAbanico;
            float giro = c.giroAbanico;
            float escala = 1f, alfa = 1f;
            Vector2 subida = Vector2.zero;
            float escalaImagen = 1f;

            if (!abierto)
            {
                pos = posicionGuardada;
                giro = 0f;
            }
            else if (seleccionada == i)
            {
                pos = posicionSeleccionada;
                giro = 0f;
                escala = escalaSeleccionada;
            }
            else if (seleccionada >= 0)
            {
                pos += new Vector2(0f, -140f);
                alfa = 0f;
            }
            else if (hover == i)
            {
                subida = new Vector2(0f, subidaHover);
                escalaImagen = escalaHover;
            }

            c.zona.anchoredPosition = Vector2.Lerp(c.zona.anchoredPosition, pos, k);
            c.zona.localRotation = Quaternion.Slerp(c.zona.localRotation, Quaternion.Euler(0f, 0f, giro), k);
            c.zona.localScale = Vector3.Lerp(c.zona.localScale, Vector3.one * escala, k);
            c.grupo.alpha = Mathf.Lerp(c.grupo.alpha, alfa, k);
            c.imagen.anchoredPosition = Vector2.Lerp(c.imagen.anchoredPosition, subida, k);
            c.imagen.localScale = Vector3.Lerp(c.imagen.localScale, Vector3.one * escalaImagen, k);
        }
    }

    /// <summary>La del centro queda encima de las de los costados; la que se mira, encima de todas.</summary>
    private void Reordenar()
    {
        var orden = new List<Carta>(cartas);
        orden.Sort((a, b) => b.distanciaCentro.CompareTo(a.distanciaCentro));
        foreach (Carta c in orden) c.zona.SetAsLastSibling();

        int arriba = seleccionada >= 0 ? seleccionada : hover;
        if (arriba >= 0) cartas[arriba].zona.SetAsLastSibling();

        // Con la postal en mano el cursor está tomado: nada recibe el mouse.
        bool abanico = abierto && seleccionada < 0;
        for (int i = 0; i < cartas.Count; i++)
            cartas[i].golpe.raycastTarget = abanico;
        fondo.raycastTarget = abanico;

        if (ayuda != null)
            ayuda.text = seleccionada >= 0
                ? "Esc para ver las postales  ·  R para guardar"
                : "Clic en una postal para verla  ·  R para guardar";
    }

    private void Construir()
    {
        raiz = new GameObject("PostalesUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                              typeof(GraphicRaycaster), typeof(CanvasGroup));
        raiz.transform.SetParent(transform, false);
        var canvas = raiz.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = ordenCanvas;
        var scaler = raiz.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        raizGrupo = raiz.GetComponent<CanvasGroup>();

        // Fondo oscuro detrás del abanico; también evita que el clic llegue al juego.
        var rectFondo = Hijo("Fondo", raiz.transform, typeof(Image));
        Estirar(rectFondo);
        fondo = rectFondo.GetComponent<Image>();
        fondo.color = colorFondo;

        contenedor = Hijo("Postales", raiz.transform);
        Estirar(contenedor);

        int n = postales != null ? postales.Length : 0;
        for (int i = 0; i < n; i++)
        {
            Sprite sprite = postales[i];
            if (sprite == null) continue;
            cartas.Add(CrearCarta(sprite, i, n));
        }

        CrearBarra();
        ayuda = CrearAyuda();
        Reordenar();
    }

    // Del otro lado de la pantalla que la postal elegida, a la altura de su marco de arriba.
    private void CrearBarra()
    {
        var barra = Hijo("BarraPunto", raiz.transform, typeof(Image), typeof(CanvasGroup));
        float altoElegida = altoPostal * escalaSeleccionada;
        float arribaMarco = posicionSeleccionada.y + altoElegida * (0.5f - margenArribaPostal);
        barra.sizeDelta = tamanoBarra;
        barra.anchoredPosition = new Vector2(-posicionSeleccionada.x, arribaMarco - tamanoBarra.y * 0.5f);
        var fondoBarra = barra.GetComponent<Image>();
        fondoBarra.color = new Color(1f, 1f, 1f, 0.25f);
        fondoBarra.raycastTarget = false;
        barraGrupo = barra.GetComponent<CanvasGroup>();
        barraGrupo.alpha = 0f;
        barraGrupo.blocksRaycasts = false;

        barraRelleno = Hijo("Relleno", barra, typeof(Image));
        barraRelleno.anchorMin = Vector2.zero;
        barraRelleno.anchorMax = new Vector2(0f, 1f);
        barraRelleno.offsetMin = barraRelleno.offsetMax = Vector2.zero;
        var relleno = barraRelleno.GetComponent<Image>();
        relleno.color = Color.white;
        relleno.raycastTarget = false;
    }

    private Carta CrearCarta(Sprite sprite, int i, int n)
    {
        float t = n > 1 ? (float)i / (n - 1) * 2f - 1f : 0f;
        float aspecto = sprite.rect.width / sprite.rect.height;

        var zona = Hijo("Postal " + (i + 1), contenedor, typeof(Image), typeof(CanvasGroup));
        zona.sizeDelta = new Vector2(altoPostal * aspecto, altoPostal);
        var golpe = zona.GetComponent<Image>();
        golpe.color = Color.clear; // invisible, solo recibe el mouse

        var imagen = Hijo("Imagen", zona, typeof(Image));
        Estirar(imagen);
        var img = imagen.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        img.raycastTarget = false;

        var carta = new Carta
        {
            zona = zona,
            imagen = imagen,
            grupo = zona.GetComponent<CanvasGroup>(),
            golpe = golpe,
            sprite = sprite,
            posAbanico = new Vector2(t * separacion, alturaAbanico - Mathf.Abs(t) * caidaBordes),
            giroAbanico = -t * giroBordes,
            distanciaCentro = Mathf.Abs(t),
        };

        int indice = cartas.Count;
        var trigger = zona.gameObject.AddComponent<EventTrigger>();
        Agregar(trigger, EventTriggerType.PointerEnter, _ => Hover(indice, true));
        Agregar(trigger, EventTriggerType.PointerExit, _ => Hover(indice, false));
        Agregar(trigger, EventTriggerType.PointerClick, d =>
        {
            if (((PointerEventData)d).button == PointerEventData.InputButton.Left) Elegir(indice);
        });
        return carta;
    }

    private TMP_Text CrearAyuda()
    {
        var r = Hijo("Ayuda", raiz.transform, typeof(TextMeshProUGUI));
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
        r.pivot = new Vector2(0.5f, 0f);
        r.anchoredPosition = new Vector2(0f, 28f);
        r.sizeDelta = new Vector2(1200f, 40f);
        var t = r.GetComponent<TextMeshProUGUI>();
        t.fontSize = 24f;
        t.color = new Color(1f, 1f, 1f, 0.75f);
        t.alignment = TextAlignmentOptions.Bottom;
        t.raycastTarget = false;
        return t;
    }

    private static RectTransform Hijo(string nombre, Transform padre, params System.Type[] componentes)
    {
        var tipos = new System.Type[componentes.Length + 1];
        tipos[0] = typeof(RectTransform);
        componentes.CopyTo(tipos, 1);
        var go = new GameObject(nombre, tipos);
        go.transform.SetParent(padre, false);
        return (RectTransform)go.transform;
    }

    private static void Estirar(RectTransform r)
    {
        r.anchorMin = Vector2.zero;
        r.anchorMax = Vector2.one;
        r.offsetMin = r.offsetMax = Vector2.zero;
    }

    private static void Agregar(EventTrigger trigger, EventTriggerType tipo, UnityEngine.Events.UnityAction<BaseEventData> accion)
    {
        var entrada = new EventTrigger.Entry { eventID = tipo };
        entrada.callback.AddListener(accion);
        trigger.triggers.Add(entrada);
    }

    // Las escenas de juego ya traen uno (los botones del diálogo lo usan); esto es por si no.
    private static void AsegurarEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }
}
