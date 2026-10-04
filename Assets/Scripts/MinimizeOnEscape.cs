using UnityEngine;

public class MinimizeOnEscape : MonoBehaviour
{
    private static MinimizeOnEscape instance;

    [Header("Petite fenêtre")]
    [SerializeField] private int smallWidth = 640;
    [SerializeField] private int smallHeight = 360;

    private bool isSmall;
    private int savedWidth;
    private int savedHeight;
    private FullScreenMode savedMode;

    private void Awake()
    {
        // Évite les doublons si le script est présent dans plusieurs scènes
        if (instance != null) { Destroy(gameObject); return; }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            ToggleSmallWindow();
    }

    public void ToggleSmallWindow()
    {
        if (!isSmall)
        {
            // Mémorise l'affichage actuel pour pouvoir le restaurer
            savedMode = Screen.fullScreenMode;
            savedWidth = Screen.width;
            savedHeight = Screen.height;

            Screen.SetResolution(smallWidth, smallHeight, FullScreenMode.Windowed);
            isSmall = true;
        }
        else
        {
            Screen.SetResolution(savedWidth, savedHeight, savedMode);
            isSmall = false;
        }
    }
}