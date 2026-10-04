using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Cinématique d'intro :
/// le piéton sort de chez lui et marche tout droit (HappyWalk = true),
/// la caméra recule à la même vitesse devant lui, un camion arrive de la droite (-X)
/// et l'écrase. Le piéton est propulsé par son Rigidbody, puis le menu "Jouer" s'affiche.
/// </summary>
public class IntroCinematic : MonoBehaviour
{
    [Header("Piéton")]
    [SerializeField] private Rigidbody pedestrian;
    [SerializeField] private Animator pedestrianAnimator;
    [SerializeField] private string walkBoolName = "HappyWalk";
    [SerializeField] private float walkSpeed = 1.5f;
    [Tooltip("Direction de marche. Caméra face au piéton regardant vers +Z : le piéton marche vers -Z, donc vers la caméra.")]
    [SerializeField] private Vector3 walkDirection = Vector3.back; // (0,0,-1)

    [Header("Caméra")]
    [Tooltip("Si vide, utilise Camera.main. La caméra ne bouge QUE sur Z : même vitesse que le piéton, X et Y restent fixes.")]
    [SerializeField] private Transform cam;
    [Tooltip("Distance (m) que la caméra continue de reculer sur Z après l'accident, en ralentissant jusqu'à l'arrêt")]
    [SerializeField] private float cameraExtraDistance = 3f;

    [Header("Camion")]
    [Tooltip("Rigidbody (sera mis en Kinematic) + Collider. Placez-le à droite de la route, hors champ.")]
    [SerializeField] private Rigidbody truck;
    [SerializeField] private float truckSpeed = 12f;
    [Tooltip("Distance (m) que le camion continue de rouler après l'impact, en ralentissant jusqu'à l'arrêt")]
    [SerializeField] private float truckExtraDistance = 25f;
    [Tooltip("Point sur la route où le piéton doit être percuté (un Empty suffit)")]
    [SerializeField] private Transform impactPoint;

    [Header("Accident")]
    [Tooltip("Instancié au point d'impact, puis attaché au camion")]
    [SerializeField] private GameObject accidentPrefab;
    [SerializeField] private float accidentLifetime = 5f;
    [SerializeField] private AudioClip accidentSound;
    [Range(0f, 1f)] [SerializeField] private float accidentVolume = 1f;
    [Tooltip("Vitesse de projection du piéton (m/s)")]
    [SerializeField] private float launchSpeed = 14f;
    [Tooltip("Part verticale de la projection (0 = horizontal)")]
    [SerializeField] private float launchUpRatio = 0.8f;

