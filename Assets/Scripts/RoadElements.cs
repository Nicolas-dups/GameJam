using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  Base : un élément de voirie est posé sur une tuile (noeud) et agit sur
//  les voitures qui APPROCHENT de cette tuile. Il renvoie une vitesse max.
//  (Les classes sont créées par code via AddComponent : pas besoin de les
//   mettre sur des prefabs.)
// =====================================================================
public abstract class RoadElement : MonoBehaviour
{
    static readonly List<RoadElement> all = new List<RoadElement>();
    static readonly Dictionary<int, List<RoadElement>> byNode = new Dictionary<int, List<RoadElement>>();
    static readonly List<RoadElement> none = new List<RoadElement>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { all.Clear(); byNode.Clear(); }

    public static IReadOnlyList<RoadElement> All => all;
    public static IReadOnlyList<RoadElement> At(int node) => byNode.TryGetValue(node, out var l) ? l : none;

    public int Node { get; private set; }
    public Vector3 Facing { get; private set; }
    public bool AllDirections { get; private set; }
    bool registered;

    /// <summary>Vrai : agit dans les deux sens de l'axe (dos d'âne, passage piéton).</summary>
    protected virtual bool Symmetric => false;
    /// <summary>Vrai : agit toujours sur toutes les directions (feux, agent).</summary>
    protected virtual bool ForceAllDirections => false;

    protected float Half => RoadGraph.Instance.NodeHalfSize(Node);
    protected Vector3 Center => RoadGraph.Instance.NodePos(Node);
    protected Vector3 SideOffset => new Vector3(Half * 0.85f, 0f, -Half * 0.6f); // local : droite de la route

    public void Place(int node, Vector3 facing, bool allDirections)
    {
        Node = node;
        Facing = facing.normalized;
        AllDirections = allDirections || ForceAllDirections;
        if (!byNode.TryGetValue(node, out var l)) byNode[node] = l = new List<RoadElement>();
        l.Add(this);
        all.Add(this);
        registered = true;
        OnPlaced();
    }

    public void Remove()
    {
        Unregister();
        OnRemoved();
        Destroy(gameObject);
    }

    void Unregister()
    {
        if (!registered) return;
        registered = false;
        all.Remove(this);
        if (byNode.TryGetValue(Node, out var l)) l.Remove(this);
    }

    void OnDestroy() => Unregister();

    public bool Applies(CarAI car)
    {
        if (AllDirections) return true;
        float d = Vector3.Dot(car.ApproachDir, Facing);
        return Symmetric ? Mathf.Abs(d) > 0.5f : d > 0.5f;
    }

    protected virtual void OnPlaced() { }
    protected virtual void OnRemoved() { }
    /// <summary>Remet l'élément à zéro (début de chaque essai).</summary>
    public virtual void ResetState() { }
    /// <summary>Appelé au lancement d'un essai (ex : la police fait apparaître sa voiture).</summary>
    public virtual void OnRunStart(int seed) { }
    /// <summary>Vitesse max imposée à cette voiture (float.MaxValue = aucune contrainte).</summary>
    public virtual float Limit(CarAI car, float dt) => float.MaxValue;

    // ---------- Aides ----------
    /// <summary>Distance jusqu'à la ligne d'arrêt (négatif = ligne franchie).</summary>
    protected float LineDistance(CarAI car) => car.DistToNode - (Half + car.stopMargin);

    protected static float BrakeSpeed(CarAI car, float freeDist) =>
        Mathf.Sqrt(2f * car.braking * Mathf.Max(0f, freeDist)) * 0.9f;

    /// <summary>Vitesse permettant d'arriver à `target` après `dist` mètres en freinant normalement.</summary>
    protected static float SlowTo(CarAI car, float target, float dist) =>
        Mathf.Sqrt(target * target + 2f * car.braking * Mathf.Max(0f, dist));

    protected GameObject Prim(PrimitiveType type, Vector3 localPos, Vector3 localScale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(transform, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().material.color = color;
        return go;
    }

    protected GameObject SignPost(Color color, PrimitiveType shape, Vector3 headScale, Vector3 headEuler)
    {
        Vector3 s = SideOffset;
        Prim(PrimitiveType.Cylinder, s + Vector3.up * 1.25f, new Vector3(0.1f, 1.25f, 0.1f), Color.gray);
        var head = Prim(shape, s + Vector3.up * 2.7f, headScale, color);
        head.transform.localEulerAngles = headEuler;
        return head;
    }
}

// =====================================================================
//  STOP : arrêt obligatoire `waitTime` secondes devant la ligne
// =====================================================================
public class StopSign : RoadElement
{
    public float waitTime = 2f;

    protected override void OnPlaced() =>
        SignPost(Color.red, PrimitiveType.Cylinder, new Vector3(1.1f, 0.05f, 1.1f), new Vector3(90, 0, 0));

