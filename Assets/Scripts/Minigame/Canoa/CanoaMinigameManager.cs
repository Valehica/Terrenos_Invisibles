using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Minijuego "Travesía por los canales Kawésqar" (zona sur).
///
/// Flujo: el NPC de Puerto Edén propone la travesía -> fundido -> panel con el
/// objetivo y los controles -> el jugador rema por la ruta hasta la isla del
/// Parque Nacional Bernardo O'Higgins evitando las rocas -> llegada (victoria)
/// o resistencia en 0 (derrota, con reinicio desde Puerto Edén).
///
/// No hay tiempo ni puntaje: lo único que se mide es la resistencia de la canoa.
/// Mientras dura se apaga el jugador de exploración y la Main Camera pasa a
/// seguir a la canoa (se conserva el pase de estilo y el Volume de la escena).
///
/// La ruta, las rocas y el faro de llegada los arma el menú
/// Terrenos Invisibles > Canoa > Generar travesía (Editor/GenerarTravesiaCanoa.cs).
/// La UI se construye por código al arrancar.
/// </summary>
public class CanoaMinigameManager : MinigameBase
{
    private enum State { Idle, Transition, Briefing, Navigating, Won, Lost }

    [Serializable]
    public struct Tramo
    {
        public string nombre;
        [Tooltip("Metros recorridos por la ruta donde empieza el tramo.")]
        public float desde;
    }

    public static bool IsRunning { get; private set; }

    /// <summary>Se dispara al llegar a la isla, para encadenar la narrativa.</summary>
    public static event Action TravesiaCompletada;

    [Header("Canoa")]
    [SerializeField] private CanoaController canoe;
    [SerializeField] private Transform startPoint;

    [Header("Ruta")]
    [Tooltip("Los hijos, en orden, son los puntos de la ruta desde Puerto Edén hasta la isla.")]
    [SerializeField] private Transform route;
    [SerializeField] private Transform destination;
    [SerializeField] private float arrivalRadius = 14f;
    [Tooltip("Medio ancho de la ruta donde no hay un valor por punto: la canoa no puede alejarse más de esto.")]
    [SerializeField] private float corridorHalfWidth = 22f;
    [Tooltip("Medio ancho permitido en cada punto de la ruta (mismo orden que sus hijos). Lo llena el generador: " +
             "angosto en los canales, para que no se pueda salir por entre las rocas.")]
    [SerializeField] private float[] routeHalfWidths;
    [SerializeField] private Tramo[] tramos =
    {
        new Tramo { nombre = "Salida desde Puerto Edén", desde = 0f },
        new Tramo { nombre = "Primer sector de rocas", desde = 110f },
        new Tramo { nombre = "Canal estrecho", desde = 290f },
        new Tramo { nombre = "Zona de navegación compleja", desde = 460f },
        new Tramo { nombre = "Llegada", desde = 650f },
    };

    [Header("Faro de llegada")]
    [SerializeField] private Renderer beaconRenderer;
    [SerializeField] private Light beaconLight;
    [SerializeField] private Color beaconColor = new Color(1f, 0.62f, 0.2f);
    [SerializeField] private Color beaconReachedColor = new Color(0.35f, 1f, 0.55f);

    [Header("Resistencia")]
    [SerializeField] private float lightDamage = 5f;
    [SerializeField] private float moderateDamage = 12f;
    [SerializeField] private float strongDamage = 22f;
    [Tooltip("Resistencia por segundo que se pierde remando pegado a una roca o a la costa.")]
    [SerializeField] private float scrapeDamagePerSecond = 2.5f;
    [SerializeField] private float lowResistance = 30f;

    [Header("Exploración (se apaga durante el minijuego)")]
    [SerializeField] private GameObject explorationPlayer;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private CanoaCamera canoeCamera;
    [Tooltip("Componentes de la Main Camera que se apagan mientras se rema (control de tercera persona, escáner).")]
    [SerializeField] private Behaviour[] explorationCameraBehaviours;
    [Tooltip("Modelo del jugador que se sube a la canoa. Si queda vacío se busca el hijo PlayerModel del Player.")]
    [SerializeField] private Transform playerModel;
    [Tooltip("Dónde va parado el jugador en la canoa. Si queda vacío se busca Asiento dentro de la canoa.")]
    [SerializeField] private Transform canoeSeat;
    [Tooltip("Bool del Animator del jugador que se enciende mientras va en la canoa (para la futura animación de sentarse). " +
             "Si el Animator no lo tiene, el jugador queda en Idle.")]
    [SerializeField] private string seatedAnimatorBool = "EnCanoa";
    [Tooltip("Si se asigna, al terminar la travesía el jugador aparece aquí (en la isla). Si no, vuelve a donde estaba.")]
    [SerializeField] private Transform playerArrivalPoint;

