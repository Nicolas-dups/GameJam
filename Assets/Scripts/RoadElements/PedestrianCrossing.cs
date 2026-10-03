using UnityEngine;

// =====================================================================
//  PASSAGE PIÉTON (élément posé par le joueur)
//  Les piétons existent indépendamment (script Pedestrian) et traversent déjà
//  à toutes les intersections. Cet élément :
//   - fait s'arrêter les voitures (qui obéissent) tant qu'un piéton traverse la chaussée de cette tuile ;
//   - sur une tuile droite, sert de point de traversée aux piétons (ils l'utilisent plutôt que de traverser n'importe où).
// =====================================================================
public class PedestrianCrossing : RoadElement
{
    [Tooltip("Distance d'arrêt (m) entre l'avant de la voiture et le piéton")]
    public float stopDistance = 3f;
    [Tooltip("Distance (m) à partir de laquelle la voiture surveille les piétons")]
    public float lookAhead = 14f;
    [Tooltip("Demi-largeur (m) de chaussée surveillée, mesurée depuis l'AXE de la route (piétons sur les trottoirs à ±4.5 m)")]
    public float watchWidth = 6f;
    [Tooltip("Temps (s) pendant lequel la voiture reste arrêtée après le départ du dernier piéton")]
    public float releaseDelay = 0.6f;

    protected override bool Symmetric => true;
    public override bool LocalEffect => true;

    protected override void OnPlaced() { }
    public override void ResetState() { }

    /// <summary>Y a-t-il un passage piéton posé par le joueur sur cette tuile ?</summary>
    public static bool IsAt(int node)
    {
        var list = RoadElement.At(node);
        for (int i = 0; i < list.Count; i++)
            if (list[i] is PedestrianCrossing) return true;
        return false;
    }

    public override float Limit(CarAI car, float dt)
    {
        Vector3 pos = car.transform.position;
        Vector3 fwd = car.transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, fwd);

        float limit = float.MaxValue;
        bool blocked = false;

        foreach (var ped in Pedestrian.All)
        {
            if (ped == null || !ped.IsCrossing || ped.CrossNode != Node) continue;

            // il ne gêne que s'il traverse la route de la voiture (pas s'il longe la même route)
            if (Mathf.Abs(Vector3.Dot(ped.MoveDirection, fwd)) > 0.6f) continue;

            Vector3 d = ped.transform.position - pos;
            d.y = 0f;
            float ahead = Vector3.Dot(d, fwd);
            if (ahead <= 0f || ahead > lookAhead) continue;                 // derrière la voiture : sans danger

            // position latérale par rapport à l'AXE de la route (la voiture roule décalée de laneOffset à droite)
            float lateralFromAxis = Vector3.Dot(d, right) + car.laneOffset;
            if (Mathf.Abs(lateralFromAxis) > watchWidth) continue;

            if (!car.Obeys(this))
            {
                if (ahead < 5f) car.Report("Piéton non respecté");
                continue;
            }

            blocked = true;
            float free = ahead - car.halfLength - stopDistance;             // distance libre avant la distance d'arrêt
            limit = Mathf.Min(limit, free <= 0f ? 0f : BrakeSpeed(car, free));
        }

        // Petit délai avant de repartir : évite de redémarrer dès que le piéton sort de la zone
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