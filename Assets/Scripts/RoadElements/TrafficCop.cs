using UnityEngine;

/// <summary>
/// Agent de circulation (intersections T et X uniquement, voir ToolDef.onlyOnIntersections).
///  - deux phases, une par axe : le vert va à l'axe qui a le plus de voitures en approche ;
///  - tout est bloqué (rouge pour tous) tant que des piétons sont à proximité ;
///  - l'agent pivote de 90° pour faire face au trafic qu'il bloque.
/// Placement du prefab : CenterShort.
/// </summary>
public class TrafficCop : RoadElement
{
    [Header("Obéissance")]
    [Range(0f, 1f), Tooltip("Probabilité qu'une voiture respecte l'agent")]
    public float obeyChance = 0.95f;

    [Header("Cycle des voitures")]
    [Tooltip("Durée minimale d'un vert quand des voitures attendent sur la file de ce vert")]
    public float minGreen = 3f;
    [Tooltip("Durée maximale d'un vert si l'autre axe attend (évite la famine)")] 
    public float maxGreen = 12f;
    [Tooltip("L'autre axe doit avoir au moins N voitures de plus pour reprendre le vert avant maxGreen")]
    public int switchMargin = 1;
    [Tooltip("Tout-rouge minimal entre deux phases (les voitures déjà engagées finissent de passer)")]
    public float minClearance = 0.8f;
    public float maxClearance = 4f;
    [Tooltip("Distance (m) jusqu'à laquelle une voiture en approche est comptée dans la file")]
    public float queueRange = 25f;

    [Header("Piétons")]
    public bool stopForPedestrians = true;
    [Tooltip("Marge (m) autour de la tuile : un piéton dans cette zone bloque les voitures")]
    public float pedestrianMargin = 3f;
    [Tooltip("Coché : seuls les piétons en train de traverser comptent. Décoché : tout piéton dans la zone")]
    public bool onlyCrossingPedestrians = false;
    [Tooltip("Temps (s) sans piéton avant de rouvrir")]
    public float pedestrianReleaseDelay = 0.8f;
    [Tooltip("Durée max (s) d'un blocage piétons")]
    public float maxPedestrianHold = 8f;
    [Tooltip("Après un blocage piétons, durée (s) pendant laquelle les piétons ne rebloquent pas (les voitures passent)")]
    public float pedestrianCooldown = 5f;

    [Header("Animation")]
    [Tooltip("Optionnel : le modèle à pivoter (enfant du prefab). Sinon la racine pivote.")]
    public Transform visual;
    public float turnSpeed = 360f;
    [Tooltip("Correction (°) si le modèle ne regarde pas vers +Z : essayez 90, 180 ou -90")]
    public float facingYawOffset = 0f;

    public bool debugLogs = false;

    protected override bool ForceAllDirections => true;   // l'agent gère lui-même les deux axes
    public override bool LocalEffect => false;

    // ---------- État ----------
    enum Stage { Go, Clearing, PedHold }
    Stage stage, afterClearing;
    int greenAxis, nextAxis;                  // 0 = axe X, 1 = axe Z
    float stageTime, cooldown, pedFree;
    bool needInit = true;
    float currentYaw, targetYaw;
    readonly int[] waiting = new int[2];
    readonly Vector3[] faceDir = { Vector3.right, Vector3.forward };   // direction regardée quand l'axe i est bloqué

    Transform V => visual != null ? visual : transform;

    // ---------- API statique (utilisée par CarAI) ----------
    public static TrafficCop FindAt(int node)
    {
        var list = RoadElement.At(node);
        for (int i = 0; i < list.Count; i++)
            if (list[i] is TrafficCop c) return c;
        return null;
    }

    static int AxisOf(Vector3 d) => Mathf.Abs(d.x) > Mathf.Abs(d.z) ? 0 : 1;

    // ---------- Cycle de vie ----------
    protected override void OnPlaced() => Init();
    public override void ResetState() => Init();

    void Init()
    {
        ComputeFaces();
        stage = Stage.Go;
        greenAxis = 0;
        stageTime = cooldown = pedFree = 0f;
        needInit = true;
        Face(1 - greenAxis, true);
    }

    // Pour chaque axe : le bras vers lequel l'agent se tourne pour bloquer cet axe
    void ComputeFaces()
    {
        var g = RoadGraph.Instance;
        for (int axis = 0; axis < 2; axis++)
        {
            Vector3 plus = axis == 0 ? Vector3.right : Vector3.forward;
            faceDir[axis] = g.HasArm(Node, plus) ? plus : (g.HasArm(Node, -plus) ? -plus : plus);
        }
    }

    void Face(int blockedAxis, bool snap)
    {
        Vector3 d = faceDir[blockedAxis];
        targetYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + facingYawOffset;
        if (snap) { currentYaw = targetYaw; V.rotation = Quaternion.Euler(0f, currentYaw, 0f); }
    }

