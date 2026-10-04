using System.Collections;
using TMPro;
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

    public GameObject msg;
    public TMP_Text msg_txt;


    private string[] phrases = new string[]
    {
    "Les amis ne laissent pas leurs amis conduire en état d’ébriété.",
    "Sécurité routière, tous responsables.",
    "Ne pas conduire en envoyant des SMS.",
    "Adoptez les bons réflexes, préservez des vies.",
    "Pas de ceinture, pas d’excuse.",
    "Conduire sobre ou se faire arrêter.",
    "Restez en vie, ne buvez pas et ne conduisez pas.",
    "En alerte aujourd’hui, en vie demain.",
    "S’arrêter, regarder et écouter.",
    "Évitez les accidents avant qu’ils ne vous arrêtent.",
    "Rouler avec raison pendant les fêtes de fin d’année.",
    "Réfléchissez et conduisez, restez en vie.",
    "La vitesse normale répond à tous les besoins.",
    "Respectez le code de la route, sauvez votre avenir.",
    "Mieux vaut tard que jamais !",
    "Conseil de conduite du jour : céder le passage.",
    "Évitez la mort : mettez votre ceinture de sécurité !",
    "Un trajet rapide pourrait être votre dernier trajet.",
    "Les règles de sécurité routière sont vos meilleurs outils.",
    "Les accidents ne se produisent pas, ils sont causés.",
    "La vitesse tue.",
    "Boire et perdre.",
    "Téléphone au volant, danger au tournant.",
    "Votre vie vaut plus que quelques secondes gagnées.",
    "Sur la route, chaque seconde d’attention compte."
    };
    private int indice_msg;
    private Coroutine msgRoutine;

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
        indice_msg = 0;
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

    public void DisplayMsg()
    {
        if (msg != null)
        {
            msg.SetActive(true);
            if (msgRoutine != null)
                StopCoroutine(msgRoutine);

            msgRoutine = StartCoroutine(HideMsgAfterDelay(4f));
        }

        if (msg_txt != null)
        {
            msg_txt.text = phrases[indice_msg % phrases.Length];
            indice_msg++;
        }
    }

    private IEnumerator HideMsgAfterDelay(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);

        msg?.SetActive(false);

        msgRoutine = null;
    }
}