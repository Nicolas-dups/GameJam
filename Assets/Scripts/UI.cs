using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UI : MonoBehaviour
{
    [Header("Camera")]
    public Camera previewCamera;

    [Header("UI Images")]
    public RawImage image1;
    public RawImage image2;
    public RawImage image3;
    public RawImage image4;
    public RawImage image5;
    public RawImage image6;
    public RawImage image7;
    public RawImage image8;
    public RawImage image9;
    public RawImage image10;

    [Header("Settings")]
    public int imageWidth = 256;
    public int imageHeight = 256;
    public float distanceBetweenItems = 2f;

    private RawImage[] images;

    private void Start()
    {
        images = new RawImage[]
        {
            image1,
            image2,
            image3,
            image4,
            image5,
            image6,
            image7,
            image8,
            image9,
            image10
        };

        StartCoroutine(CaptureItems());
    }

    private IEnumerator CaptureItems()
    {
        // Position initiale de la caméra
        Vector3 initialPosition = previewCamera.transform.position;

        // Use an alpha-capable target and clear the camera to transparent.
        RenderTexture renderTexture = new RenderTexture(
            imageWidth,
            imageHeight,
            24,
            RenderTextureFormat.ARGB32
        );

        CameraClearFlags originalClearFlags = previewCamera.clearFlags;
        Color originalBackgroundColor = previewCamera.backgroundColor;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        previewCamera.targetTexture = renderTexture;

        for (int i = 0; i < 10; i++)
        {
            // Déplace la caméra de 2 unités sur X
            previewCamera.transform.position =
                initialPosition + Vector3.forward * (i * distanceBetweenItems);

            // Attend une frame pour laisser Unity mettre à jour la caméra
            yield return new WaitForEndOfFrame();

            // Capture
            previewCamera.Render();

            RenderTexture.active = renderTexture;

            Texture2D screenshot = new Texture2D(
                imageWidth,
                imageHeight,
                TextureFormat.RGBA32,
                false
            );

            screenshot.ReadPixels(
                new Rect(0, 0, imageWidth, imageHeight),
                0,
                0
            );

            screenshot.Apply();

            // Assigne l'image au RawImage correspondant
            images[i].texture = screenshot;

            // Nettoyage
            RenderTexture.active = null;
        }

        // Désactive la RenderTexture
        previewCamera.targetTexture = null;
        previewCamera.clearFlags = originalClearFlags;
        previewCamera.backgroundColor = originalBackgroundColor;

        // Détruit la RenderTexture
        Destroy(renderTexture);

        // Retour à la position initiale
        previewCamera.transform.position = initialPosition;
    }
}