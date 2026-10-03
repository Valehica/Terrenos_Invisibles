using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Movimiento de la canoa del minijuego de los canales Kawésqar.
///
/// W/↑ rema hacia adelante (la velocidad sube de a poco mientras se mantiene),
/// S/↓ frena y, ya detenida, retrocede despacio; A/D o ←/→ giran. La canoa va
/// siempre al nivel del mar: el Rigidbody tiene congelada la altura y la
/// inclinación, y el cabeceo que se ve es solo del modelo (<see cref="visual"/>).
///
/// Los choques los mide aquí (fuerza del golpe contra la normal) y los avisa con
/// <see cref="Impacted"/>; la resistencia la lleva el manager.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CanoaController : MonoBehaviour
{
    public enum Severidad { Leve, Moderada, Fuerte }

    [Header("Remo")]
    [SerializeField] private float maxForwardSpeed = 7.5f;
    [Tooltip("Aceleración al empezar a remar; baja a medida que la canoa toma velocidad.")]
    [SerializeField] private float acceleration = 1.6f;
    [SerializeField] private float maxReverseSpeed = 2f;
    [SerializeField] private float brakeDeceleration = 4f;
    [Tooltip("Frenado natural del agua cuando no se rema.")]
    [SerializeField] private float waterDrag = 0.45f;

    [Header("Giro")]
    [Tooltip("Grados por segundo con la canoa detenida (girar remando de un lado).")]
    [SerializeField] private float turnRateStill = 55f;
    [Tooltip("Grados por segundo a velocidad máxima.")]
    [SerializeField] private float turnRateFast = 34f;
    [Tooltip("Qué tan rápido se amortigua la deriva lateral después de un giro.")]
    [SerializeField] private float lateralGrip = 2.1f;

    [Header("Choques (velocidad del golpe contra la roca, m/s)")]
    [SerializeField] private float minImpactSpeed = 0.5f;
    [SerializeField] private float moderateImpactSpeed = 2.2f;
    [SerializeField] private float strongImpactSpeed = 4.2f;
    [SerializeField] private float impactCooldown = 0.6f;
    [Tooltip("Parte de la velocidad que vuelve como rebote hacia atrás después de chocar.")]
    [SerializeField] private float impactBounce = 0.15f;

    [Header("Viento y corriente")]
    [Tooltip("Parte de la velocidad del viento que arrastra la canoa de costado (deriva).")]
    [SerializeField] private float windLeeway = 0.18f;
    [Tooltip("Grados por segundo con que un viento de 5 m/s de través gira la proa.")]
    [SerializeField] private float windTurnRate = 10f;

    [Header("Oleaje")]
    [Tooltip("Renderer del mar (Sea): la canoa sube y baja con sus olas de Gerstner.")]
    [SerializeField] private Renderer seaRenderer;

    [Header("Modelo")]
    [Tooltip("Hijo con la malla: se mece con el agua y se sacude con los golpes.")]
    [SerializeField] private Transform visual;
    [SerializeField] private float bobAmplitude = 0.05f;
    [SerializeField] private float rollAmplitude = 2.5f;

    [Tooltip("Velocidad desde la que rozar una roca va gastando la canoa.")]
    [SerializeField] private float scrapeMinSpeed = 0.6f;

    public event Action<Severidad, float> Impacted;
    /// <summary>La canoa va raspando contra algo; el argumento es el tiempo de roce de este paso de física.</summary>
    public event Action<float> Scraping;

    /// <summary>
    /// Límite de la ruta: recibe la posición y, si está fuera, devuelve true con el
    /// punto corregido y la normal hacia afuera.
    /// </summary>
    public delegate bool CorridorLimit(Vector3 position, out Vector3 clamped, out Vector3 outward);

    public CorridorLimit Corridor { get; set; }

    /// <summary>
    /// Agua y viento en un punto, en m/s y en coordenadas del mundo: la corriente
    /// mueve la canoa con el agua; el viento la deriva y le gira la proa.
    /// </summary>
    public delegate void EnvironmentSample(Vector3 position, out Vector3 waterFlow, out Vector3 wind);

    public EnvironmentSample Environment { get; set; }
    /// <summary>La canoa llegó al borde de la ruta y se la detuvo ahí.</summary>
    public event Action HitCorridor;

    public bool InputEnabled { get; set; }
    public float Speed => speed;
    public float MaxForwardSpeed => maxForwardSpeed;
    /// <summary>Altura del agua bajo la canoa (la del modelo, no la del Rigidbody).</summary>
    public float SurfaceY { get; private set; }

    private Rigidbody body;
    private float speed;
    private float lateral;
    private float lastImpactTime = -10f;
    private float shake;
    private float shakeSign = 1f;
    private float sinkAmount;
    private Vector3 externalPush;
    private float throttle;
    private float steer;
    private OleajeMuestreo oleaje;

    private void Awake()
    {
        SetupBody();
        if (seaRenderer == null)
        {
            var sea = GameObject.Find("Sea");
            if (sea != null) seaRenderer = sea.GetComponent<Renderer>();
        }
        oleaje = new OleajeMuestreo(seaRenderer);
        SurfaceY = transform.position.y;
    }

    // El manager puede llamar a ResetTo desde su Awake, antes que el de la canoa.
    private void SetupBody()
    {
        if (body != null) return;
        body = GetComponent<Rigidbody>();
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.constraints = RigidbodyConstraints.FreezePositionY |
                           RigidbodyConstraints.FreezeRotationX |
                           RigidbodyConstraints.FreezeRotationZ;
        body.linearDamping = 0f;
        body.angularDamping = 6f;
    }

    /// <summary>Deja la canoa quieta en una posición y rumbo, sin rastros del intento anterior.</summary>
    public void ResetTo(Vector3 position, Quaternion rotation)
    {
        SetupBody();
        speed = 0f;
        lateral = 0f;
        shake = 0f;
        sinkAmount = 0f;
        externalPush = Vector3.zero;

        body.position = position;
        body.rotation = rotation;
        transform.SetPositionAndRotation(position, rotation);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    /// <summary>Hunde un poco la canoa y la ladea: la embarcación quedó inutilizada.</summary>
    public void SetSunk(bool sunk) => sinkAmount = sunk ? 1f : 0f;

    /// <summary>Empuje suave hacia la ruta cuando el jugador se aleja del canal.</summary>
    public void Push(Vector3 velocity) => externalPush = velocity;

    private void Update()
    {
        throttle = 0f;
        steer = 0f;

        var kb = Keyboard.current;
        if (InputEnabled && kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
        }

        AnimateVisual();
    }

    private void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        if (throttle > 0f)
        {
            // Cada palada rinde menos cuanto más rápido va: la velocidad crece
            // progresivamente y se asienta cerca del máximo.
            float headroom = Mathf.Clamp01(1f - speed / maxForwardSpeed);
            speed += acceleration * (0.35f + 0.65f * headroom) * dt;
        }
        else if (throttle < 0f)
        {
            speed -= (speed > 0f ? brakeDeceleration : acceleration * 0.6f) * dt;
        }
        else
        {
            speed = Mathf.MoveTowards(speed, 0f, waterDrag * dt * (0.6f + Mathf.Abs(speed) * 0.25f));
        }

        speed = Mathf.Clamp(speed, -maxReverseSpeed, maxForwardSpeed);

        float speedFactor = Mathf.Clamp01(Mathf.Abs(speed) / maxForwardSpeed);
        float turnRate = Mathf.Lerp(turnRateStill, turnRateFast, speedFactor);
        float turnSign = speed < -0.1f ? -1f : 1f;
        float yawDelta = steer * turnRate * turnSign * dt;

        Vector3 flow = Vector3.zero, wind = Vector3.zero;
        if (Environment != null) Environment(body.position, out flow, out wind);
        flow.y = 0f;
        wind.y = 0f;

        // El viento empuja la proa hacia sotavento: más cuanto más de través sople.
        // Hay que corregir con A/D para mantener el rumbo.
        if (wind.sqrMagnitude > 0.01f)
        {
            Vector3 fwdNow = body.rotation * Vector3.forward;
            float angle = Vector3.SignedAngle(fwdNow, wind, Vector3.up);
            yawDelta += Mathf.Sin(angle * Mathf.Deg2Rad) * windTurnRate * (wind.magnitude / 5f) * dt;
        }

        if (Mathf.Abs(yawDelta) > 0f)
        {
            body.MoveRotation(body.rotation * Quaternion.Euler(0f, yawDelta, 0f));
            // Al girar rápido la canoa sigue un poco con la inercia de antes.
            lateral -= Mathf.Deg2Rad * yawDelta * speed * 0.4f;
        }

        lateral = Mathf.MoveTowards(lateral, 0f, lateralGrip * dt * (1f + Mathf.Abs(lateral)));

        Vector3 forward = body.rotation * Vector3.forward;
        Vector3 right = body.rotation * Vector3.right;
        Vector3 v = forward * speed + right * lateral + externalPush + flow + wind * windLeeway;
        v.y = 0f;

        // Muro invisible de la ruta: se devuelve la canoa al borde y se anula la
        // parte de la velocidad que la sacaba; lo paralelo al borde se conserva, así
        // que se desliza por él en vez de quedar pegada.
        if (Corridor != null && Corridor(body.position, out Vector3 clamped, out Vector3 outward))
        {
            clamped.y = body.position.y;
            body.position = clamped;

            float outV = Vector3.Dot(v, outward);
            if (outV > 0f) v -= outward * outV;
            float outP = Vector3.Dot(externalPush, outward);
            if (outP > 0f) externalPush -= outward * outP;

            Vector3 own = v - externalPush - flow - wind * windLeeway;
            speed = Vector3.Dot(own, forward);
            lateral = Vector3.Dot(own, right);
            HitCorridor?.Invoke();
        }

        body.linearVelocity = v;

        externalPush = Vector3.MoveTowards(externalPush, Vector3.zero, 4f * dt);
    }

    private void OnCollisionEnter(Collision collision) => HandleContact(collision);

    private void OnCollisionStay(Collision collision)
    {
        // Avanzar pegado a la roca frena y va gastando la canoa.
        if (Mathf.Abs(speed) > scrapeMinSpeed || Mathf.Abs(lateral) > scrapeMinSpeed)
            Scraping?.Invoke(Time.fixedDeltaTime);
        speed = Mathf.MoveTowards(speed, 0f, 3f * Time.fixedDeltaTime);
        if (Time.time - lastImpactTime > impactCooldown * 2f) HandleContact(collision);
    }

    private void HandleContact(Collision collision)
    {
        if (collision.contactCount == 0) return;

        Vector3 normal = collision.GetContact(0).normal;
        normal.y = 0f;
        if (normal.sqrMagnitude < 1e-4f) return;
        normal.Normalize();

        // Velocidad propia contra la roca: la del Rigidbody ya viene corregida
        // por el solver, así que se reconstruye desde el remo.
        Vector3 forward = transform.forward;
        Vector3 intended = forward * speed + transform.right * lateral;
        float impactSpeed = Mathf.Max(0f, -Vector3.Dot(intended, normal));

        if (impactSpeed < minImpactSpeed) return;
        if (Time.time - lastImpactTime < impactCooldown) return;
        lastImpactTime = Time.time;

        Severidad severity = impactSpeed >= strongImpactSpeed ? Severidad.Fuerte
            : impactSpeed >= moderateImpactSpeed ? Severidad.Moderada
            : Severidad.Leve;

        // Rebote: se pierde casi toda la velocidad y la canoa se aparta de la roca.
        speed *= -impactBounce;
        externalPush = normal * Mathf.Clamp(impactSpeed * 0.5f, 0.5f, 2.5f);

        shake = Mathf.Clamp01(impactSpeed / strongImpactSpeed) * 0.8f + 0.2f;
        shakeSign = Vector3.Dot(normal, transform.right) > 0f ? -1f : 1f;

        Impacted?.Invoke(severity, impactSpeed);
    }

    private void AnimateVisual()
    {
        float t = Time.time;
        shake = Mathf.MoveTowards(shake, 0f, Time.deltaTime * 1.4f);

        // Oleaje: altura bajo el centro, y la pendiente de proa a popa y de banda a banda.
        Vector3 pos = transform.position;
        Vector3 fwd = transform.forward, right = transform.right;
        float hC = oleaje.HeightAt(pos);
        float hBow = oleaje.HeightAt(pos + fwd * 2f), hStern = oleaje.HeightAt(pos - fwd * 2f);
        float hRight = oleaje.HeightAt(pos + right * 0.8f), hLeft = oleaje.HeightAt(pos - right * 0.8f);
        SurfaceY = hC;
        float wavePitch = -Mathf.Atan2(hBow - hStern, 4f) * Mathf.Rad2Deg;
        float waveRoll = Mathf.Atan2(hRight - hLeft, 1.6f) * Mathf.Rad2Deg;

        if (visual != null)
        {
            float bob = Mathf.Sin(t * 1.3f) * bobAmplitude + Mathf.Sin(t * 2.1f + 1f) * bobAmplitude * 0.4f;
            float roll = waveRoll + Mathf.Sin(t * 0.9f) * rollAmplitude - steer * Mathf.Abs(speed) * 0.8f;
            float pitch = wavePitch + Mathf.Sin(t * 1.1f + 0.5f) * rollAmplitude * 0.5f - Mathf.Clamp(speed, -2f, 7f) * 0.25f;

            // Sacudida del golpe: oscilación que se apaga.
            float hit = shake * shake;
            roll += Mathf.Sin(t * 28f) * 14f * hit * shakeSign;
            pitch += Mathf.Sin(t * 23f + 0.7f) * 5f * hit;

            float sinkY = -0.28f * sinkAmount;
            roll += 16f * sinkAmount;
            pitch += 5f * sinkAmount;

            visual.localPosition = new Vector3(0f, (hC - pos.y) + bob + sinkY, 0f);
            visual.localRotation = Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
