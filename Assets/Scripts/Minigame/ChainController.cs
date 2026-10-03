using UnityEngine;
using UnityEngine.InputSystem;

public class ChainController : MonoBehaviour
{
    public static ChainController Instance { get; private set; }

    [Header("Formación")]
    [SerializeField] private int nodesPerSide = 4;
    [SerializeField] private float segmentLength = 1.5f;
    [SerializeField] private Vector3 personScale = new Vector3(0.567f, 1.94f, 0.583f);

    [Header("Movimiento")]
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float turnSpeedDegPerSec = 90f;
    [SerializeField] private Vector3 forwardHeading = Vector3.forward;
    [SerializeField] private float mouseRotateSensitivity = 0.04f;

    [Header("Soga")]
    [SerializeField] private Color ropeColor = new Color(0.55f, 0.35f, 0.15f);
    [SerializeField] private float ropeWidth = 0.15f;

    [Header("Suelo")]
    [SerializeField] private Terrain groundTerrain;

    [Header("Obstáculos")]
    [SerializeField] private LayerMask obstacleMask;
    [SerializeField] private float personRadius = 0.35f;

    private Vector3 spawnCenter;
    private Vector3 spawnForwardHeading;
    private Vector3 centerPos;

    // Cada brazo tiene su propia curvatura, totalmente independiente uno del otro:
    // 0 = brazo recto (perpendicular al avance), 1 = semicírculo completo hacia el frente.
    private float leftCurl;
    private float rightCurl;

    private Vector3[] leftPositions;
    private Vector3[] rightPositions;
    private Transform[] leftNodes;
    private Transform[] rightNodes;
    private Transform centerNode;
    private LineRenderer rope;
    private bool inputEnabled = true;

    public Vector3 CenterPosition => centerPos;
    public Vector3 ForwardHeading => forwardHeading;

    private void Awake()
    {
        Instance = this;

        if (groundTerrain == null)
            groundTerrain = Terrain.activeTerrain;

        spawnCenter = SnapToGround(transform.position);
        spawnForwardHeading = forwardHeading;
        centerPos = spawnCenter;
        leftCurl = 0f;
        rightCurl = 0f;

        leftPositions = new Vector3[nodesPerSide];
        rightPositions = new Vector3[nodesPerSide];
        leftNodes = new Transform[nodesPerSide];
        rightNodes = new Transform[nodesPerSide];

        for (int i = 0; i < nodesPerSide; i++)
        {
            leftNodes[i] = CreatePersonCube("Persona_Izq_" + i);
            rightNodes[i] = CreatePersonCube("Persona_Der_" + i);
        }

        // El jugador mismo también ocupa un lugar en la fila (justo en el centro, entre los
        // dos brazos), así la cadena queda continua de punta a punta y no deja un hueco visual
        // ni un tramo sin nadie justo donde el jugador está parado.
        centerNode = CreatePersonCube("Jugador");

        RebuildArms();
        SetupRope();
        UpdateRope();
    }

    private Transform CreatePersonCube(string cubeName)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = cubeName;
        cube.transform.SetParent(transform);
        cube.transform.localScale = personScale;

        Collider col = cube.GetComponent<Collider>();
        if (col != null) col.isTrigger = false;

