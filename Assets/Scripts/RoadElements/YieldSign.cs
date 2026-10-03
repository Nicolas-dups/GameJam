using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  CÉDER LE PASSAGE : ralentit, s'arrête seulement s'il y a du monde
// =====================================================================
public class YieldSign : RoadElement
{
    public float approachSpeed = 3f;
    public float maxWait = 6f;
    public override bool LocalEffect => true;

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
        float d = SignLineDistance(car);
        if (d > 14f) return float.MaxValue;

        bool traffic = CrossTraffic(car);
        if (d <= 0f)
        {
            if (JustCrossed(d) && traffic && !car.Obeys(this)) car.Report("Refus de céder le passage");
            return float.MaxValue;
        }
        if (!car.Obeys(this)) return float.MaxValue;

        float slow = SlowTo(car, approachSpeed, d);
        if (!traffic || car.GetMemo(this) >= maxWait) return slow;

        if (car.Speed < 0.2f) car.SetMemo(this, car.GetMemo(this) + dt);   // anti-blocage
        return Mathf.Min(slow, BrakeSpeed(car, d - 0.3f));
    }
}