using UnityEngine;

public class Ping : MonoBehaviour
{
    [Header("Sprite à afficher")]
    [SerializeField] private string spriteName = "ping";

    [Header("Position")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 5f, 0f);

    [Header("Taille")]
    [SerializeField] private float scale = 1f;

    [Header("Animation")]
    [SerializeField] private float bobAmplitude = 0.5f;
    [SerializeField] private float bobSpeed = 2f;

    private GameObject spriteObject;

    private void Start()
    {
        // Charge le sprite depuis Assets/Resources/
        Sprite sprite = Resources.Load<Sprite>(spriteName);

        if (sprite == null)
        {
            Debug.LogError($"Impossible de trouver le sprite '{spriteName}' dans Resources.");
            return;
        }

        spriteObject = new GameObject("SpriteAboveHead");
        spriteObject.transform.SetParent(transform);
        spriteObject.transform.localPosition = offset;
        Vector3 parentScale = transform.lossyScale;
        spriteObject.transform.localScale = new Vector3(
            scale / parentScale.x,
            scale / parentScale.y,
            scale / parentScale.z
        );

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = 100;
    }



    private void Update()
    {
        if (spriteObject != null)
        {
            float verticalOffset = Mathf.Sin(Time.unscaledTime * bobSpeed) * bobAmplitude;
            spriteObject.transform.localPosition = offset + Vector3.up * verticalOffset;

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Vector3 directionToCamera = mainCamera.transform.position - spriteObject.transform.position;
                directionToCamera.y = 0f;

                if (directionToCamera.sqrMagnitude > 0f)
                {
                    spriteObject.transform.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
                }
            }
           // Debug.Log(Time.time);
        }
        else
        {
            Debug.Log("spriteObject null");
        }
    }

    private void OnDestroy()
    {
        if (spriteObject != null)
        {
            Destroy(spriteObject);
        }
    }
}