    [Header("Viento y corrientes")]
    [Tooltip("Si queda vacío se usa (o se agrega) el del mismo objeto.")]
    [SerializeField] private CanoaVientoCorriente clima;

    [Header("Sonidos (si quedan vacíos se generan unos simples)")]
    [SerializeField] private AudioClip impactClip;
    [SerializeField] private AudioClip arrivalClip;
    [SerializeField] private AudioClip warningClip;
    [SerializeField] private AudioClip failClip;

    [Header("Continuación")]
    [SerializeField] private UnityEvent onTravesiaCompletada;

    [Header("Textos")]
    [TextArea(4, 12)]
    [SerializeField] private string briefingBody =
        "<size=140%><b>Travesía por los canales</b></size>\n\n" +
        "Durante miles de años el pueblo Kawésqar recorrió en canoa los canales, fiordos e islas de este territorio. " +
        "La canoa era la casa: en ella viajaba la familia, se mantenía el fuego y se buscaba el alimento.\n\n" +
        "Rema desde Puerto Edén hasta la isla del Parque Nacional Bernardo O'Higgins, marcada con una luz. " +
        "No hay apuro: observa el canal, busca el paso entre las rocas y cuida la canoa. " +
        "Cada choque le quita resistencia; si llega a cero, la travesía termina.\n" +
        "El viento sopla del oeste y trae ráfagas, y en los pasos entre rocas hay corrientes: " +
        "mira las flechas arriba a la derecha y apunta la canoa contra ellas.\n\n" +
        "W / ↑  remar      S / ↓  frenar y retroceder      A D / ← →  girar\n" +
        "Mouse: mirar alrededor      Rueda: acercar o alejar la cámara\n\n" +
        "Enter: comenzar";
    [TextArea(3, 8)]
    [SerializeField] private string victoryBody =
        "<size=140%><b>Travesía completada</b></size>\n\n" +
        "Llegaste a la isla. Moverse por los canales exige conocer el mar: dónde se abre el paso, " +
        "dónde se esconden las rocas y cuándo conviene esperar. Ese conocimiento, transmitido entre generaciones, " +
        "es parte del patrimonio del pueblo Kawésqar.\n\n" +
        "Enter: continuar";
    [TextArea(3, 8)]
    [SerializeField] private string defeatBody =
        "<size=140%><b>La canoa no puede continuar</b></size>\n\n" +
        "Los golpes contra las rocas la dejaron inutilizada.\n\n" +
        "Enter: reiniciar desde Puerto Edén      Esc: salir";

    private State state = State.Idle;
    private float resistance = 100f;
    private readonly List<Vector3> points = new List<Vector3>();
    private readonly List<float> cumulative = new List<float>();
    private readonly List<float> widths = new List<float>();
    private Transform modelParent;
    private Vector3 modelLocalPos;
    private Quaternion modelLocalRot;
    private Vector3 modelLocalScale;
    private bool modelRootMotion;
    private bool modelInCanoe;
    private float routeLength;
    private int currentTramo = -1;
    private bool hasMoved;
    private float navigationStartTime;
    private float nextWarningTime;
    private float barShake;
    private float barFlash;
    private string flashMessage;
    private float flashUntil;
    private Vector3 playerSavedPos;
    private Quaternion playerSavedRot;
    private AudioSource audioSource;
    private Material beaconMaterial;

    // UI construida por código.
    private GameObject uiRoot;
    private Image fade;
    private GameObject hud;
    private RectTransform barFrame;
    private RectTransform barFill;
    private Image barFillImage;
    private TMP_Text barLabel;
    private TMP_Text messageText;
    private TMP_Text tramoText;
    private GameObject controlsHint;
    private RectTransform marker;
    private TMP_Text markerLabel;
    private GameObject panel;
    private TMP_Text panelText;
    private Vector2 barFrameBasePos;
    private float tramoShownAt = -10f;

