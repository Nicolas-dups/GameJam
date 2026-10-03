using UnityEngine;

public class Sounds : MonoBehaviour
{
    public static Sounds Instance { get; private set; }

    public AudioClip crash;
    public AudioClip build;
    public AudioClip clickUI;
    public AudioClip music;
    


    private void Awake()
    {
        Instance = this;
    }
   public void play_sound(string s)
{
    AudioClip clip = null;
    switch (s)
    {
        case "crash":   clip = crash;   break;
        case "build":   clip = build;   break;
        case "clickUI": clip = clickUI; break;
    }
    if (clip != null)
        AudioSource.PlayClipAtPoint(clip, transform.position);
}
}
