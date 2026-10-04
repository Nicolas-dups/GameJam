using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class PlayOnSpawn : MonoBehaviour
{
    [SerializeField] private float delay = 0f;

    private void Start()
    {
        var source = GetComponent<AudioSource>();
        if (delay > 0f) source.PlayDelayed(delay);
        else source.Play();
    }
}