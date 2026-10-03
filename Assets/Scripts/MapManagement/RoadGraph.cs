using System;
using System.Collections.Generic;
using UnityEngine;

public enum Dir { N = 0, E = 1, S = 2, W = 3 }

public enum TileType { Isolated, DeadEnd, Straight, Corner, TJunction, Cross }

public enum Turn { Straight, Left, Right, UTurn, Arrived, NoPath }

/// <summary>
/// Formal graph of a road grid.
/// Nodes  = Vector2Int(x, y) of every '1' cell. x = column, y = row counted from the BOTTOM
///          (so the text looks the same as the map seen from above, N = +y).
/// Edges  = 4-neighbour adjacency between road cells.
/// </summary>
public class RoadGraph
{
    // Index = (int)Dir
    public static readonly Vector2Int[] Offsets =
    {
        new Vector2Int(0, 1),   // N
        new Vector2Int(1, 0),   // E
        new Vector2Int(0, -1),  // S
        new Vector2Int(-1, 0),  // W
    };

    public const int MaskN = 1, MaskE = 2, MaskS = 4, MaskW = 8;

    readonly HashSet<Vector2Int> nodes = new HashSet<Vector2Int>();
    readonly Dictionary<Vector2Int, List<Vector2Int>> adjacency = new Dictionary<Vector2Int, List<Vector2Int>>();
    readonly Dictionary<Vector2Int, int> masks = new Dictionary<Vector2Int, int>();

    // goal -> (node -> distance to goal). Built lazily, one BFS per goal.
    readonly Dictionary<Vector2Int, Dictionary<Vector2Int, int>> distanceFields =
        new Dictionary<Vector2Int, Dictionary<Vector2Int, int>>();

    public IReadOnlyCollection<Vector2Int> Nodes => nodes;
    public int Width { get; private set; }
    public int Height { get; private set; }

    // ---------------------------------------------------------------- build

    public static RoadGraph FromAscii(string ascii)
    {
        var g = new RoadGraph();
        var lines = ascii.Replace("\r", "").Split('\n');

        // ignore empty lines
        var rows = new List<string>();
        foreach (var l in lines)
        {
            var t = l.Trim();
            if (t.Length > 0) rows.Add(t);
        }

        g.Height = rows.Count;
        for (int r = 0; r < rows.Count; r++)
        {
            int y = rows.Count - 1 - r; // first text line = top of the map
            g.Width = Mathf.Max(g.Width, rows[r].Length);
            for (int x = 0; x < rows[r].Length; x++)
                if (rows[r][x] == '1') g.nodes.Add(new Vector2Int(x, y));
        }

        foreach (var n in g.nodes)
        {
            var list = new List<Vector2Int>(4);
            int mask = 0;
            for (int d = 0; d < 4; d++)
            {
                var nb = n + Offsets[d];
                if (g.nodes.Contains(nb))
                {
                    list.Add(nb);
                    mask |= 1 << d;
                }
            }
            g.adjacency[n] = list;
            g.masks[n] = mask;
        }
        return g;
    }

    // ---------------------------------------------------------------- queries

    public bool HasNode(Vector2Int n) => nodes.Contains(n);
    public IReadOnlyList<Vector2Int> Neighbors(Vector2Int n) => adjacency[n];
    public int Mask(Vector2Int n) => masks[n];

    public static int CountBits(int m)
    {
        int c = 0;
        while (m != 0) { c += m & 1; m >>= 1; }
        return c;
    }

    public static TileType Classify(int mask)
    {
        switch (CountBits(mask))
        {
            case 0: return TileType.Isolated;
            case 1: return TileType.DeadEnd;
            case 2: return (mask == (MaskN | MaskS) || mask == (MaskE | MaskW))
                        ? TileType.Straight : TileType.Corner;
            case 3: return TileType.TJunction;
            default: return TileType.Cross;
        }
    }

    public TileType GetTileType(Vector2Int n) => Classify(masks[n]);

