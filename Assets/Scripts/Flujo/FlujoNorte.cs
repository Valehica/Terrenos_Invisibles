using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Pasos del Acto 2 en Norte (Isluga), según Docs/Fase0_Dialogos_y_Fichas.md, desde la llegada
/// hasta que Wara deja a Maite en el lugar de la postal (los géiseres):
///
///   Llegada → Conocer a Wara → Ir a la iglesia → Foto de la iglesia → Chaku →
///   Viaje (Maite le muestra la postal, fundido a negro, las dos aparecen en PuntoTransportar
///   y se esconde el pueblo) → Seguir a Wara a la queñoa → Foto de la queñoa →
///   Seguir a Wara a las vicuñas → Foto de una vicuña → Seguir a Wara a la postal → En la postal
///
/// El paso 8 (sacar la postal, el recuerdo y la salida a Centro) va después: cuando Maite le
/// habla a Wara en los géiseres se dispara <see cref="alLlegarALaPostal"/>.
///
/// Maite puede explorar en cualquier momento: Wara nunca empieza a hablar sola. Cuando tiene
/// algo que decir espera a que Maite le hable (F), y el objetivo en pantalla dice dónde está.
/// El único que parte solo es el chaku, al terminar su conversación.
///
/// Los textos son campos del Inspector con el guion como valor inicial. Si se cambia el guion
/// en el documento, hay que corregirlos acá (o quitar el componente y volver a correr el menú
/// Terrenos/Norte/Preparar flujo hasta la postal, que lo crea de nuevo).
/// </summary>
public class FlujoNorte : MonoBehaviour
{
    public enum Paso { Llegada, ConocerAWara, IrALaIglesia, FotoIglesia, Chaku, Viaje, SeguirALaQuenoa, FotoQuenoa, SeguirALasVicunas, FotoVicunas, SeguirALaPostal, EnLaPostal }

    [Header("Escena")]
    [SerializeField] private Interactable wara;
    [SerializeField] private NpcGuia guia;
    [SerializeField] private Transform jugador;
    [SerializeField] private MinigameBase chaku;
    [SerializeField] private RectTransform canvas;
    [Tooltip("Hijos en orden: el último es donde Wara se para a hablar.")]
    [SerializeField] private Transform rutaIglesia;
    [Tooltip("Donde aparece Wara después del viaje; Maite queda a su lado. Las tres rutas siguientes parten de aquí.")]
    [SerializeField] private Transform puntoTransportar;
    [SerializeField] private Transform rutaQuenoa;
    [SerializeField] private Transform rutaVicunas;
    [SerializeField] private Transform rutaPostal;
    [Tooltip("Donde termina Wara en los géiseres, después de los puntos de Ruta Postal.")]
    [SerializeField] private Transform puntoPostalNpc;
    [Tooltip("Se apaga en el viaje (el pueblo de Isluga y su escaneo).")]
    [SerializeField] private GameObject[] ocultarAlViajar;

    [Header("Reglas")]
    [Tooltip("Segundos antes del primer monólogo (lo que tarda el fundido de entrada).")]
    [SerializeField] private float esperaInicial = 2.2f;
    [SerializeField] private string idIglesia = "iglesia_isluga";
    [SerializeField] private string idQuenoa = "quenoa";
    [SerializeField] private string idVicuna = "vicuna";
    [Tooltip("Segundos que tarda en oscurecerse (y en aclararse) la pantalla en el viaje.")]
    [SerializeField] private float duracionFundido = 1f;
    [Tooltip("Segundos en negro, con Wara y Maite ya en el destino.")]
    [SerializeField] private float esperaEnNegro = 0.6f;

    [Header("Para probar (M salta al lugar de la postal, solo en el editor)")]
    [Tooltip("Paso con el que parte la escena. Wara y Maite se ubican donde corresponde.")]
    [SerializeField] private Paso empezarEn = Paso.Llegada;

    [Header("Postal")]
    [Tooltip("Se muestra en pantalla mientras Maite le enseña la postal a Wara (después del chaku). Vacío = no se muestra.")]
    [SerializeField] private Sprite postal;
    [SerializeField] private UnityEvent alLlegarALaPostal;

