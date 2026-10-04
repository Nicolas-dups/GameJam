using UnityEngine;

public class MouseLook : MonoBehaviour
{
    private Texture2D c1;
    private Texture2D c2;

    void Start()
    {
        c1 = Resources.Load<Texture2D>("c1");
        c2 = Resources.Load<Texture2D>("c2");

        if (c1 == null || c2 == null)
        {
            Debug.LogError("Impossible de trouver c1 ou c2 dans Resources.");
            return;
        }

        Cursor.SetCursor(c1, Vector2.zero, CursorMode.Auto);
    }

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            Cursor.SetCursor(c2, Vector2.zero, CursorMode.Auto);
        }
        else
        {
            Cursor.SetCursor(c1, Vector2.zero, CursorMode.Auto);
        }
    }
}