    [Header("Musique de fond")]
    [Tooltip("Clip audio de la musique : le script crée lui-même l'AudioSource (en boucle)")]
    [SerializeField] private AudioClip musicClip;
    [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.5f;

    private AudioSource musicSource;

    [Header("Timing")]
    [SerializeField] private float startDelay = 0.5f;
    [Tooltip("Délai après l'impact avant d'afficher le menu (laissez le temps à la caméra et au camion de s'arrêter)")]
    [SerializeField] private float delayBeforeMenu = 4.5f;
    [Tooltip("Sécurité : si l'impact n'a pas lieu après ce délai, on passe au menu")]
    [SerializeField] private float maxWaitForImpact = 20f;

    [Header("UI")]
    [SerializeField] private GameObject menuPanel;   // Panel/Canvas contenant le bouton, désactivé au départ
    [SerializeField] private Button playButton;
    [Tooltip("Bouton visible pendant la cinématique : affiche directement le panneau Jouer. Ne le mettez PAS dans le menuPanel.")]
    [SerializeField] private Button skipButton;
    [SerializeField] private string gameSceneName = "GameScene";

    private Collider pedestrianCol;
    private Collider truckCol;
    private Vector3 dir;
    private float cameraZOffset;
    private float cameraZSign;

    private bool walking;
    private bool followCamera;
    private bool cameraCoasting;
    private bool truckMoving;
    private bool impactDone;

    private float truckCurrentSpeed;
    private float truckBrake;
    private float cameraCurrentSpeed;
    private float cameraBrake;

    private void Awake()
    {
        if (cam == null && Camera.main != null) cam = Camera.main.transform;

        dir = walkDirection.normalized;

        pedestrianCol = pedestrian.GetComponentInChildren<Collider>();
        truckCol = truck.GetComponentInChildren<Collider>();

        // Piéton : Rigidbody classique, rotation bloquée pendant la marche
        pedestrian.interpolation = RigidbodyInterpolation.Interpolate;
        pedestrian.constraints = RigidbodyConstraints.FreezeRotation;
        pedestrian.transform.rotation = Quaternion.LookRotation(dir);

        // Camion : déplacé par script, mais pousse quand même le piéton physiquement
        truck.isKinematic = true;
        truck.interpolation = RigidbodyInterpolation.Interpolate;

        // Source audio de la musique, créée automatiquement à partir du clip
        if (musicClip != null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.clip = musicClip;
            musicSource.volume = musicVolume;
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f; // son 2D, indépendant de la position de la caméra
        }

        if (menuPanel != null) menuPanel.SetActive(false);
        if (playButton != null) playButton.onClick.AddListener(PlayGame);
        if (skipButton != null) skipButton.onClick.AddListener(SkipCinematic);
    }

    private void Start()
    {
        // Seul l'écart sur Z est mémorisé : X et Y de la caméra ne changeront jamais
        cameraZOffset = cam.position.z - pedestrian.transform.position.z;
        cameraZSign = dir.z >= 0f ? 1f : -1f;
        StartCoroutine(CinematicSequence());
    }

    private IEnumerator CinematicSequence()
    {
        // Musique de fond
        if (musicSource != null) musicSource.Play();

        yield return new WaitForSeconds(startDelay);

        // Synchronisation : le camion démarre au bon moment pour arriver en même temps que le piéton
        float distToImpact = Mathf.Max(0f, Vector3.Dot(impactPoint.position - pedestrian.position, dir));
        float pedestrianTime = distToImpact / walkSpeed;

        float truckTravel = Mathf.Max(0f, truckCol.bounds.min.x - impactPoint.position.x);
        float truckTime = truckTravel / truckSpeed;

        float truckDelay = pedestrianTime - truckTime;
        if (truckDelay < 0f)
        {
            Debug.LogWarning("IntroCinematic : le camion est trop proche ou le piéton trop loin. " +
                             "Éloignez le camion, rapprochez l'impactPoint ou réduisez truckSpeed.");
            truckDelay = 0f;
        }

        // Le piéton marche, la caméra recule
        walking = true;
        followCamera = true;
        pedestrianAnimator.SetBool(walkBoolName, true);

        yield return new WaitForSeconds(truckDelay);

        // Le camion arrive de la droite (-X)
        truckCurrentSpeed = truckSpeed;
        truckMoving = true;

        // Attente de l'impact
        float waited = 0f;
        while (!truckCol.bounds.Intersects(pedestrianCol.bounds) && waited < maxWaitForImpact)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        if (waited < maxWaitForImpact) DoImpact();

        yield return new WaitForSeconds(delayBeforeMenu);
        ShowMenu();
    }

    private void DoImpact()
    {
        impactDone = true;
        walking = false;

        // La caméra n'est plus liée au piéton : elle continue de reculer sur Z puis s'arrête.
        // Décélération constante calculée pour s'arrêter exactement après la distance voulue (a = v² / 2d).
        followCamera = false;
        cameraCoasting = true;
        cameraCurrentSpeed = walkSpeed;
        cameraBrake = (walkSpeed * walkSpeed) / (2f * Mathf.Max(0.01f, cameraExtraDistance));

        // Idem pour le camion : il roule encore truckExtraDistance mètres avant de s'arrêter
        truckBrake = (truckCurrentSpeed * truckCurrentSpeed) / (2f * Mathf.Max(0.01f, truckExtraDistance));

        pedestrianAnimator.SetBool(walkBoolName, false);

        Vector3 hitPos = pedestrianCol.bounds.center;

        // Effet d'accident
        if (accidentPrefab != null)
        {
            // Le préfab apparaît au point d'impact mais devient enfant du camion : il le suit dans son déplacement
            GameObject fx = Instantiate(accidentPrefab, hitPos, Quaternion.identity, truck.transform);
            Destroy(fx, accidentLifetime);
        }

        // Son d'accident
        if (accidentSound != null)
            AudioSource.PlayClipAtPoint(accidentSound, hitPos, accidentVolume);

        // Projection du piéton par le Rigidbody
        pedestrian.constraints = RigidbodyConstraints.None;
        Vector3 launchDir = (Vector3.left + Vector3.up * launchUpRatio).normalized; // sens du camion (-X) + vers le haut
        pedestrian.AddForce(launchDir * launchSpeed, ForceMode.VelocityChange);
        pedestrian.AddTorque(Random.insideUnitSphere * 6f, ForceMode.VelocityChange);
    }

    private void FixedUpdate()
    {
        // Marche du piéton
        if (walking)
        {
            Vector3 next = pedestrian.position + dir * walkSpeed * Time.fixedDeltaTime;
            pedestrian.MovePosition(next);
        }

        // Camion qui roule vers -X (et freine après l'impact)
        if (truckMoving)
        {
            if (impactDone)
                truckCurrentSpeed = Mathf.MoveTowards(truckCurrentSpeed, 0f, truckBrake * Time.fixedDeltaTime);

            truck.MovePosition(truck.position + Vector3.left * truckCurrentSpeed * Time.fixedDeltaTime);
        }
    }

    private void LateUpdate()
    {
        Vector3 p = cam.position;

        if (followCamera)
        {
            // Même avancée que le piéton sur Z uniquement : si le piéton descend un escalier,
            // la caméra garde son X et son Y.
            p.z = pedestrian.transform.position.z + cameraZOffset;
            cam.position = p;
        }
        else if (cameraCoasting)
        {
            // Après l'accident : la caméra continue de reculer sur Z en ralentissant
            cameraCurrentSpeed = Mathf.MoveTowards(cameraCurrentSpeed, 0f, cameraBrake * Time.deltaTime);
            p.z += cameraZSign * cameraCurrentSpeed * Time.deltaTime;
            cam.position = p;

            if (cameraCurrentSpeed <= 0f) cameraCoasting = false;
        }
    }

    // Appelée par le bouton "Passer" : affiche directement le panneau Jouer,
    // la cinématique continue de se jouer derrière.
    public void SkipCinematic()
    {
        ShowMenu();
    }

    private void ShowMenu()
    {
        if (skipButton != null) skipButton.gameObject.SetActive(false);

        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        if (menuPanel != null) menuPanel.SetActive(true);
    }

    // Appelée par le bouton "Jouer"
    public void PlayGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }
}