    [Header("Objetivos")]
    [SerializeField] private string objHablarConWara = "Habla con la joven del pueblo.";
    [SerializeField] private string objIrALaIglesia = "Acompaña a Wara a la iglesia.";
    [SerializeField] private string objFotoIglesia = "Fotografía la iglesia con tu teléfono (E).";
    [SerializeField] private string objHablarEnIglesia = "Habla con Wara frente a la iglesia.";
    [SerializeField] private string objVolverConWara = "Vuelve con Wara y háblale.";
    [SerializeField] private string objHablarTrasChaku = "Habla con Wara.";
    [SerializeField] private string objHablarTrasViaje = "Habla con Wara.";
    [SerializeField] private string objHablarEnQuenoa = "Habla con Wara junto a la queñoa.";
    [SerializeField] private string objHablarEnVicunas = "Habla con Wara.";
    [SerializeField] private string objHablarEnGeiseres = "Habla con Wara en los géiseres.";
    [SerializeField] private string objChaku = "Ayuda a la comunidad en el chaku.";
    [SerializeField] private string objReintentarChaku = "Habla con Wara para intentar el chaku otra vez.";
    [SerializeField] private string objSeguirAWara = "Sigue a Wara.";
    [SerializeField] private string objFotoQuenoa = "Fotografía la queñoa.";
    [SerializeField] private string objFotoVicunas = "Escanea una vicuña con tu teléfono.";
    [SerializeField] private string objSeguirALaPostal = "Sigue a Wara hasta el lugar de la postal.";

    [Header("Diálogos")]
    [SerializeField] private LineaDialogo[] llegada =
    {
        new LineaDialogo("Maite", "Isluga... Es igual a la postal. Mamá y papá estuvieron aquí, en este mismo silencio.", true),
        new LineaDialogo("Maite", "Hace frío, y el aire pesa. Casi cuatro mil metros de altura.", true),
    };

    [SerializeField] private LineaDialogo[] conocerAWara =
    {
        new LineaDialogo("Wara", "Kamisaraki. ¿Llegaste en el bus de la mañana? No viene mucha gente hasta acá."),
        new LineaDialogo("Maite", "Sí. Me llamo Maite. Vengo siguiendo un viaje que hicieron mis papás hace muchos años."),
        new LineaDialogo("Wara", "Yo soy Wara. En aymara quiere decir \"estrella\"."),
        new LineaDialogo("Wara", "Si quieres conocer Isluga, empecemos por lo más importante. Ven, te muestro la iglesia."),
    };

    [SerializeField] private LineaDialogo[] iglesia =
    {
        new LineaDialogo("Wara", "Esta es la iglesia de Isluga. Sus muros se blanquean con cal, y el campanario está aparte, separado del templo."),
        new LineaDialogo("Wara", "El pueblo pasa casi vacío durante el año. Las familias vuelven para las fiestas, y un fabriquero se queda cuidándola siempre."),
        new LineaDialogo("Maite", "Mis papás le sacaron una foto. Quiero tener la mía."),
        new LineaDialogo("Wara", "Entonces usa tu teléfono. Presiona <b>E</b> para sacarlo y mantén la iglesia al centro hasta que se registre. Con la rueda del mouse puedes acercarte."),
    };

    [SerializeField] private LineaDialogo[] recordatorioIglesia =
    {
        new LineaDialogo("Wara", "Saca el teléfono con <b>E</b> y apunta a la iglesia hasta que se registre."),
    };

    [SerializeField] private LineaDialogo[] chakuDialogo =
    {
        new LineaDialogo("Wara", "¡Te quedó bonita! Ahora ven, que hoy toca algo especial: el chaku."),
        new LineaDialogo("Maite", "¿Chaku?"),
        new LineaDialogo("Wara", "Es una tradición muy antigua de los Andes, de antes de los incas. La comunidad forma una cadena de personas y arrea a las vicuñas, sin correr ni gritar, hasta un corral."),
        new LineaDialogo("Wara", "Ahí se las esquila con cuidado y se las suelta de nuevo. A las crías y a las hembras preñadas no se las toca. Así la lana vuelve a crecer y la vicuña sigue libre."),
        new LineaDialogo("Wara", "Ven, ayúdanos a formar la cadena."),
    };