        return cube.transform;
    }

    private void SetupRope()
    {
        GameObject ropeObj = new GameObject("Soga");
        ropeObj.transform.SetParent(transform);

        rope = ropeObj.AddComponent<LineRenderer>();
        rope.positionCount = nodesPerSide * 2 + 1;
        rope.widthMultiplier = ropeWidth;
        rope.material = new Material(Shader.Find("Sprites/Default"));
        rope.startColor = ropeColor;
        rope.endColor = ropeColor;
        rope.useWorldSpace = true;
    }

    public void SetInputEnabled(bool value) => inputEnabled = value;

    public void ResetChain()
    {
        centerPos = spawnCenter;
        forwardHeading = spawnForwardHeading;
        leftCurl = 0f;
        rightCurl = 0f;
        transform.position = centerPos;
        RebuildArms();
        UpdateRope();
    }

    private void Update()
    {
        if (inputEnabled)
            ReadInput();

        RebuildArms();
        UpdateRope();
    }

    private void ReadInput()
    {
        if (Keyboard.current == null) return;

        bool forward = Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed;
        bool backward = Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed;

        // Cada brazo se controla de forma independiente: A/D lo achican (curvan hacia
        // adentro), Q/E lo extienden de vuelta (lo enderezan).
        bool shrinkLeft = Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed;
        bool extendLeft = Keyboard.current.qKey.isPressed;
        bool shrinkRight = Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed;
        bool extendRight = Keyboard.current.eKey.isPressed;

        // El mouse orienta hacia dónde apunta toda la formación (adelante/atrás y la
        // curvatura de los brazos son siempre relativos a esta dirección).
        if (Mouse.current != null)
        {
            float mouseDeltaX = Mouse.current.delta.ReadValue().x;
            if (Mathf.Abs(mouseDeltaX) > 0.0001f)
            {
                Vector3 candidateHeading = Quaternion.Euler(0f, mouseDeltaX * mouseRotateSensitivity, 0f) * forwardHeading;

                // Si girar hacia allá metería algún brazo dentro de un árbol, se bloquea el giro
                // por completo en vez de dejar que la cadena lo atraviese de golpe.
                if (!WouldArmsHitObstacle(candidateHeading))
                    forwardHeading = candidateHeading;
            }
        }

        Vector3 previousCenter = centerPos;
        if (forward) centerPos += forwardHeading.normalized * moveSpeed * Time.deltaTime;
        if (backward) centerPos -= forwardHeading.normalized * moveSpeed * Time.deltaTime;

        // Los árboles frenan al jugador como si fueran una pared: si el paso lo metió dentro
        // de un obstáculo, se lo empuja de vuelta justo a su superficie.
        centerPos = ResolveObstacles(centerPos, previousCenter);

        // Cada extremo de la cadena es independiente del otro: se puede achicar/extender
        // un brazo sin afectar para nada al otro. Igual que con el giro del mouse, si el
        // movimiento metería algún nodo del brazo dentro de un árbol, se bloquea del todo
        // en vez de dejar que la curvatura siga cambiando mientras el árbol la frena.
        float curlSpeed = turnSpeedDegPerSec / 180f;
        if (shrinkLeft) TryApplyCurl(ref leftCurl, leftCurl + curlSpeed * Time.deltaTime, -90f);
        if (extendLeft) TryApplyCurl(ref leftCurl, leftCurl - curlSpeed * Time.deltaTime, -90f);
        if (shrinkRight) TryApplyCurl(ref rightCurl, rightCurl + curlSpeed * Time.deltaTime, 90f);
        if (extendRight) TryApplyCurl(ref rightCurl, rightCurl - curlSpeed * Time.deltaTime, 90f);

        centerPos = SnapToGround(centerPos);
        transform.position = centerPos;
    }

    // Igual que WouldArmsHitObstacle bloquea el giro del mouse, esto bloquea achicar/extender
    // un brazo si la nueva curvatura metería alguno de sus nodos dentro de un árbol.
    private void TryApplyCurl(ref float curl, float candidateValue, float baseAngle)
    {
        float candidate = Mathf.Clamp01(candidateValue);
        if (Mathf.Approximately(candidate, curl)) return;

        float sweepSign = baseAngle < 0f ? 180f : -180f;
        if (!ArmHitsObstacle(forwardHeading, baseAngle, sweepSign * candidate))
            curl = candidate;
    }

    private void RebuildArms()
    {
        if (centerNode != null) centerNode.position = centerPos;

        // A curvatura 0 el brazo queda recto (perpendicular al avance); a curvatura 1 barre
        // 180° a través del frente hasta formar un semicírculo completo.
        BuildArm(rightPositions, rightNodes, 90f, -180f * rightCurl);
        BuildArm(leftPositions, leftNodes, -90f, 180f * leftCurl);
    }

    private void BuildArm(Vector3[] positions, Transform[] nodes, float baseAngle, float totalSweepDegrees)
    {
        float anglePerSegment = totalSweepDegrees / positions.Length;
        Vector3 previous = centerPos;

        for (int i = 0; i < positions.Length; i++)
        {
            float angle = baseAngle + anglePerSegment * (i + 1);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * forwardHeading.normalized;
            Vector3 point = previous + dir * segmentLength;
            point = ResolveObstacles(point, previous);
            point = SnapToGround(point);

            positions[i] = point;

            if (nodes[i] != null) nodes[i].position = point;

            previous = point;
        }
    }

    // Comprueba, sin mover nada todavía, si con este nuevo rumbo algún nodo de cualquiera de
    // los dos brazos terminaría metido dentro de un árbol/obstáculo.
    private bool WouldArmsHitObstacle(Vector3 heading)
    {
        if (obstacleMask == 0) return false;

        return ArmHitsObstacle(heading, 90f, -180f * rightCurl) || ArmHitsObstacle(heading, -90f, 180f * leftCurl);
    }

    private bool ArmHitsObstacle(Vector3 heading, float baseAngle, float totalSweepDegrees)
    {
        float anglePerSegment = totalSweepDegrees / nodesPerSide;
        Vector3 previous = centerPos;

        for (int i = 0; i < nodesPerSide; i++)
        {
            float angle = baseAngle + anglePerSegment * (i + 1);
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * heading.normalized;
            Vector3 point = previous + dir * segmentLength;

            // Cápsula a lo largo de TODO el tramo (no solo un chequeo puntual en el extremo):
            // así un árbol angosto que cae justo en la mitad de la soga entre dos personas
            // también se detecta, no solo cuando queda pegado a uno de los nodos.
            if (Physics.CheckCapsule(previous, point, personRadius, obstacleMask, QueryTriggerInteraction.Ignore))
                return true;

            previous = point;
        }

        return false;
    }

    // Si el punto cae dentro de un árbol/obstáculo, se lo empuja de vuelta a su superficie en
    // vez de dejarlo atravesarlo: para el jugador esto actúa como una pared sólida, y para los
    // nodos de los brazos hace que la cadena se "achate" pegada al borde para poder pasar.
    private Vector3 ResolveObstacles(Vector3 point, Vector3 previous)
    {
        if (obstacleMask == 0) return point;

        // Varias pasadas para terminar de sacar el punto cuando queda solapado con más de un
        // obstáculo a la vez (p. ej. entre dos árboles pegados).
        for (int pass = 0; pass < 3; pass++)
        {
            // Cápsula entre "previous" y "point" (no solo una esfera en "point"): un árbol
            // que cae en medio del tramo entre dos personas de la cadena también cuenta como
            // colisión, no solo cuando toca directamente a uno de los nodos.
            Collider[] hits = Physics.OverlapCapsule(previous, point, personRadius, obstacleMask, QueryTriggerInteraction.Ignore);
            if (hits.Length == 0) break;

            foreach (Collider col in hits)
            {
                Vector3 closest = col.ClosestPoint(point);
                Vector3 push = point - closest;
                push.y = 0f;

                if (push.sqrMagnitude < 0.0001f)
                {
                    push = point - previous;
                    push.y = 0f;
                    if (push.sqrMagnitude < 0.0001f) push = Vector3.forward;
                    push = Vector3.Cross(push.normalized, Vector3.up);
                }

                push.Normalize();
                point = closest + push * personRadius;
            }
        }

        return point;
    }

    private void UpdateRope()
    {
        if (rope == null) return;

        int idx = 0;
        for (int i = nodesPerSide - 1; i >= 0; i--)
            rope.SetPosition(idx++, leftPositions[i]);
        rope.SetPosition(idx++, centerPos);
        for (int i = 0; i < nodesPerSide; i++)
            rope.SetPosition(idx++, rightPositions[i]);
    }

    private Vector3 SnapToGround(Vector3 point)
    {
        if (groundTerrain == null) return point;
        point.y = groundTerrain.SampleHeight(point) + groundTerrain.transform.position.y;
        return point;
    }

    public float GetNearestNodeDistance(Vector3 point, out Vector3 nearestPos)
    {
        float best = (point - centerPos).sqrMagnitude;
        nearestPos = centerPos;

        for (int i = 0; i < nodesPerSide; i++)
        {
            float dl = (point - leftPositions[i]).sqrMagnitude;
            if (dl < best) { best = dl; nearestPos = leftPositions[i]; }

            float dr = (point - rightPositions[i]).sqrMagnitude;
            if (dr < best) { best = dr; nearestPos = rightPositions[i]; }
        }

        return Mathf.Sqrt(best);
    }

    // Distancia al punto más cercano sobre la LÍNEA de la soga (no solo a los nodos discretos).
    // Empujar en base a esto evita que, al alejar a una vicuña de un nodo, quede pegada al
    // nodo vecino (que puede estar más cerca en esa misma dirección si la cuerda es angosta).
    public float GetNearestRopePoint(Vector3 point, out Vector3 nearestPos)
    {
        nearestPos = centerPos;
        float best = float.MaxValue;

        Vector3 previous = centerPos;
        for (int i = nodesPerSide - 1; i >= 0; i--)
            EvaluateSegment(point, ref previous, leftPositions[i], ref best, ref nearestPos);

        previous = centerPos;
        for (int i = 0; i < nodesPerSide; i++)
            EvaluateSegment(point, ref previous, rightPositions[i], ref best, ref nearestPos);

        return Mathf.Sqrt(best);
    }

    private static void EvaluateSegment(Vector3 point, ref Vector3 segStart, Vector3 segEnd, ref float best, ref Vector3 nearestPos)
    {
        Vector3 ab = segEnd - segStart;
        float sqrLen = Mathf.Max(ab.sqrMagnitude, 0.0001f);
        float tRaw = Vector3.Dot(point - segStart, ab) / sqrLen;
        float t = Mathf.Clamp01(tRaw);
        Vector3 closest = segStart + ab * t;

        float d = (point - closest).sqrMagnitude;
        if (d < best)
        {
            best = d;
            nearestPos = closest;
        }

        segStart = segEnd;
    }
}