    public override float Limit(CarAI car, float dt)
    {
        float d = LineDistance(car);
        if (d > 12f) return float.MaxValue;
        if (car.GetMemo(this) >= waitTime) return float.MaxValue;     // arrêt effectué

        if (d <= 0f) { car.Report("Stop grillé"); return float.MaxValue; }
        if (!car.Obeys(this)) return float.MaxValue;

        if (d < 1.2f && car.Speed < 0.2f) car.SetMemo(this, car.GetMemo(this) + dt);
        return BrakeSpeed(car, d - 0.3f);
    }
}

// =====================================================================
//  CÉDER LE PASSAGE : ralentit, s'arrête seulement s'il y a du monde
// =====================================================================
public class YieldSign : RoadElement
{
    public float approachSpeed = 3f;
    public float maxWait = 6f;

    protected override void OnPlaced() =>
        SignPost(Color.yellow, PrimitiveType.Cube, new Vector3(1.1f, 1.1f, 0.05f), new Vector3(0, 0, 45));

    bool CrossTraffic(CarAI car)
    {
        foreach (var o in CarAI.Cars)
        {
            if (o == car || o.ApproachNode != Node || o.PrevNode < 0 || o.PrevNode == car.PrevNode) continue;
            if (o.DistToNode < 14f) return true;
        }
        return false;
    }

    public override float Limit(CarAI car, float dt)
    {
        float d = LineDistance(car);
        if (d > 14f) return float.MaxValue;

        bool traffic = CrossTraffic(car);
        if (d <= 0f)
        {
            if (traffic && !car.Obeys(this)) car.Report("Refus de céder le passage");
            return float.MaxValue;
        }
        if (!car.Obeys(this)) return float.MaxValue;

        float slow = SlowTo(car, approachSpeed, d);
        if (!traffic || car.GetMemo(this) >= maxWait) return slow;

        if (car.Speed < 0.2f) car.SetMemo(this, car.GetMemo(this) + dt);   // anti-blocage
        return Mathf.Min(slow, BrakeSpeed(car, d - 0.3f));
    }
}

// =====================================================================
//  LIMITATION DE VITESSE
// =====================================================================
public class SpeedLimitSign : RoadElement
{
    public float limit = 3.5f;

    protected override void OnPlaced()
    {
        var red = SignPost(Color.red, PrimitiveType.Cylinder, new Vector3(1.2f, 0.05f, 1.2f), new Vector3(90, 0, 0));
        var white = Prim(PrimitiveType.Cylinder, SideOffset + new Vector3(0, 2.7f, -0.04f), new Vector3(0.85f, 0.05f, 0.85f), Color.white);
        white.transform.localEulerAngles = new Vector3(90, 0, 0);
    }

    public override float Limit(CarAI car, float dt)
    {
        float d = car.DistToNode, half = Half;
        if (d > half + 10f) return float.MaxValue;

        if (car.Obeys(this)) return SlowTo(car, limit, d - half);

        if (d < half && car.Speed > limit * 1.2f) car.Report("Excès de vitesse");
        return float.MaxValue;
    }
}

// =====================================================================
//  DOS D'ÂNE : tout le monde ralentit (pas d'infraction possible)
// =====================================================================
public class SpeedBump : RoadElement
{
    public float bumpSpeed = 1.8f;
    protected override bool Symmetric => true;

    protected override void OnPlaced() =>
        Prim(PrimitiveType.Cube, new Vector3(0, 0.08f, 0), new Vector3(Half * 1.6f, 0.16f, 0.8f), new Color(1f, 0.8f, 0f));

    public override float Limit(CarAI car, float dt)
    {
        float ahead = Vector3.Dot(Center - car.transform.position, car.ApproachDir);
        if (ahead < -0.5f) return float.MaxValue;
        return SlowTo(car, bumpSpeed, ahead);
    }
}

// =====================================================================
//  ROUTE BARRÉE : la tuile est retirée du graphe, les voitures contournent
// =====================================================================
public class RoadBlock : RoadElement
{
    protected override void OnPlaced()
    {
        RoadGraph.Instance.SetBlocked(Node, true);
        Prim(PrimitiveType.Cube, new Vector3(0, 0.7f, 0), new Vector3(Half * 1.7f, 0.5f, 0.3f), Color.red);
        for (int i = -2; i <= 2; i += 2)
            Prim(PrimitiveType.Cube, new Vector3(i * Half * 0.3f, 0.7f, -0.02f), new Vector3(Half * 0.25f, 0.52f, 0.32f), Color.white);
        Prim(PrimitiveType.Cube, new Vector3(Half * 0.7f, 0.3f, 0), new Vector3(0.15f, 0.6f, 0.15f), Color.gray);
        Prim(PrimitiveType.Cube, new Vector3(-Half * 0.7f, 0.3f, 0), new Vector3(0.15f, 0.6f, 0.15f), Color.gray);
    }

