using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("Coché : l'élément se pose au centre de la tuile (dos d'âne, passage piéton, route barrée, agent...) " +
             "au lieu du bord de route choisi.")]
    public bool centerOnRoad;

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
}
