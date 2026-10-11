using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Gaviotín de la costa de Centro. Es un ave activa que no se queda quieta:
/// alterna entre volar, posarse en la playa y pescar en picada, y si el jugador
/// se acerca se va volando rápido a otro posadero.
///
/// No usa NavMesh. El vuelo es un vector de velocidad que gira con un límite
/// (y se inclina en las curvas). El despegue y el aterrizaje duran lo mismo que
/// sus animaciones, que van en el lugar. Posado intercala Idle con Rascar_Panza,
/// Gorjear y giros cortos.
/// </summary>
public class GaviotinBehavior : MonoBehaviour
{
    private enum Estado { Posado, Despegue, Vuelo, Picada, Aterrizaje }
    private enum Plan { Pasear, Pescar, Posarse }

    [Header("Zona")]
    [Tooltip("Centro de la zona por la que anda. Vacío = donde aparece.")]
    [SerializeField] private Transform centroZona;
    [SerializeField] private float radioZona = 60f;
    [Tooltip("Lugares donde se puede posar. Si está vacío, los busca solo en la playa.")]
    [SerializeField] private Transform[] puntosDePosado;
    [SerializeField] private int posaderosAutomaticos = 40;
    [Tooltip("Un posadero automático tiene que tener agua a esta distancia o menos.")]
    [SerializeField] private float distanciaMaxAlAgua = 30f;
    [SerializeField] private float pendienteMaxima = 25f;
    [Tooltip("No elige un posadero nuevo a menos de esto de donde está.")]
    [SerializeField] private float distanciaMinimaEntrePosaderos = 15f;
    [Tooltip("Vacío = el primero que encuentre en la escena.")]
    [SerializeField] private MareaController marea;
    [Tooltip("Renderer del mar, para seguir el oleaje del shader. Vacío = el de la marea.")]
    [SerializeField] private Renderer mar;
    [SerializeField] private LayerMask capasSuelo = ~0;

    [Header("Posado")]
    [SerializeField] private Vector2 tiempoPosado = new Vector2(10f, 25f);
    [SerializeField] private Vector2 pausaEntreGestos = new Vector2(1.5f, 4f);
    [Range(0f, 1f)] [SerializeField] private float probRascar = 0.4f;
    [Range(0f, 1f)] [SerializeField] private float probGorjear = 0.35f;
    [Tooltip("Lo que sobra de las dos probabilidades es girar en el lugar.")]
    [SerializeField] private Vector2 anguloGiro = new Vector2(30f, 90f);

    [Header("Vuelo")]
    [SerializeField] private float velocidadCrucero = 6f;
    [SerializeField] private float velocidadHuida = 12f;
    [Tooltip("Grados por segundo.")]
    [SerializeField] private float giroMaximo = 100f;
    [Tooltip("Altura sobre el suelo o el agua.")]
    [SerializeField] private Vector2 alturaVuelo = new Vector2(5f, 12f);
    [SerializeField] private float alturaSegura = 1.5f;
    [SerializeField] private float inclinacionMaxima = 35f;
    [SerializeField] private float cabeceoMaximo = 35f;
    [SerializeField] private Vector2Int tramosDePaseo = new Vector2Int(1, 3);

    [Header("Pesca")]
    [SerializeField] private float alturaAcecho = 6f;
    [SerializeField] private float radioAcecho = 5f;
    [SerializeField] private Vector2 tiempoAcecho = new Vector2(1.5f, 4f);
    [SerializeField] private Vector2Int picadas = new Vector2Int(1, 3);
    [SerializeField] private float velocidadPicada = 10f;
    [Tooltip("Profundidad mínima del agua donde pesca.")]
    [SerializeField] private float profundidadMinima = 1f;
    [Tooltip("Prefab opcional que aparece donde toca el agua.")]
    [SerializeField] private GameObject efectoChapuzon;

    [Header("Jugador")]
    [SerializeField] private float distanciaAlerta = 5f;
    [SerializeField] private float distanciaHuida = 2.5f;
    [SerializeField] private float distanciaHuidaAgachado = 1.2f;
    [Tooltip("Al huir, busca un posadero al menos a esta distancia del jugador.")]
    [SerializeField] private float distanciaRefugio = 15f;
    [Tooltip("En vuelo solo huye si va más bajo que esto.")]
    [SerializeField] private float alturaHuidaEnVuelo = 3f;

