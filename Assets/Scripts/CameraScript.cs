using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

/// <summary>
/// Caméra type RTS / vue du dessus :
/// - Clic droit maintenu + glisser (bouton réglable via dragButton) : déplacement sur le plan XZ (le sol "suit" la souris)
/// - Molette : zoom / dézoom VERS la position de la souris (le point sous le curseur reste sous le curseur)
/// - FocusOn(point) : déplacement fluide vers un point (appelé par le GameManager au moment d'un accident)
/// - LockInput(secondes) : ignore les inputs du joueur pendant un laps de temps (séquence d'accident)
/// - Limites min / max sur X, Y et Z
/// Fonctionne avec Time.timeScale = 0 (tout est en temps non mis à l'échelle).
/// À attacher sur l'objet Camera (caméra en perspective).
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    public static CameraController Instance { get; private set; }

    [Header("Déplacement (glisser)")]
    [Tooltip("0 = clic gauche, 1 = clic droit, 2 = clic molette")]
    [SerializeField] private int dragButton = 1;
    [Tooltip("Hauteur (Y) du plan de sol utilisé pour calculer le glissement")]
    [SerializeField] private float groundHeight = 0f;
    [Tooltip("Vitesse de lissage du déplacement à la souris")]
    [SerializeField] private float movementSmoothing = 12f;
    [Tooltip("Ne pas interagir quand la souris est sur un élément d'interface INTERACTIF (bouton, slider...). " +
             "Un panneau de fond ne bloque plus la caméra.")]
    [SerializeField] private bool ignoreWhenOverUI = true;

    [Header("Zoom (molette)")]
    [Tooltip("Distance parcourue vers le curseur par cran de molette")]
    [SerializeField] private float zoomSpeed = 2f;
    [Tooltip("Plus la valeur est grande, plus le zoom est réactif")]
    [SerializeField] private float zoomSmoothing = 10f;

    [Header("Focus sur un accident")]
    [Tooltip("Distance caméra -> point d'accident (limitée par les hauteurs min/max)")]
    [SerializeField] private float crashZoomDistance = 15f;
    [Tooltip("Plus la valeur est grande, plus le déplacement vers l'accident est rapide")]
    [SerializeField] private float focusSmoothing = 4f;

    [Header("Limites de la caméra")]
    [SerializeField] private Vector3 minPosition = new Vector3(-50f, 5f, -50f);
    [SerializeField] private Vector3 maxPosition = new Vector3(50f, 40f, 50f);

    private Camera cam;
    private Plane groundPlane;
    private Vector3 dragOrigin;
    private Vector3 dragStartPosition;
    private bool isDragging;

    private float pendingZoom;        // distance de zoom restant à appliquer (lissage)
    private Vector3 zoomDir = Vector3.forward;   // direction (vers le curseur) du zoom en cours

    private bool focusing;
    private Vector3 focusTarget;

    private float lockUntil;          // temps non mis à l'échelle jusqu'auquel les inputs sont ignorés

    /// <summary>Vrai tant que les inputs du joueur sont ignorés.</summary>
    public bool InputLocked { get; private set; }

    private void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        groundPlane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
        zoomDir = transform.forward;
    }

    private void Update()
    {
        if (InputLocked && Time.unscaledTime >= lockUntil) InputLocked = false;

        if (InputLocked)
        {
            // Pendant le verrouillage : seul le focus automatique agit
            if (focusing) UpdateFocus();
            ClampPosition();
            return;
        }

        // Toute action du joueur interrompt un focus automatique
        if (focusing && (Input.GetMouseButtonDown(dragButton) || Mathf.Abs(Input.mouseScrollDelta.y) > 0.01f))
            focusing = false;

        if (focusing) UpdateFocus();
        else
        {
            HandleDrag();
            HandleZoom();
        }
        ClampPosition();
    }

    // =================================================================
    //  Verrouillage des inputs
    // =================================================================
    /// <summary>Ignore le glisser et la molette pendant `seconds` secondes (temps réel).</summary>
    public void LockInput(float seconds)
    {
        InputLocked = seconds > 0f;
        lockUntil = Time.unscaledTime + seconds;
        isDragging = false;
        pendingZoom = 0f;
    }

    public void UnlockInput()
    {
        InputLocked = false;
    }

    // =================================================================
    //  Focus (accident)
    // =================================================================
    /// <summary>Déplace la caméra en douceur pour centrer `point` à l'écran, à `distance` mètres (par défaut crashZoomDistance).</summary>
    public void FocusOn(Vector3 point, float distance = -1f)
    {
        if (distance <= 0f) distance = crashZoomDistance;
        point.y = groundHeight;
        Vector3 f = transform.forward;

        // limite la distance pour que la hauteur finale reste dans [minY, maxY]
        if (Mathf.Abs(f.y) > 0.0001f)
        {
            float d1 = (point.y - minPosition.y) / f.y;
            float d2 = (point.y - maxPosition.y) / f.y;
            distance = Mathf.Clamp(distance, Mathf.Min(d1, d2), Mathf.Max(d1, d2));
        }

        focusTarget = ClampToBounds(point - f * distance);
        focusing = true;
        isDragging = false;
        pendingZoom = 0f;
    }

    private void UpdateFocus()
    {
        float k = 1f - Mathf.Exp(-focusSmoothing * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, focusTarget, k);
        if ((transform.position - focusTarget).sqrMagnitude < 0.0004f)
        {
            transform.position = focusTarget;
            focusing = false;
        }
    }

    // =================================================================
    //  Déplacement
    // =================================================================
    private void HandleDrag()
    {
        if (Input.GetMouseButtonDown(dragButton))
        {
            if (ignoreWhenOverUI && PointerOverInteractiveUI())
                return;

            isDragging = TryGetGroundPoint(out dragOrigin);
            if (isDragging)
                dragStartPosition = transform.position;
        }

        if (Input.GetMouseButtonUp(dragButton))
            isDragging = false;

        if (isDragging && Input.GetMouseButton(dragButton))
        {
            if (TryGetGroundPoint(out Vector3 current))
            {
                // Différence entre le point saisi au départ et le point actuellement sous la souris
                Vector3 delta = dragOrigin - current;
                delta.y = 0f; // on ne bouge que sur XZ
                Vector3 targetPosition = ClampToBounds(dragStartPosition + delta);
                float t = 1f - Mathf.Exp(-movementSmoothing * Time.unscaledDeltaTime);
                transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            }
        }
    }

    /// <summary>
    /// Vrai seulement si la souris est sur un élément d'UI interactif (bouton...).
    /// Un panneau de fond ne bloque pas la caméra.
    /// </summary>
    private static bool PointerOverInteractiveUI()
    {
        if (EventSystem.current == null) return false;
        var data = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(data, results);
        foreach (var r in results)
            if (r.gameObject.GetComponentInParent<UnityEngine.UI.Selectable>() != null) return true;
        return false;
    }

    // =================================================================
    //  Zoom vers la souris
    // =================================================================
    private void HandleZoom()
    {
        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f && !(ignoreWhenOverUI && PointerOverInteractiveUI()))
        {
            // Direction du zoom = du point de vue vers le point du sol sous le curseur
            // (sinon, curseur au-dessus de l'horizon : droit devant)
            Vector3 dir = transform.forward;
            if (TryGetGroundPoint(out Vector3 ground))
            {
                Vector3 d = ground - transform.position;
                if (d.sqrMagnitude > 0.0001f) dir = d.normalized;
            }
            zoomDir = dir;

            // Molette vers l'avant = on se rapproche du curseur, vers l'arrière = on s'en éloigne
            float newPending = pendingZoom + scroll * zoomSpeed;

            // Limite la distance totale pour que Y reste dans [minY, maxY]
            if (Mathf.Abs(zoomDir.y) > 0.0001f)
            {
                float yAfter = transform.position.y + zoomDir.y * newPending;
                float yClamped = Mathf.Clamp(yAfter, minPosition.y, maxPosition.y);
                newPending = (yClamped - transform.position.y) / zoomDir.y;
            }

            pendingZoom = newPending;
        }

        // Applique le zoom progressivement (lissage), temps non mis à l'échelle => marche en pause
        float step = pendingZoom * (1f - Mathf.Exp(-zoomSmoothing * Time.unscaledDeltaTime));
        transform.position += zoomDir * step;
        pendingZoom -= step;
    }

    // =================================================================
    //  Utilitaires
    // =================================================================
    private Vector3 ClampToBounds(Vector3 pos)
    {
        pos.x = Mathf.Clamp(pos.x, minPosition.x, maxPosition.x);
        pos.y = Mathf.Clamp(pos.y, minPosition.y, maxPosition.y);
        pos.z = Mathf.Clamp(pos.z, minPosition.z, maxPosition.z);
        return pos;
    }

    private void ClampPosition() => transform.position = ClampToBounds(transform.position);

    /// <summary>Projette la position de la souris sur le plan du sol.</summary>
    private bool TryGetGroundPoint(out Vector3 point)
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        if (groundPlane.Raycast(ray, out float distance))
        {
            point = ray.GetPoint(distance);
            return true;
        }

        point = Vector3.zero;
        return false;
    }

    // Affiche la zone limite dans la Scene view
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = (minPosition + maxPosition) * 0.5f;
        Vector3 size = maxPosition - minPosition;
        Gizmos.DrawWireCube(center, size);
    }
}