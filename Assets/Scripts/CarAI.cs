using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// IA de voiture : plus court chemin, conduite à droite, vitesse variable,
/// détection de la voiture devant et priorité à droite aux carrefours.
/// </summary>
public class CarAI : MonoBehaviour
{
    [Header("Vitesse")]
    public float maxSpeed = 6f;
    [Range(0f, 0.5f)] public float speedVariation = 0.15f; // chaque voiture roule à ±15 % de maxSpeed
    public float acceleration = 4f;
    public float braking = 8f;
    [Range(0.1f, 1f)] public float cornerSpeedFactor = 0.4f; // vitesse dans les virages (fraction de la vitesse max)
    public float turnSpeed = 360f;                           // degrés / seconde

    /// <summary>Multiplicateur appliqué à toutes les voitures (ex : curseur de vitesse du jeu).</summary>
    public static float GlobalSpeedMultiplier = 1f;

    [Header("Trajet")]
    public bool wanderRandomly = true;
    [Tooltip("Décalage latéral vers la droite (≈ 1/4 de la largeur de la route)")]
    public float laneOffset = 1.5f;

    [Header("Détection de la voiture devant")]
    public float lookAhead = 12f;     // distance de détection
    [Tooltip("Demi-largeur du couloir surveillé. Doit rester < 2 x laneOffset sinon on détecte les voitures en sens inverse")]
    public float detectWidth = 1f;
    public float carLength = 4f;
    public float safeGap = 1.5f;      // distance mini laissée entre deux voitures
    public float maxBlockedTime = 5f; // anti-blocage face à face / croisement

    [Header("Carrefours (priorité à droite)")]
    [Tooltip("Distance entre le bord de la tuile de carrefour et le centre de la voiture à l'arrêt")]
    public float stopMargin = 2.5f;
    [Tooltip("Une voiture qui arrive à moins de cette distance du carrefour est prise en compte")]
    public float awareness = 10f;
    [Tooltip("Temps d'attente avant de passer quand tout le monde se bloque (4 voitures qui se font priorité à droite)")]
    public float patience = 2f;

    public float Speed => currentSpeed;

    // ---------- État ----------
    static readonly List<CarAI> All = new List<CarAI>();
    const int TurnLeft = -1, TurnStraight = 0, TurnRight = 1;

    List<int> nodes;
    List<Vector3> waypoints;
    List<int> turns;
    int index;
    Vector3 target;

    float currentSpeed;
    float personalSpeed;
    float personalPatience;

    // contexte du carrefour / noeud vers lequel on roule
    int approachNode = -1, prevNode = -1, turn;
    bool committed;      // a franchi la ligne d'arrêt, ne cède plus
    float waitTime;
    float blockedTime, ignoreFrontTimer;

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Awake()
    {
        SetMaxSpeed(maxSpeed);
        personalPatience = patience * Random.Range(0.7f, 1.5f); // évite que tout le monde parte en même temps
    }

    void Start()
    {
        if (wanderRandomly) GoTo(RoadGraph.Instance.RandomNode());
    }

    /// <summary>Change la vitesse max de cette voiture (la variation aléatoire est réappliquée).</summary>
    public void SetMaxSpeed(float v)
    {
        maxSpeed = v;
        personalSpeed = v * (1f + Random.Range(-speedVariation, speedVariation));
    }

    // ---------- Navigation ----------
    public bool GoTo(Vector3 worldPos) => GoTo(RoadGraph.Instance.WorldToNode(worldPos));

    public bool GoTo(int destinationNode)
    {
        var graph = RoadGraph.Instance;
        int start = graph.WorldToNode(transform.position);

        var path = graph.FindPath(start, destinationNode);
        if (path == null)
        {
            Debug.LogWarning($"{name} : pas de chemin de {start} vers {destinationNode}");
            return false;
        }

        BuildWaypoints(path);
        index = 0;
        SetTarget();
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
                float s = Vector3.Cross(Dir(a, c), Dir(c, b)).y;   // > 0 : virage à droite
                t = s > 0f ? TurnRight : TurnLeft;
            }

