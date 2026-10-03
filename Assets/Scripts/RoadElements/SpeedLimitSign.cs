using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  LIMITATION DE VITESSE
// =====================================================================
public class SpeedLimitSign : RoadElement
{
    public float limit = 3.5f;
    public override bool LocalEffect => true;

    public override float Limit(CarAI car, float dt)
    {
        float ahead = Ahead(car);
        if (ahead > 10f) return float.MaxValue;

        if (ahead > 0f) car.SetMemo(this, 1f);                 // la voiture a vu le panneau
        else if (car.GetMemo(this) <= 0f) return float.MaxValue; // arrivée derrière le panneau : ne le voit pas

        if (car.Obeys(this)) return SlowTo(car, limit, Mathf.Max(0f, ahead));

        if (ahead <= 0f && car.Speed > limit * 1.2f) car.Report("Excès de vitesse");
        return float.MaxValue;
    }
}