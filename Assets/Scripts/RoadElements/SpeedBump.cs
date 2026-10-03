using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  DOS D'ÂNE : tout le monde ralentit (pas d'infraction possible)
//  (prefab conseillé avec centerOnRoad = true)
// =====================================================================
public class SpeedBump : RoadElement
{
    public float bumpSpeed = 1.8f;
    protected override bool Symmetric => true;

    public override float Limit(CarAI car, float dt)
    {
        float ahead = Vector3.Dot(Center - car.transform.position, car.ApproachDir);
        if (ahead < -0.5f) return float.MaxValue;
        return SlowTo(car, bumpSpeed, ahead);
    }
}
