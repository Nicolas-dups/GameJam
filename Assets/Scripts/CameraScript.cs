using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Caméra type RTS / vue du dessus :
/// - Clic droit maintenu + glisser (bouton réglable via dragButton) : déplacement sur le plan XZ (le sol "suit" la souris)
/// - Molette : zoom / dézoom (change la hauteur Y)
/// - Limites min / max sur X, Y et Z
/// À attacher sur l'objet Camera.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Déplacement (glisser)")]
    [Tooltip("0 = clic gauche, 1 = clic droit, 2 = clic molette")]
    [SerializeField] private int dragButton = 1;
    [Tooltip("Hauteur (Y) du plan de sol utilisé pour calculer le glissement")]
    [SerializeField] private float groundHeight = 0f;
    [SerializeField] private bool ignoreWhenOverUI = true;

    [Header("Zoom (molette)")]
    [Tooltip("Distance parcourue le long de l'axe avant par cran de molette")]
    [SerializeField] private float zoomSpeed = 2f;
    [Tooltip("Plus la valeur est grande, plus le zoom est réactif")]
    [SerializeField] private float zoomSmoothing = 10f;

    [Header("Limites de la caméra")]
    [SerializeField] private Vector3 minPosition = new Vector3(-50f, 5f, -50f);
    [SerializeField] private Vector3 maxPosition = new Vector3(50f, 40f, 50f);

    private Camera cam;
    private Plane groundPlane;
    private Vector3 dragOrigin;
    private bool isDragging;
    private float pendingZoom; // distance de zoom restant à appliquer (lissage)

    private void Awake()
    {
        cam = GetComponent<Camera>();
        groundPlane = new Plane(Vector3.up, new Vector3(0f, groundHeight, 0f));
    }

    private void Update()
    {
        HandleDrag();
        HandleZoom();
        ClampPosition();
    }

    private void HandleDrag()
    {
        if (Input.GetMouseButtonDown(dragButton))
        {
            if (ignoreWhenOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            isDragging = TryGetGroundPoint(out dragOrigin);
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
                transform.position += delta;
            }
        }
    }

    private void HandleZoom()
    {
        if (ignoreWhenOverUI && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        Vector3 forward = transform.forward;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            // Molette vers l'avant = zoom (on avance), vers l'arrière = dézoom (on recule)
            float newPending = pendingZoom + scroll * zoomSpeed;

            // On limite la distance totale pour que Y reste dans [minY, maxY]
            if (Mathf.Abs(forward.y) > 0.0001f)
            {
                float yAfter = transform.position.y + forward.y * newPending;
                float yClamped = Mathf.Clamp(yAfter, minPosition.y, maxPosition.y);
                newPending = (yClamped - transform.position.y) / forward.y;
            }

            pendingZoom = newPending;
        }

        // Applique le zoom progressivement (lissage) le long de l'axe avant
        float step = pendingZoom * (1f - Mathf.Exp(-zoomSmoothing * Time.unscaledDeltaTime));
        transform.position += forward * step;
        pendingZoom -= step;
    }

    private void ClampPosition()
    {
        Vector3 pos = transform.position;
        pos.x = Mathf.Clamp(pos.x, minPosition.x, maxPosition.x);
        pos.y = Mathf.Clamp(pos.y, minPosition.y, maxPosition.y);
        pos.z = Mathf.Clamp(pos.z, minPosition.z, maxPosition.z);
        transform.position = pos;
    }

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