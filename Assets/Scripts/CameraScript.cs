using UnityEngine;
using UnityEngine.InputSystem;

public class CameraScript : MonoBehaviour
{
    [Header("Camera Movement")]
    [SerializeField] private float panSpeed = 30f;
    [SerializeField] private float movementSmoothing = 10f;
    [SerializeField] private float zoomSpeed = 20f;
    [SerializeField] private float minHeight = 8f;
    [SerializeField] private float maxHeight = 60f;
    [SerializeField] private float minX = -100f;
    [SerializeField] private float maxX = 100f;
    [SerializeField] private float minZ = -100f;
    [SerializeField] private float maxZ = 100f;

    private bool isDragging;
    private Vector2 dragStartMousePos;
    private Vector3 targetPosition;

    private void OnEnable()
    {
        isDragging = false;
        targetPosition = transform.position;
    }

    private void Update()
    {
        HandleDragPan();
        HandleKeyboardPan();
        HandleZoom();
        SmoothMovement();
    }

    private void HandleDragPan()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            isDragging = true;
            dragStartMousePos = mouse.position.ReadValue();
            return;
        }

        if (!mouse.rightButton.isPressed)
        {
            isDragging = false;
            return;
        }

        if (!isDragging)
            return;

        Vector2 mousePos = mouse.position.ReadValue();
        Vector2 mouseDelta = mousePos - dragStartMousePos;
        Vector3 move = new Vector3(-mouseDelta.x, 0f, -mouseDelta.y) * (panSpeed / 100f);

        targetPosition += move;
        ClampTargetPosition();

        dragStartMousePos = mousePos;
    }

    private void HandleKeyboardPan()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        float horizontal = 0f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
            horizontal += 1f;
        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
            horizontal -= 1f;

        float vertical = 0f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
            vertical += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
            vertical -= 1f;

        if (horizontal == 0f && vertical == 0f)
            return;

        Vector3 move = new Vector3(horizontal, 0f, vertical).normalized * panSpeed * Time.deltaTime;
        targetPosition += move;
        ClampTargetPosition();
    }

    private void HandleZoom()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) < 0.001f)
            return;

        float zoomAmount = scroll * (zoomSpeed / 120f) * 2f;
        Vector3 forward = transform.forward;
        Vector3 pos = targetPosition + forward * zoomAmount;
        pos.y = Mathf.Clamp(pos.y, minHeight, maxHeight);
        targetPosition = pos;
    }

    private void SmoothMovement()
    {
        transform.position = Vector3.Lerp(transform.position, targetPosition, movementSmoothing * Time.deltaTime);
    }

    private void ClampTargetPosition()
    {
        Vector3 pos = targetPosition;
        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
        targetPosition = pos;
    }
}