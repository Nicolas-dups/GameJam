using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boucle de jeu :  PLANIFICATION (poser des éléments) -> SIMULATION -> ACCIDENT -> retour à la planification
/// (les voitures repartent exactement du même point de départ, les éléments posés restent).
/// Si la ville tient `duration` secondes sans accident : victoire.
/// Nécessite l'ancien Input Manager (Input.GetMouseButton...).
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum Phase { Planning, Running, Crashed, Won }

    class Tool
    {
        public string label; public System.Type type; public int cost;
        public Tool(string l, System.Type t, int c) { label = l; type = t; cost = c; }
    }

    [Header("Scène")]
    [Tooltip("Prefab de voiture avec CarAI (sinon un cube est généré)")]
    public GameObject carPrefab;
    public float spawnHeight = 0.5f;
    public int carCount = 12;
    public int seed = 1;

    [Header("Règles")]
    public float duration = 60f;          // secondes à tenir sans accident
    public int startMoney = 1000;
    [Tooltip("Distance entre deux centres de voitures pour compter un accident")]
    public float accidentDistance = 2.2f;

    readonly Tool[] tools =
    {
        new Tool("Stop",            typeof(StopSign),           50),
        new Tool("Feu",             typeof(TrafficLight),      150),
        new Tool("Passage piéton",  typeof(PedestrianCrossing), 60),
        new Tool("Route barrée",    typeof(RoadBlock),          40),
        new Tool("Sens unique",     typeof(OneWaySign),         40),
        new Tool("Limitation",      typeof(SpeedLimitSign),     40),
        new Tool("Céder le passage",typeof(YieldSign),          40),
        new Tool("Dos d'âne",       typeof(SpeedBump),          30),
        new Tool("Agent routier",   typeof(TrafficCop),        200),
        new Tool("Police",          typeof(PoliceStation),     250),
    };

    public Phase CurrentPhase { get; private set; } = Phase.Planning;

    int selected, rotation, money, attempt = 1, speedIndex;
    bool allDirections;
    float elapsed, bestTime;
    string crashReason = "";
    Rect uiRect = new Rect(5, 5, 240, 400);

    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<GameObject> markers = new List<GameObject>();
    Transform elementsHolder;
    GameObject hover, hoverArrow;
    static readonly float[] speeds = { 1f, 2f, 4f };

    void Awake()
    {
        Instance = this;
        Time.fixedDeltaTime = 1f / 120f;   // simulation fine et déterministe
        Time.timeScale = 0f;
        money = startMoney;
        elementsHolder = new GameObject("RoadElements").transform;
    }

    void Start()
    {
        hover = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(hover.GetComponent<Collider>());
        hover.GetComponent<Renderer>().material.color = new Color(1f, 0.9f, 0.2f, 1f);
        hoverArrow = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(hoverArrow.GetComponent<Collider>());
        hoverArrow.GetComponent<Renderer>().material.color = Color.red;
        hover.SetActive(false);
        hoverArrow.SetActive(false);
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

        elapsed = 0f;
        CurrentPhase = Phase.Running;
        Time.timeScale = speeds[speedIndex];
    }

    public CarAI SpawnCar(int node, int carSeed, bool police)
    {
        var graph = RoadGraph.Instance;
        Vector3 p = graph.NodePos(node) + Vector3.up * spawnHeight;

        GameObject go;
        if (carPrefab != null) go = Instantiate(carPrefab, p, Quaternion.identity);
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.position = p;
            go.transform.localScale = new Vector3(1.8f, 1.1f, 4f);
        }
        go.name = police ? "PoliceCar" : "Car" + carSeed;

        var car = go.GetComponent<CarAI>();
        if (car == null) car = go.AddComponent<CarAI>();
        car.wanderRandomly = true;
        car.isPolice = police;

        var rend = go.GetComponentsInChildren<Renderer>();
        Color c = police ? new Color(0.1f, 0.3f, 1f) : Color.HSVToRGB((carSeed % 1000) / 1000f, 0.6f, 0.9f);
        if (carPrefab == null || police) foreach (var r in rend) r.material.color = c;

        if (police) go.AddComponent<PoliceCar>();
        car.Begin(carSeed);
        spawned.Add(go);
        return car;
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
        float sq = accidentDistance * accidentDistance;

        for (int i = 0; i < cars.Count; i++)
            for (int j = i + 1; j < cars.Count; j++)
            {
                Vector3 d = cars[i].transform.position - cars[j].transform.position;
                d.y = 0f;
                if (d.sqrMagnitude < sq)
                {
                    Crash((cars[i].transform.position + cars[j].transform.position) * 0.5f, "Collision entre deux voitures");
                    return;
                }
            }

        foreach (var ped in Pedestrian.All)
            foreach (var car in cars)
            {
                Vector3 l = car.transform.InverseTransformPoint(ped.transform.position);
                if (Mathf.Abs(l.z) < 2.3f && Mathf.Abs(l.x) < 1.3f)
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
        else if (hover != null) { hover.SetActive(false); hoverArrow.SetActive(false); }
    }

    void HandlePlacement()
    {
        var cam = Camera.main;
        if (cam == null) return;

        if (Input.GetKeyDown(KeyCode.R)) rotation = (rotation + 1) % 4;
        if (Input.GetKeyDown(KeyCode.Tab)) allDirections = !allDirections;

        Vector2 mp = Input.mousePosition;
        hover.SetActive(false);
        hoverArrow.SetActive(false);
        if (uiRect.Contains(new Vector2(mp.x, Screen.height - mp.y))) return;

        var graph = RoadGraph.Instance;
        var plane = new Plane(Vector3.up, new Vector3(0f, graph.NodePos(0).y, 0f));
        Ray ray = cam.ScreenPointToRay(mp);
        if (!plane.Raycast(ray, out float t)) return;

        Vector3 hit = ray.GetPoint(t);
        int node = graph.WorldToNode(hit);
        Vector3 c = graph.NodePos(node);
        float half = graph.NodeHalfSize(node);
        if (Mathf.Abs(hit.x - c.x) > half || Mathf.Abs(hit.z - c.z) > half) return;   // pas sur une tuile

        // aperçu
        hover.SetActive(true);
        hover.transform.position = c + Vector3.up * 0.1f;
        hover.transform.rotation = Quaternion.identity;
        hover.transform.localScale = new Vector3(half * 2f, 0.05f, half * 2f);
        Vector3 f = Facing();
        hoverArrow.transform.position = c + f * half * 0.6f + Vector3.up * 0.2f;
        hoverArrow.transform.localScale = new Vector3(0.4f, 0.4f, 0.4f);
        hoverArrow.SetActive(!allDirections);

        if (Input.GetMouseButtonDown(0)) TryPlace(node);
        if (Input.GetMouseButtonDown(1)) RemoveTop(node);
    }

    Vector3 Facing() => Quaternion.Euler(0f, rotation * 90f, 0f) * Vector3.forward;

    void TryPlace(int node)
    {
        var tool = tools[selected];
        if (money < tool.cost) return;
        if (tool.type == typeof(RoadBlock) && RoadGraph.Instance.IsBlocked(node)) return;

        Vector3 f = Facing();
        // remplace un élément du même type déjà présent sur ce côté
        foreach (var e in new List<RoadElement>(RoadElement.At(node)))
            if (e.GetType() == tool.type && (e.AllDirections || allDirections || Vector3.Dot(e.Facing, f) > 0.99f))
                RemoveElement(e);

        var go = new GameObject(tool.label);
        go.transform.SetParent(elementsHolder);
        go.transform.position = RoadGraph.Instance.NodePos(node);
        go.transform.rotation = Quaternion.LookRotation(f, Vector3.up);
        var element = (RoadElement)go.AddComponent(tool.type);
        element.Place(node, f, allDirections);
        money -= tool.cost;
    }

    void RemoveTop(int node)
    {
        var list = RoadElement.At(node);
        if (list.Count > 0) RemoveElement(list[list.Count - 1]);
    }

    void RemoveElement(RoadElement e)
    {
        foreach (var tool in tools) if (tool.type == e.GetType()) { money += tool.cost; break; }
        e.Remove();
    }

    // =================================================================
    //  Interface (IMGUI : rapide à mettre en place, à remplacer par un vrai Canvas plus tard)
    // =================================================================
    void OnGUI()
    {
        uiRect = new Rect(5, 5, 240, CurrentPhase == Phase.Planning ? 150 + tools.Length * 30 : 150);
        GUILayout.BeginArea(uiRect, GUI.skin.box);

        GUILayout.Label($"Budget : {money}   |   Essai n°{attempt}   |   Record : {bestTime:0}s");

        switch (CurrentPhase)
        {
            case Phase.Planning:
                for (int i = 0; i < tools.Length; i++)
                    if (GUILayout.Toggle(selected == i, $"{tools[i].label} ({tools[i].cost})", GUI.skin.button)) selected = i;
                GUILayout.Label($"R : tourner  |  Tab : tous sens = {(allDirections ? "OUI" : "non")}\nClic droit : retirer");
                if (GUILayout.Button("LANCER")) StartRun();
                if (markers.Count > 0 && GUILayout.Button("Effacer les repères")) ClearMarkers();
                break;

            case Phase.Running:
                GUILayout.Label($"Temps : {elapsed:0}/{duration:0}s   Infractions : {Infractions.Total}   Arrêtés : {Infractions.Arrests}");
                GUILayout.BeginHorizontal();
                for (int i = 0; i < speeds.Length; i++)
                    if (GUILayout.Toggle(speedIndex == i, "x" + speeds[i], GUI.skin.button)) { speedIndex = i; Time.timeScale = speeds[i]; }
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Arrêter")) EnterPlanning();
                break;

            case Phase.Crashed:
                GUILayout.Label($"ACCIDENT : {crashReason}\nTenu {elapsed:0.0}s");
                if (GUILayout.Button("Modifier la ville")) EnterPlanning();
                if (GUILayout.Button("Relancer tel quel")) StartRun();
                break;

            case Phase.Won:
                GUILayout.Label("BRAVO ! Aucun accident.");
                if (GUILayout.Button("Modifier la ville")) EnterPlanning();
                break;
        }
        GUILayout.EndArea();
    }

    void ClearMarkers()
    {
        foreach (var m in markers) if (m != null) Destroy(m);
        markers.Clear();
    }
}
