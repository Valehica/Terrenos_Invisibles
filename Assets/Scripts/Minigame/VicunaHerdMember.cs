using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class VicunaHerdMember : MonoBehaviour
{
    [Header("Grupo")]
    [SerializeField] private VicunaHerdGroup group;

    [Header("Reacción a la cadena")]
    [SerializeField] private float scareRadius = 6f;
    [SerializeField] private float fleeSpeed = 3f;
    [SerializeField] private float fleeStepDistance = 4f;

    [Header("Barrera física de la cadena")]
    [SerializeField] private float chainBlockRadius = 1f;

    [Header("Suavizado cerca del corral")]
    [SerializeField] private float corralSoftenDistance = 5f;
    [SerializeField] private float corralLateralPadding = 6f;

    [Header("Cohesión de grupo")]
    [SerializeField] private float cohesionWeight = 1f;
    [SerializeField] private float separationRadius = 2f;
    [SerializeField] private float separationWeight = 1.5f;

    [Header("Instinto de arreo (más tontas, van fácil al corral)")]
    [SerializeField] private float corralPullWeight = 1.2f;
    [SerializeField] private float chainHerdWeight = 0.8f;

    [Header("Salto aleatorio de la cadena")]
    [SerializeField] private float chainJumpChancePerSecond = 0.03f;
    [SerializeField] private float chainJumpDuration = 1.2f;
    [SerializeField] private float chainJumpHeight = 1.2f;

    [Header("Deambular")]
    [SerializeField] private float wanderSpeed = 1.4f;
    [SerializeField] private float wanderRadius = 3f;
    [SerializeField] private float minRetargetTime = 2f;
    [SerializeField] private float maxRetargetTime = 5f;

    [Header("Suelo")]
    [SerializeField] private Terrain groundTerrain;

    [Header("Zona de juego")]
    [SerializeField] private float outerBoundsMargin = 1f;

    private NavMeshAgent agent;
    private Animator animator;
    private float retargetTimer;
    private bool collected;
    private float corralFactor = 1f;
    private float jumpTimer;

    public bool IsCollected => collected;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        if (group == null) group = GetComponentInParent<VicunaHerdGroup>();
        if (groundTerrain == null) groundTerrain = Terrain.activeTerrain;

        // El prefab viene con isStopped=true (estado "Idle" del comportamiento de exploración).
        agent.isStopped = false;
    }

    private void OnEnable()
    {
        retargetTimer = Random.Range(minRetargetTime, maxRetargetTime);
    }

    public void SetCollected()
    {
        if (collected) return;

        collected = true;
        agent.isStopped = true;
        agent.ResetPath();
    }

    public void ResetCollected()
    {
        collected = false;
    }

    private void Update()
    {
        if (collected)
        {
            if (animator != null)
                animator.SetFloat(SpeedHash, 0f);
            return;
        }

        // Defensivo: el prefab trae isStopped=true (estado Idle del comportamiento de
        // exploración) y NavMeshAgent puede reafirmarlo en su propia inicialización tras Awake.
        if (agent.isStopped)
            agent.isStopped = false;

        if (animator != null)
            animator.SetFloat(SpeedHash, agent.velocity.magnitude);

        corralFactor = ComputeCorralFactor();

        // Ya está en la boca del corral: deja de competir con el miedo a la cadena o la
        // cohesión del grupo (que la tironeaban de vuelta) y camina directo a entrar.
        if (corralFactor < 1f)
        {
            EnterCorral();
            return;
        }

        if (jumpTimer > 0f)
        {
            jumpTimer -= Time.deltaTime;

            // Mientras "salta" la cadena ignora por completo el miedo a la soga y su barrera
            // física (ver EnforceChainBarrier), así puede cruzar libremente al otro lado.
            Wander();

            // Va rápido y con la animación de correr durante todo el salto (Wander() de por sí
            // usa la velocidad lenta de deambular, así que se sobreescribe después de llamarlo).
            agent.speed = fleeSpeed;
            if (animator != null)
                animator.SetFloat(SpeedHash, fleeSpeed);

            return;
        }

        Vector3 nearestChainPos = transform.position;
        float chainDistance = float.MaxValue;
        if (ChainController.Instance != null)
            chainDistance = ChainController.Instance.GetNearestRopePoint(transform.position, out nearestChainPos);

        if (chainDistance <= scareRadius)
        {
            // Más tontas: en vez de esquivar siempre con cuidado, de vez en cuando alguna
            // simplemente "salta" la cadena y sigue de largo.
            if (Random.value < chainJumpChancePerSecond * Time.deltaTime)
            {
                jumpTimer = chainJumpDuration;
                return;
            }

            Flee(nearestChainPos);
            return;
        }

        Wander();
    }

    // Camina directo hacia el centro del corral, ignorando el resto del comportamiento
    // (miedo a la cadena, cohesión de grupo, deambular) para que entre con facilidad.
    private void EnterCorral()
    {
        agent.speed = fleeSpeed;

        var mgr = ChakuMinigameManager.Instance;
        if (mgr == null || mgr.CorralZone == null) return;

        Vector3 target = mgr.CorralZone.bounds.center;
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, corralSoftenDistance * 2f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    private void LateUpdate()
    {
        if (groundTerrain != null)
        {
            Vector3 pos = transform.position;
            pos.y = groundTerrain.SampleHeight(pos) + groundTerrain.transform.position.y + ComputeJumpOffset();
            transform.position = pos;
        }

        EnforceChainBarrier();
    }

    // Arco vertical simple (sube y vuelve a bajar) mientras dura el salto de la cadena, para
    // que se vea como un salto real y no solo como cruzar caminando.
    private float ComputeJumpOffset()
    {
        if (jumpTimer <= 0f || chainJumpDuration <= 0f) return 0f;

        float t = 1f - Mathf.Clamp01(jumpTimer / chainJumpDuration);
        return Mathf.Sin(t * Mathf.PI) * chainJumpHeight;
    }

    // La cadena no tiene collider físico (es una fila de cubos moviéndose cada frame), así que
    // sin esto la vicuña podía "atravesarla": una vez dentro del radio de susto puede que su
    // ruta la lleve a cruzar la soga igual. Esto la empuja de vuelta como si fuera una pared sólida.
    private void EnforceChainBarrier()
    {
        if (collected || ChainController.Instance == null || jumpTimer > 0f) return;

        // Cerca del corral esta barrera se relaja (corralFactor -> 0): si no, el empujón
        // instantáneo de la cadena compite justo con el intento de entrar y la vicuña
        // "rebota" en la boca del corral en vez de cruzar.
        float effectiveBlockRadius = chainBlockRadius * corralFactor;
        if (effectiveBlockRadius <= 0.001f) return;

        Vector3 corrected = transform.position;
        bool pushed = false;

        // Un par de pasadas: al empujar lejos de un tramo de soga la posición puede quedar
        // pegada a un tramo vecino (p. ej. en una curva cerrada), esto lo termina de resolver.
        for (int i = 0; i < 3; i++)
        {
            float dist = ChainController.Instance.GetNearestRopePoint(corrected, out Vector3 nearestPoint);
            if (dist >= effectiveBlockRadius) break;

            Vector3 push = corrected - nearestPoint;
            push.y = 0f;
            if (push.sqrMagnitude < 0.0001f) push = Random.insideUnitSphere;
            push.Normalize();

            corrected = nearestPoint + push * effectiveBlockRadius;
            corrected.y = transform.position.y;
            pushed = true;
        }

        if (!pushed) return;

        if (agent.isOnNavMesh)
            agent.Warp(corrected);
        else
            transform.position = corrected;
    }

    // 1 = lejos del corral (resistencia normal contra la cadena), 0 = ya en la boca del
    // corral (sin resistencia, para que termine de entrar sin trabarse).
    private float ComputeCorralFactor()
    {
        var mgr = ChakuMinigameManager.Instance;
        if (mgr == null || mgr.CorralZone == null) return 1f;

        // Igual que entrar por el centro (justo frente a la boca) también ceda fácil cuando
        // se empuja desde un poco más a la derecha o izquierda: para medir la distancia se usa
        // una caja ensanchada a los lados (padding solo en X), no la caja real de recolección.
        Bounds b = mgr.CorralZone.bounds;
        b.size += new Vector3(corralLateralPadding * 2f, 0f, 0f);

        Vector3 closest = b.ClosestPoint(transform.position);
        float dist = Vector3.Distance(transform.position, closest);
        return Mathf.Clamp01(dist / corralSoftenDistance);
    }

    private void Flee(Vector3 threatPos)
    {
        agent.speed = fleeSpeed;

        Vector3 away = transform.position - threatPos;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitSphere;
        away.Normalize();

        Vector3 towardGroup = Vector3.zero;
        if (group != null)
        {
            Vector3 toCentroid = group.Centroid - transform.position;
            toCentroid.y = 0f;
            towardGroup = toCentroid.normalized * cohesionWeight;
        }

        Vector3 separation = ComputeSeparation();

        // Más tontas y fáciles de arrear: además de huir de la soga, las atrae el corral y la
        // dirección hacia la que avanza la cadena, como si caminaran empujadas por ella.
        Vector3 towardCorral = Vector3.zero;
        var mgr = ChakuMinigameManager.Instance;
        if (mgr != null && mgr.CorralZone != null)
        {
            Vector3 toCorral = mgr.CorralZone.bounds.center - transform.position;
            toCorral.y = 0f;
            if (toCorral.sqrMagnitude > 0.0001f)
                towardCorral = toCorral.normalized * corralPullWeight;
        }

        Vector3 herdDirection = ChainController.Instance != null
            ? ChainController.Instance.ForwardHeading.normalized * chainHerdWeight
            : Vector3.zero;

        Vector3 desired = (away * 2f + towardGroup + separation * separationWeight + towardCorral + herdDirection);
        if (desired.sqrMagnitude < 0.0001f) desired = away;
        desired.Normalize();

        Vector3 destination = ClampToPlayArea(transform.position + desired * fleeStepDistance);

        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, fleeStepDistance + 2f, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    private void Wander()
    {
        agent.speed = wanderSpeed;
        retargetTimer -= Time.deltaTime;

        bool arrived = !agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.15f;
        if (retargetTimer > 0f && !arrived) return;

        retargetTimer = Random.Range(minRetargetTime, maxRetargetTime);

        Vector3 center = group != null ? group.Centroid : transform.position;
        Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
        Vector3 candidate = ClampToPlayArea(center + new Vector3(randomCircle.x, 0f, randomCircle.y));

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    // La cohesión/separación del grupo puede, al sumarse con el miedo a la cadena, apuntar un
    // destino más allá del área de juego aunque nadie la esté empujando ahí a propósito. Esto
    // evita que la IA se mande sola afuera: solo debería reiniciar si la cadena la saca de
    // verdad (EnforceChainBarrier), no un cálculo de deambular/huida que se pasó de largo.
    private Vector3 ClampToPlayArea(Vector3 point)
    {
        var mgr = ChakuMinigameManager.Instance;
        if (mgr == null || mgr.OuterBounds == null) return point;

        Bounds b = mgr.OuterBounds.bounds;
        point.x = Mathf.Clamp(point.x, b.min.x + outerBoundsMargin, b.max.x - outerBoundsMargin);
        point.z = Mathf.Clamp(point.z, b.min.z + outerBoundsMargin, b.max.z - outerBoundsMargin);
        return point;
    }

    private Vector3 ComputeSeparation()
    {
        if (group == null) return Vector3.zero;

        Vector3 push = Vector3.zero;
        foreach (var member in group.Members)
        {
            if (member == this || member == null) continue;

            Vector3 diff = transform.position - member.transform.position;
            float dist = diff.magnitude;
            if (dist > 0.001f && dist < separationRadius)
                push += diff.normalized * (separationRadius - dist);
        }

        return push;
    }
}
