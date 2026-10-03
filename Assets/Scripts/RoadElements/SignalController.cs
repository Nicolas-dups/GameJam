using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  FEUX / AGENT ROUTIER : régulent les deux axes (X et Z) de la tuile.
//  Le feu est à durée fixe ; l'agent donne la main à l'axe le plus chargé.
//  Les lampes sont à assigner dans le prefab (lampAxisX = feu pour les
//  voitures roulant selon X, lampAxisZ = selon Z). Optionnelles.
// =====================================================================
public abstract class SignalController : RoadElement
{
    public float greenTime = 8f, yellowTime = 1.5f;
    public float minGreen = 4f, maxGreen = 14f;

    [Header("Lampes (optionnel)")]
    public Renderer lampAxisX;
    public Renderer lampAxisZ;

    protected abstract bool Smart { get; }
    protected override bool ForceAllDirections => true;

    int axisGreen;
    float timer;
    bool yellow;

    static int Axis(Vector3 h) => Mathf.Abs(h.x) >= Mathf.Abs(h.z) ? 0 : 1;

    protected override void OnPlaced() => ResetState();

    public override void ResetState()
    {
        axisGreen = 0; timer = 0f; yellow = false;
        Paint();
    }

    void FixedUpdate()
    {
        timer += Time.fixedDeltaTime;
        if (yellow)
        {
            if (timer >= yellowTime) { yellow = false; axisGreen = 1 - axisGreen; timer = 0f; }
        }
        else if (Smart ? ShouldSwitch() : timer >= greenTime)
        {
            yellow = true; timer = 0f;
        }
        Paint();
    }

    bool ShouldSwitch()
    {
        if (timer < minGreen) return false;
        if (timer >= maxGreen) return true;
        int mine = CountWaiting(axisGreen), other = CountWaiting(1 - axisGreen);
        return other > 0 && other > mine;
    }

    int CountWaiting(int axis)
    {
        int n = 0;
        foreach (var c in CarAI.Cars)
            if (c.ApproachNode == Node && c.PrevNode >= 0 && Axis(c.ApproachDir) == axis && c.DistToNode < 25f) n++;
        return n;
    }

    void Paint()
    {
        if (lampAxisX != null) lampAxisX.material.color = LampColor(0);
        if (lampAxisZ != null) lampAxisZ.material.color = LampColor(1);
    }

    Color LampColor(int axis) =>
        axis != axisGreen ? Color.red : (yellow ? new Color(1f, 0.7f, 0f) : Color.green);

    public override float Limit(CarAI car, float dt)
    {
        float d = LineDistance(car);
        if (d > 14f) return float.MaxValue;

        bool myGreen = Axis(car.ApproachDir) == axisGreen;

        if (myGreen && !yellow)
        {
            if (d <= 0f) car.SetMemo(this, 1f);       // a franchi la ligne au vert
            return float.MaxValue;
        }
        if (car.GetMemo(this) > 0f) return float.MaxValue;

        if (d <= 0f)
        {
            if (!myGreen) car.Report(Smart ? "Refus d'obtempérer (agent)" : "Feu grillé");
            car.SetMemo(this, 1f);
            return float.MaxValue;
        }

        if (!car.Obeys(this, Smart ? 0.97f : car.obeyChance)) return float.MaxValue;

        // Orange : si la voiture ne peut plus s'arrêter, elle passe
        if (myGreen && car.Speed * car.Speed / (2f * car.braking) > d) return float.MaxValue;

        return BrakeSpeed(car, d - 0.3f);
    }
}
