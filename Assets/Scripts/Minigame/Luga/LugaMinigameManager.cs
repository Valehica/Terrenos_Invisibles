using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Minijuego "Recolección de luga" (zona centro, Isla Mocha).
///
/// Flujo: el NPC plantea la pregunta -> panel de objetivo -> el jugador explora
/// la costa durante la marea baja y recolecta luga -> al juntar la cantidad
/// pedida vuelve al punto de inicio -> resultado.
///
/// El jugador y la cámara de exploración se siguen usando tal cual; el minijuego
/// solo agrega los puntos de recolección, la UI, el reloj de marea y el límite
/// de la zona.
/// </summary>
public class LugaMinigameManager : MinigameBase
{
    private enum State { Idle, Briefing, Collecting, Returning, Ended }

    public static bool IsRunning { get; private set; }

    [Header("Objetivo")]
    [SerializeField] private int goalAmount = 8;
    [Tooltip("Cuántas porciones puede cargar el jugador antes de ir a dejarlas.")]
    [SerializeField] private int carryLimit = 2;
    [Tooltip("Duración de la jornada, en segundos: el mar empieza a subir de inmediato.")]
    [SerializeField] private float roundSeconds = 180f;

    [Header("Zona, acopio y punto de inicio")]
    [SerializeField] private BoxCollider zone;
    [SerializeField] private Transform startPoint;
    [SerializeField] private float finishRadius = 4f;
    [Tooltip("Dónde se dejan las porciones recolectadas.")]
    [SerializeField] private Transform depositPoint;
    [SerializeField] private float depositRadius = 3.5f;

    [Header("Puntos de recolección")]
    [SerializeField] private LugaPatch lugaPrefab;
    [SerializeField] private LugaPatch[] decoyPrefabs;
    [SerializeField] private int lugaSpawnCount = 16;
    [SerializeField] private int decoySpawnCount = 14;
    [SerializeField] private float minSpacing = 3f;
    [Tooltip("Banda intermareal donde aparecen los puntos, en metros sobre el nivel del mar.")]
    [SerializeField] private float minSpawnHeight = 0.05f;
    [SerializeField] private float maxSpawnHeight = 2.2f;

    [Header("Interacción")]
    [SerializeField] private Transform player;
    [SerializeField] private float collectRadius = 2.2f;
    [Tooltip("Linterna del jugador: se prende con click izquierdo (el input lo maneja el propio script Flashlight).")]
    [SerializeField] private Flashlight flashlight;

    [Header("Marea")]
    [SerializeField] private Transform sea;
    [SerializeField] private float lowTideY = -1.4f;
    [Tooltip("Nivel base del mar al terminar la jornada: por encima de los puntos de la costa.")]
    [SerializeField] private float highTideY = 2.4f;
    // La playa de Centro tiene ~2.2% de pendiente: cada metro de altura son ~45 m
    // de arena. Con 0.8 m en 0.9 s el agua avanzaba ~40 m/s; con estos valores la
    // ola sube en ~4.5 s y corre 11-16 m, a 2.5-3.5 m/s: se escapa corriendo.
    [Tooltip("Cada cuántos segundos entra una oleada. Tiene que dejar ~1.2 s de calma despues de cada una para el aviso.")]
    [SerializeField] private float surgeInterval = 12f;
    [Tooltip("Segundos que dura la oleada, subida y bajada: la mitad es lo que tarda en llegar arriba.")]
    [SerializeField] private float surgeDuration = 9f;
    [Tooltip("Altura extra de la primera oleada y de la última: la marea sube cada vez más.")]
    [SerializeField] private float minSurgeHeight = 0.25f;
    [SerializeField] private float maxSurgeHeight = 0.35f;

    [Header("Sonido de oleaje")]
    [Tooltip("Sonido de cada oleada; si hay varios se elige uno al azar. Vacío = un rumor sintético de reemplazo.")]
    [SerializeField] private AudioClip[] surgeClips;
    [Tooltip("Segundos respecto del comienzo de la ola en que arranca el sonido. Para que el golpe del " +
             "clip coincida con la cresta (la mitad de surgeDuration): offset = surgeDuration/2 - segundo del golpe en el clip. " +
             "Puede ser negativo: el sonido arranca antes que la ola.")]
    [SerializeField] private float surgeSoundOffset = 0f;
    [Range(0f, 1f)] [SerializeField] private float surgeVolume = 0.8f;
    [Tooltip("Volumen relativo de la primera oleada: las siguientes suben hasta 1, como la altura.")]
    [Range(0f, 1f)] [SerializeField] private float firstSurgeVolume = 0.7f;