    private void Awake()
    {
        IsRunning = false;

        if (mainCamera == null) mainCamera = Camera.main;
        if (canoeCamera != null) canoeCamera.enabled = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;

        if (impactClip == null) impactClip = ProceduralClips.Impact();
        if (arrivalClip == null) arrivalClip = ProceduralClips.Chime();
        if (warningClip == null) warningClip = ProceduralClips.Beep();
        if (failClip == null) failClip = ProceduralClips.Fail();

        if (beaconRenderer != null) beaconMaterial = beaconRenderer.material;
        SetBeaconColor(beaconColor);

        BuildRoute();
        BuildUI();
        SetUIVisible(false);

        if (clima == null) clima = GetComponent<CanoaVientoCorriente>();
        if (clima == null) clima = gameObject.AddComponent<CanoaVientoCorriente>();
        var sea = GameObject.Find("Sea");
        clima.Setup(route, TramoIndexAt, canoe != null ? canoe.transform : null, mainCamera,
                    sea != null ? sea.GetComponent<Renderer>() : null);
        clima.BuildHud(hud.transform);
        clima.Aviso += message => Flash(message, 2.5f);

        if (canoe != null)
        {
            canoe.InputEnabled = false;
            canoe.Impacted += OnCanoeImpact;
            canoe.Scraping += OnCanoeScraping;
            canoe.HitCorridor += OnCanoeHitCorridor;
            canoe.Corridor = ConstrainToRoute;
            canoe.Environment = clima.Sample;
            if (canoeSeat == null) canoeSeat = FindDeep(canoe.transform, "Asiento");
            PlaceCanoeAtStart();
        }
    }

    private void OnDestroy()
    {
        if (canoe != null)
        {
            canoe.Impacted -= OnCanoeImpact;
            canoe.Scraping -= OnCanoeScraping;
            canoe.HitCorridor -= OnCanoeHitCorridor;
        }
    }

    public override void StartMinigame()
    {
        if (state != State.Idle || canoe == null) return;
        StartCoroutine(EnterRoutine());
    }

    private IEnumerator EnterRoutine()
    {
        state = State.Transition;
        IsRunning = true;
        UIManager.Abrir(CapaUI.Minijuego, this);
        uiRoot.SetActive(true);
        hud.SetActive(false);
        panel.SetActive(false);

        yield return Fade(0f, 1f, 0.6f);

        if (explorationPlayer != null)
        {
            playerSavedPos = explorationPlayer.transform.position;
            playerSavedRot = explorationPlayer.transform.rotation;
            BoardPlayerModel();
            explorationPlayer.SetActive(false);
        }

        SetExplorationCamera(false);
        ResetRun();

        if (canoeCamera != null)
        {
            canoeCamera.SetTarget(canoe);
            canoeCamera.enabled = true;
            canoeCamera.SnapToTarget();
        }

        ShowPanel(briefingBody);
        hud.SetActive(false);

        yield return Fade(1f, 0f, 0.8f);
        state = State.Briefing;
    }

    private void Update()
    {
        switch (state)
        {
            case State.Briefing:
                if (EnterPressed()) BeginNavigation();
                break;

            case State.Navigating:
                Tick();
                break;

            case State.Won:
                if (EnterPressed()) StartCoroutine(ExitRoutine());
                break;

            case State.Lost:
                if (EnterPressed()) StartCoroutine(RestartRoutine());
                else if (EscPressed()) StartCoroutine(ExitRoutine());
                break;
        }

        UpdateHUD();
    }

    private void BeginNavigation()
    {
        panel.SetActive(false);
        hud.SetActive(true);
        controlsHint.SetActive(true);
        hasMoved = false;
        navigationStartTime = Time.time;
        currentTramo = -1;
        canoe.InputEnabled = true;
        clima.ResetRun();
        clima.SetActive(true);
        state = State.Navigating;
    }

    private void Tick()
    {
        if (EscPressed())
        {
            StartCoroutine(ExitRoutine());
            return;
        }

        Vector3 pos = canoe.transform.position;
        float along = ProjectOnRoute(pos, out Vector3 closest, out Vector3 tangent);

        if (!hasMoved && Vector3.Distance(pos, startPoint != null ? startPoint.position : pos) > 40f)
            hasMoved = true;
        if (controlsHint.activeSelf && (hasMoved && Time.time - navigationStartTime > 12f))
            controlsHint.SetActive(false);

        UpdateTramo(along);

        if (destination != null)
        {
            Vector3 d = destination.position - pos;
            d.y = 0f;
            if (d.magnitude <= arrivalRadius) Win();
        }
    }

    /// <summary>
    /// Límite duro de la ruta (lo llama la canoa en cada paso de física): fuera del
    /// ancho permitido se la devuelve al borde. Así las rocas no se pueden esquivar
    /// rodeándolas por mar abierto ni saliendo del canal por entre dos de ellas.
    /// </summary>
    private bool ConstrainToRoute(Vector3 position, out Vector3 clamped, out Vector3 outward)
    {
        clamped = position;
        outward = Vector3.zero;
        if (points.Count < 2) return false;

        ProjectOnRoute(position, out Vector3 closest, out _, out float halfWidth);
        Vector3 off = position - closest;
        off.y = 0f;
        float dist = off.magnitude;
        if (dist <= halfWidth || dist < 1e-4f) return false;

        outward = off / dist;
        clamped = closest + outward * halfWidth;
        clamped.y = position.y;
        return true;
    }