    /// <summary>Rotates a connection mask 90° clockwise (seen from above) k times.</summary>
    public static int RotateMask(int mask, int k)
    {
        for (int i = 0; i < k; i++)
            mask = ((mask << 1) | (mask >> 3)) & 15;
        return mask;
    }

    // ---------------------------------------------------------------- editing

    /// <summary>
    /// Removes undirected edges between adjacent road nodes. Nodes stay in the graph.
    /// Ignores edges that don't exist. Returns how many were actually removed.
    /// </summary>
    public int RemoveEdges(IEnumerable<(Vector2Int a, Vector2Int b)> edges)
    {
        int removed = 0;
        foreach (var (a, b) in edges)
        {
            if (!nodes.Contains(a) || !nodes.Contains(b)) continue;
            if (!adjacency[a].Remove(b)) continue;   // no such edge
            adjacency[b].Remove(a);

            masks[a] &= ~(1 << (int)DirBetween(a, b));
            masks[b] &= ~(1 << (int)DirBetween(b, a));
            removed++;
        }

        if (removed > 0) distanceFields.Clear();     // cached paths are now stale
        return removed;
    }

    // ---------------------------------------------------------------- navigation

    Dictionary<Vector2Int, int> GetField(Vector2Int goal)
    {
        if (distanceFields.TryGetValue(goal, out var f)) return f;

        f = new Dictionary<Vector2Int, int>();
        if (nodes.Contains(goal))
        {
            var q = new Queue<Vector2Int>();
            f[goal] = 0;
            q.Enqueue(goal);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var nb in adjacency[cur])
                {
                    if (f.ContainsKey(nb)) continue;
                    f[nb] = f[cur] + 1;
                    q.Enqueue(nb);
                }
            }
        }
        distanceFields[goal] = f;
        return f;
    }

    /// <summary>Next node to step to from 'from' to reach 'goal' by the shortest route.</summary>
    public bool NextStep(Vector2Int from, Vector2Int goal, out Vector2Int next)
    {
        next = from;
        if (from == goal) return false;
        var f = GetField(goal);
        if (!f.TryGetValue(from, out int d)) return false; // unreachable

        foreach (var nb in adjacency[from])
            if (f[nb] == d - 1) { next = nb; return true; }
        return false;
    }

    public List<Vector2Int> GetPath(Vector2Int from, Vector2Int goal)
    {
        var path = new List<Vector2Int>();
        if (!nodes.Contains(from) || !GetField(goal).ContainsKey(from)) return path;
        path.Add(from);
        var cur = from;
        while (NextStep(cur, goal, out var nx)) { path.Add(nx); cur = nx; }
        return path;
    }

    /// <summary>True and the node list (A..B inclusive) if B is reachable from A by roads.</summary>
    public bool TryGetPath(Vector2Int a, Vector2Int b, out List<Vector2Int> path)
    {
        path = GetPath(a, b);
        return path.Count > 0;
    }

    public static Dir DirBetween(Vector2Int a, Vector2Int b)
    {
        var d = b - a;
        if (d.y > 0) return Dir.N;
        if (d.x > 0) return Dir.E;
        if (d.y < 0) return Dir.S;
        return Dir.W;
    }

    /// <summary>
    /// What a car at 'pos' currently facing 'heading' should do to go to 'goal'.
    /// outNext / outDir give the node and direction it will end up going.
    /// </summary>
    public Turn GetInstruction(Vector2Int pos, Dir heading, Vector2Int goal,
                               out Vector2Int next, out Dir newHeading)
    {
        next = pos;
        newHeading = heading;
        if (pos == goal) return Turn.Arrived;
        if (!NextStep(pos, goal, out next)) return Turn.NoPath;

        newHeading = DirBetween(pos, next);
        switch (((int)newHeading - (int)heading + 4) % 4)
        {
            case 0: return Turn.Straight;
            case 1: return Turn.Right;
            case 3: return Turn.Left;
            default: return Turn.UTurn;
        }
    }
}