    [SerializeField] private LineaDialogo[] chakuAbandonado =
    {
        new LineaDialogo("Wara", "No pasa nada, las vicuñas son desconfiadas. ¿Lo intentamos otra vez?"),
    };

    [Tooltip("Después del chaku. Mientras dura se muestra la postal; al terminar viene el viaje.")]
    [SerializeField] private LineaDialogo[] postalTrasChaku =
    {
        new LineaDialogo("Wara", "¡Lo hiciste muy bien! Las vicuñas ni se asustaron."),
        new LineaDialogo("Wara", "Oye, Maite... me dijiste que venías siguiendo a tus papás. ¿Qué buscas exactamente?"),
        new LineaDialogo("Maite", "Tengo unas postales que ellos guardaron de este viaje. Mira, esta es la de aquí."),
        new LineaDialogo("Wara", "Mmm... ese vapor que sale de la tierra, con los cerros atrás... Son los géiseres. Mi abuelo me llevaba cuando era chica."),
        new LineaDialogo("Wara", "Yo sé dónde es. Ven, te llevo."),
    };

    [SerializeField] private LineaDialogo[] trasElViaje =
    {
        new LineaDialogo("Wara", "Desde aquí seguimos a pie. Sígueme, que en el camino hay cosas que quiero mostrarte."),
    };

    [SerializeField] private LineaDialogo[] quenoa =
    {
        new LineaDialogo("Wara", "Esta es una queñoa. Crece donde casi ningún otro árbol puede: es el árbol que vive a mayor altura en el mundo."),
        new LineaDialogo("Wara", "Fíjate en su corteza, se suelta en capas finitas como papel. Así se protege del frío. Sácale una foto, para que no se te olvide."),
    };

    [SerializeField] private LineaDialogo[] recordatorioQuenoa =
    {
        new LineaDialogo("Wara", "Apúntale con el teléfono hasta que se registre."),
    };

    [SerializeField] private LineaDialogo[] trasQuenoa =
    {
        new LineaDialogo("Wara", "¡Bien! Sigamos, que ya no queda tanto."),
    };

    [SerializeField] private LineaDialogo[] vicunas =
    {
        new LineaDialogo("Wara", "Mira, allá. Son vicuñas, como las del chaku, pero estas andan libres."),
        new LineaDialogo("Wara", "Acércate despacio, que se espantan fácil. Escanea una con tu teléfono."),
    };

    [SerializeField] private LineaDialogo[] recordatorioVicunas =
    {
        new LineaDialogo("Wara", "Apúntale a una vicuña con el teléfono hasta que se registre."),
    };

    [SerializeField] private LineaDialogo[] trasVicunas =
    {
        new LineaDialogo("Wara", "Son lindas, ¿cierto? Vamos, los géiseres están ahí nomás."),
    };

    [Tooltip("Lo que dice Wara si le hablas al llegar a los géiseres, mientras no exista el paso 8.")]
    [SerializeField] private LineaDialogo[] enLaPostal =
    {
        new LineaDialogo("Wara", "Aquí es."),
    };

    public Paso PasoActual => paso;

    private Paso paso;
    private bool chakuEnCurso;
    private float inicio;
    private bool empezado;

    // Lo que Wara tiene que decir la próxima vez que Maite le hable.
    private LineaDialogo[] pendiente;
    private Action alTerminarPendiente;
    private bool pendienteConPostal;

    private TMP_Text objetivoText;
    private CanvasGroup objetivoGrupo;
    private Image postalImage;
    private Image fundido;

    // ------------------------------------------------------------ Arranque

    private void OnEnable()
    {
        AnimalScanner.Fotografiado += AlFotografiar;
        if (chaku != null) chaku.Terminado += AlTerminarChaku;
        if (wara != null) wara.AlHablar = AlHablarConWara;
    }

    private void OnDisable()
    {
        AnimalScanner.Fotografiado -= AlFotografiar;
        if (chaku != null) chaku.Terminado -= AlTerminarChaku;
        if (wara != null && wara.AlHablar == AlHablarConWara) wara.AlHablar = null;
    }

