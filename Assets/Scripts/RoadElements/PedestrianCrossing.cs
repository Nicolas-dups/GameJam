using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  PASSAGE PIÉTON : fait traverser des piétons, les voitures ralentissent
//  et s'arrêtent si un piéton est sur/près du passage.
//  (prefab conseillé avec centerOnRoad = true, sur une tuile droite)
// =====================================================================
public class PedestrianCrossing : RoadElement
{
    public float crossSpeed = 3.5f;
    [Tooltip("Optionnel : prefab de piéton (sinon une capsule est générée)")]
    public GameObject pedestrianPrefab;

    System.Random rng;
    float spawnTimer;

    protected override bool Symmetric => true;
    float Width => Half * 2f * 0.8f;
    Vector3 Right => Vector3.Cross(Vector3.up, Facing);

    protected override void OnPlaced() => ResetState();

    public override void ResetState()
    {
        rng = new System.Random(Node * 7919 + 17);
        spawnTimer = (float)rng.NextDouble() * 4f;
    }

    void FixedUpdate()
    {
        if (rng == null) return;
        spawnTimer -= Time.fixedDeltaTime;
        if (spawnTimer > 0f) return;
        spawnTimer = 3f + (float)rng.NextDouble() * 5f;

        Vector3 a = Center - Right * (Width * 0.5f + 1f);
        Vector3 b = Center + Right * (Width * 0.5f + 1f);
        if (rng.Next(2) == 0) { var t = a; a = b; b = t; }
        a.y += 0.5f; b.y += 0.5f;

        GameObject go;
        if (pedestrianPrefab != null) go = Instantiate(pedestrianPrefab);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.5f;
            go.GetComponent<Renderer>().material.color = new Color(0.9f, 0.5f, 0.2f);
        }
        var ped = go.GetComponent<Pedestrian>();
        if (ped == null) ped = go.AddComponent<Pedestrian>();
        ped.Init(a, b);
    }

    bool PedestrianNear()
    {
        Vector3 c = Center, r = Right;
        foreach (var p in Pedestrian.All)
        {
            Vector3 d = p.transform.position - c;
            if (Mathf.Abs(Vector3.Dot(d, Facing)) < 2.5f && Mathf.Abs(Vector3.Dot(d, r)) < Width * 0.5f + 1f)
                return true;
        }
        return false;
    }

    public override float Limit(CarAI car, float dt)
    {
        float ahead = Vector3.Dot(Center - car.transform.position, car.ApproachDir);
        if (ahead > 16f || ahead < -2.5f) return float.MaxValue;

        float slow = SlowTo(car, crossSpeed, ahead - 4f);
        if (!PedestrianNear()) return slow;

        if (!car.Obeys(this))
        {
            if (ahead < 5f) car.Report("Piéton non respecté");
            return float.MaxValue;
        }
        if (ahead < 3.5f) return float.MaxValue;               // trop tard pour s'arrêter
        return Mathf.Min(slow, BrakeSpeed(car, ahead - 4f));
    }
}
