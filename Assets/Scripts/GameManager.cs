using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Un emplacement de pose (cube semi-transparent) sur le bord d'une route.
/// </summary>
public class PlacementSpot : MonoBehaviour
{
    public int node;
    public Vector3 anchor;      // point au sol où sera posé le prefab
    public Vector3 travelDir;   // sens de circulation dont ce bord est le côté DROIT
    public Renderer rend;
}

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
        public string Label => prefab != null ? prefab.name : "(prefab manquant)";
        [System.NonSerialized] public RoadElement element;
        [System.NonSerialized] public System.Type type;
    }

    [Header("Véhicules")]
    public List<VehicleEntry> vehicles = new List<VehicleEntry>();
    [Tooltip("Prefab de la voiture de police (CarAI ; PoliceCar est ajouté si absent)")]
    public GameObject policePrefab;
    public int carCount = 12;
    public int seed = 1;

    [Header("Outils (un prefab par élément de voirie)")]
    public ToolDef[] tools;

    [Header("Emplacements de pose")]
    public float spotSize = 1f;
    public float spotSpacing = 1.3f;
    [Tooltip("Distance entre l'axe de la route et les cubes de bord")]
    public float spotSideOffset = 3.5f;
    [Tooltip("Aux carrefours/virages : zone centrale sans emplacement")]
    public float junctionClearance = 4f;
    [Tooltip("Optionnel (sinon un matériau transparent est généré)")]
    public Material spotMaterial;
    public Material spotHoverMaterial;
    [Tooltip("Optionnel : remplace les matériaux de l'aperçu (ex : matériau fantôme transparent)")]
    public Material ghostMaterial;

    [Header("Règles")]
    public float duration = 60f;          // secondes à tenir sans accident
    public int startMoney = 1000;

    public Phase CurrentPhase { get; private set; } = Phase.Planning;

    int selected, money, attempt = 1, speedIndex;
    bool flip, allDirections;
    float elapsed, bestTime;
    string crashReason = "";
    Rect uiRect = new Rect(5, 5, 240, 400);

    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<GameObject> markers = new List<GameObject>();
    Transform elementsHolder, spotsHolder;
    GameObject ghost;
    int ghostTool = -1;
    PlacementSpot hovered;
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
    //  Génération des cubes de placement le long des routes
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
            float limit = graph.NodeHalfSize(node) * 0.85f;
            bool straight = dirs.Count == 1 || (dirs.Count == 2 && Vector3.Dot(dirs[0], dirs[1]) < -0.9f);

            if (straight) AddRow(node, c, dirs[0], -limit, limit);
            else foreach (var d in dirs) AddRow(node, c, d, junctionClearance, limit);
        }
    }

    void AddRow(int node, Vector3 center, Vector3 axis, float t0, float t1)
    {
        if (t1 <= t0) return;
        int count = Mathf.Max(1, Mathf.RoundToInt((t1 - t0) / spotSpacing));
        Vector3 rightOfAxis = Vector3.Cross(Vector3.up, axis);

        for (int i = 0; i < count; i++)
        {
            float t = Mathf.Lerp(t0, t1, (i + 0.5f) / count);
            for (int s = -1; s <= 1; s += 2)
            {
                Vector3 side = rightOfAxis * s;                       // bord de route
                Vector3 anchor = center + axis * t + side * spotSideOffset;
                Vector3 travel = Vector3.Cross(side, Vector3.up);     // sens dont ce bord est à droite

                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "Spot";
                go.transform.SetParent(spotsHolder, false);
                go.transform.position = anchor + Vector3.up * 0.15f;
                go.transform.localScale = new Vector3(spotSize, 0.3f, spotSize);

                var spot = go.AddComponent<PlacementSpot>();
                spot.node = node;
                spot.anchor = anchor;
                spot.travelDir = travel;
                spot.rend = go.GetComponent<Renderer>();
                spot.rend.sharedMaterial = spotMaterial;
            }
        }
    }

    void SetSpotsVisible(bool v)
    {
        if (spotsHolder != null && spotsHolder.gameObject.activeSelf != v) spotsHolder.gameObject.SetActive(v);
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
    }

    void ClearHover()
    {
        if (hovered != null && hovered.rend != null) hovered.rend.sharedMaterial = spotMaterial;
        hovered = null;
    }

    PlacementSpot FindSpot(Ray ray)
    {
        PlacementSpot best = null;
        float bestD = float.MaxValue;
        foreach (var h in Physics.RaycastAll(ray, 1000f))
        {
            var s = h.collider.GetComponent<PlacementSpot>();
            if (s != null && h.distance < bestD) { bestD = h.distance; best = s; }
        }
        return best;
    }

    void RefreshGhost()
    {
        if (ghost != null && ghostTool == selected) return;
        if (ghost != null) Destroy(ghost);
        ghost = null;
        ghostTool = selected;

        if (selected < 0 || selected >= tools.Length || tools[selected].prefab == null) return;

        ghost = Instantiate(tools[selected].prefab);
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

    void HandlePlacement()
    {
        var cam = Camera.main;
        if (cam == null) return;

        SetSpotsVisible(true);
        RefreshGhost();
        ClearHover();
        if (ghost != null) ghost.SetActive(false);

        if (Input.GetKeyDown(KeyCode.R)) flip = !flip;
        if (Input.GetKeyDown(KeyCode.Tab)) allDirections = !allDirections;

        Vector2 mp = Input.mousePosition;
        if (uiRect.Contains(new Vector2(mp.x, Screen.height - mp.y))) return;

        var spot = FindSpot(cam.ScreenPointToRay(mp));
        if (spot == null) return;

        hovered = spot;
        spot.rend.sharedMaterial = spotHoverMaterial;

        if (selected >= 0 && selected < tools.Length && tools[selected].element != null)
        {
            Vector3 f = flip ? -spot.travelDir : spot.travelDir;
            Vector3 pos = tools[selected].element.centerOnRoad
                ? RoadGraph.Instance.NodePos(spot.node)
                : spot.anchor;

            if (ghost != null)
            {
                ghost.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(f, Vector3.up));
                ghost.SetActive(true);
            }
            if (Input.GetMouseButtonDown(0)) TryPlace(spot);
        }

        if (Input.GetMouseButtonDown(1)) RemoveNearest(spot);
    }

    void TryPlace(PlacementSpot spot)
    {
        var tool = tools[selected];
        if (tool.element == null || money < tool.cost) return;

        int node = spot.node;
        if (tool.type == typeof(RoadBlock) && RoadGraph.Instance.IsBlocked(node)) return;

        Vector3 f = flip ? -spot.travelDir : spot.travelDir;
        Vector3 pos = tool.element.centerOnRoad ? RoadGraph.Instance.NodePos(node) : spot.anchor;

        // remplace un élément du même type déjà présent sur ce côté
        foreach (var e in new List<RoadElement>(RoadElement.At(node)))
            if (e.GetType() == tool.type && (e.AllDirections || allDirections || Vector3.Dot(e.Facing, f) > 0.99f))
                RemoveElement(e);

        var go = Instantiate(tool.prefab, pos, Quaternion.LookRotation(f, Vector3.up), elementsHolder);
        go.name = tool.Label;
        var element = go.GetComponent<RoadElement>();
        element.Place(node, f, allDirections);
        money -= tool.cost;
    }

    /// <summary>Clic droit : retire l'élément de la tuile le plus proche de l'emplacement visé.</summary>
    void RemoveNearest(PlacementSpot spot)
    {
        RoadElement best = null;
        float bestD = float.MaxValue;
        foreach (var e in RoadElement.At(spot.node))
        {
            float d = (e.transform.position - spot.anchor).sqrMagnitude;
            if (d < bestD) { bestD = d; best = e; }
        }
        if (best != null) RemoveElement(best);
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
                    if (GUILayout.Toggle(selected == i, $"{tools[i].Label} ({tools[i].cost})", GUI.skin.button)) selected = i;
                GUILayout.Label($"R : inverser le sens ({(flip ? "inversé" : "normal")})  |  Tab : tous sens = {(allDirections ? "OUI" : "non")}\nClic droit : retirer");
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