    protected override void OnRemoved() => RoadGraph.Instance.SetBlocked(Node, false);
}

// =====================================================================
//  SENS UNIQUE : interdit de rouler contre `Facing` sur cette tuile
// =====================================================================
public class OneWaySign : RoadElement
{
    protected override void OnPlaced()
    {
        RoadGraph.Instance.SetOneWay(Node, Facing, true);
        SignPost(new Color(0.1f, 0.3f, 0.9f), PrimitiveType.Cube, new Vector3(1.2f, 0.7f, 0.05f), Vector3.zero);
        // flèche au sol
        Prim(PrimitiveType.Cube, new Vector3(0, 0.07f, -0.5f), new Vector3(0.6f, 0.04f, 3f), Color.white);
        var tip = Prim(PrimitiveType.Cube, new Vector3(0, 0.07f, 1.4f), new Vector3(1.4f, 0.04f, 1.4f), Color.white);
        tip.transform.localEulerAngles = new Vector3(0, 45, 0);
    }

    protected override void OnRemoved() => RoadGraph.Instance.SetOneWay(Node, Facing, false);
}

// =====================================================================
//  PASSAGE PIÉTON : fait traverser des piétons, les voitures ralentissent
//  et s'arrêtent si un piéton est sur/près du passage.
//  Conseil : à poser sur une tuile droite (pas un carrefour).
// =====================================================================
public class PedestrianCrossing : RoadElement
{
    public float crossSpeed = 3.5f;
    System.Random rng;
    float spawnTimer;

    protected override bool Symmetric => true;
    float Width => Half * 2f * 0.8f;
    Vector3 Right => Vector3.Cross(Vector3.up, Facing);

    protected override void OnPlaced()
    {
        int count = Mathf.Max(3, Mathf.FloorToInt(Width / 0.9f));
        for (int i = 0; i < count; i++)
        {
            float x = (i - (count - 1) * 0.5f) * 0.9f;
            Prim(PrimitiveType.Cube, new Vector3(x, 0.05f, 0), new Vector3(0.5f, 0.02f, 2f), Color.white);
        }
        ResetState();
    }

    public override void ResetState()
    {
        rng = new System.Random(Node * 7919 + 17);
        spawnTimer = (float)rng.NextDouble() * 4f;
    }

    void FixedUpdate()
    {
        spawnTimer -= Time.fixedDeltaTime;
        if (spawnTimer > 0f) return;
        spawnTimer = 3f + (float)rng.NextDouble() * 5f;

        Vector3 a = Center - Right * (Width * 0.5f + 1f);
        Vector3 b = Center + Right * (Width * 0.5f + 1f);
        if (rng.Next(2) == 0) { var t = a; a = b; b = t; }
        a.y += 0.5f; b.y += 0.5f;

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(go.GetComponent<Collider>());
        go.transform.localScale = Vector3.one * 0.5f;
        go.GetComponent<Renderer>().material.color = new Color(0.9f, 0.5f, 0.2f);
        go.AddComponent<Pedestrian>().Init(a, b);
    }

    bool PedestrianNear()
    {
        Vector3 c = Center, r = Right;
        foreach (var p in Pedestrian.All)
        {
            Vector3 d = p.transform.position - c;
            if (Mathf.Abs(Vector3.Dot(d, Facing)) < 2.5f && Mathf.Abs(Vector3.Dot(d, r)) < Width * 0.5f + 1f)
                return true;
        }
        return false;
    }

    public override float Limit(CarAI car, float dt)
    {
        float ahead = Vector3.Dot(Center - car.transform.position, car.ApproachDir);
        if (ahead > 16f || ahead < -2.5f) return float.MaxValue;

        float slow = SlowTo(car, crossSpeed, ahead - 4f);
        if (!PedestrianNear()) return slow;

        if (!car.Obeys(this))
        {
            if (ahead < 5f) car.Report("Piéton non respecté");
            return float.MaxValue;
        }
        if (ahead < 3.5f) return float.MaxValue;               // trop tard pour s'arrêter
        return Mathf.Min(slow, BrakeSpeed(car, ahead - 4f));
    }
}

// =====================================================================
//  FEUX / AGENT ROUTIER : régulent les deux axes (X et Z) de la tuile.
//  Le feu est à durée fixe ; l'agent donne la main à l'axe le plus chargé.
// =====================================================================
public abstract class SignalController : RoadElement
{
    public float greenTime = 8f, yellowTime = 1.5f;
    public float minGreen = 4f, maxGreen = 14f;

    protected abstract bool Smart { get; }
    protected override bool ForceAllDirections => true;

