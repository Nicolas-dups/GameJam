using UnityEngine;

// =====================================================================
//  PASSAGE PIÉTON (élément posé par le joueur)
//  Une voiture qui obéit s'arrête pour TOUT piéton en train de traverser
//  dans la zone de la tuile (rayon `zoneMargin` autour de la tuile) et à moins de
//  `lookAhead` mètres d'elle, quelle que soit sa direction (parallèle comprise).
//  Elle ne repart que `releaseDelay` secondes après le départ du dernier piéton.
// =====================================================================
public class PedestrianCrossing : RoadElement
{
    [Tooltip("Distance d'arrêt (m) entre l'avant de la voiture et le piéton")]
    public float stopDistance = 3f;
    [Tooltip("Rayon (m) autour de la voiture dans lequel les piétons sont pris en compte (toutes directions)")]
    public float lookAhead = 20f;
    [Tooltip("Marge (m) autour de la tuile : un piéton qui traverse dans cette zone compte")]
    public float zoneMargin = 6f;
    [Tooltip("Temps (s) pendant lequel la voiture reste arrêtée après le départ du dernier piéton")]
    public float releaseDelay = 0.6f;
    [Tooltip("Affiche des logs de diagnostic dans la Console")]
    public bool debugLogs = true;

    protected override bool Symmetric => true;
    public override bool LocalEffect => true;

    protected override void OnPlaced()
    {
        if (debugLogs) Debug.Log($"[Passage] posé : noeud {Node}, AllDirections {AllDirections}", this);
    }

    public override void ResetState() { }

    public static bool IsAt(int node)
    {
        var list = RoadElement.At(node);
        for (int i = 0; i < list.Count; i++)
            if (list[i] is PedestrianCrossing) return true;
        return false;
    }

    public override float Limit(CarAI car, float dt)
    {
        Vector3 center = Center;
        float zone = Half + zoneMargin;
        Vector3 pos = car.transform.position;
        Vector3 fwd = car.transform.forward;
        fwd.y = 0f;
        fwd.Normalize();

        float limit = float.MaxValue;
        bool blocked = false;

        foreach (var ped in Pedestrian.All)
        {
            if (ped == null || !ped.IsCrossing) continue;

            // le piéton doit traverser sur cette tuile ou tout près
            Vector3 fromCenter = ped.transform.position - center;
            fromCenter.y = 0f;
            bool inZone = ped.CrossNode == Node
                          || (Mathf.Abs(fromCenter.x) <= zone && Mathf.Abs(fromCenter.z) <= zone);
            if (!inZone) continue;

            Vector3 d = ped.transform.position - pos;
            d.y = 0f;
            float dist = d.magnitude;
            if (dist > lookAhead) continue;

            // ignore ceux qui sont nettement derrière la voiture (sauf s'ils sont tout près : virage)
            float ahead = Vector3.Dot(d, fwd);
            if (ahead < -car.halfLength && dist > car.halfLength + 2f) continue;

            if (!car.Obeys(this))
            {
                if (dist < 5f) car.Report("Piéton non respecté");
                continue;
            }

            blocked = true;
            float free = dist - car.halfLength - stopDistance;
            limit = Mathf.Min(limit, free <= 0f ? 0f : BrakeSpeed(car, free));

            if (debugLogs && car.GetMemo(this) <= 0f)
                Debug.Log($"[Passage] {car.name} s'arrête pour {ped.name} : dist={dist:0.0} m", this);
        }

        // Délai avant de repartir
        float hold = car.GetMemo(this);
        if (blocked) car.SetMemo(this, releaseDelay);
        else if (hold > 0f)
        {
            car.SetMemo(this, hold - dt);
            if (car.Speed < 1f) return 0f;
        }

        return limit;
    }
}