    private void Start()
    {
        ArmarUI();
        inicio = Time.time;
        Objetivo("");
    }

    private void Update()
    {
        // Solo en el editor y en builds de desarrollo.
        if (Debug.isDebugBuild && Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame)
        {
            SaltarALaPostal();
            return;
        }

        if (!empezado)
        {
            if (Time.time - inicio < esperaInicial) return;
            empezado = true;
            Empezar();
        }
    }

    /// <summary>
    /// Para probar (M): corta lo que esté pasando (diálogo, caminata, chaku, viaje) y deja a Wara
    /// en <see cref="puntoPostalNpc"/> con Maite al lado, en el paso <see cref="Paso.EnLaPostal"/>.
    /// </summary>
    private void SaltarALaPostal()
    {
        if (chakuEnCurso && chaku is ChakuMinigameManager chakuNorte) chakuNorte.ReturnToExploration();
        chakuEnCurso = false;

        StopAllCoroutines(); // el viaje, si estaba a mitad del fundido
        if (fundido != null) fundido.gameObject.SetActive(false);
        var control = jugador != null ? jugador.GetComponent<PlayerController>() : null;
        if (control != null) control.enabled = true;

        if (DialogueManager.Instance != null) DialogueManager.Instance.CloseDialogue();
        MostrarPostal(false);
        pendiente = null;
        alTerminarPendiente = null;
        pendienteConPostal = false;

        empezado = true;
        UbicarPara(Paso.EnLaPostal);
        Entrar(Paso.EnLaPostal);
        Debug.Log("FlujoNorte: salto de prueba (M) al lugar de la postal.");
    }

    private void DecirPendiente()
    {
        var lineas = pendiente;
        var fin = alTerminarPendiente;
        bool conPostal = pendienteConPostal;
        pendiente = null;
        alTerminarPendiente = null;
        pendienteConPostal = false;
        Conversar(lineas, fin, conPostal);
    }

    private void Empezar()
    {
        if (empezarEn != Paso.Llegada) UbicarPara(empezarEn);
        Entrar(empezarEn);
    }

    // ------------------------------------------------------------ Pasos

    private void Entrar(Paso nuevo)
    {
        paso = nuevo;
        if (wara != null) wara.enabled = true;

        switch (nuevo)
        {
            case Paso.Llegada:
                Monologo(llegada, () => Entrar(Paso.ConocerAWara));
                break;

            case Paso.ConocerAWara:
                Objetivo(objHablarConWara);
                break;

            case Paso.IrALaIglesia:
                Objetivo(objIrALaIglesia);
                Caminar(rutaIglesia, () => Pendiente(iglesia, () => Entrar(Paso.FotoIglesia), objHablarEnIglesia));
                break;

            case Paso.FotoIglesia:
                Objetivo(objFotoIglesia);
                break;

            case Paso.Chaku:
                Objetivo(objChaku);
                if (chaku != null)
                {
                    chakuEnCurso = true;
                    chaku.StartMinigame();
                }
                break;

            case Paso.Viaje:
                Objetivo("");
                StartCoroutine(Viajar());
                break;

            case Paso.SeguirALaQuenoa:
                Objetivo(objSeguirAWara);
                Caminar(rutaQuenoa, () => Pendiente(quenoa, () => Entrar(Paso.FotoQuenoa), objHablarEnQuenoa));
                break;

            case Paso.FotoQuenoa:
                Objetivo(objFotoQuenoa);
                break;

            case Paso.SeguirALasVicunas:
                Objetivo(objSeguirAWara);
                Caminar(rutaVicunas, () => Pendiente(vicunas, () => Entrar(Paso.FotoVicunas), objHablarEnVicunas));
                break;

            case Paso.FotoVicunas:
                Objetivo(objFotoVicunas);
                break;

            case Paso.SeguirALaPostal:
                Objetivo(objSeguirALaPostal);
                Caminar(rutaPostal, () => Pendiente(enLaPostal, () => Entrar(Paso.EnLaPostal), objHablarEnGeiseres), puntoPostalNpc);
                break;

            case Paso.EnLaPostal:
                Objetivo("");
                Debug.Log("FlujoNorte: Maite llegó con Wara al lugar de la postal (falta el paso 8).");
                alLlegarALaPostal?.Invoke();
                break;
        }
    }

