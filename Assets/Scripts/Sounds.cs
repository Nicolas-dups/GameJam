using UnityEngine;

public class Sounds : MonoBehaviour
{
    public static Sounds Instance { get; private set; }

    public AudioClip crash;
    public AudioClip build;
    public AudioClip clickUI;
    public AudioClip music1; //chill
    public AudioClip music2; //dynamique


    [Range(0f, 1f)] public float musicVolume = 0.15f;
    [Range(0f, 1f)] public float sfxVolume = 1f;

    AudioSource sfxSource;
    AudioSource musicSource;

    private void Awake()
    {
        Instance = this;

        // Effets sonores (2D)
        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f;
        sfxSource.volume = sfxVolume;

        // Musique de fond (2D, en boucle)
        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.volume = musicVolume;

        set_music("music1");
    }
    private void OnEnable()
    {
        set_music("music1");
    }

    private void OnValidate()
    {
        // Permet de régler les volumes en direct depuis l'Inspector pendant le jeu
        if (musicSource != null) musicSource.volume = musicVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume;
    }

    public void set_music(string musicName)
    {
        AudioClip clip = null;
        switch (musicName)
        {
            case "music1": clip = music1; break;
            case "music2": clip = music2; break;
        }

        if (clip == null) return;                                   // nom inconnu ou clip non assigné
        if (musicSource.clip == clip && musicSource.isPlaying) return; // déjà en cours : on ne redémarre pas

        musicSource.clip = clip;
        musicSource.Play();
    }

    public void play_sound(string s)
    {
        AudioClip clip = null;
        switch (s)
        {
            case "crash": clip = crash; break;
            case "build": clip = build; break;
            case "clickUI":    clip = clickUI; break;
        }
        if (clip != null) sfxSource.PlayOneShot(clip);

    }
}