    private void OnCanoeHitCorridor()
    {
        if (state == State.Navigating && Time.time >= flashUntil)
            Flash("No te alejes de la ruta de los canales.", 1.2f);
    }

    /// <summary>
    /// Sube el PlayerModel a la canoa: se cuelga del asiento (así se mece con el
    /// casco) y queda en Idle. Se hace antes de apagar el Player para que el
    /// modelo siga activo.
    /// </summary>
    private void BoardPlayerModel()
    {
        if (playerModel == null && explorationPlayer != null)
            playerModel = explorationPlayer.transform.Find("PlayerModel");
        if (playerModel == null || canoeSeat == null || modelInCanoe) return;

        modelParent = playerModel.parent;
        modelLocalPos = playerModel.localPosition;
        modelLocalRot = playerModel.localRotation;
        modelLocalScale = playerModel.localScale;

        playerModel.SetParent(canoeSeat, false);
        playerModel.localPosition = Vector3.zero;
        playerModel.localRotation = Quaternion.identity;
        modelInCanoe = true;

        var animator = playerModel.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            modelRootMotion = animator.applyRootMotion;
            animator.applyRootMotion = false;
            SetAnimatorParam(animator, "Speed", 0f);
            SetAnimatorParam(animator, "IsGrounded", true);
            SetAnimatorParam(animator, "IsCrouching", false);
            SetAnimatorParam(animator, seatedAnimatorBool, true);
        }
    }

    private void UnboardPlayerModel()
    {
        if (!modelInCanoe || playerModel == null) return;

        var animator = playerModel.GetComponentInChildren<Animator>();
        if (animator != null)
        {
            animator.applyRootMotion = modelRootMotion;
            SetAnimatorParam(animator, seatedAnimatorBool, false);
        }

        playerModel.SetParent(modelParent, false);
        playerModel.localPosition = modelLocalPos;
        playerModel.localRotation = modelLocalRot;
        playerModel.localScale = modelLocalScale;
        modelInCanoe = false;
    }

    private static void SetAnimatorParam(Animator animator, string name, object value)
    {
        if (string.IsNullOrEmpty(name) || animator.runtimeAnimatorController == null) return;
        foreach (var p in animator.parameters)
        {
            if (p.name != name) continue;
            if (p.type == AnimatorControllerParameterType.Bool && value is bool b) animator.SetBool(name, b);
            else if (p.type == AnimatorControllerParameterType.Float && value is float f) animator.SetFloat(name, f);
            return;
        }
    }

    private static Transform FindDeep(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private int TramoIndexAt(float along)
    {
        int index = 0;
        if (tramos != null)
            for (int i = 0; i < tramos.Length; i++)
                if (along >= tramos[i].desde) index = i;
        return index;
    }

    private void UpdateTramo(float along)
    {
        if (tramos == null || tramos.Length == 0) return;

        int index = 0;
        for (int i = 0; i < tramos.Length; i++)
            if (along >= tramos[i].desde) index = i;

        // Solo se avanza: volver un poco atrás no repite el cartel.
        if (index <= currentTramo) return;
        currentTramo = index;
        tramoText.text = $"Tramo {index + 1}:  {tramos[index].nombre}"; // la fuente TMP no trae "·"
        tramoShownAt = Time.time;
    }

    private void OnCanoeImpact(CanoaController.Severidad severity, float impactSpeed)
    {
        if (state != State.Navigating) return;

        float damage = severity == CanoaController.Severidad.Fuerte ? strongDamage
            : severity == CanoaController.Severidad.Moderada ? moderateDamage
            : lightDamage;

        resistance = Mathf.Max(0f, resistance - damage);

        float volume = severity == CanoaController.Severidad.Fuerte ? 1f
            : severity == CanoaController.Severidad.Moderada ? 0.75f : 0.45f;
        audioSource.pitch = severity == CanoaController.Severidad.Fuerte ? 0.8f : UnityEngine.Random.Range(0.95f, 1.15f);
        audioSource.PlayOneShot(impactClip, volume);

        barShake = severity == CanoaController.Severidad.Fuerte ? 1f : severity == CanoaController.Severidad.Moderada ? 0.7f : 0.4f;
        barFlash = 1f;

        Flash(severity == CanoaController.Severidad.Fuerte ? "¡Golpe fuerte contra las rocas!"
            : severity == CanoaController.Severidad.Moderada ? "La canoa golpeó una roca."
            : "Rozaste una roca.", 1.6f);

        if (resistance <= 0f) Lose();
        else if (resistance <= lowResistance && nextWarningTime < Time.time + 0.5f)
            nextWarningTime = Time.time + 0.6f;
    }

    private void OnCanoeScraping(float dt)
    {
        if (state != State.Navigating) return;

        resistance = Mathf.Max(0f, resistance - scrapeDamagePerSecond * dt);
        barShake = Mathf.Max(barShake, 0.15f);
        if (Time.time >= flashUntil) Flash("La canoa raspa contra las rocas.", 0.6f);

        if (resistance <= 0f) Lose();
    }

    private void Win()
    {
        state = State.Won;
        canoe.InputEnabled = false;
        clima.SetActive(false);
        controlsHint.SetActive(false);

        SetBeaconColor(beaconReachedColor);
        audioSource.pitch = 1f;
        audioSource.PlayOneShot(arrivalClip, 0.9f);

        ShowPanel(victoryBody);
        hud.SetActive(false);

        TravesiaCompletada?.Invoke();
        onTravesiaCompletada?.Invoke();
    }

    private void Lose()
    {
        state = State.Lost;
        canoe.InputEnabled = false;
        clima.SetActive(false);
        canoe.SetSunk(true);
        controlsHint.SetActive(false);

        audioSource.pitch = 1f;
        audioSource.PlayOneShot(failClip, 0.8f);

        ShowPanel(defeatBody);
    }

    private IEnumerator RestartRoutine()
    {
        state = State.Transition;
        yield return Fade(0f, 1f, 0.5f);

        ResetRun();
        if (canoeCamera != null) canoeCamera.SnapToTarget();
        panel.SetActive(false);

        yield return Fade(1f, 0f, 0.6f);
        BeginNavigation();
    }

    private IEnumerator ExitRoutine()
    {
        bool completed = state == State.Won;
        state = State.Transition;
        canoe.InputEnabled = false;
        clima.SetActive(false);

        yield return Fade(0f, 1f, 0.6f);

        panel.SetActive(false);
        hud.SetActive(false);

        if (canoeCamera != null) canoeCamera.enabled = false;

        if (explorationPlayer != null)
        {
            Vector3 pos = playerSavedPos;
            Quaternion rot = playerSavedRot;
            if (completed && playerArrivalPoint != null)
            {
                pos = playerArrivalPoint.position;
                rot = playerArrivalPoint.rotation;
            }

            // El CharacterController pisa las teletransportaciones si está activo.
            var cc = explorationPlayer.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            explorationPlayer.transform.SetPositionAndRotation(pos, rot);
            if (cc != null) cc.enabled = true;
            UnboardPlayerModel();
            explorationPlayer.SetActive(true);
        }

        SetExplorationCamera(true);

        // Si no llegó, la canoa vuelve a su amarre para un próximo intento.
        if (!completed) PlaceCanoeAtStart();
        canoe.SetSunk(false);

        yield return Fade(1f, 0f, 0.8f);

        uiRoot.SetActive(false);
        IsRunning = false;
        UIManager.Cerrar(CapaUI.Minijuego);
        state = State.Idle;
    }

    private void ResetRun()
    {
        resistance = 100f;
        barShake = 0f;
        barFlash = 0f;
        flashUntil = 0f;
        currentTramo = -1;
        tramoShownAt = -10f;
        SetBeaconColor(beaconColor);
        canoe.SetSunk(false);
        PlaceCanoeAtStart();
    }

    private void PlaceCanoeAtStart()
    {
        if (canoe == null) return;

        Vector3 pos = startPoint != null ? startPoint.position : canoe.transform.position;
        pos.y = canoe.transform.position.y; // la altura del agua la fija el generador

        Vector3 dir = points.Count > 1 ? points[1] - points[0] : canoe.transform.forward;
        dir.y = 0f;
        Quaternion rot = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : canoe.transform.rotation;
        canoe.ResetTo(pos, rot);
    }

    private void SetExplorationCamera(bool on)
    {
        if (explorationCameraBehaviours == null) return;
        foreach (var b in explorationCameraBehaviours)
            if (b != null) b.enabled = on;
    }

    private void SetBeaconColor(Color c)
    {
        if (beaconMaterial != null)
        {
            beaconMaterial.SetColor("_BaseColor", c);
            beaconMaterial.SetColor("_EmissionColor", c * 3f);
        }
        if (beaconLight != null) beaconLight.color = c;
    }

    // ---------------------------------------------------------------- Ruta

    private void BuildRoute()
    {
        points.Clear();
        cumulative.Clear();
        widths.Clear();
        routeLength = 0f;
        if (route == null) return;

        foreach (Transform child in route)
        {
            Vector3 p = child.position;
            p.y = 0f;
            if (points.Count > 0) routeLength += Vector3.Distance(points[points.Count - 1], p);
            int i = points.Count;
            points.Add(p);
            cumulative.Add(routeLength);
            widths.Add(routeHalfWidths != null && i < routeHalfWidths.Length ? routeHalfWidths[i] : corridorHalfWidth);
        }
    }

    /// <summary>Metros recorridos por la ruta hasta el punto más cercano a <paramref name="pos"/>.</summary>
    private float ProjectOnRoute(Vector3 pos, out Vector3 closest, out Vector3 tangent)
        => ProjectOnRoute(pos, out closest, out tangent, out _);

    private float ProjectOnRoute(Vector3 pos, out Vector3 closest, out Vector3 tangent, out float halfWidth)
    {
        pos.y = 0f;
        closest = pos;
        tangent = Vector3.forward;
        halfWidth = corridorHalfWidth;
        if (points.Count < 2) return 0f;

        float best = float.MaxValue, along = 0f;
        for (int i = 0; i < points.Count - 1; i++)
        {
            Vector3 a = points[i], b = points[i + 1];
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(pos - a, ab) / ab.sqrMagnitude);
            Vector3 c = a + ab * t;
            float d = (pos - c).sqrMagnitude;
            if (d < best)
            {
                best = d;
                closest = c;
                tangent = ab.normalized;
                along = cumulative[i] + ab.magnitude * t;
                halfWidth = Mathf.Lerp(widths[i], widths[i + 1], t);
            }
        }
        return along;
    }

    // ---------------------------------------------------------------- UI

    private void UpdateHUD()
    {
        if (uiRoot == null || !hud.activeSelf) return;

        float r01 = resistance / 100f;
        barFill.anchorMax = new Vector2(Mathf.Lerp(barFill.anchorMax.x, r01, 1f - Mathf.Exp(-10f * Time.deltaTime)), 1f);

        Color ok = new Color(0.45f, 0.85f, 0.55f);
        Color mid = new Color(0.98f, 0.78f, 0.3f);
        Color bad = new Color(0.95f, 0.3f, 0.25f);
        Color c = r01 > 0.6f ? Color.Lerp(mid, ok, (r01 - 0.6f) / 0.4f) : Color.Lerp(bad, mid, Mathf.Clamp01((r01 - lowResistance / 100f) / (0.6f - lowResistance / 100f)));

        bool low = resistance <= lowResistance && state == State.Navigating;
        if (low)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 7f);
            c = Color.Lerp(bad, new Color(1f, 0.75f, 0.7f), pulse * 0.6f);
            if (Time.time >= nextWarningTime)
            {
                audioSource.pitch = 1f;
                audioSource.PlayOneShot(warningClip, 0.35f);
                nextWarningTime = Time.time + 3.5f;
            }
        }

        barFlash = Mathf.MoveTowards(barFlash, 0f, Time.deltaTime * 3f);
        barFillImage.color = Color.Lerp(c, Color.white, barFlash * 0.7f);
        barLabel.text = low ? $"Resistencia  {Mathf.CeilToInt(resistance)}%   ¡canoa muy dañada!" : $"Resistencia  {Mathf.CeilToInt(resistance)}%";
        barLabel.color = low ? new Color(1f, 0.6f, 0.55f) : Color.white;

        barShake = Mathf.MoveTowards(barShake, 0f, Time.deltaTime * 2.5f);
        Vector2 jitter = barShake > 0f ? new Vector2(Mathf.Sin(Time.time * 60f), Mathf.Cos(Time.time * 47f)) * 9f * barShake : Vector2.zero;
        barFrame.anchoredPosition = barFrameBasePos + jitter;

        // Cartel del tramo: aparece unos segundos al entrar en cada uno.
        float since = Time.time - tramoShownAt;
        float alpha = since < 0.5f ? since / 0.5f : since < 3.5f ? 1f : Mathf.Clamp01(1f - (since - 3.5f) / 1f);
        tramoText.alpha = alpha;

        string message = Time.time < flashUntil ? flashMessage : null;
        messageText.text = message ?? string.Empty;

        UpdateMarker();
    }

    /// <summary>Indicador del destino: sobre la isla si se ve, pegado al borde si no.</summary>
    private void UpdateMarker()
    {
        if (destination == null || mainCamera == null)
        {
            marker.gameObject.SetActive(false);
            return;
        }

        marker.gameObject.SetActive(state == State.Navigating);
        Vector3 target = destination.position + Vector3.up * 14f;
        Vector3 sp = mainCamera.WorldToScreenPoint(target);
        bool behind = sp.z < 0f;
        if (behind) sp = -sp;

        var canvasRect = (RectTransform)uiRoot.transform;
        Vector2 size = canvasRect.rect.size;
        Vector2 local = new Vector2(sp.x / Screen.width * size.x, sp.y / Screen.height * size.y) - size * 0.5f;

        Vector2 half = size * 0.5f - new Vector2(70f, 70f);
        bool offscreen = behind || Mathf.Abs(local.x) > half.x || Mathf.Abs(local.y) > half.y;
        if (offscreen)
        {
            if (behind) local.y = -Mathf.Abs(local.y) - 1f;
            float k = Mathf.Min(half.x / Mathf.Max(1f, Mathf.Abs(local.x)), half.y / Mathf.Max(1f, Mathf.Abs(local.y)));
            local *= k;
        }

        marker.anchoredPosition = local;
        float dist = Vector3.Distance(new Vector3(canoe.transform.position.x, 0f, canoe.transform.position.z),
                                      new Vector3(destination.position.x, 0f, destination.position.z));
        markerLabel.text = offscreen ? "Isla" : $"Isla (P. N. Bernardo O'Higgins)\n{Mathf.RoundToInt(dist)} m";
    }

    private void ShowPanel(string body)
    {
        panel.SetActive(true);
        panelText.text = body;
    }

    private void Flash(string message, float duration)
    {
        flashMessage = message;
        flashUntil = Time.time + duration;
    }

    private IEnumerator Fade(float from, float to, float duration)
    {
        fade.gameObject.SetActive(true);
        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / duration)));
            yield return null;
        }
        fade.color = new Color(0f, 0f, 0f, to);
        fade.gameObject.SetActive(to > 0f);
    }

    private void SetUIVisible(bool visible)
    {
        if (uiRoot != null) uiRoot.SetActive(visible);
    }

    private void BuildUI()
    {
        uiRoot = new GameObject("CanoaUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        uiRoot.transform.SetParent(transform, false);
        var canvas = uiRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 60;
        var scaler = uiRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        hud = NewRect("HUD", uiRoot.transform);
        Stretch((RectTransform)hud.transform);

        // Barra de resistencia (arriba a la izquierda).
        barFrame = NewImage("Resistencia", hud.transform, new Color(0f, 0f, 0f, 0.55f)).rectTransform;
        barFrame.anchorMin = barFrame.anchorMax = barFrame.pivot = new Vector2(0f, 1f);
        barFrame.sizeDelta = new Vector2(460f, 30f);
        barFrame.anchoredPosition = barFrameBasePos = new Vector2(48f, -78f);

        var fillBg = NewImage("Fondo", barFrame, new Color(1f, 1f, 1f, 0.08f)).rectTransform;
        Stretch(fillBg, 4f);
        barFillImage = NewImage("Relleno", fillBg, Color.green);
        barFill = barFillImage.rectTransform;
        barFill.anchorMin = Vector2.zero;
        barFill.anchorMax = Vector2.one;
        barFill.offsetMin = barFill.offsetMax = Vector2.zero;

        barLabel = NewText("Etiqueta", barFrame, 26f, TextAlignmentOptions.BottomLeft);
        var labelRect = barLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0f, 0f);
        labelRect.sizeDelta = new Vector2(200f, 40f);
        labelRect.anchoredPosition = new Vector2(0f, 4f);

        // Mensajes breves (choques, salirse de la ruta).
        messageText = NewText("Mensaje", hud.transform, 34f, TextAlignmentOptions.Center);
        var msgRect = messageText.rectTransform;
        msgRect.anchorMin = new Vector2(0.5f, 1f);
        msgRect.anchorMax = new Vector2(0.5f, 1f);
        msgRect.sizeDelta = new Vector2(1200f, 60f);
        msgRect.anchoredPosition = new Vector2(0f, -200f);

        tramoText = NewText("Tramo", hud.transform, 44f, TextAlignmentOptions.Center);
        tramoText.fontStyle = FontStyles.Bold;
        var tramoRect = tramoText.rectTransform;
        tramoRect.anchorMin = new Vector2(0.5f, 1f);
        tramoRect.anchorMax = new Vector2(0.5f, 1f);
        tramoRect.sizeDelta = new Vector2(1400f, 70f);
        tramoRect.anchoredPosition = new Vector2(0f, -110f);
        tramoText.alpha = 0f;

        // Controles básicos durante el primer tramo.
        var hint = NewImage("Controles", hud.transform, new Color(0f, 0f, 0f, 0.5f));
        controlsHint = hint.gameObject;
        var hintRect = hint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.sizeDelta = new Vector2(1000f, 64f);
        hintRect.anchoredPosition = new Vector2(0f, 60f);
        var hintText = NewText("Texto", hintRect, 28f, TextAlignmentOptions.Center);
        Stretch(hintText.rectTransform);
        hintText.text = "W / ↑  remar      S / ↓  frenar      A D / ← →  girar      Mouse  mirar      Esc  salir";

        // Indicador del destino.
        marker = NewRect("Destino", hud.transform).GetComponent<RectTransform>();
        marker.anchorMin = marker.anchorMax = new Vector2(0.5f, 0.5f);
        marker.sizeDelta = new Vector2(40f, 40f);
        var diamond = NewImage("Rombo", marker, new Color(1f, 0.7f, 0.3f, 0.95f)).rectTransform;
        diamond.sizeDelta = new Vector2(22f, 22f);
        diamond.localRotation = Quaternion.Euler(0f, 0f, 45f);
        markerLabel = NewText("Etiqueta", marker, 24f, TextAlignmentOptions.Top);
        var mlRect = markerLabel.rectTransform;
        mlRect.sizeDelta = new Vector2(420f, 70f);
        mlRect.anchoredPosition = new Vector2(0f, -52f);

        // Panel central (objetivo, victoria, derrota).
        var panelImage = NewImage("Panel", uiRoot.transform, new Color(0.04f, 0.06f, 0.08f, 0.82f));
        panel = panelImage.gameObject;
        var panelRect = panelImage.rectTransform;
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(1180f, 640f);
        panelText = NewText("Texto", panelRect, 32f, TextAlignmentOptions.Center);
        Stretch(panelText.rectTransform, 56f);
        panelText.textWrappingMode = TextWrappingModes.Normal;
        panel.SetActive(false);

        fade = NewImage("Fundido", uiRoot.transform, new Color(0f, 0f, 0f, 0f));
        Stretch(fade.rectTransform);
        fade.raycastTarget = false;
        fade.gameObject.SetActive(false);
    }

    private static GameObject NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    private static Image NewImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text NewText(string name, Transform parent, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.alignment = align;
        text.color = Color.white;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        return text;
    }

    private static void Stretch(RectTransform rect, float padding = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding, padding);
        rect.offsetMax = new Vector2(-padding, -padding);
    }

    private static bool EnterPressed()
    {
        return Keyboard.current != null &&
               (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
    }

    private static bool EscPressed()
    {
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
    }

    private void OnDisable()
    {
        IsRunning = false;
        UIManager.Cerrar(CapaUI.Minijuego, this);
    }

    /// <summary>
    /// Sonidos de respaldo generados por código, para que el minijuego tenga
    /// feedback aunque todavía no se hayan asignado clips de audio reales.
    /// </summary>
    private static class ProceduralClips
    {
        private const int Rate = 44100;

        public static AudioClip Impact()
        {
            // Golpe sordo: ruido filtrado con caída rápida más un tono grave (madera contra piedra).
            int n = (int)(Rate * 0.45f);
            var data = new float[n];
            var rng = new System.Random(7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (noise - lp) * 0.12f;
                float env = Mathf.Exp(-t * 14f);
                float thump = Mathf.Sin(2f * Mathf.PI * (95f - 40f * t) * t) * Mathf.Exp(-t * 9f);
                data[i] = (lp * 2.2f * env + thump * 0.8f) * 0.8f;
            }
            return Make("canoa_golpe", data);
        }

        public static AudioClip Chime()
        {
            int n = (int)(Rate * 1.6f);
            var data = new float[n];
            float[] freqs = { 523.25f, 659.25f, 783.99f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float s = 0f;
                for (int k = 0; k < freqs.Length; k++)
                {
                    float start = k * 0.16f;
                    if (t < start) continue;
                    float tt = t - start;
                    s += Mathf.Sin(2f * Mathf.PI * freqs[k] * tt) * Mathf.Exp(-tt * 2.6f) * Mathf.Min(1f, tt * 200f);
                }
                data[i] = s * 0.3f;
            }
            return Make("canoa_llegada", data);
        }

        public static AudioClip Beep()
        {
            int n = (int)(Rate * 0.5f);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float env = (t < 0.18f ? 1f : 0f) + (t > 0.26f && t < 0.44f ? 1f : 0f);
                data[i] = Mathf.Sin(2f * Mathf.PI * 440f * t) * env * 0.35f;
            }
            return Make("canoa_alerta", data);
        }

        public static AudioClip Fail()
        {
            int n = (int)(Rate * 1.2f);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float f = Mathf.Lerp(330f, 165f, t / 1.2f);
                data[i] = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-t * 2f) * 0.35f;
            }
            return Make("canoa_derrota", data);
        }

        private static AudioClip Make(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
