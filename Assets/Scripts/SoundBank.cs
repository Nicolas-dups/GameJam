using UnityEngine;

/// <summary>
/// All settings for a RandomSoundPlayer. Create via Assets > Create > Audio > Sound Bank.
/// </summary>
[CreateAssetMenu(fileName = "NewSoundBank", menuName = "Audio/Sound Bank")]
public class SoundBank : ScriptableObject
{
    [Header("Sounds")]
    public AudioClip[] clips;
    [Tooltip("Avoid playing the same clip twice in a row (needs 2+ clips).")]
    public bool avoidRepeat = true;

    [Header("Timing")]
    public bool playOnStart = true;
    public float minInterval = 2f;
    public float maxInterval = 6f;
    [Tooltip("Wait for the current clip to finish before counting down the interval.")]
    public bool waitForClipToEnd = false;

    [Header("Variation")]
    public Vector2 volumeRange = new Vector2(0.8f, 1f);
    public Vector2 pitchRange = new Vector2(0.95f, 1.05f);

    // private void OnValidate()
    // {
    //     minInterval = Mathf.Max(0f, minInterval);
    //     maxInterval = Mathf.Max(minInterval, maxInterval);
    // }
}