    private bool AlHablarConWara()
    {
        // Si Wara tenía algo que decir, lo dice ahora.
        if (pendiente != null)
        {
            DecirPendiente();
            return true;
        }

        switch (paso)
        {
            case Paso.ConocerAWara:
                Conversar(conocerAWara, () => Entrar(Paso.IrALaIglesia));
                break;
            case Paso.FotoIglesia:
                Conversar(recordatorioIglesia, null);
                break;
            case Paso.Chaku:
                if (!chakuEnCurso) Conversar(chakuAbandonado, () => Entrar(Paso.Chaku));
                break;
            case Paso.FotoQuenoa:
                Conversar(recordatorioQuenoa, null);
                break;
            case Paso.FotoVicunas:
                Conversar(recordatorioVicunas, null);
                break;
            case Paso.EnLaPostal:
                Conversar(enLaPostal, null);
                break;
        }
        return true; // mientras haya flujo, el texto fijo del Interactable no se usa
    }

    private void AlFotografiar(Scannable s)
    {
        if (s == null) return;
        if (paso == Paso.FotoIglesia && s.SpeciesId == idIglesia)
        {
            paso = Paso.Chaku; // ya no vuelve a contar otra foto mientras espera
            Pendiente(chakuDialogo, () => Entrar(Paso.Chaku), objVolverConWara);
        }
        else if (paso == Paso.FotoQuenoa && s.SpeciesId == idQuenoa)
        {
            paso = Paso.SeguirALasVicunas;
            Pendiente(trasQuenoa, () => Entrar(Paso.SeguirALasVicunas), objVolverConWara);
        }
        else if (paso == Paso.FotoVicunas && s.SpeciesId == idVicuna)
        {
            paso = Paso.SeguirALaPostal;
            Pendiente(trasVicunas, () => Entrar(Paso.SeguirALaPostal), objVolverConWara);
        }
    }

    private void AlTerminarChaku(bool completado)
    {
        chakuEnCurso = false;
        if (completado)
        {
            paso = Paso.Viaje;
            Pendiente(postalTrasChaku, () => Entrar(Paso.Viaje), objHablarTrasChaku, mostrarPostal: true);
        }
        else
        {
            // Al hablarle, Wara dice su línea y el chaku parte de nuevo (ver AlHablarConWara).
            Objetivo(objReintentarChaku);
        }
    }

    // ------------------------------------------------------------ Conversar y caminar

    private void Conversar(LineaDialogo[] lineas, Action alTerminar, bool mostrarPostal = false)
    {
        if (DialogueManager.Instance == null) { alTerminar?.Invoke(); return; }

        // Se miran antes de hablar.
        if (guia != null && jugador != null)
        {
            guia.Mirar(jugador.position);
            Vector3 d = guia.transform.position - jugador.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.01f) jugador.rotation = Quaternion.LookRotation(d, Vector3.up);
        }

