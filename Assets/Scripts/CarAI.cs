using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IA de voiture : plus court chemin, conduite à droite, vitesse variable,
/// détection de la voiture devant, priorité à droite aux carrefours,
/// + éléments de voirie (RoadElement), infractions, arrestation par la police.
/// La simulation tourne dans FixedUpdate => déterministe (même scénario à chaque essai).
/// </summary>
public class CarAI : MonoBehaviour
{
    [Header("Vitesse")]
    public float maxSpeed = 6f;
    [Range(0f, 0.5f)] public float speedVariation = 0.15f;
    public float acceleration = 4f;
    public float braking = 8f;
    [Range(0.1f, 1f)] public float cornerSpeedFactor = 0.4f;
    public float turnSpeed = 360f;

    public static float GlobalSpeedMultiplier = 1f;

    [Header("Trajet")]
    public bool wanderRandomly = true;
    public float laneOffset = 1.5f;

    [Header("Détection de la voiture devant")]
    public float lookAhead = 12f;
    public float detectWidth = 1f;
    public float carLength = 4f;
    public float safeGap = 1.5f;
    public float maxBlockedTime = 5f;

    [Header("Carrefours (priorité à droite)")]
    public float stopMargin = 2.5f;
    public float awareness = 10f;
    public float patience = 2f;

    [Header("Comportement")]
    [Tooltip("Probabilité de respecter chaque panneau / feu posé")]
    [Range(0f, 1f)] public float obeyChance = 0.85f;
    [Tooltip("Probabilité (par carrefour) d'ignorer la priorité à droite : source principale d'accidents")]
    [Range(0f, 1f)] public float recklessChance = 0.1f;
    [Range(0f, 1f), Tooltip("Probabilité que ce conducteur roule bien au-dessus de sa vitesse normale")]
    public float speedingChance = 0f;
    [Tooltip("Multiplicateur de vitesse quand il est en excès de vitesse")]
    public float speedingMultiplier = 1.5f;
    public bool isPolice;


    [Header("Collision / visuel")]
    public float collisionRadius = 1.1f;   // accident si distance < somme des rayons des deux voitures
    public float halfLength = 2.3f;        // zone de renversement des piétons
    public float halfWidth = 1.3f;
    [Tooltip("Renderers colorés au hasard (carrosserie uniquement)")]
    public Renderer[] bodyRenderers;
    [HideInInspector] public float speedBoost = 1f;

    public float Speed => currentSpeed;

    // ---------- État ----------
    static readonly List<CarAI> All = new List<CarAI>();
    public static IReadOnlyList<CarAI> Cars => All;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    const int TurnLeft = -1, TurnStraight = 0, TurnRight = 1;

    System.Random rng = new System.Random();
    float Roll() => (float)rng.NextDouble();
    float Range(float a, float b) => a + Roll() * (b - a);

    List<int> nodes;
    List<Vector3> waypoints;
    List<int> turns;
    int index;
    Vector3 target;

    float currentSpeed, personalSpeed, personalPatience;

    int approachNode = -1, prevNode = -1, turn, destination = -1;
    int lastNode = -1, lastPrev = -1;   // fin du dernier trajet : où je suis, et d'où je viens
    Vector3 approachDir = Vector3.forward;
    bool committed, ignoreIntersection;
    float waitTime, blockedTime, ignoreFrontTimer, arrestTimer, idleTimer;
    bool begun;

    // mémoire par approche de noeud (réinitialisée à chaque nouveau noeud visé)
    readonly Dictionary<RoadElement, float> memo = new Dictionary<RoadElement, float>();
    readonly Dictionary<RoadElement, bool> obey = new Dictionary<RoadElement, bool>();
    readonly HashSet<string> reported = new HashSet<string>();

    // ---------- API pour les éléments de voirie ----------
    public int ApproachNode => approachNode;
    public int PrevNode => prevNode;
    public Vector3 ApproachDir => approachDir;
    public int Destination => destination;
    public int NextNode => (nodes != null && index + 1 < nodes.Count) ? nodes[index + 1] : -1;
    public bool IsArrested => arrestTimer > 0f;
    public float DistToNode => approachNode < 0 ? float.MaxValue
        : Dist(transform.position, RoadGraph.Instance.NodePos(approachNode));

    public void Arrest(float seconds) => arrestTimer = seconds;
    public float GetMemo(RoadElement e) => memo.TryGetValue(e, out var v) ? v : 0f;
    public void SetMemo(RoadElement e, float v) => memo[e] = v;

