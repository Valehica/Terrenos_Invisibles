using System;
using UnityEngine;

/// <summary>
/// NPC que guía al jugador: camina por una ruta (los hijos de un Transform, en orden) pegado
/// al suelo del Terrain. Si el jugador se queda un poco atrás lo espera mirándolo; si se aleja
/// mucho (se fue a explorar), sigue sola y lo espera al final de la ruta.
///
/// Animación: si el Animator tiene un parámetro float "Speed", vale 1 mientras camina y 0
/// detenido. El NPC de Norte por ahora solo tiene Idle, así que se desliza; basta con agregarle
/// un estado Walk con ese parámetro al controlador para que camine de verdad.
/// </summary>
public class NpcGuia : MonoBehaviour
{
    [SerializeField] private float velocidad = 2.2f;
    [Tooltip("Si el jugador queda más lejos que esto, se detiene a esperarlo.")]
    [SerializeField] private float esperarSiLejos = 12f;
    [Tooltip("Retoma la marcha cuando el jugador se acerca a menos de esto.")]
    [SerializeField] private float retomarSiCerca = 7f;
    [Tooltip("Si el jugador se aleja más que esto (se fue a explorar), deja de esperarlo y sigue sola hasta el final de la ruta, donde lo espera.")]
    [SerializeField] private float seguirSolaSiMasLejos = 30f;
    [SerializeField] private float rapidezGiro = 6f;
    [SerializeField] private string parametroVelocidad = "Speed";

    public bool Caminando => puntos != null;

    private Animator animator;
    private bool tieneParametro;
    private float alturaSobreSuelo;
    private float velocidadAnim;

    private Transform[] puntos;
    private int siguiente;
    private Transform seguidor;
    private Action alLlegar;
    private bool esperando;
    private Vector3? mirarA;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
            foreach (var p in animator.parameters)
                if (p.name == parametroVelocidad && p.type == AnimatorControllerParameterType.Float)
                    tieneParametro = true;

        alturaSobreSuelo = transform.position.y - Suelo(transform.position);
    }

    /// <summary>
    /// Camina por los hijos de <paramref name="ruta"/> y, si se da, termina en <paramref name="destino"/>;
    /// al llegar al último punto llama a <paramref name="onArrive"/>.
    /// </summary>
    public void Ir(Transform ruta, Transform jugador, Action onArrive, Transform destino = null)
    {
        int n = (ruta != null ? ruta.childCount : 0) + (destino != null ? 1 : 0);
        if (n == 0)
        {
            onArrive?.Invoke();
            return;
        }
        puntos = new Transform[n];
        for (int i = 0; ruta != null && i < ruta.childCount; i++) puntos[i] = ruta.GetChild(i);
        if (destino != null) puntos[n - 1] = destino;
        siguiente = 0;
        seguidor = jugador;
        alLlegar = onArrive;
        esperando = false;
        mirarA = null;
    }

    /// <summary>Lo deja parado en un punto (para empezar el flujo a mitad de camino al probar).</summary>
    public void Ubicar(Vector3 posicion)
    {
        puntos = null;
        posicion.y = Suelo(posicion) + alturaSobreSuelo;
        transform.position = posicion;
    }

    /// <summary>Se gira de a poco hacia ese punto (por ejemplo, el jugador al conversar).</summary>
    public void Mirar(Vector3 punto) => mirarA = punto;

    private void Update()
    {
        float vel = 0f;
        Vector3 pos = transform.position;

        if (puntos != null)
        {
            if (seguidor != null && seguidor.gameObject.activeInHierarchy)
            {
                float d = Plano(seguidor.position - pos).magnitude;
                if (esperando && (d < retomarSiCerca || d > seguirSolaSiMasLejos)) esperando = false;
                else if (!esperando && d > esperarSiLejos && d <= seguirSolaSiMasLejos) esperando = true;
            }

            if (esperando)
            {
                Girar(seguidor.position - pos);
            }
            else
            {
                Vector3 delta = Plano(puntos[siguiente].position - pos);
                float paso = velocidad * Time.deltaTime;
                if (delta.magnitude <= paso + 0.05f)
                {
                    pos += delta;
                    siguiente++;
                    if (siguiente >= puntos.Length)
                    {
                        puntos = null;
                        Action fin = alLlegar;
                        alLlegar = null;
                        if (seguidor != null) mirarA = seguidor.position;
                        fin?.Invoke();
                    }
                }
                else
                {
                    pos += delta.normalized * paso;
                    Girar(delta);
                    vel = 1f;
                }
            }
        }
        else if (mirarA.HasValue)
        {
            Girar(mirarA.Value - pos);
        }

        pos.y = Suelo(pos) + alturaSobreSuelo;
        transform.position = pos;

        velocidadAnim = Mathf.MoveTowards(velocidadAnim, vel, Time.deltaTime * 4f);
        if (tieneParametro) animator.SetFloat(parametroVelocidad, velocidadAnim);
    }

    private void Girar(Vector3 direccion)
    {
        direccion = Plano(direccion);
        if (direccion.sqrMagnitude < 0.0001f) return;
        Quaternion objetivo = Quaternion.LookRotation(direccion, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, objetivo, 1f - Mathf.Exp(-rapidezGiro * Time.deltaTime));
    }

    private static Vector3 Plano(Vector3 v)
    {
        v.y = 0f;
        return v;
    }

    private static float Suelo(Vector3 p)
    {
        Terrain t = Terrain.activeTerrain;
        return t != null ? t.SampleHeight(p) + t.transform.position.y : p.y;
    }
}
