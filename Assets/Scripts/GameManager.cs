using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Un emplacement de pose (cube / rectangle semi-transparent) sur une tuile.
/// Un emplacement ne porte qu'UN élément à la fois (occupant).
/// </summary>
public class PlacementSpot : MonoBehaviour
{
    public int node;
    public PlacementKind kind;  // Edge / CenterLong / CenterShort / WholeTile
    public Vector3 anchor;      // point au sol où sera posé le prefab
    public float yaw;           // rotation Y ajoutée à l'orientation du prefab (visuel seulement)
    public Vector3 travelDir;   // sens de circulation de référence pour l'orientation du prefab
    public Renderer rend;
    public RoadElement occupant; // élément actuellement posé ici (null = libre)
    public TileShape shape;      // forme de la tuile (utile pour les emplacements WholeTile)
    public Quaternion tileRotation = Quaternion.identity; // rotation de la tuile d'origine (WholeTile)
    public Transform tile;       // RoadTile d'origine à désactiver quand l'élément la remplace (WholeTile)
}

/// <summary>Forme d'une tuile (pour choisir le bon prefab de tuile entière). None = tuile droite.</summary>
public enum TileShape { None, L, T, X }

/// <summary>
/// Boucle de jeu :  PLANIFICATION (poser des éléments) -> SIMULATION -> ACCIDENT -> retour à la planification
/// (les voitures repartent exactement du même point de départ, les éléments posés restent).
/// Si la ville tient `duration` secondes sans accident : victoire.
/// Nécessite l'ancien Input Manager (Input.GetMouseButton...).
/// L'interface est un Canvas construit dans l'éditeur : on glisse ses éléments dans la section "UI (Canvas)".
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum Phase { Planning, Running, Crashed, Won }

    [System.Serializable]
    public class VehicleEntry
    {
        [Tooltip("Prefab avec CarAI (ses paramètres de comportement sont réglés sur le prefab)")]
        public GameObject prefab;
        [Min(0f), Tooltip("Probabilité relative d'apparition")]
        public float weight = 1f;
        public string Name => prefab != null ? prefab.name : "(prefab manquant)";
    }

    [System.Serializable]
    public class ToolDef
    {
        [Tooltip("Prefab dont la RACINE porte un script RoadElement (StopSign, TrafficLight...)")]
        public GameObject prefab;
        public int cost = 50;
        [Min(1), Tooltip("Nombre max d'éléments de CE type sur une même tuile.\n" +
                         "Ignoré pour les éléments de bord sur les intersections (X, T, L) : la limite y est le nombre d'angles (4, 2, 1), tous types confondus.")]
        public int maxPerTile = 1;

        [Header("Tuile entière (PlacementKind.WholeTile) - prefabs optionnels selon la forme")]
        [Tooltip("Utilisé pour les tuiles en X (sinon 'prefab')")] public GameObject prefabX;
        [Tooltip("Utilisé pour les tuiles en T (sinon 'prefab')")] public GameObject prefabT;
        [Tooltip("Utilisé pour les angles L (sinon 'prefab')")] public GameObject prefabL;

        public string Label => prefab != null ? prefab.name : "(prefab manquant)";

        public GameObject PrefabFor(TileShape shape)
        {
            GameObject p = shape == TileShape.X ? prefabX : shape == TileShape.T ? prefabT : shape == TileShape.L ? prefabL : null;
            return p != null ? p : prefab;
        }
        [System.NonSerialized] public RoadElement element;
        [System.NonSerialized] public System.Type type;
    }

    [Header("Véhicules")]
    public List<VehicleEntry> vehicles = new List<VehicleEntry>();
    [Tooltip("Prefab de la voiture de police (CarAI ; PoliceCar est ajouté si absent)")]
    public GameObject policePrefab;
    public int carCount = 12;
    public int seed = 1;

    [System.Serializable]
    public class PedestrianEntry
    {
        [Tooltip("Prefab avec Pedestrian (vitesse, tendance à traverser hors passage... réglées sur le prefab) et un Animator (booléens Walking / Running)")]
        public GameObject prefab;
        [Min(0f), Tooltip("Probabilité relative d'apparition")]
        public float weight = 1f;
        public string Name => prefab != null ? prefab.name : "(prefab manquant)";
    }

    [Header("Piétons")]
    [Tooltip("Types de piétons. Si vide, une capsule est générée. Le script Pedestrian est ajouté si absent du prefab.")]
    public List<PedestrianEntry> pedestrians = new List<PedestrianEntry>();
    public int pedestrianCount = 10;

    [Header("Outils (un prefab par élément de voirie)")]
    [Tooltip("Même ordre que le tableau 'toolButtons' de l'UI")]
    public ToolDef[] tools;

    [Header("Emplacements de bord de route (PlacementKind.Edge)")]
    public float spotSize = 1f;
    public float spotSpacing = 1.3f;
    [Tooltip("Distance entre l'axe de la route et les cubes de bord")]
    public float spotSideOffset = 3.5f;
    [Tooltip("Rotation Y ajoutée aux prefabs posés sur les bords de route droits (visuel seulement)")]
    public float rowYaw = 0f;
    [Tooltip("Coché : un cube d'angle est aussi posé à l'intérieur des tuiles de virage")]
    public bool cornerOnTurns = true;

    [System.Serializable]
    public class CornerSetup
    {
        [Tooltip("Distance du centre de la tuile, le long du bras d'arrivée (m)")]
        public float along = 8f;
        [Tooltip("Décalage latéral vers le coin, perpendiculairement au bras d'arrivée (m)")]
        public float side = 8f;
        [Tooltip("Rotation Y ajoutée à l'orientation du prefab (°) : visuel seulement, la logique des voitures ne change pas")]
        public float yaw = 180f;
    }

    [Header("Cubes d'angle (T, croisements, virages)")]
    public CornerSetup corner = new CornerSetup();

    [Header("Emplacements au centre (PlacementKind.CenterLong / CenterShort)")]
    [Tooltip("Rectangle (PlacementKind.CenterLong) : x = largeur en travers de la route, y = profondeur le long de la route")]
    public Vector2 longSize = new Vector2(5.5f, 2f);
    [Tooltip("Rotation Y ajoutée aux prefabs posés sur un rectangle (visuel seulement)")]
    public float longYaw = 0f;
    [Range(0f, 1f), Tooltip("Sur les T / croisements / virages : un rectangle par bras, à cette fraction de la demi-taille de la tuile")]
    public float longArmFactor = 0.6f;
    [Tooltip("Taille du petit cube central (PlacementKind.CenterShort)")]
    public float shortSize = 2f;
    [Tooltip("Rotation Y ajoutée aux prefabs posés sur le petit cube central (visuel seulement)")]
    public float shortYaw = 0f;

    [Header("Tuile entière (PlacementKind.WholeTile : passage piéton)")]
    [Tooltip("Rotation Y ajoutée à la rotation de la tuile remplacée (°), si le prefab de remplacement n'a pas la même orientation de base que vos tuiles")]
    public float wholeTileYaw = 0f;
    [Tooltip("Coché : l'emplacement 'tuile entière' existe aussi sur les tuiles droites (passage piéton classique)")]
    public bool wholeTileOnStraight = true;

    [Header("Matériaux des emplacements")]
    [Tooltip("Optionnel (sinon un matériau transparent est généré)")]
    public Material spotMaterial;
    public Material spotHoverMaterial;
    [Tooltip("Optionnel : remplace les matériaux de l'aperçu (ex : matériau fantôme transparent)")]
    public Material ghostMaterial;

    [Header("Règles")]
    public float duration = 60f;          // secondes à tenir sans accident
    public int startMoney = 1000;

    [Header("UI (Canvas) - panneaux, un par phase")]
    public GameObject panelPlanning;
    public GameObject panelRunning;
    public GameObject panelCrashed;
    public GameObject panelWon;

    [Header("UI (Canvas) - textes")]
    [Tooltip("Budget / essai / record (visible dans toutes les phases)")]
    public TMP_Text headerText;
    public TMP_Text planningHelpText;
    public TMP_Text runningText;
    public TMP_Text crashedText;

    [Header("UI (Canvas) - boutons")]
    [Tooltip("Dans le MEME ORDRE que le tableau 'tools'")]
    public Button[] toolButtons;
    public Button launchButton;
    public Button clearMarkersButton;
    [Tooltip("Boutons x1, x2, x4 dans cet ordre")]
    public Button[] speedButtons;
    public Button stopButton;
    public Button modifyAfterCrashButton;
    public Button relaunchButton;
    public Button modifyAfterWinButton;

    [Header("UI (Canvas) - couleurs")]
    public Color normalColor = Color.white;
    public Color selectedColor = new Color(1f, 0.85f, 0.3f);

    public Phase CurrentPhase { get; private set; } = Phase.Planning;

    int selected, money, attempt = 1, speedIndex;
    bool flip, allDirections;
    float elapsed, bestTime;
    string crashReason = "";

    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<GameObject> markers = new List<GameObject>();
    Transform elementsHolder, spotsHolder;
    GameObject ghost;
    GameObject ghostPrefab;
    readonly Dictionary<int, int> cornerCount = new Dictionary<int, int>();   // nombre d'angles par tuile (X:4, T:2, L:1, droite:0)
    PlacementSpot hovered;
    readonly List<PlacementSpot> allSpots = new List<PlacementSpot>();
    readonly Dictionary<RoadElement, PlacementSpot> placedSpot = new Dictionary<RoadElement, PlacementSpot>();
    float lastPlaceTime = -10f;
    static readonly float[] speeds = { 1f, 2f, 4f };

    void Awake()
    {
        Instance = this;
        Time.fixedDeltaTime = 1f / 120f;   // simulation fine et déterministe
        Time.timeScale = 0f;
        money = startMoney;
        elementsHolder = new GameObject("RoadElements").transform;

        if (tools == null) tools = new ToolDef[0];
        foreach (var t in tools)
        {
            if (t.prefab == null) { Debug.LogWarning("Outil sans prefab"); continue; }
            t.element = t.prefab.GetComponent<RoadElement>();
            if (t.element == null) Debug.LogWarning($"Outil '{t.Label}' : la racine du prefab n'a pas de script RoadElement");
            else t.type = t.element.GetType();
        }
        SetupUI();
    }

    void Start()
    {
        if (spotMaterial == null) spotMaterial = MakeMat(new Color(0.2f, 0.8f, 1f, 0.25f));
        if (spotHoverMaterial == null) spotHoverMaterial = MakeMat(new Color(1f, 0.9f, 0.1f, 0.7f));
        BuildSpots();
    }

    static Material MakeMat(Color c)
    {
        // Sprites/Default gère la transparence dans tous les pipelines (Built-in, URP...)
        var m = new Material(Shader.Find("Sprites/Default"));
        m.color = c;
        return m;
    }

    // =================================================================
    //  Génération des emplacements de placement
    // =================================================================
    void BuildSpots()
    {
        var graph = RoadGraph.Instance;
        spotsHolder = new GameObject("PlacementSpots").transform;

        for (int node = 0; node < graph.NodeCount; node++)
        {
            // directions (alignées sur les axes) des liaisons de la tuile
            var dirs = new List<Vector3>();
            foreach (int nb in graph.Neighbors(node))
            {
                Vector3 d = graph.NodePos(nb) - graph.NodePos(node);
                d = Mathf.Abs(d.x) > Mathf.Abs(d.z)
                    ? new Vector3(Mathf.Sign(d.x), 0f, 0f)
                    : new Vector3(0f, 0f, Mathf.Sign(d.z));
                if (!dirs.Contains(d)) dirs.Add(d);
            }
            if (dirs.Count == 0) continue;

            Vector3 c = graph.NodePos(node);
            float half = graph.NodeHalfSize(node);
            float limit = half * 0.85f;
            bool straight = dirs.Count == 1 || (dirs.Count == 2 && Vector3.Dot(dirs[0], dirs[1]) < -0.9f);

            // nombre d'angles = paires de bras perpendiculaires (X : 4, T : 2, L : 1) :
            // c'est le nombre max d'éléments de bord sur une intersection
            int corners = 0;
            for (int i = 0; i < dirs.Count; i++)
                for (int j = i + 1; j < dirs.Count; j++)
                    if (Mathf.Abs(Vector3.Dot(dirs[i], dirs[j])) <= 0.1f) corners++;
            cornerCount[node] = corners;

            // ---- Emplacements de bord de route (panneaux, feux...) ----
            if (straight)
            {
                Vector3 r = Vector3.Cross(Vector3.up, dirs[0]);
                AddRow(node, c, dirs[0], -limit, limit, r, -r);          // les deux bords
            }
            else
            {
                // T / croisement / virage : un cube par coin + des rangées normales sur les côtés sans coin
                bool turn = dirs.Count == 2;
                if (!turn || cornerOnTurns) AddCorners(node, c, dirs, corner);
                AddFreeSides(node, c, dirs, limit);
            }

            // ---- Emplacements au centre (rectangles longs + petit cube) ----
            AddCenterSpots(node, c, dirs, straight, half);

            // ---- Tuile entière (passage piéton) : X, T, angles L, et tuiles droites si activé ----
            if (!straight || wholeTileOnStraight) AddWholeTileSpot(node, dirs, half, straight);
        }
    }

    /// <summary>Un emplacement recouvrant toute la tuile. Il sert à REMPLACER la tuile par un prefab de tuile.</summary>
    void AddWholeTileSpot(int node, List<Vector3> dirs, float half, bool straight)
    {
        var graph = RoadGraph.Instance;
        Transform tile = graph.NodeTile(node);
        Vector3 center = tile != null ? tile.position : graph.NodePos(node);

        var spot = CreateSpot(node, PlacementKind.WholeTile, center, dirs[0], wholeTileYaw, half * 2f);
        spot.shape = straight ? TileShape.None
                   : dirs.Count >= 4 ? TileShape.X
                   : dirs.Count == 3 ? TileShape.T : TileShape.L;
        spot.tile = tile != null ? tile : graph.FindTileAt(center);   // la RoadTile de CE noeud (pas de recherche géométrique)
        spot.tileRotation = spot.tile != null ? spot.tile.rotation : Quaternion.identity;
    }

    /// <summary>Rangée de cubes le long d'un bras, sur les côtés donnés.</summary>
    void AddRow(int node, Vector3 center, Vector3 axis, float t0, float t1, params Vector3[] sides)
    {
        if (t1 <= t0) return;
        int count = Mathf.Max(1, Mathf.RoundToInt((t1 - t0) / spotSpacing));

        for (int i = 0; i < count; i++)
        {
            float t = Mathf.Lerp(t0, t1, (i + 0.5f) / count);
            foreach (var side in sides)
            {
                Vector3 anchor = center + axis * t + side * spotSideOffset;
                Vector3 travel = Vector3.Cross(side, Vector3.up);     // sens dont ce bord est à droite
                CreateSpot(node, PlacementKind.Edge, anchor, travel, rowYaw);
            }
        }
    }

    /// <summary>
    /// Côtés sans coin : ceux où aucun bras perpendiculaire ne rejoint la route
    /// (extérieur de la barre d'un T, extérieur d'un virage).
    /// </summary>
    void AddFreeSides(int node, Vector3 center, List<Vector3> dirs, float limit)
    {
        foreach (var d in dirs)
        {
            bool hasOpposite = dirs.Contains(-d);
            if (hasOpposite && d.x + d.z < 0f) continue;            // axe traité une seule fois
            Vector3 right = Vector3.Cross(Vector3.up, d);

            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 side = right * s;
                if (dirs.Contains(side)) continue;                  // un bras de ce côté : c'est un coin
                AddRow(node, center, d, hasOpposite ? -limit : 0f, limit, side);
            }
        }
    }

    /// <summary>
    /// Carrefours / virages : UN seul cube par coin formé par deux bras perpendiculaires.
    /// Il est orienté pour la voie de DROITE : celle des voitures qui arrivent par le bras
    /// dont ce coin est sur la droite (ex. T : coin bas-gauche = voitures venant de la gauche,
    /// coin bas-droit = voitures venant du bas).
    /// </summary>
    void AddCorners(int node, Vector3 center, List<Vector3> dirs, CornerSetup setup)
    {
        for (int i = 0; i < dirs.Count; i++)
            for (int j = i + 1; j < dirs.Count; j++)
            {
                if (Mathf.Abs(Vector3.Dot(dirs[i], dirs[j])) > 0.1f) continue;   // bras opposés : pas de coin

                // a = bras d'arrivée, b = bras formant le coin, avec b à droite du trafic entrant (qui roule vers -a)
                Vector3 a = dirs[i], b = dirs[j];
                if (Vector3.Dot(Vector3.Cross(Vector3.up, -a), b) < 0.5f) { var tmp = a; a = b; b = tmp; }

                Vector3 anchor = center + a * setup.along + b * setup.side;
                CreateSpot(node, PlacementKind.Edge, anchor, -a, setup.yaw);
            }
    }

    /// <summary>
    /// Emplacements au centre de la route :
    ///  - un petit cube au centre de chaque tuile (agent de circulation...)
    ///  - des rectangles en travers de la route (passage piéton, dos d'âne, route barrée...) :
    ///    un seul au centre d'une tuile droite, un par bras sur les T / croisements / virages.
    /// </summary>
    void AddCenterSpots(int node, Vector3 center, List<Vector3> dirs, bool straight, float half)
    {
        // court : toujours au centre
        CreateSpot(node, PlacementKind.CenterShort, center, dirs[0], shortYaw);

        // long
        if (straight)
        {
            CreateSpot(node, PlacementKind.CenterLong, center, dirs[0], longYaw);
        }
        else
        {
            foreach (var d in dirs)
                CreateSpot(node, PlacementKind.CenterLong, center + d * (half * longArmFactor), -d, longYaw);
        }
    }

    PlacementSpot CreateSpot(int node, PlacementKind kind, Vector3 anchor, Vector3 travel, float yaw, float size = 0f)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Spot_" + kind;
        go.transform.SetParent(spotsHolder, false);
        go.transform.position = anchor + Vector3.up * 0.15f;

        switch (kind)
        {
            case PlacementKind.CenterLong:
                // axe Z local = sens de la route ; X local = en travers de la route
                go.transform.rotation = Quaternion.LookRotation(travel, Vector3.up);
                go.transform.localScale = new Vector3(longSize.x, 0.3f, longSize.y);
                break;
            case PlacementKind.CenterShort:
                go.transform.localScale = new Vector3(shortSize, 0.3f, shortSize);
                break;
            case PlacementKind.WholeTile:
                // dalle fine couvrant toute la tuile
                go.transform.position = anchor + Vector3.up * 0.05f;
                go.transform.localScale = new Vector3(size, 0.1f, size);
                break;
            default:
                go.transform.localScale = new Vector3(spotSize, 0.3f, spotSize);
                break;
        }

        var spot = go.AddComponent<PlacementSpot>();
        spot.node = node;
        spot.kind = kind;
        spot.anchor = anchor;
        spot.travelDir = travel;
        spot.yaw = yaw;
        spot.rend = go.GetComponent<Renderer>();
        spot.rend.sharedMaterial = spotMaterial;
        allSpots.Add(spot);
        return spot;
    }

    void SetSpotsVisible(bool v)
    {
        if (spotsHolder != null && spotsHolder.gameObject.activeSelf != v) spotsHolder.gameObject.SetActive(v);
    }

    /// <summary>
    /// N'affiche que les emplacements où l'outil sélectionné peut réellement être posé
    /// (bon type, libre, quota par tuile non atteint). Les emplacements masqués ne sont pas non plus cliquables.
    /// </summary>
    void RefreshSpotVisibility()
    {
        bool hasTool = selected >= 0 && selected < tools.Length && tools[selected].element != null;
        ToolDef tool = hasTool ? tools[selected] : null;

        foreach (var s in allSpots)
        {
            if (s == null) continue;
            bool show = hasTool && CanPlace(tool, s);
            if (s.gameObject.activeSelf != show) s.gameObject.SetActive(show);
        }
    }

    /// <summary>L'outil peut-il être posé sur cet emplacement ? (hors budget)</summary>
    bool CanPlace(ToolDef tool, PlacementSpot spot)
    {
        if (tool == null || tool.element == null || spot == null) return false;
        if (spot.occupant != null) return false;                                // emplacement déjà pris
        if (spot.kind != tool.element.placement) return false;                  // mauvais type d'emplacement

        // tuile entière : si la RoadTile d'origine est déjà masquée, un élément la remplace
        if (spot.kind == PlacementKind.WholeTile && spot.tile != null && !spot.tile.gameObject.activeSelf) return false;

        int node = spot.node;
        if (tool.element.placement == PlacementKind.Edge && cornerCount.TryGetValue(node, out int corners) && corners > 0)
        {
            // intersection (X, T, L) : au plus un élément de bord par angle (4, 2, 1), tous types confondus
            // (le passage piéton, qui remplace la tuile, ne compte pas)
            int edges = 0;
            foreach (var e in RoadElement.At(node)) if (e != null && e.placement == PlacementKind.Edge) edges++;
            if (edges >= corners) return false;
        }
        else
        {
            int same = 0;
            foreach (var e in RoadElement.At(node)) if (e != null && e.GetType() == tool.type) same++;
            if (same >= tool.maxPerTile) return false;                          // quota de ce type atteint sur la tuile
        }

        if (tool.type == typeof(RoadBlock) && RoadGraph.Instance.IsBlocked(node)) return false;
        return true;
    }

    // =================================================================
    //  Cycle de jeu
    // =================================================================
    void StartRun()
    {
        Cleanup();
        Infractions.Reset();
        foreach (var e in RoadElement.All) e.ResetState();

        var graph = RoadGraph.Instance;
        var rng = new System.Random(seed);
        var used = new HashSet<int>();
        int made = 0;
        for (int tries = 0; made < carCount && tries < carCount * 30; tries++)
        {
            int node = rng.Next(graph.NodeCount);
            if (graph.IsBlocked(node) || !used.Add(node)) continue;
            SpawnCar(node, rng.Next(), false);
            made++;
        }
        foreach (var e in new List<RoadElement>(RoadElement.All)) e.OnRunStart(rng.Next());

        // Piétons (graine séparée pour ne pas modifier le scénario des voitures)
        var pedRng = new System.Random(seed + 12345);
        for (int i = 0; i < pedestrianCount; i++)
            SpawnPedestrian(pedRng.Next(graph.NodeCount), pedRng.Next());

        elapsed = 0f;
        CurrentPhase = Phase.Running;
        Time.timeScale = speeds[speedIndex];
    }

    VehicleEntry PickVehicle(int carSeed)
    {
        float total = 0f;
        foreach (var v in vehicles) if (v.prefab != null) total += v.weight;
        if (total <= 0f) return null;

        float r = (float)(new System.Random(carSeed ^ 0x5bd1e995).NextDouble() * total);
        foreach (var v in vehicles)
        {
            if (v.prefab == null) continue;
            r -= v.weight;
            if (r <= 0f) return v;
        }
        return vehicles[vehicles.Count - 1];
    }

    public CarAI SpawnCar(int node, int carSeed, bool police)
    {
        var graph = RoadGraph.Instance;
        Vector3 p = graph.NodePos(node);
        p.y = 0f;

        VehicleEntry entry = police ? null : PickVehicle(carSeed);
        GameObject prefab = police ? policePrefab : (entry != null ? entry.prefab : null);

        GameObject go;
        if (prefab != null) go = Instantiate(prefab, p, Quaternion.identity);
        else
        {
            // secours si aucun prefab n'est assigné
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = p;
            go.transform.localScale = new Vector3(1.8f, 1.1f, 4f);
            Color fc = police ? new Color(0.1f, 0.3f, 1f) : Color.HSVToRGB((carSeed % 1000) / 1000f, 0.6f, 0.9f);
            go.GetComponent<Renderer>().material.color = fc;
        }
        go.name = (prefab != null ? prefab.name : "Car") + "_" + carSeed;

        var car = go.GetComponent<CarAI>();
        if (car == null) car = go.AddComponent<CarAI>();
        car.wanderRandomly = true;
        car.isPolice = police;

        if (police && go.GetComponent<PoliceCar>() == null) go.AddComponent<PoliceCar>();
        car.Begin(carSeed);
        spawned.Add(go);
        return car;
    }

    PedestrianEntry PickPedestrian(int pedSeed)
    {
        float total = 0f;
        foreach (var v in pedestrians) if (v.prefab != null) total += v.weight;
        if (total <= 0f) return null;

        float r = (float)(new System.Random(pedSeed ^ 0x2c1b3c6d).NextDouble() * total);
        foreach (var v in pedestrians)
        {
            if (v.prefab == null) continue;
            r -= v.weight;
            if (r <= 0f) return v;
        }
        return pedestrians[pedestrians.Count - 1];
    }

    /// <summary>Fait apparaître un piéton concret (prefab choisi au hasard pondéré) qui se déplace dans le graphe routier.</summary>
    public Pedestrian SpawnPedestrian(int node, int pedSeed)
    {
        var entry = PickPedestrian(pedSeed);
        GameObject prefab = entry != null ? entry.prefab : null;

        GameObject go;
        bool fallback = prefab == null;
        if (!fallback) go = Instantiate(prefab);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * 0.5f;
            go.GetComponent<Renderer>().material.color = Color.HSVToRGB((pedSeed % 1000) / 1000f, 0.6f, 0.9f);
        }
        go.name = (fallback ? "Pedestrian" : prefab.name) + "_" + pedSeed;

        var ped = go.GetComponent<Pedestrian>();
        if (ped == null) ped = go.AddComponent<Pedestrian>();
        if (fallback) ped.heightOffset = 0.5f;
        ped.Begin(node, pedSeed);
        return ped;
    }

    void Cleanup()
    {
        foreach (var go in spawned) if (go != null) { go.SetActive(false); Destroy(go); }
        spawned.Clear();
        foreach (var p in new List<Pedestrian>(Pedestrian.All)) { p.gameObject.SetActive(false); Destroy(p.gameObject); }
    }

    void EnterPlanning()
    {
        Cleanup();
        Infractions.Reset();
        CurrentPhase = Phase.Planning;
        Time.timeScale = 0f;
    }

    void Crash(Vector3 where, string reason)
    {
        if (CurrentPhase != Phase.Running) return;
        CurrentPhase = Phase.Crashed;
        Time.timeScale = 0f;
        crashReason = reason;
        bestTime = Mathf.Max(bestTime, elapsed);

        // Repère rouge conservé entre les essais : indique où ça a coincé
        var m = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(m.GetComponent<Collider>());
        m.transform.position = where + Vector3.up * 3f;
        m.transform.localScale = Vector3.one * 2f;
        m.GetComponent<Renderer>().material.color = Color.red;
        markers.Add(m);
        attempt++;
    }

    void Win()
    {
        CurrentPhase = Phase.Won;
        Time.timeScale = 0f;
        bestTime = duration;
    }

    void FixedUpdate()
    {
        if (CurrentPhase != Phase.Running) return;
        elapsed += Time.fixedDeltaTime;
        CheckAccidents();
        if (CurrentPhase == Phase.Running && elapsed >= duration) Win();
    }

    void CheckAccidents()
    {
        var cars = CarAI.Cars;

        for (int i = 0; i < cars.Count; i++)
            for (int j = i + 1; j < cars.Count; j++)
            {
                Vector3 d = cars[i].transform.position - cars[j].transform.position;
                d.y = 0f;
                float r = cars[i].collisionRadius + cars[j].collisionRadius;
                if (d.sqrMagnitude < r * r)
                {
                    Crash((cars[i].transform.position + cars[j].transform.position) * 0.5f, "Collision entre deux voitures");
                    return;
                }
            }

        foreach (var ped in Pedestrian.All)
            foreach (var car in cars)
            {
                Vector3 l = car.transform.InverseTransformPoint(ped.transform.position);
                if (Mathf.Abs(l.z) < car.halfLength && Mathf.Abs(l.x) < car.halfWidth)
                {
                    Crash(ped.transform.position, "Piéton renversé");
                    return;
                }
            }
    }

    // =================================================================
    //  Placement des éléments
    // =================================================================
    void Update()
    {
        if (CurrentPhase == Phase.Planning) HandlePlacement();
        else
        {
            SetSpotsVisible(false);
            ClearHover();
            if (ghost != null) ghost.SetActive(false);
        }

        RefreshUI();
    }

    void ClearHover()
    {
        if (hovered != null && hovered.rend != null) hovered.rend.sharedMaterial = spotMaterial;
        hovered = null;
    }

    /// <summary>Emplacement visible le plus proche sous le rayon (les emplacements masqués n'ont pas de collider actif).</summary>
    PlacementSpot FindSpot(Ray ray, out float dist)
    {
        PlacementSpot best = null;
        dist = float.MaxValue;
        foreach (var h in Physics.RaycastAll(ray, 1000f))
        {
            var s = h.collider.GetComponent<PlacementSpot>();
            if (s != null && h.distance < dist) { dist = h.distance; best = s; }
        }
        return best;
    }

    void EnsureGhost(GameObject prefab)
    {
        if (ghost != null && ghostPrefab == prefab) return;
        if (ghost != null) Destroy(ghost);
        ghost = null;
        ghostPrefab = prefab;
        if (prefab == null) return;

        ghost = Instantiate(prefab);
        ghost.name = "Ghost";
        var el = ghost.GetComponent<RoadElement>();
        if (el != null) el.enabled = false;                       // ne s'enregistre pas, ne simule rien
        foreach (var c in ghost.GetComponentsInChildren<Collider>()) Destroy(c);

        if (ghostMaterial != null)
            foreach (var r in ghost.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = ghostMaterial;
                r.sharedMaterials = mats;
            }
        ghost.SetActive(false);
    }

    /// <summary>
    /// Élément déjà posé sous la souris (le plus proche de la caméra) + sa distance.
    /// Utilise les colliders ; pour les prefabs SANS collider, secours : boîte englobante des renderers.
    /// </summary>
    RoadElement FindPlaced(Ray ray, out float dist)
    {
        RoadElement best = null;
        dist = float.MaxValue;

        // Une tuile entière (passage piéton) couvre toute la tuile : pour éviter de la retirer par erreur,
        // elle n'est cliquable que lorsque son outil est sélectionné.
        bool wholeOk = selected >= 0 && selected < tools.Length && tools[selected].element != null
                       && tools[selected].element.placement == PlacementKind.WholeTile;

        foreach (var h in Physics.RaycastAll(ray, 1000f))
        {
            var e = h.collider.GetComponentInParent<RoadElement>();
            if (e == null || !e.IsPlaced) continue;
            if (e.placement == PlacementKind.WholeTile && !wholeOk) continue;
            if (wholeOk && e.placement != PlacementKind.WholeTile) continue;   // un panneau/feu ne doit pas "manger" le clic
            if (h.distance < dist) { dist = h.distance; best = e; }
        }

        foreach (var e in RoadElement.All)
        {
            if (e == null || e.GetComponentInChildren<Collider>() != null) continue;   // déjà géré par les colliders
            if (e.placement == PlacementKind.WholeTile && !wholeOk) continue;
            if (wholeOk && e.placement != PlacementKind.WholeTile) continue;
            var rends = e.GetComponentsInChildren<Renderer>();
            if (rends.Length == 0) continue;

            Bounds b = rends[0].bounds;
            for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

            if (b.IntersectRay(ray, out float d) && d < dist) { dist = d; best = e; }
        }
        return best;
    }

    void HandlePlacement()
    {
        var cam = Camera.main;
        if (cam == null) return;

        SetSpotsVisible(true);
        RefreshSpotVisibility();      // seuls les emplacements adaptés à l'outil sélectionné restent visibles
        ClearHover();
        if (ghost != null) ghost.SetActive(false);

        if (Input.GetKeyDown(KeyCode.R)) flip = !flip;
        if (Input.GetKeyDown(KeyCode.Tab)) allDirections = !allDirections;

        // Souris au-dessus du Canvas : on ne pose rien dans la ville
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        RoadElement placed = FindPlaced(ray, out float placedDist);
        PlacementSpot spot = FindSpot(ray, out float spotDist);

        // 1) Un élément posé est plus proche de la caméra que tout emplacement visible : un clic gauche le retire
        if (placed != null && (spot == null || placedDist <= spotDist))
        {
            // petit délai pour ne pas retirer par accident un élément qu'on vient de poser
            if (Input.GetMouseButtonDown(0) && Time.unscaledTime - lastPlaceTime > 0.3f) RemoveElement(placed);
            return;
        }

        // 2) Sinon, un emplacement libre et adapté : aperçu + pose au clic
        if (spot == null) return;

        hovered = spot;
        spot.rend.sharedMaterial = spotHoverMaterial;

        if (selected >= 0 && selected < tools.Length && tools[selected].element != null)
        {
            bool whole = spot.kind == PlacementKind.WholeTile;
            Vector3 f = (flip && !whole) ? -spot.travelDir : spot.travelDir;

            EnsureGhost(PrefabFor(tools[selected], spot));
            if (ghost != null)
            {
                // petit décalage vertical pour la tuile entière : évite le scintillement avec la tuile d'origine
                Vector3 p = spot.anchor + (whole ? Vector3.up * 0.02f : Vector3.zero);
                ghost.transform.SetPositionAndRotation(p, SpotRotation(spot, f));
                ghost.SetActive(true);
            }
            if (Input.GetMouseButtonDown(0)) TryPlace(spot);
        }
    }

    void TryPlace(PlacementSpot spot)
    {
        var tool = tools[selected];
        if (money < tool.cost) { Debug.Log($"Pose refusée : budget insuffisant ({money} < {tool.cost})"); return; }
        if (!CanPlace(tool, spot)) { Debug.Log($"Pose refusée : CanPlace = false (noeud {spot.node}, {spot.kind})"); return; }

        bool whole = spot.kind == PlacementKind.WholeTile;
        Vector3 f = (flip && !whole) ? -spot.travelDir : spot.travelDir;

        GameObject prefab = PrefabFor(tool, spot);
        var go = Instantiate(prefab, spot.anchor, SpotRotation(spot, f), elementsHolder);
        go.name = tool.Label;

        var element = go.GetComponent<RoadElement>();
        if (element == null) element = go.GetComponentInChildren<RoadElement>();
        if (element == null)
        {
            // Variante de tuile (prefabX / prefabT / prefabL) sans script : on lui donne celui de l'outil
            Debug.LogWarning($"Le prefab '{prefab.name}' (tuile {spot.shape}) n'a pas de RoadElement : " +
                             $"ajout automatique de {tool.type?.Name}. Ajoutez-le sur le prefab pour régler ses paramètres.", prefab);
            if (tool.type == null) { Destroy(go); return; }
            element = (RoadElement)go.AddComponent(tool.type);
        }
        // une variante doit se comporter comme l'outil : même type d'emplacement (sinon elle est comptée comme "Edge",
        // ne peut pas être retirée en cliquant, etc.)
        if (element.placement != tool.element.placement)
        {
            Debug.LogWarning($"Le prefab '{prefab.name}' avait Placement = {element.placement} : forcé à {tool.element.placement}.", prefab);
            element.placement = tool.element.placement;
        }

        try
        {
            // une tuile entière agit toujours dans toutes les directions
            element.Place(spot.node, f, allDirections || whole);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Exception dans RoadElement.Place du prefab '{prefab.name}' (noeud {spot.node}) :", prefab);
            Debug.LogException(ex, go);
            Destroy(go);
            return;
        }

        money -= tool.cost;

        // l'emplacement est pris : on le masque tout de suite
        spot.occupant = element;
        placedSpot[element] = spot;
        spot.gameObject.SetActive(false);
        ClearHover();

        // la RoadTile d'origine est désactivée (le graphe routier, lui, n'est pas modifié)
        if (whole) SetTileVisible(spot, false);

        lastPlaceTime = Time.unscaledTime;
    }

    /// <summary>Prefab à poser : pour une tuile entière, la variante X / T / L si elle existe.</summary>
    GameObject PrefabFor(ToolDef tool, PlacementSpot spot) =>
        spot.kind == PlacementKind.WholeTile ? tool.PrefabFor(spot.shape) : tool.prefab;

    /// <summary>Rotation du prefab : celle de la tuile remplacée pour une tuile entière, sinon orientée selon le sens de la route.</summary>
    Quaternion SpotRotation(PlacementSpot spot, Vector3 facing)
    {
        if (spot.kind == PlacementKind.WholeTile) return spot.tileRotation * Quaternion.Euler(0f, spot.yaw, 0f);
        return Quaternion.LookRotation(facing, Vector3.up) * Quaternion.Euler(0f, spot.yaw, 0f);
    }

    /// <summary>Active / désactive la RoadTile d'origine de cet emplacement (retrouvée par sa position si besoin).</summary>
    void SetTileVisible(PlacementSpot spot, bool visible)
    {
        Transform tile = spot.tile;
        if (tile == null) tile = RoadGraph.Instance.FindTileAt(spot.anchor);
        if (tile == null) { Debug.LogWarning($"Aucune RoadTile trouvée à {spot.anchor}"); return; }
        spot.tile = tile;
        tile.gameObject.SetActive(visible);
    }

    void RemoveElement(RoadElement e)
    {
        foreach (var tool in tools) if (tool.type == e.GetType()) { money += tool.cost; break; }

        if (placedSpot.TryGetValue(e, out var spot))
        {
            if (spot != null)
            {
                spot.occupant = null;                   // l'emplacement est de nouveau libre
                if (spot.kind == PlacementKind.WholeTile) SetTileVisible(spot, true);   // la tuile d'origine revient
            }
            placedSpot.Remove(e);
        }
        e.Remove();
    }

    // =================================================================
    //  Interface (Canvas construit dans l'éditeur)
    // =================================================================

    /// <summary>Branche les boutons du Canvas sur la logique du jeu (une seule fois, au démarrage).</summary>
    void SetupUI()
    {
        // Boutons d'outils : texte (nom du prefab + prix) tiré du tableau 'tools', une seule source de vérité
        for (int i = 0; i < tools.Length && toolButtons != null && i < toolButtons.Length; i++)
        {
            if (toolButtons[i] == null) continue;
            var label = toolButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = $"{tools[i].Label} ({tools[i].cost})";
            int idx = i;                                   // copie locale pour la lambda
            Bind(toolButtons[i], () => selected = idx);
        }

        // Boutons de vitesse
        for (int i = 0; i < speeds.Length && speedButtons != null && i < speedButtons.Length; i++)
        {
            if (speedButtons[i] == null) continue;
            var label = speedButtons[i].GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = "x" + speeds[i];
            int idx = i;
            Bind(speedButtons[i], () =>
            {
                speedIndex = idx;
                if (CurrentPhase == Phase.Running) Time.timeScale = speeds[idx];
            });
        }

        Bind(launchButton, StartRun);
        Bind(clearMarkersButton, ClearMarkers);
        Bind(stopButton, EnterPlanning);
        Bind(modifyAfterCrashButton, EnterPlanning);
        Bind(relaunchButton, StartRun);
        Bind(modifyAfterWinButton, EnterPlanning);
    }

    static void Bind(Button b, UnityAction action)
    {
        if (b != null) b.onClick.AddListener(action);
    }

    /// <summary>Met à jour l'affichage : quel panneau est visible, textes, bouton sélectionné.</summary>
    void RefreshUI()
    {
        if (panelPlanning != null) panelPlanning.SetActive(CurrentPhase == Phase.Planning);
        if (panelRunning != null) panelRunning.SetActive(CurrentPhase == Phase.Running);
        if (panelCrashed != null) panelCrashed.SetActive(CurrentPhase == Phase.Crashed);
        if (panelWon != null) panelWon.SetActive(CurrentPhase == Phase.Won);

        if (headerText != null)
            headerText.text = $"Budget : {money}   |   Essai n°{attempt}   |   Record : {bestTime:0}s";

        switch (CurrentPhase)
        {
            case Phase.Planning:
                if (planningHelpText != null)
                    planningHelpText.text = $"R : inverser le sens ({(flip ? "inversé" : "normal")})  |  Tab : tous sens = {(allDirections ? "OUI" : "non")}\nClic gauche sur un objet : le retirer\n(passage piéton : outil sélectionné + clic sur la tuile)";
                if (clearMarkersButton != null)
                    clearMarkersButton.gameObject.SetActive(markers.Count > 0);
                Highlight(toolButtons, selected);
                break;

            case Phase.Running:
                if (runningText != null)
                    runningText.text = $"Temps : {elapsed:0}/{duration:0}s\nInfractions : {Infractions.Total}   Arrêtés : {Infractions.Arrests}";
                Highlight(speedButtons, speedIndex);
                break;

            case Phase.Crashed:
                if (crashedText != null)
                    crashedText.text = $"ACCIDENT : {crashReason}\nTenu {elapsed:0.0}s";
                break;
        }
    }

    /// <summary>Colore le bouton choisi, remet les autres en couleur normale.</summary>
    void Highlight(Button[] buttons, int index)
    {
        if (buttons == null) return;
        for (int i = 0; i < buttons.Length; i++)
            if (buttons[i] != null && buttons[i].image != null)
                buttons[i].image.color = i == index ? selectedColor : normalColor;
    }

    void ClearMarkers()
    {
        foreach (var m in markers) if (m != null) Destroy(m);
        markers.Clear();
    }
}