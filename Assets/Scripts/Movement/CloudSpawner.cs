using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fait apparaître des nuages (prefabs) à Z min, les fait avancer lentement vers Z max, puis les recycle.
///  - tous à la même hauteur (height), X aléatoire dans [xMin, xMax] ;
///  - rotation Y aléatoire entre yawMin et yawMax, échelle aléatoire entre scaleMin et scaleMax ;
///  - vitesse quasi identique pour tous (speedVariation) ;
///  - densité = nombre de nuages pour 1000 m² de la zone (le nombre de nuages simultanés reste stable) ;
///  - temps non mis à l'échelle par défaut : les nuages avancent à vitesse constante, même en pause ou en x4.
/// Les nuages ne sont pas des RoadElement : ils sont ignorés par la simulation et le rembobinage.
/// </summary>
public class CloudSpawner : MonoBehaviour
{
    [Header("Prefabs")]
    [Tooltip("Un prefab est tiré au hasard à chaque apparition")]
    public GameObject[] cloudPrefabs;

    [Header("Zone (monde)")]
    [Tooltip("Hauteur (Y) commune à tous les nuages")]
    public float height = 40f;
    public float xMin = -80f;
    public float xMax = 80f;
    [Tooltip("Les nuages apparaissent ici... (placez-le hors du champ de la caméra)")]
    public float zMin = -100f;
    [Tooltip("... avancent vers ici, puis disparaissent")]
    public float zMax = 100f;

    [Header("Mouvement")]
    [Tooltip("Vitesse (m/s) le long de Z")]
    public float speed = 1.5f;
    [Range(0f, 0.3f), Tooltip("Écart de vitesse entre nuages (0.05 = ±5 %)")]
    public float speedVariation = 0.05f;
    [Tooltip("Coché : insensible à Time.timeScale (pause, x2, x4)")]
    public bool useUnscaledTime = true;

    [Header("Densité")]
    [Min(0f), Tooltip("Nombre de nuages pour 1000 m² de la zone (largeur X × longueur Z)")]
    public float density = 1f;
    [Min(0), Tooltip("Plafond de sécurité du nombre de nuages simultanés")]
    public int maxClouds = 150;
    [Tooltip("Coché : le ciel est déjà rempli au lancement (sinon il se remplit depuis Z min)")]
    public bool prewarm = true;

    [Header("Apparence")]
    [Range(0f, 360f)] public float yawMin = 0f;
    [Range(0f, 360f)] public float yawMax = 360f;
    [Min(0.01f)] public float scaleMin = 0.8f;
    [Min(0.01f)] public float scaleMax = 1.5f;

    class Cloud
    {
        public Transform tr;
        public GameObject prefab;
        public float speed;
    }

    readonly List<Cloud> active = new List<Cloud>();
    readonly Dictionary<GameObject, Stack<Transform>> pool = new Dictionary<GameObject, Stack<Transform>>();
    Transform holder;
    float spawnAccumulator;

    float Length => Mathf.Abs(zMax - zMin);
    float Dir => zMax >= zMin ? 1f : -1f;

    /// <summary>Nombre de nuages simultanés visé, selon la densité et la taille de la zone.</summary>
    public int TargetCount
    {
        get
        {
            float area = Mathf.Abs(xMax - xMin) * Length;
            return Mathf.Min(maxClouds, Mathf.RoundToInt(density * area / 1000f));
        }
    }

    void Start()
    {
        holder = new GameObject("Clouds").transform;
        holder.SetParent(transform, false);

        if (prewarm)
        {
            int n = TargetCount;
            for (int i = 0; i < n; i++) Spawn(Random.value);
        }
    }

    void Update()
    {
        if (!HasPrefabs() || Length < 0.01f) return;

        float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        dt = Mathf.Min(dt, 0.1f);   // évite un gros saut après un freeze

        // ---- Apparitions : débit = nombre visé × vitesse / longueur de la zone (nuages par seconde) ----
        if (active.Count < maxClouds)
        {
            float rate = TargetCount * Mathf.Max(0.01f, speed) / Length;
            spawnAccumulator += rate * dt;
            while (spawnAccumulator >= 1f)
            {
                spawnAccumulator -= 1f;
                if (active.Count < maxClouds) Spawn(0f);
            }
        }
        else spawnAccumulator = 0f;

        // ---- Déplacement et disparition ----
        float dir = Dir;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var c = active[i];
            Vector3 p = c.tr.position;
            p.z += dir * c.speed * dt;

            if ((p.z - zMax) * dir >= 0f)
            {
                Despawn(c);
                active.RemoveAt(i);
                continue;
            }
            c.tr.position = p;
        }
    }

    bool HasPrefabs()
    {
        if (cloudPrefabs == null) return false;
        for (int i = 0; i < cloudPrefabs.Length; i++) if (cloudPrefabs[i] != null) return true;
        return false;
    }

    GameObject PickPrefab()
    {
        for (int tries = 0; tries < 10; tries++)
        {
            var p = cloudPrefabs[Random.Range(0, cloudPrefabs.Length)];
            if (p != null) return p;
        }
        return null;
    }

    /// <summary>Crée (ou réutilise) un nuage. progress = 0 : à Z min ; 1 : à Z max.</summary>
    void Spawn(float progress)
    {
        if (!HasPrefabs()) return;
        var prefab = PickPrefab();
        if (prefab == null) return;

        Transform tr = null;
        if (pool.TryGetValue(prefab, out var stack))
            while (stack.Count > 0 && tr == null) tr = stack.Pop();

        if (tr == null)
        {
            tr = Instantiate(prefab, holder).transform;
            tr.name = prefab.name;
        }

        float z = Mathf.Lerp(zMin, zMax, progress);
        float x = Random.Range(Mathf.Min(xMin, xMax), Mathf.Max(xMin, xMax));
        float yaw = Random.Range(Mathf.Min(yawMin, yawMax), Mathf.Max(yawMin, yawMax));
        float s = Random.Range(Mathf.Min(scaleMin, scaleMax), Mathf.Max(scaleMin, scaleMax));

        tr.SetPositionAndRotation(new Vector3(x, height, z), Quaternion.Euler(0f, yaw, 0f));
        tr.localScale = prefab.transform.localScale * s;
        tr.gameObject.SetActive(true);

        active.Add(new Cloud
        {
            tr = tr,
            prefab = prefab,
            speed = speed * (1f + Random.Range(-speedVariation, speedVariation))
        });
    }

    void Despawn(Cloud c)
    {
        if (c.tr == null) return;
        c.tr.gameObject.SetActive(false);
        if (!pool.TryGetValue(c.prefab, out var stack)) pool[c.prefab] = stack = new Stack<Transform>();
        stack.Push(c.tr);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
        Vector3 a = new Vector3(xMin, height, zMin), b = new Vector3(xMax, height, zMin);
        Vector3 c = new Vector3(xMax, height, zMax), d = new Vector3(xMin, height, zMax);
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);

        // ligne d'apparition (verte) et de disparition (rouge)
        Gizmos.color = Color.green; Gizmos.DrawLine(a, b);
        Gizmos.color = Color.red; Gizmos.DrawLine(d, c);
    }
}