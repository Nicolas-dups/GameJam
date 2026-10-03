using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Piéton concret qui se déplace dans le RoadGraph comme une voiture, mais :
///  - sur le "trottoir" (offset plus grand que celui des voitures) ;
///  - sans côté privilégié : chaque trajet choisit le côté le plus proche de sa position actuelle ;
///  - il traverse TOUJOURS aux intersections (les bras sans passage posé sont traversés quand même) ;
///  - ailleurs, s'il n'y a pas de passage à proximité, il peut décider de traverser n'importe où
///    (jaywalkChance) => risque d'accident ;
///  - s'il y a un passage posé par le joueur (PedestrianCrossing) sur une tuile droite, il peut l'utiliser.
/// Simulation dans FixedUpdate (déterministe, comme les voitures).
/// </summary>
public class Pedestrian : MonoBehaviour
{
    [Header("Déplacement")]
    public float walkSpeed = 1.6f;
    public float crossSpeed = 2.4f;
    [Range(0f, 0.4f)] public float speedVariation = 0.2f;
    public float turnSpeed = 540f;
    [Tooltip("Distance entre l'axe de la route et le piéton (plus grand que laneOffset des voitures)")]
    public float sidewalkOffset = 4.5f;
    [Tooltip("Décalage vertical (0 si l'origine du prefab est aux pieds ; 0.5 pour la capsule de secours)")]
    public float heightOffset = 0f;

    [Header("Animation (Animator avec les booléens Walking et Running)")]
    [Tooltip("Au-dessus de cette vitesse réelle (m/s) : Running. Entre 0.15 et ce seuil : Walking. En dessous : immobile")]
    public float runSpeedThreshold = 3f;
    [Tooltip("Adapte la vitesse de l'Animator à la vitesse réelle (évite l'effet patinage)")]
    public bool syncAnimatorSpeed = true;
    [Tooltip("Vitesse (m/s) à laquelle l'animation de marche est jouée à vitesse normale")]
    public float walkAnimRefSpeed = 1.5f;
    [Tooltip("Vitesse (m/s) à laquelle l'animation de course est jouée à vitesse normale")]
    public float runAnimRefSpeed = 4f;

    [Header("Traversée")]
    [Range(0f, 1f), Tooltip("Probabilité que ce piéton regarde les voitures avant de traverser")]
    public float carefulChance = 0.8f;
    [Tooltip("Une voiture en mouvement plus proche que ça du passage fait attendre un piéton prudent")]
    public float carDangerDistance = 9f;
    [Tooltip("Idem quand un passage piéton posé par le joueur est sur la tuile (il fait confiance aux voitures)")]
    public float markedDangerDistance = 3.5f;
    [Tooltip("Attente max avant de traverser quand même")]
    public float maxWaitTime = 4f;
    [Range(0f, 1f), Tooltip("Sur une tuile droite avec un passage posé : probabilité de l'utiliser")]
    public float useMarkedCrossingChance = 0.6f;
    [Range(0f, 1f), Tooltip("Tendance à traverser n'importe où : sur une tuile droite sans passage proche, probabilité de traverser hors passage piéton")]
    public float jaywalkChance = 0.15f;

    // ---------- Registre ----------
    static readonly List<Pedestrian> all = new List<Pedestrian>();
    public static IReadOnlyList<Pedestrian> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => all.Clear();

    void OnEnable() => all.Add(this);
    void OnDisable() => all.Remove(this);

    // ---------- API pour les voitures / éléments ----------
    /// <summary>Le piéton est en train de traverser une chaussée.</summary>
    public bool IsCrossing { get; private set; }
    /// <summary>Traverse hors passage (aucune intersection ni passage posé).</summary>
    public bool IsJaywalking { get; private set; }
    /// <summary>Noeud (tuile) sur lequel il traverse, -1 sinon.</summary>
    public int CrossNode { get; private set; } = -1;
    /// <summary>Direction de marche actuelle (horizontale).</summary>
    public Vector3 MoveDirection { get; private set; }

    /// <summary>Point d'arrivée du tronçon en cours (le piéton y va en ligne droite).</summary>
    public Vector3 CurrentTarget => hasTarget ? current.pos : transform.position;
    /// <summary>Vitesse de déplacement actuelle (m/s), 0 si immobile ou en attente.</summary>
    public float MoveSpeed { get; private set; }

    // ---------- État ----------
    struct Step
    {
        public Vector3 pos;
        public bool crossing;   // le trajet vers ce point traverse une chaussée
        public bool marked;     // passage piéton posé par le joueur
        public int node;
    }