        if (mostrarPostal) MostrarPostal(true);
        Action fin = () =>
        {
            if (mostrarPostal) MostrarPostal(false);
            alTerminar?.Invoke();
        };
        if (!DialogueManager.Instance.StartConversation(lineas, wara != null ? wara.transform : null, fin))
            Pendiente(lineas, alTerminar, null, mostrarPostal); // había otro diálogo abierto: se intenta apenas se cierre
    }

    private void Monologo(LineaDialogo[] lineas, Action alTerminar)
    {
        if (DialogueManager.Instance == null || !DialogueManager.Instance.StartConversation(lineas, null, alTerminar))
            alTerminar?.Invoke();
    }

    private void Pendiente(LineaDialogo[] lineas, Action alTerminar, string objetivoMientras = null, bool mostrarPostal = false)
    {
        pendiente = lineas;
        alTerminarPendiente = alTerminar;
        pendienteConPostal = mostrarPostal;
        if (objetivoMientras != null) Objetivo(objetivoMientras);
    }

    private void Caminar(Transform ruta, Action alLlegar, Transform destino = null)
    {
        if (guia == null) { alLlegar?.Invoke(); return; }
        if (wara != null) wara.enabled = false; // no se le habla mientras camina
        guia.Ir(ruta, jugador, () =>
        {
            if (wara != null) wara.enabled = true;
            alLlegar?.Invoke();
        }, destino);
    }

    /// <summary>
    /// Fundido a negro, Wara y Maite aparecen en <see cref="puntoTransportar"/>, se esconde el
    /// pueblo y se aclara. Después Wara espera que Maite le hable para seguir caminando.
    /// </summary>
    private IEnumerator Viajar()
    {
        if (wara != null) wara.enabled = false;
        var control = jugador != null ? jugador.GetComponent<PlayerController>() : null;
        if (control != null) control.enabled = false; // que Maite no camine a oscuras

        yield return Fundir(0f, 1f);
        OcultarPueblo();
        if (puntoTransportar != null) Ubicar(puntoTransportar.position);
        yield return new WaitForSeconds(esperaEnNegro);
        if (control != null) control.enabled = true;
        yield return Fundir(1f, 0f);

        if (wara != null) wara.enabled = true;
        Pendiente(trasElViaje, () => Entrar(Paso.SeguirALaQuenoa), objHablarTrasViaje);
    }

    private IEnumerator Fundir(float desde, float hasta)
    {
        if (fundido == null) yield break;
        fundido.gameObject.SetActive(true);
        for (float t = 0f; t < duracionFundido; t += Time.deltaTime)
        {
            fundido.color = new Color(0f, 0f, 0f, Mathf.Lerp(desde, hasta, t / duracionFundido));
            yield return null;
        }
        fundido.color = new Color(0f, 0f, 0f, hasta);
        if (hasta <= 0f) fundido.gameObject.SetActive(false);
    }

    private void OcultarPueblo()
    {
        if (ocultarAlViajar == null) return;
        foreach (var go in ocultarAlViajar)
            if (go != null) go.SetActive(false);
    }

    /// <summary>Para empezar a mitad del flujo: deja a Wara donde estaría y a Maite al lado.</summary>
    private void UbicarPara(Paso p)
    {
        if (p > Paso.Viaje) OcultarPueblo();
        Transform punto = p >= Paso.EnLaPostal ? (puntoPostalNpc != null ? puntoPostalNpc : Ultimo(rutaPostal))
                        : p >= Paso.FotoVicunas ? Ultimo(rutaVicunas)
                        : p >= Paso.FotoQuenoa ? Ultimo(rutaQuenoa)
                        : p >= Paso.SeguirALaQuenoa ? puntoTransportar
                        : p >= Paso.FotoIglesia ? Ultimo(rutaIglesia)
                        : null;
        if (punto != null) Ubicar(punto.position);
    }

    private static Transform Ultimo(Transform ruta) =>
        ruta != null && ruta.childCount > 0 ? ruta.GetChild(ruta.childCount - 1) : null;

    /// <summary>Deja a Wara parada en el punto y a Maite a su lado, mirándola.</summary>
    private void Ubicar(Vector3 punto)
    {
        if (guia != null) guia.Ubicar(punto);
        if (jugador == null) return;

        var cc = jugador.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        Vector3 lado = punto + new Vector3(2.5f, 0f, -2.5f);
        Terrain t = Terrain.activeTerrain;
        if (t != null) lado.y = t.SampleHeight(lado) + t.transform.position.y + 1f;
        jugador.position = lado;
        Vector3 d = punto - lado;
        d.y = 0f;
        if (d.sqrMagnitude > 0.01f) jugador.rotation = Quaternion.LookRotation(d, Vector3.up);
        if (cc != null) cc.enabled = true;
        if (guia != null) guia.Mirar(lado);
    }

    // ------------------------------------------------------------ UI

    private void ArmarUI()
    {
        if (canvas == null) return;

        // Objetivo: arriba a la izquierda, chico, sobre una franja oscura.
        var caja = new GameObject("Objetivo", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
        caja.transform.SetParent(canvas, false);
        var img = caja.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.45f);
        img.raycastTarget = false;
        objetivoGrupo = caja.GetComponent<CanvasGroup>();
        objetivoGrupo.blocksRaycasts = false;
        var r = (RectTransform)caja.transform;
        r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
        r.anchoredPosition = new Vector2(32f, -32f);
        r.sizeDelta = new Vector2(620f, 74f);

        var titulo = Texto(r, "Titulo", "OBJETIVO", 18f, new Color(1f, 0.82f, 0.5f), new Vector2(18f, -8f), new Vector2(580f, 24f));
        titulo.fontStyle = FontStyles.Bold;
        titulo.characterSpacing = 6f;
        objetivoText = Texto(r, "Texto", "", 26f, Color.white, new Vector2(18f, -32f), new Vector2(590f, 36f));

        // Postal: a la derecha, mientras Maite se la enseña a Wara.
        var p = new GameObject("Postal", typeof(RectTransform), typeof(Image));
        p.transform.SetParent(canvas, false);
        postalImage = p.GetComponent<Image>();
        postalImage.preserveAspect = true;
        postalImage.raycastTarget = false;
        var pr = postalImage.rectTransform;
        pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0.5f);
        pr.anchoredPosition = new Vector2(0f, 120f);
        pr.sizeDelta = new Vector2(720f, 480f);
        p.SetActive(false);

        // Que el panel de diálogo y el fundido de entrada queden por encima.
        caja.transform.SetSiblingIndex(0);
        p.transform.SetSiblingIndex(1);

        // Fundido del viaje: encima de todo, apagado mientras no se usa.
        var f = new GameObject("FundidoViaje", typeof(RectTransform), typeof(Image));
        f.transform.SetParent(canvas, false);
        fundido = f.GetComponent<Image>();
        fundido.color = Color.clear;
        fundido.raycastTarget = false;
        var fr = fundido.rectTransform;
        fr.anchorMin = Vector2.zero;
        fr.anchorMax = Vector2.one;
        fr.offsetMin = fr.offsetMax = Vector2.zero;
        f.transform.SetAsLastSibling();
        f.SetActive(false);
    }

    private TMP_Text Texto(RectTransform padre, string nombre, string texto, float tam, Color color, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(nombre, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(padre, false);
        var t = go.GetComponent<TextMeshProUGUI>();
        t.text = texto;
        t.fontSize = tam;
        t.color = color;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return t;
    }

    private void Objetivo(string texto)
    {
        if (objetivoText == null) return;
        objetivoText.text = texto;
        objetivoGrupo.gameObject.SetActive(!string.IsNullOrEmpty(texto));
    }

    private void MostrarPostal(bool visible)
    {
        if (postalImage == null) return;
        postalImage.sprite = postal;
        postalImage.gameObject.SetActive(visible && postal != null);
    }

    // ------------------------------------------------------------ Guías en el editor

    private void OnDrawGizmos()
    {
        DibujarRuta(rutaIglesia, new Color(1f, 0.85f, 0.3f));
        DibujarRuta(rutaQuenoa, new Color(0.5f, 1f, 0.5f));
        DibujarRuta(rutaVicunas, new Color(1f, 0.6f, 0.4f));
        DibujarRuta(rutaPostal, new Color(0.5f, 0.85f, 1f));
        Gizmos.color = Color.white;
        if (puntoTransportar != null)
            Gizmos.DrawWireSphere(puntoTransportar.position + Vector3.up * 0.3f, 0.8f);
        if (puntoPostalNpc != null)
        {
            Gizmos.DrawWireSphere(puntoPostalNpc.position + Vector3.up * 0.3f, 0.8f);
            Transform ultimo = Ultimo(rutaPostal);
            if (ultimo != null) Gizmos.DrawLine(ultimo.position + Vector3.up * 0.3f, puntoPostalNpc.position + Vector3.up * 0.3f);
        }
    }

    private static void DibujarRuta(Transform ruta, Color color)
    {
        if (ruta == null) return;
        Gizmos.color = color;
        for (int i = 0; i < ruta.childCount; i++)
        {
            Vector3 p = ruta.GetChild(i).position;
            Gizmos.DrawSphere(p + Vector3.up * 0.3f, i == ruta.childCount - 1 ? 0.6f : 0.3f);
            if (i > 0) Gizmos.DrawLine(ruta.GetChild(i - 1).position + Vector3.up * 0.3f, p + Vector3.up * 0.3f);
        }
    }
}
