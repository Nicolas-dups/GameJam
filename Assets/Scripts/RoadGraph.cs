using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Construit le graphe routier à partir des enfants d'un objet parent.
///  - noeud = une tuile de route
///  - arête = deux tuiles dont les bords se touchent (et qui se font face, pas en diagonale)
/// La détection se fait avec les dimensions réelles (bounds) des tuiles :
/// plus de grille théorique, donc tailles différentes et petits décalages supportés.
/// </summary>
public class RoadGraph : MonoBehaviour
{
    public static RoadGraph Instance { get; private set; }

    [Tooltip("Objet parent qui contient toutes les tuiles de route en enfants directs")]
    public Transform roadsParent;

    [Tooltip("Écart max (en unités) entre les bords de deux tuiles pour qu'elles soient considérées voisines")]
    public float edgeTolerance = 0.5f;

    [Tooltip("Taille utilisée si une tuile n'a ni Renderer ni Collider")]
    public float fallbackTileSize = 10f;

    Vector3[] pos;        // centre de chaque tuile (= noeud)
    List<int>[] adj;      // voisins de chaque noeud
    float[] half;         // demi-taille de chaque tuile (pour placer les lignes d'arrêt)

    public int NodeCount => pos == null ? 0 : pos.Length;

    void Awake()
    {
        Instance = this;
        Build();
    }

    void Build()
    {
        int n = roadsParent.childCount;
        if (n == 0)
        {
            Debug.LogError("RoadGraph : aucune tuile trouvée dans roadsParent");
            return;
        }

        pos = new Vector3[n];
        adj = new List<int>[n];
        half = new float[n];
        var rects = new Rect[n];   // emprise de chaque tuile sur le plan XZ (x = X, y = Z)

        for (int i = 0; i < n; i++)
        {
            Transform t = roadsParent.GetChild(i);
            pos[i] = t.position;
            adj[i] = new List<int>();
            rects[i] = GetRect(t);
            half[i] = 0.5f * Mathf.Min(rects[i].width, rects[i].height);
        }

        int edges = 0;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (!AreNeighbors(rects[i], rects[j])) continue;
                adj[i].Add(j);
                adj[j].Add(i);
                edges++;
            }
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
            else
            {
                b = new Bounds(t.position, new Vector3(fallbackTileSize, 0f, fallbackTileSize));
            }
        }
        return Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
    }

    bool AreNeighbors(Rect a, Rect b)
    {
        // Recouvrement sur chaque axe : > 0 = ils se chevauchent, ~0 = les bords se touchent, < 0 = écart
        float ox = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
        float oz = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
        float minW = Mathf.Min(a.width, b.width);
        float minH = Mathf.Min(a.height, b.height);

        // Voisins en Z : bords en Z qui se touchent + bonne partie du côté en commun en X
        bool alongZ = Mathf.Abs(oz) <= edgeTolerance && ox > 0.5f * minW;
        // Voisins en X : bords en X qui se touchent + bonne partie du côté en commun en Z
        bool alongX = Mathf.Abs(ox) <= edgeTolerance && oz > 0.5f * minH;
        return alongZ || alongX;
    }

    // ---------- Accès aux noeuds ----------
    public Vector3 NodePos(int node) => pos[node];
    public int RandomNode() => Random.Range(0, pos.Length);
    public IReadOnlyList<int> Neighbors(int node) => adj[node];
    public float NodeHalfSize(int node) => half[node];

    /// <summary>Carrefour = tuile avec au moins 3 voisins.</summary>
    public bool IsIntersection(int node) => adj[node].Count >= 3;

    /// <summary>Tuile la plus proche d'une position monde (plan XZ).</summary>
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

    /// <summary>Plus court chemin (BFS). Liste de noeuds start→goal incluses, ou null.</summary>
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
                if (cameFrom[nb] != -1) continue;
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

    // ---------- Debug : graphe visible dans la vue Scene en mode Play ----------
    void OnDrawGizmos()
    {
        if (!Application.isPlaying || pos == null) return;
        Vector3 up = Vector3.up * 0.5f;
        for (int i = 0; i < pos.Length; i++)
        {
            // rouge = tuile isolée (aucun voisin) → probablement un problème
            Gizmos.color = adj[i].Count == 0 ? Color.red : Color.cyan;
            Gizmos.DrawSphere(pos[i] + up, 0.4f);
            Gizmos.color = Color.cyan;
            foreach (int nb in adj[i])
                if (nb > i) Gizmos.DrawLine(pos[i] + up, pos[nb] + up);
        }
    }
}