    void Update()
    {
        if (!IsPlaced) return;
        currentYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, turnSpeed * Time.deltaTime);
        V.rotation = Quaternion.Euler(0f, currentYaw, 0f);
    }

    // ---------- Décision (une fois par tick physique) ----------
    void FixedUpdate()
    {
        if (!IsPlaced) return;
        float dt = Time.fixedDeltaTime;
        stageTime += dt;
        if (cooldown > 0f) cooldown -= dt;

        CountWaiting();
        bool peds = stopForPedestrians && PedestriansNear();
        pedFree = peds ? 0f : pedFree + dt;

        if (needInit)   // premier tick de la simulation : on commence par l'axe qui a le plus de voitures
        {
            needInit = false;
            greenAxis = waiting[1] > waiting[0] ? 1 : 0;
            stageTime = 0f;
            Face(1 - greenAxis, true);
        }

        switch (stage)
        {
            case Stage.Go:
            {
                if (peds && cooldown <= 0f) { BeginClearing(Stage.PedHold, greenAxis); break; }

                int cur = waiting[greenAxis], oth = waiting[1 - greenAxis];
                bool canSwitch = stageTime >= (cur == 0 ? 0.5f : minGreen);
                bool want = oth > 0 && (cur == 0 || oth >= cur + switchMargin || stageTime >= maxGreen);
                if (canSwitch && want) BeginClearing(Stage.Go, 1 - greenAxis);
                break;
            }
            case Stage.Clearing:
                if (stageTime >= maxClearance || (stageTime >= minClearance && !Busy()))
                {
                    if (afterClearing == Stage.PedHold) SetStage(Stage.PedHold);
                    else { greenAxis = nextAxis; SetStage(Stage.Go); }
                }
                break;

            case Stage.PedHold:
                if ((!peds && pedFree >= pedestrianReleaseDelay) || stageTime >= maxPedestrianHold)
                {
                    cooldown = pedestrianCooldown;
                    greenAxis = waiting[1] > waiting[0] ? 1 : (waiting[0] > waiting[1] ? 0 : greenAxis);
                    Face(1 - greenAxis, false);
                    SetStage(Stage.Go);
                }
                break;
        }
    }

    // Passage au tout-rouge. Si la phase suivante est un vert, l'agent se tourne tout de suite vers
    // l'axe qui vient de perdre le vert (il le bloque), puis le vert de l'autre axe démarre après le dégagement.
    void BeginClearing(Stage after, int next)
    {
        afterClearing = after;
        nextAxis = next;
        if (after == Stage.Go) Face(1 - next, false);
        SetStage(Stage.Clearing);
    }

    void SetStage(Stage s)
    {
        stage = s;
        stageTime = 0f;
        if (debugLogs)
            Debug.Log($"[Agent] noeud {Node} : {s}, vert = {(s == Stage.Go ? (greenAxis == 0 ? "axe X" : "axe Z") : "aucun")} " +
                      $"(file X={waiting[0]}, file Z={waiting[1]})", this);
    }

    float CommitDist(CarAI car) => RoadGraph.Instance.NodeHalfSize(Node) + car.stopMargin;   // même seuil que CarAI.committed

    // Voitures en approche (pas encore engagées) par axe
    void CountWaiting()
    {
        waiting[0] = waiting[1] = 0;
        foreach (var car in CarAI.Cars)
        {
            if (car == null || car.ApproachNode != Node || car.PrevNode < 0) continue;
            float d = car.DistToNode;
            if (d > queueRange || d <= CommitDist(car)) continue;
            waiting[AxisOf(car.ApproachDir)]++;
        }
    }

    // Une voiture est engagée dans la moitié d'entrée de la tuile ?
    bool Busy()
    {
        foreach (var car in CarAI.Cars)
        {
            if (car == null || car.ApproachNode != Node || car.PrevNode < 0) continue;
            if (car.DistToNode <= CommitDist(car)) return true;
        }
        return false;
    }

    bool PedestriansNear()
    {
        var g = RoadGraph.Instance;
        Vector3 c = g.NodePos(Node);
        float r = g.NodeHalfSize(Node) + pedestrianMargin;
        foreach (var p in Pedestrian.All)
        {
            if (p == null) continue;
            if (onlyCrossingPedestrians && !p.IsCrossing) continue;
            Vector3 d = p.transform.position - c;
            if (Mathf.Abs(d.x) <= r && Mathf.Abs(d.z) <= r) return true;
        }
        return false;
    }

    bool IsRed(int axis) => stage != Stage.Go || axis != greenAxis;

    // ---------- Effet sur les voitures ----------
    public override float Limit(CarAI car, float dt)
    {
        if (car.PrevNode < 0 || car.ApproachNode != Node) return float.MaxValue;
        if (!IsRed(AxisOf(car.ApproachDir))) return float.MaxValue;

        float dist = car.DistToNode;
        float commit = CommitDist(car);
        if (dist <= commit) return float.MaxValue;          // déjà engagée : on la laisse finir de passer

        if (!car.Obeys(this, obeyChance))
        {
            if (dist < commit + 3f) car.Report("Agent de circulation ignoré");
            return float.MaxValue;
        }

        // arrêt juste AVANT le seuil d'engagement (sinon elle passerait "committed" et bloquerait les autres)
        float free = dist - (commit + 0.4f);
        return free <= 0f ? 0f : Mathf.Sqrt(2f * car.braking * free) * 0.9f;
    }
}