    public bool Obeys(RoadElement e) => Obeys(e, obeyChance);
    public bool Obeys(RoadElement e, float chance)
    {
        if (isPolice) return true;
        if (!obey.TryGetValue(e, out bool o)) { o = Roll() < chance; obey[e] = o; }
        return o;
    }

    public void Report(string reason)
    {
        if (reported.Add(reason)) Infractions.Report(this, reason);
    }

    // ---------- Cycle de vie ----------
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Awake()
    {
        SetMaxSpeed(maxSpeed);
        personalPatience = patience * Range(0.7f, 1.5f);
    }

    void Start()
    {
        if (!begun) { begun = true; if (wanderRandomly) Wander(); }
    }

    /// <summary>Démarrage déterministe : appelé par le GameManager avec une graine.</summary>
    public void Begin(int seed)
    {
        rng = new System.Random(seed);
        begun = true;
        SetMaxSpeed(maxSpeed);
        personalPatience = patience * Range(0.7f, 1.5f);
        lastNode = lastPrev = -1;
        Wander();
        SnapToLane();
    }

    /// <summary>Place la voiture sur sa voie de droite, orientée dans le sens du trajet (appelé uniquement au spawn).</summary>
    void SnapToLane()
    {
        if (nodes == null || waypoints == null || waypoints.Count < 2 || nodes.Count < 2) return;

        var graph = RoadGraph.Instance;
        Vector3 pos = waypoints[0];
        pos.y = transform.position.y;

        // sens de circulation = direction du premier tronçon du trajet
        Vector3 dir = Dir(graph.NodePos(nodes[0]), graph.NodePos(nodes[1]));
        if (dir.sqrMagnitude < 0.0001f) return;

        transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir, Vector3.up));
        target.y = pos.y;
    }

    public void SetMaxSpeed(float v)
    {
        maxSpeed = v;
        personalSpeed = v * (1f + Range(-speedVariation, speedVariation));
        if (Roll() < speedingChance) personalSpeed *= speedingMultiplier;
    }

    // ---------- Navigation ----------
    void Wander()
    {
        for (int i = 0; i < 10; i++)
            if (GoTo(RoadGraph.Instance.RandomNode(rng))) return;
        nodes = null; // aucun chemin trouvé : on réessaiera plus tard
    }

    public bool GoTo(Vector3 worldPos) => GoTo(RoadGraph.Instance.WorldToNode(worldPos));

    public bool GoTo(int destinationNode)
    {
        var graph = RoadGraph.Instance;
        bool midRoute = nodes != null && index < nodes.Count;
        int start = midRoute ? nodes[index] : graph.WorldToNode(transform.position);
        int keepPrev = midRoute ? prevNode : -1;

        // nœud d'où je viens : un chemin qui y retourne directement serait un demi-tour
        int cameFrom = midRoute ? prevNode : (start == lastNode ? lastPrev : -1);

        var path = graph.FindPath(start, destinationNode);
        if (path == null) return false;

        if (cameFrom >= 0 && path.Count >= 2 && path[1] == cameFrom && !IsDeadEnd(start))
            return false;   // demi-tour interdit (hors cul-de-sac)

        destination = destinationNode;
        BuildWaypoints(path);
        index = 0;
        SetTarget();

        // en cours de route : on garde le contexte (d'où l'on vient) pour respecter les règles
        if (keepPrev >= 0 && keepPrev != start)
        {
            prevNode = keepPrev;
            approachDir = Dir(graph.NodePos(keepPrev), graph.NodePos(start));
        }
        return true;
    }

    /// <summary>Cul-de-sac : un seul voisin, le demi-tour est alors autorisé.</summary>
    bool IsDeadEnd(int node)
    {
        int n = 0;
        foreach (int nb in RoadGraph.Instance.Neighbors(node))
            if (++n > 1) return false;
        return true;
    }

    static Vector3 Dir(Vector3 a, Vector3 b)
    {
        Vector3 d = b - a;
        d.y = 0f;
        return d.normalized;
    }

    static Vector3 RightOf(Vector3 a, Vector3 b) => Vector3.Cross(Vector3.up, Dir(a, b));

    static float Dist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    void BuildWaypoints(List<int> path)
    {
        var graph = RoadGraph.Instance;
        nodes = path;
        waypoints = new List<Vector3>();
        turns = new List<int>();
        int last = path.Count - 1;

        for (int i = 0; i <= last; i++)
        {
            Vector3 c = graph.NodePos(path[i]);
            Vector3 a = i > 0 ? graph.NodePos(path[i - 1]) : c;
            Vector3 b = i < last ? graph.NodePos(path[i + 1]) : c;

            Vector3 rIn = i > 0 ? RightOf(a, c) : Vector3.zero;
            Vector3 rOut = i < last ? RightOf(c, b) : Vector3.zero;

            Vector3 offset;
            int t = TurnStraight;
            if (i == 0) offset = rOut;
            else if (i == last) offset = rIn;
            else if (rIn == rOut) offset = rIn;
            else
            {
                offset = rIn + rOut;
                float s = Vector3.Cross(Dir(a, c), Dir(c, b)).y;
                t = s > 0f ? TurnRight : TurnLeft;
            }

            waypoints.Add(c + offset * laneOffset);
            turns.Add(t);
        }
    }

    void SetTarget()
    {
        var graph = RoadGraph.Instance;
        target = waypoints[index];
        target.y = transform.position.y;

        approachNode = nodes[index];
        prevNode = index > 0 ? nodes[index - 1] : -1;
        approachDir = prevNode >= 0 ? Dir(graph.NodePos(prevNode), graph.NodePos(approachNode)) : transform.forward;
        turn = turns[index];
        committed = false;
        waitTime = 0f;

        PruneMemory();
        ignoreIntersection = !isPolice && Roll() < recklessChance;
    }

    static readonly List<RoadElement> pruneTmp = new List<RoadElement>();

    // garde la mémoire (arrêt effectué, choix d'obéir...) des éléments de la tuile qu'on vient de quitter
    void PruneMemory()
    {
        pruneTmp.Clear();
        foreach (var k in memo.Keys) if (k == null || k.Node != prevNode) pruneTmp.Add(k);
        foreach (var k in obey.Keys) if (k == null || k.Node != prevNode) pruneTmp.Add(k);
        foreach (var k in pruneTmp) { memo.Remove(k); obey.Remove(k); }
        reported.Clear();
    }

    // ---------- Boucle principale ----------
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        

        if (nodes == null)
        {
            if (wanderRandomly && begun)
            {
                idleTimer += dt;
                if (idleTimer > 1f) { idleTimer = 0f; Wander(); }
            }
            return;
        }

        float desired = personalSpeed * GlobalSpeedMultiplier * speedBoost;
        desired = Mathf.Min(desired, CornerLimit());
        desired = Mathf.Min(desired, FrontLimit(dt));
        desired = Mathf.Min(desired, IntersectionLimit(dt));
        desired = Mathf.Min(desired, ElementsLimit(dt));
        
        if (arrestTimer > 0f) { arrestTimer -= dt; desired = 0f; }

        float rate = desired > currentSpeed ? acceleration : braking;
        currentSpeed = Mathf.MoveTowards(currentSpeed, desired, rate * dt);

        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);

            float align = Mathf.Clamp01(Vector3.Dot(transform.forward, dir.normalized));
            transform.position = Vector3.MoveTowards(transform.position, target, currentSpeed * align * dt);
        }

        if ((transform.position - target).sqrMagnitude < 0.0025f)
        {
            index++;
            if (index < waypoints.Count) { SetTarget(); return; }

            lastNode = approachNode;
            lastPrev = prevNode;

            nodes = null;
            approachNode = prevNode = -1;
            committed = false;
            if (wanderRandomly) Wander();
        }
    }

    // ---------- 1. Virages ----------
    float CornerLimit()
    {
        if (turn == TurnStraight) return float.MaxValue;
        float vc = personalSpeed * GlobalSpeedMultiplier * cornerSpeedFactor;
        return Mathf.Sqrt(vc * vc + 2f * braking * Dist(transform.position, target));
    }

    // ---------- 2. Voiture devant ----------
    float FrontLimit(float dt)
    {
        if (ignoreFrontTimer > 0f) { ignoreFrontTimer -= dt; return float.MaxValue; }

        float limit = float.MaxValue;
        CarAI blocker = null;

        foreach (var o in All)
        {
            if (o == this) continue;

            Vector3 d = o.transform.position - transform.position;
            d.y = 0f;
            float fwd = Vector3.Dot(d, transform.forward);
            if (fwd <= 0f || fwd > lookAhead) continue;
            if (Mathf.Abs(Vector3.Dot(d, transform.right)) > detectWidth) continue;

            float free = fwd - (carLength + o.carLength) * 0.5f - safeGap;
            float allowed = free <= 0f
                ? 0f
                : o.currentSpeed * 0.8f + Mathf.Sqrt(2f * braking * free);

            if (allowed < limit) { limit = allowed; blocker = o; }
        }

        bool stuck = limit < 0.1f && currentSpeed < 0.1f && blocker != null
                     && Vector3.Dot(transform.forward, blocker.transform.forward) < 0.5f;
        if (stuck)
        {
            blockedTime += dt;
            if (blockedTime > maxBlockedTime) { ignoreFrontTimer = 1.5f; blockedTime = 0f; }
        }
        else blockedTime = 0f;

        return limit;
    }

    // ---------- 3. Carrefours : priorité à droite ----------
    float IntersectionLimit(float dt)
    {
        var graph = RoadGraph.Instance;
        if (committed || approachNode < 0 || prevNode < 0) return float.MaxValue;
        if (!graph.IsIntersection(approachNode)) return float.MaxValue;

        Vector3 nPos = graph.NodePos(approachNode);
        float dist = Dist(transform.position, nPos);
        float stopDist = graph.NodeHalfSize(approachNode) + stopMargin;

        if (dist <= stopDist) { committed = true; return float.MaxValue; }
        if (dist > stopDist + awareness) return float.MaxValue;

        if (!MustYield(nPos, stopDist)) return float.MaxValue;

        // Conducteur imprudent : il fonce malgré la priorité => infraction + risque d'accident
        if (ignoreIntersection)
        {
            if (dist < stopDist + 3f) Report("Priorité refusée");
            return float.MaxValue;
        }

        if (currentSpeed < 0.2f) waitTime += dt;

        float free = Mathf.Max(0f, dist - (stopDist + 0.3f));
        return Mathf.Sqrt(2f * braking * free) * 0.9f;
    }

    bool MustYield(Vector3 nPos, float stopDist)
    {
        var graph = RoadGraph.Instance;
        Vector3 head = Dir(graph.NodePos(prevNode), nPos);
        Vector3 right = Vector3.Cross(Vector3.up, head);

        foreach (var b in All)
        {
            if (b == this || b.approachNode != approachNode) continue;
            if (b.prevNode < 0 || b.prevNode == prevNode) continue;

            if (!b.committed && Dist(b.transform.position, nPos) > stopDist + awareness) continue;

            Vector3 side = Dir(nPos, graph.NodePos(b.prevNode));
            bool bRight = Vector3.Dot(side, right) > 0.7f;
            bool bOncoming = Vector3.Dot(side, head) > 0.7f;

            if (b.committed)
            {
                if (turn == TurnRight)
                {
                    if (bOncoming && b.turn == TurnLeft) return true;
                }
                else if (bOncoming)
                {
                    if ((turn == TurnLeft) != (b.turn == TurnLeft)) return true;
                }
                else return true;
            }
            else if (bRight)
            {
                bool deadlock = waitTime > personalPatience && b.currentSpeed < 0.2f;
                if (!deadlock) return true;
            }
            else if (bOncoming && turn == TurnLeft && b.turn != TurnLeft)
            {
                return true;
            }
        }
        return false;
    }

    // ---------- 4. Éléments de voirie posés par le joueur ----------
    float ElementsLimit(float dt)
    {
        if (approachNode < 0 || prevNode < 0) return float.MaxValue;
        float limit = EvalElements(approachNode, false, dt);

        // éléments à effet local posés sur la tuile qu'on vient de traverser (moitié après le centre)
        var graph = RoadGraph.Instance;
        if (Dist(transform.position, graph.NodePos(prevNode)) < graph.NodeHalfSize(prevNode) + carLength * 0.5f)
            limit = Mathf.Min(limit, EvalElements(prevNode, true, dt));
        return limit;
    }

    float EvalElements(int node, bool localOnly, float dt)
    {
        var list = RoadElement.At(node);
        float limit = float.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (localOnly && !e.LocalEffect) continue;
            if (!e.Applies(this)) continue;
            limit = Mathf.Min(limit, e.Limit(this, dt));
        }
        return limit;
    }

    void OnDrawGizmosSelected()
    {
        if (waypoints == null) return;
        Gizmos.color = Color.yellow;
        for (int i = index; i < waypoints.Count; i++)
        {
            Gizmos.DrawSphere(waypoints[i] + Vector3.up, 0.3f);
            if (i > index) Gizmos.DrawLine(waypoints[i - 1] + Vector3.up, waypoints[i] + Vector3.up);
        }
    }
}
