using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Graphe routier construit à partir des tuiles enfants d'un objet parent.
/// Gère aussi les tuiles barrées et les sens uniques (posés par le joueur).
/// Le graphe est construit UNE seule fois (Awake) : masquer ou remplacer visuellement une tuile ne le modifie pas.
/// </summary>
public class RoadGraph : MonoBehaviour
{
    public static RoadGraph Instance { get; private set; }

    [Tooltip("Objet parent qui contient toutes les tuiles de route en enfants directs")]
    public Transform roadsParent;
    [Tooltip("Écart max entre les bords de deux tuiles pour qu'elles soient voisines")]
    public float edgeTolerance = 0.5f;
    [Tooltip("Taille utilisée si une tuile n'a ni Renderer ni Collider")]
    public float fallbackTileSize = 10f;

    Vector3[] pos;
    List<int>[] adj;
    float[] half;
    Transform[] tiles;

    // Règles dynamiques
    readonly HashSet<int> blocked = new HashSet<int>();
    readonly Dictionary<int, Vector3> oneWays = new Dictionary<int, Vector3>();

    public int NodeCount => pos == null ? 0 : pos.Length;

    void Awake()
    {
        Instance = this;
        Build();
    }

    void Build()
    {
        blocked.Clear();
        oneWays.Clear();

        int n = roadsParent.childCount;
        if (n == 0)
        {
            Debug.LogError("RoadGraph : aucune tuile trouvée dans roadsParent");
            return;
        }

        pos = new Vector3[n];
        adj = new List<int>[n];
        half = new float[n];
        tiles = new Transform[n];
        var rects = new Rect[n];

        for (int i = 0; i < n; i++)
        {
            Transform t = roadsParent.GetChild(i);
            tiles[i] = t;
            pos[i] = t.position;
            adj[i] = new List<int>();
            rects[i] = GetRect(t);
            half[i] = 0.5f * Mathf.Min(rects[i].width, rects[i].height);
        }

        int edges = 0;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (!AreNeighbors(rects[i], rects[j])) continue;
                adj[i].Add(j);
                adj[j].Add(i);
                edges++;
            }

        Debug.Log($"RoadGraph : {n} tuiles, {edges} liaisons");
    }

    Rect GetRect(Transform t)
    {
        Bounds b;
        var renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            b = renderers[0].bounds;
            for (int k = 1; k < renderers.Length; k++) b.Encapsulate(renderers[k].bounds);
        }
        else
        {
            var colliders = t.GetComponentsInChildren<Collider>();
            if (colliders.Length > 0)
            {
                b = colliders[0].bounds;
                for (int k = 1; k < colliders.Length; k++) b.Encapsulate(colliders[k].bounds);
            }
            else b = new Bounds(t.position, new Vector3(fallbackTileSize, 0f, fallbackTileSize));
        }
        return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
    }

    bool AreNeighbors(Rect a, Rect b)
    {
        float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float oz = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        float minW = Mathf.Min(a.width, b.width);
        float minH = Mathf.Min(a.height, b.height);

        bool alongZ = Mathf.Abs(oz) <= edgeTolerance && ox > 0.5f * minW;
        bool alongX = Mathf.Abs(ox) <= edgeTolerance && oz > 0.5f * minH;
        return alongZ || alongX;
    }

    // ---------- Accès aux noeuds ----------
    public Vector3 NodePos(int node) => pos[node];
    /// <summary>Transform de la tuile d'origine de ce noeud (pour la masquer / la remplacer visuellement).</summary>
    public Transform NodeTile(int node) => tiles != null && node >= 0 && node < tiles.Length ? tiles[node] : null;
    public int RandomNode() => Random.Range(0, pos.Length);
    public int RandomNode(System.Random r) => r.Next(pos.Length);
    public IReadOnlyList<int> Neighbors(int node) => adj[node];
    public float NodeHalfSize(int node) => half[node];
    public bool IsIntersection(int node) => adj[node].Count >= 3;

    public int WorldToNode(Vector3 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < pos.Length; i++)
        {
            float dx = pos[i].x - p.x, dz = pos[i].z - p.z;
            float d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    // ---------- Règles dynamiques ----------
    public void SetBlocked(int node, bool on) { if (on) blocked.Add(node); else blocked.Remove(node); }
    public bool IsBlocked(int node) => blocked.Contains(node);

    public void SetOneWay(int node, Vector3 dir, bool on)
    {
        if (on) oneWays[node] = dir.normalized; else oneWays.Remove(node);
    }

    /// <summary>Peut-on aller de a vers b (voisins) ? Tient compte des barrages et sens uniques.</summary>
    public bool CanTravel(int a, int b)
    {
        if (blocked.Contains(b)) return false;
        Vector3 d = pos[b] - pos[a];
        d.y = 0f;
        d.Normalize();
        if (oneWays.TryGetValue(a, out var fa) && Vector3.Dot(d, fa) < -0.5f) return false;
        if (oneWays.TryGetValue(b, out var fb) && Vector3.Dot(d, fb) < -0.5f) return false;
        return true;
    }

    /// <summary>Plus court chemin (BFS) en respectant les règles. Null si impossible.</summary>
    public List<int> FindPath(int start, int goal)
    {
        var cameFrom = new int[pos.Length];
        for (int i = 0; i < cameFrom.Length; i++) cameFrom[i] = -1;

        var queue = new Queue<int>();
        queue.Enqueue(start);
        cameFrom[start] = start;

        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            if (cur == goal) break;
            foreach (int nb in adj[cur])
            {
                if (cameFrom[nb] != -1 || !CanTravel(cur, nb)) continue;
                cameFrom[nb] = cur;
                queue.Enqueue(nb);
            }
        }

        if (cameFrom[goal] == -1) return null;

        var path = new List<int>();
        for (int c = goal; c != start; c = cameFrom[c]) path.Add(c);
        path.Add(start);
        path.Reverse();
        return path;
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying || pos == null) return;
        Vector3 up = Vector3.up * 0.5f;
        for (int i = 0; i < pos.Length; i++)
        {
            Gizmos.color = adj[i].Count == 0 ? Color.red : Color.cyan;
            if (blocked.Contains(i)) Gizmos.color = Color.magenta;
            Gizmos.DrawSphere(pos[i] + up, 0.4f);
            Gizmos.color = Color.cyan;
            foreach (int nb in adj[i])
                if (nb > i) Gizmos.DrawLine(pos[i] + up, pos[nb] + up);
        }
    }
}