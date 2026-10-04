using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a random clip from a SoundBank on an AudioSource at random intervals.
/// All settings live in the SoundBank asset.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class RandomSoundPlayer : MonoBehaviour
{
    [SerializeField] private SoundBank soundBank;

    private AudioSource source;
    private Coroutine routine;
    private int lastIndex = -1;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
    }

    private void Start()
    {
        if (soundBank != null && soundBank.playOnStart) StartPlaying();
    }

    public void StartPlaying()
    {
        Debug.Log($"Starting random sound player on {gameObject.name}");
        if (soundBank == null || routine != null) return;
        routine = StartCoroutine(PlayLoop());
    }

    public void StopPlaying()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }

    private void OnDisable()
    {
        routine = null; // coroutines stop automatically when disabled
    }

    private IEnumerator PlayLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(soundBank.minInterval, soundBank.maxInterval));
            PlayRandom();

            if (soundBank.waitForClipToEnd)
                while (source.isPlaying) yield return null;
        }
    }

    public void PlayRandom()
    {
        if (soundBank == null || soundBank.clips == null || soundBank.clips.Length == 0) return;

        AudioClip[] list = soundBank.clips;
        int index = Random.Range(0, list.Length);
        if (soundBank.avoidRepeat && list.Length > 1)
        {
            while (index == lastIndex)
                index = Random.Range(0, list.Length);
        }
        lastIndex = index;

        source.pitch = Random.Range(soundBank.pitchRange.x, soundBank.pitchRange.y);
        source.PlayOneShot(list[index], Random.Range(soundBank.volumeRange.x, soundBank.volumeRange.y));
    }
}