    [Header("Animación")]
    [SerializeField] private float fundido = 0.25f;
    [Tooltip("Fracción del clip Despegue en que las patas dejan el suelo.")]
    [Range(0f, 1f)] [SerializeField] private float despegueSueltaSuelo = 0.3f;
    [Tooltip("Fracción del clip Aterrizaje en que las patas tocan el suelo.")]
    [Range(0f, 1f)] [SerializeField] private float aterrizajeToqueSuelo = 0.65f;
    [Tooltip("Velocidad del clip Despegue cuando huye.")]
    [SerializeField] private float velocidadDespegueHuida = 1.5f;

    [Header("Sonido (opcional)")]
    [SerializeField] private AudioClip[] graznidos;
    [SerializeField] private AudioClip sonidoChapuzon;

    private static readonly int HIdle = Animator.StringToHash("Idle");
    private static readonly int HRascar = Animator.StringToHash("Rascar_Panza");
    private static readonly int HGorjear = Animator.StringToHash("Gorjear");
    private static readonly int HPlanear = Animator.StringToHash("Planear");
    private static readonly int HDespegue = Animator.StringToHash("Despegue");
    private static readonly int HAterrizaje = Animator.StringToHash("Aterrizaje");

    private Animator animator;
    private AudioSource audioSource;
    private Transform player;
    private PlayerController playerController;
    private OleajeMuestreo oleaje;
    private Terrain terreno;
    private readonly Dictionary<string, float> largos = new Dictionary<string, float>();
    private readonly List<Vector3> posaderos = new List<Vector3>();
    private Vector3 centro;

    private Estado estado;
    private Plan plan;
    private bool huyendo;
    private float distanciaJugador = Mathf.Infinity;

    // Posado
    private float timerPosado;
    private float timerGesto;
    private bool enGesto;
    private bool alarmaDada;
    private float giroDesde, giroHasta, giroT, giroDuracion;

    // Vuelo
    private Vector3 velocidad;
    private Vector3 destino;
    private Vector3 posadero;
    private int tramosRestantes;
    private float alabeo;
    private float rumboAnterior;

    // Pesca
    private Vector3 puntoPesca;
    private bool acechando;
    private float timerAcecho;
    private int picadasRestantes;
    private Vector3 puntoImpacto;
    private bool saliendoDePicada;

    // Despegue y aterrizaje
    private float timerEstado;
    private float duracionClip;
    private float yawInicio, yawObjetivo;
    private Vector3 p0, m0, m1;
    private Quaternion rotInicio;

    public string EstadoActual => estado + " / " + plan + (huyendo ? " (huyendo)" : "");

    // Todos los gaviotines de la escena, para que dos no elijan el mismo posadero.
    private static readonly List<GaviotinBehavior> bandada = new List<GaviotinBehavior>();
    private const float SeparacionEnPosadero = 1.5f;

    private void OnEnable() => bandada.Add(this);
    private void OnDisable() => bandada.Remove(this);

    private bool Ocupado(Vector3 p)
    {
        foreach (var otro in bandada)
            if (otro != this && Plana(otro.posadero - p).sqrMagnitude < SeparacionEnPosadero * SeparacionEnPosadero)
                return true;
        return false;
    }