    [Header("Golpe de ola")]
    [SerializeField] private PlayerStun playerStun;
    [SerializeField] private float stunSeconds = 3f;
    [Tooltip("Profundidad del agua sobre los pies, en metros, que tumba al jugador.")]
    [SerializeField] private float waterHitMargin = 0.2f;

    [Header("Anochecer (Tenkoku)")]
    [Tooltip("Módulo de Tenkoku de la escena: la jornada avanza su hora hasta que se hace de noche.")]
    [SerializeField] private Tenkoku.Core.TenkokuModule tenkoku;
    [Tooltip("Hora del cielo al empezar la jornada.")]
    [SerializeField] private float startHour = 18f;
    [Tooltip("Hora del cielo al terminar el tiempo: ya de noche.")]
    [SerializeField] private float endHour = 21.5f;

    [Header("UI")]
    [SerializeField] private GameObject objectivePanel;
    [SerializeField] private TMP_Text objectiveText;
    [SerializeField] private GameObject hudRoot;
    [SerializeField] private TMP_Text counterText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text promptText;
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button returnButton;

    [Header("Textos")]
    [TextArea(4, 10)]
    [SerializeField] private string objectiveBody =
        "Jornada de recolección de luga.\n\n" +
        "La marea está baja y las algas quedaron expuestas en la orilla. Recorre el sector de la costa, " +
        "reconoce la luga entre las piedras y las otras algas, y recolecta {0} porciones antes de que suba el mar.\n\n" +
        "Tienes tres minutos, los que quedan antes de que vuelva a subir el mar y caiga la noche. " +
        "Cada pocos segundos entra una oleada, y cada una llega más arriba que la anterior: si te alcanza, " +
        "te tumba y pierdes unos segundos en el suelo. Al final el agua cubre toda la orilla.\n\n" +
        "Solo puedes cargar {1} porciones a la vez: déjalas en el acopio (el cubo café) y vuelve por más.\n" +
        "Cuando completes la cantidad, vuelve al punto de inicio para cerrar la jornada.\n\n" +
        "F: recolectar y dejar la carga     Click: linterna     Esc: abandonar\n" +
        "Enter: comenzar";

    private CharacterController playerBody;
    private State state = State.Idle;
    private int carried;
    private int deposited;
    private float timeLeft;
    private float seaOriginalY;
    private MareaController seaTide;
    private AudioSource surgeAudio;
    private AudioClip fallbackSurgeClip;
    private int nextSurgeSound;
    private int skyHour, skyMinute, skySecond;
    private bool skySaved;
    private bool surgeIncoming;
    private int stunCount;
    private readonly List<LugaPatch> spawned = new List<LugaPatch>();
    private LugaPatch focusedPatch;
    private string flashMessage;
    private float flashUntil;

    private void Awake()
    {
        IsRunning = false;
        if (returnButton != null) returnButton.onClick.AddListener(ReturnToExploration);
        if (sea != null)
        {
            seaOriginalY = sea.position.y;
            seaTide = sea.GetComponent<MareaController>();
        }

        SetUIVisible(false);
    }

    public override void StartMinigame()
    {
        if (state != State.Idle) return;

        if (player == null)
        {
            var found = GameObject.Find("Player");
            player = found != null ? found.transform : null;
        }

        if (playerStun == null && player != null)
            playerStun = player.GetComponent<PlayerStun>();

        if (player != null)
            playerBody = player.GetComponent<CharacterController>();

        if (tenkoku == null)
            tenkoku = FindAnyObjectByType<Tenkoku.Core.TenkokuModule>();

        if (tenkoku != null && !skySaved)
        {
            skyHour = tenkoku.currentHour;
            skyMinute = tenkoku.currentMinute;
            skySecond = tenkoku.currentSecond;
            skySaved = true;
        }

        carried = 0;
        deposited = 0;
        stunCount = 0;
        timeLeft = roundSeconds;
        state = State.Briefing;
        IsRunning = true;

        // La marea normal del mar tambien escribe su altura cada frame: sin
        // apagarla, las dos se pisan y gana la que corra ultima.
        if (seaTide != null) seaTide.enabled = false;
        nextSurgeSound = 0;

        SpawnPatches();
        UpdateTide(0f);
        UpdateSkyTime(0f);

        if (objectivePanel != null) objectivePanel.SetActive(true);
        if (objectiveText != null) objectiveText.text = string.Format(objectiveBody, goalAmount, carryLimit);
        if (hudRoot != null) hudRoot.SetActive(true);
        if (resultPanel != null) resultPanel.SetActive(false);
        UpdateHUD();
    }

