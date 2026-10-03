using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Type d'emplacement de pose utilisé par un élément de voirie.
/// </summary>
public enum PlacementKind
{
    [Tooltip("Cube sur le bord de la route (panneaux, feux...)")]
    Edge,
    [Tooltip("Rectangle en travers de la route (passage piéton, dos d'âne, route barrée...)")]
    CenterLong,
    [Tooltip("Petit cube au centre de la tuile (agent de circulation...)")]
    CenterShort,
    [Tooltip("Remplace TOUTE la tuile (passage piéton) : uniquement sur les intersections X, T et les angles L")]
    WholeTile
}

// =====================================================================
//  Base : un élément de voirie est un PREFAB posé sur un emplacement
//  (PlacementSpot) d'une tuile. Il agit sur les voitures qui APPROCHENT
//  de cette tuile et renvoie une vitesse max.
//
//  Convention prefab : le +Z local du prefab = sens de circulation concerné
//  (la face du panneau regarde donc vers -Z, vers les voitures qui arrivent).
//  L'origine du prefab = le pied du panneau.
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

    [Header("Placement")]
    [Tooltip("Type de cube de sélection sur lequel cet élément se pose :\n" +
             "Edge = bord de route (panneaux, feux)\n" +
             "CenterLong = rectangle en travers de la route (passage piéton, dos d'âne, route barrée)\n" +
             "CenterShort = petit cube au centre de la tuile (agent)")]
    public PlacementKind placement = PlacementKind.Edge;

    /// <summary>Compatibilité : vrai si l'élément se pose au centre de la route.</summary>
    public bool centerOnRoad => placement != PlacementKind.Edge;

    public int Node { get; private set; }
    public Vector3 Facing { get; private set; }
    public bool AllDirections { get; private set; }
    bool registered;
    public bool IsPlaced => registered;

    /// <summary>Vrai : agit dans les deux sens de l'axe (dos d'âne, passage piéton).</summary>
    protected virtual bool Symmetric => false;
    /// <summary>Vrai : agit toujours sur toutes les directions (feux, agent).</summary>
    protected virtual bool ForceAllDirections => false;
    /// <summary>Vrai : l'effet est localisé à la POSITION de l'élément sur la tuile (panneaux, dos d'âne, passage piéton).
    /// Faux : l'effet concerne toute la tuile, ligne d'arrêt à l'entrée (feux, agent).</summary>
    public virtual bool LocalEffect => false;

    protected float Half => RoadGraph.Instance.NodeHalfSize(Node);
    protected Vector3 Center => RoadGraph.Instance.NodePos(Node);

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
    protected Vector3 Anchor => transform.position;

    /// <summary>Distance devant la voiture jusqu'à l'élément, dans son sens de marche (négatif = dépassé).</summary>
    protected float Ahead(CarAI car) => Vector3.Dot(Anchor - car.transform.position, car.ApproachDir);

    /// <summary>Ligne d'arrêt juste avant l'élément : 0 quand l'avant de la voiture arrive au panneau.</summary>
    protected float SignLineDistance(CarAI car) => Ahead(car) - car.carLength * 0.5f - 0.5f;

    /// <summary>Vrai uniquement au moment où la ligne vient d'être franchie (évite les faux positifs des voitures arrivées derrière).</summary>
    protected static bool JustCrossed(float lineDist) => lineDist <= 0f && lineDist > -1.5f;

    /// <summary>Distance jusqu'à la ligne d'arrêt (négatif = ligne franchie).</summary>
    protected float LineDistance(CarAI car) => car.DistToNode - (Half + car.stopMargin);

    protected static float BrakeSpeed(CarAI car, float freeDist) =>
        Mathf.Sqrt(2f * car.braking * Mathf.Max(0f, freeDist)) * 0.9f;

    /// <summary>Vitesse permettant d'arriver à `target` après `dist` mètres en freinant normalement.</summary>
    protected static float SlowTo(CarAI car, float target, float dist) =>
        Mathf.Sqrt(target * target + 2f * car.braking * Mathf.Max(0f, dist));
}