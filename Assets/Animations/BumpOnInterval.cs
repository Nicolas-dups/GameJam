using UnityEngine;

/// <summary>
/// Petit "bump" cartoon (squash & stretch avec rebond) sur l'échelle verticale,
/// déclenché à intervalles réguliers. À mettre sur l'enfant "Visual" (celui qui porte le renderer).
/// Purement visuel : n'utilise pas le rng de CarAI, donc la simulation reste déterministe.
/// </summary>
public class BumpOnInterval : MonoBehaviour
{
    [Header("Intervalle")]
    public float interval = 1.2f;
    [Range(0f, 1f), Tooltip("Variation aléatoire de l'intervalle (0.3 = ±30 %)")]
    public float intervalJitter = 0.3f;
    [Tooltip("Décale le premier bump au hasard pour que les voitures ne soient pas synchronisées")]
    public bool randomStartDelay = true;

    [Header("Ressort")]
    [Tooltip("Intensité de l'étirement vertical (0.2 = +20 %)")]
    public float amplitude = 0.2f;
    [Tooltip("Vitesse d'oscillation (rad/s). Plus haut = rebonds plus rapides")]
    public float frequency = 22f;
    [Tooltip("Amortissement. Plus haut = le ressort s'arrête plus vite")]
    public float damping = 7f;
    [Tooltip("Compense X/Z pour garder le volume (effet squash & stretch)")]
    public bool preserveVolume = true;
    [Tooltip("Garde le bas du mesh au sol (suppose un pivot au centre)")]
    public bool keepGrounded = true;

    [Header("Optionnel : lié à la voiture")]
    [Tooltip("Si renseigné, le bump ne se produit que quand la voiture roule")]
    public CarAI car;
    public float minSpeed = 0.3f;

    Vector3 baseScale, basePos;
    float minY;          // bas du mesh en espace local
    float timer;         // temps avant le prochain bump
    float t = -1f;       // temps depuis le début du bump (-1 = inactif)

    void Awake()
    {
        if (car == null) car = GetComponentInParent<CarAI>();
    }

    void OnEnable()
    {
        baseScale = transform.localScale;
        basePos = transform.localPosition;

        var r = GetComponent<Renderer>();
        minY = r != null ? r.localBounds.min.y : 0f;

        timer = randomStartDelay ? Random.Range(0f, interval) : interval;
        t = -1f;
    }

    void OnDisable()
    {
        transform.localScale = baseScale;
        transform.localPosition = basePos;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (t < 0f)
        {
            if (car != null && car.Speed < minSpeed) return;   // à l'arrêt : on attend

            timer -= dt;
            if (timer <= 0f)
            {
                t = 0f;
                timer = interval * (1f + Random.Range(-intervalJitter, intervalJitter));
            }
            return;
        }

        t += dt;

        // oscillation amortie : part de 0, s'étire, rebondit, se stabilise
        float env = Mathf.Exp(-damping * t);
        float k = 1f + amplitude * env * Mathf.Sin(frequency * t);

        Vector3 s = baseScale;
        s.y = baseScale.y * k;
        if (preserveVolume)
        {
            float xz = 1f / Mathf.Sqrt(k);
            s.x = baseScale.x * xz;
            s.z = baseScale.z * xz;
        }
        transform.localScale = s;

        if (keepGrounded)
            transform.localPosition = basePos + Vector3.up * (minY * baseScale.y * (1f - k));

        // fin du bump quand l'amplitude est négligeable
        if (amplitude * env < 0.001f)
        {
            t = -1f;
            transform.localScale = baseScale;
            transform.localPosition = basePos;
        }
    }
}
