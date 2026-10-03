using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  STOP : arrêt obligatoire `waitTime` secondes devant la ligne
// =====================================================================
public class StopSign : RoadElement
{
    public float waitTime = 2f;
    public override bool LocalEffect => true;

    public override float Limit(CarAI car, float dt)
    {
        float d = SignLineDistance(car);
        if (d > 12f) return float.MaxValue;
        if (car.GetMemo(this) >= waitTime) return float.MaxValue;     // arrêt effectué

        if (d <= 0f) { if (JustCrossed(d)) car.Report("Stop grillé"); return float.MaxValue; }
        if (!car.Obeys(this)) return float.MaxValue;

        if (d < 1.2f && car.Speed < 0.2f) car.SetMemo(this, car.GetMemo(this) + dt);
        return BrakeSpeed(car, d - 0.3f);
    }
}