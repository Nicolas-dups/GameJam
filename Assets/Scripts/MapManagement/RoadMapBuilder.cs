using UnityEngine;

/// <summary>
/// Reads the ASCII map, builds the RoadGraph, and spawns the road tiles.
///
/// Prefab conventions (orientation at rotation 0, N = +Z, E = +X, seen from above):
///   deadEnd    : opening toward N
///   straight   : N-S road
///   corner     : connects N and E
///   tJunction  : connects N, E and W (closed on S)
///   cross      : all 4 sides
/// Each prefab's pivot should be at the centre of the tile, sized to cellSize.
/// </summary>
public class RoadMapBuilder : MonoBehaviour
{
    [Header("Map (TextAsset wins over the text field if set)")]
    public TextAsset asciiFile;
    [TextArea(8, 20)]
    public string ascii =
        "00001000\n" +
        "01111110\n" +
        "00101010\n" +
        "00111110";

    [Header("Tiles")]
    public GameObject deadEndPrefab;
    public GameObject straightPrefab;
    public GameObject cornerPrefab;
    public GameObject tJunctionPrefab;
    public GameObject crossPrefab;
    public float cellSize = 10f;

    public RoadGraph Graph { get; private set; }

    void Awake()
    {
        Graph = RoadGraph.FromAscii(asciiFile != null ? asciiFile.text : ascii);
        Build();
    }

    /// <summary>Destroys and respawns all tiles from the current graph (call after editing edges).</summary>
    public void RebuildTiles()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);
        Build();
    }

    void Build()
    {
        foreach (var n in Graph.Nodes)
        {
            int mask = Graph.Mask(n);
            var type = RoadGraph.Classify(mask);

            GameObject prefab;
            int canonical;
            switch (type)
            {
                case TileType.Straight:  prefab = straightPrefab;  canonical = RoadGraph.MaskN | RoadGraph.MaskS; break;
                case TileType.Corner:    prefab = cornerPrefab;    canonical = RoadGraph.MaskN | RoadGraph.MaskE; break;
                case TileType.TJunction: prefab = tJunctionPrefab; canonical = RoadGraph.MaskN | RoadGraph.MaskE | RoadGraph.MaskW; break;
                case TileType.Cross:     prefab = crossPrefab;     canonical = 15; break;
                default:                 prefab = deadEndPrefab;   canonical = RoadGraph.MaskN; break; // dead end + isolated
            }
            if (prefab == null) { Debug.LogWarning($"Missing prefab for {type}"); continue; }

            // find the clockwise rotation (0..3) that turns the canonical tile into the needed mask
            int rot = 0;
            for (int k = 0; k < 4; k++)
                if (RoadGraph.RotateMask(canonical, k) == mask) { rot = k; break; }

            Instantiate(prefab, GridToWorld(n), prefab.transform.rotation * Quaternion.Euler(0, 90f * rot, 0), transform)
                .name = $"{type}_{n.x}_{n.y}";
        }
    }

    public Vector3 GridToWorld(Vector2Int n) =>
        transform.position + new Vector3(n.x * cellSize, 0f, n.y * cellSize);

    /// <summary>Grid cell under a world position. May be a non-road cell.</summary>
    public Vector2Int WorldToGrid(Vector3 p)
    {
        var l = p - transform.position;
        return new Vector2Int(Mathf.RoundToInt(l.x / cellSize), Mathf.RoundToInt(l.z / cellSize));
    }

    /// <summary>True only if the position lies on a road cell (a graph node).</summary>
    public bool TryWorldToNode(Vector3 p, out Vector2Int node)
    {
        node = WorldToGrid(p);
        return Graph.HasNode(node);
    }

    /// <summary>Road node under the position, or the closest road node if it is off-road.</summary>
    public Vector2Int NearestNode(Vector3 p)
    {
        var cell = WorldToGrid(p);
        if (Graph.HasNode(cell)) return cell;

        var best = cell;
        float bestDist = float.MaxValue;
        foreach (var n in Graph.Nodes)
        {
            float d = (GridToWorld(n) - p).sqrMagnitude;
            if (d < bestDist) { bestDist = d; best = n; }
        }
        return best;
    }
}