            waypoints.Add(c + offset * laneOffset);
            turns.Add(t);
        }
    }

    void SetTarget()
    {
        target = waypoints[index];
        target.y = transform.position.y;

        approachNode = nodes[index];
        prevNode = index > 0 ? nodes[index - 1] : -1;
        turn = turns[index];
        committed = false;
        waitTime = 0f;
    }

    // ---------- Boucle principale ----------
    void Update()
    {
        if (nodes == null) return;
        float dt = Time.deltaTime;

        // La vitesse voulue est le minimum de toutes les contraintes
        float desired = personalSpeed * GlobalSpeedMultiplier;
        desired = Mathf.Min(desired, CornerLimit());
        desired = Mathf.Min(desired, FrontLimit(dt));
        desired = Mathf.Min(desired, IntersectionLimit(dt));

        float rate = desired > currentSpeed ? acceleration : braking;
        currentSpeed = Mathf.MoveTowards(currentSpeed, desired, rate * dt);

        // Déplacement
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);

            float align = Mathf.Clamp01(Vector3.Dot(transform.forward, dir.normalized));
            transform.position = Vector3.MoveTowards(transform.position, target, currentSpeed * align * dt);
        }

        // Waypoint atteint
        if ((transform.position - target).sqrMagnitude < 0.0025f)
        {
            index++;
            if (index < waypoints.Count) { SetTarget(); return; }

            nodes = null;
            approachNode = prevNode = -1;
            committed = false;
            if (wanderRandomly) GoTo(RoadGraph.Instance.RandomNode());
        }
    }

    // ---------- 1. Ralentir dans les virages ----------
    float CornerLimit()
    {
        if (turn == TurnStraight) return float.MaxValue;
        float vc = personalSpeed * GlobalSpeedMultiplier * cornerSpeedFactor;
        // v² = vc² + 2·a·d : on arrive au virage exactement à la vitesse vc
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

            float free = fwd - carLength - safeGap;                       // place restante avant la collision
            float allowed = free <= 0f
                ? 0f
                : o.currentSpeed * 0.8f + Mathf.Sqrt(2f * braking * free); // peut suivre l'autre + freiner à temps

            if (allowed < limit) { limit = allowed; blocker = o; }
        }

        // Anti-blocage : face à face / croisement bloqué trop longtemps (jamais pour une file d'attente)
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

        if (dist <= stopDist) { committed = true; return float.MaxValue; }   // ligne franchie
        if (dist > stopDist + awareness) return float.MaxValue;               // encore loin

        if (!MustYield(nPos, stopDist)) return float.MaxValue;

        if (currentSpeed < 0.2f) waitTime += dt;

        // S'arrêter juste avant la ligne (0,3 de marge pour ne pas la franchir en glissant)
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
            if (b.prevNode < 0 || b.prevNode == prevNode) continue;   // même file : géré par la détection devant

            if (!b.committed && Dist(b.transform.position, nPos) > stopDist + awareness) continue;

            Vector3 side = Dir(nPos, graph.NodePos(b.prevNode));      // d'où vient b
            bool bRight = Vector3.Dot(side, right) > 0.7f;
            bool bOncoming = Vector3.Dot(side, head) > 0.7f;

            if (b.committed)
            {
                // Déjà engagée dans le carrefour : on attend si les trajectoires se croisent
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
                // Priorité à droite, sauf si on est bloqués en cercle (la voiture de droite attend aussi)
                bool deadlock = waitTime > personalPatience && b.currentSpeed < 0.2f;
                if (!deadlock) return true;
            }
            else if (bOncoming && turn == TurnLeft && b.turn != TurnLeft)
            {
                return true;   // tourne à gauche : cède à la voiture en face
            }
        }
        return false;
    }

    // ---------- Debug : trajet de la voiture sélectionnée ----------
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