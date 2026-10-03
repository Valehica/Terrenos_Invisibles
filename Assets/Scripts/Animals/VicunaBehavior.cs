using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class VicunaBehavior : MonoBehaviour
{
    private enum State { Idle, Wander, Flee }

    [Header("Detección del jugador")]
    [SerializeField] private float alertDistance = 5f;
    [SerializeField] private float fleeDistance = 2f;
    [SerializeField] private float fleeDistanceCrouching = 1f;

    [Header("Velocidades")]
    [SerializeField] private float walkSpeed = 1.4f;
    [SerializeField] private float fleeSpeed = 5f;

    [Header("Deambular (NavMesh)")]
    [SerializeField] private Transform wanderCenter;
    [SerializeField] private float wanderRadius = 15f;
    [SerializeField] private float minIdleTime = 2f;
    [SerializeField] private float maxIdleTime = 6f;
    [SerializeField] private float minWanderTime = 4f;
    [SerializeField] private float maxWanderTime = 9f;

    [Header("Huida")]
    [SerializeField] private float fleeMinDistance = 6.5f;
    [SerializeField] private float fleeMaxDistance = 7.5f;

    [Header("Cabeza (mirar al jugador)")]
    [SerializeField] private string headBoneName = "DEF-head";
    [SerializeField] private string headControlBoneName = "ORG-head";
    [SerializeField] private float maxHeadYaw = 70f;
    [SerializeField] private float headTurnSpeed = 8f;

    [Header("Apoyo en el suelo")]
    [SerializeField] private Terrain groundTerrain;

    private NavMeshAgent agent;
    private Animator animator;
    private Transform player;
    private PlayerController playerController;
    private Transform headBone;
    private Transform headControlBone;

    private State state;
    private float stateTimer;
    private float currentHeadYaw;
    private Vector3 spawnPosition;
    private float arrivalGuardTimer;
    private float distanceToPlayer = Mathf.Infinity;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        spawnPosition = wanderCenter != null ? wanderCenter.position : transform.position;

        if (groundTerrain == null)
            groundTerrain = Terrain.activeTerrain;

        foreach (var t in GetComponentsInChildren<Transform>())
        {
            if (t.name == headBoneName) headBone = t;
            else if (t.name == headControlBoneName) headControlBone = t;
        }
    }

    private void Start()
    {
        playerController = FindFirstObjectByType<PlayerController>();
        if (playerController != null)
            player = playerController.transform;

        EnterIdle();
    }

    private void Update()
    {
        if (animator != null)
            animator.SetFloat(SpeedHash, agent.velocity.magnitude);

        if (player == null) return;

        distanceToPlayer = Vector3.Distance(transform.position, player.position);

        if (state == State.Flee)
        {
            UpdateFlee();
            return;
        }

        bool crouching = playerController != null && playerController.IsCrouching;
        float currentFleeDistance = crouching ? fleeDistanceCrouching : fleeDistance;

        if (distanceToPlayer <= currentFleeDistance)
        {
            EnterFlee();
            return;
        }

        if (state == State.Idle) UpdateIdle();
        else if (state == State.Wander) UpdateWander();
    }

    private void LateUpdate()
    {
        SnapToGround();
        UpdateHeadLook();
    }

    private void SnapToGround()
    {
        if (groundTerrain == null) return;

        Vector3 pos = transform.position;
        pos.y = groundTerrain.SampleHeight(pos) + groundTerrain.transform.position.y;
        transform.position = pos;
    }

    private void UpdateHeadLook()
    {
        if (headBone == null && headControlBone == null) return;

        float targetYaw = 0f;
        bool shouldLook = player != null && state != State.Flee && distanceToPlayer <= alertDistance;
        if (shouldLook)
        {
            Vector3 headPos = headBone != null ? headBone.position : headControlBone.position;
            Vector3 toPlayerFlat = player.position - headPos;
            toPlayerFlat.y = 0f;
            Vector3 bodyForwardFlat = transform.forward;
            bodyForwardFlat.y = 0f;

            if (toPlayerFlat.sqrMagnitude > 0.0001f && bodyForwardFlat.sqrMagnitude > 0.0001f)
                targetYaw = Mathf.Clamp(Vector3.SignedAngle(bodyForwardFlat, toPlayerFlat, Vector3.up), -maxHeadYaw, maxHeadYaw);
        }

        currentHeadYaw = Mathf.LerpAngle(currentHeadYaw, targetYaw, headTurnSpeed * Time.deltaTime);
        if (Mathf.Abs(currentHeadYaw) > 0.01f)
        {
            Quaternion delta = Quaternion.AngleAxis(currentHeadYaw, Vector3.up);
            if (headBone != null)
                headBone.rotation = delta * headBone.rotation;
            if (headControlBone != null)
                headControlBone.rotation = delta * headControlBone.rotation;
        }
    }

    private void EnterIdle()
    {
        state = State.Idle;
        agent.isStopped = true;
        agent.updateRotation = true;
        stateTimer = Random.Range(minIdleTime, maxIdleTime);
    }

    private void UpdateIdle()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f) EnterWander();
    }

    private void EnterWander()
    {
        state = State.Wander;
        agent.speed = walkSpeed;
        agent.updateRotation = true;
        agent.isStopped = false;
        stateTimer = Random.Range(minWanderTime, maxWanderTime);

        Vector2 randomCircle = Random.insideUnitCircle * wanderRadius;
        Vector3 candidate = spawnPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            arrivalGuardTimer = 0.3f;
        }
        else
        {
            EnterIdle();
        }
    }

    private void UpdateWander()
    {
        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f || HasArrived()) EnterIdle();
    }

    private bool HasArrived()
    {
        if (arrivalGuardTimer > 0f)
        {
            arrivalGuardTimer -= Time.deltaTime;
            return false;
        }

        if (agent.pathPending) return false;
        if (agent.remainingDistance > agent.stoppingDistance + 0.15f) return false;

        return !agent.hasPath || agent.velocity.sqrMagnitude < 0.01f;
    }

    private void EnterFlee()
    {
        state = State.Flee;
        agent.speed = fleeSpeed;
        agent.updateRotation = true;
        agent.isStopped = false;
        PickFleeDestination();
    }

    private void UpdateFlee()
    {
        if (HasArrived()) EnterIdle();
    }

    private void PickFleeDestination()
    {
        Vector3 awayFromPlayer = transform.position - player.position;
        awayFromPlayer.y = 0f;
        if (awayFromPlayer.sqrMagnitude < 0.0001f)
            awayFromPlayer = Random.insideUnitSphere;
        awayFromPlayer.Normalize();

        float randomAngle = Random.Range(-30f, 30f);
        Vector3 fleeDirection = Quaternion.Euler(0f, randomAngle, 0f) * awayFromPlayer;
        float fleeDistanceTarget = Random.Range(fleeMinDistance, fleeMaxDistance);
        Vector3 candidate = transform.position + fleeDirection * fleeDistanceTarget;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, fleeMaxDistance, NavMesh.AllAreas))
        {
            agent.SetDestination(hit.position);
            arrivalGuardTimer = 0.3f;
        }
        else
        {
            EnterIdle();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, alertDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, fleeDistance);
    }
}