    private void Update()
    {
        switch (state)
        {
            case State.Briefing:
                if (EnterPressed())
                {
                    if (objectivePanel != null) objectivePanel.SetActive(false);
                    state = State.Collecting;
                }
                break;

            case State.Collecting:
            case State.Returning:
                Tick();
                break;

            case State.Ended:
                if (EnterPressed()) ReturnToExploration();
                break;
        }
    }

    private void Tick()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            EndRound(false, "Abandonaste la jornada antes de completar la recolección.");
            return;
        }

        timeLeft -= Time.deltaTime;
        float progress = 1f - Mathf.Clamp01(timeLeft / Mathf.Max(1f, roundSeconds));

        UpdateTide(progress);
        UpdateSurgeSound(progress);
        UpdateSkyTime(progress);
        CheckWaterHit();

        if (timeLeft <= 0f)
        {
            EndRound(false,
                $"El mar cubrió la orilla con {deposited} de {goalAmount} porciones en el acopio.\n\n" +
                "La recolección solo es posible mientras el mar deja la orilla al descubierto.");
            return;
        }

        UpdateFocus();
        HandleCollectInput();

        if (state == State.Returning && startPoint != null && player != null)
        {
            Vector3 a = player.position, b = startPoint.position;
            a.y = b.y = 0f;
            if (Vector3.Distance(a, b) <= finishRadius)
            {
                EndRound(true,
                    $"Jornada completa: {deposited} porciones de luga en el acopio.\n\n" +
                    "En Isla Mocha la recolección de algas es una actividad de subsistencia de las comunidades " +
                    "lafkenche, y se organiza según el ciclo de las mareas: el mar marca la hora de trabajo.");
            }
        }

        UpdateHUD();
    }

    private void UpdateFocus()
    {
        LugaPatch nearest = null;
        float best = collectRadius * collectRadius;

        foreach (var patch in spawned)
        {
            if (patch == null || patch.Collected || !patch.gameObject.activeSelf) continue;
            if (player == null) break;

            float d = (patch.transform.position - player.position).sqrMagnitude;
            if (d < best)
            {
                best = d;
                nearest = patch;
            }
        }

        if (focusedPatch != nearest)
        {
            if (focusedPatch != null) focusedPatch.SetFocused(false);
            focusedPatch = nearest;
            if (focusedPatch != null) focusedPatch.SetFocused(true);
        }
    }

    /// <summary>
    /// F hace, en este orden: dejar la carga si estás en el acopio, revisar el
    /// punto de recolección que tengas a mano, o prender/apagar la linterna.
    /// </summary>
    private void HandleCollectInput()
    {
        if (PlayerStun.IsStunned) return;
        if (Keyboard.current == null || !Keyboard.current.fKey.wasPressedThisFrame) return;

        if (carried > 0 && AtDepositPoint())
        {
            deposited += carried;
            Flash($"Dejaste {carried} en el acopio: {deposited} de {goalAmount}.", 2.5f);
            carried = 0;

            if (deposited >= goalAmount && state == State.Collecting)
            {
                state = State.Returning;
                Flash("Ya está la cantidad pedida: vuelve al punto de inicio.", 4f);
            }
            return;
        }

        if (focusedPatch != null)
        {
            if (!focusedPatch.IsLuga)
            {
                Flash($"Eso es {focusedPatch.Label}, no luga.");
                return;
            }

            if (carried >= carryLimit)
            {
                Flash($"Ya llevas {carried}: ve a dejarlas al acopio.", 2.5f);
                return;
            }

            focusedPatch.Collect();
            focusedPatch = null;
            carried++;
            Flash(carried >= carryLimit
                ? $"Llevas {carried}: vuelve al acopio."
                : "Recolectaste luga.");
        }
    }

    private bool AtDepositPoint()
    {
        if (depositPoint == null || player == null) return false;
        Vector3 a = player.position, b = depositPoint.position;
        a.y = b.y = 0f;
        return Vector3.Distance(a, b) <= depositRadius;
    }

    private void SpawnPatches()
    {
        if (spawned.Count > 0)
        {
            foreach (var patch in spawned)
                if (patch != null) Destroy(patch.gameObject);
            spawned.Clear();
        }

        if (zone == null || lugaPrefab == null) return;

        var terrain = Terrain.activeTerrain;
        var positions = new List<Vector3>();
        Bounds b = ZoneBounds();

        int total = lugaSpawnCount + decoySpawnCount;
        int attempts = 0;
        while (positions.Count < total && attempts < total * 200)
        {
            attempts++;
            Vector3 p = new Vector3(
                Random.Range(b.min.x, b.max.x), 0f,
                Random.Range(b.min.z, b.max.z));

            float groundY = terrain != null
                ? terrain.SampleHeight(p) + terrain.transform.position.y
                : 0f;
            if (groundY < minSpawnHeight || groundY > maxSpawnHeight) continue;

            p.y = groundY;

            if (startPoint != null && Vector3.Distance(p, startPoint.position) < finishRadius + 3f) continue;

            bool tooClose = false;
            foreach (var other in positions)
                if ((other - p).sqrMagnitude < minSpacing * minSpacing) { tooClose = true; break; }
            if (tooClose) continue;

            positions.Add(p);
        }

        // Mezcla para que la luga y los distractores queden repartidos.
        for (int i = positions.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (positions[i], positions[j]) = (positions[j], positions[i]);
        }

        for (int i = 0; i < positions.Count; i++)
        {
            bool isLuga = i < lugaSpawnCount;
            LugaPatch prefab = lugaPrefab;

            if (!isLuga && decoyPrefabs != null && decoyPrefabs.Length > 0)
                prefab = decoyPrefabs[Random.Range(0, decoyPrefabs.Length)];

            if (prefab == null) continue;

            var instance = Instantiate(prefab, positions[i], Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform);
            instance.gameObject.SetActive(true);
            spawned.Add(instance);
        }
    }

    /// <summary>
    /// Nivel del mar: sube de forma continua durante la jornada y además entra
    /// una oleada cada <see cref="surgeInterval"/> segundos, cada vez más alta.
    /// </summary>
    private void UpdateTide(float progress)
    {
        if (sea == null) return;

        float elapsed = roundSeconds - timeLeft;
        float phase = surgeInterval > 0f ? Mathf.Repeat(elapsed, surgeInterval) : 0f;
        float pulse = phase < surgeDuration ? Mathf.Sin(Mathf.PI * phase / surgeDuration) : 0f;

        surgeIncoming = pulse <= 0f && phase > surgeInterval - 1.2f;

        float baseLevel = Mathf.Lerp(lowTideY, highTideY, progress);
        float surge = Mathf.Lerp(minSurgeHeight, maxSurgeHeight, progress) * pulse;

        Vector3 p = sea.position;
        p.y = baseLevel + surge;
        sea.position = p;
    }

    /// <summary>
    /// Un sonido por oleada. La ola k empieza en k * surgeInterval; el sonido va
    /// corrido surgeSoundOffset segundos, para alinear el golpe del clip con la cresta.
    /// </summary>
    private void UpdateSurgeSound(float progress)
    {
        if (surgeInterval <= 0f) return;

        float elapsed = roundSeconds - timeLeft;
        if (elapsed < nextSurgeSound * surgeInterval + surgeSoundOffset) return;
        nextSurgeSound++;

        AudioClip clip = null;
        if (surgeClips != null && surgeClips.Length > 0)
            clip = surgeClips[Random.Range(0, surgeClips.Length)];
        if (clip == null)
        {
            if (fallbackSurgeClip == null) fallbackSurgeClip = SurfClip(surgeDuration);
            clip = fallbackSurgeClip;
        }

        if (surgeAudio == null)
        {
            surgeAudio = gameObject.AddComponent<AudioSource>();
            surgeAudio.playOnAwake = false;
            surgeAudio.spatialBlend = 0f;
        }

        surgeAudio.pitch = Random.Range(0.94f, 1.06f);
        surgeAudio.PlayOneShot(clip, surgeVolume * Mathf.Lerp(firstSurgeVolume, 1f, progress));
    }

    /// <summary>
    /// Rumor de reemplazo mientras no haya un clip grabado: ruido filtrado que crece
    /// hasta la cresta (la mitad de la duración) y después se deshace en espuma.
    /// </summary>
    private static AudioClip SurfClip(float seconds)
    {
        const int rate = 44100;
        int n = Mathf.Max(1, Mathf.RoundToInt(rate * Mathf.Max(1f, seconds)));
        float peak = seconds * 0.5f;
        var data = new float[n];
        var rng = new System.Random(7);
        float lp = 0f, lp2 = 0f, max = 1e-5f;

        for (int i = 0; i < n; i++)
        {
            float t = (float)i / rate;
            float x = (float)rng.NextDouble() * 2f - 1f;

            // Cresta redondeada: sube suave y baja con pendiente cero en el pico
            // (exp(-u²/(1+u)) arranca como gaussiana y termina como exponencial),
            // asi la ola rompe sin golpe y se retira con una cola larga.
            float env;
            if (t < peak)
                env = Mathf.SmoothStep(0f, 1f, t / peak);
            else
            {
                float u = (t - peak) / 1.6f;
                env = Mathf.Exp(-u * u / (1f + u));
            }

            // El rugido es ruido grave que se abre un poco en la cresta.
            float a = Mathf.Lerp(0.012f, 0.06f, env);
            lp += a * (x - lp);
            lp2 += a * (lp - lp2);

            // La espuma: siseo que entra de a poco al romper y se va con la resaca.
            float fizzIn = Mathf.SmoothStep(0f, 1f, (t - peak * 0.7f) / (peak * 0.6f));
            float fizz = (x - lp) * 0.05f * fizzIn * Mathf.Exp(-Mathf.Max(0f, t - peak) * 0.45f);

            float v = lp2 * env + fizz;
            data[i] = v;
            max = Mathf.Max(max, Mathf.Abs(v));
        }

        // Fundido al final para que no haga click.
        int fade = Mathf.Min(n, rate / 2);
        for (int i = 0; i < n; i++)
        {
            data[i] *= 0.8f / max;
            if (i > n - fade) data[i] *= (float)(n - i) / fade;
        }

        var clip = AudioClip.Create("OleajeSintetico", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>
    /// Adelanta la hora del cielo de Tenkoku durante la jornada: atardecer al
    /// empezar y noche al terminar. Tenkoku recalcula sol, luna, luz y colores
    /// a partir de currentHour/Minute/Second en cada frame.
    /// </summary>
    private void UpdateSkyTime(float progress)
    {
        if (tenkoku == null) return;

        float hour = Mathf.Lerp(startHour, endHour, progress);
        int h = Mathf.FloorToInt(hour);
        float minutes = (hour - h) * 60f;

        tenkoku.currentHour = (h % 24 + 24) % 24;
        tenkoku.currentMinute = Mathf.Clamp(Mathf.FloorToInt(minutes), 0, 59);
        tenkoku.currentSecond = Mathf.Clamp(Mathf.FloorToInt((minutes - Mathf.Floor(minutes)) * 60f), 0, 59);
    }

    private void RestoreSkyTime()
    {
        if (tenkoku == null || !skySaved) return;

        tenkoku.currentHour = skyHour;
        tenkoku.currentMinute = skyMinute;
        tenkoku.currentSecond = skySecond;
    }

    /// <summary>
    /// Altura de los pies del jugador. El transform del Player está en el centro
    /// de su CharacterController (más de un metro sobre el suelo), así que hay que
    /// bajar hasta la base de la cápsula; si no, el agua nunca "lo alcanza".
    /// </summary>
    private float PlayerFeetY()
    {
        if (playerBody != null)
            return player.position.y + (playerBody.center.y - playerBody.height * 0.5f) * player.lossyScale.y;

        var terrain = Terrain.activeTerrain;
        if (terrain != null)
            return terrain.SampleHeight(player.position) + terrain.transform.position.y;

        return player.position.y;
    }

    /// <summary>Si el agua tapa los pies del jugador, lo tumba unos segundos.</summary>
    private void CheckWaterHit()
    {
        if (sea == null || player == null || playerStun == null) return;
        if (PlayerStun.IsStunned) return;

        if (sea.position.y > PlayerFeetY() + waterHitMargin)
        {
            playerStun.Stun(stunSeconds);
            stunCount++;

            if (focusedPatch != null) focusedPatch.SetFocused(false);
            focusedPatch = null;

            Flash("¡Te alcanzó la ola!", stunSeconds);
        }
    }

    private void UpdateHUD()
    {
        if (counterText != null)
            counterText.text = $"Acopio: {deposited} / {goalAmount}    Llevas: {carried} / {carryLimit}";

        if (timerText != null)
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt(timeLeft));
            timerText.text = $"Marea alta en {seconds / 60:0}:{seconds % 60:00}";
            timerText.color = timeLeft < 20f ? new Color(1f, 0.45f, 0.35f) : Color.white;
        }

        if (promptText == null) return;

        string message = null;

        if (PlayerStun.IsStunned)
            message = "¡Te alcanzó la ola! Espera a levantarte.";
        else if (Time.time < flashUntil)
            message = flashMessage;
        else if (surgeIncoming)
            message = "¡Viene una ola! Sube a terreno más alto.";
        else if (zone != null && player != null && !InsideZoneXZ(player.position))
            message = "Vuelve al sector de recolección.";
        else if (carried > 0 && AtDepositPoint())
            message = $"F: dejar {carried} en el acopio";
        else if (state == State.Returning)
            message = "Vuelve al punto de inicio para cerrar la jornada.";
        else if (focusedPatch != null && carried >= carryLimit)
            message = "Ya llevas la carga completa: ve al acopio.";
        else if (focusedPatch != null)
            message = $"F: revisar {focusedPatch.Label.ToLower()}";
        else if (flashlight != null && !flashlight.IsOn)
            message = "Click izquierdo: encender linterna";

        promptText.text = message ?? string.Empty;
        promptText.gameObject.SetActive(!string.IsNullOrEmpty(message));
    }

    private bool InsideZoneXZ(Vector3 position)
    {
        Bounds b = ZoneBounds();
        return position.x >= b.min.x && position.x <= b.max.x &&
               position.z >= b.min.z && position.z <= b.max.z;
    }

    /// <summary>
    /// Límites de la zona calculados desde el transform: Collider.bounds puede
    /// venir sin actualizar si la física todavía no sincronizó el collider.
    /// </summary>
    private Bounds ZoneBounds()
    {
        Vector3 center = zone.transform.TransformPoint(zone.center);
        Vector3 size = Vector3.Scale(zone.size, zone.transform.lossyScale);
        return new Bounds(center, size);
    }

    private void Flash(string message, float duration = 2f)
    {
        flashMessage = message;
        flashUntil = Time.time + duration;
    }

    private void EndRound(bool won, string message)
    {
        state = State.Ended;

        if (focusedPatch != null) focusedPatch.SetFocused(false);
        focusedPatch = null;

        if (playerStun != null) playerStun.ClearStun();

        if (hudRoot != null) hudRoot.SetActive(false);
        if (objectivePanel != null) objectivePanel.SetActive(false);
        if (resultPanel != null) resultPanel.SetActive(true);
        if (resultText != null)
        {
            string stunNote = stunCount > 0
                ? $"\n\nLas olas te alcanzaron {stunCount} {(stunCount == 1 ? "vez" : "veces")}."
                : string.Empty;
            resultText.text = (won ? "¡Recolección lograda!\n\n" : "Jornada incompleta.\n\n") + message + stunNote;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void ReturnToExploration()
    {
        state = State.Idle;
        IsRunning = false;

        foreach (var patch in spawned)
            if (patch != null) Destroy(patch.gameObject);
        spawned.Clear();

        if (sea != null)
        {
            Vector3 p = sea.position;
            p.y = seaOriginalY;
            sea.position = p;
        }

        if (seaTide != null) seaTide.enabled = true;

        if (playerStun != null) playerStun.ClearStun();
        if (flashlight != null) flashlight.SetOn(false);
        if (surgeAudio != null) surgeAudio.Stop();

        RestoreSkyTime();

        SetUIVisible(false);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void SetUIVisible(bool visible)
    {
        if (objectivePanel != null) objectivePanel.SetActive(visible);
        if (hudRoot != null) hudRoot.SetActive(visible);
        if (resultPanel != null) resultPanel.SetActive(visible);
    }

    private static bool EnterPressed()
    {
        return Keyboard.current != null &&
               (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame);
    }

    private void OnDisable()
    {
        IsRunning = false;
    }
}
