using UnityEngine;

/// <summary>
/// Minimal car: at every node it asks the graph what to do to reach the goal.
/// Place the car on a road cell; set 'goal' (grid coordinates) in the inspector or via SetGoal().
/// </summary>
public class CarNavigator : MonoBehaviour
{
    public RoadMapBuilder map;
    public Vector2Int goal;
    public float speed = 8f;
    public float turnSpeed = 360f;

    Vector2Int current;
    Vector2Int target;
    Dir heading = Dir.N;
    bool moving;

    void Start()
    {
        current = map.WorldToGrid(transform.position);
        transform.position = map.GridToWorld(current);
        PickNext();
    }

    public void SetGoal(Vector2Int newGoal)
    {
        goal = newGoal;
        if (!moving) PickNext();
    }

    void PickNext()
    {
        Turn turn = map.Graph.GetInstruction(current, heading, goal, out target, out heading);
        moving = turn != Turn.Arrived && turn != Turn.NoPath;
        if (turn != Turn.Straight && moving) Debug.Log($"{name} at {current}: {turn}");
    }

    void Update()
    {
        if (!moving) return;

        Vector3 targetPos = map.GridToWorld(target);

        // face the direction of travel
        Vector3 dir = targetPos - transform.position;
        if (dir.sqrMagnitude > 0.0001f)
        {
            var look = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);

        if ((transform.position - targetPos).sqrMagnitude < 0.0001f)
        {
            current = target;
            PickNext();
        }
    }
}