    int axisGreen;
    float timer;
    bool yellow;
    Renderer lampX, lampZ;

    static int Axis(Vector3 h) => Mathf.Abs(h.x) >= Mathf.Abs(h.z) ? 0 : 1;

    protected override void OnPlaced()
    {
        // La tuile peut être tournée : on place les éléments visuels en coordonnées monde
        Vector3 corner = Center + new Vector3(Half * 0.8f, 0, Half * 0.8f);
        var pole = Prim(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.15f, 1.7f, 0.15f), Color.gray);
        pole.transform.position = corner + Vector3.up * 1.7f;
        lampX = Lamp(corner + Vector3.up * 3.8f);
        lampZ = Lamp(corner + Vector3.up * 3.0f);
        if (Smart)
        {
            var cop = Prim(PrimitiveType.Capsule, Vector3.zero, new Vector3(0.7f, 0.9f, 0.7f), Color.white);
            cop.transform.position = Center + Vector3.up * 0.9f;
            var vest = Prim(PrimitiveType.Cube, Vector3.zero, new Vector3(0.75f, 0.4f, 0.75f), new Color(1f, 0.5f, 0f));
            vest.transform.position = Center + Vector3.up * 1.0f;
        }
        ResetState();
    }

    Renderer Lamp(Vector3 worldPos)
    {
        var s = Prim(PrimitiveType.Sphere, Vector3.zero, Vector3.one * 0.6f, Color.red);
        s.transform.position = worldPos;
        return s.GetComponent<Renderer>();
    }

    public override void ResetState()
    {
        axisGreen = 0; timer = 0f; yellow = false;
        Paint();
    }

    void FixedUpdate()
    {
        timer += Time.fixedDeltaTime;
        if (yellow)
        {
            if (timer >= yellowTime) { yellow = false; axisGreen = 1 - axisGreen; timer = 0f; }
        }
        else if (Smart ? ShouldSwitch() : timer >= greenTime)
        {
            yellow = true; timer = 0f;
        }
        Paint();
    }

    bool ShouldSwitch()
    {
        if (timer < minGreen) return false;
        if (timer >= maxGreen) return true;
        int mine = CountWaiting(axisGreen), other = CountWaiting(1 - axisGreen);
        return other > 0 && other > mine;
    }

    int CountWaiting(int axis)
    {
        int n = 0;
        foreach (var c in CarAI.Cars)
            if (c.ApproachNode == Node && c.PrevNode >= 0 && Axis(c.ApproachDir) == axis && c.DistToNode < 25f) n++;
        return n;
    }

    void Paint()
    {
        if (lampX == null) return;
        lampX.material.color = LampColor(0);
        lampZ.material.color = LampColor(1);
    }

    Color LampColor(int axis) =>
        axis != axisGreen ? Color.red : (yellow ? new Color(1f, 0.7f, 0f) : Color.green);

    public override float Limit(CarAI car, float dt)
    {
        float d = LineDistance(car);
        if (d > 14f) return float.MaxValue;

        bool myGreen = Axis(car.ApproachDir) == axisGreen;

        if (myGreen && !yellow)
        {
            if (d <= 0f) car.SetMemo(this, 1f);       // a franchi la ligne au vert
            return float.MaxValue;
        }
        if (car.GetMemo(this) > 0f) return float.MaxValue;

        if (d <= 0f)
        {
            if (!myGreen) car.Report(Smart ? "Refus d'obtempérer (agent)" : "Feu grillé");
            car.SetMemo(this, 1f);
            return float.MaxValue;
        }

        if (!car.Obeys(this, Smart ? 0.97f : car.obeyChance)) return float.MaxValue;

        // Orange : si la voiture ne peut plus s'arrêter, elle passe
        if (myGreen && car.Speed * car.Speed / (2f * car.braking) > d) return float.MaxValue;

        return BrakeSpeed(car, d - 0.3f);
    }
}

public class TrafficLight : SignalController { protected override bool Smart => false; }
public class TrafficCop : SignalController { protected override bool Smart => true; }

// =====================================================================
//  POSTE DE POLICE : fait apparaître une voiture de police qui patrouille
//  et poursuit les infractionnistes (voir PoliceCar).
// =====================================================================
public class PoliceStation : RoadElement
{
    protected override void OnPlaced()
    {
        Prim(PrimitiveType.Cube, SideOffset + new Vector3(0, 1.2f, 0), new Vector3(2.2f, 2.4f, 2.2f), new Color(0.1f, 0.25f, 0.8f));
        Prim(PrimitiveType.Cube, SideOffset + new Vector3(0, 2.6f, 0), new Vector3(2.4f, 0.2f, 2.4f), Color.white);
    }

    public override void OnRunStart(int seed) => GameManager.Instance.SpawnCar(Node, seed, true);
}
