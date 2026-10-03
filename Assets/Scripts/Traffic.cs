using System.Collections.Generic;
using UnityEngine;

/// <summary>Registre des infractions : la police y puise ses cibles.</summary>
public static class Infractions
{
    public static readonly List<CarAI> Wanted = new List<CarAI>();
    public static int Total, Arrests;

    public static void Report(CarAI car, string reason)
    {
        if (car == null || car.isPolice) return;
        if (!Wanted.Contains(car)) Wanted.Add(car);
        Total++;
        Debug.Log($"Infraction : {car.name} - {reason}");
    }

    public static void Arrest(CarAI car)
    {
        Wanted.Remove(car);
        Arrests++;
    }

    public static void Reset()
    {
        Wanted.Clear();
        Total = 0;
        Arrests = 0;
    }
}

/// <summary>Piéton : traverse en ligne droite puis disparaît.</summary>
public class Pedestrian : MonoBehaviour
{
    public static readonly List<Pedestrian> All = new List<Pedestrian>();
    public float speed = 1.4f;
    Vector3 to;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() => All.Clear();

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    public void Init(Vector3 from, Vector3 destination)
    {
        transform.position = from;
        to = destination;
    }

    void FixedUpdate()
    {
        transform.position = Vector3.MoveTowards(transform.position, to, speed * Time.fixedDeltaTime);
        if ((transform.position - to).sqrMagnitude < 0.01f) Destroy(gameObject);
    }
}

/// <summary>
/// Comportement de police (à ajouter à un objet qui a déjà CarAI) :
/// patrouille au hasard, poursuit la voiture en infraction la plus proche
/// puis l'arrête quelques secondes quand elle est à portée.
/// </summary>
[RequireComponent(typeof(CarAI))]
public class PoliceCar : MonoBehaviour
{
    public float sight = 45f;
    public float arrestDistance = 7f;     // doit rester > carLength + safeGap (la détection devant empêche de coller)
    public float arrestTime = 4f;
    public float chaseBoost = 1.4f;

    CarAI car, target;
    float repath;

    void Awake() => car = GetComponent<CarAI>();

    void FixedUpdate()
    {
        if (car.IsArrested) return;
        float dt = Time.fixedDeltaTime;

        if (target == null || !Infractions.Wanted.Contains(target))
        {
            target = FindTarget();
            car.speedBoost = target != null ? chaseBoost : 1f;
        }
        if (target == null) return;

        Vector3 d = target.transform.position - transform.position;
        d.y = 0f;
        if (d.magnitude < arrestDistance)
        {
            target.Arrest(arrestTime);
            car.Arrest(arrestTime);
            Infractions.Arrest(target);
            target = null;
            car.speedBoost = 1f;
            return;
        }

        repath -= dt;
        if (repath <= 0f)
        {
            repath = 1f;
            car.GoTo(target.transform.position);
        }
    }

    CarAI FindTarget()
    {
        CarAI best = null;
        float bestD = sight;
        foreach (var c in Infractions.Wanted)
        {
            if (c == null || c.IsArrested) continue;
            float dist = Vector3.Distance(c.transform.position, transform.position);
            if (dist < bestD) { bestD = dist; best = c; }
        }
        return best;
    }
}
