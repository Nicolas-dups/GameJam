using UnityEngine;

public class Sounds : MonoBehaviour
{
    public static Sounds Instance { get; private set; }

    public AudioClip crash;
    public AudioClip build;
    public AudioClip clickUI;
    public AudioClip music;

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
        musicSource.clip = music;
        musicSource.loop = true;
        musicSource.playOnAwake = false;
        musicSource.spatialBlend = 0f;
        musicSource.volume = musicVolume;
        if (music != null) musicSource.Play();
    }

    private void OnValidate()
    {
        // Permet de régler les volumes en direct depuis l'Inspector pendant le jeu
        if (musicSource != null) musicSource.volume = musicVolume;
        if (sfxSource != null) sfxSource.volume = sfxVolume;
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