using UnityEngine;

public class PathDebug : MonoBehaviour
{
    public RoadMapBuilder map;
    public Vector2Int a = new Vector2Int(1, 2);
    public Vector2Int b = new Vector2Int(6, 0);

    void Start()
    {
        if (map.Graph.TryGetPath(a, b, out var path))
        {
            for (int i = 0; i < path.Count - 1; i++)
                Debug.DrawLine(map.GridToWorld(path[i]) + Vector3.up,
                               map.GridToWorld(path[i + 1]) + Vector3.up,
                               Color.red, 5f);
        }
        else
        {
            Debug.Log("No road connects A and B");
        }
    }
}