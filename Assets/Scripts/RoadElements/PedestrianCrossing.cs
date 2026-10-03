using UnityEngine;

// =====================================================================
//  PASSAGE PIÉTON (élément posé par le joueur)
//  Les piétons existent maintenant indépendamment (script Pedestrian) et traversent déjà
//  à toutes les intersections. Cet élément :
//   - fait s'arrêter les voitures (qui obéissent) devant un piéton en train de traverser sur la tuile ;
//   - sur une tuile droite, sert de point de traversée aux piétons (ils l'utilisent plutôt que de traverser n'importe où).
// =====================================================================
public class PedestrianCrossing : RoadElement
{
    [Tooltip("Distance d'arrêt (m) entre l'avant de la voiture et le piéton")]
    public float stopDistance = 3f;
    [Tooltip("Distance (m) à partir de laquelle la voiture surveille les piétons")]
    public float lookAhead = 14f;
    [Tooltip("Largeur (m) surveillée de part et d'autre de l'axe de la voiture")]
    public float watchWidth = 6f;

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

        foreach (var ped in Pedestrian.All)
        {
            if (ped == null || !ped.IsCrossing || ped.CrossNode != Node) continue;

            // il ne gêne que s'il traverse la route de la voiture (pas s'il longe la même route)
            if (Mathf.Abs(Vector3.Dot(ped.MoveDirection, fwd)) > 0.6f) continue;

            Vector3 d = ped.transform.position - pos;
            d.y = 0f;
            float ahead = Vector3.Dot(d, fwd);
            if (ahead < -1f || ahead > lookAhead) continue;
            if (Mathf.Abs(Vector3.Dot(d, right)) > watchWidth) continue;

            if (!car.Obeys(this))
            {
                if (ahead < 5f) car.Report("Piéton non respecté");
                continue;
            }

            if (ahead < car.halfLength + 1f) continue;               // trop tard pour s'arrêter

            float free = ahead - car.halfLength - stopDistance;
            limit = Mathf.Min(limit, Mathf.Sqrt(2f * car.braking * Mathf.Max(0f, free)));
        }
        return limit;
    }
}