    readonly Queue<Step> steps = new Queue<Step>();
    Step current;
    bool hasTarget, begun, crossingStarted;
    List<int> path;
    int pathIndex, curNode, side = 1;
    float waitTime, idleTimer, speedFactor;
    bool careful;
    System.Random rng = new System.Random();
    float Roll() => (float)rng.NextDouble();

    // =================================================================
    //  Démarrage
    // =================================================================
    public void Begin(int node, int seed)
    {
        rng = new System.Random(seed);
        speedFactor = 1f + (Roll() * 2f - 1f) * speedVariation;
        careful = Roll() < carefulChance;
        curNode = node;
        begun = true;

        // Apparition sur un COIN de trottoir de la tuile (±offset, ±offset) : jamais sur la chaussée
        Vector3 p = RoadGraph.Instance.NodePos(node);
        p.y += heightOffset;
        p.x += (Roll() < 0.5f ? 1f : -1f) * sidewalkOffset;
        p.z += (Roll() < 0.5f ? 1f : -1f) * sidewalkOffset;
        transform.position = p;

        PlanPath();
    }

    // =================================================================
    //  Planification
    // =================================================================
    void PlanPath()
    {
        var g = RoadGraph.Instance;
        for (int i = 0; i < 10; i++)
        {
            int dest = g.RandomNode(rng);
            if (dest == curNode) continue;
            var p = g.FindPath(curNode, dest, true);          // ignore barrages / sens uniques
            if (p == null || p.Count < 2) continue;

            path = p;
            pathIndex = 0;

            // côté le plus proche de la position actuelle (aucune préférence de côté)
            Vector3 dir = RoadGraph.SnapAxis(g.NodePos(p[1]) - g.NodePos(p[0]));
            Vector3 r = Vector3.Cross(Vector3.up, dir);
            Vector3 c = g.NodePos(curNode);
            Vector3 cur = transform.position;
            float dPlus = Flat(c + (dir + r) * sidewalkOffset, cur);     // coin de départ du trajet, côté +
            float dMinus = Flat(c + (dir - r) * sidewalkOffset, cur);    // idem côté -
            side = Mathf.Abs(dPlus - dMinus) < 0.01f ? (Roll() < 0.5f ? 1 : -1) : (dPlus < dMinus ? 1 : -1);

            steps.Clear();
            hasTarget = false;
            crossingStarted = false;
            EnqueueNode(0);
            return;
        }
        path = null;   // pas de chemin : on réessaiera
    }

