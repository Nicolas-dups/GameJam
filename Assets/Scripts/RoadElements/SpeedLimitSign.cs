using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  LIMITATION DE VITESSE
// =====================================================================
public class SpeedLimitSign : RoadElement
{
    public float limit = 3.5f;

    public override float Limit(CarAI car, float dt)
    {
        float d = car.DistToNode, half = Half;
        if (d > half + 10f) return float.MaxValue;

        if (car.Obeys(this)) return SlowTo(car, limit, d - half);

        if (d < half && car.Speed > limit * 1.2f) car.Report("Excès de vitesse");
        return float.MaxValue;
    }
}
