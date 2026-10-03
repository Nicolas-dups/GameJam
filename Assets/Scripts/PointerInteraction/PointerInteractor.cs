using UnityEngine;
using UnityEngine.InputSystem;

public class PointerInteractor : MonoBehaviour
{
    [SerializeField] private Camera cam;
    [SerializeField] private float maxDistance = 5f;
    [SerializeField] private LayerMask interactMask = ~0;

    public Item equippedItem;

    // Kept as MonoBehaviour so Unity's overloaded null check detects destroyed objects
    private MonoBehaviour currentBehaviour;
    private IPointerInteractible current;

    private void Awake()
    {
        if (cam == null) cam = Camera.main;
    }


    // ########################################################################################    
    // ########################################################################################    
    

    public void EquipItem(Item item)
    {
        equippedItem = item;
    }

    public void UnequipItem()
    {
        equippedItem = null;
    }

    public void HoverResponse(IPointerInteractible target)
    {
        ShowItemPreview(target);
    }

    public void UnhoverResponse(IPointerInteractible target)
    {
        HideItemPreview();
    }

    public void ClickResponse(IPointerInteractible target)
    {
        SpawnItem(target);
    }


    // ########################################################################################
    // ########################################################################################

    [HideInInspector] public GameObject itemPreviewInstance;

    public void ShowItemPreview(IPointerInteractible target)
    {
        if (equippedItem == null || equippedItem.prefab == null) return;

        if (itemPreviewInstance == null)
        {
            itemPreviewInstance = Instantiate(equippedItem.itemPreviewPrefab, target.transform.position, equippedItem.itemPreviewPrefab.transform.rotation * target.transform.rotation);
            // Optionally, set the preview instance to a specific layer or make it semi-transparent
        }
    }

    public void HideItemPreview()
    {
        if (itemPreviewInstance != null)
        {
            Destroy(itemPreviewInstance);
            itemPreviewInstance = null;
        }
    }

    public void SpawnItem(IPointerInteractible target)
    {
        if (equippedItem == null || equippedItem.prefab == null) return;

        // Optionally, play placement effect
        if (equippedItem.itemPreviewPrefab != null)
        {
            Instantiate(equippedItem.prefab, target.transform.position, equippedItem.prefab.transform.rotation * target.transform.rotation);
            equippedItem.MapCsq?.Invoke();
        }
    }





    // ########################################################################################
    // ########################################################################################


    private void Update()
    {
        MonoBehaviour behaviour = FindTarget();

        // Target changed: lost, switched object, or equipped item changed
        if (behaviour != currentBehaviour)
        {
            ClearHover();

            if (behaviour != null)
            {
                currentBehaviour = behaviour;
                current = (IPointerInteractible)behaviour;
                HoverResponse(current);
            }
        }

        if (current != null && Mouse.current.leftButton.wasPressedThisFrame)
            ClickResponse(current);
    }

    private void OnDisable()
    {
        ClearHover(); // so ghosts don't linger if the interactor is disabled
    }

    private void ClearHover()
    {
        // Unity null check: skips OnUnhover if the target was destroyed
        if (currentBehaviour != null)
            UnhoverResponse(current);

        currentBehaviour = null;
        current = null;
    }

    private MonoBehaviour FindTarget()
    {
        System.Type type = equippedItem?.interactibleType;
        if (type == null) return null;

        // Safety: the type must be a MonoBehaviour that implements the interface
        if (!typeof(MonoBehaviour).IsAssignableFrom(type) ||
            !typeof(IPointerInteractible).IsAssignableFrom(type))
        {
            Debug.LogWarning($"{type.Name} must derive from MonoBehaviour and implement IPointerInteractible.");
            return null;
        }

        // Center-of-screen ray (FPS-style). For a mouse cursor use:
        // cam.ScreenPointToRay(Input.mousePosition)
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());        
        
        // QueryTriggerInteraction.Collide is what lets the ray hit trigger hitboxes
        if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, interactMask, QueryTriggerInteraction.Collide))
        {
            return null;
        }
        // GetComponentInParent so the hitbox can be a child of the object holding the script
        return hit.collider.GetComponentInParent(type) as MonoBehaviour;
    }
}