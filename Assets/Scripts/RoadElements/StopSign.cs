using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  STOP : arrêt obligatoire `waitTime` secondes devant la ligne
// =====================================================================
public class StopSign : RoadElement
{
    public float waitTime = 2f;
    [Tooltip("Affiche des logs de diagnostic dans la Console")]
    public bool debugLogs = true;
    public override bool LocalEffect => true;

    readonly HashSet<CarAI> logged = new HashSet<CarAI>();

    protected override void OnPlaced()
    {
        if (debugLogs)
            Debug.Log($"[Stop] posé : noeud {Node}, Facing {Facing}, AllDirections {AllDirections}, position {transform.position}", this);
    }

    public override void ResetState() => logged.Clear();

    public override float Limit(CarAI car, float dt)
    {
        float d = SignLineDistance(car);

        // Premier appel pour cette voiture : on dit ce qu'elle voit
        if (debugLogs && d < 12f && logged.Add(car))
            Debug.Log($"[Stop] {car.name} évalue le stop : d={d:0.0}, obéit={car.Obeys(this)}, mémo={car.GetMemo(this):0.0}", this);

        if (d > 12f) return float.MaxValue;
        if (car.GetMemo(this) >= waitTime) return float.MaxValue;     // arrêt effectué

        if (d <= 0f) { if (JustCrossed(d)) car.Report("Stop grillé"); return float.MaxValue; }
        if (!car.Obeys(this)) return float.MaxValue;

        if (d < 1.2f && car.Speed < 0.2f) car.SetMemo(this, car.GetMemo(this) + dt);
        return BrakeSpeed(car, d - 0.3f);
    }
}