    private void Start()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = false;
            if (animator.runtimeAnimatorController != null)
                foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    largos[clip.name] = clip.length;
        }

        playerController = FindFirstObjectByType<PlayerController>();
        if (playerController != null) player = playerController.transform;

        if (marea == null) marea = FindFirstObjectByType<MareaController>();
        if (mar == null && marea != null) mar = marea.GetComponent<Renderer>();
        if (mar != null) oleaje = new OleajeMuestreo(mar);
        terreno = Terrain.activeTerrain;
        centro = centroZona != null ? centroZona.position : transform.position;

        if ((graznidos != null && graznidos.Length > 0) || sonidoChapuzon != null)
        {
            audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
            audioSource.minDistance = 2f;
            audioSource.maxDistance = 40f;
        }

        BuscarPosaderos();
        posadero = MasCercano(transform.position);
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        EntrarPosado();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        ActualizarJugador();

        switch (estado)
        {
            case Estado.Posado: ActualizarPosado(dt); break;
            case Estado.Despegue: ActualizarDespegue(dt); break;
            case Estado.Vuelo: ActualizarVuelo(dt); break;
            case Estado.Picada: ActualizarPicada(dt); break;
            case Estado.Aterrizaje: ActualizarAterrizaje(dt); break;
        }
    }

    // ───────────────────────── Jugador ─────────────────────────

    private void ActualizarJugador()
    {
        if (player == null) return;

        distanciaJugador = Vector3.Distance(transform.position, player.position);
        bool agachado = playerController != null && playerController.IsCrouching;
        float huida = agachado ? distanciaHuidaAgachado : distanciaHuida;

        switch (estado)
        {
            case Estado.Posado:
                if (distanciaJugador <= huida)
                {
                    Huir();
                }
                else if (distanciaJugador <= distanciaAlerta && !alarmaDada && !enGesto && giroDuracion <= 0f)
                {
                    // Primero avisa con un graznido; si el jugador sigue acercándose, se va.
                    alarmaDada = true;
                    HacerGesto(HGorjear, "Gorjear");
                }
                break;

            case Estado.Aterrizaje:
                if (player != null && Vector3.Distance(posadero, player.position) <= huida + 1f)
                    AbortarAterrizaje();
                break;

            case Estado.Vuelo:
            case Estado.Picada:
                if (!huyendo && distanciaJugador <= huida && AlturaSobrePiso(transform.position) < alturaHuidaEnVuelo)
                {
                    huyendo = true;
                    PlanPosarse(true);
                    if (estado == Estado.Picada) SalirDePicada(false);
                }
                break;
        }

        if (huyendo && estado == Estado.Vuelo && distanciaJugador > distanciaRefugio * 0.6f)
            huyendo = false;
    }

    private void Huir()
    {
        huyendo = true;
        PlanPosarse(true);

        Vector3 lejos = Plana(transform.position - player.position);
        if (lejos.sqrMagnitude < 0.0001f) lejos = transform.forward;
        // Se va hacia el lado opuesto, con algo de azar para que no sea una línea recta.
        float yaw = Mathf.Atan2(lejos.x, lejos.z) * Mathf.Rad2Deg + Random.Range(-30f, 30f);
        EntrarDespegue(velocidadDespegueHuida, yaw);
    }

    // ───────────────────────── Posado ─────────────────────────

    private void EntrarPosado()
    {
        estado = Estado.Posado;
        huyendo = false;
        alarmaDada = false;
        enGesto = false;
        giroDuracion = 0f;
        velocidad = Vector3.zero;
        alabeo = 0f;

        transform.position = posadero;
        transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        timerPosado = Random.Range(tiempoPosado.x, tiempoPosado.y);
        timerGesto = Random.Range(pausaEntreGestos.x, pausaEntreGestos.y);
        Reproducir(HIdle, 1f, fundido);
    }

    private void ActualizarPosado(float dt)
    {
        timerPosado -= dt;

        if (giroDuracion > 0f)
        {
            giroT += dt / giroDuracion;
            float yaw = Mathf.LerpAngle(giroDesde, giroHasta, Mathf.SmoothStep(0f, 1f, giroT));
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (giroT >= 1f) giroDuracion = 0f;
            return;
        }

        if (enGesto)
        {
            timerGesto -= dt;
            if (timerGesto <= 0f)
            {
                enGesto = false;
                Reproducir(HIdle, 1f, fundido);
                timerGesto = Random.Range(pausaEntreGestos.x, pausaEntreGestos.y);
            }
            return;
        }

        if (timerPosado <= 0f)
        {
            DecidirYDespegar();
            return;
        }

        timerGesto -= dt;
        if (timerGesto > 0f) return;

        float r = Random.value;
        if (r < probRascar) HacerGesto(HRascar, "Rascar_Panza");
        else if (r < probRascar + probGorjear) HacerGesto(HGorjear, "Gorjear");
        else Girar(Random.Range(anguloGiro.x, anguloGiro.y) * (Random.value < 0.5f ? -1f : 1f));
    }

    private void HacerGesto(int hash, string clip)
    {
        enGesto = true;
        Reproducir(hash, 1f, fundido);
        timerGesto = Mathf.Max(0.2f, Largo(clip) - fundido);
        if (hash == HGorjear) Sonar(graznidos);
    }

    private void Girar(float angulo)
    {
        giroDesde = transform.eulerAngles.y;
        giroHasta = giroDesde + angulo;
        giroT = 0f;
        giroDuracion = Mathf.Lerp(0.3f, 0.7f, Mathf.Abs(angulo) / 90f);
        timerGesto = Random.Range(pausaEntreGestos.x, pausaEntreGestos.y);
    }

    private void DecidirYDespegar()
    {
        float r = Random.value;
        if (r < 0.45f && PlanPescar()) { }
        else if (r < 0.8f) PlanPasear();
        else PlanPosarse(false);

        Vector3 hacia = Plana((plan == Plan.Posarse ? posadero : destino) - transform.position);
        // Con el jugador cerca no despega hacia él (se le tiraba encima a la cámara).
        if (player != null && distanciaJugador < distanciaAlerta * 2f)
        {
            Vector3 aJugador = Plana(player.position - transform.position).normalized;
            if (Vector3.Dot(hacia.normalized, aJugador) > 0f) hacia = -aJugador;
        }
        float yaw = hacia.sqrMagnitude > 0.01f ? Mathf.Atan2(hacia.x, hacia.z) * Mathf.Rad2Deg : transform.eulerAngles.y;
        EntrarDespegue(1f, yaw);
    }

    // ───────────────────────── Despegue ─────────────────────────

    private void EntrarDespegue(float velocidadClip, float yaw)
    {
        estado = Estado.Despegue;
        enGesto = false;
        giroDuracion = 0f;
        timerEstado = 0f;
        duracionClip = Largo("Despegue") / velocidadClip;
        yawInicio = transform.eulerAngles.y;
        yawObjetivo = yaw;
        velocidad = Vector3.zero;
        Reproducir(HDespegue, velocidadClip, 0.15f);
    }

    private void ActualizarDespegue(float dt)
    {
        timerEstado += dt;
        float t = Mathf.Clamp01(timerEstado / duracionClip);

        // Gira hacia el rumbo mientras se agacha para saltar.
        float giro = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, despegueSueltaSuelo + 0.15f, t));
        float yaw = Mathf.LerpAngle(yawInicio, yawObjetivo, giro);
        Vector3 adelante = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;

        float aire = Mathf.InverseLerp(despegueSueltaSuelo, 1f, t);
        float vFinal = (huyendo ? velocidadHuida : velocidadCrucero) * 0.7f;
        velocidad = adelante * (vFinal * aire) + Vector3.up * (2.5f * Mathf.SmoothStep(0f, 1f, aire * 2.5f));

        Vector3 pos = transform.position + velocidad * dt;
        if (aire > 0f) pos.y = Mathf.Max(pos.y, Piso(pos) + 0.05f);
        transform.position = pos;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        rumboAnterior = yaw;

        if (t >= 1f) EntrarVuelo();
    }

    // ───────────────────────── Vuelo ─────────────────────────

    private void EntrarVuelo()
    {
        estado = Estado.Vuelo;
        Reproducir(HPlanear, 1f, 0.2f);
    }

    private void ActualizarVuelo(float dt)
    {
        Vector3 pos = transform.position;
        Vector3 objetivo;
        float vObjetivo = huyendo ? velocidadHuida : velocidadCrucero;
        float margen = alturaSegura;

        switch (plan)
        {
            case Plan.Pasear:
                objetivo = destino;
                if (Plana(destino - pos).magnitude < 4f)
                {
                    if (--tramosRestantes > 0) destino = PuntoDePaseo();
                    else if (Random.value < 0.5f && PlanPescar()) { }
                    else PlanPosarse(false);
                }
                break;

            case Plan.Pescar:
                if (!acechando)
                {
                    objetivo = destino;
                    if (Plana(puntoPesca - pos).magnitude < radioAcecho + 1f)
                    {
                        acechando = true;
                        timerAcecho = Random.Range(tiempoAcecho.x, tiempoAcecho.y);
                    }
                }
                else
                {
                    // Da vueltas lentas sobre el cardumen mirando el agua.
                    vObjetivo = velocidadCrucero * 0.6f;
                    Vector3 radial = Plana(pos - puntoPesca);
                    if (radial.sqrMagnitude < 0.01f) radial = transform.right;
                    radial = Quaternion.Euler(0f, 35f, 0f) * radial.normalized * radioAcecho;
                    objetivo = puntoPesca + radial;
                    objetivo.y = AlturaAgua(objetivo) + alturaAcecho;

                    timerAcecho -= dt;
                    if (timerAcecho <= 0f)
                    {
                        if (!huyendo && EntrarPicada()) return;
                        timerAcecho = 0.5f;
                    }
                }
                break;

            default: // Posarse
                Vector3 hacia = posadero - pos;
                float dPlana = Plana(hacia).magnitude;
                float altura = posadero.y + Mathf.Clamp(dPlana * 0.35f, 0.8f, alturaVuelo.y);
                objetivo = new Vector3(posadero.x, altura, posadero.z);
                margen = Mathf.Lerp(0.3f, alturaSegura, Mathf.InverseLerp(3f, 18f, dPlana));
                if (!huyendo && dPlana < 20f)
                    vObjetivo = Mathf.Lerp(velocidadCrucero * 0.6f, velocidadCrucero, dPlana / 20f);

                float tToque = Largo("Aterrizaje") * aterrizajeToqueSuelo;
                float inicio = Mathf.Clamp(velocidad.magnitude * tToque * 0.6f, 2f, 10f);
                if (dPlana < inicio && pos.y - posadero.y < 4f &&
                    Vector3.Angle(Plana(velocidad), Plana(hacia)) < 40f)
                {
                    EntrarAterrizaje();
                    return;
                }
                break;
        }

        Volar(objetivo, vObjetivo, margen, dt);

        // Bate más cuando sube y planea más cuando baja.
        float batido = Mathf.Clamp(1f + velocidad.y * 0.15f, 0.6f, 1.5f) * (huyendo ? 1.25f : 1f);
        animator.speed = Mathf.Lerp(animator.speed, batido, 2f * dt);
    }

    private void Volar(Vector3 objetivo, float vObjetivo, float margen, float dt)
    {
        Vector3 pos = transform.position;
        Vector3 dir = velocidad.sqrMagnitude > 0.01f ? velocidad.normalized : transform.forward;

        // No bajar del piso, ni acá ni un poco más adelante.
        float piso = Mathf.Max(Piso(pos), Piso(pos + velocidad * 1.2f)) + margen;
        objetivo.y = Mathf.Max(objetivo.y, piso);

        Vector3 deseada = LimitarCabeceo((objetivo - pos).normalized, 40f, dir);
        if (pos.y < piso) deseada = LimitarCabeceo(deseada + Vector3.up, 40f, dir);

        dir = Vector3.RotateTowards(dir, deseada, giroMaximo * Mathf.Deg2Rad * dt, 0f);
        float rapidez = Mathf.MoveTowards(velocidad.magnitude, vObjetivo, 4f * dt);
        velocidad = dir * rapidez;

        pos += velocidad * dt;
        pos.y = Mathf.Max(pos.y, Piso(pos) + 0.2f);
        transform.position = pos;
        Orientar(dt, 0.6f, cabeceoMaximo);
    }

    // ───────────────────────── Pesca ─────────────────────────

    private bool EntrarPicada()
    {
        Vector3 pos = transform.position;
        Vector3 adelante = Plana(velocidad).sqrMagnitude > 0.01f ? Plana(velocidad).normalized : Plana(transform.forward).normalized;
        Vector3 impacto = pos + adelante * 2.5f;
        if (AlturaTerreno(impacto) > NivelMar - profundidadMinima * 0.5f) return false; // pasa sobre un bajo

        estado = Estado.Picada;
        saliendoDePicada = false;
        puntoImpacto = impacto;
        Reproducir(HPlanear, 0.25f, 0.2f); // casi sin batir, con las alas quietas
        return true;
    }

    private void ActualizarPicada(float dt)
    {
        Vector3 pos = transform.position;

        if (!saliendoDePicada)
        {
            Vector3 objetivo = puntoImpacto;
            objetivo.y = AlturaAgua(objetivo) - 0.3f;
            Vector3 dir = velocidad.sqrMagnitude > 0.01f ? velocidad.normalized : transform.forward;
            dir = Vector3.RotateTowards(dir, (objetivo - pos).normalized, 6f * dt, 0f);
            velocidad = dir * Mathf.MoveTowards(velocidad.magnitude, velocidadPicada, 12f * dt);
            pos += velocidad * dt;

            float agua = AlturaAgua(pos);
            if (pos.y <= agua)
            {
                pos.y = agua;
                transform.position = pos;
                Chapuzon(pos);
                SalirDePicada(true);
                return;
            }
        }
        else
        {
            timerEstado += dt;
            Vector3 adelante = Plana(velocidad).sqrMagnitude > 0.01f ? Plana(velocidad).normalized : Plana(transform.forward).normalized;
            Vector3 meta = adelante * (velocidadCrucero * 0.7f) + Vector3.up * 3f;
            velocidad = Vector3.MoveTowards(velocidad, meta, 8f * dt);
            pos += velocidad * dt;
            pos.y = Mathf.Max(pos.y, AlturaAgua(pos));

            if (pos.y - AlturaAgua(pos) > alturaAcecho * 0.7f || timerEstado > 3f)
            {
                estado = Estado.Vuelo;
                animator.speed = 1f;
                if (plan == Plan.Pescar && picadasRestantes > 0)
                {
                    acechando = true;
                    timerAcecho = Random.Range(tiempoAcecho.x, tiempoAcecho.y);
                }
                else if (plan == Plan.Pescar)
                {
                    if (Random.value < 0.6f) PlanPosarse(false);
                    else PlanPasear();
                }
            }
        }

        transform.position = pos;
        Orientar(dt, 1f, 75f);
    }

    private void SalirDePicada(bool tocoAgua)
    {
        saliendoDePicada = true;
        timerEstado = 0f;
        if (tocoAgua)
        {
            picadasRestantes--;
            Vector3 adelante = Plana(velocidad).sqrMagnitude > 0.01f ? Plana(velocidad).normalized : Plana(transform.forward).normalized;
            velocidad = adelante * 3f + Vector3.up * 5f;
        }
        Reproducir(HPlanear, 1.4f, 0.15f);
    }

    private void Chapuzon(Vector3 pos)
    {
        if (efectoChapuzon != null) Destroy(Instantiate(efectoChapuzon, pos, Quaternion.identity), 3f);
        if (sonidoChapuzon != null && audioSource != null) audioSource.PlayOneShot(sonidoChapuzon);
    }

    // ───────────────────────── Aterrizaje ─────────────────────────

    private void EntrarAterrizaje()
    {
        estado = Estado.Aterrizaje;
        timerEstado = 0f;
        duracionClip = Largo("Aterrizaje");
        float tToque = duracionClip * aterrizajeToqueSuelo;

        // Curva de Hermite: sale con la velocidad que trae y llega casi frenado.
        p0 = transform.position;
        Vector3 dirFinal = Plana(posadero - p0);
        if (dirFinal.sqrMagnitude < 0.01f) dirFinal = Plana(transform.forward);
        dirFinal.Normalize();
        m0 = velocidad * tToque;
        m1 = dirFinal * (0.3f * tToque);
        rotInicio = transform.rotation;
        yawObjetivo = Mathf.Atan2(dirFinal.x, dirFinal.z) * Mathf.Rad2Deg;

        Reproducir(HAterrizaje, 1f, 0.2f);
    }

    private void ActualizarAterrizaje(float dt)
    {
        timerEstado += dt;
        float tToque = duracionClip * aterrizajeToqueSuelo;
        float u = Mathf.Clamp01(timerEstado / tToque);

        Vector3 pos = Hermite(p0, m0, posadero, m1, u);
        if (u < 1f) pos.y = Mathf.Max(pos.y, Piso(pos) + 0.02f);
        transform.position = pos;
        transform.rotation = Quaternion.Slerp(rotInicio, Quaternion.Euler(0f, yawObjetivo, 0f), Mathf.SmoothStep(0f, 1f, u));

        if (timerEstado >= duracionClip - fundido) EntrarPosado();
    }

    private void AbortarAterrizaje()
    {
        huyendo = true;
        PlanPosarse(true);
        estado = Estado.Vuelo;
        Vector3 adelante = Plana(transform.forward).normalized;
        velocidad = adelante * 4f + Vector3.up * 3f;
        Reproducir(HPlanear, 1.3f, 0.15f);
    }

    // ───────────────────────── Planes ─────────────────────────

    private void PlanPasear()
    {
        plan = Plan.Pasear;
        tramosRestantes = Random.Range(tramosDePaseo.x, tramosDePaseo.y + 1);
        destino = PuntoDePaseo();
    }

    private bool PlanPescar()
    {
        for (int i = 0; i < 40; i++)
        {
            Vector2 r = Random.insideUnitCircle * radioZona;
            Vector3 p = centro + new Vector3(r.x, 0f, r.y);
            if (AlturaTerreno(p) > NivelMar - profundidadMinima) continue;

            plan = Plan.Pescar;
            acechando = false;
            picadasRestantes = Random.Range(picadas.x, picadas.y + 1);
            puntoPesca = new Vector3(p.x, NivelMar, p.z);
            destino = puntoPesca + Vector3.up * alturaAcecho;
            return true;
        }
        return false;
    }

    private void PlanPosarse(bool lejosDelJugador)
    {
        plan = Plan.Posarse;
        posadero = ElegirPosadero(lejosDelJugador);
    }

    private Vector3 PuntoDePaseo()
    {
        Vector2 r = Random.insideUnitCircle * radioZona;
        Vector3 p = centro + new Vector3(r.x, 0f, r.y);
        p.y = Piso(p) + Random.Range(alturaVuelo.x, alturaVuelo.y);
        return p;
    }

    private Vector3 ElegirPosadero(bool lejosDelJugador)
    {
        if (posaderos.Count == 0) return transform.position;

        float minJugador = lejosDelJugador ? distanciaRefugio : distanciaAlerta + 2f;
        var candidatos = new List<Vector3>();
        Vector3 masLejano = posaderos[0];
        float mejor = -1f;

        foreach (var p in posaderos)
        {
            // Un posadero muy cerca da un saltito despegue-aterrizaje que no se ve bien.
            if (Plana(p - transform.position).sqrMagnitude < distanciaMinimaEntrePosaderos * distanciaMinimaEntrePosaderos) continue;
            if (Ocupado(p)) continue;
            float dj = player != null ? Vector3.Distance(p, player.position) : Mathf.Infinity;
            if (dj > mejor) { mejor = dj; masLejano = p; }
            if (dj >= minJugador) candidatos.Add(p);
        }
        if (mejor < 0f) return posadero;
        return candidatos.Count > 0 ? candidatos[Random.Range(0, candidatos.Count)] : masLejano;
    }

    // ───────────────────────── Posaderos ─────────────────────────

    private void BuscarPosaderos()
    {
        posaderos.Clear();

        if (puntosDePosado != null && puntosDePosado.Length > 0)
        {
            foreach (var t in puntosDePosado)
            {
                if (t == null) continue;
                posaderos.Add(SueloEn(t.position, out Vector3 p, out _) ? p : t.position);
            }
            if (posaderos.Count > 0) return;
        }

        float pleamar = marea != null ? marea.nivelMedio + marea.amplitud : NivelMar + 1.5f;
        float minAltura = pleamar + 0.3f;
        for (int i = 0; i < posaderosAutomaticos * 40 && posaderos.Count < posaderosAutomaticos; i++)
        {
            Vector2 r = Random.insideUnitCircle * radioZona;
            if (!SueloEn(centro + new Vector3(r.x, 0f, r.y), out Vector3 p, out Vector3 n)) continue;
            if (p.y < minAltura) continue;
            if (Vector3.Angle(n, Vector3.up) > pendienteMaxima) continue;
            if (!AguaCerca(p)) continue;
            if (posaderos.Exists(q => (q - p).sqrMagnitude < 16f)) continue;
            posaderos.Add(p);
        }

        if (posaderos.Count == 0)
        {
            Debug.LogWarning($"{name}: no encontré posaderos en la playa; se posa donde aparece.", this);
            posaderos.Add(SueloEn(transform.position, out Vector3 p, out _) ? p : transform.position);
        }
    }

    private bool AguaCerca(Vector3 p)
    {
        for (float d = 10f; d <= distanciaMaxAlAgua + 0.01f; d += 10f)
            for (int k = 0; k < 8; k++)
            {
                Vector3 q = p + Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward * d;
                if (AlturaTerreno(q) < NivelMar) return true;
            }
        return false;
    }

    private Vector3 MasCercano(Vector3 a)
    {
        Vector3 mejor = a;
        float dMin = Mathf.Infinity;
        foreach (var p in posaderos)
        {
            if (Ocupado(p)) continue;
            float d = (p - a).sqrMagnitude;
            if (d < dMin) { dMin = d; mejor = p; }
        }
        return mejor;
    }

    // ───────────────────────── Ayudas ─────────────────────────

    private float NivelMar => marea != null ? marea.Nivel : (mar != null ? mar.transform.position.y : 0f);

    private float AlturaAgua(Vector3 p) => oleaje != null ? oleaje.HeightAt(p, 1) : NivelMar;

    private float AlturaTerreno(Vector3 p) =>
        terreno != null ? terreno.SampleHeight(p) + terreno.transform.position.y : NivelMar - 10f;

    private float Piso(Vector3 p) => Mathf.Max(AlturaTerreno(p), AlturaAgua(p));

    private float AlturaSobrePiso(Vector3 p) => p.y - Piso(p);

    private bool SueloEn(Vector3 p, out Vector3 punto, out Vector3 normal)
    {
        // RaycastAll no viene ordenado: se queda con el impacto más alto que no
        // sea el jugador ni el propio gaviotín.
        var rayo = new Ray(new Vector3(p.x, p.y + 200f, p.z), Vector3.down);
        float mejorY = float.NegativeInfinity;
        punto = p;
        normal = Vector3.up;
        foreach (var hit in Physics.RaycastAll(rayo, 400f, capasSuelo, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider is CharacterController || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.point.y <= mejorY) continue;
            mejorY = hit.point.y;
            punto = hit.point;
            normal = hit.normal;
        }
        return mejorY > float.NegativeInfinity;
    }

    private void Orientar(float dt, float factorCabeceo, float cabeceoTope)
    {
        Vector3 plana = Plana(velocidad);
        if (plana.sqrMagnitude < 0.0001f) return;

        float yaw = Mathf.Atan2(plana.x, plana.z) * Mathf.Rad2Deg;
        float cabeceo = Mathf.Clamp(-Mathf.Atan2(velocidad.y, plana.magnitude) * Mathf.Rad2Deg * factorCabeceo, -cabeceoTope, cabeceoTope);
        float giroPorSegundo = Mathf.DeltaAngle(rumboAnterior, yaw) / dt;
        rumboAnterior = yaw;

        // Girar a la derecha baja el ala derecha (Z negativo).
        float objetivo = Mathf.Clamp(-giroPorSegundo * 0.35f, -inclinacionMaxima, inclinacionMaxima);
        alabeo = Mathf.Lerp(alabeo, objetivo, 3f * dt);
        // Suavizado: al entrar y salir de la picada el cabeceo cambia de golpe.
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(cabeceo, yaw, alabeo), 1f - Mathf.Exp(-8f * dt));
    }

    private static Vector3 LimitarCabeceo(Vector3 d, float maxGrados, Vector3 respaldo)
    {
        Vector3 plana = Plana(d);
        float horizontal = plana.magnitude;
        if (horizontal < 0.001f)
        {
            plana = Plana(respaldo);
            if (plana.sqrMagnitude < 0.001f) plana = Vector3.forward;
            horizontal = 0f;
        }
        float cabeceo = Mathf.Clamp(Mathf.Atan2(d.y, horizontal) * Mathf.Rad2Deg, -maxGrados, maxGrados) * Mathf.Deg2Rad;
        return plana.normalized * Mathf.Cos(cabeceo) + Vector3.up * Mathf.Sin(cabeceo);
    }

    private static Vector3 Hermite(Vector3 a, Vector3 ma, Vector3 b, Vector3 mb, float u)
    {
        float u2 = u * u, u3 = u2 * u;
        return (2f * u3 - 3f * u2 + 1f) * a + (u3 - 2f * u2 + u) * ma + (-2f * u3 + 3f * u2) * b + (u3 - u2) * mb;
    }

    private static Vector3 Plana(Vector3 v) => new Vector3(v.x, 0f, v.z);

    private void Reproducir(int estadoAnimator, float velocidadClip, float transicion)
    {
        if (animator == null) return;
        animator.speed = velocidadClip;
        animator.CrossFadeInFixedTime(estadoAnimator, transicion);
    }

    private float Largo(string clip) => largos.TryGetValue(clip, out float l) ? l : 2f;

    private void Sonar(AudioClip[] clips)
    {
        if (audioSource == null || clips == null || clips.Length == 0) return;
        var c = clips[Random.Range(0, clips.Length)];
        if (c != null) audioSource.PlayOneShot(c);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 c = Application.isPlaying ? centro : (centroZona != null ? centroZona.position : transform.position);
        Gizmos.color = new Color(0.3f, 0.7f, 1f);
        Gizmos.DrawWireSphere(c, radioZona);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, distanciaAlerta);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, distanciaHuida);

        Gizmos.color = Color.green;
        foreach (var p in posaderos) Gizmos.DrawSphere(p, 0.3f);

        if (Application.isPlaying && estado != Estado.Posado)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(transform.position, plan == Plan.Posarse ? posadero : (plan == Plan.Pescar ? puntoPesca : destino));
        }
    }
}
