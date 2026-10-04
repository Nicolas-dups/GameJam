using System;
using System.Runtime.InteropServices;
using UnityEngine;

public class MinimizeOnEscape : MonoBehaviour
{
    private static MinimizeOnEscape instance;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_MINIMIZE = 6;
#endif

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
            Minimize();
    }

    public static void Minimize()
    {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        ShowWindow(GetActiveWindow(), SW_MINIMIZE);
#else
        Debug.Log("Minimize : fonctionne uniquement dans le build Windows (.exe).");
#endif
    }
}