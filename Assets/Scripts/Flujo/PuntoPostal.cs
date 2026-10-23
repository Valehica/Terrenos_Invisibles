using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// El lugar desde donde se sacó una postal. Va en el punto de inicio (donde hay que pararse) y
/// apunta al punto final (hacia donde hay que mirar). Con la postal en mano, parado a menos de
/// <see cref="radio"/> del inicio y con la cámara mirando a menos de <see cref="radio"/> del
/// final, suena el aviso y se llena la barra de <see cref="PostalesUI"/>; si se sostiene
/// <see cref="duracion"/> segundos, suena el de completado y el punto queda encontrado.
/// Si el jugador se sale antes, la barra vuelve a cero.
/// </summary>
public class PuntoPostal : MonoBehaviour
{
    [SerializeField] private Transform final;
    [Tooltip("La postal que hay que tener en la mano. Vacío: sirve cualquiera.")]
    [SerializeField] private Sprite postal;
    [Tooltip("Metros de margen alrededor de cada punto.")]
    [SerializeField] private float radio = 1f;
    [SerializeField] private float duracion = 5f;

    [Header("Sonidos")]
    [SerializeField] private AudioClip sonidoEncontrar;
    [SerializeField] private AudioClip sonidoCompletado;
    [SerializeField, Range(0f, 1f)] private float volumen = 0.9f;

    [Header("Al completarse")]
    [SerializeField] private UnityEvent alCompletar;

    /// <summary>Lo escucha el flujo de la zona para seguir con la cinemática.</summary>
    public static event Action<PuntoPostal> Encontrado;

    public bool Completado { get; private set; }
    public Sprite Postal => postal;

    private PostalesUI postales;
    private Transform jugador;
    private AudioSource audioSource;
    private float progreso;

    private void Awake()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
    }

    private void Update()
    {
        if (Completado) return;
        if (postales == null) postales = FindAnyObjectByType<PostalesUI>();
        if (jugador == null)
        {
            var pc = FindAnyObjectByType<PlayerController>();
            if (pc != null) jugador = pc.transform;
        }

        if (!EnElLugar())
        {
            if (progreso > 0f) Reiniciar();
            return;
        }

        if (progreso == 0f && sonidoEncontrar != null)
            audioSource.PlayOneShot(sonidoEncontrar, volumen);

        progreso = Mathf.Min(1f, progreso + Time.deltaTime / Mathf.Max(0.01f, duracion));
        postales.MostrarProgreso(this, progreso);

        if (progreso >= 1f) Completar();
    }

    private bool EnElLugar()
    {
        if (postales == null || jugador == null || final == null) return false;

        Sprite enMano = postales.PostalEnMano;
        if (enMano == null || (postal != null && enMano != postal)) return false;

        // Parado: en el plano, para que no importe la altura del pivote del jugador.
        Vector3 d = jugador.position - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude > radio * radio) return false;

        // Mirando: el rayo de la cámara pasa a menos de un radio del punto final.
        Camera cam = Camera.main;
        if (cam == null) return false;
        Vector3 v = final.position - cam.transform.position;
        float adelante = Vector3.Dot(v, cam.transform.forward);
        if (adelante <= 0f) return false;
        return (v - cam.transform.forward * adelante).sqrMagnitude <= radio * radio;
    }

    private void Reiniciar()
    {
        progreso = 0f;
        audioSource.Stop();
        if (postales != null) postales.MostrarProgreso(this, -1f);
    }

    private void Completar()
    {
        Completado = true;
        if (sonidoCompletado != null) audioSource.PlayOneShot(sonidoCompletado, volumen);
        postales.MostrarProgreso(this, 1f, completado: true);
        Encontrado?.Invoke(this);
        alCompletar?.Invoke();
    }

    private void OnDisable()
    {
        if (progreso > 0f && !Completado) Reiniciar();
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Completado ? Color.green : new Color(1f, 0.8f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, radio);
        if (final == null) return;
        Gizmos.DrawWireSphere(final.position, radio);
        Gizmos.DrawLine(transform.position, final.position);
    }
}