    static float Flat(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    static Vector3 Right(Vector3 d) => Vector3.Cross(Vector3.up, d);
    static int Sgn(float v) => v >= 0f ? 1 : -1;

    void Push(Vector3 pos, int node, bool crossing = false, bool marked = false)
    {
        steps.Enqueue(new Step { pos = pos, node = node, crossing = crossing, marked = marked });
    }

    bool NearIntersection(int node)
    {
        var g = RoadGraph.Instance;
        if (g.IsIntersection(node)) return true;
        foreach (int nb in g.Neighbors(node)) if (g.IsIntersection(nb)) return true;
        return false;
    }

    /// <summary>Génère les points de passage du noeud path[k] (avec les traversées éventuelles).</summary>
    void EnqueueNode(int k)
    {
        var g = RoadGraph.Instance;
        int last = path.Count - 1;
        int node = path[k];
        float off = sidewalkOffset;

        Vector3 c = g.NodePos(node);
        c.y += heightOffset;

        Vector3 din = k > 0 ? RoadGraph.SnapAxis(g.NodePos(node) - g.NodePos(path[k - 1])) : Vector3.zero;
        Vector3 dout = k < last ? RoadGraph.SnapAxis(g.NodePos(path[k + 1]) - g.NodePos(node)) : Vector3.zero;

        bool isStart = k == 0, isEnd = k == last;
        Vector3 rIn = isStart ? Vector3.zero : Right(din);
        bool straight = !isStart && !isEnd && Vector3.Dot(din, dout) > 0.9f;

        // ---- Tuile droite sans rue latérale : traversée volontaire (passage posé) ou sauvage ----
        if (straight && !g.IsIntersection(node))
        {
            bool marked = PedestrianCrossing.IsAt(node);
            bool wantCross = marked ? Roll() < useMarkedCrossingChance
                                    : (!NearIntersection(node) && Roll() < jaywalkChance);
            if (wantCross)
            {
                Push(c + rIn * side * off, node);
                side = -side;                                        // on change de trottoir
                Push(c + rIn * side * off, node, true, marked);
            }
            else Push(c + rIn * side * off, node);
            return;
        }

        // ---- Virage ou intersection : on circule sur les 4 coins (±off, ±off) du noeud ----
        // Passer d'un coin à un coin voisin = traverser le bras situé entre les deux.
        Vector2Int E, X;
        if (isStart)
        {
            // départ : coin où se trouve le piéton -> coin de sortie vers le premier tronçon
            Vector3 rel = transform.position - c;
            E = new Vector2Int(Mathf.Abs(rel.x) > 0.5f ? Sgn(rel.x) : (Roll() < 0.5f ? 1 : -1),
                               Mathf.Abs(rel.z) > 0.5f ? Sgn(rel.z) : (Roll() < 0.5f ? 1 : -1));
            Vector3 xs = dout + Right(dout) * side;
            X = new Vector2Int(Sgn(xs.x), Sgn(xs.z));
        }
        else if (isEnd)
        {
            // arrivée : on s'arrête sur le coin d'entrée (trottoir), pas au centre de la tuile
            Vector3 es = -din + rIn * side;
            E = new Vector2Int(Sgn(es.x), Sgn(es.z));
            X = E;
        }
        else
        {
            Vector3 e = -din + rIn * side;                 // coin d'entrée
            Vector3 x = dout + Right(dout) * side;         // coin de sortie
            E = new Vector2Int(Sgn(e.x), Sgn(e.z));
            X = new Vector2Int(Sgn(x.x), Sgn(x.z));
        }

        var pts = new List<Vector2Int> { E };
        if (E != X)
        {
            if (E.x != X.x && E.y != X.y)
            {
                // coins opposés : deux chemins possibles, on prend celui qui traverse le moins de bras
                var viaA = new Vector2Int(X.x, E.y);
                var viaB = new Vector2Int(E.x, X.y);
                int costA = Arm(node, 0, E.y) + Arm(node, X.x, 0);
                int costB = Arm(node, E.x, 0) + Arm(node, 0, X.y);
                bool useA = costA < costB || (costA == costB && Roll() < 0.5f);
                pts.Add(useA ? viaA : viaB);
            }
            pts.Add(X);
        }

        var cross = new List<bool>();
        bool any = false;
        for (int i = 1; i < pts.Count; i++)
        {
            Vector2Int P = pts[i - 1], Q = pts[i];
            // déplacement en x à z constant => on traverse le bras orienté en z (et inversement)
            bool has = P.x != Q.x ? g.HasArm(node, new Vector3(0f, 0f, P.y))
                                  : g.HasArm(node, new Vector3(P.x, 0f, 0f));
            cross.Add(has);
            any |= has;
        }

        if (straight && !any)
        {
            Push(c + rIn * side * off, node);              // rien à traverser : un seul point suffit
            return;
        }

        Push(CornerPos(c, pts[0], off), node);
        for (int i = 1; i < pts.Count; i++)
            Push(CornerPos(c, pts[i], off), node, cross[i - 1], PedestrianCrossing.IsAt(node));
    }

    int Arm(int node, int sx, int sz) => RoadGraph.Instance.HasArm(node, new Vector3(sx, 0f, sz)) ? 1 : 0;

    static Vector3 CornerPos(Vector3 c, Vector2Int s, float off) => c + new Vector3(s.x, 0f, s.y) * off;

    // =================================================================
    //  Déplacement
    // =================================================================
    // ---------- Animation ----------
    Animator anim;
    bool hasWalking, hasRunning;
    static readonly int WalkingHash = Animator.StringToHash("Walking");
    static readonly int RunningHash = Animator.StringToHash("Running");
    float smoothSpeed;

    void Awake()
    {
        anim = GetComponentInChildren<Animator>();
        if (anim == null) return;
        foreach (var prm in anim.parameters)
        {
            if (prm.nameHash == WalkingHash && prm.type == AnimatorControllerParameterType.Bool) hasWalking = true;
            if (prm.nameHash == RunningHash && prm.type == AnimatorControllerParameterType.Bool) hasRunning = true;
        }
        if (!hasWalking || !hasRunning)
            Debug.LogWarning($"Animator de '{name}' : booléens 'Walking' et/ou 'Running' introuvables.", this);
    }

    void UpdateAnimator(float measuredSpeed, float dt)
    {
        if (anim == null) return;

        // lissage : évite le clignotement entre les états
        smoothSpeed = Mathf.Lerp(smoothSpeed, measuredSpeed, 1f - Mathf.Exp(-10f * dt));

        bool running = smoothSpeed >= runSpeedThreshold;
        bool walking = !running && smoothSpeed > 0.15f;

        if (hasWalking) anim.SetBool(WalkingHash, walking);
        if (hasRunning) anim.SetBool(RunningHash, running);

        if (syncAnimatorSpeed)
        {
            float refSpeed = running ? runAnimRefSpeed : walkAnimRefSpeed;
            anim.speed = (walking || running) ? Mathf.Clamp(smoothSpeed / Mathf.Max(0.01f, refSpeed), 0.6f, 1.6f) : 1f;
        }
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Vector3 before = transform.position;
        Simulate(dt);
        Vector3 moved = transform.position - before;
        moved.y = 0f;
        UpdateAnimator(moved.magnitude / dt, dt);
    }

    void Simulate(float dt)
    {
        MoveSpeed = 0f;
        if (!begun) return;

        if (path == null)
        {
            idleTimer += dt;
            if (idleTimer > 1f) { idleTimer = 0f; PlanPath(); }
            return;
        }

        if (!hasTarget)
        {
            if (steps.Count == 0)
            {
                curNode = path[pathIndex];
                pathIndex++;
                if (pathIndex >= path.Count) PlanPath();     // arrivé : nouvelle destination
                else EnqueueNode(pathIndex);
                if (path == null || steps.Count == 0) return;
            }
            current = steps.Dequeue();
            hasTarget = true;
            waitTime = 0f;
            crossingStarted = false;
        }

        // Avant de traverser : un piéton prudent regarde les voitures
        if (current.crossing && !crossingStarted)
        {
            if (ShouldWait(current)) { waitTime += dt; MoveDirection = Vector3.zero; return; }
            crossingStarted = true;
            IsCrossing = true;
            CrossNode = current.node;
            IsJaywalking = !current.marked && !RoadGraph.Instance.IsIntersection(current.node);
        }

        Vector3 to = current.pos - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        float speed = (current.crossing ? crossSpeed : walkSpeed) * speedFactor;

        if (dist > 0.01f)
        {
            MoveSpeed = speed;
            Vector3 dir = to / dist;
            MoveDirection = dir;
            var look = Quaternion.LookRotation(dir, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
            Vector3 np = Vector3.MoveTowards(transform.position, current.pos, speed * dt);
            np.y = current.pos.y;
            transform.position = np;
        }

        if (dist <= speed * dt + 0.02f)
        {
            transform.position = current.pos;
            hasTarget = false;
            if (current.crossing) { IsCrossing = false; IsJaywalking = false; CrossNode = -1; }
        }
    }

    bool ShouldWait(Step s)
    {
        if (!careful || waitTime >= maxWaitTime) return false;

        Vector3 seg = s.pos - transform.position;
        seg.y = 0f;
        if (seg.sqrMagnitude < 0.01f) return false;
        Vector3 u = seg.normalized;                                   // sens de la traversée
        Vector3 mid = (transform.position + s.pos) * 0.5f;

        float minDist = (s.marked || PedestrianCrossing.IsAt(s.node)) ? markedDangerDistance : carDangerDistance;

        foreach (var car in CarAI.Cars)
        {
            // une voiture à l'arrêt ne bloque pas (sinon blocage mutuel avec la voiture qui nous laisse passer)
            if (car == null || car.IsArrested || car.Speed < 0.3f) continue;

            Vector3 fwd = car.transform.forward;
            fwd.y = 0f;
            fwd.Normalize();

            Vector3 d = mid - car.transform.position;
            d.y = 0f;
            float ahead = Vector3.Dot(d, fwd);
            if (ahead < -car.halfLength) continue;                     // déjà passée

            float stopDist = car.Speed * car.Speed / (2f * car.braking) + car.halfLength + 1.5f;
            float range = Mathf.Max(minDist, stopDist);

            // Voiture qui arrive sur cette tuile ou vient d'en repartir : elle peut tourner vers ce passage,
            // même si elle roule pour l'instant parallèlement à ma traversée
            bool atTile = car.ApproachNode == s.node || car.PrevNode == s.node;
            if (atTile)
            {
                if (d.magnitude <= range + 6f) return true;
                continue;
            }

            // Autre voiture : seulement si elle coupe ma traversée
            if (Mathf.Abs(Vector3.Dot(fwd, u)) > 0.6f) continue;
            if (Mathf.Abs(Vector3.Dot(d, u)) > 6f) continue;
            if (ahead <= range) return true;
        }
        return false;
    }
}